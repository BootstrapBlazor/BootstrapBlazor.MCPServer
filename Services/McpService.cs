using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.IO;

namespace BootstrapBlazor.McpServer.Services;

/// <summary>
/// Service options for PromptTemplateManager
/// </summary>
public class PromptTemplateManagerOptions
{
    public string ConfigPath { get; set; } = "io/repos/prompt-templates.json";
}

/// <summary>
/// Service for managing prompt templates from configuration
/// </summary>
public class PromptTemplateManager
{
    private readonly string _configPath;
    private Dictionary<string, object> _templates = new();
    private Dictionary<string, string> _frameworkOverrides = new();

    public PromptTemplateManager(IOptions<PromptTemplateManagerOptions> options)
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, options.Value.ConfigPath);
        LoadTemplates();
    }

    private void LoadTemplates()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("promptTemplates", out var pt) && 
                    pt.TryGetProperty("templates", out var templates))
                {
                    foreach (var prop in templates.EnumerateObject())
                    {
                        if (prop.Value.TryGetProperty("template", out var template))
                        {
                            _templates[prop.Name] = template.GetString() ?? "";
                        }
                    }
                }

                if (doc.RootElement.TryGetProperty("promptTemplates", out pt) && 
                    pt.TryGetProperty("frameworkOverrides", out var overrides))
                {
                    foreach (var prop in overrides.EnumerateObject())
                    {
                        _frameworkOverrides[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }
            }
        }
        catch
        {
            // Use default templates if loading fails
        }
    }

    public string GetSystemPrompt(string framework, string componentName, string apiContent, string sampleContent)
    {
        // Try framework-specific override first
        if (_frameworkOverrides.TryGetValue(framework, out var overrideTemplate) && !string.IsNullOrEmpty(overrideTemplate))
        {
            return FillTemplate(overrideTemplate, componentName, apiContent, sampleContent, framework);
        }

        // Try default system prompt template
        if (_templates.TryGetValue("systemPrompt", out var template))
        {
            return FillTemplate(template.ToString(), componentName, apiContent, sampleContent, framework);
        }

        // Fallback to generic prompt
        return $"You are a senior {framework} expert. Answer based on the provided documentation.\n\nAPI:\n{{apiContent}}\n\nExamples:\n{{sampleContent}}";
    }

    /// <summary>
    /// Get system prompt with optional optimized prompts for specific repository
    /// </summary>
    public string GetOptimizedSystemPrompt(string framework, string componentName, string apiContent, string sampleContent, OptimizedPromptTemplate? optimizedPrompts)
    {
        // If optimized prompts exist for this repository, use them
        if (optimizedPrompts != null && !string.IsNullOrEmpty(optimizedPrompts.SystemPrompt))
        {
            return FillTemplate(optimizedPrompts.SystemPrompt, componentName, apiContent, sampleContent, framework);
        }

        // Otherwise use default framework-based prompts
        return GetSystemPrompt(framework, componentName, apiContent, sampleContent);
    }

    private string FillTemplate(string template, string componentName, string apiContent, string sampleContent, string framework)
    {
        return template
            .Replace("{{componentName}}", componentName)
            .Replace("{{apiContent}}", apiContent)
            .Replace("{{sampleContent}}", sampleContent)
            .Replace("{{framework}}", framework);
    }
}

[McpServerToolType]
public class McpService
{
    private readonly DocsExtractorService _extractor;
    private readonly AiIntegrationService _aiService;
    private readonly ILogger<McpService> _logger;
    private readonly AppSettingsManager _settingsManager;
    private readonly RepositoryManager _repositoryManager;
    private readonly PromptTemplateManager _promptManager;

    public McpService(
        DocsExtractorService extractor, 
        AiIntegrationService aiService, 
        ILogger<McpService> logger, 
        AppSettingsManager settingsManager,
        RepositoryManager repositoryManager,
        PromptTemplateManager promptManager)
    {
        _extractor = extractor;
        _aiService = aiService;
        _logger = logger;
        _settingsManager = settingsManager;
        _repositoryManager = repositoryManager;
        _promptManager = promptManager;
    }

    /// <summary>
    /// Arguments for GetComponentList
    /// </summary>
    public class GetComponentListArgs
    {
        /// <summary>
        /// Repository ID to query (optional - if not provided, queries first enabled repository)
        /// </summary>
        public string? RepositoryId { get; set; }
    }

    [McpServerTool]
    public string GetComponentList(GetComponentListArgs args)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] GetComponentList called{RepositoryInfo}",
            correlationId, 
            string.IsNullOrEmpty(args.RepositoryId) ? "" : $" for repository: {args.RepositoryId}");
        
        var (outputDir, error) = GetRepositoryOutputDir(args.RepositoryId, correlationId);
        if (error != null)
        {
            return error;
        }
        
        var apiPath = Path.Combine(outputDir, "API");
        
        if (!Directory.Exists(apiPath))
        {
            _logger.LogWarning(
                "[{CorrelationId}] Documentation directory not found: {ApiPath}",
                correlationId, apiPath);
            return "Documentation not found.";
        }

        var files = Directory.GetFiles(apiPath, "*.md");
        var componentNames = files.Select(Path.GetFileNameWithoutExtension).ToArray();
        
        _logger.LogDebug(
            "[{CorrelationId}] Found {Count} components",
            correlationId, componentNames.Length);
        
        return string.Join("\n", componentNames);
    }

    /// <summary>
    /// Arguments for SearchComponentKeyword
    /// </summary>
    public class SearchComponentArgs
    {
        /// <summary>
        /// Keyword to search for
        /// </summary>
        public string Keyword { get; set; } = string.Empty;
        
        /// <summary>
        /// Repository ID to query (optional - if not provided, queries first enabled repository)
        /// </summary>
        public string? RepositoryId { get; set; }
    }

    [McpServerTool]
    public string SearchComponentKeyword(SearchComponentArgs args)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] SearchComponentKeyword called with keyword: {Keyword}{RepositoryInfo}",
            correlationId, args.Keyword,
            string.IsNullOrEmpty(args.RepositoryId) ? "" : $" for repository: {args.RepositoryId}");
        
        var (outputDir, error) = GetRepositoryOutputDir(args.RepositoryId, correlationId);
        if (error != null)
        {
            return error;
        }
        
        var apiPath = Path.Combine(outputDir, "API");
        
        if (!Directory.Exists(apiPath))
        {
            _logger.LogWarning(
                "[{CorrelationId}] Documentation directory not found: {ApiPath}",
                correlationId, apiPath);
            return "Documentation not found at " + apiPath;
        }

        var files = Directory.GetFiles(apiPath, "*.md");
        var results = new System.Collections.Generic.List<string>();

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            if (content.Contains(args.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        if (results.Count == 0)
        {
            _logger.LogDebug(
                "[{CorrelationId}] No components found matching keyword: {Keyword}",
                correlationId, args.Keyword);
            return $"No components found matching the keyword '{args.Keyword}'.";
        }
        
        _logger.LogDebug(
            "[{CorrelationId}] Found {Count} components matching keyword: {Keyword}",
            correlationId, results.Count, args.Keyword);
        
        return string.Join("\n", results);
    }

    /// <summary>
    /// Arguments for GetComponentDocs
    /// </summary>
    public class GetComponentDocsArgs
    {
        /// <summary>
        /// Component name to get documentation for
        /// </summary>
        public string ComponentName { get; set; } = string.Empty;
        
        /// <summary>
        /// Repository ID to query (optional - if not provided, queries first enabled repository)
        /// </summary>
        public string? RepositoryId { get; set; }
    }

    /// <summary>
    /// The original documentation information (API parameter table + code example) is returned directly based on the component name, without AI processing.
    /// </summary>
    [McpServerTool]
    public string GetComponentDocs(GetComponentDocsArgs args)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] GetComponentDocs called for component: {ComponentName}{RepositoryInfo}",
            correlationId, args.ComponentName,
            string.IsNullOrEmpty(args.RepositoryId) ? "" : $" for repository: {args.RepositoryId}");
        
        var (outputDir, error) = GetRepositoryOutputDir(args.RepositoryId, correlationId);
        if (error != null)
        {
            return error;
        }
        
        var (apiContent, sampleContent, docError) = LoadComponentDocs(outputDir, args.ComponentName, correlationId);
        
        if (docError != null)
        {
            _logger.LogWarning(
                "[{CorrelationId}] Component not found: {ComponentName}, Error: {Error}",
                correlationId, args.ComponentName, docError);
            return docError;
        }

        _logger.LogDebug(
            "[{CorrelationId}] Successfully retrieved docs for component: {ComponentName}",
            correlationId, args.ComponentName);
        
        return $"# Component: {args.ComponentName}\n\n## API\n{apiContent}\n\n## Samples\n{sampleContent}";
    }

    /// <summary>
    /// Arguments for AskComponentExpert
    /// </summary>
    public class AskComponentExpertArgs
    {
        /// <summary>
        /// Component name to ask about
        /// </summary>
        public string ComponentName { get; set; } = string.Empty;
        
        /// <summary>
        /// Question to ask
        /// </summary>
        public string Question { get; set; } = string.Empty;
        
        /// <summary>
        /// Repository ID to query (optional - if not provided, queries first enabled repository)
        /// </summary>
        public string? RepositoryId { get; set; }
    }

    [McpServerTool]
    public async Task<string> AskComponentExpert(AskComponentExpertArgs args)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] AskComponentExpert called for component: {ComponentName}{RepositoryInfo}",
            correlationId, args.ComponentName,
            string.IsNullOrEmpty(args.RepositoryId) ? "" : $" for repository: {args.RepositoryId}");
        
        var settings = _settingsManager.LoadSettings();
        
        var (outputDir, repoError) = GetRepositoryOutputDir(args.RepositoryId, correlationId);
        if (repoError != null)
        {
            return repoError;
        }
        
        var (apiContent, sampleContent, error) = LoadComponentDocs(outputDir, args.ComponentName, correlationId);
        
        if (error != null)
        {
            _logger.LogWarning(
                "[{CorrelationId}] Component not found: {ComponentName}, Error: {Error}",
                correlationId, args.ComponentName, error);
            return error;
        }

        // If AI is not enabled, return directly to the original document.
        if (!settings.AiEnabled)
        {
            _logger.LogInformation(
                "[{CorrelationId}] AI is disabled. Returning raw documentation for component: {ComponentName}",
                correlationId, args.ComponentName);
            
            return $"# Component: {args.ComponentName}\n\n## API\n{apiContent}\n\n## Samples\n{sampleContent}";
        }

        _logger.LogDebug(
            "[{CorrelationId}] Building AI prompt for component: {ComponentName}",
            correlationId, args.ComponentName);
        
        // Get repository for framework info
        var repository = GetRepositoryForQuery(args.RepositoryId, correlationId);
        var framework = repository?.Framework ?? settings.Framework ?? "Blazor";
        
        var systemPrompt = _promptManager.GetSystemPrompt(framework, args.ComponentName, apiContent, sampleContent);

        _logger.LogDebug(
            "[{CorrelationId}] Calling AI service for component: {ComponentName}, Question length: {QuestionLength}",
            correlationId, args.ComponentName, args.Question.Length);
        
        var answer = await _aiService.AskExpertAsync(systemPrompt, args.Question);
        
        _logger.LogDebug(
            "[{CorrelationId}] Received AI response for component: {ComponentName}, Answer length: {AnswerLength}",
            correlationId, args.ComponentName, answer.Length);
        
        return answer;
    }

    /// <summary>
    /// Gets the output directory for a repository, with fallback to legacy settings
    /// </summary>
    private (string? outputDir, string? error) GetRepositoryOutputDir(string? repositoryId, string correlationId)
    {
        // If no repository ID provided, try to get the first enabled repository
        if (string.IsNullOrEmpty(repositoryId))
        {
            var repos = _repositoryManager.GetEnabledRepositories();
            if (repos.Count == 0)
            {
                // Fall back to legacy settings
                _logger.LogDebug(
                    "[{CorrelationId}] No enabled repositories found, falling back to legacy settings",
                    correlationId);
                var settings = _settingsManager.LoadSettings();
                return (settings.OutputDir, null);
            }
            
            repositoryId = repos[0].Id;
            _logger.LogDebug(
                "[{CorrelationId}] Using first enabled repository: {RepositoryId}",
                correlationId, repositoryId);
        }
        
        var repo = _repositoryManager.GetRepository(repositoryId);
        if (repo == null)
        {
            _logger.LogWarning(
                "[{CorrelationId}] Repository not found: {RepositoryId}, falling back to legacy settings",
                correlationId, repositoryId);
            var settings = _settingsManager.LoadSettings();
            return (settings.OutputDir, null);
        }
        
        return (repo.OutputDir, null);
    }

    /// <summary>
    /// Gets the repository for a query, with fallback to legacy settings
    /// </summary>
    private RepositoryInfo? GetRepositoryForQuery(string? repositoryId, string correlationId)
    {
        if (string.IsNullOrEmpty(repositoryId))
        {
            var repos = _repositoryManager.GetEnabledRepositories();
            return repos.Count > 0 ? repos[0] : null;
        }
        
        return _repositoryManager.GetRepository(repositoryId);
    }

    /// <summary>
    /// General method: Loads API documentation and sample code based on the component name.
    /// Returns (apiContent, sampleContent, error), where a non-null error indicates that the component cannot be found.
    /// </summary>
    private (string apiContent, string sampleContent, string? error) LoadComponentDocs(string? outputDir, string componentName, string correlationId)
    {
        if (string.IsNullOrEmpty(outputDir))
        {
            _logger.LogWarning(
                "[{CorrelationId}] Output directory is null or empty",
                correlationId);
            return (string.Empty, string.Empty, "Output directory not configured.");
        }
        
        _logger.LogTrace(
            "[{CorrelationId}] Loading docs for component: {ComponentName}",
            correlationId, componentName);
        
        var apiDir = Path.Combine(outputDir, "API");
        var samplesDir = Path.Combine(outputDir, "Samples");

        var apiFile = Path.Combine(apiDir, $"{componentName}.md");
        var sampleFile = Path.Combine(samplesDir, $"{componentName}.md");

        // If an exact match fails, perform a case-insensitive fuzzy search (compatible with service classes such as AjaxService).
        if (!File.Exists(apiFile) && !File.Exists(sampleFile))
        {
            if (Directory.Exists(apiDir))
            {
                var fuzzyApi = Directory.GetFiles(apiDir, "*.md")
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(componentName, StringComparison.OrdinalIgnoreCase));
                if (fuzzyApi != null)
                {
                    apiFile = fuzzyApi;
                    _logger.LogTrace(
                        "[{CorrelationId}] Found fuzzy API match: {ApiFile}",
                        correlationId, apiFile);
                }
            }

            if (Directory.Exists(samplesDir))
            {
                var fuzzySample = Directory.GetFiles(samplesDir, "*.md")
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(componentName, StringComparison.OrdinalIgnoreCase));
                if (fuzzySample != null)
                {
                    sampleFile = fuzzySample;
                    _logger.LogTrace(
                        "[{CorrelationId}] Found fuzzy sample match: {SampleFile}",
                        correlationId, sampleFile);
                }
            }

            if (!File.Exists(apiFile) && !File.Exists(sampleFile))
            {
                _logger.LogDebug(
                    "[{CorrelationId}] Component not found: {ComponentName}",
                    correlationId, componentName);
                return (string.Empty, string.Empty, $"Component '{componentName}' not found in documentation.");
            }
        }

        if (!Directory.Exists(apiDir))
        {
            _logger.LogWarning(
                "[{CorrelationId}] Documentation directory not found: {ApiDir}",
                correlationId, apiDir);
            return (string.Empty, string.Empty, "Documentation directory not found.");
        }

        var apiContent = File.Exists(apiFile) ? File.ReadAllText(apiFile) : "No API documentation available.";
        var sampleContent = File.Exists(sampleFile) ? File.ReadAllText(sampleFile) : "No sample code available.";
        
        _logger.LogTrace(
            "[{CorrelationId}] Loaded docs for component: {ComponentName}, API length: {ApiLength}, Sample length: {SampleLength}",
            correlationId, componentName, apiContent.Length, sampleContent.Length);
        
        return (apiContent, sampleContent, null);
    }
}
