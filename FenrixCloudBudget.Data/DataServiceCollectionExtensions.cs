using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FenrixCloudBudget.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core context (via IDbContextFactory so Blazor components can
    /// create short-lived contexts) and the IDataProvider matching the chosen mode.
    /// </summary>
    public static IServiceCollection AddFenrixData(this IServiceCollection services, DataProviderOptions options)
    {
        services.AddSingleton(options);
        services.AddDbContextFactory<AppDbContext>(b => DbContextFactoryBuilder.Configure(b, options));

        services.AddSingleton<IDataProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
            return options.Mode switch
            {
                DataProviderMode.SqlServer => new SqlServerDataProvider(factory, DataProviderMode.SqlServer),
                DataProviderMode.CloudSql  => new SqlServerDataProvider(factory, DataProviderMode.CloudSql),
                DataProviderMode.Saas      => new SaaSDataProvider(factory, options.SaasBaseUrl ?? string.Empty),
                _                          => new SqliteDataProvider(factory)
            };
        });

        return services;
    }
}
