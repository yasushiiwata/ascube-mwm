using System.Text.Json.Nodes;
using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Core.Tests.Config;

public class ProfileValidatorTests
{
    // 有効な最小プロファイル。各テストはここから1点だけ壊して検証失敗を確認する。
    private const string MinimalValidProfileJson = """
        {
          "schemaVersion": 1,
          "network": { "aeTitle": "TEST", "port": 11112, "callingAeMatching": "ignore" },
          "matching": { "modalityMatching": "ignore", "maxResults": 1 },
          "visibility": { "mode": "current-only", "currentTtlMinutes": 15 },
          "charset": { "specificCharacterSet": "ISO_IR 192" },
          "dataset": {
            "elements": [
              { "tag": "(0010,0020)", "vr": "LO", "vm": "1", "source": "db:StablePatientId" },
              {
                "tag": "(0040,0100)", "vr": "SQ",
                "items": [
                  { "elements": [
                    { "tag": "(0008,0060)", "vr": "CS", "vm": "1", "source": "const:" }
                  ] }
                ]
              }
            ]
          }
        }
        """;

    private static JsonObject ParseMinimalValid() => JsoncLoader.Parse(MinimalValidProfileJson, "test");

    [Fact]
    public void Validate_MinimalValidProfile_Passes()
    {
        var result = ProfileValidator.Validate(ParseMinimalValid());

        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(i => $"{i.Path}: {i.Message}")));
    }

    [Fact]
    public void Validate_MissingParenthesisInTag_FailsAtTagPath()
    {
        var profile = ParseMinimalValid();
        profile["dataset"]!["elements"]![0]!["tag"] = "0010,0020)";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.dataset.elements[0].tag");
    }

    [Fact]
    public void Validate_UnknownTag_FailsNamingTheDictionaryLookup()
    {
        var profile = ParseMinimalValid();
        profile["dataset"]!["elements"]![0]!["tag"] = "(0009,0001)";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("DICOM 辞書に存在しない"));
    }

    [Fact]
    public void Validate_WrongVr_FailsNamingTheExpectedVr()
    {
        var profile = ParseMinimalValid();
        profile["dataset"]!["elements"]![0]!["vr"] = "SH"; // 実際の PatientID は LO

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("VR が DICOM 辞書と一致しません"));
    }

    [Fact]
    public void Validate_VmOutsideDictionaryRange_Fails()
    {
        var profile = ParseMinimalValid();
        profile["dataset"]!["elements"]![0]!["vm"] = "2"; // PatientID は 1-1

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("VM が DICOM 辞書の許容範囲外です"));
    }

    [Fact]
    public void Validate_NonexistentDbColumn_FailsNamingTheColumn()
    {
        var profile = ParseMinimalValid();
        profile["dataset"]!["elements"]![0]!["source"] = "db:NoSuchColumn";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("db: が参照する列が存在しません: NoSuchColumn"));
    }

    [Fact]
    public void Validate_UnknownSourceExpression_Fails()
    {
        var profile = ParseMinimalValid();
        profile["dataset"]!["elements"]![0]!["source"] = "bogus:whatever";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("未知の source 式です"));
    }

    [Fact]
    public void Validate_UnknownCharacterSet_FailsAsUnknownDefinedTerm()
    {
        var profile = ParseMinimalValid();
        profile["charset"]!["specificCharacterSet"] = "SHIFT_JIS";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("未知の定義語"));
    }

    [Fact]
    public void Validate_Iso2022CharacterSet_IsForbiddenEvenThoughItIsAKnownDicomTerm()
    {
        var profile = ParseMinimalValid();
        profile["charset"]!["specificCharacterSet"] = "ISO 2022 IR 87";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("ISO 2022 を含む"));
    }

    [Fact]
    public void Validate_Iso2022CombinedCharacterSet_IsAlsoForbidden()
    {
        var profile = ParseMinimalValid();
        profile["charset"]!["specificCharacterSet"] = "ISO 2022 IR 6\\ISO 2022 IR 87";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("ISO 2022 を含む"));
    }

    [Fact]
    public void Validate_SqNestingBeyondTwoLevels_Fails()
    {
        // elements[1]（深さ1）→ 追加した SQ（深さ2）→ さらにその中の SQ（深さ3、上限超過）
        var profile = ParseMinimalValid();
        var innerElements = profile["dataset"]!["elements"]![1]!["items"]![0]!["elements"]!.AsArray();
        innerElements.Add(JsonNode.Parse("""
            {
              "tag": "(0040,0100)", "vr": "SQ",
              "items": [ { "elements": [
                { "tag": "(0008,0060)", "vr": "CS", "vm": "1", "source": "const:" },
                {
                  "tag": "(0040,0100)", "vr": "SQ",
                  "items": [ { "elements": [ { "tag": "(0008,0060)", "vr": "CS", "vm": "1", "source": "const:" } ] } ]
                }
              ] } ]
            }
            """));

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("入れ子"));
    }

    [Fact]
    public void Validate_SqWithoutItems_Fails()
    {
        var profile = ParseMinimalValid();
        profile["dataset"]!["elements"]![1]!.AsObject().Remove("items");

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("items が必要です"));
    }

    [Fact]
    public void Validate_FallbackToAllTodayFlag_IsForbidden()
    {
        var profile = ParseMinimalValid();
        profile["visibility"]!["fallbackToAllTodayWhenNoActive"] = true;

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.visibility.fallbackToAllTodayWhenNoActive");
    }

    [Fact]
    public void Validate_VisibilityModeOtherThanCurrentOnly_IsForbidden()
    {
        var profile = ParseMinimalValid();
        profile["visibility"]!["mode"] = "active-preferred";

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.visibility.mode");
    }

    [Fact]
    public void Validate_MaxResultsNotOne_IsForbidden()
    {
        var profile = ParseMinimalValid();
        profile["matching"]!["maxResults"] = 50;

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.matching.maxResults");
    }

    [Fact]
    public void Validate_UnsupportedSchemaVersion_Fails()
    {
        var profile = ParseMinimalValid();
        profile["schemaVersion"] = 99;

        var result = ProfileValidator.Validate(profile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "$.schemaVersion");
    }
}
