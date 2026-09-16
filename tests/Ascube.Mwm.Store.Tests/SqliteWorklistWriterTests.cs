using Ascube.Mwm.Abstractions;

namespace Ascube.Mwm.Store.Tests;

public class SqliteWorklistWriterTests
{
    private static WorklistEntry MakeEntry(string patientId = "000012345678", string scheduledDate = "20260928") => new()
    {
        StablePatientId = patientId,
        FamilyNameKanji = "武田",
        GivenNameKanji = "太郎",
        FamilyNameKana = "ﾀｹﾀﾞ",
        GivenNameKana = "ﾀﾛｳ",
        BirthDate = "19800101",
        Sex = Sex.Male,
        ScheduledDate = scheduledDate,
        AccessionNumber = "2609280001010053",
        RequestedProcedureId = "00110",
        RequestedProcedureDesc = "B0100：一日ドック",
        PatientHeightCm = 170,
        PatientWeightKg = 65.0,
        SourceMessageId = "1irai20260928083008.csv",
    };

    [Fact]
    public async Task SetCurrentAsync_CalledOneHundredTimesWithSameEntry_DoesNotGrowWorkItemAndKeepsSameUid()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        var entry = MakeEntry();

        for (var i = 0; i < 100; i++)
        {
            await writer.SetCurrentAsync(entry);
        }

        Assert.Equal(1, await SqlProbe.CountAsync(db.Path, "WorkItem"));
        Assert.Equal(1, await SqlProbe.CountAsync(db.Path, "UidAllocation"));

        var uid = await SqlProbe.ScalarAsync(db.Path, "SELECT StudyInstanceUid FROM UidAllocation;");
        Assert.NotNull(uid);
        Assert.Matches(@"^2\.25\.\d{1,39}$", uid);
    }

    [Fact]
    public async Task SetCurrentAsync_TwiceWithDifferentPatients_KeepsStudyInstanceUidStablePerPatient()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        var entry = MakeEntry();

        await writer.SetCurrentAsync(entry);
        var firstUid = await SqlProbe.ScalarAsync(db.Path, "SELECT StudyInstanceUid FROM UidAllocation;");

        await writer.SetCurrentAsync(entry with { PatientWeightKg = 66.0 });
        var secondUid = await SqlProbe.ScalarAsync(db.Path, "SELECT StudyInstanceUid FROM UidAllocation;");

        Assert.Equal(firstUid, secondUid);
        Assert.Equal(1, await SqlProbe.CountAsync(db.Path, "WorkItem"));
    }

    [Fact]
    public async Task SetCurrentAsync_AThenB_GetCurrentReturnsOnlyB()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        var patientA = MakeEntry(patientId: "000000000001");
        var patientB = MakeEntry(patientId: "000000000002");

        await writer.SetCurrentAsync(patientA);
        await writer.SetCurrentAsync(patientB);

        var current = await writer.GetCurrentAsync();

        Assert.NotNull(current);
        Assert.Equal(patientB.StablePatientId, current!.StablePatientId);
    }

    [Fact]
    public async Task ClearCurrentAsync_ThenGetCurrentAsync_ReturnsNullButKeepsWorkItemHistory()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry());

        await writer.ClearCurrentAsync();

        Assert.Null(await writer.GetCurrentAsync());
        Assert.Equal(1, await SqlProbe.CountAsync(db.Path, "WorkItem")); // WorkItem は消さない
        Assert.Equal(0, await SqlProbe.CountAsync(db.Path, "CurrentEntry"));
    }

    [Fact]
    public async Task GetCurrentAsync_AfterTtlExpires_ReturnsNull()
    {
        using var db = new TestDatabase();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));
        var writer = new SqliteWorklistWriter(db.Options(currentTtl: TimeSpan.FromMinutes(15)), time);

        await writer.SetCurrentAsync(MakeEntry());
        Assert.NotNull(await writer.GetCurrentAsync());

        time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        Assert.Null(await writer.GetCurrentAsync());
    }

    [Fact]
    public async Task GetCurrentAsync_JustBeforeTtlExpires_StillReturnsEntry()
    {
        using var db = new TestDatabase();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));
        var writer = new SqliteWorklistWriter(db.Options(currentTtl: TimeSpan.FromMinutes(15)), time);

        await writer.SetCurrentAsync(MakeEntry());
        time.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));

        Assert.NotNull(await writer.GetCurrentAsync());
    }
}
