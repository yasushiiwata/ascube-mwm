using System.Text.Json.Nodes;
using Ascube.Mwm.Core.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ascube.Mwm.Scp;

/// <summary>
/// T10：<c>config/profiles/</c> を <see cref="FileSystemWatcher"/> で監視し、500ms デバウンス後に
/// 対象プロファイルを再読込・再検証する。成功時のみ <see cref="LiveProfileRegistry"/> を差し替え、
/// <c>config/backup/&lt;id&gt;.&lt;timestamp&gt;.jsonc</c> に差し替え前の内容を自動バックアップする。
/// 失敗時は稼働中の設定を維持し（自動ロールバック）、エラーをログに出す。
/// </summary>
public sealed class ProfileHotReloadService : IHostedService, IDisposable
{
    private readonly LiveProfileRegistry _registry;
    private readonly IReadOnlyList<string> _profileIds;
    private readonly string _profilesDir;
    private readonly string _backupDir;
    private readonly ILogger<ProfileHotReloadService> _logger;
    private readonly Lock _gate = new();

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    public ProfileHotReloadService(
        LiveProfileRegistry registry,
        IReadOnlyList<string> profileIds,
        string profilesDir,
        ILogger<ProfileHotReloadService> logger)
    {
        _registry = registry;
        _profileIds = profileIds;
        _profilesDir = profilesDir;
        _backupDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profilesDir)) ?? profilesDir, "backup");
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_backupDir);

        _watcher = new FileSystemWatcher(_profilesDir, "*.jsonc")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("ProfileHotReloadService: {Dir} の監視を開始しました（500msデバウンス）", _profilesDir);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _watcher = null;

        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        return Task.CompletedTask;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => Reload(), null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }
    }

    private void Reload()
    {
        var result = _registry.TryReload(_profileIds, _profilesDir);

        if (!result.Success)
        {
            foreach (var (profileId, validation) in result.Issues)
            {
                _logger.LogError(
                    "ProfileHotReloadService: プロファイル \"{ProfileId}\" の検証に失敗したため、設定の差し替えを中止しました（稼働中の設定を維持）。{IssueCount} 件の問題: {Issues}",
                    profileId, validation.Issues.Count,
                    string.Join(" / ", validation.Issues.Select(i => $"{i.Path}: {i.Message}")));
            }

            return;
        }

        _logger.LogInformation("ProfileHotReloadService: プロファイルの再読込に成功しました。差し替えます。");

        foreach (var previous in result.Previous)
        {
            BackupBeforeReplace(previous);

            var applied = result.Applied!.FirstOrDefault(p => p.Id == previous.Id);
            if (applied is not null)
            {
                var diff = ProfileDiffFormatter.Format(previous, applied);
                if (diff.Count > 0)
                {
                    _logger.LogInformation(
                        "ProfileHotReloadService: プロファイル \"{ProfileId}\" の差分:\n{Diff}",
                        previous.Id, string.Join("\n", diff));
                }
            }
        }
    }

    private void BackupBeforeReplace(DeviceProfile previous)
    {
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(_backupDir, $"{previous.Id}.{stamp}.jsonc");
            var json = previous.Raw.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.WriteAllText(path, json);
            _logger.LogInformation("ProfileHotReloadService: 差し替え前の設定をバックアップしました: {Path}", path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "ProfileHotReloadService: バックアップの書き込みに失敗しました（プロファイル \"{ProfileId}\"）", previous.Id);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        lock (_gate)
        {
            _debounceTimer?.Dispose();
        }
    }
}
