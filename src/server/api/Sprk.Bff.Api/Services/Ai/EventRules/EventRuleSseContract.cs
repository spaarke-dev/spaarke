using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.EventRules;

/// <summary>
/// The Event-path SSE contract (FR-P1-03, spaarke-ai-architecture-redesign-r1
/// task 022) — the wire vocabulary the Event Rules stream emits and the
/// SpaarkeAi client (task 023 <c>dispatchConsumer</c> / chips UI) consumes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Envelope</b>: every item is a <see cref="Sprk.Bff.Api.Api.Ai.ChatSseEvent"/>
/// (<c>{type, content, data}</c>, camelCase, framed <c>data: {json}\n\n</c>) —
/// the SAME envelope the chat message stream uses, so the client's canonical
/// <c>useSseStream</c> + <c>sseToPaneEventBridge</c> path consumes it with no
/// second parser. Unknown <c>type</c> discriminants are ignored by existing
/// consumers (established chat-SSE back-compat contract).
/// </para>
/// <para>
/// <b>Event order for the happy path</b> (single upload, high confidence):
/// <c>event_classification</c> → <c>event_output</c> (summary) → <c>chips</c> →
/// <c>done</c>. Low-confidence path: <c>event_classification</c> →
/// <c>event_confirmation</c> (M4 — carries its own chips) → <c>done</c>.
/// Bound-denied paths: a single <c>event_notice</c> → <c>done</c>.
/// Failures: <c>error</c> (standard chat error shape, content = safe message).
/// </para>
/// <para>
/// <b>ADR-040</b>: <see cref="EventOutputData.Payload"/> is ALWAYS the payload of
/// the already-stored ledger entry (<see cref="EventOutputData.LedgerKey"/>) —
/// render follows store; the stream never carries pre-store output.
/// </para>
/// </remarks>
public static class EventRuleSseEvents
{
    /// <summary>Graceful non-execution notice. Data: <see cref="EventNoticeData"/>.</summary>
    public const string Notice = "event_notice";

    /// <summary>Classification result (classify member). Data: <see cref="EventClassificationData"/>.</summary>
    public const string Classification = "event_classification";

    /// <summary>M4 confirmation turn (classify confidence below policy threshold). Data: <see cref="EventConfirmationData"/>.</summary>
    public const string Confirmation = "event_confirmation";

    /// <summary>A member's stored output (render-follows-store). Data: <see cref="EventOutputData"/>.</summary>
    public const string Output = "event_output";

    /// <summary>Next-step chips (Click-path affordances carrying binding ids). Data: <see cref="EventChipsData"/>.</summary>
    public const string Chips = "chips";

    /// <summary>Terminal event (matches the chat stream's <c>done</c> convention).</summary>
    public const string Done = "done";
}

/// <summary>Bounded reason vocabulary for <see cref="EventNoticeData.Reason"/>.</summary>
public static class EventNoticeReasons
{
    /// <summary>A typed command accompanied the upload — the Text path wins; the rule did not fire.</summary>
    public const string Superseded = "superseded";

    /// <summary>The user opted out of automatic Event-path runs.</summary>
    public const string OptedOut = "opted-out";

    /// <summary>The per-user daily Event-path budget is exhausted (NFR-09) — deferred with a chip.</summary>
    public const string DailyCap = "daily-cap";

    /// <summary>The upload set resolved empty — Event-path precondition (task 025 r7 guard re-home).</summary>
    public const string NoAttachments = "no-attachments";

    /// <summary>No enabled Binding row declares membership in this event.</summary>
    public const string NoRule = "no-rule";
}

/// <summary>
/// One Click-path chip: <c>targetBindingId</c> IS the routing decision (ADR-039) —
/// the client dispatches it verbatim via <c>dispatchConsumer(bindingId, args)</c>
/// (task 023); no client-side intent detection.
/// </summary>
/// <param name="TargetBindingId">The <c>sprk_playbookconsumer</c> row id to invoke.</param>
/// <param name="Label">User-facing chip label.</param>
/// <param name="Args">Optional invocation args forwarded verbatim (e.g. <c>{ fileIds: [...] }</c>).</param>
public sealed record EventChip(string TargetBindingId, string Label, object? Args = null);

/// <summary>Data payload for <see cref="EventRuleSseEvents.Notice"/>.</summary>
/// <param name="Reason">Bounded reason token (<see cref="EventNoticeReasons"/>).</param>
/// <param name="Message">User-renderable message (never a silent drop — NFR-09).</param>
/// <param name="Chips">Optional defer/manual-run chips (e.g. daily-cap defers with a chip per spec §7.1).</param>
public sealed record EventNoticeData(
    string Reason,
    string Message,
    IReadOnlyList<EventChip>? Chips = null);

/// <summary>Data payload for <see cref="EventRuleSseEvents.Classification"/>.</summary>
/// <param name="FileId">Session-file id that was classified.</param>
/// <param name="FileName">Display name of the classified file.</param>
/// <param name="DocType">Classifier label (also persisted to <c>ChatSessionFile.ClassifiedDocType</c>).</param>
/// <param name="Confidence">Calibrated confidence [0,1] (also persisted to <c>ChatSessionFile.ClassifiedConfidence</c>).</param>
/// <param name="BindingId">The classify Binding that produced this output.</param>
/// <param name="Ucid">Use-case id (e.g. <c>UC-A-7</c>).</param>
/// <param name="LedgerKey">Addressable ledger key of the stored classify output (ADR-040).</param>
public sealed record EventClassificationData(
    string FileId,
    string FileName,
    string DocType,
    double Confidence,
    string BindingId,
    string? Ucid,
    string LedgerKey);

/// <summary>
/// Data payload for <see cref="EventRuleSseEvents.Confirmation"/> — the M4 gate
/// turn inserted when classify confidence falls below the policy threshold.
/// Remaining rule members did NOT run; the confirm chip resumes them via the
/// Click path.
/// </summary>
/// <param name="FileId">The classified file.</param>
/// <param name="DocType">The uncertain classification.</param>
/// <param name="Confidence">The below-threshold confidence.</param>
/// <param name="Threshold">The policy threshold that fired the gate.</param>
/// <param name="Message">User-renderable confirmation question.</param>
/// <param name="Chips">Confirm/continue chips (Click path).</param>
public sealed record EventConfirmationData(
    string FileId,
    string DocType,
    double Confidence,
    double Threshold,
    string Message,
    IReadOnlyList<EventChip> Chips);

/// <summary>Data payload for <see cref="EventRuleSseEvents.Output"/>.</summary>
/// <param name="BindingId">The Binding that produced the output.</param>
/// <param name="Ucid">Use-case id (e.g. <c>UC-A-1</c>).</param>
/// <param name="LedgerKey">Addressable key of the STORED ledger entry (ADR-040 — storage preceded this event).</param>
/// <param name="Disposition">Ledger disposition vocabulary value (P1: <c>informational</c>).</param>
/// <param name="Payload">The stored entry's payload, verbatim (render follows store).</param>
public sealed record EventOutputData(
    string BindingId,
    string? Ucid,
    string LedgerKey,
    string Disposition,
    JsonElement Payload);

/// <summary>Data payload for <see cref="EventRuleSseEvents.Chips"/>.</summary>
/// <param name="SourceBindingId">The Binding whose completion these chips follow.</param>
/// <param name="Chips">Curated <c>sprk_chiptransitions</c> chips + contextual chips (e.g. bulk "Summarize all files?").</param>
public sealed record EventChipsData(
    string SourceBindingId,
    IReadOnlyList<EventChip> Chips);
