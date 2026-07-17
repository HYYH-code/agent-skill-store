// -----------------------------------------------------------------------
// <copyright file="ApiKeyAuthenticationHandler.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using AgentSkillStore.Server.Services;

namespace AgentSkillStore.Server;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AgentSkillStoreApiKey";
    private readonly ApiKeyService _apiKeyService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService apiKeyService)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        var rawKey = authorization["Bearer ".Length..].Trim();
        var apiKey = string.IsNullOrWhiteSpace(rawKey)
            ? null
            : await _apiKeyService.ValidateAndGetKeyAsync(rawKey, Context.RequestAborted);
        if (apiKey is null)
            return AuthenticateResult.Fail("Invalid API key.");

        var scopes = ApiKeyService.ParseScopes(apiKey.Scopes);
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, $"agent:{apiKey.AgentId}"),
            new Claim(ClaimTypes.Name, apiKey.AgentId),
            new Claim("display_name", $"Agent Key: {apiKey.Label}"),
            new Claim("team", "agent"),
            new Claim("agent_id", apiKey.AgentId),
            new Claim("machine_hash", apiKey.MachineHash),
            new Claim(ClaimTypes.Role, "agent"),
            new Claim("scope", string.Join(' ', scopes))
        ], SchemeName);
        foreach (var scope in scopes)
            identity.AddClaim(new Claim("scope", scope));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
