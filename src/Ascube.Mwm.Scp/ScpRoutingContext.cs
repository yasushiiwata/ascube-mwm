using System.Linq;
using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Scp;

/// <summary>
/// 1つの <c>DicomServer</c>（1ポート）に紐づく、解決候補のプロファイル一覧。
/// <c>IDicomServerFactory.Create</c> の <c>userState</c> として渡し、各アソシエーションから
/// <see cref="Ascube.Mwm.Scp.MwmDicomService.UserState"/> 経由で参照する。
/// </summary>
public sealed class ScpRoutingContext
{
    public required IReadOnlyList<DeviceProfile> Profiles { get; init; }

    /// <summary>
    /// Called AE Title でプロファイルを解決する（T3 のルーティング）。
    /// DICOM の AE Title は16バイト固定長で空白パディングされ得るため前後の空白は無視する。
    /// 一致するプロファイルが無ければ null（呼び出し側は A-ASSOCIATE-RJ を返す）。
    /// </summary>
    public DeviceProfile? ResolveByCalledAe(string calledAe)
    {
        var trimmed = calledAe.Trim();
        return Profiles.FirstOrDefault(p => string.Equals(p.AeTitle, trimmed, StringComparison.Ordinal));
    }
}
