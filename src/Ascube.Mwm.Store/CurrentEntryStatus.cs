namespace Ascube.Mwm.Store;

/// <summary><c>admin health</c>（T11）用：CurrentEntry の有無とTTLだけを見る軽量な状態。</summary>
public sealed record CurrentEntryStatus(bool Exists, bool IsAlive, DateTimeOffset? SetAtUtc, DateTimeOffset? ExpiresAtUtc);
