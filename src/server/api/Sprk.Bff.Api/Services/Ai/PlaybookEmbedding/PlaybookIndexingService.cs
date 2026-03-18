using System.Diagnostics;
using System.Threading.Channels;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

/// <summary>
/// Background service that processes playbook embedding indexing requests.
/// Receives indexing requests via a bounded channel and processes them sequentially
/// with bounded concurrency (ADR-016).
/// </summary>
/// <remarks>
/// <para>
/// This service is NOT registered in DI (ADR-010 budget constraint). It is instantiated
/// once by the <see cref="PlaybookIndexingBackgroundService"/> which is the hosted service entry point.
/// </para>
/// <para>
/// Pipeline: Trigger endpoint enqueues playbookId -> Channel -> This service dequeues ->
/// Fetch from Dataverse via IPlaybookService -> Generate embedding -> Upsert into AI Search index.
/// </para>
/// <para>
/// Error handling per task spec: failures are logged at Warning level with playbookId only
/// (no content per ADR-015). Stale index entries are preferred over missing ones.
/// </para>
/// </remarks>
public sealed class PlaybookIndexingService
{
    private readonly Channel<string> _indexingChannel;
    private readonly IPlaybookService _playbookService;
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PlaybookIndexingService> _logger;

    /// <summary>
    /// Maximum number of concurrent indexing operations (ADR-016: bound concurrent AI calls).
    /// </summary>
    private readonly SemaphoreSlim _concurrencyGate = new(3, 3);

    /// <summary>
    /// Bounded channel capacity — prevents unbounded memory growth if indexing falls behind.
    /// </summary>
    private const int ChannelCapacity = 100;

    /// <summary>
    /// Initializes a new instance of <see cref="PlaybookIndexingService"/>.
    /// </summary>
    /// <param name="playbookService">Service to fetch playbook data from Dataverse.</param>
    /// <param name="searchIndexClient">Azure AI Search index client.</param>
    /// <param name="openAiClient">OpenAI client for embedding generation.</param>
    /// <param name="loggerFactory">Logger factory for creating typed loggers.</param>
    public PlaybookIndexingService(
        IPlaybookService playbookService,
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        ILoggerFactory loggerFactory)
    {
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<PlaybookIndexingService>();

        _indexingChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Enqueues a playbook for embedding indexing. Returns immediately (fire-and-forget).
    /// </summary>
    /// <param name="playbookId">The playbook GUID to index.</param>
    /// <returns>True if enqueued successfully, false if channel is full (oldest will be dropped).</returns>
    public bool EnqueueIndexing(string playbookId)
    {
        if (string.IsNullOrWhiteSpace(playbookId))
        {
            _logger.LogWarning("Attempted to enqueue empty playbookId for indexing");
            return false;
        }

        var written = _indexingChannel.Writer.TryWrite(playbookId);
        if (written)
        {
            _logger.LogDebug("Enqueued playbook {PlaybookId} for embedding indexing", playbookId);
        }
        else
        {
            _logger.LogWarning(
                "Indexing channel full — oldest request dropped when enqueuing playbook {PlaybookId}",
                playbookId);
        }

        return written;
    }

    /// <summary>
    /// Processes indexing requests from the channel. Called by the background service.
    /// Runs until cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop processing.</param>
    public async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Playbook indexing queue processor started");

        try
        {
            await foreach (var playbookId in _indexingChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await _concurrencyGate.WaitAsync(cancellationToken);
                try
                {
                    await IndexPlaybookInternalAsync(playbookId, cancellationToken);
                }
                finally
                {
                    _concurrencyGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Playbook indexing queue processor stopping (cancellation requested)");
        }
    }

    /// <summary>
    /// Fetches playbook from Dataverse, generates embedding, and upserts into AI Search index.
    /// </summary>
    private async Task IndexPlaybookInternalAsync(string playbookId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Step 1: Fetch playbook from Dataverse
            if (!Guid.TryParse(playbookId, out var playbookGuid))
            {
                _logger.LogWarning("Invalid playbookId format for indexing: {PlaybookId}", playbookId);
                return;
            }

            var playbook = await _playbookService.GetPlaybookAsync(playbookGuid, cancellationToken);
            if (playbook is null)
            {
                _logger.LogWarning(
                    "Playbook {PlaybookId} not found in Dataverse — skipping indexing",
                    playbookId);
                return;
            }

            // Step 2: Build embedding document from playbook data
            var document = new PlaybookEmbeddingDocument
            {
                Id = playbookId,
                PlaybookId = playbookId,
                PlaybookName = playbook.Name,
                Description = playbook.Description ?? string.Empty,
                TriggerPhrases = playbook.TriggerPhrases?.ToList() ?? [],
                RecordType = playbook.RecordType ?? string.Empty,
                EntityType = playbook.EntityType ?? string.Empty,
                Tags = ParseTags(playbook)
            };

            // Step 3: Use PlaybookEmbeddingService to generate embedding and upsert
            // Factory instantiation per ADR-010
            var embeddingService = new PlaybookEmbeddingService(
                _searchIndexClient,
                _openAiClient,
                _loggerFactory.CreateLogger<PlaybookEmbeddingService>());

            await embeddingService.IndexPlaybookAsync(playbookId, document, cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Background indexing of playbook {PlaybookId} completed in {ElapsedMs}ms",
                playbookId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ADR-015: Log only playbookId, no content. Leave stale entry in place.
            _logger.LogWarning(ex,
                "Failed to index playbook {PlaybookId} — stale entry retained in index",
                playbookId);
        }
    }

    /// <summary>
    /// Extracts tags from a playbook response. Tags may come from various sources;
    /// this method normalizes them into a string list for the embedding document.
    /// </summary>
    private static IList<string> ParseTags(PlaybookResponse playbook)
    {
        // Tags could be derived from capabilities, record type, or entity type
        var tags = new List<string>();

        if (!string.IsNullOrWhiteSpace(playbook.RecordType))
            tags.Add(playbook.RecordType);

        if (!string.IsNullOrWhiteSpace(playbook.EntityType))
            tags.Add(playbook.EntityType);

        if (playbook.Capabilities is { Length: > 0 })
            tags.AddRange(playbook.Capabilities);

        return tags;
    }
}
