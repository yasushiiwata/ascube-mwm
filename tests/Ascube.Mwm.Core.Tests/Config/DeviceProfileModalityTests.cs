using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Core.Tests.Config;

/// <summary>T5 が使う DeviceProfile.ModalityMatching / ScheduledStationModality の解決。</summary>
public class DeviceProfileModalityTests
{
    [Fact]
    public void BmdHologicProfile_DefaultsToIgnoreWithNoConfiguredModality()
    {
        // _base.jsonc の (0008,0060) は "const:"（空）のまま。APEX の Modality 既定は None（実装指示書 v2 T5）。
        var result = ProfileLoader.LoadAndValidate("BMD_HOLOGIC", RepoPaths.ProfilesDir);

        Assert.True(result.Validation.IsValid);
        Assert.Equal("ignore", result.Profile!.ModalityMatching);
        Assert.Null(result.Profile.ScheduledStationModality);
    }

    [Fact]
    public void FromValidated_ConfiguredModalityConst_IsExtracted()
    {
        var profile = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "schemaVersion": 1,
              "network": { "aeTitle": "TEST", "port": 11112, "callingAeMatching": "ignore",
                "acceptedTransferSyntaxes": ["1.2.840.10008.1.2"] },
              "matching": { "modalityMatching": "strict", "maxResults": 1 },
              "visibility": { "mode": "current-only", "currentTtlMinutes": 15 },
              "charset": { "specificCharacterSet": "ISO_IR 192" },
              "dataset": {
                "elements": [
                  {
                    "tag": "(0040,0100)", "vr": "SQ",
                    "items": [
                      { "elements": [
                        { "tag": "(0008,0060)", "vr": "CS", "vm": "1", "source": "const:BMD" }
                      ] }
                    ]
                  }
                ]
              }
            }
            """)!.AsObject();

        var deviceProfile = DeviceProfile.FromValidated("test", profile);

        Assert.Equal("strict", deviceProfile.ModalityMatching);
        Assert.Equal("BMD", deviceProfile.ScheduledStationModality);
    }
}
