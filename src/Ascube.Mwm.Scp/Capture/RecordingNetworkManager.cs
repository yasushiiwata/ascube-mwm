using System.Net.Sockets;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Tls;

namespace Ascube.Mwm.Scp.Capture;

/// <summary>
/// T0/S-3 で検証済みの方式：<see cref="DesktopNetworkManager"/> をラップし、
/// 生成する <see cref="INetworkStream"/> の Read/Write を <see cref="RawCaptureWriter"/> に記録する。
/// fo-dicom 本体は無改変（規則11）。DI 登録は <c>AddFellowOakDicom()</c> の後で
/// <c>AddNetworkManager&lt;RecordingNetworkManager&gt;()</c> を呼ぶこと（順序を守らないと差し替わらない）。
/// </summary>
public sealed class RecordingNetworkManager(RawCaptureWriter captureWriter) : INetworkManager
{
    private readonly INetworkManager _inner = new DesktopNetworkManager();

    public INetworkListener CreateNetworkListener(string ipAddress, int port)
        => _inner.CreateNetworkListener(ipAddress, port);

    public INetworkStream CreateNetworkStream(NetworkStreamCreationOptions options)
        => Wrap(_inner.CreateNetworkStream(options));

    public INetworkStream CreateNetworkStream(TcpClient tcpClient, ITlsAcceptor? tlsAcceptor, bool ownsTcpClient)
        => Wrap(_inner.CreateNetworkStream(tcpClient, tlsAcceptor, ownsTcpClient));

    public bool IsSocketException(Exception exception, out int errorCode, out string errorDescriptor)
        => _inner.IsSocketException(exception, out errorCode, out errorDescriptor);

    public bool TryGetNetworkIdentifier(out DicomUID identifier)
        => _inner.TryGetNetworkIdentifier(out identifier);

    public string MachineName => _inner.MachineName;

    private INetworkStream Wrap(INetworkStream inner)
    {
        var associationId = Guid.NewGuid().ToString("N")[..12];
        return new RecordingNetworkStream(inner, associationId, captureWriter);
    }
}

internal sealed class RecordingNetworkStream(INetworkStream inner, string associationId, RawCaptureWriter captureWriter) : INetworkStream
{
    private Stream? _wrapped;

    public string RemoteHost => inner.RemoteHost;
    public string LocalHost => inner.LocalHost;
    public int RemotePort => inner.RemotePort;
    public int LocalPort => inner.LocalPort;

    public Stream AsStream() => _wrapped ??= new TeeStream(inner.AsStream(), associationId, captureWriter);

    public void Dispose()
    {
        _wrapped?.Dispose();
        inner.Dispose();
    }
}

/// <summary>受信を direction=0（In）、送信を direction=1（Out）として記録する。</summary>
internal sealed class TeeStream(Stream inner, string associationId, RawCaptureWriter captureWriter) : Stream
{
    private const byte DirectionIn = 0;
    private const byte DirectionOut = 1;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, count);
        if (n > 0)
        {
            captureWriter.Append(associationId, DirectionIn, buffer.AsSpan(offset, n));
        }

        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        if (n > 0)
        {
            captureWriter.Append(associationId, DirectionIn, buffer.AsSpan(offset, n));
        }

        return n;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (count > 0)
        {
            captureWriter.Append(associationId, DirectionOut, buffer.AsSpan(offset, count));
        }

        inner.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count > 0)
        {
            captureWriter.Append(associationId, DirectionOut, buffer.AsSpan(offset, count));
        }

        await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override void Flush() => inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
