// Copyright (c) BootstrapBlazor & Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://www.blazor.zone

using System.Text.Json;
using System.Text.Json.Nodes;

namespace BootstrapBlazor.McpServer.Services;

public class AppSettingsManager
{
    private readonly string _settingFilePath;
    private readonly ILogger<AppSettingsManager>? _logger;

    public AppSettingsManager(ILogger<AppSettingsManager>? logger = null)
    {
        _logger = logger;
        
        // Use PathHelper to get the data path relative to the application
        var dataDir = PathHelper.GetDataPath();
        _settingFilePath = Path.Combine(dataDir, "config.json");
    }

    public AppSettingsModel LoadSettings()
    {
        var model = new AppSettingsModel();
        if (!File.Exists(_settingFilePath)) 
        {
            model.EnsurePaths();
            return model;
        }

        var json = File.ReadAllText(_settingFilePath);
        var root = JsonNode.Parse(json);
        if (root == null) 
        {
            model.EnsurePaths();
            return model;
        }

        var gitSync = root["GitSync"];
        if (gitSync?["RepositoryUrl"] is { } repoUrl) model.RepositoryUrl = repoUrl.ToString();
        if (gitSync?["CronSchedule"] is { } cron) model.CronSchedule = cron.ToString();
        if (gitSync?["LocalPath"] is { } localPath) model.LocalPath = localPath.ToString();
        if (gitSync?["OutputDir"] is { } outputDir) model.OutputDir = outputDir.ToString();

        var ai = root["AI"];
        if (ai?["BaseUrl"] is { } baseUrl) model.AiBaseUrl = baseUrl.ToString();
        if (ai?["ApiKey"] is { } apiKey) model.AiApiKey = apiKey.ToString();
        if (ai?["Model"] is { } aiModel) model.AiModel = aiModel.ToString();
        if (ai?["Enabled"] is { } enabled) model.AiEnabled = enabled.GetValue<bool>();

        var auth = root["Auth"];
        if (auth?["AdminUsername"] is { } username) model.AdminUsername = username.ToString();
        if (auth?["AdminPassword"] is { } password) model.AdminPassword = password.ToString();

        // Ensure paths are computed using PathHelper if not explicitly set
        model.EnsurePaths();

        return model;
    }

    public void SaveSettings(AppSettingsModel model)
    {
        JsonNode root;
        if (File.Exists(_settingFilePath))
        {
            var json = File.ReadAllText(_settingFilePath);
            root = JsonNode.Parse(json) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (root["GitSync"] == null) root["GitSync"] = new JsonObject();
        root["GitSync"]!["RepositoryUrl"] = model.RepositoryUrl;
        root["GitSync"]!["CronSchedule"] = model.CronSchedule;
        root["GitSync"]!["LocalPath"] = model.LocalPath;
        root["GitSync"]!["OutputDir"] = model.OutputDir;

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
    }
}

public class AppSettingsModel
{
    public string RepositoryUrl { get; set; } = "https://gitee.com/LongbowEnterprise/BootstrapBlazor.git";
    public string CronSchedule { get; set; } = "0 3 * * *";
    public string LocalPath { get; set; } = ""; // Will be computed from PathHelper if empty
    public string OutputDir { get; set; } = ""; // Will be computed from PathHelper if empty
    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string AiApiKey { get; set; } = ""; // No default - must be configured
    public string AiModel { get; set; } = "gpt-4o";
    public bool AiEnabled { get; set; } = false;
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = ""; // Must be changed on first use

    /// <summary>
    /// Compute default paths using PathHelper if not explicitly set
    /// </summary>
    public void EnsurePaths()
    {
        if (string.IsNullOrEmpty(LocalPath))
        {
            LocalPath = Path.Combine(PathHelper.GetDataPath(), "repositories", "BootstrapBlazor");
        }
        if (string.IsNullOrEmpty(OutputDir))
        {
            OutputDir = Path.Combine(PathHelper.GetDataPath(), "OutputRAG");
        }
    }
}
