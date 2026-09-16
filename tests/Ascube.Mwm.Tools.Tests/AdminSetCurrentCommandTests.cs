using Ascube.Mwm.Store;
using Ascube.Mwm.Tools.Commands;

namespace Ascube.Mwm.Tools.Tests;

/// <summary>
/// docs/連携テスト手順.md §2 が指摘する「受診者を投入する手段が無い問題」への対応。
/// <c>admin set-current</c>/<c>admin clear-current</c> は <see cref="SqliteWorklistWriter"/> の薄いCLIラッパーであり、
/// 中身（1回だけのUID採番・A→B切り替え・Clear後0件）は Store.Tests / PatientContinuityTests で既に固定済みのため、
/// ここではCLI層固有の受入条件（引数の必須チェック・プロファイル検証・実際にDBへ反映されること）だけを確認する。
/// </summary>
public class AdminSetCurrentCommandTests
{
    private static string NewDbPath()
    {
        var dir = Directory.CreateTempSubdirectory("ascube-mwm-tools-tests-");
        return Path.Combine(dir.FullName, "mwm.db");
    }

    private static string[] BaseSetArgs(string db, string patientId = "000012345678", string scheduledDate = "20260916") =>
    [
        "--profile", "BMD_HOLOGIC",
        "--profiles-dir", RepoPaths.ProfilesDir,
        "--db", db,
        "--patient-id", patientId,
        "--scheduled-date", scheduledDate,
        "--family-kanji", "武田",
        "--given-kanji", "太郎",
    ];

    [Fact]
    public async Task SetCurrent_ThenGetCurrent_ReflectsTheGivenValues()
    {
        var db = NewDbPath();

        var exitCode = await AdminSetCurrentCommand.RunAsync(BaseSetArgs(db));

        Assert.Equal(0, exitCode);
        var reader = new SqliteWorklistWriter(new MwmStoreOptions { DatabasePath = db, DeviceProfileId = "BMD_HOLOGIC" });
        var current = await reader.GetCurrentAsync();
        Assert.NotNull(current);
        Assert.Equal("000012345678", current!.StablePatientId);
        Assert.Equal("武田", current.FamilyNameKanji);
        Assert.Equal("20260916", current.ScheduledDate);
    }

    [Fact]
    public async Task SetCurrent_MissingRequiredArgs_ReturnsUsageError()
    {
        var exitCode = await AdminSetCurrentCommand.RunAsync(["--profile", "BMD_HOLOGIC"]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task SetCurrent_UnknownProfile_ReturnsErrorWithoutWriting()
    {
        var db = NewDbPath();
        var args = BaseSetArgs(db);
        args[1] = "NO_SUCH_PROFILE";

        var exitCode = await AdminSetCurrentCommand.RunAsync(args);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(db));
    }

    [Fact]
    public async Task SetCurrent_CalledTwiceForSamePatientAndDate_KeepsSameStudyInstanceUid()
    {
        var db = NewDbPath();
        await AdminSetCurrentCommand.RunAsync(BaseSetArgs(db));
        var repository = new SqliteWorklistRepository(new MwmStoreOptions { DatabasePath = db, DeviceProfileId = "BMD_HOLOGIC" });
        var first = await repository.GetLatestByPatientIdAsync("000012345678");

        await AdminSetCurrentCommand.RunAsync(BaseSetArgs(db));
        var second = await repository.GetLatestByPatientIdAsync("000012345678");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.WorkItemId, second!.WorkItemId);
    }

    [Fact]
    public async Task ClearCurrent_AfterSetCurrent_RemovesCurrentEntry()
    {
        var db = NewDbPath();
        await AdminSetCurrentCommand.RunAsync(BaseSetArgs(db));

        var exitCode = await AdminClearCurrentCommand.RunAsync(
            ["--profile", "BMD_HOLOGIC", "--profiles-dir", RepoPaths.ProfilesDir, "--db", db]);

        Assert.Equal(0, exitCode);
        var reader = new SqliteWorklistWriter(new MwmStoreOptions { DatabasePath = db, DeviceProfileId = "BMD_HOLOGIC" });
        Assert.Null(await reader.GetCurrentAsync());
    }

    [Fact]
    public async Task ClearCurrent_MissingProfile_ReturnsUsageError()
    {
        var exitCode = await AdminClearCurrentCommand.RunAsync([]);

        Assert.Equal(2, exitCode);
    }

    /// <summary>
    /// docs/連携テスト手順.md S3-4「visibility.currentTtlMinutes を書き換えて短縮TTLを試す」の前提。
    /// プロファイルの currentTtlMinutes が実際の TTL 計算に反映されていないと、この手順は動かない。
    /// </summary>
    [Fact]
    public async Task SetCurrent_UsesProfilesCurrentTtlMinutes_NotTheHardcodedDefault()
    {
        var profilesDir = Directory.CreateTempSubdirectory("ascube-mwm-tools-tests-profiles-").FullName;
        File.Copy(
            Path.Combine(RepoPaths.ProfilesDir, "_base.jsonc"),
            Path.Combine(profilesDir, "_base.jsonc"));
        await File.WriteAllTextAsync(Path.Combine(profilesDir, "TEST_TTL.jsonc"), """
            {
              "schemaVersion": 1,
              "id": "TEST_TTL",
              "network": { "aeTitle": "TEST_TTL_AE" },
              "visibility": { "currentTtlMinutes": 1000 },
              "charset": {
                "specificCharacterSet": "ISO_IR 192",
                "patientName": { "group1": "kanaFull", "group2": "kanji", "group3": "kanaFull" }
              }
            }
            """);
        var db = NewDbPath();
        var args = BaseSetArgs(db);
        args[1] = "TEST_TTL";
        args[3] = profilesDir;

        var exitCode = await AdminSetCurrentCommand.RunAsync(args);

        Assert.Equal(0, exitCode);
        var repository = new SqliteWorklistRepository(new MwmStoreOptions { DatabasePath = db, DeviceProfileId = "TEST_TTL" });
        var status = await repository.GetCurrentStatusAsync();
        Assert.True(status.Exists);
        // 既定15分なら1時間も残らない。プロファイル通り1000分が使われていれば900分以上残っているはず。
        var remaining = status.ExpiresAtUtc!.Value - DateTimeOffset.UtcNow;
        Assert.True(remaining > TimeSpan.FromMinutes(900), $"TTL残が短すぎる（{remaining}）。プロファイルのcurrentTtlMinutesが使われていない疑い。");
    }
}
