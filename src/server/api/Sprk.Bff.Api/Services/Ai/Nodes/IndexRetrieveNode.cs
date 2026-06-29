using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Config-driven AI Search query node for the <c>spaarke-insights-index</c> per SPEC §3.4.3
/// worked-example queries (D-P12). Composes filter + optional vector search and emits the
/// retrieved <see cref="InsightArtifact"/>-shaped rows (Observations + Precedents differentiated
/// by <c>artifactType</c>) into <see cref="NodeOutput.StructuredData"/> for downstream
/// synthesis nodes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Config schema</b> (read from <see cref="Sprk.Bff.Api.Models.Ai.PlaybookNodeDto.ConfigJson"/>):
/// </para>
/// <code>
/// {
///   "indexName":    "spaarke-insights-index",            // optional — defaults to spaarke-insights-index
///   "artifactType": "observation",                        // optional — "observation" | "precedent" | null (both)
///   "predicate":    "outcomeCategory",                    // optional — narrows by claim name (SPEC §3.4.3 Query 1)
///   "filter":       "value/raw/scope/matterType eq 'IP'", // optional — additional OData filter
///   "vectorQuery":  "predict cost for IP-licensing matter", // optional — when set, vector search is enabled
///   "topK":         12,                                    // optional — defaults to 12 (predict-matter-cost cohort size)
///   "requireEvidence": true                                // optional — D-A23 / D-48 EvidenceGuard; default true
/// }
/// </code>
/// <para>
/// <b>Tenant isolation</b>: <c>tenantId</c> is always pushed into the filter from
/// <see cref="NodeExecutionContext.TenantId"/> — playbook config cannot override it.
/// </para>
/// <para>
/// <b>D-A23 / D-48 EvidenceGuard</b>: when <c>requireEvidence: true</c> (default), an empty
/// result set yields a node-level error so downstream synthesis nodes never run against zero
/// evidence. Set to <c>false</c> only for non-evidence-bearing diagnostic queries.
/// </para>
/// <para>
/// <b>Zone A</b> per SPEC §3.5 — lives under <c>Services/Ai/Nodes/</c>. Uses the Q5-audit
/// <c>MultiIndexComposer</c> helper at the synthesis-prompt layer (the helper is invoked by
/// downstream <c>AiCompletion</c> nodes, not here); IndexRetrieveNode itself emits raw rows.
/// </para>
/// </remarks>
public sealed class IndexRetrieveNode : INodeExecutor
{
    /// <summary>Default index name when config does not specify one.</summary>
    public const string DefaultIndexName = "spaarke-insights-index";

    /// <summary>Default top-K when config does not specify one (matches predict-matter-cost cohort size from SPEC).</summary>
    public const int DefaultTopK = 12;

    /// <summary>Vector field name on <c>spaarke-insights-index</c> (per schema in <c>infrastructure/ai-search/</c>).</summary>
    public const string VectorFieldName = "contentVector";

    /// <summary>Embedding dimensionality (text-embedding-3-large).</summary>
    public const int EmbeddingDimensions = 3072;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] SelectFields =
    {
        "id", "tenantId", "artifactType", "subject", "predicate",
        "valueJson", "confidence", "evidence", "asOf", "producedBy", "status",
        // Wave D6 (task 035) — hybrid scope shape per design-a6 §4. Selecting the scope
        // ComplexType so downstream consumers (Wave E1 RAG retriever) can project entityType
        // + entityId without re-parsing the subject.
        "scope"
    };

    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<IndexRetrieveNode> _logger;

    public IndexRetrieveNode(
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        ILogger<IndexRetrieveNode> logger)
    {
        _searchIndexClient = searchIndexClient;
        _openAiClient = openAiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.IndexRetrieve
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var config = ParseConfig(context.Node.ConfigJson);
        if (config is null)
            return NodeValidationResult.Failure(
                "IndexRetrieve node requires ConfigJson with at least one selector " +
                "(artifactType, predicate, filter, or vectorQuery).");

        // Require at least one narrowing dimension beyond the implicit tenant filter to prevent
        // accidental full-index scans against multi-tenant indexes.
        // Wave D6 (task 035): subjectScope also counts as a narrowing dimension (matter:/project:/invoice:).
        var hasNarrowing =
            !string.IsNullOrWhiteSpace(config.ArtifactType) ||
            !string.IsNullOrWhiteSpace(config.Predicate) ||
            !string.IsNullOrWhiteSpace(config.Filter) ||
            !string.IsNullOrWhiteSpace(config.VectorQuery) ||
            !string.IsNullOrWhiteSpace(config.SubjectScope);

        return hasNarrowing
            ? NodeValidationResult.Success()
            : NodeValidationResult.Failure(
                "IndexRetrieve config requires at least one of: artifactType, predicate, filter, vectorQuery, subjectScope.");
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var validation = Validate(context);
        if (!validation.IsValid)
        {
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                string.Join("; ", validation.Errors),
                NodeErrorCodes.ValidationFailed,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }

        var config = ParseConfig(context.Node.ConfigJson)!;
        var indexName = string.IsNullOrWhiteSpace(config.IndexName) ? DefaultIndexName : config.IndexName!;
        var topK = config.TopK ?? DefaultTopK;
        var requireEvidence = config.RequireEvidence ?? true;

        try
        {
            // 1. Build filter — tenantId is ALWAYS enforced from context; cannot be overridden.
            var filter = BuildFilter(context.TenantId, config);

            // 2. Build SearchOptions
            var searchOptions = new SearchOptions
            {
                Filter = filter,
                Size = topK,
                IncludeTotalCount = true
            };
            foreach (var field in SelectFields)
                searchOptions.Select.Add(field);

            // 3. Optional vector search
            ReadOnlyMemory<float> queryEmbedding = default;
            var useVector = !string.IsNullOrWhiteSpace(config.VectorQuery);
            if (useVector)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    queryEmbedding = await _openAiClient.GenerateEmbeddingAsync(
                        config.VectorQuery!,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "IndexRetrieveNode {NodeId}: embedding generation failed in {ElapsedMs}ms — falling back to filter-only retrieval",
                        context.Node.Id, sw.ElapsedMilliseconds);
                    useVector = false;
                }
            }

            if (useVector && queryEmbedding.Length > 0)
            {
                var vectorQuery = new VectorizedQuery(queryEmbedding)
                {
                    KNearestNeighborsCount = topK,
                    Fields = { VectorFieldName }
                };
                searchOptions.VectorSearch = new VectorSearchOptions
                {
                    Queries = { vectorQuery }
                };
            }

            // 4. Execute search
            var searchClient = _searchIndexClient.GetSearchClient(indexName);
            var searchText = useVector ? null : "*";

            _logger.LogDebug(
                "IndexRetrieveNode {NodeId}: querying {IndexName} (filter={FilterLen} chars, vector={UseVector}, topK={TopK})",
                context.Node.Id, indexName, filter?.Length ?? 0, useVector, topK);

            var response = await searchClient.SearchAsync<SearchDocument>(
                searchText, searchOptions, cancellationToken).ConfigureAwait(false);

            // 5. Materialize rows + parse valueJson into InsightArtifact-shaped projections
            var rows = new List<InsightRowProjection>();
            await foreach (var hit in response.Value.GetResultsAsync().ConfigureAwait(false))
            {
                rows.Add(MapToProjection(hit));
            }

            var totalCount = response.Value.TotalCount ?? rows.Count;

            // 6. D-A23 / D-48 EvidenceGuard
            if (requireEvidence && rows.Count == 0)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    "IndexRetrieve returned zero artifacts and requireEvidence=true (D-A23/D-48 EvidenceGuard). " +
                    "Downstream synthesis would have no evidence to ground on.",
                    NodeErrorCodes.InternalError,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            var output = new IndexRetrieveOutput
            {
                IndexName = indexName,
                Count = rows.Count,
                TotalCount = (int)totalCount,
                Artifacts = rows
            };

            _logger.LogInformation(
                "IndexRetrieveNode {NodeId} returned {Count}/{TotalCount} artifacts from {IndexName}",
                context.Node.Id, rows.Count, totalCount, indexName);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                output,
                textContent: $"Retrieved {rows.Count} artifacts from {indexName}",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "IndexRetrieveNode {NodeId}: AI Search request failed ({Status}): {Message}",
                context.Node.Id, ex.Status, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"AI Search request failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "IndexRetrieveNode {NodeId} failed: {Message}", context.Node.Id, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Index retrieval failed: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    private static IndexRetrieveNodeConfig? ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<IndexRetrieveNodeConfig>(configJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the OData filter combining the tenant guard with config-supplied narrowing.
    /// </summary>
    /// <remarks>
    /// Wave D6 (task 035) — when <see cref="IndexRetrieveNodeConfig.SubjectScope"/> is set,
    /// emits the hybrid dual-read OR-filter per design-a6 §4.5:
    /// <list type="bullet">
    ///   <item>matter:&lt;guid&gt; → <c>(scope/matterId eq '&lt;guid&gt;' or (scope/entityType eq 'matter' and scope/entityId eq '&lt;guid&gt;'))</c></item>
    ///   <item>project:&lt;guid&gt; → <c>(scope/entityType eq 'project' and scope/entityId eq '&lt;guid&gt;')</c></item>
    ///   <item>invoice:&lt;guid&gt; → <c>(scope/entityType eq 'invoice' and scope/entityId eq '&lt;guid&gt;')</c></item>
    /// </list>
    /// This preserves NFR-08 — Phase 1 Observations carrying only <c>scope.matterId</c>
    /// (no <c>scope.entityType</c>) remain findable for matter-subject queries.
    /// </remarks>
    internal static string BuildFilter(string tenantId, IndexRetrieveNodeConfig config)
    {
        var parts = new List<string>
        {
            $"tenantId eq '{EscapeODataValue(tenantId)}'"
        };

        if (!string.IsNullOrWhiteSpace(config.ArtifactType))
            parts.Add($"artifactType eq '{EscapeODataValue(config.ArtifactType!)}'");

        if (!string.IsNullOrWhiteSpace(config.Predicate))
            parts.Add($"predicate eq '{EscapeODataValue(config.Predicate!)}'");

        if (!string.IsNullOrWhiteSpace(config.SubjectScope))
        {
            var scopeFilter = BuildSubjectScopeFilter(config.SubjectScope!);
            if (scopeFilter is not null)
                parts.Add(scopeFilter);
        }

        if (!string.IsNullOrWhiteSpace(config.Filter))
            parts.Add($"({config.Filter})");

        return string.Join(" and ", parts);
    }

    /// <summary>
    /// Wave D6 (task 035) — builds the hybrid dual-read OR-filter for a subject of shape
    /// <c>&lt;scheme&gt;:&lt;entityId&gt;</c>. Returns null when the subject is malformed (caller falls
    /// back to other narrowing dimensions or the validator rejects).
    /// </summary>
    internal static string? BuildSubjectScopeFilter(string subjectScope)
    {
        var colonIdx = subjectScope.IndexOf(':');
        if (colonIdx <= 0 || colonIdx >= subjectScope.Length - 1)
        {
            return null;
        }

        var scheme = subjectScope[..colonIdx].Trim().ToLowerInvariant();
        var entityId = subjectScope[(colonIdx + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        var escapedId = EscapeODataValue(entityId);
        var escapedScheme = EscapeODataValue(scheme);

        // Matter subjects: dual-read OR-filter per design-a6 §4.5 — covers Phase 1
        // Observations carrying only scope.matterId AND Phase 1.5 Observations carrying both.
        if (string.Equals(scheme, "matter", StringComparison.OrdinalIgnoreCase))
        {
            return $"(scope/matterId eq '{escapedId}' or " +
                   $"(scope/entityType eq 'matter' and scope/entityId eq '{escapedId}'))";
        }

        // All other schemes: canonical (entityType, entityId) filter only.
        return $"(scope/entityType eq '{escapedScheme}' and scope/entityId eq '{escapedId}')";
    }

    private static string EscapeODataValue(string value) =>
        value.Replace("'", "''");

    /// <summary>
    /// Maps a raw <see cref="SearchDocument"/> to the canonical <see cref="InsightRowProjection"/>.
    /// </summary>
    private static InsightRowProjection MapToProjection(SearchResult<SearchDocument> hit)
    {
        var doc = hit.Document;
        string? GetString(string key) => doc.TryGetValue(key, out var v) ? v?.ToString() : null;

        // Wave D6 (task 035) — project the top-level scope ComplexType so downstream
        // consumers (Wave E1 RAG retriever) can read entityType + entityId without re-parsing
        // the subject.
        InsightRowScope? scope = null;
        if (doc.TryGetValue("scope", out var scopeObj) && scopeObj is IDictionary<string, object?> scopeDict)
        {
            scope = new InsightRowScope
            {
                MatterId = scopeDict.TryGetValue("matterId", out var mid) ? mid?.ToString() : null,
                EntityType = scopeDict.TryGetValue("entityType", out var et) ? et?.ToString() : null,
                EntityId = scopeDict.TryGetValue("entityId", out var eid) ? eid?.ToString() : null,
                TenantId = scopeDict.TryGetValue("tenantId", out var tid) ? tid?.ToString() : null,
                PracticeArea = scopeDict.TryGetValue("practiceArea", out var pa) ? pa?.ToString() : null
            };
        }

        return new InsightRowProjection
        {
            Id = GetString("id") ?? string.Empty,
            TenantId = GetString("tenantId") ?? string.Empty,
            ArtifactType = GetString("artifactType") ?? string.Empty,
            Subject = GetString("subject") ?? string.Empty,
            Predicate = GetString("predicate") ?? string.Empty,
            ValueJson = GetString("valueJson"),
            Confidence = doc.TryGetValue("confidence", out var c) && c is not null
                ? Convert.ToDouble(c)
                : null,
            ProducedBy = GetString("producedBy"),
            Status = GetString("status"),
            AsOf = doc.TryGetValue("asOf", out var ts) && ts is DateTimeOffset dto
                ? dto
                : (DateTimeOffset?)null,
            Score = hit.Score,
            Scope = scope
        };
    }
}

/// <summary>
/// Config schema for <see cref="IndexRetrieveNode"/>.
/// </summary>
internal sealed record IndexRetrieveNodeConfig
{
    [JsonPropertyName("indexName")]
    public string? IndexName { get; init; }

    [JsonPropertyName("artifactType")]
    public string? ArtifactType { get; init; }

    [JsonPropertyName("predicate")]
    public string? Predicate { get; init; }

    /// <summary>
    /// Optional additional OData filter clause concatenated (with parentheses) to the
    /// tenant + artifactType + predicate guards. <b>Trusted operator input only</b> — this
    /// field is passed through to AI Search unsanitized so playbook authors can express
    /// arbitrary OData (per SPEC §3.4.3 worked examples,
    /// e.g., <c>value/raw/scope/matterType eq 'IP'</c>). MUST NOT be sourced from
    /// template-rendered end-user input; ConfigJson is authored by administrators in the
    /// Dataverse <c>sprk_playbooknode.ConfigJson</c> field, not by request bodies.
    /// </summary>
    [JsonPropertyName("filter")]
    public string? Filter { get; init; }

    [JsonPropertyName("vectorQuery")]
    public string? VectorQuery { get; init; }

    [JsonPropertyName("topK")]
    public int? TopK { get; init; }

    [JsonPropertyName("requireEvidence")]
    public bool? RequireEvidence { get; init; }

    /// <summary>
    /// Wave D6 (task 035) — optional subject scope filter of shape <c>&lt;scheme&gt;:&lt;entityId&gt;</c>
    /// (e.g., <c>"matter:M-2024-0341"</c>, <c>"project:p-abc"</c>, <c>"invoice:i-xyz"</c>).
    /// When set, the node emits the hybrid dual-read OR-filter per design-a6 §4.5 — for
    /// matter subjects this is <c>(scope/matterId eq … or (scope/entityType eq 'matter' and
    /// scope/entityId eq …))</c>, preserving NFR-08 backward-compat with Phase 1 Observations
    /// that carry only <c>scope.matterId</c>. For non-matter schemes the filter narrows by
    /// <c>scope/entityType</c> + <c>scope/entityId</c> only.
    /// </summary>
    [JsonPropertyName("subjectScope")]
    public string? SubjectScope { get; init; }
}

/// <summary>
/// Structured output of <see cref="IndexRetrieveNode"/>.
/// </summary>
public sealed record IndexRetrieveOutput
{
    public required string IndexName { get; init; }
    public required int Count { get; init; }
    public required int TotalCount { get; init; }
    public required IReadOnlyList<InsightRowProjection> Artifacts { get; init; }
}

/// <summary>
/// Flattened projection of a single <c>spaarke-insights-index</c> row. The full
/// <see cref="InsightArtifact"/> can be reconstructed from <see cref="ValueJson"/> by
/// downstream nodes that need the typed envelope.
/// </summary>
public sealed record InsightRowProjection
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string ArtifactType { get; init; }
    public required string Subject { get; init; }
    public required string Predicate { get; init; }
    public string? ValueJson { get; init; }
    public double? Confidence { get; init; }
    public string? ProducedBy { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? AsOf { get; init; }
    public double? Score { get; init; }

    /// <summary>
    /// Wave D6 (task 035) — projection of the index's top-level <c>scope</c> ComplexType per
    /// design-a6 §4. Null for Phase 1 Observations written before Wave D6 migration; populated
    /// for Phase 1.5+ Observations.
    /// </summary>
    public InsightRowScope? Scope { get; init; }
}

/// <summary>
/// Wave D6 (task 035) — flattened projection of the <c>scope</c> ComplexType fields per
/// design-a6 §4. Mirrors <see cref="Sprk.Bff.Api.Services.Ai.Insights.Ingest.ScopeIndexEntry"/>
/// (which is internal to the Ingest namespace); this Zone A public shape is consumed by
/// downstream nodes + Wave E1 RAG retriever.
/// </summary>
public sealed record InsightRowScope
{
    public string? MatterId { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? TenantId { get; init; }
    public string? PracticeArea { get; init; }
}
