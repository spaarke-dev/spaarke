using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Memory;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Production implementation of <see cref="IPinnedContextRecallService"/>.
///
/// Ranks the user's pinned-context items by cosine similarity of their content embedding
/// against the current user-message embedding and returns the top-K most relevant pins.
/// Reuses the existing <see cref="IEmbeddingCache"/> + <see cref="IOpenAiClient"/>
/// embedding pipeline per the R6 spec FR-43 binding ("use the existing IEmbeddingCache
/// infrastructure — do NOT introduce a new embedding service").
/// </summary>
/// <remarks>
/// <para>
/// <b>R6 Pillar 7 role</b>: this service is the selective-recall primitive for task 067
/// (hierarchical memory composition). Task 067 calls <see cref="RecallAsync"/> when the
/// matter has more pinned items than fit the NFR-10 8K system-prompt budget and uses the
/// ranked subset for injection into the chat-agent system prompt.
/// </para>
/// <para>
/// <b>ADR-010</b>: registered as <c>AddScoped&lt;IPinnedContextRecallService,
/// PinnedContextRecallService&gt;()</c> inside the existing
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/>. ZERO new
/// Program.cs lines. The interface seam is justified by ADR-010's "interface required for
/// genuine substitution" carve-out — task 067 will choose between a real impl and a
/// unit-test fake; the interface is the canonical substitution point.
/// </para>
/// <para>
/// <b>ADR-013</b>: this service lives entirely inside <see cref="Memory"/>. It depends
/// on <see cref="IOpenAiClient"/> and <see cref="IEmbeddingCache"/> directly because it
/// is itself AI-internal — no PublicContracts facade is needed for AI-internal
/// collaborators per the refined 2026-05-20 ADR-013 boundary rule.
/// </para>
/// <para>
/// <b>ADR-014</b>: <c>tenantId</c> scopes every <see cref="IPinnedContextRepository"/>
/// call (partition key). Embedding cache keys are content-hashed and tenant-agnostic by
/// design (same content → same vector) and contain no PII.
/// </para>
/// <para>
/// <b>ADR-015</b>: pin content is user-authored. This service does NOT log pin content
/// bodies — only counts and identifiers appear in telemetry.
/// </para>
/// <para>
/// <b>Soft-failure behaviour</b>: returns an empty list (NOT a thrown exception) when the
/// kill switch is off, no pins exist, or the user-message embedding cannot be computed.
/// Per-pin embedding failures are logged and the pin is dropped from the ranking — they
/// do NOT fail the whole call. This is the canonical P2 Quiet kill-switch posture for
/// memory infrastructure (mirrors <see cref="SummarizationCompressionService"/>).
/// </para>
/// </remarks>
public sealed class PinnedContextRecallService : IPinnedContextRecallService
{
    /// <summary>Absolute floor and ceiling for the per-call <c>topK</c> argument.</summary>
    internal const int MinTopK = 1;
    internal const int MaxTopK = 20;

    private readonly IPinnedContextRepository _pinnedContextRepository;
    private readonly IEmbeddingCache _embeddingCache;
    private readonly IOpenAiClient _openAiClient;
    private readonly PinnedContextRecallOptions _options;
    private readonly ILogger<PinnedContextRecallService> _logger;

    public PinnedContextRecallService(
        IPinnedContextRepository pinnedContextRepository,
        IEmbeddingCache embeddingCache,
        IOpenAiClient openAiClient,
        IOptions<PinnedContextRecallOptions> options,
        ILogger<PinnedContextRecallService> logger)
    {
        ArgumentNullException.ThrowIfNull(pinnedContextRepository);
        ArgumentNullException.ThrowIfNull(embeddingCache);
        ArgumentNullException.ThrowIfNull(openAiClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _pinnedContextRepository = pinnedContextRepository;
        _embeddingCache = embeddingCache;
        _openAiClient = openAiClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PinnedContextItem>> RecallAsync(
        string tenantId,
        string matterId,
        string userMessage,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(matterId);

        // 1. Kill switch (P2 Quiet — no exception; caller short-circuits to unranked or skips).
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "PinnedContextRecallService: kill switch off (PinnedContextRecall:Enabled=false); returning empty");
            return Array.Empty<PinnedContextItem>();
        }

        // 2. Empty / whitespace-only message — nothing meaningful to embed against.
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            _logger.LogDebug(
                "PinnedContextRecallService: userMessage empty/whitespace; returning empty");
            return Array.Empty<PinnedContextItem>();
        }

        // 3. Clamp the caller-supplied topK to the hardened bounds. The interface contract
        // documents [1, 20]; we clamp here defensively rather than throwing because this
        // service is on the chat hot path and a misconfigured caller must not surface as
        // a 500 to the end user.
        var clampedTopK = Math.Clamp(topK, MinTopK, MaxTopK);
        if (clampedTopK != topK)
        {
            _logger.LogWarning(
                "PinnedContextRecallService: caller-supplied topK={Requested} outside [{Min}, {Max}]; clamped to {Clamped}",
                topK, MinTopK, MaxTopK, clampedTopK);
        }

        // 4. Fetch candidate pins for the matter (tenant-scoped via partition key).
        var pins = await _pinnedContextRepository.GetByMatterAsync(tenantId, matterId, cancellationToken);
        if (pins is null || pins.Count == 0)
        {
            _logger.LogDebug(
                "PinnedContextRecallService: no pins for tenant={TenantId} matter={MatterId}; returning empty",
                tenantId, matterId);
            return Array.Empty<PinnedContextItem>();
        }

        // Defensive cap on the per-call embedding cost. A pathological matter with
        // thousands of pins would otherwise trigger thousands of cache-miss embedding
        // calls. The MaxPinsToRank cap takes the FIRST N pins from the repository (which
        // returns insertion order); task 067 may choose a smarter pre-filter in a
        // follow-up if needed.
        IReadOnlyList<PinnedContextItem> candidatePins = pins.Count > _options.MaxPinsToRank
            ? pins.Take(_options.MaxPinsToRank).ToList()
            : pins;

        if (candidatePins.Count < pins.Count)
        {
            _logger.LogWarning(
                "PinnedContextRecallService: pin count {Total} exceeds MaxPinsToRank {Cap}; ranking first {Cap2} only (tenant={TenantId} matter={MatterId})",
                pins.Count, _options.MaxPinsToRank, _options.MaxPinsToRank, tenantId, matterId);
        }

        // 5. Compute the user-message embedding (cache-first per the existing pattern in
        // RagService). Soft-fails on circuit-broken / generic exception by returning empty.
        ReadOnlyMemory<float> queryEmbedding;
        try
        {
            queryEmbedding = await GetOrComputeEmbeddingAsync(userMessage, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            _logger.LogWarning(ex,
                "PinnedContextRecallService: OpenAI circuit broken while computing user-message embedding; returning empty");
            return Array.Empty<PinnedContextItem>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PinnedContextRecallService: failed to compute user-message embedding; returning empty (tenant={TenantId} matter={MatterId})",
                tenantId, matterId);
            return Array.Empty<PinnedContextItem>();
        }

        if (queryEmbedding.Length == 0)
        {
            _logger.LogWarning(
                "PinnedContextRecallService: user-message embedding empty; returning empty (tenant={TenantId} matter={MatterId})",
                tenantId, matterId);
            return Array.Empty<PinnedContextItem>();
        }

        // 6. Rank each candidate pin by cosine similarity. Per-pin embedding failures are
        // logged and the pin is omitted — they do NOT fail the whole call.
        var ranked = new List<(PinnedContextItem Pin, double Similarity)>(candidatePins.Count);
        var dropped = 0;
        foreach (var pin in candidatePins)
        {
            if (string.IsNullOrWhiteSpace(pin.Content))
            {
                dropped++;
                continue;
            }

            ReadOnlyMemory<float> pinEmbedding;
            try
            {
                pinEmbedding = await GetOrComputeEmbeddingAsync(pin.Content, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OpenAiCircuitBrokenException)
            {
                // Circuit broken mid-loop — no further embedding calls will succeed. Stop early.
                _logger.LogWarning(
                    "PinnedContextRecallService: OpenAI circuit broken mid-loop after embedding {Done}/{Total} pins; ranking partial set (tenant={TenantId} matter={MatterId})",
                    ranked.Count, candidatePins.Count, tenantId, matterId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "PinnedContextRecallService: embedding failed for pin {PinId}; dropping from ranking",
                    pin.Id);
                dropped++;
                continue;
            }

            if (pinEmbedding.Length == 0 || pinEmbedding.Length != queryEmbedding.Length)
            {
                // Dimension mismatch (e.g., model upgrade between writes) — skip.
                dropped++;
                continue;
            }

            var similarity = CosineSimilarity(queryEmbedding.Span, pinEmbedding.Span);
            if (similarity < _options.SimilarityThreshold)
            {
                continue;
            }
            ranked.Add((pin, similarity));
        }

        if (dropped > 0)
        {
            _logger.LogDebug(
                "PinnedContextRecallService: dropped {Dropped} pin(s) from ranking (empty content / embedding failure / dim mismatch) for tenant={TenantId} matter={MatterId}",
                dropped, tenantId, matterId);
        }

        if (ranked.Count == 0)
        {
            return Array.Empty<PinnedContextItem>();
        }

        // 7. Sort by similarity descending and take topK.
        ranked.Sort(static (a, b) => b.Similarity.CompareTo(a.Similarity));
        var topPins = ranked.Take(clampedTopK).Select(t => t.Pin).ToList();

        _logger.LogDebug(
            "PinnedContextRecallService: returning {Returned}/{Candidates} pins above threshold (tenant={TenantId} matter={MatterId})",
            topPins.Count, candidatePins.Count, tenantId, matterId);

        return topPins;
    }

    // =========================================================================
    // Internal helpers (internal for unit test access)
    // =========================================================================

    /// <summary>
    /// Cache-first embedding lookup using the existing <see cref="IEmbeddingCache"/>
    /// infrastructure. Mirrors the canonical pattern used by <see cref="RagService"/>:
    /// hit cache → return; miss → call <see cref="IOpenAiClient.GenerateEmbeddingAsync"/>
    /// → store in cache → return. The embedding model deployment can be overridden via
    /// <see cref="PinnedContextRecallOptions.EmbeddingModelOverride"/>.
    /// </summary>
    internal async Task<ReadOnlyMemory<float>> GetOrComputeEmbeddingAsync(
        string content,
        CancellationToken cancellationToken)
    {
        var cached = await _embeddingCache.GetEmbeddingForContentAsync(content, cancellationToken);
        if (cached.HasValue)
        {
            return cached.Value;
        }

        var embedding = await _openAiClient.GenerateEmbeddingAsync(
            text: content,
            model: _options.EmbeddingModelOverride,
            dimensions: null,
            cancellationToken: cancellationToken);

        await _embeddingCache.SetEmbeddingForContentAsync(content, embedding, cancellationToken);
        return embedding;
    }

    /// <summary>
    /// Computes cosine similarity between two equal-length embedding vectors. Returns 0
    /// when either vector is zero-magnitude (degenerate case).
    /// </summary>
    /// <remarks>
    /// Uses <see cref="ReadOnlySpan{T}"/> for zero-allocation dot-product over the float
    /// vectors. Caller MUST ensure equal lengths (enforced upstream in <see cref="RecallAsync"/>).
    /// </remarks>
    internal static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0.0;
        }

        double dot = 0.0;
        double magA = 0.0;
        double magB = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            double av = a[i];
            double bv = b[i];
            dot += av * bv;
            magA += av * av;
            magB += bv * bv;
        }

        if (magA <= 0.0 || magB <= 0.0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
