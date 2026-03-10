using Coravel.Invocable;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace BootstrapBlazor.McpServer.Services;

public class GitSyncInvocable : IInvocable
{
    private readonly ILogger<GitSyncInvocable> _logger;
    private readonly DocsExtractorService _extractorService;
    private readonly RepositoryManager _repositoryManager;
    
    /// <summary>
    /// Repository ID to sync (optional - if null, syncs all enabled repositories)
    /// </summary>
    public string? RepositoryId { get; set; }

    public GitSyncInvocable(
        ILogger<GitSyncInvocable> logger,
        DocsExtractorService extractorService,
        RepositoryManager repositoryManager)
    {
        _logger = logger;
        _extractorService = extractorService;
        _repositoryManager = repositoryManager;
    }

    public async Task Invoke()
    {
        var globalCorrelationId = Guid.NewGuid().ToString("N")[..16];
        _logger.LogInformation(
            "[{CorrelationId}] ============================================\n" +
            "[{CorrelationId}] Starting Git Sync & Extraction Job...\n" +
            "[{CorrelationId}] ============================================",
            globalCorrelationId, globalCorrelationId);

        try
        {
            // Determine which repositories to sync
            List<RepositoryInfo> reposToSync;
            
            if (!string.IsNullOrEmpty(RepositoryId))
            {
                // Sync specific repository by ID
                var repo = _repositoryManager.GetRepository(RepositoryId);
                if (repo == null)
                {
                    _logger.LogError(
                        "[{CorrelationId}] Repository not found with ID: {RepositoryId}",
                        globalCorrelationId, RepositoryId);
                    return;
                }
                if (!repo.IsEnabled)
                {
                    _logger.LogWarning(
                        "[{CorrelationId}] Repository {Name} is disabled, skipping sync",
                        globalCorrelationId, repo.Name);
                    return;
                }
                reposToSync = new List<RepositoryInfo> { repo };
            }
            else
            {
                // Sync all enabled repositories
                reposToSync = _repositoryManager.GetEnabledRepositories();
                _logger.LogInformation(
                    "[{CorrelationId}] Found {Count} enabled repository(ies) to sync",
                    globalCorrelationId, reposToSync.Count);
            }

            foreach (var repo in reposToSync)
            {
                await SyncRepository(repo, globalCorrelationId);
            }

            _logger.LogInformation(
                "[{CorrelationId}] ============================================\n" +
                "[{CorrelationId}] Git Sync & Extraction Job completed successfully\n" +
                "[{CorrelationId}] ============================================",
                globalCorrelationId, globalCorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{CorrelationId}] Fatal error occurred during Git Sync & Extraction",
                globalCorrelationId);
            
            // Try to log sync failure for all enabled repos
            try
            {
                var repos = _repositoryManager.GetEnabledRepositories();
                foreach (var repo in repos)
                {
                    _repositoryManager.UpdateSyncStatus(repo.Id, SyncStatus.Failed, 
                        $"Global sync error: {ex.Message}");
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    private async Task SyncRepository(RepositoryInfo repo, string parentCorrelationId)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..16];
        var syncContext = SyncContext.StartOperation(repo.Id, "RepositorySync");
        
        _logger.LogInformation(
            "[{CorrelationId}] ============================================\n" +
            "[{CorrelationId}] [{RepositoryId}] Starting sync for repository: {Name} ({Url})\n" +
            "[{CorrelationId}] ============================================",
            correlationId, correlationId, repo.Id, repo.Name, repo.Url);
        
        // Update sync status to Running
        _repositoryManager.UpdateSyncStatus(repo.Id, SyncStatus.Running);
        _logger.LogDebug(
            "[{CorrelationId}] [{RepositoryId}] Sync status updated to Running",
            correlationId, repo.Id);

        var syncStartTime = DateTime.UtcNow;
        string? lastError = null;

        try
        {
            var repoUrl = repo.Url;
            var basePath = repo.LocalPath;
            var outputDir = repo.OutputDir;

            // Clone or Pull
            if (!Directory.Exists(basePath) || !Repository.IsValid(basePath))
            {
                _logger.LogInformation(
                    "[{CorrelationId}] [{RepositoryId}] Cloning repository from {Url} to {Path}...",
                    correlationId, repo.Id, repoUrl, basePath);
                
                if (!Directory.Exists(basePath))
                {
                    Directory.CreateDirectory(basePath);
                    _logger.LogDebug(
                        "[{CorrelationId}] [{RepositoryId}] Created directory: {Path}",
                        correlationId, repo.Id, basePath);
                }

                Repository.Clone(repoUrl, basePath);
                
                _logger.LogInformation(
                    "[{CorrelationId}] [{RepositoryId}] Repository cloned successfully",
                    correlationId, repo.Id);
            }
            else
            {
                _logger.LogInformation(
                    "[{CorrelationId}] [{RepositoryId}] Repository exists at {Path}. Pulling latest changes...",
                    correlationId, repo.Id, basePath);
                
                using var gitRepo = new Repository(basePath);
                Commands.Pull(gitRepo, new Signature("RAGServer", "rag@local", DateTimeOffset.Now), new PullOptions());
                
                _logger.LogInformation(
                    "[{CorrelationId}] [{RepositoryId}] Repository pulled latest changes successfully",
                    correlationId, repo.Id);
            }

            // Build the project
            _logger.LogInformation(
                "[{CorrelationId}] [{RepositoryId}] Building BootstrapBlazor project to generate DLL and XML...",
                correlationId, repo.Id);
            
            var buildResult = await ExecuteBuildAsync(basePath, correlationId, repo.Id, repo.TargetFramework, repo.BuildProjectPath);
            
            if (!buildResult.Success)
            {
                lastError = buildResult.ErrorMessage;
                _logger.LogError(
                    "[{CorrelationId}] [{RepositoryId}] Build failed with exit code {ExitCode}: {ErrorMessage}",
                    correlationId, repo.Id, buildResult.ExitCode, buildResult.ErrorMessage);
                
                _repositoryManager.UpdateSyncStatus(repo.Id, SyncStatus.Failed, 
                    $"Build failed with exit code {buildResult.ExitCode}");
                
                SyncContext.CompleteOperation(syncContext.CorrelationId, false, 
                    $"Build failed: {buildResult.ErrorMessage}");
                
                _logger.LogSyncComplete(correlationId, repo.Id, "RepositorySync", 
                    false, DateTime.UtcNow - syncStartTime, $"Build failed: {buildResult.ErrorMessage}");
                return;
            }

            _logger.LogInformation(
                "[{CorrelationId}] [{RepositoryId}] Build completed successfully",
                correlationId, repo.Id);

            // Execute the extraction
            _logger.LogInformation(
                "[{CorrelationId}] [{RepositoryId}] Starting documentation extraction to {OutputDir}...",
                correlationId, repo.Id, outputDir);
            
            _extractorService.Extract(basePath, outputDir);
            
            _logger.LogInformation(
                "[{CorrelationId}] [{RepositoryId}] Documentation extraction completed",
                correlationId, repo.Id);

            // Update sync status to Success
            _repositoryManager.UpdateSyncStatus(repo.Id, SyncStatus.Success);
            
            var duration = DateTime.UtcNow - syncStartTime;
            SyncContext.CompleteOperation(syncContext.CorrelationId, true);
            
            _logger.LogSyncComplete(correlationId, repo.Id, "RepositorySync", 
                true, duration);
            
            _logger.LogInformation(
                "[{CorrelationId}] ============================================\n" +
                "[{CorrelationId}] [{RepositoryId}] Repository {Name} synced successfully in {Duration}ms\n" +
                "[{CorrelationId}] ============================================",
                correlationId, correlationId, repo.Id, repo.Name, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            
            _logger.LogError(ex,
                "[{CorrelationId}] [{RepositoryId}] Error syncing repository {Name}",
                correlationId, repo.Id, repo.Name);
            
            _repositoryManager.UpdateSyncStatus(repo.Id, SyncStatus.Failed, ex.Message);
            
            SyncContext.CompleteOperation(syncContext.CorrelationId, false, ex.Message);
            
            _logger.LogSyncComplete(correlationId, repo.Id, "RepositorySync", 
                false, DateTime.UtcNow - syncStartTime, ex.Message);
            
            _logger.LogWarning(
                "[{CorrelationId}] [{RepositoryId}] Sync failed: {Error}",
                correlationId, repo.Id, ex.Message);
        }
    }

    private async Task<BuildResult> ExecuteBuildAsync(string basePath, string correlationId, string repoId, string? framework, string? buildProjectPath)
    {
        var tf = framework ?? "net10.0";
        
        // Use configurable build project path, with fallback to common defaults
        var projectPath = buildProjectPath ?? FindProjectToBuild(basePath);
        if (string.IsNullOrEmpty(projectPath))
        {
            return new BuildResult
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = "Could not find a .csproj file to build in the repository"
            };
        }
        
        _logger.LogDebug(
            "[{CorrelationId}] [{RepositoryId}] Executing dotnet build for {ProjectPath}...",
            correlationId, repoId, projectPath);
        
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release -f {tf} /p:LangVersion=preview /p:RunTargetFramework=bypass",
            WorkingDirectory = basePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        using (var process = new System.Diagnostics.Process { StartInfo = psi })
        {
            process.OutputDataReceived += (sender, e) => 
            { 
                if (!string.IsNullOrEmpty(e.Data)) 
                {
                    outputBuilder.AppendLine(e.Data);
                    _logger.LogDebug(
                        "[{CorrelationId}] [{RepositoryId}] [BUILD] {Output}",
                        correlationId, repoId, e.Data);
                }
            };
            
            process.ErrorDataReceived += (sender, e) => 
            { 
                if (!string.IsNullOrEmpty(e.Data)) 
                {
                    errorBuilder.AppendLine(e.Data);
                    _logger.LogWarning(
                        "[{CorrelationId}] [{RepositoryId}] [BUILD ERROR] {Error}",
                        correlationId, repoId, e.Data);
                }
            };

            _logger.LogDebug(
                "[{CorrelationId}] [{RepositoryId}] Build process started",
                correlationId, repoId);
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            
            var exitCode = process.ExitCode;
            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            _logger.LogDebug(
                "[{CorrelationId}] [{RepositoryId}] Build process completed with exit code {ExitCode}",
                correlationId, repoId, exitCode);

            return new BuildResult
            {
                Success = exitCode == 0,
                ExitCode = exitCode,
                Output = output,
                ErrorMessage = error
            };
        }
    }

    private class BuildResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Find a .csproj file to build in the repository
    /// </summary>
    private string? FindProjectToBuild(string basePath)
    {
        // Common project file patterns to search for
        var patterns = new[]
        {
            "src/**/*.csproj",
            "*.csproj",
            "**/*.csproj"
        };

        foreach (var pattern in patterns)
        {
            try
            {
                var matches = Directory.GetFiles(basePath, pattern, SearchOption.AllDirectories)
                    .Where(f => !f.Contains("obj" + Path.DirectorySeparatorChar) && !f.Contains("bin" + Path.DirectorySeparatorChar))
                    .OrderBy(f => f.Length)
                    .ToList();

                if (matches.Count > 0)
                {
                    // Prefer Main/Entry point projects
                    var mainProject = matches.FirstOrDefault(f => 
                        f.Contains("BootstrapBlazor", StringComparison.OrdinalIgnoreCase) ||
                        f.Contains("Server", StringComparison.OrdinalIgnoreCase) ||
                        f.Contains("MCP", StringComparison.OrdinalIgnoreCase)) ?? matches.First();

                    _logger.LogDebug("Found project to build: {ProjectPath}", mainProject);
                    return mainProject;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching for projects with pattern {Pattern}", pattern);
            }
        }

        return null;
    }
}
