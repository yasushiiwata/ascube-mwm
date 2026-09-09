namespace Ascube.Mwm.Core.Config;

/// <summary>_base.jsonc とのマージ・検証・DeviceProfile 化までを一括で行う。</summary>
public static class ProfileLoader
{
    public static ProfileLoadResult LoadAndValidate(string profileId, string profilesDir)
    {
        try
        {
            var merged = JsoncLoader.LoadMerged(profileId, profilesDir);
            var validation = ProfileValidator.Validate(merged);
            var profile = validation.IsValid ? DeviceProfile.FromValidated(profileId, merged) : null;
            return new ProfileLoadResult(profile, validation);
        }
        catch (ProfileFormatException ex)
        {
            var validation = ValidationResult.Fail(new[] { new ValidationIssue("$", ex.Message) });
            return new ProfileLoadResult(null, validation);
        }
    }
}
