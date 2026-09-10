using System.Net;
using System.Net.Sockets;
using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Store.Audit;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.DependencyInjection;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>空きポートで <see cref="MwmDicomService"/> を実際に待受させるテスト用ヘルパー。</summary>
internal sealed class ScpTestServer : IDisposable
{
    private readonly ServiceProvider _services;

    public int Port { get; }

    public IDicomServer Server { get; }

    public IWorklistRepository Repository { get; }

    public FakeAuditWriter AuditWriter { get; }

    public LiveProfileRegistry ProfileRegistry { get; }

    public ScpTestServer(params DeviceProfile[] profiles) : this(new FakeWorklistRepository(), profiles)
    {
    }

    public ScpTestServer(IWorklistRepository repository, params DeviceProfile[] profiles)
    {
        Port = GetFreeTcpPort();
        Repository = repository;
        AuditWriter = new FakeAuditWriter();
        ProfileRegistry = new LiveProfileRegistry(profiles);

        var services = new ServiceCollection();
        services.AddFellowOakDicom();
        _services = services.BuildServiceProvider();

        var factory = _services.GetRequiredService<IDicomServerFactory>();

        // ScpRoutingContext.Port はルーティングキー（プロファイルの network.port と一致させる必要がある）。
        // 実運用では ScpHostedService がプロファイルの port でリッスンするため常に一致するが、
        // テストはポート衝突を避けるため待受には空きポートを使う。プロファイルが1件も無い呼び出し元
        // （T3のAE照合テスト等）向けに、プロファイルが無ければ実際の待受ポートをそのまま使う。
        var routingPort = profiles.Length > 0 ? profiles[0].Port : Port;
        var routing = new ScpRoutingContext
        {
            ProfileRegistry = ProfileRegistry,
            Port = routingPort,
            Repository = repository,
            AuditWriter = AuditWriter,
        };
        Server = factory.Create<MwmDicomService>(Port, userState: routing);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        Server.Stop();
        Server.Dispose();
        _services.Dispose();
    }
}
