namespace Ascube.Mwm.Core.PatientName;

/// <summary>プロファイルの charset.patientName（group1/2/3）。</summary>
public sealed record PnGroupAssignment(PnGroupKind Group1, PnGroupKind Group2, PnGroupKind Group3)
{
    public static PnGroupKind Parse(string? value) => value switch
    {
        null or "none" => PnGroupKind.None,
        "kanaHalf" => PnGroupKind.KanaHalf,
        "kanaFull" => PnGroupKind.KanaFull,
        "kanji" => PnGroupKind.Kanji,
        "romaji" => PnGroupKind.Romaji,
        _ => throw new NotSupportedException($"未知の patientName group 値です: {value}"),
    };
}
