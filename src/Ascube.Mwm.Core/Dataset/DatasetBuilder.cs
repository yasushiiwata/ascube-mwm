using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Core.PatientName;
using FellowOakDicom;

namespace Ascube.Mwm.Core.Dataset;

/// <summary>
/// プロファイルの dataset.elements DSL から実際の C-FIND 応答 <see cref="DicomDataset"/> を組み立てる
/// （実装指示書 v2 T6）。source は db: / const: / echo: / uid:study / coalesce: / auto:* の6種。
/// 1件の生成失敗（required な要素が解決できない）は全体を落とさず <see cref="DatasetBuildResult.Suppressed"/> で報告する。
/// </summary>
public static partial class DatasetBuilder
{
    public static DatasetBuildResult Build(DeviceProfile profile, WorkItemView item, DicomDataset requestDataset)
    {
        var dataset = new DicomDataset();
        var provenance = new Dictionary<string, string>();

        // 規則6：SpecificCharacterSet は DicomDataset に最初に設定する。
        dataset.Add(DicomTag.SpecificCharacterSet, profile.SpecificCharacterSet);

        var elements = profile.Raw["dataset"]?["elements"] as JsonArray ?? [];

        foreach (var node in elements)
        {
            if (node is not JsonObject element)
            {
                continue;
            }

            var outcome = BuildElement(element, profile, item, requestDataset, dataset, provenance, out var rejectReason);
            if (outcome == ElementOutcome.RejectItem)
            {
                return new DatasetBuildResult { Suppressed = true, SuppressedReason = rejectReason };
            }
        }

        return new DatasetBuildResult { Suppressed = false, Dataset = dataset, Provenance = provenance };
    }

    private enum ElementOutcome
    {
        Included,
        Omitted,
        RejectItem,
    }

    private static ElementOutcome BuildElement(
        JsonObject element,
        DeviceProfile profile,
        WorkItemView item,
        DicomDataset requestDataset,
        DicomDataset outputDataset,
        Dictionary<string, string> provenance,
        out string? rejectReason,
        string tagPathPrefix = "")
    {
        rejectReason = null;
        var tagString = element["tag"]!.GetValue<string>();
        var tag = ParseTag(tagString)!;
        var vrString = element["vr"]?.GetValue<string>();
        var provenanceKey = tagPathPrefix.Length == 0 ? tagString : $"{tagPathPrefix}.{tagString}";

        if (!EvaluateWhen(element, profile, item, requestDataset))
        {
            return ElementOutcome.Omitted;
        }

        if (string.Equals(vrString, "SQ", StringComparison.Ordinal))
        {
            return BuildSequenceElement(element, tag, provenanceKey, profile, item, requestDataset, outputDataset, provenance, out rejectReason);
        }

        var sourceExpr = element["source"]?.GetValue<string>();
        var (value, winningSource) = ResolveWithFallback(element, sourceExpr, profile, item, requestDataset);

        if (value is not null && element["maxLength"]?.GetValue<int>() is { } maxLength && value.Length > maxLength)
        {
            // 超過分を切り詰めない（データを損なわない。規則4の精神）。解決できなかったものとして扱う。
            value = null;
            winningSource = null;
        }

        var onMissing = element["onMissing"]?.GetValue<string>() ?? "empty";

        if (value is null)
        {
            switch (onMissing)
            {
                case "omit":
                    return ElementOutcome.Omitted;
                case "reject-item":
                    rejectReason = $"{provenanceKey} が必須ですが値を解決できませんでした（source: {sourceExpr}）";
                    return ElementOutcome.RejectItem;
                default:
                    outputDataset.Add(tag, string.Empty);
                    return ElementOutcome.Included;
            }
        }

        outputDataset.Add(tag, value);
        provenance[provenanceKey] = winningSource ?? sourceExpr ?? string.Empty;
        return ElementOutcome.Included;
    }

    private static ElementOutcome BuildSequenceElement(
        JsonObject element,
        DicomTag tag,
        string provenanceKey,
        DeviceProfile profile,
        WorkItemView item,
        DicomDataset requestDataset,
        DicomDataset outputDataset,
        Dictionary<string, string> provenance,
        out string? rejectReason)
    {
        rejectReason = null;
        var itemsArray = element["items"] as JsonArray;
        var firstItem = itemsArray?.Count > 0 ? itemsArray[0] as JsonObject : null;
        var innerElements = firstItem?["elements"] as JsonArray ?? [];

        var sqItemDataset = new DicomDataset();

        foreach (var node in innerElements)
        {
            if (node is not JsonObject innerElement)
            {
                continue;
            }

            var outcome = BuildElement(innerElement, profile, item, requestDataset, sqItemDataset, provenance, out rejectReason, $"{provenanceKey}[0]");
            if (outcome == ElementOutcome.RejectItem)
            {
                return ElementOutcome.RejectItem;
            }
        }

        outputDataset.Add(new DicomSequence(tag, sqItemDataset));
        return ElementOutcome.Included;
    }

    private static bool EvaluateWhen(JsonObject element, DeviceProfile profile, WorkItemView item, DicomDataset requestDataset)
    {
        var whenExpr = element["when"]?.GetValue<string>();
        return whenExpr is null || ResolveSource(whenExpr, profile, item, requestDataset) is not null;
    }

    private static (string? Value, string? WinningSource) ResolveWithFallback(
        JsonObject element, string? sourceExpr, DeviceProfile profile, WorkItemView item, DicomDataset requestDataset)
    {
        var primary = ResolveSource(sourceExpr, profile, item, requestDataset);
        if (primary is not null)
        {
            return (primary, sourceExpr);
        }

        var fallbackExpr = element["fallback"]?.GetValue<string>();
        if (fallbackExpr is not null)
        {
            var fallbackValue = ResolveSource(fallbackExpr, profile, item, requestDataset);
            if (fallbackValue is not null)
            {
                return (fallbackValue, fallbackExpr);
            }
        }

        return (null, null);
    }

    private static string? ResolveSource(string? source, DeviceProfile profile, WorkItemView item, DicomDataset requestDataset)
    {
        if (source is null)
        {
            return null;
        }

        if (source.StartsWith("db:", StringComparison.Ordinal))
        {
            return ResolveDbColumn(source["db:".Length..], item);
        }

        if (source.StartsWith("const:", StringComparison.Ordinal))
        {
            // const: は空文字列も有効な値（未解決とは区別する）。
            return source["const:".Length..];
        }

        if (source == "uid:study")
        {
            return NullIfEmpty(item.StudyInstanceUid);
        }

        if (source == "auto:patientName")
        {
            // T7：群割当（charset.patientName）＋生バイト検証（規則19）。検証に落ちたら Suppressed（=null）。
            var result = PnEncoder.Encode(profile.PatientNameGroups, item, profile.SpecificCharacterSet);
            return result.Suppressed ? null : result.Value;
        }

        if (source == "auto:patientSex")
        {
            return ToDicomSex(item.Sex);
        }

        if (source.StartsWith("echo:", StringComparison.Ordinal))
        {
            return ResolveEcho(source["echo:".Length..], requestDataset);
        }

        if (source.StartsWith("coalesce:", StringComparison.Ordinal))
        {
            return ResolveCoalesce(source["coalesce:".Length..], profile, item, requestDataset);
        }

        // T1 の ProfileValidator が起動時にこれらの形式を既に検証済みのため、ここに来るのは
        // プロファイルとバリデータの不整合というバグの場合のみ。
        throw new NotSupportedException($"未知の source 式です（ProfileValidator を通過しているはずなのに）: {source}");
    }

    private static string? ResolveDbColumn(string column, WorkItemView item) => column switch
    {
        "StablePatientId" => NullIfEmpty(item.StablePatientId),
        "FamilyNameKanji" => NullIfEmpty(item.FamilyNameKanji),
        "GivenNameKanji" => NullIfEmpty(item.GivenNameKanji),
        "FamilyNameKana" => NullIfEmpty(item.FamilyNameKana),
        "GivenNameKana" => NullIfEmpty(item.GivenNameKana),
        "BirthDate" => NullIfEmpty(item.BirthDate),
        "Sex" => ToDicomSex(item.Sex),
        "ScheduledDate" => NullIfEmpty(item.ScheduledDate),
        "AccessionNumber" => NullIfEmpty(item.AccessionNumber),
        "RequestedProcedureId" => NullIfEmpty(item.RequestedProcedureId),
        "RequestedProcedureDesc" => NullIfEmpty(item.RequestedProcedureDesc),
        // (0010,1020)はDICOM規格上メートル単位。WorkItemView.PatientHeightCmはBRIDGE-Navi由来のセンチメートル値のため、
        // ここで変換する（設計変更メモ_v2.1.md §I：変換責務をascube-mwm側に一元化）。
        "PatientHeightCm" => FormatDecimal(item.PatientHeightCm / 100.0),
        "PatientWeightKg" => FormatDecimal(item.PatientWeightKg),
        "SourceMessageId" => NullIfEmpty(item.SourceMessageId),
        _ => throw new NotSupportedException($"未知の db: 列です（ProfileValidator を通過しているはずなのに）: {column}"),
    };

    private static string? ResolveEcho(string tagSpec, DicomDataset dataset)
    {
        var tag = ParseTag(tagSpec);
        if (tag is null)
        {
            return null;
        }

        if (dataset.Contains(tag))
        {
            var value = NullIfEmpty(dataset.GetSingleValueOrDefault(tag, string.Empty));
            if (value is not null)
            {
                return value;
            }
        }

        // echo: は SQ 内も探索する。
        foreach (var dicomItem in dataset)
        {
            if (dicomItem is DicomSequence sq)
            {
                foreach (var sqItem in sq.Items)
                {
                    var found = ResolveEcho(tagSpec, sqItem);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    private static string? ResolveCoalesce(string spec, DeviceProfile profile, WorkItemView item, DicomDataset requestDataset)
    {
        // 実装指示書には coalesce: の区切り文字の指定が無いため、"[src1|src2|...]" 形式を
        // 独自に採用した（実際の利用例が出たら見直すこと。現行プロファイルでは未使用）。
        var trimmed = spec.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        foreach (var candidate in trimmed.Split('|', StringSplitOptions.TrimEntries))
        {
            var value = ResolveSource(candidate, profile, item, requestDataset);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? ToDicomSex(Sex sex) => sex switch
    {
        Sex.Male => "M",
        Sex.Female => "F",
        Sex.Other => "O",
        _ => null,
    };

    private static string? FormatDecimal(double? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static DicomTag? ParseTag(string? tagString)
    {
        if (tagString is null)
        {
            return null;
        }

        var match = TagPattern().Match(tagString);
        if (!match.Success)
        {
            return null;
        }

        var group = Convert.ToUInt16(match.Groups[1].Value, 16);
        var elementNo = Convert.ToUInt16(match.Groups[2].Value, 16);
        return new DicomTag(group, elementNo);
    }

    [GeneratedRegex(@"^\(([0-9A-Fa-f]{4}),([0-9A-Fa-f]{4})\)$")]
    private static partial Regex TagPattern();
}
