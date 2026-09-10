using Ascube.Mwm.Core.Config;

namespace Ascube.Mwm.Tools.Commands;

internal static class AdminValidateCommand
{
    public const string Usage = "使い方: mwm-admin admin validate --profile <id> [--profiles-dir <dir>]";

    public static int Run(string[] args)
    {
        string? profileId = null;
        var profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "config", "profiles");

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--profile" when i + 1 < args.Length:
                    profileId = args[++i];
                    break;
                case "--profiles-dir" when i + 1 < args.Length:
                    profilesDir = args[++i];
                    break;
            }
        }

        if (profileId is null)
        {
            Console.Error.WriteLine("エラー: --profile <id> が必要です");
            return 2;
        }

        var result = ProfileLoader.LoadAndValidate(profileId, profilesDir);
        if (result.Validation.IsValid)
        {
            Console.WriteLine($"OK: プロファイル \"{profileId}\" は正常です。");
            return 0;
        }

        Console.WriteLine($"NG: プロファイル \"{profileId}\" の検証に失敗しました（{result.Validation.Issues.Count} 件）。");
        foreach (var issue in result.Validation.Issues)
        {
            Console.WriteLine($"  {issue.Path}: {issue.Message}");
        }

        return 1;
    }
}
