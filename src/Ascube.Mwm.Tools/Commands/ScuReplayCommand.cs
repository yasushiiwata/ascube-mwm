using Ascube.Mwm.Store.Audit;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using FellowOakDicom.Serialization;

namespace Ascube.Mwm.Tools.Commands;

/// <summary>
/// 実装指示書 v2 T11：<c>mwm-scu scu replay --from-audit &lt;id&gt;</c>。
/// AuditCFind.RequestJson（DICOM JSON Model）から装置の実クエリを復元して再送する。
/// </summary>
internal static class ScuReplayCommand
{
    public const string Usage =
        "使い方: mwm-scu scu replay --from-audit <runId> [--audit-db <path>] [--host <h>] [--port <p>] [--aet <calling>] [--aec <called>]";

    public static async Task<int> RunAsync(string[] args)
    {
        var conn = new ScuConnectionOptions();
        long? runId = null;
        var auditDbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm-audit.db");

        for (var i = 0; i < args.Length; i++)
        {
            if (conn.TryParse(args, ref i))
            {
                continue;
            }

            switch (args[i])
            {
                case "--from-audit" when i + 1 < args.Length:
                    runId = long.Parse(args[++i]);
                    break;
                case "--audit-db" when i + 1 < args.Length:
                    auditDbPath = args[++i];
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        if (runId is null)
        {
            Console.Error.WriteLine(Usage);
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
            Console.WriteLine($"NG: RunId={runId} が監査DBに見つかりません。");
            return 1;
        }

        DicomDataset queryDataset;
        try
        {
            queryDataset = DicomJson.ConvertJsonToDicom(record.RequestJson);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: RequestJson の復元に失敗しました: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"RunId={runId} の要求を再送します（元の要求: CalledAe={record.CalledAe}, CallingAe={record.CallingAe}）");
        Console.WriteLine($"送信先: {conn.Host}:{conn.Port}（aet={conn.CallingAe}, aec={conn.CalledAe}）");

        var client = DicomClientFactory.Create(conn.Host, conn.Port, false, conn.CallingAe, conn.CalledAe);
        var count = 0;
        DicomStatus? finalStatus = null;

        var request = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind) { Dataset = queryDataset };
        request.OnResponseReceived += (_, response) =>
        {
            if (response.Status == DicomStatus.Pending)
            {
                count++;
                Console.WriteLine($"--- Pending [{count}] ---");
                Console.WriteLine($"  PatientID: {response.Dataset!.GetSingleValueOrDefault(DicomTag.PatientID, "")}");
            }
            else
            {
                finalStatus = response.Status;
            }
        };

        try
        {
            await client.AddRequestAsync(request);
            await client.SendAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"件数: {count}, 最終ステータス: {finalStatus}");
        return finalStatus == DicomStatus.Success ? 0 : 1;
    }
}
