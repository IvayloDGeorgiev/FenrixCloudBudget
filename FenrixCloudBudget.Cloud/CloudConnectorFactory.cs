using FenrixCloudBudget.Cloud.Aws;
using FenrixCloudBudget.Cloud.Azure;
using FenrixCloudBudget.Cloud.Gcp;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;

namespace FenrixCloudBudget.Cloud;

public sealed class CloudConnectorFactory : ICloudConnectorFactory
{
    private readonly IServiceProviderShim _sp;
    public CloudConnectorFactory(IServiceProviderShim sp) => _sp = sp;

    public ICloudConnector Create(CloudProvider provider) => provider switch
    {
        CloudProvider.Aws   => _sp.Get<AwsCloudConnector>(),
        CloudProvider.Azure => _sp.Get<AzureCloudConnector>(),
        CloudProvider.Gcp   => _sp.Get<GcpCloudConnector>(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}

/// <summary>
/// Tiny indirection so this assembly doesn't take a hard dependency on a specific DI
/// container. The host registers an implementation backed by IServiceProvider.
/// </summary>
public interface IServiceProviderShim
{
    T Get<T>() where T : notnull;
}
