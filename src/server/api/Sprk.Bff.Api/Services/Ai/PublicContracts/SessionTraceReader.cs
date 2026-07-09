using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Production <see cref="ISessionTraceReader"/> — loads a session's stored ADR-040 ledger
/// markers via <see cref="ChatSessionManager"/> (Redis hot → Cosmos warm, the same read the
/// rest of the chat surface uses) and projects them into the <see cref="TraceEvent"/> v1
/// stream (AIR2-038 / FR-A1-09).
/// </summary>
/// <remarks>
/// <para>
/// <b>No new store (ADR-040)</b>: this reader is a pure projection over markers the session
/// already carries (<see cref="ChatSession.ToolChains"/> / <see cref="ChatSession.Gates"/> /
/// <see cref="ChatSession.ContextFingerprints"/>). It performs no ledger write and no mutation —
/// reads are free (D-F0(b)) and store-before-render is untouched (this is the render-half read).
/// </para>
/// <para>
/// <b>Facade boundary (ADR-013)</b>: the ledger record types stay internal to the projection;
/// callers receive only <see cref="TraceEvent"/>. The one new PublicContract input,
/// <see cref="TraceContextFingerprint"/>, is materialized here from the stored
/// <see cref="SessionContextFingerprint"/> entries.
/// </para>
/// </remarks>
public sealed class SessionTraceReader : ISessionTraceReader
{
    private readonly ChatSessionManager _sessionManager;

    public SessionTraceReader(ChatSessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TraceEvent>> ReadTraceAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = await _sessionManager
            .GetSessionAsync(tenantId, sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            // A trace read of an unknown/expired session is empty, not an error — the
            // view renders an empty timeline rather than surfacing a 404 for a read.
            return Array.Empty<TraceEvent>();
        }

        return ProjectTrace(session.ToolChains, session.Gates, session.ContextFingerprints);
    }

    /// <summary>
    /// Pure projection over a session's stored ledger markers → ordered <see cref="TraceEvent"/>
    /// v1 stream. Separated (and internal) so the projection is unit-testable without a live
    /// session store. Maps the stored <see cref="SessionContextFingerprint"/> entries to the
    /// <see cref="TraceContextFingerprint"/> PublicContract input, then defers ordering/sequencing
    /// to <see cref="TraceEventProjection.Project"/>.
    /// </summary>
    internal static IReadOnlyList<TraceEvent> ProjectTrace(
        IReadOnlyList<SessionToolChain>? toolChains,
        IReadOnlyList<SessionGate>? gates,
        IReadOnlyList<SessionContextFingerprint>? contextFingerprints)
    {
        var fingerprints = contextFingerprints is not { Count: > 0 }
            ? null
            : contextFingerprints
                .Select(f => new TraceContextFingerprint
                {
                    Turn = f.Turn,
                    FingerprintId = f.FingerprintId,
                    SliceCount = f.SliceCount,
                    CreatedAt = f.CreatedAt,
                })
                .ToList();

        return TraceEventProjection.Project(fingerprints, toolChains, gates);
    }
}

/// <summary>
/// Null-Object <see cref="ISessionTraceReader"/> (ADR-032 P2 quiet no-op) — returns an empty
/// trace stream. Registered when the AI chat surface is not active so the unconditionally-mapped
/// trace endpoint resolves its handler dependency cleanly (§F.1 asymmetric-registration rule)
/// and returns an empty 200 rather than 500. No content, no store, no throw.
/// </summary>
public sealed class NullSessionTraceReader : ISessionTraceReader
{
    /// <inheritdoc />
    public Task<IReadOnlyList<TraceEvent>> ReadTraceAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TraceEvent>>(Array.Empty<TraceEvent>());
}
