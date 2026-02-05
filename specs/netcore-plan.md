# OpenCode .NET Core 迁移推进计划（先核心后扩展）

## 总体策略

采用“**核心能力优先**”的增量式推进：先完成可用的服务端与最小客户端（CLI/TUI），再逐步扩展至桌面/Web/生态集成。整体计划分为 4 个阶段。

---

## 阶段 0：基线与技术选型（1-2 周）

**目标**：搭建 .NET 技术基线、架构骨架与开发规范。

- 选型：ASP.NET Core + Minimal API/Controller + EF Core/SQLite。
- CLI：System.CommandLine 或 Spectre.Console.Cli（对齐现有 CLI 命令结构）。【F:packages/opencode/src/index.ts†L1-L123】
- 定义核心数据模型：Project / Session / Message / Tool / Provider。
- 搭建配置系统（环境变量、配置文件、多环境）。

**输出**：
- 空服务端可启动（health check）。
- CLI 可输出命令帮助并完成命令解析。

---

## 阶段 1：核心服务端 + API（4-6 周）

**目标**：实现核心 REST API 与会话驱动能力。

- 实现 Project/Session/Message API 端点（与现有 API 语义一致）。【F:specs/project.md†L5-L41】
- 实现 Session 状态机与消息存储（SQLite + 简单索引）。
- 提供最小 Tool 执行框架（文件读写、patch）。
- 完成配置读取、日志、权限控制的最小实现。

**输出**：
- CLI 可创建会话并发送消息，服务端可返回模型响应（先用 mock provider 或单一模型）。

---

## 阶段 2：协议/生态核心（4-6 周）

**目标**：完成外部协议与生态能力。

- MCP 客户端集成（连接 MCP server、tool 列表、tool 调用）。【F:packages/opencode/src/mcp/index.ts†L1-L200】
- ACP server（外部客户端可初始化、创建/加载会话、prompt）。【F:packages/opencode/src/acp/README.md†L1-L83】
- LSP 管理与桥接（启动 LSP 服务，提供符号/诊断能力）。【F:packages/opencode/src/lsp/index.ts†L1-L174】
- Provider 统一抽象与多模型支持（最少 2-3 家）。【F:packages/opencode/src/provider/provider.ts†L1-L90】
- 插件系统与 Hook 生命周期（认证/事件/配置）。【F:packages/opencode/src/plugin/index.ts†L1-L120】

**输出**：
- 与外部 MCP/ACP 客户端互通。
- 多模型可切换，工具生态可扩展。

---

## 阶段 3：客户端形态与非核心能力扩展（持续迭代）

**目标**：补齐非核心能力，使产品完整。

- TUI 体验细化与兼容（可保持与 JS TUI 同等能力）。【F:packages/opencode/AGENTS.md†L27-L27】
- Desktop 客户端（.NET MAUI/WPF/Uno 替代 Tauri）。【F:packages/desktop/README.md†L1-L23】
- Web Console/管理后台（Blazor/React）。【F:packages/console/app/README.md†L1-L24】
- Slack Bot、企业版、文档站点等扩展能力。【F:packages/slack/README.md†L1-L24】

**输出**：
- 产品形态补齐，覆盖 CLI/TUI/Desktop/Web。

---

## 风险与依赖

- **协议兼容性**：ACP/MCP 需严格遵循协议版本，建议引入对应 SDK 或规范测试套件。
- **Provider 复杂度**：多模型鉴权、配额与兼容性需要统一抽象。
- **LSP 稳定性**：跨平台进程与诊断流稳定性需要专门测试。

---

## 推进原则

1. 先跑通“核心功能链路”（CLI → Session → Provider → Response）。
2. 用 MVP 代替完整 UI，优先交付服务端能力。
3. 协议与生态能力在核心稳定后再逐步接入。
