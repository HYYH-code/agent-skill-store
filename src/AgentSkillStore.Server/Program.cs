// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;
using AgentSkillStore.Server;
using AgentSkillStore.Server.Data;
using AgentSkillStore.Server.Models;
using AgentSkillStore.Server.Options;
using AgentSkillStore.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization (AOT-compatible)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AgentSkillStoreJsonContext.Default);
});

// Add OpenAPI
builder.Services.AddOpenApi();
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection(BrandingOptions.SectionName));
builder.Services.Configure<LocalAuthOptions>(builder.Configuration.GetSection(LocalAuthOptions.SectionName));
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "AgentSkillStoreAuth";
        options.DefaultChallengeScheme = "AgentSkillStoreAuth";
    })
    .AddPolicyScheme("AgentSkillStoreAuth", "Local session or API Key", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? ApiKeyAuthenticationHandler.SchemeName
                : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "agent-skill-store.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SkillSubmitter", policy => policy.RequireAssertion(context =>
        context.User.IsInRole("platform_admin")
        || context.User.IsInRole("author")
        || HasScope(context.User, "skills:submit")))
    .AddPolicy("KeyManager", policy => policy.RequireAssertion(context =>
        context.User.IsInRole("platform_admin") || HasScope(context.User, "keys:manage")))
    .AddPolicy("Author", policy => policy.RequireAssertion(context =>
        context.User.IsInRole("platform_admin")
        || context.User.IsInRole("author")
        || HasScope(context.User, "skills:submit")))
    .AddPolicy("Reviewer", policy => policy.RequireRole("platform_admin", "review_admin"))
    .AddPolicy("AuditReader", policy => policy.RequireRole("platform_admin"))
    .AddPolicy("PlatformAdmin", policy => policy.RequireRole("platform_admin"));
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("login", limiter =>
{
    limiter.PermitLimit = 10;
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.QueueLimit = 0;
    limiter.AutoReplenishment = true;
}));

// Add services
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<BlobStorage>();
builder.Services.AddSingleton<SkillRepository>();
builder.Services.AddSingleton<EnterpriseRepository>();
builder.Services.AddSingleton<SubAgentRepository>();
builder.Services.AddSingleton<ApiKeyRepository>();
builder.Services.AddSingleton<IndexGenerator>();
builder.Services.AddSingleton<NativeManifestGenerator>();
builder.Services.AddSingleton<SkillUploadService>();
builder.Services.AddSingleton<SubAgentUploadService>();
builder.Services.AddSingleton<SkillArchiveBackfillService>();
builder.Services.AddSingleton<ApiKeyService>();
builder.Services.AddSingleton<SeedDataService>();
builder.Services.AddSingleton<LocalAuthService>();
builder.Services.AddSingleton<EnterprisePackageUploadService>();

var app = builder.Build();

if (app.Environment.IsProduction())
{
    var baseUrl = app.Configuration["AgentSkillStore:BaseUrl"] ?? "";
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl)
        || parsedBaseUrl.IsLoopback)
        throw new InvalidOperationException("Production requires a non-loopback AGENTSKILLSTORE__BASEURL.");

    var authOptions = app.Services.GetRequiredService<IOptions<LocalAuthOptions>>().Value;
    if (authOptions.Mode.Equals("none", StringComparison.OrdinalIgnoreCase) || authOptions.Users.Count == 0)
        throw new InvalidOperationException("Production requires at least one local authenticated user.");
    if (authOptions.Users.Any(user => user.Password.Length < 12))
        throw new InvalidOperationException("Production local account passwords must contain at least 12 characters.");
}

// Initialize database
var dbInitializer = app.Services.GetRequiredService<DatabaseInitializer>();
await dbInitializer.InitializeAsync();

var archiveBackfillService = app.Services.GetRequiredService<SkillArchiveBackfillService>();
await archiveBackfillService.BackfillAsync();

// Seed skills and sub-agents from files
var seedService = app.Services.GetRequiredService<SeedDataService>();
await seedService.SeedAsync();

// Seed API key from environment variable
var apiKeyService = app.Services.GetRequiredService<ApiKeyService>();
await apiKeyService.SeedFromEnvironmentAsync();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Map API endpoints (must be before fallback and static files)
app.MapAgentSkillStoreEndpoints();

app.UseDefaultFiles();
app.UseStaticFiles();

// Serve the React + TypeScript Console shell for client-side routes.
app.MapFallbackToFile("index.html");

app.Run();

static bool HasScope(ClaimsPrincipal user, string scope) =>
    user.FindAll("scope")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Any(value => value.Equals(scope, StringComparison.Ordinal));

public partial class Program;
