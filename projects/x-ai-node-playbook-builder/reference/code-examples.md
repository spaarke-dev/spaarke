# Node-Based Playbook Builder - Code Examples Reference

> **Purpose**: Implementation reference code for the Node-Based Playbook Builder architecture.
> **Usage**: Optional reference during implementation. Claude Code should adapt these patterns to actual codebase needs.
> **Related**: See `NODE-PLAYBOOK-BUILDER-DESIGN-V2.md` for architecture and design rationale.

---

## Table of Contents

1. [Dataverse Entity Models](#1-dataverse-entity-models)
2. [Extended Scope Resolver](#2-extended-scope-resolver)
3. [Playbook Orchestration Service](#3-playbook-orchestration-service)
4. [Node Executors](#4-node-executors)
5. [Execution Graph](#5-execution-graph)
6. [Template Engine](#6-template-engine)
7. [API Endpoints](#7-api-endpoints)
8. [DI Registration](#8-di-registration)

---

## 1. Dataverse Entity Models

### PlaybookNode Record

```csharp
/// <summary>
/// Playbook node wrapping an action with execution metadata.
/// Maps to sprk_playbooknode Dataverse entity.
/// </summary>
public record PlaybookNode
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Reference to parent playbook.</summary>
    public required Guid PlaybookId { get; init; }

    /// <summary>Reference to the action this node executes.</summary>
    public required Guid ActionId { get; init; }

    /// <summary>Optional AI model override for this node.</summary>
    public Guid? AiModelDeploymentId { get; init; }

    /// <summary>Execution order within the playbook (for sequential execution).</summary>
    public int SortOrder { get; init; }

    /// <summary>
    /// JSON array of node IDs this node depends on.
    /// Empty array or null means no dependencies (can run first).
    /// </summary>
    public string? DependsOnJson { get; init; }

    /// <summary>
    /// JSON object mapping input variables to source expressions.
    /// Example: {"documentText": "{{node1.extractedText}}", "context": "{{input.userQuery}}"}
    /// </summary>
    public string? InputMappingJson { get; init; }

    /// <summary>
    /// JSON object for node-specific configuration overrides.
    /// </summary>
    public string? ConfigurationJson { get; init; }

    /// <summary>Whether this node is active in the playbook.</summary>
    public bool IsActive { get; init; } = true;
}
```

### AiModelDeployment Record

```csharp
/// <summary>
/// AI model deployment configuration.
/// Maps to sprk_aimodeldeployment Dataverse entity.
/// </summary>
public record AiModelDeployment
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Model identifier (e.g., "gpt-4o", "gpt-4o-mini", "o1-preview").</summary>
    public required string ModelId { get; init; }

    /// <summary>Azure OpenAI deployment name.</summary>
    public required string DeploymentName { get; init; }

    /// <summary>Endpoint URL (if different from default).</summary>
    public string? EndpointUrl { get; init; }

    /// <summary>Default max tokens for this deployment.</summary>
    public int DefaultMaxTokens { get; init; } = 4096;

    /// <summary>Default temperature setting.</summary>
    public double DefaultTemperature { get; init; } = 0.3;

    /// <summary>Whether this deployment supports streaming.</summary>
    public bool SupportsStreaming { get; init; } = true;

    /// <summary>Whether this is the default deployment for new nodes.</summary>
    public bool IsDefault { get; init; }
}
```

### PlaybookRun and NodeRun Records

```csharp
/// <summary>
/// Tracks a single execution of a playbook.
/// Maps to sprk_playbookrun Dataverse entity.
/// </summary>
public record PlaybookRun
{
    public Guid Id { get; init; }
    public required Guid PlaybookId { get; init; }
    public required Guid DocumentId { get; init; }
    public string? TenantId { get; init; }
    public string? UserId { get; init; }

    public PlaybookRunStatus Status { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>JSON object containing all node outputs keyed by node name.</summary>
    public string? OutputsJson { get; init; }

    /// <summary>Error message if status is Failed.</summary>
    public string? ErrorMessage { get; init; }
}

public enum PlaybookRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Tracks execution of a single node within a playbook run.
/// Maps to sprk_playbooknoderun Dataverse entity.
/// </summary>
public record PlaybookNodeRun
{
    public Guid Id { get; init; }
    public required Guid PlaybookRunId { get; init; }
    public required Guid PlaybookNodeId { get; init; }

    public PlaybookRunStatus Status { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>JSON object containing this node's output.</summary>
    public string? OutputJson { get; init; }

    /// <summary>Error message if status is Failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Token usage for AI nodes.</summary>
    public int? TokensUsed { get; init; }
}
```

### Extended AnalysisAction

```csharp
/// <summary>
/// Extended action definition with ActionType.
/// Add ActionType field to existing AnalysisAction record.
/// </summary>
public record AnalysisAction
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string SystemPrompt { get; init; } = string.Empty;
    public int SortOrder { get; init; }

    /// <summary>Type of action for execution routing.</summary>
    public ActionType ActionType { get; init; } = ActionType.AiAnalysis;
}

/// <summary>
/// Action types for execution routing.
/// </summary>
public enum ActionType
{
    /// <summary>AI-powered document analysis (existing behavior).</summary>
    AiAnalysis = 0,

    /// <summary>AI completion/generation without document.</summary>
    AiCompletion = 1,

    /// <summary>Create a task record in Dataverse.</summary>
    CreateTask = 10,

    /// <summary>Send an email notification.</summary>
    SendEmail = 11,

    /// <summary>Update a Dataverse record.</summary>
    UpdateRecord = 12,

    /// <summary>Conditional branching based on previous outputs.</summary>
    Condition = 20,

    /// <summary>Transform data between formats.</summary>
    Transform = 21,

    /// <summary>Custom action with handler class.</summary>
    Custom = 99
}
```

---

## 2. Extended Scope Resolver

### Interface Extension

```csharp
// Add to IScopeResolverService interface

/// <summary>
/// Load scopes from a specific node's N:N relationships.
/// </summary>
/// <param name="nodeId">Playbook node entity ID.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Resolved scopes from node's relationships.</returns>
Task<ResolvedScopes> ResolveNodeScopesAsync(
    Guid nodeId,
    CancellationToken cancellationToken);

/// <summary>
/// Get action with extended properties including ActionType.
/// </summary>
/// <param name="actionId">Action entity ID.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Extended action definition or null if not found.</returns>
Task<AnalysisAction?> GetActionExtendedAsync(
    Guid actionId,
    CancellationToken cancellationToken);

/// <summary>
/// Get playbook node by ID with all related data.
/// </summary>
Task<PlaybookNode?> GetPlaybookNodeAsync(
    Guid nodeId,
    CancellationToken cancellationToken);

/// <summary>
/// Get all nodes for a playbook ordered by SortOrder.
/// </summary>
Task<PlaybookNode[]> GetPlaybookNodesAsync(
    Guid playbookId,
    CancellationToken cancellationToken);

/// <summary>
/// Get AI model deployment configuration.
/// </summary>
Task<AiModelDeployment?> GetAiModelDeploymentAsync(
    Guid deploymentId,
    CancellationToken cancellationToken);

/// <summary>
/// Get default AI model deployment.
/// </summary>
Task<AiModelDeployment?> GetDefaultAiModelDeploymentAsync(
    CancellationToken cancellationToken);
```

### Implementation Pattern

```csharp
public async Task<ResolvedScopes> ResolveNodeScopesAsync(
    Guid nodeId,
    CancellationToken cancellationToken)
{
    // Query N:N relationships for the node
    var skillIds = await GetRelatedIdsAsync(
        "sprk_playbooknode_skill",
        "sprk_playbooknodeid",
        nodeId,
        "sprk_analysisskillid",
        cancellationToken);

    var knowledgeIds = await GetRelatedIdsAsync(
        "sprk_playbooknode_knowledge",
        "sprk_playbooknodeid",
        nodeId,
        "sprk_analysisknowledgeid",
        cancellationToken);

    var toolIds = await GetRelatedIdsAsync(
        "sprk_playbooknode_tool",
        "sprk_playbooknodeid",
        nodeId,
        "sprk_analysistoolid",
        cancellationToken);

    // Reuse existing resolution logic
    return await ResolveScopesAsync(
        skillIds.ToArray(),
        knowledgeIds.ToArray(),
        toolIds.ToArray(),
        cancellationToken);
}
```

---

## 3. Playbook Orchestration Service

### Interface

```csharp
/// <summary>
/// Orchestrates multi-node playbook execution.
/// Builds on existing analysis pipeline components.
/// </summary>
public interface IPlaybookOrchestrationService
{
    /// <summary>
    /// Execute a playbook with streaming results.
    /// </summary>
    /// <param name="request">Execution request with playbook and document IDs.</param>
    /// <param name="httpContext">HTTP context for auth/tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async stream of execution chunks.</returns>
    IAsyncEnumerable<PlaybookStreamChunk> ExecutePlaybookAsync(
        PlaybookExecutionRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validate playbook configuration before execution.
    /// </summary>
    Task<PlaybookValidationResult> ValidatePlaybookAsync(
        Guid playbookId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get status of a running or completed playbook execution.
    /// </summary>
    Task<PlaybookRunStatus?> GetRunStatusAsync(
        Guid runId,
        CancellationToken cancellationToken);
}

public record PlaybookExecutionRequest
{
    public required Guid PlaybookId { get; init; }
    public required Guid DocumentId { get; init; }
    public string? UserContext { get; init; }
    public Dictionary<string, object>? InputVariables { get; init; }
}

public record PlaybookStreamChunk
{
    public required string Type { get; init; } // "node_start", "content", "node_complete", "error", "complete"
    public string? NodeName { get; init; }
    public string? Content { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }
}

public record PlaybookValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
```

### Implementation Pattern

```csharp
public class PlaybookOrchestrationService : IPlaybookOrchestrationService
{
    private readonly IScopeResolverService _scopeResolver;
    private readonly INodeExecutorRegistry _executorRegistry;
    private readonly IExecutionGraphBuilder _graphBuilder;
    private readonly ITemplateEngine _templateEngine;
    private readonly ILogger<PlaybookOrchestrationService> _logger;

    public async IAsyncEnumerable<PlaybookStreamChunk> ExecutePlaybookAsync(
        PlaybookExecutionRequest request,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 1. Load playbook and nodes
        var nodes = await _scopeResolver.GetPlaybookNodesAsync(
            request.PlaybookId, cancellationToken);

        if (nodes.Length == 0)
        {
            yield return new PlaybookStreamChunk
            {
                Type = "error",
                Error = "Playbook has no active nodes"
            };
            yield break;
        }

        // 2. Build execution graph
        var graph = _graphBuilder.BuildGraph(nodes);

        // 3. Initialize execution state
        var outputs = new Dictionary<string, object>();
        if (request.InputVariables != null)
        {
            outputs["input"] = request.InputVariables;
        }

        // 4. Execute nodes in topological order
        foreach (var batch in graph.GetExecutionBatches())
        {
            // Execute batch nodes (could be parallelized)
            foreach (var node in batch)
            {
                yield return new PlaybookStreamChunk
                {
                    Type = "node_start",
                    NodeName = node.Name
                };

                // Resolve inputs from template
                var resolvedInputs = _templateEngine.ResolveInputs(
                    node.InputMappingJson, outputs);

                // Get executor for this node's action type
                var action = await _scopeResolver.GetActionExtendedAsync(
                    node.ActionId, cancellationToken);
                var executor = _executorRegistry.GetExecutor(action!.ActionType);

                // Build node execution context
                var nodeContext = await BuildNodeContextAsync(
                    node, action, request, resolvedInputs, outputs, cancellationToken);

                // Execute and stream
                object? nodeOutput = null;
                await foreach (var chunk in executor.ExecuteAsync(nodeContext, cancellationToken))
                {
                    yield return new PlaybookStreamChunk
                    {
                        Type = "content",
                        NodeName = node.Name,
                        Content = chunk.Content,
                        Data = chunk.Data
                    };

                    // Accumulate output
                    if (chunk.Data != null)
                    {
                        nodeOutput = chunk.Data;
                    }
                }

                // Store output for downstream nodes
                outputs[node.Name] = nodeOutput ?? new { };

                yield return new PlaybookStreamChunk
                {
                    Type = "node_complete",
                    NodeName = node.Name,
                    Data = nodeOutput
                };
            }
        }

        yield return new PlaybookStreamChunk
        {
            Type = "complete",
            Data = outputs
        };
    }
}
```

---

## 4. Node Executors

### Executor Interface

```csharp
/// <summary>
/// Executes a specific type of playbook node.
/// </summary>
public interface INodeExecutor
{
    /// <summary>Action types this executor handles.</summary>
    IReadOnlyList<ActionType> SupportedActionTypes { get; }

    /// <summary>Execute the node with streaming output.</summary>
    IAsyncEnumerable<NodeExecutionChunk> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>Validate node configuration.</summary>
    Task<NodeValidationResult> ValidateAsync(
        PlaybookNode node,
        CancellationToken cancellationToken);
}

public record NodeExecutionContext
{
    public required PlaybookNode Node { get; init; }
    public required AnalysisAction Action { get; init; }
    public required ResolvedScopes Scopes { get; init; }
    public AiModelDeployment? ModelDeployment { get; init; }
    public required string TenantId { get; init; }
    public required Guid DocumentId { get; init; }
    public string? DocumentText { get; init; }
    public Dictionary<string, object> ResolvedInputs { get; init; } = new();
    public Dictionary<string, object> PreviousOutputs { get; init; } = new();
    public string? UserContext { get; init; }
}

public record NodeExecutionChunk
{
    public string? Content { get; init; }
    public object? Data { get; init; }
    public bool IsComplete { get; init; }
}

public record NodeValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
```

### AI Analysis Node Executor (Bridge Pattern)

```csharp
/// <summary>
/// Executes AI analysis nodes by bridging to existing analysis pipeline.
/// </summary>
public class AiAnalysisNodeExecutor : INodeExecutor
{
    private readonly IAnalysisContextBuilder _contextBuilder;
    private readonly OpenAiClient _openAiClient;
    private readonly IToolHandlerRegistry _toolRegistry;
    private readonly ILogger<AiAnalysisNodeExecutor> _logger;

    public IReadOnlyList<ActionType> SupportedActionTypes =>
        new[] { ActionType.AiAnalysis, ActionType.AiCompletion };

    public async IAsyncEnumerable<NodeExecutionChunk> ExecuteAsync(
        NodeExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Build prompt using existing context builder (extended)
        var prompt = await _contextBuilder.BuildUserPromptWithContextAsync(
            context.DocumentText ?? string.Empty,
            BuildKnowledgeContext(context.Scopes),
            context.PreviousOutputs,
            context.UserContext,
            cancellationToken);

        // Get model configuration
        var deployment = context.ModelDeployment ??
            await GetDefaultDeploymentAsync(cancellationToken);

        // Build messages
        var messages = new List<ChatMessage>
        {
            new SystemMessage(context.Action.SystemPrompt),
            new UserMessage(prompt)
        };

        // Stream from OpenAI
        var options = new ChatCompletionOptions
        {
            MaxTokens = deployment.DefaultMaxTokens,
            Temperature = (float)deployment.DefaultTemperature
        };

        var accumulated = new StringBuilder();

        await foreach (var update in _openAiClient.CompleteChatStreamingAsync(
            messages, options, cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                accumulated.Append(part.Text);
                yield return new NodeExecutionChunk { Content = part.Text };
            }
        }

        // Execute tools if configured
        if (context.Scopes.Tools.Length > 0)
        {
            var toolResults = await ExecuteToolsAsync(
                context, accumulated.ToString(), cancellationToken);

            yield return new NodeExecutionChunk
            {
                Data = new
                {
                    text = accumulated.ToString(),
                    toolResults
                },
                IsComplete = true
            };
        }
        else
        {
            yield return new NodeExecutionChunk
            {
                Data = new { text = accumulated.ToString() },
                IsComplete = true
            };
        }
    }

    private async Task<Dictionary<string, ToolResult>> ExecuteToolsAsync(
        NodeExecutionContext context,
        string analysisText,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ToolResult>();

        foreach (var tool in context.Scopes.Tools)
        {
            var handler = _toolRegistry.GetHandler(tool.Type);
            if (handler == null) continue;

            var toolContext = new ToolExecutionContext
            {
                AnalysisId = Guid.NewGuid(),
                TenantId = context.TenantId,
                Document = new DocumentContext
                {
                    DocumentId = context.DocumentId,
                    Name = "Analysis Document",
                    ExtractedText = context.DocumentText ?? string.Empty
                },
                PreviousResults = results,
                UserContext = context.UserContext
            };

            var result = await handler.ExecuteAsync(toolContext, tool, cancellationToken);
            results[tool.Name] = result;
        }

        return results;
    }
}
```

### Deterministic Node Executor (Create Task)

```csharp
/// <summary>
/// Executes task creation nodes.
/// </summary>
public class CreateTaskNodeExecutor : INodeExecutor
{
    private readonly IDataverseClient _dataverse;
    private readonly ITemplateEngine _templateEngine;

    public IReadOnlyList<ActionType> SupportedActionTypes =>
        new[] { ActionType.CreateTask };

    public async IAsyncEnumerable<NodeExecutionChunk> ExecuteAsync(
        NodeExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Parse configuration
        var config = JsonSerializer.Deserialize<CreateTaskConfig>(
            context.Node.ConfigurationJson ?? "{}");

        // Resolve template values
        var subject = _templateEngine.Resolve(
            config.SubjectTemplate, context.PreviousOutputs);
        var description = _templateEngine.Resolve(
            config.DescriptionTemplate, context.PreviousOutputs);

        // Create task in Dataverse
        var task = new Entity("task")
        {
            ["subject"] = subject,
            ["description"] = description,
            ["regardingobjectid"] = context.ResolvedInputs.GetValueOrDefault("regardingId"),
            ["scheduledend"] = config.DueDateOffset.HasValue
                ? DateTime.UtcNow.AddDays(config.DueDateOffset.Value)
                : null
        };

        var taskId = await _dataverse.CreateAsync(task, cancellationToken);

        yield return new NodeExecutionChunk
        {
            Content = $"Created task: {subject}",
            Data = new { taskId, subject, description },
            IsComplete = true
        };
    }
}

public record CreateTaskConfig
{
    public string SubjectTemplate { get; init; } = "{{input.subject}}";
    public string DescriptionTemplate { get; init; } = "{{previousNode.text}}";
    public int? DueDateOffset { get; init; }
    public Guid? AssignedToId { get; init; }
}
```

### Executor Registry

```csharp
/// <summary>
/// Registry for node executors by action type.
/// </summary>
public interface INodeExecutorRegistry
{
    INodeExecutor? GetExecutor(ActionType actionType);
    void Register(INodeExecutor executor);
}

public class NodeExecutorRegistry : INodeExecutorRegistry
{
    private readonly Dictionary<ActionType, INodeExecutor> _executors = new();

    public NodeExecutorRegistry(IEnumerable<INodeExecutor> executors)
    {
        foreach (var executor in executors)
        {
            foreach (var actionType in executor.SupportedActionTypes)
            {
                _executors[actionType] = executor;
            }
        }
    }

    public INodeExecutor? GetExecutor(ActionType actionType) =>
        _executors.GetValueOrDefault(actionType);

    public void Register(INodeExecutor executor)
    {
        foreach (var actionType in executor.SupportedActionTypes)
        {
            _executors[actionType] = executor;
        }
    }
}
```

---

## 5. Execution Graph

### Graph Builder

```csharp
/// <summary>
/// Builds execution graph from playbook nodes.
/// </summary>
public interface IExecutionGraphBuilder
{
    ExecutionGraph BuildGraph(PlaybookNode[] nodes);
}

public class ExecutionGraph
{
    private readonly List<List<PlaybookNode>> _batches;

    public ExecutionGraph(List<List<PlaybookNode>> batches)
    {
        _batches = batches;
    }

    /// <summary>
    /// Get execution batches in topological order.
    /// Nodes within a batch can execute in parallel.
    /// </summary>
    public IEnumerable<IReadOnlyList<PlaybookNode>> GetExecutionBatches() => _batches;
}

public class ExecutionGraphBuilder : IExecutionGraphBuilder
{
    public ExecutionGraph BuildGraph(PlaybookNode[] nodes)
    {
        // Build adjacency list
        var nodeMap = nodes.ToDictionary(n => n.Id);
        var dependencies = new Dictionary<Guid, HashSet<Guid>>();
        var dependents = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var node in nodes)
        {
            dependencies[node.Id] = new HashSet<Guid>();
            dependents[node.Id] = new HashSet<Guid>();

            if (!string.IsNullOrEmpty(node.DependsOnJson))
            {
                var deps = JsonSerializer.Deserialize<Guid[]>(node.DependsOnJson) ?? Array.Empty<Guid>();
                foreach (var dep in deps)
                {
                    dependencies[node.Id].Add(dep);
                    if (!dependents.ContainsKey(dep))
                        dependents[dep] = new HashSet<Guid>();
                    dependents[dep].Add(node.Id);
                }
            }
        }

        // Kahn's algorithm for topological sort with batching
        var batches = new List<List<PlaybookNode>>();
        var inDegree = dependencies.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Count);

        while (inDegree.Any(kvp => kvp.Value == 0))
        {
            // Get all nodes with no remaining dependencies
            var batch = inDegree
                .Where(kvp => kvp.Value == 0)
                .Select(kvp => nodeMap[kvp.Key])
                .OrderBy(n => n.SortOrder)
                .ToList();

            if (batch.Count == 0)
                throw new InvalidOperationException("Circular dependency detected in playbook nodes");

            batches.Add(batch);

            // Remove processed nodes and update in-degrees
            foreach (var node in batch)
            {
                inDegree.Remove(node.Id);
                foreach (var dependent in dependents[node.Id])
                {
                    if (inDegree.ContainsKey(dependent))
                        inDegree[dependent]--;
                }
            }
        }

        return new ExecutionGraph(batches);
    }
}
```

---

## 6. Template Engine

### Template Engine Interface

```csharp
/// <summary>
/// Resolves template expressions in node configurations.
/// </summary>
public interface ITemplateEngine
{
    /// <summary>
    /// Resolve a template string with variable substitution.
    /// </summary>
    string Resolve(string template, Dictionary<string, object> context);

    /// <summary>
    /// Resolve input mappings from JSON configuration.
    /// </summary>
    Dictionary<string, object> ResolveInputs(
        string? inputMappingJson,
        Dictionary<string, object> context);
}

public class TemplateEngine : ITemplateEngine
{
    private static readonly Regex TemplatePattern =
        new(@"\{\{(\w+(?:\.\w+)*)\}\}", RegexOptions.Compiled);

    public string Resolve(string template, Dictionary<string, object> context)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        return TemplatePattern.Replace(template, match =>
        {
            var path = match.Groups[1].Value;
            var value = ResolvePath(path, context);
            return value?.ToString() ?? match.Value;
        });
    }

    public Dictionary<string, object> ResolveInputs(
        string? inputMappingJson,
        Dictionary<string, object> context)
    {
        if (string.IsNullOrEmpty(inputMappingJson))
            return new Dictionary<string, object>();

        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(inputMappingJson)
            ?? new Dictionary<string, string>();

        var resolved = new Dictionary<string, object>();
        foreach (var (key, template) in mapping)
        {
            // Check if it's a template expression
            if (template.StartsWith("{{") && template.EndsWith("}}"))
            {
                var path = template[2..^2];
                var value = ResolvePath(path, context);
                if (value != null)
                    resolved[key] = value;
            }
            else
            {
                resolved[key] = Resolve(template, context);
            }
        }

        return resolved;
    }

    private object? ResolvePath(string path, Dictionary<string, object> context)
    {
        var parts = path.Split('.');
        object? current = context;

        foreach (var part in parts)
        {
            if (current == null)
                return null;

            if (current is Dictionary<string, object> dict)
            {
                current = dict.GetValueOrDefault(part);
            }
            else if (current is JsonElement element)
            {
                if (element.TryGetProperty(part, out var prop))
                    current = prop;
                else
                    return null;
            }
            else
            {
                // Try reflection for object properties
                var prop = current.GetType().GetProperty(part);
                current = prop?.GetValue(current);
            }
        }

        return current;
    }
}
```

---

## 7. API Endpoints

### Playbook Execution Endpoints

```csharp
public static class PlaybookEndpoints
{
    public static IEndpointRouteBuilder MapPlaybookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/playbooks")
            .RequireAuthorization()
            .AddEndpointFilter<TenantAuthorizationFilter>();

        // Execute playbook with streaming
        group.MapPost("/{playbookId:guid}/execute", ExecutePlaybook)
            .Produces<PlaybookStreamChunk>(StatusCodes.Status200OK, "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("ExecutePlaybook")
            .WithOpenApi();

        // Validate playbook configuration
        group.MapPost("/{playbookId:guid}/validate", ValidatePlaybook)
            .Produces<PlaybookValidationResult>()
            .WithName("ValidatePlaybook")
            .WithOpenApi();

        // Get run status
        group.MapGet("/runs/{runId:guid}", GetRunStatus)
            .Produces<PlaybookRunStatus>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetPlaybookRunStatus")
            .WithOpenApi();

        return app;
    }

    private static async Task ExecutePlaybook(
        Guid playbookId,
        PlaybookExecutionRequestDto request,
        IPlaybookOrchestrationService orchestrator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";

        var executionRequest = new PlaybookExecutionRequest
        {
            PlaybookId = playbookId,
            DocumentId = request.DocumentId,
            UserContext = request.UserContext,
            InputVariables = request.InputVariables
        };

        await foreach (var chunk in orchestrator.ExecutePlaybookAsync(
            executionRequest, httpContext, cancellationToken))
        {
            var json = JsonSerializer.Serialize(chunk);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static async Task<IResult> ValidatePlaybook(
        Guid playbookId,
        IPlaybookOrchestrationService orchestrator,
        CancellationToken cancellationToken)
    {
        var result = await orchestrator.ValidatePlaybookAsync(playbookId, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetRunStatus(
        Guid runId,
        IPlaybookOrchestrationService orchestrator,
        CancellationToken cancellationToken)
    {
        var status = await orchestrator.GetRunStatusAsync(runId, cancellationToken);
        return status != null
            ? Results.Ok(status)
            : Results.NotFound();
    }
}

public record PlaybookExecutionRequestDto
{
    public required Guid DocumentId { get; init; }
    public string? UserContext { get; init; }
    public Dictionary<string, object>? InputVariables { get; init; }
}
```

---

## 8. DI Registration

### Service Registration Pattern

```csharp
public static class PlaybookServiceExtensions
{
    /// <summary>
    /// Register playbook orchestration services.
    /// Adds 5 new registrations (within ADR-010 limit of 15).
    /// </summary>
    public static IServiceCollection AddPlaybookOrchestration(
        this IServiceCollection services)
    {
        // Core orchestration
        services.AddScoped<IPlaybookOrchestrationService, PlaybookOrchestrationService>();

        // Graph building
        services.AddSingleton<IExecutionGraphBuilder, ExecutionGraphBuilder>();

        // Template engine
        services.AddSingleton<ITemplateEngine, TemplateEngine>();

        // Node executors (registered as collection)
        services.AddScoped<INodeExecutor, AiAnalysisNodeExecutor>();
        services.AddScoped<INodeExecutor, CreateTaskNodeExecutor>();
        services.AddScoped<INodeExecutor, SendEmailNodeExecutor>();
        services.AddScoped<INodeExecutor, ConditionNodeExecutor>();

        // Executor registry
        services.AddScoped<INodeExecutorRegistry>(sp =>
        {
            var executors = sp.GetServices<INodeExecutor>();
            return new NodeExecutorRegistry(executors);
        });

        return services;
    }
}

// In Program.cs
builder.Services.AddPlaybookOrchestration();
```

---

## Notes for Implementation

1. **Bridge Pattern**: `AiAnalysisNodeExecutor` wraps existing pipeline components - don't reimplement
2. **Streaming**: Use `IAsyncEnumerable` throughout for consistent streaming
3. **Error Handling**: Each executor should handle errors gracefully and yield error chunks
4. **Testing**: Unit test executors in isolation, integration test full orchestration
5. **ADR Compliance**: Keep total DI registrations under 15 per ADR-010

---

*Last updated: January 2026*
