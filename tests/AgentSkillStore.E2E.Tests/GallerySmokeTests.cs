// -----------------------------------------------------------------------
// <copyright file="GallerySmokeTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AgentSkillStore.Server.Services;
using Xunit;

namespace AgentSkillStore.E2E.Tests;

public sealed class AppFixture : IAsyncLifetime
{
    private readonly StringBuilder _serverLogs = new();
    private Process? _serverProcess;
    private HttpClient? _httpClient;
    private string? _dataPath;

    public HttpClient Client => _httpClient
        ?? throw new InvalidOperationException("HttpClient not initialized");

    public string ServiceEndpoint { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _dataPath = Path.Combine(Path.GetTempPath(), $"agent-skill-store-e2e-{Guid.NewGuid():N}");
        var serverAssemblyPath = typeof(ApiKeyService).Assembly.Location;
        var serverDirectory = Path.GetDirectoryName(serverAssemblyPath)
                              ?? throw new InvalidOperationException("Unable to locate the server output directory.");
        var port = GetAvailablePort();
        ServiceEndpoint = $"http://127.0.0.1:{port}";
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = serverDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(serverAssemblyPath);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = ServiceEndpoint;
        startInfo.Environment["AGENTSKILLSTORE__BASEURL"] = ServiceEndpoint;
        startInfo.Environment["AGENTSKILLSTORE__DATAPATH"] = _dataPath;
        startInfo.Environment["AGENTSKILLSTORE__SEEDDATA"] = "true";
        startInfo.Environment["AGENTSKILLSTORE__SEEDPATH"] = Path.Combine(serverDirectory, "seed");

        _serverProcess = Process.Start(startInfo)
                         ?? throw new InvalidOperationException("Unable to start AgentSkillStore.Server.");
        _serverProcess.OutputDataReceived += (_, eventArgs) => AppendServerLog(eventArgs.Data);
        _serverProcess.ErrorDataReceived += (_, eventArgs) => AppendServerLog(eventArgs.Data);
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(ServiceEndpoint),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_serverProcess.HasExited)
                break;
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

        var exitDetail = _serverProcess.HasExited ? $" Exit code: {_serverProcess.ExitCode}." : "";
        var detail = _serverLogs.Length == 0 ? "No server logs were captured." : _serverLogs.ToString();
        throw new TimeoutException(
            $"Server at {ServiceEndpoint} did not become ready within 30 seconds.{exitDetail}{Environment.NewLine}{detail}");
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        if (_serverProcess is not null)
        {
            if (!_serverProcess.HasExited)
                _serverProcess.Kill(entireProcessTree: true);
            await _serverProcess.WaitForExitAsync();
            _serverProcess.Dispose();
        }
        if (_dataPath is not null && Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private void AppendServerLog(string? line)
    {
        if (line is null)
            return;
        lock (_serverLogs)
        {
            _serverLogs.AppendLine(line);
        }
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
