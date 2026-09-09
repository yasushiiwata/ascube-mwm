using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// T4：C-FIND 最小実装（固定1件）。実データ照合（T5）・実データ組み立て（T6）はまだ無く、
/// ハードコードした1件が Pending で返り、最後に Success で終わることだけを確認する。
/// </summary>
public class CFindMinimalTests
{
    [Fact]
    public async Task Find_ReturnsExactlyOnePendingResult_ThenSuccess()
    {
        using var server = new ScpTestServer(TestProfileFactory.Build());

        var client = DicomClientFactory.Create("127.0.0.1", server.Port, false, "ANY_SCU", "ASCUBE_MWM");
        var pendingResults = new List<DicomDataset>();
        DicomStatus? finalStatus = null;

        var request = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind)
        {
            Dataset = new DicomDataset { { DicomTag.PatientName, "" } },
        };
        request.OnResponseReceived += (_, response) =>
        {
            if (response.Status == DicomStatus.Pending)
            {
                pendingResults.Add(response.Dataset!);
            }
            else
            {
                finalStatus = response.Status;
            }
        };

        await client.AddRequestAsync(request);
        await client.SendAsync();

        Assert.Single(pendingResults);
        Assert.Equal(DicomStatus.Success, finalStatus);
    }

    [Fact]
    public async Task Find_ResultContainsReadableScheduledProcedureStepSequence()
    {
        using var server = new ScpTestServer(TestProfileFactory.Build());

        var client = DicomClientFactory.Create("127.0.0.1", server.Port, false, "ANY_SCU", "ASCUBE_MWM");
        DicomDataset? result = null;

        var request = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind);
        request.OnResponseReceived += (_, response) =>
        {
            if (response.Status == DicomStatus.Pending)
            {
                result = response.Dataset;
            }
        };

        await client.AddRequestAsync(request);
        await client.SendAsync();

        Assert.NotNull(result);
        Assert.Equal("ISO_IR 192", result!.GetString(DicomTag.SpecificCharacterSet));

        var sps = Assert.Single(result.GetSequence(DicomTag.ScheduledProcedureStepSequence));
        Assert.Equal("BMD", sps.GetString(DicomTag.Modality));
        Assert.Equal("ASCUBE_MWM", sps.GetString(DicomTag.ScheduledStationAETitle));
    }
}
