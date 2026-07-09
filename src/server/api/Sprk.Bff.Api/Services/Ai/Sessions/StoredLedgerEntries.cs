using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Cosmos-shape parallels of the session-ledger entry records
/// (<c>Sprk.Bff.Api.Models.Ai.Chat.SessionOutput / SessionToolChain / SessionWidgetEvent /
/// SessionGate</c>) — ADR-040 / FR-P0-01.
///
/// <para><b>Why parallel Cosmos shapes rather than reusing the model records directly</b>:
/// same rationale as <see cref="StoredUploadedFile"/> (task 072) and
/// <see cref="StoredWorkspaceTab"/> (task 065) — the domain records use PascalCase with no
/// <c>[JsonPropertyName]</c> attributes, while every property on <see cref="StoredSession"/>
/// is camelCase on the wire. Additionally, <see cref="StoredSession"/> travels through TWO
/// serializers: System.Text.Json on the Redis hot tier (<c>ITenantCache</c>) and the Cosmos
/// SDK's built-in Newtonsoft-based serializer (camelCase naming policy) on the warm tier.
/// <c>System.Text.Json.JsonElement</c> does NOT round-trip through Newtonsoft — so the
/// schema-validated <c>Payload</c> travels as a raw JSON <b>string</b> here and is
/// re-hydrated to <c>JsonElement</c> by the mappers in
/// <see cref="SessionPersistenceService"/>.</para>
///
/// <para><b>Placement (CLAUDE.md §10 / ADR-013)</b>: in-process DTOs on the existing
/// <see cref="SessionPersistenceService"/> pipeline. No new DI module, no new service,
/// no new NuGet packages — purely additive to <see cref="StoredSession"/> (additive schema
/// evolution per ADR-015; partition key <c>/tenantId</c> unchanged; older documents
/// deserialize cleanly to empty lists).</para>
/// </summary>
public class StoredSessionOutput
{
    /// <summary>Addressable ledger key: <c>{bindingId}@t{n}</c>.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Binding id that produced this output (<c>"loop"</c> for loop-native outputs).</summary>
    [JsonPropertyName("bindingId")]
    public string BindingId { get; set; } = string.Empty;

    /// <summary>Stable use-case vocabulary id.</summary>
    [JsonPropertyName("ucId")]
    public string UcId { get; set; } = string.Empty;

    /// <summary>1-based session turn number.</summary>
    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    /// <summary>Rendering disposition (the only rendering contract — ADR-040).</summary>
    [JsonPropertyName("disposition")]
    public string Disposition { get; set; } = string.Empty;

    /// <summary>
    /// Schema-validated payload as raw JSON text. String (not <c>JsonElement</c>) so the
    /// value survives BOTH the System.Text.Json Redis path and the Newtonsoft Cosmos path.
    /// </summary>
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "null";

    /// <summary>Overlay target widget instance id, when disposition is <c>overlay</c>.</summary>
    [JsonPropertyName("widgetId")]
    public string? WidgetId { get; set; }

    /// <summary>Citations / document ids / ledger keys the output was grounded on.</summary>
    [JsonPropertyName("sourceRefs")]
    public List<string>? SourceRefs { get; set; }

    /// <summary>UTC timestamp the output was written to the ledger.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Cosmos-shape parallel of <c>Sprk.Bff.Api.Models.Ai.Chat.SessionToolChain</c>.
/// Identifiers / filters / counts / citation ids only — never verbatim content (NFR-07).
/// </summary>
public class StoredToolChain
{
    /// <summary>1-based session turn number this chain executed on.</summary>
    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    /// <summary>Ordered tool calls executed during the turn.</summary>
    [JsonPropertyName("calls")]
    public List<StoredToolCall> Calls { get; set; } = [];

    /// <summary>UTC timestamp the chain record was written.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Cosmos-shape parallel of <c>Sprk.Bff.Api.Models.Ai.Chat.SessionToolCall</c>.
/// </summary>
public class StoredToolCall
{
    /// <summary>Namespaced tool id.</summary>
    [JsonPropertyName("toolId")]
    public string ToolId { get; set; } = string.Empty;

    /// <summary>Identifier/filter summary — MUST NOT contain content (NFR-07).</summary>
    [JsonPropertyName("argsSummary")]
    public string? ArgsSummary { get; set; }

    /// <summary>Number of results the tool returned, when countable.</summary>
    [JsonPropertyName("resultCount")]
    public int? ResultCount { get; set; }

    /// <summary>Citation / source ids the call emitted (ids only).</summary>
    [JsonPropertyName("citations")]
    public List<string>? Citations { get; set; }

    /// <summary>Wall-clock duration of the call in milliseconds.</summary>
    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; set; }
}

/// <summary>
/// Cosmos-shape parallel of <c>Sprk.Bff.Api.Models.Ai.Chat.SessionWidgetEvent</c>.
/// </summary>
public class StoredWidgetEvent
{
    /// <summary>Widget instance id the action occurred in.</summary>
    [JsonPropertyName("widgetId")]
    public string WidgetId { get; set; } = string.Empty;

    /// <summary>Event type vocabulary (e.g. <c>selection</c>, <c>highlight</c>, <c>edit</c>).</summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>1-based session turn number the action occurred on.</summary>
    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    /// <summary>Ledger keys / document ids the event references.</summary>
    [JsonPropertyName("entryRefs")]
    public List<string>? EntryRefs { get; set; }

    /// <summary>
    /// Structured event payload as raw JSON text (string for the same dual-serializer
    /// reason as <see cref="StoredSessionOutput.Payload"/>). Null when the event carries
    /// refs only.
    /// </summary>
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    /// <summary>UTC timestamp the event was written.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Cosmos-shape parallel of <c>Sprk.Bff.Api.Models.Ai.Chat.SessionGate</c>.
/// </summary>
public class StoredGate
{
    /// <summary>Stable gate id correlating pending and resolution entries.</summary>
    [JsonPropertyName("gateId")]
    public string GateId { get; set; } = string.Empty;

    /// <summary>Gate kind: <c>confirmation</c> or <c>elicitation</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gate state: <c>pending | confirmed | rejected | expired | superseded</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>1-based session turn number the gate was raised on.</summary>
    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    /// <summary>Binding id the gated invocation targets, when known.</summary>
    [JsonPropertyName("bindingId")]
    public string? BindingId { get; set; }

    /// <summary>Declared side-effect class (<c>read | write | communicate | pure</c>).</summary>
    [JsonPropertyName("sideEffectClass")]
    public string? SideEffectClass { get; set; }

    /// <summary>For elicitation gates: missing input-schema field names (identifiers only — NFR-07).</summary>
    [JsonPropertyName("missingFields")]
    public List<string>? MissingFields { get; set; }

    /// <summary>Ledger key of the Output entry produced once the gate resolved.</summary>
    [JsonPropertyName("outputKey")]
    public string? OutputKey { get; set; }

    /// <summary>UTC timestamp the gate entry was written.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp the gate was resolved; null while pending.</summary>
    [JsonPropertyName("resolvedAt")]
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>
/// Cosmos-shape parallel of <c>Sprk.Bff.Api.Models.Ai.Chat.SessionContextFingerprint</c>
/// (AIR2-038 / FR-A1-09). Fingerprint identifier + slice count only — never slice
/// content (NFR-07). Additive to <see cref="StoredSession"/>; older documents
/// deserialize to an empty list.
/// </summary>
public class StoredContextFingerprint
{
    /// <summary>1-based session turn the context selection applied to.</summary>
    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    /// <summary>Opaque ContextEnvelope fingerprint id (identifier only — NFR-07).</summary>
    [JsonPropertyName("fingerprintId")]
    public string FingerprintId { get; set; } = string.Empty;

    /// <summary>Number of context slices the fingerprint covered (count only).</summary>
    [JsonPropertyName("sliceCount")]
    public int SliceCount { get; set; }

    /// <summary>UTC timestamp the context selection was made / entry was written.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}
