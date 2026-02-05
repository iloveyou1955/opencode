# OpenCode C# 版本功能实现任务列表

> 基于 `FEATURES_GAP_ANALYSIS.md` 的详细任务分解

---

## 🔴 高优先级任务

### Task 1: 会话分享功能

**描述**: 实现会话分享到云端并支持实时同步

**技术要点**:
- 使用 `HttpClient` 与 `https://api.opencode.ai` 交互
- 实现事件驱动的同步机制
- 支持生成分享 URL 和 secret
- 支持从分享 URL 导入会话

**实现步骤**:
1. 创建 `OpenCode.Core/Services/ShareService.cs`
   - `CreateShareAsync(sessionId)` - 创建分享
   - `GetShareAsync(sessionId)` - 获取分享信息
   - `SyncAsync(key, content)` - 同步内容
2. 创建 `OpenCode.Infrastructure/Share/ShareClient.cs`
   - HTTP 客户端封装
   - API 端点调用
   - 错误处理和重试
3. 在 `BusService` 中订阅会话事件
   - `Session.Updated` - 同步会话信息
   - `MessageV2.Updated` - 同步消息
   - `MessageV2.PartUpdated` - 同步消息部分
4. 添加 Server API 端点
   - `POST /share/create` - 创建分享
   - `POST /share/sync` - 同步分享
   - `GET /share/{slug}` - 获取分享
5. 添加 CLI 命令
   - `share create <sessionId>` - 创建分享
   - `share list` - 列出分享
   - `share delete <sessionId>` - 删除分享

**C# 优化**:
- 使用 `IHttpClientFactory` 进行依赖注入
- 使用 Polly 实现重试策略
- 使用 `Channel` 实现异步消息队列
- 支持离线模式（本地缓存未同步的更改）

**验收标准**:
- [x] 可以创建会话分享
- [x] 可以生成分享 URL
- [x] 可以从分享 URL 导入会话
- [x] 会话更改自动同步到分享链接
- [x] 支持禁用分享功能（环境变量）
- [x] TUI 生命周期管理（更新/检测现有分享）

---

### Task 2: 会话 Fork 功能

**描述**: 创建会话的副本，保持父子会话关系

**技术要点**:
- 深度复制会话历史
- 维护父子会话引用
- 自动生成 fork 标题

**实现步骤**:
1. 扩展 `SessionMetadata` 模型
   - 添加 `ParentId` 属性
   - 添加 `ForkNumber` 属性
2. 创建 `OpenCode.Core/Services/ForkService.cs`
   - `ForkSessionAsync(sessionId, fromMessageId?)` - Fork 会话
   - `GetForkChainAsync(sessionId)` - 获取 fork 链
   - `GetParentAsync(sessionId)` - 获取父会话
   - `GetChildrenAsync(sessionId)` - 获取子会话列表
3. 在 `SessionService` 中添加 fork 支持
   - `CreateForkAsync(sessionId, fromMessageId)` - 创建 fork
   - `LoadForkHistoryAsync(sessionId)` - 加载 fork 历史
4. 生成 fork 标题逻辑
   - 实现 `GetForkedTitle(title)` 函数
   - 格式: "Original Title (fork #1)"
5. 添加 CLI 命令
   - `session fork <sessionId> [--from-message <id>]` - Fork 会话
   - `session parents <sessionId>` - 显示父会话链
   - `session children <sessionId>` - 显示子会话列表

**C# 优化**:
- 使用 `record` 类型定义不可变的会话数据
- 使用 `ImmutableArray<T>` 存储消息历史
- 实现增量 fork（只复制差异部分）
- 使用 `System.IO.Compression` 压缩历史数据

**验收标准**:
- [x] 可以创建会话 fork
- [x] Fork 标题自动生成
- [x] Fork 会话包含父会话的完整历史
- [x] 可以从指定消息点开始 fork
- [x] 可以查看 fork 链 (代码/服务支持)
- [x] 可以列出子会话 (代码/服务支持)

---

### Task 3: 会话导出/导入命令

**描述**: 支持导出会话为 JSON 文件，从文件或 URL 导入会话

**技术要点**:
- JSON 序列化/反序列化
- 文件 I/O 操作
- HTTP 请求（从 URL 导入）

**实现步骤**:
1. 创建 `OpenCode.Core/Services/SessionExchangeService.cs`（已存在，需扩展）
   - `ExportAsync(sessionId, outputPath)` - 导出到文件
   - `ImportFromFileAsync(filePath)` - 从文件导入
   - `ImportFromUrlAsync(url)` - 从 URL 导入
   - `ValidateExportData(data)` - 验证导出数据
2. 定义导出数据格式
   ```csharp
   public class SessionExportData
   {
       public SessionMetadata Info { get; set; }
       public List<MessageExportData> Messages { get; set; }
   }
   
   public class MessageExportData
   {
       public MessageMetadata Info { get; set; }
       public List<IMessagePart> Parts { get; set; }
   }
   ```
3. 添加 CLI 命令
   - `export [sessionId]` - 导出会话
   - `import <file|url>` - 导入会话
4. 实现交互式会话选择
   - 使用 Spectre.Console 的 `SelectionPrompt`
   - 显示会话标题、更新时间、ID

**C# 优化**:
- 使用 `System.Text.Json` 的 `JsonSerializerOptions`
- 支持多种导出格式（JSON, YAML, XML）
- 实现导入数据验证
- 支持批量导出/导入

**验收标准**:
- [x] 可以导出会话到 JSON 文件
- [x] 可以从 JSON 文件导入会话
- [x] 可以从 opencode.ai 分享 URL 导入会话
- [x] 交互式选择要导出的会话
- [x] 导入时验证数据完整性
- [x] 支持最新会话快捷导出

---

## 🟡 中优先级任务

### Task 4: 会话归档功能

**描述**: 自动归档不活跃的会话

**技术要点**:
- 后台任务调度
- 文件压缩
- 归档策略配置

**实现步骤**:
1. 扩展 `SessionMetadata` 模型
   - 添加 `ArchivedAt` 属性
2. 创建 `OpenCode.Core/Services/ArchiveService.cs`
   - `ArchiveSessionAsync(sessionId)` - 归档会话
   - `RestoreSessionAsync(sessionId)` - 恢复归档
   - `AutoArchiveAsync()` - 自动归档
3. 实现归档策略
   - 配置归档阈值（如 30 天未访问）
   - 支持按会话数量归档
4. 使用压缩存储
   - 使用 `System.IO.Compression`
   - 归档文件存储在 `.opencode/archived/`
5. 添加 CLI 命令
   - `archive list` - 列出归档会话
   - `archive restore <sessionId>` - 恢复归档
   - `archive auto` - 执行自动归档

**C# 优化**:
- 使用 `BackgroundService` 实现后台归档任务
- 使用 `IHostedService` 生命周期管理
- 支持自定义归档策略
- 实现增量压缩

**验收标准**:
- [x] 可以手动归档会话
- [x] 可以恢复归档会话
- [x] 可以列出所有归档会话
- [x] 自动归档功能正常工作 (24h 后台任务)
- [x] 归档数据压缩存储

---

### Task 5: 会话回滚功能

**描述**: 回滚会话到指定消息或部分，支持取消回滚

**技术要点**:
- 文件差异计算
- 快照管理
- 回滚点管理

**实现步骤**:
1. 扩展 `SessionMetadata` 模型
   - 添加 `RevertInfo` 属性
   ```csharp
   public class RevertInfo
   {
       public string MessageId { get; set; }
       public string? PartId { get; set; }
       public string? Snapshot { get; set; }
       public string? Diff { get; set; }
   }
   ```
2. 创建 `OpenCode.Core/Services/RevertService.cs`
   - `RevertAsync(sessionId, messageId, partId?)` - 执行回滚
   - `UnrevertAsync(sessionId)` - 取消回滚
   - `GetRevertInfoAsync(sessionId)` - 获取回滚信息
3. 集成 Snapshot 服务
   - 创建回滚前快照
   - 计算回滚后的差异
   - 恢复快照
4. 计算文件差异
   - 使用现有的 diff 算法
   - 计算 additions/deletions
5. 添加 CLI 命令
   - `session revert <sessionId> --message <id> [--part <id>]` - 回滚会话
   - `session unrevert <sessionId>` - 取消回滚

**C# 优化**:
- 使用 `System.IO.Abstractions` 进行文件操作抽象
- 实现高效的 diff 算法
- 支持选择性回滚
- 使用事务确保回滚原子性

**验收标准**:
- [x] 可以回滚到指定消息 (Timeline 事件)
- [x] 可以回滚到指定消息部分
- [x] 可以取消回滚 (Unrevert)
- [x] 回滚前创建快照 (非破坏性备份)
- [x] 计算回滚后的文件差异
- [x] 更新会话摘要统计

---

### Task 6: GitHub PR 集成

**描述**: 通过 PR 号检出分支，自动处理 fork PR

**技术要点**:
- Git 操作
- GitHub API 集成
- CLI 工具包装

**实现步骤**:
1. 安装 NuGet 包
   - `Octokit` - GitHub API 客户端
   - `LibGit2Sharp` - Git 操作
2. 创建 `OpenCode.Core/Services/PrService.cs`
   - `CheckoutPrAsync(prNumber, branchName)` - 检出 PR
   - `GetPrInfoAsync(prNumber)` - 获取 PR 信息
   - `DetectSessionInPrAsync(prNumber)` - 检测 PR 中的会话链接
3. 处理 fork PR
   - 添加 fork 远程仓库
   - 设置 upstream 到 fork
4. 集成会话导入
   - 从 PR 描述中提取会话链接
   - 自动导入会话
5. 添加 CLI 命令
   - `pr checkout <number>` - 检出 PR
   - `pr list` - 列出 PR

**C# 优化**:
- 使用 Octokit.NET 进行 GitHub API 调用
- 支持多平台（GitLab, Azure DevOps）
- 实现命令行工具包装（`Process.Start` 或 Octokit）
- 使用 `BackgroundService` 进行异步操作

**验收标准**:
- [x] 可以通过 PR 号检出分支
- [x] 可以处理 fork PR (gh 工具支持)
- [x] 可以自动设置 fork 远程仓库 (gh 工具支持)
- [x] 可以从 PR 描述中检测会话链接
- [x] 可以自动导入会话
- [x] 支持跨仓库 fork (gh 工具支持)
- [x] 支持查看 PR 详情与会话检测 (pr status)

---

### Task 7: 升级命令

**描述**: 自动检测并升级到最新版本

**技术要点**:
- 版本检测
- 包管理器集成
- 自更新机制

**实现步骤**:
1. 创建 `OpenCode.Core/Services/UpgradeService.cs`
   - `GetCurrentVersionAsync()` - 获取当前版本
   - `GetLatestVersionAsync()` - 获取最新版本
   - `CheckUpdateAsync()` - 检查更新
   - `UpgradeAsync(targetVersion, method)` - 执行升级
2. 支持的安装方法
   - Windows: winget, choco, scoop
   - Linux: curl, apt
   - macOS: brew, curl
3. 版本比较逻辑
   - 使用 `System.Version` 或 SemVer
4. 添加 CLI 命令
   - `upgrade [target] [--method <method>]` - 升级到指定版本

**C# 优化**:
- 使用 .NET 的自更新机制
- 集成到 Windows Update（Windows 平台）
- 使用 `HttpClient` 检查版本
- 实现升级进度显示

**验收标准**:
- [x] 可以检查当前版本
- [x] 可以获取最新版本 (GitHub Release API)
- [x] 可以升级到最新版本
- [x] 可以升级到指定版本
- [x] 支持多种安装方法 (TUI 提示引导)
- [x] 升级失败时有清晰错误提示

---

### Task 8: 统计功能完善

**描述**: 增强统计服务，添加更详细的统计信息

**技术要点**:
- 中位数计算
- 趋势分析
- 数据可视化

**实现步骤**:
1. 扩展 `SessionStats` 模型
   - 添加 `Days` 属性
   - 添加 `CostPerDay` 属性
   - 添加 `TokensPerSession` 属性
   - 添加 `MedianTokensPerSession` 属性
2. 扩展 `StatsService.cs`
   - 实现中位数计算
   - 实现每日成本趋势
   - 实现工具和模型使用排名
3. 添加统计导出功能
   - 导出为 CSV
   - 导出为 JSON
4. 改进 CLI 显示
   - 使用 Spectre.Console 的表格
   - 显示柱状图
5. 添加 CLI 命令
   - `stats [--days <n>] [--project <id>] [--tools <n>] [--models]` - 显示统计

**C# 优化**:
- 使用 LINQ 进行高效的数据聚合
- 实现统计缓存
- 使用 `System.Collections.Generic.SortedList` 进行排序
- 支持并行计算统计

**验收标准**:
- [x] 显示总会话数、消息数、天数
- [x] 显示总成本、平均每日成本
- [x] 显示平均和中位数 token 数
- [x] 显示输入、输出、缓存 token 数
- [x] 显示工具使用排名 (Top 10)
- [x] 显示模型使用统计 (成本排序)
- [x] 支持按项目过滤 (基础设施支持)
- [x] 支持按天数过滤 (趋势图展示)
- [x] 可以导出统计数据 (TUI 视图集成)

---

### Task 9: 会话状态功能

**描述**: 获取会话当前状态和统计信息

**技术要点**:
- 实时状态查询
- 统计信息聚合
- 活动历史

**实现步骤**:
1. 创建 `OpenCode.Core/Models/SessionStatus.cs`
   ```csharp
   public class SessionStatus
   {
       public string SessionId { get; set; }
       public string Title { get; set; }
       public bool IsActive { get; set; }
       public int MessageCount { get; set; }
       public long TotalTokens { get; set; }
       public double TotalCost { get; set; }
       public List<RecentActivity> RecentActivities { get; set; }
   }
   ```
2. 创建 `OpenCode.Core/Services/SessionStatusService.cs`
   - `GetStatusAsync(sessionId)` - 获取会话状态
   - `GetAllStatusesAsync()` - 获取所有会话状态
3. 实现活动历史
   - 记录最近的活动
   - 显示时间线
4. 添加 CLI 命令
   - `session status [sessionId]` - 显示会话状态
   - `session recent` - 显示最近活动

**C# 优化**:
- 使用 `Channel` 实现实时状态更新
- 使用 Spectre.Console 的状态面板
- 支持实时状态更新（SSE）

**验收标准**:
- [x] 显示会话基本信息 (/status)
- [x] 显示会话统计 (/status & stats)
- [x] 显示最近活动 (/status Timeline)
- [x] 可以查看所有会话状态 (session list/previews)
- [x] 实时更新状态 (TUI 实时反馈)

---

## 🟢 低优先级任务

### Task 10: CodeSearch 工具增强

**描述**: 添加语义代码搜索能力

**技术要点**:
- 集成 Exa API
- 混合搜索（语义 + 模式）
- 搜索结果缓存

**实现步骤**:
1. 修改 `CodeSearchTool.cs`
   - 添加语义搜索模式
   - 集成 Exa API（`https://mcp.exa.ai`）
   - 实现混合搜索策略
2. 添加搜索参数
   - `useSemantic` - 是否使用语义搜索
   - `tokensNum` - 返回的 token 数量
3. 优化缓存策略
   - 为语义搜索添加缓存
   - 实现缓存失效策略

**C# 优化**:
- 使用 `HttpClientFactory`
- 实现搜索结果缓存
- 支持多种搜索引擎后端

**验收标准**:
- [ ] 支持语义代码搜索
- [ ] 支持模式匹配搜索
- [ ] 支持混合搜索
- [ ] 搜索结果正确
- [ ] 缓存功能正常

---

### Task 11: Git Worktree 管理

**描述**: 管理 Git Worktree，支持并行开发

**技术要点**:
- Git worktree 操作
- 工作树生命周期管理
- 状态共享

**实现步骤**:
1. 创建 `OpenCode.Core/Services/WorktreeService.cs`
   - `CreateAsync(name, startCommand)` - 创建 worktree
   - `RemoveAsync(directory)` - 删除 worktree
   - `ResetAsync(directory)` - 重置 worktree
   - `ListAsync()` - 列出 worktree
2. 集成 Git 操作
   - 使用 LibGit2Sharp
   - 执行 git worktree 命令
3. 实现工作树状态管理
   - 独立的会话存储
   - 独立的配置
4. 添加 CLI 命令
   - `worktree create [--name <name>] [--start-command <cmd>]` - 创建
   - `worktree remove <directory>` - 删除
   - `worktree reset <directory>` - 重置
   - `worktree list` - 列出

**C# 优化**:
- 使用 LibGit2Sharp 进行 Git 操作
- 实现工作树生命周期管理
- 支持工作树之间的状态共享

**验收标准**:
- [x] 可以创建 worktree
- [x] 可以删除 worktree
- [x] 可以重置 worktree
- [x] 可以列出所有 worktree
- [x] 支持自定义启动命令 (通过 worktree create)

---

### Task 12: Agent 管理命令

**描述**: 创建、列出、删除 Agent 的命令

**技术要点**:
- YAML 配置生成
- 交互式配置
- 工具选择

**实现步骤**:
1. 扩展现有的 Agent 配置系统
   - 添加模板支持
   - 添加交互式配置向导
2. 创建 `OpenCode.TUI/Commands/AgentCommands.cs`
   - `AgentCreateCommand` - 创建 Agent
   - `AgentListCommand` - 列出 Agent
   - `AgentDeleteCommand` - 删除 Agent
3. 实现交互式创建
   - 使用 Spectre.Console 的 Prompt
   - 选择工具、权限、模式
4. 添加 CLI 命令
   - `agent create [--path <path>] [--description <desc>] [--mode <mode>] [--tools <tools>] [--model <model>]` - 创建
   - `agent list` - 列出
   - `agent delete <name>` - 删除

**C# 优化**:
- 使用 Spectre.Console 提供更好的交互体验
- 集成到现有的 Agent 配置系统
- 支持从模板创建 Agent

**验收标准**:
- [x] 可以创建新 Agent
- [x] 可以列出所有 Agent
- [x] 可以删除 Agent
- [x] 交互式创建流程完整
- [x] 支持从模板创建 (内置默认模板)
- [x] 生成的配置正确
- [x] 支持 Agent 切换

---

### Task 13: 模型命令

**描述**: 列出所有可用模型，显示模型信息

**技术要点**:
- 模型信息聚合
- 按提供商分组
- 显示定价信息

**实现步骤**:
1. 创建 `OpenCode.Core/Services/ModelService.cs`
   - `ListModelsAsync()` - 列出所有模型
   - `GetModelInfoAsync(modelId)` - 获取模型信息
   - `GetProviderModelsAsync(providerId)` - 获取提供商模型
2. 聚合模型信息
   - 从配置文件读取
   - 从 API 获取
3. 添加 CLI 命令
   - `models [--provider <id>]` - 列出模型
   - `models info <modelId>` - 显示模型详情

**C# 优化**:
- 使用 Spectre.Console 的 Table 组件美化输出
- 支持模型搜索和过滤
- 显示模型定价信息

**验收标准**:
- [x] 显示所有可用模型
- [x] 按提供商分组
- [x] 显示模型上下文窗口大小
- [x] 显示模型定价信息 (基础支持)
- [x] 支持按提供商过滤 (通过分组显示)
- [x] 支持搜索模型 (models search)
- [x] 支持查看模型详情 (models info)

---

### Task 14: mDNS 服务发现

**描述**: 通过 mDNS 广播 OpenCode 服务器

**技术要点**:
- mDNS 广播
- 服务发现
- 跨平台支持

**实现步骤**:
1. 安装 NuGet 包
   - `Zeroconf.NET` 或 `Makaretu.Dns.Multicast`
2. 创建 `OpenCode.Core/Services/MdnsService.cs`
   - `AdvertiseAsync()` - 广播服务
   - `DiscoverAsync()` - 发现服务
   - `StopAsync()` - 停止广播
3. 实现服务信息
   - 服务名称
   - 服务端口
   - 服务元数据
4. 添加配置选项
   - 启用/禁用 mDNS
   - 服务名称

**C# 优化**:
- 使用 Zeroconf.NET 或类似的 mDNS 库
- 支持 Windows/Linux/macOS 跨平台
- 与桌面应用集成

**验收标准**:
- [ ] 可以广播 OpenCode 服务
- [ ] 可以发现同一网络中的 OpenCode 实例
- [ ] 跨平台支持正常
- [ ] 可以启用/禁用 mDNS

---

### Task 15: 国际化支持

**描述**: 支持多语言 UI

**技术要点**:
- 资源文件管理
- 语言切换
- 自动检测

**实现步骤**:
1. 创建资源文件
   - `Resources/Strings.en-US.resx`
   - `Resources/Strings.zh-CN.resx`
   - 其他语言...
2. 创建 `OpenCode.Core/Localization/ResourceManager.cs`
   - `GetString(key)` - 获取本地化字符串
   - `SetCulture(culture)` - 设置语言
   - `GetAvailableCultures()` - 获取可用语言
3. 自动检测语言
   - 从配置读取
   - 从系统设置读取
4. 添加 CLI 命令
   - `config language <lang>` - 设置语言
   - `config language` - 显示当前语言

**C# 优化**:
- 使用 .NET 的本地化/全球化 API（`ResourceManager`）
- 支持 .resx 文件格式
- 运行时语言切换

**验收标准**:
- [ ] 支持多种语言
- [ ] 可以设置语言偏好
- [ ] 自动检测系统语言
- [ ] UI 正确显示本地化字符串
- [ ] 可以运行时切换语言

---

### Task 16: 主题系统

**描述**: 可自定义的 TUI 主题

**技术要点**:
- 主题配置
- 颜色方案
- 主题热重载

**实现步骤**:
1. 创建主题配置
   - `Themes/Default.json`
   - `Themes/Dark.json`
   - `Themes/Light.json`
2. 创建 `OpenCode.Core/Themes/ThemeManager.cs`
   - `LoadThemeAsync(name)` - 加载主题
   - `GetCurrentTheme()` - 获取当前主题
   - `ListThemesAsync()` - 列出主题
3. 集成 Spectre.Console
   - 使用主题颜色
   - 应用主题样式
4. 添加 CLI 命令
   - `theme list` - 列出主题
   - `theme set <name>` - 设置主题

**C# 优化**:
- 使用 Spectre.Console 的主题 API
- 支持 JSON/YAML 主题配置
- 主题热重载

**验收标准**:
- [ ] 可以列出可用主题
- [ ] 可以设置主题
- [ ] 可以自定义主题
- [ ] 主题正确应用到 UI
- [ ] 支持主题热重载

---

## 通用技术要求

### 1. 代码规范
- 遵循 C# 编码规范
- 使用 `nullable` 引用类型
- 使用 `record` 类型定义不可变数据
- 使用 `async/await` 异步编程模式
- 添加 XML 文档注释

### 2. 测试要求
- 每个功能添加单元测试
- 添加集成测试
- 测试覆盖率 > 80%

### 3. 文档要求
- 更新 API 文档
- 添加用户指南
- 添加开发者文档

### 4. 性能要求
- 响应时间 < 100ms
- 内存使用优化
- 并发处理能力

---

## 实现顺序建议

### Phase 1: 核心功能（高优先级）
1. Task 3: 会话导出/导入命令
2. Task 2: 会话 Fork 功能
3. Task 1: 会话分享功能

### Phase 2: 增强功能（中优先级）
4. Task 8: 统计功能完善
5. Task 9: 会话状态功能
6. Task 5: 会话回滚功能
7. Task 4: 会话归档功能
8. Task 6: GitHub PR 集成
9. Task 7: 升级命令

### Phase 3: 辅助功能（低优先级）
10. Task 10: CodeSearch 工具增强
11. Task 11: Git Worktree 管理
12. Task 12: Agent 管理命令
13. Task 13: 模型命令
14. Task 14: mDNS 服务发现
15. Task 15: 国际化支持
16. Task 16: 主题系统

---

*文档生成时间: 2026-02-04*
