// -----------------------------------------------------------------------
// <copyright file="EnterpriseEndpoints.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using AgentSkillStore.Server.Data;
using AgentSkillStore.Server.Models;
using AgentSkillStore.Server.Services;

namespace AgentSkillStore.Server;

public static partial class EnterpriseEndpoints
{
    public static void MapEnterpriseApi(this WebApplication app)
    {
        var enterprise = app.MapGroup("/api/enterprise/v1");

        enterprise.MapPost("/session/login", Login).RequireRateLimiting("login");
        enterprise.MapPost("/session/logout", Logout).RequireAuthorization();
        enterprise.MapGet("/session", Session).RequireAuthorization();

        enterprise.MapGet("/skills", ListSkills);
        enterprise.MapGet("/skills/{name}", GetSkill);
        enterprise.MapGet("/skills/{name}/install-reference", GetInstallReference).RequireAuthorization();

        enterprise.MapPost("/publications", CreatePublication).DisableAntiforgery().RequireAuthorization("SkillSubmitter");
        enterprise.MapGet("/publications/mine", ListMyPublications).RequireAuthorization("SkillSubmitter");
        enterprise.MapGet("/reviews", ListReviews).RequireAuthorization("Reviewer");
        enterprise.MapPost("/reviews/{id}/approve", ApproveReview).RequireAuthorization("Reviewer");
        enterprise.MapPost("/reviews/{id}/reject", RejectReview).RequireAuthorization("Reviewer");
        enterprise.MapGet("/audit", ListAudit).RequireAuthorization("AuditReader");
        enterprise.MapGet("/api-keys", ListApiKeys).RequireAuthorization("KeyManager");
        enterprise.MapPost("/api-keys", CreateApiKey).RequireAuthorization("KeyManager");
        enterprise.MapDelete("/api-keys/{id:long}", DeleteApiKey).RequireAuthorization("KeyManager");
        enterprise.MapGet("/teams", ListTeams).RequireAuthorization("PlatformAdmin");
        enterprise.MapPut("/teams/{id}", UpsertTeam).RequireAuthorization("PlatformAdmin");
        enterprise.MapGet("/categories", ListCategories).RequireAuthorization("PlatformAdmin");
        enterprise.MapPut("/categories/{id}", UpsertCategory).RequireAuthorization("PlatformAdmin");

        enterprise.MapGet("/system/status", SystemStatus).RequireAuthorization("PlatformAdmin");
    }

    private static async Task<IResult> Login(
        LocalLoginRequest request,
        LocalAuthService authService,
        EnterpriseRepository repository,
        HttpContext context)
    {
        var user = authService.Authenticate(request.Username, request.Password);
        if (user is null)
        {
            await repository.AppendAuditAsync(new EnterpriseSessionUser
            {
                Id = $"local:{request.Username}", Username = request.Username,
                DisplayName = request.Username, Team = "", Roles = []
            }, "auth.login", "session", "denied", context.TraceIdentifier, context.RequestAborted);
            return Results.Json(new EnterpriseError
            {
                Code = "AUTH_REQUIRED",
                Message = "用户名或密码错误。",
                RequestId = context.TraceIdentifier
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        await repository.AppendAuditAsync(user, "auth.login", "session", "success",
            context.TraceIdentifier, context.RequestAborted);

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            LocalAuthService.CreatePrincipal(user),
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });
        return Results.Ok(user);
    }

    private static async Task<IResult> Logout(HttpContext context, EnterpriseRepository repository)
    {
        await repository.AppendAuditAsync(LocalAuthService.FromPrincipal(context.User), "auth.logout",
            "session", "success", context.TraceIdentifier, context.RequestAborted);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static IResult Session(HttpContext context) =>
        Results.Ok(LocalAuthService.FromPrincipal(context.User));

    private static async Task<IResult> ListSkills(
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct) =>
        Results.Ok(FilterPublicSkills(await repository.GetSkillsAsync(ct), context.User.Identity?.IsAuthenticated == true));

    private static async Task<IResult> GetSkill(
        string name,
        EnterpriseRepository repository,
        SkillRepository skillRepository,
        BlobStorage blobStorage,
        HttpContext context,
        CancellationToken ct)
    {
        var result = await repository.GetSkillAsync(name, ct);
        if (result is null)
            return Results.NotFound();
        if (context.User.Identity?.IsAuthenticated != true)
        {
            result = ToPublicSkill(result);
            if (result is null)
                return Results.NotFound();
        }

        var coreSkill = await skillRepository.GetSkillByNameAsync(name, ct);
        if (coreSkill is null)
            return Results.NotFound();
        var version = await skillRepository.GetVersionAsync(coreSkill.Id, result.RecommendedVersion, ct);
        if (version is null)
            return Results.Ok(result);
        var stream = blobStorage.GetBlob(version.Sha256);
        if (stream is null)
            return Results.Ok(result);
        await using (stream)
        using (var reader = new StreamReader(stream))
        {
            var markdown = await reader.ReadToEndAsync(ct);
            return Results.Ok(result with { SkillMarkdown = markdown });
        }
    }

    private static async Task<IResult> GetInstallReference(
        string name,
        string? version,
        EnterpriseRepository repository,
        SkillRepository skillRepository,
        IConfiguration configuration,
        HttpContext context,
        CancellationToken ct)
    {
        var skill = await repository.GetSkillAsync(name, ct);
        if (skill is null)
            return Results.NotFound();
        var selectedVersion = string.IsNullOrWhiteSpace(version) ? skill.RecommendedVersion : version;
        var selected = skill.Versions.FirstOrDefault(item => item.Version == selectedVersion);
        if (selected is null)
            return Results.NotFound();
        if (selected.Status is "revoked" or "draft" or "pending")
        {
            return Results.Json(new EnterpriseError
            {
                Code = selected.Status == "revoked" ? "VERSION_REVOKED" : "VERSION_NOT_INSTALLABLE",
                Message = selected.Status == "revoked" ? "已撤回版本禁止新装配。" : "草稿或待审核版本不可装配。",
                RequestId = context.TraceIdentifier
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var coreSkill = await skillRepository.GetSkillByNameAsync(name, ct);
        if (coreSkill is null)
            return Results.NotFound();
        var coreVersion = await skillRepository.GetVersionAsync(coreSkill.Id, selected.Version, ct);
        if (coreVersion is null || string.IsNullOrWhiteSpace(coreVersion.ArtifactSha256))
            return Results.NotFound();

        var baseUrl = configuration["AgentSkillStore:BaseUrl"]?.TrimEnd('/') ?? $"{context.Request.Scheme}://{context.Request.Host}";
        return Results.Ok(new InstallReference
        {
            Skill = $"{skill.Namespace}/{skill.Name}",
            Version = selected.Version,
            Digest = coreVersion.ArtifactSha256,
            SourceUrl = $"{baseUrl}/api/v1/skills/{Uri.EscapeDataString(skill.Name)}/{Uri.EscapeDataString(selected.Version)}/archive.zip",
            Status = selected.Status,
            RiskLevel = selected.RiskLevel,
            RequestedPermissions = skill.Permissions
        });
    }

    private static async Task<IResult> CreatePublication(
        [FromForm] IFormFile package,
        [FromForm] string name,
        [FromForm] string displayName,
        [FromForm] string @namespace,
        [FromForm] string version,
        [FromForm] string owner,
        [FromForm] string riskLevel,
        [FromForm] string networkPolicy,
        [FromForm] string changelog,
        EnterprisePackageUploadService packageUploadService,
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct)
    {
        if (!SkillName.TryCreate(name, out var skillName)
            || !SkillVersionString.TryCreate(version, out var skillVersion)
            || !StrictSemVerRegex().IsMatch(version)
            || !SkillName.TryCreate(@namespace, out _)
            || string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(owner)
            || string.IsNullOrWhiteSpace(changelog)
            || riskLevel is not ("low" or "medium" or "high" or "critical")
            || networkPolicy is not ("deny-all" or "allow-list" or "unrestricted"))
        {
            return Results.Json(new EnterpriseError
            {
                Code = "VALIDATION_FAILED",
                Message = "工装名称或版本不符合规范。",
                RequestId = context.TraceIdentifier
            }, statusCode: StatusCodes.Status400BadRequest);
        }
        var upload = await packageUploadService.UploadAsync(package, skillName.Value, skillVersion.Value,
            @namespace, riskLevel, networkPolicy, ct);
        if (!upload.Success)
        {
            var status = upload.IsDuplicateVersion ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity;
            return Results.Json(new EnterpriseError
            {
                Code = upload.IsDuplicateVersion ? "VERSION_CONFLICT" : "PACKAGE_UNSAFE",
                Message = upload.Error ?? "工装包校验失败。",
                RequestId = context.TraceIdentifier
            }, statusCode: status);
        }
        if (upload.EnterprisePermissions is null
            || upload.EnterpriseManifestJson is null
            || !string.Equals(upload.EnterpriseRiskLevel, riskLevel, StringComparison.Ordinal)
            || !string.Equals(upload.EnterprisePermissions.Network.Policy, networkPolicy, StringComparison.Ordinal))
        {
            return Results.Json(new EnterpriseError
            {
                Code = "MANIFEST_MISMATCH",
                Message = "表单中的风险等级或网络策略必须与 manifest.yaml 一致。",
                RequestId = context.TraceIdentifier
            }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        var request = new CreatePublicationRequest
        {
            Name = name,
            DisplayName = displayName,
            Namespace = @namespace,
            Version = version,
            Owner = owner,
            RiskLevel = riskLevel,
            NetworkPolicy = networkPolicy,
            Changelog = changelog
        };
        var actor = LocalAuthService.FromPrincipal(context.User);
        return Results.Created($"/api/enterprise/v1/publications/{name}/{version}",
            await repository.CreatePublicationAsync(request, actor, context.TraceIdentifier,
                upload.Digest!.Value.Value, upload.EnterpriseManifestJson, ct));
    }

    private static async Task<IResult> ListReviews(
        EnterpriseRepository repository,
        CancellationToken ct) =>
        Results.Ok(await repository.GetReviewsAsync(ct));

    private static async Task<IResult> ListMyPublications(
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct) =>
        Results.Ok(await repository.GetPublicationsForSubmitterAsync(
            LocalAuthService.FromPrincipal(context.User).Id,
            ct));

    private static Task<IResult> ApproveReview(
        string id,
        ReviewDecisionRequest request,
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct) =>
        DecideReview(id, request, true, repository, context, ct);

    private static Task<IResult> RejectReview(
        string id,
        ReviewDecisionRequest request,
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct) =>
        DecideReview(id, request, false, repository, context, ct);

    private static async Task<IResult> DecideReview(
        string id,
        ReviewDecisionRequest request,
        bool approved,
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            return Results.Json(new EnterpriseError
            {
                Code = "VALIDATION_FAILED",
                Message = approved ? "审核意见不能为空。" : "拒绝原因不能为空。",
                RequestId = context.TraceIdentifier
            }, statusCode: StatusCodes.Status400BadRequest);
        }
        if (approved && request.Channel is not "beta" and not "stable")
        {
            return Results.Json(new EnterpriseError
            {
                Code = "VALIDATION_FAILED",
                Message = "通过审核必须选择 beta 或 stable 通道。",
                RequestId = context.TraceIdentifier
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var decided = await repository.DecideReviewAsync(id, approved, request,
                LocalAuthService.FromPrincipal(context.User), context.TraceIdentifier, ct);
            return decided ? Results.NoContent() : Results.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Results.Json(new EnterpriseError
            {
                Code = "SEPARATION_OF_DUTIES",
                Message = exception.Message,
                RequestId = context.TraceIdentifier
            }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> ListAudit(
        EnterpriseRepository repository,
        CancellationToken ct) =>
        Results.Ok(await repository.GetAuditAsync(ct));

    private static async Task<IResult> ListApiKeys(ApiKeyService service, CancellationToken ct)
    {
        var keys = await service.ListKeysAsync(ct);
        return Results.Ok(keys.Select(key => new ApiKeySummary
        {
            Id = key.Id,
            Label = key.Label,
            AgentId = key.AgentId,
            MachineHash = key.MachineHash,
            Scopes = ApiKeyService.ParseScopes(key.Scopes),
            CreatedAt = key.CreatedAt,
            ExpiresAt = key.ExpiresAt
        }).ToList());
    }

    private static async Task<IResult> CreateApiKey(
        CreateApiKeyRequest request,
        ApiKeyService service,
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
            return Results.BadRequest();
        var (rawKey, stored) = await service.CreateKeyAsync(
            request.Label,
            request.AgentId,
            request.MachineHash,
            request.Scopes,
            request.ExpiresAt,
            ct);
        await repository.AppendAuditAsync(LocalAuthService.FromPrincipal(context.User), "api-key.create",
            $"api-key:{stored.Id}", "success", context.TraceIdentifier, ct);
        return Results.Ok(new CreateApiKeyResponse
        {
            Id = stored.Id,
            Label = stored.Label,
            Key = rawKey,
            AgentId = stored.AgentId,
            MachineHash = stored.MachineHash,
            Scopes = ApiKeyService.ParseScopes(stored.Scopes),
            CreatedAt = stored.CreatedAt,
            ExpiresAt = stored.ExpiresAt
        });
    }

    private static async Task<IResult> DeleteApiKey(
        long id,
        ApiKeyService service,
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct)
    {
        var deleted = await service.DeleteKeyAsync(id, ct);
        await repository.AppendAuditAsync(LocalAuthService.FromPrincipal(context.User), "api-key.revoke",
            $"api-key:{id}", deleted ? "success" : "failed", context.TraceIdentifier, ct);
        return deleted ? Results.NoContent() : Results.Conflict();
    }

    private static async Task<IResult> ListTeams(EnterpriseRepository repository, CancellationToken ct) =>
        Results.Ok(await repository.GetTeamsAsync(ct));

    private static async Task<IResult> UpsertTeam(
        string id,
        GovernanceTeam team,
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct)
    {
        if (!id.Equals(team.Id, StringComparison.Ordinal))
            return Results.BadRequest();
        await repository.UpsertTeamAsync(team, LocalAuthService.FromPrincipal(context.User),
            context.TraceIdentifier, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListCategories(EnterpriseRepository repository, CancellationToken ct) =>
        Results.Ok(await repository.GetCategoriesAsync(ct));

    private static async Task<IResult> UpsertCategory(
        string id,
        GovernanceCategory category,
        EnterpriseRepository repository,
        HttpContext context,
        CancellationToken ct)
    {
        if (!id.Equals(category.Id, StringComparison.Ordinal))
            return Results.BadRequest();
        await repository.UpsertCategoryAsync(category, LocalAuthService.FromPrincipal(context.User),
            context.TraceIdentifier, ct);
        return Results.NoContent();
    }

    private static IResult SystemStatus() => Results.Ok(new EnterpriseSystemStatus
    {
        Registry = "healthy",
        Storage = "healthy",
        DatabaseSchema = 8,
        Cli = new EnterpriseCliStatus
        {
            Mode = "local",
            SupportedOperations = ["install", "upgrade", "uninstall", "rollback"]
        }
    });

    private static IReadOnlyList<EnterpriseSkillSummary> FilterPublicSkills(
        IReadOnlyList<EnterpriseSkillSummary> skills,
        bool authenticated) =>
        authenticated
            ? skills
            : skills.Select(ToPublicSkill).Where(skill => skill is not null).Select(skill => skill!).ToList();

    private static EnterpriseSkillSummary? ToPublicSkill(EnterpriseSkillSummary skill)
    {
        var installableVersions = skill.Versions
            .Where(version => version.Status is not ("draft" or "pending" or "revoked"))
            .ToList();
        var selected = installableVersions.FirstOrDefault(version => version.Status == "stable")
                       ?? installableVersions.FirstOrDefault(version => version.Status == "beta")
                       ?? installableVersions.FirstOrDefault();
        if (selected is null)
            return null;

        return skill with
        {
            LatestVersion = selected.Version,
            RecommendedVersion = selected.Version,
            Status = selected.Status,
            RiskLevel = selected.RiskLevel,
            Digest = selected.Digest,
            Versions = installableVersions,
            ReviewHistory = skill.ReviewHistory
                .Where(item => installableVersions.Any(version => version.Version == item.Version))
                .ToList()
        };
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$")]
    private static partial Regex StrictSemVerRegex();
}
