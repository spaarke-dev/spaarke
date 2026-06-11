using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Wraps an <see cref="IToolHandler"/> instance as a
/// <see cref="Microsoft.Extensions.AI.AIFunction"/> so the chat-agent's LLM call sees a
/// standard function-calling tool. The adapter reads <see cref="AnalysisTool.JsonSchema"/>
/// (populated by task D-A-08) for parameter declaration, reads
/// <see cref="AnalysisTool.AvailableInContexts"/> (task D-A-07) for filtering, and builds
/// a <see cref="ChatInvocationContext"/> (task D-A-09) when the LLM invokes the tool.
/// </summary>
/// <remarks>
/// <para>
/// <b>R6 Pillar 2 (task D-A-10, FR-10)</b>: this adapter is the structural bridge that lets
/// the chat agent's tool list become data-driven. Tasks 011 (factory wiring) and 012 (10-tool
/// batch migration) both depend on this adapter existing.
/// </para>
/// <para>
/// <b>NFR-04 binding</b>: this type uses <c>Microsoft.Extensions.AI</c> primitives ONLY.
/// It does NOT introduce any <c>Microsoft.Agents.*</c> namespace reference. The naming —
/// <see cref="Microsoft.Extensions.AI.AIFunction"/> — is the Extensions.AI primitive, NOT the
/// Microsoft Agent Framework's <c>AgentFunction</c>. Reviewer-grep: zero
/// <c>Microsoft.Agents</c> references introduced by this file.
/// </para>
/// <para>
/// <b>ADR-013 binding</b>: this type is AI-internal — it lives in <c>Services/Ai/Chat/</c>
/// and is NOT surfaced through the <c>Services/Ai/PublicContracts/</c> facade. CRUD-side
/// callers never inject it.
/// </para>
/// <para>
/// <b>ADR-015 binding</b>: when the LLM invokes the wrapped function, the adapter emits
/// telemetry with tool name + decision id + outcome + duration ONLY. It MUST NOT log the
/// LLM's argument payload (which may transitively echo user-message content), the
/// underlying handler's input, or any portion of the model response body.
/// </para>
/// <para>
/// <b>ADR-010 binding</b>: the adapter has no constructor DI dependency on top-level
/// services — only an <see cref="AnalysisTool"/> row, an <see cref="IToolHandler"/>
/// instance, a per-invocation <see cref="ChatInvocationContext"/> factory, and an optional
/// <see cref="ILogger"/>. <see cref="SprkChatAgentFactory.ResolveTools"/> (task 011 wiring)
/// instantiates these per chat session — no new top-level Program.cs DI registration line
/// is required.
/// </para>
/// <para>
/// <b>JSON Schema validation scope (FR-08 separation-of-concerns; R6 audit item 1)</b>:
/// the constructor performs THREE layers of validation on the supplied schema:
/// </para>
/// <list type="number">
/// <item><b>JSON well-formedness</b> — schema parses via <see cref="JsonDocument.Parse(string)"/>.</item>
/// <item><b>Top-level shape</b> — root is a JSON object; if a <c>"properties"</c> member
/// is present it is itself a JSON object (function-calling protocol invariant).</item>
/// <item><b>Semantic JSON Schema validation</b> — the schema is itself validated against
/// the JSON Schema Draft 2020-12 meta-schema via <c>JsonSchema.Net</c> (~300 KB
/// compressed delta). This catches admin-edited schemas that are well-formed JSON but
/// invalid JSON Schema — e.g., <c>"properties"</c> whose member value is a primitive
/// rather than a schema object, malformed <c>"required"</c> arrays, illegal keyword
/// combinations. Surfacing these at chat-session start is materially better UX than
/// silent LLM-invocation-time failures.</item>
/// </list>
/// <para>
/// Per-property argument validation (e.g., the LLM-supplied <c>query</c> value matches
/// the schema's <c>{"type": "string"}</c> constraint) is still deferred to the underlying
/// handler's <see cref="IToolHandler.ValidateChat"/> — the LLM is also constrained by
/// the schema at the function-calling protocol layer (the schema becomes part of the
/// function declaration sent to the model).
/// </para>
/// </remarks>
public sealed class ToolHandlerToAIFunctionAdapter : AIFunction
{
    /// <summary>
    /// Source identifier for distributed traces emitted by this adapter (ADR-015).
    /// </summary>
    /// <remarks>
    /// Telemetry events are emitted via this activity source so OpenTelemetry exporters
    /// observe them under a single, well-known name. Sibling chat-side tracing uses
    /// <c>Sprk.Bff.Api.Services.Ai.Chat</c> too — adapter events appear in the same trace
    /// surface as the chat-agent's other diagnostic events.
    /// </remarks>
    private static readonly ActivitySource ActivitySource = new("Sprk.Bff.Api.Services.Ai.Chat");

    private readonly AnalysisTool _tool;
    private readonly IToolHandler _handler;
    private readonly Func<ChatInvocationContext> _contextFactory;
    private readonly ILogger? _logger;
    private readonly JsonElement _jsonSchema;
    private readonly CitationContext? _citationAccumulator;
    private readonly Func<ChatSseEvent, CancellationToken, Task>? _sseWriter;
    private readonly Func<Models.Ai.Chat.DocumentStreamEvent, CancellationToken, Task>? _documentStreamWriter;
    private readonly Guid? _analysisId;

    /// <summary>
    /// Constructs the adapter. Validates the tool's JSON Schema (well-formedness + top-level
    /// shape) and the handler's chat-invocation support. Throws on misconfiguration so the
    /// chat factory (task 011) can surface a clear error rather than registering a broken
    /// tool with the LLM.
    /// </summary>
    /// <param name="tool">
    /// The Dataverse <see cref="AnalysisTool"/> row. Must have non-null
    /// <see cref="AnalysisTool.JsonSchema"/> and non-empty <see cref="AnalysisTool.Name"/>.
    /// </param>
    /// <param name="handler">
    /// The matching <see cref="IToolHandler"/> implementation. Must declare
    /// <see cref="InvocationContextKind.Chat"/> (or <see cref="InvocationContextKind.Both"/>)
    /// via <see cref="IToolHandler.SupportedInvocationContexts"/> — otherwise the adapter
    /// rejects construction because exposing such a handler to the LLM would always
    /// short-circuit to the default <see cref="IToolHandler.ValidateChat"/> failure.
    /// </param>
    /// <param name="contextFactory">
    /// Factory that produces a per-invocation <see cref="ChatInvocationContext"/>. The chat
    /// agent factory (task 011) wires this to a captured chat session id + tenant id so
    /// every LLM invocation gets a fresh decision id and arguments payload.
    /// </param>
    /// <param name="logger">
    /// Optional logger for diagnostic events (ADR-015: IDs + outcome + duration ONLY).
    /// </param>
    /// <param name="citationAccumulator">
    /// Optional per-chat-turn citation accumulator (R6 Wave 7b). When non-null AND the handler
    /// returns <see cref="ToolResult.Metadata"/> containing <see cref="ToolResultMetadataKeys.Citations"/>,
    /// the adapter forwards each citation envelope into the accumulator via
    /// <see cref="CitationContext.AddCitation"/>. When null, citation metadata is dropped silently
    /// (backward-compat for callers that don't need accumulation). Per ADR-014 the accumulator is
    /// per-chat-session (= per-tenant scope); per ADR-015 citations carry deterministic source
    /// identifiers only — NEVER user message content.
    /// </param>
    /// <param name="sseWriter">
    /// Optional SSE writer delegate (R6 Wave 7b). When non-null AND the handler returns
    /// <see cref="ToolResult.Metadata"/> containing <see cref="ToolResultMetadataKeys.Widget"/>,
    /// the adapter emits the corresponding <c>source_pane</c> or <c>output_pane</c>
    /// <see cref="ChatSseEvent"/>. When null, widget metadata is dropped silently. Failures are
    /// non-fatal (logged + skipped) — never propagate to LLM-visible errors.
    /// </param>
    /// <param name="documentStreamWriter">
    /// Optional document-stream SSE writer delegate (R6 Wave 9 / ADR-033). When non-null, the
    /// adapter forwards it onto every per-invocation <see cref="ChatInvocationContext"/> via
    /// <see cref="ChatInvocationContext.DocumentStreamWriter"/>. Handlers that emit
    /// <see cref="Models.Ai.Chat.DocumentStreamEvent"/> (currently <c>WorkingDocumentHandler</c>)
    /// read the field from the context and emit Start → N×Token → End events directly during
    /// streaming. When null, the field stays null on the per-call context and handlers MUST
    /// degrade gracefully (e.g., return <see cref="ToolResult.Failure"/> with a clear
    /// "no stream writer wired" message per ADR-033 §3.1).
    /// </param>
    /// <param name="analysisId">
    /// Optional analysis id from the active chat session (R6 Wave 9 / ADR-033 Stage 4). When
    /// non-null, the adapter forwards it onto every per-invocation
    /// <see cref="ChatInvocationContext"/> via <see cref="ChatInvocationContext.AnalysisId"/>.
    /// Currently consumed by <c>WorkingDocumentHandler</c> (fetch + write-back operations
    /// against <c>sprk_analysisoutput</c>). Sourced by <see cref="Chat.SprkChatAgentFactory"/>
    /// from <c>ChatContext.AnalysisMetadata["analysisId"]</c>. Null when the chat session is
    /// not bound to an analysis — handlers that require it MUST return
    /// <see cref="ToolResult.Failure"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tool"/>, <paramref name="handler"/>, or <paramref name="contextFactory"/>
    /// is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tool"/>.Name is null/whitespace, or
    /// <paramref name="tool"/>.JsonSchema is null/whitespace, or
    /// <paramref name="tool"/>.JsonSchema is not parseable JSON, or
    /// the parsed schema root is not a JSON object, or
    /// the parsed schema's <c>"properties"</c> member is present but is not a JSON object, or
    /// the parsed schema does not itself validate against the JSON Schema Draft 2020-12
    /// meta-schema (R6 audit item 1; semantic validation via <c>JsonSchema.Net</c>).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="handler"/>.SupportedInvocationContexts does not include
    /// <see cref="InvocationContextKind.Chat"/>.
    /// </exception>
    public ToolHandlerToAIFunctionAdapter(
        AnalysisTool tool,
        IToolHandler handler,
        Func<ChatInvocationContext> contextFactory,
        ILogger? logger = null,
        CitationContext? citationAccumulator = null,
        Func<ChatSseEvent, CancellationToken, Task>? sseWriter = null,
        Func<Models.Ai.Chat.DocumentStreamEvent, CancellationToken, Task>? documentStreamWriter = null,
        Guid? analysisId = null)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger;
        _citationAccumulator = citationAccumulator;
        _sseWriter = sseWriter;
        _documentStreamWriter = documentStreamWriter;
        _analysisId = analysisId;

        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            throw new ArgumentException(
                $"[FR-10] AnalysisTool.Name is required for chat-tool exposure (tool id {tool.Id}).",
                nameof(tool));
        }

        // FR-08: JsonSchema is null for playbook-only rows. The chat-side resolver (task 011)
        // is responsible for filtering those out before the adapter is constructed — but we
        // also guard here so a misconfigured caller surfaces a clear error rather than
        // sending a function declaration with no schema (which the LLM would happily invoke
        // with arbitrary garbage arguments).
        if (string.IsNullOrWhiteSpace(tool.JsonSchema))
        {
            throw new ArgumentException(
                $"[FR-10] AnalysisTool '{tool.Name}' (id {tool.Id}) has null/empty JsonSchema; " +
                "chat-available tools require a populated schema (FR-08). The chat-side resolver " +
                "must filter playbook-only rows before constructing the adapter.",
                nameof(tool));
        }

        // NFR-04 / FR-10: parse the schema into a JsonElement so we can expose it on
        // AIFunctionDeclaration.JsonSchema (the protocol-level parameter declaration the LLM sees).
        try
        {
            using var doc = JsonDocument.Parse(tool.JsonSchema);
            // Defensive sanity: every tool the LLM invokes via function-calling has an
            // OBJECT root with a properties bag. Anything else is either a bug or an
            // upstream type mismatch. Surfacing this here prevents the LLM from being
            // handed a schema it can't satisfy.
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    $"[FR-10] AnalysisTool '{tool.Name}' JsonSchema root must be a JSON object " +
                    $"(was {doc.RootElement.ValueKind}).",
                    nameof(tool));
            }

            // We don't enforce the presence of "type": "object" (some valid schemas use
            // composition keywords without a top-level type) but if "properties" is present
            // it MUST be an object.
            if (doc.RootElement.TryGetProperty("properties", out var props)
                && props.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    $"[FR-10] AnalysisTool '{tool.Name}' JsonSchema 'properties' must be a JSON object " +
                    $"(was {props.ValueKind}).",
                    nameof(tool));
            }

            // Clone before the JsonDocument is disposed so the JsonElement remains valid
            // for the lifetime of this adapter (AIFunctionDeclaration.JsonSchema property
            // is read by the chat client at invocation time).
            _jsonSchema = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                $"[FR-10] AnalysisTool '{tool.Name}' JsonSchema is not valid JSON: {ex.Message}",
                nameof(tool),
                ex);
        }

        // R6 audit item 1: semantic JSON Schema validation via JsonSchema.Net (~300 KB
        // compressed delta). Catches admin-edited schemas that are well-formed JSON but
        // invalid JSON Schema — e.g., properties whose value is a primitive rather than a
        // schema object, illegal keyword shapes. Surfacing here (chat-session start) is
        // materially better UX than LLM-invocation-time silent failures.
        ValidateAgainstMetaSchema(tool, _jsonSchema);

        // FR-09 / D-A-09: defensive guard — task 009 added a default ValidateChat impl that
        // refuses chat invocation. Constructing the adapter for a handler that hasn't opted
        // in would expose a tool to the LLM that returns "not supported" on every call.
        var supported = handler.SupportedInvocationContexts;
        if ((supported & InvocationContextKind.Chat) == 0)
        {
            throw new InvalidOperationException(
                $"[FR-10] Handler '{handler.HandlerId}' does not declare InvocationContextKind.Chat " +
                $"(reports {supported}). Refusing to expose to LLM. Override " +
                $"IToolHandler.SupportedInvocationContexts and implement ValidateChat/ExecuteChatAsync " +
                $"to opt in (see TemplateHandler.cs).");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// FR-10: this is the function name the LLM invokes. Sourced from the Dataverse
    /// <c>sprk_analysistool.sprk_name</c> column.
    /// </remarks>
    public override string Name => _tool.Name;

    /// <inheritdoc />
    /// <remarks>
    /// FR-10: this is the natural-language description the LLM consults when deciding
    /// whether to invoke this tool. Sourced from <c>sprk_analysistool.sprk_description</c>.
    /// </remarks>
    public override string Description => _tool.Description ?? string.Empty;

    /// <inheritdoc />
    /// <remarks>
    /// FR-10: parameter declaration the LLM sees. Sourced from
    /// <c>sprk_analysistool.sprk_jsonschema</c> (Memo column) via the
    /// <see cref="AnalysisTool.JsonSchema"/> DTO field (task D-A-08). Parsed once at
    /// construction; safe to expose as the cached <see cref="JsonElement"/>.
    /// </remarks>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// LLM-driven invocation entry point. Builds a fresh <see cref="ChatInvocationContext"/>
    /// via the constructor-supplied factory (so the chat-agent can capture session id /
    /// tenant id / decision id), runs <see cref="IToolHandler.ValidateChat"/> first, then
    /// dispatches to <see cref="IToolHandler.ExecuteChatAsync"/>.
    /// </para>
    /// <para>
    /// ADR-015 telemetry: emits start + complete activity events with tool name + decision
    /// id + outcome + duration only. Argument payloads (which may transitively echo user
    /// message text) are NEVER logged. The decision id is the per-tool-call correlation
    /// identifier the chat-agent already uses for trace correlation — it's safe to log
    /// because it's a freshly-generated GUID with no semantic content.
    /// </para>
    /// </remarks>
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        // Build the per-invocation context. The factory captures chat session id +
        // tenant id from the chat-agent's resolution time; we mutate the captured
        // record below to attach this specific call's LLM arguments.
        var baseContext = _contextFactory();

        // Serialize the LLM-supplied AIFunctionArguments into the schema-validated JSON
        // string the handler expects. The schema enforcement happens at the function-calling
        // protocol layer (the LLM is given the schema as part of the function declaration);
        // here we forward whatever the LLM produced and let ValidateChat fail fast if the
        // payload is structurally wrong for this handler.
        var argsJson = SerializeArgumentsToJson(arguments);

        var context = baseContext with
        {
            RequestedToolName = _tool.Name,
            ToolArgumentsJson = argsJson,
            // ADR-033 (R6 Wave 9): forward the adapter-level document-stream writer onto
            // the per-call context so handlers (currently WorkingDocumentHandler) can emit
            // Start / Token / End events directly during streaming. Null when not wired —
            // handlers MUST check for null per ADR-033 §3.1.
            DocumentStreamWriter = _documentStreamWriter,
            // ADR-033 Stage 4 (R6 Wave 9): forward the adapter-level analysis id onto the
            // per-call context. WorkingDocumentHandler reads it to fetch the current working
            // document and to target write-back persistence. Null when standalone chat —
            // handlers that require it MUST return ToolResult.Failure with diagnostic.
            AnalysisId = _analysisId
        };

        using var activity = ActivitySource.StartActivity("chat-tool.invoke");
        // ADR-015: deterministic IDs only — never the args payload.
        activity?.SetTag("sprk.tool.name", _tool.Name);
        activity?.SetTag("sprk.tool.handlerId", _handler.HandlerId);
        activity?.SetTag("sprk.tool.decisionId", context.DecisionId);
        activity?.SetTag("sprk.tool.tenantId", context.TenantId);
        activity?.SetTag("sprk.tool.chatSessionId", context.ChatSessionId);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // FR-09: ValidateChat first (default impl rejects; chat-available handlers override).
            var validation = _handler.ValidateChat(context, _tool);
            if (!validation.IsValid)
            {
                stopwatch.Stop();
                LogToolOutcome(context.DecisionId, outcome: "validation_failed", stopwatch.ElapsedMilliseconds, errorCode: "ValidationFailed");
                activity?.SetTag("sprk.tool.outcome", "validation_failed");
                activity?.SetStatus(ActivityStatusCode.Error, description: "validation_failed");

                // Return a structured envelope the LLM can interpret. We surface validation
                // errors as a tool-error response (NOT a thrown exception) so the LLM can
                // include them in its reasoning and potentially recover via a follow-up call.
                return new
                {
                    success = false,
                    errorCode = "ValidationFailed",
                    errors = validation.Errors
                };
            }

            // FR-09: dispatch to the chat-context overload. Handlers that opt in override
            // ExecuteChatAsync (default throws NotSupportedException — but we already
            // verified opt-in in the constructor, so reaching the default here is a
            // handler implementation bug).
            var result = await _handler.ExecuteChatAsync(context, _tool, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            var outcome = result.Success ? "ok" : "error";
            LogToolOutcome(context.DecisionId, outcome, stopwatch.ElapsedMilliseconds, errorCode: result.ErrorCode);
            activity?.SetTag("sprk.tool.outcome", outcome);
            if (!result.Success)
            {
                activity?.SetStatus(ActivityStatusCode.Error, description: result.ErrorCode);
            }

            // R6 Wave 7b: post-process well-known metadata keys for citation accumulation +
            // widget event emission. Failures here are non-fatal — handlers stay pure
            // input/output; this is cross-cutting infrastructure that MUST NOT bleed into
            // tool-call success/failure visible to the LLM.
            await PostProcessMetadataAsync(result, context.DecisionId, cancellationToken)
                .ConfigureAwait(false);

            // Return the ToolResult itself; Microsoft.Extensions.AI will serialize it back
            // to the LLM. Downstream chat-agent middleware may transform this further.
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            LogToolOutcome(context.DecisionId, outcome: "cancelled", stopwatch.ElapsedMilliseconds, errorCode: "Cancelled");
            activity?.SetTag("sprk.tool.outcome", "cancelled");
            activity?.SetStatus(ActivityStatusCode.Error, description: "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // ADR-015: log the exception TYPE + message ONLY when the message comes from
            // our own code (it never includes user content because handlers must follow
            // the same governance). Third-party exception messages (e.g., from the LLM
            // SDK) might transitively include arg content — but at this point we've
            // already failed and the operator needs the error type to diagnose.
            LogToolOutcome(context.DecisionId, outcome: "exception", stopwatch.ElapsedMilliseconds, errorCode: ex.GetType().Name);
            activity?.SetTag("sprk.tool.outcome", "exception");
            activity?.SetTag("sprk.tool.exceptionType", ex.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error, description: ex.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// R6 Wave 7b — post-processes well-known metadata keys returned by chat-side handlers.
    /// Forwards citation envelopes into the per-chat-turn <see cref="CitationContext"/> and
    /// emits widget envelopes as <see cref="ChatSseEvent"/> via the SSE writer delegate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resilience contract: failures here are NEVER fatal. The adapter logs ADR-015-compliant
    /// counts/buckets and proceeds. Citation/widget emission is cross-cutting infrastructure —
    /// the handler's primary success/failure surface is its <see cref="ToolResult"/>, which
    /// is returned to the LLM regardless of post-processing outcome.
    /// </para>
    /// <para>
    /// ADR-015 logging hygiene: this method logs counts and outcome only — never citation
    /// excerpts, widget data content, or source identifiers beyond what the ADR-015 telemetry
    /// rules already allow (deterministic IDs).
    /// </para>
    /// </remarks>
    private async Task PostProcessMetadataAsync(
        ToolResult result,
        Guid decisionId,
        CancellationToken cancellationToken)
    {
        if (result.Metadata is null || result.Metadata.Count == 0)
        {
            return;
        }

        // Citations — accumulate into the per-turn context if both metadata + accumulator present.
        if (result.Metadata.TryGetValue(ToolResultMetadataKeys.Citations, out var citationsObj)
            && citationsObj is not null)
        {
            if (_citationAccumulator is null)
            {
                // No accumulator wired (likely non-chat path or older factory wiring). Drop silently.
                _logger?.LogDebug(
                    "[Wave7b] chat-tool '{ToolName}' returned citations metadata but no accumulator wired; " +
                    "decisionId={DecisionId}",
                    _tool.Name, decisionId);
            }
            else
            {
                var added = TryAccumulateCitations(citationsObj, decisionId);
                // ADR-015: count + outcome only. Never citation text.
                _logger?.LogInformation(
                    "[Wave7b][ADR-015] chat-tool '{ToolName}' citations accumulated: decisionId={DecisionId} count={Count}",
                    _tool.Name, decisionId, added);
            }
        }

        // Widget — emit a single SSE event via the writer if both metadata + writer present.
        if (result.Metadata.TryGetValue(ToolResultMetadataKeys.Widget, out var widgetObj)
            && widgetObj is not null)
        {
            if (_sseWriter is null)
            {
                _logger?.LogDebug(
                    "[Wave7b] chat-tool '{ToolName}' returned widget metadata but no sseWriter wired; " +
                    "decisionId={DecisionId}",
                    _tool.Name, decisionId);
            }
            else
            {
                await TryEmitWidgetAsync(widgetObj, decisionId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Accepts the handler-supplied citations value (record array, JSON string, JsonElement,
    /// or IEnumerable of envelope-shaped objects) and forwards each into the accumulator.
    /// Returns the count successfully added. Malformed entries are logged-and-skipped.
    /// </summary>
    private int TryAccumulateCitations(object citationsObj, Guid decisionId)
    {
        if (_citationAccumulator is null) return 0;

        try
        {
            // Normalize to JsonElement array for uniform parsing. Handlers may supply either
            // ToolResultCitation records directly, a JsonElement, a JSON-string, or another
            // IEnumerable; serialize-then-parse covers all of those at low cost.
            JsonElement arrayElement;
            if (citationsObj is JsonElement preElement)
            {
                arrayElement = preElement;
            }
            else if (citationsObj is string jsonText)
            {
                using var doc = JsonDocument.Parse(jsonText);
                arrayElement = doc.RootElement.Clone();
            }
            else
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(citationsObj);
                using var doc = JsonDocument.Parse(bytes);
                arrayElement = doc.RootElement.Clone();
            }

            if (arrayElement.ValueKind != JsonValueKind.Array)
            {
                _logger?.LogWarning(
                    "[Wave7b] chat-tool '{ToolName}' citations metadata is not a JSON array (was {Kind}); skipping. " +
                    "decisionId={DecisionId}",
                    _tool.Name, arrayElement.ValueKind, decisionId);
                return 0;
            }

            int added = 0;
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                var chunkId = GetStringProp(item, "chunkId", "ChunkId");
                var sourceName = GetStringProp(item, "sourceName", "SourceName");
                if (string.IsNullOrWhiteSpace(chunkId) || string.IsNullOrWhiteSpace(sourceName))
                {
                    continue;
                }

                int? pageNumber = TryGetIntProp(item, "pageNumber", "PageNumber");
                var excerpt = GetStringProp(item, "excerpt", "Excerpt") ?? string.Empty;
                var sourceType = GetStringProp(item, "sourceType", "SourceType");
                var url = GetStringProp(item, "url", "Url");
                var snippet = GetStringProp(item, "snippet", "Snippet");

                _citationAccumulator.AddCitation(
                    chunkId: chunkId!,
                    sourceName: sourceName!,
                    pageNumber: pageNumber,
                    excerpt: excerpt,
                    sourceType: sourceType,
                    url: url,
                    snippet: snippet);
                added++;
            }
            return added;
        }
        catch (JsonException ex)
        {
            // Malformed JSON in metadata — log type + decision id; never the content.
            _logger?.LogWarning(
                "[Wave7b] chat-tool '{ToolName}' citations metadata could not be parsed as JSON " +
                "({ExceptionType}); skipping accumulation. decisionId={DecisionId}",
                _tool.Name, ex.GetType().Name, decisionId);
            return 0;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                "[Wave7b] chat-tool '{ToolName}' citation accumulation failed unexpectedly " +
                "({ExceptionType}); skipping. decisionId={DecisionId}",
                _tool.Name, ex.GetType().Name, decisionId);
            return 0;
        }
    }

    /// <summary>
    /// Accepts the handler-supplied widget value, derives the pane type, and emits the
    /// corresponding <c>source_pane</c> or <c>output_pane</c> SSE event via the writer.
    /// Malformed envelopes or writer failures are logged-and-skipped.
    /// </summary>
    private async Task TryEmitWidgetAsync(
        object widgetObj,
        Guid decisionId,
        CancellationToken cancellationToken)
    {
        if (_sseWriter is null) return;

        try
        {
            // Normalize to JsonElement for uniform reading.
            JsonElement envelope;
            if (widgetObj is JsonElement preElement)
            {
                envelope = preElement;
            }
            else if (widgetObj is string jsonText)
            {
                using var doc = JsonDocument.Parse(jsonText);
                envelope = doc.RootElement.Clone();
            }
            else
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(widgetObj);
                using var doc = JsonDocument.Parse(bytes);
                envelope = doc.RootElement.Clone();
            }

            if (envelope.ValueKind != JsonValueKind.Object)
            {
                _logger?.LogWarning(
                    "[Wave7b] chat-tool '{ToolName}' widget metadata is not a JSON object (was {Kind}); skipping. " +
                    "decisionId={DecisionId}",
                    _tool.Name, envelope.ValueKind, decisionId);
                return;
            }

            var paneType = GetStringProp(envelope, "paneType", "PaneType");
            var widgetType = GetStringProp(envelope, "widgetType", "WidgetType");
            if (string.IsNullOrWhiteSpace(paneType) || string.IsNullOrWhiteSpace(widgetType))
            {
                _logger?.LogWarning(
                    "[Wave7b] chat-tool '{ToolName}' widget metadata missing paneType or widgetType; skipping. " +
                    "decisionId={DecisionId}",
                    _tool.Name, decisionId);
                return;
            }

            // Data payload may be absent — pass an empty object in that case to keep the
            // SSE frame structurally valid.
            object dataPayload;
            if (envelope.TryGetProperty("data", out var dataElement)
                || envelope.TryGetProperty("Data", out dataElement))
            {
                dataPayload = dataElement;
            }
            else
            {
                dataPayload = new { };
            }

            ChatSseEvent? sseEvent = paneType switch
            {
                "source_pane" => ChatSseEventFactory.CreateSourcePaneEvent(
                    widgetType!, dataPayload, GetStringProp(envelope, "citationId", "CitationId")),
                "output_pane" => ChatSseEventFactory.CreateOutputPaneEvent(widgetType!, dataPayload),
                _ => null
            };

            if (sseEvent is null)
            {
                _logger?.LogWarning(
                    "[Wave7b] chat-tool '{ToolName}' widget paneType '{PaneType}' is not recognized; skipping. " +
                    "decisionId={DecisionId}",
                    _tool.Name, paneType, decisionId);
                return;
            }

            await _sseWriter(sseEvent, cancellationToken).ConfigureAwait(false);

            // ADR-015: log pane + widget type + decisionId only. NEVER widget data content.
            _logger?.LogInformation(
                "[Wave7b][ADR-015] chat-tool '{ToolName}' widget emitted: " +
                "paneType={PaneType} widgetType={WidgetType} decisionId={DecisionId}",
                _tool.Name, paneType, widgetType, decisionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation propagates — caller already aborted.
            throw;
        }
        catch (Exception ex)
        {
            // SSE writer faults are non-fatal — the handler's ToolResult still flows to LLM.
            _logger?.LogWarning(
                "[Wave7b] chat-tool '{ToolName}' widget emission failed ({ExceptionType}); skipping. " +
                "decisionId={DecisionId}",
                _tool.Name, ex.GetType().Name, decisionId);
        }
    }

    /// <summary>Reads a string property from a JsonElement, trying the first then alternate name.</summary>
    private static string? GetStringProp(JsonElement element, string primary, string alternate)
    {
        if (element.TryGetProperty(primary, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString();
        if (element.TryGetProperty(alternate, out var a) && a.ValueKind == JsonValueKind.String)
            return a.GetString();
        return null;
    }

    /// <summary>Reads an integer property from a JsonElement, trying first then alternate name.</summary>
    private static int? TryGetIntProp(JsonElement element, string primary, string alternate)
    {
        if (element.TryGetProperty(primary, out var p) && p.ValueKind == JsonValueKind.Number
            && p.TryGetInt32(out var v1)) return v1;
        if (element.TryGetProperty(alternate, out var a) && a.ValueKind == JsonValueKind.Number
            && a.TryGetInt32(out var v2)) return v2;
        return null;
    }

    /// <summary>
    /// R6 audit item 1 — validates a candidate function-calling schema against the JSON
    /// Schema Draft 2020-12 meta-schema. Throws <see cref="ArgumentException"/> with a
    /// clear, admin-actionable error message on the first failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementation notes:
    /// </para>
    /// <list type="bullet">
    /// <item>Uses <c>JsonSchema.Net</c>'s <see cref="MetaSchemas.Draft202012"/> evaluator
    /// — the canonical Draft 2020-12 meta-schema as published by json-schema.org.</item>
    /// <item>Deserializing the schema text into <see cref="Json.Schema.JsonSchema"/> can
    /// throw <see cref="JsonException"/> when the JSON cannot be coerced to a valid schema
    /// object (e.g., a string at the root). We catch and re-throw as
    /// <see cref="ArgumentException"/> so the upstream chat resolver / DI factory can
    /// uniformly treat schema problems via one exception type.</item>
    /// <item>Catalog-write-time validation (admins authoring rows in Dataverse) is the
    /// complementary line of defense — see <c>scripts/Test-AnalysisToolSchemaValid.ps1</c>.
    /// This adapter-level check ensures defense-in-depth: even if a row was authored
    /// before write-time validation existed, the LLM is never handed a malformed schema.</item>
    /// </list>
    /// </remarks>
    private static void ValidateAgainstMetaSchema(AnalysisTool tool, JsonElement schemaRoot)
    {
        JsonNode? schemaNode;
        try
        {
            // Round-trip via raw JSON so we get a writable JsonNode the evaluator accepts.
            // (JsonElement → JsonNode is awkward in .NET 8; serialize then parse.)
            schemaNode = JsonNode.Parse(schemaRoot.GetRawText());
        }
        catch (JsonException ex)
        {
            // Should be unreachable — we already proved the text parses as JSON above —
            // but defend against any future change to how _jsonSchema is materialized.
            throw new ArgumentException(
                $"[R6-audit-1] AnalysisTool '{tool.Name}' JsonSchema could not be re-parsed " +
                $"for semantic validation: {ex.Message}",
                nameof(tool),
                ex);
        }

        if (schemaNode is null)
        {
            throw new ArgumentException(
                $"[R6-audit-1] AnalysisTool '{tool.Name}' JsonSchema parsed to a null JsonNode; " +
                "the schema must be a JSON object document.",
                nameof(tool));
        }

        // Evaluate the candidate schema against the Draft 2020-12 meta-schema. This is
        // the canonical "is this a valid JSON Schema?" check.
        EvaluationResults metaResults;
        try
        {
            metaResults = MetaSchemas.Draft202012.Evaluate(
                schemaNode,
                new EvaluationOptions
                {
                    OutputFormat = OutputFormat.List,
                    ValidateAgainstMetaSchema = false  // we ARE the meta-schema evaluation
                });
        }
        catch (Exception ex)
        {
            // JsonSchema.Net throws on certain malformed inputs that the parser accepts
            // but the evaluator rejects (e.g., $ref cycles). Surface as an actionable error.
            throw new ArgumentException(
                $"[R6-audit-1] AnalysisTool '{tool.Name}' JsonSchema could not be evaluated " +
                $"against the JSON Schema Draft 2020-12 meta-schema: {ex.Message}",
                nameof(tool),
                ex);
        }

        if (!metaResults.IsValid)
        {
            // Collect a compact, admin-actionable error summary. We deliberately keep
            // the message short — full evaluation output is verbose and the failing keys
            // (e.g., /properties/query/type) tell admins where the fix belongs.
            var errors = metaResults.Details
                .Where(d => d.HasErrors && d.Errors is not null)
                .SelectMany(d => d.Errors!.Select(kv => $"{d.InstanceLocation}: {kv.Value}"))
                .Take(5)
                .ToArray();

            var summary = errors.Length > 0
                ? string.Join("; ", errors)
                : "schema failed Draft 2020-12 meta-schema validation (no detailed errors reported)";

            throw new ArgumentException(
                $"[R6-audit-1] AnalysisTool '{tool.Name}' JsonSchema is well-formed JSON but " +
                $"is not a valid JSON Schema (Draft 2020-12): {summary}",
                nameof(tool));
        }
    }

    /// <summary>
    /// Serializes the LLM's <see cref="AIFunctionArguments"/> payload to the JSON string the
    /// handler expects on <see cref="ChatInvocationContext.ToolArgumentsJson"/>.
    /// </summary>
    /// <remarks>
    /// We materialize via <see cref="JsonSerializer.Serialize{TValue}(TValue, JsonSerializerOptions)"/>
    /// against a dictionary projection. <see cref="AIFunctionArguments"/> implements
    /// <see cref="IDictionary{TKey, TValue}"/> for the nominal arguments — the same shape the
    /// chat-agent receives from the model. Returns "{}" when the argument set is empty so
    /// downstream parsers don't have to special-case null.
    /// </remarks>
    private static string SerializeArgumentsToJson(AIFunctionArguments arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "{}";
        }

        // AIFunctionArguments implements IDictionary<string, object?> — serialize directly.
        // The chat client supplies values that round-trip through System.Text.Json already
        // (the function-calling protocol uses JSON wire format). Default options are fine.
        return JsonSerializer.Serialize(
            (IDictionary<string, object?>)arguments,
            new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// ADR-015-compliant outcome log: tool name + decision id + outcome + duration + optional
    /// error code. Never includes args / response body / user message text.
    /// </summary>
    private void LogToolOutcome(Guid decisionId, string outcome, long durationMs, string? errorCode)
    {
        if (_logger is null)
        {
            return;
        }

        // Structured event; the logger sink applies its own filtering. ADR-015 invariant
        // is enforced by what we DON'T pass: no AIFunctionArguments, no ToolResult.Data,
        // no ChatInvocationContext.ToolArgumentsJson.
        _logger.LogInformation(
            "[ADR-015] chat-tool invocation: tool={ToolName} handler={HandlerId} " +
            "decisionId={DecisionId} outcome={Outcome} durationMs={DurationMs} errorCode={ErrorCode}",
            _tool.Name,
            _handler.HandlerId,
            decisionId,
            outcome,
            durationMs,
            errorCode);
    }
}
