using System.Text.Json;
using Azure.Search.Documents.Models;

namespace Sprk.Bff.Api.Services.Insights.Precedents;

/// <summary>
/// Maps a <see cref="PrecedentRecord"/> (and supporting inputs) to a
/// <see cref="SearchDocument"/> in the <c>spaarke-insights-index</c> schema, per the
/// SPEC §3.4.2 worked example.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 — pure mapping logic, zero AI internals.
/// </para>
/// <para>
/// <b>Document shape</b> (matches the deployed <c>spaarke-insights-index</c> schema in
/// <c>infrastructure/ai-search/spaarke-insights-index.json</c>):
/// <list type="bullet">
///   <item><c>id</c> — <c>prec:{precedentId-as-N}:v1</c> (deterministic; enables MergeOrUpload idempotency)</item>
///   <item><c>tenantId</c> — supplied by caller (D-52 required)</item>
///   <item><c>artifactType</c> — constant <c>"precedent"</c> (discriminator per D-53)</item>
///   <item><c>subject</c> — <c>pattern:{precedentId-as-N}</c> (Phase 1 default; future scope-driven subjects per D-A24)</item>
///   <item><c>predicate</c> — constant <c>"pattern"</c> per SPEC §3.4.2</item>
///   <item><c>value</c> — complex nested per SPEC §3.4.2: raw.patternTitle / raw.scope / raw.sampleSize / raw.supportingMatters, displayHint = "precedent-statement"</item>
///   <item><c>valueJson</c> — full SPEC §3.4.2 value object serialized as JSON string (for downstream consumers reading the typed envelope)</item>
///   <item><c>confidence</c> — <c>null</c> per SPEC §3.4.2 ("Precedents are SME-confirmed; no probabilistic confidence")</item>
///   <item><c>evidence</c> — array of <c>{ refType:"supporting-matter", ref:"matter://{id}" }</c> per SPEC §3.4.2</item>
///   <item><c>asOf</c> — current UTC time at projection</item>
///   <item><c>producedBy</c> — from the Dataverse row (typically <c>manual-sme-author</c>)</item>
///   <item><c>content</c> — the <c>sprk_patternstatement</c> verbatim (the text the synthesis playbook retrieves via vector similarity)</item>
///   <item><c>contentVector</c> — 3072-dim embedding of <c>sprk_patternstatement</c> via <c>IInsightsAi.EmbedTextAsync</c></item>
///   <item><c>status</c> — constant <c>"confirmed"</c> (only Confirmed Precedents are projected — see <see cref="IPrecedentProjectionSync"/> remarks)</item>
/// </list>
/// </para>
/// <para>
/// <b>Why <see cref="SearchDocument"/> and not a typed POCO</b>: the index has nested complex types
/// (<c>value</c>, <c>value.raw</c>, <c>value.raw.scope</c>, <c>evidence[]</c>) whose shape varies by
/// artifactType (an Observation's <c>value.raw</c> is a flat object; a Precedent's is the nested
/// pattern structure). A single POCO covering both would be either deeply optional or use
/// inheritance — both add maintenance cost. <see cref="SearchDocument"/> is the Azure SDK's
/// schema-flexible bag for exactly this case; the cost is no compile-time field-name checking,
/// mitigated by the constant-named field accessors in this file plus unit tests against the
/// schema fixture.
/// </para>
/// </remarks>
public static class PrecedentProjectionMapper
{
    // ─────────────────────────────────────────────────────────────────────────
    // Field name constants — mirror infrastructure/ai-search/spaarke-insights-index.json
    // ─────────────────────────────────────────────────────────────────────────

    internal const string FieldId = "id";
    internal const string FieldTenantId = "tenantId";
    internal const string FieldArtifactType = "artifactType";
    internal const string FieldSubject = "subject";
    internal const string FieldPredicate = "predicate";
    internal const string FieldValue = "value";
    internal const string FieldValueJson = "valueJson";
    internal const string FieldConfidence = "confidence";
    internal const string FieldEvidence = "evidence";
    internal const string FieldAsOf = "asOf";
    internal const string FieldProducedBy = "producedBy";
    internal const string FieldContent = "content";
    internal const string FieldContentVector = "contentVector";
    internal const string FieldStatus = "status";

    // Nested field names on `value`
    internal const string FieldValueRaw = "raw";
    internal const string FieldValueDisplayHint = "displayHint";

    // Nested field names on `value.raw`
    internal const string FieldRawPatternTitle = "patternTitle";
    internal const string FieldRawScope = "scope";
    internal const string FieldRawSampleSize = "sampleSize";
    internal const string FieldRawSupportingMatters = "supportingMatters";

    // Nested field names on `value.raw.scope`
    internal const string FieldScopeMatterType = "matterType";
    internal const string FieldScopeOpposingCounsel = "opposingCounsel";

    // Nested field names on `evidence[*]`
    internal const string FieldEvidenceRefType = "refType";
    internal const string FieldEvidenceRef = "ref";
    internal const string FieldEvidenceQuote = "quote";

    // ─────────────────────────────────────────────────────────────────────────
    // Constant field values
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Discriminator value for Precedent rows per D-53 (single-index pattern).</summary>
    public const string ArtifactTypeValue = "precedent";

    /// <summary>Predicate constant for Precedent rows per SPEC §3.4.2.</summary>
    public const string PredicateValue = "pattern";

    /// <summary>Display hint constant per SPEC §3.4.2.</summary>
    public const string DisplayHintValue = "precedent-statement";

    /// <summary>Status value written for projected (always Confirmed) Precedents per D-P4 acceptance.</summary>
    public const string StatusValue = "confirmed";

    /// <summary>Evidence refType constant for supporting matter references per SPEC §3.4.2.</summary>
    public const string EvidenceRefTypeSupportingMatter = "supporting-matter";

    // ─────────────────────────────────────────────────────────────────────────
    // Mapping
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the deterministic document id used as the AI Search key (also the idempotency key
    /// for <c>MergeOrUploadDocumentsAsync</c>). Format: <c>prec:{precedentId-as-N}:v1</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors the SPEC §3.4.2 example id pattern <c>prec:bigfirm-cure-period-survives:v1</c>
    /// while remaining mechanically derivable from the Dataverse row id (we don't have a
    /// human-readable slug to use, and a stable id-based slug is what idempotency requires).
    /// The <c>v1</c> suffix anticipates Phase 1.5 if the Precedent schema changes — a v2
    /// projection would write to a new id, allowing a parallel run during cutover.
    /// </remarks>
    public static string BuildDocumentId(Guid precedentId)
    {
        if (precedentId == Guid.Empty)
        {
            throw new ArgumentException("PrecedentId is required.", nameof(precedentId));
        }

        return $"prec:{precedentId:N}:v1";
    }

    /// <summary>
    /// Build a <see cref="SearchDocument"/> for the supplied Precedent + embedding.
    /// </summary>
    /// <param name="record">The Dataverse row data (from <see cref="IPrecedentBoard.GetAsync"/>).</param>
    /// <param name="tenantId">Tenant identifier (D-52 required).</param>
    /// <param name="contentVector">3072-dim embedding of <see cref="PrecedentRecord.PatternStatement"/>.</param>
    /// <param name="supportingMatterIds">Supporting matter row ids (from the
    /// <c>sprk_precedent_matter</c> N:N relationship; may be empty). Optional — the projection
    /// proceeds whether or not supporting matters are attached (Phase 1 acceptance per
    /// <see cref="CreatePrecedentRequest.SupportingMatterIds"/> docs).</param>
    /// <param name="asOf">Timestamp for the <c>asOf</c> field (typically <see cref="DateTimeOffset.UtcNow"/>;
    /// parameter exists so unit tests can pin time).</param>
    /// <returns>A populated <see cref="SearchDocument"/> ready for
    /// <c>SearchClient.MergeOrUploadDocumentsAsync</c>.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="record"/> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="tenantId"/> is null/whitespace,
    /// or <paramref name="contentVector"/> is empty.</exception>
    public static SearchDocument BuildDocument(
        PrecedentRecord record,
        string tenantId,
        ReadOnlyMemory<float> contentVector,
        IReadOnlyCollection<Guid> supportingMatterIds,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(supportingMatterIds);

        if (contentVector.IsEmpty)
        {
            throw new ArgumentException(
                "contentVector is required (embedding must be generated before projection).",
                nameof(contentVector));
        }

        var documentId = BuildDocumentId(record.Id);
        var patternTitle = DerivePatternTitle(record);
        var subject = $"pattern:{record.Id:N}";

        // Build the nested value.raw structure. SPEC §3.4.2 includes scope.matterType +
        // scope.opposingCounsel. Phase 1 does not surface these from the Dataverse row
        // (the scope discriminator rides as a JSON tag in sprk_clusterdefinition per task 012);
        // Phase 1.5+ may add dedicated columns. For now we emit an empty scope object so
        // the schema fields are populated but filterable queries returning no Precedents
        // for an unknown opposing counsel is a true negative, not a missing-field error.
        var rawValue = new Dictionary<string, object?>
        {
            [FieldRawPatternTitle] = patternTitle,
            [FieldRawScope] = new Dictionary<string, object?>
            {
                [FieldScopeMatterType] = (string?)null,
                [FieldScopeOpposingCounsel] = (string?)null,
            },
            [FieldRawSampleSize] = supportingMatterIds.Count,
            [FieldRawSupportingMatters] = supportingMatterIds.Select(id => id.ToString("D")).ToArray(),
        };

        var valueObject = new Dictionary<string, object?>
        {
            [FieldValueRaw] = rawValue,
            [FieldValueDisplayHint] = DisplayHintValue,
        };

        // Build the evidence array per SPEC §3.4.2: one entry per supporting matter id.
        var evidence = supportingMatterIds
            .Select(matterId => new Dictionary<string, object?>
            {
                [FieldEvidenceRefType] = EvidenceRefTypeSupportingMatter,
                [FieldEvidenceRef] = $"matter://{matterId:D}",
                // quote is omitted for supporting-matter refs (the matter is the evidence,
                // not a verbatim quote from a document) — Azure Search index accepts null
                // for string fields within a complex type.
                [FieldEvidenceQuote] = (string?)null,
            })
            .ToArray();

        // Serialize the value object to JSON for the valueJson field. Consumers reading the
        // typed envelope (D-P15 endpoint, D-P14 synthesis) can parse this without round-tripping
        // through the complex-type field projection.
        var valueJson = JsonSerializer.Serialize(valueObject);

        // contentVector field: Azure Search expects float[]. ReadOnlyMemory<float>.ToArray()
        // copies once; acceptable for the single-row projection use case.
        var vectorArray = contentVector.ToArray();

        return new SearchDocument
        {
            [FieldId] = documentId,
            [FieldTenantId] = tenantId,
            [FieldArtifactType] = ArtifactTypeValue,
            [FieldSubject] = subject,
            [FieldPredicate] = PredicateValue,
            [FieldValue] = valueObject,
            [FieldValueJson] = valueJson,
            // confidence: omitted (null) per SPEC §3.4.2 — Precedents are SME-confirmed.
            // Azure Search treats omitted fields as null, which is what the schema permits.
            [FieldEvidence] = evidence,
            [FieldAsOf] = asOf,
            [FieldProducedBy] = record.ProducedBy ?? string.Empty,
            [FieldContent] = record.PatternStatement,
            [FieldContentVector] = vectorArray,
            [FieldStatus] = StatusValue,
        };
    }

    /// <summary>
    /// Derive a human-readable pattern title from the Precedent record. Uses
    /// <see cref="PrecedentRecord.Name"/> when populated (the model-driven view title,
    /// already truncated to 200 chars by <see cref="DataversePrecedentBoard.DerivePrimaryName"/>);
    /// falls back to a truncated <see cref="PrecedentRecord.PatternStatement"/> when Name is empty.
    /// </summary>
    internal static string DerivePatternTitle(PrecedentRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.Name))
        {
            return record.Name;
        }

        var statement = record.PatternStatement ?? string.Empty;
        return statement.Length <= 200 ? statement : statement[..200];
    }
}
