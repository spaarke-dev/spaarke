using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// Typed projection of the Layer 2 (D-P6) outcome-extraction LLM response. Carries the
/// per-field extracted value, verbatim evidence quote, and producer confidence — the inputs
/// the three mechanical gates (grounding → confidence → emission) operate on.
/// <para>
/// Lives in Zone A per <c>SPEC §3.5</c> — Layer 2 internal contract, NOT a wire shape.
/// Consumers: <c>GroundingVerifier</c> (D-P9), <see cref="IObservationEmitter"/> (D-P10),
/// and future re-extraction tooling that targets a specific <see cref="ProducedByVersion"/>.
/// </para>
/// <para>
/// Mirrors the LLM JSON schema published in <c>SPEC-phase-1-minimum.md §3.4</c> with one
/// transposition: the prompt returns three sibling maps (<c>fields</c>, <c>evidence</c>,
/// <c>confidence</c>) keyed by field name; this POCO collapses them into one
/// <see cref="Fields"/> dictionary keyed by field name where each entry carries all three.
/// The transposition happens in the Layer 2 node executor (D-P6) before this record is built.
/// </para>
/// </summary>
public sealed record ExtractionResult
{
    /// <summary>
    /// Subject of the extraction (the matter the source document belongs to). Free-form
    /// scheme-prefixed identifier per <c>SPEC §3.4.1</c> (e.g., <c>matter:M-2024-0341</c>).
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Reference to the source document the LLM extracted from. Populated as the primary
    /// <c>document</c> evidence ref on every emitted Observation. Typically an SPE URI
    /// (e.g., <c>spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx</c>).
    /// </summary>
    public required string DocumentRef { get; init; }

    /// <summary>
    /// Tenant the extraction belongs to. Mirrored onto each emitted Observation envelope
    /// (per <c>D-52</c> and <c>spaarke-insights-index</c> schema).
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Versioned producer identity for every emitted Observation. Phase 1 starter:
    /// <c>("playbook", "playbook://outcome-extraction@v1", "v1")</c>. Drives D-62 targeted
    /// re-extraction (<c>v1 → v2</c> re-runs only Observations whose <c>ProducedBy.Version</c>
    /// is <c>v1</c>).
    /// </summary>
    public required ProducerIdentity ProducedBy { get; init; }

    /// <summary>
    /// Wall-clock timestamp at which extraction completed. Stamped on every emitted Observation's
    /// <c>AsOf</c> field. Provided as input (not <c>DateTimeOffset.UtcNow</c>) so the entire
    /// extraction run has a consistent timestamp across all fields and so tests are deterministic.
    /// </summary>
    public required DateTimeOffset AsOf { get; init; }

    /// <summary>
    /// Per-field extraction results, keyed by the field name (the Observation's predicate —
    /// e.g., <c>outcomeCategory</c>, <c>settlementAmount</c>, <c>outcomeDate</c>,
    /// <c>matterDurationDays</c> per <c>SPEC-phase-1-minimum.md §3.4</c>).
    /// <para>
    /// Fields the LLM returned as null (not present in the document) MUST be omitted from
    /// this dictionary by the caller — D-P10 only sees fields the LLM actually attempted.
    /// </para>
    /// </summary>
    public required IReadOnlyDictionary<string, ExtractionField> Fields { get; init; } =
        new Dictionary<string, ExtractionField>();

    /// <summary>
    /// Optional matter scope context propagated to emitted Observations'
    /// <c>Models.Insights.Scope</c> envelope. <see cref="TenantId"/> is already required at the
    /// top level; this struct carries the OPTIONAL fields (practice area, jurisdiction, etc.).
    /// </summary>
    public ExtractionScope? Scope { get; init; }
}

/// <summary>
/// Per-field extraction record — the unit the three mechanical gates (grounding → confidence →
/// emission) operate on. Every field gets one Observation if it survives all three gates.
/// </summary>
public sealed record ExtractionField
{
    /// <summary>
    /// The extracted typed value as a <see cref="JsonElement"/> so the schema can carry
    /// strings, numbers, dates, and nested objects without custom converters (matches
    /// <see cref="Models.Insights.Value.Raw"/> for round-trip into the Observation envelope).
    /// </summary>
    public required JsonElement Value { get; init; }

    /// <summary>
    /// Verbatim quote from the source document that the LLM cited as evidence for
    /// <see cref="Value"/>. Verified by <c>GroundingVerifier</c> (D-P9) before this record
    /// reaches <see cref="IObservationEmitter"/>; if the quote did not appear in the source,
    /// the field is dropped upstream and never reaches D-P10.
    /// </summary>
    public required string Quote { get; init; }

    /// <summary>
    /// Producer confidence in [0.0, 1.0]. D-P10 compares this against the per-field threshold
    /// from <see cref="ConfidenceThresholdOptions"/>; below-threshold fields are dropped
    /// (and logged) without persisting.
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// Render hint that surfaces use for the emitted Observation's
    /// <c>Models.Insights.Value.DisplayHint</c> (e.g., <c>currency-usd</c>, <c>duration-days</c>,
    /// <c>enum</c>, <c>text</c>, <c>date</c>). Caller (Layer 2 node executor) is responsible
    /// for assigning this per-field — the LLM doesn't return display hints.
    /// </summary>
    public required string DisplayHint { get; init; }
}

/// <summary>
/// Versioned producer identity for D-62 prompt versioning. Phase 1 starter values are
/// <c>("playbook", "playbook://outcome-extraction@v1", "v1")</c> for D-P6 outcome extraction
/// and <c>("playbook", "playbook://classification@v1", "v1")</c> for D-P5 classification.
/// </summary>
public sealed record ProducerIdentity
{
    /// <summary>Producer kind. Phase 1 extraction values: <c>playbook</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Producer URI (e.g., <c>playbook://outcome-extraction@v1</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Producer version string. Mandatory for Observations per D-05.</summary>
    public required string Version { get; init; }
}

/// <summary>
/// Optional scope context propagated to emitted Observations' <c>Models.Insights.Scope</c>
/// envelope. <c>TenantId</c> is required and lives at <see cref="ExtractionResult.TenantId"/>;
/// the optional fields are nested here so callers can omit the whole struct when they only
/// have a tenant.
/// </summary>
public sealed record ExtractionScope
{
    public string? MatterId { get; init; }
    public string? ClientId { get; init; }
    public string? PracticeArea { get; init; }
    public string? Jurisdiction { get; init; }
    public int? Year { get; init; }
}
