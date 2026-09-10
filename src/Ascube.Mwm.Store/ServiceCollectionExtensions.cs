using Ascube.Mwm.Abstractions;
using Ascube.Mwm.Store.Audit;
using Microsoft.Extensions.DependencyInjection;

namespace Ascube.Mwm.Store;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAscubeMwmStore(this IServiceCollection services, Action<MwmStoreOptions> configure)
    {
        var options = new MwmStoreOptions { DatabasePath = string.Empty, DeviceProfileId = string.Empty };
        configure(options);

        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            throw new InvalidOperationException("MwmStoreOptions.DatabasePath を設定してください。");
        }

        if (string.IsNullOrWhiteSpace(options.DeviceProfileId))
        {
            throw new InvalidOperationException("MwmStoreOptions.DeviceProfileId を設定してください。");
        }

        services.AddSingleton(options);
        services.AddSingleton<IWorklistWriter>(sp => new SqliteWorklistWriter(sp.GetRequiredService<MwmStoreOptions>()));
        services.AddSingleton<IWorklistRepository>(sp => new SqliteWorklistRepository(sp.GetRequiredService<MwmStoreOptions>()));

        return services;
    }

    /// <summary>
    /// 監査ログ（AuditCFind/AuditCFindItem）。ワークリストDBとは別ファイル（規則5。Audit/AuditStoreOptions.cs 参照）。
    /// <paramref name="asReaderOnly"/> が true なら <see cref="IAuditReader"/> のみ登録する（mwm-admin 向け）。
    /// false（既定）なら <see cref="IAuditWriter"/> も登録する（SCP 向け）。
    /// </summary>
    public static IServiceCollection AddAscubeMwmAudit(
        this IServiceCollection services, Action<AuditStoreOptions> configure, bool asReaderOnly = false)
    {
        var options = new AuditStoreOptions { DatabasePath = string.Empty };
        configure(options);

        if (string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            throw new InvalidOperationException("AuditStoreOptions.DatabasePath を設定してください。");
        }

        services.AddSingleton(options);
        services.AddSingleton<SqliteAuditStore>(sp => new SqliteAuditStore(sp.GetRequiredService<AuditStoreOptions>()));
        services.AddSingleton<IAuditReader>(sp => sp.GetRequiredService<SqliteAuditStore>());

        if (!asReaderOnly)
        {
            services.AddSingleton<IAuditWriter>(sp => sp.GetRequiredService<SqliteAuditStore>());
        }

        return services;
    }
}
