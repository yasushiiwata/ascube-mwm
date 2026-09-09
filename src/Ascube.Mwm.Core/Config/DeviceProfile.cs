using System.Text.Json.Nodes;

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

    public required JsonObject Raw { get; init; }

    public static DeviceProfile FromValidated(string id, JsonObject profile)
    {
        return new DeviceProfile
        {
            Id = id,
            SchemaVersion = profile["schemaVersion"]!.GetValue<int>(),
            VisibilityMode = profile["visibility"]?["mode"]?.GetValue<string>() ?? "current-only",
            CurrentTtlMinutes = profile["visibility"]?["currentTtlMinutes"]?.GetValue<int>() ?? 15,
            SpecificCharacterSet = profile["charset"]?["specificCharacterSet"]?.GetValue<string>() ?? "ISO_IR 192",
            Raw = profile,
        };
    }
}
