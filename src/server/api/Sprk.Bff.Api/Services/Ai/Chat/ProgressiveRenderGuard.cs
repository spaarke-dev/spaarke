using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Render-boundary guard for progressive rendering of dispatched capability outputs
/// (spaarke-ai-architecture-redesign-r2 task 039, FR-A1-10 / D-F5).
///
/// <para>
/// ADR-040 requires storage to precede rendering — every rendered chunk (progressive or
/// terminal) MUST correspond to already-stored ledger content. <see cref="OutputRouter"/>
/// already enforces the write-then-return ordering (the ledger write is awaited to
/// completion BEFORE <c>RouteAsync</c> returns the <see cref="RoutedOutput"/>), but that
/// ordering lived only in prose/xmldoc — no code path made "was this actually stored?"
/// load-bearing at the render call site. <see cref="EnsureStored"/> closes that gap: it is
/// the explicit assertion <see cref="SessionDispatchOrchestrator.DispatchAsync"/> calls
/// immediately before turning a <see cref="RoutedOutput.Entry"/> into the terminal
/// (or, for a future section-keyed emitter, per-section) render chunk.
/// </para>
/// <para>
/// <b>What the guard checks</b>: <see cref="SessionOutput.CreatedAt"/> is set by
/// <see cref="OutputRouter.RouteAsync"/> at construction time, immediately before the
/// ledger write — it is never set by any other production code path. An entry whose
/// <see cref="SessionOutput.CreatedAt"/> is <c>default</c> (unset) did NOT come from a
/// completed <c>RouteAsync</c> call and therefore carries no evidence of having been
/// written to the ledger. Rendering from such an entry would be a render-ahead-of-store
/// violation (ADR-040) — <see cref="EnsureStored"/> fails loudly instead of silently
/// rendering it.
/// </para>
/// <para>
/// This is intentionally a narrow, cheap check (not a full ledger re-read) — the seam
/// that actually enforces the write is <see cref="OutputRouter"/>; this guard is the
/// render-side trip-wire that makes a future refactor which accidentally renders from a
/// pre-store <c>JsonElement</c> (instead of the routed, stored <see cref="SessionOutput"/>)
/// fail a test instead of silently shipping.
/// </para>
/// </summary>
public static class ProgressiveRenderGuard
{
    /// <summary>
    /// Asserts <paramref name="entry"/> was written to the session ledger via
    /// <see cref="OutputRouter.RouteAsync"/> before returning it unchanged. Callers pass the
    /// returned entry straight to whatever builds the render chunk(s) — this method never
    /// mutates the payload, it only gates on provenance.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="entry"/> carries no evidence of a completed ledger write
    /// (<see cref="SessionOutput.CreatedAt"/> is <c>default</c>) — rendering it would violate
    /// ADR-040 storage-precedes-rendering.
    /// </exception>
    public static SessionOutput EnsureStored(SessionOutput entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.CreatedAt == default)
        {
            throw new InvalidOperationException(
                $"ProgressiveRenderGuard: attempted to render SessionOutput '{entry.Key}' before it was " +
                "written to the ledger (CreatedAt is unset). ADR-040 requires storage to precede rendering — " +
                "only render from the SessionOutput returned by IOutputRouter.RouteAsync, never from a " +
                "pre-store JsonElement or a hand-built entry.");
        }

        return entry;
    }
}
