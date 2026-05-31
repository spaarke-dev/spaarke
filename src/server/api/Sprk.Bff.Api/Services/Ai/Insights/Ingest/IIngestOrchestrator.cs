using Sprk.Bff.Api.Models.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Insights.Ingest;

/// <summary>
/// D-P7 — the universal ingest orchestrator. Composes the Phase 1 layered extraction
/// pipeline (Sanitizer → Layer 1 → conditional Layer 2 → mechanical gates → emission
/// → substrate write → mirror) for a single SPE-uploaded document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a code-defined orchestrator (NOT a Dataverse playbook entity)</b>: per D-54
/// "questions-as-playbooks" — the universal ingest IS a playbook, but in Phase 1 it's
/// realized as a deterministic Zone A code sequence rather than a row in
/// <c>sprk_analysisplaybook</c>. Rationale:
/// <list type="bullet">
///   <item>The ingest sequence is fixed (it runs the same nodes in the same order on
///   every document); the configurability that drives Dataverse-playbook-as-data
///   doesn't apply to ingest.</item>
///   <item>The existing Layer 1 + Layer 2 primitives (tasks 030 + 031) ship as JSON
///   node configs + emitter code, not Dataverse playbook rows; the orchestrator wires
///   them directly — same pattern, scaled up.</item>
///   <item>The JSON spec file <c>universal-ingest.v1.json</c> sits alongside
///   <c>layer1-classification.node.json</c> + <c>layer2-outcome-extraction.node.json</c>
///   in <c>Services/Ai/Insights/Playbooks/</c>, documenting the contract for external
///   readers without forcing a Dataverse round-trip on every ingest.</item>
/// </list>
/// </para>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/Ingest/</c>. The Zone B
/// facade (<see cref="PublicContracts.IInsightsAi.RunIngestAsync"/>) delegates to this
/// orchestrator; Zone B never imports this interface directly.
/// </para>
/// </remarks>
public interface IIngestOrchestrator
{
    /// <summary>
    /// Run the universal ingest pipeline for a single document. Returns the count of
    /// Observations emitted + Layer 1 classification + whether Layer 2 was triggered.
    /// </summary>
    /// <param name="request">Document + matter + tenant identifiers. Required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The aggregate result reported to the D-P8 SPE-upload consumer (task 050).
    /// On documents not found in <c>spaarke-files-index</c> (e.g., zero-byte upload),
    /// returns an empty result (ObservationsEmitted=0, Layer1Classification=null,
    /// Layer2Triggered=false) rather than throwing — non-indexable uploads are
    /// expected, not errors.</returns>
    Task<InsightsIngestResult> RunAsync(
        InsightsIngestRequest request,
        CancellationToken ct);
}
