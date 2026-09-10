using Ascube.Mwm.Abstractions;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// T4（C-FIND プラミング）＋ T5（MatchEngine による実データ照合）。
/// 1人モデルなので候補は常に0件か1件。該当0件は Success かつ結果なし（規則2）。
/// </summary>
public class CFindMinimalTests
{
    private static WorkItemView MakeCandidate(
        string scheduledDate = "20260910",
        string patientId = "000012345678",
        string? familyKanji = "アスキューブ",
        string? givenKanji = "タロウ") =>
        new()
        {
            WorkItemId = "wi-1",
            StudyInstanceUid = "2.25.999999999999999999999999999999999999",
            StablePatientId = patientId,
            FamilyNameKanji = familyKanji,
            GivenNameKanji = givenKanji,
            BirthDate = "19700101",
            Sex = Sex.Male,
            ScheduledDate = scheduledDate,
            RequestedProcedureDesc = "骨密度測定",
        };

    private static async Task<(List<DicomDataset> Pending, DicomStatus? Final)> RunFindAsync(
        int port, DicomDataset? queryKeys = null)
    {
        var client = DicomClientFactory.Create("127.0.0.1", port, false, "ANY_SCU", "ASCUBE_MWM");
        var pending = new List<DicomDataset>();
        DicomStatus? final = null;

        var request = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind)
        {
            Dataset = queryKeys ?? new DicomDataset(),
        };
        request.OnResponseReceived += (_, response) =>
        {
            if (response.Status == DicomStatus.Pending)
            {
                pending.Add(response.Dataset!);
            }
            else
            {
                final = response.Status;
            }
        };

        await client.AddRequestAsync(request);
        await client.SendAsync();

        return (pending, final);
    }

    [Fact]
    public async Task Find_WithMatchingCandidate_ReturnsExactlyOnePendingResult_ThenSuccess()
    {
        var repository = new FakeWorklistRepository { Current = MakeCandidate() };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        var (pending, final) = await RunFindAsync(server.Port);

        Assert.Single(pending);
        Assert.Equal(DicomStatus.Success, final);
    }

    [Fact]
    public async Task Find_ResultContainsReadableScheduledProcedureStepSequence()
    {
        var repository = new FakeWorklistRepository { Current = MakeCandidate() };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        var (pending, _) = await RunFindAsync(server.Port);

        var result = Assert.Single(pending);
        Assert.Equal("ISO_IR 192", result.GetString(DicomTag.SpecificCharacterSet));
        Assert.Equal("000012345678", result.GetString(DicomTag.PatientID));
        Assert.Equal("アスキューブ^タロウ", result.GetString(DicomTag.PatientName));
        Assert.Equal("2.25.999999999999999999999999999999999999", result.GetString(DicomTag.StudyInstanceUID));

        var sps = Assert.Single(result.GetSequence(DicomTag.ScheduledProcedureStepSequence));
        Assert.Equal("ASCUBE_MWM", sps.GetString(DicomTag.ScheduledStationAETitle));
        Assert.Equal("20260910", sps.GetString(DicomTag.ScheduledProcedureStepStartDate));
    }

    [Fact]
    public async Task Find_NoCurrentEntry_ReturnsZeroResultsWithSuccess()
    {
        // 規則2：該当0件は Success かつ結果なし（Error にしない）。
        var repository = new FakeWorklistRepository { Current = null };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        var (pending, final) = await RunFindAsync(server.Port);

        Assert.Empty(pending);
        Assert.Equal(DicomStatus.Success, final);
    }

    [Fact]
    public async Task Find_ScheduledDateOutOfRequestedRange_ReturnsZeroResultsWithSuccess()
    {
        var repository = new FakeWorklistRepository { Current = MakeCandidate(scheduledDate: "20260910") };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        var sps = new DicomDataset { { DicomTag.ScheduledProcedureStepStartDate, "20260101-20260901" } };
        var query = new DicomDataset { new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps) };

        var (pending, final) = await RunFindAsync(server.Port, query);

        Assert.Empty(pending);
        Assert.Equal(DicomStatus.Success, final);
    }

    [Fact]
    public async Task Find_WideDaysBackForwardRange_StillReturnsOnlyTheCurrentEntry()
    {
        var repository = new FakeWorklistRepository { Current = MakeCandidate(scheduledDate: "20260910") };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        var sps = new DicomDataset { { DicomTag.ScheduledProcedureStepStartDate, "20260711-20260912" } }; // -60日〜+2日
        var query = new DicomDataset { new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps) };

        var (pending, final) = await RunFindAsync(server.Port, query);

        Assert.Single(pending);
        Assert.Equal(DicomStatus.Success, final);
    }

    [Fact]
    public async Task Find_PatientIdMismatch_ReturnsZeroResultsWithSuccess()
    {
        var repository = new FakeWorklistRepository { Current = MakeCandidate(patientId: "000012345678") };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        var query = new DicomDataset { { DicomTag.PatientID, "999999999999" } };

        var (pending, final) = await RunFindAsync(server.Port, query);

        Assert.Empty(pending);
        Assert.Equal(DicomStatus.Success, final);
    }

    [Fact]
    public async Task Find_PatientNameMissing_SuppressesTheItem_ReturnsZeroResultsWithSuccess()
    {
        // 規則4：患者に属する値（氏名）が欠損している行は捏造せず返さない。
        var repository = new FakeWorklistRepository
        {
            Current = MakeCandidate(familyKanji: null, givenKanji: null),
        };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        var (pending, final) = await RunFindAsync(server.Port);

        Assert.Empty(pending);
        Assert.Equal(DicomStatus.Success, final);
    }
}
