# Serilog Logging Implementation Plan

## Overview
Add comprehensive logging using Serilog to all services in the BootstrapBlazor.MCPServer project.

## Services to Log

| Service | Purpose | Key Logging Points |
|---------|---------|-------------------|
| `GitSyncInvocable` | Main sync orchestrator | Sync initialization, clone/pull, build, extraction, completion, errors |
| `RepositoryManager` | Repository CRUD | Load, add, update, delete, sync status updates |
| `DocsExtractorService` | Documentation extraction | XML processing, assembly loading, sample extraction, errors |
| `McpService` | MCP server tools | Tool invocations, component lookups |
| `AiIntegrationService` | AI integration | API calls, responses, errors |
| `AppSettingsManager` | Settings | Load/save operations |
| `RepositoryService` | Repository operations | CRUD wrapper operations |

## Implementation Steps

### 1. Add Serilog Packages
- Serilog
- Serilog.Sinks.File
- Serilog.Extensions.Logging
- Serilog.Formatting.Compact (for JSON formatting)

### 2. Configure Serilog in Program.cs
- Output directory: `/io/logs`
- File naming: `log-{Date}.json`
- Rolling: Daily
- Retained: 30 days
- Levels: Debug minimum, Information default

### 3. Correlation ID Infrastructure
- Create `SyncContext` class for correlation IDs
- Use `ILogger.BeginScope` for contextual logging
- Track: RepositoryId, SyncId, Operation type

### 4. Enhanced Logging by Service

#### GitSyncInvocable
- **Initialization**: Log sync start with correlation ID
- **Data Fetch**: Log clone/pull operations with URLs
- **Transformation**: Log build start/end with exit codes
- **Database Ops**: Log status updates (Running, Success, Failed)
- **Errors**: Log exceptions with full context
- **Completion**: Log sync success/failure with duration

#### RepositoryManager
- Log all CRUD operations
- Log sync status transitions
- Log migration events

#### DocsExtractorService
- Log extraction steps
- Log file discoveries
- Log processing results

#### Other Services
- Add appropriate logging for operations

### 5. Unit Tests
- Test all log levels (Debug, Information, Warning, Error)
- Validate `/io/logs` directory output
- Validate error logging captures exceptions
- Validate correlation IDs are present

## File Structure Changes

```
BootstrapBlazor.McpServer.csproj  - Add Serilog packages
Program.cs                        - Configure Serilog
Services/
  SyncContext.cs                  - NEW: Correlation ID infrastructure
  GitSyncInvocable.cs             - Enhanced logging
  RepositoryManager.cs            - Enhanced logging
  DocsExtractorService.cs         - Enhanced logging
  McpService.cs                   - Enhanced logging
  AiIntegrationService.cs         - Enhanced logging
  AppSettingsManager.cs           - Enhanced logging
  RepositoryService.cs            - Enhanced logging
tests/
  BootstrapBlazor.MCPServer.Tests/
    Logging/
      SerilogLoggingTests.cs     - NEW: Unit tests
```
