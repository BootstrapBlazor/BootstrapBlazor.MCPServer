using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BootstrapBlazor.McpServer.Services;

public class DocsExtractorService
{
    private readonly ILogger<DocsExtractorService> _logger;
    private Dictionary<string, string> _localizerDict = new();

    public DocsExtractorService(ILogger<DocsExtractorService> logger)
    {
        _logger = logger;
    }

    public void Extract(string basePath, string outputDir)
    {
        _logger.LogInformation("BootstrapBlazor RAG Extractor started for basePath: {BasePath}", basePath);
        
        string docsJsonPath = Path.Combine(basePath, "src/BootstrapBlazor.Server/docs.json");
        string xmlPath = Path.Combine(basePath, "src/BootstrapBlazor/BootstrapBlazor.xml");
        string samplesPath = Path.Combine(basePath, "src/BootstrapBlazor.Server/Components/Samples");
        string localePath = Path.Combine(basePath, "src/BootstrapBlazor.Server/Locales/zh-CN.json");
        string? dllPath = Path.Combine(basePath, "src/BootstrapBlazor/bin/Release/net10.0/BootstrapBlazor.dll");

        if (!File.Exists(dllPath))
        {
            dllPath = Directory.GetFiles(Path.Combine(basePath, "src/BootstrapBlazor/bin"), "BootstrapBlazor.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (dllPath == null)
            {
                _logger.LogWarning("BootstrapBlazor.dll not found. The project must be built first before extraction.");
                return;
            }
        }

        Directory.CreateDirectory(Path.Combine(outputDir, "API"));
        Directory.CreateDirectory(Path.Combine(outputDir, "Samples"));

        // 0. Load Localization Dictionary
        _logger.LogInformation("Loading Localization Dictionary...");
        _localizerDict.Clear();
        if (File.Exists(localePath))
        {
            var localeJson = File.ReadAllText(localePath);
            var localeDoc = JsonDocument.Parse(localeJson);
            foreach (var classNode in localeDoc.RootElement.EnumerateObject())
            {
                var className = classNode.Name.Split('.').Last();
                foreach (var keyNode in classNode.Value.EnumerateObject())
                {
                    var dictKey = $"{className}:{keyNode.Name}";
                    _localizerDict[dictKey] = keyNode.Value.GetString() ?? "";
                }
            }
        }

        // 1. Process XML for API
        _logger.LogInformation("Processing API...");
        var summaryDict = new Dictionary<string, string>();
        if (File.Exists(xmlPath))
        {
            try
            {
                var xmlDoc = XDocument.Load(xmlPath);
                var members = xmlDoc.Descendants("member")
                    .Select(x => new
                    {
                        Name = x.Attribute("name")?.Value ?? "",
                        Summary = x.Element("summary")?.Value.Trim() ?? ""
                    })
                    .Where(x => x.Name.StartsWith("P:BootstrapBlazor.Components."));

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
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load XML documentation.");
            }
        }

        // Use Reflection to get components
        try
        {
            // Use the already loaded assembly instead of trying to load it from a physical file
            // to prevent AssemblyLoadContext mismatched versions.
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                    .FirstOrDefault(a => a.GetName().Name == "BootstrapBlazor") 
                           ?? Assembly.LoadFrom(dllPath);

            var componentTypes = assembly.GetTypes()
                .Where(t => t.IsPublic && !t.IsAbstract && typeof(IComponent).IsAssignableFrom(t));

            foreach (var type in componentTypes)
            {
                var parameters = type.GetProperties()
                    .Where(p => p.GetCustomAttributes(typeof(ParameterAttribute), true).Any())
                    .ToList();

                if (parameters.Any())
                {
                    var typeName = type.Name;
                    if (type.IsGenericType)
                    {
                        typeName = typeName.Split('`')[0];
                    }

                    var mdPath = Path.Combine(outputDir, "API", $"{typeName}.md");
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
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load assembly or extract parameters.");
        }

        // 2. Process docs.json for Samples
        _logger.LogInformation("Processing Samples...");
        if (File.Exists(docsJsonPath))
        {
            var docsJson = File.ReadAllText(docsJsonPath);
            var docsDoc = JsonDocument.Parse(docsJson);
            var srcObj = docsDoc.RootElement.GetProperty("src");

            foreach (var prop in srcObj.EnumerateObject())
            {
                var folderKey = prop.Name;
                var folderVal = prop.Value.GetString();
                if (string.IsNullOrEmpty(folderVal)) continue;

                var relPath = string.Join(Path.DirectorySeparatorChar.ToString(), folderVal.Split('\\', '/'));
                var razorPath = Path.Combine(samplesPath, relPath + ".razor");
                var csPath = Path.Combine(samplesPath, relPath + ".razor.cs");

                if (!File.Exists(razorPath)) continue;

                // 用 folderVal 的最后一段类名推断组件名（Buttons→Button, Ajaxs→Ajax）
                var sampleClassName = folderVal!.Contains('/') ? folderVal.Split('/').Last() : folderVal;
                // 去掉末尾的 's'（示例类通常以 s 结尾），与 API 文件名一致
                var componentKey = sampleClassName.EndsWith('s') && sampleClassName.Length > 2 ? sampleClassName[..^1] : sampleClassName;
                var mdPath = Path.Combine(outputDir, "Samples", $"{componentKey}.md");
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
            }
        }
        else
        {
            _logger.LogWarning("docs.json not found.");
        }

        _logger.LogInformation("Done! RAG Datasets created in {OutputDir}.", outputDir);
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
