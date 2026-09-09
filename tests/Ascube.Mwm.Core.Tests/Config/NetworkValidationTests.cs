using System.Text.Json.Nodes;
using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Core.Tests.Config;

/// <summary>
/// T3（C-ECHO SCP・アソシエーション制御）が使う network セクションの検証。
/// aeTitle / acceptedTransferSyntaxes / callingAeMatching+allowedCallingAeTitles の組合せを見る。
/// </summary>
public class NetworkValidationTests
{
    private const string MinimalValidProfileJson = """
        {
          "schemaVersion": 1,
          "network": { "aeTitle": "TEST", "port": 11112, "callingAeMatching": "ignore" },
          "matching": { "modalityMatching": "ignore", "maxResults": 1 },
          "visibility": { "mode": "current-only", "currentTtlMinutes": 15 },
          "charset": { "specificCharacterSet": "ISO_IR 192" },
          "dataset": {
            "elements": [
              { "tag": "(0010,0020)", "vr": "LO", "vm": "1", "source": "db:StablePatientId" }
            ]
          }
        }
        """;

    private static JsonObject ParseMinimalValid() => JsoncLoader.Parse(MinimalValidProfileJson, "test");

    [Fact]
    public void Validate_AeTitleTooLong_Fails()
    {
        var profile = ParseMinimalValid();
        profile["network"]!["aeTitle"] = "THIS_AE_TITLE_IS_WAY_TOO_LONG"; // 17文字超

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.network.aeTitle");
    }

    [Fact]
    public void Validate_AeTitleEmpty_Fails()
    {
        var profile = ParseMinimalValid();
        profile["network"]!["aeTitle"] = "";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.network.aeTitle");
    }

    [Fact]
    public void Validate_UnknownTransferSyntaxUid_Fails()
    {
        var profile = ParseMinimalValid();
        profile["network"]!["acceptedTransferSyntaxes"] = new JsonArray("1.2.840.10008.1.2.4.70"); // JPEG Lossless。規則9で禁止

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("規則9"));
    }

    [Theory]
    [InlineData("1.2.840.10008.1.2")]
    [InlineData("1.2.840.10008.1.2.1")]
    [InlineData("1.2.840.10008.1.2.2")]
    public void Validate_KnownTransferSyntaxUid_Passes(string uid)
    {
        var profile = ParseMinimalValid();
        profile["network"]!["acceptedTransferSyntaxes"] = new JsonArray(uid);

        var result = ProfileValidator.Validate(profile);

        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(i => $"{i.Path}: {i.Message}")));
    }

    [Fact]
    public void Validate_StrictCallingAeMatchingWithoutAllowedList_Fails()
    {
        var profile = ParseMinimalValid();
        profile["network"]!["callingAeMatching"] = "strict";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.network.allowedCallingAeTitles");
    }

    [Fact]
    public void Validate_LogOnlyCallingAeMatchingWithoutAllowedList_Fails()
    {
        var profile = ParseMinimalValid();
        profile["network"]!["callingAeMatching"] = "logOnly";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.network.allowedCallingAeTitles");
    }

    [Fact]
    public void Validate_StrictCallingAeMatchingWithAllowedList_Passes()
    {
        var profile = ParseMinimalValid();
        profile["network"]!["callingAeMatching"] = "strict";
        profile["network"]!["allowedCallingAeTitles"] = new JsonArray("APEX_HOLOGIC");

        var result = ProfileValidator.Validate(profile);

        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(i => $"{i.Path}: {i.Message}")));
    }

    [Fact]
    public void Validate_IgnoreCallingAeMatchingWithoutAllowedList_Passes()
    {
        var profile = ParseMinimalValid();
        profile["network"]!["callingAeMatching"] = "ignore";

        var result = ProfileValidator.Validate(profile);

        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(i => $"{i.Path}: {i.Message}")));
    }

    [Fact]
    public void Validate_AllowedCallingAeTitleTooLong_Fails()
    {
        var profile = ParseMinimalValid();
        profile["network"]!["callingAeMatching"] = "strict";
        profile["network"]!["allowedCallingAeTitles"] = new JsonArray("THIS_AE_TITLE_IS_WAY_TOO_LONG");

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.network.allowedCallingAeTitles[0]");
    }

    [Fact]
    public void FromValidated_ExposesNetworkFields()
    {
        var profile = ParseMinimalValid();
        profile["network"]!["acceptedTransferSyntaxes"] = new JsonArray(
            "1.2.840.10008.1.2", "1.2.840.10008.1.2.1", "1.2.840.10008.1.2.2");

        var result = ProfileValidator.Validate(profile);
        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(i => $"{i.Path}: {i.Message}")));

        var deviceProfile = DeviceProfile.FromValidated("test", profile);

        Assert.Equal("TEST", deviceProfile.AeTitle);
        Assert.Equal(11112, deviceProfile.Port);
        Assert.Equal("ignore", deviceProfile.CallingAeMatching);
        Assert.Equal(
            new[] { "1.2.840.10008.1.2", "1.2.840.10008.1.2.1", "1.2.840.10008.1.2.2" },
            deviceProfile.AcceptedTransferSyntaxUids);
        Assert.Empty(deviceProfile.AllowedCallingAeTitles);
    }
}
