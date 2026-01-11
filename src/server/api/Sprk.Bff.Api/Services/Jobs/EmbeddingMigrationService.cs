using System.Text.Json.Serialization;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Configuration options for the embedding migration service.
/// </summary>
public class EmbeddingMigrationOptions
{
    public const string SectionName = "EmbeddingMigration";

    /// <summary>
    /// Whether to run the embedding migration on service startup.
    /// Default: false (must be explicitly enabled)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Batch size for processing chunks (affects Azure OpenAI batch embedding calls).
    /// Default: 20 (balances throughput with rate limits)
    /// </summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Maximum concurrent embedding requests to Azure OpenAI.
    /// Default: 5 (respects TPM/RPM limits)
    /// </summary>
    public int ConcurrencyLimit { get; set; } = 5;

    /// <summary>
    /// Delay between batches in milliseconds.
    /// Default: 2000 (2 seconds) - allows rate limit recovery
    /// </summary>
    public int DelayBetweenBatchesMs { get; set; } = 2000;

    /// <summary>
    /// Tenant IDs to process. If empty, processes all tenants in shared index.
    /// </summary>
    public List<string> TenantIds { get; set; } = new();

    /// <summary>
    /// Maximum documents to process (0 for unlimited).
    /// Useful for testing or incremental migration.
    /// </summary>
    public int MaxDocuments { get; set; } = 0;

    /// <summary>
    /// Last processed document ID for resuming migration.
    /// Set this to resume from a specific point after restart.
    /// </summary>
    public string? ResumeFromDocumentId { get; set; }

    /// <summary>
    /// Whether to process only documents that are missing 3072-dim vectors.
    /// Default: true (skip already migrated documents)
    /// </summary>
    public bool SkipAlreadyMigrated { get; set; } = true;
}

/// <summary>
/// Background service that migrates document embeddings from 1536 to 3072 dimensions.
/// Re-embeds content using text-embedding-3-large and updates the 3072-dim vector fields.
/// </summary>
/// <remarks>
/// <para>
/// This is a migration service for Phase 5b of the schema unification project.
/// It re-generates embeddings for all indexed documents using the higher-quality
/// text-embedding-3-large model (3072 dimensions).
/// </para>
///
/// <para>
/// <strong>Processing Logic:</strong>
/// <list type="number">
/// <item>Query for documents missing contentVector3072 (by tenant)</item>
/// <item>Batch chunks by documentId for document-level vector computation</item>
/// <item>Generate 3072-dim embeddings via OpenAI API</item>
/// <item>Compute document-level vector as normalized average of chunk vectors</item>
/// <item>Update contentVector3072 and documentVector3072 in index</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Rate Limiting:</strong>
/// Uses SemaphoreSlim for concurrency control and delays between batches
/// to respect Azure OpenAI TPM/RPM limits.
/// </para>
///
/// <para>
/// <strong>Configuration:</strong>
/// Enable via appsettings.json:
/// <code>
/// "EmbeddingMigration": {
///   "Enabled": true,
///   "BatchSize": 20,
///   "ConcurrencyLimit": 5,
///   "DelayBetweenBatchesMs": 2000,
///   "TenantIds": ["tenant-1"],
///   "MaxDocuments": 1000,
///   "ResumeFromDocumentId": "doc-123"
/// }
/// </code>
/// </para>
/// </remarks>
public class EmbeddingMigrationService : BackgroundService
{
    private readonly IKnowledgeDeploymentService _deploymentService;
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<EmbeddingMigrationService> _logger;
    private readonly EmbeddingMigrationOptions _options;
    private readonly DocumentIntelligenceOptions _docIntelOptions;

    private const int VectorDimensions3072 = 3072;

    // Progress tracking
    private int _totalProcessed;
    private int _totalMigrated;
    private int _totalErrors;
    private string? _lastProcessedDocumentId;

    public EmbeddingMigrationService(
        IKnowledgeDeploymentService deploymentService,
        IOpenAiClient openAiClient,
        IOptions<EmbeddingMigrationOptions> options,
        IOptions<DocumentIntelligenceOptions> docIntelOptions,
        ILogger<EmbeddingMigrationService> logger)
    {
        _deploymentService = deploymentService;
        _openAiClient = openAiClient;
        _options = options.Value;
        _docIntelOptions = docIntelOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("[MIGRATION] Embedding migration is disabled");
            return;
        }

        _logger.LogInformation(
            "[MIGRATION] Starting embedding migration to 3072 dimensions: " +
            "BatchSize={BatchSize}, Concurrency={Concurrency}, MaxDocuments={MaxDocuments}",
            _options.BatchSize, _options.ConcurrencyLimit, _options.MaxDocuments);

        try
        {
            // Delay startup to allow other services to initialize
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            // Process each configured tenant (or all if none specified)
            var tenantIds = _options.TenantIds.Count > 0
                ? _options.TenantIds
                : await GetAllTenantIdsAsync(stoppingToken);

            if (tenantIds.Count == 0)
            {
                _logger.LogWarning("[MIGRATION] No tenants to process. Configure TenantIds in EmbeddingMigration section.");
                return;
            }

            foreach (var tenantId in tenantIds)
            {
                if (stoppingToken.IsCancellationRequested) break;

                await ProcessTenantAsync(tenantId, stoppingToken);

                if (_options.MaxDocuments > 0 && _totalProcessed >= _options.MaxDocuments)
                {
                    _logger.LogInformation("[MIGRATION] Reached max documents limit: {Max}", _options.MaxDocuments);
                    break;
                }
            }

            _logger.LogInformation(
                "[MIGRATION] Embedding migration completed: " +
                "Processed={Processed}, Migrated={Migrated}, Errors={Errors}, LastDoc={LastDoc}",
                _totalProcessed, _totalMigrated, _totalErrors, _lastProcessedDocumentId ?? "(none)");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "[MIGRATION] Embedding migration cancelled. Progress: Processed={Processed}, LastDoc={LastDoc}",
                _totalProcessed, _lastProcessedDocumentId ?? "(none)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[MIGRATION] Embedding migration failed: {Error}. Progress: Processed={Processed}, LastDoc={LastDoc}",
                ex.Message, _totalProcessed, _lastProcessedDocumentId ?? "(none)");
        }
    }

    /// <summary>
    /// Process all documents for a single tenant.
    /// </summary>
    private async Task ProcessTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[MIGRATION] Processing tenant: {TenantId}", tenantId);

        var tenantProcessed = 0;
        var tenantMigrated = 0;
        var tenantErrors = 0;

        try
        {
            var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);

            // Get unique document IDs that need migration
            var documentIds = await GetDocumentIdsNeedingMigrationAsync(
                searchClient, tenantId, cancellationToken);

            _logger.LogInformation(
                "[MIGRATION] Found {Count} documents needing migration for tenant {TenantId}",
                documentIds.Count, tenantId);

            // Use semaphore for rate limiting
            using var semaphore = new SemaphoreSlim(_options.ConcurrencyLimit, _options.ConcurrencyLimit);

            foreach (var documentId in documentIds)
            {
                if (cancellationToken.IsCancellationRequested) break;

                await semaphore.WaitAsync(cancellationToken);

                try
                {
                    var wasMigrated = await MigrateDocumentAsync(
                        searchClient, tenantId, documentId, cancellationToken);

                    tenantProcessed++;
                    _totalProcessed++;
                    _lastProcessedDocumentId = documentId;

                    if (wasMigrated)
                    {
                        tenantMigrated++;
                        _totalMigrated++;
                    }

                    // Progress logging every 50 documents
                    if (tenantProcessed % 50 == 0)
                    {
                        _logger.LogInformation(
                            "[MIGRATION] Progress: {Processed}/{Total} documents for tenant {TenantId}",
                            tenantProcessed, documentIds.Count, tenantId);
                    }

                    // Rate limiting delay between batches
                    if (tenantProcessed % _options.BatchSize == 0 && _options.DelayBetweenBatchesMs > 0)
                    {
                        await Task.Delay(_options.DelayBetweenBatchesMs, cancellationToken);
                    }

                    // Check max documents limit
                    if (_options.MaxDocuments > 0 && _totalProcessed >= _options.MaxDocuments)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    tenantErrors++;
                    _totalErrors++;
                    _logger.LogWarning(ex,
                        "[MIGRATION] Error migrating document {DocumentId} for tenant {TenantId}: {Error}",
                        documentId, tenantId, ex.Message);

                    // Exponential backoff on errors
                    await Task.Delay(Math.Min(5000, _options.DelayBetweenBatchesMs * 2), cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[MIGRATION] Error processing tenant {TenantId}: {Error}",
                tenantId, ex.Message);
        }

        _logger.LogInformation(
            "[MIGRATION] Tenant {TenantId} complete: Processed={Processed}, Migrated={Migrated}, Errors={Errors}",
            tenantId, tenantProcessed, tenantMigrated, tenantErrors);
    }

    /// <summary>
    /// Get document IDs that need 3072-dim embedding migration.
    /// </summary>
    private async Task<List<string>> GetDocumentIdsNeedingMigrationAsync(
        SearchClient searchClient,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var documentIds = new HashSet<string>();

        // Build filter for documents needing migration
        var filter = $"tenantId eq '{EscapeFilterValue(tenantId)}'";

        // If resuming, start after the last processed document
        if (!string.IsNullOrEmpty(_options.ResumeFromDocumentId))
        {
            filter += $" and documentId gt '{EscapeFilterValue(_options.ResumeFromDocumentId)}'";
            _logger.LogInformation("[MIGRATION] Resuming from document ID: {DocId}", _options.ResumeFromDocumentId);
        }

        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Select = { "documentId", "contentVector3072" },
            Size = 1000,
            OrderBy = { "documentId" }
        };

        var skip = 0;

        do
        {
            searchOptions.Skip = skip;

            var results = await searchClient.SearchAsync<MigrationDocument>("*", searchOptions, cancellationToken);

            var batchCount = 0;
            await foreach (var result in results.Value.GetResultsAsync())
            {
                batchCount++;

                // Skip if already has 3072-dim vector (when SkipAlreadyMigrated is true)
                if (_options.SkipAlreadyMigrated && result.Document.ContentVector3072.Length > 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(result.Document.DocumentId))
                {
                    documentIds.Add(result.Document.DocumentId);
                }
            }

            // If we got fewer results than page size, we're done
            if (batchCount < 1000)
            {
                break;
            }

            skip += 1000;
        }
        while (true);

        return documentIds.OrderBy(id => id).ToList();
    }

    /// <summary>
    /// Migrate a single document: re-embed all chunks with 3072 dimensions.
    /// </summary>
    private async Task<bool> MigrateDocumentAsync(
        SearchClient searchClient,
        string tenantId,
        string documentId,
        CancellationToken cancellationToken)
    {
        // Get all chunks for this document
        var searchOptions = new SearchOptions
        {
            Filter = $"tenantId eq '{EscapeFilterValue(tenantId)}' and documentId eq '{EscapeFilterValue(documentId)}'",
            Select = { "id", "content", "contentVector3072", "documentVector3072" },
            Size = 1000,
            OrderBy = { "chunkIndex" }
        };

        var results = await searchClient.SearchAsync<MigrationDocument>("*", searchOptions, cancellationToken);

        var chunks = new List<MigrationDocument>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            chunks.Add(result.Document);
        }

        if (chunks.Count == 0)
        {
            _logger.LogDebug("[MIGRATION] No chunks found for document {DocumentId}", documentId);
            return false;
        }

        // Check if already migrated (first chunk has 3072-dim vector)
        if (_options.SkipAlreadyMigrated && chunks[0].ContentVector3072.Length == VectorDimensions3072)
        {
            _logger.LogDebug("[MIGRATION] Document {DocumentId} already has 3072-dim vectors", documentId);
            return false;
        }

        // Get content from all chunks for batch embedding
        var contents = chunks
            .Where(c => !string.IsNullOrWhiteSpace(c.Content))
            .Select(c => c.Content)
            .ToList();

        if (contents.Count == 0)
        {
            _logger.LogWarning("[MIGRATION] Document {DocumentId} has no content to embed", documentId);
            return false;
        }

        // Generate 3072-dim embeddings for all chunks
        var embeddings = await _openAiClient.GenerateEmbeddingsAsync(
            contents,
            _docIntelOptions.EmbeddingModel,
            VectorDimensions3072,
            cancellationToken);

        if (embeddings.Count != contents.Count)
        {
            _logger.LogWarning(
                "[MIGRATION] Embedding count mismatch for document {DocumentId}: expected {Expected}, got {Actual}",
                documentId, contents.Count, embeddings.Count);
            return false;
        }

        // Compute document-level vector (normalized average of chunk vectors)
        var documentVector = ComputeAverageVector(embeddings.ToList());

        // Prepare batch update
        var updates = new List<MigrationUpdate>();
        var embeddingIndex = 0;

        foreach (var chunk in chunks)
        {
            var contentVector = !string.IsNullOrWhiteSpace(chunk.Content) && embeddingIndex < embeddings.Count
                ? embeddings[embeddingIndex++]
                : ReadOnlyMemory<float>.Empty;

            updates.Add(new MigrationUpdate
            {
                Id = chunk.Id,
                ContentVector3072 = contentVector,
                DocumentVector3072 = documentVector
            });
        }

        // Batch update to index
        var batch = IndexDocumentsBatch.MergeOrUpload(updates);
        var response = await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

        var successCount = response.Value.Results.Count(r => r.Succeeded);
        if (successCount != chunks.Count)
        {
            _logger.LogWarning(
                "[MIGRATION] Partial update for document {DocumentId}: {Success}/{Total} chunks updated",
                documentId, successCount, chunks.Count);
        }

        _logger.LogDebug(
            "[MIGRATION] Migrated document {DocumentId}: {ChunkCount} chunks, vector dim={Dim}",
            documentId, chunks.Count, documentVector.Length);

        return true;
    }

    /// <summary>
    /// Compute average of multiple vectors with L2 normalization.
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

        // Sum all vectors
        foreach (var vector in vectors)
        {
            var span = vector.Span;
            for (int i = 0; i < dimensions; i++)
            {
                result[i] += span[i];
            }
        }

        // Average
        var count = vectors.Count;
        for (int i = 0; i < dimensions; i++)
        {
            result[i] /= count;
        }

        // L2 normalization
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
    /// Get all tenant IDs from the index.
    /// </summary>
    private Task<List<string>> GetAllTenantIdsAsync(CancellationToken cancellationToken)
    {
        // For explicit control, require tenant IDs in configuration
        _logger.LogWarning("[MIGRATION] No tenant IDs configured. Please specify TenantIds in EmbeddingMigration section.");
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
    /// Document model for migration queries.
    /// </summary>
    private record MigrationDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("documentId")]
        public string DocumentId { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;

        [JsonPropertyName("contentVector3072")]
        public ReadOnlyMemory<float> ContentVector3072 { get; init; }

        [JsonPropertyName("documentVector3072")]
        public ReadOnlyMemory<float> DocumentVector3072 { get; init; }
    }

    /// <summary>
    /// Document model for migration updates.
    /// </summary>
    private record MigrationUpdate
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("contentVector3072")]
        public ReadOnlyMemory<float> ContentVector3072 { get; init; }

        [JsonPropertyName("documentVector3072")]
        public ReadOnlyMemory<float> DocumentVector3072 { get; init; }
    }
}
