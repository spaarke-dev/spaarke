namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade over the server-side decision-traceability READ surface
/// (AIR2-038 / FR-A1-09, design D-F4). It projects a session's stored ADR-040 ledger
/// markers — <c>SessionToolChain</c>, <c>SessionGate</c>, and the AIR2-038
/// <c>SessionContextFingerprint</c> entries — into the versioned, tolerant-reader
/// <see cref="TraceEvent"/> v1 stream (task 013). This is the endpoint that lets a
/// decision-traceability view survive a hard refresh: the client 50-entry
/// <c>executionTraceBuffer</c> is per-page-load and loses in-flight trace on reload
/// (its own documented mount-gap); this surface rehydrates the trace from the durable
/// ledger instead.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-013 (facade boundary)</b>: any CRUD/satellite consumer that needs the decision
/// trace reaches it through THIS facade — it returns only the <see cref="TraceEvent"/>
/// PublicContract shape and never leaks ledger internals (<c>SessionToolChain</c> /
/// <c>SessionGate</c> / <c>ChatSession</c>) to callers. Mirrors the canonical facade
/// pattern (<see cref="IBriefingAi"/>, ADR-007 <c>SpeFileStore</c>).
/// </para>
/// <para>
/// <b>ADR-040 (read projection — NOT a store)</b>: there is deliberately no parallel trace
/// store. The ledger remains the single source of truth; the stream is materialized on
/// demand by <see cref="TraceEventProjection.Project"/> from markers loaded via the session
/// API. Reads are free (D-F0(b)) — no side effects, no ledger mutation, store-before-render
/// intact.
/// </para>
/// <para>
/// <b>NFR-07 (no-content telemetry)</b>: every projected <see cref="TraceEvent"/> carries
/// identifiers/filters/counts only; <see cref="TraceEventContract.CarriesOnlySanctionedFields"/>
/// is the emission guard.
/// </para>
/// </remarks>
public interface ISessionTraceReader
{
    /// <summary>
    /// Reads the decision-traceability stream for a session as an ordered
    /// <see cref="TraceEvent"/> v1 sequence (context → tool_chain → tool_call → gate,
    /// ordered by <see cref="TraceEvent.Sequence"/>). Returns an empty list when the
    /// session does not exist or has no ledger markers yet — never null, never throws
    /// for a missing session (a trace read of an unknown session is simply empty).
    /// </summary>
    /// <param name="tenantId">Tenant the session belongs to (ADR-014 tenant scoping).</param>
    /// <param name="sessionId">Session whose ledger markers to project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ordered, sequence-stamped trace stream; empty when nothing to project.</returns>
    Task<IReadOnlyList<TraceEvent>> ReadTraceAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default);
}
