using Ascube.Mwm.Abstractions;
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
}
