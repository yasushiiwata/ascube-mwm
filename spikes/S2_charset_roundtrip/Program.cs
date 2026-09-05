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

    // ── 案④（docs/実装指示書.md §1-3）: ISO_IR 13 単独値。符号拡張ではない単一バイト集合。 ──
    new(
        Name: "ISO_IR13_features",
        CharsetValues: ["ISO_IR 13"],
        PatientName: "ﾀﾞﾊﾟｰ･^ﾄｸﾅｶﾞ",
        Description: "案④。濁点(ﾀﾞ=ﾀ+ﾞ)・半濁点(ﾊﾟ=ﾊ+ﾟ)・長音(ｰ)・中点(･)を含む半角カナのみの氏名。"),

    new(
        Name: "ISO_IR13_stress30",
        CharsetValues: ["ISO_IR 13"],
        PatientName: stress30Half,
        Description: "案④ 半角カナ30文字級（VM/長さの境界確認）。"),
};

var allPass = true;
var report = new StringBuilder();
report.AppendLine($"# S2 charset roundtrip report ({(noRegister ? "NO-REGISTER" : "registered")})");
report.AppendLine();

var caseResults = new Dictionary<string, (bool ok, string detail)>();

foreach (var tc in cases)
{
    var (ok, detail) = RunCase(tc, outDir);
    allPass &= ok;
    caseResults[tc.Name] = (ok, detail);
    report.AppendLine($"## {tc.Name} — {(ok ? "PASS" : "FAIL")}");
    report.AppendLine(detail);
    report.AppendLine();
}

// ── 案④ 期待④（実装指示書 §1-3）: 漢字を混ぜたケース。 ──────────────
// ISO_IR 13 は半角カナのみの単一バイト集合であり、漢字は原理的に表現できない。
// 「代替文字で誤魔化して往復一致に見える」のではなく、往復検証で不一致が検出できることを確認する。
var kanjiMixedCase = new TestCase(
    Name: "ISO_IR13_kanji_mixed",
    CharsetValues: ["ISO_IR 13"],
    PatientName: "山田^ﾀﾛｳ",
    Description: "案④に漢字を混ぜた異常系。ISO_IR 13 は漢字を表現できないので、Suppressed 相当の不一致検出ができるかを見る。");
var (kanjiOk, kanjiDetail) = RunKanjiMixedCase(kanjiMixedCase, outDir);
allPass &= kanjiOk;
caseResults[kanjiMixedCase.Name] = (kanjiOk, kanjiDetail);
report.AppendLine($"## {kanjiMixedCase.Name} — {(kanjiOk ? "PASS" : "FAIL")}");
report.AppendLine(kanjiDetail);
report.AppendLine();

// ── alt4 専用レポート（docs/実装指示書.md §1-3 の受入判定用）。 ──────────
if (!noRegister)
{
    var alt4Names = new[] { "ISO_IR13_features", "ISO_IR13_stress30", "ISO_IR13_kanji_mixed" };
    var alt4All = alt4Names.All(n => caseResults[n].ok);
    var alt4 = new StringBuilder();
    alt4.AppendLine("# T0 / alt4（ISO_IR 13 単独値）検証結果");
    alt4.AppendLine();
    alt4.AppendLine("実装指示書 §1-3 の受入条件に対する結果。判定は16進バイト列（生バイト直接抽出）と");
    alt4.AppendLine("DCMTK dcmdump（別実装）で行う。コンソール表示（CP932）は判定に使わない。");
    alt4.AppendLine();
    alt4.AppendLine($"## 総合判定: {(alt4All ? "○ 合格" : "× 不合格")}");
    alt4.AppendLine();
    foreach (var n in alt4Names)
    {
        var (ok, detail) = caseResults[n];
        alt4.AppendLine($"## {n} — {(ok ? "○ PASS" : "× FAIL")}");
        alt4.AppendLine(detail);
        alt4.AppendLine();
    }
    File.WriteAllText(Path.Combine(outDir, "..", "alt4_result.md"), alt4.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"alt4 result written to {Path.GetFullPath(Path.Combine(outDir, "..", "alt4_result.md"))}");
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

    // PatientName の生バイトを常に抽出してレポートに残す（目視の16進判定用。
    // 手で打ち直さずプログラムから直接生成することで CLAUDE.md 禁則16に抵触しない）。
    var pnRaw = ExtractShortVrTagBytes(raw, 0x0010, 0x0010);
    sb.AppendLine($"- PatientName 生バイト(hex): {(pnRaw is null ? "(見つからない)" : ToHex(pnRaw))}");

    if (tc.CharsetValues.Contains("ISO_IR 13"))
    {
        // 案④の判定（実装指示書 §1-3）。
        // 期待②: エスケープシーケンス(1B ...)が現れない（単一バイト集合なので不要）。
        var hasEscape = pnRaw is not null && ContainsSeq(pnRaw, [0x1B]);
        sb.AppendLine($"- 1B (エスケープ) 検出: {hasEscape}（期待: False）");
        if (hasEscape) { ok = false; sb.AppendLine("  → FAIL: 単一バイト集合のはずがエスケープシーケンスが出ている。"); }

        // 期待①: 半角カナは JIS X 0201 の 0xA1〜0xDF の1バイトで出ている。
        // 許容するのはこの範囲のほか、PN の区切り文字 '^'(0x5E) とパディング空白(0x20)のみ。
        if (pnRaw is not null)
        {
            var badBytes = pnRaw.Where(b => b is not 0x5E and not 0x20 && (b < 0xA1 || b > 0xDF)).ToArray();
            sb.AppendLine($"- 0xA1〜0xDF 範囲外(区切り/パディング除く)のバイト数: {badBytes.Length}"
                + (badBytes.Length > 0 ? $"（{ToHex(badBytes)}）" : ""));
            if (badBytes.Length > 0) { ok = false; sb.AppendLine("  → FAIL: 半角カナが JIS X 0201 の範囲外のバイトで出力されている。"); }
        }
        else
        {
            ok = false;
            sb.AppendLine("  → FAIL: PatientName タグ自体が見つからない。");
        }
    }

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

        // 期待③（案④）: 警告なく読める。fo-dicom で読めるだけでは合格にしない（S-2の教訓）。
        if (tc.CharsetValues.Contains("ISO_IR 13"))
        {
            var hasWarning = err.Contains("W:") || err.Contains("E:") || !string.IsNullOrWhiteSpace(err);
            sb.AppendLine($"- dcmdump 警告/エラー出力: {(hasWarning ? err.Trim() : "(なし)")}");
            if (hasWarning) { ok = false; sb.AppendLine("  → FAIL: DCMTK dcmdump が警告/エラーを出した。"); }
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

// 案④ 期待④専用: 漢字が混入したときに「静かに通ってしまう」ことがないかを見る。
// 通常ケース(RunCase)と違い、不一致が“検出できること”自体が合格条件になる。
static (bool ok, string detail) RunKanjiMixedCase(TestCase tc, string outDir)
{
    var sb = new StringBuilder();
    var filePath = Path.Combine(outDir, tc.Name + ".dcm");

    try
    {
        var ds = new DicomDataset();
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
        sb.AppendLine($"- charset申告: {string.Join("\\", tc.CharsetValues)}");
        sb.AppendLine($"- 元のPatientName: {tc.PatientName}");
        sb.AppendLine($"- 書き込み時に例外: {ex.GetType().Name}: {ex.Message}");
        sb.AppendLine("  → PASS: 書き込みの時点で拒否された。不一致どころか未然に検出できている。");
        return (true, sb.ToString());
    }

    var raw = File.ReadAllBytes(filePath);
    var pnRaw = ExtractShortVrTagBytes(raw, 0x0010, 0x0010);
    sb.AppendLine($"- charset申告: {string.Join("\\", tc.CharsetValues)}");
    sb.AppendLine($"- 元のPatientName: {tc.PatientName}（ISO_IR 13 では原理的に表現できないはず）");
    sb.AppendLine($"- PatientName 生バイト(hex): {(pnRaw is null ? "(見つからない)" : ToHex(pnRaw))}");

    string? selfReadName = null;
    try
    {
        var reread = DicomFile.Open(filePath);
        selfReadName = reread.Dataset.GetString(DicomTag.PatientName);
    }
    catch (Exception ex)
    {
        sb.AppendLine($"- fo-dicom自己再読込 例外: {ex.GetType().Name}: {ex.Message}");
    }
    sb.AppendLine($"- fo-dicom自己再読込値: {selfReadName ?? "(例外/null)"}（hex: {ToCodepointHex(selfReadName)}）");
    sb.AppendLine($"- 元の文字列と自己再読込値が一致するか: {(selfReadName == tc.PatientName ? "一致（要注意）" : "不一致（検出できている）")}");

    var dcmdumpOut = "";
    var dcmdumpErr = "";
    var dcmdumpExit = -1;
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
        dcmdumpErr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(10_000);
        dcmdumpExit = proc.ExitCode;
    }
    catch (Exception ex)
    {
        sb.AppendLine($"- dcmdump 起動失敗: {ex.GetType().Name}: {ex.Message}");
    }

    sb.AppendLine($"- dcmdump exit={dcmdumpExit}");
    if (!string.IsNullOrWhiteSpace(dcmdumpErr)) sb.AppendLine($"- dcmdump stderr: {dcmdumpErr.Trim()}");

    var line = dcmdumpOut.Split('\n').FirstOrDefault(l => l.Contains("(0010,0010)"));
    sb.AppendLine($"- dcmdump出力行: {line?.Trim() ?? "(なし)"}");

    string? dumpedValue = null;
    if (line is not null)
    {
        var start = line.IndexOf('[');
        var end = line.LastIndexOf(']');
        dumpedValue = (start >= 0 && end > start) ? line[(start + 1)..end] : null;
    }
    sb.AppendLine($"- dcmdump値と元の文字列が一致するか: {(dumpedValue == tc.PatientName ? "一致（要注意）" : "不一致（検出できている）")}");

    // 合格条件：DCMTK(独立実装)の目から見て「静かに元の文字列と一致してしまう」ことがないこと。
    // ・dcmdump が警告/エラーを出す、または exit!=0 → 明確な検出（合格）
    // ・dcmdump は成功するが値が食い違う → 不一致として検出できている（合格）
    // ・dcmdump が警告なく成功し、かつ値が元とビット一致 → 危険な静かな通過（不合格）
    var dcmdumpSilentlyMatched = dcmdumpExit == 0 && string.IsNullOrWhiteSpace(dcmdumpErr) && dumpedValue == tc.PatientName;
    var detected = !dcmdumpSilentlyMatched;

    sb.AppendLine($"- 総合判定: {(detected ? "不一致/エラーとして検出できた（Suppressed化の材料になる）" : "検出できず、警告なしで元の文字列と一致してしまった")}");
    if (!detected)
    {
        sb.AppendLine("  → FAIL: ISO_IR 13 で漢字が静かに通ってしまった。値の捏造を検出できない危険な状態。");
    }

    return (detected, sb.ToString());
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

static string ToHex(byte[] bytes)
    => string.Join(' ', bytes.Select(b => b.ToString("X2")));

// Explicit VR Little Endian、短VR形式（tag 4byte + VR 2byte + length 2byte + value）のタグを
// 生バイト列から取り出す。PN・CS・LO・SH・UI 等はこの形式。
static byte[]? ExtractShortVrTagBytes(byte[] file, ushort group, ushort element)
{
    var tagBytes = new byte[]
    {
        (byte)(group & 0xFF), (byte)(group >> 8),
        (byte)(element & 0xFF), (byte)(element >> 8),
    };
    for (var i = 0; i <= file.Length - 8; i++)
    {
        var match = true;
        for (var j = 0; j < 4; j++)
        {
            if (file[i + j] != tagBytes[j]) { match = false; break; }
        }
        if (!match) continue;

        var len = BitConverter.ToUInt16(file, i + 6);
        var valueStart = i + 8;
        if (valueStart + len > file.Length) continue;
        return file[valueStart..(valueStart + len)];
    }
    return null;
}

internal sealed record TestCase(string Name, string[] CharsetValues, string PatientName, string Description);
