# OpenCode .NET Core 迁移：非核心需求清单

本文档列出**非核心**需求：这些能力并非实现“可用的 AI coding agent”所必需，但对完整产品体验重要，建议在核心能力稳定后逐步补齐。

## 客户端与分发

### 1) 桌面客户端（Tauri）
现有 OpenCode 提供基于 Tauri 的桌面应用，用于桌面端体验与系统级封装。 .NET 迁移中可延后实现，或未来以 .NET MAUI/WPF/Uno 等方案替代。【F:packages/desktop/README.md†L1-L23】

### 2) Web Console
仓库中包含基于 SolidStart 的 Web 控制台，可用于远程管理或可视化展示。该能力可在 .NET 版本中后置实现（例如 React/Blazor Web）。【F:packages/console/app/README.md†L1-L24】

### 3) TUI 的完整 UI 复刻
虽然 TUI 作为最小客户端是核心，但“完整 UI 一致性与体验复刻”属于非核心需求，可在服务端稳定后逐步完成（UI/交互细节、动画、主题等）。【F:packages/opencode/AGENTS.md†L27-L27】

## 集成与生态

### 4) Slack Bot 集成
OpenCode 提供 Slack Bot，将线程映射为会话，属于生态扩展，建议在核心能力之后实现。【F:packages/slack/README.md†L1-L24】

### 5) 额外发布渠道与安装脚本
现有版本支持多平台安装脚本、包管理器分发。 .NET 迁移中可先用最小分发（单文件发布），后续再补齐各平台安装体验。【F:README.md†L19-L45】

### 6) Desktop 运行时依赖与打包
桌面版涉及 Tauri/Rust 工具链等构建要求，属于非核心；后期再规划适配 .NET Desktop 生态即可。【F:packages/desktop/README.md†L23-L33】

## 测试与质量保障（可后置）

### 7) E2E UI 测试体系
当前 UI 端有 Playwright E2E 测试流程， .NET 迁移后可在 UI 稳定后逐步补齐。【F:packages/app/README.md†L34-L55】

## 其他非核心模块（按需补齐）

- 企业版/私有化部署能力（packages/enterprise）
- 文档站点与内容（packages/docs、packages/web）
- 其他生态扩展（extensions、plugins 中的非关键功能）

> 以上非核心需求会在核心能力达成后，按业务优先级与资源逐步扩展实现。
