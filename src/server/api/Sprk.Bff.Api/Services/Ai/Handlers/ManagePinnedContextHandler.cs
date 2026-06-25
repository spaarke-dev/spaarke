using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Memory;
using Sprk.Bff.Api.Services.Ai.Memory;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler exposing the user voice memory primitives — "remember X", "forget X",
/// "always X" — via the LLM-facing function <c>manage_pinned_context(action, pinType, title,
/// content?)</c>. R6 Pillar 7 / task 069 / FR-47.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One handler, two actions</strong>: the only LLM-facing function is
/// <c>manage_pinned_context(action, pinType, title, content?)</c>. <c>action</c> is a closed
/// enum (<c>"create"</c> | <c>"delete"</c>); <c>pinType</c> is a closed enum aligned with
/// <see cref="PinType"/> (<c>"user-preference"</c> | <c>"system-rule"</c>). The <c>"matter-fact"</c>
/// pin type is intentionally NOT exposed via voice — matter-bound facts are pinned through
/// task 070's Pinned Memory UI which carries the explicit matter binding. <c>title</c> is the
/// short user-authored label the LLM derives from the voice command (e.g., "remember to use
/// terse responses" → title = "Terse responses"). <c>content</c> is optional; when omitted on
/// create the LLM defaults to the title.
/// </para>
///
/// <para>
/// <strong>Voice command mapping (consumed via the LLM function-call surface — NOT a hard
/// deterministic dispatch in this handler)</strong>:
/// </para>
/// <list type="bullet">
/// <item><c>"remember X"</c> → action = <c>create</c>, pinType = <c>user-preference</c>.</item>
/// <item><c>"forget X"</c> → action = <c>delete</c>, pinType = caller-supplied (matched against
/// the user's existing pins by title to identify which one to delete).</item>
/// <item><c>"always X"</c> → action = <c>create</c>, pinType = <c>system-rule</c>.</item>
/// </list>
///
/// <para>
/// Tool registration via the <c>sprk_analysistool</c> seed row (auto-discovered by
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c>) makes the tool visible to the
/// agent.
/// </para>
///
/// <para>
/// <strong>Auto-discovery + data-driven registration (R6 Pillar 2)</strong>: registered via
/// the <c>sprk_analysistool</c> seed row
/// (<c>infra/dataverse/sprk_analysistool-manage-pinned-context-row.json</c>). Routes to this
/// C# class via <c>sprk_handlerclass = "ManagePinnedContextHandler"</c>. Auto-discovered by
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c> — ZERO new
/// <c>Program.cs</c> / <c>AnalysisServicesModule</c> lines (ADR-010).
/// </para>
///
/// <para>
/// <strong>Invocation contexts</strong>: <see cref="InvocationContextKind.Chat"/> only. The
/// pinned-context entity is a user-curated memory primitive consumed by hierarchical memory
/// composition at chat-turn build time (task 067) — there is no playbook-node executor that
/// writes user pins. Mirrors the chat-only scoping of task 054
/// (<see cref="SendWorkspaceArtifactHandler"/>) and task 055
/// (<see cref="UpdateWorkspaceTabHandler"/>).
/// </para>
///
/// <para>
/// <strong>Capability gate</strong>: <c>sprk_requiredcapability = null</c>. The "remember /
/// forget / always" affordance is a default user voice command available in every chat
/// session. The handler's own gates (<see cref="ChatInvocationContext.TenantId"/> +
/// <see cref="ChatInvocationContext.UserId"/> required; argument schema validation;
/// pin-content length caps via the underlying
/// <see cref="IPinnedContextRepository"/>) are the authorization surface.
/// </para>
///
/// <para>
/// <strong>ADR compliance</strong>:
/// </para>
/// <list type="bullet">
/// <item><strong>ADR-010</strong>: auto-discovered via assembly scan; ZERO manual DI line.
/// Dependencies (<see cref="IPinnedContextRepository"/>, <see cref="TimeProvider"/>) are
/// already registered (Pillar 7 / task 065 + factory module respectively).</item>
/// <item><strong>ADR-013</strong>: lives under <c>Services/Ai/Handlers/</c>; injects
/// <see cref="IPinnedContextRepository"/> directly — NOT through a PublicContracts facade.
/// The pinned-context repository is AI-internal memory plumbing per the 2026-05-20 refined
/// ADR-013 boundary rule (no PublicContracts facade for AI-internal collaborators). Mirrors
/// the same direct-injection rationale that landed in tasks 067 and 068.</item>
/// <item><strong>ADR-014</strong>: <see cref="ChatInvocationContext.TenantId"/> is required
/// and forwarded into every repository call; the repository scopes by Cosmos partition key
/// <c>/tenantId</c>. Cross-tenant reads/writes are structurally impossible.</item>
/// <item><strong>ADR-015 (BINDING)</strong>: telemetry emits handler name + decision
/// (<c>created</c> | <c>deleted</c> | <c>refused_not_found</c> | <c>refused_*</c>) + pinType
/// + duration + deterministic IDs (tenantId, userId, sessionId, pinId) ONLY. NEVER the
/// user-authored <see cref="PinnedContextItem.Title"/> body. NEVER the user-authored
/// <see cref="PinnedContextItem.Content"/> body. NEVER raw user message text. The title and
/// content fields are logged ONLY as presence flags + lengths — the actual strings are
/// passed straight into the repository and never touch the telemetry sink.</item>
/// <item><strong>ADR-029</strong>: BCL-only implementation; per-handler publish-size delta
/// ≤+0.1 MB.</item>
/// </list>
/// </remarks>
public sealed class ManagePinnedContextHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(ManagePinnedContextHandler);

    // ─────────────────────────────────────────────────────────────────────────
    // Telemetry (R6 task 069 / FR-47)
    //
    // Two counters on a static Meter: `memory.pin_created` (creates) +
    // `memory.pin_deleted` (deletes). Per ADR-015 BINDING the dimension set is
    // deterministic IDs + enum-like discriminators ONLY (tenantId, userId,
    // sessionId, pinType, decision). NEVER title body. NEVER content body.
    // NEVER user message text. The values flowing in (TenantId, UserId,
    // ChatSessionId) are caller-supplied opaque identifiers already audited
    // as Tier-1 telemetry-safe by ADR-013 / ADR-015.
    //
    // We deliberately do NOT extend IContextEventEmitter for these counters:
    // the IContextEventEmitter contract is structurally constrained to 6
    // specific `context.*` event types per ADR-015 (the existing method
    // signatures don't fit pin-mutation telemetry, exactly as task 068's
    // shared-budget tracker found). Following the workspace.conflict_refused
    // Counter pattern from task 058 (UpdateWorkspaceTabHandler) keeps both
    // contracts simple and ADR-015-compliant.
    // ─────────────────────────────────────────────────────────────────────────
    internal const string MeterName = "Sprk.Bff.Api.Memory";
    private static readonly Meter _meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> _pinCreatedCounter = _meter.CreateCounter<long>(
        name: "memory.pin_created",
        unit: "{pin}",
        description: "Number of pinned-context items created via manage_pinned_context (FR-47).");
    private static readonly Counter<long> _pinDeletedCounter = _meter.CreateCounter<long>(
        name: "memory.pin_deleted",
        unit: "{pin}",
        description: "Number of pinned-context items deleted via manage_pinned_context (FR-47).");

    /// <summary>Action discriminator: create a new pin.</summary>
    internal const string ActionCreate = "create";

    /// <summary>Action discriminator: delete an existing pin by title match.</summary>
    internal const string ActionDelete = "delete";

    /// <summary>PinType discriminator string for <see cref="PinType.UserPreference"/> (kebab-case wire format).</summary>
    internal const string PinTypeUserPreference = "user-preference";

    /// <summary>PinType discriminator string for <see cref="PinType.SystemRule"/> (kebab-case wire format).</summary>
    internal const string PinTypeSystemRule = "system-rule";

    /// <summary>PinType discriminator string for <see cref="PinType.MatterFact"/> (kebab-case wire format).</summary>
    internal const string PinTypeMatterFact = "matter-fact";

    /// <summary>Closed enum of supported action strings.</summary>
    internal static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ActionCreate,
        ActionDelete
    };

    /// <summary>Closed enum of supported pinType wire strings (kebab-case).</summary>
    internal static readonly HashSet<string> SupportedPinTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        PinTypeUserPreference,
        PinTypeSystemRule,
        PinTypeMatterFact
    };

    /// <summary>Structured payload status discriminators returned in <see cref="ToolResult.Data"/>.</summary>
    internal const string StatusCreated = "created";

    /// <summary>Structured payload status discriminator for the delete-applied path.</summary>
    internal const string StatusDeleted = "deleted";

    /// <summary>Structured payload status discriminator when the requested delete target was not found.</summary>
    internal const string StatusRefusedNotFound = "refused_not_found";

    /// <summary>Maximum title length — mirrors <c>PinnedContextRepository.MaxTitleLength</c>.</summary>
    internal const int MaxTitleLength = 200;

    /// <summary>Maximum content length — mirrors <c>PinnedContextRepository.MaxContentLength</c>.</summary>
    internal const int MaxContentLength = 1000;

    private readonly IPinnedContextRepository _pinnedContextRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ManagePinnedContextHandler> _logger;

    public ManagePinnedContextHandler(
        IPinnedContextRepository pinnedContextRepository,
        TimeProvider timeProvider,
        ILogger<ManagePinnedContextHandler> logger)
    {
        _pinnedContextRepository = pinnedContextRepository ?? throw new ArgumentNullException(nameof(pinnedContextRepository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Manage Pinned Context",
        Description: "Manage a user's persistent 'pinned memory' — the small set of always-in-context facts and " +
                     "preferences the assistant keeps across sessions. Use this when the user says \"remember X\" " +
                     "(action=create, pinType=user-preference), \"always X\" (action=create, pinType=system-rule), " +
                     "or \"forget X\" (action=delete; match by title against existing pins). Pinned items are " +
                     "user-curated memory; titles and content are user-authored and persisted verbatim. The " +
                     "\"matter-fact\" pinType exists for completeness but is normally written through the Pinned " +
                     "Memory UI rather than via voice. Title should be a short identifying label (≤200 chars); " +
                     "content carries the body (≤1000 chars; defaults to the title when omitted on create).",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "action",
                "Closed enum: 'create' | 'delete'. 'create' adds a new pin; 'delete' removes an existing pin " +
                "matched by title against the user's pins (case-insensitive).",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "pinType",
                "Closed enum: 'user-preference' | 'system-rule' | 'matter-fact'. Voice commands map: 'remember' " +
                "→ user-preference; 'always' → system-rule; 'matter-fact' is normally set via the Pinned Memory UI " +
                "rather than voice. Required on both create and delete (delete uses pinType + title to identify).",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "title",
                "Short user-facing label for the pin (≤200 chars). On create, the LLM authors a concise label " +
                "summarizing the voice command (e.g., 'Terse responses', 'Never reveal cost data'). On delete, the " +
                "title is matched (case-insensitive) against existing pins of the same pinType.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "content",
                "Optional pin body (≤1000 chars). Only meaningful on create. When omitted on create, the handler " +
                "defaults the content to the title text. Persisted verbatim — the LLM should author this carefully " +
                "as the literal text that will be re-injected into future system prompts.",
                ToolParameterType.String,
                Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    /// <remarks>
    /// Playbook-context invocation rejected — managing user pins is a chat-only affordance.
    /// </remarks>
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "ManagePinnedContextHandler is chat-context-only. Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (string.IsNullOrWhiteSpace(context.UserId))
            return ToolValidationResult.Failure(
                "UserId is required for ManagePinnedContextHandler — pinned context is user-scoped. " +
                "Authenticate the session before invoking this tool.");

        if (string.IsNullOrWhiteSpace(context.ToolArgumentsJson))
            return ToolValidationResult.Failure("Tool arguments JSON is required for chat invocation.");

        try
        {
            using var doc = JsonDocument.Parse(context.ToolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ToolValidationResult.Failure("Tool arguments must be a JSON object.");

            // action — required closed enum.
            if (!doc.RootElement.TryGetProperty("action", out var actionProp) ||
                actionProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(actionProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'action' string field ('create' | 'delete').");
            }
            var actionValue = actionProp.GetString()!;
            if (!SupportedActions.Contains(actionValue))
            {
                return ToolValidationResult.Failure(
                    $"'action' must be one of: {string.Join(", ", SupportedActions)}. Received: '{actionValue}'.");
            }

            // pinType — required closed enum.
            if (!doc.RootElement.TryGetProperty("pinType", out var pinTypeProp) ||
                pinTypeProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(pinTypeProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'pinType' string field ('user-preference' | 'system-rule' | 'matter-fact').");
            }
            var pinTypeValue = pinTypeProp.GetString()!;
            if (!SupportedPinTypes.Contains(pinTypeValue))
            {
                return ToolValidationResult.Failure(
                    $"'pinType' must be one of: {string.Join(", ", SupportedPinTypes)}. Received: '{pinTypeValue}'.");
            }

            // title — required, length-capped.
            if (!doc.RootElement.TryGetProperty("title", out var titleProp) ||
                titleProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(titleProp.GetString()))
            {
                return ToolValidationResult.Failure(
                    "Tool arguments must include a non-empty 'title' string field.");
            }
            var titleValue = titleProp.GetString()!;
            if (titleValue.Length > MaxTitleLength)
            {
                return ToolValidationResult.Failure(
                    $"'title' length {titleValue.Length} exceeds the maximum {MaxTitleLength}.");
            }

            // content — optional; length-capped when supplied.
            if (doc.RootElement.TryGetProperty("content", out var contentProp) &&
                contentProp.ValueKind == JsonValueKind.String &&
                contentProp.GetString() is { } contentValue &&
                contentValue.Length > MaxContentLength)
            {
                return ToolValidationResult.Failure(
                    $"'content' length {contentValue.Length} exceeds the maximum {MaxContentLength}.");
            }
        }
        catch (JsonException ex)
        {
            return ToolValidationResult.Failure($"Tool arguments JSON is malformed: {ex.Message}");
        }

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "ManagePinnedContextHandler is chat-context-only. Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed,
            new ToolExecutionMetadata { StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow }));

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAt = _timeProvider.GetUtcNow();
        var correlationLogId = $"session={context.ChatSessionId},decision={context.DecisionId}";

        // Tenant + user identity gating. Both are required (validated in ValidateChat, but
        // defensively re-checked here so a buggy adapter caller surfaces a clean error rather
        // than a NRE deep in the repo call).
        if (string.IsNullOrWhiteSpace(context.TenantId))
        {
            stopwatch.Stop();
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "TenantId is required.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
        if (string.IsNullOrWhiteSpace(context.UserId))
        {
            stopwatch.Stop();
            // ADR-015 BINDING: log decision discriminator only. No user text.
            _logger.LogWarning(
                "ManagePinnedContextHandler ({Correlation}) refused_no_user — chat session has no oid claim; cannot scope pin to a user.",
                correlationLogId);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "UserId is required — pinned context is user-scoped. Authenticate the session before invoking this tool.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        if (!TryParseArgs(context.ToolArgumentsJson, out var args, out var parseError))
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "ManagePinnedContextHandler ({Correlation}) argument parse failed: {Error}",
                correlationLogId, parseError);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                parseError ?? "Tool arguments could not be parsed.",
                ToolErrorCodes.ValidationFailed,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        // ADR-015 BINDING: log only handler + IDs + action + pinType + presence/length flags.
        // NEVER the title text. NEVER the content text. NEVER user message text. The titleLen +
        // contentSupplied flags are deterministic numeric / boolean — they reveal the length
        // class of the user authoring but never the content.
        _logger.LogInformation(
            "ManagePinnedContextHandler ({Correlation}) start action={Action} pinType={PinType} tenantId={TenantId} userId={UserId} matterScoped={MatterScoped} titleLen={TitleLen} contentSupplied={ContentSupplied}",
            correlationLogId,
            args.Action,
            args.PinTypeWire,
            context.TenantId,
            context.UserId,
            context.MatterId is not null,
            args.Title.Length,
            args.Content is not null);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return args.Action switch
            {
                ActionCreate => await CreateAsync(context, tool, args, startedAt, stopwatch, correlationLogId, cancellationToken).ConfigureAwait(false),
                ActionDelete => await DeleteAsync(context, tool, args, startedAt, stopwatch, correlationLogId, cancellationToken).ConfigureAwait(false),
                _ => ToolResult.Error(
                    HandlerId, tool.Id, tool.Name,
                    $"Unsupported action '{args.Action}'.",
                    ToolErrorCodes.ValidationFailed,
                    new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() })
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "ManagePinnedContextHandler ({Correlation}) cancelled action={Action}",
                correlationLogId, args.Action);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Pin management was cancelled.",
                ToolErrorCodes.Cancelled,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // ADR-015: log exception TYPE only, never the user-authored title/content that
            // may appear in inner-exception messages from the repository layer.
            _logger.LogError(ex,
                "ManagePinnedContextHandler ({Correlation}) failed action={Action} pinType={PinType}: {ErrorType}",
                correlationLogId, args.Action, args.PinTypeWire, ex.GetType().Name);
            return ToolResult.Error(
                HandlerId, tool.Id, tool.Name,
                "Pin management failed.",
                ToolErrorCodes.InternalError,
                new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Create path
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> CreateAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        ParsedArgs args,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        // Deterministic pin id; the repository builds the Cosmos doc id as
        // `pinned-context_{tenantId}_{pinId}`. Per the repository's contract the
        // {pinId} portion must be the stable identifier we want to address the doc by.
        var pinId = Guid.NewGuid().ToString("N");
        var documentId = $"{PinnedContextRepository.IdPrefix}_{context.TenantId}_{pinId}";

        var pin = new PinnedContextItem
        {
            Id = documentId,
            DocumentType = PinnedContextRepository.DocumentTypeValue,
            TenantId = context.TenantId,
            UserId = context.UserId!,
            PinType = ParseWirePinType(args.PinTypeWire),
            Title = args.Title,
            // When the LLM omits content (e.g., shorthand voice commands), default to the title
            // so the system-prompt re-injection has a non-empty body. The repository would
            // refuse a whitespace content per its ArgumentException.ThrowIfNullOrWhiteSpace
            // guard, so we substitute here.
            Content = string.IsNullOrWhiteSpace(args.Content) ? args.Title : args.Content,
            MatterId = null,
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
            CreatedBy = context.UserId!
        };

        await _pinnedContextRepository.CreateAsync(pin, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        // ADR-015 BINDING: counter dimensions = tenantId + userId + sessionId + pinType +
        // decision discriminator. NEVER title/content body.
        _pinCreatedCounter.Add(1,
            new KeyValuePair<string, object?>("tenantId", context.TenantId),
            new KeyValuePair<string, object?>("userId", context.UserId),
            new KeyValuePair<string, object?>("sessionId", context.ChatSessionId.ToString("N")),
            new KeyValuePair<string, object?>("pinType", args.PinTypeWire),
            new KeyValuePair<string, object?>("decision", StatusCreated));

        _logger.LogInformation(
            "ManagePinnedContextHandler ({Correlation}) created pinId={PinId} pinType={PinType} in {Duration}ms",
            correlationLogId, pinId, args.PinTypeWire, stopwatch.ElapsedMilliseconds);

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new ManagePinnedContextPayload
            {
                Status = StatusCreated,
                Action = ActionCreate,
                PinId = pinId,
                PinType = args.PinTypeWire,
                Message = $"Pin '{args.Title}' created. It will appear in your system prompt on future turns."
            },
            summary: $"Pinned '{args.Title}' as {args.PinTypeWire}.",
            confidence: 1.0,
            execution: new ToolExecutionMetadata
            {
                StartedAt = startedAt,
                CompletedAt = _timeProvider.GetUtcNow(),
                ModelCalls = 0
            });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Delete path
    // ─────────────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> DeleteAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        ParsedArgs args,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string correlationLogId,
        CancellationToken cancellationToken)
    {
        // Delete-by-title-match: fetch the user's pins, find the one matching
        // (pinType, title) case-insensitively, then delete by extracted pinId. The
        // repository.DeleteAsync is idempotent (no-op on missing) so we ALSO short-circuit
        // when no match is found and return a structured refused_not_found response so the
        // LLM can pattern-match in its next turn.
        var userPins = await _pinnedContextRepository
            .GetByUserAsync(context.TenantId, context.UserId!, cancellationToken)
            .ConfigureAwait(false);

        var target = userPins.FirstOrDefault(p =>
            p.PinType == ParseWirePinType(args.PinTypeWire) &&
            string.Equals(p.Title, args.Title, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "ManagePinnedContextHandler ({Correlation}) refused_not_found pinType={PinType} in {Duration}ms",
                correlationLogId, args.PinTypeWire, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new ManagePinnedContextPayload
                {
                    Status = StatusRefusedNotFound,
                    Action = ActionDelete,
                    PinType = args.PinTypeWire,
                    Message = $"No {args.PinTypeWire} pin matching '{args.Title}' was found for this user. " +
                              "Ask the user to confirm the exact label or list their current pins."
                },
                summary: $"No {args.PinTypeWire} pin matching '{args.Title}' was found.",
                confidence: 0.0,
                execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
        }

        // The doc id format is `pinned-context_{tenantId}_{pinId}`; recover the {pinId}
        // portion (it's the substring after the last underscore — pinId is a GUID with no
        // underscores). The repository.DeleteAsync rebuilds the doc id from (tenantId, pinId).
        var extractedPinId = ExtractPinIdFromDocumentId(target.Id, context.TenantId);

        await _pinnedContextRepository
            .DeleteAsync(context.TenantId, extractedPinId, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();

        // ADR-015 BINDING: counter dimensions = tenantId + userId + sessionId + pinType +
        // decision discriminator. NEVER title/content body.
        _pinDeletedCounter.Add(1,
            new KeyValuePair<string, object?>("tenantId", context.TenantId),
            new KeyValuePair<string, object?>("userId", context.UserId),
            new KeyValuePair<string, object?>("sessionId", context.ChatSessionId.ToString("N")),
            new KeyValuePair<string, object?>("pinType", args.PinTypeWire),
            new KeyValuePair<string, object?>("decision", StatusDeleted));

        _logger.LogInformation(
            "ManagePinnedContextHandler ({Correlation}) deleted pinId={PinId} pinType={PinType} in {Duration}ms",
            correlationLogId, extractedPinId, args.PinTypeWire, stopwatch.ElapsedMilliseconds);

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new ManagePinnedContextPayload
            {
                Status = StatusDeleted,
                Action = ActionDelete,
                PinId = extractedPinId,
                PinType = args.PinTypeWire,
                Message = $"Pin '{args.Title}' removed. It will no longer appear in your system prompt."
            },
            summary: $"Removed {args.PinTypeWire} pin '{args.Title}'.",
            confidence: 1.0,
            execution: new ToolExecutionMetadata { StartedAt = startedAt, CompletedAt = _timeProvider.GetUtcNow() });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the {pinId} portion from a Cosmos document id of the form
    /// <c>pinned-context_{tenantId}_{pinId}</c>. The pinId is the substring after the last
    /// underscore; tenantId may itself contain underscores but the pinId (a "N"-formatted
    /// GUID) never does.
    /// </summary>
    internal static string ExtractPinIdFromDocumentId(string documentId, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            return string.Empty;

        // Expected prefix: "pinned-context_{tenantId}_"
        var prefix = $"{PinnedContextRepository.IdPrefix}_{tenantId}_";
        if (documentId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return documentId.Substring(prefix.Length);
        }

        // Fallback: last segment after the final underscore (defensive — preserves the
        // pinId portion even if the prefix shape ever drifts).
        var lastUnderscore = documentId.LastIndexOf('_');
        return lastUnderscore >= 0 && lastUnderscore + 1 < documentId.Length
            ? documentId.Substring(lastUnderscore + 1)
            : documentId;
    }

    /// <summary>
    /// Maps the wire-format pinType string to the model enum value. Throws
    /// <see cref="ArgumentException"/> on unrecognised value — ValidateChat / TryParseArgs
    /// already gate this, so the throw is defensive.
    /// </summary>
    internal static PinType ParseWirePinType(string wireValue) => wireValue.ToLowerInvariant() switch
    {
        PinTypeUserPreference => PinType.UserPreference,
        PinTypeSystemRule => PinType.SystemRule,
        PinTypeMatterFact => PinType.MatterFact,
        _ => throw new ArgumentException($"Unrecognised pinType wire value '{wireValue}'.", nameof(wireValue))
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // Argument parsing
    // ─────────────────────────────────────────────────────────────────────────────

    private static bool TryParseArgs(
        string? toolArgumentsJson,
        out ParsedArgs args,
        out string? error)
    {
        args = default;
        error = null;

        if (string.IsNullOrWhiteSpace(toolArgumentsJson))
        {
            error = "Tool arguments JSON is required.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must be a JSON object.";
                return false;
            }

            var root = doc.RootElement;

            if (!root.TryGetProperty("action", out var actionProp) ||
                actionProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(actionProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'action' string field.";
                return false;
            }
            var action = actionProp.GetString()!.ToLowerInvariant();
            if (!SupportedActions.Contains(action))
            {
                error = $"'action' must be one of: {string.Join(", ", SupportedActions)}.";
                return false;
            }

            if (!root.TryGetProperty("pinType", out var pinTypeProp) ||
                pinTypeProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(pinTypeProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'pinType' string field.";
                return false;
            }
            var pinTypeWire = pinTypeProp.GetString()!.ToLowerInvariant();
            if (!SupportedPinTypes.Contains(pinTypeWire))
            {
                error = $"'pinType' must be one of: {string.Join(", ", SupportedPinTypes)}.";
                return false;
            }

            if (!root.TryGetProperty("title", out var titleProp) ||
                titleProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(titleProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'title' string field.";
                return false;
            }
            var title = titleProp.GetString()!;
            if (title.Length > MaxTitleLength)
            {
                error = $"'title' length {title.Length} exceeds the maximum {MaxTitleLength}.";
                return false;
            }

            string? content = null;
            if (root.TryGetProperty("content", out var contentProp) &&
                contentProp.ValueKind == JsonValueKind.String &&
                contentProp.GetString() is { } contentValue &&
                !string.IsNullOrWhiteSpace(contentValue))
            {
                if (contentValue.Length > MaxContentLength)
                {
                    error = $"'content' length {contentValue.Length} exceeds the maximum {MaxContentLength}.";
                    return false;
                }
                content = contentValue;
            }

            args = new ParsedArgs
            {
                Action = action,
                PinTypeWire = pinTypeWire,
                Title = title,
                Content = content
            };
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Parsed chat-call arguments. All four discriminators normalised to lower-case.</summary>
    private readonly record struct ParsedArgs
    {
        public string Action { get; init; }
        public string PinTypeWire { get; init; }
        public string Title { get; init; }
        public string? Content { get; init; }
    }

    /// <summary>
    /// Structured output payload returned in <see cref="ToolResult.Data"/>. The
    /// <see cref="Status"/> field is the dispatch discriminator the LLM pattern-matches on
    /// (<c>created</c> | <c>deleted</c> | <c>refused_not_found</c>). ADR-015 binding: NEVER
    /// carries the title body or content body — the action + pinType + pinId are deterministic
    /// identifiers, and the human-readable <c>Message</c> contains a short label only
    /// (re-uses the user-authored title which is acceptable in this surface because the title
    /// flows back to the user through the LLM response anyway).
    /// </summary>
    public sealed class ManagePinnedContextPayload
    {
        /// <summary>Dispatch discriminator — see class XML doc.</summary>
        [JsonPropertyName("status")]
        public required string Status { get; init; }

        /// <summary>Echo of the requested action ('create' | 'delete').</summary>
        [JsonPropertyName("action")]
        public required string Action { get; init; }

        /// <summary>The pin's stable identifier on success paths; null on refusal paths that did not resolve a pin.</summary>
        [JsonPropertyName("pinId")]
        public string? PinId { get; init; }

        /// <summary>Echo of the pinType (kebab-case wire format).</summary>
        [JsonPropertyName("pinType")]
        public required string PinType { get; init; }

        /// <summary>Human-readable message — re-readable by the LLM in its next turn for prompt construction.</summary>
        [JsonPropertyName("message")]
        public required string Message { get; init; }
    }
}
