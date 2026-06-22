using FenrixCloudBudget.Core.Entities;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Core.Models;
using FenrixCloudBudget.Services.Cloud;
using FenrixCloudBudget.Services.Security;
using FenrixCloudBudget.Services.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FenrixCloudBudget.Tests;

public class CostSyncServiceTests
{
    [Fact]
    public async Task ForcedSync_ReplacesWindowAndMapsExactResourceIds()
    {
        using var test = new TestDb();
        var now = new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
        var store = new AesSecretStore(new DevFileKeyProvider(TempKey()), test.Factory);
        var connector = new FakeConnector(store, CloudProvider.Azure);
        var connections = new CloudConnectionService(
            new FakeFactory(connector),
            store,
            test.Factory,
            NullLogger<CloudConnectionService>.Instance);

        var connected = await connections.ConnectAsync(CloudProvider.Azure, "Azure prod", new Dictionary<string, string>
        {
            ["tenantId"] = "tenant",
            ["clientId"] = "client",
            ["clientSecret"] = "secret"
        });

        int serviceId;
        await using (var db = test.NewContext())
        {
            var project = new Project { Name = "Web", Currency = "USD" };
            var connectedService = new Service
            {
                Project = project,
                Name = "API VM",
                Provider = CloudProvider.Azure,
                Source = ServiceSource.Connected,
                ExternalResourceId = "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/api",
                CloudAccountId = connected.Account!.Id,
                Currency = "USD"
            };
            db.Services.Add(connectedService);
            await db.SaveChangesAsync();
            serviceId = connectedService.Id;
        }

        var day = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);
        connector.Costs =
        [
            new CostDatum(
                day,
                12.34m,
                "USD",
                "Virtual Machines",
                "/SUBSCRIPTIONS/SUB-1/RESOURCEGROUPS/RG/PROVIDERS/MICROSOFT.COMPUTE/VIRTUALMACHINES/API/")
        ];

        var syncService = new CostSyncService(
            connections,
            test.Factory,
            new FixedTimeProvider(now),
            NullLogger<CostSyncService>.Instance);

        var first = await syncService.SyncAllAsync(force: true);

        Assert.True(first.Success);
        Assert.Equal(1, first.RecordsWritten);
        await using (var db = test.NewContext())
        {
            var record = await db.CostRecords.SingleAsync();
            Assert.Equal(serviceId, record.ServiceId);
            Assert.Equal(12.34m, record.Amount);
            Assert.Equal(now, (await db.CloudAccounts.SingleAsync()).LastSyncedUtc);
        }

        connector.Costs =
        [
            new CostDatum(day, 9.87m, "USD", "Virtual Machines",
                "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/api")
        ];

        var second = await syncService.SyncAllAsync(force: true);

        Assert.Equal(1, second.RecordsWritten);
        await using (var db = test.NewContext())
        {
            var records = await db.CostRecords.ToListAsync();
            Assert.Single(records);
            Assert.Equal(9.87m, records[0].Amount);
        }
    }

    [Fact]
    public async Task AutomaticAwsSync_UsesCacheAndSplitsServiceLevelCost()
    {
        using var test = new TestDb();
        var now = new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
        var store = new AesSecretStore(new DevFileKeyProvider(TempKey()), test.Factory);
        var connector = new FakeConnector(store, CloudProvider.Aws);
        var connections = new CloudConnectionService(
            new FakeFactory(connector),
            store,
            test.Factory,
            NullLogger<CloudConnectionService>.Instance);

        var connected = await connections.ConnectAsync(CloudProvider.Aws, "AWS prod", new Dictionary<string, string>
        {
            ["accessKeyId"] = "AKIA",
            ["secretAccessKey"] = "secret",
            ["region"] = "eu-west-1"
        });

        await using (var db = test.NewContext())
        {
            var project = new Project { Name = "Compute", Currency = "USD" };
            db.Services.AddRange(
                new Service
                {
                    Project = project,
                    Name = "api-1",
                    ServiceType = "ec2",
                    Provider = CloudProvider.Aws,
                    Source = ServiceSource.Connected,
                    ExternalResourceId = "arn:aws:ec2:eu-west-1:123:instance/i-1",
                    CloudAccountId = connected.Account!.Id
                },
                new Service
                {
                    Project = project,
                    Name = "api-2",
                    ServiceType = "ec2",
                    Provider = CloudProvider.Aws,
                    Source = ServiceSource.Connected,
                    ExternalResourceId = "arn:aws:ec2:eu-west-1:123:instance/i-2",
                    CloudAccountId = connected.Account!.Id
                });
            await db.SaveChangesAsync();
        }

        connector.Costs =
        [
            new CostDatum(
                DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1),
                10m,
                "USD",
                "Amazon Elastic Compute Cloud - Compute")
        ];

        var syncService = new CostSyncService(
            connections,
            test.Factory,
            new FixedTimeProvider(now),
            NullLogger<CostSyncService>.Instance);

        await syncService.SyncDueAsync();

        await using (var db = test.NewContext())
        {
            var records = await db.CostRecords.OrderBy(c => c.ServiceId).ToListAsync();
            Assert.Equal(2, records.Count);
            Assert.All(records, record => Assert.Equal(5m, record.Amount));
        }
        Assert.Equal(1, connector.CostCallCount);

        await syncService.SyncDueAsync();

        Assert.Equal(1, connector.CostCallCount); // recent LastSyncedUtc satisfies the 12-hour AWS cache
    }

    private static string TempKey() => Path.Combine(Path.GetTempPath(), $"fx-{Guid.NewGuid():N}.key");

    private sealed class FakeFactory : ICloudConnectorFactory
    {
        private readonly ICloudConnector _connector;
        public FakeFactory(ICloudConnector connector) => _connector = connector;
        public ICloudConnector Create(CloudProvider provider) => _connector;
    }

    private sealed class FakeConnector : ICloudConnector
    {
        private readonly ISecretStore _store;

        public FakeConnector(ISecretStore store, CloudProvider provider)
        {
            _store = store;
            Provider = provider;
        }

        public CloudProvider Provider { get; }
        public IReadOnlyList<CostDatum> Costs { get; set; } = Array.Empty<CostDatum>();
        public int CostCallCount { get; private set; }

        public IReadOnlyList<CloudFieldSpec> CredentialSchema => Provider switch
        {
            CloudProvider.Azure =>
            [
                new CloudFieldSpec("tenantId", "Tenant"),
                new CloudFieldSpec("clientId", "Client"),
                new CloudFieldSpec("clientSecret", "Secret", IsSecret: true)
            ],
            _ =>
            [
                new CloudFieldSpec("accessKeyId", "Access key"),
                new CloudFieldSpec("secretAccessKey", "Secret", IsSecret: true),
                new CloudFieldSpec("region", "Region", Required: false)
            ]
        };

        public async Task<AuthResult> AuthenticateAsync(CloudCredential credential, CancellationToken ct = default)
        {
            var secretKey = CredentialSchema.Single(field => field.IsSecret).Key;
            var handle = await _store.SaveAsync(credential.Fields[secretKey], ct);
            return new AuthResult(true, handle.Reference, handle.Hint);
        }

        public Task<IReadOnlyList<CloudScope>> ListScopesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CloudScope>>(
                [new CloudScope("sub-1", "Scope", Provider)]);

        public Task<IReadOnlyList<DiscoveredResource>> DiscoverResourcesAsync(string scopeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiscoveredResource>>(Array.Empty<DiscoveredResource>());

        public Task<IReadOnlyList<CostDatum>> GetCostsAsync(
            string scopeId,
            DateOnly from,
            DateOnly to,
            CostGroupBy groupBy,
            CancellationToken ct = default)
        {
            CostCallCount++;
            return Task.FromResult(Costs);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
