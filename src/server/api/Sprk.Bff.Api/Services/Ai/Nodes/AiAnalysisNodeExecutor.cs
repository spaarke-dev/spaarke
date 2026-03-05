using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Schemas;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for AI analysis actions that delegate to existing tool handlers.
/// Bridges the node-based orchestration system to the IAnalysisToolHandler pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This executor reuses the existing tool handler infrastructure per ADR-013
/// "reuse existing tool handler infrastructure". It:
/// </para>
/// <list type="bullet">
/// <item>Converts NodeExecutionContext to ToolExecutionContext</item>
/// <item>Looks up the appropriate handler from IToolHandlerRegistry</item>
/// <item>Executes the handler and converts ToolResult to NodeOutput</item>
/// <item>Tracks metrics (tokens, duration) from the tool execution</item>
/// </list>
/// <para>
/// Registered as Singleton (required by NodeExecutorRegistry). Uses IServiceProvider
/// to resolve IToolHandlerRegistry per execution since the registry is Scoped.
/// </para>
/// </remarks>
public sealed class AiAnalysisNodeExecutor : INodeExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AiAnalysisNodeExecutor> _logger;

    public AiAnalysisNodeExecutor(
        IServiceProvider serviceProvider,
        ILogger<AiAnalysisNodeExecutor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.AiAnalysis
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        // AI analysis requires a tool
        if (context.Tool is null)
        {
            errors.Add("AI analysis node requires a tool to be configured");
        }
        else
        {
            // Tool must have a handler class
            if (string.IsNullOrWhiteSpace(context.Tool.HandlerClass))
            {
                errors.Add($"Tool '{context.Tool.Name}' does not have a handler class configured");
            }
            else
            {
                // Handler must exist in registry (resolved per-call since IToolHandlerRegistry is Scoped)
                using var scope = _serviceProvider.CreateScope();
                var toolHandlerRegistry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();
                var handler = toolHandlerRegistry.GetHandler(context.Tool.HandlerClass);
                if (handler is null)
                {
                    var availableHandlers = toolHandlerRegistry.GetRegisteredHandlerIds();
                    errors.Add(
                        $"Tool handler '{context.Tool.HandlerClass}' is not registered. " +
                        $"Available handlers: [{string.Join(", ", availableHandlers)}]");
                }
            }
        }

        // Document context required for analysis
        if (context.Document is null)
        {
            errors.Add("AI analysis node requires document context");
        }
        else if (string.IsNullOrWhiteSpace(context.Document.ExtractedText))
        {
            errors.Add("Document has no extracted text for analysis");
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

        _logger.LogDebug(
            "Executing AI analysis node {NodeId} ({NodeName}) with tool {ToolName}",
            context.Node.Id,
            context.Node.Name,
            context.Tool?.Name ?? "none");

        try
        {
            // Validate first
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

            // Resolve IToolHandlerRegistry from scope (Scoped service, executor is Singleton)
            using var scope = _serviceProvider.CreateScope();
            var toolHandlerRegistry = scope.ServiceProvider.GetRequiredService<IToolHandlerRegistry>();

            // Get the tool handler
            var tool = context.Tool!;
            var handler = toolHandlerRegistry.GetHandler(tool.HandlerClass!);
            if (handler is null)
            {
                var availableHandlers = toolHandlerRegistry.GetRegisteredHandlerIds();
                _logger.LogWarning(
                    "Tool handler '{HandlerClass}' not found for tool '{ToolName}'. " +
                    "Available handlers: [{AvailableHandlers}]",
                    tool.HandlerClass, tool.Name, string.Join(", ", availableHandlers));

                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    $"Tool handler '{tool.HandlerClass}' not found. Available handlers: [{string.Join(", ", availableHandlers)}]",
                    NodeErrorCodes.ToolHandlerNotFound,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Convert to tool execution context (async: resolves JPS $ref entries)
            var toolContext = await CreateToolExecutionContextAsync(
                context, scope.ServiceProvider, cancellationToken);

            // Convert AnalysisTool to the handler's expected format
            var analysisTool = tool;

            // Validate with handler
            var toolValidation = handler.Validate(toolContext, analysisTool);
            if (!toolValidation.IsValid)
            {
                _logger.LogWarning(
                    "Tool handler validation failed for node {NodeId}: {Errors}",
                    context.Node.Id,
                    string.Join(", ", toolValidation.Errors));

                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", toolValidation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Execute the tool handler — streaming or blocking path
            _logger.LogDebug(
                "Calling tool handler {HandlerId} for node {NodeId}",
                handler.HandlerId,
                context.Node.Id);

            ToolResult toolResult;

            // Per-token streaming path: use StreamExecuteAsync when the handler supports it
            // and the caller provided a token callback (ADR-014: do not cache tokens).
            if (handler is IStreamingAnalysisToolHandler streamingHandler
                && context.OnTokenReceived != null)
            {
                _logger.LogDebug(
                    "Using streaming path for node {NodeId} with handler {HandlerId}",
                    context.Node.Id, handler.HandlerId);

                ToolResult? streamResult = null;

                await foreach (var evt in streamingHandler.StreamExecuteAsync(
                    toolContext, analysisTool, cancellationToken))
                {
                    if (evt is ToolStreamEvent.Token token)
                    {
                        // Forward immediately to SSE — no buffering per ADR-014
                        await context.OnTokenReceived(token.Text);
                    }
                    else if (evt is ToolStreamEvent.Completed completed)
                    {
                        streamResult = completed.Result;
                    }
                }

                toolResult = streamResult ?? ToolResult.Error(
                    handler.HandlerId,
                    analysisTool.Id,
                    analysisTool.Name,
                    "Streaming completed without a Completed event",
                    "STREAM_INCOMPLETE");
            }
            else
            {
                // Blocking path: handler does not support streaming or no callback provided
                toolResult = await handler.ExecuteAsync(
                    toolContext,
                    analysisTool,
                    cancellationToken);
            }

            // Convert tool result to node output
            var nodeOutput = ConvertToNodeOutput(context, toolResult, startedAt);

            _logger.LogDebug(
                "AI analysis node {NodeId} completed successfully. " +
                "Tokens: {TokensIn}/{TokensOut}, Duration: {Duration}ms",
                context.Node.Id,
                nodeOutput.Metrics.TokensIn,
                nodeOutput.Metrics.TokensOut,
                nodeOutput.Metrics.DurationMs);

            return nodeOutput;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "AI analysis node {NodeId} was cancelled",
                context.Node.Id);

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
                "AI analysis node {NodeId} failed with error: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Internal error: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Creates a ToolExecutionContext from the NodeExecutionContext.
    /// Passes Action.SystemPrompt and Skill context so tool handlers
    /// use the Action's prompt as primary instruction (Option A).
    /// </summary>
    /// <remarks>
    /// <para>
    /// If the node's <c>ConfigJson</c> contains a <c>promptSchemaOverride</c> and the
    /// Action's system prompt is in JPS format, the override is merged into the base
    /// schema before passing to the tool handler. See <see cref="PromptSchemaOverrideMerger"/>.
    /// </para>
    /// <para>
    /// After prompt assembly, JPS <c>$ref</c> entries in the <c>scopes</c> section are
    /// resolved against Dataverse via <see cref="IScopeResolverService"/>. Resolved
    /// knowledge and skill references are populated into the context so that
    /// <see cref="PromptSchemaRenderer"/> can merge them into the assembled prompt.
    /// </para>
    /// </remarks>
    private async Task<ToolExecutionContext> CreateToolExecutionContextAsync(
        NodeExecutionContext context,
        IServiceProvider scopedProvider,
        CancellationToken cancellationToken)
    {
        // Build previous results dictionary from node outputs
        var previousResults = new Dictionary<string, ToolResult>();
        foreach (var (varName, output) in context.PreviousOutputs)
        {
            // If the previous node had tool results, use the first one
            var firstToolResult = output.ToolResults.FirstOrDefault();
            if (firstToolResult is not null)
            {
                previousResults[varName] = firstToolResult;
            }
        }

        // Build knowledge context from resolved scopes
        var knowledgeContext = BuildKnowledgeContext(context.Scopes);

        // Build skill context from resolved scopes (prompt fragments)
        var skillContext = BuildSkillContext(context.Scopes);

        // Pass Action's SystemPrompt — this is the primary AI instruction.
        // Tool handlers should use this instead of their internal defaults.
        var actionSystemPrompt = !string.IsNullOrWhiteSpace(context.Action.SystemPrompt)
            ? context.Action.SystemPrompt
            : null;

        // Merge node-level promptSchemaOverride into the base prompt when both are JPS.
        // The override comes from ConfigJson.promptSchemaOverride on the playbook node.
        actionSystemPrompt = ApplyPromptSchemaOverride(actionSystemPrompt, context.Node.ConfigJson);

        // Resolve JPS $ref entries to Dataverse records
        var (additionalKnowledge, additionalSkills) = await ResolveJpsRefsAsync(
            actionSystemPrompt, scopedProvider, cancellationToken);

        // Extract template parameters from ConfigJson (if present)
        var templateParameters = ExtractTemplateParameters(context.Node.ConfigJson);

        return new ToolExecutionContext
        {
            AnalysisId = context.RunId,
            TenantId = context.TenantId,
            Document = context.Document!,
            PreviousResults = previousResults,
            UserContext = context.UserContext,
            ActionSystemPrompt = actionSystemPrompt,
            SkillContext = skillContext,
            KnowledgeContext = knowledgeContext,
            DownstreamNodes = context.DownstreamNodes,
            MaxTokens = context.MaxTokens,
            Temperature = context.Temperature,
            ModelDeploymentId = context.ModelDeploymentId ?? context.Node.ModelDeploymentId,
            CorrelationId = context.CorrelationId,
            CreatedAt = context.CreatedAt,
            TemplateParameters = templateParameters,
            AdditionalKnowledge = additionalKnowledge,
            AdditionalSkills = additionalSkills
        };
    }

    /// <summary>
    /// Resolves JPS <c>$ref</c> entries from the action system prompt by querying
    /// Dataverse for matching knowledge and skill records.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses <see cref="JpsRefResolver"/> to extract <c>knowledge:</c> and <c>skill:</c>
    /// references from the <c>scopes</c> section, then resolves each against
    /// <see cref="IScopeResolverService.GetKnowledgeByNameAsync"/> and
    /// <see cref="IScopeResolverService.GetSkillByNameAsync"/> respectively.
    /// </para>
    /// <para>
    /// Resolution within each type runs in parallel via <see cref="Task.WhenAll"/>.
    /// Unresolved references are silently skipped (graceful degradation).
    /// </para>
    /// </remarks>
    /// <param name="actionSystemPrompt">The (possibly merged) action system prompt. May be null or non-JPS.</param>
    /// <param name="scopedProvider">Scoped service provider for resolving <see cref="IScopeResolverService"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple of resolved knowledge and skill reference lists. Both lists are null
    /// when the prompt is null, not JPS format, or contains no <c>$ref</c> entries.
    /// </returns>
    private async Task<(IReadOnlyList<ResolvedKnowledgeRef>?, IReadOnlyList<ResolvedSkillRef>?)> ResolveJpsRefsAsync(
        string? actionSystemPrompt,
        IServiceProvider scopedProvider,
        CancellationToken cancellationToken)
    {
        // Fast path: skip resolution when prompt is null or not JPS format
        if (string.IsNullOrWhiteSpace(actionSystemPrompt) || !IsJpsFormat(actionSystemPrompt))
            return (null, null);

        var knowledgeRefs = JpsRefResolver.ExtractKnowledgeRefs(actionSystemPrompt);
        var skillRefs = JpsRefResolver.ExtractSkillRefs(actionSystemPrompt);

        if (knowledgeRefs.Count == 0 && skillRefs.Count == 0)
            return (null, null);

        var scopeResolver = scopedProvider.GetRequiredService<IScopeResolverService>();

        // Resolve knowledge refs in parallel
        IReadOnlyList<ResolvedKnowledgeRef>? resolvedKnowledge = null;
        if (knowledgeRefs.Count > 0)
        {
            var knowledgeTasks = knowledgeRefs.Select(async kRef =>
            {
                var record = await scopeResolver.GetKnowledgeByNameAsync(kRef.Name, cancellationToken);
                if (record is null)
                {
                    _logger.LogDebug(
                        "JPS $ref knowledge '{KnowledgeName}' not found in Dataverse; skipping",
                        kRef.Name);
                    return null;
                }

                return new ResolvedKnowledgeRef(
                    Name: record.Name,
                    Content: record.Content ?? string.Empty,
                    Label: kRef.Label);
            });

            var results = await Task.WhenAll(knowledgeTasks);
            var filtered = results.Where(r => r is not null).Cast<ResolvedKnowledgeRef>().ToList();
            resolvedKnowledge = filtered.Count > 0 ? filtered : null;
        }

        // Resolve skill refs in parallel
        IReadOnlyList<ResolvedSkillRef>? resolvedSkills = null;
        if (skillRefs.Count > 0)
        {
            var skillTasks = skillRefs.Select(async sRef =>
            {
                var record = await scopeResolver.GetSkillByNameAsync(sRef, cancellationToken);
                if (record is null)
                {
                    _logger.LogDebug(
                        "JPS $ref skill '{SkillName}' not found in Dataverse; skipping",
                        sRef);
                    return null;
                }

                return new ResolvedSkillRef(
                    Name: record.Name,
                    PromptFragment: record.PromptFragment ?? string.Empty);
            });

            var results = await Task.WhenAll(skillTasks);
            var filtered = results.Where(r => r is not null).Cast<ResolvedSkillRef>().ToList();
            resolvedSkills = filtered.Count > 0 ? filtered : null;
        }

        var knowledgeCount = resolvedKnowledge?.Count ?? 0;
        var skillCount = resolvedSkills?.Count ?? 0;

        if (knowledgeCount > 0 || skillCount > 0)
        {
            _logger.LogDebug(
                "Resolved JPS $ref entries: {KnowledgeCount} knowledge, {SkillCount} skills",
                knowledgeCount, skillCount);
        }

        return (resolvedKnowledge, resolvedSkills);
    }

    /// <summary>
    /// Applies a node-level <c>promptSchemaOverride</c> from ConfigJson to the base
    /// Action system prompt. Only applies when the base prompt is in JPS format and
    /// the override can be extracted and parsed.
    /// </summary>
    /// <param name="basePrompt">The Action's system prompt (flat text or JPS JSON).</param>
    /// <param name="configJson">The node's ConfigJson (may contain <c>promptSchemaOverride</c>).</param>
    /// <returns>
    /// The merged JPS JSON string if both base and override are present; otherwise the
    /// original <paramref name="basePrompt"/> unchanged.
    /// </returns>
    private string? ApplyPromptSchemaOverride(string? basePrompt, string? configJson)
    {
        if (string.IsNullOrWhiteSpace(basePrompt) || string.IsNullOrWhiteSpace(configJson))
            return basePrompt;

        // Only merge when the base prompt is JPS format
        if (!IsJpsFormat(basePrompt))
            return basePrompt;

        // Extract override from ConfigJson
        var schemaOverride = PromptSchemaOverrideMerger.ExtractOverride(configJson);
        if (schemaOverride is null)
            return basePrompt;

        try
        {
            // Parse the base prompt as PromptSchema
            var baseSchema = JsonSerializer.Deserialize<PromptSchema>(basePrompt, JpsDeserializeOptions);
            if (baseSchema is null)
                return basePrompt;

            // Merge base + override
            var merged = PromptSchemaOverrideMerger.Merge(baseSchema, schemaOverride);

            // Re-serialize to JSON
            var mergedJson = JsonSerializer.Serialize(merged, JpsSerializeOptions);

            _logger.LogDebug(
                "Applied promptSchemaOverride from node ConfigJson to Action system prompt");

            return mergedJson;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse or merge promptSchemaOverride; using base prompt unchanged");
            return basePrompt;
        }
    }

    /// <summary>
    /// Extracts the <c>templateParameters</c> dictionary from a node's ConfigJson.
    /// Returns null if ConfigJson is missing, malformed, or does not contain templateParameters.
    /// </summary>
    /// <param name="configJson">The node's ConfigJson string (may be null or empty).</param>
    /// <returns>
    /// A dictionary of template parameter names to their values, or null if no parameters found.
    /// </returns>
    private Dictionary<string, object?>? ExtractTemplateParameters(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("templateParameters", out var paramsElement))
                return null;

            if (paramsElement.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning(
                    "ConfigJson templateParameters is not an object (found {ValueKind}); ignoring",
                    paramsElement.ValueKind);
                return null;
            }

            var result = new Dictionary<string, object?>();
            foreach (var prop in paramsElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetRawText()
                };
            }

            return result.Count > 0 ? result : null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse templateParameters from ConfigJson; using null fallback");
            return null;
        }
    }

    /// <summary>
    /// Detects whether a raw prompt string is in JPS format.
    /// Matches the same detection logic as <see cref="PromptSchemaRenderer"/>.
    /// </summary>
    private static bool IsJpsFormat(string rawPrompt)
    {
        return rawPrompt.TrimStart().StartsWith('{') && rawPrompt.Contains("\"$schema\"");
    }

    private static readonly JsonSerializerOptions JpsDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions JpsSerializeOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds knowledge context string from resolved scopes.
    /// </summary>
    private static string? BuildKnowledgeContext(ResolvedScopes scopes)
    {
        if (scopes.Knowledge.Length == 0)
            return null;

        var contextParts = new List<string>();

        foreach (var knowledge in scopes.Knowledge)
        {
            if (knowledge.Type == KnowledgeType.Inline && !string.IsNullOrWhiteSpace(knowledge.Content))
            {
                contextParts.Add($"[{knowledge.Name}]\n{knowledge.Content}");
            }
            // TODO: Document and RagIndex types will be resolved in PlaybookOrchestrationService
            // and pre-populated into knowledge.Content before reaching the executor
        }

        return contextParts.Count > 0
            ? string.Join("\n\n", contextParts)
            : null;
    }

    /// <summary>
    /// Builds skill context string from resolved scopes.
    /// Skills are prompt fragments that provide additional instructions or focus areas.
    /// </summary>
    private static string? BuildSkillContext(ResolvedScopes scopes)
    {
        if (scopes.Skills.Length == 0)
            return null;

        var contextParts = new List<string>();

        foreach (var skill in scopes.Skills)
        {
            if (!string.IsNullOrWhiteSpace(skill.PromptFragment))
            {
                contextParts.Add($"[{skill.Name}]\n{skill.PromptFragment}");
            }
        }

        return contextParts.Count > 0
            ? string.Join("\n\n", contextParts)
            : null;
    }

    /// <summary>
    /// Converts a ToolResult to NodeOutput.
    /// </summary>
    private static NodeOutput ConvertToNodeOutput(
        NodeExecutionContext context,
        ToolResult toolResult,
        DateTimeOffset startedAt)
    {
        if (!toolResult.Success)
        {
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                toolResult.ErrorMessage ?? "Tool execution failed",
                toolResult.ErrorCode ?? NodeErrorCodes.InternalError,
                NodeExecutionMetrics.FromToolMetadata(toolResult.Execution));
        }

        return new NodeOutput
        {
            NodeId = context.Node.Id,
            OutputVariable = context.Node.OutputVariable,
            Success = true,
            TextContent = toolResult.Summary,
            StructuredData = toolResult.Data,
            Confidence = toolResult.Confidence,
            Metrics = NodeExecutionMetrics.FromToolMetadata(toolResult.Execution),
            ToolResults = new[] { toolResult },
            Warnings = toolResult.Warnings
        };
    }
}
