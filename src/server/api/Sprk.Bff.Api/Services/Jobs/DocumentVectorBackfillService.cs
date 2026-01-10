using System.Text.Json.Serialization;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Configuration options for the document vector backfill service.
/// </summary>
public class DocumentVectorBackfillOptions
{
    public const string SectionName = "DocumentVectorBackfill";

    /// <summary>
    /// Whether to run the backfill on service startup.
    /// Default: false (must be explicitly enabled)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Batch size for processing documents.
    /// Default: 100
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Tenant IDs to process. If empty, processes all tenants in shared index.
    /// </summary>
    public List<string> TenantIds { get; set; } = new();

    /// <summary>
    /// Maximum documents to process (0 for unlimited).
    /// Useful for testing with a small subset.
    /// </summary>
    public int MaxDocuments { get; set; } = 0;

    /// <summary>
    /// Delay between batches in milliseconds.
    /// Default: 1000 (1 second)
    /// </summary>
    public int BatchDelayMs { get; set; } = 1000;
}

/// <summary>
/// Background service that backfills documentVector for existing indexed documents.
/// Computes document-level embeddings by averaging chunk contentVector values.
/// </summary>
/// <remarks>
/// <para>
/// This is a one-time migration service that should be run when the documentVector
/// field is added to the index schema. After all documents are backfilled, this
/// service can be disabled via configuration.
/// </para>
///
/// <para>
/// <strong>Processing Logic:</strong>
/// <list type="number">
/// <item>Query for unique documentIds that have chunks with contentVector</item>
/// <item>For each document, retrieve all chunks</item>
/// <item>Compute average of all chunk contentVector values</item>
/// <item>Update all chunks for that document with the computed documentVector</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Configuration:</strong>
/// Enable via appsettings.json:
/// <code>
/// "DocumentVectorBackfill": {
///   "Enabled": true,
///   "BatchSize": 100,
///   "TenantIds": ["tenant-1", "tenant-2"],
///   "MaxDocuments": 1000,
///   "BatchDelayMs": 1000
/// }
/// </code>
/// </para>
/// </remarks>
public class DocumentVectorBackfillService : BackgroundService
{
    private readonly IKnowledgeDeploymentService _deploymentService;
    private readonly ILogger<DocumentVectorBackfillService> _logger;
    private readonly DocumentVectorBackfillOptions _options;

    private const int VectorDimensions = 1536;

    public DocumentVectorBackfillService(
        IKnowledgeDeploymentService deploymentService,
        IOptions<DocumentVectorBackfillOptions> options,
        ILogger<DocumentVectorBackfillService> logger)
    {
        _deploymentService = deploymentService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("[BACKFILL] Document vector backfill is disabled");
            return;
        }

        _logger.LogInformation(
            "[BACKFILL] Starting document vector backfill: BatchSize={BatchSize}, MaxDocuments={MaxDocuments}",
            _options.BatchSize, _options.MaxDocuments);

        try
        {
            // Delay startup to allow other services to initialize
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            var totalProcessed = 0;
            var totalUpdated = 0;
            var totalErrors = 0;

            // Process each configured tenant (or all if none specified)
            var tenantIds = _options.TenantIds.Count > 0
                ? _options.TenantIds
                : await GetAllTenantIdsAsync(stoppingToken);

            foreach (var tenantId in tenantIds)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var (processed, updated, errors) = await ProcessTenantAsync(tenantId, stoppingToken);
                totalProcessed += processed;
                totalUpdated += updated;
                totalErrors += errors;

                if (_options.MaxDocuments > 0 && totalProcessed >= _options.MaxDocuments)
                {
                    _logger.LogInformation("[BACKFILL] Reached max documents limit: {Max}", _options.MaxDocuments);
                    break;
                }
            }

            _logger.LogInformation(
                "[BACKFILL] Document vector backfill completed: Processed={Processed}, Updated={Updated}, Errors={Errors}",
                totalProcessed, totalUpdated, totalErrors);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[BACKFILL] Document vector backfill cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BACKFILL] Document vector backfill failed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Process all documents for a single tenant.
    /// </summary>
    private async Task<(int processed, int updated, int errors)> ProcessTenantAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[BACKFILL] Processing tenant: {TenantId}", tenantId);

        var processed = 0;
        var updated = 0;
        var errors = 0;

        try
        {
            var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);

            // Get all unique document IDs for this tenant
            var documentIds = await GetUniqueDocumentIdsAsync(searchClient, tenantId, cancellationToken);
            _logger.LogInformation("[BACKFILL] Found {Count} unique documents for tenant {TenantId}",
                documentIds.Count, tenantId);

            foreach (var documentId in documentIds)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var wasUpdated = await ProcessDocumentAsync(searchClient, tenantId, documentId, cancellationToken);
                    processed++;
                    if (wasUpdated) updated++;

                    if (processed % 100 == 0)
                    {
                        _logger.LogInformation(
                            "[BACKFILL] Progress: {Processed}/{Total} documents for tenant {TenantId}",
                            processed, documentIds.Count, tenantId);
                    }

                    // Rate limiting
                    if (_options.BatchDelayMs > 0 && processed % _options.BatchSize == 0)
                    {
                        await Task.Delay(_options.BatchDelayMs, cancellationToken);
                    }

                    if (_options.MaxDocuments > 0 && processed >= _options.MaxDocuments)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[BACKFILL] Error processing document {DocumentId} for tenant {TenantId}: {Error}",
                        documentId, tenantId, ex.Message);
                    errors++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BACKFILL] Error processing tenant {TenantId}: {Error}", tenantId, ex.Message);
        }

        _logger.LogInformation(
            "[BACKFILL] Tenant {TenantId} complete: Processed={Processed}, Updated={Updated}, Errors={Errors}",
            tenantId, processed, updated, errors);

        return (processed, updated, errors);
    }

    /// <summary>
    /// Get all unique document IDs for a tenant.
    /// </summary>
    private async Task<List<string>> GetUniqueDocumentIdsAsync(
        SearchClient searchClient,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var documentIds = new HashSet<string>();
        var searchOptions = new SearchOptions
        {
            Filter = $"tenantId eq '{EscapeFilterValue(tenantId)}'",
            Select = { "documentId" },
            Size = 1000,
            OrderBy = { "documentId" }
        };

        string? continuationToken = null;

        do
        {
            SearchResults<BackfillDocument> results;

            if (continuationToken != null)
            {
                // Use skip for pagination (Azure AI Search doesn't have native continuation tokens for search)
                searchOptions.Skip = documentIds.Count;
            }

            results = await searchClient.SearchAsync<BackfillDocument>("*", searchOptions, cancellationToken);

            var hasResults = false;
            await foreach (var result in results.GetResultsAsync())
            {
                hasResults = true;
                if (!string.IsNullOrEmpty(result.Document.DocumentId))
                {
                    documentIds.Add(result.Document.DocumentId);
                }
            }

            // If we got no results or fewer than page size, we're done
            if (!hasResults || documentIds.Count % 1000 != 0)
            {
                break;
            }
        }
        while (true);

        return documentIds.ToList();
    }

    /// <summary>
    /// Process a single document: compute average vector and update all chunks.
    /// </summary>
    private async Task<bool> ProcessDocumentAsync(
        SearchClient searchClient,
        string tenantId,
        string documentId,
        CancellationToken cancellationToken)
    {
        // Get all chunks for this document
        var searchOptions = new SearchOptions
        {
            Filter = $"tenantId eq '{EscapeFilterValue(tenantId)}' and documentId eq '{EscapeFilterValue(documentId)}'",
            Select = { "id", "contentVector", "documentVector" },
            Size = 1000
        };

        var results = await searchClient.SearchAsync<BackfillDocument>("*", searchOptions, cancellationToken);

        var chunks = new List<BackfillDocument>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            chunks.Add(result.Document);
        }

        if (chunks.Count == 0)
        {
            _logger.LogDebug("[BACKFILL] No chunks found for document {DocumentId}", documentId);
            return false;
        }

        // Check if documentVector is already populated on first chunk
        if (chunks[0].DocumentVector.Length > 0)
        {
            _logger.LogDebug("[BACKFILL] Document {DocumentId} already has documentVector", documentId);
            return false;
        }

        // Collect all chunk vectors
        var validVectors = chunks
            .Where(c => c.ContentVector.Length == VectorDimensions)
            .Select(c => c.ContentVector)
            .ToList();

        if (validVectors.Count == 0)
        {
            _logger.LogWarning("[BACKFILL] Document {DocumentId} has no valid contentVector values", documentId);
            return false;
        }

        // Compute average vector
        var avgVector = ComputeAverageVector(validVectors);

        // Update all chunks with the computed documentVector
        var updates = chunks.Select(c => new BackfillUpdate
        {
            Id = c.Id,
            DocumentVector = avgVector
        }).ToList();

        // Batch update
        var batch = IndexDocumentsBatch.MergeOrUpload(updates);
        var response = await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

        var successCount = response.Value.Results.Count(r => r.Succeeded);
        if (successCount != chunks.Count)
        {
            _logger.LogWarning(
                "[BACKFILL] Partial update for document {DocumentId}: {Success}/{Total} chunks updated",
                documentId, successCount, chunks.Count);
        }

        return true;
    }

    /// <summary>
    /// Compute average of multiple vectors (average pooling).
    /// </summary>
    private static ReadOnlyMemory<float> ComputeAverageVector(List<ReadOnlyMemory<float>> vectors)
    {
        if (vectors.Count == 0)
        {
            return ReadOnlyMemory<float>.Empty;
        }

        if (vectors.Count == 1)
        {
            return vectors[0];
        }

        var dimensions = vectors[0].Length;
        var result = new float[dimensions];

        foreach (var vector in vectors)
        {
            var span = vector.Span;
            for (int i = 0; i < dimensions; i++)
            {
                result[i] += span[i];
            }
        }

        var count = vectors.Count;
        for (int i = 0; i < dimensions; i++)
        {
            result[i] /= count;
        }

        // Normalize the result vector (L2 normalization)
        var magnitude = 0f;
        for (int i = 0; i < dimensions; i++)
        {
            magnitude += result[i] * result[i];
        }
        magnitude = MathF.Sqrt(magnitude);

        if (magnitude > 0)
        {
            for (int i = 0; i < dimensions; i++)
            {
                result[i] /= magnitude;
            }
        }

        return result;
    }

    /// <summary>
    /// Get all tenant IDs from the index (for processing all tenants).
    /// </summary>
    private Task<List<string>> GetAllTenantIdsAsync(CancellationToken cancellationToken)
    {
        // For now, return empty list - caller should specify tenant IDs in config
        // A full implementation would query the index for distinct tenantId values
        _logger.LogWarning("[BACKFILL] No tenant IDs configured. Please specify TenantIds in configuration.");
        return Task.FromResult(new List<string>());
    }

    /// <summary>
    /// Escape special characters for OData filter values.
    /// </summary>
    private static string EscapeFilterValue(string value)
    {
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Document model for backfill queries.
    /// </summary>
    private record BackfillDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("documentId")]
        public string DocumentId { get; init; } = string.Empty;

        [JsonPropertyName("contentVector")]
        public ReadOnlyMemory<float> ContentVector { get; init; }

        [JsonPropertyName("documentVector")]
        public ReadOnlyMemory<float> DocumentVector { get; init; }
    }

    /// <summary>
    /// Document model for backfill updates (only updates documentVector).
    /// </summary>
    private record BackfillUpdate
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("documentVector")]
        public ReadOnlyMemory<float> DocumentVector { get; init; }
    }
}
