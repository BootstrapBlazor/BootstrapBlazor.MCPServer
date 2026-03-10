using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BootstrapBlazor.McpServer.Services;

/// <summary>
/// Provides correlation ID infrastructure for tracking sync operations end-to-end
/// </summary>
public class SyncContext
{
    private static readonly ConcurrentDictionary<string, SyncOperation> _operations = new();
    
    /// <summary>
    /// Creates a new sync context with correlation ID for tracking operations
    /// </summary>
    public static SyncOperation StartOperation(string repositoryId, string operationType)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..16];
        var operation = new SyncOperation
        {
            CorrelationId = correlationId,
            RepositoryId = repositoryId,
            OperationType = operationType,
            StartedAt = DateTime.UtcNow
        };
        
        _operations[correlationId] = operation;
        return operation;
    }
    
    /// <summary>
    /// Gets an existing operation by correlation ID
    /// </summary>
    public static SyncOperation? GetOperation(string correlationId)
    {
        return _operations.TryGetValue(correlationId, out var operation) ? operation : null;
    }
    
    /// <summary>
    /// Completes an operation and calculates duration
    /// </summary>
    public static void CompleteOperation(string correlationId, bool success, string? errorMessage = null)
    {
        if (_operations.TryGetValue(correlationId, out var operation))
        {
            operation.CompletedAt = DateTime.UtcNow;
            operation.Success = success;
            operation.ErrorMessage = errorMessage;
            operation.Duration = operation.CompletedAt - operation.StartedAt;
        }
    }
    
    /// <summary>
    /// Clears completed operations older than specified hours
    /// </summary>
    public static void CleanupOldOperations(int hoursOld = 24)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hoursOld);
        var keysToRemove = _operations
            .Where(kvp => kvp.Value.CompletedAt.HasValue && kvp.Value.CompletedAt.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in keysToRemove)
        {
            _operations.TryRemove(key, out _);
        }
    }
}

/// <summary>
/// Represents a sync operation with correlation tracking
/// </summary>
public class SyncOperation
{
    public string CorrelationId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan? Duration { get; set; }
}

/// <summary>
/// Logger extension methods for structured logging with correlation IDs
/// </summary>
public static class SyncLoggerExtensions
{
    /// <summary>
    /// Logs with sync operation context
    /// </summary>
    public static void LogSyncOperation(this ILogger logger, LogLevel level, string correlationId, string repositoryId, 
        string operation, string message, Exception? exception = null)
    {
        var state = new SyncLogState
        {
            CorrelationId = correlationId,
            RepositoryId = repositoryId,
            Operation = operation
        };
        
        if (exception != null)
            logger.Log(level, exception, "[{CorrelationId}] [{RepositoryId}] {Operation}: {Message}", 
                correlationId, repositoryId, operation, message);
        else
            logger.Log(level, "[{CorrelationId}] [{RepositoryId}] {Operation}: {Message}", 
                correlationId, repositoryId, operation, message);
    }
    
    /// <summary>
    /// Logs sync operation starting
    /// </summary>
    public static void LogSyncStart(this ILogger logger, string correlationId, string repositoryId, string operation)
    {
        logger.LogInformation("[{CorrelationId}] [{RepositoryId}] Starting: {Operation}", 
            correlationId, repositoryId, operation);
    }
    
    /// <summary>
    /// Logs sync operation completion
    /// </summary>
    public static void LogSyncComplete(this ILogger logger, string correlationId, string repositoryId, 
        string operation, bool success, TimeSpan duration, string? errorMessage = null)
    {
        if (success)
        {
            logger.LogInformation("[{CorrelationId}] [{RepositoryId}] Completed: {Operation} in {Duration}ms", 
                correlationId, repositoryId, operation, duration.TotalMilliseconds);
        }
        else
        {
            logger.LogError("[{CorrelationId}] [{RepositoryId}] Failed: {Operation} after {Duration}ms - {Error}", 
                correlationId, repositoryId, operation, duration.TotalMilliseconds, errorMessage ?? "Unknown error");
        }
    }
}

/// <summary>
/// State object for log scope
/// </summary>
public class SyncLogState
{
    public string CorrelationId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
}
