using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Core.Dataset;
using Ascube.Mwm.Store;
using FellowOakDicom;

namespace Ascube.Mwm.Tools.Commands;

/// <summary>
/// 実装指示書 v2 T11：<c>mwm-scu scu preview</c>。装置なしで生成データセットを
/// dcmdump 形式＋16進で表示する（規則19・T7受入条件：判定は必ず16進で行う。コンソールはCP932）。
/// </summary>
internal static class ScuPreviewCommand
{
    public const string Usage =
        "使い方: mwm-scu scu preview --profile <id> --patient <StablePatientId> " +
        "[--profiles-dir <dir>] [--db <path>]";

    public static async Task<int> RunAsync(string[] args)
    {
        string? profileId = null;
        string? patientId = null;
        var profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "config", "profiles");
        var databasePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm.db");

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--profile" when i + 1 < args.Length:
                    profileId = args[++i];
                    break;
                case "--patient" when i + 1 < args.Length:
                    patientId = args[++i];
                    break;
                case "--profiles-dir" when i + 1 < args.Length:
                    profilesDir = args[++i];
                    break;
                case "--db" when i + 1 < args.Length:
                    databasePath = args[++i];
                    break;
            }
        }

        if (profileId is null || patientId is null)
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

        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"エラー: ワークリストDBが見つかりません: {databasePath}");
            return 2;
        }

        var repository = new SqliteWorklistRepository(new MwmStoreOptions { DatabasePath = databasePath, DeviceProfileId = profileId });
        var item = await repository.GetLatestByPatientIdAsync(patientId);
        if (item is null)
        {
            Console.WriteLine($"NG: PatientID \"{patientId}\" の受診履歴が見つかりません（{databasePath}）。");
            return 1;
        }

        var built = DatasetBuilder.Build(profileResult.Profile!, item, new DicomDataset());
        if (built.Suppressed)
        {
            Console.WriteLine($"NG: データセット生成が Suppressed になりました: {built.SuppressedReason}");
            return 1;
        }

        Console.WriteLine($"# プロファイル: {profileId}（{profileResult.Profile!.SpecificCharacterSet}）");
        Console.WriteLine($"# PatientID: {patientId}, WorkItemId: {item.WorkItemId}");
        Console.WriteLine();
        Console.WriteLine("# dcmdump 形式（タグ / VR / 値。来歴つき）:");
        PrintDataset(built.Dataset!, built.Provenance, indent: "");

        // dcmdump 等の他ツールで開けるように、C-FIND 応答には無い SOPClassUID/SOPInstanceUID を
        // プレビュー専用に補って .dcm ファイルとして書き出す（ワイヤ上のP-DATA-TFにはこの2つは乗らない。
        // あくまで確認用の副産物）。実際にシリアライズされたバイト列を16進表示する
        // （DicomDataset の内部バッファを直接読むと符号化前の値になるため、規則19どおり
        // 必ず書き出し後のバイト列を検査する）。
        var fileDataset = built.Dataset!;
        if (!fileDataset.Contains(DicomTag.SOPClassUID))
        {
            fileDataset.Add(DicomTag.SOPClassUID, DicomUID.ModalityWorklistInformationModelFind.UID);
        }

        if (!fileDataset.Contains(DicomTag.SOPInstanceUID))
        {
            fileDataset.Add(DicomTag.SOPInstanceUID, "1.2.840.10008.5.1.4.31.999.1"); // プレビュー専用の仮UID
        }

        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "captures");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"preview-{profileId}-{patientId}.dcm");
        await new DicomFile(fileDataset).SaveAsync(outputPath);

        Console.WriteLine();
        Console.WriteLine("# 生バイト（16進。実際にシリアライズされたファイルそのもの。判定はこちらで行うこと。コンソール表示のCP932化けでは判定しない）:");
        HexDump.Print(await File.ReadAllBytesAsync(outputPath));
        Console.WriteLine();
        Console.WriteLine($"# dcmdump 等の他ツールで確認する場合はこちら（プレビュー専用の仮UIDを補ったファイル）: {outputPath}");

        return 0;
    }

    private static void PrintDataset(DicomDataset dataset, IReadOnlyDictionary<string, string> provenance, string indent)
    {
        foreach (var element in dataset)
        {
            if (element is DicomSequence sq)
            {
                Console.WriteLine($"{indent}{element.Tag} SQ ({sq.Items.Count} item(s))");
                for (var i = 0; i < sq.Items.Count; i++)
                {
                    Console.WriteLine($"{indent}  [{i}]");
                    PrintDataset(sq.Items[i], provenance, indent + "    ");
                }

                continue;
            }

            var value = TryGetValue(dataset, element.Tag);
            var tagKey = $"({element.Tag.Group:X4},{element.Tag.Element:X4})";
            var source = provenance.TryGetValue(tagKey, out var s) ? $"  <- {s}" : "";
            Console.WriteLine($"{indent}{element.Tag} {element.ValueRepresentation.Code} [{value}]{source}");
        }
    }

    private static string TryGetValue(DicomDataset dataset, DicomTag tag)
    {
        try
        {
            return string.Join("\\", dataset.GetValues<string>(tag));
        }
        catch (Exception)
        {
            return "(バイナリ値)";
        }
    }
}
