using System.Security.Cryptography;
using System.Text;
using Ascube.Mwm.Scp.Capture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ascube.Mwm.Scp.Tests.Capture;

/// <summary>T9：生バイト記録（固定ヘッダ＋追記専用バイナリ、index JSONL、容量ロールオーバー、SHA-256）。</summary>
public class RawCaptureWriterTests
{
    private static RawCaptureWriter CreateWriter(string dir, long maxFileSizeBytes = 200L * 1024 * 1024, int channelCapacity = 4096) =>
        new(
            new RawCaptureOptions { OutputDirectory = dir, MaxFileSizeBytes = maxFileSizeBytes, ChannelCapacity = channelCapacity },
            NullLogger<RawCaptureWriter>.Instance);

    [Fact]
    public async Task Append_WritesBinAndIndexFiles()
    {
        using var temp = new TempDir();
        var writer = CreateWriter(temp.Path);
        await writer.StartAsync(CancellationToken.None);

        writer.Append("assoc-1", 0, "HELLO"u8);
        await writer.StopAsync(CancellationToken.None);

        var binFiles = Directory.GetFiles(temp.Path, "*.bin");
        var indexFiles = Directory.GetFiles(temp.Path, "*.index.jsonl");
        Assert.Single(binFiles);
        Assert.Single(indexFiles);
    }

    [Fact]
    public async Task Append_BinFile_ContainsPayloadBytesLiterally()
    {
        using var temp = new TempDir();
        var writer = CreateWriter(temp.Path);
        await writer.StartAsync(CancellationToken.None);

        writer.Append("assoc-1", 1, "PATIENTNAME=TANAKA"u8);
        await writer.StopAsync(CancellationToken.None);

        var binFile = Directory.GetFiles(temp.Path, "*.bin").Single();
        var bytes = await File.ReadAllBytesAsync(binFile);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.Contains("PATIENTNAME=TANAKA", text);
        Assert.Contains("assoc-1", text); // association id も生バイトのまま書かれる
    }

    [Fact]
    public async Task Append_IndexJsonl_RecordsDirectionAndAssociationId()
    {
        using var temp = new TempDir();
        var writer = CreateWriter(temp.Path);
        await writer.StartAsync(CancellationToken.None);

        writer.Append("assoc-xyz", 0, "IN"u8);
        writer.Append("assoc-xyz", 1, "OUT"u8);
        await writer.StopAsync(CancellationToken.None);

        var indexFile = Directory.GetFiles(temp.Path, "*.index.jsonl").Single();
        var lines = await File.ReadAllLinesAsync(indexFile);

        Assert.Equal(2, lines.Length);
        Assert.Contains("\"assoc-xyz\"", lines[0]);
        Assert.Contains("\"Direction\":0", lines[0]);
        Assert.Contains("\"Direction\":1", lines[1]);
    }

    [Fact]
    public async Task StopAsync_WritesSha256SidecarMatchingActualFileHash()
    {
        using var temp = new TempDir();
        var writer = CreateWriter(temp.Path);
        await writer.StartAsync(CancellationToken.None);

        writer.Append("assoc-1", 0, "PAYLOAD-FOR-HASH-CHECK"u8);
        await writer.StopAsync(CancellationToken.None);

        var binFile = Directory.GetFiles(temp.Path, "*.bin").Single();
        var sha256File = $"{binFile}.sha256";
        Assert.True(File.Exists(sha256File));

        var recordedHash = (await File.ReadAllTextAsync(sha256File)).Trim();
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(binFile)));
        Assert.Equal(actualHash, recordedHash);
    }

    [Fact]
    public async Task Append_ExceedingMaxFileSize_RotatesToANewFile()
    {
        using var temp = new TempDir();
        // 1チャンクごとに確実にロールオーバーするよう、極端に小さい上限にする。
        var writer = CreateWriter(temp.Path, maxFileSizeBytes: 10);
        await writer.StartAsync(CancellationToken.None);

        writer.Append("assoc-1", 0, "AAAAAAAAAA"u8);
        writer.Append("assoc-2", 0, "BBBBBBBBBB"u8);
        writer.Append("assoc-3", 0, "CCCCCCCCCC"u8);
        await writer.StopAsync(CancellationToken.None);

        var binFiles = Directory.GetFiles(temp.Path, "*.bin");
        Assert.True(binFiles.Length >= 2, $"ロールオーバーが発生しませんでした（ファイル数={binFiles.Length}）");

        // 各ファイルが確定時に SHA-256 サイドカーを持つこと。
        foreach (var bin in binFiles)
        {
            Assert.True(File.Exists($"{bin}.sha256"));
        }
    }

    [Fact]
    public async Task Append_ChannelFull_IncrementsDroppedCount_DoesNotThrow()
    {
        using var temp = new TempDir();
        var writer = CreateWriter(temp.Path, channelCapacity: 1);

        // StartAsync を呼ばない＝書き込みループが動かない状態でチャンネルを詰まらせる。
        for (var i = 0; i < 50; i++)
        {
            writer.Append("assoc-1", 0, "X"u8);
        }

        Assert.True(writer.DroppedCount > 0);
    }

    [Fact]
    public void Append_EmptyPayload_IsIgnoredWithoutError()
    {
        using var temp = new TempDir();
        var writer = CreateWriter(temp.Path);

        writer.Append("assoc-1", 0, ReadOnlySpan<byte>.Empty);

        Assert.Equal(0, writer.DroppedCount);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("ascube-mwm-capture-tests-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
