using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// Deterministic <see cref="ContextEnvelope"/> → prompt renderer (spaarke-ai-architecture-redesign-r2
/// task AIR2-053, FR-B-04). Turns an assembled envelope's fragment-bearing slices into prompt strings.
/// </summary>
/// <remarks>
/// <para>
/// <b>NO cache-key machinery, NO hit-rate plumbing</b> (operator ruling 2026-07-09 (g)): this renderer
/// keeps the FREE cache-stability discipline — deterministic slice ordering and no timestamps/GUIDs in the
/// stable-prefix output — because it doubles as regression pinning + eval reproducibility, but it builds no
/// cache feature. The determinism is proven by a cheap byte-equality assertion
/// (<c>ContextEnvelopeRendererTests</c>), not a cache mechanism.
/// </para>
/// <para>
/// <b>Not yet composed into dispatch prompts (053 scope boundary).</b> The dispatch executor
/// (<c>ActionRunner</c>) renders the OPERAND only; the envelope becomes feature-complete (slices populated)
/// but is not rendered into the dispatch prompt in this task. This renderer ships ready so adopting it is a
/// one-line composition change gated on 054 budget reconciliation + eval re-baseline (recorded deferral, not
/// a dead end). The interactive path continues to assemble its prompt strings from the SAME producers
/// (<see cref="ContextSliceProducers"/>) the envelope is built from, so no second render path exists.
/// </para>
/// </remarks>
public static class ContextEnvelopeRenderer
{
    /// <summary>
    /// The stable-prefix additions block: the User fragment then the Business fragment (host-identity +
    /// schema card), each included only when present, joined with a blank line in the canonical relative
    /// order (User before Business). Byte-stable across turns for the same inputs — no timestamp/GUID beyond
    /// the caller-supplied ids already embedded in the Business fragment. Returns <see cref="string.Empty"/>
    /// when neither fragment is present.
    /// </summary>
    public static string RenderStablePrefixAdditions(ContextEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var parts = new List<string>(2);

        var user = envelope.User?.Fragment;
        if (!string.IsNullOrEmpty(user))
        {
            parts.Add(user!);
        }

        var business = envelope.Business?.Fragment;
        if (!string.IsNullOrEmpty(business))
        {
            parts.Add(business!);
        }

        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// The environment SUFFIX: the Workspace fragment (the current-date directive). Placement as a prompt
    /// SUFFIX is deliberate and matches the shipped interactive layout — the date changes daily, so suffix
    /// placement preserves the long stable prefix across days (cross-day prefix caching). Returns
    /// <see cref="string.Empty"/> when the Workspace slice carries no fragment.
    /// </summary>
    public static string RenderEnvironmentSuffix(ContextEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope.Workspace?.Fragment ?? string.Empty;
    }

    /// <summary>
    /// The conversation system message (ADR-040 ledger facade) — delegates to
    /// <see cref="ConversationContextProducer.BuildLedgerOutputsContext"/> over the supplied ledger outputs.
    /// The envelope's <c>Memory.Conversation</c> references pin the SAME window this renders (identifiers
    /// only; the payloads live in the ledger, never in the envelope). Returns null when there are no outputs.
    /// </summary>
    public static string? RenderConversationSystemMessage(
        ContextEnvelope envelope, IReadOnlyList<SessionOutput>? outputs)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return ConversationContextProducer.BuildLedgerOutputsContext(outputs);
    }
}
