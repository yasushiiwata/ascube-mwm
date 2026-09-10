using System.Net;
using System.Net.Sockets;
using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
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

    public ScpTestServer(params DeviceProfile[] profiles) : this(new FakeWorklistRepository(), profiles)
    {
    }

    public ScpTestServer(IWorklistRepository repository, params DeviceProfile[] profiles)
    {
        Port = GetFreeTcpPort();
        Repository = repository;

        var services = new ServiceCollection();
        services.AddFellowOakDicom();
        _services = services.BuildServiceProvider();

        var factory = _services.GetRequiredService<IDicomServerFactory>();
        var routing = new ScpRoutingContext { Profiles = profiles, Repository = repository };
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
