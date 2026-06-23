using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

/// <summary>
/// Generates embeddings for playbook content and manages the playbook-embeddings AI Search index.
/// Supports indexing, vector similarity search, and deletion of playbook documents.
/// </summary>
/// <remarks>
/// <para>
/// This service is NOT registered in DI (ADR-010 budget constraint — BFF is at 16 non-framework
/// registrations, over the ≤15 limit). It is instantiated via factory pattern at the call site:
/// <code>
/// var service = new PlaybookEmbeddingService(searchIndexClient, openAiClient, logger);
/// </code>
/// </para>
/// <para>
/// Index: playbook-embeddings (infrastructure/ai-search/playbook-embeddings.json)
/// Vector: text-embedding-3-large (3072 dimensions), HNSW with cosine metric
/// </para>
/// <para>
/// Content composition for embedding generation (chat-routing-redesign-r1 FR-10):
/// playbookName + description + triggerPhrases (joined with " | ") + tags (joined with ", ")
/// + documentTypes + intents + jpsTriggerPhrases parsed from
/// <see cref="PlaybookEmbeddingDocument.JpsMatchingMetadata"/> (when present).
/// Sections joined with "\n"; deterministic ordering preserved for cache-key stability.
/// Token limit for text-embedding-3-large is 8191 tokens — content is truncated if necessary.
/// </para>
/// </remarks>
public sealed class PlaybookEmbeddingService
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Index name in Azure AI Search.
    /// </summary>
    internal const string IndexName = "playbook-embeddings";

    /// <summary>
    /// Vector field name in the index (3072-dim text-embedding-3-large).
    /// </summary>
    private const string VectorFieldName = "contentVector3072";

    /// <summary>
    /// Maximum character length for embedding content to stay within token limits.
    /// text-embedding-3-large supports 8191 tokens; ~4 chars/token = ~32K chars.
    /// Using conservative limit since playbook content is typically short.
    /// </summary>
    private const int MaxContentLength = 30_000;

    /// <summary>
    /// Initializes a new instance of <see cref="PlaybookEmbeddingService"/>.
    /// </summary>
    /// <param name="searchIndexClient">Azure AI Search index client for index operations.</param>
    /// <param name="openAiClient">OpenAI client for embedding generation.</param>
    /// <param name="logger">Logger instance.</param>
    /// <remarks>
    /// ADR-010: This service is factory-instantiated, NOT DI-registered.
    /// Callers create instances directly:
    /// <code>new PlaybookEmbeddingService(searchIndexClient, openAiClient, loggerFactory.CreateLogger&lt;PlaybookEmbeddingService&gt;())</code>
    /// </remarks>
    public PlaybookEmbeddingService(
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        ILogger<PlaybookEmbeddingService> logger)
    {
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates an embedding from playbook content and upserts the document into the
    /// playbook-embeddings index.
    /// </summary>
    /// <param name="playbookId">Playbook record identifier (sprk_aiplaybook GUID).</param>
    /// <param name="document">Playbook embedding document with metadata fields populated.
    /// The <see cref="PlaybookEmbeddingDocument.ContentVector3072"/> field will be overwritten
    /// with the generated embedding.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="RequestFailedException">Thrown when Azure AI Search upsert fails.</exception>
    public async Task IndexPlaybookAsync(
        string playbookId,
        PlaybookEmbeddingDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playbookId, nameof(playbookId));
        ArgumentNullException.ThrowIfNull(document);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Indexing playbook {PlaybookId} ({PlaybookName}) into {IndexName}",
            playbookId, document.PlaybookName, IndexName);

        try
        {
            // Step 1: Compose content text for embedding (FR-10: includes JPS matching metadata
            // when present and well-formed; tolerantly falls back to baseline composition on
            // null / missing / malformed JSON).
            var jpsParse = ParseJpsMatchingMetadata(document.JpsMatchingMetadata);
            if (jpsParse.Malformed)
            {
                // ADR-015: Log only playbook ID + parse outcome, NEVER the JSON content.
                _logger.LogWarning(
                    "Malformed sprk_jps_matching_metadata for playbook {PlaybookId} — falling back to baseline embed-input composition",
                    playbookId);
            }
            else if (jpsParse.HasAny)
            {
                _logger.LogDebug(
                    "Parsed sprk_jps_matching_metadata for playbook {PlaybookId}: " +
                    "{DocumentTypeCount} documentTypes, {IntentCount} intents, {TriggerPhraseCount} triggerPhrases",
                    playbookId,
                    jpsParse.DocumentTypes.Count,
                    jpsParse.Intents.Count,
                    jpsParse.TriggerPhrases.Count);
            }

            var contentText = ComposeContentText(document, jpsParse);

            // Step 2: Generate embedding via Azure OpenAI
            var embedding = await _openAiClient.GenerateEmbeddingAsync(
                contentText, cancellationToken: cancellationToken);

            // Step 3: Set embedding and identifiers on document
            document.Id = playbookId;
            document.PlaybookId = playbookId;
            document.ContentVector3072 = embedding;

            // Step 4: Upsert into AI Search index
            var searchClient = _searchIndexClient.GetSearchClient(IndexName);
            var batch = IndexDocumentsBatch.MergeOrUpload(new[] { document });
            var response = await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

            stopwatch.Stop();

            // Check for individual document errors
            var result = response.Value.Results.FirstOrDefault();
            if (result is { Succeeded: false })
            {
                _logger.LogError(
                    "Failed to index playbook {PlaybookId}: {ErrorMessage} (status={Status})",
                    playbookId, result.ErrorMessage, result.Status);
                throw new InvalidOperationException(
                    $"Failed to index playbook {playbookId}: {result.ErrorMessage}");
            }

            _logger.LogInformation(
                "Indexed playbook {PlaybookId} ({PlaybookName}) in {ElapsedMs}ms. " +
                "Content length={ContentLength} chars",
                playbookId, document.PlaybookName, stopwatch.ElapsedMilliseconds,
                contentText.Length);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Azure AI Search error indexing playbook {PlaybookId} into {IndexName}",
                playbookId, IndexName);
            throw;
        }
    }

    /// <summary>
    /// Performs a vector similarity search against the playbook-embeddings index.
    /// Returns the top-K most semantically similar playbooks to the query.
    /// </summary>
    /// <param name="query">Natural language query to match against playbooks.</param>
    /// <param name="recordTypeFilter">Optional filter to restrict results to a specific record type
    /// (e.g., "sprk_matter"). Applied as an OData filter expression.</param>
    /// <param name="topK">Maximum number of results to return. Defaults to 5.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of playbook search results ordered by similarity score (descending).</returns>
    /// <exception cref="RequestFailedException">Thrown when Azure AI Search query fails.</exception>
    public async Task<PlaybookSearchResult[]> SearchPlaybooksAsync(
        string query,
        string? recordTypeFilter = null,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query, nameof(query));

        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Searching playbooks: query length={QueryLength}, recordType={RecordType}, topK={TopK}",
            query.Length, recordTypeFilter ?? "(none)", topK);

        try
        {
            // Step 1: Generate query embedding
            var queryEmbedding = await _openAiClient.GenerateEmbeddingAsync(
                query, cancellationToken: cancellationToken);

            // Step 2: Build search options with vector query
            var searchOptions = new SearchOptions
            {
                Size = topK,
                VectorSearch = new VectorSearchOptions
                {
                    Queries =
                    {
                        new VectorizedQuery(queryEmbedding)
                        {
                            KNearestNeighborsCount = topK,
                            Fields = { VectorFieldName }
                        }
                    }
                }
            };

            // Step 3: Apply optional recordType filter
            if (!string.IsNullOrWhiteSpace(recordTypeFilter))
            {
                searchOptions.Filter = $"recordType eq '{EscapeODataValue(recordTypeFilter)}'";
            }

            // Step 4: Execute vector search
            var searchClient = _searchIndexClient.GetSearchClient(IndexName);
            var response = await searchClient.SearchAsync<PlaybookEmbeddingDocument>(
                null, searchOptions, cancellationToken);

            // Step 5: Process results
            var results = new List<PlaybookSearchResult>();
            await foreach (var result in response.Value.GetResultsAsync()
                               .WithCancellation(cancellationToken))
            {
                if (result.Document is null) continue;

                var doc = result.Document;
                results.Add(new PlaybookSearchResult
                {
                    PlaybookId = doc.PlaybookId,
                    PlaybookName = doc.PlaybookName,
                    Description = doc.Description,
                    TriggerPhrases = doc.TriggerPhrases?.ToList() ?? [],
                    RecordType = doc.RecordType,
                    EntityType = doc.EntityType,
                    Tags = doc.Tags?.ToList() ?? [],
                    Score = result.Score ?? 0
                });
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Playbook search completed: {ResultCount} results in {ElapsedMs}ms " +
                "(query length={QueryLength}, filter={Filter})",
                results.Count, stopwatch.ElapsedMilliseconds,
                query.Length, recordTypeFilter ?? "(none)");

            return results.ToArray();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Azure AI Search error searching playbooks in {IndexName}",
                IndexName);
            throw;
        }
    }

    /// <summary>
    /// Deletes a playbook document from the playbook-embeddings index.
    /// </summary>
    /// <param name="playbookId">Playbook record identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    /// <exception cref="RequestFailedException">Thrown when Azure AI Search delete fails.</exception>
    public async Task DeletePlaybookAsync(
        string playbookId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playbookId, nameof(playbookId));

        _logger.LogDebug("Deleting playbook {PlaybookId} from {IndexName}", playbookId, IndexName);

        try
        {
            var searchClient = _searchIndexClient.GetSearchClient(IndexName);
            var batch = IndexDocumentsBatch.Delete("id", new[] { playbookId });
            var response = await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

            var result = response.Value.Results.FirstOrDefault();
            if (result is { Succeeded: false })
            {
                _logger.LogWarning(
                    "Delete may have failed for playbook {PlaybookId}: {ErrorMessage} (status={Status})",
                    playbookId, result.ErrorMessage, result.Status);
            }
            else
            {
                _logger.LogInformation("Deleted playbook {PlaybookId} from {IndexName}", playbookId, IndexName);
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Azure AI Search error deleting playbook {PlaybookId} from {IndexName}",
                playbookId, IndexName);
            throw;
        }
    }

    #region Private Methods

    /// <summary>
    /// Composes the text content used for embedding generation, tolerantly parsing
    /// <see cref="PlaybookEmbeddingDocument.JpsMatchingMetadata"/> via the internal helper.
    /// Convenience overload that performs its own parse — exposed for testability.
    /// </summary>
    /// <remarks>
    /// FR-10 composition (chat-routing-redesign-r1):
    /// <para>
    /// <c>playbookName</c> + <c>description</c> + <c>triggerPhrases</c> + <c>tags</c>
    /// + (when JPS metadata is present and well-formed)
    /// <c>documentTypes</c> + <c>intents</c> + <c>jpsTriggerPhrases</c>.
    /// </para>
    /// <para>
    /// Sections are joined with <c>\n</c>; arrays within a section are joined consistently
    /// (<c>", "</c> for keyword-style lists, <c>" | "</c> for free-text trigger phrases) to
    /// preserve deterministic ordering for embedding cache-key stability.
    /// </para>
    /// </remarks>
    internal static string ComposeContentText(PlaybookEmbeddingDocument document)
    {
        var jpsParse = ParseJpsMatchingMetadata(document.JpsMatchingMetadata);
        return ComposeContentText(document, jpsParse);
    }

    /// <summary>
    /// Composes the embed-input text from a playbook document and a pre-parsed JPS-metadata
    /// snapshot. Used by <see cref="IndexPlaybookAsync"/> so the call site can log parse
    /// counts / malformed-JSON warnings without re-parsing.
    /// </summary>
    internal static string ComposeContentText(
        PlaybookEmbeddingDocument document,
        JpsMatchingMetadataParse jpsParse)
    {
        var parts = new List<string>
        {
            document.PlaybookName,
            document.Description
        };

        if (document.TriggerPhrases is { Count: > 0 })
        {
            parts.Add(string.Join(" | ", document.TriggerPhrases));
        }

        if (document.Tags is { Count: > 0 })
        {
            parts.Add(string.Join(", ", document.Tags));
        }

        // FR-10: append JPS matching metadata sections when well-formed. Order is
        // documentTypes → intents → triggerPhrases (deterministic for cache stability).
        // Empty arrays contribute no section (no blank-line padding).
        if (jpsParse.DocumentTypes.Count > 0)
        {
            parts.Add(string.Join(", ", jpsParse.DocumentTypes));
        }

        if (jpsParse.Intents.Count > 0)
        {
            parts.Add(string.Join(", ", jpsParse.Intents));
        }

        if (jpsParse.TriggerPhrases.Count > 0)
        {
            parts.Add(string.Join(" | ", jpsParse.TriggerPhrases));
        }

        var content = string.Join("\n", parts);

        // Truncate if necessary to stay within token limits
        if (content.Length > MaxContentLength)
        {
            content = content[..MaxContentLength];
        }

        return content;
    }

    /// <summary>
    /// Tolerantly parses <paramref name="json"/> as the JPS Matching Metadata JSON
    /// (schema at <c>projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/jpsmatchingmetadata-schema.json</c>)
    /// and returns the three array fields used by embed-input composition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Backwards-compatibility contract (chat-routing-redesign-r1 FR-10):
    /// </para>
    /// <list type="bullet">
    ///   <item><description>null / empty / whitespace-only input → empty arrays + <c>Malformed=false</c> (baseline composition).</description></item>
    ///   <item><description>Well-formed JSON object with the field absent or non-array → empty array for that field.</description></item>
    ///   <item><description>Well-formed JSON array of strings → returned verbatim (empty strings are filtered).</description></item>
    ///   <item><description>Malformed JSON / non-object root / parser exception → empty arrays + <c>Malformed=true</c> (caller logs warning).</description></item>
    /// </list>
    /// <para>
    /// Per ADR-015, this helper NEVER logs or surfaces the JSON content itself —
    /// only counts / a malformed flag. The caller is responsible for logging with
    /// the playbook ID.
    /// </para>
    /// <para>
    /// Tolerated-but-extra properties (<c>preferredOver</c>, <c>outputDestination</c>,
    /// <c>scopeHints</c>, <c>exclusionHints</c>) are ignored here — embed-input uses
    /// only the three array fields per FR-10.
    /// </para>
    /// </remarks>
    internal static JpsMatchingMetadataParse ParseJpsMatchingMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JpsMatchingMetadataParse.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                // Tolerant: a non-object root is treated as malformed for our purpose.
                return JpsMatchingMetadataParse.EmptyMalformed;
            }

            var documentTypes = ReadStringArray(doc.RootElement, "documentTypes");
            var intents = ReadStringArray(doc.RootElement, "intents");
            var triggerPhrases = ReadStringArray(doc.RootElement, "triggerPhrases");

            return new JpsMatchingMetadataParse(
                DocumentTypes: documentTypes,
                Intents: intents,
                TriggerPhrases: triggerPhrases,
                Malformed: false);
        }
        catch (JsonException)
        {
            // Tolerant: malformed JSON returns baseline; caller logs warning with playbook ID.
            return JpsMatchingMetadataParse.EmptyMalformed;
        }
    }

    /// <summary>
    /// Reads a JSON property as an array of non-empty strings. Missing property,
    /// null property, non-array property, or non-string array elements all degrade
    /// gracefully to "skip that value" — never throw.
    /// </summary>
    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                list.Add(value);
            }
        }

        return list;
    }

    /// <summary>
    /// Escapes a string value for use in OData filter expressions.
    /// Single quotes are doubled per OData convention.
    /// </summary>
    private static string EscapeODataValue(string value)
    {
        return value.Replace("'", "''");
    }

    #endregion
}

/// <summary>
/// Immutable snapshot of a tolerant parse of <c>sprk_jps_matching_metadata</c> JSON, used
/// by <see cref="PlaybookEmbeddingService.ComposeContentText(PlaybookEmbeddingDocument, JpsMatchingMetadataParse)"/>
/// to build the FR-10 extended embed-input string.
/// </summary>
/// <remarks>
/// All three arrays default to empty; <see cref="Malformed"/> is set when the JSON was non-empty
/// but failed to parse as a JSON object (caller logs warning with playbook ID per ADR-015).
/// </remarks>
internal sealed record JpsMatchingMetadataParse(
    IReadOnlyList<string> DocumentTypes,
    IReadOnlyList<string> Intents,
    IReadOnlyList<string> TriggerPhrases,
    bool Malformed)
{
    /// <summary>
    /// Baseline result for null / empty / whitespace input — empty arrays, not malformed.
    /// </summary>
    public static JpsMatchingMetadataParse Empty { get; } =
        new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Malformed: false);

    /// <summary>
    /// Baseline result for parse failure — empty arrays, malformed flag set so caller can log.
    /// </summary>
    public static JpsMatchingMetadataParse EmptyMalformed { get; } =
        new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Malformed: true);

    /// <summary>True iff any of the three arrays has at least one element.</summary>
    public bool HasAny => DocumentTypes.Count > 0 || Intents.Count > 0 || TriggerPhrases.Count > 0;
}
