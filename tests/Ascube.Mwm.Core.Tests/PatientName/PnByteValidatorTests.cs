using Ascube.Mwm.Core.PatientName;

namespace Ascube.Mwm.Core.Tests.PatientName;

/// <summary>
/// 規則19：生成したバイト列を直接検査する。fo-dicom の自己再読込には頼らない。
/// </summary>
public class PnByteValidatorTests
{
    [Fact]
    public void IsValidIsoIr13_AsciiAndHalfWidthKana_IsValid()
    {
        var bytes = PnByteValidator.EncodeAsIsoIr13Bytes("ﾀｹﾀﾞ^ﾀﾛｳ");
        Assert.True(PnByteValidator.IsValidIsoIr13(bytes));
    }

    [Fact]
    public void IsValidIsoIr13_Kanji_IsInvalid()
    {
        // CP932 の漢字は先頭バイトが 0x81〜0x9F / 0xE0〜0xEF になるため、この検査で必ず捕まる（規則19）。
        var bytes = PnByteValidator.EncodeAsIsoIr13Bytes("武田^太郎");
        Assert.False(PnByteValidator.IsValidIsoIr13(bytes));
    }

    [Fact]
    public void IsValidIsoIr13_GaijiKanji_IsInvalid()
    {
        // 外字（髙・﨑・德）も漢字なので同様に不正。
        var bytes = PnByteValidator.EncodeAsIsoIr13Bytes("髙﨑^德");
        Assert.False(PnByteValidator.IsValidIsoIr13(bytes));
    }

    [Fact]
    public void IsValidIsoIr13_DoesNotContainCp932LeadByteRange()
    {
        var bytes = PnByteValidator.EncodeAsIsoIr13Bytes("武田^太郎");
        Assert.Contains(bytes, b => (b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xEF));
    }

    [Fact]
    public void IsValidIsoIr13_Backslash_IsInvalid()
    {
        var bytes = PnByteValidator.EncodeAsIsoIr13Bytes("A\\B");
        Assert.False(PnByteValidator.IsValidIsoIr13(bytes));
    }

    [Fact]
    public void IsValidUtf8_Kanji_IsValid()
    {
        var bytes = PnByteValidator.EncodeAsUtf8Bytes("武田^太郎");
        Assert.True(PnByteValidator.IsValidUtf8(bytes));
    }

    [Fact]
    public void IsValidUtf8_Gaiji_IsValid()
    {
        var bytes = PnByteValidator.EncodeAsUtf8Bytes("髙﨑^德");
        Assert.True(PnByteValidator.IsValidUtf8(bytes));
    }

    [Fact]
    public void IsValidUtf8_Backslash_IsInvalid()
    {
        var bytes = PnByteValidator.EncodeAsUtf8Bytes("A\\B");
        Assert.False(PnByteValidator.IsValidUtf8(bytes));
    }

    [Fact]
    public void IsValidUtf8_InvalidByteSequence_IsInvalid()
    {
        byte[] invalid = [0xFF, 0xFE, 0x00];
        Assert.False(PnByteValidator.IsValidUtf8(invalid));
    }
}
