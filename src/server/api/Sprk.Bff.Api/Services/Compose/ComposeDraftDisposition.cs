using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-04 <b>draft-into-editor</b> consumer of the core <see cref="ComposeDisposition"/> v1
/// A0 seam (spaarkeai-compose-r2 task 016). This is the Compose-owned PAYLOAD ↔ ENVELOPE
/// bridge: the core owns the envelope (the <c>compose</c> disposition member, the SSE frame,
/// supersession keying — all in <see cref="ComposeDisposition"/>); Compose owns the opaque
/// structured-edit payload (<see cref="ComposeDraftPayload"/>) that rides inside
/// <see cref="SessionOutput.Payload"/>.
///
/// <para>
/// <b>ADR-040 (store-before-render)</b>: this type NEVER stores. It builds the
/// <c>compose</c>-disposition <see cref="SessionOutput"/> that the universal
/// <see cref="Ai.IOutputRouter"/> writes to the session ledger BEFORE any render (the router
/// is the single write seam — a second store would violate ADR-040). Frames are only ever
/// derived FROM an already-stored entry (<see cref="BuildFrame"/> → <see cref="ComposeDisposition.BuildFrame"/>),
/// and materialization re-reads the stored entry (<see cref="MaterializeDraft"/> →
/// <see cref="ComposeDisposition.Materialize"/>, which throws when the referenced entry is absent).
/// </para>
///
/// <para>
/// <b>ADR-013 (facade boundary)</b>: <c>Services/Compose</c> reaches AI ONLY through the
/// <c>Services/Ai/PublicContracts</c> facade — this file injects no <c>IOpenAiClient</c>,
/// executor, router-internal, or routing type. It is pure over its inputs (no DI, no HTTP),
/// mirroring the walking-skeleton reference impl on <see cref="ComposeDisposition"/> so the
/// contract test locks the shape without a host.
/// </para>
///
/// <para>
/// <b>ADR-039 (one dispatch protocol)</b>: no new endpoint, no routing config, no
/// intent-detection surface is introduced here. The Compose draft flows through the shipped
/// session-dispatch seam; this bridge only shapes the Compose payload into / out of the
/// stored ledger entry.
/// </para>
///
/// <para>
/// <b>Production routing promotion (APPLIED 2026-07-09 by spaarkeai-compose-r2)</b>: for a
/// maker-selectable Binding to route to <c>compose</c>, <c>BindingDisposition.Compose</c>
/// (Binding.cs), its <c>ToLedgerValue</c> mapping, and an <see cref="Ai.OutputRouter"/>
/// pass-through case are required. Core task 010 published the CONTRACT only and deferred this
/// promotion (it was then unscheduled in redesign-r2); spaarkeai-compose-r2 applied it. A Binding
/// may now declare the compose disposition through <c>sprk_disposition</c>.
/// </para>
/// </summary>
public static class ComposeDraftDisposition
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds the <c>compose</c>-disposition <see cref="SessionOutput"/> for a drafted edit —
    /// the entry the <see cref="Ai.IOutputRouter"/> stores to the ledger BEFORE any render
    /// (ADR-040). The Compose-owned <paramref name="draft"/> is serialized into the opaque
    /// <see cref="SessionOutput.Payload"/>; the core never parses it.
    /// </summary>
    /// <param name="bindingId">The <c>sprk_playbookconsumer</c> Binding id that produced the draft (provenance root of the <c>{bindingId}@t{n}</c> key).</param>
    /// <param name="ucId">The Binding's use-case vocabulary id (ADR-040 requires <c>uc_id</c> on every Output entry).</param>
    /// <param name="turn">The 1-based per-session output ordinal allocated by the router (<c>t{n}</c>).</param>
    /// <param name="draft">The Compose-owned structured-edit payload.</param>
    /// <param name="sourceRefs">Citations / document ids / ledger keys the draft was grounded on (identifiers only).</param>
    /// <param name="createdAt">Write timestamp; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    public static SessionOutput BuildDraftOutput(
        string bindingId,
        string ucId,
        int turn,
        ComposeDraftPayload draft,
        IReadOnlyList<string>? sourceRefs = null,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ucId);
        ArgumentNullException.ThrowIfNull(draft);

        return new SessionOutput
        {
            Key = SessionLedger.BuildOutputKey(bindingId, turn),
            BindingId = bindingId,
            UcId = ucId,
            Turn = turn,
            Disposition = ComposeDisposition.DispositionValue,
            Payload = JsonSerializer.SerializeToElement(draft, PayloadJsonOptions),
            SourceRefs = sourceRefs is { Count: > 0 } ? sourceRefs : null,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Render-follows-store PRODUCER: derive the SSE frame from an already-stored compose
    /// entry (delegates to <see cref="ComposeDisposition.BuildFrame"/>). The frame carries the
    /// addressable <c>ledger_ref</c> (<c>{bindingId}@t{n}</c>) + provenance, NEVER the payload.
    /// </summary>
    public static ComposeDispositionFrame BuildFrame(
        SessionOutput storedEntry,
        string? supersedesRef = null,
        string status = ComposeDisposition.StatusReady)
        => ComposeDisposition.BuildFrame(storedEntry, supersedesRef, status);

    /// <summary>
    /// Store-before-render CONSUMER: re-materialize the Compose-owned draft payload the frame
    /// references from CURRENT ledger state. Delegates entry resolution to
    /// <see cref="ComposeDisposition.Materialize"/> (which throws when the referenced entry is
    /// absent — store-before-render), then deserializes the opaque payload.
    /// </summary>
    /// <exception cref="InvalidOperationException">No ledger entry exists at the frame's <c>ledger_ref</c> (ADR-040), or the stored payload is a truncation marker.</exception>
    public static ComposeDraftPayload MaterializeDraft(
        ComposeDispositionFrame frame,
        Func<string, SessionOutput?> ledgerLookup)
    {
        var entry = ComposeDisposition.Materialize(frame, ledgerLookup);
        return DeserializePayload(entry);
    }

    /// <summary>
    /// Supersession resolver for undo/replace (FR-17): the CURRENT compose draft for a binding
    /// is the highest-turn <c>compose</c> entry in the ledger. Delegates to
    /// <see cref="ComposeDisposition.ResolveCurrent"/> — the consumer re-materializes from
    /// current ledger state, never from a client-only DOM undo. Null when the binding has no
    /// compose output.
    /// </summary>
    public static SessionOutput? ResolveCurrent(IEnumerable<SessionOutput> ledger, string bindingId)
        => ComposeDisposition.ResolveCurrent(ledger, bindingId);

    /// <summary>
    /// Deserializes the Compose-owned payload from a stored <c>compose</c>-disposition entry.
    /// Loud on both failure modes rather than rendering wrong content: the entry must declare
    /// the <c>compose</c> disposition, and its payload must not be an ADR-040 truncation marker
    /// (a compose leg fails loud on a truncated payload — never a partial draft).
    /// </summary>
    /// <exception cref="ArgumentException">The entry is not a <c>compose</c>-disposition entry.</exception>
    /// <exception cref="InvalidOperationException">The stored payload is a truncation marker, or is not a valid <see cref="ComposeDraftPayload"/>.</exception>
    public static ComposeDraftPayload DeserializePayload(SessionOutput entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.Equals(entry.Disposition, ComposeDisposition.DispositionValue, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Ledger entry '{entry.Key}' declares disposition '{entry.Disposition}', not " +
                $"'{ComposeDisposition.DispositionValue}'; it is not a Compose draft.",
                nameof(entry));
        }

        if (SessionLedger.IsTruncationMarker(entry.Payload))
        {
            throw new InvalidOperationException(
                $"Compose draft '{entry.Key}' was stored as an ADR-040 truncation marker (over the " +
                $"{SessionLedger.InlinePayloadCapBytes}-byte inline cap). A compose leg fails loud on a " +
                "truncated payload rather than materialize a partial draft; the blob/SPE-pointer offload " +
                "is the designed upgrade path.");
        }

        return entry.Payload.Deserialize<ComposeDraftPayload>(PayloadJsonOptions)
            ?? throw new InvalidOperationException(
                $"Compose draft '{entry.Key}' payload did not deserialize to a {nameof(ComposeDraftPayload)}.");
    }

    /// <summary>
    /// FR-13 (task 030): pure server-side derivation of <see cref="ComposeDraftPayload.ConfidenceBand"/>
    /// from verifiability evidence ALREADY PRESENT on <paramref name="draft"/> — never the model's own
    /// declaration. This method never reads <see cref="ComposeDraftPayload.ConfidenceBand"/>, so a
    /// hostile/buggy model output that smuggled a <c>confidence_band</c> key is structurally ignored
    /// (ADR-039: the frozen <c>compose-draft-alternative</c> / <c>compose-revise-document</c> JPS output
    /// schema does not declare this field in the first place). Two independent grounding signals
    /// combine (design §6.2):
    /// <list type="bullet">
    ///   <item><description><b>Cited source</b> — <see cref="ComposeDraftPayload.Sources"/> is
    ///   non-empty.</description></item>
    ///   <item><description><b>Retained-original support</b> — <see cref="ComposeDraftPayload.ParaId"/>
    ///   is resolved (the strongest signal — <see cref="ResolveParaIdAnchor"/> anchored the edit to a
    ///   real paragraph in the E2 substrate), OR a non-empty <see cref="ComposeDraftPayload.TargetText"/>
    ///   is declared under a non-<c>insert</c> <see cref="ComposeDraftPayload.MatchMode"/> (an unresolved
    ///   but precision-scoped claim against real document text).</description></item>
    /// </list>
    /// Both signals present =&gt; <see cref="ComposeConfidenceBand.High"/>; exactly one =&gt;
    /// <see cref="ComposeConfidenceBand.Medium"/>; neither (a bare, uncited, unanchored insertion)
    /// =&gt; <see cref="ComposeConfidenceBand.Low"/> — never guessed, never a false-precision numeric
    /// score (design §6.2). Deliberately NOT wired into <see cref="BuildDraftOutput"/> /
    /// <see cref="DeserializePayload"/> / <see cref="MaterializeDraft"/>: those three stay pure
    /// serialize/deserialize round-trips (the existing <c>ComposeDraftDispositionTests</c> contract) —
    /// the caller that owns the render/materialize moment invokes this explicitly (e.g. immediately
    /// before returning a draft to the client) so the band is always freshly computed, never stale or
    /// silently defaulted.
    /// </summary>
    public static ComposeConfidenceBand DeriveConfidenceBand(ComposeDraftPayload draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var citedSource = draft.Sources is { Count: > 0 };
        var retainedOriginalSupport =
            !string.IsNullOrWhiteSpace(draft.ParaId)
            || (!string.IsNullOrWhiteSpace(draft.TargetText)
                && !string.Equals(draft.MatchMode, "insert", StringComparison.OrdinalIgnoreCase));

        return (citedSource, retainedOriginalSupport) switch
        {
            (true, true) => ComposeConfidenceBand.High,
            (true, false) => ComposeConfidenceBand.Medium,
            (false, true) => ComposeConfidenceBand.Medium,
            (false, false) => ComposeConfidenceBand.Low,
        };
    }

    /// <summary>
    /// FR-16 (task 030): resolves <paramref name="draft"/>'s <see cref="ComposeDraftPayload.TargetText"/>
    /// against the E2 substrate (the load-time <paramref name="paraIdMap"/> + the index-aligned
    /// <paramref name="paragraphTexts"/> corpus — see <c>ParaIdPreParser</c> /
    /// <see cref="AnnotationReanchorService.ExtractParagraphTexts"/>), returning <paramref name="draft"/>
    /// enriched with <see cref="ComposeDraftPayload.ParaId"/> + <see cref="ComposeDraftPayload.StartOffset"/>
    /// / <see cref="ComposeDraftPayload.EndOffset"/> when a verbatim match is found — the FIRST paragraph
    /// carrying the text wins (mirrors the <c>first</c>/<c>strict</c> match-mode precedent;
    /// multi-match disambiguation is out of this method's scope). An insertion-style draft (no
    /// <see cref="ComposeDraftPayload.TargetText"/>) or an unresolved target returns
    /// <paramref name="draft"/> UNCHANGED — a floating/unmatched edit is honestly left unanchored,
    /// never guessed. Pure — no I/O, no AI-internal type (ADR-013).
    /// </summary>
    public static ComposeDraftPayload ResolveParaIdAnchor(
        ComposeDraftPayload draft,
        IReadOnlyList<ParaIdMapEntry> paraIdMap,
        IReadOnlyList<string> paragraphTexts)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(paraIdMap);
        ArgumentNullException.ThrowIfNull(paragraphTexts);

        if (string.IsNullOrEmpty(draft.TargetText))
        {
            return draft;
        }

        for (var i = 0; i < paragraphTexts.Count; i++)
        {
            var offset = paragraphTexts[i].IndexOf(draft.TargetText, StringComparison.Ordinal);
            if (offset < 0)
            {
                continue;
            }

            ParaIdMapEntry? mapped = null;
            foreach (var candidate in paraIdMap)
            {
                if (candidate.Index == i)
                {
                    mapped = candidate;
                    break;
                }
            }

            if (mapped is null)
            {
                continue;
            }

            return draft with
            {
                ParaId = mapped.ParaId,
                StartOffset = offset,
                EndOffset = offset + draft.TargetText.Length,
            };
        }

        return draft;
    }
}

/// <summary>
/// FR-13 (task 030) coarse VERIFIABILITY band for a Compose draft — <c>high</c> / <c>medium</c> /
/// <c>low</c>, serialized lower-case via <see cref="CamelCaseStringEnumConverter"/> (single-word
/// members camelCase to all-lowercase — same converter as the sibling <c>MatchMode</c> /
/// <c>EditErrorKind</c> enums in <c>ComposeEditModels.cs</c>). Deliberately NOT a numeric/percentage
/// score (design §6.2 — a coarse band avoids the over-reliance + false-precision failure mode a raw
/// confidence number invites).
/// </summary>
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum ComposeConfidenceBand
{
    /// <summary>Grounded: a cited source AND a resolved retained-original anchor (paraId, or a
    /// declared non-insert target-text match).</summary>
    High,

    /// <summary>Weakly supported: exactly one of {cited source, resolved anchor} is present.</summary>
    Medium,

    /// <summary>Unsupported: no cited source and no resolvable anchor — never guessed.</summary>
    Low,
}

/// <summary>
/// The Compose-owned structured-edit payload (handoff §1 shape) that rides inside the opaque
/// <see cref="SessionOutput.Payload"/> of a <c>compose</c>-disposition ledger entry. The core
/// treats this as opaque bytes; Compose validates + renders it. snake_case wire vocabulary,
/// consistent with the ledger + the sibling A0 frames.
/// </summary>
public sealed record ComposeDraftPayload
{
    /// <summary>
    /// The text the draft targets for replacement, when the edit replaces an existing span.
    /// Null / empty for an insertion-style draft (draft-into-editor at cursor / append).
    /// </summary>
    [JsonPropertyName("target_text")]
    public string? TargetText { get; init; }

    /// <summary>The drafted content to materialize into the editor. The load-bearing field of the draft.</summary>
    [JsonPropertyName("new_text")]
    public required string NewText { get; init; }

    /// <summary>
    /// How the client resolves <see cref="TargetText"/> in the document (Compose-owned vocabulary,
    /// e.g. <c>strict</c> / <c>fuzzy</c> / <c>insert</c>). Opaque to the core.
    /// </summary>
    [JsonPropertyName("match_mode")]
    public string MatchMode { get; init; } = "insert";

    /// <summary>Optional model-supplied rationale for the draft (shown as provenance / explanation).</summary>
    [JsonPropertyName("rationale")]
    public string? Rationale { get; init; }

    /// <summary>Citations / source ids the draft was grounded on (ids only — never quoted content).</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<string>? Sources { get; init; }

    /// <summary>
    /// FR-13 (task 030): server-derived, coarse VERIFIABILITY band — never a raw model self-report
    /// (design §6.2). The frozen JPS output schema for <c>compose-draft-alternative</c> /
    /// <c>compose-revise-document</c> (ADR-039) does not declare this field, so a freshly-materialized
    /// draft always deserializes with this <c>null</c> until a caller explicitly computes it via
    /// <see cref="ComposeDraftDisposition.DeriveConfidenceBand"/>. Additive + optional; never a
    /// numeric/percentage score (design §6.2 anti-false-precision rule).
    /// </summary>
    [JsonPropertyName("confidence_band")]
    public ComposeConfidenceBand? ConfidenceBand { get; init; }

    /// <summary>
    /// FR-16 (task 030): the redline's <c>w14:paraId</c> — the exact paragraph in the retained
    /// original the edit targets (E2 substrate, task 012). Resolved via
    /// <see cref="ComposeDraftDisposition.ResolveParaIdAnchor"/> against the load-time paraId map +
    /// paragraph texts; <c>null</c> for an insertion-style draft (no <see cref="TargetText"/> to
    /// anchor) or an unresolved target. Additive + optional; camelCase (matches the existing
    /// <c>paraId</c> wire vocabulary on <c>AnchoredAnnotationAnchor</c> / <c>ParaIdMapEntry</c> /
    /// <c>ComposeEditedParagraph</c> — deliberately NOT snake_case, per that established precedent).
    /// </summary>
    [JsonPropertyName("paraId")]
    public string? ParaId { get; init; }

    /// <summary>
    /// FR-16 (task 030): the character offset (inclusive, 0-based) of <see cref="TargetText"/>'s
    /// start within its <see cref="ParaId"/> paragraph's settled text. Additive + optional; null
    /// unless <see cref="ParaId"/> is also resolved.
    /// </summary>
    [JsonPropertyName("start_offset")]
    public int? StartOffset { get; init; }

    /// <summary>
    /// FR-16 (task 030): the character offset (exclusive) of <see cref="TargetText"/>'s end within
    /// its <see cref="ParaId"/> paragraph's settled text. Additive + optional.
    /// </summary>
    [JsonPropertyName("end_offset")]
    public int? EndOffset { get; init; }
}
