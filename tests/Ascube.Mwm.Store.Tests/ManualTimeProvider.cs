namespace Ascube.Mwm.Store.Tests;

/// <summary>TTL 判定を「時刻を注入してテストする」ための手動進行 TimeProvider。</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
