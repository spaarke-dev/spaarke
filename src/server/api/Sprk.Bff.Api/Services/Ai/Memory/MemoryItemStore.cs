using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Ai.Audit;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Cosmos DB-backed implementation of <see cref="IMemoryItemStore"/> (task 050, FR-B-01) —
/// the generalization of the retired matter-only <c>MatterMemoryService</c>.
///
/// Storage: container <c>memory-items</c>, partition key <c>/subjectId</c> (SUBJECT-partitioned:
/// entityId for Record scope, userId for User scope — NEVER <c>/tenantId</c>). One document per
/// fact, aligned to MemoryItem v1 (<see cref="MemoryItemDocument"/>). The legacy <c>memory</c>
/// container is left untouched (fresh-container ruling 2026-07-09) — it is SHARED with
/// pinned-context and workspace-tab documents and is not retired.
///
/// Reused logic (spec FR-B-01 "reuse the code, not the container"): the 500-token prompt-fragment
/// budget serialization, confidence filtering, and truncation policy are ported VERBATIM from the
/// legacy service (constants and algorithm unchanged); ETag-based optimistic concurrency and the
/// GDPR hard-delete posture (Tier 3 only, audit container never touched) are preserved on the
/// per-fact document granularity.
///
/// Upsert-by-key supersession (operator ruling 2026-07-09 (d)): the document id is deterministic
/// over <c>(scope, fact.Type, fact.Key)</c>, so a repeated capture for the same key REPLACES the
/// prior fact instead of accumulating duplicates or stale contradictions — load-bearing for the
/// silent AI-initiated write posture (FR-B-08 / task 057).
///
/// Lifetime: Scoped — one instance per HTTP request; <see cref="CosmosClient"/> is singleton.
/// </summary>
public sealed class MemoryItemStore : IMemoryItemStore
{
    /// <summary>NEW subject-partitioned Cosmos container (ADR-015 Tier 3). The legacy <c>memory</c> container is NOT reused.</summary>
    internal const string ContainerName = "memory-items";

    /// <summary>Document discriminator value (defensive future-proofing of container queries).</summary>
    internal const string DocumentTypeValue = "memory-item";

    // ── Prompt-fragment budget constants — ported VERBATIM from the legacy service ──

    /// <summary>Conservative characters-per-token estimate for the 500-token budget enforcement (legacy value preserved).</summary>
    private const double CharsPerToken = 1.3;

    /// <summary>Target maximum token count for the system prompt fragment (legacy value preserved).</summary>
    private const int MaxTokens = 500;

    /// <summary>Minimum confidence for unconfirmed facts to enter the prompt fragment (legacy value preserved).</summary>
    private const double MinUnconfirmedConfidence = 0.7;

    private readonly Container _container;
    private readonly ILogger<MemoryItemStore> _logger;
    private readonly IAuditLogService? _auditLog;

    /// <summary>
    /// Initialises the <see cref="MemoryItemStore"/>.
    /// </summary>
    /// <param name="cosmosClient">Singleton Cosmos DB client (DefaultAzureCredential, no connection strings).</param>
    /// <param name="databaseName">Cosmos DB database name from <c>CosmosPersistence:DatabaseName</c> config.</param>
    /// <param name="logger">Logger for diagnostic messages (identifiers/counts only, ADR-015 Tier 1).</param>
    /// <param name="auditLog">
    /// Optional append-only Tier-2 audit log (task 052, NFR-07). When supplied, every memory WRITE
    /// emits a compliance audit event carrying identifiers/counts ONLY — never memory content. Reuses
    /// the EXISTING <see cref="IAuditLogService"/> (operator ruling 2026-07-09 — no new audit component).
    /// Optional so the task-050 construction/tests that predate the audit hook stay green.
    /// </param>
    public MemoryItemStore(
        CosmosClient cosmosClient,
        string databaseName,
        ILogger<MemoryItemStore> logger,
        IAuditLogService? auditLog = null)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(logger);

        _container = cosmosClient.GetContainer(databaseName, ContainerName);
        _logger = logger;
        _auditLog = auditLog;
    }

    // =========================================================================
    // IMemoryItemStore
    // =========================================================================

    /// <inheritdoc/>
    public async Task<MemoryItem> UpsertAsync(MemoryItem item, string tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        MemoryItemContract.EnsureValidMemoryScope(item.Scope);
        EnsureSubjectKeying(item);

        if (string.Equals(item.Scope, MemoryScope.Record, StringComparison.Ordinal))
        {
            DataverseFieldMirrorGuard.EnsureNotDataverseFieldMirror(item.Fact);
        }

        // Subject-key normalization at the SINGLE chokepoint (consolidation fix, 2026-07-10): callers
        // supply keys with mixed casing/braces (LLM-supplied subjectType via memory.write; Dataverse
        // host contexts often send "{UPPERCASE}" GUIDs), while Cosmos partition keys + query params are
        // case-sensitive. Without this, a fact written as ("Matter","{ABC…}") is never recalled by a
        // read for ("matter","abc…"). Same discipline as BuildItemId's fact-key normalization.
        item = string.Equals(item.Scope, MemoryScope.User, StringComparison.Ordinal)
            ? item with { UserId = NormalizeSubjectId(item.UserId!) }
            : item with { SubjectType = NormalizeSubjectType(item.SubjectType!), SubjectId = NormalizeSubjectId(item.SubjectId!) };

        var subjectKey = item.PartitionKey;
        var id = BuildItemId(item.Scope, item.Fact.Type, item.Fact.Key);
        var partitionKey = new PartitionKey(subjectKey);

        // Point-read the existing doc so a repeated capture for the same (Type, Key) SUPERSEDES it:
        // preserve creation stamps, mark the update, and thread the ETag so a concurrent writer
        // between this read and the write triggers 412 PreconditionFailed (legacy contract preserved).
        MemoryItemDocument? existing = null;
        try
        {
            var readResponse = await _container.ReadItemAsync<MemoryItemDocument>(id, partitionKey, cancellationToken: ct);
            existing = readResponse.Resource;
            existing.ETag = readResponse.ETag;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // First capture for this key — create path.
        }

        // Retention (task 052): map retentionClass → per-item Cosmos ttl at WRITE time. Cosmos does the
        // expiry — no reaper, no read-filter (operator ruling 2026-07-09: minimal governance).
        var ttlSeconds = MemoryRetentionPolicy.ResolveTtlSeconds(item.RetentionClass);

        MemoryItemDocument document;
        ItemRequestOptions requestOptions;

        if (existing is not null)
        {
            // Supersession: same (scope, Type, Key) → REPLACE the fact, preserving the original
            // creation stamps and marking the update instant.
            document = MemoryItemDocument.FromItem(
                item with { CreatedAt = existing.CreatedAt, CreatedBy = existing.CreatedBy ?? item.CreatedBy, UpdatedAt = DateTimeOffset.UtcNow },
                id, subjectKey, tenantId, ttlSeconds);
            requestOptions = new ItemRequestOptions { IfMatchEtag = existing.ETag };
        }
        else
        {
            document = MemoryItemDocument.FromItem(item, id, subjectKey, tenantId, ttlSeconds);
            requestOptions = new ItemRequestOptions();
        }

        await _container.UpsertItemAsync(document, partitionKey, requestOptions, ct);

        _logger.LogDebug(
            "MemoryItemStore: Upserted fact (scope={Scope}, type={FactType}, superseded={Superseded}, ttl={Ttl}) for subject {SubjectKey}",
            item.Scope, item.Fact.Type, existing is not null, ttlSeconds, subjectKey);

        // Audit the WRITE (NFR-07): identifiers/counts ONLY — the fact Key/Value are NEVER emitted.
        // Fire-and-forget via the existing Tier-2 AuditLogService (no new audit component).
        if (_auditLog is not null)
        {
            var entry = MemoryAuditEvents.Build(
                action: existing is not null ? MemoryAuditEvents.ActionSupersede : MemoryAuditEvents.ActionWrite,
                tenantId: tenantId,
                userId: item.CreatedBy,
                subjectKey: subjectKey,
                itemIds: new[] { id });
            await _auditLog.LogInteractionAsync(entry, ct);
        }

        return document.ToItem();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MemoryItem>> GetForRecordAsync(string subjectType, string subjectId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.documentType = @docType AND c.scope = @scope AND c.subjectType = @subjectType")
            .WithParameter("@docType", DocumentTypeValue)
            .WithParameter("@scope", MemoryScope.Record)
            .WithParameter("@subjectType", NormalizeSubjectType(subjectType));

        return await QueryPartitionAsync(query, NormalizeSubjectId(subjectId), ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MemoryItem>> GetForUserAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.documentType = @docType AND c.scope = @scope")
            .WithParameter("@docType", DocumentTypeValue)
            .WithParameter("@scope", MemoryScope.User);

        return await QueryPartitionAsync(query, NormalizeSubjectId(userId), ct);
    }

    /// <inheritdoc/>
    public async Task DeleteItemAsync(string subjectKey, string itemId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        subjectKey = NormalizeSubjectId(subjectKey);

        try
        {
            await _container.DeleteItemAsync<MemoryItemDocument>(itemId, new PartitionKey(subjectKey), cancellationToken: ct);
            _logger.LogInformation(
                "MemoryItemStore: Deleted memory item {ItemId} for subject {SubjectKey}", itemId, subjectKey);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug(
                "MemoryItemStore: DeleteItemAsync for {ItemId} (subject {SubjectKey}) — already gone", itemId, subjectKey);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// DESIGN NOTE — GDPR vs. Compliance Audit Distinction (preserved from the legacy service):
    /// erasure is an intentional user/admin-initiated action affecting ONLY the Tier 3
    /// <c>memory-items</c> container. The Tier 2 <c>audit</c> container (append-only compliance
    /// log) is NEVER touched — independent governance tiers per ADR-015.
    /// </remarks>
    public async Task EraseSubjectAsync(string subjectKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);

        subjectKey = NormalizeSubjectId(subjectKey);
        var partitionKey = new PartitionKey(subjectKey);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.documentType = @docType")
            .WithParameter("@docType", DocumentTypeValue);

        var deleted = 0;
        using var iterator = _container.GetItemQueryIterator<MemoryItemDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            foreach (var row in page)
            {
                try
                {
                    await _container.DeleteItemAsync<MemoryItemDocument>(row.Id, partitionKey, cancellationToken: ct);
                    deleted++;
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Concurrent delete — idempotent.
                }
            }
        }

        _logger.LogInformation(
            "MemoryItemStore: Erased subject {SubjectKey} — {Count} memory items deleted (GDPR Art. 17)",
            subjectKey, deleted);
    }

    /// <inheritdoc/>
    public async Task<string> ToRecordPromptFragmentAsync(string subjectType, string subjectId, CancellationToken ct = default)
    {
        var items = await GetForRecordAsync(subjectType, subjectId, ct);
        if (items.Count == 0)
        {
            return string.Empty;
        }

        return BuildPromptFragment(items.Select(i => i.Fact).ToList(), subjectType);
    }

    // =========================================================================
    // Internal helpers (internal for unit test access — legacy test pattern preserved)
    // =========================================================================

    /// <summary>
    /// Normalizes a record subjectType (entity logical name): trim + lowercase. Cosmos query params
    /// are case-sensitive; writers (LLM-supplied via memory.write, host contexts) and readers
    /// (governance endpoints, chat provider) must land on one canonical form.
    /// </summary>
    internal static string NormalizeSubjectType(string subjectType)
        => subjectType.Trim().ToLowerInvariant();

    /// <summary>
    /// Normalizes a subject id (entityId or userId — the PARTITION value): trim, strip braces, and
    /// canonicalize GUIDs to lowercase "D" format (Dataverse host contexts often send
    /// <c>{UPPERCASE}</c> GUIDs). Non-GUID ids are trimmed only — casing is preserved because they
    /// may be externally meaningful.
    /// </summary>
    internal static string NormalizeSubjectId(string subjectId)
    {
        var trimmed = subjectId.Trim().Trim('{', '}');
        return Guid.TryParse(trimmed, out var guid) ? guid.ToString("D") : trimmed;
    }

    /// <summary>
    /// Builds the DETERMINISTIC document id for a fact: <c>mem_{scope}_{factTypeInt}_{keyHash16}</c>.
    /// Deterministic over <c>(scope, fact.Type, normalized fact.Key)</c> so a repeated capture for
    /// the same key maps to the SAME document → upsert supersession, not accumulation. The key is
    /// hashed (SHA-256, first 16 hex chars) because fact keys are free text and Cosmos ids forbid
    /// several characters; uniqueness is only required within the subject partition.
    /// </summary>
    internal static string BuildItemId(string scope, MemoryFactType factType, string key)
    {
        var normalizedKey = key.Trim().ToLowerInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedKey));
        var keyHash16 = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
        return $"mem_{scope}_{(int)factType}_{keyHash16}";
    }

    /// <summary>
    /// Renders facts into the structured system-prompt fragment. Algorithm ported VERBATIM from
    /// the legacy service: confidence filter (unconfirmed &lt; 0.7 excluded) → confidence-descending
    /// sort → render grouped by type → drop lowest-confidence fact until within the 500-token budget.
    /// The heading is byte-identical to the legacy fragment for <c>subjectType == "matter"</c> and
    /// generalized for other entity types.
    /// </summary>
    internal static string BuildPromptFragment(IReadOnlyList<MemoryFact> allFacts, string subjectType)
    {
        var eligible = allFacts
            .Where(f => f.ConfirmedByUser || f.Confidence >= MinUnconfirmedConfidence)
            .ToList();

        if (eligible.Count == 0)
        {
            return string.Empty;
        }

        var candidates = eligible
            .OrderByDescending(f => f.Confidence)
            .ToList();

        string fragment;
        while (true)
        {
            fragment = RenderFragment(candidates, subjectType);

            var estimatedTokens = fragment.Length / CharsPerToken;
            if (estimatedTokens <= MaxTokens || candidates.Count <= 1)
            {
                break;
            }

            candidates.RemoveAt(candidates.Count - 1);
        }

        return fragment;
    }

    /// <summary>
    /// Fragment heading: byte-identical legacy heading for matters; generalized for other record types
    /// (e.g. <c>### Project Context (from prior sessions)</c>).
    /// </summary>
    internal static string BuildFragmentHeading(string subjectType)
    {
        if (string.Equals(subjectType, "matter", StringComparison.OrdinalIgnoreCase))
        {
            return "### Matter Context (from prior sessions)";
        }

        var display = char.ToUpperInvariant(subjectType[0]) + subjectType[1..];
        return $"### {display} Context (from prior sessions)";
    }

    private static string RenderFragment(IReadOnlyList<MemoryFact> facts, string subjectType)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildFragmentHeading(subjectType));

        AppendSection(sb, "**Parties**", facts, MemoryFactType.Party);
        AppendSection(sb, "**Key Dates**", facts, MemoryFactType.KeyDate);
        AppendSection(sb, "**Prior Analyses**", facts, MemoryFactType.PriorAnalysis);
        AppendSection(sb, "**Key Facts**", facts, MemoryFactType.KeyFact);

        return sb.ToString().TrimEnd();
    }

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

        var entries = sectionFacts.Select(f => $"{f.Key} — {f.Value}");
        sb.AppendLine($"{heading}: {string.Join("; ", entries)}");
    }

    private static void EnsureSubjectKeying(MemoryItem item)
    {
        if (string.Equals(item.Scope, MemoryScope.Record, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(item.SubjectType) || string.IsNullOrWhiteSpace(item.SubjectId))
            {
                throw new ArgumentException(
                    "Record-scope memory requires generic (subjectType, subjectId) keying — matter, project, invoice, or any entity type (FR-B-01).",
                    nameof(item));
            }
        }
        else if (string.IsNullOrWhiteSpace(item.UserId))
        {
            throw new ArgumentException(
                "User-scope memory requires userId keying (canonical: Dataverse systemuserid).",
                nameof(item));
        }
    }

    private async Task<IReadOnlyList<MemoryItem>> QueryPartitionAsync(
        QueryDefinition query, string subjectKey, CancellationToken ct)
    {
        var results = new List<MemoryItem>();

        using var iterator = _container.GetItemQueryIterator<MemoryItemDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(subjectKey) });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToItem()));
        }

        _logger.LogDebug(
            "MemoryItemStore: Read {Count} memory items for subject {SubjectKey}", results.Count, subjectKey);

        return results;
    }
}

/// <summary>
/// FR-B-01 negative guard: record memory holds DERIVED/synthesized knowledge only — it is never a
/// mirror of live Dataverse fields (the Context Binder reads those directly per FR-B-04, so a
/// mirrored field would be a stale duplicate). Mechanically rejectable cases (a fact keyed by a
/// Dataverse logical column name, e.g. <c>sprk_matternumber</c>) are REJECTED; the general rule is
/// documented-as-disallowed here and on <see cref="IMemoryItemStore"/> (a semantic duplicate under
/// a human-readable key cannot be detected mechanically).
/// </summary>
public static partial class DataverseFieldMirrorGuard
{
    /// <summary>Publisher-prefixed Dataverse logical-name shapes (sprk_/new_/msdyn_/mscrm_/crXXX_) and OData field references.</summary>
    [GeneratedRegex(@"^\s*(sprk|new|msdyn|mscrm|cr[0-9a-z]{3,5})_[a-z0-9_]+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DataverseLogicalNameRegex();

    /// <summary>True when the fact key mechanically identifies a Dataverse column (logical name or OData ref).</summary>
    public static bool IsDataverseFieldReference(string key)
        => DataverseLogicalNameRegex().IsMatch(key) || key.Contains("@odata", StringComparison.OrdinalIgnoreCase);

    /// <summary>Throws when a record-scope fact merely mirrors a live Dataverse field (FR-B-01).</summary>
    public static void EnsureNotDataverseFieldMirror(MemoryFact fact)
    {
        if (IsDataverseFieldReference(fact.Key))
        {
            throw new ArgumentException(
                $"Record memory must hold derived/synthesized knowledge, not a mirror of live Dataverse fields — " +
                $"fact key '{fact.Key}' is a Dataverse column reference. The Context Binder reads live record " +
                "fields directly (FR-B-04); store the conclusion, not the field.",
                nameof(fact));
        }
    }
}
