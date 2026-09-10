using Ascube.Mwm.Store.Audit;

namespace Ascube.Mwm.Store.Tests;

public class SqliteAuditStoreTests
{
    private static AuditCFindRecord MakeRecord(
        int resultCount = 1, string? explain = null, bool peerAborted = false,
        IReadOnlyList<AuditCFindItemRecord>? items = null) => new()
    {
        TimestampUtc = new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero),
        ProfileId = "BMD_HOLOGIC",
        CalledAe = "ASCUBE_MWM",
        CallingAe = "APEX_HOLOGIC",
        RequestJson = """{"00080052":{"vr":"CS","Value":["WORKLIST"]}}""",
        CriteriaJson = """{"ScheduledDateRange":null}""",
        ResultCount = resultCount,
        DurationMs = 12,
        Status = "Success",
        Explain = explain,
        PeerAborted = peerAborted,
        Items = items ?? [],
    };

    [Fact]
    public async Task RecordCFindAsync_ReturnsAnAssignedRunId()
    {
        using var db = new TestAuditDatabase();
        var store = new SqliteAuditStore(db.Options());

        var runId = await store.RecordCFindAsync(MakeRecord());

        Assert.True(runId > 0);
    }

    [Fact]
    public async Task RecordCFindAsync_RunIdsAreSequentialAndUnique()
    {
        using var db = new TestAuditDatabase();
        var store = new SqliteAuditStore(db.Options());

        var first = await store.RecordCFindAsync(MakeRecord());
        var second = await store.RecordCFindAsync(MakeRecord());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task GetCFindAsync_RoundTripsAllFields()
    {
        using var db = new TestAuditDatabase();
        var store = new SqliteAuditStore(db.Options());
        var record = MakeRecord(resultCount: 0, explain: "CurrentEntry が存在しません", peerAborted: true);

        var runId = await store.RecordCFindAsync(record);
        var loaded = await store.GetCFindAsync(runId);

        Assert.NotNull(loaded);
        Assert.Equal(runId, loaded!.RunId);
        Assert.Equal(record.ProfileId, loaded.ProfileId);
        Assert.Equal(record.CalledAe, loaded.CalledAe);
        Assert.Equal(record.CallingAe, loaded.CallingAe);
        Assert.Equal(record.RequestJson, loaded.RequestJson);
        Assert.Equal(record.CriteriaJson, loaded.CriteriaJson);
        Assert.Equal(0, loaded.ResultCount);
        Assert.Equal("CurrentEntry が存在しません", loaded.Explain);
        Assert.True(loaded.PeerAborted);
        Assert.Equal(record.TimestampUtc, loaded.TimestampUtc);
    }

    [Fact]
    public async Task GetCFindAsync_UnknownRunId_ReturnsNull()
    {
        using var db = new TestAuditDatabase();
        var store = new SqliteAuditStore(db.Options());

        var loaded = await store.GetCFindAsync(999);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task RecordCFindAsync_ItemsAreStoredAndReadBackInOrder()
    {
        using var db = new TestAuditDatabase();
        var store = new SqliteAuditStore(db.Options());
        var items = new[]
        {
            new AuditCFindItemRecord { ItemIndex = 0, ProvenanceJson = """{"(0010,0020)":"db:StablePatientId"}""" },
        };

        var runId = await store.RecordCFindAsync(MakeRecord(items: items));
        var loaded = await store.GetCFindAsync(runId);

        var item = Assert.Single(loaded!.Items);
        Assert.Equal(0, item.ItemIndex);
        Assert.Equal("""{"(0010,0020)":"db:StablePatientId"}""", item.ProvenanceJson);
        Assert.Null(item.SuppressedReason);
    }

    [Fact]
    public async Task RecordCFindAsync_SuppressedItem_StoresReason()
    {
        using var db = new TestAuditDatabase();
        var store = new SqliteAuditStore(db.Options());
        var items = new[]
        {
            new AuditCFindItemRecord { ItemIndex = 0, SuppressedReason = "(0010,0010) が必須ですが値を解決できませんでした" },
        };

        var runId = await store.RecordCFindAsync(MakeRecord(resultCount: 0, items: items));
        var loaded = await store.GetCFindAsync(runId);

        var item = Assert.Single(loaded!.Items);
        Assert.Null(item.ProvenanceJson);
        Assert.Equal("(0010,0010) が必須ですが値を解決できませんでした", item.SuppressedReason);
    }
}
