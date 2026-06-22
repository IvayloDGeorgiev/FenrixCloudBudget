using System.Net;
using System.Net.Http.Json;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Models;
using FenrixCloudBudget.Data.Providers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FenrixCloudBudget.Tests;

public class SaaSDataProviderTests
{
    [Fact]
    public async Task Sync_HydratesAuthenticatedSnapshotIntoLocalCache()
    {
        using var test = new TestDb();
        var serverUtc = new DateTimeOffset(2026, 6, 22, 15, 0, 0, TimeSpan.Zero);
        var snapshot = new SaasWorkspaceSnapshot(
            serverUtc,
            [
                new SaasClientDto(
                    10, "Acme", "Acme Ltd", null, "ops@acme.test", null, null, null, null, null,
                    serverUtc.AddDays(-10), serverUtc.AddDays(-1))
            ],
            [
                new SaasProjectDto(
                    20, "Platform", null, ProjectStatus.Active, "USD", 10,
                    serverUtc.AddDays(-9), serverUtc.AddDays(-1))
            ],
            [
                new SaasServiceDto(
                    30, 20, "API", CloudProvider.Azure, "App Service", null, ServiceSource.Connected,
                    "/subscriptions/example/api", 25m, BudgetPeriod.Monthly, "USD",
                    serverUtc.AddDays(-8), serverUtc.AddDays(-1))
            ],
            [
                new SaasBudgetDto(
                    40, 20, null, "Monthly", BudgetPeriod.Monthly, 100m, "USD", "50,80,100", true,
                    serverUtc.AddDays(-7), serverUtc.AddDays(-1))
            ],
            [],
            [
                new SaasCostRecordDto(
                    50, 30, new DateOnly(2026, 6, 21), 4.25m, "USD", ServiceSource.Connected,
                    serverUtc, serverUtc, null)
            ]);

        var handler = new SnapshotHandler(snapshot);
        var provider = new SaaSDataProvider(
            test.Factory,
            "https://api.fenrix.test",
            "session-token",
            new HttpClient(handler));

        var result = await provider.SyncAsync();

        Assert.True(result.Success);
        Assert.Equal(5, result.RecordsHydrated);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("session-token", handler.AuthorizationParameter);

        await using var db = test.NewContext();
        Assert.Equal("Acme", (await db.Clients.SingleAsync()).Name);
        Assert.Equal("Platform", (await db.Projects.SingleAsync()).Name);
        Assert.Equal(4.25m, (await db.CostRecords.SingleAsync()).Amount);
        Assert.Null((await db.Services.SingleAsync()).CloudAccountId); // credentials stay device-local
    }

    private sealed class SnapshotHandler(SaasWorkspaceSnapshot snapshot) : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(snapshot)
            });
        }
    }
}
