using System;
using System.IO;
using System.Threading.Tasks;
using Coravel.Invocable;
using LibGit2Sharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BootstrapBlazor.McpServer.Services;

public class GitSyncInvocable : IInvocable
{
    private readonly ILogger<GitSyncInvocable> _logger;
    private readonly DocsExtractorService _extractorService;
    private readonly AppSettingsManager _settingsManager;

    public GitSyncInvocable(
        ILogger<GitSyncInvocable> logger,
        DocsExtractorService extractorService,
        AppSettingsManager settingsManager)
    {
        _logger = logger;
        _extractorService = extractorService;
        _settingsManager = settingsManager;
    }

    public Task Invoke()
    {
        _logger.LogInformation("Starting Git Sync & Extraction Job...");
        
        try
        {
            var settings = _settingsManager.LoadSettings();
            var repoUrl = settings.RepositoryUrl;
            var basePath = settings.LocalPath;
            var outputDir = settings.OutputDir;

            // Clone or Pull
            if (!Directory.Exists(basePath) || !Repository.IsValid(basePath))
            {
                _logger.LogInformation("Cloning repository from {Url} to {Path}...", repoUrl, basePath);
                if (!Directory.Exists(basePath))
                    Directory.CreateDirectory(basePath);

                Repository.Clone(repoUrl, basePath);
            }
            else
            {
                _logger.LogInformation("Repository exists at {Path}. Pulling latest changes...", basePath);
                using var repo = new Repository(basePath);
                Commands.Pull(repo, new Signature("RAGServer", "rag@local", DateTimeOffset.Now), new PullOptions());
            }

            _logger.LogInformation("Building BootstrapBlazor project to generate DLL and XML...");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build src/BootstrapBlazor/BootstrapBlazor.csproj -c Release -f net10.0 /p:LangVersion=preview /p:RunTargetFramework=bypass",
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
                    return Task.CompletedTask; // Early return so we don't proceed to extract on failed build outputs
                }
            }

            // Execute the extraction
            _extractorService.Extract(basePath, outputDir);
            
            _logger.LogInformation("Git Sync & Extraction Job completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during Git Sync & Extraction.");
        }

        return Task.CompletedTask;
    }
}
