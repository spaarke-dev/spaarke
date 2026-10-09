using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Foundry;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor that routes playbook nodes to the Azure AI Foundry Agent Service.
/// Implements <see cref="INodeExecutor"/> for <see cref="ExecutorType.AgentService"/> (value 60).
/// </summary>
/// <remarks>
/// <para>
/// Follows the registry pattern per ADR-010 — registered as a Singleton INodeExecutor,
/// auto-discovered by <see cref="NodeExecutorRegistry"/> via the
/// <c>IEnumerable&lt;INodeExecutor&gt;</c> constructor injection.
/// </para>
/// <para>
/// Node parameters are read from <c>node.ConfigJson</c> (JSON) using the same pattern as
/// all other executors (e.g., <see cref="ConditionNodeExecutor"/>). Keys:
/// <list type="bullet">
///   <item><c>tenantId</c> — tenant scope for the Agent thread cache key (ADR-009). Required.</item>
///   <item><c>prompt</c> — user message sent to the Agent thread. Optional when the node links an Action
///   with a system prompt.</item>
///   <item><c>templateParameters</c> / <c>inputBinding</c> — inputs for the linked Action's JPS prompt, as for
///   <see cref="AiCompletionNodeExecutor"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Prompt source (D-98, task 135).</b> The message is the node's own <c>prompt</c> when it authors one;
/// otherwise the linked Action's <c>sprk_systemprompt</c>, rendered by <see cref="PromptSchemaRenderer"/> with the
/// node's <c>templateParameters</c> and <c>inputBinding</c> — one source of truth for the prompt, as for every other
/// prompt-driven executor. A JPS prompt with <c>structuredOutput</c> gets its JSON schema appended, because the
/// Agent Service has no constrained decoding.
/// </para>
/// <para>
/// <b>Output.</b> When the agent replies with a JSON object (optionally inside a code fence), that object is the
/// node's <see cref="NodeOutput.StructuredData"/>, so downstream nodes can read its fields. Any other reply keeps the
/// previous shape: <c>{ threadId, responseLength }</c> with the text in <see cref="NodeOutput.TextContent"/>.
/// </para>
/// <para>
/// Exception mapping (ADR-016 / ADR-018):
/// <list type="bullet">
///   <item><see cref="ConcurrencyLimitExceededException"/> → <c>NODE_AGENT_CONCURRENCY_EXCEEDED</c> (HTTP 429 equivalent).</item>
///   <item><see cref="FeatureDisabledException"/> → <c>NODE_AGENT_FEATURE_DISABLED</c> (HTTP 503 equivalent).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class AgentServiceNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly AgentServiceClient _agentServiceClient;
    private readonly PromptSchemaRenderer _promptSchemaRenderer;
    private readonly ILogger<AgentServiceNodeExecutor> _logger;

    public AgentServiceNodeExecutor(
        AgentServiceClient agentServiceClient,
        PromptSchemaRenderer promptSchemaRenderer,
        ILogger<AgentServiceNodeExecutor> logger)
    {
        _agentServiceClient = agentServiceClient;
        _promptSchemaRenderer = promptSchemaRenderer;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.AgentService
    };

    // R7 task 085 / FR-23 — typed config schema for Playbook Builder canvas.
    // Derived from AgentServiceNodeConfig (TenantId required; Prompt required unless the node links an Action).
    private static readonly ExecutorConfigSchema ConfigSchemaInstance = new(
        ExecutorTypeName: nameof(ExecutorType.AgentService),
        ExecutorTypeValue: (int)ExecutorType.AgentService,
        Description: "Routes the playbook node to Azure AI Foundry Agent Service (Phase 2). Creates/resumes a tenant-scoped Agent thread (ADR-009 Redis-first) and streams the response.",
        Fields: new ConfigSchemaField[]
        {
            new(
                Name: "tenantId",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Tenant identifier for the Redis Agent-thread cache key. The CONVERSATION scope is the playbook RunId, supplied by the executor — not by this config (task 122). Required.",
                Default: null),
            new(
                Name: "prompt",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "User message sent to the Agent thread. Supports {{var}} template substitution against upstream node outputs. Optional when the node links an Action: the Action's system prompt is then rendered with templateParameters / inputBinding (D-98).",
                Default: null)
        });

    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() => ConfigSchemaInstance;

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("AgentService node requires configuration (ConfigJson with 'tenantId', and 'prompt' unless the node links an Action)");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        try
        {
            var config = JsonSerializer.Deserialize<AgentServiceNodeConfig>(context.Node.ConfigJson, JsonOptions);
            if (config is null)
            {
                errors.Add("Failed to parse AgentService node configuration");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(config.TenantId))
                    errors.Add("AgentService node requires 'tenantId' in ConfigJson");

                if (!IsAuthoredPrompt(config.Prompt) && !HasActionPrompt(context))
                    errors.Add("AgentService node requires 'prompt' in ConfigJson, or a linked Action with a system prompt (sprk_systemprompt)");
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid AgentService node configuration JSON: {ex.Message}");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        // OTEL span: ai.agent.node_execute — executor-level span that is a child of
        // the routing middleware span (ai.routing.decision) per FR-20 span hierarchy.
        // ADR-015: only node.id, action_type, and outcome are tagged — no prompt content.
        using var activity = AiTelemetry.ActivitySource.StartActivity(
            "ai.agent.node_execute", ActivityKind.Internal);
        activity?.SetTag("node.id", context.Node.Id.ToString());
        activity?.SetTag("node.name", context.Node.Name);
        activity?.SetTag("action_type", 60); // ExecutorType.AgentService = 60

        _logger.LogDebug(
            "Executing AgentService node {NodeId} ({NodeName})",
            context.Node.Id,
            context.Node.Name);

        try
        {
            // Validate first — fail fast on invalid configuration
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                activity?.SetTag("node.outcome", "validation_failed");
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Parse configuration — ConfigJson is validated above so safe to deserialize
            var config = JsonSerializer.Deserialize<AgentServiceNodeConfig>(context.Node.ConfigJson!, JsonOptions)!;
            var tenantId = config.TenantId!;
            var prompt = ResolvePrompt(context, config, _promptSchemaRenderer, _logger);
            activity?.SetTag("prompt.source", IsAuthoredPrompt(config.Prompt) ? "node" : "action");

            // Fail fast, before any agent call: a placeholder left in the prompt means an input never arrived, and the
            // agent would answer about "{{assessments}}" instead of the data.
            if (HasUnrenderedPlaceholder(prompt))
            {
                activity?.SetTag("node.outcome", "unrendered_placeholder");
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    "AgentService prompt still contains an unrendered {{placeholder}}; check the node's templateParameters / inputBinding against the Action's prompt",
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            _logger.LogDebug(
                "AgentService node {NodeId}: creating/resuming thread for tenant {TenantId}",
                context.Node.Id, tenantId);

            // Create or resume the thread for THIS PLAYBOOK RUN (ADR-009: Redis-first).
            //
            // Scoped by RunId as of task 122. It was tenant-only, and the cache id was the constant
            // `thread`, so concurrent runs of ANY playbook for ANY user in the tenant appended to one
            // shared Foundry conversation — each run's synthesis prompt becoming context for the next.
            // A run is this path's conversation, so it is the correct scope.
            var threadId = await _agentServiceClient.CreateOrResumeThreadAsync(
                tenantId,
                context.RunId.ToString(),
                cancellationToken);

            // ADR-015: thread.id is an opaque SDK identifier, not PII.
            activity?.SetTag("agent.thread.id", threadId);

            // Send the user message to the thread
            await _agentServiceClient.SendMessageAsync(threadId, prompt, cancellationToken);

            // Stream the agent response and collect all tokens into the output
            var responseBuilder = new StringBuilder();
            await foreach (var token in _agentServiceClient.StreamResponseAsync(threadId, cancellationToken))
            {
                responseBuilder.Append(token);

                // Forward token to SSE stream when callback is registered (per-token streaming path)
                if (context.OnTokenReceived is not null)
                {
                    await context.OnTokenReceived(token);
                }
            }

            var responseText = responseBuilder.ToString();

            _logger.LogInformation(
                "AgentService node {NodeId} completed — thread {ThreadId}, response length {Length}",
                context.Node.Id, threadId, responseText.Length);

            // ADR-015: response length is metadata (not content).
            activity?.SetTag("node.outcome", "success");
            activity?.SetTag("agent.response_length", responseText.Length);

            return BuildOutput(context, threadId, responseText, startedAt);
        }
        catch (ConcurrencyLimitExceededException ex)
        {
            _logger.LogWarning(
                "AgentService node {NodeId} rejected — concurrency limit exceeded: {Message}",
                context.Node.Id, ex.Message);

            activity?.SetTag("node.outcome", "concurrency_exceeded");
            activity?.SetStatus(ActivityStatusCode.Error, "ConcurrencyLimitExceeded");
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Agent Service concurrency limit exceeded: {ex.Message}",
                NodeErrorCodes.AgentConcurrencyExceeded,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (FeatureDisabledException ex)
        {
            _logger.LogWarning(
                "AgentService node {NodeId} skipped — feature disabled: {Message}",
                context.Node.Id, ex.Message);

            activity?.SetTag("node.outcome", "feature_disabled");
            activity?.SetStatus(ActivityStatusCode.Error, "FeatureDisabled");
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Agent Service feature is disabled: {ex.Message}",
                NodeErrorCodes.AgentFeatureDisabled,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "AgentService node {NodeId} was cancelled",
                context.Node.Id);

            activity?.SetTag("node.outcome", "cancelled");
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                "Node execution was cancelled",
                NodeErrorCodes.Cancelled,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AgentService node {NodeId} failed: {ErrorMessage}",
                context.Node.Id, ex.Message);

            activity?.SetTag("node.outcome", "error");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Agent Service internal error: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// True when the node authors its own message. A literal <c>$ref:…</c> (an unresolved file reference a deploy
    /// once wrote into predict-matter-cost) is not a message and counts as unset.
    /// </summary>
    internal static bool IsAuthoredPrompt(string? prompt) =>
        !string.IsNullOrWhiteSpace(prompt) && !prompt.TrimStart().StartsWith("$ref:", StringComparison.OrdinalIgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex UnrenderedPlaceholder =
        new(@"\{\{\s*[A-Za-z_][\w.]*\s*\}\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static bool HasUnrenderedPlaceholder(string prompt) => UnrenderedPlaceholder.IsMatch(prompt);

    /// <summary>
    /// True when the node links an Action whose system prompt can be the message (D-98).
    /// </summary>
    private static bool HasActionPrompt(NodeExecutionContext context) =>
        context.Node.ActionId != Guid.Empty
        && context.Action is not null
        && !string.IsNullOrWhiteSpace(context.Action.SystemPrompt);

    /// <summary>
    /// The message sent to the agent: the node's own <c>prompt</c> when set, otherwise the linked Action's system
    /// prompt rendered with the node's <c>templateParameters</c> and <c>inputBinding</c> (D-98). When the rendered
    /// JPS declares structured output, its JSON schema is appended, since the Agent Service cannot constrain decoding.
    /// </summary>
    internal static string ResolvePrompt(
        NodeExecutionContext context,
        AgentServiceNodeConfig config,
        PromptSchemaRenderer renderer,
        ILogger logger)
    {
        if (IsAuthoredPrompt(config.Prompt))
            return config.Prompt!;

        var rendered = renderer.Render(
            rawPrompt: context.Action.SystemPrompt,
            skillContext: null,
            knowledgeContext: null,
            documentText: null,
            templateParameters: NodeConfigPromptInputs.ExtractTemplateParameters(context.Node.ConfigJson, logger, "AgentService"),
            downstreamNodes: null,
            runtimeInput: NodeConfigPromptInputs.ExtractInputBinding(context.Node.ConfigJson, logger, "AgentService"));

        if (rendered.JsonSchema is null)
            return rendered.PromptText;

        return rendered.PromptText.TrimEnd()
            + "\n\nRespond with a single JSON object that conforms to this JSON schema. No prose and no code fence.\n"
            + rendered.JsonSchema.ToJsonString();
    }

    /// <summary>
    /// Builds the node output from the agent's reply. A JSON-object reply (optionally fenced) becomes the
    /// structured data; any other reply keeps the <c>{ threadId, responseLength }</c> shape.
    /// </summary>
    internal static NodeOutput BuildOutput(
        NodeExecutionContext context,
        string threadId,
        string responseText,
        DateTimeOffset startedAt)
    {
        var metrics = NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow);

        if (TryParseJsonObject(responseText, out var structured))
        {
            return new NodeOutput
            {
                NodeId = context.Node.Id,
                OutputVariable = context.Node.OutputVariable,
                Success = true,
                TextContent = responseText,
                StructuredData = structured,
                Metrics = metrics
            };
        }

        return NodeOutput.Ok(
            context.Node.Id,
            context.Node.OutputVariable,
            data: new { threadId, responseLength = responseText.Length },
            textContent: responseText,
            metrics: metrics);
    }

    private static bool TryParseJsonObject(string responseText, out JsonElement structured)
    {
        structured = default;
        var text = responseText.Trim();

        // Tolerate one Markdown code fence (a json-tagged fence), which agents add despite instructions.
        if (text.StartsWith(Fence, StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var closingFence = text.LastIndexOf(Fence, StringComparison.Ordinal);
            if (firstNewline < 0 || closingFence <= firstNewline)
                return false;
            text = text[(firstNewline + 1)..closingFence].Trim();
        }

        if (!text.StartsWith('{'))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            structured = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private const string Fence = "```";
}

/// <summary>
/// Configuration for AgentService node read from <c>ConfigJson</c>.
/// </summary>
internal sealed record AgentServiceNodeConfig
{
    /// <summary>
    /// Tenant identifier for the Redis thread cache key. The CONVERSATION scope is the playbook
    /// <c>RunId</c>, supplied by the executor rather than configured here (task 122). Required.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// User message sent to the Agent thread. Optional when the node links an Action (D-98).
    /// Supports template variable substitution when the orchestrator pre-renders ConfigJson.
    /// </summary>
    public string? Prompt { get; init; }
}
