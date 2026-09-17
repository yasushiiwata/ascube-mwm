using Ascube.Mwm.Abstractions;
using Microsoft.Data.Sqlite;

namespace Ascube.Mwm.Store.Tests;

public class SqliteWorklistRepositoryTests
{
    private static WorklistEntry MakeEntry() => new()
    {
        StablePatientId = "000012345678",
        FamilyNameKanji = "武田",
        GivenNameKanji = "太郎",
        BirthDate = "19800101",
        Sex = Sex.Male,
        ScheduledDate = "20260928",
    };

    [Fact]
    public async Task QueryAsync_NoCurrentEntry_ReturnsEmpty()
    {
        using var db = new TestDatabase();
        _ = new SqliteWorklistWriter(db.Options()); // スキーマだけ作らせる
        var repository = new SqliteWorklistRepository(db.Options());

        var results = await CollectAsync(repository);

        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_AfterSetCurrent_ReturnsExactlyOneMatchingWorkItem()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        var entry = MakeEntry();
        await writer.SetCurrentAsync(entry);
        var repository = new SqliteWorklistRepository(db.Options());

        var results = await CollectAsync(repository);

        var view = Assert.Single(results);
        Assert.Equal(entry.StablePatientId, view.StablePatientId);
        Assert.Matches(@"^2\.25\.\d{1,39}$", view.StudyInstanceUid);
    }

    [Fact]
    public async Task QueryAsync_AfterClearCurrent_ReturnsEmpty()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry());
        await writer.ClearCurrentAsync();
        var repository = new SqliteWorklistRepository(db.Options());

        Assert.Empty(await CollectAsync(repository));
    }

    [Fact]
    public async Task QueryAsync_AfterTtlExpires_ReturnsEmpty()
    {
        using var db = new TestDatabase();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));
        var writer = new SqliteWorklistWriter(db.Options(currentTtl: TimeSpan.FromMinutes(15)), time);
        await writer.SetCurrentAsync(MakeEntry());

        time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        var repository = new SqliteWorklistRepository(db.Options(currentTtl: TimeSpan.FromMinutes(15)), time);

        Assert.Empty(await CollectAsync(repository));
    }

    [Fact]
    public async Task QueryAsync_LimitZero_ReturnsEmptyEvenWhenEntryExists()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry());
        var repository = new SqliteWorklistRepository(db.Options());

        var results = await CollectAsync(repository, limit: 0);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ExplainAsync_NoCurrentEntry_NamesTheReason()
    {
        using var db = new TestDatabase();
        _ = new SqliteWorklistWriter(db.Options());
        var repository = new SqliteWorklistRepository(db.Options());

        var result = await repository.ExplainAsync(new QueryCriteria());

        Assert.False(result.Found);
        Assert.Contains("存在しません", result.Reason);
    }

    [Fact]
    public async Task ExplainAsync_TtlExpired_NamesTheReasonWithTimestamps()
    {
        using var db = new TestDatabase();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));
        var writer = new SqliteWorklistWriter(db.Options(currentTtl: TimeSpan.FromMinutes(15)), time);
        await writer.SetCurrentAsync(MakeEntry());
        time.Advance(TimeSpan.FromMinutes(20));
        var repository = new SqliteWorklistRepository(db.Options(currentTtl: TimeSpan.FromMinutes(15)), time);

        var result = await repository.ExplainAsync(new QueryCriteria());

        Assert.False(result.Found);
        Assert.Contains("TTL", result.Reason);
    }

    [Fact]
    public async Task ExplainAsync_LiveEntry_ReportsFound()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry());
        var repository = new SqliteWorklistRepository(db.Options());

        var result = await repository.ExplainAsync(new QueryCriteria());

        Assert.True(result.Found);
    }

    [Fact]
    public async Task QueryAsync_WideDaysBackForwardRange_StillReturnsOnlyTheCurrentEntry()
    {
        // T5受入条件：「装置がDays Back 60/Forward2の広い範囲を要求しても、返るのはCurrentEntryの1件だけ」。
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry()); // ScheduledDate = 20260928
        var repository = new SqliteWorklistRepository(db.Options());

        var wideRange = new QueryCriteria { ScheduledDateRange = "20260729-20260930" }; // -60日〜+2日相当
        var results = await CollectAsync(repository, criteria: wideRange);

        Assert.Single(results);
    }

    [Fact]
    public async Task QueryAsync_ScheduledDateOutOfRange_ReturnsEmptyWithoutError()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry()); // ScheduledDate = 20260928
        var repository = new SqliteWorklistRepository(db.Options());

        var outOfRange = new QueryCriteria { ScheduledDateRange = "20260101-20260901" };
        Assert.Empty(await CollectAsync(repository, criteria: outOfRange));
    }

    [Fact]
    public async Task QueryAsync_PatientIdMismatch_ReturnsEmpty()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry());
        var repository = new SqliteWorklistRepository(db.Options());

        var wrongPatient = new QueryCriteria { PatientId = "999999999999" };
        Assert.Empty(await CollectAsync(repository, criteria: wrongPatient));
    }

    [Fact]
    public async Task QueryAsync_PatientIdMatchesWithWildcard_ReturnsTheEntry()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry()); // StablePatientId = 000012345678
        var repository = new SqliteWorklistRepository(db.Options());

        var wildcard = new QueryCriteria { PatientId = "0000*" };
        Assert.Single(await CollectAsync(repository, criteria: wildcard));
    }

    [Fact]
    public async Task ExplainAsync_ScheduledDateOutOfRange_NamesRequestedAndHeldValues()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry()); // ScheduledDate = 20260928
        var repository = new SqliteWorklistRepository(db.Options());

        var result = await repository.ExplainAsync(new QueryCriteria { ScheduledDateRange = "20260101-20260901" });

        Assert.False(result.Found);
        Assert.Contains("20260101-20260901", result.Reason);
        Assert.Contains("20260928", result.Reason);
    }

    [Fact]
    public async Task ExplainAsync_PatientIdMismatch_NamesRequestedAndHeldValues()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry()); // StablePatientId = 000012345678
        var repository = new SqliteWorklistRepository(db.Options());

        var result = await repository.ExplainAsync(new QueryCriteria { PatientId = "999999999999" });

        Assert.False(result.Found);
        Assert.Contains("999999999999", result.Reason);
        Assert.Contains("000012345678", result.Reason);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_NoCurrentEntry_ReportsNotExists()
    {
        using var db = new TestDatabase();
        _ = new SqliteWorklistWriter(db.Options());
        var repository = new SqliteWorklistRepository(db.Options());

        var status = await repository.GetCurrentStatusAsync();

        Assert.False(status.Exists);
        Assert.False(status.IsAlive);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_LiveEntry_ReportsExistsAndAlive()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry());
        var repository = new SqliteWorklistRepository(db.Options());

        var status = await repository.GetCurrentStatusAsync();

        Assert.True(status.Exists);
        Assert.True(status.IsAlive);
        Assert.NotNull(status.ExpiresAtUtc);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_ExpiredEntry_ReportsExistsButNotAlive()
    {
        using var db = new TestDatabase();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));
        var writer = new SqliteWorklistWriter(db.Options(currentTtl: TimeSpan.FromMinutes(15)), time);
        await writer.SetCurrentAsync(MakeEntry());
        time.Advance(TimeSpan.FromMinutes(20));
        var repository = new SqliteWorklistRepository(db.Options(currentTtl: TimeSpan.FromMinutes(15)), time);

        var status = await repository.GetCurrentStatusAsync();

        Assert.True(status.Exists);
        Assert.False(status.IsAlive);
    }

    [Fact]
    public async Task GetLatestByPatientIdAsync_KnownPatient_ReturnsWorkItem()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry());
        var repository = new SqliteWorklistRepository(db.Options());

        var view = await repository.GetLatestByPatientIdAsync("000012345678");

        Assert.NotNull(view);
        Assert.Equal("武田", view!.FamilyNameKanji);
        Assert.Matches(@"^2\.25\.\d{1,39}$", view.StudyInstanceUid);
    }

    [Fact]
    public async Task GetLatestByPatientIdAsync_AfterClearCurrent_StillReturnsHistoricalWorkItem()
    {
        // ClearCurrentAsync は「今の1人」を消すだけで、WorkItem の履歴は残る（mwm-scu preview は履歴から引く）。
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry());
        await writer.ClearCurrentAsync();
        var repository = new SqliteWorklistRepository(db.Options());

        var view = await repository.GetLatestByPatientIdAsync("000012345678");

        Assert.NotNull(view);
    }

    [Fact]
    public async Task GetLatestByPatientIdAsync_UnknownPatient_ReturnsNull()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        await writer.SetCurrentAsync(MakeEntry());
        var repository = new SqliteWorklistRepository(db.Options());

        var view = await repository.GetLatestByPatientIdAsync("999999999999");

        Assert.Null(view);
    }

    // FIX-001：data\mwm.db が存在しない（＝誰も SetCurrentAsync していない）状態は
    // 「今この端末に受診者はいない」であって異常ではない（規則2）。例外を投げてはならない。
    // Repository.TryGetLiveViewAsync は private のため、その唯一の呼び出し元である QueryAsync
    // 経由でこの受入条件を検証する。

    [Fact]
    public async Task QueryAsync_WhenDatabaseFileMissing_ReturnsEmptyWithoutError()
    {
        using var db = new TestDatabase(); // ファイルパスを払い出すだけで、ファイルは作らない
        var repository = new SqliteWorklistRepository(db.Options());

        var results = await CollectAsync(repository);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ExplainAsync_WhenDatabaseFileMissing_ReturnsNotFoundWithoutError()
    {
        using var db = new TestDatabase();
        var repository = new SqliteWorklistRepository(db.Options());

        var result = await repository.ExplainAsync(new QueryCriteria());

        Assert.False(result.Found);
        Assert.Contains("未作成", result.Reason);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_WhenDatabaseFileMissing_ReturnsNotExists()
    {
        using var db = new TestDatabase();
        var repository = new SqliteWorklistRepository(db.Options());

        var status = await repository.GetCurrentStatusAsync();

        Assert.False(status.Exists);
        Assert.False(status.IsAlive);
    }

    [Fact]
    public async Task QueryAsync_WhenDatabaseFileUnreadable_StillThrows()
    {
        // 握りつぶしていないことの証明：ファイルは「ある」が開けない場合は、
        // 「ファイルが無い」判定に化けてはならず、これまでどおり例外が上に投げられること。
        using var db = new TestDatabase();
        File.WriteAllBytes(db.Path, []);
        using var exclusiveLock = new FileStream(db.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var repository = new SqliteWorklistRepository(db.Options());

        await Assert.ThrowsAsync<SqliteException>(() => CollectAsync(repository));
    }

    private static async Task<List<WorkItemView>> CollectAsync(SqliteWorklistRepository repository, int limit = 10, QueryCriteria? criteria = null)
    {
        var results = new List<WorkItemView>();
        await foreach (var item in repository.QueryAsync(criteria ?? new QueryCriteria(), limit))
        {
            results.Add(item);
        }

        return results;
    }
}
