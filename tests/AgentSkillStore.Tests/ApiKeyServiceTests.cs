// -----------------------------------------------------------------------
// <copyright file="ApiKeyServiceTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Server.Services;
using AgentSkillStore.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentSkillStore.Tests;

public sealed class ApiKeyHashTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"agent-skill-store-api-keys-{Guid.NewGuid():N}");

    [Fact]
    public void HashKey_ProducesDeterministicResult()
    {
        var hash1 = ApiKeyService.HashKey("test-key-123");
        var hash2 = ApiKeyService.HashKey("test-key-123");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashKey_DifferentInputs_ProduceDifferentHashes()
    {
        var hash1 = ApiKeyService.HashKey("key-one");
        var hash2 = ApiKeyService.HashKey("key-two");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashKey_Returns64CharLowercaseHex()
    {
        var hash = ApiKeyService.HashKey("test-key");
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Matches("^[a-f0-9]{64}$", hash);
    }

    [Fact]
    public async Task SeedFromEnvironment_StoresOnlyBootstrapKeyHash()
    {
        const string rawKey = "sk-bootstrap-test-key";
        var service = await CreateServiceAsync(rawKey, TestContext.Current.CancellationToken);

        await service.SeedFromEnvironmentAsync(TestContext.Current.CancellationToken);

        var keys = await service.ListKeysAsync(TestContext.Current.CancellationToken);
        var stored = Assert.Single(keys);
        Assert.Equal(ApiKeyService.HashKey(rawKey), stored.KeyHash);
        Assert.NotEqual(rawKey, stored.KeyHash);
        Assert.Contains("keys:manage", ApiKeyService.ParseScopes(stored.Scopes));
        Assert.True(await service.ValidateKeyAsync(rawKey, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteKeyAsync_RejectsLastUnexpiredKeyManager()
    {
        var service = await CreateServiceAsync(null, TestContext.Current.CancellationToken);
        var (_, manager) = await service.CreateKeyAsync(
            "manager",
            scopes: ["keys:manage"],
            ct: TestContext.Current.CancellationToken);
        var (_, reader) = await service.CreateKeyAsync(
            "reader",
            scopes: ["skills:read"],
            ct: TestContext.Current.CancellationToken);

        Assert.False(await service.DeleteKeyAsync(manager.Id, TestContext.Current.CancellationToken));
        Assert.True(await service.DeleteKeyAsync(reader.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteKeyAsync_AllowsManagerDeletionWhenAnotherValidManagerExists()
    {
        var service = await CreateServiceAsync(null, TestContext.Current.CancellationToken);
        var (_, first) = await service.CreateKeyAsync(
            "first-manager",
            scopes: ["keys:manage"],
            ct: TestContext.Current.CancellationToken);
        await service.CreateKeyAsync(
            "second-manager",
            scopes: ["keys:manage"],
            expiresAt: DateTimeOffset.UtcNow.AddHours(1),
            ct: TestContext.Current.CancellationToken);

        Assert.True(await service.DeleteKeyAsync(first.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteKeyAsync_DoesNotCountExpiredManagerAsRecoveryPath()
    {
        var service = await CreateServiceAsync(null, TestContext.Current.CancellationToken);
        var (_, validManager) = await service.CreateKeyAsync(
            "valid-manager",
            scopes: ["keys:manage"],
            ct: TestContext.Current.CancellationToken);
        var (_, expiredManager) = await service.CreateKeyAsync(
            "expired-manager",
            scopes: ["keys:manage"],
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            ct: TestContext.Current.CancellationToken);

        Assert.False(await service.DeleteKeyAsync(validManager.Id, TestContext.Current.CancellationToken));
        Assert.True(await service.DeleteKeyAsync(expiredManager.Id, TestContext.Current.CancellationToken));
    }

    private async Task<ApiKeyService> CreateServiceAsync(string? bootstrapKey, CancellationToken ct)
    {
        Directory.CreateDirectory(_tempDir);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentSkillStore:DataPath"] = _tempDir,
                ["AgentSkillStore:BootstrapApiKey"] = bootstrapKey
            })
            .Build();
        var initializer = new DatabaseInitializer(configuration, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync(ct);
        return new ApiKeyService(
            new ApiKeyRepository(initializer),
            configuration,
            NullLogger<ApiKeyService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
