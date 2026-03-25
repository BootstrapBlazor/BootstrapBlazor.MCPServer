// Copyright (c) BootstrapBlazor & Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://www.blazor.zone

using BootstrapBlazor.McpServer.Services;
using Coravel;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Log startup information
Console.WriteLine("[BootstrapBlazor] ================================================");
Console.WriteLine("[BootstrapBlazor] BootstrapBlazor MCP Server Starting");
Console.WriteLine("[BootstrapBlazor] ================================================");
Console.WriteLine($"[BootstrapBlazor] Base Path: {PathHelper.GetBasePath()}");
Console.WriteLine($"[BootstrapBlazor] Data Path: {PathHelper.GetDataPath()}");
Console.WriteLine("[BootstrapBlazor] ================================================");

// Add Services
builder.Services.AddSingleton<SyncStatusService>();
builder.Services.AddSingleton<DocsExtractorService>();
builder.Services.AddTransient<GitSyncInvocable>();
builder.Services.AddSingleton<McpService>();
builder.Services.AddHttpClient<AiIntegrationService>();
builder.Services.AddSingleton<AppSettingsManager>();

// Add localization
builder.Services.AddLocalization();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Inject BootstrapBlazor with Localization
builder.Services.AddBootstrapBlazor();

builder.Services.AddScheduler();
var mcpBuilder = builder.Services.AddMcpServer();

// Disable Stdio transport if running in Docker container or development mode,
// as it will crash Kestrel on startup due to immediate EOF on empty standard input.
var isContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
var disableStdio = isContainer || Environment.GetEnvironmentVariable("DISABLE_STDIO") == "true";
if (!disableStdio)
{
    mcpBuilder.WithStdioServerTransport();
}

mcpBuilder.WithHttpTransport()       // Native MCP SSE support via ASP.NET Core
          .WithToolsFromAssembly(); // Registers RAGService tools

var app = builder.Build();

// Configure the localization middleware - default to English only
var supportedCultures = new[] { "en-US" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

app.UseStaticFiles();
app.UseRouting();

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

app.MapRazorComponents<BootstrapBlazor.McpServer.Components.App>()
   .AddInteractiveServerRenderMode();

app.MapMcp("/mcp");

Console.WriteLine("[BootstrapBlazor] MCP Server initialization complete");
Console.WriteLine("[BootstrapBlazor] ================================================");

// Avoid port conflicts, run via Stdio but also spin up Kestrel quietly
// In some environments, starting both might cause issues, but builder.Run() operates on console
await app.RunAsync();
