using System.IO.Compression;
using System.Text.Json;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Store.Audit;

namespace Ascube.Mwm.Tools.Commands;

/// <summary>
/// 実装指示書 v2 T11：<c>mwm-admin admin export-support-bundle</c>。
/// 証跡パッケージ（プロファイル・直近の監査ログ・キャプチャファイル一覧）を1つのzipにまとめる。
/// 受診者データそのもの（ワークリストDB本体）やキャプチャの生バイトは含めない
/// （規則14：受診者データと通信記録をコミットしない、の精神を証跡パッケージにも適用する。
/// 生キャプチャが要る場合は captures/ から別途手動で取得すること）。
/// </summary>
internal static class AdminExportSupportBundleCommand
{
    public const string Usage =
        "使い方: mwm-admin admin export-support-bundle --profile <id> [--profiles-dir <dir>] " +
        "[--audit-db <path>] [--captures-dir <dir>] [--out <path.zip>] [--last-n-audits <n>]";

    public static async Task<int> RunAsync(string[] args)
    {
        string? profileId = null;
        var profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "config", "profiles");
        var auditDbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm-audit.db");
        var capturesDir = Path.Combine(Directory.GetCurrentDirectory(), "captures");
        var lastN = 50;
        string? outPath = null;

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
                case "--audit-db" when i + 1 < args.Length:
                    auditDbPath = args[++i];
                    break;
                case "--captures-dir" when i + 1 < args.Length:
                    capturesDir = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--last-n-audits" when i + 1 < args.Length:
                    lastN = int.Parse(args[++i]);
                    break;
            }
        }

        if (profileId is null)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        outPath ??= Path.Combine(Directory.GetCurrentDirectory(), $"support-bundle-{stamp}.zip");

        var workDir = Directory.CreateTempSubdirectory("ascube-mwm-support-bundle-").FullName;
        try
        {
            await WriteManifestAsync(workDir, profileId, stamp);
            CopyProfileFiles(workDir, profileId, profilesDir);
            await ExportRecentAuditsAsync(workDir, auditDbPath, lastN);
            WriteCapturesManifest(workDir, capturesDir);

            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }

            ZipFile.CreateFromDirectory(workDir, outPath);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }

        Console.WriteLine($"OK: 証跡パッケージを作成しました: {outPath}");
        return 0;
    }

    private static async Task WriteManifestAsync(string workDir, string profileId, string stamp)
    {
        var manifest = $"""
            ascube-mwm support bundle
            作成日時(UTC): {stamp}
            プロファイル : {profileId}
            """;
        await File.WriteAllTextAsync(Path.Combine(workDir, "manifest.txt"), manifest);
    }

    private static void CopyProfileFiles(string workDir, string profileId, string profilesDir)
    {
        var profilesOut = Path.Combine(workDir, "profiles");
        Directory.CreateDirectory(profilesOut);

        var baseFile = Path.Combine(profilesDir, "_base.jsonc");
        if (File.Exists(baseFile))
        {
            File.Copy(baseFile, Path.Combine(profilesOut, "_base.jsonc"));
        }

        var profileFile = Path.Combine(profilesDir, $"{profileId}.jsonc");
        if (File.Exists(profileFile))
        {
            File.Copy(profileFile, Path.Combine(profilesOut, $"{profileId}.jsonc"));
        }

        var validation = ProfileLoader.LoadAndValidate(profileId, profilesDir);
        var validationText = validation.Validation.IsValid
            ? "OK"
            : "NG:\n" + string.Join("\n", validation.Validation.Issues.Select(i => $"  {i.Path}: {i.Message}"));
        File.WriteAllText(Path.Combine(profilesOut, "validation-result.txt"), validationText);
    }

    private static async Task ExportRecentAuditsAsync(string workDir, string auditDbPath, int lastN)
    {
        if (!File.Exists(auditDbPath))
        {
            await File.WriteAllTextAsync(Path.Combine(workDir, "audit-recent.json"), $"監査DBが見つかりません: {auditDbPath}");
            return;
        }

        var store = new SqliteAuditStore(new AuditStoreOptions { DatabasePath = auditDbPath });
        var latest = await store.GetLatestCFindAsync();
        var records = new List<AuditCFindRecord>();

        if (latest is not null)
        {
            for (var runId = latest.RunId; runId > 0 && records.Count < lastN; runId--)
            {
                var record = await store.GetCFindAsync(runId);
                if (record is not null)
                {
                    records.Add(record);
                }
            }
        }

        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(workDir, "audit-recent.json"), json);
    }

    private static void WriteCapturesManifest(string workDir, string capturesDir)
    {
        var lines = new List<string> { $"captures dir: {capturesDir}" };

        if (Directory.Exists(capturesDir))
        {
            foreach (var file in Directory.GetFiles(capturesDir).OrderBy(f => f))
            {
                var info = new FileInfo(file);
                lines.Add($"{info.Name}\t{info.Length} bytes\t{info.LastWriteTimeUtc:O}");
            }
        }
        else
        {
            lines.Add("(ディレクトリが存在しません)");
        }

        File.WriteAllLines(Path.Combine(workDir, "captures-manifest.txt"), lines);
    }
}
