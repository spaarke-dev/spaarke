using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// <see cref="IRelocatedFileIndexing"/> over the EXISTING indexing seams (CLAUDE.md §11: no new indexing pipeline):
/// <see cref="IPostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync"/> for the new item (the app-only
/// <c>RagIndexing</c> job, its idempotency key and its applicability rules unchanged) and
/// <see cref="IRagService.DeleteSupersededFileChunksAsync"/> for the old item's chunks.
/// </summary>
/// <remarks>
/// <para><b>Order.</b> The old chunks are removed only after the new item's index job is ENQUEUED (durable on Service
/// Bus). An enqueue that fails or is switched off leaves the old chunks in place and answers pending, so the document is
/// never made unsearchable by a re-index that will not come; the relocator's repeat call retries both steps (the enqueue
/// is idempotent per item, the delete finds nothing the second time).</para>
/// <para><b>Indexes.</b> The old chunks are removed from the index the document was last stamped into
/// (<c>sprk_searchindexname</c>) and from the tenant default — the two places the file pipeline writes them. An index the
/// allow-list no longer admits is unreachable for search as well, so it is skipped and logged.</para>
/// <para><b>Tenant.</b> The same configuration every app-only indexing producer uses (<c>TENANT_ID</c>, else
/// <c>AzureAd:TenantId</c>); none configured = pending, never a guess.</para>
/// <para>Scoped (its enqueuer is Scoped) and registered UNCONDITIONALLY with the enqueuer: the relocator that consumes it
/// serves an unconditionally mapped route (bff-extensions.md §F.1). With AI off, <see cref="NullRagService"/> answers
/// "nothing is indexed here", which settles the old-chunk step.</para>
/// </remarks>
public sealed class RelocatedFileIndexing : IRelocatedFileIndexing
{
    /// <summary>The <c>Source</c> tag of the index job (telemetry / debugging).</summary>
    internal const string SourceTag = "DocumentRelocation";

    private readonly IPostUploadIndexingEnqueuer _enqueuer;
    private readonly IRagService _rag;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RelocatedFileIndexing> _logger;

    public RelocatedFileIndexing(
        IPostUploadIndexingEnqueuer enqueuer,
        IRagService rag,
        IConfiguration configuration,
        ILogger<RelocatedFileIndexing> logger)
    {
        _enqueuer = enqueuer;
        _rag = rag;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RelocatedFileIndexOutcome> ReindexRelocatedFileAsync(
        RelocatedFileIndexRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenantId = _configuration["TENANT_ID"] ?? _configuration["AzureAd:TenantId"];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return RelocatedFileIndexOutcome.Pending("no tenant id is configured (TENANT_ID / AzureAd:TenantId)");
        }

        // (1) Index the NEW item (app-only: the copy was uploaded by the BFF identity).
        PostUploadIndexingResult enqueued;
        try
        {
            enqueued = await _enqueuer.EnqueueAppOnlyIfApplicableAsync(
                new PostUploadIndexingRequest(
                    TenantId: tenantId,
                    DriveId: request.DriveId,
                    ItemId: request.ItemId,
                    FileName: request.FileName ?? string.Empty,
                    FileSizeBytes: request.FileSizeBytes,
                    ContentType: null,
                    DocumentId: request.DocumentId.ToString("D"),
                    ParentEntity: null,
                    SearchIndexName: null, // the handler's resolver chain decides, as for every app-only producer
                    Source: SourceTag,
                    CorrelationId: request.DocumentId.ToString("N")),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[DOCUMENT-RELOCATE] the re-index of document {DocumentId}'s new item could not be enqueued.",
                request.DocumentId);
            return RelocatedFileIndexOutcome.Pending($"the re-index of the new item could not be enqueued ({ex.GetType().Name})");
        }

        string indexed;
        if (enqueued.JobSubmitted)
        {
            indexed = $"re-index of {request.ItemId} enqueued (job {enqueued.JobId})";
        }
        else if (enqueued.FailureReason is { } failure)
        {
            return RelocatedFileIndexOutcome.Pending($"the re-index of the new item could not be enqueued ({failure})");
        }
        else if (enqueued.SkipReason is "FeatureFlagDisabled" or "MissingTenantId" or "MissingSpeIdentifiers")
        {
            // Not a property of the FILE: indexing is switched off or unconfigured. The old chunks stay until it can run.
            return RelocatedFileIndexOutcome.Pending($"the new item was not indexed ({enqueued.SkipReason})");
        }
        else
        {
            // The file itself is not indexable (empty, too large, a non-text type): there is nothing to index for it.
            indexed = $"the new item is not indexable ({enqueued.SkipReason})";
        }

        // (2) Remove the OLD item's chunks.
        var onlyFor = request.OldItemRemoved ? null : request.DocumentId.ToString("D");
        var indexes = new List<string?> { null };
        if (!string.IsNullOrWhiteSpace(request.PreviousSearchIndexName))
        {
            indexes.Insert(0, request.PreviousSearchIndexName.Trim());
        }

        var removed = 0;
        foreach (var index in indexes)
        {
            try
            {
                removed += await _rag.DeleteSupersededFileChunksAsync(tenantId, request.OldItemId, onlyFor, index, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (FeatureDisabledException)
            {
                // AI search is off in this deployment: nothing was ever indexed, so nothing is stale.
                return RelocatedFileIndexOutcome.Done($"{indexed}; no RAG index in this deployment");
            }
            catch (SdapProblemException ex) when (ex.Code == "INDEX_NOT_ALLOWED")
            {
                _logger.LogWarning(
                    "[DOCUMENT-RELOCATE] index {Index} is not on the allow-list; the old item {OldItem}'s chunks there are "
                    + "unreachable for search and are left (document {DocumentId}).", index, request.OldItemId, request.DocumentId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "[DOCUMENT-RELOCATE] the old item {OldItem}'s chunks could not be removed from {Index} (document {DocumentId}).",
                    request.OldItemId, index ?? "(tenant-default)", request.DocumentId);
                return RelocatedFileIndexOutcome.Pending(
                    $"{indexed}; the old item's chunks could not be removed from {index ?? "the tenant default index"} ({ex.GetType().Name})");
            }
        }

        _logger.LogInformation(
            "[DOCUMENT-RELOCATE] index re-keyed: document {DocumentId} {OldItem} -> {NewItem}; {Indexed}; {Removed} old chunk(s) "
            + "removed ({Scope}).", request.DocumentId, request.OldItemId, request.ItemId, indexed, removed,
            request.OldItemRemoved ? "all" : "this document's");
        return RelocatedFileIndexOutcome.Done(
            $"{indexed}; {removed} chunk(s) of the old item removed ({(request.OldItemRemoved ? "all" : "this document's")})");
    }
}
