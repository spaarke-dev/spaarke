using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-C02 + FR-C03 (spaarkeai-compose-r8 task 051) — the ONE place a proposed compose edit's TARGET
/// becomes a concrete <c>w14:paraId</c>. Two anchor sources, one deterministic path, zero text search:
/// <list type="bullet">
///   <item><b>FR-C03 — an explicit paraId</b> (client-captured at selection time per FR-C01, or returned
///   by the model from the enumerated closed set) is VALIDATED FOR MEMBERSHIP in that closed set. An id
///   outside the set is rejected loudly (<see cref="ComposeAnchorStatus.UnknownParaId"/>) — never
///   repaired, never approximated, never fallen back to a search.</item>
///   <item><b>FR-C02 — a legal citation</b> ("clause 4.2", "4.2(b)(iii)", "Sections 4–7") is resolved by
///   <see cref="CitationResolver"/> against the SAME closed set, reading the numbering engine's
///   already-computed <c>ComputedNumber</c>/<c>ListPath</c> chain. This is that resolver's first
///   consumer in the BFF outside its own file (spec FR-C02 acceptance criterion).</item>
/// </list>
///
/// <para>
/// <b>Why this exists / CLAUDE.md §11.</b> (1) <i>Existing</i> — <see cref="CitationResolver"/> resolves a
/// citation string but has no notion of an explicit paraId, a closed set, or a rejection outcome;
/// <see cref="ComposeEditValidator"/> resolves by TEXT SEARCH over document prose, which is the mechanism
/// FR-C04 (task 052) retires. Neither answers "what paragraph does this edit target, and may I trust it".
/// (2) <i>Extension</i> — extending <c>CitationResolver</c> was rejected: it is a pure citation↔paraId
/// function used by three call sites with no closed-set concept, and pushing admission policy into it
/// would couple string parsing to edit-envelope trust. This type composes it instead. (3)
/// <i>Cost-of-doing-nothing</i> — without a single resolution point, each anchor source grows its own
/// fallback, and task 052's deletion of the text-search path silently breaks whichever source was missed.
/// </para>
///
/// <para>
/// <b>Pure (ADR-007/013).</b> No I/O, no DI, no AI-internal type — string/ordinal matching over an
/// in-memory reference map, mirroring <see cref="CitationResolver"/>'s own contract. The caller supplies
/// the closed set (the session's <see cref="ChatSession.ReferenceMap"/> or the projection's
/// <c>ParaIdMap</c>); this type never fetches one, so it cannot resolve against a map the caller did not
/// authorize. Total — never throws for any input.
/// </para>
///
/// <para>
/// <b>Invariant 3 (one coordinate system).</b> Both branches resolve through the projection's reference
/// map and nothing else. There is deliberately NO text-matching branch here, not even as a fallback: an
/// edit with no anchor returns <see cref="ComposeAnchorStatus.NoAnchor"/> and the CALLER decides what to
/// do with it. Task 052 removes the caller's remaining text-search fallback; this type never had one.
/// </para>
/// </summary>
public static class ComposeAnchorResolver
{
    /// <summary>
    /// Resolves an edit's anchor against the session-ledger closed set
    /// (<see cref="ChatSession.ReferenceMap"/>). See <see cref="ComposeAnchorStatus"/> for outcomes.
    /// </summary>
    public static ComposeAnchorResolution Resolve(
        string? targetParaId,
        string? targetRef,
        IReadOnlyList<ParaReferenceMapEntry>? closedSet)
        => ResolveCore(
            targetParaId,
            targetRef,
            closedSet?.Select(e => e.ParaId).ToArray() ?? Array.Empty<string>(),
            citation => CitationResolver.Resolve(citation, closedSet!));

    /// <summary>
    /// Resolves an edit's anchor against the projection-payload closed set
    /// (<c>ComposeDocxProjection.ParaIdMap</c>). See <see cref="ComposeAnchorStatus"/> for outcomes.
    /// </summary>
    public static ComposeAnchorResolution Resolve(
        string? targetParaId,
        string? targetRef,
        IReadOnlyList<ParaIdMapEntry>? closedSet)
        => ResolveCore(
            targetParaId,
            targetRef,
            closedSet?.Select(e => e.ParaId).ToArray() ?? Array.Empty<string>(),
            citation => CitationResolver.Resolve(citation, closedSet!));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // Core
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private static ComposeAnchorResolution ResolveCore(
        string? targetParaId,
        string? targetRef,
        IReadOnlyList<string> closedSetIds,
        Func<string, CitationResolution> resolveCitation)
    {
        var hasParaId = !string.IsNullOrWhiteSpace(targetParaId);
        var hasRef = !string.IsNullOrWhiteSpace(targetRef);

        // No anchor at all: NOT a rejection — the caller (today) may still have a legacy text path.
        // Reported distinctly so task 052 can assert the legacy path is unreachable once it lands.
        if (!hasParaId && !hasRef)
        {
            return ComposeAnchorResolution.NoAnchor();
        }

        // An anchor was supplied but there is nothing to validate it against. Rejecting is the only
        // honest answer: accepting an unvalidated id would defeat the closed set's entire purpose.
        if (closedSetIds.Count == 0)
        {
            return ComposeAnchorResolution.Rejected(
                ComposeAnchorStatus.EmptyClosedSet,
                "No reference map was available to validate the anchor against. The document must be "
                + "loaded (which builds the paraId reference map) before an anchored edit can be resolved.");
        }

        string? paraIdMatch = null;
        if (hasParaId)
        {
            // Return the CANONICAL id from the closed set, never the caller's casing — downstream
            // comparisons are ordinal, so echoing a differently-cased id would fail to match later.
            paraIdMatch = FindCanonical(targetParaId!, closedSetIds);
            if (paraIdMatch is null)
            {
                return ComposeAnchorResolution.Rejected(
                    ComposeAnchorStatus.UnknownParaId,
                    $"Target paraId '{Trim(targetParaId!)}' is not in this document's reference map "
                    + $"({closedSetIds.Count} paragraph(s)). It was neither repaired nor searched for.");
            }
        }

        string? refMatch = null;
        if (hasRef)
        {
            var resolution = resolveCitation(targetRef!);

            if (resolution.Matches.Count == 0)
            {
                return ComposeAnchorResolution.Rejected(
                    ComposeAnchorStatus.UnresolvedReference,
                    $"Reference '{Trim(targetRef!)}' did not resolve to any paragraph in this document's "
                    + "numbering. No nearest-match guess was made.");
            }

            // A single edit addresses ONE paragraph. A citation that names several (a range such as
            // "Sections 4-7", or a genuinely ambiguous sub-item) is rejected rather than narrowed —
            // picking the first would be exactly the silently-wrong-target failure FR-C02 removes.
            if (resolution.Matches.Count > 1)
            {
                return ComposeAnchorResolution.Rejected(
                    ComposeAnchorStatus.AmbiguousReference,
                    $"Reference '{Trim(targetRef!)}' resolved to {resolution.Matches.Count} paragraphs "
                    + $"({string.Join(", ", resolution.ParaIds.Take(5))}). Name a single clause.",
                    resolution.ParaIds);
            }

            refMatch = resolution.ParaIds[0];
        }

        // Both anchors supplied and disagreeing is a real signal that the edit was built against a
        // different document state. Reject; do not silently prefer one source over the other.
        if (paraIdMatch is not null && refMatch is not null
            && !string.Equals(paraIdMatch, refMatch, StringComparison.OrdinalIgnoreCase))
        {
            return ComposeAnchorResolution.Rejected(
                ComposeAnchorStatus.ConflictingAnchors,
                $"Target paraId '{paraIdMatch}' and reference '{Trim(targetRef!)}' (which resolves to "
                + $"'{refMatch}') name different paragraphs. Neither was preferred.");
        }

        return ComposeAnchorResolution.Resolved(paraIdMatch ?? refMatch!);
    }

    /// <summary>
    /// Membership test over the closed set, ordinal-case-insensitive (paraIds are 8-hex; a model or an
    /// older client may echo lower case). Returns the set's OWN spelling of the id, or null.
    /// </summary>
    private static string? FindCanonical(string candidate, IReadOnlyList<string> closedSetIds)
    {
        var probe = candidate.Trim();
        for (var i = 0; i < closedSetIds.Count; i++)
        {
            if (string.Equals(closedSetIds[i], probe, StringComparison.OrdinalIgnoreCase))
            {
                return closedSetIds[i];
            }
        }

        return null;
    }

    /// <summary>Bounds an echoed caller value so a hostile/oversized string cannot bloat a log line or a message.</summary>
    private static string Trim(string value)
    {
        var s = value.Trim();
        return s.Length <= 64 ? s : s[..64] + "…";
    }
}

/// <summary>The outcome kinds of <see cref="ComposeAnchorResolver.Resolve(string?, string?, IReadOnlyList{ParaReferenceMapEntry})"/>.</summary>
public enum ComposeAnchorStatus
{
    /// <summary>Exactly one paragraph was named deterministically. <see cref="ComposeAnchorResolution.ParaId"/> is non-null.</summary>
    Resolved,

    /// <summary>Neither a paraId nor a reference was supplied. NOT a rejection — the caller decides (task 052 makes this terminal).</summary>
    NoAnchor,

    /// <summary>FR-C03 — the supplied paraId is not a member of the closed set. Rejected loudly; never guessed.</summary>
    UnknownParaId,

    /// <summary>FR-C02 — the citation parsed but names no paragraph in this document's numbering.</summary>
    UnresolvedReference,

    /// <summary>FR-C02 — the citation names more than one paragraph; a single edit cannot address a range.</summary>
    AmbiguousReference,

    /// <summary>Both anchors were supplied and they name different paragraphs.</summary>
    ConflictingAnchors,

    /// <summary>An anchor was supplied but no reference map was available to validate it against.</summary>
    EmptyClosedSet,
}

/// <summary>
/// The result of resolving one edit's anchor: a single <see cref="ParaId"/> on success, or a
/// <see cref="ComposeAnchorStatus"/> plus a human-readable <see cref="Reason"/> on rejection. Pure data.
/// </summary>
public sealed record ComposeAnchorResolution(
    ComposeAnchorStatus Status,
    string? ParaId,
    string? Reason,
    IReadOnlyList<string> Candidates)
{
    /// <summary>True when exactly one paragraph was named. <see cref="ParaId"/> is non-null iff this is true.</summary>
    public bool IsResolved => Status == ComposeAnchorStatus.Resolved;

    /// <summary>True when an anchor WAS supplied and was refused. Distinct from <see cref="ComposeAnchorStatus.NoAnchor"/>.</summary>
    public bool IsRejected => Status is not ComposeAnchorStatus.Resolved and not ComposeAnchorStatus.NoAnchor;

    internal static ComposeAnchorResolution Resolved(string paraId)
        => new(ComposeAnchorStatus.Resolved, paraId, null, Array.Empty<string>());

    internal static ComposeAnchorResolution NoAnchor()
        => new(ComposeAnchorStatus.NoAnchor, null, null, Array.Empty<string>());

    internal static ComposeAnchorResolution Rejected(
        ComposeAnchorStatus status, string reason, IReadOnlyList<string>? candidates = null)
        => new(status, null, reason, candidates ?? Array.Empty<string>());
}
