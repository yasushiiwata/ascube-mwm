using Ascube.Mwm.Core.PatientName;

namespace Ascube.Mwm.Core.Tests.PatientName;

public class HalfToFullWidthKanaTests
{
    [Theory]
    [InlineData("ｱｽｷｭｰﾌﾞ", "アスキューブ")]
    [InlineData("ﾀｹﾀﾞ", "タケダ")] // ﾀﾞ は「ﾀ」+「ﾞ」の2文字 → 「ダ」の1文字に結合
    [InlineData("ﾊﾟﾋﾟﾌﾟﾍﾟﾎﾟ", "パピプペポ")] // 半濁点も同様
    [InlineData("ｳﾞｧｲｵﾘﾝ", "ヴァイオリン")]
    [InlineData("ﾀﾛｰ", "タロー")] // 長音 ｰ→ー
    [InlineData("ﾔﾏﾀﾞ･ﾀﾛｳ", "ヤマダ・タロウ")] // 中点 ･→・
    public void Convert_HalfWidthKana_ToFullWidth(string input, string expected)
    {
        Assert.Equal(expected, HalfToFullWidthKana.Convert(input));
    }

    [Fact]
    public void Convert_KanjiPassesThroughUnchanged()
    {
        Assert.Equal("武田太郎", HalfToFullWidthKana.Convert("武田太郎"));
    }

    [Fact]
    public void Convert_AlreadyFullWidth_Unchanged()
    {
        Assert.Equal("タロウ", HalfToFullWidthKana.Convert("タロウ"));
    }

    [Fact]
    public void Convert_DakutenAfterNonCombiningChar_ConvertedAsStandaloneMark()
    {
        // ｱ に濁点の組合せは無い（結合表に無い）ので、それぞれ単独変換される。
        Assert.Equal("ア゛", HalfToFullWidthKana.Convert("ｱﾞ"));
    }
}
