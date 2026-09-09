using System.Linq;
using Ascube.Mwm.Core.Config;
using FellowOakDicom.Network;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ascube.Mwm.Scp;

/// <summary>
/// 検証済みプロファイル一覧からポートごとに <see cref="MwmDicomService"/> の DicomServer を起動する。
/// 同じポートを共有する複数プロファイルは1つの DicomServer にまとめ、Called AE Title で解決する。
/// </summary>
public sealed class ScpHostedService(
    IDicomServerFactory serverFactory,
    IReadOnlyList<DeviceProfile> profiles,
    ILogger<ScpHostedService> logger) : IHostedService
{
    private readonly List<IDicomServer> _servers = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var group in profiles.GroupBy(p => p.Port))
        {
            var routing = new ScpRoutingContext { Profiles = group.ToArray() };
            var server = serverFactory.Create<MwmDicomService>(group.Key, userState: routing, logger: logger);
            _servers.Add(server);

            logger.LogInformation(
                "SCP: ポート {Port} で待受を開始しました。対象プロファイル: {ProfileIds}",
                group.Key, string.Join(", ", group.Select(p => $"{p.Id}(AE={p.AeTitle})")));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var server in _servers)
        {
            server.Stop();
            server.Dispose();
        }

        _servers.Clear();
        return Task.CompletedTask;
    }
}
