namespace OpenCode.Core.Contracts;

/// <summary>
/// 用于创建和运行子任务工作流的工厂接口。
/// </summary>
public interface IWorkflowFactory
{
    /// <summary>
    /// 异步运行一个子任务。
    /// </summary>
    /// <param name="prompt">子任务提示词。</param>
    /// <param name="agentType">子智能体类型。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>子任务的执行总结。</returns>
    Task<string> RunSubtaskAsync(string prompt, string agentType, CancellationToken ct = default);
}
