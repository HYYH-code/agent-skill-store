// -----------------------------------------------------------------------
// <copyright file="GalleryScreenshotTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Microsoft.Playwright;
using Xunit;

namespace AgentSkillStore.E2E.Tests;

[Collection("App")]
public sealed class ResponsiveScreenshotTests : IAsyncLifetime
{
    private readonly AppFixture _fixture;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public ResponsiveScreenshotTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    [Theory]
    [InlineData("desktop", "/", 1440, 900)]
    [InlineData("tablet", "/skills", 1024, 768)]
    [InlineData("mobile", "/about", 390, 844)]
    public async Task PrimaryRoutes_FitViewportAndShowBrand(
        string name,
        string path,
        int width,
        int height)
    {
        await CaptureAndAssertAsync(name, path, width, height, ".app-shell");
    }

    [Theory]
    [InlineData("login-desktop", 1440, 900)]
    [InlineData("login-tablet", 1024, 768)]
    [InlineData("login-mobile", 390, 844)]
    public async Task LoginPage_FitsViewportAndShowsBrand(string name, int width, int height)
    {
        await CaptureAndAssertAsync(name, "/login", width, height, ".login-page");
    }

    private async Task CaptureAndAssertAsync(
        string name,
        string path,
        int width,
        int height,
        string rootSelector)
    {
        var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        var page = await context.NewPageAsync();
        var consoleErrors = new List<string>();
        var failedRequests = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.StartsWith("Failed to load resource:", StringComparison.Ordinal))
                consoleErrors.Add(message.Text);
        };
        page.RequestFailed += (_, request) => failedRequests.Add($"{request.Method} {request.Url}");
        page.Response += (_, response) =>
        {
            var expectedAnonymousSession = response.Status == 401
                && response.Url.EndsWith("/api/enterprise/v1/session", StringComparison.Ordinal);
            if (response.Status >= 400 && !expectedAnonymousSession)
                failedRequests.Add($"{response.Status} {response.Url}");
        };

        await page.GotoAsync(
            $"{_fixture.ServiceEndpoint}{path}",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(rootSelector).WaitForAsync();

        var hasHorizontalOverflow = await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > window.innerWidth + 1");
        var logo = page.Locator("img[alt^='Agent Skill Store']:visible").First;
        Assert.True(await logo.IsVisibleAsync());
        Assert.False(hasHorizontalOverflow);
        Assert.Empty(consoleErrors);
        Assert.Empty(failedRequests);

        var screenshotPath = Path.Combine(Path.GetTempPath(), $"agent-skill-store-{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        Assert.True(new FileInfo(screenshotPath).Length > 0);
        await context.CloseAsync();
    }
}
