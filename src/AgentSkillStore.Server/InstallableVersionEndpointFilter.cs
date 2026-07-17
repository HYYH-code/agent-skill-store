// -----------------------------------------------------------------------
// <copyright file="InstallableVersionEndpointFilter.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Server.Data;
using AgentSkillStore.Server.Models;

namespace AgentSkillStore.Server;

/// <summary>
/// Prevents direct use of the upstream download endpoints from bypassing
/// enterprise publication state checks.
/// </summary>
public sealed class InstallableVersionEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var name = context.HttpContext.Request.RouteValues["name"]?.ToString();
        var version = context.HttpContext.Request.RouteValues["version"]?.ToString();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
            return Results.NotFound();

        var repository = context.HttpContext.RequestServices.GetRequiredService<EnterpriseRepository>();
        if (await repository.IsVersionInstallableAsync(name, version, context.HttpContext.RequestAborted))
            return await next(context);

        return Results.Json(
            new EnterpriseError
            {
                Code = "VERSION_NOT_INSTALLABLE",
                Message = "该版本不存在，或当前状态不允许下载与装配。",
                RequestId = context.HttpContext.TraceIdentifier
            },
            AgentSkillStoreJsonContext.Default.EnterpriseError,
            statusCode: StatusCodes.Status409Conflict);
    }
}
