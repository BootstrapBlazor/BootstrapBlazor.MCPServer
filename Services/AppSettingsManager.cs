using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BootstrapBlazor.McpServer.Services;

public class AppSettingsManager
{
    private readonly string _settingFilePath;
    private readonly ILogger<AppSettingsManager> _logger;

    public AppSettingsManager(ILogger<AppSettingsManager> logger)
    {
        _logger = logger;
        
        var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
            _logger.LogDebug(
                "Created data directory: {DataDir}",
                dataDir);
        }
        _settingFilePath = Path.Combine(dataDir, "config.json");
        
        _logger.LogTrace(
            "Settings file path: {SettingFilePath}",
            _settingFilePath);
    }

    public AppSettingsModel LoadSettings()
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] Loading settings from: {SettingFilePath}",
            correlationId, _settingFilePath);
        
        var model = new AppSettingsModel();
        if (!File.Exists(_settingFilePath))
        {
            _logger.LogWarning(
                "[{CorrelationId}] Settings file not found, using defaults",
                correlationId);
            return model;
        }

        try
        {
            var json = File.ReadAllText(_settingFilePath);
            var root = JsonNode.Parse(json);
            if (root == null)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Settings file is empty or invalid, using defaults",
                    correlationId);
                return model;
            }

            var gitSync = root["GitSync"];
            if (gitSync?["RepositoryUrl"] is { } repoUrl) model.RepositoryUrl = repoUrl.ToString();
            if (gitSync?["CronSchedule"] is { } cron) model.CronSchedule = cron.ToString();
            if (gitSync?["LocalPath"] is { } localPath) model.LocalPath = localPath.ToString();
            if (gitSync?["OutputDir"] is { } outputDir) model.OutputDir = outputDir.ToString();
            if (gitSync?["Framework"] is { } framework) model.Framework = framework.ToString();

            var ai = root["AI"];
            if (ai?["BaseUrl"] is { } baseUrl) model.AiBaseUrl = baseUrl.ToString();
            if (ai?["ApiKey"] is { } apiKey) model.AiApiKey = apiKey.ToString();
            if (ai?["Model"] is { } aiModel) model.AiModel = aiModel.ToString();
            if (ai?["Enabled"] is { } enabled) model.AiEnabled = enabled.GetValue<bool>();

            var auth = root["Auth"];
            if (auth?["AdminUsername"] is { } username) model.AdminUsername = username.ToString();
            if (auth?["AdminPassword"] is { } password) model.AdminPassword = password.ToString();

            _logger.LogDebug(
                "[{CorrelationId}] Settings loaded successfully. Repository: {RepositoryUrl}, AI Enabled: {AiEnabled}",
                correlationId, model.RepositoryUrl, model.AiEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{CorrelationId}] Failed to parse settings file, using defaults",
                correlationId);
        }

        return model;
    }

    public void SaveSettings(AppSettingsModel model)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] Saving settings to: {SettingFilePath}",
            correlationId, _settingFilePath);
        
        JsonNode root;
        if (File.Exists(_settingFilePath))
        {
            try
            {
                var json = File.ReadAllText(_settingFilePath);
                root = JsonNode.Parse(json) ?? new JsonObject();
                
                _logger.LogTrace(
                    "[{CorrelationId}] Loaded existing settings file",
                    correlationId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[{CorrelationId}] Failed to parse existing settings, creating new",
                    correlationId);
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
            _logger.LogTrace(
                "[{CorrelationId}] Creating new settings file",
                correlationId);
        }

        if (root["GitSync"] == null) root["GitSync"] = new JsonObject();
        root["GitSync"]!["RepositoryUrl"] = model.RepositoryUrl;
        root["GitSync"]!["CronSchedule"] = model.CronSchedule;
        root["GitSync"]!["LocalPath"] = model.LocalPath;
        root["GitSync"]!["OutputDir"] = model.OutputDir;
        root["GitSync"]!["Framework"] = model.Framework;

        if (root["AI"] == null) root["AI"] = new JsonObject();
        root["AI"]!["BaseUrl"] = model.AiBaseUrl;
        root["AI"]!["ApiKey"] = model.AiApiKey;
        root["AI"]!["Model"] = model.AiModel;
        root["AI"]!["Enabled"] = model.AiEnabled;

        if (root["Auth"] == null) root["Auth"] = new JsonObject();
        root["Auth"]!["AdminUsername"] = model.AdminUsername;
        root["Auth"]!["AdminPassword"] = model.AdminPassword;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_settingFilePath, root.ToJsonString(options));
        
        _logger.LogInformation(
            "[{CorrelationId}] Settings saved successfully",
            correlationId);
    }
}

public class AppSettingsModel
{
    public string RepositoryUrl { get; set; } = "https://gitee.com/LongbowEnterprise/BootstrapBlazor.git";
    public string CronSchedule { get; set; } = "0 3 * * *";
    public string LocalPath { get; set; } = "/app/data/BootstrapBlazorRepo";
    public string OutputDir { get; set; } = "/app/data/OutputRAG";
    public string Framework { get; set; } = "Blazor";  // Configurable framework type
    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string AiApiKey { get; set; } = "YOUR_API_KEY_HERE";
    public string AiModel { get; set; } = "gpt-4o";
    public bool AiEnabled { get; set; } = false;
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "123456";
}
