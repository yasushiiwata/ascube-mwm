namespace Ascube.Mwm.Store.Audit;

/// <summary>
/// 1回の C-FIND の監査記録（実装指示書 v2 T8）。<see cref="RequestJson"/>（DICOM JSON Model）が
/// 後日の再生に使う最重要データ。書き込み時は <see cref="RunId"/> は無視され、DBが採番した値が返る。
/// </summary>
public sealed record AuditCFindRecord
{
    public long RunId { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required string ProfileId { get; init; }

    public required string CalledAe { get; init; }

    public required string CallingAe { get; init; }

    /// <summary>要求データセットの DICOM JSON Model 表現。</summary>
    public required string RequestJson { get; init; }

    /// <summary>QueryCriteria の JSON 表現（デバッグ用）。</summary>
    public required string CriteriaJson { get; init; }

    /// <summary>日付クランプの有無。1人モデルでは現状クランプしないため常に false（将来の拡張用）。</summary>
    public bool Clamped { get; init; }

    public required int ResultCount { get; init; }

    public required long DurationMs { get; init; }

    /// <summary>DICOM Status（"Success" 等）。</summary>
    public required string Status { get; init; }

    /// <summary>0件だった理由。explain-query がそのまま表示する（実装指示書 T8 の受入条件1〜5）。</summary>
    public string? Explain { get; init; }

    /// <summary>規則3：応答送信中の相手の一方的切断を検知できた場合 true。</summary>
    public bool PeerAborted { get; init; }

    public IReadOnlyList<AuditCFindItemRecord> Items { get; init; } = [];
}
