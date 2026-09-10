using System.Net.Sockets;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Store;
using Ascube.Mwm.Store.Audit;

namespace Ascube.Mwm.Tools.Commands;

/// <summary>
/// 実装指示書 v2 T11：<c>mwm-admin admin health</c>。待受状態／直近アソシエーション／プロファイル／
/// **現在の受診者の有無とTTL残**（T12の00_watch.ps1が真っ先に確認する項目）を表示する。
/// mwm-scp プロセスとは通信しない（HTTP等のヘルスエンドポイントを持たない）。
/// 待受確認はTCP接続プローブ、受診者・監査情報はDBファイルを直接読む。
/// </summary>
internal static class AdminHealthCommand
{
    public const string Usage =
        "使い方: mwm-admin admin health --profile <id> [--profiles-dir <dir>] [--db <path>] [--audit-db <path>]";

    public static async Task<int> RunAsync(string[] args)
    {
        string? profileId = null;
        var profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "config", "profiles");
        var databasePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm.db");
        var auditDbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm-audit.db");

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
                case "--db" when i + 1 < args.Length:
                    databasePath = args[++i];
                    break;
                case "--audit-db" when i + 1 < args.Length:
                    auditDbPath = args[++i];
                    break;
            }
        }

        if (profileId is null)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        var healthy = true;

        // --- プロファイル ---
        var profileResult = ProfileLoader.LoadAndValidate(profileId, profilesDir);
        if (profileResult.Validation.IsValid)
        {
            var profile = profileResult.Profile!;
            Console.WriteLine($"プロファイル   : {profileId}（{profile.SpecificCharacterSet}, AE={profile.AeTitle}, Port={profile.Port}）");

            // --- 待受状態（TCP プローブ） ---
            var listening = await IsPortListeningAsync(profile.Port);
            Console.WriteLine($"待受状態       : {(listening ? $"応答あり（ポート {profile.Port}）" : $"応答なし（ポート {profile.Port}）")}");
            healthy &= listening;
        }
        else
        {
            Console.WriteLine($"プロファイル   : {profileId} は検証に失敗しています（{profileResult.Validation.Issues.Count} 件）");
            healthy = false;
        }

        // --- 現在の受診者の有無とTTL残 ---
        if (File.Exists(databasePath))
        {
            var repository = new SqliteWorklistRepository(new MwmStoreOptions { DatabasePath = databasePath, DeviceProfileId = profileId });
            var status = await repository.GetCurrentStatusAsync();

            if (!status.Exists)
            {
                Console.WriteLine("現在の受診者   : なし");
            }
            else if (status.IsAlive)
            {
                var remaining = status.ExpiresAtUtc!.Value - DateTimeOffset.UtcNow;
                var remainingText = remaining > TimeSpan.Zero
                    ? $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}"
                    : "00:00";
                Console.WriteLine($"現在の受診者   : あり（TTL残 {remainingText}）");
            }
            else
            {
                Console.WriteLine("現在の受診者   : あり（ただしTTL切れ。次のC-FINDは0件になります）");
            }
        }
        else
        {
            Console.WriteLine($"現在の受診者   : 不明（ワークリストDBが見つかりません: {databasePath}）");
            healthy = false;
        }

        // --- 直近アソシエーション（監査ログ） ---
        if (File.Exists(auditDbPath))
        {
            var auditStore = new SqliteAuditStore(new AuditStoreOptions { DatabasePath = auditDbPath });
            var latest = await auditStore.GetLatestCFindAsync();

            Console.WriteLine(latest is null
                ? "直近のC-FIND  : 記録なし"
                : $"直近のC-FIND  : {latest.TimestampUtc:O}（RunId={latest.RunId}, 件数={latest.ResultCount}, Calling AE={latest.CallingAe}）");
        }
        else
        {
            Console.WriteLine($"直近のC-FIND  : 不明（監査DBが見つかりません: {auditDbPath}）");
        }

        Console.WriteLine();
        Console.WriteLine(healthy ? "OK" : "NG");
        return healthy ? 0 : 1;
    }

    private static async Task<bool> IsPortListeningAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(2)));
            return completed == connectTask && client.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
