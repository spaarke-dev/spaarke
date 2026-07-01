namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Opt-in interface for tool handlers that support per-token streaming.
/// Extends <see cref="IAnalysisToolHandler"/> — handlers that implement this
/// will stream tokens to the client as they arrive from Azure OpenAI, rather
/// than blocking until the full response is available.
/// </summary>
/// <remarks>
/// Follows the per-token streaming pattern used by playbook node executors invoked via
/// <see cref="IPlaybookOrchestrationService.ExecuteAsync"/> (the canonical AI invocation
/// surface per ADR-013). Non-streaming handlers continue to work unchanged via
/// <see cref="IAnalysisToolHandler.ExecuteAsync"/>.
/// </remarks>
public interface IStreamingAnalysisToolHandler : IAnalysisToolHandler
{
    /// <summary>
    /// Executes the tool analysis with per-token streaming.
    /// Yields <see cref="ToolStreamEvent.Token"/> for each token from the AI model,
    /// then yields <see cref="ToolStreamEvent.Completed"/> with the final parsed result.
    /// </summary>
    /// <param name="context">The execution context containing document text, analysis state, etc.</param>
    /// <param name="tool">The tool configuration from Dataverse including custom parameters.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Async stream of token events followed by a completion event.</returns>
    IAsyncEnumerable<ToolStreamEvent> StreamExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken);
}

/// <summary>
/// Events emitted during streaming tool execution.
/// </summary>
public abstract record ToolStreamEvent
{
    /// <summary>A single token from the AI model response.</summary>
    public sealed record Token(string Text) : ToolStreamEvent;

    /// <summary>Final event containing the parsed <see cref="ToolResult"/>.</summary>
    public sealed record Completed(ToolResult Result) : ToolStreamEvent;
}
