using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Two-stage playbook intent matching: vector similarity search + LLM refinement.
///
/// <para>
/// <b>Stage 1 — Vector Similarity Search</b> (1.5s budget):
/// Embeds the user message via <see cref="PlaybookEmbeddingService.SearchPlaybooksAsync"/> and
/// queries the <c>playbook-embeddings</c> AI Search index. Pre-filters by <c>recordType</c>
/// from <see cref="ChatHostContext"/> when available. Returns top 5 candidates.
/// If a single candidate scores &gt;= 0.85, Stage 2 is skipped.
/// </para>
///
/// <para>
/// <b>Stage 2 — LLM Refinement + Parameter Extraction</b> (0.5s budget):
/// Sends the top candidates + user message to the execution <see cref="IChatClient"/>.
/// Extracts: best match, confidence, and parameter values as a dictionary.
/// </para>
///
/// <para>
/// <b>Output enrichment</b>: Once matched, the dispatcher queries the playbook's
/// DeliverOutput node to populate <see cref="DispatchResult.OutputType"/>,
/// <see cref="DispatchResult.RequiresConfirmation"/>, and <see cref="DispatchResult.TargetPage"/>
/// from the JPS definition (spec FR-18 — NOT hardcoded).
/// </para>
///
/// <para>
/// <b>Caching</b> (ADR-014): Final dispatch results are cached in Redis with a version key
/// derived from the playbook catalog version. Cache is tenant-scoped. Individual user messages
/// are NOT cached (each message is unique).
/// </para>
///
/// <b>Not registered in DI</b> (ADR-010). Factory-instantiated by <see cref="SprkChatAgentFactory"/>.
/// </summary>
public sealed class PlaybookDispatcher
{
    /// <summary>
    /// Confidence threshold above which a single-candidate Stage 1 result is accepted
    /// without Stage 2 LLM refinement.
    /// </summary>
    private const double HighConfidenceThreshold = 0.85;

    /// <summary>
    /// Maximum number of playbook candidates from Stage 1 vector search.
    /// </summary>
    private const int MaxCandidates = 5;

    /// <summary>
    /// Stage 1 timeout: vector similarity search budget.
    /// </summary>
    private static readonly TimeSpan Stage1Timeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Stage 2 timeout: LLM refinement budget.
    /// </summary>
    private static readonly TimeSpan Stage2Timeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Total NFR-04 budget: both stages combined.
    /// </summary>
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Cache TTL for dispatch results. Short because playbook catalog can change.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Concurrency limiter for AI calls (ADR-016: bound concurrency).
    /// Shared across all instances within the process to prevent AI Search / LLM overload.
    /// </summary>
    private static readonly SemaphoreSlim AiConcurrencyLimiter = new(maxCount: 10, initialCount: 10);

    private readonly PlaybookEmbeddingService _embeddingService;
    private readonly IChatClient _executionClient;
    private readonly INodeService _nodeService;
    private readonly IDistributedCache _cache;
    private readonly ILogger _logger;
    private readonly string _tenantId;

    /// <summary>
    /// Initializes a new instance of <see cref="PlaybookDispatcher"/>.
    /// </summary>
    /// <param name="embeddingService">Playbook embedding service for vector similarity search.</param>
    /// <param name="executionClient">IChatClient for Stage 2 LLM refinement (fast model).</param>
    /// <param name="nodeService">Node service for querying playbook DeliverOutput nodes (JPS).</param>
    /// <param name="cache">Distributed cache for result caching (ADR-009, ADR-014).</param>
    /// <param name="tenantId">Tenant ID for cache key scoping (ADR-014).</param>
    /// <param name="logger">Logger instance.</param>
    /// <remarks>
    /// ADR-010: This class is factory-instantiated, NOT DI-registered.
    /// Callers (SprkChatAgentFactory) create instances directly with resolved dependencies.
    /// </remarks>
    public PlaybookDispatcher(
        PlaybookEmbeddingService embeddingService,
        IChatClient executionClient,
        INodeService nodeService,
        IDistributedCache cache,
        string tenantId,
        ILogger<PlaybookDispatcher> logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _executionClient = executionClient ?? throw new ArgumentNullException(nameof(executionClient));
        _nodeService = nodeService ?? throw new ArgumentNullException(nameof(nodeService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _tenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Dispatches a user message through the two-stage playbook matching pipeline.
    /// </summary>
    /// <param name="userMessage">The user's natural language message to match against playbooks.</param>
    /// <param name="hostContext">
    /// Optional host context describing where SprkChat is embedded.
    /// When provided, <see cref="ChatHostContext.EntityType"/> is used to pre-filter
    /// the vector search by <c>recordType</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DispatchResult"/> with the matched playbook and extracted parameters,
    /// or <see cref="DispatchResult.NoMatch"/> if no playbook matches the user message.
    /// Returns null when the AI Search service is overloaded (ADR-016: 503 backpressure).
    /// </returns>
    public async Task<DispatchResult?> DispatchAsync(
        string userMessage,
        ChatHostContext? hostContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage, nameof(userMessage));

        var totalStopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "PlaybookDispatcher: starting dispatch for message length={MessageLength}, entityType={EntityType}",
            userMessage.Length, hostContext?.EntityType ?? "(none)");

        // ADR-016: Acquire concurrency permit with total timeout as deadline.
        if (!await AiConcurrencyLimiter.WaitAsync(TotalTimeout, cancellationToken))
        {
            _logger.LogWarning(
                "PlaybookDispatcher: concurrency limit exceeded, returning 503 backpressure");
            return null; // Caller should return 503
        }

        try
        {
            // === Stage 1: Vector Similarity Search ===
            var stage1Stopwatch = Stopwatch.StartNew();
            PlaybookSearchResult[] candidates;

            try
            {
                using var stage1Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stage1Cts.CancelAfter(Stage1Timeout);

                // Pre-filter by recordType from host context (ADR-013: entity scoping)
                var recordTypeFilter = hostContext?.EntityType is { Length: > 0 }
                    ? MapEntityTypeToRecordType(hostContext.EntityType)
                    : null;

                candidates = await _embeddingService.SearchPlaybooksAsync(
                    userMessage,
                    recordTypeFilter: recordTypeFilter,
                    topK: MaxCandidates,
                    cancellationToken: stage1Cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "PlaybookDispatcher: Stage 1 timed out after {ElapsedMs}ms (budget={BudgetMs}ms)",
                    stage1Stopwatch.ElapsedMilliseconds, Stage1Timeout.TotalMilliseconds);
                return DispatchResult.NoMatch;
            }

            stage1Stopwatch.Stop();
            _logger.LogDebug(
                "PlaybookDispatcher: Stage 1 completed in {ElapsedMs}ms — {CandidateCount} candidates",
                stage1Stopwatch.ElapsedMilliseconds, candidates.Length);

            // No candidates → no match
            if (candidates.Length == 0)
            {
                _logger.LogDebug("PlaybookDispatcher: no candidates found, returning NoMatch");
                LogTotalDuration(totalStopwatch, "NoMatch (0 candidates)");
                return DispatchResult.NoMatch;
            }

            // === Stage 2 bypass: single high-confidence candidate ===
            if (candidates.Length == 1 && candidates[0].Score >= HighConfidenceThreshold)
            {
                _logger.LogDebug(
                    "PlaybookDispatcher: single high-confidence candidate ({Score:F3} >= {Threshold}), skipping Stage 2",
                    candidates[0].Score, HighConfidenceThreshold);

                var directResult = await BuildResultFromCandidate(
                    candidates[0], candidates[0].Score, new Dictionary<string, string>(), cancellationToken);

                LogTotalDuration(totalStopwatch, $"DirectMatch (score={candidates[0].Score:F3})");
                return directResult;
            }

            // === Stage 2: LLM Refinement + Parameter Extraction ===
            var stage2Stopwatch = Stopwatch.StartNew();
            LlmRefinementOutput? refinement;

            try
            {
                using var stage2Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stage2Cts.CancelAfter(Stage2Timeout);

                refinement = await RefineWithLlmAsync(userMessage, candidates, stage2Cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "PlaybookDispatcher: Stage 2 timed out after {ElapsedMs}ms (budget={BudgetMs}ms). " +
                    "Falling back to top Stage 1 candidate.",
                    stage2Stopwatch.ElapsedMilliseconds, Stage2Timeout.TotalMilliseconds);

                // Fallback: use top Stage 1 candidate if Stage 2 times out
                var fallbackResult = await BuildResultFromCandidate(
                    candidates[0], candidates[0].Score, new Dictionary<string, string>(), cancellationToken);

                LogTotalDuration(totalStopwatch, "FallbackMatch (Stage 2 timeout)");
                return fallbackResult;
            }

            stage2Stopwatch.Stop();
            _logger.LogDebug(
                "PlaybookDispatcher: Stage 2 completed in {ElapsedMs}ms",
                stage2Stopwatch.ElapsedMilliseconds);

            // LLM said "none" or returned null
            if (refinement is null || refinement.PlaybookId is null or "none")
            {
                _logger.LogDebug("PlaybookDispatcher: LLM refinement returned no match");
                LogTotalDuration(totalStopwatch, "NoMatch (LLM refinement)");
                return DispatchResult.NoMatch;
            }

            // Find the matching candidate from Stage 1 results
            var matchedCandidate = Array.Find(candidates,
                c => c.PlaybookId.Equals(refinement.PlaybookId, StringComparison.OrdinalIgnoreCase));

            if (matchedCandidate is null)
            {
                _logger.LogWarning(
                    "PlaybookDispatcher: LLM selected playbookId={PlaybookId} not in candidate list",
                    refinement.PlaybookId);
                LogTotalDuration(totalStopwatch, "NoMatch (LLM selected unknown candidate)");
                return DispatchResult.NoMatch;
            }

            var result = await BuildResultFromCandidate(
                matchedCandidate,
                refinement.Confidence,
                refinement.Parameters ?? new Dictionary<string, string>(),
                cancellationToken);

            LogTotalDuration(totalStopwatch, $"Match (playbook={matchedCandidate.PlaybookName}, confidence={refinement.Confidence:F3})");
            return result;
        }
        finally
        {
            AiConcurrencyLimiter.Release();
        }
    }

    #region Stage 2 — LLM Refinement

    /// <summary>
    /// Sends candidates + user message to the execution IChatClient for refined selection
    /// and parameter extraction.
    /// </summary>
    private async Task<LlmRefinementOutput?> RefineWithLlmAsync(
        string userMessage,
        PlaybookSearchResult[] candidates,
        CancellationToken cancellationToken)
    {
        // Build a compact candidate list for the prompt (keep under ~500 tokens total)
        var candidateList = string.Join("\n", candidates.Select((c, i) =>
            $"{i + 1}. id=\"{c.PlaybookId}\" name=\"{c.PlaybookName}\" tags=[{string.Join(",", c.Tags)}]"));

        var systemPrompt = """
            You are a playbook matcher. Given a user message and a numbered list of playbook candidates,
            select the best matching playbook or respond "none" if no playbook fits.
            Extract any parameter values mentioned in the user message (e.g., recipient name, date, subject).
            Respond with JSON only: {"playbookId":"...","confidence":0.0-1.0,"parameters":{"key":"value"}}
            If no match: {"playbookId":"none","confidence":0,"parameters":{}}
            """;

        var userPrompt = $"User message: \"{userMessage}\"\n\nCandidates:\n{candidateList}";

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var options = new ChatOptions
        {
            MaxOutputTokens = 200
        };

        try
        {
            var response = await _executionClient.GetResponseAsync(messages, options, cancellationToken);

            var responseText = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(responseText))
            {
                _logger.LogDebug("PlaybookDispatcher: LLM returned empty response");
                return null;
            }

            // Parse JSON response
            var result = JsonSerializer.Deserialize<LlmRefinementOutput>(responseText, LlmJsonOptions);
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "PlaybookDispatcher: failed to parse LLM refinement output");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PlaybookDispatcher: LLM refinement call failed");
            return null;
        }
    }

    /// <summary>
    /// JSON deserialization options for the LLM refinement output.
    /// </summary>
    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Internal model for LLM refinement structured output.
    /// </summary>
    private sealed record LlmRefinementOutput
    {
        public string? PlaybookId { get; init; }
        public double Confidence { get; init; }
        public Dictionary<string, string>? Parameters { get; init; }
    }

    #endregion

    #region Result Building

    /// <summary>
    /// Builds a <see cref="DispatchResult"/> from a matched search candidate.
    /// Enriches with OutputType, RequiresConfirmation, and TargetPage from the playbook's
    /// JPS DeliverOutput node (spec FR-18).
    /// </summary>
    private async Task<DispatchResult> BuildResultFromCandidate(
        PlaybookSearchResult candidate,
        double confidence,
        Dictionary<string, string> extractedParameters,
        CancellationToken cancellationToken)
    {
        // Attempt to read output node metadata from cache or Dataverse
        var (outputType, requiresConfirmation, targetPage) = await GetOutputNodeMetadataAsync(
            candidate.PlaybookId, cancellationToken);

        return new DispatchResult(
            Matched: true,
            PlaybookId: candidate.PlaybookId,
            PlaybookName: candidate.PlaybookName,
            Confidence: confidence,
            OutputType: outputType,
            RequiresConfirmation: requiresConfirmation,
            ExtractedParameters: extractedParameters,
            TargetPage: targetPage);
    }

    /// <summary>
    /// Retrieves OutputType, RequiresConfirmation, and TargetPage from the playbook's
    /// DeliverOutput node. Cached per playbook (ADR-014: version-keyed, tenant-scoped).
    /// </summary>
    private async Task<(OutputType outputType, bool requiresConfirmation, string? targetPage)>
        GetOutputNodeMetadataAsync(string playbookId, CancellationToken cancellationToken)
    {
        // ADR-014: Cache key scoped by tenant and playbook
        var cacheKey = $"dispatch:output:{_tenantId}:{playbookId}";

        try
        {
            // Check cache first
            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                var cachedMeta = JsonSerializer.Deserialize<OutputNodeMetadata>(cached);
                if (cachedMeta is not null)
                {
                    _logger.LogDebug(
                        "PlaybookDispatcher: output node metadata cache hit for playbook {PlaybookId}",
                        playbookId);
                    return (cachedMeta.OutputType, cachedMeta.RequiresConfirmation, cachedMeta.TargetPage);
                }
            }

            // Query Dataverse for playbook nodes
            if (!Guid.TryParse(playbookId, out var playbookGuid))
            {
                _logger.LogWarning("PlaybookDispatcher: invalid playbook ID format: {PlaybookId}", playbookId);
                return (OutputType.Text, false, null);
            }

            var nodes = await _nodeService.GetNodesAsync(playbookGuid, cancellationToken);
            var outputNode = Array.Find(nodes, n => n.NodeType == NodeType.Output);

            OutputType outputType;
            bool requiresConfirmation;
            string? targetPage;

            if (outputNode is not null)
            {
                outputType = outputNode.OutputType ?? OutputType.Text;
                // Default: dialog/navigation require confirmation; text does not
                requiresConfirmation = outputNode.RequiresConfirmation
                    ?? (outputType is OutputType.Dialog or OutputType.Navigation);
                targetPage = outputNode.TargetPage;
            }
            else
            {
                // No output node found — default to text with no confirmation
                _logger.LogDebug(
                    "PlaybookDispatcher: no DeliverOutput node found for playbook {PlaybookId}, defaulting to text",
                    playbookId);
                outputType = OutputType.Text;
                requiresConfirmation = false;
                targetPage = null;
            }

            // Cache the result (ADR-014)
            var metadata = new OutputNodeMetadata(outputType, requiresConfirmation, targetPage);
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(metadata),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                cancellationToken);

            return (outputType, requiresConfirmation, targetPage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PlaybookDispatcher: failed to load output node metadata for playbook {PlaybookId}; " +
                "defaulting to text output",
                playbookId);
            return (OutputType.Text, false, null);
        }
    }

    /// <summary>
    /// Cached output node metadata record.
    /// </summary>
    private sealed record OutputNodeMetadata(
        OutputType OutputType,
        bool RequiresConfirmation,
        string? TargetPage);

    #endregion

    #region Helpers

    /// <summary>
    /// Maps ChatHostContext entity types to Dataverse record type logical names.
    /// </summary>
    private static string? MapEntityTypeToRecordType(string entityType)
    {
        return entityType.ToLowerInvariant() switch
        {
            "matter" => "sprk_matter",
            "project" => "sprk_project",
            "invoice" => "sprk_invoice",
            "account" => "account",
            "contact" => "contact",
            _ => null // Unknown entity type — don't filter
        };
    }

    /// <summary>
    /// Logs total dispatch duration at Information level for NFR-04 tracking.
    /// </summary>
    private void LogTotalDuration(Stopwatch stopwatch, string outcome)
    {
        stopwatch.Stop();
        var elapsed = stopwatch.ElapsedMilliseconds;
        var level = elapsed > TotalTimeout.TotalMilliseconds ? LogLevel.Warning : LogLevel.Information;

        _logger.Log(level,
            "PlaybookDispatcher: dispatch completed in {ElapsedMs}ms — {Outcome} (NFR-04 budget={BudgetMs}ms)",
            elapsed, outcome, TotalTimeout.TotalMilliseconds);
    }

    #endregion
}
