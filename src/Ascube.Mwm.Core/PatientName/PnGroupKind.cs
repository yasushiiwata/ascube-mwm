namespace Ascube.Mwm.Core.PatientName;

/// <summary>charset.patientName.group1/2/3 に書ける値（実装指示書 v2 T7）。</summary>
public enum PnGroupKind
{
    /// <summary>この群を使わない。</summary>
    None,

    /// <summary>半角カナ（DB の *Kana 列をそのまま使う）。</summary>
    KanaHalf,

    /// <summary>全角カナ（DB の *Kana 列を <see cref="HalfToFullWidthKana"/> で変換する）。</summary>
    KanaFull,

    /// <summary>漢字（DB の *Kanji 列をそのまま使う）。</summary>
    Kanji,

    /// <summary>ローマ字。現状 WorkItemView にローマ字表記の列が無いため未実装（常に未解決扱い）。</summary>
    Romaji,
}
