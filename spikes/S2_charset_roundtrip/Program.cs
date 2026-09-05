using System.Diagnostics;
using System.Text;
using FellowOakDicom;

// ── CLAUDE.md 絶対規則 #6/#7 の検証が本スパイクの目的。 ──────────────
// --no-register を付けると Encoding.RegisterProvider を呼ばずに走らせ、
// 「呼び忘れると何が起きるか」を同じテストケースで再現できるようにする。
var noRegister = args.Contains("--no-register");
if (!noRegister)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}

var outDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out");
Directory.CreateDirectory(outDir);
outDir = Path.GetFullPath(outDir);

var stress30 = string.Concat(Enumerable.Repeat("アイウエオ", 6)); // 30文字
var stress30Half = string.Concat(Enumerable.Repeat("ｱｲｳｴｵ", 6));   // 30文字（半角）

var cases = new List<TestCase>
{
    new(
        Name: "UTF8_features",
        CharsetValues: ["ISO_IR 192"],
        PatientName: "=髙﨑^德永=タカザキ^トクナガー",
        Description: "外字(髙 U+9AD9,﨑 U+FA11,德 U+5FB7)・濁点・長音を含む氏名。UTF-8。"),

    new(
        Name: "ISO2022_features",
        CharsetValues: ["ISO 2022 IR 13", "ISO 2022 IR 87"],
        PatientName: "=髙﨑^德永=ﾀｶｻﾞｷ^ﾄｸﾅｶﾞｰ",
        Description: "同じ氏名を符号拡張(IR13\\IR87)で。半角カナは濁点分解(ｻ+ﾞ)・半角長音(ｰ)。"),

    new(
        Name: "UTF8_stress30",
        CharsetValues: ["ISO_IR 192"],
        PatientName: stress30,
        Description: "全角カナ30文字級（VM/長さの境界確認）。UTF-8。"),

    new(
        Name: "ISO2022_stress30",
        CharsetValues: ["ISO 2022 IR 13"],
        PatientName: stress30Half,
        Description: "半角カナ30文字級（VM/長さの境界確認）。符号拡張(IR13のみ)。"),
};

var allPass = true;
var report = new StringBuilder();
report.AppendLine($"# S2 charset roundtrip report ({(noRegister ? "NO-REGISTER" : "registered")})");
report.AppendLine();

foreach (var tc in cases)
{
    var (ok, detail) = RunCase(tc, outDir);
    allPass &= ok;
    report.AppendLine($"## {tc.Name} — {(ok ? "PASS" : "FAIL")}");
    report.AppendLine(detail);
    report.AppendLine();
}

var reportPath = Path.Combine(outDir, noRegister ? "report_no-register.md" : "report.md");
File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"Report written to {reportPath}");
Console.WriteLine(allPass ? "ALL PASS" : "SOME FAILED");
return allPass ? 0 : 1;

static (bool ok, string detail) RunCase(TestCase tc, string outDir)
{
    var sb = new StringBuilder();
    var filePath = Path.Combine(outDir, tc.Name + ".dcm");

    try
    {
        var ds = new DicomDataset();

        // CLAUDE.md 絶対規則 #6: SpecificCharacterSet を最初に設定する。
        ds.Add(DicomTag.SpecificCharacterSet, tc.CharsetValues);

        ds.Add(DicomTag.SOPClassUID, DicomUID.ModalityWorklistInformationModelFind);
        ds.Add(DicomTag.SOPInstanceUID, DicomUID.Generate());
        ds.Add(DicomTag.PatientID, "0001234");
        ds.Add(DicomTag.PatientName, tc.PatientName);

        var file = new DicomFile(ds);
        file.Save(filePath);
    }
    catch (Exception ex)
    {
        sb.AppendLine($"- 書き込み失敗: {ex.GetType().Name}: {ex.Message}");
        return (false, sb.ToString());
    }

    var ok = true;

    // ① 生バイトでエスケープシーケンスを確認（コンソールの文字コードに依存しない判定）。
    var raw = File.ReadAllBytes(filePath);
    sb.AppendLine($"- charset申告: {string.Join("\\", tc.CharsetValues)}");
    sb.AppendLine($"- ファイル: {filePath} ({raw.Length} bytes)");

    if (tc.CharsetValues.Contains("ISO 2022 IR 87"))
    {
        var hasKanjiEsc = ContainsSeq(raw, [0x1B, 0x24, 0x42]);
        var hasAsciiEsc = ContainsSeq(raw, [0x1B, 0x28, 0x42]);
        sb.AppendLine($"- 1B 24 42 (漢字切替) 検出: {hasKanjiEsc}");
        sb.AppendLine($"- 1B 28 42 (ASCII復帰) 検出: {hasAsciiEsc}");
        if (!hasKanjiEsc) { ok = false; sb.AppendLine("  → FAIL: 漢字切替エスケープが出力されていない。"); }
    }
    if (tc.CharsetValues.Contains("ISO 2022 IR 13"))
    {
        var hasKanaEsc = ContainsSeq(raw, [0x1B, 0x29, 0x49]);
        sb.AppendLine($"- 1B 29 49 (半角カナ切替) 検出: {hasKanaEsc}");
        if (!hasKanaEsc) { ok = false; sb.AppendLine("  → FAIL: 半角カナ切替エスケープが出力されていない。"); }
    }

    // ② fo-dicom 自身での再読込（自己無矛盾の確認。独立検証ではない点に注意）。
    try
    {
        var reread = DicomFile.Open(filePath);
        var selfReadName = reread.Dataset.GetString(DicomTag.PatientName);
        var selfOk = selfReadName == tc.PatientName;
        sb.AppendLine($"- fo-dicom自己再読込: {(selfOk ? "一致" : "不一致")}" + (selfOk ? "" : $" (got: {ToCodepointHex(selfReadName)})"));
        ok &= selfOk;
    }
    catch (Exception ex)
    {
        sb.AppendLine($"- fo-dicom自己再読込 失敗: {ex.GetType().Name}: {ex.Message}");
        ok = false;
    }

    // ③ DCMTK dcmdump（別実装）で独立に decode し、UTF-8 として比較する。
    //    コンソールの CP932 変換を経由しないよう、標準出力を直接 UTF-8 バイト列として捕捉する。
    //    +L: 値を省略せず全文表示（既定では長い値が "..." で切られる）。
    string dcmdumpOut;
    try
    {
        var psi = new ProcessStartInfo("dcmdump", $"+U8 +L \"{filePath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        using var proc = Process.Start(psi)!;
        dcmdumpOut = proc.StandardOutput.ReadToEnd();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit(10_000);
        if (proc.ExitCode != 0)
        {
            sb.AppendLine($"- dcmdump 失敗 (exit {proc.ExitCode}): {err}");
            return (false, sb.ToString());
        }
    }
    catch (Exception ex)
    {
        sb.AppendLine($"- dcmdump 起動失敗: {ex.GetType().Name}: {ex.Message}");
        return (false, sb.ToString());
    }

    var line = dcmdumpOut
        .Split('\n')
        .FirstOrDefault(l => l.Contains("PatientName") || l.Contains("(0010,0010)"));

    sb.AppendLine($"- dcmdump出力行: {line?.Trim()}");

    if (line is null)
    {
        sb.AppendLine("  → FAIL: dcmdump出力に PatientName が見つからない。");
        return (false, sb.ToString());
    }

    // dcmdump は "(0010,0010) PN [値]  # ..." の形式で出す。[] の中身を取り出して比較する。
    var start = line.IndexOf('[');
    var end = line.LastIndexOf(']');
    var dumpedValue = (start >= 0 && end > start) ? line[(start + 1)..end] : null;

    var match = dumpedValue == tc.PatientName;
    sb.AppendLine($"- dcmdump値と元の文字列: {(match ? "一致" : "不一致")}");
    if (!match)
    {
        sb.AppendLine($"  期待: {ToCodepointHex(tc.PatientName)}");
        sb.AppendLine($"  実際: {ToCodepointHex(dumpedValue ?? "(null)")}");
        ok = false;
    }

    return (ok, sb.ToString());
}

static bool ContainsSeq(byte[] haystack, byte[] needle)
{
    for (var i = 0; i <= haystack.Length - needle.Length; i++)
    {
        var found = true;
        for (var j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j]) { found = false; break; }
        }
        if (found) return true;
    }
    return false;
}

static string ToCodepointHex(string? s)
    => s is null ? "(null)" : string.Join(' ', s.Select(c => ((int)c).ToString("X4")));

internal sealed record TestCase(string Name, string[] CharsetValues, string PatientName, string Description);
