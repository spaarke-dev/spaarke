using Microsoft.AspNetCore.Http;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade for the Workspace "Create Matter" pre-fill flow. Wraps the playbook
/// orchestration call used to extract structured matter fields from uploaded documents.
/// </summary>
/// <remarks>
/// <para>
/// Per refined ADR-013 (2026-05-20), external CRUD code MUST consume AI through this
/// facade rather than injecting <see cref="IPlaybookOrchestrationService"/> directly.
/// The facade preserves the SSE-stream return shape because pre-fill needs the
/// node-level completion event to extract structured output + confidence.
/// </para>
/// <para>
/// Current consumer (Phase 1 inventory, 2026-05-24):
/// - <c>Services/Workspace/MatterPreFillService.cs</c> — Create-Matter wizard pre-fill
/// </para>
/// <para>
/// Unlike <see cref="IBriefingAi"/> / <see cref="IInvoiceAi"/>, this facade wraps
/// <see cref="IPlaybookOrchestrationService"/> rather than <see cref="IOpenAiClient"/> +
/// <see cref="IPlaybookService"/>: pre-fill is a multi-node playbook execution, not a
/// single chat completion. The boundary intent is identical (no AI-internal type leaks
/// into CRUD code) but the wrapped surface differs accordingly.
/// </para>
/// </remarks>
public interface IWorkspacePrefillAi
{
    /// <summary>
    /// Execute the matter pre-fill playbook against uploaded document text. Returns
    /// an SSE-style event stream; the caller consumes <c>NodeCompleted</c> events to
    /// extract structured pre-fill data + confidence and handles <c>RunFailed</c> for
    /// graceful degradation.
    /// </summary>
    /// <param name="request">Playbook execution request. Caller is responsible for
    /// setting <see cref="PlaybookRunRequest.PlaybookId"/>, <see cref="PlaybookRunRequest.Document"/>
    /// (with <c>ExtractedText</c>), and any <see cref="PlaybookRunRequest.Parameters"/>.</param>
    /// <param name="httpContext">HTTP context (required for OBO auth inside the orchestrator).</param>
    /// <param name="cancellationToken">Cancellation token. Pair with a timeout — pre-fill
    /// has a 45 s upper bound in the current MatterPreFillService impl.</param>
    /// <returns>Stream of <see cref="PlaybookStreamEvent"/> events.</returns>
    /// <remarks>
    /// Despite the facade name <c>IWorkspacePrefillAi</c>, this method is a generic playbook-execution
    /// wrapper used by workspace-domain consumers including AI summary (<c>WorkspaceAiService</c>),
    /// matter pre-fill, and project pre-fill. The facade name reflects its origin in the pre-fill flow;
    /// the method itself is playbook-agnostic.
    /// </remarks>
    IAsyncEnumerable<PlaybookStreamEvent> ExecutePlaybookAsync(
        PlaybookRunRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}
