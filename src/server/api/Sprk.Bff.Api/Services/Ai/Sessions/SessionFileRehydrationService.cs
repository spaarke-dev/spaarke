using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// What a rehydration attempt actually did. Deliberately NOT a bool: "the file is still unusable" has
/// several causes with different operator actions, and collapsing them is how the original defect
/// presented to users as an undifferentiated "no longer available".
/// </summary>
public enum SessionFileRehydrationOutcome
{
    /// <summary>
    /// The durable bytes were found, text was recovered from them, and the hot AI-Search chunks were
    /// rebuilt under the SAME deterministic chunk ids the manifest already records.
    /// </summary>
    Reindexed,

    /// <summary>
    /// The durable bytes were found and text was recovered, but the RAG indexing pipeline is not
    /// registered in this deployment, so the hot index was NOT rebuilt. Text-only consumers
    /// (<c>SessionFileTextSource</c>) still work; search-backed recall does not.
    /// </summary>
    TextOnly,

    /// <summary>
    /// The durable store is configured, but it holds no copy of this file FOR THIS TENANT. This is also
    /// the answer a cross-tenant probe gets, by design (see <see cref="SessionFileBlobStore.ReadAsync"/>).
    /// Expected for files uploaded before FR-B01 shipped.
    /// </summary>
    NoDurableCopy,

    /// <summary>
    /// <c>SessionFileStore:BlobEndpoint</c> is not configured, so no durable copy was ever written.
    /// This is the pre-FR-B01 world and, as of task 060, the deliberate default until tasks 062
    /// (retention) and 063 (erasure) merge.
    /// </summary>
    StoreDisabled,

    /// <summary>Text extraction is unavailable (the AI feature gate is off - NullTextExtractor).</summary>
    Unavailable,

    /// <summary>Bytes were found but could not be turned back into indexed content. Logged with the cause.</summary>
    Failed
}

/// <summary>Result of one lazy rehydration attempt.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="ExtractedText">
/// The text recovered from the durable bytes, or <c>null</c> when nothing was recovered. Consumers that
/// need text (rather than search hits) can use this directly and skip the index entirely.
/// </param>
/// <param name="ChunkCount">Number of chunks written back into the hot index (0 unless <c>Reindexed</c>).</param>
public sealed record SessionFileRehydrationResult(
    SessionFileRehydrationOutcome Outcome,
    string? ExtractedText,
    int ChunkCount)
{
    internal static SessionFileRehydrationResult Nothing(SessionFileRehydrationOutcome outcome)
        => new(outcome, null, 0);
}

/// <summary>
/// spaarkeai-compose-r8 FR-B02 (task 061) - rebuilds a session file's HOT search index on demand, from
/// the durable byte copy task 060 wrote at upload time.
/// </summary>
/// <remarks>
/// <para>
/// <b>The lifecycle mismatch this closes.</b> A chat session lives 90 days (Cosmos manifest, ADR-015
/// Tier 3). Its searchable content did not: <c>SessionFilesCleanupJob</c> evicts every
/// <c>spaarke-session-files</c> chunk once the session's Redis key expires on its 24h sliding TTL, and
/// <c>ChatSessionFile.ExtractedText</c> - the R7 inline-text fallback - is NOT a field on
/// <see cref="StoredUploadedFile"/>, so it is dropped on the Cosmos round trip. A conversation reopened
/// on day 2 therefore held a manifest (<c>SearchDocumentIdsCsv</c>) pointing at chunk ids that no longer
/// existed, and nothing anywhere held the content. That is R7 UAT point 1b verbatim.
/// </para>
/// <para>
/// <b>Why the results come back IDENTICAL rather than merely non-empty.</b> Chunk ids in the
/// session-files index are deterministic: <c>RagIndexingPipeline.BuildKnowledgeDocuments</c> composes
/// <c>{documentId}_s_{chunkIndex}</c>, and the upload path passes the <see cref="ChatSessionFile.FileId"/>
/// as <c>documentId</c>. Re-indexing the SAME bytes through the SAME extractor and the SAME chunking
/// profile therefore reproduces the SAME ids, so the manifest's <c>SearchDocumentIdsCsv</c> - which every
/// recall consumer post-filters on - keeps matching without being rewritten. Nothing has to be migrated,
/// and a partially-rehydrated file degrades to a subset rather than to garbage.
/// </para>
/// <para>
/// <b>Lazy by contract.</b> This runs on RECALL, never on a schedule and never on session load. A
/// background rehydration sweep would re-warm every session's files and defeat the eviction that keeps
/// the hot index small - which is the point of evicting at 24h in the first place.
/// </para>
/// <para>
/// <b>Tenant isolation (ADR-014 / ADR-015).</b> The read is
/// <see cref="SessionFileBlobStore.ReadAsync"/>, whose blob name is tenant-prefixed and re-asserted; a
/// rehydration performed under tenant B cannot reach tenant A's bytes even with A's exact session id and
/// file id - it gets <see cref="SessionFileRehydrationOutcome.NoDurableCopy"/>, indistinguishable from
/// "no such file". The re-index then carries the CALLING tenant on every emitted document, so a
/// cross-tenant rehydration cannot write into another tenant's partition either.
/// </para>
/// <para>
/// <b>Idempotent, and deliberately not de-duplicated.</b> Two concurrent recalls of the same file both
/// rehydrate; both produce the same chunk ids and both write via <c>MergeOrUploadDocuments</c>, so the
/// duplicate costs a wasted extraction and cannot corrupt anything. An in-process de-dup cache was
/// considered and rejected: ADR-009 forbids <c>IMemoryCache</c>, and a keyed-semaphore table on a
/// singleton is a leak looking for a home. The wasted work is bounded by how often a user recalls the
/// same evicted file twice in the same second.
/// </para>
/// <para>
/// <b>Known bounded limitation - Azure AI Search visibility lag.</b> A re-index is not instantly
/// queryable. A caller that re-runs its search in the same call MAY still see zero hits; the durable
/// effect has nevertheless happened, so the next recall (and every other consumer) succeeds. Callers
/// should say "restoring" rather than "not found" in that window -
/// <c>RecallSessionFileHandler.TruncationReasonRehydrating</c> exists for exactly that. This is the same
/// index-catchup race R7 Wave 12.3 hit on the upload path; it is not made worse here, and it is NOT
/// papered over with a retry-sleep in a request path.
/// </para>
/// <para>
/// <b>Placement (root CLAUDE.md section 10).</b> In the BFF, under <c>Services/Ai/Sessions/</c>, beside
/// the store it reads and inside the AI boundary (ADR-013) - no CRUD code touches it, so no
/// <c>PublicContracts/</c> facade is needed. It cannot live outside the BFF: it is on the synchronous
/// recall path of a tool call, and its two collaborators (the durable store and the RAG indexing
/// pipeline) are both BFF singletons. No new package, no new Azure resource.
/// </para>
/// <para>
/// <b>Section 11 three-question gate.</b> (1) <i>Existing overlap</i> - none:
/// <c>SessionRestoreService</c> restores the CONVERSATION (Cosmos messages + Dataverse ETag staleness)
/// and never touches file content; <c>RagIndexingPipeline</c> indexes text it is handed and has no
/// notion of durable bytes; <c>SessionFileBlobStore</c> is bytes only and must stay that way so the
/// cleanup sweep has nothing to reach (FR-B03). Verified by grep, not assumed. (2) <i>Extend
/// instead?</i> - extending <c>SessionRestoreService</c> would build the "new session-restore surface"
/// this project's CLAUDE.md forbids; extending <c>RagIndexingPipeline</c> would give a
/// conditionally-registered indexing singleton a dependency on session file storage. (3) <i>Cost of
/// doing nothing</i> - the durable bytes task 060 writes are never read by anything, so a day-60 recall
/// still returns nothing: FR-B01 alone buys zero user-visible behaviour.
/// </para>
/// </remarks>
public sealed class SessionFileRehydrationService
{
    private readonly SessionFileBlobStore _durableStore;
    private readonly ITextExtractor? _textExtractor;
    private readonly RagIndexingPipeline? _indexingPipeline;
    private readonly ILogger<SessionFileRehydrationService> _logger;

    public SessionFileRehydrationService(
        SessionFileBlobStore durableStore,
        ITextExtractor? textExtractor,
        RagIndexingPipeline? indexingPipeline,
        ILogger<SessionFileRehydrationService> logger)
    {
        _durableStore = durableStore ?? throw new ArgumentNullException(nameof(durableStore));
        // Both collaborators are legitimately absent in an AI-off deployment. They are resolved with
        // GetService at composition time and passed as null rather than being made hard dependencies,
        // because a hard dependency on the conditionally-registered RagIndexingPipeline would make THIS
        // registration conditional too - the asymmetric-registration anti-pattern (ADR-032 / F.1).
        _textExtractor = textExtractor;
        _indexingPipeline = indexingPipeline;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// True when this deployment can rehydrate at all. False means every call returns
    /// <see cref="SessionFileRehydrationOutcome.StoreDisabled"/> or
    /// <see cref="SessionFileRehydrationOutcome.Unavailable"/>, so callers can skip the attempt.
    /// </summary>
    public bool IsAvailable => _durableStore.IsEnabled && _textExtractor is not null;

    /// <summary>
    /// Rebuilds <paramref name="file"/>'s hot-index chunks from its durable byte copy.
    /// </summary>
    /// <param name="tenantId">Calling tenant - the partition the durable read is scoped to (ADR-014).</param>
    /// <param name="sessionId">Session the file belongs to (32-char "N" form, as the manifest stores it).</param>
    /// <param name="file">
    /// The manifest entry. Only <see cref="ChatSessionFile.FileId"/> and
    /// <see cref="ChatSessionFile.FileName"/> are used; the file NAME never reaches a log line here.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Never throws for an expected miss - every terminal state is a
    /// <see cref="SessionFileRehydrationOutcome"/>. Cancellation still propagates.
    /// </remarks>
    public async Task<SessionFileRehydrationResult> RehydrateAsync(
        string tenantId,
        string sessionId,
        ChatSessionFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(file.FileId);

        if (!_durableStore.IsEnabled)
        {
            return SessionFileRehydrationResult.Nothing(SessionFileRehydrationOutcome.StoreDisabled);
        }

        if (_textExtractor is null)
        {
            _logger.LogDebug(
                "Session-file rehydration unavailable - no text extractor registered. TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}",
                tenantId, sessionId, file.FileId);
            return SessionFileRehydrationResult.Nothing(SessionFileRehydrationOutcome.Unavailable);
        }

        SessionFileBytes? durable;
        try
        {
            // ADR-014/ADR-015: tenant-scoped by construction. A different tenant asking for the same
            // (sessionId, fileId) gets null here, not another tenant's bytes.
            durable = await _durableStore
                .ReadAsync(tenantId, sessionId, file.FileId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ADR-015: identifiers only - never the file name, never the bytes.
            _logger.LogWarning(ex,
                "Session-file rehydration could not read the durable copy. TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}",
                tenantId, sessionId, file.FileId);
            return SessionFileRehydrationResult.Nothing(SessionFileRehydrationOutcome.Failed);
        }

        if (durable is null)
        {
            _logger.LogInformation(
                "Session-file rehydration found no durable copy for this tenant. TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}",
                tenantId, sessionId, file.FileId);
            return SessionFileRehydrationResult.Nothing(SessionFileRehydrationOutcome.NoDurableCopy);
        }

        var sizeBytes = durable.Content.ToMemory().Length;

        TextExtractionResult extraction;
        try
        {
            using var stream = durable.Content.ToStream();
            extraction = await _textExtractor
                .ExtractAsync(stream, file.FileName ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureDisabledException ex)
        {
            _logger.LogDebug(
                "Session-file rehydration attempted while the AI feature is disabled. ErrorCode={ErrorCode}, FileId={FileId}",
                ex.ErrorCode, file.FileId);
            return SessionFileRehydrationResult.Nothing(SessionFileRehydrationOutcome.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Session-file rehydration failed to re-extract text from the durable copy. TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}, SizeBytes={SizeBytes}",
                tenantId, sessionId, file.FileId, sizeBytes);
            return SessionFileRehydrationResult.Nothing(SessionFileRehydrationOutcome.Failed);
        }

        if (!extraction.Success || string.IsNullOrWhiteSpace(extraction.Text))
        {
            _logger.LogWarning(
                "Session-file rehydration recovered the durable bytes but extracted no text. TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}, SizeBytes={SizeBytes}",
                tenantId, sessionId, file.FileId, sizeBytes);
            return SessionFileRehydrationResult.Nothing(SessionFileRehydrationOutcome.Failed);
        }

        var recoveredText = extraction.Text!;

        if (_indexingPipeline is null)
        {
            _logger.LogInformation(
                "Session-file rehydration recovered text but the RAG indexing pipeline is not registered - " +
                "hot index NOT rebuilt. TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}",
                tenantId, sessionId, file.FileId);
            return new SessionFileRehydrationResult(
                SessionFileRehydrationOutcome.TextOnly, recoveredText, 0);
        }

        try
        {
            // documentId AND speFileId are both the manifest's FileId - exactly what
            // ChatDocumentEndpoints passed at upload time. That is what makes the regenerated chunk ids
            // ({fileId}_s_{index}) equal to the ones SearchDocumentIdsCsv already names.
            var indexed = await _indexingPipeline.IndexSessionFileAsync(
                document: new ParsedDocument
                {
                    Text = recoveredText,
                    Pages = 0,
                    ExtractedAt = DateTimeOffset.UtcNow,
                },
                documentId: file.FileId,
                tenantId: tenantId,
                sessionId: sessionId,
                fileName: file.FileName ?? string.Empty,
                speFileId: file.FileId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var chunkCount = indexed.KnowledgeChunksIndexed;

            WarnIfChunkCountDivergedFromManifest(tenantId, sessionId, file, chunkCount);

            _logger.LogInformation(
                "Session-file rehydration re-indexed from the durable copy. TenantId={TenantId}, SessionId={SessionId}, " +
                "FileId={FileId}, SizeBytes={SizeBytes}, ChunkCount={ChunkCount}, DurationMs={DurationMs}",
                tenantId, sessionId, file.FileId, sizeBytes, chunkCount, indexed.DurationMs);

            return new SessionFileRehydrationResult(
                SessionFileRehydrationOutcome.Reindexed, recoveredText, chunkCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureDisabledException ex)
        {
            _logger.LogDebug(
                "Session-file re-indexing declined by a downstream kill-switch. ErrorCode={ErrorCode}, FileId={FileId}",
                ex.ErrorCode, file.FileId);
            return new SessionFileRehydrationResult(
                SessionFileRehydrationOutcome.TextOnly, recoveredText, 0);
        }
        catch (Exception ex)
        {
            // The text is real even though the index write failed - hand it back rather than throw it
            // away, so text-only consumers still answer.
            _logger.LogError(ex,
                "Session-file re-indexing failed after the durable copy was recovered. TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}",
                tenantId, sessionId, file.FileId);
            return new SessionFileRehydrationResult(
                SessionFileRehydrationOutcome.TextOnly, recoveredText, 0);
        }
    }

    /// <summary>
    /// The manifest's <c>SearchDocumentIdsCsv</c> is the authority every recall consumer post-filters on.
    /// If a re-extraction ever produces a different chunk count (a Document Intelligence model revision
    /// is the realistic cause), the recall silently returns a SUBSET of day-1's content rather than
    /// failing - so it is logged loudly here instead of being discovered as "the answer got shorter".
    /// </summary>
    private void WarnIfChunkCountDivergedFromManifest(
        string tenantId, string sessionId, ChatSessionFile file, int chunkCount)
    {
        var manifestChunkCount = (file.SearchDocumentIdsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;

        if (manifestChunkCount > 0 && manifestChunkCount != chunkCount)
        {
            _logger.LogWarning(
                "Session-file rehydration produced {ChunkCount} chunks but the manifest records " +
                "{ManifestChunkCount}. Recall post-filters on the manifest, so the overlap is what the " +
                "user will see - text extraction is no longer reproducing the day-1 chunking. " +
                "TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}",
                chunkCount, manifestChunkCount, tenantId, sessionId, file.FileId);
        }
    }
}
