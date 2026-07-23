using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler exposing the AI-INITIATED, SILENT, provenance-tagged
/// <c>memory.write(scope, factType, key, value, subjectType?, subjectId?, confidence?)</c> tool
/// (spec FR-B-08 / FR-30, task 057). The assistant invokes it AUTOMATICALLY to capture salient
/// facts as a normal turn side effect — automatic memory is the value proposition (operator ruling
/// 2026-07-08: an explicit-only / write-gated floor was ruled over-engineering for this stage).
/// </summary>
/// <remarks>
/// <para>
/// <b>Silent, but NOT a gate bypass (task-044 anti-shim precedent)</b>: the <c>memory.write</c> seed
/// row declares <c>sprk_sideeffectclass = Write</c>, so
/// <see cref="Chat.SprkChatAgentFactory"/> wraps this tool in the ONE confirmation gate
/// (<see cref="Chat.SideEffectGateAIFunction"/>;
/// <see cref="Chat.PendingPlanManager.RequiresConfirmation(ToolSideEffectClass?, BindingRisk, bool)"/>
/// is true for <see cref="ToolSideEffectClass.Write"/>). The deterministic
/// <see cref="Chat.Gate.ConfirmationPolicyEngine"/> then reads the row's declared
/// <c>riskProfile</c> (tier 1 + reversible, no record-of-truth / external / deadline /
/// confidentiality impact) and resolves the invocation to <see cref="Chat.Gate.GateRiskTier"/>
/// Tier 1 ⇒ <c>Execute</c> — SILENT inline execution, no confirmation dialog. Declaring Read/Pure to
/// dodge the gate would be the ADR-039 gate BYPASS the task forbids; silence must be the ENGINE's
/// decision from catalog DATA. Proven end-to-end by <c>MemoryWriteCaptureRecallEvalTests</c>.
/// </para>
/// <para>
/// <b>Provenance envelope is METADATA, not a gate (FR-B-08)</b>: every write records
/// <see cref="MemoryItem.Source"/> = <c>ai-derived</c> (this is AI-initiated capture),
/// <see cref="MemoryItem.BindingId"/> = <c>loop</c> (the reserved loop binding the gate ledger uses
/// for loop-executed tools), <see cref="MemoryItem.SessionId"/>, and a carried-but-inert
/// <see cref="MemoryItem.TrustLevel"/>. These DESCRIBE the item; they never block the write. The
/// FR-B-03 user review/delete surface (task 052) is the control, and provenance is surfaced on read
/// (<see cref="IMemoryItemStore.GetForRecordAsync"/> / <see cref="IMemoryItemStore.GetForUserAsync"/>
/// return the full envelope).
/// </para>
/// <para>
/// <b>DEFERRED (documented boundary — NOT implemented here)</b>: the untrusted-origin ban, the
/// semantic-retrieval trust boundary, poisoning evals, and <see cref="MemoryItem.TrustLevel"/>
/// ENFORCEMENT belong to a separate governance project. The only residual runtime control at this
/// stage is Policy v2 overlay 1 (a content-safety / injection-suspect signal on the turn still forces
/// a dialog before this write); the hard "untrusted content can never originate a memory write" rule
/// is deferred.
/// </para>
/// <para>
/// <b>Two scopes (task 050)</b>: <c>scope = record</c> keys the item generically to
/// <c>(subjectType, subjectId)</c> = <c>(entityType, entityId)</c> — defaulting to the record in the
/// current workspace context (<c>subjectType = "matter"</c>, <c>subjectId</c> = host
/// <see cref="ChatInvocationContext.MatterId"/>) when the LLM omits them; <c>scope = user</c> keys to
/// the authenticated principal's <see cref="ChatInvocationContext.UserId"/> (Dataverse
/// <c>systemuserid</c>) — NEVER an LLM-supplied value. Persistence goes through the generalized
/// <see cref="IMemoryItemStore"/> (store-before-render; upsert-by-(Type,Key)-per-subject supersession
/// so a repeated capture for the same key UPDATES the fact rather than accumulating duplicates).
/// </para>
/// <para>
/// <b>ADR compliance</b>: ADR-010 (auto-discovered via assembly scan; ZERO manual DI line);
/// ADR-013 (injects <see cref="IMemoryItemStore"/> directly — AI-internal memory plumbing, no
/// PublicContracts facade, same rationale as <see cref="ManagePinnedContextHandler"/>); ADR-014
/// (TenantId required, forwarded into the subject-partitioned store); ADR-015 (telemetry emits handler
/// + decision + scope + factType + deterministic ids only — NEVER the fact key/value body); ADR-039
/// (gate keys on the declared class, silent-execute is catalog DATA); ADR-040 (the durable store, not
/// a parallel session cache).
/// </para>
/// </remarks>
public sealed class MemoryWriteHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(MemoryWriteHandler);

    /// <summary>Provenance binding id recorded on every AI-initiated capture — the reserved loop binding the gate ledger uses for loop-executed tools (ADR-040).</summary>
    internal const string LoopBindingId = "loop";

    /// <summary>Scope discriminator wire value: generic (entityType, entityId) record memory.</summary>
    internal const string ScopeRecord = "record";

    /// <summary>Scope discriminator wire value: per-user memory.</summary>
    internal const string ScopeUser = "user";

    /// <summary>Default record subjectType applied when the LLM omits subjectType/subjectId (falls back to the host matter context).</summary>
    internal const string DefaultRecordSubjectType = "matter";

    /// <summary>Structured payload status discriminator returned on a successful capture.</summary>
    internal const string StatusCaptured = "captured";

    /// <summary>Closed set of accepted scope wire values.</summary>
    internal static readonly HashSet<string> SupportedScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        ScopeRecord,
        ScopeUser,
    };

    /// <summary>Closed map of factType wire values to <see cref="MemoryFactType"/> (camelCase wire, case-insensitive).</summary>
    internal static readonly IReadOnlyDictionary<string, MemoryFactType> SupportedFactTypes =
        new Dictionary<string, MemoryFactType>(StringComparer.OrdinalIgnoreCase)
        {
            ["party"] = MemoryFactType.Party,
            ["keyDate"] = MemoryFactType.KeyDate,
            ["priorAnalysis"] = MemoryFactType.PriorAnalysis,
            ["keyFact"] = MemoryFactType.KeyFact,
        };

    // ── Telemetry (FR-B-08) — one counter; ADR-015 dimensions are deterministic ids +
    //    enum-like discriminators ONLY. NEVER the fact key/value body. ──────────────────
    internal const string MeterName = "Sprk.Bff.Api.Memory";
    private static readonly Meter _meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> _memoryWrittenCounter = _meter.CreateCounter<long>(
        name: "memory.item_written",
        unit: "{item}",
        description: "Number of memory items captured via memory.write (AI-initiated, FR-B-08).");

    private readonly IMemoryItemStore _memoryItemStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MemoryWriteHandler> _logger;

    public MemoryWriteHandler(
        IMemoryItemStore memoryItemStore,
        TimeProvider timeProvider,
        ILogger<MemoryWriteHandler> logger)
    {
        _memoryItemStore = memoryItemStore ?? throw new ArgumentNullException(nameof(memoryItemStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Memory Write",
        // FR-A-01 (AIR2-020): byte-exact mirror of the authored sprk_description in
        // infra/dataverse/sprk_analysistool-memory-write-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: "Records a durable memory item for the current user or record scope so later turns can recall it. " +
                     "Memory writes are AI-initiated and silent; the user review/delete surface is the control. " +
                     "Tag every item with provenance (source, bindingId/ledgerRef, trustLevel) at capture time.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "scope",
                "Closed enum: 'record' | 'user'. 'record' stores the fact against the current record " +
                "(matter/project/invoice/etc.); 'user' stores it against the current user's general memory.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "subjectType",
                "Record scope only: the entity TYPE (e.g. 'matter', 'project'). Omit to default to the record " +
                "in the current workspace context. Ignored for user scope.",
                ToolParameterType.String,
                Required: false),
            new ToolParameterDefinition(
                "subjectId",
                "Record scope only: the entity ID. Omit to default to the record in the current workspace context. " +
                "Never invent an id — omit it to use the record in context. Ignored for user scope.",
                ToolParameterType.String,
                Required: false),
            new ToolParameterDefinition(
                "factType",
                "Closed enum: 'party' | 'keyDate' | 'priorAnalysis' | 'keyFact'.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "key",
                "Short human-readable label for the fact (e.g. 'Plaintiff'). A repeated capture with the same " +
                "(factType, key) for the same subject UPDATES the existing fact rather than duplicating it.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "value",
                "The fact content (e.g. 'Acme Corp'). Store the derived conclusion, never a raw Dataverse-field mirror.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "confidence",
                "Optional confidence in [0,1] (default 1.0).",
                ToolParameterType.Decimal,
                Required: false),
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "MemoryWriteHandler is chat-context-only. Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (string.IsNullOrWhiteSpace(context.ToolArgumentsJson))
            return ToolValidationResult.Failure("Tool arguments JSON is required for chat invocation.");

        if (!TryParseArgs(context.ToolArgumentsJson, out _, out var parseError))
            return ToolValidationResult.Failure(parseError ?? "Tool arguments could not be parsed.");

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "MemoryWriteHandler is chat-context-only. Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = _timeProvider.GetUtcNow(), CompletedAt = _timeProvider.GetUtcNow() }));

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        var correlationLogId = $"session={context.ChatSessionId},decision={context.DecisionId}";

        if (string.IsNullOrWhiteSpace(context.TenantId))
        {
            return Fail(tool, startedAt, "TenantId is required.", ToolErrorCodes.ValidationFailed);
        }

        if (!TryParseArgs(context.ToolArgumentsJson, out var args, out var parseError))
        {
            _logger.LogWarning("MemoryWriteHandler ({Correlation}) argument parse failed: {Error}", correlationLogId, parseError);
            return Fail(tool, startedAt, parseError ?? "Tool arguments could not be parsed.", ToolErrorCodes.ValidationFailed);
        }

        // Resolve subject keying from scope + context (never from LLM-supplied identity for user scope).
        string? subjectType = null;
        string? subjectId = null;
        string? userId = null;

        if (string.Equals(args.Scope, ScopeRecord, StringComparison.OrdinalIgnoreCase))
        {
            subjectType = string.IsNullOrWhiteSpace(args.SubjectType)
                ? (context.MatterId is { } m ? DefaultRecordSubjectType : null)
                : args.SubjectType;
            subjectId = string.IsNullOrWhiteSpace(args.SubjectId)
                ? context.MatterId?.ToString()
                : args.SubjectId;

            if (string.IsNullOrWhiteSpace(subjectType) || string.IsNullOrWhiteSpace(subjectId))
            {
                return Fail(tool, startedAt,
                    "Record-scope memory needs a subject: supply subjectType + subjectId, or invoke within a record workspace " +
                    "so the record in context can be used. Nothing was captured.",
                    ToolErrorCodes.ValidationFailed);
            }
        }
        else
        {
            // User scope: the subject is the authenticated principal — NEVER an LLM-supplied value (ADR-028 systemuserid).
            userId = context.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Fail(tool, startedAt,
                    "User-scope memory needs an authenticated user. Authenticate the session before capturing user memory. " +
                    "Nothing was captured.",
                    ToolErrorCodes.ValidationFailed);
            }
        }

        var fact = new MemoryFact
        {
            Type = args.FactType,
            Key = args.Key,
            Value = args.Value,
            Source = MemoryOrigin.AiDerived,   // AI-initiated capture
            ConfirmedByUser = false,           // unconfirmed until the user reviews (FR-B-03)
            Confidence = args.Confidence,
            RecordedAt = startedAt,
        };

        var item = new MemoryItem
        {
            Version = MemoryItemContract.SchemaVersion,
            Scope = string.Equals(args.Scope, ScopeRecord, StringComparison.OrdinalIgnoreCase) ? MemoryScope.Record : MemoryScope.User,
            SubjectType = subjectType,
            SubjectId = subjectId,
            UserId = userId,
            Fact = fact,
            // ── Provenance envelope recorded as METADATA at capture time (FR-B-08). Never a gate. ──
            Source = MemoryOrigin.AiDerived,
            BindingId = LoopBindingId,
            SessionId = context.ChatSessionId.ToString(),
            // LedgerRef / TurnId: the gate writes the loop@t{n} ledger output AFTER this handler returns,
            // so the turn index is not knowable here; bindingId='loop' carries the "which action" provenance.
            LedgerRef = null,
            TurnId = null,
            // TrustLevel is DELIBERATELY left unset: it is carried, not acted on — enforcement is DEFERRED
            // to the separate governance project (FR-B-08). Setting/reading it here would imply enforcement.
            TrustLevel = null,
            CreatedBy = context.UserId,
            CreatedAt = startedAt,
            DeletionPolicy = "user-erasable",
            RetentionClass = "tier-3-user-owned",
        };

        MemoryItem persisted;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Store-before-render (ADR-040): the durable capture lands in the subject-partitioned store,
            // with upsert-by-(Type,Key) supersession, before the gate composes its outcome card.
            persisted = await _memoryItemStore.UpsertAsync(item, context.TenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Fail(tool, startedAt, "Memory capture was cancelled.", ToolErrorCodes.Cancelled);
        }
        catch (ArgumentException ex)
        {
            // The store rejects a record-scope fact that merely mirrors a live Dataverse field (FR-B-01
            // DataverseFieldMirrorGuard), or invalid subject keying. Relay honestly; never fabricate success.
            _logger.LogWarning("MemoryWriteHandler ({Correlation}) rejected by store: {ErrorType}", correlationLogId, ex.GetType().Name);
            return Fail(tool, startedAt,
                "That memory could not be captured: " + ex.Message + " Nothing was stored.",
                ToolErrorCodes.ValidationFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MemoryWriteHandler ({Correlation}) failed: {ErrorType}", correlationLogId, ex.GetType().Name);
            return Fail(tool, startedAt, "Memory capture failed.", ToolErrorCodes.InternalError);
        }

        // ADR-015: deterministic dimensions only — scope + factType + ids. NEVER the fact key/value.
        _memoryWrittenCounter.Add(1,
            new KeyValuePair<string, object?>("tenantId", context.TenantId),
            new KeyValuePair<string, object?>("scope", persisted.Scope),
            new KeyValuePair<string, object?>("factType", persisted.Fact.Type.ToString()),
            new KeyValuePair<string, object?>("source", persisted.Source));

        var superseded = persisted.UpdatedAt is not null;

        _logger.LogInformation(
            "MemoryWriteHandler ({Correlation}) captured itemId={ItemId} scope={Scope} factType={FactType} superseded={Superseded}",
            correlationLogId, persisted.Id, persisted.Scope, persisted.Fact.Type, superseded);

        var payload = new MemoryWritePayload
        {
            Tool = tool.Name,
            Status = StatusCaptured,
            Scope = persisted.Scope,
            SubjectType = persisted.SubjectType,
            SubjectId = persisted.SubjectId,
            UserId = persisted.UserId,
            FactType = args.FactWire,
            Key = persisted.Fact.Key,
            ItemId = persisted.Id,
            Source = persisted.Source,
            Superseded = superseded,
        };

        var scopeLabel = string.Equals(persisted.Scope, MemoryScope.User, StringComparison.Ordinal) ? "your memory" : "this record";
        var userSummary = superseded
            ? $"Updated a remembered fact ({persisted.Fact.Key}) in {scopeLabel}."
            : $"Remembered a fact ({persisted.Fact.Key}) in {scopeLabel}.";

        var result = ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: payload,
            summary: userSummary,
            confidence: 1.0,
            execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow(), ModelCalls = 0 });

        return result with
        {
            Metadata = new Dictionary<string, object?> { [ToolResultMetadataKeys.UserSummary] = userSummary },
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private ToolResult Fail(AnalysisTool tool, DateTimeOffset startedAt, string message, string errorCode) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name, message, errorCode,
            new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });

    private static bool TryParseArgs(string? toolArgumentsJson, out ParsedArgs args, out string? error)
    {
        args = default;
        error = null;

        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
        {
            error = "Tool arguments JSON is required.";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(toolArgumentsJson);
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must be a JSON object.";
                return false;
            }

            if (!TryGetNonEmptyString(root, "scope", out var scope))
            {
                error = "Tool arguments must include a non-empty 'scope' string field ('record' | 'user').";
                return false;
            }
            if (!SupportedScopes.Contains(scope))
            {
                error = $"'scope' must be one of: {string.Join(", ", SupportedScopes)}. Received: '{scope}'.";
                return false;
            }

            if (!TryGetNonEmptyString(root, "factType", out var factWire))
            {
                error = "Tool arguments must include a non-empty 'factType' string field.";
                return false;
            }
            if (!SupportedFactTypes.TryGetValue(factWire, out var factType))
            {
                error = $"'factType' must be one of: {string.Join(", ", SupportedFactTypes.Keys)}. Received: '{factWire}'.";
                return false;
            }

            if (!TryGetNonEmptyString(root, "key", out var key))
            {
                error = "Tool arguments must include a non-empty 'key' string field.";
                return false;
            }

            if (!TryGetNonEmptyString(root, "value", out var value))
            {
                error = "Tool arguments must include a non-empty 'value' string field.";
                return false;
            }

            string? subjectType = TryGetNonEmptyString(root, "subjectType", out var st) ? st : null;
            string? subjectId = TryGetNonEmptyString(root, "subjectId", out var sid) ? sid : null;

            var confidence = 1.0;
            if (root.TryGetProperty("confidence", out var confEl) && confEl.ValueKind == JsonValueKind.Number
                && confEl.TryGetDouble(out var c))
            {
                confidence = Math.Clamp(c, 0.0, 1.0);
            }

            args = new ParsedArgs
            {
                Scope = scope,
                SubjectType = subjectType,
                SubjectId = subjectId,
                FactWire = factWire,
                FactType = factType,
                Key = key,
                Value = value,
                Confidence = confidence,
            };
            return true;
        }
    }

    private static bool TryGetNonEmptyString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String &&
            el.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
        {
            value = s;
            return true;
        }
        return false;
    }

    private readonly record struct ParsedArgs
    {
        public string Scope { get; init; }
        public string? SubjectType { get; init; }
        public string? SubjectId { get; init; }
        public string FactWire { get; init; }
        public MemoryFactType FactType { get; init; }
        public string Key { get; init; }
        public string Value { get; init; }
        public double Confidence { get; init; }
    }

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. ADR-015: carries the fact
    /// KEY (a short identifying label, re-readable by the LLM) + deterministic ids + provenance origin,
    /// but never the fact VALUE body in a telemetry-bound field.
    /// </summary>
    public sealed class MemoryWritePayload
    {
        [JsonPropertyName("tool")]
        public required string Tool { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("scope")]
        public required string Scope { get; init; }

        [JsonPropertyName("subjectType")]
        public string? SubjectType { get; init; }

        [JsonPropertyName("subjectId")]
        public string? SubjectId { get; init; }

        [JsonPropertyName("userId")]
        public string? UserId { get; init; }

        [JsonPropertyName("factType")]
        public required string FactType { get; init; }

        [JsonPropertyName("key")]
        public required string Key { get; init; }

        [JsonPropertyName("itemId")]
        public string? ItemId { get; init; }

        [JsonPropertyName("source")]
        public required string Source { get; init; }

        [JsonPropertyName("superseded")]
        public bool Superseded { get; init; }
    }
}
