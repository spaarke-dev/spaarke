using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Insights.Observations;

/// <summary>
/// Pure mapping logic — builds an <see cref="Entity"/> payload for the <c>sprk_analysis</c>
/// table from an <see cref="ObservationArtifact"/>, and computes the idempotency dedup key
/// (per the §3.5 boundary, no AI internals, no Dataverse client, no logging here — just
/// transformation).
/// </summary>
/// <remarks>
/// <para>
/// <b>Schema mapping</b> (per <c>projects/.../notes/sprk-analysis-polymorphic-confirmation.md</c>):
/// the <c>sprk_analysis</c> table does NOT have a source-type discriminator field. Instead,
/// the existing <c>sprk_searchprofile NVARCHAR(100)</c> field carries the artifactType
/// discriminator (<c>"insights-observation@v1"</c>), and the existing
/// <c>sprk_sessionid NVARCHAR(50)</c> field carries a SHA-256-hashed Observation Id as the
/// idempotency key.
/// </para>
/// <para>
/// <b>Required field mapping</b>:
/// <list type="bullet">
///   <item><c>sprk_name</c> (NOT NULL, 200 chars) — <c>"{predicate}: {value-summary} ({confidence:F2})"</c>, truncated.</item>
///   <item><c>sprk_actionid</c> (NOT NULL, lookup → sprk_analysisaction) — supplied by caller from <see cref="InsightsMirrorOptions.InsightsObservationActionId"/>.</item>
///   <item><c>sprk_documentid</c> (NOT NULL, lookup → sprk_document) — supplied by caller from a Dataverse lookup on <c>sprk_driveitemid</c> derived from the Observation's primary <see cref="EvidenceRef"/>.</item>
/// </list>
/// </para>
/// </remarks>
public static class ObservationMirrorMapper
{
    /// <summary>Logical name of the target Dataverse entity.</summary>
    public const string EntityName = "sprk_analysis";

    /// <summary>The fixed value written to <c>sprk_searchprofile</c> for all Observation mirrors.</summary>
    public const string ArtifactTypeDiscriminator = "insights-observation@v1";

    /// <summary>Field name carrying the artifact-type discriminator.</summary>
    public const string DiscriminatorField = "sprk_searchprofile";

    /// <summary>Field name carrying the idempotency dedup key (SHA-256 hash of <see cref="InsightArtifact.Id"/>).</summary>
    public const string IdempotencyKeyField = "sprk_sessionid";

    /// <summary>Logical name of the lookup target for <c>sprk_actionid</c>.</summary>
    public const string AnalysisActionEntityName = "sprk_analysisaction";

    /// <summary>Logical name of the lookup target for <c>sprk_documentid</c>.</summary>
    public const string DocumentEntityName = "sprk_document";

    /// <summary>
    /// <c>sprk_analysisstatus</c> picklist value for "Completed" (mirror rows are read-only
    /// and always complete on creation).
    /// </summary>
    public const int AnalysisStatusCompleted = 2;

    /// <summary>
    /// Maximum length of the <c>sprk_sessionid</c> field. SHA-256 hex is 64 chars; we
    /// truncate to fit. 50 chars of SHA-256 hex is still ~200 bits of collision resistance.
    /// </summary>
    public const int IdempotencyKeyMaxLength = 50;

    /// <summary>Maximum length of the <c>sprk_name</c> field per schema.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Field name carrying the QA disposition picklist (task 052).</summary>
    public const string DispositionField = "sprk_disposition";

    /// <summary>
    /// Field name carrying the Observation confidence as a dedicated decimal column (Path 2
    /// schema addition, 2026-05-29). Enables numeric sort/filter in the review view without
    /// parsing <c>sprk_name</c>. Populated on every mirror write.
    /// </summary>
    public const string ConfidenceField = "sprk_confidence";

    /// <summary>
    /// <c>sprk_disposition</c> picklist value for "Pending Review" — set on mirror write
    /// when the per-Observation sampling draw fires (task 052). Reviewers see only rows
    /// with this disposition in the "Insights Observations — Review Queue" view.
    /// </summary>
    public const int DispositionPendingReview = 100000000;

    /// <summary>
    /// Compute the deterministic idempotency key for an Observation. SHA-256 over
    /// <see cref="InsightArtifact.Id"/>, hex-encoded, truncated to
    /// <see cref="IdempotencyKeyMaxLength"/> chars.
    /// </summary>
    /// <param name="observationId">The <see cref="InsightArtifact.Id"/>. Required, non-empty.</param>
    /// <returns>The dedup key suitable for <c>sprk_sessionid</c>.</returns>
    /// <exception cref="ArgumentException">When <paramref name="observationId"/> is null/whitespace.</exception>
    public static string ComputeIdempotencyKey(string observationId)
    {
        if (string.IsNullOrWhiteSpace(observationId))
        {
            throw new ArgumentException("Observation Id is required.", nameof(observationId));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(observationId));
        var hex = Convert.ToHexString(bytes); // 64-char uppercase hex
        return hex.Length <= IdempotencyKeyMaxLength ? hex : hex[..IdempotencyKeyMaxLength];
    }

    /// <summary>
    /// Build the <see cref="Entity"/> payload for a <c>sprk_analysis</c> row representing
    /// the given Observation. Does NOT include the document lookup (caller resolves
    /// <c>sprk_driveitemid</c> → <c>sprk_documentid</c> separately).
    /// </summary>
    /// <param name="observation">The Observation to project. Required.</param>
    /// <param name="analysisActionId">GUID of the <c>sprk_analysisaction</c> row for the
    /// "Insights Observation Mirror" semantic. Required, non-empty.</param>
    /// <param name="documentId">GUID of the resolved <c>sprk_document</c> row. Required,
    /// non-empty (the schema enforces NOT NULL).</param>
    /// <param name="disposition">Optional picklist value for <c>sprk_disposition</c>
    /// (task 052). When non-null, the row is tagged with this disposition (typically
    /// <see cref="DispositionPendingReview"/> when the sampling draw fires); when null,
    /// the column is left unset and the row is invisible to the review queue.</param>
    /// <returns>An <see cref="Entity"/> ready for <c>IGenericEntityService.CreateAsync</c>.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="observation"/> is null.</exception>
    /// <exception cref="ArgumentException">When required GUIDs are empty.</exception>
    public static Entity BuildEntity(
        ObservationArtifact observation,
        Guid analysisActionId,
        Guid documentId,
        int? disposition = null)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (analysisActionId == Guid.Empty)
        {
            throw new ArgumentException("Analysis action id is required.", nameof(analysisActionId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Document id is required.", nameof(documentId));
        }

        var entity = new Entity(EntityName)
        {
            // Required (NOT NULL) fields
            ["sprk_name"] = BuildDisplayName(observation),
            ["sprk_actionid"] = new EntityReference(AnalysisActionEntityName, analysisActionId),
            ["sprk_documentid"] = new EntityReference(DocumentEntityName, documentId),

            // Discriminator + idempotency key (Phase 1 scheme — see notes file)
            [DiscriminatorField] = ArtifactTypeDiscriminator,
            [IdempotencyKeyField] = ComputeIdempotencyKey(observation.Id),

            // Status: mirror rows are read-only, always complete on creation
            ["sprk_analysisstatus"] = new OptionSetValue(AnalysisStatusCompleted),

            // Timestamps: instantaneous creation == AsOf
            ["sprk_startedon"] = observation.AsOf.UtcDateTime,
            ["sprk_completedon"] = observation.AsOf.UtcDateTime,

            // Full Observation envelope (rich text — fits 10^6+ chars)
            ["sprk_finaloutput"] = SerializeFullEnvelope(observation),

            // Producer + scope + AsOf as a small JSON for filterable view columns
            ["sprk_chathistory"] = SerializeProducerContext(observation),

            // Primary verbatim quote (Layer 2 outcome extraction signature)
            ["sprk_workingdocument"] = ExtractPrimaryQuote(observation) ?? string.Empty,

            // Dedicated confidence column for view sort/filter (Path 2 — see field XML doc)
            [ConfidenceField] = (decimal)observation.Confidence,
        };

        // QA disposition (task 052) — set only when sampling draw fires; null disposition
        // means the row is invisible to the "Insights Observations — Review Queue" view.
        if (disposition.HasValue)
        {
            entity[DispositionField] = new OptionSetValue(disposition.Value);
        }

        return entity;
    }

    /// <summary>
    /// Build the <c>sprk_name</c> column value: <c>"{predicate}: {value-summary} ({confidence:F2})"</c>,
    /// truncated to <see cref="NameMaxLength"/> chars.
    /// </summary>
    internal static string BuildDisplayName(ObservationArtifact observation)
    {
        var valueSummary = SummarizeRawValue(observation.Value.Raw);
        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{observation.Predicate}: {valueSummary} ({observation.Confidence:F2})");
        return raw.Length <= NameMaxLength ? raw : raw[..NameMaxLength];
    }

    /// <summary>
    /// Render the Observation's <c>Value.Raw</c> <see cref="JsonElement"/> as a short
    /// human-readable string for the display name. Strings render as-is; numbers/booleans
    /// via invariant culture; objects/arrays via compact JSON.
    /// </summary>
    internal static string SummarizeRawValue(JsonElement raw)
    {
        return raw.ValueKind switch
        {
            JsonValueKind.String => raw.GetString() ?? string.Empty,
            JsonValueKind.Number => raw.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Undefined => string.Empty,
            _ => raw.GetRawText(),
        };
    }

    /// <summary>
    /// Serialize the full Observation envelope as compact JSON for <c>sprk_finaloutput</c>.
    /// Uses the polymorphic <see cref="InsightArtifact"/> serializer so the round-trip
    /// shape matches the wire format.
    /// </summary>
    internal static string SerializeFullEnvelope(ObservationArtifact observation)
    {
        return JsonSerializer.Serialize<InsightArtifact>(observation, EnvelopeJsonOptions);
    }

    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serialize a small filterable-friendly producer/scope/timestamp record as JSON for
    /// <c>sprk_chathistory</c> (e.g., so model-driven views can filter by producer version
    /// without parsing the full envelope).
    /// </summary>
    internal static string SerializeProducerContext(ObservationArtifact observation)
    {
        var context = new
        {
            producedBy = new
            {
                kind = observation.ProducedBy.Kind,
                id = observation.ProducedBy.Id,
                version = observation.ProducedBy.Version,
            },
            scope = new
            {
                tenantId = observation.Scope.TenantId,
                matterId = observation.Scope.MatterId,
                clientId = observation.Scope.ClientId,
                practiceArea = observation.Scope.PracticeArea,
                jurisdiction = observation.Scope.Jurisdiction,
                year = observation.Scope.Year,
            },
            asOf = observation.AsOf,
            tenantId = observation.TenantId,
            evidenceCount = observation.Evidence.Count,
        };
        return JsonSerializer.Serialize(context, EnvelopeJsonOptions);
    }

    /// <summary>
    /// Extract the verbatim <c>Quote</c> from the first <see cref="EvidenceRef"/> that
    /// carries one (typically the primary <c>document</c>-type evidence emitted by Layer 2).
    /// Returns <c>null</c> when no evidence has a quote.
    /// </summary>
    internal static string? ExtractPrimaryQuote(ObservationArtifact observation)
    {
        foreach (var ev in observation.Evidence)
        {
            if (!string.IsNullOrWhiteSpace(ev.Quote))
            {
                return ev.Quote;
            }
        }
        return null;
    }
}
