using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Services.Security;
using Xunit;

namespace FenrixCloudBudget.Tests;

public class SecretStoreTests
{
    [Fact]
    public void Mask_ShowsFirstFourThenDots()
    {
        var masked = ISecretStore.Mask("abcd1234567890");
        Assert.StartsWith("abcd", masked);
        Assert.Contains("•", masked);
        Assert.DoesNotContain("1234567890", masked);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsThroughAes()
    {
        using var test = new TestDb();
        var keys = new DevFileKeyProvider(Path.Combine(Path.GetTempPath(), $"fx-{Guid.NewGuid():N}.key"));
        var store = new AesSecretStore(keys, test.Factory);

        var handle = await store.SaveAsync("super-secret-value");
        var roundTripped = await store.GetAsync(handle.Reference);

        Assert.Equal("super-secret-value", roundTripped);
        Assert.StartsWith("supe", handle.Hint);
    }

    [Fact]
    public async Task Remove_DeletesSecret()
    {
        using var test = new TestDb();
        var keys = new DevFileKeyProvider(Path.Combine(Path.GetTempPath(), $"fx-{Guid.NewGuid():N}.key"));
        var store = new AesSecretStore(keys, test.Factory);

        var handle = await store.SaveAsync("to-be-removed");
        await store.RemoveAsync(handle.Reference);

        Assert.Null(await store.GetAsync(handle.Reference));
    }
}
