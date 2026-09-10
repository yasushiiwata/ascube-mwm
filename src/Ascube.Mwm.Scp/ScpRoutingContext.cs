using System.Linq;
using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Store.Audit;

namespace Ascube.Mwm.Scp;

/// <summary>
/// 1つの <c>DicomServer</c>（1ポート）に紐づく、解決候補のプロファイル一覧とワークリストの読み取り口。
/// <c>IDicomServerFactory.Create</c> の <c>userState</c> として渡し、各アソシエーションから
/// <see cref="Ascube.Mwm.Scp.MwmDicomService.UserState"/> 経由で参照する。
/// T10：プロファイルは固定リストではなく <see cref="LiveProfileRegistry"/> 経由で毎回読む。
/// ホットリロードで差し替わっても、進行中のアソシエーションは
/// <see cref="MwmDicomService"/> がアソシエーション確立時に1回だけ解決した参照を使い続けるため影響を受けない。
/// </summary>
public sealed class ScpRoutingContext
{
    public required LiveProfileRegistry ProfileRegistry { get; init; }

    /// <summary>この DicomServer が待ち受けているポート。ProfileRegistry.Current からこのポート分だけを見る。</summary>
    public required int Port { get; init; }

    /// <summary>C-FIND（T4/T5）が読む、TTL・存在確認のみを行う読み取り専用リポジトリ（規則5）。</summary>
    public required IWorklistRepository Repository { get; init; }

    /// <summary>C-FIND ごとの監査ログ書き込み口（T8）。ワークリストDBとは別ファイル（規則5）。</summary>
    public required IAuditWriter AuditWriter { get; init; }

    /// <summary>
    /// Called AE Title でプロファイルを解決する（T3 のルーティング）。
    /// DICOM の AE Title は16バイト固定長で空白パディングされ得るため前後の空白は無視する。
    /// 一致するプロファイルが無ければ null（呼び出し側は A-ASSOCIATE-RJ を返す）。
    /// </summary>
    public DeviceProfile? ResolveByCalledAe(string calledAe)
    {
        var trimmed = calledAe.Trim();
        return ProfileRegistry.Current
            .Where(p => p.Port == Port)
            .FirstOrDefault(p => string.Equals(p.AeTitle, trimmed, StringComparison.Ordinal));
    }
}
