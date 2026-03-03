// Copyright (c) BootstrapBlazor & Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://www.blazor.zone

using ModelContextProtocol.Server;

namespace BootstrapBlazor.McpServer.Services;

[McpServerToolType]
public class McpService
{
    private readonly DocsExtractorService _extractor;
    private readonly AiIntegrationService _aiService;
    private readonly ILogger<McpService> _logger;
    private readonly AppSettingsManager _settingsManager;

    public McpService(DocsExtractorService extractor, AiIntegrationService aiService, ILogger<McpService> logger, AppSettingsManager settingsManager)
    {
        _extractor = extractor;
        _aiService = aiService;
        _logger = logger;
        _settingsManager = settingsManager;
    }

    [McpServerTool]
    public string GetComponentList()
    {
        var settings = _settingsManager.LoadSettings();
        var apiPath = Path.Combine(settings.OutputDir, "API");
        if (!Directory.Exists(apiPath)) return "Documentation not found.";

        var files = Directory.GetFiles(apiPath, "*.md");
        var componentNames = files.Select(Path.GetFileNameWithoutExtension).ToArray();
        return string.Join("\n", componentNames);
    }

    public class SearchComponentArgs
    {
        public string Keyword { get; set; } = string.Empty;
    }

    [McpServerTool]
    public string SearchComponentKeyword(SearchComponentArgs args)
    {
        var settings = _settingsManager.LoadSettings();
        var apiPath = Path.Combine(settings.OutputDir, "API");
        if (!Directory.Exists(apiPath)) return "Documentation not found at " + apiPath;

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

        if (results.Count == 0) return $"No components found matching the keyword '{args.Keyword}'.";
        return string.Join("\n", results);
    }

    public class GetComponentDocsArgs
    {
        public string ComponentName { get; set; } = string.Empty;
    }

    /// <summary>
    /// 根据组件名直接返回原始文档信息（API 参数表 + 代码示例），不经过 AI 处理。
    /// </summary>
    [McpServerTool]
    public string GetComponentDocs(GetComponentDocsArgs args)
    {
        var settings = _settingsManager.LoadSettings();
        var (apiContent, sampleContent, error) = LoadComponentDocs(settings, args.ComponentName);
        if (error != null) return error;

        return $"# Component: {args.ComponentName}\n\n## API\n{apiContent}\n\n## Samples\n{sampleContent}";
    }

    public class AskComponentExpertArgs
    {
        public string ComponentName { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
    }

    [McpServerTool]
    public async Task<string> AskComponentExpert(AskComponentExpertArgs args)
    {
        var settings = _settingsManager.LoadSettings();
        var (apiContent, sampleContent, error) = LoadComponentDocs(settings, args.ComponentName);
        if (error != null) return error;

        // 如果 AI 未启用，直接返回原始文档
        if (!settings.AiEnabled)
        {
            _logger.LogInformation("AI is disabled. Returning raw documentation for '{Component}'.", args.ComponentName);
            return $"# Component: {args.ComponentName}\n\n## API\n{apiContent}\n\n## Samples\n{sampleContent}";
        }

        var systemPrompt = $@"You are a senior BootstrapBlazor expert. The user is asking a question about the component '{args.ComponentName}'.
Here is the official API parameter documentation for this component:
=== API START ===
{apiContent}
=== API END ===

Here are the official code samples for this component:
=== SAMPLES START ===
{sampleContent}
=== SAMPLES END ===

Based strictly on the API and examples provided above (and your general Blazor server knowledge), please answer the user's question clearly and concisely.
If writing code, ensure you use the exact parameter names listed in the API table.";

        var answer = await _aiService.AskExpertAsync(systemPrompt, args.Question);
        return answer;
    }

    /// <summary>
    /// 通用方法：根据组件名加载 API 文档和示例代码。
    /// 返回 (apiContent, sampleContent, error)，error 不为 null 时表示找不到组件。
    /// </summary>
    private (string apiContent, string sampleContent, string? error) LoadComponentDocs(AppSettingsModel settings, string componentName)
    {
        var apiDir = Path.Combine(settings.OutputDir, "API");
        var samplesDir = Path.Combine(settings.OutputDir, "Samples");

        var apiFile = Path.Combine(apiDir, $"{componentName}.md");
        var sampleFile = Path.Combine(samplesDir, $"{componentName}.md");

        // 如果精确匹配失败，做大小写不敏感的 fuzzy 搜索（兼容 AjaxService 等服务类）
        if (!File.Exists(apiFile) && !File.Exists(sampleFile))
        {
            if (Directory.Exists(apiDir))
            {
                var fuzzyApi = Directory.GetFiles(apiDir, "*.md")
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(componentName, StringComparison.OrdinalIgnoreCase));
                if (fuzzyApi != null) apiFile = fuzzyApi;
            }

            if (Directory.Exists(samplesDir))
            {
                var fuzzySample = Directory.GetFiles(samplesDir, "*.md")
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(componentName, StringComparison.OrdinalIgnoreCase));
                if (fuzzySample != null) sampleFile = fuzzySample;
            }

            if (!File.Exists(apiFile) && !File.Exists(sampleFile))
            {
                return (string.Empty, string.Empty, $"Component '{componentName}' not found in documentation.");
            }
        }

        if (!Directory.Exists(apiDir))
        {
            return (string.Empty, string.Empty, "Documentation directory not found.");
        }

        var apiContent = File.Exists(apiFile) ? File.ReadAllText(apiFile) : "No API documentation available.";
        var sampleContent = File.Exists(sampleFile) ? File.ReadAllText(sampleFile) : "No sample code available.";
        return (apiContent, sampleContent, null);
    }
}
