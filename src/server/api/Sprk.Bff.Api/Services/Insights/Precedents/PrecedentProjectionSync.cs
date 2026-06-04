using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Insights.Precedents;

/// <summary>
/// Phase 1 implementation of <see cref="IPrecedentProjectionSync"/>. Reads the
/// <c>sprk_precedent</c> row via <see cref="IPrecedentBoard"/>, generates a 3072-dim
/// embedding via <see cref="IInsightsAi.EmbedTextAsync"/> (the §3.5 facade-permitted
/// path for Zone B embedding generation, per task 042), and writes a single
/// <c>spaarke-insights-index</c> row via <c>SearchClient.MergeOrUploadDocumentsAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 — see <see cref="IPrecedentProjectionSync"/>
/// remarks. The only Zone A import is <see cref="IInsightsAi"/> from
/// <c>Sprk.Bff.Api.Services.Ai.PublicContracts</c>, which is explicitly the §3.5 facade
/// and the ONLY Zone-A type permitted in Zone B paths per project CLAUDE.md §3.5.4.
/// </para>
/// <para>
/// <b>Idempotency</b>: the document id is deterministic
/// (<see cref="PrecedentProjectionMapper.BuildDocumentId"/> → <c>prec:{precedentId-as-N}:v1</c>).
/// <c>MergeOrUploadDocumentsAsync</c> performs an in-place overwrite when the id already
/// exists, so repeated projection of the same Precedent never produces duplicate rows.
/// </para>
/// <para>
/// <b>Status gate</b>: only Precedents with <c>sprk_status = Confirmed</c>
/// (<see cref="PrecedentStatus.Confirmed"/>) are projected per D-P4 acceptance. The check
/// happens after the Dataverse read, so any race condition between the caller's "is
/// Confirmed?" check and this method's projection is resolved in favor of the
/// database-of-record value.
/// </para>
/// <para>
/// <b>Index name</b>: hard-coded to <c>spaarke-insights-index</c> per SPEC §3.4 and D-P2
/// (task 010 schema deployment). This matches the
/// <c>IndexRetrieveNode.DefaultIndexName</c> constant the synthesis path (D-P14) reads from.
/// </para>
/// </remarks>
public sealed class PrecedentProjectionSync : IPrecedentProjectionSync
{
    /// <summary>
    /// Target AI Search index name. Constant for Phase 1 (single derived index per SPEC §3.4 +
    /// D-53 single-index pattern). If a future tenant needs a different physical index, the
    /// projection sync becomes options-bound — for now it's a single deployment-shared index.
    /// </summary>
    internal const string TargetIndexName = "spaarke-insights-index";

    private readonly IPrecedentBoard _board;
    private readonly IInsightsAi _insightsAi;
    private readonly SearchIndexClient _searchIndexClient;
    private readonly ILogger<PrecedentProjectionSync> _logger;
    private readonly TimeProvider _timeProvider;

    public PrecedentProjectionSync(
        IPrecedentBoard board,
        IInsightsAi insightsAi,
        SearchIndexClient searchIndexClient,
        ILogger<PrecedentProjectionSync> logger,
        TimeProvider? timeProvider = null)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _insightsAi = insightsAi ?? throw new ArgumentNullException(nameof(insightsAi));
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<PrecedentProjectionResult> ProjectAsync(
        Guid precedentId,
        string tenantId,
        CancellationToken ct)
    {
        if (precedentId == Guid.Empty)
        {
            throw new ArgumentException("PrecedentId is required.", nameof(precedentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // ---------------------------------------------------------------
        // 1. Read the row from Dataverse
        // ---------------------------------------------------------------
        var record = await _board.GetAsync(precedentId, ct);
        if (record is null)
        {
            _logger.LogWarning(
                "[PRECEDENT-PROJECTION] Precedent {PrecedentId} not found in Dataverse; nothing to project.",
                precedentId);
            return new PrecedentProjectionResult(
                Outcome: PrecedentProjectionOutcome.NotFound,
                DocumentId: null,
                StatusValue: null);
        }

        // ---------------------------------------------------------------
        // 2. Gate on status — only Confirmed Precedents project per D-P4
        // ---------------------------------------------------------------
        if (record.StatusValue != PrecedentStatus.Confirmed)
        {
            _logger.LogInformation(
                "[PRECEDENT-PROJECTION] Skipping Precedent {PrecedentId}: status={StatusValue} (only Confirmed Precedents project to {IndexName} per D-P4).",
                precedentId, record.StatusValue, TargetIndexName);
            return new PrecedentProjectionResult(
                Outcome: PrecedentProjectionOutcome.Skipped,
                DocumentId: null,
                StatusValue: record.StatusValue);
        }

        if (string.IsNullOrWhiteSpace(record.PatternStatement))
        {
            // Confirmed Precedents must have a pattern statement (the field is required at
            // creation per task 012 validation). A blank value here indicates corruption; log
            // and skip rather than embedding an empty string.
            _logger.LogWarning(
                "[PRECEDENT-PROJECTION] Skipping Precedent {PrecedentId}: status=Confirmed but patternStatement is blank.",
                precedentId);
            return new PrecedentProjectionResult(
                Outcome: PrecedentProjectionOutcome.Skipped,
                DocumentId: null,
                StatusValue: record.StatusValue);
        }

        // ---------------------------------------------------------------
        // 3. Read supporting matter ids (N:N) — used for evidence + supportingMatters[]
        // ---------------------------------------------------------------
        var supportingMatterIds = await _board.GetSupportingMatterIdsAsync(precedentId, ct);

        // ---------------------------------------------------------------
        // 4. Generate the 3072-dim embedding via the §3.5 facade
        //    (the ONLY Zone-A import in this Zone B file)
        // ---------------------------------------------------------------
        var contentVector = await _insightsAi.EmbedTextAsync(record.PatternStatement, ct);

        // ---------------------------------------------------------------
        // 5. Build the SearchDocument per SPEC §3.4.2
        // ---------------------------------------------------------------
        var asOf = _timeProvider.GetUtcNow();
        var document = PrecedentProjectionMapper.BuildDocument(
            record,
            tenantId,
            contentVector,
            supportingMatterIds,
            asOf);

        var documentId = (string)document[PrecedentProjectionMapper.FieldId];

        // ---------------------------------------------------------------
        // 6. Upsert to spaarke-insights-index (idempotent via deterministic id)
        // ---------------------------------------------------------------
        var searchClient = _searchIndexClient.GetSearchClient(TargetIndexName);
        var response = await searchClient.MergeOrUploadDocumentsAsync(
            new[] { document },
            cancellationToken: ct);

        var firstResult = response.Value.Results.FirstOrDefault();
        if (firstResult is null || !firstResult.Succeeded)
        {
            // MergeOrUpload returned a result row that did not succeed — surface as an exception
            // so the fire-and-forget caller's try/catch logs the structured failure.
            var errorMessage = firstResult?.ErrorMessage ?? "no result returned";
            throw new InvalidOperationException(
                $"Failed to upsert Precedent {precedentId} (documentId={documentId}) to " +
                $"{TargetIndexName}: {errorMessage}");
        }

        _logger.LogInformation(
            "[PRECEDENT-PROJECTION] Projected Precedent {PrecedentId} → {DocumentId} in {IndexName} " +
            "(tenantId={TenantId}, supportingMatters={SupportingMatterCount}, vectorDims={VectorDims})",
            precedentId, documentId, TargetIndexName, tenantId, supportingMatterIds.Count, contentVector.Length);

        return new PrecedentProjectionResult(
            Outcome: PrecedentProjectionOutcome.Written,
            DocumentId: documentId,
            StatusValue: record.StatusValue);
    }
}
