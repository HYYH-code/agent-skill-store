// -----------------------------------------------------------------------
// <copyright file="ApiKeyService.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using AgentSkillStore.Server.Data;
using AgentSkillStore.Server.Models;

namespace AgentSkillStore.Server.Services;

public sealed class ApiKeyService
{
    public static readonly IReadOnlyList<string> DefaultAgentScopes = ["skills:read", "skills:submit"];
    public static readonly IReadOnlyList<string> BootstrapScopes = ["skills:read", "skills:submit", "keys:manage"];
    private readonly ApiKeyRepository _repository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(
        ApiKeyRepository repository,
        IConfiguration configuration,
        ILogger<ApiKeyService> logger)
    {
        _repository = repository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(string RawKey, ApiKey StoredKey)> CreateKeyAsync(
        string label,
        string? agentId = null,
        string? machineHash = null,
        IReadOnlyList<string>? scopes = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default)
    {
        var rawKey = GenerateRawKey();
        var keyHash = HashKey(rawKey);
        var now = DateTimeOffset.UtcNow;
        var normalizedAgentId = NormalizeAgentId(agentId);
        var normalizedMachineHash = NormalizeMachineHash(machineHash);
        var normalizedScopes = NormalizeScopes(scopes);

        var id = await _repository.CreateKeyAsync(
            label,
            keyHash,
            normalizedAgentId,
            normalizedMachineHash,
            string.Join(' ', normalizedScopes),
            expiresAt,
            ct);

        _logger.LogInformation("Created API key '{Label}' (id={Id})", label, id);

        return (rawKey, new ApiKey
        {
            Id = id,
            Label = label,
            KeyHash = keyHash,
            AgentId = normalizedAgentId,
            MachineHash = normalizedMachineHash,
            Scopes = string.Join(' ', normalizedScopes),
            CreatedAt = now,
            ExpiresAt = expiresAt
        });
    }

    public async Task<bool> ValidateKeyAsync(string rawKey, CancellationToken ct = default)
    {
        return await ValidateAndGetKeyAsync(rawKey, ct) is not null;
    }

    public async Task<ApiKey?> ValidateAndGetKeyAsync(string rawKey, CancellationToken ct = default)
    {
        var keyHashHex = HashKey(rawKey);
        var storedKey = await _repository.GetKeyByHashAsync(keyHashHex, ct);

        if (storedKey is null)
            return null;

        if (storedKey.ExpiresAt.HasValue && storedKey.ExpiresAt.Value < DateTimeOffset.UtcNow)
            return null;

        return storedKey;
    }

    public async Task SeedFromEnvironmentAsync(CancellationToken ct = default)
    {
        var envKey = _configuration["AgentSkillStore:BootstrapApiKey"];
        if (string.IsNullOrWhiteSpace(envKey))
            return;

        var keyHash = HashKey(envKey);
        var existingBootstrapKey = await _repository.GetKeyByHashAsync(keyHash, ct);
        if (existingBootstrapKey is not null)
        {
            var scopes = MergeScopes(ParseScopes(existingBootstrapKey.Scopes), BootstrapScopes);
            await _repository.UpdateKeyIdentityAsync(
                existingBootstrapKey.Id,
                string.IsNullOrWhiteSpace(existingBootstrapKey.AgentId) || existingBootstrapKey.AgentId == "legacy-agent"
                    ? "bootstrap-admin"
                    : existingBootstrapKey.AgentId,
                existingBootstrapKey.MachineHash,
                string.Join(' ', scopes),
                ct);
            _logger.LogDebug("API key from environment already exists, bootstrap permissions verified");
            return;
        }

        if (await _repository.AnyKeysExistAsync(ct))
        {
            _logger.LogDebug("API keys already exist, skipping environment seed");
            return;
        }

        await _repository.CreateKeyAsync(
            "bootstrap",
            keyHash,
            "bootstrap-admin",
            "",
            string.Join(' ', BootstrapScopes),
            expiresAt: null,
            ct);
        _logger.LogInformation("Seeded initial API key from the configured environment variable");
    }

    public async Task<IReadOnlyList<ApiKey>> ListKeysAsync(CancellationToken ct = default)
    {
        return await _repository.GetAllKeysAsync(ct);
    }

    public async Task<bool> DeleteKeyAsync(long id, CancellationToken ct = default)
    {
        var deleted = await _repository.DeleteKeyIfSafeAsync(id, DateTimeOffset.UtcNow, ct);
        if (deleted)
        {
            _logger.LogInformation("Deleted API key id={Id}", id);
        }

        return deleted;
    }

    private static string GenerateRawKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"sk-{Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }

    internal static string HashKey(string rawKey)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
    }

    public static IReadOnlyList<string> ParseScopes(string scopes) =>
        scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> MergeScopes(
        IReadOnlyList<string> existingScopes,
        IReadOnlyList<string> requiredScopes) =>
        existingScopes
            .Concat(requiredScopes)
            .Where(scope => scope is "skills:read" or "skills:submit" or "keys:manage")
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> NormalizeScopes(IReadOnlyList<string>? scopes)
    {
        var requested = scopes is { Count: > 0 } ? scopes : DefaultAgentScopes;
        var allowed = requested
            .Where(scope => scope is "skills:read" or "skills:submit" or "keys:manage")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return allowed.Count == 0 ? DefaultAgentScopes : allowed;
    }

    private static string NormalizeAgentId(string? agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return $"agent-{Guid.NewGuid():N}"[..18];
        var normalized = agentId.Trim().ToLowerInvariant();
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static string NormalizeMachineHash(string? machineHash)
    {
        if (string.IsNullOrWhiteSpace(machineHash))
            return "";
        var normalized = machineHash.Trim().ToLowerInvariant();
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }
}
