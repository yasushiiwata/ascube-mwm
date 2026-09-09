using System.Net;
using System.Net.Sockets;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// 規則3：応答送信中にクライアントが一方的に切断するのは正常系。<c>OnConnectionClosed</c> で
/// 例外を再スローするとリスナが道連れになり「二度と繋がらない」状態になる。FIN・RST 双方で検証する。
/// ここでは DICOM 上位層の途中（不完全な PDU を送った直後の切断）を再現し、切断後もリスナが
/// 新しい接続を正常に受け続けられることを確認する。
/// </summary>
public class AbruptDisconnectTests
{
    [Fact]
    public async Task AbruptFinDuringHandshake_DoesNotCrashListener()
    {
        using var server = new ScpTestServer(TestProfileFactory.Build());

        using (var socket = new TcpClient())
        {
            await socket.ConnectAsync("127.0.0.1", server.Port);
            // 不完全な PDU（A-ASSOCIATE-RQ の先頭バイトのみ）を送ってから正常切断（FIN）。
            await socket.GetStream().WriteAsync(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        } // Dispose で Close → FIN

        await Task.Delay(300);

        Assert.True(server.Server.IsListening);
        Assert.Equal(DicomStatus.Success, await RunEchoAsync(server.Port));
    }

    [Fact]
    public async Task AbruptRstDuringHandshake_DoesNotCrashListener()
    {
        using var server = new ScpTestServer(TestProfileFactory.Build());

        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            socket.LingerState = new LingerOption(true, 0); // Close() で RST を送らせる
            await socket.ConnectAsync(IPAddress.Loopback, server.Port);
            await socket.SendAsync(new byte[] { 0x01, 0x00, 0x00, 0x00 }, SocketFlags.None);
        } // Dispose で Close → RST（LingerState(true,0) のため）

        await Task.Delay(300);

        Assert.True(server.Server.IsListening);
        Assert.Equal(DicomStatus.Success, await RunEchoAsync(server.Port));
    }

    [Fact]
    public async Task AbruptCloseWithNoDataSent_DoesNotCrashListener()
    {
        using var server = new ScpTestServer(TestProfileFactory.Build());

        using (var socket = new TcpClient())
        {
            await socket.ConnectAsync("127.0.0.1", server.Port);
        } // 何も送らずに即切断

        await Task.Delay(300);

        Assert.True(server.Server.IsListening);
        Assert.Equal(DicomStatus.Success, await RunEchoAsync(server.Port));
    }

    private static async Task<DicomStatus?> RunEchoAsync(int port)
    {
        var client = DicomClientFactory.Create("127.0.0.1", port, false, "ANY_SCU", "ASCUBE_MWM");
        DicomStatus? status = null;
        var request = new DicomCEchoRequest();
        request.OnResponseReceived += (_, response) => status = response.Status;

        await client.AddRequestAsync(request);
        await client.SendAsync();

        return status;
    }
}
