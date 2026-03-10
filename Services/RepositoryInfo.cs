namespace BootstrapBlazor.McpServer.Services;

/// <summary>
/// Git hosting provider
/// </summary>
public enum GitProvider
{
    GitHub,
    Gitee,
    GitLab,
    Other
}

/// <summary>
/// Repository synchronization status
/// </summary>
public enum SyncStatus
{
    NeverRun,
    Running,
    Success,
    Failed
}

/// <summary>
/// Extractor type for documentation extraction
/// </summary>
public enum ExtractorType
{
    AutoDetect,
    BlazorComponent,
    DotNetLibrary,
    MarkdownDocs,
    CustomScript
}

/// <summary>
/// Repository information model
/// </summary>
public class RepositoryInfo
{
    /// <summary>
    /// Unique identifier (UUID)
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL-safe identifier
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Git repository URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Git hosting provider
    /// </summary>
    public GitProvider Provider { get; set; } = GitProvider.Gitee;

    /// <summary>
    /// Extractor type for documentation extraction
    /// </summary>
    public ExtractorType ExtractorType { get; set; } = ExtractorType.BlazorComponent;

    /// <summary>
    /// Framework name (for AI prompts)
    /// </summary>
    public string Framework { get; set; } = "Blazor";

    /// <summary>
    /// Local clone directory path
    /// </summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>
    /// RAG output directory
    /// </summary>
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>
    /// Cron schedule for sync
    /// </summary>
    public string CronSchedule { get; set; } = "0 */4 * * *";

    /// <summary>
    /// Target .NET framework version (e.g., "net10.0")
    /// </summary>
    public string? TargetFramework { get; set; }

    /// <summary>
    /// Relative path to the project file to build (e.g., "src/BootstrapBlazor/BootstrapBlazor.csproj")
    /// </summary>
    public string? BuildProjectPath { get; set; }

    /// <summary>
    /// Override extraction source path for docs.json (e.g., "src/MyProject/docs.json")
    /// </summary>
    public string? DocsJsonPath { get; set; }

    /// <summary>
    /// Override extraction source path for XML documentation
    /// </summary>
    public string? XmlPath { get; set; }

    /// <summary>
    /// Override extraction source path for samples directory
    /// </summary>
    public string? SamplesPath { get; set; }

    /// <summary>
    /// Whether repository is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Current synchronization status
    /// </summary>
    public SyncStatus SyncStatus { get; set; } = SyncStatus.NeverRun;

    /// <summary>
    /// Last successful sync timestamp
    /// </summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>
    /// Last sync error message
    /// </summary>
    public string? LastSyncError { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Root object for mcp-repositories.json
/// </summary>
public class RepositoryCollection
{
    /// <summary>
    /// List of repositories
    /// </summary>
    public List<RepositoryInfo> Repositories { get; set; } = new();
}
