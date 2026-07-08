using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics for AI operations (OpenTelemetry-compatible).
/// Tracks: summarization, RAG search, tool execution, and export operations.
///
/// Usage:
/// - Meter name: "Sprk.Bff.Api.Ai" for OpenTelemetry configuration
/// - Metrics prefixes: ai.summarize.*, ai.rag.*, ai.tool.*, ai.export.*
/// - Common dimensions: ai.status (success/failed), ai.error_code
///
/// Application Insights custom queries:
/// - RAG latency: customMetrics | where name == "ai.rag.duration" | summarize percentile(value, 95)
/// - Tool success rate: customMetrics | where name == "ai.tool.requests" | summarize count() by customDimensions["ai.status"]
/// - Export by format: customMetrics | where name == "ai.export.requests" | summarize count() by customDimensions["ai.format"]
/// </summary>
public class AiTelemetry : IDisposable
{
    private readonly Meter _meter;

    // Summarization metrics
    private readonly Counter<long> _summarizeRequests;
    private readonly Counter<long> _summarizeSuccesses;
    private readonly Counter<long> _summarizeFailures;
    private readonly Histogram<double> _summarizeDuration;
    private readonly Counter<long> _tokenUsage;
    private readonly Histogram<long> _fileSize;

    // RAG metrics
    private readonly Counter<long> _ragRequests;
    private readonly Histogram<double> _ragDuration;
    private readonly Histogram<double> _ragEmbeddingDuration;
    private readonly Histogram<double> _ragSearchDuration;
    private readonly Histogram<long> _ragResultCount;

    // Tool execution metrics
    private readonly Counter<long> _toolRequests;
    private readonly Histogram<double> _toolDuration;
    private readonly Counter<long> _toolTokens;

    // Export metrics
    private readonly Counter<long> _exportRequests;
    private readonly Histogram<double> _exportDuration;
    private readonly Histogram<long> _exportFileSize;

    // Privilege filter metrics (AIPU2-027)
    private readonly Counter<long> _privilegeFilterApplied;
    private readonly Counter<long> _privilegeFilterEmptyResult;

    // Honest-refusal metrics (FR-P2-04, spaarke-ai-architecture-redesign-r1 task 033)
    private readonly Counter<long> _dispatchRefused;

    // Invalid-tool-schema exclusions (G-P3 UAT round-1 H1, spaarke-ai-architecture-redesign-r1)
    private readonly Counter<long> _invalidToolSchema;

    // Per-tenant metering counters (FR-P4-05 / NFR-05, spaarke-ai-architecture-redesign-r1 task 054)
    private readonly Counter<long> _meteredTurns;
    private readonly Counter<long> _meteredToolCalls;
    private readonly Counter<long> _meteredTokens;
    private readonly Counter<long> _meteredCapabilityInvocations;

    // Meter name for OpenTelemetry
    private const string MeterName = "Sprk.Bff.Api.Ai";

    // Static ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    public AiTelemetry()
    {
        // Create meter (OpenTelemetry-compatible)
        _meter = new Meter(MeterName, "1.0.0");

        // === Privilege Filter Metrics (AIPU2-027) ===
        _privilegeFilterApplied = _meter.CreateCounter<long>(
            name: "ai_retrieval_privilege_filter_applied_total",
            unit: "{request}",
            description: "Total number of RAG search requests with privilege_group_ids filter applied");

        _privilegeFilterEmptyResult = _meter.CreateCounter<long>(
            name: "ai_retrieval_privilege_filter_empty_result_total",
            unit: "{request}",
            description: "Total number of RAG search requests where user has no matching groups (public docs only)");

        // === Honest-Refusal Metrics (FR-P2-04 / ADR-039 L4) ===
        _dispatchRefused = _meter.CreateCounter<long>(
            name: "dispatch_refused",
            unit: "{refusal}",
            description: "Number of text-path turns that ended in the honest-refusal outcome " +
                         "(the tenant's no_match_handler Binding rendered). The refusal-backlog " +
                         "product signal (FR-P4-07 deferred admin view) aggregates this counter.");

        // === Invalid-Tool-Schema Metrics (G-P3 UAT round-1 H1) ===
        _invalidToolSchema = _meter.CreateCounter<long>(
            name: "ai.tool.schema_invalid",
            unit: "{row}",
            description: "Catalog rows excluded from the agent-turn tool projection because their " +
                         "authored schema would fail OpenAI function-parameters validation (one bad " +
                         "row must never 400 the whole turn — G-P3 UAT round-1 resilience fix).");

        // === Per-Tenant Metering Counters (FR-P4-05 / NFR-05) ===
        // Usage counters dimensioned per tenant AND per user. Deliberate exception to the
        // low-cardinality discipline for user.id: NFR-05 REQUIRES per-user drill-down;
        // both values are opaque AAD GUIDs (identifiers only — NFR-07/ADR-015 compliant).
        // Queried by the KQL pack at scripts/kql/ai-metering/.
        _meteredTurns = _meter.CreateCounter<long>(
            name: "ai.metering.turns",
            unit: "{turn}",
            description: "Agent-turn loop turns completed (FR-P2-01), dimensioned per tenant/user. " +
                         "Carries the ADR-016/NFR-09 per-turn tool budget consumed-vs-cap dimensions " +
                         "(tool_budget.spent / tool_budget.cap / tool_budget.denied).");

        _meteredToolCalls = _meter.CreateCounter<long>(
            name: "ai.metering.tool_calls",
            unit: "{call}",
            description: "Tool invocations executed inside agent turns (BudgetedAIFunction-wrapped), " +
                         "dimensioned per tenant/user/tool.id.");

        _meteredTokens = _meter.CreateCounter<long>(
            name: "ai.metering.tokens",
            unit: "{token}",
            description: "LLM tokens consumed (input + output as token.type), dimensioned per " +
                         "tenant/user/entry.path. source=loop (agent-turn streaming usage) or " +
                         "source=executor (prompted-executor structured completions).");

        _meteredCapabilityInvocations = _meter.CreateCounter<long>(
            name: "ai.metering.capability_invocations",
            unit: "{invocation}",
            description: "Capability (Binding) executions across the closed entry paths " +
                         "(text/click/event/coded), dimensioned per tenant/user/capability/outcome. " +
                         "Event-path records carry budget.cap so the NFR-09 per-user daily budget " +
                         "is queryable as consumed-vs-cap.");

        // === Summarization Metrics ===
        _summarizeRequests = _meter.CreateCounter<long>(
            name: "ai.summarize.requests",
            unit: "{request}",
            description: "Total number of summarization requests");

        _summarizeSuccesses = _meter.CreateCounter<long>(
            name: "ai.summarize.successes",
            unit: "{request}",
            description: "Number of successful summarizations");

        _summarizeFailures = _meter.CreateCounter<long>(
            name: "ai.summarize.failures",
            unit: "{request}",
            description: "Number of failed summarizations");

        _summarizeDuration = _meter.CreateHistogram<double>(
            name: "ai.summarize.duration",
            unit: "ms",
            description: "Summarization operation duration in milliseconds");

        _tokenUsage = _meter.CreateCounter<long>(
            name: "ai.summarize.tokens",
            unit: "{token}",
            description: "Total tokens used for summarization");

        _fileSize = _meter.CreateHistogram<long>(
            name: "ai.summarize.file_size",
            unit: "By",
            description: "Size of files processed for summarization");

        // === RAG Metrics ===
        _ragRequests = _meter.CreateCounter<long>(
            name: "ai.rag.requests",
            unit: "{request}",
            description: "Total number of RAG search requests");

        _ragDuration = _meter.CreateHistogram<double>(
            name: "ai.rag.duration",
            unit: "ms",
            description: "Total RAG search duration in milliseconds");

        _ragEmbeddingDuration = _meter.CreateHistogram<double>(
            name: "ai.rag.embedding_duration",
            unit: "ms",
            description: "Embedding generation duration in milliseconds");

        _ragSearchDuration = _meter.CreateHistogram<double>(
            name: "ai.rag.search_duration",
            unit: "ms",
            description: "Azure AI Search query duration in milliseconds");

        _ragResultCount = _meter.CreateHistogram<long>(
            name: "ai.rag.result_count",
            unit: "{result}",
            description: "Number of results returned from RAG search");

        // === Tool Execution Metrics ===
        _toolRequests = _meter.CreateCounter<long>(
            name: "ai.tool.requests",
            unit: "{request}",
            description: "Total number of tool executions");

        _toolDuration = _meter.CreateHistogram<double>(
            name: "ai.tool.duration",
            unit: "ms",
            description: "Tool execution duration in milliseconds");

        _toolTokens = _meter.CreateCounter<long>(
            name: "ai.tool.tokens",
            unit: "{token}",
            description: "Total tokens used by tools");

        // === Export Metrics ===
        _exportRequests = _meter.CreateCounter<long>(
            name: "ai.export.requests",
            unit: "{request}",
            description: "Total number of export requests");

        _exportDuration = _meter.CreateHistogram<double>(
            name: "ai.export.duration",
            unit: "ms",
            description: "Export operation duration in milliseconds");

        _exportFileSize = _meter.CreateHistogram<long>(
            name: "ai.export.file_size",
            unit: "By",
            description: "Size of exported files in bytes");
    }

    /// <summary>
    /// Record one honest-refusal outcome of the agent-turn loop (FR-P2-04 /
    /// ADR-039 grounded-execution clause (d)): an utterance matched nothing in the
    /// closed catalog, could not be answered as a cited ad-hoc read, and the
    /// tenant's <c>no_match_handler</c> Binding rendered the refusal.
    /// </summary>
    /// <remarks>
    /// Lands in App Insights as <c>customMetrics | where name == "dispatch_refused"</c>.
    /// Dimensions are BOUNDED per the R5 summarize-telemetry cardinality discipline:
    /// <c>tenant.id</c> (low-cardinality, ADR-014 precedent) and
    /// <c>render_status</c> ∈ { <c>rendered</c>, <c>render_failed</c> }. Session /
    /// binding / output-key identifiers ride the companion structured log line
    /// (<c>[FR-P2-04][dispatch_refused]</c> in <see cref="Services.Ai.Chat.RefusalCapabilityTool"/>)
    /// — identifiers only, never utterance content (NFR-07 / ADR-015).
    /// </remarks>
    /// <param name="rendered">
    /// True when the tenant template rendered + ledger-stored; false when the refusal
    /// capability itself failed (the turn still ends in a refusal, so the backlog
    /// signal is still counted).
    /// </param>
    /// <param name="tenantId">Optional low-cardinality tenant id dimension; null omits it.</param>
    public void RecordDispatchRefused(bool rendered, string? tenantId = null)
    {
        var tags = new TagList
        {
            { "render_status", rendered ? "rendered" : "render_failed" },
        };
        if (!string.IsNullOrEmpty(tenantId))
        {
            tags.Add("tenant.id", tenantId);
        }

        _dispatchRefused.Add(1, tags);
    }

    /// <summary>
    /// Record one catalog row excluded from the agent-turn tool projection because its
    /// authored schema fails OpenAI function-parameters validation (G-P3 UAT round-1 H1:
    /// one malformed row 400-failed every text-path turn until projection-time validation
    /// excluded the row instead).
    /// </summary>
    /// <remarks>
    /// Lands in App Insights as <c>customMetrics | where name == "ai.tool.schema_invalid"</c>.
    /// Dimensions are BOUNDED (NFR-07 / ADR-015): <c>catalog</c> ∈ { <c>binding</c>,
    /// <c>tool</c> }, <c>row.identifier</c> (deterministic consumer type / tool name — the
    /// closed catalogs are small), and optional <c>tenant.id</c>. The validation error
    /// detail rides the companion structured log line (<c>[invalid-tool-schema]</c>).
    /// </remarks>
    /// <param name="catalog">Which closed catalog the row belongs to: "binding" (sprk_playbookconsumer/sprk_analysisaction input schema) or "tool" (sprk_analysistool json schema).</param>
    /// <param name="rowIdentifier">Deterministic catalog identifier (consumer type or tool name) — never content.</param>
    /// <param name="tenantId">Optional low-cardinality tenant id dimension; null omits it.</param>
    public void RecordInvalidToolSchema(string catalog, string rowIdentifier, string? tenantId = null)
    {
        var tags = new TagList
        {
            { "catalog", catalog },
            { "row.identifier", rowIdentifier },
        };
        if (!string.IsNullOrEmpty(tenantId))
        {
            tags.Add("tenant.id", tenantId);
        }

        _invalidToolSchema.Add(1, tags);
    }

    #region Per-Tenant Metering (FR-P4-05 / NFR-05)

    /// <summary>
    /// Record one completed agent-turn loop turn (FR-P2-01), dimensioned per tenant/user,
    /// carrying the ADR-016/NFR-09 per-turn tool-budget consumed-vs-cap observability
    /// (<c>tool_budget.spent</c> / <c>tool_budget.cap</c> / <c>tool_budget.denied</c> —
    /// all bounded small integers, cap defaults to 8).
    /// </summary>
    /// <remarks>
    /// Lands in App Insights as <c>customMetrics | where name == "ai.metering.turns"</c>.
    /// NFR-07: identifiers + counts only — tenant/user ids are opaque AAD GUIDs.
    /// </remarks>
    public void RecordMeteredTurn(
        string? tenantId,
        string? userId,
        int toolBudgetSpent,
        int toolBudgetCap,
        int toolBudgetDenied)
    {
        var tags = new TagList
        {
            { "tool_budget.spent", toolBudgetSpent },
            { "tool_budget.cap", toolBudgetCap },
            { "tool_budget.denied", toolBudgetDenied },
        };
        AddIdentityTags(ref tags, tenantId, userId);

        _meteredTurns.Add(1, tags);
    }

    /// <summary>
    /// Record one executed tool invocation inside an agent turn (the unit the ADR-016
    /// per-turn tool budget counts), dimensioned per tenant/user/tool.
    /// </summary>
    /// <param name="tenantId">Opaque tenant id ('tid'); null omits the dimension.</param>
    /// <param name="userId">Opaque user object id ('oid'); null omits the dimension.</param>
    /// <param name="toolId">Deterministic tool identifier from the closed catalog projection (never content).</param>
    public void RecordMeteredToolCall(string? tenantId, string? userId, string toolId)
    {
        var tags = new TagList
        {
            { "tool.id", toolId },
            { "outcome", "executed" },
        };
        AddIdentityTags(ref tags, tenantId, userId);

        _meteredToolCalls.Add(1, tags);
    }

    /// <summary>
    /// Record LLM token consumption, dimensioned per tenant/user/entry-path. Emits two
    /// counter increments (token.type = input / output). When <paramref name="tenantId"/>
    /// is null, identity + entry path fall back to the ambient <see cref="AiMeteringContext"/>
    /// scope (set at the entry seams) — this is how executor-path usage observed inside
    /// <c>OpenAiClient</c> is attributed.
    /// </summary>
    /// <param name="tenantId">Opaque tenant id, or null to use <see cref="AiMeteringContext.Current"/>.</param>
    /// <param name="userId">Opaque user object id, or null to use the ambient scope.</param>
    /// <param name="inputTokens">Prompt token count reported by the model.</param>
    /// <param name="outputTokens">Completion token count reported by the model.</param>
    /// <param name="source">"loop" (agent-turn streaming usage) or "executor" (prompted-executor completions).</param>
    /// <param name="model">Optional model/deployment name.</param>
    /// <param name="entryPath">Optional entry path override; defaults to the ambient scope's.</param>
    public void RecordMeteredTokens(
        string? tenantId,
        string? userId,
        long inputTokens,
        long outputTokens,
        string source,
        string? model = null,
        string? entryPath = null)
    {
        var scope = AiMeteringContext.Current;
        tenantId ??= scope?.TenantId;
        userId ??= scope?.UserId;
        entryPath ??= scope?.EntryPath;

        if (inputTokens <= 0 && outputTokens <= 0)
        {
            return;
        }

        var baseTags = new TagList
        {
            { "source", source },
        };
        if (!string.IsNullOrEmpty(entryPath)) baseTags.Add("entry.path", entryPath);
        if (!string.IsNullOrEmpty(model)) baseTags.Add("ai.model", model);
        AddIdentityTags(ref baseTags, tenantId, userId);

        if (inputTokens > 0)
        {
            var tags = baseTags;
            tags.Add("token.type", "input");
            _meteredTokens.Add(inputTokens, tags);
        }

        if (outputTokens > 0)
        {
            var tags = baseTags;
            tags.Add("token.type", "output");
            _meteredTokens.Add(outputTokens, tags);
        }
    }

    /// <summary>
    /// Record one capability (Binding) execution at a dispatch seam, dimensioned per
    /// tenant/user/entry-path/capability/outcome (FR-P4-05). Event-path callers pass
    /// <paramref name="budgetCap"/> so the NFR-09 per-user daily Event-path budget is
    /// queryable as consumed-vs-cap in the KQL pack.
    /// </summary>
    /// <param name="tenantId">Opaque tenant id.</param>
    /// <param name="userId">Opaque user object id, or null to use the ambient <see cref="AiMeteringContext"/> scope.</param>
    /// <param name="entryPath">One of the <see cref="AiMeteringContext"/> entry-path constants; null uses the ambient scope (default "click" at the dispatch seam).</param>
    /// <param name="capability">Bounded capability identifier — the Binding's ucid or consumer type (closed catalog, never content).</param>
    /// <param name="outcome">"success" / "failed".</param>
    /// <param name="budgetCap">Optional daily budget cap (Event path only) for consumed-vs-cap views.</param>
    public void RecordCapabilityInvocation(
        string? tenantId,
        string? userId,
        string? entryPath,
        string capability,
        string outcome,
        int? budgetCap = null)
    {
        var scope = AiMeteringContext.Current;
        tenantId ??= scope?.TenantId;
        userId ??= scope?.UserId;
        entryPath ??= scope?.EntryPath ?? AiMeteringContext.EntryPathClick;

        var tags = new TagList
        {
            { "entry.path", entryPath },
            { "capability", capability },
            { "outcome", outcome },
        };
        if (budgetCap.HasValue) tags.Add("budget.cap", budgetCap.Value);
        AddIdentityTags(ref tags, tenantId, userId);

        _meteredCapabilityInvocations.Add(1, tags);
    }

    /// <summary>
    /// Append the tenant/user identity dimensions when present. Omission (not a sentinel
    /// value) is the null representation so KQL rollups can filter empties explicitly.
    /// </summary>
    private static void AddIdentityTags(ref TagList tags, string? tenantId, string? userId)
    {
        if (!string.IsNullOrEmpty(tenantId))
        {
            tags.Add("tenant.id", tenantId);
        }
        if (!string.IsNullOrEmpty(userId))
        {
            tags.Add("user.id", userId);
        }
    }

    #endregion

    /// <summary>
    /// Record the start of a summarization request.
    /// Returns a Stopwatch for timing.
    /// </summary>
    /// <param name="method">Processing method: streaming, batch</param>
    /// <param name="extraction">Extraction method: native, document_intelligence, vision</param>
    public Stopwatch RecordRequestStart(string method = "streaming", string? extraction = null)
    {
        var tags = new TagList
        {
            { "ai.method", method }
        };
        if (extraction != null)
        {
            tags.Add("ai.extraction", extraction);
        }

        _summarizeRequests.Add(1, tags);
        return Stopwatch.StartNew();
    }

    /// <summary>
    /// Record successful summarization completion.
    /// </summary>
    /// <param name="stopwatch">Stopwatch from RecordRequestStart</param>
    /// <param name="method">Processing method: streaming, batch</param>
    /// <param name="extraction">Extraction method: native, document_intelligence, vision</param>
    /// <param name="fileType">File extension (e.g., .pdf, .txt)</param>
    /// <param name="fileSizeBytes">Size of the file in bytes</param>
    public void RecordSuccess(
        Stopwatch stopwatch,
        string method = "streaming",
        string? extraction = null,
        string? fileType = null,
        long? fileSizeBytes = null)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        var tags = new TagList
        {
            { "ai.method", method },
            { "ai.status", "success" }
        };
        if (extraction != null) tags.Add("ai.extraction", extraction);
        if (fileType != null) tags.Add("ai.file_type", fileType);

        _summarizeSuccesses.Add(1, tags);
        _summarizeDuration.Record(durationMs, tags);

        if (fileSizeBytes.HasValue)
        {
            _fileSize.Record(fileSizeBytes.Value, tags);
        }
    }

    /// <summary>
    /// Record failed summarization.
    /// </summary>
    /// <param name="stopwatch">Stopwatch from RecordRequestStart</param>
    /// <param name="errorCode">Error code (e.g., openai_rate_limit, extraction_failed)</param>
    /// <param name="method">Processing method: streaming, batch</param>
    /// <param name="extraction">Extraction method: native, document_intelligence, vision</param>
    public void RecordFailure(
        Stopwatch stopwatch,
        string errorCode,
        string method = "streaming",
        string? extraction = null)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        var tags = new TagList
        {
            { "ai.method", method },
            { "ai.status", "failed" },
            { "ai.error_code", errorCode }
        };
        if (extraction != null) tags.Add("ai.extraction", extraction);

        _summarizeFailures.Add(1, tags);
        _summarizeDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Record token usage for cost tracking.
    /// </summary>
    /// <param name="promptTokens">Number of tokens in the prompt</param>
    /// <param name="completionTokens">Number of tokens in the completion</param>
    /// <param name="model">Model name (e.g., gpt-4o-mini, gpt-4o)</param>
    public void RecordTokenUsage(long promptTokens, long completionTokens, string model = "gpt-4o-mini")
    {
        _tokenUsage.Add(promptTokens,
            new KeyValuePair<string, object?>("ai.token_type", "prompt"),
            new KeyValuePair<string, object?>("ai.model", model));

        _tokenUsage.Add(completionTokens,
            new KeyValuePair<string, object?>("ai.token_type", "completion"),
            new KeyValuePair<string, object?>("ai.model", model));
    }

    /// <summary>
    /// Start a new Activity for distributed tracing.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., SummarizeStream, SummarizeBatch)</param>
    /// <param name="documentId">Document ID being processed</param>
    public Activity? StartActivity(string operationName, Guid? documentId = null)
    {
        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        if (activity != null && documentId.HasValue)
        {
            activity.SetTag("document.id", documentId.Value.ToString());
        }
        return activity;
    }

    #region RAG Metrics

    /// <summary>
    /// Record a RAG search operation.
    /// </summary>
    /// <param name="totalDurationMs">Total search duration in milliseconds</param>
    /// <param name="embeddingDurationMs">Embedding generation duration in milliseconds</param>
    /// <param name="searchDurationMs">Azure AI Search query duration in milliseconds</param>
    /// <param name="resultCount">Number of results returned</param>
    /// <param name="success">Whether the operation succeeded</param>
    /// <param name="embeddingCacheHit">Whether embedding was retrieved from cache</param>
    /// <param name="errorCode">Error code if failed</param>
    public void RecordRagSearch(
        double totalDurationMs,
        double embeddingDurationMs,
        double searchDurationMs,
        int resultCount,
        bool success,
        bool embeddingCacheHit = false,
        string? errorCode = null)
    {
        var tags = new TagList
        {
            { "ai.status", success ? "success" : "failed" },
            { "ai.cache_hit", embeddingCacheHit.ToString().ToLowerInvariant() }
        };
        if (!success && errorCode != null)
        {
            tags.Add("ai.error_code", errorCode);
        }

        _ragRequests.Add(1, tags);
        _ragDuration.Record(totalDurationMs, tags);
        _ragEmbeddingDuration.Record(embeddingDurationMs, tags);
        _ragSearchDuration.Record(searchDurationMs, tags);
        _ragResultCount.Record(resultCount, tags);
    }

    /// <summary>
    /// Record that a privilege_group_ids filter was applied to a RAG search (AIPU2-027).
    /// </summary>
    /// <param name="groupCount">Number of group IDs included in the filter (0 = public-only).</param>
    public void RecordPrivilegeFilterApplied(int groupCount)
    {
        _privilegeFilterApplied.Add(1,
            new KeyValuePair<string, object?>("ai.privilege_group_count", groupCount));
    }

    /// <summary>
    /// Record that a RAG search returned zero results due to the user having no matching groups.
    /// </summary>
    public void RecordPrivilegeFilterEmptyResult()
    {
        _privilegeFilterEmptyResult.Add(1);
    }

    #endregion

    #region Tool Metrics

    /// <summary>
    /// Record a tool execution.
    /// </summary>
    /// <param name="toolId">Tool identifier (e.g., GenericAnalysisHandler, SummaryHandler)</param>
    /// <param name="durationMs">Execution duration in milliseconds</param>
    /// <param name="success">Whether the operation succeeded</param>
    /// <param name="inputTokens">Number of input tokens used</param>
    /// <param name="outputTokens">Number of output tokens generated</param>
    /// <param name="errorCode">Error code if failed</param>
    public void RecordToolExecution(
        string toolId,
        double durationMs,
        bool success,
        int inputTokens = 0,
        int outputTokens = 0,
        string? errorCode = null)
    {
        var tags = new TagList
        {
            { "ai.tool_id", toolId },
            { "ai.status", success ? "success" : "failed" }
        };
        if (!success && errorCode != null)
        {
            tags.Add("ai.error_code", errorCode);
        }

        _toolRequests.Add(1, tags);
        _toolDuration.Record(durationMs, tags);

        if (inputTokens > 0 || outputTokens > 0)
        {
            _toolTokens.Add(inputTokens,
                new KeyValuePair<string, object?>("ai.tool_id", toolId),
                new KeyValuePair<string, object?>("ai.token_type", "input"));
            _toolTokens.Add(outputTokens,
                new KeyValuePair<string, object?>("ai.tool_id", toolId),
                new KeyValuePair<string, object?>("ai.token_type", "output"));
        }
    }

    #endregion

    #region Export Metrics

    /// <summary>
    /// Record an export operation.
    /// </summary>
    /// <param name="format">Export format (docx, pdf, email)</param>
    /// <param name="durationMs">Export duration in milliseconds</param>
    /// <param name="success">Whether the operation succeeded</param>
    /// <param name="fileSizeBytes">Size of exported file in bytes (null for action-based exports)</param>
    /// <param name="errorCode">Error code if failed</param>
    public void RecordExport(
        string format,
        double durationMs,
        bool success,
        long? fileSizeBytes = null,
        string? errorCode = null)
    {
        var tags = new TagList
        {
            { "ai.format", format.ToLowerInvariant() },
            { "ai.status", success ? "success" : "failed" }
        };
        if (!success && errorCode != null)
        {
            tags.Add("ai.error_code", errorCode);
        }

        _exportRequests.Add(1, tags);
        _exportDuration.Record(durationMs, tags);

        if (fileSizeBytes.HasValue && fileSizeBytes.Value > 0)
        {
            _exportFileSize.Record(fileSizeBytes.Value, tags);
        }
    }

    #endregion

    /// <summary>
    /// Dispose the meter when the service is disposed.
    /// </summary>
    public void Dispose()
    {
        _meter?.Dispose();
    }
}

/// <summary>
/// Extension methods for metric tag building.
/// </summary>
public static class AiTelemetryExtensions
{
    /// <summary>
    /// Convert TextExtractionMethod to telemetry-friendly string.
    /// </summary>
    public static string ToTelemetryString(this Models.Ai.TextExtractionMethod method) => method switch
    {
        Models.Ai.TextExtractionMethod.Native => "native",
        Models.Ai.TextExtractionMethod.DocumentIntelligence => "document_intelligence",
        Models.Ai.TextExtractionMethod.VisionOcr => "vision",
        Models.Ai.TextExtractionMethod.NotSupported => "not_supported",
        _ => "unknown"
    };
}
