namespace Ascube.Mwm.Core.PatientName;

/// <summary>
/// <see cref="PnEncoder.Encode"/> の結果。<see cref="Suppressed"/> が true のとき
/// <see cref="Value"/> は null（代替文字で無言に化けさせない。規則4・実装指示書 T7）。
/// </summary>
public sealed record PnEncodeResult(bool Suppressed, string? Value, string? Reason);
