// -----------------------------------------------------------------------
// <copyright file="SkillSubmitScopeEndpointFilter.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using AgentSkillStore.Server.Models;

namespace AgentSkillStore.Server;

public sealed class SkillSubmitScopeEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var hasScope = context.HttpContext.User.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(scope => scope.Equals("skills:submit", StringComparison.Ordinal));

        if (!hasScope)
        {
            return Results.Json(
                new ErrorResponse
                {
                    Error = "forbidden",
                    Message = "Skill publishing requires the skills:submit scope."
                },
                AgentSkillStoreJsonContext.Default.ErrorResponse,
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
