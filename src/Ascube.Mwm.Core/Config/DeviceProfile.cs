using System.Linq;
using System.Text.Json.Nodes;
using Ascube.Mwm.Core.PatientName;

namespace Ascube.Mwm.Core.Config;

/// <summary>
/// 検証済みプロファイル。現時点（T1）で確定している項目だけを型付けし、
/// それ以外（dataset.elements の詳細等）は <see cref="Raw"/> から T2 以降のタスクが読む。
/// </summary>
public sealed class DeviceProfile
{
    public required string Id { get; init; }

    public required int SchemaVersion { get; init; }

    public required string VisibilityMode { get; init; }

    public required int CurrentTtlMinutes { get; init; }

    public required string SpecificCharacterSet { get; init; }

    /// <summary>この端末を宛てる Called AE Title。ルーティング（T3）でこの値により受信アソシエーションの解決先プロファイルを決める。</summary>
    public required string AeTitle { get; init; }

    public required int Port { get; init; }

    /// <summary>accept する Transfer Syntax の UID 一覧（規則9：Implicit VR LE / Explicit VR LE / Explicit VR BE の3種のみ）。</summary>
    public required IReadOnlyList<string> AcceptedTransferSyntaxUids { get; init; }

    /// <summary>Calling AE（装置側の AE Title）の照合モード。strict/logOnly/ignore（既定 ignore）。</summary>
    public required string CallingAeMatching { get; init; }

    /// <summary>callingAeMatching が strict/logOnly のときに照合する許可リスト。ignore では未使用。</summary>
    public required IReadOnlyList<string> AllowedCallingAeTitles { get; init; }

    /// <summary>Modality の照合モード。strict/lenient/ignore（既定 ignore。APEX の Modality 既定は None）。</summary>
    public required string ModalityMatching { get; init; }

    /// <summary>
    /// この装置に固定で割り当てる Modality（(0040,0100)[0].(0008,0060) の const: 値）。
    /// dataset.elements にその要素が無い、または const: が空文字なら null（実装指示書どおり "APEX の Modality 既定は None"）。
    /// </summary>
    public string? ScheduledStationModality { get; init; }

    /// <summary>charset.patientName の群割当（T7 PnEncoder が使う）。</summary>
    public required PnGroupAssignment PatientNameGroups { get; init; }

    public required JsonObject Raw { get; init; }

    public static DeviceProfile FromValidated(string id, JsonObject profile)
    {
        var network = profile["network"] as JsonObject;

        return new DeviceProfile
        {
            Id = id,
            SchemaVersion = profile["schemaVersion"]!.GetValue<int>(),
            VisibilityMode = profile["visibility"]?["mode"]?.GetValue<string>() ?? "current-only",
            CurrentTtlMinutes = profile["visibility"]?["currentTtlMinutes"]?.GetValue<int>() ?? 15,
            SpecificCharacterSet = profile["charset"]?["specificCharacterSet"]?.GetValue<string>() ?? "ISO_IR 192",
            AeTitle = network?["aeTitle"]?.GetValue<string>() ?? "ASCUBE_MWM",
            Port = network?["port"]?.GetValue<int>() ?? 11112,
            AcceptedTransferSyntaxUids = network?["acceptedTransferSyntaxes"] is JsonArray tsArray
                ? tsArray.Select(v => v!.GetValue<string>()).ToArray()
                : Array.Empty<string>(),
            CallingAeMatching = network?["callingAeMatching"]?.GetValue<string>() ?? "ignore",
            AllowedCallingAeTitles = network?["allowedCallingAeTitles"] is JsonArray aeArray
                ? aeArray.Select(v => v!.GetValue<string>()).ToArray()
                : Array.Empty<string>(),
            ModalityMatching = profile["matching"]?["modalityMatching"]?.GetValue<string>() ?? "ignore",
            ScheduledStationModality = ExtractScheduledStationModality(profile),
            PatientNameGroups = new PnGroupAssignment(
                PnGroupAssignment.Parse(profile["charset"]?["patientName"]?["group1"]?.GetValue<string>()),
                PnGroupAssignment.Parse(profile["charset"]?["patientName"]?["group2"]?.GetValue<string>()),
                PnGroupAssignment.Parse(profile["charset"]?["patientName"]?["group3"]?.GetValue<string>())),
            Raw = profile,
        };
    }

    /// <summary>
    /// dataset.elements の (0040,0100) SQ の1件目にある (0008,0060) Modality 要素の const: 値を取り出す。
    /// DatasetBuilder（T6）が実装するまでの暫定処理（T5：MatchEngine が Modality 照合に使う）。
    /// </summary>
    private static string? ExtractScheduledStationModality(JsonObject profile)
    {
        var elements = profile["dataset"]?["elements"] as JsonArray;
        var spsElement = elements?
            .OfType<JsonObject>()
            .FirstOrDefault(e => e["tag"]?.GetValue<string>() == "(0040,0100)");

        var firstItem = (spsElement?["items"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        var innerElements = firstItem?["elements"] as JsonArray;
        var modalityElement = innerElements?
            .OfType<JsonObject>()
            .FirstOrDefault(e => e["tag"]?.GetValue<string>() == "(0008,0060)");

        var source = modalityElement?["source"]?.GetValue<string>();
        if (source is null || !source.StartsWith("const:", StringComparison.Ordinal))
        {
            return null;
        }

        var value = source["const:".Length..];
        return value.Length == 0 ? null : value;
    }
}
