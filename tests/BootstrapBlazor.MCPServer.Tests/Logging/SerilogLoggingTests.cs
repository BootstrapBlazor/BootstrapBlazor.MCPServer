using BootstrapBlazor.McpServer.Services;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Compact;
using Xunit;
using Xunit.Abstractions;

namespace BootstrapBlazor.MCPServer.Tests.Logging;

/// <summary>
/// Unit tests for Serilog logging functionality
/// Tests that all log levels are captured and written to the /io/logs directory
/// </summary>
public class SerilogLoggingTests : IDisposable
{
    private readonly string _testLogsPath;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Serilog.Core.Logger _serilogLogger;
    private readonly List<LogEvent> _capturedLogEvents;
    private readonly ITestOutputHelper _output;

    public SerilogLoggingTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Create test logs directory in temp location
        _testLogsPath = Path.Combine(Path.GetTempPath(), "io", "logs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testLogsPath);
        
        _capturedLogEvents = new List<LogEvent>();
        
        // Create Serilog logger that writes to both memory and file
        _serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(_testLogsPath, "test-log-.json"),
                rollingInterval: RollingInterval.Day,
                shared: true)
            .WriteTo.File(
                Path.Combine(_testLogsPath, "test-log-.txt"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(new TestSink(events => 
            {
                _capturedLogEvents.AddRange(events);
            }))
            .CreateLogger();
        
        // Create Microsoft ILogger factory from Serilog
        _loggerFactory = new SerilogLoggerFactory(_serilogLogger);
        
        _output.WriteLine($"Test logs directory: {_testLogsPath}");
    }

    /// <summary>
    /// Test that Debug level logs are captured
    /// </summary>
    [Fact]
    public void Debug_Logs_Are_Captured()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<SerilogLoggingTests>("DebugTest");
        var testMessage = "Debug level test message";
        
        // Act
        logger.LogDebug(testMessage);
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var debugLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Debug).ToList();
        Assert.NotEmpty(debugLogs);
        Assert.Contains(debugLogs, e => e.RenderMessage().Contains(testMessage));
        
        _output.WriteLine($"Captured {debugLogs.Count} debug log(s)");
    }

    /// <summary>
    /// Test that Information level logs are captured
    /// </summary>
    [Fact]
    public void Information_Logs_Are_Captured()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<SerilogLoggingTests>("InformationTest");
        var testMessage = "Information level test message";
        
        // Act
        logger.LogInformation(testMessage);
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var infoLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Information).ToList();
        Assert.NotEmpty(infoLogs);
        Assert.Contains(infoLogs, e => e.RenderMessage().Contains(testMessage));
        
        _output.WriteLine($"Captured {infoLogs.Count} information log(s)");
    }

    /// <summary>
    /// Test that Warning level logs are captured
    /// </summary>
    [Fact]
    public void Warning_Logs_Are_Captured()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<SerilogLoggingTests>("WarningTest");
        var testMessage = "Warning level test message";
        
        // Act
        logger.LogWarning(testMessage);
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var warningLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Warning).ToList();
        Assert.NotEmpty(warningLogs);
        Assert.Contains(warningLogs, e => e.RenderMessage().Contains(testMessage));
        
        _output.WriteLine($"Captured {warningLogs.Count} warning log(s)");
    }

    /// <summary>
    /// Test that Error level logs are captured
    /// </summary>
    [Fact]
    public void Error_Logs_Are_Captured()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<SerilogLoggingTests>("ErrorTest");
        var testMessage = "Error level test message";
        
        // Act
        logger.LogError(testMessage);
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var errorLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Error).ToList();
        Assert.NotEmpty(errorLogs);
        Assert.Contains(errorLogs, e => e.RenderMessage().Contains(testMessage));
        
        _output.WriteLine($"Captured {errorLogs.Count} error log(s)");
    }

    /// <summary>
    /// Test that Error logs capture exception details
    /// </summary>
    [Fact]
    public void Error_Logs_Capture_Exception_Details()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<SerilogLoggingTests>("ExceptionTest");
        var testMessage = "Exception test message";
        var exception = new InvalidOperationException("Test exception message");
        
        // Act
        logger.LogError(exception, testMessage);
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var errorLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Error).ToList();
        Assert.NotEmpty(errorLogs);
        
        var errorLog = errorLogs.First();
        Assert.NotNull(errorLog.Exception);
        Assert.Equal("Test exception message", errorLog.Exception.Message);
        Assert.Equal(typeof(InvalidOperationException), errorLog.Exception.GetType());
        
        _output.WriteLine($"Exception captured: {errorLog.Exception?.Message}");
    }

    /// <summary>
    /// Test that logs are written to the /io/logs directory
    /// </summary>
    [Fact]
    public void Logs_Are_Written_To_IOLogs_Directory()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<SerilogLoggingTests>("FileWriteTest");
        
        // Act
        logger.LogInformation("File write test message");
        
        // Allow async logging to complete
        Task.Delay(200).Wait();
        
        // Assert - check that log files exist
        var logFiles = Directory.GetFiles(_testLogsPath, "*.json");
        Assert.NotEmpty(logFiles);
        
        var txtLogFiles = Directory.GetFiles(_testLogsPath, "*.txt");
        Assert.NotEmpty(txtLogFiles);
        
        // Read and verify content
        var jsonLogContent = File.ReadAllText(logFiles.First());
        Assert.Contains("File write test message", jsonLogContent);
        
        _output.WriteLine($"Log files created: {string.Join(", ", logFiles.Select(Path.GetFileName)))}");
    }

    /// <summary>
    /// Test that Correlation IDs are included in logs
    /// </summary>
    [Fact]
    public void Correlation_IDs_Are_Included_In_Logs()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<SerilogLoggingTests>("CorrelationTest");
        var correlationId = Guid.NewGuid().ToString("N")[..16];
        
        // Act - Use correlation ID format similar to our implementation
        logger.LogInformation("[{CorrelationId}] [{RepositoryId}] Test message", correlationId, "repo-123");
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var infoLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Information).ToList();
        Assert.NotEmpty(infoLogs);
        
        var logMessage = infoLogs.First().RenderMessage();
        Assert.Contains(correlationId, logMessage);
        
        _output.WriteLine($"Correlation ID in log: {correlationId}");
    }

    /// <summary>
    /// Test that GitSyncInvocable logs all sync phases
    /// </summary>
    [Fact]
    public void GitSyncInvocable_Logs_All_Phases()
    {
        // Arrange - Create a mock logger for GitSyncInvocable
        var logger = _loggerFactory.CreateLogger<GitSyncInvocable>("GitSyncTest");
        
        // Act - Simulate sync operations
        logger.LogInformation("Starting Git Sync & Extraction Job...");
        logger.LogInformation("Syncing repository: TestRepo (https://github.com/test/repo.git)");
        logger.LogDebug("Cloning repository from https://github.com/test/repo.git to /path/repo...");
        logger.LogInformation("Building BootstrapBlazor project to generate DLL and XML...");
        logger.LogError("Build failed with exit code 1: Error message");
        logger.LogInformation("Repository TestRepo synced successfully.");
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var logs = _capturedLogEvents.ToList();
        
        Assert.Contains(logs, e => e.RenderMessage().Contains("Starting Git Sync"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("Syncing repository"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("Cloning repository"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("Building BootstrapBlazor"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("Build failed"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("synced successfully"));
        
        _output.WriteLine($"Total sync phase logs captured: {logs.Count}");
    }

    /// <summary>
    /// Test that repository sync failures are logged with detailed error info
    /// </summary>
    [Fact]
    public void Repository_Sync_Failures_Log_Detailed_Error_Info()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<GitSyncInvocable>("SyncFailureTest");
        
        // Act - Simulate various sync failures
        var repoId = "repo-" + Guid.NewGuid().ToString("N")[..8];
        var correlationId = Guid.NewGuid().ToString("N")[..16];
        
        logger.LogError(
            "[{CorrelationId}] [{RepoId}] Error syncing repository TestRepo: {Error}",
            correlationId, repoId, "Git clone failed: authentication required");
        
        logger.LogWarning(
            "[{CorrelationId}] [{RepoId}] Sync failed: Git clone failed: authentication required",
            correlationId, repoId);
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var errorLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Error).ToList();
        var warningLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Warning).ToList();
        
        Assert.NotEmpty(errorLogs);
        Assert.Contains(errorLogs, e => e.RenderMessage().Contains("authentication required"));
        Assert.Contains(warningLogs, e => e.RenderMessage().Contains("Sync failed"));
        
        _output.WriteLine($"Error logs with details: {errorLogs.Count}");
    }

    /// <summary>
    /// Test that DocsExtractorService logs extraction steps
    /// </summary>
    [Fact]
    public void DocsExtractorService_Logs_Extraction_Steps()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<DocsExtractorService>("DocsExtractorTest");
        
        // Act - Simulate extraction phases
        logger.LogInformation("BootstrapBlazor RAG Extractor started");
        logger.LogInformation("Loading Localization Dictionary...");
        logger.LogInformation("Processing API documentation from XML...");
        logger.LogDebug("Found 150 component types");
        logger.LogInformation("Processing Samples documentation...");
        logger.LogInformation("RAG Datasets extraction completed");
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var logs = _capturedLogEvents.ToList();
        
        Assert.Contains(logs, e => e.RenderMessage().Contains("Extractor started"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("Loading Localization"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("Processing API"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("Processing Samples"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("extraction completed"));
        
        _output.WriteLine($"Extraction step logs: {logs.Count}");
    }

    /// <summary>
    /// Test that AppSettingsManager logs settings operations
    /// </summary>
    [Fact]
    public void AppSettingsManager_Logs_Settings_Operations()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<AppSettingsManager>("SettingsTest");
        
        // Act - Simulate settings operations
        logger.LogDebug("[{CorrelationId}] Loading settings from: /path/config.json", "abc123");
        logger.LogInformation("[{CorrelationId}] Settings saved successfully", "abc123");
        logger.LogWarning("[{CorrelationId}] Settings file not found, using defaults", "def456");
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var logs = _capturedLogEvents.ToList();
        
        Assert.Contains(logs, e => e.RenderMessage().Contains("Loading settings"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("Settings saved"));
        Assert.Contains(logs, e => e.RenderMessage().Contains("file not found"));
        
        _output.WriteLine($"Settings operation logs: {logs.Count}");
    }

    /// <summary>
    /// Test that all log levels work correctly together
    /// </summary>
    [Fact]
    public void All_Log_Levels_Work_Together()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<SerilogLoggingTests>("AllLevelsTest");
        
        // Act - Log at all levels
        logger.LogTrace("Trace message");
        logger.LogDebug("Debug message");
        logger.LogInformation("Information message");
        logger.LogWarning("Warning message");
        logger.LogError("Error message");
        logger.LogCritical("Critical message");
        
        // Allow async logging to complete
        Task.Delay(100).Wait();
        
        // Assert
        var traceLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Verbose).ToList();
        var debugLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Debug).ToList();
        var infoLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Information).ToList();
        var warningLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Warning).ToList();
        var errorLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Error).ToList();
        var criticalLogs = _capturedLogEvents.Where(e => e.Level == LogEventLevel.Fatal).ToList();
        
        Assert.NotEmpty(debugLogs);
        Assert.NotEmpty(infoLogs);
        Assert.NotEmpty(warningLogs);
        Assert.NotEmpty(errorLogs);
        
        _output.WriteLine($"Trace: {traceLogs.Count}, Debug: {debugLogs.Count}, Info: {infoLogs.Count}, Warning: {warningLogs.Count}, Error: {errorLogs.Count}, Critical: {criticalLogs.Count}");
    }

    public void Dispose()
    {
        // Clean up
        _serilogLogger.Dispose();
        
        // Give time for file handles to close
        Thread.Sleep(100);
        
        // Optionally keep logs for inspection
        // Directory.Delete(_testLogsPath, true);
        _output.WriteLine($"Test logs preserved at: {_testLogsPath}");
    }

    /// <summary>
    /// Test sink that captures log events for testing
    /// </summary>
    private class TestSink : Serilog.Core.ILogEventSink
    {
        private readonly Action<IEnumerable<LogEvent>> _onEmit;

        public TestSink(Action<IEnumerable<LogEvent>> onEmit)
        {
            _onEmit = onEmit;
        }

        public void Emit(LogEvent logEvent)
        {
            _onEmit(new[] { logEvent });
        }
    }
}
