using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Core.Tests.Config;

/// <summary>config/profiles/ に実際に置く6枚が全て検証を通ることを保証する（実装指示書 v2 §5-T1）。</summary>
public class RealProfilesTests
{
    public static IEnumerable<object[]> ShippedProfileIds() =>
    [
        ["GENERIC"],
        ["BMD_HOLOGIC"],
        ["BMD_HOLOGIC.alt1"],
        ["BMD_HOLOGIC.alt2"],
        ["ES_DEFAULT"],
    ];

    [Theory]
    [MemberData(nameof(ShippedProfileIds))]
    public void ShippedProfile_PassesValidation(string profileId)
    {
        var result = ProfileLoader.LoadAndValidate(profileId, RepoPaths.ProfilesDir);

        Assert.True(
            result.Validation.IsValid,
            $"{profileId}: " + string.Join("; ", result.Validation.Issues.Select(i => $"{i.Path}: {i.Message}")));
    }

    [Fact]
    public void BaseProfileAlone_PassesValidation()
    {
        var result = ProfileLoader.LoadAndValidate("_base", RepoPaths.ProfilesDir);

        Assert.True(
            result.Validation.IsValid,
            string.Join("; ", result.Validation.Issues.Select(i => $"{i.Path}: {i.Message}")));
    }

    [Theory]
    [InlineData("BMD_HOLOGIC", "ISO_IR 192")]
    [InlineData("BMD_HOLOGIC.alt1", "ISO_IR 192")]
    [InlineData("BMD_HOLOGIC.alt2", "ISO_IR 13")]
    public void BmdProfiles_UseTheExpectedCharacterSet(string profileId, string expectedCharset)
    {
        var result = ProfileLoader.LoadAndValidate(profileId, RepoPaths.ProfilesDir);

        Assert.True(result.Validation.IsValid);
        Assert.Equal(expectedCharset, result.Profile!.SpecificCharacterSet);
    }
}
