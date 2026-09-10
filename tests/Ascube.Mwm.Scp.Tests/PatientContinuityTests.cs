using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Store;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>
/// T13「絶対に削らない」新規2項目：
/// W-01（<c>SetCurrentAsync(A)</c> → <c>SetCurrentAsync(B)</c> → C-FIND は B だけを返す。A が混ざらない）
/// W-02（<c>ClearCurrentAsync</c> → C-FIND は0件＋Success。前の受診者が残らない）。
/// 規則16（1人モデル。患者取り違えは最悪の事故）の中核不変条件。
/// Store.Tests は <see cref="SqliteWorklistWriter"/> 単体・<see cref="SqliteWorklistRepository"/> 単体しか見ておらず、
/// CFindMinimalTests は <see cref="FakeWorklistRepository"/> までしか見ていない。ここでは
/// BRIDGE-Navi（writer）→実SQLite→SCP（repository）→実 C-FIND という配線全体を1本で通す。
/// </summary>
public class PatientContinuityTests
{
    private static WorklistEntry MakeEntry(string patientId, string familyKanji, string givenKanji) => new()
    {
        StablePatientId = patientId,
        FamilyNameKanji = familyKanji,
        GivenNameKanji = givenKanji,
        BirthDate = "19800101",
        Sex = Sex.Male,
        ScheduledDate = "20260910",
    };

    private static async Task<(List<DicomDataset> Pending, DicomStatus? Final)> RunFindAsync(int port)
    {
        var client = DicomClientFactory.Create("127.0.0.1", port, false, "ANY_SCU", "ASCUBE_MWM");
        var pending = new List<DicomDataset>();
        DicomStatus? final = null;

        var request = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind) { Dataset = new DicomDataset() };
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
    public async Task W01_SetCurrentAThenB_CFindOverTheWireReturnsOnlyB()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        var repository = new SqliteWorklistRepository(db.Options());
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        await writer.SetCurrentAsync(MakeEntry("000000000001", "武田", "太郎"));
        var (pendingA, finalA) = await RunFindAsync(server.Port);
        var itemA = Assert.Single(pendingA);
        Assert.Equal("000000000001", itemA.GetString(DicomTag.PatientID));
        Assert.Equal(DicomStatus.Success, finalA);

        await writer.SetCurrentAsync(MakeEntry("000000000002", "鈴木", "花子"));
        var (pendingB, finalB) = await RunFindAsync(server.Port);

        var itemB = Assert.Single(pendingB);
        Assert.Equal("000000000002", itemB.GetString(DicomTag.PatientID));
        Assert.Equal(DicomStatus.Success, finalB);
        Assert.DoesNotContain(pendingB, d => d.GetString(DicomTag.PatientID) == "000000000001");
    }

    [Fact]
    public async Task W02_ClearCurrent_CFindOverTheWireReturnsZeroResultsWithSuccess()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        var repository = new SqliteWorklistRepository(db.Options());
        using var server = new ScpTestServer(repository, TestProfileFactory.Build());

        await writer.SetCurrentAsync(MakeEntry("000000000001", "武田", "太郎"));
        var (pendingBefore, _) = await RunFindAsync(server.Port);
        Assert.Single(pendingBefore);

        await writer.ClearCurrentAsync();
        var (pendingAfter, finalAfter) = await RunFindAsync(server.Port);

        Assert.Empty(pendingAfter);
        Assert.Equal(DicomStatus.Success, finalAfter);
    }
}
