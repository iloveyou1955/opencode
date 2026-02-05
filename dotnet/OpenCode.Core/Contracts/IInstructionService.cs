namespace OpenCode.Core.Contracts;

public interface IInstructionService
{
    Task<string> GetAggregatedInstructionsAsync(string agentName);
    Task<string> GetAggregatedInstructionsAsync(string agentName, string modelId);
}
