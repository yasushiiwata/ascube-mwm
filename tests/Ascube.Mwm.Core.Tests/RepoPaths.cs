namespace Ascube.Mwm.Core.Tests;

/// <summary>テスト実行ディレクトリ（bin/配下）からリポジトリルートを逆算する。</summary>
internal static class RepoPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string ProfilesDir => Path.Combine(RepoRoot, "config", "profiles");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ascube.Mwm.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("リポジトリルート（Ascube.Mwm.slnx）が見つかりません");
    }
}
