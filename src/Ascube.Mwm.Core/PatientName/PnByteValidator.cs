using System.Text;

namespace Ascube.Mwm.Core.PatientName;

/// <summary>
/// 生成した PN の生バイト列を直接検査する（規則19）。fo-dicom の自己再読込には頼らない：
/// fo-dicom は ISO_IR 13 指定で漢字を渡しても無警告で CP932 相当のバイト列を書き、
/// 自分ではそれを正しく読み戻してしまう（T0/alt4 で確認済みの盲点）。
/// </summary>
public static class PnByteValidator
{
    /// <summary>
    /// ISO_IR 13（JIS X 0201）として妥当か。全バイトが 0x20〜0x7E または 0xA1〜0xDF に収まること。
    /// 0x5C（DICOM の値区切り文字と衝突する）も不可とする。
    /// CP932 の漢字は先頭バイトが 0x81〜0x9F / 0xE0〜0xEF になるため、この検査で必ず捕まる。
    /// </summary>
    public static bool IsValidIsoIr13(byte[] bytes)
    {
        foreach (var b in bytes)
        {
            if (b == 0x5C)
            {
                return false;
            }

            var inRange = (b >= 0x20 && b <= 0x7E) || (b >= 0xA1 && b <= 0xDF);
            if (!inRange)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// ISO_IR 192（UTF-8）として妥当か。<see cref="UTF8Encoding"/>（throwOnInvalidBytes: true）で
    /// decode でき、かつ置換文字（U+FFFD）を含まないこと。
    /// </summary>
    public static bool IsValidUtf8(byte[] bytes)
    {
        // 0x5C（バックスラッシュ）は charset に関わらず DICOM の値区切り文字と衝突する。
        if (Array.IndexOf(bytes, (byte)0x5C) >= 0)
        {
            return false;
        }

        try
        {
            var decoded = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return !decoded.Contains('�');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// value を ISO_IR 13 相当のバイト列にしたときの生バイトを得る（CP932 でエンコードする。
    /// ASCII・半角カナ範囲は JIS X 0201 と同一バイトになるため、その範囲だけを使う分には等価。
    /// T0/alt4 で確認済みの手法）。
    /// </summary>
    public static byte[] EncodeAsIsoIr13Bytes(string value) => Encoding.GetEncoding(932).GetBytes(value);

    public static byte[] EncodeAsUtf8Bytes(string value) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(value);
}
