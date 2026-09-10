using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Core.Dataset;
using FellowOakDicom;

namespace Ascube.Mwm.Core.Tests.Dataset;

/// <summary>T6：source DSL の各キーワード（fallback / maxLength / coalesce: / echo: / when）を個別に検証する。</summary>
public class DatasetBuilderDslTests
{
    private static WorkItemView MakeItem(string? accessionNumber = "A0000001") => new()
    {
        WorkItemId = "wi-1",
        StudyInstanceUid = "2.25.1",
        StablePatientId = "000012345678",
        FamilyNameKanji = "武田",
        GivenNameKanji = "太郎",
        ScheduledDate = "20260910",
        AccessionNumber = accessionNumber,
    };

    private static DeviceProfile BuildProfile(string elementJson)
    {
        var json = $$"""
            {
              "schemaVersion": 1,
              "network": { "aeTitle": "TEST", "port": 11112, "callingAeMatching": "ignore",
                "acceptedTransferSyntaxes": ["1.2.840.10008.1.2"] },
              "matching": { "modalityMatching": "ignore", "maxResults": 1 },
              "visibility": { "mode": "current-only", "currentTtlMinutes": 15 },
              "charset": { "specificCharacterSet": "ISO_IR 192" },
              "dataset": { "elements": [ {{elementJson}} ] }
            }
            """;

        var profile = JsoncLoader.Parse(json, "test");
        var validation = ProfileValidator.Validate(profile);
        Assert.True(validation.IsValid, string.Join("; ", validation.Issues.Select(i => $"{i.Path}: {i.Message}")));

        return DeviceProfile.FromValidated("test", profile);
    }

    [Fact]
    public void Fallback_PrimarySourceMissing_UsesFallback()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "db:AccessionNumber", "fallback": "const:UNKNOWN", "required": false, "onMissing": "empty" }
            """);
        var item = MakeItem(accessionNumber: null);

        var result = DatasetBuilder.Build(profile, item, new DicomDataset());

        Assert.False(result.Suppressed);
        Assert.Equal("UNKNOWN", result.Dataset!.GetString(DicomTag.AccessionNumber));
        Assert.Equal("const:UNKNOWN", result.Provenance["(0008,0050)"]);
    }

    [Fact]
    public void Fallback_PrimarySourcePresent_FallbackNotUsed()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "db:AccessionNumber", "fallback": "const:UNKNOWN", "required": false, "onMissing": "empty" }
            """);
        var item = MakeItem(accessionNumber: "A9999999");

        var result = DatasetBuilder.Build(profile, item, new DicomDataset());

        Assert.Equal("A9999999", result.Dataset!.GetString(DicomTag.AccessionNumber));
        Assert.Equal("db:AccessionNumber", result.Provenance["(0008,0050)"]);
    }

    [Fact]
    public void MaxLength_ValueExceeds_TreatedAsMissing_UsesOnMissingEmpty()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "const:0123456789ABCDEFG", "maxLength": 16, "required": false, "onMissing": "empty" }
            """);

        var result = DatasetBuilder.Build(profile, MakeItem(), new DicomDataset());

        Assert.False(result.Suppressed);
        Assert.True(result.Dataset!.Contains(DicomTag.AccessionNumber));
        Assert.Equal(string.Empty, result.Dataset.GetString(DicomTag.AccessionNumber));
    }

    [Fact]
    public void MaxLength_ValueExceeds_WithRejectItem_Suppresses()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "const:0123456789ABCDEFG", "maxLength": 16, "required": true, "onMissing": "reject-item" }
            """);

        var result = DatasetBuilder.Build(profile, MakeItem(), new DicomDataset());

        Assert.True(result.Suppressed);
    }

    [Fact]
    public void MaxLength_ValueWithinLimit_IsKept()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "const:SHORT", "maxLength": 16, "required": false, "onMissing": "empty" }
            """);

        var result = DatasetBuilder.Build(profile, MakeItem(), new DicomDataset());

        Assert.Equal("SHORT", result.Dataset!.GetString(DicomTag.AccessionNumber));
    }

    [Fact]
    public void Coalesce_FirstMissing_SecondUsed()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "coalesce:[db:AccessionNumber|const:FALLBACK]", "required": false, "onMissing": "empty" }
            """);
        var item = MakeItem(accessionNumber: null);

        var result = DatasetBuilder.Build(profile, item, new DicomDataset());

        Assert.Equal("FALLBACK", result.Dataset!.GetString(DicomTag.AccessionNumber));
    }

    [Fact]
    public void Coalesce_FirstPresent_FirstUsed()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "coalesce:[db:AccessionNumber|const:FALLBACK]", "required": false, "onMissing": "empty" }
            """);
        var item = MakeItem(accessionNumber: "A1111111");

        var result = DatasetBuilder.Build(profile, item, new DicomDataset());

        Assert.Equal("A1111111", result.Dataset!.GetString(DicomTag.AccessionNumber));
    }

    [Fact]
    public void Echo_ReturnsValueFromTopLevelRequestDataset()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "echo:(0008,0050)", "required": false, "onMissing": "empty" }
            """);
        var request = new DicomDataset { { DicomTag.AccessionNumber, "REQ-ECHO" } };

        var result = DatasetBuilder.Build(profile, MakeItem(), request);

        Assert.Equal("REQ-ECHO", result.Dataset!.GetString(DicomTag.AccessionNumber));
        Assert.Equal("echo:(0008,0050)", result.Provenance["(0008,0050)"]);
    }

    [Fact]
    public void Echo_SearchesInsideRequestSequenceItems()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0060)", "vr": "CS", "vm": "1", "source": "echo:(0008,0060)", "required": false, "onMissing": "empty" }
            """);
        var sps = new DicomDataset { { DicomTag.Modality, "BMD" } };
        var request = new DicomDataset { new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps) };

        var result = DatasetBuilder.Build(profile, MakeItem(), request);

        Assert.Equal("BMD", result.Dataset!.GetString(DicomTag.Modality));
    }

    [Fact]
    public void When_ConditionFalse_ElementOmitted()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "const:VALUE", "when": "db:RequestedProcedureId", "required": false, "onMissing": "empty" }
            """);

        var result = DatasetBuilder.Build(profile, MakeItem(), new DicomDataset());

        Assert.False(result.Dataset!.Contains(DicomTag.AccessionNumber));
    }

    [Fact]
    public void When_ConditionTrue_ElementIncluded()
    {
        var profile = BuildProfile("""
            { "tag": "(0008,0050)", "vr": "SH", "vm": "1", "source": "const:VALUE", "when": "db:AccessionNumber", "required": false, "onMissing": "empty" }
            """);

        var result = DatasetBuilder.Build(profile, MakeItem(), new DicomDataset());

        Assert.Equal("VALUE", result.Dataset!.GetString(DicomTag.AccessionNumber));
    }
}
