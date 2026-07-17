// -----------------------------------------------------------------------
// <copyright file="LocalAuthService.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using AgentSkillStore.Server.Models;
using AgentSkillStore.Server.Options;

namespace AgentSkillStore.Server.Services;

public sealed class LocalAuthService
{
    private readonly LocalAuthOptions _options;

    public LocalAuthService(IOptions<LocalAuthOptions> options)
    {
        _options = options.Value;
    }

    public EnterpriseSessionUser? Authenticate(string username, string password)
    {
        var configured = _options.Users.FirstOrDefault(user =>
            user.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (configured is null || !SecureEquals(configured.Password, password))
            return null;

        return new EnterpriseSessionUser
        {
            Id = $"local:{configured.Username.ToLowerInvariant()}",
            Username = configured.Username,
            DisplayName = string.IsNullOrWhiteSpace(configured.DisplayName)
                ? configured.Username
                : configured.DisplayName,
            Team = configured.Team,
            Roles = configured.Roles,
            Scopes = []
        };
    }

    public static ClaimsPrincipal CreatePrincipal(EnterpriseSessionUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Username),
            new("display_name", user.DisplayName),
            new("team", user.Team)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "skillstore-local"));
    }

    public static EnterpriseSessionUser FromPrincipal(ClaimsPrincipal principal)
    {
        return new EnterpriseSessionUser
        {
            Id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown",
            Username = principal.Identity?.Name ?? "unknown",
            DisplayName = principal.FindFirst("display_name")?.Value ?? principal.Identity?.Name ?? "unknown",
            Team = principal.FindFirst("team")?.Value ?? "",
            Roles = principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToList(),
            Scopes = principal.FindAll("scope")
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
    }

    private static bool SecureEquals(string expected, string actual)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
