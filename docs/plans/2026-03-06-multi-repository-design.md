# Multi-Repository MCP Server Design

**Date:** 2026-03-06
**Status:** Approved
**Scope:** Transform single-repo MCP server into multi-repository platform

---

## Executive Summary

Transform the BootstrapBlazor MCP Server from supporting a single hardcoded repository to managing **multiple repositories** with:
- SQLite database for repo configurations and metadata
- Per-repo extraction pipelines (Blazor, .NET, Markdown, Custom)
- Background sync and metadata refresh
- Dashboard UI with repository cards
- Cross-repository MCP tool queries

---

## 1. Data Model

### 1.1 Database: SQLite via Entity Framework Core

**Connection String:** `Data Source=data/mcpserver.db`

### 1.2 Entities

```csharp
// Main repository entity
public class Repository
{
    public int Id { get; set; }

    // Identity
    public string Name { get; set; }           // "BootstrapBlazor"
    public string Slug { get; set; }           // "bootstrap-blazor" (URL-safe)
    public string Url { get; set; }            // "https://github.com/ArgoZhang/BootstrapBlazor"
    public GitProvider Provider { get; set; }  // GitHub, Gitee, GitLab, Other

    // Display
    public string? Description { get; set; }
    public string Category { get; set; }       // "UI Components", "Charts", "Icons"
    public string? IconUrl { get; set; }

    // GitHub/Gitee metadata (refreshed hourly)
    public int Stars { get; set; }
    public int Forks { get; set; }
    public int Watchers { get; set; }
    public int OpenIssues { get; set; }
    public DateTime? LastCommitDate { get; set; }
    public string? LastCommitSha { get; set; }
    public string? LatestVersion { get; set; }

    // Extraction configuration
    public ExtractorType ExtractorType { get; set; }  // AutoDetect, Blazor, DotNet, Markdown, Custom
    public string? BuildCommand { get; set; }         // Default: "dotnet build"
    public string? BuildProjectPath { get; set; }     // Relative path to .csproj
    public string? DllName { get; set; }              // For non-standard DLL names
    public string? DocsPath { get; set; }             // Custom docs folder
    public string? CustomExtractorCommand { get; set; }

    // Storage paths
    public string LocalPath { get; set; }       // Clone directory
    public string OutputDir { get; set; }       // RAG output directory

    // Sync configuration
    public string CronSchedule { get; set; } = "0 */4 * * *";
    public bool IsEnabled { get; set; } = true;

    // Sync status
    public SyncStatus SyncStatus { get; set; } = SyncStatus.NeverRun;
    public DateTime? LastSyncAt { get; set; }
    public int? LastSyncDurationSeconds { get; set; }
    public string? LastSyncError { get; set; }

    // Metadata refresh
    public DateTime? LastMetadataRefresh { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum GitProvider { GitHub, Gitee, GitLab, Other }
public enum ExtractorType { AutoDetect, BlazorComponent, DotNetLibrary, MarkdownDocs, CustomScript }
public enum SyncStatus { NeverRun, Running, Success, Failed }

// Curated trending suggestions
public class TrendingSuggestion
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
    public string Category { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public bool IsVisible { get; set; } = true;
    public int SortOrder { get; set; }
}
```

---

## 2. Service Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                           UI Layer                                   │
│   Index.razor        │   Repos.razor (Admin)   │   Config.razor     │
│   (Repo Cards)       │   (CRUD + Sync)         │   (Settings)       │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
┌───────────────────────────────▼─────────────────────────────────────┐
│                      RepositoryService                               │
│   - GetAllAsync(), GetByIdAsync(), AddAsync(), UpdateAsync()        │
│   - DeleteAsync(), TriggerSyncAsync(), GetSyncStatusAsync()         │
│   - GetByCategoryAsync(), SearchAsync()                             │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
        ┌───────────────────────┼───────────────────────┐
        ▼                       ▼                       ▼
┌───────────────────┐  ┌───────────────────┐  ┌────────────────────┐
│   McpDbContext    │  │   GitSyncService  │  │  MetadataService   │
│   (EF Core)       │  │   (Multi-repo)    │  │  (GitHub API)      │
└───────────────────┘  └─────────┬─────────┘  └────────────────────┘
                                 │
                                 ▼
                       ┌───────────────────┐
                       │ ExtractionEngine  │
                       │ (Plugin-based)    │
                       └─────────┬─────────┘
                                 │
        ┌────────────────────────┼────────────────────────┐
        ▼                        ▼                        ▼
┌─────────────────┐    ┌─────────────────┐    ┌──────────────────┐
│BlazorExtractor  │    │DotNetExtractor  │    │MarkdownExtractor │
└─────────────────┘    └─────────────────┘    └──────────────────┘
```

### 2.1 Core Services

| Service | Responsibility |
|---------|----------------|
| `RepositoryService` | CRUD operations, orchestration, business logic |
| `GitSyncService` | Clone/pull/build/extract per repo |
| `MetadataRefreshService` | Background GitHub/Gitee API polling |
| `ExtractionEngine` | Plugin selection and execution |
| `TrendingService` | Curated suggestions + live stats |

---

## 3. Extraction Pipeline (Multi-Repo Aware)

### 3.1 Plugin Interface

```csharp
public interface IExtractorPlugin
{
    string Name { get; }
    int Priority { get; }
    bool CanHandle(string repoPath, Repository repo);
    Task<ExtractionResult> ExtractAsync(string repoPath, string outputDir, Repository repo);
}
```

### 3.2 Output Directory Structure

```
data/
├── mcpserver.db
├── repos/
│   ├── bootstrap-blazor/
│   │   └── ... (cloned repo)
│   ├── vizor-echarts/
│   │   └── ...
│   └── bootstrap-icons/
│       └── ...
└── output/
    ├── bootstrap-blazor/
    │   ├── API/
    │   │   ├── Button.md
    │   │   ├── Table.md
    │   │   └── ...
    │   ├── Samples/
    │   │   ├── Button.md
    │   │   └── ...
    │   └── meta.json
    ├── vizor-echarts/
    │   ├── API/
    │   └── meta.json
    └── bootstrap-icons/
        └── ...
```

### 3.3 Plugin Types

| Plugin | Detects By | Extracts |
|--------|------------|----------|
| `BlazorComponentExtractor` | `.razor` files + `IComponent` in DLL | API params, samples |
| `DotNetLibraryExtractor` | `.csproj` + DLL | Public API + XML docs |
| `MarkdownDocsExtractor` | `.md` files | Copies markdown, parses frontmatter |
| `CustomScriptExtractor` | `CustomExtractorCommand` set | Executes user script |

---

## 4. MCP Service (Multi-Repo Queries)

### 4.1 Updated Tools

```csharp
// List all components across ALL repos
[McpServerTool]
public string GetComponentList(string? repository = null, string? category = null)

// Search across ALL repos
[McpServerTool]
public string SearchComponentKeyword(string keyword, string? repository = null)

// Get docs for a component (repo-prefixed: "BootstrapBlazor/Button")
[McpServerTool]
public string GetComponentDocs(string componentName, string? repository = null)

// Ask expert with repo context
[McpServerTool]
public async Task<string> AskComponentExpert(string componentName, string question, string? repository = null)

// NEW: List all available repositories
[McpServerTool]
public string GetRepositoryList()

// NEW: Get repository info
[McpServerTool]
public string GetRepositoryInfo(string slug)
```

### 4.2 Component Naming Convention

- Single repo mode: `Button`, `Table`
- Multi-repo mode: `BootstrapBlazor/Button`, `VizorEcharts/Chart`
- Backward compatible: If no slash, search all repos

---

## 5. Background Jobs (Coravel)

| Job | Schedule | Purpose |
|-----|----------|---------|
| `RepoSyncJob` | Per-repo cron | Clone/pull/build/extract |
| `MetadataRefreshJob` | Every 1 hour | Update stars/forks/version from API |
| `InitialSyncJob` | On startup | Auto-sync repos with `SyncStatus == NeverRun` |

---

## 6. API Endpoints

```
GET    /api/repositories                    - List all repos
GET    /api/repositories/{slug}             - Get repo details
POST   /api/repositories                    - Add new repo
PUT    /api/repositories/{slug}             - Update repo
DELETE /api/repositories/{slug}             - Delete repo
POST   /api/repositories/{slug}/sync        - Trigger manual sync
GET    /api/repositories/{slug}/status      - Get sync status
GET    /api/trending                        - Get trending suggestions
POST   /api/trending/{id}/add               - Add trending to managed repos
```

---

## 7. UI Pages

### 7.1 Home Page (Index.razor)

```
┌────────────────────────────────────────────────────────────────────┐
│  Meccano.Framework.MCP - Dashboard                                 │
├────────────────────────────────────────────────────────────────────┤
│  ┌─ Overview ─────────────────────────────────────────────────────┐│
│  │ 🟢 Online  │  3 Repositories  │  Last Sync: 2 min ago         ││
│  └─────────────────────────────────────────────────────────────────┘│
│                                                                    │
│  ┌─ Repositories ─────────────────────────────────────────────────┐│
│  │  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐           ││
│  │  │ Bootstrap    │ │ Vizor        │ │ Bootstrap    │           ││
│  │  │ Blazor       │ │ ECharts      │ │ Icons        │           ││
│  │  │              │ │              │ │              │           ││
│  │  │ ⭐ 1.2k      │ │ ⭐ 456       │ │ ⭐ 234       │           ││
│  │  │ v9.0.0       │ │ v2.1.0       │ │ v1.5.0       │           ││
│  │  │ UI Components│ │ Charts       │ │ Icons        │           ││
│  │  │ Synced: 2h   │ │ Synced: 4h   │ │ Synced: 1d   │           ││
│  │  │ [▶ Details]  │ │ [▶ Details]  │ │ [▶ Details]  │           ││
│  │  └──────────────┘ └──────────────┘ └──────────────┘           ││
│  │                                                                ││
│  │  [+ Add Repository]                                            ││
│  └─────────────────────────────────────────────────────────────────┘│
│                                                                    │
│  ┌─ Trending Suggestions ─────────────────────────────────────────┐│
│  │  🔥 UI Components    │  📊 Charts       │  🎨 Icons            ││
│  │  • Blazorise        │  • ApexCharts    │  • FA.Blazor         ││
│  │  • MudBlazor        │  • ChartJs       │  • BlazorIcons       ││
│  │  [Add]              │  [Add]           │  [Add]               ││
│  └─────────────────────────────────────────────────────────────────┘│
└────────────────────────────────────────────────────────────────────┘
```

### 7.2 Repositories Admin Page (Repos.razor)

- Full table with all metadata
- Edit/Delete/Enable/Disable
- Manual sync trigger
- View sync logs/errors

### 7.3 Add Repository Modal

- URL input (auto-detect provider, name, slug)
- Category selector
- Extractor type selector (with auto-detect option)
- Custom build command (optional)
- Cron schedule picker

---

## 8. Localization Fix

Create `.resx` files with proper values:

```
PageTitle = "Dashboard - Meccano.Framework.MCP"
AppTitle = "Meccano.Framework.MCP"
Dashboard = "Dashboard"
Settings = "Settings"
Logout = "Logout"
ServerStatus = "Server Status"
Online = "Online"
Repositories = "Repositories"
AddRepository = "Add Repository"
TrendingSuggestions = "Trending Suggestions"
```

---

## 9. Configuration

### appsettings.json additions

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=data/mcpserver.db"
  },
  "GitHub": {
    "Token": ""
  },
  "Gitee": {
    "Token": ""
  },
  "Sync": {
    "DefaultCron": "0 */4 * * *",
    "AutoSyncOnStartup": true,
    "MaxConcurrentSyncs": 2
  },
  "Metadata": {
    "RefreshIntervalHours": 1
  }
}
```

---

## 10. File Structure

```
Services/
├── RepositoryService.cs           # NEW - CRUD + orchestration
├── GitSyncService.cs              # NEW - Per-repo sync
├── MetadataRefreshService.cs      # NEW - GitHub/Gitee API
├── TrendingService.cs             # NEW - Curated suggestions
├── McpDbContext.cs                # NEW - EF Core context
│
├── Extraction/
│   ├── IExtractorPlugin.cs        # NEW
│   ├── ExtractionResult.cs        # NEW
│   ├── ExtractionEngine.cs        # NEW
│   └── Plugins/
│       ├── BlazorComponentExtractor.cs   # NEW
│       ├── DotNetLibraryExtractor.cs     # NEW
│       ├── MarkdownDocsExtractor.cs      # NEW
│       └── CustomScriptExtractor.cs      # NEW
│
├── DocsExtractorService.cs        # REFACTOR into plugins
├── McpService.cs                  # UPDATE - Multi-repo queries
├── AppSettingsManager.cs          # KEEP - Non-repo settings
└── AiIntegrationService.cs        # KEEP (multi-provider TBD)

Models/
├── Repository.cs                  # NEW
├── TrendingSuggestion.cs          # NEW
├── GitProvider.cs                 # NEW - Enum
├── ExtractorType.cs               # NEW - Enum
├── SyncStatus.cs                  # NEW - Enum
└── AppSettingsModel.cs            # KEEP

Invocables/
├── RepoSyncJob.cs                 # NEW - Per-repo sync
├── MetadataRefreshJob.cs          # NEW - Background refresh
└── InitialSyncJob.cs              # NEW - Startup check

Components/Pages/
├── Index.razor                    # REDESIGN - Repo cards
├── Repos.razor                    # NEW - Admin page
├── Config.razor                   # KEEP
└── Login.razor                    # KEEP

Resources/
├── Pages/
│   ├── Index.en-US.resx           # NEW
│   └── Index.zh-CN.resx           # NEW
└── Layout/
    ├── MainLayout.en-US.resx      # NEW
    └── MainLayout.zh-CN.resx      # NEW
```

---

## 11. Implementation Phases

### Phase 1: Foundation (Database + Models)
- Add EF Core SQLite package
- Create `McpDbContext` and entities
- Run migrations
- Update Program.cs DI

### Phase 2: Repository Service
- `RepositoryService` (CRUD)
- Seed default curated trending suggestions
- API endpoints for repos

### Phase 3: Extraction Engine
- `IExtractorPlugin` interface
- `ExtractionEngine`
- Port `DocsExtractorService` to `BlazorComponentExtractor`
- Add `MarkdownDocsExtractor` for docs-only repos

### Phase 4: Sync Service
- `GitSyncService` (multi-repo aware)
- `RepoSyncJob` (Coravel)
- `InitialSyncJob` (startup)

### Phase 5: Metadata Service
- `MetadataRefreshService`
- GitHub/Gitee API clients
- `MetadataRefreshJob`

### Phase 6: MCP Service Update
- Update `McpService` for multi-repo queries
- Cross-repo search
- Repository-prefixed component names

### Phase 7: UI
- Fix localization (PageTitle, AppTitle)
- Redesign `Index.razor` with repo cards
- Create `Repos.razor` admin page
- Add Repository modal

---

## 12. Migration Strategy

1. **Preserve existing data:** On first run with SQLite, check for existing `config.json` and migrate single-repo settings to a Repository record
2. **Backward compatible MCP:** Component queries without repo prefix search all repos
3. **Existing output:** If `data/OutputRAG/` exists, move to `data/output/bootstrap-blazor/`

---

**Approved by:** User
**Date:** 2026-03-06
