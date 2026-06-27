using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="IWorkspacePrefillAi"/> registered when the
/// compound AI kill-switch is OFF (<c>Analysis:Enabled=false</c> OR
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-032 + D-09 §2 L1. Throws <see cref="FeatureDisabledException"/>
/// synchronously BEFORE returning the <see cref="IAsyncEnumerable{T}"/> so the Workspace
/// pre-fill endpoint (<c>Services/Workspace/MatterPreFillService.cs</c> + downstream Create
/// Matter wizard) sees the exception before negotiating SSE / streaming headers and
/// converts to 503 ProblemDetails per ADR-018 + ADR-019. Returning an empty event stream
/// would silently render "no pre-fill suggestions" in the wizard, masking the kill-switch
/// state.
/// </para>
/// <para>
/// Stream-method pre-stream invariant: throwing synchronously (NOT yielding an error event
/// from an async iterator) is the contract — the wire endpoint MUST be able to distinguish
/// "feature disabled at startup" (503 ProblemDetails, no SSE body) from "feature enabled
/// but errored mid-stream" (200 SSE with error chunk).
/// </para>
/// <para>
/// Introduced 2026-06-04 by <c>bff-ai-architecture-audit-r1</c> Phase 4 Migration PR #1
/// (LATENT BUG #1 remediation per W4 §4.5 + DR-003).
/// </para>
/// </remarks>
public sealed class NullWorkspacePrefillAi : IWorkspacePrefillAi
{
    private const string ErrorCode = "ai.workspace-prefill.disabled";
    private const string DetailMessage =
        "Workspace pre-fill (Create Matter wizard) requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullWorkspacePrefillAi> _logger;

    public NullWorkspacePrefillAi(ILogger<NullWorkspacePrefillAi> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IAsyncEnumerable<PlaybookStreamEvent> ExecutePlaybookAsync(
        PlaybookRunRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "NullWorkspacePrefillAi.ExecutePlaybookAsync invoked while AI feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
