using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Direction-agnostic enrichment orchestrator (ADR-045, FR-08). Invoked by BOTH the inbound
/// path (<see cref="IncomingCommunicationProcessor"/>) and the outbound send path
/// (<see cref="CommunicationService"/>) so received and sent communications get identical
/// treatment.
/// </summary>
/// <remarks>
/// <para>
/// Owns, IN ORDER: (1) association, (2) categorization, (3) AI analysis, (4) RAG indexing,
/// (5) Responsive-Intelligence trigger (assessment-event emission). Every step is BEST-EFFORT
/// and NON-FATAL (NFR-06): a failure in any step MUST NOT fail the send or the inbound-capture
/// path.
/// </para>
/// <para>
/// <b>Task 010 lands the entry point + wiring.</b> Several steps are staged (see the
/// implementation's per-step XML docs + the task-010 ESCALATIONS): the Association Engine over
/// the envelope lands in task 011; the assessment-event consumer lands in task 052 (W5, gated by
/// the 050 coordination gate with <c>spaarke-ai-architecture-redesign-r2</c>). The one substantive
/// new capability delivered in 010 is <b>outbound RAG indexing</b> (previously missing).
/// </para>
/// </remarks>
public interface ICommunicationEnrichmentService
{
    /// <summary>
    /// Enriches a communication record. Best-effort and non-fatal throughout.
    /// </summary>
    /// <param name="communicationId">The <c>sprk_communication</c> record id.</param>
    /// <param name="direction">Inbound or outbound.</param>
    /// <param name="message">The normalized envelope (see <see cref="NormalizedMessage"/>).</param>
    /// <param name="archivedDocumentId">
    /// The <c>sprk_document</c> id of the archived <c>.eml</c> (when archival occurred). Used by
    /// the RAG-indexing step to resolve the SPE drive/item + file name for the outbound half.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task EnrichAsync(
        Guid communicationId,
        CommunicationDirection direction,
        NormalizedMessage message,
        Guid? archivedDocumentId,
        CancellationToken ct);
}
