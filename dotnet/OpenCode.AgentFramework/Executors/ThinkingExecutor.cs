using Microsoft.Agents.AI;
using OpenCode.AgentFramework.Abstractions;
using OpenCode.Core.Contracts;
using OpenCode.Core.Models;
using OpenCode.Core.Services;
using OpenCode.Core.Utilities;
using Microsoft.Extensions.AI;
using OpenCode.Core.Parsing;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenCode.AgentFramework.Executors;

/// <summary>
/// 思考执行器，基于 Microsoft Agent Framework (MAF) 实现
/// </summary>
public class ThinkingExecutor : Executor
{
    private readonly IChatClient _chatClient;
    private readonly IInstructionService _instructionService;
    private readonly SessionService _sessionService;
    private readonly IAgentConfigurationProvider _agentProvider;
    private readonly CompactionService? _compactionService;
    private readonly ConfigService _configService;
    private readonly SnapshotService? _snapshotService;
    private readonly IPermissionService? _permissionService;
    private readonly PluginService? _pluginService;
    private readonly SessionRetryService? _retryService;
    private string _agentName;
    private const int MaxSteps = 30;
    private const int DoomLoopThreshold = 3;

    public class ThinkingState
    {
        public List<ChatMessage> History { get; } = new();
        public List<ToolCallInfo> RecentToolCalls { get; } = new();
        public int StepCount { get; set; } = 0;
        public string AgentName { get; set; } = "build";
    }

    public ThinkingExecutor(
        string id, 
        IChatClient chatClient, 
        IInstructionService instructionService,
        SessionService sessionService,
        IAgentConfigurationProvider agentProvider,
        ConfigService configService,
        CompactionService? compactionService = null,
        SnapshotService? snapshotService = null,
        IPermissionService? permissionService = null,
        PluginService? pluginService = null,
        SessionRetryService? retryService = null,
        string agentName = "build") : base(id)
    {
        _chatClient = chatClient;
        _instructionService = instructionService;
        _sessionService = sessionService;
        _agentProvider = agentProvider;
        _configService = configService;
        _compactionService = compactionService;
        _snapshotService = snapshotService;
        _permissionService = permissionService;
        _pluginService = pluginService;
        _retryService = retryService;
        _agentName = agentName;
    }

    public class ToolCallInfo
    {
        public string ToolName { get; set; } = string.Empty;
        public string InputHash { get; set; } = string.Empty;
    }

    public override async Task ProcessAsync(object input, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var state = context.GetOrSetState("thinking", () => new ThinkingState { AgentName = _agentName });
        state.StepCount++;
        
        if (state.StepCount > MaxSteps)
        {
            await context.YieldOutputAsync(new { status = "error", message = "Detected potential infinite loop (MaxSteps exceeded). Please check the logs." }, cancellationToken);
            return;
        }

        // 自动剪枝与压缩逻辑
        if (_compactionService != null && _configService.Config.Compaction != null)
        {
            // 1. 自动剪枝
            if (_configService.Config.Compaction.Prune)
            {
                var prunedHistory = _compactionService.Prune(state.History);
                if (prunedHistory.Count != state.History.Count) 
                {
                    state.History.Clear();
                    state.History.AddRange(prunedHistory);
                }
            }

            // 2. 自动压缩
            if (_configService.Config.Compaction.Auto && state.History.Count > 30)
            {
                await context.YieldOutputAsync(new { status = "thinking", message = "正在压缩长对话上下文..." }, cancellationToken);
                
                if (_pluginService != null)
                {
                    await _pluginService.TriggerSessionCompactingAsync(context.WorkflowId, state.History);
                }

                var summary = await _compactionService.CompactAsync(state.History, cancellationToken);
                state.History.Clear();
                state.History.Add(new ChatMessage(ChatRole.System, $"Previous conversation summary (preserving state): {summary}"));
            }
        }

        var agent = await CreateAgentAsync(state.AgentName);
        var parser = new RobustStreamingJsonParser();
        
        // 1. 处理输入并维护历史
        if (input is string userPrompt)
        {
            state.StepCount = 0; // 重置步数
            state.History.Add(new ChatMessage(ChatRole.User, userPrompt));
            
            var eventId = Guid.NewGuid().ToString("N").Substring(0, 8);
            await _sessionService.AddTimelineEventAsync(context.WorkflowId, new TimelineEvent(
                eventId, "message", null, null, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            // Background save MessageV2 compatible data
            var msgV2 = new MessageInfo(
                eventId,
                "user",
                new List<MessagePart> { new TextPart(userPrompt) },
                new MessageMetadata(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), context.WorkflowId)
                {
                    ModelId = _configService.Config.Model,
                    ProviderId = _configService.Config.Model?.Split('/')[0]
                }
            );
            _ = _sessionService.SaveMessageAsync(context.WorkflowId, msgV2);
        }
        else if (input != null && input.GetType().GetProperty("prompt") != null)
        {
            // 处理匿名对象输入 (如 TaskTool 启动的子任务)
            dynamic dynInput = input;
            string p = dynInput.prompt;
            string? a = dynInput.agent;
            if (!string.IsNullOrEmpty(a)) state.AgentName = a;
            
            state.History.Add(new ChatMessage(ChatRole.User, p));
        }
        else if (input is JsonObject toolResult)
        {
            var toolName = toolResult["tool"]?.ToString() ?? "unknown";
            var result = toolResult["result"]?.ToString() ?? "";
            var snapshotHash = toolResult["snapshot"]?.ToString();
            var callId = toolResult["callId"]?.ToString() ?? Guid.NewGuid().ToString("N").Substring(0, 8);

            // Record patch if snapshot changed
            if (_snapshotService != null && !string.IsNullOrEmpty(snapshotHash))
            {
                var patch = await _snapshotService.GetPatchAsync(snapshotHash);
                if (patch.Files.Any())
                {
                    await context.YieldOutputAsync(new { status = "patch", hash = patch.Hash, files = patch.Files }, cancellationToken);
                    
                    await _sessionService.AddTimelineEventAsync(context.WorkflowId, new TimelineEvent(
                        Guid.NewGuid().ToString("N").Substring(0, 8), "checkpoint", null, snapshotHash, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                }
            }
            
            // Handle special mode switch commands
            if (toolName == "plan_enter" && result.StartsWith("SUCCESS"))
            {
                state.AgentName = "plan";
                result = "User approved plan mode. I am now in plan mode. I will focus on research and planning.";
            }
            else if (toolName == "plan_exit" && result.StartsWith("SUCCESS"))
            {
                state.AgentName = "build";
                result = "Plan approved. I am now in build mode. I will execute the approved plan.";
            }

            state.History.Add(new ChatMessage(ChatRole.Tool, result) { AuthorName = toolName });

            var eventId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            var toolInvocation = new ToolInvocation("result", callId, toolName, new JsonObject(), result);
            var msgV2 = new MessageInfo(
                eventId,
                "tool",
                new List<MessagePart> { new ToolInvocationPart(toolInvocation) },
                new MessageMetadata(timestamp, context.WorkflowId)
                {
                    ModelId = _configService.Config.Model,
                    ProviderId = _configService.Config.Model?.Split('/')[0],
                    Completed = timestamp,
                    Tool = new Dictionary<string, ToolMetadata>
                    {
                        [toolName] = new ToolMetadata(toolName, timestamp, timestamp, snapshotHash)
                    }
                }
            );
            await _sessionService.SaveMessageAsync(context.WorkflowId, msgV2);
            await _sessionService.AddTimelineEventAsync(context.WorkflowId, new TimelineEvent(
                eventId, "message", eventId, null, timestamp));
        }

        // 2. 发送状态信号
        await context.YieldOutputAsync(new { status = "thinking", message = $"正在思考 ({state.AgentName})..." }, cancellationToken);

        // 3. 运行 Agent (使用标准 ChatMessage 历史)
        int attempt = 0;
        int outputTokens = 0;
        int finalInputTokens = 0;
        while (true)
        {
            try
            {
                var stream = agent.RunStreamingAsync(state.History, cancellationToken: cancellationToken);
                
                var assistantContent = new StringBuilder();
                var reasoningContent = new StringBuilder();
                var currentParts = new List<MessagePart>();
                outputTokens = 0;
                
                var assistantEventId = Guid.NewGuid().ToString("N").Substring(0, 8);
                var assistantStartTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                await foreach (var update in stream)
                {
                    // Handle Reasoning Content
                    foreach (var content in update.Contents)
                    {
                        if (content.GetType().Name.Contains("Reasoning"))
                        {
                            var text = content.ToString() ?? "";
                            if (!string.IsNullOrEmpty(text))
                            {
                                reasoningContent.Append(text);
                                await context.YieldOutputAsync(new { status = "thinking_stream", reasoning = text }, cancellationToken);
                            }
                            continue;
                        }
                    }

                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        assistantContent.Append(update.Text);
                        outputTokens += TruncationService.EstimateTokens(update.Text);
                        await context.YieldOutputAsync(new { status = "thinking_stream", delta = update.Text }, cancellationToken);
                        
                        foreach (var node in parser.ParseChunk(update.Text))
                        {
                            if (node != null)
                            {
                                await context.YieldOutputAsync(new { status = "parsed", data = node }, cancellationToken);

                                if (node["tool"] != null)
                                {
                                    var toolName = node["tool"]?.ToString() ?? "";
                                    var toolInputNode = node["args"]?.DeepClone() ?? new JsonObject();
                                    var toolInputStr = toolInputNode.ToString();
                                    
                                    // Doom Loop Detection
                                    state.RecentToolCalls.Add(new ToolCallInfo { ToolName = toolName, InputHash = toolInputStr });
                                    if (state.RecentToolCalls.Count > DoomLoopThreshold) state.RecentToolCalls.RemoveAt(0);

                                    if (state.RecentToolCalls.Count == DoomLoopThreshold && 
                                        state.RecentToolCalls.All(t => t.ToolName == toolName && t.InputHash == toolInputStr))
                                    {
                                        if (_permissionService != null)
                                        {
                                            var approved = await _permissionService.AskAsync(new PermissionInfo
                                            {
                                                Id = "doom_loop_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                                                Type = "doom_loop",
                                                Pattern = toolName,
                                                SessionId = context.WorkflowId,
                                                Message = $"Detected a potential infinite loop with tool '{toolName}'. Do you want to allow it to continue?",
                                                Metadata = new Dictionary<string, object> { ["tool"] = toolName, ["input"] = toolInputStr }
                                            });

                                            if (approved == PermissionResponse.Reject)
                                            {
                                                await context.YieldOutputAsync(new { status = "error", message = "Tool execution aborted due to suspected infinite loop." }, cancellationToken);
                                                return;
                                            }
                                        }
                                    }

                                    // Build message parts for persistence
                                    if (reasoningContent.Length > 0) currentParts.Add(new ReasoningPart(reasoningContent.ToString()));
                                    if (assistantContent.Length > 0) currentParts.Add(new TextPart(assistantContent.ToString()));
                                    
                                    var toolCallId = Guid.NewGuid().ToString("N").Substring(0, 8);
                                    currentParts.Add(new ToolInvocationPart(new ToolInvocation("call", toolCallId, toolName, toolInputNode)));

                                    var assistantMsg = new MessageInfo(
                                        assistantEventId,
                                        "assistant",
                                        currentParts,
                                        new MessageMetadata(assistantStartTime, context.WorkflowId)
                                        {
                                            ModelId = _configService.Config.Model,
                                            ProviderId = _configService.Config.Model?.Split('/')[0],
                                            Completed = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                        }
                                    );
                                    await _sessionService.SaveMessageAsync(context.WorkflowId, assistantMsg);
                                    await _sessionService.AddTimelineEventAsync(context.WorkflowId, new TimelineEvent(
                                        assistantEventId, "message", assistantEventId, null, assistantStartTime));

                                    // Execute tool
                                    var toolCall = new JsonObject
                                    {
                                        ["tool"] = toolName,
                                        ["args"] = toolInputNode,
                                        ["agent"] = state.AgentName,
                                        ["callId"] = toolCallId
                                    };

                                    if (_snapshotService != null) await _snapshotService.TrackAsync();
                                    await context.SendMessageAsync("ToolRouter", toolCall, cancellationToken);
                                    return;
                                }
                                else if (node["answer"] != null)
                                {
                                    var answer = node["answer"]?.ToString() ?? "";
                                    
                                    if (reasoningContent.Length > 0) currentParts.Add(new ReasoningPart(reasoningContent.ToString()));
                                    currentParts.Add(new TextPart(answer));

                                    var assistantMsg = new MessageInfo(
                                        assistantEventId,
                                        "assistant",
                                        currentParts,
                                        new MessageMetadata(assistantStartTime, context.WorkflowId)
                                        {
                                            ModelId = _configService.Config.Model,
                                            ProviderId = _configService.Config.Model?.Split('/')[0],
                                            Completed = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                        }
                                    );
                                    await _sessionService.SaveMessageAsync(context.WorkflowId, assistantMsg);
                                    await _sessionService.AddTimelineEventAsync(context.WorkflowId, new TimelineEvent(
                                        assistantEventId, "message", assistantEventId, null, assistantStartTime));

                                    await context.YieldOutputAsync(new { status = "completed", answer }, cancellationToken);
                                    return;
                                }
                            }
                        }
                    }
                }

                // Final token usage estimate if not provided by metadata
                finalInputTokens = TruncationService.EstimateTokens(string.Join("\n", state.History.Select(m => m.Text)));
                await context.YieldOutputAsync(new { status = "usage", inputTokens = finalInputTokens, outputTokens = outputTokens }, cancellationToken);

                if (currentParts.Count == 0)
                {
                    if (reasoningContent.Length > 0) currentParts.Add(new ReasoningPart(reasoningContent.ToString()));
                    if (assistantContent.Length > 0) currentParts.Add(new TextPart(assistantContent.ToString()));
                }

                if (currentParts.Count > 0)
                {
                    var finalMsg = new MessageInfo(
                        assistantEventId,
                        "assistant",
                        currentParts,
                        new MessageMetadata(assistantStartTime, context.WorkflowId)
                        {
                            ModelId = _configService.Config.Model,
                            ProviderId = _configService.Config.Model?.Split('/')[0],
                            Completed = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        }
                    );
                    await _sessionService.SaveMessageAsync(context.WorkflowId, finalMsg);
                    state.History.Add(new ChatMessage(ChatRole.Assistant, assistantContent.ToString()));
                }
                
                break; // 成功执行，退出重试循环
            }
            catch (Exception ex) when (_retryService != null && _retryService.IsRetryable(ex))
            {
                attempt++;
                var delay = _retryService.GetDelay(attempt);
                await context.YieldOutputAsync(new { status = "thinking", message = $"请求失败，正在进行第 {attempt} 次重试 (延迟 {delay}ms)..." }, cancellationToken);
                await _retryService.SleepAsync(delay, cancellationToken);
            }
        }

        // Check for compaction
        if (_compactionService != null && _configService.Config.Compaction?.Auto != false && _compactionService.IsOverflow(finalInputTokens, outputTokens, 128000))
        {
            await context.YieldOutputAsync(new { status = "compacting" }, cancellationToken);

            if (_pluginService != null)
            {
                await _pluginService.TriggerSessionCompactingAsync(context.WorkflowId, state.History);
            }

            var summary = await _compactionService.CompactAsync(state.History, cancellationToken);
            state.History.Clear();
            state.History.Add(new ChatMessage(ChatRole.System, summary));
            await context.YieldOutputAsync(new { status = "compacted", summary }, cancellationToken);
        }
    }

    private async Task<AIAgent> CreateAgentAsync(string agentName)
    {
        var currentAgent = agentName;
        // 自动选择默认 Agent
        if (currentAgent == "build")
        {
            var config = _configService.Config;
            if (!string.IsNullOrEmpty(config.DefaultAgent))
            {
                currentAgent = config.DefaultAgent;
            }
            else
            {
                var allAgents = await _agentProvider.GetAllAgentsAsync();
                var defaultAgent = allAgents.FirstOrDefault(a => !a.Hidden && a.Mode != "subagent");
                if (defaultAgent != null && defaultAgent.Name != "build")
                {
                    currentAgent = defaultAgent.Name;
                }
            }
        }

        var modelId = _configService.Config.Model ?? "claude-3-5-sonnet";
        var instructions = await _instructionService.GetAggregatedInstructionsAsync(currentAgent, modelId);
        
        return _chatClient.AsAIAgent(
            name: currentAgent,
            instructions: instructions
        );
    }

    private static async IAsyncEnumerable<string> ToTextStream(IAsyncEnumerable<AgentResponseUpdate> updates)
    {
        await foreach (var update in updates)
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}
