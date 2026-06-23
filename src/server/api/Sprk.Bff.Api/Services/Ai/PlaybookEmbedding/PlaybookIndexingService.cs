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
    // sprk_indexstatus option codes (chat-routing-redesign-r1 FR-13, task 030).
    // Mirrored from PlaybookService / PlaybookIndexDriftDetectionJob to keep this
    // pipeline self-contained for indexing-state transitions.
    private const int IndexStatusIndexed = 100_000_002;
    private const int IndexStatusFailed = 100_000_004;

    /// <summary>
    /// Maximum length of the lastError string written to Dataverse <c>sprk_lastindexerror</c>.
    /// Caps the column to a sensible diagnostic preview; ADR-015 still applies (the value
    /// MUST NOT be logged).
    /// </summary>
    private const int MaxLastErrorLength = 500;

    private readonly Channel<string> _indexingChannel;
    private readonly IPlaybookService _playbookService;
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly IPlaybookEmbeddingHashCalculator _hashCalculator;
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
    /// <param name="hashCalculator">Canonical embed-input hash calculator (chat-routing-redesign-r1
    /// FR-13 — task 034 Gap 5 closure). Used to compute <c>sprk_indexhash</c> at successful
    /// index completion so the nightly drift job can compare against the same value.</param>
    /// <param name="loggerFactory">Logger factory for creating typed loggers.</param>
    public PlaybookIndexingService(
        IPlaybookService playbookService,
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        IPlaybookEmbeddingHashCalculator hashCalculator,
        ILoggerFactory loggerFactory)
    {
        _playbookService = playbookService ?? throw new ArgumentNullException(nameof(playbookService));
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
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
                Tags = ParseTags(playbook),
                JpsMatchingMetadata = playbook.JpsMatchingMetadata,
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

            // Step 4 (chat-routing-redesign-r1 FR-13, task 034 Gap 5 closure): write the
            // canonical embed-input hash + Indexed status so the nightly drift-detection
            // job has a fingerprint to compare against. Failure to persist the tracking
            // fields MUST NOT fail the indexing operation — the embedding was created
            // successfully even if the bookkeeping update failed.
            try
            {
                var hash = _hashCalculator.ComputeHash(document);
                await _playbookService.UpdateIndexStatusAsync(
                    playbookGuid,
                    statusCode: IndexStatusIndexed,
                    indexHash: hash,
                    lastError: null,
                    cancellationToken: cancellationToken);
            }
            catch (Exception bookkeepingEx) when (bookkeepingEx is not OperationCanceledException)
            {
                // ADR-015 safe: only playbook ID + exception type name. The embedding
                // succeeded; drift detection will skip this row until the next index
                // attempt populates the hash. Per pattern: LogWarning, not LogError.
                _logger.LogWarning(
                    "Failed to persist tracking fields after successful indexing of playbook {PlaybookId} ({ExceptionType}); embedding is live but drift detection will skip this row",
                    playbookId, bookkeepingEx.GetType().Name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ADR-015: Log only playbookId + exception type. Leave stale entry in place.
            _logger.LogWarning(
                "Failed to index playbook {PlaybookId} ({ExceptionType}) — stale entry retained in index",
                playbookId, ex.GetType().Name);

            // chat-routing-redesign-r1 FR-13 (task 034 Gap 5 closure — failure path):
            // record Failed status so admins can see the row needs attention. The error
            // message goes to the Dataverse column (admin-visible) but per ADR-015 MUST
            // NOT be logged here — note we deliberately log only the exception TYPE above,
            // not the message body.
            if (Guid.TryParse(playbookId, out var failGuid))
            {
                try
                {
                    var truncated = ex.Message.Length > MaxLastErrorLength
                        ? ex.Message[..MaxLastErrorLength]
                        : ex.Message;
                    await _playbookService.UpdateIndexStatusAsync(
                        failGuid,
                        statusCode: IndexStatusFailed,
                        indexHash: null,
                        lastError: truncated,
                        cancellationToken: cancellationToken);
                }
                catch (Exception bookkeepingEx) when (bookkeepingEx is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        "Failed to persist Failed status for playbook {PlaybookId} after index failure ({ExceptionType}); row will appear as previous status",
                        playbookId, bookkeepingEx.GetType().Name);
                }
            }
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
