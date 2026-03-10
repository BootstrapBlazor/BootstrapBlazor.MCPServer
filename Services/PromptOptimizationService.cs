using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BootstrapBlazor.McpServer.Services;

/// <summary>
/// Service for optimizing AI prompts using web search and AI model calls
/// </summary>
public class PromptOptimizationService
{
    private readonly HttpClient _httpClient;
    private readonly AppSettingsManager _settingsManager;
    private readonly ILogger<PromptOptimizationService> _logger;
    private readonly string _configPath;
    private readonly string _templatesPath;

    public PromptOptimizationService(
        HttpClient httpClient,
        AppSettingsManager settingsManager,
        ILogger<PromptOptimizationService> logger)
    {
        _httpClient = httpClient;
        _settingsManager = settingsManager;
        _logger = logger;
        
        // Set HTTP timeout to prevent indefinite hangs
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        
        var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "io", "repos");
        _templatesPath = Path.Combine(basePath, "prompt-templates.json");
        _configPath = basePath;
    }

    /// <summary>
    /// Optimize prompts for a repository using AI with web search
    /// </summary>
    public async Task<OptimizedPrompts> OptimizePromptsForRepositoryAsync(RepositoryInfo repository)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogInformation(
            "[{CorrelationId}] Starting prompt optimization for repository: {RepoName}",
            correlationId, repository.Name);

        var settings = _settingsManager.LoadSettings();
        
        if (string.IsNullOrEmpty(settings.AiApiKey) || settings.AiApiKey == "YOUR_API_KEY_HERE")
        {
            _logger.LogWarning(
                "[{CorrelationId}] AI API Key not configured, skipping prompt optimization",
                correlationId);
            return new OptimizedPrompts { Success = false, ErrorMessage = "AI API Key not configured" };
        }

        try
        {
            // Build context for the AI model
            var context = BuildRepositoryContext(repository);
            
            // Create web search query to find best practices
            var searchQuery = BuildSearchQuery(repository);
            
            // Call AI with web search to get optimized prompts
            var optimizedContent = await CallAIWithWebSearchAsync(
                settings.AiBaseUrl,
                settings.AiApiKey,
                settings.AiModel,
                searchQuery,
                context);

            if (string.IsNullOrEmpty(optimizedContent))
            {
                return new OptimizedPrompts { Success = false, ErrorMessage = "No response from AI" };
            }

            // Parse the optimized prompts
            var prompts = ParseOptimizedPrompts(optimizedContent, repository);
            
            // Save optimized prompts
            await SaveOptimizedPromptsAsync(repository.Slug, prompts);

            _logger.LogInformation(
                "[{CorrelationId}] Successfully optimized prompts for repository: {RepoName}",
                correlationId, repository.Name);

            return new OptimizedPrompts
            {
                Success = true,
                SystemPrompt = prompts.SystemPrompt,
                ComponentQueryTemplate = prompts.ComponentQueryTemplate,
                ExpertQueryTemplate = prompts.ExpertQueryTemplate,
                BestPractices = prompts.BestPractices
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{CorrelationId}] Failed to optimize prompts for repository: {RepoName}",
                correlationId, repository.Name);
            
            return new OptimizedPrompts { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Get web search results for repository-specific best practices
    /// </summary>
    public async Task<string> GetWebSearchBestPracticesAsync(string repositoryName, string framework)
    {
        var searchQuery = $"{framework} best practices for AI code generation prompts 2024";
        
        // This would use the AI's built-in web search capability
        // For now, we'll build a comprehensive query
        return $@"
Search for:
1. {framework} component library best practices
2. AI prompt engineering for {framework} development
3. Modern {framework} patterns and conventions
4. {repositoryName} usage examples and patterns
";
    }

    private string BuildRepositoryContext(RepositoryInfo repository)
    {
        return $@"
Repository Information:
- Name: {repository.Name}
- Framework: {repository.Framework}
- Extractor Type: {repository.ExtractorType}
- URL: {repository.Url}
- Target Framework: {repository.TargetFramework ?? "Not specified"}

Please analyze this repository and provide optimized AI prompts that:
1. Are specific to the framework type ({repository.Framework})
2. Include proper terminology and conventions
3. Handle the specific component patterns of this library
4. Provide accurate parameter names and types
5. Include helpful examples based on the library's structure
";
    }

    private string BuildSearchQuery(RepositoryInfo repository)
    {
        return $@"
You are an expert in AI prompt engineering for {repository.Framework} development.

Search the web for:
1. Current best practices for AI-assisted {repository.Framework} development
2. Effective prompt patterns for {repository.Framework} component libraries
3. Common pitfalls when prompting AI for {repository.Framework} code
4. Latest {repository.Framework} conventions and patterns

Based on the search results and your expertise, create optimized prompts for a MCP server 
that helps users query the {repository.Name} component library.

The prompts should be in JSON format with the following structure:
{{
    ""systemPrompt"": ""The main system prompt for the AI assistant"",
    ""componentQueryTemplate"": ""Template for querying component information"",
    ""expertQueryTemplate"": ""Template for expert Q&A queries"",
    ""bestPractices"": [""Array of best practices""]
}}
";
    }

    private async Task<string> CallAIWithWebSearchAsync(string baseUrl, string apiKey, string model, string searchQuery, string context)
    {
        var settings = _settingsManager.LoadSettings();
        
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        
        // Use the injected HttpClient instead of creating a new one
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        
        var requestBody = new
        {
            model = model,
            messages = new object[]
            {
                new { role = "system", content = "You are an AI assistant with web search capabilities. Search for best practices and provide optimized prompts." },
                new { role = "user", content = searchQuery + "\n\nContext:\n" + context }
            },
            temperature = 0.3,
            max_tokens = 4000
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        try
        {
            var response = await _httpClient.PostAsync($"{baseUrl}chat/completions", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var contentProp))
                    {
                        return contentProp.GetString() ?? "";
                    }
                }
            }
            
            _logger.LogWarning("AI call failed: {StatusCode} - {Content}", response.StatusCode, responseContent);
            return "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call AI service");
            return "";
        }
    }

    private OptimizedPromptTemplate ParseOptimizedPrompts(string content, RepositoryInfo repository)
    {
        var prompts = new OptimizedPromptTemplate
        {
            Framework = repository.Framework,
            RepositoryName = repository.Name,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            // Try to parse as JSON first
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("systemPrompt", out var systemPrompt))
            {
                prompts.SystemPrompt = systemPrompt.GetString() ?? "";
            }

            if (root.TryGetProperty("componentQueryTemplate", out var componentQuery))
            {
                prompts.ComponentQueryTemplate = componentQuery.GetString() ?? "";
            }

            if (root.TryGetProperty("expertQueryTemplate", out var expertQuery))
            {
                prompts.ExpertQueryTemplate = expertQuery.GetString() ?? "";
            }

            if (root.TryGetProperty("bestPractices", out var bestPractices))
            {
                var practices = new List<string>();
                foreach (var practice in bestPractices.EnumerateArray())
                {
                    practices.Add(practice.GetString() ?? "");
                }
                prompts.BestPractices = practices;
            }
        }
        catch
        {
            // If JSON parsing fails, try to extract from text
            prompts.SystemPrompt = ExtractSection(content, "systemPrompt") ?? 
                $"You are a senior {repository.Framework} expert for {repository.Name}.";
            prompts.ComponentQueryTemplate = ExtractSection(content, "componentQueryTemplate") ??
                "What are the parameters for {{componentName}}?";
            prompts.ExpertQueryTemplate = ExtractSection(content, "expertQueryTemplate") ??
                "Based on the documentation, answer: {{question}}";
        }

        // Apply framework-specific placeholders
        prompts.SystemPrompt = prompts.SystemPrompt
            .Replace("{{framework}}", repository.Framework)
            .Replace("{{repositoryName}}", repository.Name);

        return prompts;
    }

    private string? ExtractSection(string content, string sectionName)
    {
        var pattern = $@"{sectionName}[""']?\s*[:=]\s*[""']?([^""'\n}}]+)";
        var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private async Task SaveOptimizedPromptsAsync(string repositorySlug, OptimizedPromptTemplate prompts)
    {
        var optimizedPath = Path.Combine(_configPath, $"{repositorySlug}-prompts.json");
        
        var json = JsonSerializer.Serialize(prompts, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        
        await File.WriteAllTextAsync(optimizedPath, json);
        
        _logger.LogInformation("Saved optimized prompts to: {Path}", optimizedPath);
    }

    /// <summary>
    /// Load optimized prompts for a repository if they exist
    /// </summary>
    public OptimizedPromptTemplate? LoadOptimizedPrompts(string repositorySlug)
    {
        var optimizedPath = Path.Combine(_configPath, $"{repositorySlug}-prompts.json");
        
        if (!File.Exists(optimizedPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(optimizedPath);
            return JsonSerializer.Deserialize<OptimizedPromptTemplate>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load optimized prompts from: {Path}", optimizedPath);
            return null;
        }
    }
}

/// <summary>
/// Result of prompt optimization
/// </summary>
public class OptimizedPrompts
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SystemPrompt { get; set; }
    public string? ComponentQueryTemplate { get; set; }
    public string? ExpertQueryTemplate { get; set; }
    public List<string>? BestPractices { get; set; }
}

/// <summary>
/// Optimized prompt template
/// </summary>
public class OptimizedPromptTemplate
{
    public string Framework { get; set; } = "";
    public string RepositoryName { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public string ComponentQueryTemplate { get; set; } = "";
    public string ExpertQueryTemplate { get; set; } = "";
    public List<string> BestPractices { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
