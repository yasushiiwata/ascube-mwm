using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ascube.Mwm.Scp.Capture;

/// <summary>
/// 送受信の生バイトを追記専用バイナリ＋index JSONL に記録する（実装指示書 v2 T9）。
/// bounded channel（<see cref="BoundedChannelFullMode.DropWrite"/>）で書き込むため、
/// 満杯でも <see cref="Append"/> 呼び出し元（DICOM 処理）をブロックしない。欠落は件数で記録する。
/// 日次／容量ロールオーバー時に確定したファイルの SHA-256 を <c>.sha256</c> サイドカーに書く。
/// fo-dicom 本体は無改変（規則11）。T0/S-3 スパイクで検証済みの方式（②: INetworkManager/INetworkStream decorator）。
/// </summary>
public sealed class RawCaptureWriter : IHostedService, IDisposable
{
    private readonly RawCaptureOptions _options;
    private readonly ILogger<RawCaptureWriter> _logger;
    private readonly Channel<CaptureChunk> _channel;
    private Task? _writerTask;
    private long _droppedCount;

    private FileStream? _currentBin;
    private StreamWriter? _currentIndex;
    private string? _currentBinPath;
    private DateOnly _currentFileDate;
    private long _currentFileOffset;

    public RawCaptureWriter(RawCaptureOptions options, ILogger<RawCaptureWriter> logger)
    {
        _options = options;
        _logger = logger;
        // FullMode は既定の Wait のまま使う。TryWrite は Wait モードでも非ブロッキングで、
        // 満杯なら即 false を返すため、満杯時に DICOM 処理をブロックせず自前で欠落をカウントできる
        // （BoundedChannelFullMode.DropWrite は TryWrite が常に true を返してしまい欠落を検知できない）。
        _channel = Channel.CreateBounded<CaptureChunk>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>いま欠落した記録の累計件数（アラーム監視用）。</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>
    /// 1チャンクを記録キューに積む。DICOM の送受信パスから直接呼ばれるため、絶対にブロック・例外を投げない。
    /// </summary>
    public void Append(string associationId, byte direction, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        var chunk = new CaptureChunk(DateTime.UtcNow.Ticks, direction, associationId, bytes.ToArray());
        if (!_channel.Writer.TryWrite(chunk))
        {
            Interlocked.Increment(ref _droppedCount);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.OutputDirectory);
        _writerTask = Task.Run(() => WriteLoopAsync(CancellationToken.None));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        if (_writerTask is not null)
        {
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }

        CloseCurrentFile();

        if (DroppedCount > 0)
        {
            _logger.LogWarning("RawCaptureWriter: 記録できなかったチャンクが {Count} 件ありました（channel full）", DroppedCount);
        }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in _channel.Reader.ReadAllAsync(ct))
            {
                WriteChunk(chunk);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RawCaptureWriter: 書き込みループが異常終了しました。以後の記録は失われます");
        }
    }

    private void WriteChunk(CaptureChunk chunk)
    {
        var chunkDate = DateOnly.FromDateTime(new DateTime(chunk.Ticks, DateTimeKind.Utc));
        var assocBytes = Encoding.UTF8.GetBytes(chunk.AssociationId);
        var chunkSize = 8 + 1 + 4 + 4 + assocBytes.Length + chunk.Payload.Length;

        EnsureFileFor(chunkDate, chunkSize);

        var header = new byte[17];
        BitConverter.TryWriteBytes(header.AsSpan(0, 8), chunk.Ticks);
        header[8] = chunk.Direction;
        BitConverter.TryWriteBytes(header.AsSpan(9, 4), assocBytes.Length);
        BitConverter.TryWriteBytes(header.AsSpan(13, 4), chunk.Payload.Length);

        var offset = _currentFileOffset;
        _currentBin!.Write(header);
        _currentBin.Write(assocBytes);
        _currentBin.Write(chunk.Payload);
        _currentFileOffset += header.Length + assocBytes.Length + chunk.Payload.Length;

        var indexLine = JsonSerializer.Serialize(new CaptureIndexEntry(
            new DateTime(chunk.Ticks, DateTimeKind.Utc).ToString("O"),
            chunk.Direction,
            chunk.AssociationId,
            offset,
            chunk.Payload.Length));
        _currentIndex!.WriteLine(indexLine);

        // 突然のプロセス終了（クラッシュ・強制終了）でも直近チャンク以外は残るように、都度ディスクへ反映する。
        // このSCPの通信量では書き込みごとのFlushで問題にならない（大量ストリーミングを想定していない）。
        _currentBin.Flush();
        _currentIndex.Flush();
    }

    private void EnsureFileFor(DateOnly chunkDate, int incomingSize)
    {
        var needsRotation = _currentBin is null
            || chunkDate != _currentFileDate
            || _currentFileOffset + incomingSize > _options.MaxFileSizeBytes;

        if (needsRotation)
        {
            CloseCurrentFile();
            OpenNewFile(chunkDate);
        }
    }

    private void OpenNewFile(DateOnly date)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        var baseName = $"capture-{stamp}";
        _currentBinPath = Path.Combine(_options.OutputDirectory, $"{baseName}.bin");
        var indexPath = Path.Combine(_options.OutputDirectory, $"{baseName}.index.jsonl");

        _currentBin = new FileStream(_currentBinPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _currentIndex = new StreamWriter(new FileStream(indexPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        _currentFileDate = date;
        _currentFileOffset = 0;

        _logger.LogInformation("RawCaptureWriter: 新しいキャプチャファイルを開始しました: {Path}", _currentBinPath);
    }

    private void CloseCurrentFile()
    {
        if (_currentBin is null)
        {
            return;
        }

        _currentBin.Flush();
        _currentBin.Dispose();
        _currentIndex!.Flush();
        _currentIndex.Dispose();

        try
        {
            using var sha256 = SHA256.Create();
            using var readStream = File.OpenRead(_currentBinPath!);
            var hash = sha256.ComputeHash(readStream);
            File.WriteAllText($"{_currentBinPath}.sha256", Convert.ToHexStringLower(hash), new UTF8Encoding(false));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "RawCaptureWriter: SHA-256 の計算に失敗しました: {Path}", _currentBinPath);
        }

        _currentBin = null;
        _currentIndex = null;
        _currentBinPath = null;
    }

    public void Dispose()
    {
        CloseCurrentFile();
    }

    private readonly record struct CaptureChunk(long Ticks, byte Direction, string AssociationId, byte[] Payload);

    private sealed record CaptureIndexEntry(string TimestampUtc, byte Direction, string AssociationId, long Offset, int Length);
}
