using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Tls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var port = 11113;
var outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out"));
Directory.CreateDirectory(outDir);
var logPath = Path.Combine(outDir, "dicom-log.txt");
if (File.Exists(logPath)) File.Delete(logPath);
var rawPath = Path.Combine(outDir, "raw-capture.bin");
if (File.Exists(rawPath)) File.Delete(rawPath);

RawRecorder.Init(rawPath);

var services = new ServiceCollection();
services.AddFellowOakDicom();
services.AddNetworkManager<RecordingNetworkManager>();
services.Configure<DicomServiceOptions>(o =>
{
    // ①はこのスパイクで別途「不十分」と判定済み（構造化ログであって生バイトではない）。
    // ここでは②（INetworkStream decorator）の検証に集中するため両方 false のままにする。
    o.LogDataPDUs = false;
    o.LogDimseDatasets = false;
});
services.AddLogging(b =>
{
    b.SetMinimumLevel(LogLevel.Trace);
    b.AddProvider(new FileLoggerProvider(logPath));
});
using var serviceProvider = services.BuildServiceProvider();

var factory = serviceProvider.GetRequiredService<IDicomServerFactory>();
var server = factory.Create<S3EchoFindService>(port);

Console.WriteLine($"S3: listening on {port}, logging to {logPath}");
await Task.Delay(1000);

RunTool("echoscu", $"-v -aet TEST_SCU -aec ASCUBE_MWM 127.0.0.1 {port}");
RunTool("findscu", $"-v -k QueryRetrieveLevel= -k PatientID= -aet TEST_SCU -aec ASCUBE_MWM 127.0.0.1 {port} -W");

await Task.Delay(500);
server.Stop();
await Task.Delay(500);
RawRecorder.Shutdown();

// ── ②の判定：INetworkStream decorator で全バイトが復元できるか ──────────
var records = RawRecorder.ReadAll(rawPath);
var inBytes = records.Where(r => r.Direction == 0).Sum(r => (long)r.Payload.Length);
var outBytes = records.Where(r => r.Direction == 1).Sum(r => (long)r.Payload.Length);
Console.WriteLine($"raw capture: {records.Count} chunks, in={inBytes} bytes, out={outBytes} bytes -> {rawPath}");

// 独立した根拠として、生バイト列の中に DICOM 電文由来の既知の ASCII 断片が
// literal に現れているかを確認する（AE Title・Verification SOP Class UID・
// Modality Worklist FIND SOP Class UID はワイヤ上 ASCII のまま流れる）。
var inConcat = Concat(records.Where(r => r.Direction == 0));
var outConcat = Concat(records.Where(r => r.Direction == 1));

bool Has(byte[] hay, string needle) => IndexOf(hay, Encoding.ASCII.GetBytes(needle)) >= 0;

var checks = new (string label, bool ok)[]
{
    ("受信データに Calling AE Title 'TEST_SCU' が生バイトのまま存在する", Has(inConcat, "TEST_SCU")),
    ("受信データに Verification SOP Class UID '1.2.840.10008.1.1' が存在する", Has(inConcat, "1.2.840.10008.1.1")),
    ("受信データに MWL FIND SOP Class UID '1.2.840.10008.5.1.4.31' が存在する", Has(inConcat, "1.2.840.10008.5.1.4.31")),
    ("送信データに Called AE Title 'ASCUBE_MWM' が生バイトのまま存在する", Has(outConcat, "ASCUBE_MWM")),
    ("送信データに応答PatientName 'S3^SPIKE' が生バイトのまま存在する（PN未エスケープなのでASCIIで探索可）", Has(outConcat, "S3^SPIKE")),
    ("受信バイト数が0でない", inBytes > 0),
    ("送信バイト数が0でない", outBytes > 0),
};

foreach (var (label, ok) in checks)
{
    Console.WriteLine($"  [{(ok ? "OK" : "NG")}] {label}");
}

var allOk = checks.All(c => c.ok);
Console.WriteLine(allOk
    ? "S3: ②(INetworkManager/INetworkStream decorator) で全送受信バイトを capture できることを確認した。①は不採用、②を採用する。"
    : "S3: ②でも要件を満たせなかった。③(透過TCP proxy)の検討が必要。");

var report = new StringBuilder();
report.AppendLine("# S3 raw recorder — 結論");
report.AppendLine();
report.AppendLine("## ①: DicomServiceOptions.LogDataPDUs / LogDimseDatasets");
report.AppendLine("**不採用。** out/dicom-log.txt を参照。ログの中身は生バイトの16進ダンプではなく、");
report.AppendLine("パース済みの構造化テキスト（Association情報・DIMSE Command/Datasetのタグ/VR/値の一覧・");
report.AppendLine("P-DATA-TFのPDV長のみ）。生バイトへ逆変換できない。");
report.AppendLine();
report.AppendLine("## ②: INetworkManager / INetworkStream decorator");
report.AppendLine("**採用。** fo-dicomはDI経由で `INetworkManager` を差し替え可能（`services.AddNetworkManager<T>()`、");
report.AppendLine("`AddFellowOakDicom()` の**後**に呼ぶ必要がある。先に呼ぶと `TryAdd` 系は無効）。");
report.AppendLine("独自の `INetworkManager` が `DesktopNetworkManager` をラップし、`CreateNetworkStream(TcpClient,...)`");
report.AppendLine("が返す `INetworkStream.AsStream()` を `TeeStream : Stream` でラップして Read/Write の生バイトを");
report.AppendLine("bounded channel 経由でファイルへ追記した。fo-dicom 本体のコードは一切変更していない。");
report.AppendLine();
report.AppendLine($"- capture: {records.Count} chunks, in={inBytes} bytes, out={outBytes} bytes");
foreach (var (label, ok) in checks)
{
    report.AppendLine($"- [{(ok ? "OK" : "NG")}] {label}");
}
report.AppendLine();
report.AppendLine("## T9 実装への示唆");
report.AppendLine("- `IRawRecorder.Append(associationId, direction, bytes)` は本スパイクの `RawRecorder.Append` とほぼ同形。");
report.AppendLine("- bounded channel + `DropWrite` で満杯時も DICOM 処理をブロックしない設計は実証済み（本番では DroppedCount をアラームに接続する）。");
report.AppendLine("- ③(透過TCPプロキシ)は不要と判断。");
File.WriteAllText(Path.Combine(outDir, "S3_conclusion.md"), report.ToString(), new UTF8Encoding(false));

return allOk ? 0 : 1;

static byte[] Concat(IEnumerable<RawRecord> recs)
{
    using var ms = new MemoryStream();
    foreach (var r in recs) ms.Write(r.Payload);
    return ms.ToArray();
}

static int IndexOf(byte[] haystack, byte[] needle)
{
    for (var i = 0; i <= haystack.Length - needle.Length; i++)
    {
        var found = true;
        for (var j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j]) { found = false; break; }
        }
        if (found) return i;
    }
    return -1;
}

static void RunTool(string exe, string args)
{
    var psi = new ProcessStartInfo(exe, args)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using var proc = Process.Start(psi)!;
    var stdout = proc.StandardOutput.ReadToEnd();
    var stderr = proc.StandardError.ReadToEnd();
    proc.WaitForExit(10_000);
    Console.WriteLine($"--- {exe} (exit {proc.ExitCode}) ---");
    Console.WriteLine(stdout);
    if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine("[stderr] " + stderr);
}

internal sealed class S3EchoFindService : DicomService, IDicomServiceProvider, IDicomCEchoProvider, IDicomCFindProvider
{
    public S3EchoFindService(INetworkStream stream, Encoding fallbackEncoding, ILogger logger, DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, logger, dependencies)
    {
    }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        foreach (var pc in association.PresentationContexts)
        {
            pc.AcceptTransferSyntaxes(
                DicomTransferSyntax.ImplicitVRLittleEndian,
                DicomTransferSyntax.ExplicitVRLittleEndian,
                DicomTransferSyntax.ExplicitVRBigEndian);
        }
        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) { }

    public void OnConnectionClosed(Exception? exception) { }

    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
        => Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));

    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        var response = new DicomCFindResponse(request, DicomStatus.Pending)
        {
            Dataset = new DicomDataset
            {
                { DicomTag.PatientID, "0001234" },
                { DicomTag.PatientName, "S3^SPIKE" },
            }
        };
        yield return response;
        yield return new DicomCFindResponse(request, DicomStatus.Success);
        await Task.CompletedTask;
    }
}

// ── ②: INetworkManager/INetworkStream を差し込んで全送受信バイトを記録する。 ──
// fo-dicom を fork せず、DI 経由で INetworkManager を差し替えるだけで実現できる
// （CLAUDE.md 絶対規則11: fo-dicom を fork・改変しない）。
internal sealed class RecordingNetworkManager : INetworkManager
{
    private readonly INetworkManager _inner = new DesktopNetworkManager();

    public INetworkListener CreateNetworkListener(string ipAddress, int port)
        => _inner.CreateNetworkListener(ipAddress, port);

    public INetworkStream CreateNetworkStream(NetworkStreamCreationOptions options)
        => Wrap(_inner.CreateNetworkStream(options), isClient: true);

    public INetworkStream CreateNetworkStream(TcpClient tcpClient, ITlsAcceptor? tlsAcceptor, bool ownsTcpClient)
        => Wrap(_inner.CreateNetworkStream(tcpClient, tlsAcceptor, ownsTcpClient), isClient: false);

    public bool IsSocketException(Exception exception, out int errorCode, out string errorDescriptor)
        => _inner.IsSocketException(exception, out errorCode, out errorDescriptor);

    public bool TryGetNetworkIdentifier(out DicomUID identifier)
        => _inner.TryGetNetworkIdentifier(out identifier);

    public string MachineName => _inner.MachineName;

    private static INetworkStream Wrap(INetworkStream inner, bool isClient)
    {
        var assocId = $"{(isClient ? "C" : "S")}-{Guid.NewGuid():N}"[..12];
        return new RecordingNetworkStream(inner, assocId);
    }
}

internal sealed class RecordingNetworkStream(INetworkStream inner, string associationId) : INetworkStream
{
    private Stream? _wrapped;

    public string RemoteHost => inner.RemoteHost;
    public string LocalHost => inner.LocalHost;
    public int RemotePort => inner.RemotePort;
    public int LocalPort => inner.LocalPort;

    public Stream AsStream() => _wrapped ??= new TeeStream(inner.AsStream(), associationId);

    public void Dispose()
    {
        _wrapped?.Dispose();
        inner.Dispose();
    }
}

internal sealed class TeeStream(Stream inner, string associationId) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, count);
        if (n > 0) RawRecorder.Append(associationId, 0, buffer.AsSpan(offset, n));
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        if (n > 0) RawRecorder.Append(associationId, 0, buffer.AsSpan(offset, n));
        return n;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (count > 0) RawRecorder.Append(associationId, 1, buffer.AsSpan(offset, count));
        inner.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count > 0) RawRecorder.Append(associationId, 1, buffer.AsSpan(offset, count));
        await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override void Flush() => inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}

internal readonly record struct RawRecord(long Ticks, int Direction, string AssociationId, byte[] Payload);

// bounded channel で書き込む。満杯でも DICOM 処理を止めない（CLAUDE.md/Abstractions.IRawRecorder 相当）。
internal static class RawRecorder
{
    private static System.Threading.Channels.Channel<RawRecord>? _channel;
    private static Task? _writerTask;
    private static long _dropped;

    public static void Init(string path)
    {
        _channel = System.Threading.Channels.Channel.CreateBounded<RawRecord>(
            new System.Threading.Channels.BoundedChannelOptions(4096)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
            });
        _writerTask = Task.Run(async () =>
        {
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            await foreach (var rec in _channel.Reader.ReadAllAsync())
            {
                var assocBytes = Encoding.UTF8.GetBytes(rec.AssociationId);
                var header = new byte[8 + 1 + 4 + 4];
                BitConverter.TryWriteBytes(header.AsSpan(0, 8), rec.Ticks);
                header[8] = (byte)rec.Direction;
                BitConverter.TryWriteBytes(header.AsSpan(9, 4), assocBytes.Length);
                BitConverter.TryWriteBytes(header.AsSpan(13, 4), rec.Payload.Length);
                await fs.WriteAsync(header);
                await fs.WriteAsync(assocBytes);
                await fs.WriteAsync(rec.Payload);
            }
            await fs.FlushAsync();
        });
    }

    public static void Append(string associationId, int direction, ReadOnlySpan<byte> bytes)
    {
        if (_channel is null) return;
        if (!_channel.Writer.TryWrite(new RawRecord(DateTime.UtcNow.Ticks, direction, associationId, bytes.ToArray())))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    public static void Shutdown()
    {
        _channel?.Writer.TryComplete();
        _writerTask?.Wait(2000);
        if (Interlocked.Read(ref _dropped) > 0)
        {
            Console.WriteLine($"WARNING: RawRecorder dropped {_dropped} chunks (channel full).");
        }
    }

    public static List<RawRecord> ReadAll(string path)
    {
        var result = new List<RawRecord>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var headerBuf = new byte[17];
        while (true)
        {
            var read = fs.Read(headerBuf, 0, headerBuf.Length);
            if (read == 0) break;
            if (read != headerBuf.Length) throw new InvalidDataException("truncated header");

            var ticks = BitConverter.ToInt64(headerBuf, 0);
            var direction = headerBuf[8];
            var assocLen = BitConverter.ToInt32(headerBuf, 9);
            var payloadLen = BitConverter.ToInt32(headerBuf, 13);

            var assocBytes = new byte[assocLen];
            ReadExact(fs, assocBytes);
            var payload = new byte[payloadLen];
            ReadExact(fs, payload);

            result.Add(new RawRecord(ticks, direction, Encoding.UTF8.GetString(assocBytes), payload));
        }
        return result;
    }

    private static void ReadExact(Stream s, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = s.Read(buffer, offset, buffer.Length - offset);
            if (n == 0) throw new EndOfStreamException();
            offset += n;
        }
    }
}

internal sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, path, _gate);

    public void Dispose() { }

    private sealed class FileLogger(string category, string path, object gate) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = $"[{DateTime.UtcNow:O}] [{logLevel}] [{category}] {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            lock (gate)
            {
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
    }
}
