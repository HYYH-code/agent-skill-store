// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Microsoft.Extensions.DependencyInjection;

namespace AgentSkillStore.Client;

/// <summary>
/// Extension methods for registering AgentSkillStoreClient with DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentSkillStoreClient(
        this IServiceCollection services,
        string serverUrl)
    {
        return services.AddAgentSkillStoreClient(serverUrl, apiKey: null);
    }

    /// <summary>
    /// Adds AgentSkillStoreClient to the service collection with an API key.
    /// </summary>
    public static IServiceCollection AddAgentSkillStoreClient(
        this IServiceCollection services,
        string serverUrl,
        string? apiKey)
    {
        services.AddHttpClient<AgentSkillStoreClient>(client =>
        {
            client.BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrEmpty(apiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        });

        return services;
    }

    /// <summary>
    /// Adds AgentSkillStoreClient to the service collection with configuration.
    /// </summary>
    public static IServiceCollection AddAgentSkillStoreClient(
        this IServiceCollection services,
        Action<HttpClient> configureClient)
    {
        services.AddHttpClient<AgentSkillStoreClient>(configureClient);
        return services;
    }
}
