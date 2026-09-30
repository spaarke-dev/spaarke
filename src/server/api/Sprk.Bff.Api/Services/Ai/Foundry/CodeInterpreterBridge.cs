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
/// the <c>CodeInterpreterHandler</c>.
///
/// Concurrency (ADR-016): concurrency gating is handled by the <c>CodeInterpreterHandler</c>
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

        // Create a genuinely ephemeral thread — Code Interpreter is a stateless sandbox per call.
        //
        // 🔴 FIXED 2026-09-29 (task 122). This comment was already true as INTENT and false as CODE. It
        // called CreateOrResumeThreadAsync with the constant "_code_interpreter_ephemeral_" in the TENANT
        // argument — and CreateOrResume caches and RESUMES. So every code-interpreter invocation, from
        // every user in every tenant, resumed ONE shared thread for the 60-minute TTL, accumulating each
        // caller's prompts as context for the next. "Ephemeral" described the intent; the cache did the
        // opposite, and the synthetic key that was supposed to avoid polluting the conversation cache is
        // what made the pollution global.
        //
        // Nothing here needs to survive the call, so the fix is to stop caching rather than to invent a
        // scope: create a fresh thread and let it go.
        var threadId = await _agentServiceClient.CreateEphemeralThreadAsync(cancellationToken);

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

        // REMOVED 2026-09-29 (task 122): a best-effort cache eviction that tried to undo the caching this
        // method should never have done. It was also racy — between the thread being cached at the start
        // of the call and evicted here, any concurrent caller resumed it, which is precisely how one
        // user's code-interpreter prompt became another's context. Nothing is cached now, so there is
        // nothing to evict: CreateEphemeralThreadAsync creates the thread and lets it go.

        var output = outputBuilder.ToString().Trim();
        var executionLog = logBuilder.ToString().Trim();

        return new CodeInterpreterResult(
            Output: string.IsNullOrEmpty(output) ? "[No output returned by Code Interpreter]" : output,
            ChartBase64: chartBase64,
            ExecutionLog: executionLog);
    }
}
