using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Core.Tests.Config;

/// <summary>
/// 受入条件：わざと壊した6パターン（括弧欠落・未知タグ・VR誤り・存在しない列・不正charset・
/// ISO 2022 を含むcharset）すべてで「どこが悪いか」が分かる形で検証失敗する。
/// フィクスチャは Fixtures/broken/ 配下（config/profiles/ 本番プロファイルとは別物）。
/// </summary>
public class BrokenProfilesTests
{
    private static string FixturesDir =>
        Path.Combine(RepoPaths.RepoRoot, "tests", "Ascube.Mwm.Core.Tests", "Fixtures", "broken");

    public static IEnumerable<object[]> BrokenProfiles() =>
    [
        ["missing-paren", "括弧欠落", "タグの形式が不正です"],
        ["unknown-tag", "未知タグ", "DICOM 辞書に存在しない"],
        ["wrong-vr", "VR誤り", "VR が DICOM 辞書と一致しません"],
        ["missing-column", "存在しない列", "db: が参照する列が存在しません"],
        ["invalid-charset", "不正charset", "未知の定義語"],
        ["iso2022-charset", "ISO 2022を含むcharset", "ISO 2022 を含む"],
    ];

    [Theory]
    [MemberData(nameof(BrokenProfiles))]
    public void BrokenProfile_FailsValidationWithActionableMessage(string profileId, string patternName, string expectedFragment)
    {
        var result = ProfileLoader.LoadAndValidate(profileId, FixturesDir);

        Assert.False(result.Validation.IsValid, $"パターン「{patternName}」が検証をすり抜けました: {profileId}");
        Assert.Contains(
            result.Validation.Issues,
            i => i.Message.Contains(expectedFragment));
        // 「どこが悪いか」が分かる = 全ての issue が具体的な JSON パスを持つ
        Assert.All(result.Validation.Issues, i => Assert.False(string.IsNullOrEmpty(i.Path)));
    }

    [Fact]
    public void FixtureBaseAlone_IsValid()
    {
        var result = ProfileLoader.LoadAndValidate("_base", FixturesDir);

        Assert.True(result.Validation.IsValid, string.Join("; ", result.Validation.Issues.Select(i => i.Message)));
    }
}
