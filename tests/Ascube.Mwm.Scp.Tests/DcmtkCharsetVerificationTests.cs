using System.Diagnostics;
using System.Text.Json.Nodes;
using Ascube.Mwm.Abstractions;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// T13「絶対に削らない」項目3：T7 の文字コード3案（①②④）が DCMTK（自作 fo-dicom 実装とは別実装）で読めること。
/// 規則19 / 実装指示書「検証の注意」：自作 SCU と自作 SCP は同じ fo-dicom を使うため、ライブラリ由来のバグは
/// 両方に等しく現れ自作テスタでは検出できない。T7 では DCMTK（<c>findscu</c>/<c>dcmdump</c>）で手動確認したが、
/// ここでは同じ検証を自動テストとして固定する（このマシンの PATH に DCMTK 3.7.0（chocolatey）が入っている前提。
/// 無い場合はテストが失敗する＝「DCMTKでの検証が抜けている」ことをそのまま検出する設計）。
///
/// 検証データは T7 の手動確認と同じ、あえて難しい文字（外字 髙﨑德・濁点入り半角カナ ﾀｹﾀﾞ）を使う。
/// </summary>
public class DcmtkCharsetVerificationTests : IDisposable
{
    private readonly DirectoryInfo _extractDir = Directory.CreateTempSubdirectory("ascube-mwm-dcmtk-verify-");

    public void Dispose()
    {
        try
        {
            _extractDir.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static WorkItemView MakeCandidate() => new()
    {
        WorkItemId = "wi-charset-verify",
        StudyInstanceUid = "2.25.222222222222222222222222222222222222",
        StablePatientId = "000098765432",
        FamilyNameKanji = "髙﨑",
        GivenNameKanji = "德",
        FamilyNameKana = "ﾀｹﾀﾞ",
        GivenNameKana = "ﾀﾛｳ",
        BirthDate = "19700101",
        Sex = Sex.Male,
        ScheduledDate = "20260910",
        RequestedProcedureDesc = "骨密度測定",
    };

    // config/profiles/BMD_HOLOGIC.jsonc（案①）と同じ charset 設定。
    [Fact]
    public Task Plan1_IsoIr192_KanaFullKanjiKanaFull_IsReadableByDcmtk() => VerifyPlanAsync(
        new JsonObject
        {
            ["specificCharacterSet"] = "ISO_IR 192",
            ["patientName"] = new JsonObject { ["group1"] = "kanaFull", ["group2"] = "kanji", ["group3"] = "kanaFull" },
        });

    // config/profiles/BMD_HOLOGIC.alt1.jsonc（案②）と同じ charset 設定。
    [Fact]
    public Task Plan2_IsoIr192_Group1None_IsReadableByDcmtk() => VerifyPlanAsync(
        new JsonObject
        {
            ["specificCharacterSet"] = "ISO_IR 192",
            ["patientName"] = new JsonObject { ["group1"] = "none", ["group2"] = "kanji", ["group3"] = "kanaFull" },
        });

    // config/profiles/BMD_HOLOGIC.alt2.jsonc（案④）と同じ charset 設定。
    [Fact]
    public async Task Plan4_IsoIr13_KanaHalfOnly_IsReadableByDcmtk_AndAllBytesAreInJisX0201Range()
    {
        var bytes = await VerifyPlanAsync(
            new JsonObject
            {
                ["specificCharacterSet"] = "ISO_IR 13",
                ["patientName"] = new JsonObject { ["group1"] = "kanaHalf", ["group2"] = "none", ["group3"] = "none" },
            });

        // 規則19：往復検証を fo-dicom の decoder に頼らない。dcmdump に加えて生バイトも直接検査する。
        // ISO_IR 13（JIS X0201片仮名）は 0x20-0x7E（ASCII範囲）か 0xA1-0xDF（半角カナ）のみを許す。
        // CP932（Shift_JIS）漢字の先頭バイト（0x81-0x9F/0xE0-0xEF）が紛れ込んでいないことを確認する。
        // ファイル全体を範囲チェックすると tag/length 等の非文字列バイトを誤検出するため、
        // PatientName 要素（Explicit VR 前提）の値バイトだけを狙って取り出す。
        var value = ExtractExplicitVrShortValue(bytes, group: 0x0010, element: 0x0010, vr: "PN");
        Assert.True(value is { Length: > 0 }, "PatientName 要素が Explicit VR 形式で見つからなかった（テスト前提の崩れ）。");

        foreach (var b in value!)
        {
            var inAscii = b is >= 0x20 and <= 0x7E;
            var inHalfKana = b is >= 0xA1 and <= 0xDF;
            Assert.True(inAscii || inHalfKana, $"PatientName の生バイトに JIS X0201 範囲外(0x{b:X2})が含まれている（漢字混入等の疑い）。");
        }
    }

    private static byte[]? ExtractExplicitVrShortValue(byte[] fileBytes, ushort group, ushort element, string vr)
    {
        // Explicit VR の短形式（PN 等）：tag(4) + VR(2 ascii) + length(2, LE) + value(length)。
        // ファイル全体から tag+VR の6バイト一致を探す（preamble/meta group の厳密なオフセット計算を避ける。
        // この6バイトの偶然一致は現実的にありえない）。
        var needle = new byte[]
        {
            (byte)(group & 0xFF), (byte)(group >> 8),
            (byte)(element & 0xFF), (byte)(element >> 8),
            (byte)vr[0], (byte)vr[1],
        };

        for (var i = 0; i <= fileBytes.Length - needle.Length - 2; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (fileBytes[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
            {
                continue;
            }

            var lengthOffset = i + needle.Length;
            var length = (ushort)(fileBytes[lengthOffset] | (fileBytes[lengthOffset + 1] << 8));
            var valueOffset = lengthOffset + 2;
            if (valueOffset + length > fileBytes.Length)
            {
                continue;
            }

            return fileBytes[valueOffset..(valueOffset + length)];
        }

        return null;
    }

    private static async Task<byte[]> VerifyPlanAsync(JsonObject charset)
    {
        var dcmtk = LocateDcmtk();
        var repository = new FakeWorklistRepository { Current = MakeCandidate() };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build(charset: charset));

        var extractDir = Directory.CreateTempSubdirectory("ascube-mwm-dcmtk-verify-run-").FullName;
        try
        {
            // -xe：Explicit VR Little Endian を最優先で提案する（生バイト検証側が Explicit VR の
            // 短形式(tag+VR+2byte長)を前提にしているため、折衝結果を固定する）。
            var findArgs = $"-aet TEST_SCU -aec ASCUBE_MWM -W -k \"0008,0052=WORKLIST\" -xe -X -od \"{extractDir}\" 127.0.0.1 {server.Port}";
            var findResult = await RunProcessAsync(dcmtk.FindScu, findArgs);
            Assert.True(findResult.ExitCode == 0,
                $"findscu が異常終了した(exit={findResult.ExitCode})。stdout:\n{findResult.StdOut}\nstderr:\n{findResult.StdErr}");

            var dcmFile = System.IO.Path.Combine(extractDir, "rsp0001.dcm");
            Assert.True(File.Exists(dcmFile), $"findscu -X が応答ファイルを書き出さなかった（{dcmFile} が無い）。stderr:\n{findResult.StdErr}");

            var dumpResult = await RunProcessAsync(dcmtk.DcmDump, $"\"{dcmFile}\"");
            Assert.True(dumpResult.ExitCode == 0,
                $"dcmdump が応答データセットを読めなかった(exit={dumpResult.ExitCode})＝DCMTK基準で規格不適合。stdout:\n{dumpResult.StdOut}\nstderr:\n{dumpResult.StdErr}");
            Assert.DoesNotContain("Illegal byte sequence", dumpResult.StdOut, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Illegal byte sequence", dumpResult.StdErr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PatientName", dumpResult.StdOut, StringComparison.Ordinal);

            return await File.ReadAllBytesAsync(dcmFile);
        }
        finally
        {
            try
            {
                Directory.Delete(extractDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static (string FindScu, string DcmDump) LocateDcmtk()
    {
        var findScu = FindOnPath("findscu.exe") ?? FindOnPath("findscu");
        var dcmDump = FindOnPath("dcmdump.exe") ?? FindOnPath("dcmdump");

        if (findScu is null || dcmDump is null)
        {
            throw new InvalidOperationException(
                "DCMTK（findscu/dcmdump）が PATH に見つからない。この開発環境では chocolatey（"
                + @"C:\ProgramData\chocolatey\bin\）に入っている前提。実装指示書「検証の注意」により、"
                + "この検証は自作ツールで代替してはならない。DCMTK をインストールして PATH を通すこと。");
        }

        return (findScu, dcmDump);
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(System.IO.Path.PathSeparator))
        {
            if (dir.Length == 0)
            {
                continue;
            }

            var candidate = System.IO.Path.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }
}
