# OpenCode 功能点结构化清单（用于 .NET 对照实现）

> 目的：按模块/结构列出当前 OpenCode 的**详细功能点**，便于在 .NET 版本中逐项对照实现与验收。

## 1) CLI 与入口层

- **统一 CLI 入口**：通过 yargs 注册所有命令、设置日志、全局异常处理与默认行为（help/version/strict）。【F:packages/opencode/src/index.ts†L1-L123】
- **run 命令**：通过 SDK 调用服务端，渲染工具执行输出与 UI 文案，覆盖常用工具的展示逻辑（grep/read/edit/bash 等）。【F:packages/opencode/src/cli/cmd/run.ts†L1-L200】
- **export 命令**：导出会话与消息/parts 为 JSON，支持交互式选择会话。【F:packages/opencode/src/cli/cmd/export.ts†L1-L100】

> 其他 CLI 命令（serve/web/mcp/acp/session/agent/auth/models/import/pr 等）与入口结构保持一致，可在 .NET 中按命令分组对齐实现。【F:packages/opencode/src/index.ts†L1-L123】

## 2) Server 核心与 API 路由

- **服务端入口**：基于 Hono 初始化应用、统一错误处理、CORS 与基础鉴权、实例上下文注入，并挂载各类路由模块。【F:packages/opencode/src/server/server.ts†L1-L200】
- **Project API**：项目列表、当前项目读取、项目更新（名称/图标/命令）。【F:packages/opencode/src/server/routes/project.ts†L1-L80】
- **Session API**：会话列表、状态、详情、子会话、待办事项、创建等核心会话端点（其余端点在同文件继续定义）。【F:packages/opencode/src/server/routes/session.ts†L1-L200】
- **File API**：文件查找/全文搜索、文件列表、文件读取、Git 状态查询等。【F:packages/opencode/src/server/routes/file.ts†L1-L200】
- **Provider API**：provider 列表、默认模型、鉴权方法、OAuth 流程处理。【F:packages/opencode/src/server/routes/provider.ts†L1-L200】
- **MCP API**：MCP 连接状态、动态添加、OAuth 流程、连接管理。【F:packages/opencode/src/server/routes/mcp.ts†L1-L200】
- **Permission API**：权限请求列表、权限答复处理。【F:packages/opencode/src/server/routes/permission.ts†L1-L120】
- **Question API**：提问请求列表、答复/拒绝处理。【F:packages/opencode/src/server/routes/question.ts†L1-L120】
- **PTY API**：创建/更新/移除 PTY 会话，并通过 WebSocket 连接终端会话。【F:packages/opencode/src/server/routes/pty.ts†L1-L200】
- **TUI API**：TUI 请求队列/响应队列与控制类 API（append prompt/open dialog 等）。【F:packages/opencode/src/server/routes/tui.ts†L1-L200】

## 3) Session 体系（消息/处理/工具链）

- **Session 模型**：会话结构、事件（创建/更新/删除/错误/变更）、子会话、标题派生与共享信息等定义。【F:packages/opencode/src/session/index.ts†L1-L120】
- **Session 处理器**：负责 LLM 流式响应解析、工具调用生命周期、错误与重试、权限拦截、会话状态更新等核心循环。【F:packages/opencode/src/session/processor.ts†L1-L200】

## 4) Tool 系统与执行协议

- **Tool 规范**：统一 tool 结构（init/execute/schema/metadata），并在 execute 时进行 schema 校验与输出截断处理。【F:packages/opencode/src/tool/tool.ts†L1-L120】
- **Tool 注册表**：内置工具 + 插件工具 + .opencode 目录自定义工具，支持基于模型与实验开关动态启用。【F:packages/opencode/src/tool/registry.ts†L1-L200】

## 5) 配置与存储

- **配置系统**：多来源合并（远程 well-known、全局/项目/目录级 config），支持 JSONC 与内联配置，并在加载过程中触发插件与命令配置。【F:packages/opencode/src/config/config.ts†L1-L200】
- **存储层**：文件系统 JSON 存储、锁、迁移与读写 API（read/write/update/remove）。【F:packages/opencode/src/storage/storage.ts†L1-L200】

## 6) 认证与权限

- **Auth 结构**：支持 API Key、OAuth、WellKnown 等多种认证信息存储与读取。【F:packages/opencode/src/auth/index.ts†L1-L120】
- **权限系统**：权限规则解析、请求/答复、规则合并、事件发布与拒绝策略。【F:packages/opencode/src/permission/next.ts†L1-L160】

## 7) Provider 与模型生态

- **Provider 注册与装配**：多模型供应商集成、兼容层、特定 provider 的加载策略与响应 API 选择逻辑。【F:packages/opencode/src/provider/provider.ts†L1-L90】

## 8) Project / Worktree / VCS

- **Project 发现与建模**：从目录解析 git/worktree，生成 project ID，记录 worktree/沙箱/命令等信息。【F:packages/opencode/src/project/project.ts†L1-L200】
- **Worktree 管理**：worktree 创建/移除/重置输入与事件模型，含命名策略与错误模型。【F:packages/opencode/src/worktree/index.ts†L1-L200】
- **VCS 监听**：git 分支变化检测，发布 branch 更新事件。【F:packages/opencode/src/project/vcs.ts†L1-L120】

## 9) LSP / MCP / ACP 协议能力

- **LSP 管理**：LSP server 列表与动态配置、进程启动与客户端管理、状态发布等。【F:packages/opencode/src/lsp/index.ts†L1-L174】
- **MCP 客户端**：MCP 连接、OAuth、tool 列表与调用、状态管理与事件通知。【F:packages/opencode/src/mcp/index.ts†L1-L200】
- **ACP 服务端**：ACP 服务器生命周期、会话映射、prompt 处理与协议遵循说明。【F:packages/opencode/src/acp/README.md†L1-L83】

## 10) 插件系统

- **插件加载**：内置插件 + npm 安装插件 + file:// 插件加载，支持 hook 生命周期与事件派发。【F:packages/opencode/src/plugin/index.ts†L1-L120】

---

## 对照实施建议（.NET）

1. 先对齐 **Server API + Session pipeline + Tool Registry** 三大核心链路。
2. 将 **Provider + MCP/LSP/ACP** 作为第二阶段核心能力接入。
3. 最后补齐 **CLI 细节 + TUI/PTY/Question/Permission 交互**，确保体验一致。
