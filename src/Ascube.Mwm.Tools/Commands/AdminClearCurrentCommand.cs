using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Store;

namespace Ascube.Mwm.Tools.Commands;

/// <summary>
/// <c>mwm-admin admin clear-current</c>。<see cref="AdminSetCurrentCommand"/> の対。
/// <see cref="SqliteWorklistWriter.ClearCurrentAsync"/> を直接叩き、次の C-FIND を 0件+Success に戻す。
/// </summary>
internal static class AdminClearCurrentCommand
{
    public const string Usage = "使い方: mwm-admin admin clear-current --profile <id> [--db <path>] [--profiles-dir <dir>]";

    public static async Task<int> RunAsync(string[] args)
    {
        string? profileId = null;
        var profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "config", "profiles");
        var databasePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm.db");

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--profile" when i + 1 < args.Length:
                    profileId = args[++i];
                    break;
                case "--db" when i + 1 < args.Length:
                    databasePath = args[++i];
                    break;
                case "--profiles-dir" when i + 1 < args.Length:
                    profilesDir = args[++i];
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        if (profileId is null)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        var profileResult = ProfileLoader.LoadAndValidate(profileId, profilesDir);
        if (!profileResult.Validation.IsValid)
        {
            Console.Error.WriteLine($"エラー: プロファイル \"{profileId}\" の検証に失敗しました。");
            foreach (var issue in profileResult.Validation.Issues)
            {
                Console.Error.WriteLine($"  {issue.Path}: {issue.Message}");
            }

            return 1;
        }

        var writer = new SqliteWorklistWriter(new MwmStoreOptions { DatabasePath = databasePath, DeviceProfileId = profileId });
        await writer.ClearCurrentAsync();

        Console.WriteLine($"OK: ClearCurrentAsync 完了。次の C-FIND から 0件+Success になります。DB={databasePath}");
        return 0;
    }
}
