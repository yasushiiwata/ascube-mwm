using Ascube.Mwm.Tools.HardClose;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Ascube.Mwm.Tools.Commands;

/// <summary>
/// 実装指示書 v2 T11：<c>mwm-scu scu find</c>。全マッチングキー指定可、
/// <c>--emulate-apex-defaults</c>（Days Back 60 / Forward 2 を再現）、
/// <c>--hard-close-after N --close-mode fin|rst</c>（N件受信後にA-RELEASEを送らずTCPを閉じる）に対応する。
/// </summary>
internal static class ScuFindCommand
{
    public const string Usage =
        "使い方: mwm-scu scu find [--host <h>] [--port <p>] [--aet <calling>] [--aec <called>] " +
        "[--date today|<YYYYMMDD>|<YYYYMMDD-YYYYMMDD>] [--patient-id <id>] [--patient-name <name>] [--modality <m>] " +
        "[--emulate-apex-defaults] [--hard-close-after <n> --close-mode fin|rst]";

    public static async Task<int> RunAsync(string[] args)
    {
        var conn = new ScuConnectionOptions();
        string? date = null;
        string? patientId = null;
        string? patientName = null;
        string? modality = null;
        var emulateApexDefaults = false;
        int? hardCloseAfter = null;
        var closeMode = HardCloseMode.Fin;

        for (var i = 0; i < args.Length; i++)
        {
            if (conn.TryParse(args, ref i))
            {
                continue;
            }

            switch (args[i])
            {
                case "--date" when i + 1 < args.Length:
                    date = args[++i];
                    break;
                case "--patient-id" when i + 1 < args.Length:
                    patientId = args[++i];
                    break;
                case "--patient-name" when i + 1 < args.Length:
                    patientName = args[++i];
                    break;
                case "--modality" when i + 1 < args.Length:
                    modality = args[++i];
                    break;
                case "--emulate-apex-defaults":
                    emulateApexDefaults = true;
                    break;
                case "--hard-close-after" when i + 1 < args.Length:
                    hardCloseAfter = int.Parse(args[++i]);
                    break;
                case "--close-mode" when i + 1 < args.Length:
                    closeMode = args[++i] switch
                    {
                        "fin" => HardCloseMode.Fin,
                        "rst" => HardCloseMode.Rst,
                        var other => throw new ArgumentException($"--close-mode は fin か rst のいずれかです: {other}"),
                    };
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        if (emulateApexDefaults)
        {
            var today = DateTime.Today;
            date = $"{today.AddDays(-60):yyyyMMdd}-{today.AddDays(2):yyyyMMdd}";
            Console.WriteLine($"--emulate-apex-defaults: Days Back 60 / Forward 2 を再現します（{date}）");
        }
        else if (date == "today")
        {
            date = DateTime.Today.ToString("yyyyMMdd");
        }

        var queryDataset = new DicomDataset { { DicomTag.PatientID, patientId ?? "" }, { DicomTag.PatientName, patientName ?? "" } };
        var sps = new DicomDataset
        {
            { DicomTag.ScheduledProcedureStepStartDate, date ?? "" },
            { DicomTag.Modality, modality ?? "" },
        };
        queryDataset.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps));

        if (hardCloseAfter is { } n)
        {
            return await RunHardCloseAsync(conn, queryDataset, n, closeMode);
        }

        return await RunNormalAsync(conn, queryDataset);
    }

    private static async Task<int> RunNormalAsync(ScuConnectionOptions conn, DicomDataset queryDataset)
    {
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
                PrintDataset(response.Dataset!);
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

    /// <summary>
    /// N件受信後、A-RELEASEを送らずTCPを閉じる（DicomClientでは実現できないため
    /// SocketCapturingNetworkManagerで生のSocketを捕まえる。実装指示書 v2 T11の警告どおり）。
    /// </summary>
    private static async Task<int> RunHardCloseAsync(ScuConnectionOptions conn, DicomDataset queryDataset, int n, HardCloseMode mode)
    {
        using var factory = new HardCloseDicomClientFactory(conn.Host, conn.Port, conn.CallingAe, conn.CalledAe);
        var count = 0;
        var closed = false;

        var request = new DicomCFindRequest(DicomUID.ModalityWorklistInformationModelFind) { Dataset = queryDataset };
        request.OnResponseReceived += (_, response) =>
        {
            if (response.Status != DicomStatus.Pending)
            {
                return;
            }

            count++;
            Console.WriteLine($"--- Pending [{count}] ---");
            PrintDataset(response.Dataset!);

            if (count >= n && !closed)
            {
                closed = true;
                Console.WriteLine($"{count}件受信。A-RELEASEを送らずTCPを閉じます（close-mode={mode}）。");
                factory.NetworkManager.HardClose(mode);
            }
        };

        try
        {
            await factory.Client.AddRequestAsync(request);
            await factory.Client.SendAsync();
        }
        catch (Exception ex) when (closed)
        {
            // 強制切断済みなので、その後の送受信で例外が出るのは想定どおり（目的はSCP側の挙動確認）。
            Console.WriteLine($"（強制切断後の例外。想定どおり）: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }

        Console.WriteLine(closed
            ? $"OK: {count}件受信後に強制切断しました（close-mode={mode}）。"
            : $"NG: {n}件に到達する前に応答が終わりました（受信件数={count}）。");
        return closed ? 0 : 1;
    }

    private static void PrintDataset(DicomDataset dataset)
    {
        foreach (var item in dataset)
        {
            Console.WriteLine($"  {item.Tag} {item.ValueRepresentation.Code} {DescribeValue(dataset, item)}");
        }
    }

    private static string DescribeValue(DicomDataset dataset, DicomItem item)
    {
        if (item is DicomSequence sq)
        {
            return $"(Sequence, {sq.Items.Count} item(s))";
        }

        try
        {
            return string.Join("\\", dataset.GetValues<string>(item.Tag));
        }
        catch (Exception)
        {
            return "(バイナリ値)";
        }
    }
}
