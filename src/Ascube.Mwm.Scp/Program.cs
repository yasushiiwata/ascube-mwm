using System.Text;
using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Core.Config;
using Ascube.Mwm.Scp;
using Ascube.Mwm.Scp.Capture;
using Ascube.Mwm.Store;
using FellowOakDicom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// 起動時に1回。呼ばないと CP932（半角カナ・JIS X 0201 等）が扱えない。
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var cli = ScpCliOptions.Parse(args);
if (cli is null)
{
    Console.Error.WriteLine(ScpCliOptions.Usage);
    return 2;
}

var profiles = new List<DeviceProfile>();
foreach (var profileId in cli.ProfileIds)
{
    var result = ProfileLoader.LoadAndValidate(profileId, cli.ProfilesDir);
    if (!result.Validation.IsValid)
    {
        Console.Error.WriteLine($"起動中止: プロファイル \"{profileId}\" の検証に失敗しました（{result.Validation.Issues.Count} 件）。");
        foreach (var issue in result.Validation.Issues)
        {
            Console.Error.WriteLine($"  {issue.Path}: {issue.Message}");
        }

        return 1;
    }

    profiles.Add(result.Profile!);
}

var builder = Host.CreateApplicationBuilder();

if (!cli.Console)
{
    // Windows サービスとして実行するとき。--console 時はコンソールアプリとして動かす（開発起動）。
    builder.Services.AddWindowsService(options => options.ServiceName = "AscubeMwm");
}

builder.Services.AddFellowOakDicom();

// T9：生バイト記録。AddFellowOakDicom() の直後に呼ぶこと（順序を逆にすると差し替わらない）。
// fo-dicom 本体は無改変（規則11）。出力先は .gitignore 済みの captures/（規則14）。
if (!cli.NoCapture)
{
    builder.Services.AddNetworkManager<RecordingNetworkManager>();
    builder.Services.AddSingleton(new RawCaptureOptions { OutputDirectory = cli.CapturesDir });
    builder.Services.AddSingleton<RawCaptureWriter>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<RawCaptureWriter>());
}

// 規則5：SCP は SQLite を読み取り専用でしか開かない。書き込む経路（IWorklistWriter）を
// DIコンテナに一切登録しないよう、Ascube.Mwm.Store.ServiceCollectionExtensions.AddAscubeMwmStore
// （Writer も一緒に登録してしまう）は使わず、IWorklistRepository だけを直接登録する。
var storeOptions = new MwmStoreOptions { DatabasePath = cli.DatabasePath, DeviceProfileId = cli.ProfileIds[0] };
builder.Services.AddSingleton(storeOptions);
builder.Services.AddSingleton<IWorklistRepository>(sp => new SqliteWorklistRepository(sp.GetRequiredService<MwmStoreOptions>()));

// T8：監査ログ（AuditCFind/AuditCFindItem）。ワークリストDBとは別ファイル（規則5。Store/Audit/AuditStoreOptions.cs 参照）。
builder.Services.AddAscubeMwmAudit(o => o.DatabasePath = cli.AuditDatabasePath);

// T10：ホットリロード＋自動ロールバック。config/profiles/ を監視し、成功時のみ差し替える。
// 進行中のアソシエーションはアソシエーション確立時に解決した参照を使い続けるため影響を受けない。
var profileRegistry = new LiveProfileRegistry(profiles);
builder.Services.AddSingleton(profileRegistry);
builder.Services.AddHostedService(sp => new ProfileHotReloadService(
    profileRegistry, cli.ProfileIds, cli.ProfilesDir, sp.GetRequiredService<ILogger<ProfileHotReloadService>>()));

builder.Services.AddHostedService<ScpHostedService>();

using var host = builder.Build();
await host.RunAsync();

return 0;
