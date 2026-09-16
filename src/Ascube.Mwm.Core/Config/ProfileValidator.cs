using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using FellowOakDicom;

namespace Ascube.Mwm.Core.Config;

/// <summary>
/// マージ済みプロファイルの検証。実装指示書 v2 §5-T1 の検査順を守る：
/// schemaVersion → タグ形式 → DICOM辞書とのVR一致 → VM → SQ入れ子（2階層以内）
/// → db: の列が実在 → specificCharacterSet が既知の定義語 → 禁止式の検出。
/// JSON構文チェックは <see cref="JsoncLoader"/> が担当する。
/// </summary>
public static partial class ProfileValidator
{
    private const int MaxSqDepth = 2;

    // WorklistEntry（Ascube.Mwm.Abstractions）が公開する列。db: source が参照してよい列名はこれだけ。
    private static readonly HashSet<string> KnownDbColumns = new(StringComparer.Ordinal)
    {
        "StablePatientId",
        "FamilyNameKanji",
        "GivenNameKanji",
        "FamilyNameKana",
        "GivenNameKana",
        "BirthDate",
        "Sex",
        "ScheduledDate",
        "AccessionNumber",
        "RequestedProcedureId",
        "RequestedProcedureDesc",
        "PatientHeightCm",
        "PatientWeightKg",
        "SourceMessageId",
    };

    // DICOM PS3.3 C.12.1.1.2 の Defined Terms。ISO 2022 系も「規格上は知られた語」として含める。
    // 規則15（ISO 2022 を使わない）は既知語チェックとは別の専用チェックで弾く。
    private static readonly HashSet<string> KnownDefinedTerms = new(StringComparer.Ordinal)
    {
        "",
        "ISO_IR 6", "ISO_IR 100", "ISO_IR 101", "ISO_IR 109", "ISO_IR 110",
        "ISO_IR 144", "ISO_IR 127", "ISO_IR 126", "ISO_IR 138", "ISO_IR 148",
        "ISO_IR 13", "ISO_IR 166", "ISO_IR 192", "GB18030", "GBK",
        "ISO 2022 IR 6", "ISO 2022 IR 100", "ISO 2022 IR 101", "ISO 2022 IR 109",
        "ISO 2022 IR 110", "ISO 2022 IR 144", "ISO 2022 IR 127", "ISO 2022 IR 126",
        "ISO 2022 IR 138", "ISO 2022 IR 148", "ISO 2022 IR 13", "ISO 2022 IR 166",
        "ISO 2022 IR 87", "ISO 2022 IR 159",
    };

    private static readonly HashSet<string> KnownAeMatchingModes = new(StringComparer.Ordinal) { "strict", "logOnly", "ignore" };
    private static readonly HashSet<string> KnownModalityMatchingModes = new(StringComparer.Ordinal) { "strict", "lenient", "ignore" };

    // 規則9：この3種以外は accept してはならない（Implicit VR LE / Explicit VR LE / Explicit VR BE）。
    private static readonly HashSet<string> KnownTransferSyntaxUids = new(StringComparer.Ordinal)
    {
        "1.2.840.10008.1.2",
        "1.2.840.10008.1.2.1",
        "1.2.840.10008.1.2.2",
    };

    [GeneratedRegex(@"^\(([0-9A-Fa-f]{4}),([0-9A-Fa-f]{4})\)$")]
    private static partial Regex TagPattern();

    public static ValidationResult Validate(JsonObject profile)
    {
        var issues = new List<ValidationIssue>();

        ValidateSchemaVersion(profile, issues);
        ValidateDataset(profile, issues);
        ValidateCharset(profile, issues);
        ValidateForbiddenPatterns(profile, issues);

        return issues.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail(issues);
    }

    private static void ValidateSchemaVersion(JsonObject profile, List<ValidationIssue> issues)
    {
        if (profile["schemaVersion"] is not JsonValue value || !value.TryGetValue<int>(out var version))
        {
            issues.Add(new ValidationIssue("$.schemaVersion", "schemaVersion が整数として存在しません"));
            return;
        }

        if (version != 1)
        {
            issues.Add(new ValidationIssue("$.schemaVersion", $"未対応の schemaVersion です（対応: 1）: {version}"));
        }
    }

    private static void ValidateDataset(JsonObject profile, List<ValidationIssue> issues)
    {
        if (profile["dataset"] is not JsonObject dataset)
        {
            return;
        }

        if (dataset["elements"] is not JsonArray elements)
        {
            return;
        }

        ValidateElements(elements, "$.dataset.elements", sqDepth: 0, issues);
    }

    private static void ValidateElements(JsonArray elements, string basePath, int sqDepth, List<ValidationIssue> issues)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            var path = $"{basePath}[{i}]";
            if (elements[i] is not JsonObject element)
            {
                issues.Add(new ValidationIssue(path, "要素は JSON オブジェクトである必要があります"));
                continue;
            }

            ValidateElement(element, path, sqDepth, issues);
        }
    }

    private static void ValidateElement(JsonObject element, string path, int sqDepth, List<ValidationIssue> issues)
    {
        var tagString = element["tag"]?.GetValue<string>();
        DicomTag? tag = ValidateTagFormat(tagString, path, issues);

        var vrString = element["vr"]?.GetValue<string>();
        var declaredVr = ValidateVrCode(vrString, path, issues);

        if (tag is { } resolvedTag)
        {
            ValidateAgainstDictionary(resolvedTag, tagString!, declaredVr, vrString, element, path, issues);
        }

        if (element["source"]?.GetValue<string>() is { } sourceString)
        {
            ValidateSource(sourceString, $"{path}.source", issues);
        }

        ValidateSqNesting(element, vrString, path, sqDepth, issues);
    }

    private static DicomTag? ValidateTagFormat(string? tagString, string path, List<ValidationIssue> issues)
    {
        if (tagString is null)
        {
            issues.Add(new ValidationIssue($"{path}.tag", "tag が指定されていません"));
            return null;
        }

        var match = TagPattern().Match(tagString);
        if (!match.Success)
        {
            issues.Add(new ValidationIssue($"{path}.tag", $"タグの形式が不正です。(gggg,eeee) の形式で書いてください: {tagString}"));
            return null;
        }

        var group = Convert.ToUInt16(match.Groups[1].Value, 16);
        var elementNo = Convert.ToUInt16(match.Groups[2].Value, 16);
        return new DicomTag(group, elementNo);
    }

    private static DicomVR? ValidateVrCode(string? vrString, string path, List<ValidationIssue> issues)
    {
        if (vrString is null)
        {
            return null;
        }

        try
        {
            return DicomVR.Parse(vrString);
        }
        catch (Exception)
        {
            issues.Add(new ValidationIssue($"{path}.vr", $"不正な VR コードです: {vrString}"));
            return null;
        }
    }

    private static void ValidateAgainstDictionary(
        DicomTag tag,
        string tagString,
        DicomVR? declaredVr,
        string? vrString,
        JsonObject element,
        string path,
        List<ValidationIssue> issues)
    {
        var entry = DicomDictionary.Default[tag];
        if (ReferenceEquals(entry, DicomDictionary.UnknownTag))
        {
            issues.Add(new ValidationIssue($"{path}.tag", $"DICOM 辞書に存在しないタグです: {tagString}"));
            return;
        }

        if (declaredVr is { } vr && !entry.ValueRepresentations.Contains(vr))
        {
            var expected = string.Join("/", entry.ValueRepresentations.Select(v => v.Code));
            issues.Add(new ValidationIssue($"{path}.vr", $"VR が DICOM 辞書と一致しません（{tagString} {entry.Name} の期待値: {expected}）: {vrString}"));
        }

        if (element["vm"]?.GetValue<string>() is { } vmString)
        {
            if (!TryParseVm(vmString, out var min, out var max))
            {
                issues.Add(new ValidationIssue($"{path}.vm", $"VM の形式が不正です: {vmString}"));
            }
            else
            {
                var dictVm = entry.ValueMultiplicity;
                if (min < dictVm.Minimum || max > dictVm.Maximum)
                {
                    var dictRange = dictVm.Maximum == int.MaxValue ? $"{dictVm.Minimum}-n" : $"{dictVm.Minimum}-{dictVm.Maximum}";
                    issues.Add(new ValidationIssue($"{path}.vm", $"VM が DICOM 辞書の許容範囲外です（{tagString} の許容: {dictRange}）: {vmString}"));
                }
            }
        }
    }

    private static bool TryParseVm(string vm, out int min, out int max)
    {
        min = 0;
        max = 0;

        var parts = vm.Split('-', 2);
        if (parts.Length == 1)
        {
            if (!int.TryParse(parts[0], out var exact))
            {
                return false;
            }

            min = max = exact;
            return true;
        }

        if (!int.TryParse(parts[0], out min))
        {
            return false;
        }

        if (parts[1].EndsWith("n", StringComparison.OrdinalIgnoreCase))
        {
            max = int.MaxValue;
            return true;
        }

        return int.TryParse(parts[1], out max);
    }

    private static void ValidateSource(string source, string path, List<ValidationIssue> issues)
    {
        if (source.StartsWith("db:", StringComparison.Ordinal))
        {
            var column = source["db:".Length..];
            if (!KnownDbColumns.Contains(column))
            {
                issues.Add(new ValidationIssue(path, $"db: が参照する列が存在しません: {column}"));
            }

            return;
        }

        if (source.StartsWith("const:", StringComparison.Ordinal)
            || source.StartsWith("echo:", StringComparison.Ordinal)
            || source.StartsWith("coalesce:", StringComparison.Ordinal)
            || source.StartsWith("auto:", StringComparison.Ordinal)
            || source == "uid:study")
        {
            // db: 以外の DSL 形式の詳細検証は T6（DatasetBuilder）で行う。
            return;
        }

        issues.Add(new ValidationIssue(path, $"未知の source 式です: {source}"));
    }

    private static void ValidateSqNesting(JsonObject element, string? vrString, string path, int sqDepth, List<ValidationIssue> issues)
    {
        var isSq = string.Equals(vrString, "SQ", StringComparison.Ordinal);

        if (element["items"] is not JsonArray itemsArray)
        {
            if (isSq)
            {
                issues.Add(new ValidationIssue($"{path}.items", "VR=SQ の要素には items が必要です"));
            }

            return;
        }

        if (!isSq)
        {
            issues.Add(new ValidationIssue($"{path}.items", "items を持てるのは VR=SQ の要素だけです"));
        }

        var nextDepth = sqDepth + 1;
        if (nextDepth > MaxSqDepth)
        {
            issues.Add(new ValidationIssue($"{path}.items", $"SQ の入れ子が{MaxSqDepth}階層を超えています（{nextDepth}階層目）"));
            return;
        }

        for (var i = 0; i < itemsArray.Count; i++)
        {
            if (itemsArray[i] is JsonObject item && item["elements"] is JsonArray innerElements)
            {
                ValidateElements(innerElements, $"{path}.items[{i}].elements", nextDepth, issues);
            }
        }
    }

    private static void ValidateCharset(JsonObject profile, List<ValidationIssue> issues)
    {
        if (profile["charset"] is not JsonObject charset)
        {
            return;
        }

        if (charset["specificCharacterSet"]?.GetValue<string>() is not { } value)
        {
            return;
        }

        const string path = "$.charset.specificCharacterSet";

        // 規則15：ISO 2022 系は fo-dicom で正しいバイト列が出せないため、単独値でも符号拡張でも禁止。
        if (value.Contains("ISO 2022", StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue(path, $"ISO 2022 を含む specificCharacterSet は禁止です（規則15。使えるのは ISO_IR 192 / ISO_IR 13 のみ）: {value}"));
            return;
        }

        foreach (var v in value.Split('\\'))
        {
            if (!KnownDefinedTerms.Contains(v))
            {
                issues.Add(new ValidationIssue(path, $"specificCharacterSet に未知の定義語が含まれています: \"{v}\""));
            }
        }
    }

    private static void ValidateForbiddenPatterns(JsonObject profile, List<ValidationIssue> issues)
    {
        if (profile["visibility"] is JsonObject visibility)
        {
            var mode = visibility["mode"]?.GetValue<string>();
            if (mode is not null && mode != "current-only")
            {
                issues.Add(new ValidationIssue("$.visibility.mode", $"visibility.mode は \"current-only\" 固定です（1人モデル・禁止事項11）: {mode}"));
            }

            if (visibility.ContainsKey("fallbackToAllTodayWhenNoActive"))
            {
                issues.Add(new ValidationIssue("$.visibility.fallbackToAllTodayWhenNoActive", "fallbackToAllTodayWhenNoActive は撤回済みの設定項目です（規則16・禁止事項11。設定項目ごと削除すること）"));
            }
        }

        if (profile["matching"] is JsonObject matching)
        {
            if (matching["maxResults"] is JsonValue maxResultsValue && maxResultsValue.TryGetValue<int>(out var maxResults) && maxResults != 1)
            {
                issues.Add(new ValidationIssue("$.matching.maxResults", $"matching.maxResults は1人モデルでは1固定です: {maxResults}"));
            }

            if (matching.ContainsKey("responseOrder"))
            {
                issues.Add(new ValidationIssue("$.matching.responseOrder", "responseOrder は1人モデルでは不要な設定項目です"));
            }

            if (matching["modalityMatching"]?.GetValue<string>() is { } modalityMatching && !KnownModalityMatchingModes.Contains(modalityMatching))
            {
                issues.Add(new ValidationIssue("$.matching.modalityMatching", $"未知の modalityMatching です（strict/lenient/ignore のいずれか）: {modalityMatching}"));
            }
        }

        if (profile["network"] is JsonObject network)
        {
            ValidateNetwork(network, issues);
        }
    }

    private static void ValidateNetwork(JsonObject network, List<ValidationIssue> issues)
    {
        var callingAeMatching = network["callingAeMatching"]?.GetValue<string>();
        if (callingAeMatching is not null && !KnownAeMatchingModes.Contains(callingAeMatching))
        {
            issues.Add(new ValidationIssue("$.network.callingAeMatching", $"未知の callingAeMatching です（strict/logOnly/ignore のいずれか）: {callingAeMatching}"));
        }

        if (network["aeTitle"]?.GetValue<string>() is { } aeTitle)
        {
            if (aeTitle.Length is 0 or > 16)
            {
                issues.Add(new ValidationIssue("$.network.aeTitle", $"aeTitle は1〜16文字である必要があります（DICOM AE Title）: \"{aeTitle}\"（{aeTitle.Length}文字）"));
            }
        }

        if (network["acceptedTransferSyntaxes"] is JsonArray tsArray)
        {
            for (var i = 0; i < tsArray.Count; i++)
            {
                var uid = tsArray[i]?.GetValue<string>();
                if (uid is null || !KnownTransferSyntaxUids.Contains(uid))
                {
                    issues.Add(new ValidationIssue($"$.network.acceptedTransferSyntaxes[{i}]", $"規則9で accept してよいのは Implicit VR LE(1.2.840.10008.1.2) / Explicit VR LE(1.2.840.10008.1.2.1) / Explicit VR BE(1.2.840.10008.1.2.2) のみです: {uid}"));
                }
            }
        }

        if (network["allowedCallingAeTitles"] is JsonArray aeArray)
        {
            for (var i = 0; i < aeArray.Count; i++)
            {
                var v = aeArray[i]?.GetValue<string>();
                if (string.IsNullOrEmpty(v) || v.Length > 16)
                {
                    issues.Add(new ValidationIssue($"$.network.allowedCallingAeTitles[{i}]", $"AE Title は1〜16文字である必要があります: \"{v}\""));
                }
            }
        }

        if (callingAeMatching is "strict" or "logOnly")
        {
            var allowedCount = (network["allowedCallingAeTitles"] as JsonArray)?.Count ?? 0;
            if (allowedCount == 0)
            {
                issues.Add(new ValidationIssue("$.network.allowedCallingAeTitles", $"callingAeMatching=\"{callingAeMatching}\" では allowedCallingAeTitles に1件以上指定する必要があります（規則8・規則10：現場の1文字ミスで検査停止させないため、空リストのまま strict/logOnly を有効化させない）"));
            }
        }
    }
}
