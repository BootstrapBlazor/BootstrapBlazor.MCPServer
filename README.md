# BootstrapBlazor MCP Server

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![BootstrapBlazor](https://img.shields.io/badge/BootstrapBlazor-10.x-blue)](https://www.blazor.zone/)
[![MCP](https://img.shields.io/badge/MCP-Model%20Context%20Protocol-green)](https://modelcontextprotocol.io/)
[![Docker](https://img.shields.io/badge/Docker-Ready-blue?logo=docker)](https://www.docker.com/)
[![License](https://img.shields.io/badge/License-MIT-yellow)](LICENSE)

[中文版](README.zh-CN.md) | English

A **Model Context Protocol (MCP) Server** built on ASP.NET Core and BootstrapBlazor that provides AI assistants (such as Claude, Cursor, Dify, FastGPT, etc.) with intelligent access to the full **BootstrapBlazor component library documentation**, including API parameters and live code samples.

---

## ✨ Features

- 🔄 **Auto Git Sync** — Periodically clones/pulls the official BootstrapBlazor repository on a configurable cron schedule.
- 📄 **Doc Extraction** — Automatically extracts component API tables (via reflection + XML docs) and Razor/C# code samples into Markdown files, ready for RAG.
- 🤖 **MCP Tools** — Exposes standard MCP tools (`GetComponentList`, `SearchComponentKeyword`, `GetComponentDocs`, `AskComponentExpert`) for any MCP-compatible AI client.
- 🌐 **REST API** — Provides HTTP endpoints for non-MCP integrations (Dify, FastGPT, custom agents).
- 🧠 **Optional AI Q&A** — Integrates with any OpenAI-compatible API to answer natural-language questions about BootstrapBlazor components.
- 🖥️ **Admin UI** — A built-in Blazor management dashboard (with login protection) to configure settings, trigger syncs, and monitor status.
- 🌍 **i18n Support** — UI supports both `zh-CN` and `en-US` locales.
- 🐳 **Docker Ready** — Ships with a `Dockerfile` for one-command deployment.

---

## 🏗️ Architecture

```
┌────────────────────────────────────────────────────────┐
│                  BootstrapBlazor.McpServer              │
│                                                        │
│  ┌──────────────┐   ┌──────────────────────────────┐  │
│  │ GitSync Job  │──▶│   DocsExtractorService        │  │
│  │ (Coravel     │   │  (Reflection + XML + Razor)   │  │
│  │  Cron)       │   └────────────┬─────────────────┘  │
│  └──────────────┘                │ Markdown Files      │
│                                  ▼                     │
│  ┌──────────────────────────────────────────────────┐  │
│  │              McpService (MCP Tools)               │  │
│  │  GetComponentList | SearchComponentKeyword        │  │
│  │  GetComponentDocs | AskComponentExpert            │  │
│  └──────┬────────────────────────┬──────────────────┘  │
│         │                        │                     │
│         ▼                        ▼                     │
│  ┌─────────────┐      ┌──────────────────────┐        │
│  │  MCP / SSE  │      │  REST HTTP API        │        │
│  │  /mcp       │      │  /api/components/...  │        │
│  └─────────────┘      └──────────────────────┘        │
└────────────────────────────────────────────────────────┘
```

---

## 🚀 Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git (installed and available in PATH)

### Run Locally

```bash
git clone https://github.com/your-org/BootstrapBlazor.McpServer.git
cd BootstrapBlazor.McpServer

# Edit appsettings.json with your configuration (see Configuration section)
dotnet run
```

The server starts at `http://localhost:5251` by default.

### Run with Docker

```bash
docker build -t bootstrapblazor-mcp .

docker run -d \
  -p 5251:5251 \
  -e GitSync__RepositoryUrl="https://gitee.com/LongbowEnterprise/BootstrapBlazor.git" \
  -e GitSync__CronSchedule="0 3 * * *" \
  -e GitSync__OutputDir="/app/data/OutputRAG" \
  -e AI__BaseUrl="https://api.openai.com/v1" \
  -e AI__ApiKey="YOUR_API_KEY" \
  -e AI__Model="gpt-4o" \
  -v /your/data/path:/app/data \
  bootstrapblazor-mcp
```

---

## ⚙️ Configuration

Edit `appsettings.json` or use environment variables (Docker-friendly):

```json
{
  "GitSync": {
    "RepositoryUrl": "https://gitee.com/LongbowEnterprise/BootstrapBlazor.git",
    "CronSchedule": "0 3 * * *",
    "LocalPath": "/app/data/BootstrapBlazorRepo",
    "OutputDir": "/app/data/OutputRAG"
  },
  "AI": {
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "YOUR_API_KEY_HERE",
    "Model": "gpt-4o"
  }
}
```

| Key | Description | Default |
|-----|-------------|---------|
| `GitSync:RepositoryUrl` | URL of the BootstrapBlazor source repository | Gitee mirror |
| `GitSync:CronSchedule` | Cron expression for auto-sync | `0 3 * * *` (3 AM daily) |
| `GitSync:LocalPath` | Local path to clone the repo into | `/app/data/BootstrapBlazorRepo` |
| `GitSync:OutputDir` | Output directory for generated Markdown | `/app/data/OutputRAG` |
| `AI:BaseUrl` | OpenAI-compatible API base URL | `https://api.openai.com/v1` |
| `AI:ApiKey` | API key for the AI service | — |
| `AI:Model` | Model name to use | `gpt-4o` |

---

## 🔌 MCP Integration

### Connecting from Claude Desktop

Add the following to your Claude Desktop config (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "bootstrapblazor": {
      "url": "http://localhost:5251/mcp"
    }
  }
}
```

### Connecting from Cursor

In Cursor settings, add an MCP server pointing to `http://localhost:5251/mcp`.

### Available MCP Tools

| Tool | Description |
|------|-------------|
| `GetComponentList` | Returns a list of all available BootstrapBlazor components |
| `SearchComponentKeyword` | Searches for components matching a keyword |
| `GetComponentDocs` | Returns the raw API table + code samples for a component |
| `AskComponentExpert` | Answers a natural-language question about a component (uses AI if enabled) |

---

## 🌐 REST API

For non-MCP integrations (e.g., Dify, FastGPT):

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/components` | List all component names |
| `GET` | `/api/components/search?keyword={kw}` | Search components by keyword |
| `GET` | `/api/components/{name}/docs` | Get raw docs for a component |
| `GET` | `/api/components/{name}/ask?q={question}` | Ask an AI-powered question |

---

## 🖥️ Admin Dashboard

Navigate to `http://localhost:5251` to access the Blazor admin UI. Log in with the credentials configured in `appsettings.json` (default: see your settings).

Features:
- View and edit server configuration
- Manually trigger Git sync
- Monitor sync status and logs
- Toggle AI integration on/off

---

## 🛠️ Tech Stack

| Layer | Technology |
|-------|------------|
| Framework | ASP.NET Core (.NET 10) |
| UI | BootstrapBlazor (Blazor Server) |
| MCP | ModelContextProtocol SDK 0.9 |
| AI Integration | Microsoft.Extensions.AI + OpenAI |
| Git Sync | LibGit2Sharp |
| Scheduling | Coravel |
| i18n | ASP.NET Core Localization |

---

## 📁 Project Structure

```
BootstrapBlazor.McpServer/
├── Components/         # Blazor UI components (Admin pages, App shell)
├── Services/
│   ├── McpService.cs           # MCP tool definitions
│   ├── DocsExtractorService.cs # Docs extraction logic (reflection + XML + Razor)
│   ├── GitSyncInvocable.cs     # Git clone/pull + trigger extraction
│   ├── AiIntegrationService.cs # OpenAI-compatible HTTP client
│   └── AppSettingsManager.cs   # Read/write appsettings.json at runtime
├── Locales/            # i18n JSON files (zh-CN, en-US)
├── Program.cs          # App startup & endpoint mapping
├── Dockerfile          # Container build definition
└── appsettings.json    # Default configuration
```

---

## 📄 License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

---

## 🤝 Contributing

Contributions are welcome! Please open an issue or submit a pull request.
