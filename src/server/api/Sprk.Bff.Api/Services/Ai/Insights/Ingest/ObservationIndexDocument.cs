using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Insights.Ingest;

/// <summary>
/// AI Search document shape for the <c>spaarke-insights-index</c> per SPEC §3.4 schema
/// (see <c>infra/insights/schemas/spaarke-insights-index.index.json</c>). One document
/// per Observation when written via the D-P7 universal ingest pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Field mapping</b> from <see cref="Models.Insights.ObservationArtifact"/>:
/// <list type="bullet">
///   <item><c>Id</c> = Observation.Id (e.g., <c>obs:M-2024-0341:outcomeCategory:doc-abc</c>)</item>
///   <item><c>ArtifactType</c> = <c>"observation"</c> (discriminator per SPEC §3.4.2)</item>
///   <item><c>TenantId</c> = Observation.TenantId</item>
///   <item><c>Subject</c> = Observation.Subject</item>
///   <item><c>Predicate</c> = Observation.Predicate</item>
///   <item><c>ValueJson</c> = serialized Observation.Value.Raw + DisplayHint</item>
///   <item><c>Confidence</c> = Observation.Confidence</item>
///   <item><c>Evidence</c> = Observation.Evidence (each <c>EvidenceRef</c> projected to a sub-doc)</item>
///   <item><c>AsOf</c> = Observation.AsOf</item>
///   <item><c>ProducedBy</c> = Observation.ProducedBy.Id (string field per index schema)</item>
///   <item><c>Content</c> = predicate + value + quote concatenation (for semantic search)</item>
///   <item><c>ContentVector</c> = embedding of <c>Content</c> via <c>IOpenAiClient.GenerateEmbeddingAsync</c></item>
///   <item><c>Status</c> = <c>"produced"</c> (Observation lifecycle starter; updated via D-P11 review surface)</item>
/// </list>
/// </para>
/// <para>
/// <b>Why a flat string for value</b>: the index schema declares <c>value</c> as a
/// complex type with optional <c>raw.matterType</c> + <c>raw.dealSizeBucket</c> + nested
/// scope. For Phase 1 universal-ingest Observations we don't yet emit those structured
/// fields (the LLM returns simple typed values like enum strings or numbers). We write
/// the raw JSON to <c>valueJson</c> (a top-level string field on the schema) so
/// consumers can deserialize without forcing the complex-type schema. The complex
/// <c>value.raw.*</c> filterable subfields will be populated in Phase 1.5+ when richer
/// extraction layers ship.
/// </para>
/// </remarks>
internal sealed record ObservationIndexDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    [JsonPropertyName("artifactType")]
    public string ArtifactType { get; init; } = "observation";

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("predicate")]
    public required string Predicate { get; init; }

    /// <summary>
    /// Flat string holding JSON-serialized Observation value (raw + displayHint). Maps to
    /// the index schema's top-level <c>valueJson</c> field (per
    /// <c>spaarke-insights-index.index.json</c>) which is retrievable but not filterable.
    /// Consumers parse on read.
    /// </summary>
    [JsonPropertyName("valueJson")]
    public required string ValueJson { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("evidence")]
    public required IReadOnlyList<EvidenceIndexEntry> Evidence { get; init; }

    [JsonPropertyName("asOf")]
    public required DateTimeOffset AsOf { get; init; }

    [JsonPropertyName("producedBy")]
    public required string ProducedBy { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("contentVector")]
    public required IReadOnlyList<float> ContentVector { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "produced";

    /// <summary>
    /// Wave D6 (task 035) — top-level scope ComplexType per design-a6 §4. Hybrid
    /// backward-compat: <see cref="ScopeIndexEntry.MatterId"/> is dual-written for matter
    /// subjects to preserve NFR-08 (Phase 1 RAG queries that filter by
    /// <c>scope/matterId</c> keep working); <see cref="ScopeIndexEntry.EntityType"/> +
    /// <see cref="ScopeIndexEntry.EntityId"/> are the canonical generalized fields.
    /// Optional (nullable) — Observations written before Wave D6 omit this property; the
    /// reader's OR-filter (<c>scope/matterId eq … OR (scope/entityType eq 'matter' and
    /// scope/entityId eq …)</c>) keeps them findable.
    /// </summary>
    [JsonPropertyName("scope")]
    public ScopeIndexEntry? Scope { get; init; }
}

/// <summary>
/// Evidence sub-document matching the <c>spaarke-insights-index</c> evidence collection
/// schema (refType + ref + quote).
/// </summary>
internal sealed record EvidenceIndexEntry
{
    [JsonPropertyName("refType")]
    public required string RefType { get; init; }

    [JsonPropertyName("ref")]
    public required string Ref { get; init; }

    [JsonPropertyName("quote")]
    public string? Quote { get; init; }
}

/// <summary>
/// Wave D6 (task 035) — projection of <see cref="Models.Insights.Scope"/> onto the index
/// schema's top-level <c>scope</c> ComplexType. Mirrors the fields defined in
/// <c>infra/insights/schemas/spaarke-insights-index.index.json</c>. All fields are
/// nullable strings; the writer populates them per design-a6 §4.4:
/// <list type="bullet">
///   <item>matter subjects: matterId + entityType="matter" + entityId=&lt;guid&gt; (dual-write per <see cref="AiSearchOptions.DualWriteScopeMatterId"/>)</item>
///   <item>project subjects: entityType="project" + entityId=&lt;guid&gt; (matterId null)</item>
///   <item>invoice subjects: entityType="invoice" + entityId=&lt;guid&gt; (matterId null)</item>
/// </list>
/// </summary>
internal sealed record ScopeIndexEntry
{
    [JsonPropertyName("matterId")]
    public string? MatterId { get; init; }

    [JsonPropertyName("entityType")]
    public string? EntityType { get; init; }

    [JsonPropertyName("entityId")]
    public string? EntityId { get; init; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("practiceArea")]
    public string? PracticeArea { get; init; }
}
