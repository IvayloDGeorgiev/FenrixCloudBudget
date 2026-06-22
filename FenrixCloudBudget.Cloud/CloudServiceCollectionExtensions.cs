using FenrixCloudBudget.Cloud.Aws;
using FenrixCloudBudget.Cloud.Azure;
using FenrixCloudBudget.Cloud.Gcp;
using FenrixCloudBudget.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FenrixCloudBudget.Cloud;

public static class CloudServiceCollectionExtensions
{
    public static IServiceCollection AddFenrixCloud(this IServiceCollection services)
    {
        services.AddTransient<AwsCloudConnector>();
        services.AddTransient<AzureCloudConnector>();
        services.AddTransient<GcpCloudConnector>();
        services.AddSingleton<IServiceProviderShim, DiServiceProviderShim>();
        services.AddSingleton<ICloudConnectorFactory, CloudConnectorFactory>();
        return services;
    }

    private sealed class DiServiceProviderShim : IServiceProviderShim
    {
        private readonly IServiceProvider _sp;
        public DiServiceProviderShim(IServiceProvider sp) => _sp = sp;
        public T Get<T>() where T : notnull => _sp.GetRequiredService<T>();
    }
}
