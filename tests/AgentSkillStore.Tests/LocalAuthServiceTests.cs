// -----------------------------------------------------------------------
// <copyright file="LocalAuthServiceTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;
using AgentSkillStore.Server.Options;
using AgentSkillStore.Server.Services;
using Xunit;

namespace AgentSkillStore.Tests;

public sealed class LocalAuthServiceTests
{
    [Fact]
    public void Authenticate_ValidCredentials_ReturnsConfiguredRoles()
    {
        var service = CreateService();

        var user = service.Authenticate("admin", "correct-password");

        Assert.NotNull(user);
        Assert.Contains("platform_admin", user.Roles);
        Assert.Equal("平台管理员", user.DisplayName);
    }

    [Fact]
    public void Authenticate_InvalidPassword_ReturnsNull()
    {
        var service = CreateService();

        var user = service.Authenticate("admin", "wrong-password");

        Assert.Null(user);
    }

    [Fact]
    public void Principal_RoundTripsSessionUser()
    {
        var service = CreateService();
        var user = service.Authenticate("admin", "correct-password")!;

        var principal = LocalAuthService.CreatePrincipal(user);
        var restored = LocalAuthService.FromPrincipal(principal);

        Assert.Equal(user.Username, restored.Username);
        Assert.Equal(user.Team, restored.Team);
        Assert.Equal(user.Roles, restored.Roles);
    }

    private static LocalAuthService CreateService()
    {
        return new LocalAuthService(Microsoft.Extensions.Options.Options.Create(new LocalAuthOptions
        {
            Users =
            [
                new LocalUserOptions
                {
                    Username = "admin",
                    Password = "correct-password",
                    DisplayName = "平台管理员",
                    Team = "Platform Team",
                    Roles = ["platform_admin"]
                }
            ]
        }));
    }
}
