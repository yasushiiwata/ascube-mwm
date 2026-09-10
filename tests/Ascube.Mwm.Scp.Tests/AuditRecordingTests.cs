using Ascube.Mwm.Abstractions;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>T8：C-FIND ごとに AuditCFind/AuditCFindItem 相当の記録が残ることを確認する。</summary>
public class AuditRecordingTests
{
    private static WorkItemView MakeCandidate(string scheduledDate = "20260910") => new()
    {
        WorkItemId = "wi-1",
        StudyInstanceUid = "2.25.1",
        StablePatientId = "000012345678",
        FamilyNameKanji = "武田",
        GivenNameKanji = "太郎",
        ScheduledDate = scheduledDate,
    };

    private static async Task RunFindAsync(int port, DicomDataset? queryKeys = null)
    {
        var client = DicomClientFactory.Create("127.0.0.1", port, false, "ANY_SCU", "ASCUBE_MWM");
        var request = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind)
        {
            Dataset = queryKeys ?? new DicomDataset(),
        };
        await client.AddRequestAsync(request);
        await client.SendAsync();
    }

    [Fact]
    public async Task Find_WithMatch_RecordsOneAuditRowWithResultCountOne()
    {
        var repository = new FakeWorklistRepository { Current = MakeCandidate() };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        await RunFindAsync(server.Port);

        var record = Assert.Single(server.AuditWriter.Records);
        Assert.Equal(1, record.ResultCount);
        Assert.Equal("Success", record.Status);
        Assert.False(record.PeerAborted);
        Assert.Null(record.Explain);
        Assert.Equal("ASCUBE_MWM", record.CalledAe);
        Assert.Equal("ANY_SCU", record.CallingAe);
        Assert.False(string.IsNullOrWhiteSpace(record.RequestJson)); // DICOM JSON Model が記録されていること

        var item = Assert.Single(record.Items);
        Assert.NotNull(item.ProvenanceJson);
        Assert.Null(item.SuppressedReason);
    }

    [Fact]
    public async Task Find_NoCurrentEntry_RecordsExplainReason()
    {
        var repository = new FakeWorklistRepository { Current = null };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        await RunFindAsync(server.Port);

        var record = Assert.Single(server.AuditWriter.Records);
        Assert.Equal(0, record.ResultCount);
        Assert.NotNull(record.Explain);
        Assert.Empty(record.Items);
    }

    [Fact]
    public async Task Find_PatientNameMissing_RecordsSuppressedReasonOnItem()
    {
        var repository = new FakeWorklistRepository
        {
            Current = MakeCandidate() with { FamilyNameKanji = null, GivenNameKanji = null },
        };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        await RunFindAsync(server.Port);

        var record = Assert.Single(server.AuditWriter.Records);
        Assert.Equal(0, record.ResultCount);
        var item = Assert.Single(record.Items);
        Assert.NotNull(item.SuppressedReason);
        Assert.Equal(item.SuppressedReason, record.Explain);
    }

    [Fact]
    public async Task Find_ScheduledDateOutOfRange_RecordsExplainWithRequestedAndHeldValues()
    {
        var repository = new FakeWorklistRepository { Current = MakeCandidate(scheduledDate: "20260910") };
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        var sps = new DicomDataset { { DicomTag.ScheduledProcedureStepStartDate, "20260101-20260901" } };
        var query = new DicomDataset { new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps) };

        await RunFindAsync(server.Port, query);

        var record = Assert.Single(server.AuditWriter.Records);
        Assert.Equal(0, record.ResultCount);
        Assert.Contains("20260101-20260901", record.Explain);
        Assert.Contains("20260910", record.Explain);
    }
}
