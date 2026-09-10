namespace Ascube.Mwm.Abstractions;

/// <summary>
/// <see cref="MatchEngine"/> の判定結果。0件になった理由を名指しできるように区別する
/// （実装指示書 v2 T8 の explain-query がそのまま使えるように設計。項番3・4に対応）。
/// </summary>
public enum MatchOutcome
{
    Matched,
    ScheduledDateOutOfRange,
    PatientIdMismatch,
    PatientNameMismatch,
    ModalityMismatch,
}
