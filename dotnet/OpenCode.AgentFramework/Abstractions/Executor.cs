namespace OpenCode.AgentFramework.Abstractions;

public abstract class Executor
{
    public string Id { get; }

    protected Executor(string id)
    {
        Id = id;
    }

    public abstract Task ProcessAsync(object input, IWorkflowContext context, CancellationToken cancellationToken);
}

public abstract class Executor<TInput> : Executor
{
    protected Executor(string id) : base(id) { }

    public override Task ProcessAsync(object input, IWorkflowContext context, CancellationToken cancellationToken)
    {
        if (input is TInput typedInput)
        {
            return ProcessAsync(typedInput, context, cancellationToken);
        }
        // 如果输入类型不匹配，可以选择忽略或抛出异常。在动态流中，可能需要更灵活的处理。
        throw new ArgumentException($"Expected input of type {typeof(TInput).Name}, but got {input?.GetType().Name} for executor {Id}");
    }

    protected abstract Task ProcessAsync(TInput input, IWorkflowContext context, CancellationToken cancellationToken);
}