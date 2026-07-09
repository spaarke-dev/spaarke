using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// <b>TraceEvent v1</b> — a versioned, tolerant-reader READ projection over the
/// existing ADR-040 session ledger (spec FR-A0-05, design D-F4). It NAMES the
/// ledger's <see cref="SessionToolChain"/> / <see cref="SessionToolCall"/> and
/// <see cref="SessionGate"/> markers as a stable, ordered decision-trace stream —
/// request → context slices used → tools selected → gate/approval path → outcome —
/// so the decision-traceability view (FR-A1-09 / task 038) and live plan narration
/// bind to THIS shape rather than reaching into ledger internals.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read projection — NOT a store (ADR-040).</b> There is deliberately NO parallel
/// trace store. The ledger remains the single source of truth; a TraceEvent stream is
/// materialized on demand by <see cref="TraceEventProjection.Project"/> from ledger
/// markers a caller has already loaded via the session API. This honors the ADR-040
/// MUST NOT: "create a second session-state store or per-surface session caches —
/// surfaces read ledger projections via the session API."
/// </para>
/// <para>
/// <b>No-content telemetry (NFR-07 / ADR-015).</b> A TraceEvent carries identifiers,
/// filters, counts, durations, and citation ids ONLY — NEVER prompt/response content,
/// verbatim tool arguments, or user text. The ledger markers this projects from are
/// already Tier-2-compatible metadata (see the NFR-07 clauses on
/// <see cref="SessionToolChain"/> / <see cref="SessionToolCall"/> /
/// <see cref="SessionGate"/>), and <see cref="TraceEventContract"/> exposes an emission
/// guard (<see cref="TraceEventContract.CarriesOnlySanctionedFields"/>) that fails loud
/// if an event ever serializes a field outside the sanctioned identifier/count set.
/// </para>
/// <para>
/// <b>Discriminated flat shape.</b> One record type carries every trace event; the
/// <see cref="Kind"/> discriminator (<see cref="TraceEventKind"/>) selects which nullable
/// fields are populated. A homogeneous stream is trivial to serialize, order, embed in an
/// arbitrary host container (D-F4 host-embeddable), and read tolerantly.
/// </para>
/// <para>
/// <b>Tolerant-reader + additive-only versioning.</b> <see cref="Version"/> stamps every
/// event with <see cref="TraceEventContract.SchemaVersion"/>. Consumers deserialize with
/// <see cref="TraceEventContract.SerializerOptions"/>, which IGNORES unknown properties —
/// so a v1 reader survives a future v1.x that adds fields. Evolution is additive-only:
/// new optional fields + new <see cref="TraceEventKind"/> members, never a rename or a
/// type change of an existing field.
/// </para>
/// <para>
/// <b>Marker → TraceEvent mapping</b> (no duplicate event types are invented for markers
/// that already exist):
/// <list type="bullet">
///   <item><see cref="SessionToolChain"/> → one <see cref="TraceEventKind.ToolChain"/>
///   event (turn + call count) followed, in call order, by one
///   <see cref="TraceEventKind.ToolCall"/> event per <see cref="SessionToolCall"/>.</item>
///   <item><see cref="SessionGate"/> → one <see cref="TraceEventKind.Gate"/> event
///   (gate id / kind / status / side-effect class / missing field NAMES / output key ref).
///   A <c>pending</c> gate projects as a partial event with no resolution — see
///   "Partial states" below.</item>
///   <item><see cref="TraceContextFingerprint"/> → one <see cref="TraceEventKind.Context"/>
///   event. This is the ONE new type (justified below) — the ledger has no
///   ContextEnvelope-fingerprint marker yet.</item>
/// </list>
/// </para>
/// <para>
/// <b>New-type justification (CLAUDE.md §11).</b> The <see cref="TraceEventKind.Context"/>
/// event + its <see cref="TraceContextFingerprint"/> input are the only NEW surface:
/// (1) <i>Existing</i> — no ledger marker records "which context slices grounded this
/// turn"; ToolChain records tool calls, Gate records approvals, Output records results,
/// but the context-selection leg of FR-A1-09's request→context→tools→gate→outcome trace
/// has no marker. (2) <i>Extension</i> — it cannot extend a ToolChain or Gate marker
/// without conflating context selection with tool execution. (3)
/// <i>Cost-of-doing-nothing</i> — without it the traceability view (task 038) cannot show
/// WHY a turn was grounded the way it was, and the ContextEnvelope fingerprint (FR-A0-01 /
/// task 015) it references would have no trace anchor. It stays identifier/count-only
/// (fingerprint id + slice count), so it does not weaken NFR-07.
/// </para>
/// <para>
/// <b>Ordering.</b> A projected stream is totally ordered by
/// (<see cref="Turn"/> asc, kind-order asc, input order asc) with a monotonic
/// <see cref="Sequence"/> assigned during projection; kind-order is
/// context(0) → tool_chain(1) → tool_call(2) → gate(3) so a turn reads as
/// context → tools selected → tools executed → gate. Consumers order by
/// <see cref="Sequence"/> alone.
/// </para>
/// <para>
/// <b>Partial states.</b> A <see cref="TraceEventKind.Gate"/> event may be unresolved
/// (<c>status=pending</c>, no <see cref="OutputKey"/>) — the trace shows the gate without
/// an outcome. A <see cref="TraceEventKind.ToolChain"/> with <see cref="ToolCallCount"/>
/// 0 is valid (a turn that selected no tools). Consumers MUST render partial events
/// rather than dropping them.
/// </para>
/// </remarks>
public sealed record TraceEvent
{
    /// <summary>Contract version stamp — always <see cref="TraceEventContract.SchemaVersion"/> for v1.</summary>
    public required string Version { get; init; }

    /// <summary>Monotonic 0-based position in the projected stream. Consumers order by this alone.</summary>
    public required int Sequence { get; init; }

    /// <summary>1-based session turn this event belongs to.</summary>
    public required int Turn { get; init; }

    /// <summary>Discriminator selecting which fields are populated — see <see cref="TraceEventKind"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>UTC timestamp copied from the source ledger marker.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    // ── Context kind (TraceEventKind.Context) — identifiers/counts only ──

    /// <summary>Opaque ContextEnvelope fingerprint id (FR-A0-01). Identifier only — never slice content.</summary>
    public string? FingerprintId { get; init; }

    /// <summary>Number of context slices the fingerprint covered. Count only.</summary>
    public int? ContextSliceCount { get; init; }

    // ── ToolChain / ToolCall kinds — identifiers/filters/counts only (NFR-07) ──

    /// <summary>For <see cref="TraceEventKind.ToolChain"/>: number of tool calls in the turn's chain.</summary>
    public int? ToolCallCount { get; init; }

    /// <summary>For <see cref="TraceEventKind.ToolCall"/>: namespaced tool id (<c>sprk_analysistool</c> / handler id).</summary>
    public string? ToolId { get; init; }

    /// <summary>
    /// Compact identifier/filter summary of the call (e.g. <c>"matterId=123; top=5"</c>), copied verbatim
    /// from <see cref="SessionToolCall.ArgsSummary"/>, whose ledger contract already forbids document
    /// text, user content, or verbatim argument payloads (NFR-07).
    /// </summary>
    public string? ArgsSummary { get; init; }

    /// <summary>Number of results the tool returned, when countable. Count only.</summary>
    public int? ResultCount { get; init; }

    /// <summary>Citation / source ids the call emitted (ids only — never quotes).</summary>
    public IReadOnlyList<string>? Citations { get; init; }

    /// <summary>Wall-clock duration of the call in milliseconds.</summary>
    public long? DurationMs { get; init; }

    // ── Gate kind — identifiers only (NFR-07) ──

    /// <summary>Stable gate id correlating a pending entry with its resolution entry.</summary>
    public string? GateId { get; init; }

    /// <summary>Gate kind: <c>confirmation</c> | <c>elicitation</c>.</summary>
    public string? GateKind { get; init; }

    /// <summary>Gate state: <c>pending | confirmed | rejected | expired | superseded</c>.</summary>
    public string? Status { get; init; }

    /// <summary>Declared side-effect class driving the gate: <c>read | write | communicate | pure</c>.</summary>
    public string? SideEffectClass { get; init; }

    /// <summary>Binding id the gated invocation targets, when known.</summary>
    public string? BindingId { get; init; }

    /// <summary>For elicitation gates: input-schema field NAMES still missing (identifiers only — never captured values).</summary>
    public IReadOnlyList<string>? MissingFields { get; init; }

    /// <summary>Ledger key (<c>{bindingId}@t{n}</c>) of the Output entry produced once the gate resolved, when applicable.</summary>
    public string? OutputKey { get; init; }
}

/// <summary>
/// The one NEW ledger-adjacent input introduced by TraceEvent v1 (justified on
/// <see cref="TraceEvent"/>): a no-content fingerprint of the context slices that grounded
/// a turn, projected into a <see cref="TraceEventKind.Context"/> event. When ContextEnvelope
/// v1 (FR-A0-01 / task 015) lands, its fingerprint id binds to <see cref="FingerprintId"/>;
/// until then callers may synthesize this from whatever context-selection metadata they hold.
/// Identifier + count only — carries no slice content (NFR-07).
/// </summary>
public sealed record TraceContextFingerprint
{
    /// <summary>1-based session turn the context selection applied to.</summary>
    public required int Turn { get; init; }

    /// <summary>Opaque fingerprint id (later: the ContextEnvelope v1 fingerprint). Identifier only.</summary>
    public required string FingerprintId { get; init; }

    /// <summary>Number of context slices the fingerprint covered. Count only.</summary>
    public required int SliceCount { get; init; }

    /// <summary>UTC timestamp the context selection was made.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Stable <see cref="TraceEvent.Kind"/> discriminator values. Additive-only.</summary>
public static class TraceEventKind
{
    /// <summary>Context slices selected for a turn (from <see cref="TraceContextFingerprint"/>).</summary>
    public const string Context = "context";

    /// <summary>A turn's tool-call chain header (from <see cref="SessionToolChain"/>).</summary>
    public const string ToolChain = "tool_chain";

    /// <summary>One tool invocation within a chain (from <see cref="SessionToolCall"/>).</summary>
    public const string ToolCall = "tool_call";

    /// <summary>A confirmation/elicitation gate marker (from <see cref="SessionGate"/>).</summary>
    public const string Gate = "gate";
}

/// <summary>
/// Version stamp, canonical serializer options (tolerant-reader), and the NFR-07 emission
/// guard for TraceEvent v1. Separated from the DTO so the sanctioned-field allow-list is a
/// deliberately curated list — adding a field to <see cref="TraceEvent"/> WITHOUT curating it
/// here makes <see cref="CarriesOnlySanctionedFields"/> fail, which is the point (a new field
/// cannot silently ship as trace telemetry).
/// </summary>
public static class TraceEventContract
{
    /// <summary>The v1 schema version stamped on every <see cref="TraceEvent.Version"/>.</summary>
    public const string SchemaVersion = "trace-event/v1";

    /// <summary>
    /// Canonical serializer options for the read surface: camelCase names, unknown properties
    /// IGNORED (tolerant reader — a v1 consumer survives a future additive v1.x), enum-as-string.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        // JsonSerializerDefaults.Web already ignores unknown members on read (tolerant reader);
        // stated explicitly for intent.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>
    /// The COMPLETE set of camelCase JSON property names a sanctioned TraceEvent may carry —
    /// every one an identifier, filter, count, duration, or citation-id. There is deliberately
    /// no free-text content property. Curated by hand (not derived from the type) so a new
    /// <see cref="TraceEvent"/> field is caught by <see cref="CarriesOnlySanctionedFields"/>
    /// until it is reviewed and added here.
    /// </summary>
    public static readonly IReadOnlySet<string> SanctionedFieldNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "version", "sequence", "turn", "kind", "timestamp",
        "fingerprintId", "contextSliceCount",
        "toolCallCount", "toolId", "argsSummary", "resultCount", "citations", "durationMs",
        "gateId", "gateKind", "status", "sideEffectClass", "bindingId", "missingFields", "outputKey",
    };

    /// <summary>
    /// NFR-07 emission guard: true iff <paramref name="serializedEvent"/> carries ONLY
    /// <see cref="SanctionedFieldNames"/> — i.e. no field exists where prompt/response content
    /// could live. Enforced against the serialized JSON (not the CLR type) so that a
    /// content-bearing property injected downstream is rejected. Note the intentional asymmetry
    /// with the tolerant reader: CONSUMERS ignore unknown fields (forward-compat), but EMISSION
    /// asserts a closed sanctioned set (no-content).
    /// </summary>
    public static bool CarriesOnlySanctionedFields(JsonElement serializedEvent)
    {
        if (serializedEvent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in serializedEvent.EnumerateObject())
        {
            if (!SanctionedFieldNames.Contains(property.Name))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Reference PRODUCER (read projection): materializes an ordered <see cref="TraceEvent"/>
/// stream from ledger markers a caller already loaded via the session API. No store, no I/O,
/// no ledger mutation — a pure projection over ADR-040 markers (ledger stays source of truth).
/// </summary>
public static class TraceEventProjection
{
    private static int KindOrder(string kind) => kind switch
    {
        TraceEventKind.Context => 0,
        TraceEventKind.ToolChain => 1,
        TraceEventKind.ToolCall => 2,
        TraceEventKind.Gate => 3,
        _ => 99,
    };

    /// <summary>
    /// Projects the supplied ledger markers into a totally ordered, sequence-stamped
    /// <see cref="TraceEvent"/> stream. Ordering: (turn, kind-order, input order). Any argument
    /// may be null/empty — a real ledger commonly has gates on some turns and none on others.
    /// </summary>
    public static IReadOnlyList<TraceEvent> Project(
        IReadOnlyList<TraceContextFingerprint>? contextFingerprints = null,
        IReadOnlyList<SessionToolChain>? toolChains = null,
        IReadOnlyList<SessionGate>? gates = null)
    {
        // Stage (sortKey, event-without-sequence) pairs, then order + stamp Sequence.
        var staged = new List<(int Turn, int KindOrder, int InputOrder, TraceEvent Event)>();
        var inputOrder = 0;

        foreach (var fp in contextFingerprints ?? Array.Empty<TraceContextFingerprint>())
        {
            staged.Add((fp.Turn, KindOrder(TraceEventKind.Context), inputOrder++, new TraceEvent
            {
                Version = TraceEventContract.SchemaVersion,
                Sequence = 0,
                Turn = fp.Turn,
                Kind = TraceEventKind.Context,
                Timestamp = fp.CreatedAt,
                FingerprintId = fp.FingerprintId,
                ContextSliceCount = fp.SliceCount,
            }));
        }

        foreach (var chain in toolChains ?? Array.Empty<SessionToolChain>())
        {
            staged.Add((chain.Turn, KindOrder(TraceEventKind.ToolChain), inputOrder++, new TraceEvent
            {
                Version = TraceEventContract.SchemaVersion,
                Sequence = 0,
                Turn = chain.Turn,
                Kind = TraceEventKind.ToolChain,
                Timestamp = chain.CreatedAt,
                ToolCallCount = chain.Calls.Count,
            }));

            foreach (var call in chain.Calls)
            {
                staged.Add((chain.Turn, KindOrder(TraceEventKind.ToolCall), inputOrder++, new TraceEvent
                {
                    Version = TraceEventContract.SchemaVersion,
                    Sequence = 0,
                    Turn = chain.Turn,
                    Kind = TraceEventKind.ToolCall,
                    Timestamp = chain.CreatedAt,
                    ToolId = call.ToolId,
                    ArgsSummary = call.ArgsSummary,
                    ResultCount = call.ResultCount,
                    Citations = call.Citations,
                    DurationMs = call.DurationMs,
                }));
            }
        }

        foreach (var gate in gates ?? Array.Empty<SessionGate>())
        {
            staged.Add((gate.Turn, KindOrder(TraceEventKind.Gate), inputOrder++, new TraceEvent
            {
                Version = TraceEventContract.SchemaVersion,
                Sequence = 0,
                Turn = gate.Turn,
                Kind = TraceEventKind.Gate,
                Timestamp = gate.CreatedAt,
                GateId = gate.GateId,
                GateKind = gate.Kind,
                Status = gate.Status,
                SideEffectClass = gate.SideEffectClass,
                BindingId = gate.BindingId,
                MissingFields = gate.MissingFields,
                OutputKey = gate.OutputKey,
            }));
        }

        return staged
            .OrderBy(s => s.Turn)
            .ThenBy(s => s.KindOrder)
            .ThenBy(s => s.InputOrder)
            .Select((s, index) => s.Event with { Sequence = index })
            .ToList();
    }
}

/// <summary>
/// Reference CONSUMER: renders a <see cref="TraceEvent"/> stream as a plain-text decision
/// trace, one line per event, identifiers/counts only. Proves the read surface is consumable
/// without reaching into ledger internals; the FR-A1-09 widget (task 038) is the production
/// consumer that replaces this stub with a rich view.
/// </summary>
public static class TraceEventRenderer
{
    /// <summary>Renders the (already-ordered) stream to one trace line per event. No content — ids/counts only.</summary>
    public static IReadOnlyList<string> Render(IEnumerable<TraceEvent> events)
    {
        var lines = new List<string>();
        foreach (var e in events.OrderBy(e => e.Sequence))
        {
            lines.Add(e.Kind switch
            {
                TraceEventKind.Context =>
                    $"t{e.Turn} context fingerprint={e.FingerprintId} slices={e.ContextSliceCount}",
                TraceEventKind.ToolChain =>
                    $"t{e.Turn} tool-chain calls={e.ToolCallCount}",
                TraceEventKind.ToolCall =>
                    $"t{e.Turn}   tool={e.ToolId} results={e.ResultCount} durationMs={e.DurationMs} citations={e.Citations?.Count ?? 0}",
                TraceEventKind.Gate =>
                    $"t{e.Turn} gate {e.GateKind} id={e.GateId} status={e.Status} sideEffect={e.SideEffectClass}",
                _ => $"t{e.Turn} {e.Kind}",
            });
        }

        return lines;
    }
}
