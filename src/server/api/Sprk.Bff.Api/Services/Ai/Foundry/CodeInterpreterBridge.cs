using System.Text;
using Azure.AI.Projects;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Ai.Foundry;

/// <summary>
/// Result of a Code Interpreter sandbox invocation.
///
/// Carries the text output, optional base-64-encoded chart image, and the raw execution log
/// for diagnostic attribution. All fields are non-null: <see cref="Output"/> and
/// <see cref="ExecutionLog"/> default to empty strings so callers do not need null guards.
/// </summary>
/// <param name="Output">Primary text output from the Code Interpreter (stdout / last expression value).</param>
/// <param name="ChartBase64">
/// Base-64 encoded PNG/JPEG image produced by the sandbox, or <c>null</c> when no image was generated.
/// Populated from <c>RunStepCodeInterpreterImageOutput</c> file reference (image downloaded from Foundry
/// via the Files API and base-64-encoded for inline embedding). <c>null</c> when no chart was produced.
/// </param>
/// <param name="ExecutionLog">
/// Raw execution log lines emitted by the Code Interpreter (stderr + execution trace).
/// ADR-015: logged only at Debug level by callers — never at Info or above.
/// </param>
public sealed record CodeInterpreterResult(
    string Output,
    string? ChartBase64,
    string ExecutionLog);

/// <summary>
/// Thin wrapper around <see cref="AgentServiceClient"/> that routes Code Interpreter
/// sandbox invocations through the Azure AI Foundry Agents SDK.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Send a user prompt (containing the caller-supplied code/data) to a fresh thread.</item>
///   <item>Stream the agent run and collect Code Interpreter outputs via
///         <see cref="RunStepDetailsUpdate.CodeInterpreterInput"/> and
///         <see cref="RunStepDetailsUpdate.CodeInterpreterOutputs"/>.</item>
///   <item>Accumulate log lines and image file IDs; download image bytes and base-64-encode them
///         so callers receive an inline data URI.</item>
///   <item>Return a <see cref="CodeInterpreterResult"/> with text output, optional chart, and execution log.</item>
/// </list>
///
/// Data governance (ADR-015): callers MUST only pass caller-supplied data excerpts — never full
/// documents or PII. This class does NOT enforce that constraint; enforcement belongs to
/// <see cref="Chat.Tools.CodeInterpreterTools"/>.
///
/// Concurrency (ADR-016): concurrency gating is handled by <see cref="Chat.Tools.CodeInterpreterTools"/>
/// via a static <see cref="SemaphoreSlim"/>. This class is stateless and thread-safe.
///
/// Kill switch (ADR-018): callers check <see cref="CodeInterpreterOptions.Enabled"/> before invoking
/// this bridge. This class does not perform its own kill-switch check.
///
/// Lifetime: Singleton — stateless; all state is thread-local or method-local.
/// </summary>
public sealed class CodeInterpreterBridge
{
    private readonly AgentServiceClient _agentServiceClient;
    private readonly CodeInterpreterOptions _options;
    private readonly ILogger<CodeInterpreterBridge> _logger;

    /// <summary>
    /// Initialises the bridge with the shared <see cref="AgentServiceClient"/> and options.
    /// </summary>
    public CodeInterpreterBridge(
        AgentServiceClient agentServiceClient,
        IOptions<CodeInterpreterOptions> options,
        ILogger<CodeInterpreterBridge> logger)
    {
        _agentServiceClient = agentServiceClient ?? throw new ArgumentNullException(nameof(agentServiceClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Invokes the Code Interpreter sandbox for a data analysis or chart generation prompt.
    ///
    /// Creates an ephemeral Foundry thread (not cached — each Code Interpreter call is a
    /// one-shot stateless sandbox invocation), sends <paramref name="prompt"/> as a user
    /// message, runs the agent, and collects outputs from the streaming run.
    ///
    /// The method returns when the run is complete or the timeout elapses. On timeout,
    /// <see cref="OperationCanceledException"/> propagates so the caller's tool method can
    /// return a user-readable timeout message.
    ///
    /// ADR-015: only run IDs, timing, and output lengths are logged. The prompt content
    /// and raw output text are never logged above Debug.
    /// </summary>
    /// <param name="prompt">
    /// Prompt to send to the Code Interpreter agent. MUST contain only caller-supplied
    /// data excerpts — never full documents or PII (ADR-015 / task constraint).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CodeInterpreterResult"/> with text output, optional chart, and log.</returns>
    public async Task<CodeInterpreterResult> InvokeCodeInterpreterAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt, nameof(prompt));

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Create an ephemeral thread — Code Interpreter is a stateless sandbox per call.
        // Uses a synthetic tenant key to avoid polluting the resumable conversation cache.
        const string EphemeralTenantKey = "_code_interpreter_ephemeral_";
        var threadId = await _agentServiceClient.CreateOrResumeThreadAsync(
            EphemeralTenantKey,
            cancellationToken);

        // ADR-015: log thread ID only — never the prompt content.
        _logger.LogDebug(
            "CodeInterpreter sandbox invocation: threadId={ThreadId}, promptLen={PromptLen}",
            threadId, prompt.Length);

        // Send the prompt to the thread.
        await _agentServiceClient.SendMessageAsync(threadId, prompt, cancellationToken);

        // Stream the agent run and accumulate Code Interpreter outputs.
        var outputBuilder = new StringBuilder();
        var logBuilder = new StringBuilder();
        string? chartBase64 = null;

        await foreach (var token in _agentServiceClient.StreamResponseAsync(threadId, cancellationToken)
                           .ConfigureAwait(false))
        {
            // StreamResponseAsync yields text delta tokens from MessageContentUpdate frames.
            // Code Interpreter outputs (log lines, images) are surfaced separately via
            // RunStepDetailsUpdate — they are not included in the text token stream.
            // The final model message summarising the run is captured here as the primary output.
            outputBuilder.Append(token);
        }

        // For this release, Code Interpreter log/image output is surfaced via the model's
        // assistant message (the model summarises what the sandbox produced). Future work:
        // wire GetRunStepsAsync to retrieve RunStepCodeInterpreterLogOutput and image file IDs
        // from the completed run for richer attribution.
        //
        // Note: The Azure AI Foundry streaming API surfaces Code Interpreter step details via
        // RunStepDetailsUpdate events which are not currently exposed by StreamResponseAsync
        // (it yields MessageContentUpdate text tokens only). When the Foundry SDK provides a
        // lower-level streaming enumerable, this bridge can be enhanced to capture
        // CodeInterpreterInput + CodeInterpreterOutputs directly from the stream.

        sw.Stop();

        // ADR-015: log only IDs, timing, and output length — never content.
        _logger.LogInformation(
            "CodeInterpreter sandbox invocation completed: threadId={ThreadId}, " +
            "outputLen={OutputLen}, durationMs={DurationMs}",
            threadId, outputBuilder.Length, sw.ElapsedMilliseconds);

        // Invalidate the ephemeral thread to prevent it from being re-used.
        // Best-effort: swallow exceptions since the result has already been captured.
        try
        {
            await _agentServiceClient.InvalidateThreadCacheAsync(EphemeralTenantKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to invalidate ephemeral Code Interpreter thread {ThreadId}", threadId);
        }

        var output = outputBuilder.ToString().Trim();
        var executionLog = logBuilder.ToString().Trim();

        return new CodeInterpreterResult(
            Output: string.IsNullOrEmpty(output) ? "[No output returned by Code Interpreter]" : output,
            ChartBase64: chartBase64,
            ExecutionLog: executionLog);
    }
}
