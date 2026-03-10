using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BootstrapBlazor.McpServer.Services;

/// <summary>
/// Configuration options for DocsExtractorService
/// </summary>
public class DocsExtractorOptions
{
    /// <summary>
    /// Relative path to docs.json file (default: "src/BootstrapBlazor.Server/docs.json")
    /// </summary>
    public string DocsJsonPath { get; set; } = "src/BootstrapBlazor.Server/docs.json";
    
    /// <summary>
    /// Relative path to XML documentation file (default: "src/BootstrapBlazor/BootstrapBlazor.xml")
    /// </summary>
    public string XmlPath { get; set; } = "src/BootstrapBlazor/BootstrapBlazor.xml";
    
    /// <summary>
    /// Relative path to samples directory (default: "src/BootstrapBlazor.Server/Components/Samples")
    /// </summary>
    public string SamplesPath { get; set; } = "src/BootstrapBlazor.Server/Components/Samples";
    
    /// <summary>
    /// Relative path to locales directory (default: "src/BootstrapBlazor.Server/Locales")
    /// </summary>
    public string LocalesPath { get; set; } = "src/BootstrapBlazor.Server/Locales";
}

public class DocsExtractorService
{
    private readonly ILogger<DocsExtractorService> _logger;
    private Dictionary<string, string> _localizerDict = new();
    private const string CorrelationId = "DocsExtractor";
    private readonly DocsExtractorOptions _options;

    public DocsExtractorService(ILogger<DocsExtractorService> logger, DocsExtractorOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public void Extract(string basePath, string outputDir)
    {
        var extractionCorrelationId = Guid.NewGuid().ToString("N")[..16];
        
        _logger.LogInformation(
            "[{CorrelationId}] ============================================\n" +
            "[{CorrelationId}] BootstrapBlazor RAG Extractor started\n" +
            "[{CorrelationId}] BasePath: {BasePath}\n" +
            "[{CorrelationId}] OutputDir: {OutputDir}\n" +
            "[{CorrelationId}] ============================================",
            extractionCorrelationId, extractionCorrelationId, basePath, outputDir);

        // Use configurable paths with fallbacks
        string docsJsonPath = Path.Combine(basePath, _options.DocsJsonPath);
        string xmlPath = Path.Combine(basePath, _options.XmlPath);
        string samplesPath = Path.Combine(basePath, _options.SamplesPath);
        string localePath = Path.Combine(basePath, _options.LocalesPath, "zh-CN.json");
        string? dllPath = Path.Combine(basePath, "src/BootstrapBlazor/bin/Release/net10.0/BootstrapBlazor.dll");

        // Check for DLL
        if (!File.Exists(dllPath))
        {
            _logger.LogDebug(
                "[{CorrelationId}] DLL not found at expected path, searching recursively...",
                extractionCorrelationId);
            
            dllPath = Directory.GetFiles(Path.Combine(basePath, "src/BootstrapBlazor/bin"), "BootstrapBlazor.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (dllPath == null)
            {
                _logger.LogError(
                    "[{CorrelationId}] BootstrapBlazor.dll not found. The project must be built first before extraction.",
                    extractionCorrelationId);
                
                _logger.LogWarning(
                    "[{CorrelationId}] Extraction aborted - no DLL found in {BasePath}",
                    extractionCorrelationId, basePath);
                return;
            }
            
            _logger.LogDebug(
                "[{CorrelationId}] Found DLL at: {DllPath}",
                extractionCorrelationId, dllPath);
        }

        // Create output directories
        _logger.LogDebug(
            "[{CorrelationId}] Creating output directories...",
            extractionCorrelationId);
        
        Directory.CreateDirectory(Path.Combine(outputDir, "API"));
        Directory.CreateDirectory(Path.Combine(outputDir, "Samples"));

        _logger.LogDebug(
            "[{CorrelationId}] Output directories created",
            extractionCorrelationId);

        // 0. Load Localization Dictionary
        _logger.LogInformation(
            "[{CorrelationId}] Loading Localization Dictionary...",
            extractionCorrelationId);
        
        _localizerDict.Clear();
        if (File.Exists(localePath))
        {
            try
            {
                var localeJson = File.ReadAllText(localePath);
                var localeDoc = JsonDocument.Parse(localeJson);
                var count = 0;
                foreach (var classNode in localeDoc.RootElement.EnumerateObject())
                {
                    var className = classNode.Name.Split('.').Last();
                    foreach (var keyNode in classNode.Value.EnumerateObject())
                    {
                        var dictKey = $"{className}:{keyNode.Name}";
                        _localizerDict[dictKey] = keyNode.Value.GetString() ?? "";
                        count++;
                    }
                }
                
                _logger.LogInformation(
                    "[{CorrelationId}] Loaded {Count} localization entries",
                    extractionCorrelationId, count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[{CorrelationId}] Failed to load localization dictionary",
                    extractionCorrelationId);
            }
        }
        else
        {
            _logger.LogWarning(
                "[{CorrelationId}] Localization file not found at {Path}",
                extractionCorrelationId, localePath);
        }

        // 1. Process XML for API
        _logger.LogInformation(
            "[{CorrelationId}] Processing API documentation from XML...",
            extractionCorrelationId);
        
        var summaryDict = new Dictionary<string, string>();
        if (File.Exists(xmlPath))
        {
            try
            {
                _logger.LogDebug(
                    "[{CorrelationId}] Loading XML file: {XmlPath}",
                    extractionCorrelationId, xmlPath);
                
                var xmlDoc = XDocument.Load(xmlPath);
                var members = xmlDoc.Descendants("member")
                    .Select(x => new
                    {
                        Name = x.Attribute("name")?.Value ?? "",
                        Summary = x.Element("summary")?.Value.Trim() ?? ""
                    })
                    .Where(x => x.Name.StartsWith("P:BootstrapBlazor.Components."));

                var apiCount = 0;
                foreach (var m in members)
                {
                    var parts = m.Name.Substring("P:".Length).Split('.');
                    if (parts.Length >= 2)
                    {
                        var propName = parts[parts.Length - 1];
                        var typeName = string.Join(".", parts.Take(parts.Length - 1).Skip(2));
                        var key = $"{typeName}.{propName}";
                        if (!summaryDict.ContainsKey(key))
                        {
                            summaryDict[key] = Regex.Replace(m.Summary.Replace("\n", " "), @"\s+", " ").Trim();
                            apiCount++;
                        }
                    }
                }
                
                _logger.LogInformation(
                    "[{CorrelationId}] Processed {Count} API documentation entries",
                    extractionCorrelationId, apiCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[{CorrelationId}] Failed to load XML documentation",
                    extractionCorrelationId);
            }
        }
        else
        {
            _logger.LogWarning(
                "[{CorrelationId}] XML documentation file not found at {Path}",
                extractionCorrelationId, xmlPath);
        }

        // Use Reflection to get components
        _logger.LogInformation(
            "[{CorrelationId}] Loading assembly and extracting component parameters...",
            extractionCorrelationId);
        
        try
        {
            // Use the already loaded assembly instead of trying to load it from a physical file
            // to prevent AssemblyLoadContext mismatched versions.
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                    .FirstOrDefault(a => a.GetName().Name == "BootstrapBlazor")
                           ?? Assembly.LoadFrom(dllPath);

            _logger.LogDebug(
                "[{CorrelationId}] Assembly loaded: {AssemblyName}",
                extractionCorrelationId, assembly.GetName().Name);

            var componentTypes = assembly.GetTypes()
                .Where(t => t.IsPublic && !t.IsAbstract && typeof(IComponent).IsAssignableFrom(t))
                .ToList();

            _logger.LogDebug(
                "[{CorrelationId}] Found {Count} component types",
                extractionCorrelationId, componentTypes.Count);

            var componentsWithParams = 0;
            var apiFilesCreated = 0;

            foreach (var type in componentTypes)
            {
                var parameters = type.GetProperties()
                    .Where(p => p.GetCustomAttributes(typeof(ParameterAttribute), true).Any())
                    .ToList();

                if (parameters.Any())
                {
                    componentsWithParams++;
                    
                    var typeName = type.Name;
                    if (type.IsGenericType)
                    {
                        typeName = typeName.Split('`')[0];
                    }

                    var mdPath = Path.Combine(outputDir, "API", $"{typeName}.md");
                    
                    _logger.LogTrace(
                        "[{CorrelationId}] Creating API file for: {ComponentName} ({ParamCount} parameters)",
                        extractionCorrelationId, typeName, parameters.Count);
                    
                    using var writer = new StreamWriter(mdPath);
                    writer.WriteLine($"# 组件 API: {typeName}");
                    writer.WriteLine();
                    writer.WriteLine($"| 参数名 | 类型 | 说明 |");
                    writer.WriteLine($"| --- | --- | --- |");

                    foreach (var p in parameters)
                    {
                        var propType = p.PropertyType.Name;
                        if (p.PropertyType.IsGenericType)
                        {
                            propType = p.PropertyType.Name.Split('`')[0] + "<T>";
                        }
                        var key = $"{type.Name}.{p.Name}";
                        if (type.IsGenericType)
                        {
                            key = $"{type.Name.Split('`')[0]}`{type.GetGenericArguments().Length}.{p.Name}";
                        }

                        var summary = summaryDict.ContainsKey(key) ? summaryDict[key] : "-";
                        summary = Regex.Replace(summary, "<.*?>", "");
                        writer.WriteLine($"| {p.Name} | `{propType}` | {summary} |");
                    }
                    
                    apiFilesCreated++;
                }
            }
            
            _logger.LogInformation(
                "[{CorrelationId}] Created {ApiFilesCreated} API documentation files from {ComponentsWithParams} components",
                extractionCorrelationId, apiFilesCreated, componentsWithParams);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{CorrelationId}] Failed to load assembly or extract parameters",
                extractionCorrelationId);
        }

        // 2. Process docs.json for Samples
        _logger.LogInformation(
            "[{CorrelationId}] Processing Samples documentation...",
            extractionCorrelationId);
        
        if (File.Exists(docsJsonPath))
        {
            try
            {
                var docsJson = File.ReadAllText(docsJsonPath);
                var docsDoc = JsonDocument.Parse(docsJson);
                var srcObj = docsDoc.RootElement.GetProperty("src");

                var sampleFilesCreated = 0;
                var folderCount = 0;

                foreach (var prop in srcObj.EnumerateObject())
                {
                    folderCount++;
                    var folderKey = prop.Name;
                    var folderVal = prop.Value.GetString();
                    if (string.IsNullOrEmpty(folderVal)) continue;

                    var relPath = string.Join(Path.DirectorySeparatorChar.ToString(), folderVal.Split('\\', '/'));
                    var razorPath = Path.Combine(samplesPath, relPath + ".razor");
                    var csPath = Path.Combine(samplesPath, relPath + ".razor.cs");

                    if (!File.Exists(razorPath)) 
                    {
                        _logger.LogTrace(
                            "[{CorrelationId}] Razor file not found: {Path}",
                            extractionCorrelationId, razorPath);
                        continue;
                    }

                    // 用 folderVal 的最后一段类名推断组件名（Buttons→Button, Ajaxs→Ajax）
                    var sampleClassName = folderVal!.Contains('/') ? folderVal.Split('/').Last() : folderVal;
                    // 去掉末尾的 's'（示例类通常以 s 结尾），与 API 文件名一致
                    var componentKey = sampleClassName.EndsWith('s') && sampleClassName.Length > 2 ? sampleClassName[..^1] : sampleClassName;
                    var mdPath = Path.Combine(outputDir, "Samples", $"{componentKey}.md");
                    
                    _logger.LogTrace(
                        "[{CorrelationId}] Creating sample file for: {ComponentName}",
                        extractionCorrelationId, componentKey);
                    
                    using var writer = new StreamWriter(mdPath);
                    writer.WriteLine($"# {folderKey} 示例 (Samples)");
                    writer.WriteLine();

                    var razorContent = File.ReadAllText(razorPath);
                    var className = Path.GetFileNameWithoutExtension(razorPath);
                    razorContent = CleanLocalization(razorContent, className);
                    razorContent = CleanAttributeTable(razorContent, outputDir);

                    razorContent = Regex.Replace(razorContent, @"@inject IStringLocalizer.*?$|@inject IOptions.*?$", "", RegexOptions.Multiline);
                    razorContent = Regex.Replace(razorContent, @"@\(\(MarkupString\)(.*?)\)", "$1");
                    razorContent = Regex.Replace(razorContent, @"\n\s*\n", "\n\n");

                    writer.WriteLine($"## 示例: {Path.GetFileName(razorPath)}");
                    writer.WriteLine("```html");
                    writer.WriteLine(razorContent.Trim());
                    writer.WriteLine("```");
                    writer.WriteLine();

                    if (File.Exists(csPath))
                    {
                        var csContent = File.ReadAllText(csPath);
                        csContent = CleanLocalization(csContent, className);

                        writer.WriteLine($"## 后台代码: {Path.GetFileName(csPath)}");
                        writer.WriteLine("```csharp");
                        writer.WriteLine(csContent.Trim());
                        writer.WriteLine("```");
                        writer.WriteLine();
                    }
                    
                    sampleFilesCreated++;
                }
                
                _logger.LogInformation(
                    "[{CorrelationId}] Processed {FolderCount} folders, created {SampleFilesCreated} sample files",
                    extractionCorrelationId, folderCount, sampleFilesCreated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[{CorrelationId}] Failed to process docs.json",
                    extractionCorrelationId);
            }
        }
        else
        {
            _logger.LogWarning(
                "[{CorrelationId}] docs.json not found at {Path}",
                extractionCorrelationId, docsJsonPath);
        }

        _logger.LogInformation(
            "[{CorrelationId}] ============================================\n" +
            "[{CorrelationId}] RAG Datasets extraction completed\n" +
            "[{CorrelationId}] Output directory: {OutputDir}\n" +
            "[{CorrelationId}] ============================================",
            extractionCorrelationId, extractionCorrelationId, outputDir);
    }

    private string CleanLocalization(string content, string className)
    {
        var replaced = Regex.Replace(content, @"@?Localizer\[""(.*?)""\](\.Value)?", match =>
        {
            var key = match.Groups[1].Value;
            var dictKey = $"{className}:{key}";
            if (_localizerDict.TryGetValue(dictKey, out var value))
            {
                return value.Replace("\"", "'");
            }
            return match.Value;
        });

        replaced = Regex.Replace(replaced, @"@?LocalizerFoo\[nameof\(context\.(.*?)\)\]\.Value", "context.$1");

        return replaced;
    }

    private string CleanAttributeTable(string content, string outputDir)
    {
        var replaced = Regex.Replace(content, @"<AttributeTable[\s\S]*?Type=""@?typeof\((.*?)(?:<.*?>)?\)""[\s\S]*?(?:/>|></AttributeTable>)", match =>
        {
            var componentName = match.Groups[1].Value;
            var apiPath = Path.Combine(outputDir, "API", $"{componentName}.md");
            if (File.Exists(apiPath))
            {
                var apiContent = File.ReadAllText(apiPath);
                return apiContent.Trim();
            }
            return match.Value;
        });

        return replaced;
    }
}
