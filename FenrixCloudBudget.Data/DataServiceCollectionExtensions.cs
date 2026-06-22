using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data.Providers;
using FenrixCloudBudget.Data.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FenrixCloudBudget.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core context factory and the IDataProvider matching the chosen mode.
    /// The context factory is a <see cref="RoutingDbContextFactory"/> so that "test data mode"
    /// transparently swaps the entire app onto an isolated test database when toggled on.
    /// </summary>
    public static IServiceCollection AddFenrixData(this IServiceCollection services, DataProviderOptions options)
    {
        services.AddSingleton(options);

        // Test-data routing: one factory serves either the real backend or the isolated test DB.
        services.AddSingleton<TestDataState>();
        services.AddSingleton<RoutingDbContextFactory>(sp =>
            new RoutingDbContextFactory(options, sp.GetRequiredService<TestDataState>()));
        services.AddSingleton<IDbContextFactory<AppDbContext>>(sp =>
            sp.GetRequiredService<RoutingDbContextFactory>());

        services.AddSingleton<IDataProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
            return options.Mode switch
            {
                DataProviderMode.SqlServer => new SqlServerDataProvider(factory, DataProviderMode.SqlServer),
                DataProviderMode.CloudSql  => new SqlServerDataProvider(factory, DataProviderMode.CloudSql),
                DataProviderMode.Saas      => new SaaSDataProvider(
                    factory,
                    options.SaasBaseUrl ?? string.Empty,
                    options.SaasAccessToken),
                _                          => new SqliteDataProvider(factory)
            };
        });

        return services;
    }
}
