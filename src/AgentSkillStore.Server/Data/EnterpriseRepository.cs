// -----------------------------------------------------------------------
// <copyright file="EnterpriseRepository.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Dapper;
using Microsoft.Data.Sqlite;
using AgentSkillStore.Server.Models;
using System.Text.Json;

namespace AgentSkillStore.Server.Data;

public sealed class EnterpriseRepository
{
    private readonly string _connectionString;

    public EnterpriseRepository(DatabaseInitializer initializer)
    {
        DapperConfiguration.Initialize();
        _connectionString = initializer.ConnectionString;
    }

    public async Task<IReadOnlyList<EnterpriseSkillSummary>> GetSkillsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var rows = await connection.QueryAsync<EnterpriseSkillRow>(new CommandDefinition(
            """
            SELECT s.name AS Name, sv.description AS Description, sv.version AS LatestVersion,
                   sv.sha256 AS Digest, s.updated_at AS UpdatedAt,
                   COALESCE(es.display_name, s.name) AS DisplayName,
                   COALESCE(es.namespace, 'general') AS Namespace,
                   COALESCE(es.owner_team_id, 'team-unclaimed') AS OwnerId,
                   COALESCE(t.name, '待认领') AS OwnerName,
                   COALESCE(es.category_id, 'general') AS CategoryId,
                   COALESCE(c.name, sv.category, '通用工装') AS CategoryName,
                   COALESCE(es.featured, 0) AS Featured,
                   COALESCE(esv.status, 'beta') AS Status,
                   COALESCE(esv.risk_level, 'medium') AS RiskLevel
            FROM skills s
            JOIN skill_versions sv ON sv.skill_id = s.id AND sv.is_latest = 1
            LEFT JOIN enterprise_skill es ON es.skill_name = s.name COLLATE NOCASE
            LEFT JOIN enterprise_skill_version esv ON esv.skill_name = s.name COLLATE NOCASE AND esv.version = sv.version
            LEFT JOIN team t ON t.id = es.owner_team_id
            LEFT JOIN category c ON c.id = es.category_id
            ORDER BY es.featured DESC, s.updated_at DESC
            """, cancellationToken: ct));

        var results = new List<EnterpriseSkillSummary>();
        foreach (var row in rows)
            results.Add(await ToSummaryAsync(connection, row, includeFiles: false, ct));
        return results;
    }

    public async Task<EnterpriseSkillSummary?> GetSkillAsync(string name, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var row = await connection.QuerySingleOrDefaultAsync<EnterpriseSkillRow>(new CommandDefinition(
            """
            SELECT s.name AS Name, sv.description AS Description, sv.version AS LatestVersion,
                   sv.sha256 AS Digest, s.updated_at AS UpdatedAt,
                   COALESCE(es.display_name, s.name) AS DisplayName,
                   COALESCE(es.namespace, 'general') AS Namespace,
                   COALESCE(es.owner_team_id, 'team-unclaimed') AS OwnerId,
                   COALESCE(t.name, '待认领') AS OwnerName,
                   COALESCE(es.category_id, 'general') AS CategoryId,
                   COALESCE(c.name, sv.category, '通用工装') AS CategoryName,
                   COALESCE(es.featured, 0) AS Featured,
                   COALESCE(esv.status, 'beta') AS Status,
                   COALESCE(esv.risk_level, 'medium') AS RiskLevel
            FROM skills s
            JOIN skill_versions sv ON sv.skill_id = s.id AND sv.is_latest = 1
            LEFT JOIN enterprise_skill es ON es.skill_name = s.name COLLATE NOCASE
            LEFT JOIN enterprise_skill_version esv ON esv.skill_name = s.name COLLATE NOCASE AND esv.version = sv.version
            LEFT JOIN team t ON t.id = es.owner_team_id
            LEFT JOIN category c ON c.id = es.category_id
            WHERE s.name = @name COLLATE NOCASE
            """, new { name }, cancellationToken: ct));
        return row is null ? null : await ToSummaryAsync(connection, row, includeFiles: true, ct);
    }

    public async Task<bool> IsVersionInstallableAsync(
        string name,
        string version,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT COALESCE(esv.status, 'beta')
            FROM skill_versions sv
            JOIN skills s ON s.id = sv.skill_id
            LEFT JOIN enterprise_skill_version esv
              ON esv.skill_name = s.name COLLATE NOCASE AND esv.version = sv.version
            WHERE s.name = @name COLLATE NOCASE AND sv.version = @version
            """, new { name, version }, cancellationToken: ct));
        return status is not null and not ("draft" or "pending" or "revoked");
    }

    public async Task<IReadOnlyDictionary<string, string>> GetVersionStatusesAsync(
        string name,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var rows = await connection.QueryAsync<VersionStatusRow>(new CommandDefinition(
            """
            SELECT sv.version AS Version, COALESCE(esv.status, 'beta') AS Status
            FROM skill_versions sv
            JOIN skills s ON s.id = sv.skill_id
            LEFT JOIN enterprise_skill_version esv
              ON esv.skill_name = s.name COLLATE NOCASE AND esv.version = sv.version
            WHERE s.name = @name COLLATE NOCASE
            """, new { name }, cancellationToken: ct));

        return rows.ToDictionary(row => row.Version, row => row.Status, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PublicationResponse> CreatePublicationAsync(
        CreatePublicationRequest request,
        EnterpriseSessionUser actor,
        string requestId,
        string packageDigest,
        string manifestJson,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var id = $"pub_{Guid.NewGuid():N}";
        var reviewId = $"review_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow.ToString("O");
        var ownerId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT id FROM team WHERE name = @Owner LIMIT 1", new { request.Owner }, transaction,
            cancellationToken: ct)) ?? "team-unclaimed";

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO enterprise_skill(skill_name, display_name, namespace, owner_team_id, category_id,
                visibility, featured, created_at, updated_at)
            VALUES(@Name, @DisplayName, @Namespace, @ownerId, @Namespace, 'internal', 0, @now, @now)
            ON CONFLICT(skill_name) DO UPDATE SET display_name = excluded.display_name,
                namespace = excluded.namespace, owner_team_id = excluded.owner_team_id, updated_at = excluded.updated_at;
            INSERT INTO enterprise_skill_version(skill_name, version, digest, status, risk_level,
                manifest_json, changelog, created_at, updated_at)
            VALUES(@Name, @Version, @packageDigest, 'pending', @RiskLevel, @manifestJson, @Changelog, @now, @now)
            ON CONFLICT(skill_name, version) DO UPDATE SET digest = excluded.digest, status = 'pending',
                risk_level = excluded.risk_level, changelog = excluded.changelog, updated_at = excluded.updated_at;
            """, new
            {
                request.Name, request.DisplayName, request.Namespace, ownerId, packageDigest, manifestJson,
                request.Version, request.RiskLevel, request.Changelog, now
            }, transaction, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO publication(id, skill_name, version, display_name, namespace, owner_team_id,
                submitter, submitter_id, state, risk_level, package_digest, network_policy, changelog, created_at, submitted_at)
            VALUES(@id, @Name, @Version, @DisplayName, @Namespace, @Owner, @submitter, @submitterId, 'pending',
                @RiskLevel, @digest, @NetworkPolicy, @Changelog, @now, @now)
            """, new
            {
                id, request.Name, request.Version, request.DisplayName, request.Namespace, Owner = ownerId,
                submitter = actor.DisplayName, submitterId = actor.Id, request.RiskLevel, digest = packageDigest,
                request.NetworkPolicy, request.Changelog, now
            }, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO review(id, publication_id) VALUES(@reviewId, @id)",
            new { reviewId, id }, transaction, cancellationToken: ct));
        await InsertAuditAsync(connection, transaction, actor, "publication.submit",
            $"{request.Namespace}/{request.Name}@{request.Version}", "success", requestId, ct);
        await transaction.CommitAsync(ct);
        return new PublicationResponse { Id = id, State = "pending" };
    }

    public async Task<IReadOnlyList<EnterpriseReviewSummary>> GetReviewsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var rows = await connection.QueryAsync<ReviewRow>(new CommandDefinition(
            """
            SELECT r.id AS Id, p.id AS PublicationId, p.skill_name AS SkillName,
                   p.display_name AS DisplayName, p.version AS Version, p.submitter AS Submitter,
                   p.risk_level AS RiskLevel, p.submitted_at AS SubmittedAt,
                   p.state AS State, r.comment AS Comment,
                   COALESCE(esv.manifest_json, '{}') AS ManifestJson
            FROM review r JOIN publication p ON p.id = r.publication_id
            LEFT JOIN enterprise_skill_version esv ON esv.skill_name = p.skill_name COLLATE NOCASE AND esv.version = p.version
            ORDER BY CASE p.state WHEN 'pending' THEN 0 ELSE 1 END, p.submitted_at DESC
            """, cancellationToken: ct));
        return rows.Select(row => new EnterpriseReviewSummary
        {
            Id = row.Id,
            PublicationId = row.PublicationId,
            SkillName = row.SkillName,
            DisplayName = row.DisplayName,
            Version = row.Version,
            Submitter = row.Submitter,
            RiskLevel = row.RiskLevel,
            SubmittedAt = row.SubmittedAt,
            State = row.State,
            Comment = row.Comment,
            Permissions = DeserializePermissions(row.ManifestJson)
        }).ToList();
    }

    public async Task<IReadOnlyList<EnterprisePublicationSummary>> GetPublicationsForSubmitterAsync(
        string submitterId,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var rows = await connection.QueryAsync<EnterprisePublicationSummary>(new CommandDefinition(
            """
            SELECT id AS Id, skill_name AS SkillName, display_name AS DisplayName,
                   namespace AS Namespace, version AS Version, submitter_id AS SubmitterId,
                   submitter AS Submitter, state AS State, risk_level AS RiskLevel,
                   network_policy AS NetworkPolicy, created_at AS CreatedAt, submitted_at AS SubmittedAt
            FROM publication
            WHERE submitter_id = @submitterId
            ORDER BY created_at DESC
            """, new { submitterId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> DecideReviewAsync(
        string reviewId,
        bool approved,
        ReviewDecisionRequest decision,
        EnterpriseSessionUser actor,
        string requestId,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var publication = await connection.QuerySingleOrDefaultAsync<PublicationRow>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.skill_name AS SkillName, p.version AS Version,
                   p.namespace AS Namespace, p.submitter AS Submitter,
                   p.submitter_id AS SubmitterId, p.state AS State
            FROM publication p JOIN review r ON r.publication_id = p.id
            WHERE r.id = @reviewId
            """, new { reviewId }, transaction, cancellationToken: ct));
        if (publication is null || publication.State != "pending")
            return false;
        if (publication.SubmitterId.Equals(actor.Id, StringComparison.OrdinalIgnoreCase)
            || publication.Submitter.Equals(actor.DisplayName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("审核人不能审核本人提交的版本。");

        var now = DateTimeOffset.UtcNow.ToString("O");
        var state = approved ? "approved" : "rejected";
        var reviewDecision = approved ? "approved" : "rejected";
        var channel = approved ? decision.Channel ?? "beta" : null;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE review SET reviewer = @reviewer, decision = @reviewDecision,
                channel = @channel, comment = @comment, decided_at = @now WHERE id = @reviewId;
            UPDATE publication SET state = @state WHERE id = (SELECT publication_id FROM review WHERE id = @reviewId);
            """, new { reviewer = actor.DisplayName, reviewDecision, channel, comment = decision.Comment, now, reviewId, state }, transaction, cancellationToken: ct));
        if (approved)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO enterprise_skill_version(skill_name, version, digest, status, risk_level, manifest_json, changelog, created_at, updated_at)
                SELECT skill_name, version, package_digest, @channel, risk_level, '{}', changelog, created_at, @now
                FROM publication WHERE id = @id
                ON CONFLICT(skill_name, version) DO UPDATE SET status = @channel, changelog = excluded.changelog, updated_at = @now
                """, new { channel, now, id = publication.Id }, transaction, cancellationToken: ct));
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE enterprise_skill_version SET status = 'draft', updated_at = @now WHERE skill_name = @skillName AND version = @version",
                new { now, skillName = publication.SkillName, version = publication.Version }, transaction,
                cancellationToken: ct));
        }
        await InsertAuditAsync(connection, transaction, actor,
            approved ? "skill.version.approve" : "skill.version.reject",
            $"{publication.Namespace}/{publication.SkillName}@{publication.Version}", "success", requestId, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<EnterpriseAuditEvent>> GetAuditAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var events = await connection.QueryAsync<EnterpriseAuditEvent>(new CommandDefinition(
            """
            SELECT id AS Id, actor AS Actor, role AS Role, action AS Action, resource AS Resource,
                   result AS Result, request_id AS RequestId, created_at AS CreatedAt
            FROM audit_event ORDER BY created_at DESC LIMIT 500
            """, cancellationToken: ct));
        return events.ToList();
    }

    public async Task<IReadOnlyList<GovernanceTeam>> GetTeamsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var teams = await connection.QueryAsync<GovernanceTeam>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, code AS Code, status AS Status FROM team ORDER BY name",
            cancellationToken: ct));
        return teams.ToList();
    }

    public async Task<IReadOnlyList<GovernanceCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var categories = await connection.QueryAsync<GovernanceCategory>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, parent_id AS ParentId, sort_order AS SortOrder FROM category ORDER BY sort_order, name",
            cancellationToken: ct));
        return categories.ToList();
    }

    public async Task UpsertTeamAsync(
        GovernanceTeam team,
        EnterpriseSessionUser actor,
        string requestId,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO team(id, name, code, status) VALUES(@Id, @Name, @Code, @Status)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, code = excluded.code, status = excluded.status
            """, team, transaction, cancellationToken: ct));
        await InsertAuditAsync(connection, transaction, actor, "governance.team.upsert", team.Id,
            "success", requestId, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task UpsertCategoryAsync(
        GovernanceCategory category,
        EnterpriseSessionUser actor,
        string requestId,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO category(id, name, parent_id, sort_order) VALUES(@Id, @Name, @ParentId, @SortOrder)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, parent_id = excluded.parent_id, sort_order = excluded.sort_order
            """, category, transaction, cancellationToken: ct));
        await InsertAuditAsync(connection, transaction, actor, "governance.category.upsert", category.Id,
            "success", requestId, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task AppendAuditAsync(
        EnterpriseSessionUser actor,
        string action,
        string resource,
        string result,
        string requestId,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await InsertAuditAsync(connection, transaction, actor, action, resource, result, requestId, ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task<EnterpriseSkillSummary> ToSummaryAsync(
        SqliteConnection connection,
        EnterpriseSkillRow row,
        bool includeFiles,
        CancellationToken ct)
    {
        var versions = (await connection.QueryAsync<EnterpriseSkillVersionSummary>(new CommandDefinition(
            """
            SELECT sv.version AS Version, COALESCE(esv.status, 'beta') AS Status,
                   COALESCE(esv.risk_level, 'medium') AS RiskLevel,
                   sv.sha256 AS Digest, sv.published_at AS PublishedAt,
                   COALESCE(esv.changelog, '') AS Changelog, sv.size_bytes AS SizeBytes,
                   (SELECT COUNT(*) FROM skill_files sf WHERE sf.skill_version_id = sv.id) AS FileCount
            FROM skill_versions sv JOIN skills s ON s.id = sv.skill_id
            LEFT JOIN enterprise_skill_version esv ON esv.skill_name = s.name COLLATE NOCASE AND esv.version = sv.version
            WHERE s.name = @name COLLATE NOCASE ORDER BY sv.published_at DESC
            """, new { name = row.Name }, cancellationToken: ct))).ToList();
        var recommended = versions.FirstOrDefault(version => version.Status == "stable")?.Version
            ?? versions.FirstOrDefault(version => version.Status == "beta")?.Version
            ?? row.LatestVersion;
        var manifestJson = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT manifest_json FROM enterprise_skill_version WHERE skill_name = @name COLLATE NOCASE AND version = @version",
            new { name = row.Name, version = recommended }, cancellationToken: ct));
        var permissions = DeserializePermissions(manifestJson);
        IReadOnlyList<EnterpriseSkillFileSummary> files = [];
        if (includeFiles)
        {
            var resourceFiles = (await connection.QueryAsync<EnterpriseSkillFileSummary>(new CommandDefinition(
                """
                SELECT sf.relative_path AS Path, sf.size_bytes AS SizeBytes, sf.sha256 AS Sha256
                FROM skill_files sf JOIN skill_versions sv ON sv.id = sf.skill_version_id
                JOIN skills s ON s.id = sv.skill_id
                WHERE s.name = @name COLLATE NOCASE AND sv.version = @version ORDER BY sf.relative_path
                """, new { name = row.Name, version = recommended }, cancellationToken: ct))).ToList();
            var selectedVersion = versions.First(version => version.Version == recommended);
            files = new List<EnterpriseSkillFileSummary>
            {
                new() { Path = "SKILL.md", SizeBytes = selectedVersion.SizeBytes, Sha256 = selectedVersion.Digest }
            }.Concat(resourceFiles).ToList();
        }
        var reviewHistory = (await connection.QueryAsync<EnterpriseReviewHistory>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.version AS Version, p.submitter_id AS SubmitterId, p.submitter AS Submitter,
                   p.submitted_at AS SubmittedAt, p.state AS State,
                   r.reviewer AS Reviewer, r.decision AS Decision, r.channel AS Channel,
                   r.comment AS Comment, r.decided_at AS DecidedAt
            FROM publication p
            JOIN review r ON r.publication_id = p.id
            WHERE p.skill_name = @name COLLATE NOCASE
            ORDER BY p.submitted_at DESC
            """, new { name = row.Name }, cancellationToken: ct))).ToList();
        return new EnterpriseSkillSummary
        {
            Id = $"skill:{row.Name}", Name = row.Name, Namespace = row.Namespace,
            DisplayName = row.DisplayName, Description = row.Description,
            Owner = new EnterpriseOwner { Id = row.OwnerId, Name = row.OwnerName },
            Category = new EnterpriseCategory { Id = row.CategoryId, Name = row.CategoryName },
            LatestVersion = row.LatestVersion, RecommendedVersion = recommended,
            Status = row.Status, RiskLevel = versions.First(version => version.Version == recommended).RiskLevel,
            Permissions = permissions, Digest = row.Digest,
            UpdatedAt = row.UpdatedAt, Featured = row.Featured, Versions = versions, Files = files,
            ReviewHistory = reviewHistory
        };
    }

    private static async Task InsertAuditAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        EnterpriseSessionUser actor,
        string action,
        string resource,
        string result,
        string requestId,
        CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO audit_event(id, actor, role, action, resource, result, request_id, detail_json, created_at)
            VALUES(@id, @actorName, @role, @action, @resource, @result, @requestId, '{}', @createdAt)
            """, new
            {
                id = $"audit_{Guid.NewGuid():N}", actorName = actor.DisplayName,
                role = actor.Roles.LastOrDefault() ?? "engineer", action, resource, result, requestId,
                createdAt = DateTimeOffset.UtcNow.ToString("O")
            }, transaction, cancellationToken: ct));
    }

    private static EnterprisePermissions DeserializePermissions(string? manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson) || manifestJson == "{}")
            return new EnterprisePermissions();
        try
        {
            return JsonSerializer.Deserialize(
                manifestJson, AgentSkillStoreJsonContext.Default.EnterprisePermissions) ?? new EnterprisePermissions();
        }
        catch (JsonException)
        {
            return new EnterprisePermissions();
        }
    }

    private sealed record EnterpriseSkillRow
    {
        public required string Name { get; init; }
        public required string Namespace { get; init; }
        public required string DisplayName { get; init; }
        public required string Description { get; init; }
        public required string LatestVersion { get; init; }
        public required string Digest { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
        public required string OwnerId { get; init; }
        public required string OwnerName { get; init; }
        public required string CategoryId { get; init; }
        public required string CategoryName { get; init; }
        public required bool Featured { get; init; }
        public required string Status { get; init; }
        public required string RiskLevel { get; init; }
    }

    private sealed record ReviewRow
    {
        public required string Id { get; init; }
        public required string PublicationId { get; init; }
        public required string SkillName { get; init; }
        public required string DisplayName { get; init; }
        public required string Version { get; init; }
        public required string Submitter { get; init; }
        public required string RiskLevel { get; init; }
        public required DateTimeOffset SubmittedAt { get; init; }
        public required string State { get; init; }
        public string? Comment { get; init; }
        public required string ManifestJson { get; init; }
    }

    private sealed record VersionStatusRow
    {
        public required string Version { get; init; }
        public required string Status { get; init; }
    }

    private sealed record PublicationRow
    {
        public required string Id { get; init; }
        public required string SkillName { get; init; }
        public required string Version { get; init; }
        public required string Namespace { get; init; }
        public required string SubmitterId { get; init; }
        public required string Submitter { get; init; }
        public required string State { get; init; }
    }
}
