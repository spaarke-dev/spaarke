using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// <b>ComposeDisposition v1 seam</b> — the FIRST Phase-A0 contract published for the parallel
/// <c>spaarkeai-compose-r2</c> project (spec FR-A0-06 / FR-A0-08, design §3.1). Consumed by
/// Compose FR-04 (draft-into-editor), FR-16 (pending redline), FR-17 (undo/replace).
///
/// <para>
/// <b>What the CORE owns (this contract — the ENVELOPE only)</b>:
/// <list type="bullet">
/// <item>the <c>compose</c> disposition member on the ADR-040 rendering contract
/// (<see cref="SessionOutput.Disposition"/> — a string vocabulary member, NOT a second
/// rendering path; it rides the existing SSE/ledger surface, ADR-039);</item>
/// <item>the SSE frame shape (<see cref="ComposeDispositionFrame"/>) — a versioned,
/// tolerant-reader frame carrying <c>ledger_ref</c> + <c>disposition</c> + provenance +
/// supersession keying, and NEVER the payload (storage-precedes-rendering, ADR-040);</item>
/// <item>supersession semantics — undo/replace is a NEW <c>compose</c> <see cref="SessionOutput"/>
/// that supersedes the prior one, addressable by <c>{bindingId}@t{n}</c>
/// (<see cref="SessionLedger.BuildOutputKey"/>); the consumer re-materializes from CURRENT
/// ledger state (there is NO client-only DOM undo).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What COMPOSE owns (opaque to the platform — the PAYLOAD)</b>: the structured-edit schema
/// (<c>target_text</c> / <c>new_text</c> / <c>match_mode</c> / <c>rationale</c> / <c>sources</c>)
/// lives INSIDE the opaque <see cref="SessionOutput.Payload"/>. This contract deliberately bakes
/// ZERO editor semantics into the platform surface — the frame carries a ledger reference, not the
/// edit body. Compose validates + renders that payload; the core never parses it.
/// </para>
///
/// <para>
/// <b>Versioning + tolerant-reader rules (consistent with the sibling A0 contracts —
/// OutcomeCard v1, JobAwareCompletionState v1)</b>: <see cref="Version"/> is a literal
/// (<c>"1.0"</c>) present on every frame; v1 is additive-only; unknown members are IGNORED by
/// readers (surfaced via <see cref="ComposeDispositionFrame.UnknownMembers"/> for
/// forward-compat, never a parse failure). Compose consumes this frame with zero local variant.
/// </para>
///
/// <para>
/// <b>Walking-skeleton reference implementation</b>: <see cref="BuildFrame"/> is the thin
/// reference PRODUCER (render-follows-store: it derives the frame FROM an already-stored ledger
/// entry — it cannot manufacture a frame for content that was never stored); <see cref="Materialize"/>
/// + <see cref="ResolveCurrent"/> are the thin reference CONSUMER (re-materialize from ledger
/// state; store-before-render enforced by throwing when the referenced entry is absent). Both are
/// pure over an in-memory ledger — no DI, no HTTP — so Compose can adopt them directly and the
/// contract test locks the shape without a host.
/// </para>
///
/// <para>
/// <b>Production wiring (NOT in this walking skeleton — see task 010 report)</b>: promoting
/// <c>compose</c> to a maker-selectable routing disposition additionally requires
/// <c>BindingDisposition.Compose</c> (Binding.cs), its <c>ToLedgerValue</c> mapping, and an
/// <c>OutputRouter</c> routing case. Those touch shared routing files and are described (not
/// applied) by task 010 to preserve parallel-agent safety; the string member + frame published
/// here are the stable seam Compose builds on.
/// </para>
/// </summary>
public static class ComposeDisposition
{
    /// <summary>The <c>compose</c> disposition vocabulary member on <see cref="SessionOutput.Disposition"/> (ADR-040).</summary>
    public const string DispositionValue = "compose";

    /// <summary>ComposeDisposition contract version. Literal string per the widgets-r1 / work-product-envelope convention. v1 is additive-only.</summary>
    public const string Version = "1.0";

    /// <summary>Frame status: the stored compose output is complete and renderable.</summary>
    public const string StatusReady = "ready";

    /// <summary>Frame status: a partial/streaming compose output (Compose may render progressively). Additive; readers unaware of it fall back to <see cref="StatusReady"/> behavior.</summary>
    public const string StatusPartial = "partial";

    /// <summary>Frame status: the compose leg failed AFTER a ledger marker was stored (store-before-render preserved). The referenced entry carries the failure detail in its opaque payload.</summary>
    public const string StatusFailed = "failed";

    /// <summary>
    /// Reference PRODUCER (render-follows-store). Derives the SSE frame from an already-stored
    /// <c>compose</c> ledger entry — enforcing that a frame can only exist for content the ledger
    /// already holds. The frame carries the entry's addressable key (<c>ledger_ref</c>) and
    /// provenance, NOT the payload.
    /// </summary>
    /// <param name="storedEntry">The compose <see cref="SessionOutput"/> already written to the ledger.</param>
    /// <param name="supersedesRef">When this output supersedes a prior compose output, its
    /// <c>{bindingId}@t{n}</c> key; null for the first output of a binding.</param>
    /// <param name="status">One of <see cref="StatusReady"/> / <see cref="StatusPartial"/> / <see cref="StatusFailed"/>.</param>
    /// <exception cref="ArgumentException">The entry is not a <c>compose</c>-disposition entry.</exception>
    public static ComposeDispositionFrame BuildFrame(
        SessionOutput storedEntry,
        string? supersedesRef = null,
        string status = StatusReady)
    {
        ArgumentNullException.ThrowIfNull(storedEntry);
        if (!string.Equals(storedEntry.Disposition, DispositionValue, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"ComposeDisposition frame can only be produced from a '{DispositionValue}' ledger entry; " +
                $"entry '{storedEntry.Key}' declares '{storedEntry.Disposition}'.",
                nameof(storedEntry));
        }

        return new ComposeDispositionFrame
        {
            Version = Version,
            Disposition = DispositionValue,
            LedgerRef = storedEntry.Key,
            BindingId = storedEntry.BindingId,
            Turn = storedEntry.Turn,
            SupersedesRef = supersedesRef,
            Status = status,
            CreatedAt = storedEntry.CreatedAt,
        };
    }

    /// <summary>
    /// Reference CONSUMER. Re-materializes the compose output the frame references from CURRENT
    /// ledger state. <b>Store-before-render</b> is enforced: if the ledger holds no entry at
    /// <see cref="ComposeDispositionFrame.LedgerRef"/>, this throws rather than rendering a frame
    /// whose content was never stored. The returned <see cref="SessionOutput.Payload"/> is opaque
    /// (Compose-owned) — the core never interprets it.
    /// </summary>
    /// <param name="frame">The compose SSE frame.</param>
    /// <param name="ledgerLookup">Resolver from a ledger key to its stored entry (null when absent).</param>
    /// <exception cref="InvalidOperationException">No ledger entry exists at the frame's <c>ledger_ref</c>.</exception>
    public static SessionOutput Materialize(ComposeDispositionFrame frame, Func<string, SessionOutput?> ledgerLookup)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ledgerLookup);

        return ledgerLookup(frame.LedgerRef)
            ?? throw new InvalidOperationException(
                $"store-before-render violated (ADR-040): compose frame references ledger key " +
                $"'{frame.LedgerRef}' but no entry is stored there. A compose disposition MUST NOT be " +
                $"rendered before its ledger write.");
    }

    /// <summary>
    /// Supersession resolver. The CURRENT compose output for a binding is the highest-turn
    /// <c>compose</c> entry in the ledger — undo/replace re-materializes from ledger state, never
    /// from a client-only DOM undo. Returns null when the binding has no compose output.
    /// </summary>
    public static SessionOutput? ResolveCurrent(IEnumerable<SessionOutput> ledger, string bindingId)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        SessionOutput? current = null;
        foreach (var entry in ledger)
        {
            if (!string.Equals(entry.Disposition, DispositionValue, StringComparison.Ordinal)) continue;
            if (!string.Equals(entry.BindingId, bindingId, StringComparison.Ordinal)) continue;
            if (current is null || entry.Turn > current.Turn) current = entry;
        }
        return current;
    }
}

/// <summary>
/// The <b>ComposeDisposition v1 SSE frame</b> — the envelope emitted on the existing chat SSE
/// surface when a <c>compose</c> ledger entry is stored (ADR-039: no second dispatch protocol).
///
/// <para>
/// <b>Envelope only — never the payload</b>: the frame carries the addressable ledger reference
/// (<see cref="LedgerRef"/>) and provenance so the client re-materializes the stored entry
/// (storage-precedes-rendering, ADR-040). The structured-edit body stays inside the opaque
/// <see cref="SessionOutput.Payload"/> and is Compose-owned.
/// </para>
///
/// <para>
/// <b>Tolerant reader</b>: <see cref="Version"/> is always present; unknown members are captured
/// into <see cref="UnknownMembers"/> and ignored (v1 is additive-only — a future field never
/// breaks a v1 reader). Property names are the snake_case wire vocabulary consistent with the
/// ledger + sibling A0 frames.
/// </para>
/// </summary>
public sealed record ComposeDispositionFrame
{
    /// <summary>Contract version (always present; <c>"1.0"</c> for v1). Tolerant readers gate additive behavior on this.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Always <c>"compose"</c> — the ADR-040 rendering-contract member this frame rides.</summary>
    [JsonPropertyName("disposition")]
    public required string Disposition { get; init; }

    /// <summary>Addressable ledger key <c>{bindingId}@t{n}</c> of the stored compose output the client re-materializes (ADR-040). The frame's load-bearing field.</summary>
    [JsonPropertyName("ledger_ref")]
    public required string LedgerRef { get; init; }

    /// <summary>Binding (<c>sprk_playbookconsumer</c>) id that produced the output — provenance.</summary>
    [JsonPropertyName("binding_id")]
    public required string BindingId { get; init; }

    /// <summary>1-based session turn the output was produced on.</summary>
    [JsonPropertyName("turn")]
    public required int Turn { get; init; }

    /// <summary>
    /// When this compose output supersedes a prior one (undo/replace), the prior output's
    /// <c>{bindingId}@t{n}</c> key; null for a binding's first compose output. The client
    /// re-materializes from current ledger state — this field is provenance for the supersession
    /// chain, not a render instruction.
    /// </summary>
    [JsonPropertyName("supersedes_ref")]
    public string? SupersedesRef { get; init; }

    /// <summary>Frame status: <c>ready</c> | <c>partial</c> | <c>failed</c> (see <see cref="ComposeDisposition"/>). Documented failure/partial states; readers unaware of a value fall back to ready-render behavior.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = ComposeDisposition.StatusReady;

    /// <summary>UTC timestamp the stored output was written to the ledger (ISO-8601 on the wire).</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Tolerant-reader sink: any wire member not mapped above (e.g. a future additive v1.x field)
    /// is captured here and IGNORED by v1 rendering. Its presence never fails deserialization.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownMembers { get; init; }
}
