// -----------------------------------------------------------------------
// <copyright file="DirectPublishEndpointFilter.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Server.Models;

namespace AgentSkillStore.Server;

/// <summary>
/// Keeps the upstream direct-write API available only as an explicit compatibility mode.
/// Governed submissions must use the enterprise publication endpoint and review workflow.
/// </summary>
public sealed class DirectPublishEndpointFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (configuration.GetValue<bool>("AgentSkillStore:Compatibility:AllowDirectPublish"))
            return next(context);

        return ValueTask.FromResult<object?>(Results.Json(
            new ErrorResponse
            {
                Error = "direct_publish_disabled",
                Message = "Direct publishing is disabled. Submit a governed ZIP package through /api/enterprise/v1/publications."
            },
            AgentSkillStoreJsonContext.Default.ErrorResponse,
            statusCode: StatusCodes.Status403Forbidden));
    }
}
