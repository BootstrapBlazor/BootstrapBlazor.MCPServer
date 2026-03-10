using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace BootstrapBlazor.McpServer.Services;

/// <summary>
/// Configuration options for RepositoryManager
/// </summary>
public class RepositoryManagerOptions
{
    /// <summary>
    /// Directory path for storing repository data (default: "data")
    /// </summary>
    public string DataDirectory { get; set; } = "data";
    
    /// <summary>
    /// File name for repository storage (default: "mcp-repositories.json")
    /// </summary>
    public string RepositoryFileName { get; set; } = "mcp-repositories.json";
}

/// <summary>
/// Manages repository data in mcp-repositories.json
/// </summary>
public class RepositoryManager
{
    private readonly string _repositoryFilePath;
    private readonly ILogger<RepositoryManager> _logger;
    private readonly AppSettingsManager _settingsManager;
    private List<RepositoryInfo> _repositories = new();
    private readonly object _lock = new();
    private bool _isLoaded = false;

    public RepositoryManager(ILogger<RepositoryManager> logger, AppSettingsManager settingsManager, IOptions<RepositoryManagerOptions> options)
    {
        _logger = logger;
        _settingsManager = settingsManager;

        var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, options.Value.DataDirectory);
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }
        _repositoryFilePath = Path.Combine(dataDir, options.Value.RepositoryFileName);
    }

    /// <summary>
    /// Load all repositories from JSON file
    /// </summary>
    public List<RepositoryInfo> LoadRepositories()
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        lock (_lock)
        {
            _logger.LogDebug(
                "[{CorrelationId}] Loading repositories from storage",
                correlationId);
            
            if (_isLoaded && _repositories.Count > 0)
            {
                _logger.LogTrace(
                    "[{CorrelationId}] Returning cached repositories: {Count}",
                    correlationId, _repositories.Count);
                return _repositories.ToList();
            }

            _repositories.Clear();

            if (!File.Exists(_repositoryFilePath))
            {
                _logger.LogInformation(
                    "[{CorrelationId}] No mcp-repositories.json found. Creating with default repository...",
                    correlationId);
                    
                // Migrate from existing config if available
                _repositories = MigrateFromConfig();
                if (_repositories.Count > 0)
                {
                    SaveRepositories(_repositories);
                    _logger.LogInformation(
                        "[{CorrelationId}] Migrated {Count} repository(s) from config.json",
                        correlationId, _repositories.Count);
                }
                else
                {
                    // Create a default repository
                    _repositories.Add(CreateDefaultRepository());
                    SaveRepositories(_repositories);
                    _logger.LogInformation(
                        "[{CorrelationId}] Created default repository",
                        correlationId);
                }
            }
            else
            {
                try
                {
                    var json = File.ReadAllText(_repositoryFilePath);
                    var root = JsonNode.Parse(json);
                    if (root?["repositories"] is JsonArray reposArray)
                    {
                        foreach (var repoNode in reposArray)
                        {
                            if (repoNode != null)
                            {
                                var repo = ParseRepository(repoNode);
                                _repositories.Add(repo);
                            }
                        }
                    }
                    _logger.LogInformation(
                        "[{CorrelationId}] Loaded {Count} repository(ies) from mcp-repositories.json",
                        correlationId, _repositories.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[{CorrelationId}] Failed to load repositories from JSON. Creating default.",
                        correlationId);
                    _repositories.Add(CreateDefaultRepository());
                    SaveRepositories(_repositories);
                }
            }

            _isLoaded = true;
            return _repositories.ToList();
        }
    }

    /// <summary>
    /// Get repository by ID
    /// </summary>
    public RepositoryInfo? GetRepository(string id)
    {
        var repos = LoadRepositories();
        return repos.FirstOrDefault(r => r.Id == id);
    }

    /// <summary>
    /// Get repository by slug
    /// </summary>
    public RepositoryInfo? GetRepositoryBySlug(string slug)
    {
        var repos = LoadRepositories();
        return repos.FirstOrDefault(r => r.Slug == slug);
    }

    /// <summary>
    /// Add a new repository
    /// </summary>
    public RepositoryInfo AddRepository(RepositoryInfo repository)
    {
        lock (_lock)
        {
            var repos = LoadRepositories();
            
            // Generate slug from name if not provided
            if (string.IsNullOrEmpty(repository.Slug))
            {
                repository.Slug = GenerateSlug(repository.Name);
            }

            // Ensure unique ID
            while (repos.Any(r => r.Id == repository.Id))
            {
                repository.Id = Guid.NewGuid().ToString();
            }

            repository.CreatedAt = DateTime.UtcNow;
            repository.UpdatedAt = DateTime.UtcNow;
            
            repos.Add(repository);
            _repositories = repos;
            SaveRepositories(repos);
            
            _logger.LogInformation("Added repository: {Name} ({Id})", repository.Name, repository.Id);
            return repository;
        }
    }

    /// <summary>
    /// Update an existing repository
    /// </summary>
    public bool UpdateRepository(RepositoryInfo repository)
    {
        lock (_lock)
        {
            var repos = LoadRepositories();
            var index = repos.FindIndex(r => r.Id == repository.Id);
            
            if (index < 0)
            {
                _logger.LogWarning("Repository not found for update: {Id}", repository.Id);
                return false;
            }

            repository.UpdatedAt = DateTime.UtcNow;
            repos[index] = repository;
            _repositories = repos;
            SaveRepositories(repos);
            
            _logger.LogInformation("Updated repository: {Name} ({Id})", repository.Name, repository.Id);
            return true;
        }
    }

    /// <summary>
    /// Delete a repository by ID
    /// </summary>
    public bool DeleteRepository(string id)
    {
        lock (_lock)
        {
            var repos = LoadRepositories();
            var repo = repos.FirstOrDefault(r => r.Id == id);
            
            if (repo == null)
            {
                _logger.LogWarning("Repository not found for deletion: {Id}", id);
                return false;
            }

            repos.Remove(repo);
            _repositories = repos;
            SaveRepositories(repos);
            
            _logger.LogInformation("Deleted repository: {Name} ({Id})", repo.Name, repo.Id);
            return true;
        }
    }

    /// <summary>
    /// Update sync status for a repository
    /// </summary>
    public bool UpdateSyncStatus(string id, SyncStatus status, string? error = null)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        lock (_lock)
        {
            var repos = LoadRepositories();
            var repo = repos.FirstOrDefault(r => r.Id == id);
            
            if (repo == null)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Repository not found for sync status update: {Id}",
                    correlationId, id);
                return false;
            }

            var previousStatus = repo.SyncStatus;
            repo.SyncStatus = status;
            repo.UpdatedAt = DateTime.UtcNow;

            if (status == SyncStatus.Success)
            {
                repo.LastSyncAt = DateTime.UtcNow;
                repo.LastSyncError = null;
                _logger.LogInformation(
                    "[{CorrelationId}] [{RepoId}] Sync status changed: {PreviousStatus} -> {NewStatus}",
                    correlationId, id, previousStatus, status);
            }
            else if (status == SyncStatus.Failed)
            {
                repo.LastSyncError = error;
                _logger.LogWarning(
                    "[{CorrelationId}] [{RepoId}] Sync status changed: {PreviousStatus} -> {NewStatus}, Error: {Error}",
                    correlationId, id, previousStatus, status, error ?? "Unknown");
            }
            else if (status == SyncStatus.Running)
            {
                _logger.LogDebug(
                    "[{CorrelationId}] [{RepoId}] Sync status changed: {PreviousStatus} -> {NewStatus}",
                    correlationId, id, previousStatus, status);
            }

            _repositories = repos;
            SaveRepositories(repos);
            
            return true;
        }
    }

    /// <summary>
    /// Get all enabled repositories
    /// </summary>
    public List<RepositoryInfo> GetEnabledRepositories()
    {
        return LoadRepositories().Where(r => r.IsEnabled).ToList();
    }

    /// <summary>
    /// Save repositories to JSON file
    /// </summary>
    private void SaveRepositories(List<RepositoryInfo> repositories)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        try
        {
            var root = new JsonObject
            {
                ["repositories"] = new JsonArray(repositories.Select(r => SerializeRepository(r)).ToArray())
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_repositoryFilePath, root.ToJsonString(options));
            
            _logger.LogTrace(
                "[{CorrelationId}] Saved {Count} repositories to mcp-repositories.json",
                correlationId, repositories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{CorrelationId}] Failed to save repositories to JSON",
                correlationId);
        }
    }

    /// <summary>
    /// Parse repository from JSON node
    /// </summary>
    private RepositoryInfo ParseRepository(JsonNode node)
    {
        return new RepositoryInfo
        {
            Id = node["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString(),
            Name = node["name"]?.GetValue<string>() ?? "Unknown",
            Slug = node["slug"]?.GetValue<string>() ?? "",
            Url = node["url"]?.GetValue<string>() ?? "",
            Provider = Enum.TryParse<GitProvider>(node["provider"]?.GetValue<string>(), out var provider) ? provider : GitProvider.Other,
            ExtractorType = Enum.TryParse<ExtractorType>(node["extractorType"]?.GetValue<string>(), out var extractorType) ? extractorType : ExtractorType.BlazorComponent,
            Framework = node["framework"]?.GetValue<string>() ?? "Blazor",
            LocalPath = node["localPath"]?.GetValue<string>() ?? "",
            OutputDir = node["outputDir"]?.GetValue<string>() ?? "",
            CronSchedule = node["cronSchedule"]?.GetValue<string>() ?? "0 */4 * * *",
            TargetFramework = node["targetFramework"]?.GetValue<string>(),
            BuildProjectPath = node["buildProjectPath"]?.GetValue<string>(),
            DocsJsonPath = node["docsJsonPath"]?.GetValue<string>(),
            XmlPath = node["xmlPath"]?.GetValue<string>(),
            SamplesPath = node["samplesPath"]?.GetValue<string>(),
            IsEnabled = node["isEnabled"]?.GetValue<bool>() ?? true,
            SyncStatus = Enum.TryParse<SyncStatus>(node["syncStatus"]?.GetValue<string>(), out var status) ? status : SyncStatus.NeverRun,
            LastSyncAt = node["lastSyncAt"]?.GetValue<DateTime?>(),
            LastSyncError = node["lastSyncError"]?.GetValue<string>(),
            CreatedAt = node["createdAt"]?.GetValue<DateTime>() ?? DateTime.UtcNow,
            UpdatedAt = node["updatedAt"]?.GetValue<DateTime>() ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// Serialize repository to JSON node
    /// </summary>
    private JsonNode SerializeRepository(RepositoryInfo repo)
    {
        return new JsonObject
        {
            ["id"] = repo.Id,
            ["name"] = repo.Name,
            ["slug"] = repo.Slug,
            ["url"] = repo.Url,
            ["provider"] = repo.Provider.ToString(),
            ["extractorType"] = repo.ExtractorType.ToString(),
            ["framework"] = repo.Framework,
            ["localPath"] = repo.LocalPath,
            ["outputDir"] = repo.OutputDir,
            ["cronSchedule"] = repo.CronSchedule,
            ["targetFramework"] = repo.TargetFramework,
            ["buildProjectPath"] = repo.BuildProjectPath,
            ["docsJsonPath"] = repo.DocsJsonPath,
            ["xmlPath"] = repo.XmlPath,
            ["samplesPath"] = repo.SamplesPath,
            ["isEnabled"] = repo.IsEnabled,
            ["syncStatus"] = repo.SyncStatus.ToString(),
            ["lastSyncAt"] = repo.LastSyncAt?.ToString("o"),
            ["lastSyncError"] = repo.LastSyncError,
            ["createdAt"] = repo.CreatedAt.ToString("o"),
            ["updatedAt"] = repo.UpdatedAt.ToString("o")
        };
    }

    /// <summary>
    /// Migrate from existing config.json
    /// </summary>
    private List<RepositoryInfo> MigrateFromConfig()
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var repos = new List<RepositoryInfo>();
        
        _logger.LogDebug(
            "[{CorrelationId}] Attempting to migrate from config.json",
            correlationId);
        
        try
        {
            var settings = _settingsManager.LoadSettings();
            
            if (!string.IsNullOrEmpty(settings.RepositoryUrl))
            {
                var repo = new RepositoryInfo
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = ExtractRepoName(settings.RepositoryUrl),
                    Slug = GenerateSlug(ExtractRepoName(settings.RepositoryUrl)),
                    Url = settings.RepositoryUrl,
                    Provider = DetectProvider(settings.RepositoryUrl),
                    ExtractorType = ExtractorType.BlazorComponent,
                    Framework = settings.Framework ?? "Blazor",
                    LocalPath = settings.LocalPath,
                    OutputDir = settings.OutputDir,
                    CronSchedule = settings.CronSchedule,
                    IsEnabled = true,
                    SyncStatus = SyncStatus.NeverRun
                };
                repos.Add(repo);
                _logger.LogInformation(
                    "[{CorrelationId}] Migrated repository from config: {Name} ({Url})",
                    correlationId, repo.Name, repo.Url);
            }
            else
            {
                _logger.LogDebug(
                    "[{CorrelationId}] No repository URL in config, skipping migration",
                    correlationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[{CorrelationId}] Failed to migrate from config, will create default",
                correlationId);
        }

        return repos;
    }

    /// <summary>
    /// Create default repository
    /// </summary>
    private RepositoryInfo CreateDefaultRepository()
    {
        return new RepositoryInfo
        {
            Id = Guid.NewGuid().ToString(),
            Name = "BootstrapBlazor",
            Slug = "bootstrap-blazor",
            Url = "https://gitee.com/LongbowEnterprise/BootstrapBlazor.git",
            Provider = GitProvider.Gitee,
            ExtractorType = ExtractorType.BlazorComponent,
            Framework = "Blazor",
            LocalPath = "/app/data/BootstrapBlazorRepo",
            OutputDir = "/app/data/OutputRAG",
            CronSchedule = "0 3 * * *",
            TargetFramework = "net10.0",
            BuildProjectPath = "src/BootstrapBlazor/BootstrapBlazor.csproj",
            IsEnabled = true,
            SyncStatus = SyncStatus.NeverRun
        };
    }

    /// <summary>
    /// Generate URL-safe slug from name
    /// </summary>
    private string GenerateSlug(string name)
    {
        if (string.IsNullOrEmpty(name)) return "repository";
        
        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace(".", "-")
            .Replace("_", "-");
    }

    /// <summary>
    /// Extract repository name from URL
    /// </summary>
    private string ExtractRepoName(string url)
    {
        if (string.IsNullOrEmpty(url)) return "Repository";
        
        var lastPart = url.TrimEnd('/').Split('/').Last();
        if (lastPart.EndsWith(".git"))
        {
            lastPart = lastPart[..^4];
        }
        return lastPart;
    }

    /// <summary>
    /// Detect git provider from URL
    /// </summary>
    private GitProvider DetectProvider(string url)
    {
        if (url.Contains("github.com")) return GitProvider.GitHub;
        if (url.Contains("gitee.com")) return GitProvider.Gitee;
        if (url.Contains("gitlab.com")) return GitProvider.GitLab;
        return GitProvider.Other;
    }

    /// <summary>
    /// Force reload from file
    /// </summary>
    public void Reload()
    {
        lock (_lock)
        {
            _isLoaded = false;
            _repositories.Clear();
            LoadRepositories();
        }
    }
}
