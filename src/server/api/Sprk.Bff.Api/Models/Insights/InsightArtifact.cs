using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Insights;

/// <summary>
/// The canonical wire shape returned by the Spaarke Insights Engine to every consumer.
/// Implements the four-tier taxonomy (Fact / Observation / Precedent / Inference) per
/// <c>design.md §2.1</c> and the envelope schema per <c>design.md §2.2</c> / <c>SPEC §3.4</c>.
/// <para>
/// Zone B POCO per SPEC §3.5.4 — pure record, no AI internals imports. Consumers
/// (D-P15 endpoint, D-P11 review surface, D-P4 projection sync) bind to this shape.
/// </para>
/// <para>
/// Polymorphic discrimination uses the <c>type</c> JSON property mapped to one of
/// <c>fact</c>, <c>observation</c>, <c>precedent</c>, <c>inference</c>. System.Text.Json
/// round-trips each tier to its concrete subtype.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FactArtifact), typeDiscriminator: "fact")]
[JsonDerivedType(typeof(ObservationArtifact), typeDiscriminator: "observation")]
[JsonDerivedType(typeof(PrecedentArtifact), typeDiscriminator: "precedent")]
[JsonDerivedType(typeof(InferenceArtifact), typeDiscriminator: "inference")]
public abstract record InsightArtifact
{
    /// <summary>Stable identifier for this artifact (e.g., <c>obs:M-2024-0341:outcomeCategory:doc-abc123</c>).</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// What this artifact is about. Free-form scheme-prefixed identifier
    /// (e.g., <c>matter:M-1234</c>, <c>document:abc</c>, <c>party:acme</c>, <c>pattern:ip-licensing-bigfirm-llp</c>).
    /// Per SPEC §3.4 worked examples.
    /// </summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>
    /// The claim being made about the subject (e.g., <c>outcomeCategory</c>, <c>settlementAmount</c>,
    /// <c>predictedCost</c>, <c>pattern</c>). See SPEC §3.4.1–§3.4.3.
    /// </summary>
    [JsonPropertyName("predicate")]
    public required string Predicate { get; init; }

    /// <summary>The value of the claim, with display-hint metadata.</summary>
    [JsonPropertyName("value")]
    public required Value Value { get; init; }

    /// <summary>
    /// Evidence supporting the claim. Per D-04 (provenance is the API contract),
    /// every Observation / Precedent / Inference carries evidence; surfaces unable
    /// to render provenance cannot display these tiers. May be empty for Facts.
    /// Per D-48 (EvidenceGuard), evidence-bearing tools must reject empty arrays at runtime —
    /// the POCO itself does not enforce non-empty.
    /// </summary>
    [JsonPropertyName("evidence")]
    public IReadOnlyList<EvidenceRef> Evidence { get; init; } = Array.Empty<EvidenceRef>();

    /// <summary>Wall-clock timestamp when this artifact was produced.</summary>
    [JsonPropertyName("asOf")]
    public required DateTimeOffset AsOf { get; init; }

    /// <summary>Identity of the producer (query / playbook / agent) with version. Per D-05, version is mandatory for Observations.</summary>
    [JsonPropertyName("producedBy")]
    public required ProducedBy ProducedBy { get; init; }

    /// <summary>The contextual scope of the artifact (tenant, matter, practice area, etc.).</summary>
    [JsonPropertyName("scope")]
    public required Scope Scope { get; init; }

    /// <summary>
    /// Tenant identifier, also redundantly carried at the substrate level (per design.md §2.2 notes:
    /// "<c>tenantId</c> is a top-level field (not just inside <c>scope</c>) so it can be a filterable
    /// index field at the substrate level").
    /// </summary>
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    /// <summary>
    /// Optional vector embedding (populated for Observations and Inferences per design.md §2.2 notes;
    /// null for Facts, which are typically retrieved by direct filter, not similarity).
    /// </summary>
    [JsonPropertyName("embedding")]
    public IReadOnlyList<float>? Embedding { get; init; }

    /// <summary>Optional start of temporal validity (e.g., "total spend as of 2026-05-19").</summary>
    [JsonPropertyName("validFrom")]
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>Optional end of temporal validity; null means open-ended.</summary>
    [JsonPropertyName("validTo")]
    public DateTimeOffset? ValidTo { get; init; }
}

/// <summary>
/// Deterministic claim computed over a system of record (e.g., <c>Matter M-1234 was pending 287 days</c>).
/// Confidence is always 1.0. Per design.md §2.1, Facts are stated directly without hedging.
/// </summary>
public sealed record FactArtifact : InsightArtifact
{
    /// <summary>Facts are always certain; constant 1.0 per design.md §2.1.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 1.0;
}

/// <summary>
/// Probabilistic claim extracted from document content by an Insights-mode playbook (e.g.,
/// <c>Matter M-1234 outcome quality: favorable (0.92)</c>). Per design.md §2.1, carries confidence
/// in [0.0, 1.0] and evidence. Per D-05, <see cref="InsightArtifact.ProducedBy"/>.<c>Version</c> is mandatory.
/// </summary>
public sealed record ObservationArtifact : InsightArtifact
{
    /// <summary>Producer confidence in [0.0, 1.0]. Per D-63 / D-P10, fields below configured thresholds are dropped pre-persistence.</summary>
    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }
}

/// <summary>
/// SME-confirmed institutional pattern derived from multiple supporting Observations (e.g.,
/// <c>In IP-licensing matters with a 12-month cure period, settlement rates rise 18%</c>).
/// Per design.md §2.1 and the SPEC §3.4.2 worked example, Precedents do NOT carry a probabilistic
/// confidence — they are SME-confirmed, not model-emitted. The <c>confidence</c> field is therefore
/// omitted from the wire format for Precedents.
/// </summary>
public sealed record PrecedentArtifact : InsightArtifact
{
    /// <summary>
    /// Lifecycle status of the Precedent. Phase 1 manual-SME-author flow produces <c>confirmed</c>;
    /// Phase 1.5+ system-proposed flow introduces <c>tentative</c> and <c>underDriftReview</c>.
    /// Per SPEC §3.4.2 worked example.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>
/// Synthesized claim produced on demand by an Insights-mode playbook over Facts + Observations + Precedents
/// (e.g., <c>Predicted cost for this new matter: ~$280K (confidence 0.74), based on 12 comparable matters</c>).
/// Per design.md §2.1, Inferences are never authoritatively stored; per-execution cache (D-P13) only.
/// </summary>
public sealed record InferenceArtifact : InsightArtifact
{
    /// <summary>Synthesis confidence in [0.0, 1.0].</summary>
    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    /// <summary>
    /// Optional reasoning summary the synthesis playbook emitted alongside the inferred value.
    /// Per design.md §3.3 (Inference assembly).
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }
}

/// <summary>
/// The value of an artifact's claim plus a hint to surfaces about how to render it.
/// Per design.md §2.2 envelope: <c>value: { raw: &lt;typed value&gt;, displayHint: "currency-usd | percentage | duration-days | enum | text" }</c>.
/// <para>
/// <see cref="Raw"/> is typed as <see cref="JsonElement"/> so the envelope can carry arbitrary JSON
/// (string, number, array, nested object — see SPEC §3.4.2 Precedent example where <c>raw</c> is a
/// nested object with <c>patternTitle</c>, <c>scope</c>, <c>supportingMatters</c>, etc.).
/// </para>
/// </summary>
public sealed record Value
{
    /// <summary>The raw value as a JSON element. Consumers parse based on <see cref="DisplayHint"/>.</summary>
    [JsonPropertyName("raw")]
    public required JsonElement Raw { get; init; }

    /// <summary>
    /// Render hint for surfaces. Canonical values per design.md §2.2:
    /// <c>currency-usd</c>, <c>percentage</c>, <c>duration-days</c>, <c>enum</c>, <c>text</c>,
    /// <c>precedent-statement</c> (per SPEC §3.4.2).
    /// </summary>
    [JsonPropertyName("displayHint")]
    public required string DisplayHint { get; init; }
}

/// <summary>
/// Identity of the producer of an artifact. Per design.md §2.2, <see cref="Version"/> is mandatory
/// for Observations (D-05) — enables targeted re-extraction when extraction playbooks ship a new version (D-62).
/// </summary>
public sealed record ProducedBy
{
    /// <summary>
    /// The kind of producer. Canonical values per design.md §2.2:
    /// <c>query</c> (deterministic), <c>playbook</c> (probabilistic), <c>agent</c> (synthesis).
    /// </summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>
    /// The producer identifier (e.g., <c>playbook://outcome-extraction@v1</c>,
    /// <c>query://matter-duration</c>, <c>agent://insights-v1</c>, <c>manual-sme-author</c>).
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Version string of the producer. Mandatory for Observations per D-05. Drives D-62 targeted
    /// re-extraction (e.g., <c>v1 → v2</c> re-runs only Observations whose <c>Version</c> is <c>v1</c>).
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

/// <summary>
/// Contextual scope of an artifact. All fields except <see cref="TenantId"/> (also at envelope level)
/// are optional per design.md §2.2 envelope.
/// </summary>
public sealed record Scope
{
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    [JsonPropertyName("matterId")]
    public string? MatterId { get; init; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; init; }

    [JsonPropertyName("practiceArea")]
    public string? PracticeArea { get; init; }

    [JsonPropertyName("jurisdiction")]
    public string? Jurisdiction { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }
}
