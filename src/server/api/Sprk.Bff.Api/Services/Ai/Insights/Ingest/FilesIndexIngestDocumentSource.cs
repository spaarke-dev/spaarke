using System.Text;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.CitationVerification;

namespace Sprk.Bff.Api.Services.Ai.Insights.Ingest;

/// <summary>
/// Production <see cref="IIngestDocumentSource"/> backed by the <c>spaarke-files-index</c>
/// Azure Search index (per SPEC §4.3 — the existing chunked-files pipeline).
/// </summary>
/// <remarks>
/// <para>
/// <b>Query shape</b>: filters by <c>documentId eq '...'</c> + <c>tenantId eq '...'</c>,
/// orders by <c>chunkIndex asc</c>, retrieves up to <see cref="MaxChunks"/> chunks. Builds
/// the document reference from <c>driveId</c> + <c>itemId</c> when present (matching SPEC
/// §3.4.1 worked-example <c>spe://drive/{driveId}/item/{itemId}</c> shape).
/// </para>
/// <para>
/// <b>Field-name resilience</b>: the actual <c>spaarke-files-index</c> schema is owned by
/// the SDAP system, not by Insights Engine. Field-name mismatches would silently return
/// no chunks. To defend against this, the impl uses <see cref="SearchDocument"/> dynamic
/// property access (not a strongly-typed POCO) so missing fields produce structured
/// warning logs rather than parse errors. The 2026-05-28 D-P16 smoke test (task 070)
/// verifies the field names against the deployed index.
/// </para>
/// <para>
/// <b>Null return semantics</b>: returns <c>null</c> when (a) the document id has zero
/// matching chunks in the index, or (b) all chunks have empty content. The orchestrator
/// treats null as a no-op terminal condition (no Observations emitted, no error
/// propagated) — matches the SPEC §3.6 acceptance for documents that never made it
/// into <c>spaarke-files-index</c> (e.g., zero-byte uploads, unsupported file types).
/// </para>
/// </remarks>
internal sealed class FilesIndexIngestDocumentSource : IIngestDocumentSource
{
    /// <summary>
    /// Maximum number of chunks fetched per document. 100 chunks (≈ 200K chars at
    /// 2K chars/chunk) covers any reasonable single document; deeper chunking is a
    /// chunker mis-configuration that should be investigated, not silently consumed.
    /// </summary>
    internal const int MaxChunks = 100;

    private static readonly EventId NotFoundEvent = new(8042, "FilesIndexDocumentNotFound");
    private static readonly EventId EmptyContentEvent = new(8043, "FilesIndexDocumentEmptyContent");
    private static readonly EventId FieldMissingEvent = new(8044, "FilesIndexFieldMissing");

    // Field-name constants matching the SDAP spaarke-files-index schema. Defined as
    // constants (not config) because the contract is upstream-fixed; a schema change
    // upstream is a coordinated event, not a per-deploy knob.
    private const string DocumentIdField = "documentId";
    private const string TenantIdField = "tenantId";
    private const string ChunkIdField = "id";
    private const string ChunkIndexField = "chunkIndex";
    private const string ContentField = "content";
    private const string DriveIdField = "driveId";
    private const string ItemIdField = "itemId";

    private readonly SearchIndexClient _searchIndexClient;
    private readonly AiSearchOptions _options;
    private readonly ILogger<FilesIndexIngestDocumentSource> _logger;

    public FilesIndexIngestDocumentSource(
        SearchIndexClient searchIndexClient,
        IOptions<AiSearchOptions> options,
        ILogger<FilesIndexIngestDocumentSource> logger)
    {
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IngestDocumentContent?> FetchAsync(
        string documentId,
        string tenantId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ct.ThrowIfCancellationRequested();

        var searchClient = _searchIndexClient.GetSearchClient(_options.FilesIndexName);

        var searchOptions = new SearchOptions
        {
            Filter = $"{DocumentIdField} eq '{EscapeODataLiteral(documentId)}' " +
                     $"and {TenantIdField} eq '{EscapeODataLiteral(tenantId)}'",
            OrderBy = { $"{ChunkIndexField} asc" },
            Size = MaxChunks,
            Select =
            {
                ChunkIdField,
                ChunkIndexField,
                ContentField,
                DriveIdField,
                ItemIdField
            }
        };

        var chunks = new List<ChunkRef>(capacity: 8);
        string? driveId = null;
        string? itemId = null;

        try
        {
            var response = await searchClient.SearchAsync<SearchDocument>(
                searchText: "*",
                options: searchOptions,
                cancellationToken: ct);

            await foreach (var hit in response.Value.GetResultsAsync().WithCancellation(ct))
            {
                var doc = hit.Document;

                var chunkId = TryGetString(doc, ChunkIdField);
                var content = TryGetString(doc, ContentField);

                if (string.IsNullOrWhiteSpace(content))
                {
                    // Empty chunk — log + skip. Don't include in result.
                    continue;
                }

                chunks.Add(new ChunkRef(
                    ChunkId: chunkId ?? $"{documentId}#unknown-chunk-{chunks.Count}",
                    Text: content));

                // Capture driveId / itemId from the first chunk only (they're per-document,
                // duplicated on every chunk in the source index).
                if (driveId is null && itemId is null)
                {
                    driveId = TryGetString(doc, DriveIdField);
                    itemId = TryGetString(doc, ItemIdField);
                }
            }
        }
        catch (Azure.RequestFailedException ex)
        {
            // Treat search-side errors as a fail-loud condition — bubble up to the
            // orchestrator caller (D-P8 consumer) so the message can be dead-lettered
            // and retried. Do NOT swallow into a null return.
            _logger.LogError(
                ex,
                "FilesIndexIngestDocumentSource search failed: documentId={DocumentId} tenantId={TenantId} indexName={IndexName} status={Status}",
                documentId, tenantId, _options.FilesIndexName, ex.Status);
            throw;
        }

        if (chunks.Count == 0)
        {
            _logger.Log(
                LogLevel.Information,
                NotFoundEvent,
                "FilesIndexIngestDocumentSource: no chunks found for documentId={DocumentId} tenantId={TenantId} (treating as no-op terminal)",
                documentId, tenantId);
            return null;
        }

        // Build the document reference per SPEC §3.4.1 worked-example shape.
        var documentRef = (driveId is not null && itemId is not null)
            ? $"spe://drive/{driveId}/item/{itemId}"
            : $"file://{documentId}"; // Fallback when SDAP side-data is missing.

        if (driveId is null || itemId is null)
        {
            _logger.Log(
                LogLevel.Warning,
                FieldMissingEvent,
                "FilesIndexIngestDocumentSource: missing driveId/itemId fields on chunks for documentId={DocumentId}; using fallback documentRef={DocumentRef}",
                documentId, documentRef);
        }

        // Build concatenated full text for the Layer 1 + Layer 2 prompts. Join chunks
        // with double newlines so the LLM sees clean chunk boundaries (consistent with
        // how SDAP's existing files-index chunker preserves paragraph boundaries).
        var fullTextBuilder = new StringBuilder(chunks.Sum(c => c.Text.Length) + (chunks.Count * 2));
        for (int i = 0; i < chunks.Count; i++)
        {
            if (i > 0) fullTextBuilder.Append("\n\n");
            fullTextBuilder.Append(chunks[i].Text);
        }
        var fullText = fullTextBuilder.ToString();

        if (string.IsNullOrWhiteSpace(fullText))
        {
            _logger.Log(
                LogLevel.Information,
                EmptyContentEvent,
                "FilesIndexIngestDocumentSource: all chunks had empty content for documentId={DocumentId} (treating as no-op terminal)",
                documentId);
            return null;
        }

        return new IngestDocumentContent(
            DocumentRef: documentRef,
            FullText: fullText,
            Chunks: chunks);
    }

    /// <summary>Try to read a string field; returns null if missing or wrong type.</summary>
    private static string? TryGetString(SearchDocument doc, string field)
    {
        if (!doc.TryGetValue(field, out var raw) || raw is null) return null;
        return raw as string ?? raw.ToString();
    }

    /// <summary>OData literal escape — double single quotes.</summary>
    private static string EscapeODataLiteral(string value) => value.Replace("'", "''");
}
