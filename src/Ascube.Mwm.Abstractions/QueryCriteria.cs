namespace Ascube.Mwm.Abstractions;

/// <summary>
/// SCP からの C-FIND 要求の照合条件。
/// 1人モデルでは <see cref="IWorklistRepository"/> は TTL・存在確認のみを行い、
/// ここに載る条件（日付範囲・PatientID/PatientName の指名・Modality 等）の実際の評価は
/// MatchEngine（実装指示書 v2 §5-T5・Ascube.Mwm.Core）が行う。フィールドは T5 で追加する。
/// </summary>
public sealed record QueryCriteria;
