# OpenCode TypeScript vs C# 功能差异分析

## 概述

本文档对比了 TypeScript 版本和 C# (.NET 10.0) 版本的功能差异，为后续功能开发提供指导。

---

## 功能对比矩阵

| 功能模块 | TS 版本 | C# 版本 | 状态 | 优先级 |
|---------|---------|---------|------|--------|
| 核心 Agent 系统 | ✅ 完整 | ✅ 完整 | - | - |
| Session 管理 | ✅ 完整 | ✅ 完整 | - | - |
| 工具系统 | ✅ 20+ 工具 | ✅ 20+ 工具 | - | - |
| LSP 集成 | ✅ 完整 | ✅ 完整 | - | - |
| MCP 支持 | ✅ 完整 | ✅ 完整 | - | - |
| 配置系统 | ✅ 完整 | ✅ 完整 | - | - |
| 权限系统 | ✅ 完整 | ✅ 完整 | - | - |
| 统计服务 | ✅ 完整 | ⚠️ 部分 | ⚠️ 需完善 | 中 |
| HTTP Server | ✅ 完整 | ✅ 完整 | - | - |
| TUI | ✅ 完整 | ✅ 完整 | - | - |

---

## 待实现功能列表

### 1. 会话分享功能 ⚠️ 高优先级

**TS 实现**: `packages/opencode/src/share/share.ts`

**功能描述**:
- 支持会话分享到云端（opencode.ai）
- 自动同步会话更新到分享链接
- 生成分享 URL 和 secret
- 支持从分享链接导入会话

**实现细节**:
- 与 `https://api.opencode.ai` 交互
- 事件驱动的实时同步
- 支持禁用分享（`OPENCODE_DISABLE_SHARE` 环境变量）

**C# 优化建议**:
- 使用 C# 的 `HttpClient` 替代 fetch
- 使用 `IHttpClientFactory` 进行依赖注入
- 考虑使用 Polly 进行重试策略
- 支持离线模式和本地分享

---

### 2. 会话 Fork 功能 ⚠️ 高优先级

**TS 实现**: `packages/opencode/src/session/index.ts` (getForkedTitle 函数)

**功能描述**:
- 创建会话的副本（子会话）
- 保持父会话引用
- 自动生成 fork 标题（如 "My Session (fork #1)"）

**实现细节**:
- 深度复制会话历史
- 维护父子会话关系图
- 支持从任意消息点开始 fork

**C# 优化建议**:
- 使用记录类型和不可变集合
- 实现高效的会话快照机制
- 支持增量 fork（只复制差异部分）

---

### 3. 会话归档功能 🟡 中优先级

**TS 实现**: `packages/opencode/src/util/archive.ts`

**功能描述**:
- 将不活跃的会话归档
- 减少活跃会话列表大小
- 支持归档恢复

**实现细节**:
- 自动归档策略（如 30 天未访问）
- 归档后仍可查询历史
- 归档数据压缩存储

**C# 优化建议**:
- 使用 `System.IO.Compression` 进行压缩
- 实现归档后台任务（使用 `BackgroundService`）
- 支持自定义归档策略

---

### 4. 会话回滚功能 🟡 中优先级

**TS 实现**: `packages/opencode/src/session/revert.ts`

**功能描述**:
- 回滚到指定消息/部分
- 保留回滚前的快照
- 计算回滚后的 diff
- 支持取消回滚（unrevert）

**实现细节**:
- 与 Snapshot 系统集成
- 计算文件差异（additions/deletions）
- 更新会话摘要统计

**C# 优化建议**:
- 使用 `System.IO.Abstractions` 进行文件操作抽象
- 实现高效的 diff 算法
- 支持选择性回滚（部分工具调用）

---

### 5. CodeSearch 工具增强 🟢 已实现，需优化

**TS 实现**: `packages/opencode/src/tool/codesearch.ts`

**当前 C# 状态**: ✅ 已实现 `OpenCode.Infrastructure/Tools/CodeSearchTool.cs`

**功能差异**:
- TS 版本使用 Exa API（`https://mcp.exa.ai`）进行语义代码搜索
- C# 版本使用正则表达式模式匹配

**优化建议**:
- 集成 Exa API 或类似的语义搜索服务
- 支持混合搜索（语义 + 模式）
- 添加搜索结果缓存优化

---

### 6. GitHub PR 集成 🟡 中优先级

**TS 实现**: `packages/opencode/src/cli/cmd/pr.ts`

**功能描述**:
- 通过 PR 号检出分支
- 自动处理 fork PR 的远程配置
- 检测 PR 中的会话分享链接并自动导入
- 使用 GitHub CLI（gh）

**实现细节**:
- 支持跨仓库 fork
- 自动设置 upstream
- 与会话导入集成

**C# 优化建议**:
- 使用 Octokit.NET (GitHub API 客户端)
- 支持多平台（GitLab, Azure DevOps）
- 实现命令行工具包装（Process.Start 或 Octokit）

---

### 7. Git Worktree 管理 🟢 低优先级

**TS 实现**: `packages/opencode/src/worktree/index.ts`

**功能描述**:
- 创建/删除/重置 git worktree
- 为每个 worktree 独立启动项目
- 支持自定义启动命令

**实现细节**:
- 与 Git 集成
- 支持多 worktree 并行开发
- 独立的会话和状态管理

**C# 优化建议**:
- 使用 LibGit2Sharp 进行 Git 操作
- 实现工作树生命周期管理
- 支持工作树之间的状态共享

---

### 8. 升级命令 🟡 中优先级

**TS 实现**: `packages/opencode/src/cli/upgrade.ts`

**功能描述**:
- 自动检测并升级到最新版本
- 支持指定版本升级
- 多安装方法支持（curl, npm, pnpm, bun, brew, choco, scoop）

**实现细节**:
- 版本检测和比较
- 多包管理器支持
- 升级失败处理

**C# 优化建议**:
- 使用 .NET 的自更新机制
- 集成到 Windows Update（Windows 平台）
- 支持通过 winget/choco/scoop 安装

---

### 9. Agent 管理命令 🟢 低优先级

**TS 实现**: `packages/opencode/src/cli/cmd/agent.ts`

**功能描述**:
- `agent create` - 创建新 Agent
- `agent list` - 列出可用 Agent
- `agent delete` - 删除 Agent
- 交互式 Agent 配置

**实现细节**:
- YAML 配置生成
- 工具选择和权限配置
- 模式选择（primary/subagent）

**C# 优化建议**:
- 使用 Spectre.Console 提供更好的交互体验
- 集成到现有的 Agent 配置系统
- 支持从模板创建 Agent

---

### 10. 模型命令 🟢 低优先级

**TS 实现**: `packages/opencode/src/cli/cmd/models.ts`

**功能描述**:
- 列出所有可用模型
- 按提供商分组显示
- 显示模型上下文窗口大小

**C# 优化建议**:
- 使用 Spectre.Console 的 Table 组件美化输出
- 支持模型搜索和过滤
- 显示模型定价信息

---

### 11. mDNS 服务发现 🟢 低优先级

**TS 实现**: `packages/opencode/src/server/mdns.ts`

**功能描述**:
- 通过 Bonjour/mDNS 广播 OpenCode 服务器
- 允许同一网络中的设备自动发现
- 支持多个 OpenCode 实例

**C# 优化建议**:
- 使用 Zeroconf.NET 或类似的 mDNS 库
- 支持 Windows/Linux/macOS 跨平台
- 与桌面应用集成

---

### 12. 国际化支持 🟢 低优先级

**TS 实现**: 分布在多个文件中，支持 15+ 种语言

**功能描述**:
- 多语言 UI 支持
- 用户可配置语言偏好
- 自动检测系统语言

**C# 优化建议**:
- 使用 .NET 的本地化/全球化 API（`ResourceManager`）
- 支持 .resx 文件格式
- 运行时语言切换

---

### 13. 主题系统 🟢 低优先级

**TS 实现**: `packages/opencode/src/server/routes/tui.ts` (theme 配置)

**功能描述**:
- 可自定义 TUI 主题
- 支持预定义主题
- 用户可自定义颜色方案

**C# 优化建议**:
- 使用 Spectre.Console 的主题 API
- 支持 JSON/YAML 主题配置
- 主题热重载

---

### 14. 桌面应用 ❌ 暂不实现

**TS 实现**: `packages/desktop/` (Tauri + Rust)

**说明**: 桌面应用是独立的项目，C# 版本可通过 WPF/WinUI 3 实现，但优先级较低

---

### 15. Web 应用 ❌ 暂不实现

**TS 实现**: `packages/app/` (SolidJS)

**说明**: C# 版本的 Server 已提供 REST API，可通过前端框架单独实现

---

### 16. 会话导出/导入命令 ⚠️ 高优先级

**TS 实现**: 
- `packages/opencode/src/cli/cmd/export.ts`
- `packages/opencode/src/cli/cmd/import.ts`

**功能描述**:
- 导出会话为 JSON 文件
- 从 JSON 文件或分享 URL 导入会话
- 支持会话分享链接导入

**实现细节**:
- 包含会话元数据和消息历史
- 支持从 opencode.ai 导入
- 交互式会话选择

**C# 优化建议**:
- 使用 `System.Text.Json` 进行序列化
- 支持多种导出格式（JSON, XML, YAML）
- 实现导入验证和错误处理

---

### 17. 会话状态功能 🟡 中优先级

**TS 实现**: `packages/opencode/src/session/status.ts`

**功能描述**:
- 获取会话当前状态
- 显示会话统计信息
- 显示最近活动

**C# 优化建议**:
- 集成到现有的 SessionService
- 使用 Spectre.Console 的状态面板
- 支持实时状态更新

---

### 18. 统计功能完善 ⚠️ 中优先级

**TS 实现**: `packages/opencode/src/cli/cmd/stats.ts`

**当前 C# 状态**: ✅ 已实现 `OpenCode.Core/Services/StatsService.cs`

**功能差异**:
- TS 版本支持更详细的统计（中位数、每日成本等）
- 支持按项目过滤
- 支持工具和模型使用排名

**优化建议**:
- 添加中位数计算
- 添加每日成本趋势图表
- 实现统计导出功能

---

## 实现优先级总结

### 🔴 高优先级
1. 会话分享功能
2. 会话 Fork 功能
3. 会话导出/导入命令

### 🟡 中优先级
4. 会话归档功能
5. 会话回滚功能
6. GitHub PR 集成
7. 升级命令
8. 统计功能完善
9. 会话状态功能

### 🟢 低优先级
10. CodeSearch 工具增强（语义搜索）
11. Git Worktree 管理
12. Agent 管理命令
13. 模型命令
14. mDNS 服务发现
15. 国际化支持
16. 主题系统

---

## C# 特性优化建议

### 1. 使用 C# 特有特性

- **记录类型**: 用于不可变的数据模型
- **模式匹配**: 替代复杂的 if-else 链
- **异步流**: 用于大数据集的流式处理
- **Span/Memory**: 用于高性能字符串处理
- **值任务**: 减少异步操作的内存分配

### 2. 依赖注入优化

- 使用 `IHttpClientFactory` 替代直接创建 HttpClient
- 使用 `ILogger<T>` 进行结构化日志
- 使用 `IOptions<T>` 模式管理配置

### 3. 错误处理

- 使用自定义异常类型
- 实现全局异常处理中间件
- 使用 Polly 进行重试和断路器模式

### 4. 性能优化

- 使用 `ArrayPool<T>` 减少数组分配
- 使用 `StringBuilder` 进行字符串拼接
- 实现缓存策略（MemoryCache, IDistributedCache）
- 使用 `Parallel.ForEach` 进行并行处理

### 5. 安全性

- 使用 `ProtectedData` 进行敏感数据加密
- 实现令牌验证和授权
- 使用 HTTPS 和证书验证

---

## 兼容性考虑

### 数据格式兼容性

- **Session 消息格式**: 保持与 TS 版本一致的 JSON Schema
- **配置文件格式**: 保持 YAML/JSON 兼容性
- **Agent 配置**: 保持相同的字段和结构

### API 兼容性

- **Server API**: 保持 REST API 端点一致
- **事件格式**: 保持 SSE 事件格式一致
- **工具接口**: 保持 ITool 接口签名一致

---

## 后续工作计划

1. 按优先级实现上述功能
2. 每个功能实现后添加单元测试
3. 更新文档和用户指南
4. 进行性能基准测试
5. 收集用户反馈进行迭代

---

*文档生成时间: 2026-02-04*
*基于 OpenCode TypeScript 版本和 C# .NET 10.0 版本对比*
