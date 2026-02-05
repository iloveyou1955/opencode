# OpenCode .NET Core 迁移：核心能力转换文档

## 目标与范围

此文档提取 OpenCode 的**核心能力**，用于在 .NET（ASP.NET Core + C#）中建立**最小可用且可扩展**的同等系统。核心范围聚焦：能运行 AI coding agent、支持项目/会话管理、工具调用、模型提供商、核心协议与基础集成。产品外围形态（桌面、Web、企业版等）不包含在此文档中。

## 核心能力清单（需在 .NET 版本中优先实现）

### 1) 统一 CLI 入口与命令编排
OpenCode 的主入口是 CLI，承担启动服务、运行会话、执行工具、导入导出等能力的统一入口。核心命令包含：run、serve、web、mcp、acp、session、agent、auth、models、export/import、github、pr 等。 .NET 版本需要一个等价的命令入口（建议 System.CommandLine 或 Spectre.Console.Cli）并能加载所有子命令，以保持一致的用户体验与脚本兼容性。【F:packages/opencode/src/index.ts†L1-L123】

### 2) 客户端/服务端架构（Server-First）
OpenCode 的设计是客户端/服务端架构：服务端负责会话、工具调用、模型交互；客户端可以是 TUI 或其他 UI。 .NET 迁移应保持该架构，以便在未来接入多种客户端（TUI、Web、Desktop）。【F:README.md†L130-L131】

### 3) 项目/会话/消息 API（核心 REST API）
OpenCode 的核心 API 以“项目-会话-消息”为轴心，涵盖：
- 项目列表、初始化
- 会话列表/创建/加载/终止
- 消息读取/新增/回滚
- 会话分享、权限请求
- 文件查询与文件内容读取

这是服务端最重要的 API 面；.NET 版本应实现同构 API 或提供兼容层，确保 SDK/客户端可复用或平滑迁移。【F:specs/project.md†L5-L41】

### 4) MCP（Model Context Protocol）集成
OpenCode 内置 MCP 客户端能力，支持连接 MCP server、发现工具、调用 MCP tool、处理鉴权与回调。 .NET 版本需要保留 MCP 客户端能力，以保持工具生态与外部扩展能力。【F:packages/opencode/src/mcp/index.ts†L1-L200】

### 5) ACP（Agent Client Protocol）服务端
OpenCode 支持 ACP，用于对接外部 ACP 客户端。其实现包含 ACP server 启动、会话映射、能力协商、prompt 处理等。 .NET 版本必须提供等价 ACP server，使其能被 Zed/其他 ACP 客户端驱动。【F:packages/opencode/src/acp/README.md†L1-L83】

### 6) LSP 集成（代码语义能力）
OpenCode 内置 LSP 管理与客户端调度，并允许配置多种语言服务。LSP 是代码智能（symbols、document symbols、diagnostics 等）的关键来源，属核心体验。 .NET 版本应提供与 LSP 服务器的桥接能力与生命周期管理。【F:packages/opencode/src/lsp/index.ts†L1-L174】

### 7) 多模型 Provider 管理
OpenCode 在服务端内聚合多家模型提供商（OpenAI、Anthropic、Google、Azure、Mistral 等），并提供统一 Provider 接入、鉴权与模型选择能力。 .NET 版本需要实现 Provider registry 与统一模型调用抽象（建议接口 + provider plugin），以兼容现有模型生态。【F:packages/opencode/src/provider/provider.ts†L1-L90】

### 8) 插件系统（扩展与鉴权插件）
服务端包含插件系统，允许加载内部与外部插件，并触发 hook（例如配置、事件、认证）。这对于认证、扩展工具链非常关键，.NET 版本需要实现插件加载机制与事件/Hook 生命周期。【F:packages/opencode/src/plugin/index.ts†L1-L120】

### 9) 终端 TUI 客户端（最小客户端形态）
OpenCode 的默认交互形态是 TUI，并通过 SDK 调用服务端 API。即便 .NET 版本初期不复用 TUI，也需要定义等价的客户端协议/SDK 以维持未来 TUI 的可移植性。【F:packages/opencode/AGENTS.md†L27-L27】

## 详细功能点对照

完整的结构化功能点清单请参见 `specs/netcore-feature-map.md`，其中按模块列出 Server/API、Session、Tool、Provider、协议与插件等细节，便于逐项对照落地。

## .NET 实现现状

当前仓库尚未发现 .NET/C# 实现（未见 dotnet 目录或 csproj/sln 文件）。具体状态记录见 `specs/netcore-dotnet-status.md`。

## .NET 目标架构建议（核心映射）

- **Web API 层**：ASP.NET Core Minimal APIs / Controller；实现 project/session/message API 与 MCP/ACP endpoints。
- **Domain 层**：Session、Project、Message、Tool、Provider 等核心模型与状态机。
- **Tool 执行层**：封装本地文件系统、Shell、Patch、LSP、MCP 调用。
- **Provider 层**：统一接口 + provider adapters，支持多模型调用与配置管理。
- **Plugin 管理**：基于 MEF 或自定义插件加载器（AssemblyLoadContext）。
- **CLI 层**：System.CommandLine 或 Spectre.Console.Cli，提供和现有 CLI 对应的命令集合。
- **SDK/客户端协议**：为 TUI/Web 预留 API Client SDK（可用 NSwag/OpenAPI 生成）。

## 核心迁移的验收标准（Definition of Done）

1. 可以启动服务端并成功创建项目与会话。
2. CLI 可启动 session 并发送消息，服务端能返回模型响应。
3. MCP 客户端能成功连接并调用外部 MCP tool。
4. ACP server 可被外部客户端初始化并进行 prompt。
5. LSP 能启动、获取符号信息。
6. Provider 模型可以切换，并至少支持一到两家主流模型（如 OpenAI/Anthropic）。
7. 插件生命周期可用（加载、hook 调用、事件分发）。
