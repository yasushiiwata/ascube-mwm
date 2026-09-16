using System.Globalization;
using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Store;

namespace Ascube.Mwm.Tools.Commands;

/// <summary>
/// <c>mwm-admin admin set-current</c>。BRIDGE-Navi との結線がまだ済んでいない・
/// 現地で不調なときに、SCP とダミー装置の間だけで疎通を証明するための開発用コマンド
/// （docs/連携テスト手順.md §2）。<see cref="SqliteWorklistWriter.SetCurrentAsync"/> を
/// CLI から直接叩くだけの薄いラッパーで、BRIDGE-Navi 本体が実運用でやるのと同じ経路を通る。
/// </summary>
internal static class AdminSetCurrentCommand
{
    public const string Usage =
        "使い方: mwm-admin admin set-current --profile <id> --patient-id <個人番号12桁> --scheduled-date <today|YYYYMMDD> " +
        "[--db <path>] [--profiles-dir <dir>] " +
        "[--family-kanji <>] [--given-kanji <>] [--family-kana <>] [--given-kana <>] " +
        "[--birth-date <YYYYMMDD>] [--sex M|F|O] " +
        "[--accession <>] [--procedure-id <>] [--procedure-desc <>] " +
        "[--height-cm <数値>] [--weight-kg <数値>] [--source-message-id <>]";

    public static async Task<int> RunAsync(string[] args)
    {
        string? profileId = null;
        string? patientId = null;
        string? scheduledDate = null;
        var profilesDir = Path.Combine(Directory.GetCurrentDirectory(), "config", "profiles");
        var databasePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "mwm.db");
        string? familyKanji = null;
        string? givenKanji = null;
        string? familyKana = null;
        string? givenKana = null;
        string? birthDate = null;
        var sex = Sex.Unknown;
        string? accession = null;
        string? procedureId = null;
        string? procedureDesc = null;
        string? sourceMessageId = null;
        double? heightCm = null;
        double? weightKg = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--profile" when i + 1 < args.Length:
                    profileId = args[++i];
                    break;
                case "--patient-id" when i + 1 < args.Length:
                    patientId = args[++i];
                    break;
                case "--scheduled-date" when i + 1 < args.Length:
                    scheduledDate = args[++i];
                    break;
                case "--db" when i + 1 < args.Length:
                    databasePath = args[++i];
                    break;
                case "--profiles-dir" when i + 1 < args.Length:
                    profilesDir = args[++i];
                    break;
                case "--family-kanji" when i + 1 < args.Length:
                    familyKanji = args[++i];
                    break;
                case "--given-kanji" when i + 1 < args.Length:
                    givenKanji = args[++i];
                    break;
                case "--family-kana" when i + 1 < args.Length:
                    familyKana = args[++i];
                    break;
                case "--given-kana" when i + 1 < args.Length:
                    givenKana = args[++i];
                    break;
                case "--birth-date" when i + 1 < args.Length:
                    birthDate = args[++i];
                    break;
                case "--sex" when i + 1 < args.Length:
                    sex = args[++i].ToUpperInvariant() switch
                    {
                        "M" => Sex.Male,
                        "F" => Sex.Female,
                        "O" => Sex.Other,
                        var other => throw new ArgumentException($"--sex は M/F/O のいずれかです: {other}"),
                    };
                    break;
                case "--accession" when i + 1 < args.Length:
                    accession = args[++i];
                    break;
                case "--procedure-id" when i + 1 < args.Length:
                    procedureId = args[++i];
                    break;
                case "--procedure-desc" when i + 1 < args.Length:
                    procedureDesc = args[++i];
                    break;
                case "--height-cm" when i + 1 < args.Length:
                    heightCm = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--weight-kg" when i + 1 < args.Length:
                    weightKg = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--source-message-id" when i + 1 < args.Length:
                    sourceMessageId = args[++i];
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        if (profileId is null || patientId is null || scheduledDate is null)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        // 規則18：項番3（個人番号・12桁）と項番4（受診番号・5桁）の取り違えは1年後まで発覚しない。
        // ここで機械的に検知できる範囲（桁数・数字か）だけでも警告しておく。
        if (patientId.Length != 12 || !patientId.All(char.IsAsciiDigit))
        {
            Console.Error.WriteLine(
                $"警告: --patient-id \"{patientId}\" は12桁の数字ではありません。" +
                "依頼電文の項番3（個人番号）ではなく項番4（受診番号）を渡していないか確認してください（規則18）。");
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

        var resolvedDate = scheduledDate == "today" ? DateTime.Today.ToString("yyyyMMdd") : scheduledDate;

        var entry = new WorklistEntry
        {
            StablePatientId = patientId,
            FamilyNameKanji = familyKanji,
            GivenNameKanji = givenKanji,
            FamilyNameKana = familyKana,
            GivenNameKana = givenKana,
            BirthDate = birthDate,
            Sex = sex,
            ScheduledDate = resolvedDate,
            AccessionNumber = accession,
            RequestedProcedureId = procedureId,
            RequestedProcedureDesc = procedureDesc,
            PatientHeightCm = heightCm,
            PatientWeightKg = weightKg,
            SourceMessageId = sourceMessageId,
        };

        // プロファイルの visibility.currentTtlMinutes を実際のTTLに反映する。
        // BRIDGE-Navi 本体は Ascube.Mwm.Core を参照できない（プロファイルを読めない）ため、
        // 実運用ではBRIDGE-Navi側で個別に CurrentTtl を設定する必要があるが、
        // プロファイルを読み込み済みのこのCLIでは設定ファイルの値をそのまま使う。
        var writer = new SqliteWorklistWriter(new MwmStoreOptions
        {
            DatabasePath = databasePath,
            DeviceProfileId = profileId,
            CurrentTtl = TimeSpan.FromMinutes(profileResult.Profile!.CurrentTtlMinutes),
        });
        await writer.SetCurrentAsync(entry);

        Console.WriteLine($"OK: SetCurrentAsync 完了。PatientID={entry.StablePatientId}, ScheduledDate={entry.ScheduledDate}, Profile={profileId}, DB={databasePath}");
        Console.WriteLine("次の C-FIND から反映されます（admin health / scripts\\04_find.ps1 で確認）。");
        return 0;
    }
}
