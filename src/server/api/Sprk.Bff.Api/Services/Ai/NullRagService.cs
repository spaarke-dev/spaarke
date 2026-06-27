using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Null-Object implementation of <see cref="IRagService"/> registered when the compound
/// AI kill-switch is OFF OR when AI Search keys are unconfigured.
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 B7. Returning empty search results would mislead
/// consumers into believing the knowledge base is empty rather than the kill switch
/// being engaged — fail-fast clarifies the state.
/// </para>
/// <para>
/// Note: Tier 3 (D-09 §2 B8) will refactor <c>KnowledgeBaseEndpoints</c> to consume
/// <see cref="IRagService"/> in place of direct <c>SearchIndexClient</c> calls; after that
/// refactor, this Null-Object will additionally cover the KB health / list / delete paths
/// without further code changes.
/// </para>
/// <para>Introduced 2026-06-01 by task 011 Phase 1b Tier 2.</para>
/// </remarks>
public sealed class NullRagService : IRagService
{
    private const string ErrorCode = "ai.rag.disabled";
    private const string DetailMessage =
        "RAG (knowledge base) services require Analysis:Enabled=true, DocumentIntelligence:Enabled=true, and configured AI Search keys.";

    private readonly ILogger<NullRagService> _logger;

    public NullRagService(ILogger<NullRagService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<RagSearchResponse> SearchAsync(
        string query,
        RagSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(SearchAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<RagSearchResponse> SearchAsync(
        RagQuery ragQuery,
        Guid? deploymentId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(SearchAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<KnowledgeDocument> IndexDocumentAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(IndexDocumentAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
        IEnumerable<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(IndexDocumentsBatchAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    // multi-container-multi-index-r1 indexer-routing-fix (Tier 3) — 3-arg overload mirrors
    // the 2-arg behavior (fail-fast P3 Null-Object pattern). The kill-switch is engaged so
    // any caller — including new searchIndexName-aware ones — surfaces the disabled state
    // distinctly via FeatureDisabledException (mapped to 503 ProblemDetails by endpoints).
    public Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
        IEnumerable<KnowledgeDocument> documents,
        string? searchIndexName,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(IndexDocumentsBatchAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<bool> DeleteDocumentAsync(
        string documentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(DeleteDocumentAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<int> DeleteBySourceDocumentAsync(
        string sourceDocumentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(DeleteBySourceDocumentAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<ReadOnlyMemory<float>> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetEmbeddingAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    // ── B8 — knowledge-base index administration (task 011 Phase 1b Tier 3, D-09 §2 B8) ───
    // These methods absorb what KnowledgeBaseEndpoints used to do directly via SearchIndexClient.

    public Task<KnowledgeIndexHealth> GetIndexHealthAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetIndexHealthAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<IndexedDocumentsPage> GetIndexedDocumentsAsync(
        string indexName,
        string tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetIndexedDocumentsAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<int> DeleteIndexedDocumentAsync(
        string indexName,
        string documentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(DeleteIndexedDocumentAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullRagService.{Method} invoked while AI feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
