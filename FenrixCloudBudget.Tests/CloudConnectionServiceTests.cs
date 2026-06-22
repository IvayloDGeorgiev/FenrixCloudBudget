using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using FenrixCloudBudget.Services.Cloud;
using FenrixCloudBudget.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FenrixCloudBudget.Tests;

public class CloudConnectionServiceTests
{
    [Fact]
    public async Task Connect_PersistsAccount_WithNonSecretOptionsAndHint()
    {
        using var test = new TestDb();
        var store = new AesSecretStore(new DevFileKeyProvider(TempKey()), test.Factory);
        var connector = new FakeConnector(store);
        var svc = new CloudConnectionService(new FakeFactory(connector), store, test.Factory, NullLogger<CloudConnectionService>.Instance);

        var fields = new Dictionary<string, string>
        {
            ["accessKeyId"] = "AKIAEXAMPLE",
            ["secretAccessKey"] = "shhh-very-secret",
            ["region"] = "eu-west-1"
        };

        var result = await svc.ConnectAsync(CloudProvider.Aws, "Acme prod", fields);

        Assert.True(result.Success);
        await using var db = test.NewContext();
        var saved = await db.CloudAccounts.SingleAsync();
        Assert.Equal("Acme prod", saved.DisplayName);
        Assert.Contains("AKIAEXAMPLE", saved.OptionsJson);      // non-secret kept
        Assert.DoesNotContain("shhh-very-secret", saved.OptionsJson); // secret NOT in options
        Assert.StartsWith("shhh", saved.SecretHint);            // masked hint
    }

    [Fact]
    public async Task Discover_ReturnsResources_ForSavedAccount()
    {
        using var test = new TestDb();
        var store = new AesSecretStore(new DevFileKeyProvider(TempKey()), test.Factory);
        var connector = new FakeConnector(store);
        var svc = new CloudConnectionService(new FakeFactory(connector), store, test.Factory, NullLogger<CloudConnectionService>.Instance);

        var connect = await svc.ConnectAsync(CloudProvider.Aws, "Acme", new Dictionary<string, string>
        {
            ["accessKeyId"] = "AKIA",
            ["secretAccessKey"] = "secret",
            ["region"] = "us-east-1"
        });

        var resources = await svc.DiscoverAsync(connect.Account!.Id);

        Assert.Single(resources);
        Assert.Equal("vm-1", resources[0].Name);

        await using var db = test.NewContext();
        Assert.Single(await db.SecretEntries.ToListAsync()); // re-auth must not leak a duplicate encrypted secret
    }

    private static string TempKey() => Path.Combine(Path.GetTempPath(), $"fx-{Guid.NewGuid():N}.key");

    private sealed class FakeFactory : ICloudConnectorFactory
    {
        private readonly ICloudConnector _c;
        public FakeFactory(ICloudConnector c) => _c = c;
        public ICloudConnector Create(CloudProvider provider) => _c;
    }

    private sealed class FakeConnector : ICloudConnector
    {
        private readonly ISecretStore _store;
        public FakeConnector(ISecretStore store) => _store = store;

        public CloudProvider Provider => CloudProvider.Aws;

        public IReadOnlyList<CloudFieldSpec> CredentialSchema => new[]
        {
            new CloudFieldSpec("accessKeyId", "Access key ID"),
            new CloudFieldSpec("secretAccessKey", "Secret", IsSecret: true),
            new CloudFieldSpec("region", "Region", Required: false)
        };

        public async Task<AuthResult> AuthenticateAsync(CloudCredential credential, CancellationToken ct = default)
        {
            // Mimic real connectors: store the secret, return reference + hint.
            var secret = credential.Fields["secretAccessKey"];
            var handle = await _store.SaveAsync(secret, ct);
            return new AuthResult(true, handle.Reference, handle.Hint);
        }

        public Task<IReadOnlyList<CloudScope>> ListScopesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CloudScope>>(new[] { new CloudScope("acct-1", "Account", CloudProvider.Aws) });

        public Task<IReadOnlyList<DiscoveredResource>> DiscoverResourcesAsync(string scopeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiscoveredResource>>(new[]
            {
                new DiscoveredResource("arn:aws:ec2:vm-1", "vm-1", "ec2", CloudProvider.Aws)
            });

        public Task<IReadOnlyList<CostDatum>> GetCostsAsync(string scopeId, DateOnly from, DateOnly to, CostGroupBy groupBy, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CostDatum>>(Array.Empty<CostDatum>());
    }
}
