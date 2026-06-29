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
/// all other executors (e.g., <see cref="ConditionNodeExecutor"/>). Required keys:
/// <list type="bullet">
///   <item><c>tenantId</c> — tenant scope for the Agent thread cache key (ADR-009).</item>
///   <item><c>prompt</c> — user message sent to the Agent thread.</item>
/// </list>
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
    private readonly ILogger<AgentServiceNodeExecutor> _logger;

    public AgentServiceNodeExecutor(
        AgentServiceClient agentServiceClient,
        ILogger<AgentServiceNodeExecutor> logger)
    {
        _agentServiceClient = agentServiceClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.AgentService
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("AgentService node requires configuration (ConfigJson with 'tenantId' and 'prompt')");
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

                if (string.IsNullOrWhiteSpace(config.Prompt))
                    errors.Add("AgentService node requires 'prompt' in ConfigJson");
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
            var prompt = config.Prompt!;

            _logger.LogDebug(
                "AgentService node {NodeId}: creating/resuming thread for tenant {TenantId}",
                context.Node.Id, tenantId);

            // Create or resume a cached thread for this tenant (ADR-009: Redis-first)
            var threadId = await _agentServiceClient.CreateOrResumeThreadAsync(tenantId, cancellationToken);

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

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                data: new { threadId, responseLength = responseText.Length },
                textContent: responseText,
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
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
}

/// <summary>
/// Configuration for AgentService node read from <c>ConfigJson</c>.
/// </summary>
internal sealed record AgentServiceNodeConfig
{
    /// <summary>
    /// Tenant identifier used to scope the Redis thread cache key
    /// (<c>agent-thread:{tenantId}</c>). Required.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// User message sent to the Agent thread. Required.
    /// Supports template variable substitution when the orchestrator pre-renders ConfigJson.
    /// </summary>
    public string? Prompt { get; init; }
}
