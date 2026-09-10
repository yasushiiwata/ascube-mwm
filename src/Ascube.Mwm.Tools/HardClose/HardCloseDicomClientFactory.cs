using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Ascube.Mwm.Tools.HardClose;

/// <summary>
/// <see cref="SocketCapturingNetworkManager"/> を差し込んだ専用の <see cref="IDicomClient"/> を作る
/// （<c>scu find --hard-close-after</c> 専用。通常の scu コマンドは <see cref="DicomClientFactory"/> をそのまま使う）。
/// </summary>
internal sealed class HardCloseDicomClientFactory : IDisposable
{
    private readonly ServiceProvider _services;

    public IDicomClient Client { get; }

    public SocketCapturingNetworkManager NetworkManager { get; }

    public HardCloseDicomClientFactory(string host, int port, string callingAe, string calledAe)
    {
        var services = new ServiceCollection();
        services.AddFellowOakDicom();
        // AddNetworkManager<T>() はジェネリック型のみを受け付け、DI が新規にインスタンス化してしまうため、
        // SocketCapturingNetworkManager 自身（後で外から Socket を掴むために同一参照が要る）と
        // INetworkManager の両方を同じシングルトンに解決させる。
        services.AddSingleton<SocketCapturingNetworkManager>();
        services.AddSingleton<INetworkManager>(sp => sp.GetRequiredService<SocketCapturingNetworkManager>());
        _services = services.BuildServiceProvider();

        NetworkManager = _services.GetRequiredService<SocketCapturingNetworkManager>();
        Client = _services.GetRequiredService<IDicomClientFactory>().Create(host, port, false, callingAe, calledAe);
    }

    public void Dispose() => _services.Dispose();
}
