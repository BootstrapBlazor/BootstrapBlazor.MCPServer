// Copyright (c) BootstrapBlazor & Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://www.blazor.zone

using BootstrapBlazor.McpServer.Services;
using Coravel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(); // Must restore basic logging if docker container is inspecting

// Add Services
builder.Services.AddSingleton<DocsExtractorService>();
builder.Services.AddTransient<GitSyncInvocable>();
builder.Services.AddSingleton<McpService>();
builder.Services.AddHttpClient<AiIntegrationService>();
builder.Services.AddSingleton<AppSettingsManager>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
// Add localization
builder.Services.AddLocalization();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Inject BootstrapBlazor with Localization
builder.Services.AddBootstrapBlazor();

builder.Services.AddScheduler();
var mcpBuilder = builder.Services.AddMcpServer();

// Disable Stdio transport if running in Docker container, as it will crash Kestrel
// on startup due to immediate EOF on empty standard input.
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    mcpBuilder.WithStdioServerTransport();
}

mcpBuilder.WithHttpTransport()       // Native MCP SSE support via ASP.NET Core
          .WithToolsFromAssembly(); // Registers RAGService tools

var app = builder.Build();

// Configure the localization middleware
var supportedCultures = new[] { "en-US", "zh-CN" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Configure Coravel Cron Job
app.Services.UseScheduler(scheduler =>
{
    var settingsManager = app.Services.GetRequiredService<AppSettingsManager>();
    var cronPattern = settingsManager.LoadSettings().CronSchedule;
    scheduler.Schedule<GitSyncInvocable>().Cron(cronPattern);
});

// REST HTTP API Endpoints for external agents/LLMs (Dify, FastGPT, etc.)
app.MapGet("/api/components", (McpService mcp) =>
{
    return Results.Ok(mcp.GetComponentList());
});

app.MapGet("/api/components/search", (string keyword, McpService mcp) =>
{
    var content = mcp.SearchComponentKeyword(new McpService.SearchComponentArgs { Keyword = keyword });
    if (content.Contains("No components found")) return Results.NotFound(content);
    return Results.Ok(content.Split('\n'));
});

app.MapGet("/api/components/{name}/ask", async (string name, string q, McpService mcp) =>
{
    var answer = await mcp.AskComponentExpert(new McpService.AskComponentExpertArgs { ComponentName = name, Question = q });
    if (answer.Contains("not found")) return Results.NotFound(answer);
    return Results.Text(answer, "text/markdown");
});

app.MapGet("/api/components/{name}/docs", (string name, McpService mcp) =>
{
    var docs = mcp.GetComponentDocs(new McpService.GetComponentDocsArgs { ComponentName = name });
    if (docs.Contains("not found") || docs.Contains("not found")) return Results.NotFound(docs);
    return Results.Text(docs, "text/markdown");
});

// Authentication Endpoint for Ajax Login
app.MapPost("/api/login", async (HttpContext context, AppSettingsManager config, string username, string password) =>
{
    var settings = config.LoadSettings();
    if (username == settings.AdminUsername && password == settings.AdminPassword)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, username) };
        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));
        return Results.Ok();
    }
    return Results.Unauthorized();
});

// Logout endpoint
app.MapPost("/api/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});

app.MapRazorComponents<BootstrapBlazor.McpServer.Components.App>()
   .AddInteractiveServerRenderMode();

app.MapMcp("/mcp");

// Avoid port conflicts, run via Stdio but also spin up Kestrel quietly
// In some environments, starting both might cause issues, but builder.Run() operates on console
await app.RunAsync();
