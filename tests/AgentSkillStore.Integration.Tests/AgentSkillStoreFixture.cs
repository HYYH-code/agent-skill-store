// -----------------------------------------------------------------------
// <copyright file="AgentSkillStoreFixture.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Microsoft.AspNetCore.Mvc.Testing;
using AgentSkillStore.Client;
using Xunit;

namespace AgentSkillStore.Integration.Tests;

public sealed class AgentSkillStoreFixture : IAsyncLifetime
{
    public const string TestApiKey = "sk-test-integration-key-12345";

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _httpClient;
    private HttpClient? _authHttpClient;
    private AgentSkillStoreClient? _client;
    private AgentSkillStoreClient? _authClient;

    public HttpClient HttpClient => _httpClient
        ?? throw new InvalidOperationException("HttpClient not initialized");

    public HttpClient AuthenticatedHttpClient => _authHttpClient
        ?? throw new InvalidOperationException("Authenticated HttpClient not initialized");

    public AgentSkillStoreClient Client => _client
        ?? throw new InvalidOperationException("Client not initialized");

    public AgentSkillStoreClient AuthenticatedClient => _authClient
        ?? throw new InvalidOperationException("AuthenticatedClient not initialized");

    public IServiceProvider Services => _factory?.Services
        ?? throw new InvalidOperationException("Factory not initialized");

    public HttpClient CreateClient() =>
        _factory?.CreateClient() ?? throw new InvalidOperationException("Factory not initialized");

    public ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>();
        _httpClient = _factory.CreateClient();

        _authHttpClient = _factory.CreateClient();
        _authHttpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestApiKey);

        _client = new AgentSkillStoreClient(_httpClient);
        _authClient = new AgentSkillStoreClient(_authHttpClient);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        _authHttpClient?.Dispose();
        _client?.Dispose();
        _authClient?.Dispose();

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }
}

[CollectionDefinition("AgentSkillStore")]
public class AgentSkillStoreCollection : ICollectionFixture<AgentSkillStoreFixture>;
