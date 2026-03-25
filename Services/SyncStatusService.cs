// Copyright (c) BootstrapBlazor & Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://www.blazor.zone

namespace BootstrapBlazor.McpServer.Services;

/// <summary>
/// Service to track sync progress and status for UI updates
/// </summary>
public class SyncStatusService
{
    public bool IsSyncing { get; private set; }
    public string CurrentStep { get; private set; } = "";
    public int Progress { get; private set; }
    public string StatusMessage { get; private set; } = "";
    public List<string> Repositories { get; } = new();
    public string? ErrorMessage { get; private set; }

    public event Action? OnChange;

    public void StartSync()
    {
        IsSyncing = true;
        Progress = 0;
        ErrorMessage = null;
        Repositories.Clear();
        NotifyStateChanged();
    }

    public void UpdateProgress(int progress, string step, string message)
    {
        Progress = progress;
        CurrentStep = step;
        StatusMessage = message;
        NotifyStateChanged();
    }

    public void AddRepository(string repo)
    {
        Repositories.Add(repo);
        NotifyStateChanged();
    }

    public void CompleteSync()
    {
        IsSyncing = false;
        Progress = 100;
        CurrentStep = "Complete";
        StatusMessage = "Sync completed successfully!";
        NotifyStateChanged();
    }

    public void ErrorSync(string error)
    {
        IsSyncing = false;
        ErrorMessage = error;
        CurrentStep = "Error";
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}