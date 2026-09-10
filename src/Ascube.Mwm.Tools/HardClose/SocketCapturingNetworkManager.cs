using System.Net.Sockets;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Tls;

namespace Ascube.Mwm.Tools.HardClose;

/// <summary>
/// 実装指示書 v2 T11：<c>scu find --hard-close-after</c> 用。<see cref="DicomClient"/> では
/// 「N件受信後にA-RELEASEを送らずTCPを閉じる」を実現できない（内部でA-RELEASEを送りうる）ため、
/// 生の <see cref="Socket"/> を捕まえて外側から強制切断できるようにする。
/// fo-dicom 本体は無改変（規則11）。T9（raw stream recorder）と同じ DI 差し替え手法。
/// </summary>
internal sealed class SocketCapturingNetworkManager : INetworkManager
{
    private readonly INetworkManager _inner = new DesktopNetworkManager();

    public Socket? CapturedSocket { get; private set; }

    public INetworkListener CreateNetworkListener(string ipAddress, int port) => _inner.CreateNetworkListener(ipAddress, port);

    public INetworkStream CreateNetworkStream(NetworkStreamCreationOptions options)
    {
        var stream = _inner.CreateNetworkStream(options);
        if (stream.AsStream() is NetworkStream networkStream)
        {
            CapturedSocket = networkStream.Socket;
        }

        return stream;
    }

    public INetworkStream CreateNetworkStream(TcpClient tcpClient, ITlsAcceptor? tlsAcceptor, bool ownsTcpClient)
        => _inner.CreateNetworkStream(tcpClient, tlsAcceptor, ownsTcpClient);

    public bool IsSocketException(Exception exception, out int errorCode, out string errorDescriptor)
        => _inner.IsSocketException(exception, out errorCode, out errorDescriptor);

    public bool TryGetNetworkIdentifier(out DicomUID identifier) => _inner.TryGetNetworkIdentifier(out identifier);

    public string MachineName => _inner.MachineName;

    /// <summary>A-RELEASEを送らずTCPを強制的に閉じる。</summary>
    public void HardClose(HardCloseMode mode)
    {
        if (CapturedSocket is null)
        {
            return;
        }

        if (mode == HardCloseMode.Rst)
        {
            // LingerState(true, 0)：未送信データを破棄して即座にRSTを送る。
            CapturedSocket.LingerState = new LingerOption(true, 0);
        }

        CapturedSocket.Close();
    }
}

// public：Ascube.Mwm.Tools.Tests がこのファイルをリンクしてテストするため、xUnit の [Theory] の
// パラメーター型として参照できる必要がある（internal だとテストメソッドの公開シグネチャと矛盾する）。
public enum HardCloseMode
{
    Fin,
    Rst,
}
