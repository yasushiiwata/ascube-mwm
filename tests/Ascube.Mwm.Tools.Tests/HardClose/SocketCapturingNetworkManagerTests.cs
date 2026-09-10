using System.Net;
using System.Net.Sockets;
using Ascube.Mwm.Tools.HardClose;
using FellowOakDicom.Network;

namespace Ascube.Mwm.Tools.Tests.HardClose;

/// <summary>
/// T11：<c>scu find --hard-close-after</c> が使う生の Socket 捕獲・強制切断の仕組み。
/// 実際の C-FIND での結線（SCP 相手）は手動検証（DCMTKと自作SCP両方で確認済み。docs/progress.md参照）で行い、
/// ここではメカニズム自体（Socket を捕まえられること・HardClose が実際に接続を切ること）を単体で確認する。
/// </summary>
public class SocketCapturingNetworkManagerTests
{
    [Fact]
    public async Task CreateNetworkStream_ClientConnection_CapturesTheUnderlyingSocket()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var manager = new SocketCapturingNetworkManager();
        var stream = manager.CreateNetworkStream(new NetworkStreamCreationOptions
        {
            Host = "127.0.0.1",
            Port = port,
        });

        using var accepted = await acceptTask;

        Assert.NotNull(manager.CapturedSocket);
        Assert.True(manager.CapturedSocket!.Connected);

        stream.Dispose();
    }

    [Theory]
    [InlineData(HardCloseMode.Fin)]
    [InlineData(HardCloseMode.Rst)]
    public async Task HardClose_ActuallyClosesTheConnection_BothModes(HardCloseMode mode)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();

        var manager = new SocketCapturingNetworkManager();
        var stream = manager.CreateNetworkStream(new NetworkStreamCreationOptions { Host = "127.0.0.1", Port = port });
        using var accepted = await acceptTask;

        manager.HardClose(mode);

        // サーバー側で読み取ると切断（0バイト読み込み、または例外）が観測できる。
        var buffer = new byte[16];
        var serverStream = accepted.GetStream();
        Exception? readException = null;
        var read = -1;
        try
        {
            read = await serverStream.ReadAsync(buffer);
        }
        catch (Exception ex)
        {
            readException = ex;
        }

        Assert.True(read == 0 || readException is not null, "HardClose 後もサーバー側の読み取りがブロックしたままでした。");
    }

    [Fact]
    public void HardClose_NoCapturedSocket_DoesNotThrow()
    {
        var manager = new SocketCapturingNetworkManager();

        var exception = Record.Exception(() => manager.HardClose(HardCloseMode.Fin));

        Assert.Null(exception);
    }
}
