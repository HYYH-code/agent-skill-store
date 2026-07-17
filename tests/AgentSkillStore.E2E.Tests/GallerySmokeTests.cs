// -----------------------------------------------------------------------
// <copyright file="GallerySmokeTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace AgentSkillStore.E2E.Tests;

public sealed class AppFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private HttpClient? _httpClient;
    private string? _dataPath;

    public HttpClient Client => _httpClient
        ?? throw new InvalidOperationException("HttpClient not initialized");

    public string ServiceEndpoint { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _dataPath = Path.Combine(Path.GetTempPath(), $"agent-skill-store-e2e-{Guid.NewGuid():N}");
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AgentSkillStore_Server>(
            [$"--AgentSkillStore:DataPath={_dataPath}"]);
        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _app.ResourceNotifications.WaitForResourceAsync(
            "agent-skill-store",
            KnownResourceStates.Running,
            startupTimeout.Token);

        var endpoint = _app.GetEndpoint("agent-skill-store", "http");
        ServiceEndpoint = endpoint.ToString().TrimEnd('/');
        _httpClient = new HttpClient
        {
            BaseAddress = endpoint,
            Timeout = TimeSpan.FromSeconds(2)
        };

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await _httpClient.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // The distributed app is still starting.
            }
            catch (OperationCanceledException)
            {
                // The proxy is accepting connections before the app is ready.
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Server at {endpoint} did not become ready within 30 seconds.");
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
        if (_dataPath is not null && Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }
}

[CollectionDefinition("App")]
public sealed class AppCollection : ICollectionFixture<AppFixture>;

[Collection("App")]
public sealed class AppSmokeTests
{
    private readonly AppFixture _fixture;

    public AppSmokeTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/skills")]
    [InlineData("/about")]
    public async Task SpaRoutes_ServeAgentSkillStoreShell(string path)
    {
        var response = await _fixture.Client.GetAsync(path, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("<title>Agent Skill Store</title>", html);
        Assert.Contains("Discover, govern, version, and install skills for AI agents.", html);
    }

    [Theory]
    [InlineData("/assets/agent-skill-store-mark.svg")]
    [InlineData("/assets/agent-skill-store-lockup.svg")]
    [InlineData("/assets/login-visual.webp")]
    public async Task BrandAssets_ArePublished(string path)
    {
        using var response = await _fixture.Client.GetAsync(path, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Content.Headers.ContentLength > 0);
    }

    [Fact]
    public async Task PublicSkillsApi_ReturnsSeededSkills()
    {
        using var response = await _fixture.Client.GetAsync("/api/v1/skills", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("code-review-checklist", json);
    }
}
