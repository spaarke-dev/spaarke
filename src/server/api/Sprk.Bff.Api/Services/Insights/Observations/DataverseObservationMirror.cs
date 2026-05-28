using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Insights.Observations;

/// <summary>
/// Zone B implementation of <see cref="IObservationMirror"/> (task 051, D-P11). Writes one
/// <c>sprk_analysis</c> row per emitted Observation as a side-effect of the universal
/// ingest pipeline (D-P7). Idempotent via SHA-256-hashed <c>sprk_sessionid</c> dedup key.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B placement</b>: this service lives under <c>Services/Insights/Observations/</c>
/// per project CLAUDE.md §3.5 and consumes ONLY <see cref="IGenericEntityService"/>
/// (no AI internals, no LLM client, no playbook engine). It implements
/// <see cref="IObservationMirror"/> from <c>Services/Ai/PublicContracts/</c> (the
/// canonical cross-zone seam per §3.5.4) — this is the same pattern as
/// <c>PrecedentProjectionSync</c> (task 041) using <see cref="IInsightsAi"/>.
/// </para>
/// <para>
/// <b>Schema mapping</b> (deferred to <see cref="ObservationMirrorMapper"/>): per the
/// task 051 first-step-blocker resolution (<c>notes/sprk-analysis-polymorphic-confirmation.md</c>),
/// the <c>sprk_analysis</c> table has no source-type discriminator field — the existing
/// <c>sprk_searchprofile</c> carries the artifactType discriminator
/// (<c>"insights-observation@v1"</c>), and the existing <c>sprk_sessionid</c> carries the
/// SHA-256-hashed Observation Id as the idempotency key.
/// </para>
/// <para>
/// <b>Document lookup</b>: <c>sprk_analysis.sprk_documentid</c> is NOT NULL. This service
/// resolves the lookup from <c>EvidenceRef.Ref</c> for the primary <c>document</c>-type
/// evidence (e.g., <c>spe://drive/{driveId}/item/{itemId}</c> →
/// <c>sprk_document.sprk_driveitemid = itemId</c>). When the lookup fails (no document
/// row with the matching drive-item id), the mirror logs a Warning and skips the write
/// — per the §3.5 fire-and-forget contract, the caller (Zone A IngestOrchestrator) treats
/// this as non-fatal.
/// </para>
/// <para>
/// <b>Dev-safe fallback</b>: when <see cref="InsightsMirrorOptions.InsightsObservationActionId"/>
/// is <see cref="Guid.Empty"/> (default), this service logs a one-time Warning and skips all
/// writes (matches <c>NoOpObservationMirror</c> semantics). Dev/test environments without
/// the deployment-prerequisite <c>sprk_analysisaction</c> row therefore do not produce
/// malformed rows. The DI module reads the option at startup and decides which impl to
/// register — but this in-class fallback is defense-in-depth.
/// </para>
/// </remarks>
public sealed class DataverseObservationMirror : IObservationMirror
{
    // Stable EventIds for App Insights filtering / Kusto queries
    internal static readonly EventId MirrorWriteEvent = new(8050, "ObservationMirrorWrite");
    internal static readonly EventId MirrorSkipDuplicateEvent = new(8051, "ObservationMirrorSkipDuplicate");
    internal static readonly EventId MirrorSkipMissingDocumentEvent = new(8052, "ObservationMirrorSkipMissingDocument");
    internal static readonly EventId MirrorSkipMissingEvidenceEvent = new(8053, "ObservationMirrorSkipMissingEvidence");
    internal static readonly EventId MirrorSkipDisabledEvent = new(8054, "ObservationMirrorSkipDisabled");
    internal static readonly EventId MirrorSkipUnconfiguredEvent = new(8055, "ObservationMirrorSkipUnconfigured");
    internal static readonly EventId MirrorWriteFailedEvent = new(8056, "ObservationMirrorWriteFailed");

    // SPE-style document evidence ref: spe://drive/{driveId}/item/{itemId}
    private static readonly Regex SpeDocumentRefRegex = new(
        @"^spe://drive/(?<driveId>[^/]+)/item/(?<itemId>[^/?#]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IGenericEntityService _entityService;
    private readonly InsightsMirrorOptions _options;
    private readonly ILogger<DataverseObservationMirror> _logger;

    public DataverseObservationMirror(
        IGenericEntityService entityService,
        IOptions<InsightsMirrorOptions> options,
        ILogger<DataverseObservationMirror> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task MirrorAsync(ObservationArtifact observation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ct.ThrowIfCancellationRequested();

        // 1. Kill switch
        if (!_options.EnableMirror)
        {
            _logger.Log(
                LogLevel.Information,
                MirrorSkipDisabledEvent,
                "DataverseObservationMirror skipped (EnableMirror=false): observationId={ObservationId}",
                observation.Id);
            return;
        }

        // 2. Dev-safe fallback when action GUID not configured
        if (_options.InsightsObservationActionId == Guid.Empty)
        {
            _logger.Log(
                LogLevel.Warning,
                MirrorSkipUnconfiguredEvent,
                "DataverseObservationMirror skipped: InsightsObservationActionId is unset (Guid.Empty). Configure Insights:Mirror:InsightsObservationActionId once the sprk_analysisaction row exists in this environment. observationId={ObservationId}",
                observation.Id);
            return;
        }

        // 3. Resolve sprk_document GUID from primary document-type EvidenceRef.
        // Per task 051 first-step blocker: sprk_documentid is NOT NULL, so missing
        // evidence → skip with Warning (fire-and-forget contract).
        var documentId = await ResolveDocumentIdAsync(observation, ct).ConfigureAwait(false);
        if (documentId is null)
        {
            // Already logged inside ResolveDocumentIdAsync
            return;
        }

        // 4. Idempotency check (optional per config)
        var idempotencyKey = ObservationMirrorMapper.ComputeIdempotencyKey(observation.Id);
        if (_options.EnableIdempotencyCheck)
        {
            var existing = await FindExistingMirrorAsync(idempotencyKey, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                _logger.Log(
                    LogLevel.Information,
                    MirrorSkipDuplicateEvent,
                    "DataverseObservationMirror no-op (idempotent — row already exists): observationId={ObservationId} existingAnalysisId={AnalysisId} idempotencyKey={IdempotencyKey}",
                    observation.Id,
                    existing.Value,
                    idempotencyKey);
                return;
            }
        }

        // 5. Build + write the row
        var entity = ObservationMirrorMapper.BuildEntity(
            observation,
            _options.InsightsObservationActionId,
            documentId.Value);

        try
        {
            var analysisId = await _entityService.CreateAsync(entity, ct).ConfigureAwait(false);
            _logger.Log(
                LogLevel.Information,
                MirrorWriteEvent,
                "DataverseObservationMirror wrote sprk_analysis row: observationId={ObservationId} analysisId={AnalysisId} predicate={Predicate} confidence={Confidence:F2} tenantId={TenantId}",
                observation.Id,
                analysisId,
                observation.Predicate,
                observation.Confidence,
                observation.TenantId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Per IObservationMirror contract: write failures are non-fatal. The Zone A
            // IngestOrchestrator already wraps MirrorAsync in try/catch; this defense-in-depth
            // log captures the failure with stable EventId for App Insights queries.
            _logger.Log(
                LogLevel.Warning,
                MirrorWriteFailedEvent,
                ex,
                "DataverseObservationMirror failed to write sprk_analysis row (non-fatal — system-of-record is spaarke-insights-index): observationId={ObservationId} predicate={Predicate} tenantId={TenantId}",
                observation.Id,
                observation.Predicate,
                observation.TenantId);
            // Swallow per fire-and-forget contract.
        }
    }

    /// <summary>
    /// Walk <see cref="ObservationArtifact.Evidence"/> for the first <c>document</c>-type
    /// EvidenceRef whose URI matches the SPE pattern; resolve to a <c>sprk_document</c> row
    /// via <c>sprk_driveitemid</c> lookup. Returns null when no evidence is parseable or no
    /// matching document row exists.
    /// </summary>
    internal async Task<Guid?> ResolveDocumentIdAsync(ObservationArtifact observation, CancellationToken ct)
    {
        string? driveItemId = null;
        EvidenceRef? matchedRef = null;
        foreach (var ev in observation.Evidence)
        {
            if (!string.Equals(ev.RefType, "document", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = SpeDocumentRefRegex.Match(ev.Ref ?? string.Empty);
            if (match.Success)
            {
                driveItemId = match.Groups["itemId"].Value;
                matchedRef = ev;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(driveItemId))
        {
            _logger.Log(
                LogLevel.Warning,
                MirrorSkipMissingEvidenceEvent,
                "DataverseObservationMirror skipped — no parseable spe://drive/{{driveId}}/item/{{itemId}} evidence ref found on Observation: observationId={ObservationId} evidenceCount={EvidenceCount}",
                observation.Id,
                observation.Evidence.Count);
            return null;
        }

        // Query sprk_document by sprk_driveitemid. Phase 1 single-tenant (D-52) so no
        // additional tenant filter needed; if Phase 2+ federates we can add a sprk_matter
        // → tenantId filter at that time.
        var query = new QueryExpression(ObservationMirrorMapper.DocumentEntityName)
        {
            ColumnSet = new ColumnSet("sprk_documentid"),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition("sprk_driveitemid", ConditionOperator.Equal, driveItemId);

        EntityCollection results;
        try
        {
            results = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Log(
                LogLevel.Warning,
                MirrorSkipMissingDocumentEvent,
                ex,
                "DataverseObservationMirror skipped — sprk_document lookup by driveItemId={DriveItemId} failed: observationId={ObservationId}",
                driveItemId,
                observation.Id);
            return null;
        }

        if (results.Entities.Count == 0)
        {
            _logger.Log(
                LogLevel.Warning,
                MirrorSkipMissingDocumentEvent,
                "DataverseObservationMirror skipped — no sprk_document row found with sprk_driveitemid={DriveItemId} (evidenceRef={EvidenceRef}): observationId={ObservationId}",
                driveItemId,
                matchedRef?.Ref,
                observation.Id);
            return null;
        }

        return results.Entities[0].GetAttributeValue<Guid>("sprk_documentid");
    }

    /// <summary>
    /// Query <c>sprk_analysis</c> for an existing mirror row with the given idempotency key
    /// (<c>sprk_sessionid</c>) AND the artifactType discriminator (<c>sprk_searchprofile =
    /// "insights-observation@v1"</c>). Returns the row's id if found, null otherwise.
    /// </summary>
    /// <remarks>
    /// The combined filter (key + discriminator) avoids false positives if some other
    /// subsystem ever reuses <c>sprk_sessionid</c> values from a different artifact-type
    /// scope (unlikely given SHA-256, but defensive).
    /// </remarks>
    internal async Task<Guid?> FindExistingMirrorAsync(string idempotencyKey, CancellationToken ct)
    {
        var query = new QueryExpression(ObservationMirrorMapper.EntityName)
        {
            ColumnSet = new ColumnSet("sprk_analysisid"),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition(
            ObservationMirrorMapper.IdempotencyKeyField,
            ConditionOperator.Equal,
            idempotencyKey);
        query.Criteria.AddCondition(
            ObservationMirrorMapper.DiscriminatorField,
            ConditionOperator.Equal,
            ObservationMirrorMapper.ArtifactTypeDiscriminator);

        try
        {
            var results = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            if (results.Entities.Count == 0)
            {
                return null;
            }
            return results.Entities[0].GetAttributeValue<Guid>("sprk_analysisid");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // If the idempotency-check query itself fails, fall through to insert (worst
            // case = duplicate row, which is recoverable). Better than failing the whole
            // mirror over a transient lookup error.
            return null;
        }
    }
}
