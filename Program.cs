using BootstrapBlazor.McpServer.Services;
using Coravel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for file-based logging with rotation
var logsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "io", "logs");
if (!Directory.Exists(logsPath))
{
    Directory.CreateDirectory(logsPath);
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "BootstrapBlazor.MCPServer")
    .Enrich.WithProperty("MachineName", Environment.MachineName)
    .WriteTo.File(
        new CompactJsonFormatter(),
        Path.Combine(logsPath, "log.json"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true)
    .WriteTo.File(
        Path.Combine(logsPath, "log.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Use Serilog as the logging provider
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

Log.Information("BootstrapBlazor MCP Server starting...");

// Add Services
builder.Services.AddSingleton<DocsExtractorService>();
builder.Services.Configure<DocsExtractorOptions>(builder.Configuration.GetSection("DocsExtractor"));
builder.Services.AddTransient<GitSyncInvocable>();
builder.Services.AddSingleton<McpService>();
builder.Services.AddHttpClient<AiIntegrationService>();
builder.Services.AddSingleton<AppSettingsManager>();

// NEW: Repository management services
builder.Services.Configure<RepositoryManagerOptions>(builder.Configuration.GetSection("RepositoryManager"));
builder.Services.AddSingleton<RepositoryManager>();
builder.Services.AddSingleton<RepositoryService>();

// NEW: Prompt template manager with configuration options
builder.Services.Configure<PromptTemplateManagerOptions>(builder.Configuration.GetSection("PromptTemplates"));
builder.Services.AddSingleton<PromptTemplateManager>();

// NEW: Prompt optimization service
builder.Services.AddHttpClient<PromptOptimizationService>();

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
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" || OperatingSystem.IsWindows())
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

// Start background timer for SyncContext cleanup to prevent memory leaks
var syncCleanupTimer = new System.Threading.Timer(_ => 
{
    try
    {
        BootstrapBlazor.McpServer.Services.SyncContext.CleanupOldOperations();
    }
    catch (Exception ex)
    {
        // Log but don't throw - timer callbacks should not propagate exceptions
        System.Diagnostics.Debug.WriteLine($"SyncContext cleanup error: {ex.Message}");
    }
}, 
null,
TimeSpan.FromHours(1),  // Initial delay
TimeSpan.FromHours(1));  // Period

// Register timer disposal on application shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() => syncCleanupTimer?.Dispose());

// Configure Coravel Cron Job
app.Services.UseScheduler(scheduler =>
{
    var settingsManager = app.Services.GetRequiredService<AppSettingsManager>();
    var repositoryManager = app.Services.GetRequiredService<RepositoryManager>();
    
    // Load cron schedule from first enabled repository or use default
    var repos = repositoryManager.GetEnabledRepositories();
    var cronPattern = repos.FirstOrDefault()?.CronSchedule ?? settingsManager.LoadSettings().CronSchedule;
    
    scheduler.Schedule<GitSyncInvocable>().Cron(cronPattern);
});

// REST HTTP API Endpoints for external agents/LLMs (Dify, FastGPT, etc.)
app.MapGet("/api/components", (McpService mcp, string? repositoryId = null) =>
{
    return Results.Ok(mcp.GetComponentList(new McpService.GetComponentListArgs { RepositoryId = repositoryId }));
});

app.MapGet("/api/components/search", (string keyword, string? repositoryId, McpService mcp) =>
{
    var content = mcp.SearchComponentKeyword(new McpService.SearchComponentArgs { Keyword = keyword, RepositoryId = repositoryId });
    if (content.Contains("No components found")) return Results.NotFound(content);
    return Results.Ok(content.Split('\n'));
});

app.MapGet("/api/components/{name}/ask", async (string name, string q, string? repositoryId, McpService mcp) =>
{
    var answer = await mcp.AskComponentExpert(new McpService.AskComponentExpertArgs { ComponentName = name, Question = q, RepositoryId = repositoryId });
    if (answer.Contains("not found")) return Results.NotFound(answer);
    return Results.Text(answer, "text/markdown");
});

app.MapGet("/api/components/{name}/docs", (string name, string? repositoryId, McpService mcp) =>
{
    var docs = mcp.GetComponentDocs(new McpService.GetComponentDocsArgs { ComponentName = name, RepositoryId = repositoryId });
    if (docs.Contains("not found") || docs.Contains("not found")) return Results.NotFound(docs);
    return Results.Text(docs, "text/markdown");
});

// NEW: Repository Management API Endpoints
app.MapGet("/api/repositories", (RepositoryService repoService) =>
{
    return Results.Ok(repoService.GetAll());
});

app.MapGet("/api/repositories/{id}", (string id, RepositoryService repoService) =>
{
    var repo = repoService.GetById(id);
    if (repo == null) return Results.NotFound($"Repository with ID '{id}' not found");
    return Results.Ok(repo);
});

app.MapPost("/api/repositories", (RepositoryService repoService, RepositoryInfo repository) =>
{
    var added = repoService.Add(repository);
    return Results.Created($"/api/repositories/{added.Id}", added);
});

app.MapPut("/api/repositories/{id}", (string id, RepositoryService repoService, RepositoryInfo repository) =>
{
    if (repository.Id != id)
    {
        return Results.BadRequest("Repository ID mismatch");
    }
    var updated = repoService.Update(repository);
    if (!updated) return Results.NotFound($"Repository with ID '{id}' not found");
    return Results.Ok(repository);
});

app.MapDelete("/api/repositories/{id}", (string id, RepositoryService repoService) =>
{
    var deleted = repoService.Delete(id);
    if (!deleted) return Results.NotFound($"Repository with ID '{id}' not found");
    return Results.NoContent();
});

app.MapPost("/api/repositories/{id}/sync", async (string id, RepositoryService repoService, GitSyncInvocable invocable) =>
{
    var repo = repoService.GetById(id);
    if (repo == null) return Results.NotFound($"Repository with ID '{id}' not found");
    
    // Set the repository ID and invoke sync
    invocable.RepositoryId = id;
    await invocable.Invoke();
    
    // Get updated status
    var updatedRepo = repoService.GetById(id);
    return Results.Ok(updatedRepo);
});

// NEW: Prompt optimization endpoint
app.MapPost("/api/repositories/{id}/optimize-prompts", async (string id, RepositoryService repoService, PromptOptimizationService promptService) =>
{
    var repo = repoService.GetById(id);
    if (repo == null) return Results.NotFound($"Repository with ID '{id}' not found");
    
    var result = await promptService.OptimizePromptsForRepositoryAsync(repo);
    if (!result.Success) return Results.BadRequest(result.ErrorMessage);
    
    return Results.Ok(new
    {
        Success = true,
        SystemPrompt = result.SystemPrompt,
        ComponentQueryTemplate = result.ComponentQueryTemplate,
        ExpertQueryTemplate = result.ExpertQueryTemplate,
        BestPractices = result.BestPractices
    });
});

// NEW: Get optimized prompts
app.MapGet("/api/repositories/{id}/prompts", (string id, RepositoryService repoService, PromptOptimizationService promptService) =>
{
    var repo = repoService.GetById(id);
    if (repo == null) return Results.NotFound($"Repository with ID '{id}' not found");
    
    var prompts = promptService.LoadOptimizedPrompts(repo.Slug);
    if (prompts == null) return Results.NotFound("No optimized prompts found for this repository");
    
    return Results.Ok(prompts);
});

// Authentication Endpoint for Ajax Login
app.MapPost("/api/login", async (HttpContext context, AppSettingsManager config, [FromBody] LoginRequest request) =>
{
    var settings = config.LoadSettings();
    if (request.Username == settings.AdminUsername && request.Password == settings.AdminPassword)
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, request.Username) };
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

// Ensure logs are flushed on shutdown
Log.CloseAndFlush();

// Login request DTO
public record LoginRequest(string Username, string Password);
