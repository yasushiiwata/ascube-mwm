namespace Ascube.Mwm.Core.Config;

/// <summary>
/// 稼働中に差し替え可能なプロファイル集合（実装指示書 v2 T10：ホットリロード＋自動ロールバック）。
/// <see cref="Current"/> は volatile 参照の入れ替えのみで更新するため、読む側（C-FIND 処理側）は
/// ロック無しで一貫したスナップショットを取得できる。**進行中のアソシエーションは
/// アソシエーション確立時に取得したプロファイル参照を使い続けるため、この入れ替えの影響を受けない**
/// （呼び出し側が <see cref="Current"/> を都度読み直さない限り）。
/// </summary>
public sealed class LiveProfileRegistry(IReadOnlyList<DeviceProfile> initial)
{
    private volatile ProfileSet _current = new(initial);

    public IReadOnlyList<DeviceProfile> Current => _current.Profiles;

    /// <summary>
    /// 指定した全プロファイルIDを再読込・再検証する。1つでも検証に失敗したら何も差し替えない
    /// （失敗時は稼働中の設定を維持＝自動ロールバック）。
    /// </summary>
    public ProfileReloadResult TryReload(IReadOnlyList<string> profileIds, string profilesDir)
    {
        var previous = Current;
        var loaded = new List<DeviceProfile>();
        var issues = new List<(string ProfileId, ValidationResult Validation)>();

        foreach (var id in profileIds)
        {
            var result = ProfileLoader.LoadAndValidate(id, profilesDir);
            if (!result.Validation.IsValid)
            {
                issues.Add((id, result.Validation));
            }
            else
            {
                loaded.Add(result.Profile!);
            }
        }

        if (issues.Count > 0)
        {
            return ProfileReloadResult.Failed(previous, issues);
        }

        _current = new ProfileSet(loaded);
        return ProfileReloadResult.Succeeded(previous, loaded);
    }

    private sealed record ProfileSet(IReadOnlyList<DeviceProfile> Profiles);
}
