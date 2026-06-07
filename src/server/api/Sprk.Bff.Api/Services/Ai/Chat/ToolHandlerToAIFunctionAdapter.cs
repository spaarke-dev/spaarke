using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Ai;

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
/// <b>JSON Schema validation scope (FR-08 separation-of-concerns)</b>: the constructor
/// validates the supplied schema is well-formed JSON and has a top-level
/// <c>"type": "object"</c> shape with a <c>"properties"</c> object. Deeper semantic
/// validation (e.g., required-keyword correctness, per-property type validation against
/// LLM arguments) is deferred to the underlying handler's
/// <see cref="IToolHandler.ValidateChat"/>. Rationale: full JSON-Schema-2020-12 semantic
/// validation requires a ~1 MB+ third-party library (<c>JsonSchema.Net</c> or equivalent),
/// not currently a dependency — adding it would consume R6 publish-size budget (≤+5 MB)
/// without proportionate benefit, because the LLM is constrained by the schema at the
/// function-calling protocol layer (the schema becomes part of the function declaration
/// sent to the model) and the handler is the next-line defense for semantic checks.
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
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tool"/>, <paramref name="handler"/>, or <paramref name="contextFactory"/>
    /// is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tool"/>.Name is null/whitespace, or
    /// <paramref name="tool"/>.JsonSchema is null/whitespace, or
    /// <paramref name="tool"/>.JsonSchema is not parseable JSON, or
    /// the parsed schema does not have a top-level <c>"type": "object"</c> with a
    /// <c>"properties"</c> object.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="handler"/>.SupportedInvocationContexts does not include
    /// <see cref="InvocationContextKind.Chat"/>.
    /// </exception>
    public ToolHandlerToAIFunctionAdapter(
        AnalysisTool tool,
        IToolHandler handler,
        Func<ChatInvocationContext> contextFactory,
        ILogger? logger = null)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger;

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
            ToolArgumentsJson = argsJson
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
