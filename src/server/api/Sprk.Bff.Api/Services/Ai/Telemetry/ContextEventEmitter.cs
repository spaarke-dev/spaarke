using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Services.Ai.Telemetry;

/// <summary>
/// R6 Pillar 6c (FR-37 / task 063) — production implementation of
/// <see cref="IContextEventEmitter"/> that emits the six `context.*` execution-trace
/// events via:
/// <list type="bullet">
///   <item><b>OpenTelemetry Meter</b> — one counter per event type, tagged with
///         deterministic IDs only (ADR-015 BINDING).</item>
///   <item><b>Structured logger</b> — one <c>[ADR-015]</c>-prefixed log entry per event,
///         carrying typed enumerated fields only.</item>
/// </list>
///
/// <para>
/// <b>ADR-015 audit (per-method)</b>: every method below constructs <see cref="TagList"/>
/// and structured-log message-templates from typed enumerated fields ONLY (GUIDs, short
/// enum strings, numeric metrics). NO method accepts or logs user-message text, tool
/// input/output bodies, retrieved chunk text, or LLM response text. The interface itself
/// is structurally constrained to enforce this — no <c>object</c>, <c>JsonElement</c>, or
/// free-form <c>string content</c> parameters.
/// </para>
///
/// <para>
/// <b>ADR-029 publish-size</b>: this implementation uses BCL types only
/// (<c>System.Diagnostics.Metrics</c>, <c>Microsoft.Extensions.Logging</c>). Zero new
/// NuGet dependencies. Compiled IL is negligible (~2 KB).
/// </para>
///
/// <para>
/// <b>ADR-010 DI</b>: registered as singleton in <c>AiObservabilityModule</c>
/// (existing module — no new top-level Program.cs DI registration). Thread-safe by
/// construction (Meter + Counter + ILogger are all thread-safe).
/// </para>
///
/// <para>
/// <b>Meter name</b>: <c>Sprk.Bff.Api.Ai.ContextEvents</c>. Follows the existing
/// <c>Sprk.Bff.Api.*</c> Meter-naming convention used by <c>AiTelemetry</c> and
/// <c>UpdateWorkspaceTabHandler</c> (task 058 pattern).
/// </para>
/// </summary>
public sealed class ContextEventEmitter : IContextEventEmitter, IDisposable
{
    /// <summary>
    /// Meter name (canonical) — exposed for test <c>MeterListener</c> subscription.
    /// </summary>
    public const string MeterName = "Sprk.Bff.Api.Ai.ContextEvents";

    /// <summary>
    /// Counter names (canonical) — exposed for test <c>MeterListener</c> matching.
    /// </summary>
    public const string ToolCallStartedCounter = "context.tool_call_started";
    public const string ToolCallCompletedCounter = "context.tool_call_completed";
    public const string KnowledgeRetrievedCounter = "context.knowledge_retrieved";
    public const string PlaybookNodeExecutingCounter = "context.playbook_node_executing";
    public const string PlaybookNodeCompletedCounter = "context.playbook_node_completed";
    public const string DecisionMadeCounter = "context.decision_made";

    // chat-routing-redesign-r1 task 074 — Upload-pipeline counter names (canonical).
    public const string UploadStartedCounter = "context.upload_started";
    public const string UploadClassifiedCounter = "context.upload_classified";
    public const string UploadSummarizedCounter = "context.upload_summarized";
    public const string UploadManifestExtractedCounter = "context.upload_manifest_extracted";
    public const string UploadIndexedCounter = "context.upload_indexed";
    public const string UploadPersistedCounter = "context.upload_persisted";
    public const string UploadCompletedCounter = "context.upload_completed";

    private readonly Meter _meter;
    private readonly Counter<long> _toolCallStarted;
    private readonly Counter<long> _toolCallCompleted;
    private readonly Counter<long> _knowledgeRetrieved;
    private readonly Counter<long> _playbookNodeExecuting;
    private readonly Counter<long> _playbookNodeCompleted;
    private readonly Counter<long> _decisionMade;

    // chat-routing-redesign-r1 task 074 — Upload-pipeline counters.
    private readonly Counter<long> _uploadStarted;
    private readonly Counter<long> _uploadClassified;
    private readonly Counter<long> _uploadSummarized;
    private readonly Counter<long> _uploadManifestExtracted;
    private readonly Counter<long> _uploadIndexed;
    private readonly Counter<long> _uploadPersisted;
    private readonly Counter<long> _uploadCompleted;

    private readonly ILogger<ContextEventEmitter> _logger;

    public ContextEventEmitter(ILogger<ContextEventEmitter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _meter = new Meter(MeterName, "1.0.0");

        _toolCallStarted = _meter.CreateCounter<long>(ToolCallStartedCounter, unit: "{event}",
            description: "context.tool_call_started — chat agent invoked a registered tool (deterministic IDs only).");
        _toolCallCompleted = _meter.CreateCounter<long>(ToolCallCompletedCounter, unit: "{event}",
            description: "context.tool_call_completed — tool invocation returned (deterministic IDs + outcome + durationMs).");
        _knowledgeRetrieved = _meter.CreateCounter<long>(KnowledgeRetrievedCounter, unit: "{event}",
            description: "context.knowledge_retrieved — knowledge retrieval produced one result (deterministic source ID + scores).");
        _playbookNodeExecuting = _meter.CreateCounter<long>(PlaybookNodeExecutingCounter, unit: "{event}",
            description: "context.playbook_node_executing — playbook node started (wrapper-level; NFR-08 binding).");
        _playbookNodeCompleted = _meter.CreateCounter<long>(PlaybookNodeCompletedCounter, unit: "{event}",
            description: "context.playbook_node_completed — playbook node finished (wrapper-level; NFR-08 binding).");
        _decisionMade = _meter.CreateCounter<long>(DecisionMadeCounter, unit: "{event}",
            description: "context.decision_made — capability router enumerated decision (deterministic identifiers only).");

        // chat-routing-redesign-r1 task 074 — Upload-pipeline counters (ADR-015 Tier 1 SAFE).
        _uploadStarted = _meter.CreateCounter<long>(UploadStartedCounter, unit: "{event}",
            description: "context.upload_started — upload pipeline started (sessionId + fileId + contentType + fileSizeBytes).");
        _uploadClassified = _meter.CreateCounter<long>(UploadClassifiedCounter, unit: "{event}",
            description: "context.upload_classified — classification step completed (enum label + confidence + durationMs).");
        _uploadSummarized = _meter.CreateCounter<long>(UploadSummarizedCounter, unit: "{event}",
            description: "context.upload_summarized — summarization step completed (summaryCharCount LENGTH ONLY + durationMs).");
        _uploadManifestExtracted = _meter.CreateCounter<long>(UploadManifestExtractedCounter, unit: "{event}",
            description: "context.upload_manifest_extracted — manifest extraction completed (sectionCount + tableCount + pageCount + durationMs).");
        _uploadIndexed = _meter.CreateCounter<long>(UploadIndexedCounter, unit: "{event}",
            description: "context.upload_indexed — RAG indexing completed (chunkCount + durationMs).");
        _uploadPersisted = _meter.CreateCounter<long>(UploadPersistedCounter, unit: "{event}",
            description: "context.upload_persisted — uploaded-files manifest write-through completed (durationMs).");
        _uploadCompleted = _meter.CreateCounter<long>(UploadCompletedCounter, unit: "{event}",
            description: "context.upload_completed — upload pipeline end-of-pipeline marker (totalDurationMs).");
    }

    /// <inheritdoc />
    public void ToolCallStarted(string toolName, Guid decisionId, Guid? sessionId, string? tenantId)
    {
        // ADR-015: deterministic IDs only — no args payload, no user text.
        var tags = new TagList
        {
            { "toolName", toolName ?? string.Empty },
            { "decisionId", decisionId.ToString("N") },
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _toolCallStarted.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.tool_call_started] toolName={ToolName} decisionId={DecisionId} sessionId={SessionId} tenantId={TenantId} timestamp={Timestamp:o}",
            toolName, decisionId.ToString("N"), sessionId?.ToString("N"), tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void ToolCallCompleted(string toolName, Guid decisionId, Guid? sessionId, string? tenantId, string outcome, long durationMs)
    {
        // ADR-015: deterministic IDs + enum-like outcome + numeric duration only.
        var tags = new TagList
        {
            { "toolName", toolName ?? string.Empty },
            { "decisionId", decisionId.ToString("N") },
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
            { "outcome", outcome ?? string.Empty },
        };
        _toolCallCompleted.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.tool_call_completed] toolName={ToolName} decisionId={DecisionId} sessionId={SessionId} tenantId={TenantId} outcome={Outcome} durationMs={DurationMs} timestamp={Timestamp:o}",
            toolName, decisionId.ToString("N"), sessionId?.ToString("N"), tenantId, outcome, durationMs, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void KnowledgeRetrieved(string knowledgeSourceId, double relevanceScore, int resultCount, Guid? sessionId, string? tenantId)
    {
        // ADR-015: deterministic source IDs + numeric metrics only — never chunk text.
        var tags = new TagList
        {
            { "knowledgeSourceId", knowledgeSourceId ?? string.Empty },
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _knowledgeRetrieved.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.knowledge_retrieved] knowledgeSourceId={KnowledgeSourceId} relevanceScore={RelevanceScore:F4} resultCount={ResultCount} sessionId={SessionId} tenantId={TenantId} timestamp={Timestamp:o}",
            knowledgeSourceId, relevanceScore, resultCount, sessionId?.ToString("N"), tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void PlaybookNodeExecuting(Guid playbookId, Guid nodeId, string nodeType, Guid? sessionId, string? tenantId)
    {
        // ADR-015: deterministic GUIDs + enum-like nodeType only.
        var tags = new TagList
        {
            { "playbookId", playbookId.ToString("N") },
            { "nodeId", nodeId.ToString("N") },
            { "nodeType", nodeType ?? string.Empty },
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _playbookNodeExecuting.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.playbook_node_executing] playbookId={PlaybookId} nodeId={NodeId} nodeType={NodeType} sessionId={SessionId} tenantId={TenantId} timestamp={Timestamp:o}",
            playbookId.ToString("N"), nodeId.ToString("N"), nodeType, sessionId?.ToString("N"), tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void PlaybookNodeCompleted(Guid playbookId, Guid nodeId, string decision, long durationMs, Guid? sessionId, string? tenantId)
    {
        // ADR-015: deterministic GUIDs + enum-like decision + numeric duration only.
        var tags = new TagList
        {
            { "playbookId", playbookId.ToString("N") },
            { "nodeId", nodeId.ToString("N") },
            { "decision", decision ?? string.Empty },
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _playbookNodeCompleted.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.playbook_node_completed] playbookId={PlaybookId} nodeId={NodeId} decision={Decision} durationMs={DurationMs} sessionId={SessionId} tenantId={TenantId} timestamp={Timestamp:o}",
            playbookId.ToString("N"), nodeId.ToString("N"), decision, durationMs, sessionId?.ToString("N"), tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void DecisionMade(string layer, string decision, string? capabilityName, Guid? sessionId, string? tenantId)
    {
        // ADR-015: enum-like layer + decision + capability NAME (config identifier, Tier 1 safe) only.
        var tags = new TagList
        {
            { "layer", layer ?? string.Empty },
            { "decision", decision ?? string.Empty },
            { "capabilityName", capabilityName ?? string.Empty },
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _decisionMade.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.decision_made] layer={Layer} decision={Decision} capabilityName={CapabilityName} sessionId={SessionId} tenantId={TenantId} timestamp={Timestamp:o}",
            layer, decision, capabilityName, sessionId?.ToString("N"), tenantId, DateTimeOffset.UtcNow);
    }

    // =========================================================================
    // chat-routing-redesign-r1 task 074 — Upload-pipeline emissions (ADR-015 Tier 1 SAFE).
    // =========================================================================

    /// <inheritdoc />
    public void UploadStarted(Guid? sessionId, string fileId, string contentType, long fileSizeBytes, string? tenantId)
    {
        // ADR-015: deterministic IDs + enum-like contentType + numeric fileSizeBytes only.
        var tags = new TagList
        {
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "fileId", fileId ?? string.Empty },
            { "contentType", contentType ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _uploadStarted.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.upload_started] sessionId={SessionId} fileId={FileId} contentType={ContentType} fileSizeBytes={FileSizeBytes} tenantId={TenantId} timestamp={Timestamp:o}",
            sessionId?.ToString("N"), fileId, contentType, fileSizeBytes, tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void UploadClassified(Guid? sessionId, string fileId, string documentType, double confidence, long durationMs, string? tenantId)
    {
        // ADR-015: deterministic IDs + enum-like documentType label + numeric metrics only.
        // documentType MUST be a short enum string ("NDA", "Contract", "Memo"); freeform
        // classifier text is forbidden per interface contract.
        var tags = new TagList
        {
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "fileId", fileId ?? string.Empty },
            { "documentType", documentType ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _uploadClassified.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.upload_classified] sessionId={SessionId} fileId={FileId} documentType={DocumentType} confidence={Confidence:F4} durationMs={DurationMs} tenantId={TenantId} timestamp={Timestamp:o}",
            sessionId?.ToString("N"), fileId, documentType, confidence, durationMs, tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void UploadSummarized(Guid? sessionId, string fileId, int summaryCharCount, long durationMs, string? tenantId)
    {
        // ADR-015 CRITICAL: summaryCharCount is the LENGTH ONLY — NEVER the summary text.
        var tags = new TagList
        {
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "fileId", fileId ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _uploadSummarized.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.upload_summarized] sessionId={SessionId} fileId={FileId} summaryCharCount={SummaryCharCount} durationMs={DurationMs} tenantId={TenantId} timestamp={Timestamp:o}",
            sessionId?.ToString("N"), fileId, summaryCharCount, durationMs, tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void UploadManifestExtracted(Guid? sessionId, string fileId, int sectionCount, int tableCount, int pageCount, long durationMs, string? tenantId)
    {
        // ADR-015: deterministic IDs + numeric counts only — never section names / table content / page text.
        var tags = new TagList
        {
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "fileId", fileId ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _uploadManifestExtracted.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.upload_manifest_extracted] sessionId={SessionId} fileId={FileId} sectionCount={SectionCount} tableCount={TableCount} pageCount={PageCount} durationMs={DurationMs} tenantId={TenantId} timestamp={Timestamp:o}",
            sessionId?.ToString("N"), fileId, sectionCount, tableCount, pageCount, durationMs, tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void UploadIndexed(Guid? sessionId, string fileId, int chunkCount, long durationMs, string? tenantId)
    {
        // ADR-015: deterministic IDs + numeric chunkCount + durationMs only — never chunk text.
        var tags = new TagList
        {
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "fileId", fileId ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _uploadIndexed.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.upload_indexed] sessionId={SessionId} fileId={FileId} chunkCount={ChunkCount} durationMs={DurationMs} tenantId={TenantId} timestamp={Timestamp:o}",
            sessionId?.ToString("N"), fileId, chunkCount, durationMs, tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void UploadPersisted(Guid? sessionId, string fileId, long durationMs, string? tenantId)
    {
        // ADR-015: deterministic IDs + numeric duration only.
        var tags = new TagList
        {
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "fileId", fileId ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _uploadPersisted.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.upload_persisted] sessionId={SessionId} fileId={FileId} durationMs={DurationMs} tenantId={TenantId} timestamp={Timestamp:o}",
            sessionId?.ToString("N"), fileId, durationMs, tenantId, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void UploadCompleted(Guid? sessionId, string fileId, long totalDurationMs, string? tenantId)
    {
        // ADR-015: deterministic IDs + numeric total duration only.
        var tags = new TagList
        {
            { "sessionId", sessionId?.ToString("N") ?? string.Empty },
            { "fileId", fileId ?? string.Empty },
            { "tenantId", tenantId ?? string.Empty },
        };
        _uploadCompleted.Add(1, tags);

        _logger.LogInformation(
            "[ADR-015][context.upload_completed] sessionId={SessionId} fileId={FileId} totalDurationMs={TotalDurationMs} tenantId={TenantId} timestamp={Timestamp:o}",
            sessionId?.ToString("N"), fileId, totalDurationMs, tenantId, DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
