// -----------------------------------------------------------------------
// <copyright file="ApiKeyRepository.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Dapper;
using Microsoft.Data.Sqlite;
using AgentSkillStore.Server.Models;

namespace AgentSkillStore.Server.Data;

public sealed class ApiKeyRepository
{
    private readonly string _connectionString;

    public ApiKeyRepository(DatabaseInitializer initializer)
    {
        DapperConfiguration.Initialize();
        _connectionString = initializer.ConnectionString;
    }

    public async Task<long> CreateKeyAsync(
        string label,
        string keyHash,
        string agentId,
        string machineHash,
        string scopes,
        DateTimeOffset? expiresAt,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var expiresAtStr = expiresAt?.ToString("O");
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO api_keys (label, key_hash, agent_id, machine_hash, scopes, created_at, expires_at)
            VALUES (@label, @keyHash, @agentId, @machineHash, @scopes, @now, @expiresAtStr);
            SELECT last_insert_rowid();
            """,
            new { label, keyHash, agentId, machineHash, scopes, now, expiresAtStr },
            cancellationToken: ct));
    }

    public async Task<ApiKey?> GetKeyByHashAsync(string keyHash, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<ApiKey>(new CommandDefinition(
            """
            SELECT id AS Id, label AS Label, key_hash AS KeyHash,
                   agent_id AS AgentId, machine_hash AS MachineHash, scopes AS Scopes,
                   created_at AS CreatedAt, expires_at AS ExpiresAt
            FROM api_keys WHERE key_hash = @keyHash
            """,
            new { keyHash },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ApiKey>> GetAllKeysAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var keys = await connection.QueryAsync<ApiKey>(new CommandDefinition(
            """
            SELECT id AS Id, label AS Label, key_hash AS KeyHash,
                   agent_id AS AgentId, machine_hash AS MachineHash, scopes AS Scopes,
                   created_at AS CreatedAt, expires_at AS ExpiresAt
            FROM api_keys ORDER BY created_at DESC
            """,
            cancellationToken: ct));
        return keys.ToList();
    }

    public async Task UpdateKeyIdentityAsync(
        long id,
        string agentId,
        string machineHash,
        string scopes,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE api_keys
            SET agent_id = @agentId,
                machine_hash = @machineHash,
                scopes = @scopes
            WHERE id = @id
            """,
            new { id, agentId, machineHash, scopes },
            cancellationToken: ct));
    }

    public async Task<bool> DeleteKeyIfSafeAsync(
        long id,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM api_keys
            WHERE id = @id
              AND (
                instr(' ' || scopes || ' ', ' keys:manage ') = 0
                OR (expires_at IS NOT NULL AND datetime(expires_at) < datetime(@now))
                OR (
                  SELECT COUNT(*)
                  FROM api_keys AS other
                  WHERE other.id <> @id
                    AND instr(' ' || other.scopes || ' ', ' keys:manage ') > 0
                    AND (other.expires_at IS NULL OR datetime(other.expires_at) >= datetime(@now))
                ) > 0
              )
            """,
            new { id, now = now.ToString("O") },
            cancellationToken: ct));
        return deleted > 0;
    }

    public async Task<bool> AnyKeysExistAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM api_keys LIMIT 1)",
            cancellationToken: ct));
        return exists == 1;
    }
}
