// Copyright (c) BootstrapBlazor & Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://www.blazor.zone

using Coravel.Invocable;
using LibGit2Sharp;

namespace BootstrapBlazor.McpServer.Services;

public class GitSyncInvocable : IInvocable
{
    private readonly ILogger<GitSyncInvocable> _logger;
    private readonly DocsExtractorService _extractorService;
    private readonly AppSettingsManager _settingsManager;
    private readonly SyncStatusService _syncStatus;

    public GitSyncInvocable(
        ILogger<GitSyncInvocable> logger,
        DocsExtractorService extractorService,
        AppSettingsManager settingsManager,
        SyncStatusService syncStatus)
    {
        _logger = logger;
        _extractorService = extractorService;
        _settingsManager = settingsManager;
        _syncStatus = syncStatus;
    }

    public Task Invoke()
    {
        _logger.LogInformation("Starting Git Sync & Extraction Job...");
        _syncStatus.StartSync();
        _syncStatus.UpdateProgress(0, "Initializing", "Starting sync process...");

        try
        {
            var settings = _settingsManager.LoadSettings();
            var repoUrl = settings.RepositoryUrl;
            var basePath = settings.LocalPath;
            var outputDir = settings.OutputDir;

            _syncStatus.AddRepository(repoUrl);

            bool skipPullUpdate = true;
            bool skipBuild = true;

            if (!skipPullUpdate)
            {
                // Clone or Pull
                if (!Directory.Exists(basePath) || !Repository.IsValid(basePath))
                {
                    _syncStatus.UpdateProgress(5, "Cloning", $"Cloning {repoUrl}...");
                    _logger.LogInformation("Cloning repository from {Url} to {Path}...", repoUrl, basePath);
                    if (!Directory.Exists(basePath))
                        Directory.CreateDirectory(basePath);

                    Repository.Clone(repoUrl, basePath);
                }
                else
                {
                    _syncStatus.UpdateProgress(5, "Pulling", $"Pulling latest changes from {repoUrl}...");
                    _logger.LogInformation("Repository exists at {Path}. Pulling latest changes...", basePath);
                    using var repo = new Repository(basePath);
                    Commands.Pull(repo, new Signature("RAGServer", "rag@local", DateTimeOffset.Now), new PullOptions());
                }
            }

            if (!skipBuild)
            {
                _syncStatus.UpdateProgress(15, "Building", "Building BootstrapBlazor.Server project...");
                _logger.LogInformation("Building BootstrapBlazor.Server project to generate DLLs and XMLs for all extensions...");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build src/BootstrapBlazor.Server/BootstrapBlazor.Server.csproj -c Release",
                    WorkingDirectory = basePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = new System.Diagnostics.Process { StartInfo = psi })
                {
                    process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) _logger.LogInformation(e.Data); };
                    process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) _logger.LogError(e.Data); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError("Build failed with exit code {ExitCode}", process.ExitCode);
                        _syncStatus.ErrorSync($"Build failed with exit code {process.ExitCode}");
                        return Task.CompletedTask;
                    }
                }
            }

            _syncStatus.UpdateProgress(50, "Extracting", "Extracting component documentation...");

            // Execute the extraction
            _extractorService.Extract(basePath, outputDir);

            _syncStatus.UpdateProgress(90, "Finalizing", "Completing sync...");
            _logger.LogInformation("Git Sync & Extraction Job completed successfully.");

            _syncStatus.CompleteSync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during Git Sync & Extraction.");
            _syncStatus.ErrorSync(ex.Message);
        }

        return Task.CompletedTask;
    }
}
