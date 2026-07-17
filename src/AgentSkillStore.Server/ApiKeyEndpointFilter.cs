// -----------------------------------------------------------------------
// <copyright file="ApiKeyEndpointFilter.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Server.Models;
using AgentSkillStore.Server.Services;
using System.Security.Claims;

namespace AgentSkillStore.Server;

public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var apiKeyService = context.HttpContext.RequestServices.GetRequiredService<ApiKeyService>();

        var authHeader = context.HttpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(authHeader))
        {
            return Results.Json(
                new ErrorResponse
                {
                    Error = "unauthorized",
                    Message = "API key required. Provide via 'Authorization: Bearer <key>' header."
                },
                AgentSkillStoreJsonContext.Default.ErrorResponse,
                statusCode: 401);
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new ErrorResponse
                {
                    Error = "unauthorized",
                    Message = "Unsupported authorization scheme. Use 'Authorization: Bearer <key>'."
                },
                AgentSkillStoreJsonContext.Default.ErrorResponse,
                statusCode: 401);
        }

        var rawKey = authHeader["Bearer ".Length..].Trim();

        if (string.IsNullOrEmpty(rawKey))
        {
            return Results.Json(
                new ErrorResponse
                {
                    Error = "unauthorized",
                    Message = "API key required. Provide via 'Authorization: Bearer <key>' header."
                },
                AgentSkillStoreJsonContext.Default.ErrorResponse,
                statusCode: 401);
        }

        var apiKey = await apiKeyService.ValidateAndGetKeyAsync(rawKey, context.HttpContext.RequestAborted);
        if (apiKey is null)
        {
            return Results.Json(
                new ErrorResponse
                {
                    Error = "forbidden",
                    Message = "Invalid or expired API key."
                },
                AgentSkillStoreJsonContext.Default.ErrorResponse,
                statusCode: 403);
        }

        context.HttpContext.User = CreatePrincipal(apiKey);
        return await next(context);
    }

    private static ClaimsPrincipal CreatePrincipal(ApiKey apiKey)
    {
        var scopes = ApiKeyService.ParseScopes(apiKey.Scopes);
        var identity = new ClaimsIdentity("agent-skill-store-api-key");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, $"agent:{apiKey.AgentId}"));
        identity.AddClaim(new Claim(ClaimTypes.Name, apiKey.AgentId));
        identity.AddClaim(new Claim("display_name", $"Agent Key: {apiKey.Label}"));
        identity.AddClaim(new Claim("team", "agent"));
        identity.AddClaim(new Claim("agent_id", apiKey.AgentId));
        identity.AddClaim(new Claim("machine_hash", apiKey.MachineHash));
        identity.AddClaim(new Claim(ClaimTypes.Role, "agent"));
        identity.AddClaim(new Claim("scope", string.Join(' ', scopes)));
        foreach (var scope in scopes)
            identity.AddClaim(new Claim("scope", scope));
        return new ClaimsPrincipal(identity);
    }
}
