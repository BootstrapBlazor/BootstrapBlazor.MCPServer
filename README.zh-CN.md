# BootstrapBlazor MCP Server

[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![BootstrapBlazor](https://img.shields.io/badge/BootstrapBlazor-10.x-blue)](https://www.blazor.zone/)
[![MCP](https://img.shields.io/badge/MCP-模型上下文协议-green)](https://modelcontextprotocol.io/)
[![Docker](https://img.shields.io/badge/Docker-就绪-blue?logo=docker)](https://www.docker.com/)
[![License](https://img.shields.io/badge/许可证-MIT-yellow)](LICENSE)

中文版 | [English](README.md)

一个基于 ASP.NET Core 与 BootstrapBlazor 构建的 **模型上下文协议（MCP）服务器**，为 AI 助手（如 Claude、Cursor、Dify、FastGPT 等）提供对 **BootstrapBlazor 组件库完整文档** 的智能访问能力，包括 API 参数表和代码示例。

---

## ✨ 功能特性

- 🔄 **自动 Git 同步** — 按配置的 Cron 表达式定时克隆/拉取官方 BootstrapBlazor 仓库。
- 📄 **文档提取** — 通过反射 + XML 文档注释自动提取组件 API 参数表，并将 Razor/C# 代码示例转换为 Markdown 文件，供 RAG 使用。
- 🤖 **MCP 工具** — 暴露标准 MCP 工具（`GetComponentList`、`SearchComponentKeyword`、`GetComponentDocs`、`AskComponentExpert`），兼容任何支持 MCP 的 AI 客户端。
- 🌐 **REST API** — 提供 HTTP 接口，适用于 Dify、FastGPT 等非 MCP 集成场景。
- 🧠 **可选 AI 问答** — 集成任意 OpenAI 兼容 API，以自然语言回答 BootstrapBlazor 组件相关问题。
- 🖥️ **管理后台** — 内置受登录保护的 Blazor 管理界面，可配置参数、手动触发同步、查看运行状态。
- 🌍 **多语言支持** — 管理 UI 支持 `zh-CN` 与 `en-US` 两种语言。
- 🐳 **Docker 就绪** — 附带 `Dockerfile`，支持一键容器化部署。

---

## 🏗️ 架构图

```
┌────────────────────────────────────────────────────────┐
│                  BootstrapBlazor.McpServer              │
│                                                        │
│  ┌──────────────┐   ┌──────────────────────────────┐  │
│  │  Git 同步任务│──▶│   DocsExtractorService        │  │
│  │ (Coravel     │   │ （反射 + XML + Razor 解析）    │  │
│  │  定时任务)   │   └────────────┬─────────────────┘  │
│  └──────────────┘                │ Markdown 文件       │
│                                  ▼                     │
│  ┌──────────────────────────────────────────────────┐  │
│  │             McpService（MCP 工具集）               │  │
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

## 🚀 快速开始

### 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git（已安装并配置到 PATH）

### 本地运行

```bash
git clone https://github.com/your-org/BootstrapBlazor.McpServer.git
cd BootstrapBlazor.McpServer

# 根据实际情况修改 appsettings.json（参见配置说明）
dotnet run
```

服务默认启动在 `http://localhost:5251`。

### Docker 运行

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

## ⚙️ 配置说明

编辑 `appsettings.json` 或通过环境变量配置（推荐 Docker 场景）：

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

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `GitSync:RepositoryUrl` | BootstrapBlazor 源码仓库地址 | Gitee 镜像地址 |
| `GitSync:CronSchedule` | 自动同步的 Cron 表达式 | `0 3 * * *`（每天凌晨 3 点） |
| `GitSync:LocalPath` | 本地克隆仓库的路径 | `/app/data/BootstrapBlazorRepo` |
| `GitSync:OutputDir` | 生成的 Markdown 文件输出目录 | `/app/data/OutputRAG` |
| `AI:BaseUrl` | OpenAI 兼容 API 的 Base URL | `https://api.openai.com/v1` |
| `AI:ApiKey` | AI 服务的 API Key | — |
| `AI:Model` | 使用的模型名称 | `gpt-4o` |

---

## 🔌 MCP 接入方式

### 接入 Claude Desktop

在 Claude Desktop 配置文件 `claude_desktop_config.json` 中添加：

```json
{
  "mcpServers": {
    "bootstrapblazor": {
      "url": "http://localhost:5251/mcp"
    }
  }
}
```

### 接入 Cursor

在 Cursor 设置中，添加一个 MCP Server，地址指向 `http://localhost:5251/mcp`。

### 可用 MCP 工具

| 工具名 | 描述 |
|--------|------|
| `GetComponentList` | 获取所有可用 BootstrapBlazor 组件列表 |
| `SearchComponentKeyword` | 按关键词搜索匹配的组件 |
| `GetComponentDocs` | 获取指定组件的 API 参数表和代码示例原文 |
| `AskComponentExpert` | 以自然语言向组件专家提问（若启用 AI 则调用 AI 回答） |

---

## 🌐 REST API

适用于 Dify、FastGPT 等非 MCP 集成场景：

| 方法 | 接口地址 | 说明 |
|------|----------|------|
| `GET` | `/api/components` | 获取所有组件名称列表 |
| `GET` | `/api/components/search?keyword={关键词}` | 按关键词搜索组件 |
| `GET` | `/api/components/{组件名}/docs` | 获取指定组件的原始文档 |
| `GET` | `/api/components/{组件名}/ask?q={问题}` | 对指定组件进行 AI 问答 |

---

## 🖥️ 管理后台

访问 `http://localhost:5251` 进入 Blazor 管理界面，使用 `appsettings.json` 中配置的账号密码登录。

功能包括：
- 查看与修改服务器配置
- 手动触发 Git 同步
- 监控同步状态和日志
- 开启/关闭 AI 集成

---

## 🛠️ 技术栈

| 层级 | 技术 |
|------|------|
| 框架 | ASP.NET Core (.NET 10) |
| UI | BootstrapBlazor（Blazor Server） |
| MCP | ModelContextProtocol SDK 0.9 |
| AI 集成 | Microsoft.Extensions.AI + OpenAI |
| Git 同步 | LibGit2Sharp |
| 定时任务 | Coravel |
| 国际化 | ASP.NET Core Localization |

---

## 📁 项目结构

```
BootstrapBlazor.McpServer/
├── Components/         # Blazor UI 组件（管理页面、App Shell）
├── Services/
│   ├── McpService.cs           # MCP 工具定义
│   ├── DocsExtractorService.cs # 文档提取逻辑（反射 + XML + Razor 解析）
│   ├── GitSyncInvocable.cs     # Git 克隆/拉取 + 触发文档提取
│   ├── AiIntegrationService.cs # OpenAI 兼容 HTTP 客户端
│   └── AppSettingsManager.cs   # 运行时读写 appsettings.json
├── Locales/            # 国际化 JSON 文件（zh-CN、en-US）
├── Program.cs          # 应用启动与端点注册
├── Dockerfile          # 容器构建定义
└── appsettings.json    # 默认配置文件
```

---

## 📄 许可证

本项目采用 MIT 许可证。详情请见 [LICENSE](LICENSE)。

---

## 🤝 贡献指南

欢迎提交 Issue 或 Pull Request 参与贡献！
