using Ascube.Mwm.Abstractions;

namespace Ascube.Mwm.Store.Tests;

/// <summary>E-13（ロック例外なし）・E-14（トランザクション中の中途半端な行が見えない）。</summary>
public class ConcurrencyTests
{
    [Fact]
    public async Task ConcurrentSetCurrentAndQuery_NoLockExceptions()
    {
        using var db = new TestDatabase();
        var writer = new SqliteWorklistWriter(db.Options());
        var repository = new SqliteWorklistRepository(db.Options());

        using var cts = new CancellationTokenSource();

        var writers = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            for (var j = 0; j < 20; j++)
            {
                await writer.SetCurrentAsync(new WorklistEntry
                {
                    StablePatientId = $"{i:D12}",
                    ScheduledDate = "20260928",
                    Sex = Sex.Unknown,
                });
            }
        }));

        var readers = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await foreach (var view in repository.QueryAsync(new QueryCriteria(), limit: 10, cts.Token))
                {
                    // E-14: 読めた行は必ず完全な行でなければならない（require 済みプロパティが例外なく埋まる）。
                    Assert.False(string.IsNullOrEmpty(view.WorkItemId));
                    Assert.False(string.IsNullOrEmpty(view.StudyInstanceUid));
                    Assert.False(string.IsNullOrEmpty(view.StablePatientId));
                }
            }
        }));

        var writeTask = Task.WhenAll(writers);
        await writeTask;
        cts.Cancel();

        try
        {
            await Task.WhenAll(readers);
        }
        catch (OperationCanceledException)
        {
            // 読み取りループの停止用キャンセル。テスト対象の例外ではない。
        }

        // ここまで例外なく到達すれば E-13 は満たされている（SQLITE_BUSY 等は例外として飛ぶ）。
        Assert.NotNull(await writer.GetCurrentAsync());
    }
}
