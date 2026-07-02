using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="IInvokePlaybookAi"/> registered when the
/// compound AI kill-switch is OFF (<c>Analysis:Enabled=false</c> OR
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>P3 Fail-Fast pattern per ADR-032 + D-09 §2 L1</b>. Throws
/// <see cref="FeatureDisabledException"/> on the public method so that consumer code
/// (chat-tool adapter in task 021; M365 Copilot agent gateway; future R7+ consumers)
/// converts to a chat-tool error / 503 ProblemDetails per ADR-018 + ADR-019. Returning
/// an empty success result would silently mask the kill-switch state and mislead operators
/// / observability — fail-fast is the correct semantic per ADR-032.
/// </para>
/// <para>
/// <b>Why P3 (Fail-fast) and not P2 (Quiet)</b>: <see cref="IInvokePlaybookAi"/> is a
/// command surface — callers expect playbook execution, not a no-op. A quiet success
/// would silently route every LLM <c>invoke_playbook</c> tool call to a fake "no output"
/// response and break trust in the tool ecosystem. Same reasoning as
/// <see cref="NullPlaybookOrchestrationService"/>, <see cref="NullBriefingAi"/>, and
/// <see cref="NullInsightsAi"/>.
/// </para>
/// <para>
/// <b>Stable error code</b>: <c>ai.playbook-invocation.disabled</c>. Clients SHOULD switch
/// on this code for feature-specific UX. Stable across releases per the
/// <see cref="FeatureDisabledException.ErrorCode"/> contract.
/// </para>
/// <para>
/// Logger is injected for telemetry on disabled-feature invocation attempts; logged at
/// <c>Debug</c> level only because hitting a kill-switched feature is expected behavior
/// when test fixtures or operations set the gate OFF.
/// </para>
/// </remarks>
public sealed class NullInvokePlaybookAi : IInvokePlaybookAi
{
    /// <summary>
    /// Stable feature-key identifier — clients SHOULD switch on this string.
    /// </summary>
    // Stable errorCode per spaarke-insights-engine-r2 audit cross-project request
    // (2026-06-08): aligned to `ai.<tool-name>.disabled` convention to match the LLM
    // tool name `invoke_playbook`. Clients switch on this string.
    public const string ErrorCode = "ai.invoke-playbook.disabled";

    private const string DetailMessage =
        "Playbook invocation requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullInvokePlaybookAi> _logger;

    public NullInvokePlaybookAi(ILogger<NullInvokePlaybookAi> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<PlaybookInvocationResult> InvokePlaybookAsync(
        Guid playbookId,
        IReadOnlyDictionary<string, string>? parameters,
        PlaybookInvocationContext context,
        CancellationToken cancellationToken = default,
        string? userContext = null,
        Sprk.Bff.Api.Services.Ai.DocumentContext? document = null)
    {
        // Widened surface per task 095. The kill-switch semantics are unchanged: any
        // invocation attempt while the AI feature is disabled fails fast regardless of
        // whether document context was supplied. Failing before touching the widened
        // args keeps the ADR-032 P3 contract intact for Phase 1 + Phase 2 consumers alike.
        _logger.LogDebug(
            "NullInvokePlaybookAi.InvokePlaybookAsync invoked while AI feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
