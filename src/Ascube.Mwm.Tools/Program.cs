using System.Text;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Store.Audit;

// 起動時に1回。呼ばないと CP932（半角カナ・JIS X 0201 等）が扱えない。
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args is ["admin", "validate", ..])
    {
        return RunAdminValidate(args[2..]);
    }

    if (args is ["admin", "explain-query", ..])
    {
        return await RunAdminExplainQueryAsync(args[2..]);
    }

    Console.Error.WriteLine("使い方: mwm-admin admin validate --profile <id> [--profiles-dir <dir>]");
    Console.Error.WriteLine("      : mwm-admin admin explain-query --run <id> [--audit-db <path>]");
    return 2;
}

static int RunAdminValidate(string[] args)
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

static async Task<int> RunAdminExplainQueryAsync(string[] args)
{
    long? runId = null;
    var auditDbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm-audit.db");

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--run" when i + 1 < args.Length:
                if (long.TryParse(args[++i], out var parsed))
                {
                    runId = parsed;
                }

                break;
            case "--audit-db" when i + 1 < args.Length:
                auditDbPath = args[++i];
                break;
        }
    }

    if (runId is null)
    {
        Console.Error.WriteLine("エラー: --run <id>（数値の RunId）が必要です");
        return 2;
    }

    if (!File.Exists(auditDbPath))
    {
        Console.Error.WriteLine($"エラー: 監査DBが見つかりません: {auditDbPath}");
        return 2;
    }

    var store = new SqliteAuditStore(new AuditStoreOptions { DatabasePath = auditDbPath });
    var record = await store.GetCFindAsync(runId.Value);

    if (record is null)
    {
        Console.WriteLine($"RunId={runId} は監査DBに見つかりません（{auditDbPath}）。");
        return 1;
    }

    Console.WriteLine($"RunId       : {record.RunId}");
    Console.WriteLine($"日時(UTC)   : {record.TimestampUtc:O}");
    Console.WriteLine($"プロファイル: {record.ProfileId}");
    Console.WriteLine($"Called AE   : {record.CalledAe}");
    Console.WriteLine($"Calling AE  : {record.CallingAe}");
    Console.WriteLine($"Status      : {record.Status}{(record.PeerAborted ? "（応答送信中に相手が切断）" : "")}");
    Console.WriteLine($"所要時間    : {record.DurationMs} ms");
    Console.WriteLine($"件数        : {record.ResultCount}");
    Console.WriteLine($"要求条件    : {record.CriteriaJson}");
    Console.WriteLine();

    if (record.ResultCount > 0)
    {
        Console.WriteLine($"○ {record.ResultCount} 件返しました。");
    }
    else
    {
        Console.WriteLine("● 0件でした。理由：");
        Console.WriteLine($"  {record.Explain ?? "（記録されていません）"}");
    }

    if (record.Items.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("候補の内訳：");
        foreach (var item in record.Items)
        {
            if (item.SuppressedReason is not null)
            {
                Console.WriteLine($"  [{item.ItemIndex}] Suppressed: {item.SuppressedReason}");
            }
            else
            {
                Console.WriteLine($"  [{item.ItemIndex}] 返却。来歴: {item.ProvenanceJson}");
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine("要求データセット（DICOM JSON Model・再生用）：");
    Console.WriteLine(record.RequestJson);

    return 0;
}
