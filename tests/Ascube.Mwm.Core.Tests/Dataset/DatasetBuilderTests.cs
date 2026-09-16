using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Core.Dataset;
using FellowOakDicom;

namespace Ascube.Mwm.Core.Tests.Dataset;

/// <summary>
/// T6（DatasetBuilder）の受入条件。実際に配布する BMD_HOLOGIC プロファイル（_base.jsonc 継承）で検証する。
/// </summary>
public class DatasetBuilderTests
{
    private static readonly DeviceProfile Profile = LoadProfile();

    private static DeviceProfile LoadProfile()
    {
        var result = ProfileLoader.LoadAndValidate("BMD_HOLOGIC", RepoPaths.ProfilesDir);
        if (!result.Validation.IsValid)
        {
            throw new InvalidOperationException(string.Join("; ", result.Validation.Issues.Select(i => $"{i.Path}: {i.Message}")));
        }

        return result.Profile!;
    }

    private static WorkItemView MakeFullItem() => new()
    {
        WorkItemId = "wi-1",
        StudyInstanceUid = "2.25.111111111111111111111111111111111111",
        StablePatientId = "000012345678",
        FamilyNameKanji = "武田",
        GivenNameKanji = "太郎",
        FamilyNameKana = "ﾀｹﾀﾞ",
        GivenNameKana = "ﾀﾛｳ",
        BirthDate = "19800101",
        Sex = Sex.Male,
        ScheduledDate = "20260928",
        AccessionNumber = "A1000001",
        RequestedProcedureId = "RP0001",
        RequestedProcedureDesc = "骨密度測定",
        PatientHeightCm = 165,
        PatientWeightKg = 55.5,
    };

    [Fact]
    public void Build_FullItem_NotSuppressed_SetsSpecificCharacterSetFirst()
    {
        var result = DatasetBuilder.Build(Profile, MakeFullItem(), new DicomDataset());

        Assert.False(result.Suppressed);
        Assert.NotNull(result.Dataset);
        // 規則6：DicomDataset の最初の要素が SpecificCharacterSet であること。
        var firstTag = result.Dataset!.First().Tag;
        Assert.Equal(DicomTag.SpecificCharacterSet, firstTag);
        Assert.Equal("ISO_IR 192", result.Dataset.GetString(DicomTag.SpecificCharacterSet));
    }

    [Fact]
    public void Build_FullItem_MapsPatientAndStudyFields()
    {
        var item = MakeFullItem();
        var result = DatasetBuilder.Build(Profile, item, new DicomDataset());

        Assert.False(result.Suppressed);
        var ds = result.Dataset!;
        // BMD_HOLOGIC の charset.patientName: group1=kanaFull, group2=kanji, group3=kanaFull（T7 PnEncoder）。
        Assert.Equal("タケダ^タロウ=武田^太郎=タケダ^タロウ", ds.GetString(DicomTag.PatientName));
        Assert.Equal("000012345678", ds.GetString(DicomTag.PatientID));
        Assert.Equal("19800101", ds.GetString(DicomTag.PatientBirthDate));
        Assert.Equal("M", ds.GetString(DicomTag.PatientSex));
        Assert.Equal(item.StudyInstanceUid, ds.GetString(DicomTag.StudyInstanceUID));
        Assert.Equal("A1000001", ds.GetString(DicomTag.AccessionNumber));
    }

    [Fact]
    public void Build_ScheduledProcedureStepSequence_HasOneItemWithScheduledDate()
    {
        var result = DatasetBuilder.Build(Profile, MakeFullItem(), new DicomDataset());

        var sps = Assert.Single(result.Dataset!.GetSequence(DicomTag.ScheduledProcedureStepSequence));
        Assert.Equal("20260928", sps.GetString(DicomTag.ScheduledProcedureStepStartDate));
        Assert.Equal("ASCUBE_MWM", sps.GetString(DicomTag.ScheduledStationAETitle));
    }

    [Fact]
    public void Build_MissingPatientSizeAndWeight_OmitsTagsEntirely()
    {
        // 規則17：当日未測定なら (0010,1020)/(0010,1030) をタグごと省略する（空文字で出さない）。
        var item = MakeFullItem() with { PatientHeightCm = null, PatientWeightKg = null };
        var result = DatasetBuilder.Build(Profile, item, new DicomDataset());

        Assert.False(result.Suppressed);
        Assert.False(result.Dataset!.Contains(DicomTag.PatientSize));
        Assert.False(result.Dataset.Contains(DicomTag.PatientWeight));
    }

    [Fact]
    public void Build_PresentPatientSize_IsIncluded()
    {
        var item = MakeFullItem() with { PatientHeightCm = 165, PatientWeightKg = null };
        var result = DatasetBuilder.Build(Profile, item, new DicomDataset());

        Assert.True(result.Dataset!.Contains(DicomTag.PatientSize));
        Assert.False(result.Dataset.Contains(DicomTag.PatientWeight));
    }

    [Fact]
    public void Build_PatientHeightCm_IsConvertedToMetersForDicomTag()
    {
        // 設計変更メモ_v2.1.md §I：DICOM (0010,1020) はメートル単位。BRIDGE-Naviはcmで渡すため、
        // ascube-mwm側（DatasetBuilder）で変換する。165cm→1.65m。
        var item = MakeFullItem() with { PatientHeightCm = 165, PatientWeightKg = null };
        var result = DatasetBuilder.Build(Profile, item, new DicomDataset());

        Assert.Equal("1.65", result.Dataset!.GetString(DicomTag.PatientSize));
    }

    [Fact]
    public void Build_MissingPatientName_RequiredAndOnMissingRejectItem_Suppresses()
    {
        // E-11：required 欠損で Suppressed になる。
        var item = MakeFullItem() with { FamilyNameKanji = null, GivenNameKanji = null, FamilyNameKana = null, GivenNameKana = null };
        var result = DatasetBuilder.Build(Profile, item, new DicomDataset());

        Assert.True(result.Suppressed);
        Assert.Null(result.Dataset);
        Assert.Contains("(0010,0010)", result.SuppressedReason);
    }

    [Fact]
    public void Build_MissingRequiredScheduledDate_Suppresses()
    {
        // ScheduledDate は required かつ SQ 内。SQ 内の必須欠損も全体を Suppressed にする。
        var item = MakeFullItem() with { ScheduledDate = "" };

        var result = DatasetBuilder.Build(Profile, item, new DicomDataset());

        Assert.True(result.Suppressed);
        Assert.Contains("(0040,0002)", result.SuppressedReason);
    }

    [Fact]
    public void Build_MissingOptionalBirthDate_EmitsEmptyTag()
    {
        var item = MakeFullItem() with { BirthDate = null };
        var result = DatasetBuilder.Build(Profile, item, new DicomDataset());

        Assert.False(result.Suppressed);
        Assert.True(result.Dataset!.Contains(DicomTag.PatientBirthDate));
        Assert.Equal(string.Empty, result.Dataset.GetString(DicomTag.PatientBirthDate));
    }

    [Fact]
    public void Build_ProvenanceRecordsWinningSourceForEachTag()
    {
        var result = DatasetBuilder.Build(Profile, MakeFullItem(), new DicomDataset());

        Assert.Equal("db:StablePatientId", result.Provenance["(0010,0020)"]);
        Assert.Equal("auto:patientName", result.Provenance["(0010,0010)"]);
        Assert.Equal("uid:study", result.Provenance["(0020,000D)"]);
        Assert.Equal("db:ScheduledDate", result.Provenance["(0040,0100)[0].(0040,0002)"]);
    }

    [Fact]
    public void Build_KanjiMissing_KanaGroupsStillComposed_KanjiGroupEmpty()
    {
        // group2(kanji) だけ解決できないので中間の群は空文字（"=" は保持する。DICOM PN の群区切りの仕様どおり）。
        var item = MakeFullItem() with { FamilyNameKanji = null, GivenNameKanji = null };
        var result = DatasetBuilder.Build(Profile, item, new DicomDataset());

        Assert.False(result.Suppressed);
        Assert.Equal("タケダ^タロウ==タケダ^タロウ", result.Dataset!.GetString(DicomTag.PatientName));
    }
}
