namespace Ascube.Mwm.Abstractions;

/// <summary>
/// SCP からの C-FIND 要求の照合条件（実装指示書 v2 §5-T5）。
/// 各フィールドは装置から送られてきた生の値（ワイルドカード・範囲表現を含む）をそのまま保持する。
/// null または空文字は「指定なし（Universal Matching）」を意味する。
/// </summary>
public sealed record QueryCriteria
{
    /// <summary>(0040,0100)[0].(0040,0002) の生値。例: "20260904-20260918" / "-20260918" / "20260904-" / "20260904"。</summary>
    public string? ScheduledDateRange { get; init; }

    /// <summary>(0010,0020) PatientID の生値（ワイルドカード可）。</summary>
    public string? PatientId { get; init; }

    /// <summary>(0010,0010) PatientName の生値（ワイルドカード可）。</summary>
    public string? PatientName { get; init; }

    /// <summary>(0040,0100)[0].(0008,0060) Modality の生値（ワイルドカード可）。</summary>
    public string? Modality { get; init; }
}
