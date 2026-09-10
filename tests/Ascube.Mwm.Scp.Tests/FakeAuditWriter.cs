using Ascube.Mwm.Store.Audit;

namespace Ascube.Mwm.Scp.Tests;

/// <summary>インメモリの監査ログ書き込み口。T8 のテストで記録内容を検証するために使う。</summary>
internal sealed class FakeAuditWriter : IAuditWriter
{
    private long _nextRunId = 1;

    public List<AuditCFindRecord> Records { get; } = [];

    public Task<long> RecordCFindAsync(AuditCFindRecord record, CancellationToken ct = default)
    {
        var runId = _nextRunId++;
        Records.Add(record with { RunId = runId });
        return Task.FromResult(runId);
    }
}
