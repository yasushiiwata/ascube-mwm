namespace Ascube.Mwm.Core.Config;

/// <summary>
/// <see cref="LiveProfileRegistry.TryReload"/> の結果（実装指示書 v2 T10）。
/// <see cref="Success"/> が false のとき、稼働中の設定（<see cref="Previous"/>）は変更されない。
/// </summary>
public sealed class ProfileReloadResult
{
    public required bool Success { get; init; }

    public IReadOnlyList<(string ProfileId, ValidationResult Validation)> Issues { get; init; } = [];

    public required IReadOnlyList<DeviceProfile> Previous { get; init; }

    public IReadOnlyList<DeviceProfile>? Applied { get; init; }

    public static ProfileReloadResult Failed(
        IReadOnlyList<DeviceProfile> previous,
        IReadOnlyList<(string ProfileId, ValidationResult Validation)> issues) =>
        new() { Success = false, Previous = previous, Issues = issues };

    public static ProfileReloadResult Succeeded(
        IReadOnlyList<DeviceProfile> previous,
        IReadOnlyList<DeviceProfile> applied) =>
        new() { Success = true, Previous = previous, Applied = applied };
}
