namespace Ascube.Mwm.Scp;

/// <summary>
/// <c>mwm-scp</c> の起動引数。<c>Ascube.Mwm.Tools</c> の <c>--profile</c> / <c>--profiles-dir</c> と同じ流儀。
/// </summary>
public sealed class ScpCliOptions
{
    public required IReadOnlyList<string> ProfileIds { get; init; }

    public required string ProfilesDir { get; init; }

    /// <summary>ワークリストDB（SQLite）のパス。SCP は規則5により読み取り専用でしか開かない。</summary>
    public required string DatabasePath { get; init; }

    /// <summary>監査ログDB（AuditCFind/AuditCFindItem）のパス。ワークリストDBとは別ファイル（T8・規則5）。</summary>
    public required string AuditDatabasePath { get; init; }

    /// <summary>生バイト記録（T9）の出力先ディレクトリ。.gitignore 済みの captures/ が既定（規則14）。</summary>
    public required string CapturesDir { get; init; }

    /// <summary>true なら生バイト記録（T9）を無効化する（開発時のディスク節約用）。</summary>
    public required bool NoCapture { get; init; }

    public required bool Console { get; init; }

    public const string Usage =
        "使い方: mwm-scp [--console] --profile <id> [--profile <id> ...] [--profiles-dir <dir>] " +
        "[--db <path>] [--audit-db <path>] [--captures-dir <dir>] [--no-capture]";

    public static ScpCliOptions? Parse(string[] args)
    {
        var profileIds = new List<string>();
        var profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "config", "profiles");
        var databasePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm.db");
        var auditDatabasePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm-audit.db");
        var capturesDir = Path.Combine(Directory.GetCurrentDirectory(), "captures");
        var console = false;
        var noCapture = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--console":
                    console = true;
                    break;
                case "--no-capture":
                    noCapture = true;
                    break;
                case "--profile" when i + 1 < args.Length:
                    profileIds.Add(args[++i]);
                    break;
                case "--profiles-dir" when i + 1 < args.Length:
                    profilesDir = args[++i];
                    break;
                case "--db" when i + 1 < args.Length:
                    databasePath = args[++i];
                    break;
                case "--audit-db" when i + 1 < args.Length:
                    auditDatabasePath = args[++i];
                    break;
                case "--captures-dir" when i + 1 < args.Length:
                    capturesDir = args[++i];
                    break;
                default:
                    return null;
            }
        }

        if (profileIds.Count == 0)
        {
            return null;
        }

        return new ScpCliOptions
        {
            ProfileIds = profileIds,
            ProfilesDir = profilesDir,
            DatabasePath = databasePath,
            AuditDatabasePath = auditDatabasePath,
            CapturesDir = capturesDir,
            NoCapture = noCapture,
            Console = console,
        };
    }
}
