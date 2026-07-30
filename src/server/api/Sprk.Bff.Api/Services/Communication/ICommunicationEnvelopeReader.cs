using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Thin test-seam (ADR-010 "interface as a testing seam only") over
/// <see cref="CommunicationService.ReconstructEnvelopeAsync"/>, so the Job B apply path (task 031) can be unit-
/// tested for its security-critical apply-time citation re-verification (NFR-06) without constructing the sealed
/// <see cref="CommunicationService"/> and its full sender/archiver graph.
/// <para>
/// <b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <c>ReconstructEnvelopeAsync</c> already exists on
/// the sealed <see cref="CommunicationService"/>, but the class is <c>sealed</c> and interface-less, so it is not a
/// mockable seam; ADR-038 favors boundary seams over heavy fakes and the citation negative case is non-negotiable
/// (NFR-06). (2) Extension — a zero-logic pass-through interface the existing service implements as-is; no second
/// reconstruction path. (3) Cost-of-doing-nothing — without it the apply-time citation re-verify cannot be tested
/// in isolation. Mirrors the <see cref="IImpersonatedCommunicationQuery"/> precedent.
/// </para>
/// </summary>
public interface ICommunicationEnvelopeReader
{
    /// <summary>
    /// Reconstructs the normalized message (+ association context) for a stored <c>sprk_communication</c> — the
    /// same reconstruction the association/suggest paths use.
    /// </summary>
    Task<(NormalizedMessage Message, AssociationContext Context)> ReconstructEnvelopeAsync(
        Guid communicationId, CancellationToken ct);
}
