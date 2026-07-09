using System.Text;
using System.Text.Json;

namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Typed session-ledger entry records (ADR-040 — Session Ledger; FR-P0-01).
///
/// The ledger is the append-only, addressable carrier of cross-capability context:
/// every capability output, text-turn tool chain, widget user-action, and pending
/// gate lands here BEFORE any surface renders it (storage precedes rendering — D2/D8).
///
/// Persistence: these records ride the existing 3-tier session pipeline unchanged —
/// Redis hot copy (System.Text.Json via <c>ITenantCache</c>), Cosmos warm document
/// (mapped to the parallel <c>Stored*</c> shapes in
/// <c>Services/Ai/Sessions/StoredLedgerEntries.cs</c>), Dataverse cold audit.
/// The ledger changes WHAT persists, not WHERE (ADR-040).
///
/// P0 dark-landing contract (spec FR-P0-01): this task ships the model + persistence
/// round-trip ONLY. Zero production readers exist; writers arrive at P1 (FR-P1-02
/// universal ledger-write-before-render), ToolChain writers at P2, ledger-referencing
/// capabilities at P3.
///
/// Governance (ADR-015 / project NFR-07): ledger entries are Tier 3 user-owned,
/// GDPR-erasable data (erased with the session via <c>DeleteSessionAsync</c>).
/// <see cref="SessionToolChain"/> entries carry identifiers / filters / counts /
/// citations ONLY — never verbatim content — so they remain Tier-2-compatible metadata.
///
/// Append-only rule: entries are never mutated or deleted within a session;
/// corrections are NEW entries referencing the superseded key.
/// </summary>
public static class SessionLedger
{
    /// <summary>
    /// Builds the canonical addressable key for an Output entry: <c>{bindingId}@t{n}</c>
    /// (ADR-040: every output MUST be addressable by this key; loop-produced outputs
    /// use the reserved binding id <c>"loop"</c>).
    /// </summary>
    public static string BuildOutputKey(string bindingId, int turn) => $"{bindingId}@t{turn}";

    /// <summary>
    /// ADR-040 inline payload cap (UTF-8 bytes of the payload's raw JSON text). Payloads
    /// over this cap are ENFORCED at write time — replaced by a deterministic truncation
    /// marker via <see cref="CapInlinePayload"/> (operator ruling 2026-07-07, promoted
    /// from the task-021 warn-only threshold by task 055 of
    /// <c>spaarke-ai-architecture-redesign-r1</c>). The blob/SPE-pointer offload remains
    /// the designed upgrade path: when it lands, the marker becomes a pointer and the
    /// content stops being lossy.
    /// </summary>
    public const int InlinePayloadCapBytes = 128 * 1024;

    /// <summary>
    /// Max characters of the original raw JSON text preserved in the truncation marker's
    /// <c>preview</c> field. 16K chars ≤ 64 KB UTF-8 worst case, keeping the marker itself
    /// comfortably under <see cref="InlinePayloadCapBytes"/>.
    /// </summary>
    public const int TruncationPreviewChars = 16 * 1024;

    /// <summary>Sentinel property marking a payload as an over-cap truncation marker.</summary>
    public const string TruncationMarkerProperty = "$truncated";

    /// <summary>
    /// True when <paramref name="payload"/> is a truncation marker produced by
    /// <see cref="CapInlinePayload"/> (readers use this to distinguish a lossy marker
    /// from capability output — e.g. <c>ledger_resolution</c> resolving a truncated
    /// entry should fail loudly rather than feed the preview to a capability).
    /// </summary>
    public static bool IsTruncationMarker(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(TruncationMarkerProperty, out var flag)
        && flag.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Enforces the ADR-040 inline payload size cap at the ledger write seam. Under-cap
    /// payloads pass through untouched. Over-cap payloads are deterministically replaced
    /// by a truncation marker object:
    /// <c>{ "$truncated": true, "original_bytes": n, "cap_bytes": n, "preview": "…" }</c>.
    /// The marker is valid JSON, addressable like any entry payload, and carries the
    /// first <see cref="TruncationPreviewChars"/> characters of the original raw text
    /// (ledger content is Tier 3 — the preview is stored, never logged, per NFR-07).
    /// </summary>
    /// <param name="payload">The payload to store (caller supplies a detached/cloned element).</param>
    /// <returns>The payload to write, the ORIGINAL payload's UTF-8 byte size, and whether truncation occurred.</returns>
    public static CappedLedgerPayload CapInlinePayload(JsonElement payload)
    {
        var rawText = payload.GetRawText();
        var originalBytes = Encoding.UTF8.GetByteCount(rawText);
        if (originalBytes <= InlinePayloadCapBytes)
        {
            return new CappedLedgerPayload(payload, originalBytes, Truncated: false);
        }

        var previewLength = Math.Min(TruncationPreviewChars, rawText.Length);
        if (previewLength > 0 && char.IsHighSurrogate(rawText[previewLength - 1]))
        {
            previewLength--; // never split a surrogate pair at the preview boundary
        }

        var marker = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [TruncationMarkerProperty] = true,
            ["original_bytes"] = originalBytes,
            ["cap_bytes"] = InlinePayloadCapBytes,
            ["preview"] = rawText[..previewLength],
        });
        return new CappedLedgerPayload(marker, originalBytes, Truncated: true);
    }
}

/// <summary>
/// Result of <see cref="SessionLedger.CapInlinePayload"/>: the payload to store (original
/// or truncation marker), the original payload's UTF-8 byte size (for identifier-only
/// logging per NFR-07), and whether the cap fired.
/// </summary>
public readonly record struct CappedLedgerPayload(JsonElement Payload, int OriginalBytes, bool Truncated);

/// <summary>
/// A single capability output — the P4 composition carrier (canonical design §5.2, T-01).
///
/// Addressable by <see cref="Key"/> (<c>{bindingId}@t{n}</c>); later capabilities
/// reference it via <c>ledger_resolution</c> in their input schema (readers arrive P3).
/// <see cref="Disposition"/> is the ONLY rendering contract (ADR-040) — storage is
/// never coupled to how (or whether) a surface renders the entry.
/// </summary>
public sealed record SessionOutput
{
    /// <summary>Addressable ledger key: <c>{bindingId}@t{n}</c> (or <c>loop@t{n}</c>). Unique within a session.</summary>
    public required string Key { get; init; }

    /// <summary>Binding (<c>sprk_playbookconsumer</c>) id that produced this output; <c>"loop"</c> for loop-native outputs.</summary>
    public required string BindingId { get; init; }

    /// <summary>Stable use-case vocabulary id (canonical design §3).</summary>
    public required string UcId { get; init; }

    /// <summary>1-based session turn number the output was produced on.</summary>
    public required int Turn { get; init; }

    /// <summary>
    /// Rendering contract: <c>informational | work_product | overlay | email | record | notification | compose</c>.
    /// The only contract between storage and rendering (ADR-040).
    /// The <c>compose</c> member (ComposeDisposition v1 seam — spaarke-ai-architecture-redesign-r2
    /// task 010, FR-A0-06/08) rides this SAME string field and the existing SSE/ledger surface;
    /// it is NOT a second rendering path. Its SSE frame + supersession keying live in
    /// <see cref="Services.Ai.PublicContracts.ComposeDispositionFrame"/> / <see cref="Services.Ai.PublicContracts.ComposeDisposition"/>.
    /// The structured-edit payload inside <see cref="Payload"/> (target_text / new_text / match_mode / …)
    /// is Compose-owned and opaque to the platform — the core owns the envelope only.
    /// </summary>
    public required string Disposition { get; init; }

    /// <summary>
    /// Schema-validated output payload. Inline payloads are size-capped per ADR-040 —
    /// beyond <see cref="SessionLedger.InlinePayloadCapBytes"/> the payload holds a
    /// deterministic truncation marker (see <see cref="SessionLedger.CapInlinePayload"/>;
    /// blob/SPE pointer offload is the designed upgrade path), not the content.
    /// </summary>
    public required JsonElement Payload { get; init; }

    /// <summary>Overlay target widget instance id, when <see cref="Disposition"/> is <c>overlay</c>.</summary>
    public string? WidgetId { get; init; }

    /// <summary>Citations / document ids / ledger keys this output was grounded on.</summary>
    public IReadOnlyList<string>? SourceRefs { get; init; }

    /// <summary>UTC timestamp the output was written to the ledger.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Client projection of a <c>compose</c>-disposition <see cref="SessionOutput"/> for the FR-04
/// render-follows-store read endpoint (<c>GET /api/ai/chat/sessions/{id}/compose-outputs</c> —
/// spaarkeai-compose-r2 task 016). ComposeWorkspace re-reads these to materialize AI-drafted
/// content into the editor (ADR-040: the durable ledger entry is the source of truth, never a
/// client buffer).
///
/// <para>
/// <see cref="Payload"/> is passed through OPAQUELY — its snake_case Compose-owned fields
/// (<c>target_text</c> / <c>new_text</c> / <c>match_mode</c> / …) are preserved verbatim; the
/// platform never parses it (ADR-013 / ADR-040 envelope-only ownership). The serializer's
/// camelCase policy applies only to this record's OUTER property names, not the nested element.
/// </para>
/// </summary>
public sealed record ComposeLedgerOutputDto(
    string Key,
    string BindingId,
    int Turn,
    string Disposition,
    JsonElement Payload);

/// <summary>
/// A replayable audit record of one text-turn's tool-call chain (ADR-040 ToolChain entry).
///
/// NFR-07 / ADR-015 BINDING: carries identifiers, filters, counts, durations, and
/// citation ids ONLY — NEVER verbatim tool arguments, result content, or user text.
/// This keeps ToolChain entries Tier-2-compatible metadata.
/// </summary>
public sealed record SessionToolChain
{
    /// <summary>1-based session turn number this chain executed on.</summary>
    public required int Turn { get; init; }

    /// <summary>Ordered tool calls executed during the turn.</summary>
    public IReadOnlyList<SessionToolCall> Calls { get; init; } = Array.Empty<SessionToolCall>();

    /// <summary>UTC timestamp the chain record was written to the ledger.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// One tool invocation within a <see cref="SessionToolChain"/>.
/// Identifiers / filters / counts only — no content (NFR-07).
/// </summary>
public sealed record SessionToolCall
{
    /// <summary>Namespaced tool id (<c>sprk_analysistool</c> row / handler id).</summary>
    public required string ToolId { get; init; }

    /// <summary>
    /// Compact identifier/filter summary of the call's arguments (e.g.
    /// <c>"matterId=123; top=5"</c>). MUST NOT contain document text, user content,
    /// or verbatim argument payloads.
    /// </summary>
    public string? ArgsSummary { get; init; }

    /// <summary>Number of results the tool returned, when countable.</summary>
    public int? ResultCount { get; init; }

    /// <summary>Citation / source ids the call emitted (ids only — never quotes).</summary>
    public IReadOnlyList<string>? Citations { get; init; }

    /// <summary>Wall-clock duration of the call in milliseconds.</summary>
    public long? DurationMs { get; init; }
}

/// <summary>
/// A widget user-action (selection, highlight, edit, …) recorded as a consumable
/// session event (ADR-040 WidgetEvent entry; extends tab persistence + PaneEventBus
/// emissions — ADR-030). Written by widget surfaces from P1 onward.
/// </summary>
public sealed record SessionWidgetEvent
{
    /// <summary>Widget instance id the action occurred in.</summary>
    public required string WidgetId { get; init; }

    /// <summary>Event type vocabulary (e.g. <c>selection</c>, <c>highlight</c>, <c>edit</c>).</summary>
    public required string EventType { get; init; }

    /// <summary>1-based session turn number the action occurred on.</summary>
    public required int Turn { get; init; }

    /// <summary>Ledger keys / document ids the event references (e.g. the Output entry the widget renders).</summary>
    public IReadOnlyList<string>? EntryRefs { get; init; }

    /// <summary>Small structured event payload (size-capped per ADR-040). Null when the event carries refs only.</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>UTC timestamp the event was written to the ledger.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// A pending confirmation or in-flight elicitation marker (ADR-040 Gate entry).
///
/// Generalizes the <c>PendingPlanManager</c> store shape (D12 — unification lands P2);
/// gate state is ledger entries, not a dedicated approval entity. Because the ledger
/// is append-only, a resolved gate is recorded as a NEW entry (same <see cref="GateId"/>,
/// new <see cref="Status"/>) referencing the pending entry it supersedes.
/// </summary>
public sealed record SessionGate
{
    /// <summary>Stable gate id correlating the pending entry with its resolution entry.</summary>
    public required string GateId { get; init; }

    /// <summary>Gate kind: <c>confirmation</c> (side-effect approval) or <c>elicitation</c> (in-flight arg capture).</summary>
    public required string Kind { get; init; }

    /// <summary>Gate state: <c>pending | confirmed | rejected | expired | superseded</c>.</summary>
    public required string Status { get; init; }

    /// <summary>1-based session turn number the gate was raised on.</summary>
    public required int Turn { get; init; }

    /// <summary>Binding id the gated invocation targets, when known.</summary>
    public string? BindingId { get; init; }

    /// <summary>Declared side-effect class driving the gate (<c>read | write | communicate | pure</c>).</summary>
    public string? SideEffectClass { get; init; }

    /// <summary>
    /// For <c>elicitation</c> gates: the declared input-schema field NAMES still missing
    /// when the marker was written (FR-P2-03). Identifiers only — schema field names,
    /// never captured values (NFR-07).
    /// </summary>
    public IReadOnlyList<string>? MissingFields { get; init; }

    /// <summary>Ledger key of the Output entry produced once the gate resolved, when applicable.</summary>
    public string? OutputKey { get; init; }

    /// <summary>UTC timestamp the gate entry was written.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp the gate was resolved; null while pending.</summary>
    public DateTimeOffset? ResolvedAt { get; init; }
}
