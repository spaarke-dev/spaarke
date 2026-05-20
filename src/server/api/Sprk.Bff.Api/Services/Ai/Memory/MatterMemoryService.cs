using System.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Cosmos DB-backed implementation of <see cref="IMatterMemoryService"/>.
///
/// Storage: Cosmos DB container <c>memory</c>, partition key <c>/tenantId</c> (ADR-015 Tier 3).
/// Retention: 90 days (configured at container provisioning time — see infrastructure/cosmos/).
/// GDPR: <see cref="ClearMemoryAsync"/> deletes the document on explicit user/admin request (Art. 17).
///
/// Optimistic concurrency:
/// Every <see cref="SaveFactAsync"/> call performs a read-modify-write cycle.
/// The ETag from the read is passed to the upsert via <c>IfMatchEtag</c> so that a concurrent
/// writer between the read and the write triggers a 412 PreconditionFailed from Cosmos DB.
/// Callers should catch <see cref="CosmosException"/> with StatusCode 412 and retry.
///
/// Token budget for prompt injection:
/// We estimate ~1.3 characters per token (conservative). The truncation loop drops the
/// lowest-confidence facts first until the rendered fragment fits within 500 tokens (~650 chars).
/// This is an approximation; actual tokenisation varies by model.
///
/// Lifetime: Scoped — one instance per HTTP request. <see cref="CosmosClient"/> is singleton.
/// </summary>
public sealed class MatterMemoryService : IMatterMemoryService
{
    /// <summary>Cosmos DB container name (ADR-015 Tier 3 container mapping).</summary>
    private const string ContainerName = "memory";

    /// <summary>
    /// Conservative characters-per-token estimate used for the 500-token budget enforcement.
    /// GPT-4o averages ~4 chars/token for English prose; 1.3 chars/token is deliberately
    /// conservative to ensure we never exceed the target even with dense structured content.
    /// </summary>
    private const double CharsPerToken = 1.3;

    /// <summary>Target maximum token count for the system prompt fragment.</summary>
    private const int MaxTokens = 500;

    /// <summary>
    /// Minimum confidence threshold for unconfirmed facts to be included in the prompt fragment.
    /// Facts with ConfirmedByUser == false AND Confidence below this value are excluded.
    /// </summary>
    private const double MinUnconfirmedConfidence = 0.7;

    private readonly Container _container;
    private readonly ILogger<MatterMemoryService> _logger;

    /// <summary>
    /// Initialises the <see cref="MatterMemoryService"/>.
    /// </summary>
    /// <param name="cosmosClient">Singleton Cosmos DB client (DefaultAzureCredential, no connection strings).</param>
    /// <param name="databaseName">Cosmos DB database name from <c>CosmosPersistence:DatabaseName</c> config.</param>
    /// <param name="logger">Logger for diagnostic and warning messages.</param>
    public MatterMemoryService(
        CosmosClient cosmosClient,
        string databaseName,
        ILogger<MatterMemoryService> logger)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(logger);

        _container = cosmosClient.GetContainer(databaseName, ContainerName);
        _logger = logger;
    }

    // =========================================================================
    // IMatterMemoryService
    // =========================================================================

    /// <inheritdoc/>
    public async Task<MatterMemory?> GetMemoryAsync(
        string tenantId,
        string matterId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(matterId);

        var id = BuildDocumentId(tenantId, matterId);

        try
        {
            var response = await _container.ReadItemAsync<MatterMemory>(
                id: id,
                partitionKey: new PartitionKey(tenantId),
                cancellationToken: ct);

            // Capture the ETag from the response header so SaveFactAsync can use it for
            // optimistic concurrency on the next write.
            var memory = response.Resource;
            memory.ETag = response.ETag;

            _logger.LogDebug(
                "MatterMemoryService: Loaded memory for matter {MatterId} (tenant={TenantId}, facts={FactCount}, version={Version})",
                matterId, tenantId, memory.Facts.Count, memory.Version);

            return memory;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Not found means no prior memory for this matter — this is expected on first visit.
            _logger.LogDebug(
                "MatterMemoryService: No memory found for matter {MatterId} (tenant={TenantId}) — new matter",
                matterId, tenantId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveFactAsync(
        string tenantId,
        string matterId,
        MemoryFact fact,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(matterId);
        ArgumentNullException.ThrowIfNull(fact);

        // Read-modify-write with ETag-based optimistic concurrency.
        // If the document has been modified between our read and this write, Cosmos returns 412.
        var existing = await GetMemoryAsync(tenantId, matterId, ct);

        MatterMemory updated;
        ItemRequestOptions requestOptions;

        if (existing is null)
        {
            // First fact for this matter — create a new document.
            updated = new MatterMemory
            {
                Id = BuildDocumentId(tenantId, matterId),
                TenantId = tenantId,
                MatterId = matterId,
                Facts = [fact],
                LastUpdated = DateTimeOffset.UtcNow,
                Version = 1,
            };

            // No ETag — create (no IfMatchEtag needed for new documents).
            requestOptions = new ItemRequestOptions();
        }
        else
        {
            // Append to existing document, preserving order by RecordedAt.
            var updatedFacts = new List<MemoryFact>(existing.Facts) { fact };

            updated = new MatterMemory
            {
                Id = existing.Id,
                TenantId = existing.TenantId,
                MatterId = existing.MatterId,
                Facts = updatedFacts,
                LastUpdated = DateTimeOffset.UtcNow,
                Version = existing.Version + 1,
            };

            // Pass the ETag so Cosmos rejects the write if a concurrent session modified the document.
            // A 412 PreconditionFailed CosmosException will propagate to the caller for retry logic.
            requestOptions = new ItemRequestOptions
            {
                IfMatchEtag = existing.ETag
            };
        }

        await _container.UpsertItemAsync(
            item: updated,
            partitionKey: new PartitionKey(tenantId),
            requestOptions: requestOptions,
            cancellationToken: ct);

        _logger.LogDebug(
            "MatterMemoryService: Saved fact (type={FactType}, key={FactKey}) for matter {MatterId} " +
            "(tenant={TenantId}, version={Version})",
            fact.Type, fact.Key, matterId, tenantId, updated.Version);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This method performs a hard delete of the Cosmos DB document.
    ///
    /// DESIGN NOTE — GDPR vs. Compliance Audit Distinction:
    /// ClearMemoryAsync is an intentional user- or admin-initiated GDPR erasure action (Art. 17).
    /// It affects ONLY the Tier 3 "memory" container (user-owned work history). The Tier 2 "audit"
    /// container (append-only compliance log, 7-year retention) is NEVER touched by this method.
    /// The presence of an audit record for a matter does NOT prevent or contradict memory erasure —
    /// these are separate data governance tiers with independent retention and deletion policies
    /// (ADR-015 Tier 2 vs. Tier 3).
    /// </remarks>
    public async Task ClearMemoryAsync(
        string tenantId,
        string matterId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(matterId);

        var id = BuildDocumentId(tenantId, matterId);

        try
        {
            await _container.DeleteItemAsync<MatterMemory>(
                id: id,
                partitionKey: new PartitionKey(tenantId),
                cancellationToken: ct);

            _logger.LogInformation(
                "MatterMemoryService: Memory cleared for matter {MatterId} (tenant={TenantId}) — GDPR Art. 17 erasure",
                matterId, tenantId);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Idempotent: document already gone — treat as success.
            _logger.LogDebug(
                "MatterMemoryService: ClearMemoryAsync called for matter {MatterId} (tenant={TenantId}) but no document found — already clear",
                matterId, tenantId);
        }
    }

    /// <inheritdoc/>
    public async Task<string> ToSystemPromptFragmentAsync(
        string tenantId,
        string matterId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(matterId);

        var memory = await GetMemoryAsync(tenantId, matterId, ct);
        if (memory is null || memory.Facts.Count == 0)
        {
            return string.Empty;
        }

        return BuildPromptFragment(memory.Facts);
    }

    // =========================================================================
    // Internal helpers (internal for unit test access)
    // =========================================================================

    /// <summary>
    /// Builds the Cosmos DB document id for a matter memory document.
    /// Format: <c>{tenantId}_{matterId}</c> — one document per (tenant, matter) pair.
    /// </summary>
    internal static string BuildDocumentId(string tenantId, string matterId)
        => $"{tenantId}_{matterId}";

    /// <summary>
    /// Renders a list of facts into a structured system prompt fragment.
    ///
    /// Algorithm:
    /// 1. Filter: exclude unconfirmed AI-extracted facts below the confidence threshold.
    /// 2. Sort candidate facts by confidence descending within each type (highest first).
    /// 3. Render all candidate facts grouped by type.
    /// 4. If the rendered fragment exceeds MaxTokens (estimated), drop the lowest-confidence
    ///    fact and re-render. Repeat until within budget or only high-confidence facts remain.
    ///
    /// The output targets 200–500 tokens for a typical matter with 3 parties, 5 dates,
    /// 2 analyses, and 5 facts.
    /// </summary>
    internal static string BuildPromptFragment(IReadOnlyList<MemoryFact> allFacts)
    {
        // Step 1: Filter to eligible facts only.
        var eligible = allFacts
            .Where(f => f.ConfirmedByUser || f.Confidence >= MinUnconfirmedConfidence)
            .ToList();

        if (eligible.Count == 0)
        {
            return string.Empty;
        }

        // Step 2 & 3: Render with truncation loop.
        // Sort by confidence descending so truncation removes lowest-confidence facts first.
        var candidates = eligible
            .OrderByDescending(f => f.Confidence)
            .ToList();

        string fragment;
        while (true)
        {
            fragment = RenderFragment(candidates);

            // Estimate token count: characters / CharsPerToken (conservative).
            var estimatedTokens = fragment.Length / CharsPerToken;
            if (estimatedTokens <= MaxTokens || candidates.Count <= 1)
            {
                break;
            }

            // Drop the last element (lowest confidence, due to descending sort above).
            candidates.RemoveAt(candidates.Count - 1);
        }

        return fragment;
    }

    /// <summary>
    /// Renders a set of candidate facts into a structured text block grouped by <see cref="MemoryFactType"/>.
    /// </summary>
    private static string RenderFragment(IReadOnlyList<MemoryFact> facts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Matter Context (from prior sessions)");

        AppendSection(sb, "**Parties**", facts, MemoryFactType.Party);
        AppendSection(sb, "**Key Dates**", facts, MemoryFactType.KeyDate);
        AppendSection(sb, "**Prior Analyses**", facts, MemoryFactType.PriorAnalysis);
        AppendSection(sb, "**Key Facts**", facts, MemoryFactType.KeyFact);

        // Trim trailing newline for clean injection into the system prompt.
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends a section line to <paramref name="sb"/> if any facts of <paramref name="type"/> exist.
    /// Multiple facts within a section are joined with "; " to stay on a single line.
    /// </summary>
    private static void AppendSection(
        StringBuilder sb,
        string heading,
        IReadOnlyList<MemoryFact> facts,
        MemoryFactType type)
    {
        var sectionFacts = facts.Where(f => f.Type == type).ToList();
        if (sectionFacts.Count == 0)
        {
            return;
        }

        // Each fact is rendered as "Key — Value"; multiple facts joined by "; ".
        var entries = sectionFacts.Select(f => $"{f.Key} — {f.Value}");
        sb.AppendLine($"{heading}: {string.Join("; ", entries)}");
    }
}
