using Microsoft.Extensions.Logging;

namespace BootstrapBlazor.McpServer.Services;

/// <summary>
/// Service for repository operations and sync triggering
/// </summary>
public class RepositoryService
{
    private readonly RepositoryManager _repositoryManager;
    private readonly ILogger<RepositoryService> _logger;

    public RepositoryService(RepositoryManager repositoryManager, ILogger<RepositoryService> logger)
    {
        _repositoryManager = repositoryManager;
        _logger = logger;
    }

    /// <summary>
    /// Get all repositories
    /// </summary>
    public List<RepositoryInfo> GetAll()
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] Getting all repositories",
            correlationId);
        
        var repos = _repositoryManager.LoadRepositories();
        
        _logger.LogDebug(
            "[{CorrelationId}] Retrieved {Count} repositories",
            correlationId, repos.Count);
        
        return repos;
    }

    /// <summary>
    /// Get repository by ID
    /// </summary>
    public RepositoryInfo? GetById(string id)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] Getting repository by ID: {Id}",
            correlationId, id);
        
        var repo = _repositoryManager.GetRepository(id);
        
        if (repo == null)
        {
            _logger.LogDebug(
                "[{CorrelationId}] Repository not found: {Id}",
                correlationId, id);
        }
        
        return repo;
    }

    /// <summary>
    /// Get enabled repositories
    /// </summary>
    public List<RepositoryInfo> GetEnabled()
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] Getting enabled repositories",
            correlationId);
        
        var repos = _repositoryManager.GetEnabledRepositories();
        
        _logger.LogDebug(
            "[{CorrelationId}] Retrieved {Count} enabled repositories",
            correlationId, repos.Count);
        
        return repos;
    }

    /// <summary>
    /// Add a new repository
    /// </summary>
    public RepositoryInfo Add(RepositoryInfo repository)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogInformation(
            "[{CorrelationId}] Adding new repository: {Name} ({Url})",
            correlationId, repository.Name, repository.Url);
        
        var result = _repositoryManager.AddRepository(repository);
        
        _logger.LogInformation(
            "[{CorrelationId}] Repository added successfully: {Id}",
            correlationId, result.Id);
        
        return result;
    }

    /// <summary>
    /// Update a repository
    /// </summary>
    public bool Update(RepositoryInfo repository)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogInformation(
            "[{CorrelationId}] Updating repository: {Id}",
            correlationId, repository.Id);
        
        var result = _repositoryManager.UpdateRepository(repository);
        
        if (result)
        {
            _logger.LogInformation(
                "[{CorrelationId}] Repository updated successfully: {Id}",
                correlationId, repository.Id);
        }
        else
        {
            _logger.LogWarning(
                "[{CorrelationId}] Repository not found for update: {Id}",
                correlationId, repository.Id);
        }
        
        return result;
    }

    /// <summary>
    /// Delete a repository
    /// </summary>
    public bool Delete(string id)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogInformation(
            "[{CorrelationId}] Deleting repository: {Id}",
            correlationId, id);
        
        var result = _repositoryManager.DeleteRepository(id);
        
        if (result)
        {
            _logger.LogInformation(
                "[{CorrelationId}] Repository deleted successfully: {Id}",
                correlationId, id);
        }
        else
        {
            _logger.LogWarning(
                "[{CorrelationId}] Repository not found for deletion: {Id}",
                correlationId, id);
        }
        
        return result;
    }

    /// <summary>
    /// Trigger sync for a specific repository
    /// </summary>
    public async Task<bool> TriggerSyncAsync(string repositoryId)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogInformation(
            "[{CorrelationId}] Triggering sync for repository: {Id}",
            correlationId, repositoryId);
        
        var repo = _repositoryManager.GetRepository(repositoryId);
        if (repo == null)
        {
            _logger.LogWarning(
                "[{CorrelationId}] Repository not found: {Id}",
                correlationId, repositoryId);
            return false;
        }

        if (!repo.IsEnabled)
        {
            _logger.LogWarning(
                "[{CorrelationId}] Repository is disabled: {Id}",
                correlationId, repositoryId);
            return false;
        }

        try
        {
            // Update status to running
            _repositoryManager.UpdateSyncStatus(repositoryId, SyncStatus.Running);

            // Note: The actual sync is handled by GitSyncInvocable
            // This method just updates the status to indicate sync was triggered
            _logger.LogInformation(
                "[{CorrelationId}] Sync triggered for repository: {Name} ({Id})",
                correlationId, repo.Name, repo.Id);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{CorrelationId}] Failed to trigger sync for repository: {Id}",
                correlationId, repositoryId);
            
            _repositoryManager.UpdateSyncStatus(repositoryId, SyncStatus.Failed, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Get sync status for a repository
    /// </summary>
    public SyncStatus GetSyncStatus(string repositoryId)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] Getting sync status for repository: {Id}",
            correlationId, repositoryId);
        
        var repo = _repositoryManager.GetRepository(repositoryId);
        var status = repo?.SyncStatus ?? SyncStatus.NeverRun;
        
        _logger.LogDebug(
            "[{CorrelationId}] Sync status for repository {Id}: {Status}",
            correlationId, repositoryId, status);
        
        return status;
    }
}
