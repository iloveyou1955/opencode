# OpenCode.Net 项目说明与使用文档

> **注意**：本文件正在编写中。

## 目录

1. [项目简介](#项目简介)
2. [快速开始](#快速开始)
3. [核心架构](#核心架构)
4. [TUI 指令指南](#tui-指令指南)
5. [开发者指南](#开发者指南)
6. [常见问题与故障排查](#常见问题与故障排查)

---

## 项目简介

**OpenCode.Net** 是一个基于 **.NET 10.0** 构建的高性能、高度可扩展的开源 AI 辅助编程平台。它旨在为开发者提供一个极致的终端交互（TUI）环境，通过集成多种主流大语言模型（LLM）和强大的本地/远程工具链，实现深度自动化的代码开发、调试与项目管理。

### 核心价值

- **原生 .NET 10 驱动**：利用最新的 .NET 特性，提供卓越的运行效率和跨平台支持。
- **极致终端体验**：基于 `Spectre.Console` 构建，提供丰富的色彩、动态进度条、交互式选择器等现代终端 UI。
- **深度集成开发流**：不仅仅是聊天，它能理解您的 Git 仓库、LSP 符号、文件结构，并直接参与代码修改与 PR 管理。
- **灵活的工具扩展**：通过插件化架构，支持轻松扩展自定义工具（Tools）和智能体（Agents）。

### 主要特性

- 🤖 **多智能体系统**：内置多种专用智能体，支持根据任务自动切换或手动指定。
- 🛠️ **全能工具箱**：内置代码搜索、正则表达式替换、LSP 符号分析、Web 搜索、Shell 执行等数十种工具。
- 🐙 **GitHub 深度集成**：支持 PR 列表查看、详情获取、检出（Checkout）以及 PR 会话自动关联。
- ⏳ **会话版本控制**：支持会话 Fork、非破坏性回滚（Revert）、完整的时间线追踪。
- 📊 **透明度与成本**：实时显示模型思考过程（Thinking）、Token 消耗统计及预估成本。
- 🧩 **MCP 协议支持**：兼容 Model Context Protocol，支持快速接入第三方工具生态。

## 快速开始

### 1. 环境准备

- **.NET SDK**: 需要安装 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本。
- **Git**: 用于版本控制相关功能。
- **GitHub CLI (可选)**: 若需使用完整的 PR 管理功能，建议安装并登录 `gh`。

### 2. 获取代码并编译

```bash
# 克隆仓库
git clone https://github.com/your-repo/opencode.net.git
cd opencode.net/dotnet

# 恢复依赖并编译
dotnet build
```

### 3. 配置认证

首次运行前，您需要配置至少一个 AI 服务商的 API Key：

```bash
# 运行 TUI 并在提示下选择 auth 指令，或者直接运行：
dotnet run --project OpenCode.TUI -- auth login
```

支持的服务商：`openai`, `anthropic`, `google`, `openrouter`, `deepseek`, `github`。

### 4. 启动交互界面

```bash
dotnet run --project OpenCode.TUI
```

启动后，您可以输入 `help` 或 `?` 查看所有可用指令。

## 核心架构

OpenCode.Net 采用分层架构设计，各模块职责清晰，便于扩展和维护。

### 模块结构

- **OpenCode.Core**: 核心抽象层。定义了所有基础接口（如 `ITool`, `IAgent`）、数据模型（`SessionMetadata`, `MessageV2`）以及全局服务（认证、配置、总线）。
- **OpenCode.Infrastructure**: 基础设施层。实现了 Core 层定义的各种契约，包括 AI 客户端管理、LSP 客户端、本地文件系统操作、VCS（Git/GitHub）集成以及各种内置工具。
- **OpenCode.AgentFramework**: 智能体框架。负责编排 AI 的执行逻辑，支持多种 Executor（如 `ThinkingExecutor`, `ToolExecutor`）以及自定义 Workflow。
- **OpenCode.TUI**: 交互表现层。基于 `Spectre.Console` 实现，处理用户输入指令（`TuiCommandProcessor`）并呈现 AI 的输出。

### 核心设计原则

- **契约驱动 (Contract-First)**：所有功能扩展（如新工具或新 AI 模型）都必须遵循 Core 层定义的接口契约。
- **不可变性 (Immutability)**：核心状态（如 `SessionMetadata`）采用 C# Record 实现，确保会话回滚和并发处理的安全性。
- **依赖注入 (Dependency Injection)**：全面使用 .NET 依赖注入容器，通过 `ServiceRegistrationAttribute` 简化服务的发现与注册。

## TUI 指令指南

OpenCode.Net TUI 支持丰富的指令操作，指令通常以斜杠 `/` 开头（可选）。

### 基础指令

- `help` 或 `?`: 显示所有可用指令列表。
- `exit` 或 `quit`: 退出程序。
- `clear`: 清除屏幕。
- `settings`: 查看当前设置（模型、API 状态等）。

### 会话管理

- `new`: 创建新会话，支持通过交互式列表选择智能体。
- `ls`: 列出所有本地会话。
- `open`: 打开指定 ID 的会话。
- `fork`: 基于当前消息 Fork 出一个新的会话分支。
- `revert`: 回滚到历史某个时间点。
- `timeline`: 查看会话的历史消息树和时间线。
- `stats`: 显示当前会话的详细 Token 统计和成本预估。

### GitHub 集成

- `pr ls`: 列出当前仓库的 Pull Requests。
- `pr <id>`: 查看指定 PR 的详细信息、文件差异和对话历史。
- `pr checkout <id>`: 检出 PR 到本地分支并关联/创建对应会话。
- `gh import <url>`: 从 GitHub URL 导入 PR 或 Issue 背景。

### 系统与开发

- `auth login`: 交互式登录 AI 服务商。
- `auth logout`: 登出。
- `agent ls`: 查看所有可用智能体及其能力。
- `mcp ls`: 列出当前连接的 MCP 工具服务器。
- `import`: 从文件或粘贴板导入代码上下文。

> **小技巧**：在输入框直接输入文本将默认发送给当前激活的智能体。您可以在指令后直接跟参数，例如 `open session_123`。

## 开发者指南

### 如何添加新工具 (Tool)

1. 在 `OpenCode.Infrastructure` 的 `Tools` 目录下创建新类。
2. 继承 `ToolBase` 或实现 `ITool` 接口。
3. 使用 `[ServiceRegistration(typeof(ITool))]` 特性标记。
4. 实现 `ExecuteAsync` 方法编写工具逻辑。

### 如何自定义智能体 (Agent)

1. 在 `OpenCode.Infrastructure` 的 `Agents` 目录下定义配置。
2. 实现 `IAgentConfigurationProvider` 或通过 JSON 配置文件定义 Prompt 模板。
3. 注册新智能体元数据。

### 测试驱动开发 (TDD) 规范

项目严格遵循 TDD 流程。修改核心逻辑时：
- 先在 `OpenCode.IntegrationTests` 中编写失败的测试。
- 实现最简代码使测试通过。
- 进行代码重构。

使用以下命令运行测试：
```bash
dotnet test
```

## 常见问题与故障排查

**Q: 为什么输入指令后没有反应？**
A: 请检查是否已经通过 `auth login` 配置了有效的 API Key，并确保网络能够访问对应的模型服务商（可能需要代理）。

**Q: 如何修改 TUI 的显示样式？**
A: TUI 的样式主要在 `OpenCode.TUI` 项目的 `Spectre.Console` 配置中定义。目前支持通过系统环境变量进行部分自定义。

**Q: 运行 `dotnet build` 报错找不到依赖？**
A: 请确保您在 `dotnet` 目录下运行命令。如果仍有问题，尝试运行 `dotnet nuget locals all --clear` 后重新 build。

---

> 🚀 **OpenCode.Net** 持续进化中，欢迎提交 Issue 或 Pull Request！
