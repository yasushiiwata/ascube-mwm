using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.PatientName;

namespace Ascube.Mwm.Core.Tests.PatientName;

/// <summary>T7 の受入条件。BMD_HOLOGIC(案①)/.alt1(案②)/.alt2(案④) 相当の群割当で検証する。</summary>
public class PnEncoderTests
{
    private static WorkItemView MakeItem(
        string? familyKanji = "武田", string? givenKanji = "太郎",
        string? familyKana = "ﾀｹﾀﾞ", string? givenKana = "ﾀﾛｳ") => new()
    {
        WorkItemId = "wi-1",
        StudyInstanceUid = "2.25.1",
        StablePatientId = "000012345678",
        FamilyNameKanji = familyKanji,
        GivenNameKanji = givenKanji,
        FamilyNameKana = familyKana,
        GivenNameKana = givenKana,
        ScheduledDate = "20260910",
    };

    // 案①：ISO_IR 192 / group1=kanaFull, group2=kanji, group3=kanaFull
    private static readonly PnGroupAssignment Plan1 = new(PnGroupKind.KanaFull, PnGroupKind.Kanji, PnGroupKind.KanaFull);

    // 案②：ISO_IR 192 / group1=none, group2=kanji, group3=kanaFull
    private static readonly PnGroupAssignment Plan2 = new(PnGroupKind.None, PnGroupKind.Kanji, PnGroupKind.KanaFull);

    // 案④：ISO_IR 13 / group1=kanaHalf, group2=none, group3=none
    private static readonly PnGroupAssignment Plan4 = new(PnGroupKind.KanaHalf, PnGroupKind.None, PnGroupKind.None);

    [Fact]
    public void Plan1_ComposesThreeGroups_KanaFull_Kanji_KanaFull()
    {
        var result = PnEncoder.Encode(Plan1, MakeItem(), "ISO_IR 192");

        Assert.False(result.Suppressed);
        Assert.Equal("タケダ^タロウ=武田^太郎=タケダ^タロウ", result.Value);
    }

    [Fact]
    public void Plan2_FirstGroupEmpty_LeadingEqualsPreserved()
    {
        var result = PnEncoder.Encode(Plan2, MakeItem(), "ISO_IR 192");

        Assert.False(result.Suppressed);
        Assert.Equal("=武田^太郎=タケダ^タロウ", result.Value);
    }

    [Fact]
    public void Plan4_HalfWidthKanaOnly_NoKanjiSent()
    {
        var result = PnEncoder.Encode(Plan4, MakeItem(), "ISO_IR 13");

        Assert.False(result.Suppressed);
        Assert.Equal("ﾀｹﾀﾞ^ﾀﾛｳ", result.Value);
        Assert.DoesNotContain('武', result.Value!);
    }

    [Fact]
    public void Plan4_WithGaijiKanjiSomehowInKanaGroup_Suppressed()
    {
        // 案④のkanaHalf群にたまたま漢字混じりの値が来ても、無言でCP932化せずSuppressedにする。
        var item = MakeItem(familyKana: "髙﨑", givenKana: "德");
        var result = PnEncoder.Encode(Plan4, item, "ISO_IR 13");

        Assert.True(result.Suppressed);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Plan1AndPlan2_WithGaijiKanji_NotSuppressed_UTF8HandlesIt()
    {
        var item = MakeItem(familyKanji: "髙﨑", givenKanji: "德");

        var result1 = PnEncoder.Encode(Plan1, item, "ISO_IR 192");
        var result2 = PnEncoder.Encode(Plan2, item, "ISO_IR 192");

        Assert.False(result1.Suppressed);
        Assert.Contains("髙﨑^德", result1.Value!);
        Assert.False(result2.Suppressed);
        Assert.Contains("髙﨑^德", result2.Value!);
    }

    [Fact]
    public void AllGroupsUnresolved_Suppressed()
    {
        var item = MakeItem(familyKanji: null, givenKanji: null, familyKana: null, givenKana: null);

        var result = PnEncoder.Encode(Plan1, item, "ISO_IR 192");

        Assert.True(result.Suppressed);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Plan4_KanaHalfOnlyGroup_TrailingEmptyGroupsOmitted()
    {
        // group2/group3 が None なので "=" が余計につかない。
        var result = PnEncoder.Encode(Plan4, MakeItem(), "ISO_IR 13");

        Assert.DoesNotContain("=", result.Value!);
    }
}
