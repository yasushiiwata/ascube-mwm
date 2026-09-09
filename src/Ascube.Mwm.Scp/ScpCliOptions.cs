namespace Ascube.Mwm.Scp;

/// <summary>
/// <c>mwm-scp</c> の起動引数。<c>Ascube.Mwm.Tools</c> の <c>--profile</c> / <c>--profiles-dir</c> と同じ流儀。
/// </summary>
public sealed class ScpCliOptions
{
    public required IReadOnlyList<string> ProfileIds { get; init; }

    public required string ProfilesDir { get; init; }

    public required bool Console { get; init; }

    public const string Usage = "使い方: mwm-scp [--console] --profile <id> [--profile <id> ...] [--profiles-dir <dir>]";

    public static ScpCliOptions? Parse(string[] args)
    {
        var profileIds = new List<string>();
        var profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "config", "profiles");
        var console = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--console":
                    console = true;
                    break;
                case "--profile" when i + 1 < args.Length:
                    profileIds.Add(args[++i]);
                    break;
                case "--profiles-dir" when i + 1 < args.Length:
                    profilesDir = args[++i];
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
            Console = console,
        };
    }
}
