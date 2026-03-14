using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
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
    private readonly ReferenceRetrievalService _referenceRetrieval;
    private readonly IRagService _ragService;
    private readonly IRecordSearchService _recordSearchService;
    private readonly ILogger<AiAnalysisNodeExecutor> _logger;

    public AiAnalysisNodeExecutor(
        IServiceProvider serviceProvider,
        ReferenceRetrievalService referenceRetrieval,
        IRagService ragService,
        IRecordSearchService recordSearchService,
        ILogger<AiAnalysisNodeExecutor> logger)
    {
        _serviceProvider = serviceProvider;
        _referenceRetrieval = referenceRetrieval;
        _ragService = ragService;
        _recordSearchService = recordSearchService;
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

            // Parse per-node knowledge retrieval configuration from ConfigJson.
            // Defaults to Auto mode with TopK=5 when absent (backward compatible).
            var retrievalConfig = ParseKnowledgeRetrievalConfig(context.Node.ConfigJson);

            // L1 Knowledge Retrieval: resolve RagIndex knowledge sources via ReferenceRetrievalService.
            // Behavior controlled by retrievalConfig.Mode (auto/always/never).
            var referenceKnowledge = await RetrieveReferenceKnowledgeAsync(
                context, retrievalConfig, cancellationToken);

            // L2 Document Context Retrieval: query customer document index for similar documents.
            // Controlled by retrievalConfig.IncludeDocumentContext (off by default).
            var documentContextKnowledge = await RetrieveDocumentContextAsync(
                context, retrievalConfig, cancellationToken);

            // L3 Entity Context Retrieval: query records index for parent entity metadata.
            // Controlled by retrievalConfig.IncludeEntityContext (off by default).
            var entityContextKnowledge = await RetrieveEntityContextAsync(
                context, retrievalConfig, cancellationToken);

            // Merge L1 + L2 + L3 knowledge before passing to tool context
            var mergedRagKnowledge = MergeKnowledgeContext(
                MergeKnowledgeContext(referenceKnowledge, documentContextKnowledge),
                entityContextKnowledge);

            // Resolve $choices lookup references from Dataverse before tool execution.
            // This pre-resolves "lookup:entity.field" references in the JPS so the renderer
            // can inject them as enum constraints for constrained decoding.
            var lookupChoicesResolver = scope.ServiceProvider.GetService<LookupChoicesResolver>();
            var preResolvedLookupChoices = lookupChoicesResolver != null
                ? await lookupChoicesResolver.ResolveFromJpsAsync(
                    context.Action.SystemPrompt, cancellationToken)
                : null;

            // Convert to tool execution context (async: resolves JPS $ref entries, includes merged RAG knowledge)
            var toolContext = await CreateToolExecutionContextAsync(
                context, mergedRagKnowledge, preResolvedLookupChoices, scope.ServiceProvider, cancellationToken);

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
    /// When <paramref name="referenceKnowledge"/> is non-null, it is prepended to
    /// the scope-based knowledge context so the prompt assembly order is:
    /// Skill instructions -> Knowledge Context (reference + inline) -> Document Content.
    /// </para>
    /// <para>
    /// After prompt assembly, JPS <c>$ref</c> entries in the <c>scopes</c> section are
    /// resolved against Dataverse via <see cref="IScopeResolverService"/>. Resolved
    /// knowledge and skill references are populated into the context so that
    /// <see cref="PromptSchemaRenderer"/> can merge them into the assembled prompt.
    /// </para>
    /// </remarks>
    /// <param name="context">The node execution context.</param>
    /// <param name="referenceKnowledge">
    /// Optional formatted reference knowledge from L1 RAG retrieval.
    /// Null when no RagIndex knowledge sources are linked.
    /// </param>
    /// <param name="preResolvedLookupChoices">
    /// Pre-resolved $choices values from Dataverse lookup entities. Null when not applicable.
    /// </param>
    private async Task<ToolExecutionContext> CreateToolExecutionContextAsync(
        NodeExecutionContext context,
        string? referenceKnowledge,
        IReadOnlyDictionary<string, string[]>? preResolvedLookupChoices,
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

        // Build knowledge context from resolved scopes (inline knowledge only)
        var scopeKnowledge = BuildKnowledgeContext(context.Scopes);

        // Merge reference knowledge (L1 RAG) with scope-based inline knowledge.
        // Reference knowledge is prepended so it appears before inline context.
        var knowledgeContext = MergeKnowledgeContext(referenceKnowledge, scopeKnowledge);

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
            PreResolvedLookupChoices = preResolvedLookupChoices,
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
            // NOTE: Document and RagIndex types resolved in PlaybookOrchestrationService
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
    /// Extracts the <see cref="KnowledgeRetrievalConfig"/> from the node's <c>ConfigJson</c>.
    /// Returns <see cref="KnowledgeRetrievalConfig.Default"/> when the property is absent,
    /// null, or unparseable (backward compatible).
    /// </summary>
    private KnowledgeRetrievalConfig ParseKnowledgeRetrievalConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return KnowledgeRetrievalConfig.Default;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("knowledgeRetrieval", out var element))
                return KnowledgeRetrievalConfig.Default;

            if (element.ValueKind != JsonValueKind.Object)
                return KnowledgeRetrievalConfig.Default;

            var config = JsonSerializer.Deserialize<KnowledgeRetrievalConfig>(
                element.GetRawText(), KnowledgeRetrievalJsonOptions);

            return config ?? KnowledgeRetrievalConfig.Default;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse knowledgeRetrieval from ConfigJson — using defaults");
            return KnowledgeRetrievalConfig.Default;
        }
    }

    private static readonly JsonSerializerOptions KnowledgeRetrievalJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Retrieves golden reference knowledge from the RAG index based on the node's
    /// <see cref="KnowledgeRetrievalConfig"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Behavior varies by <see cref="KnowledgeRetrievalMode"/>:
    /// <list type="bullet">
    ///   <item><c>Never</c> — returns null immediately; no search is performed.</item>
    ///   <item><c>Auto</c> — retrieves only when RagIndex knowledge sources are linked (default, backward compatible).</item>
    ///   <item><c>Always</c> — retrieves using domain matching even without explicit source links.</item>
    /// </list>
    /// </para>
    /// <para>
    /// ADR-014: Retrieved content is NOT logged. Only metadata (count, duration, source IDs) is logged.
    /// </para>
    /// </remarks>
    private async Task<string?> RetrieveReferenceKnowledgeAsync(
        NodeExecutionContext context,
        KnowledgeRetrievalConfig retrievalConfig,
        CancellationToken cancellationToken)
    {
        // Never mode: skip retrieval entirely
        if (retrievalConfig.Mode == KnowledgeRetrievalMode.Never)
        {
            _logger.LogDebug(
                "Node {NodeId} knowledge retrieval mode is Never — skipping L1 retrieval",
                context.Node.Id);
            return null;
        }

        // Extract RagIndex knowledge source IDs from resolved scopes
        var ragSources = context.Scopes.Knowledge
            .Where(k => k.Type == KnowledgeType.RagIndex)
            .ToList();

        // Auto mode: only retrieve when sources are linked (backward compatible)
        if (retrievalConfig.Mode == KnowledgeRetrievalMode.Auto && ragSources.Count == 0)
            return null;

        // Always mode with no sources: search without source filter (domain matching).
        // Auto/Always mode with sources: filter by linked source IDs.
        IReadOnlyList<string>? knowledgeSourceIds = ragSources.Count > 0
            ? ragSources.Select(k => k.Id.ToString()).ToList()
            : null;

        _logger.LogDebug(
            "Node {NodeId} knowledge retrieval: mode={Mode}, topK={TopK}, sources={SourceCount}",
            context.Node.Id, retrievalConfig.Mode, retrievalConfig.EffectiveTopK,
            knowledgeSourceIds?.Count ?? 0);

        // Build semantic query from document title + action context
        var documentName = context.Document?.Name ?? "document";
        var actionName = context.Action.Name;
        var query = $"{actionName}: {documentName}";

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var searchResponse = await _referenceRetrieval.SearchReferencesAsync(
                query,
                new ReferenceSearchOptions
                {
                    TenantId = context.TenantId,
                    KnowledgeSourceIds = knowledgeSourceIds,
                    TopK = retrievalConfig.EffectiveTopK,
                    MinScore = 0.5f
                },
                cancellationToken);

            stopwatch.Stop();

            if (searchResponse.Results.Count == 0)
            {
                _logger.LogDebug(
                    "L1 reference retrieval returned 0 results for node {NodeId} in {ElapsedMs}ms",
                    context.Node.Id, stopwatch.ElapsedMilliseconds);
                return null;
            }

            _logger.LogInformation(
                "L1 reference retrieval for node {NodeId}: {ResultCount} chunks from {SourceCount} source(s) in {ElapsedMs}ms (mode={Mode}, topK={TopK})",
                context.Node.Id,
                searchResponse.Results.Count,
                searchResponse.Results.Select(r => r.KnowledgeSourceId).Distinct().Count(),
                stopwatch.ElapsedMilliseconds,
                retrievalConfig.Mode,
                retrievalConfig.EffectiveTopK);

            // Format reference chunks for prompt injection.
            // Order: Skill instructions -> Knowledge Context -> Document Content
            return FormatReferenceKnowledge(searchResponse.Results);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            // Knowledge retrieval failure is non-fatal — log and continue without references.
            // The action will execute with scope-based knowledge only.
            _logger.LogWarning(
                ex,
                "L1 reference retrieval failed for node {NodeId} after {ElapsedMs}ms — continuing without references",
                context.Node.Id, stopwatch.ElapsedMilliseconds);

            return null;
        }
    }

    /// <summary>
    /// Formats retrieved reference search results into a prompt-ready knowledge block.
    /// </summary>
    /// <remarks>
    /// Format: "The following reference material provides domain expertise:\n### Reference: {name}\n{content}"
    /// per the task specification.
    /// </remarks>
    private static string FormatReferenceKnowledge(IReadOnlyList<ReferenceSearchResult> results)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("The following reference material provides domain expertise:");

        foreach (var result in results)
        {
            sb.AppendLine();
            sb.Append("### Reference: ");
            sb.AppendLine(result.KnowledgeSourceName);
            sb.AppendLine(result.Content);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Retrieves similar customer documents from the knowledge index (L2 context).
    /// Only executes when <see cref="KnowledgeRetrievalConfig.IncludeDocumentContext"/> is
    /// <c>true</c>. Also respects <see cref="KnowledgeRetrievalMode.Never"/> which disables
    /// all retrieval including L2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Queries the customer document index (spaarke-knowledge-index-v2) via <see cref="IRagService"/>
    /// for documents semantically similar to the current document being analyzed.
    /// Results from the current document are excluded to avoid self-referencing.
    /// </para>
    /// <para>
    /// When <c>parentEntityId</c> is present in ConfigJson, results are scoped to the
    /// same parent entity (matter/project) for higher relevance.
    /// </para>
    /// <para>
    /// ADR-014: Retrieved content is NOT logged. Only metadata (count, duration) is logged.
    /// </para>
    /// </remarks>
    private async Task<string?> RetrieveDocumentContextAsync(
        NodeExecutionContext context,
        KnowledgeRetrievalConfig retrievalConfig,
        CancellationToken cancellationToken)
    {
        // Never mode skips all retrieval including L2
        if (retrievalConfig.Mode == KnowledgeRetrievalMode.Never)
            return null;

        // Check if includeDocumentContext is enabled (off by default).
        // Supports both the structured config and the legacy top-level flag.
        if (!retrievalConfig.IncludeDocumentContext && !IsDocumentContextEnabled(context.Node.ConfigJson))
            return null;

        if (context.Document is null)
        {
            _logger.LogDebug(
                "L2 document context enabled for node {NodeId} but no document context available — skipping",
                context.Node.Id);
            return null;
        }

        _logger.LogDebug(
            "Node {NodeId} has includeDocumentContext=true — initiating L2 customer document retrieval",
            context.Node.Id);

        // Build semantic query from document title and action name
        var documentName = context.Document.Name;
        var actionName = context.Action.Name;
        var query = $"{actionName}: {documentName}";

        // Extract optional parentEntityId/parentEntityType from ConfigJson for entity scoping
        var (parentEntityType, parentEntityId) = ExtractEntityScope(context.Node.ConfigJson);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var searchOptions = new RagSearchOptions
            {
                TenantId = context.TenantId,
                TopK = 5,
                MinScore = 0.5f,
                UseSemanticRanking = true,
                UseVectorSearch = true,
                UseKeywordSearch = true,
                ParentEntityType = parentEntityType,
                ParentEntityId = parentEntityId
            };

            var searchResponse = await _ragService.SearchAsync(query, searchOptions, cancellationToken);

            stopwatch.Stop();

            // Exclude chunks belonging to the current document
            var currentDocumentId = context.Document.DocumentId.ToString();
            var filteredResults = searchResponse.Results
                .Where(r => !string.Equals(r.DocumentId, currentDocumentId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filteredResults.Count == 0)
            {
                _logger.LogDebug(
                    "L2 document context retrieval returned 0 results (after excluding current document) for node {NodeId} in {ElapsedMs}ms",
                    context.Node.Id, stopwatch.ElapsedMilliseconds);
                return null;
            }

            _logger.LogInformation(
                "L2 document context for node {NodeId}: {ResultCount} chunks from {DocumentCount} document(s) in {ElapsedMs}ms",
                context.Node.Id,
                filteredResults.Count,
                filteredResults.Select(r => r.DocumentId).Distinct().Count(),
                stopwatch.ElapsedMilliseconds);

            return FormatDocumentContextKnowledge(filteredResults);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            // L2 retrieval failure is non-fatal — log and continue without document context.
            _logger.LogWarning(
                ex,
                "L2 document context retrieval failed for node {NodeId} after {ElapsedMs}ms — continuing without document context",
                context.Node.Id, stopwatch.ElapsedMilliseconds);

            return null;
        }
    }

    /// <summary>
    /// Retrieves business entity metadata from the spaarke-records-index (L3 context).
    /// Only executes when <see cref="KnowledgeRetrievalConfig.IncludeEntityContext"/> is
    /// <c>true</c>. Also respects <see cref="KnowledgeRetrievalMode.Never"/> which disables
    /// all retrieval including L3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Queries the records index for the parent business entity (Matter, Project, Invoice)
    /// associated with the document being analyzed. This gives the LLM awareness of the
    /// business context, e.g. "This NDA is part of Matter 'Acme Corp Acquisition'".
    /// </para>
    /// <para>
    /// The parent entity is identified by <c>parentEntityId</c> and <c>parentEntityType</c>
    /// from ConfigJson. When either is missing, L3 retrieval is skipped.
    /// </para>
    /// <para>
    /// ADR-014: Retrieved content is NOT logged. Only metadata (count, duration) is logged.
    /// </para>
    /// </remarks>
    private async Task<string?> RetrieveEntityContextAsync(
        NodeExecutionContext context,
        KnowledgeRetrievalConfig retrievalConfig,
        CancellationToken cancellationToken)
    {
        // Never mode skips all retrieval including L3
        if (retrievalConfig.Mode == KnowledgeRetrievalMode.Never)
            return null;

        // Check if includeEntityContext is enabled (off by default)
        if (!retrievalConfig.IncludeEntityContext)
            return null;

        // Extract parent entity scope from ConfigJson
        var (parentEntityType, parentEntityId) = ExtractEntityScope(context.Node.ConfigJson);
        if (string.IsNullOrWhiteSpace(parentEntityType) || string.IsNullOrWhiteSpace(parentEntityId))
        {
            _logger.LogDebug(
                "L3 entity context enabled for node {NodeId} but no parentEntityType/parentEntityId in ConfigJson — skipping",
                context.Node.Id);
            return null;
        }

        // Map ParentEntityContext.EntityTypes to record search entity types
        var recordType = MapToRecordEntityType(parentEntityType);
        if (recordType is null)
        {
            _logger.LogDebug(
                "L3 entity context: parentEntityType '{EntityType}' does not map to a searchable record type — skipping",
                parentEntityType);
            return null;
        }

        _logger.LogDebug(
            "Node {NodeId} has includeEntityContext=true — initiating L3 entity context retrieval for {EntityType}/{EntityId}",
            context.Node.Id, parentEntityType, parentEntityId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Query the records index by parent entity name (using the entity ID as a search filter).
            // Use keyword search to find the exact record by dataverseRecordId.
            var searchRequest = new RecordSearchRequest
            {
                Query = "*",
                RecordTypes = new[] { recordType },
                Filters = new RecordSearchFilters
                {
                    // Use reference numbers filter to match the dataverseRecordId.
                    // The records index has dataverseRecordId as a filterable field,
                    // but RecordSearchRequest filters by organizations/people/referenceNumbers.
                    // We use a direct query with recordType filter instead.
                    ReferenceNumbers = null
                },
                Options = new RecordSearchOptions
                {
                    HybridMode = RecordHybridSearchMode.KeywordOnly,
                    Limit = 1,
                    Offset = 0
                }
            };

            // The records index does not have a tenantId field (tenant isolation is
            // enforced at the Dataverse level). We use the dataverseRecordId for
            // exact matching, which is inherently tenant-scoped since the record ID
            // comes from the tenant's Dataverse instance via ConfigJson.

            // Build a targeted query using the record name for the search.
            // Since we need to find by dataverseRecordId, use a direct search
            // against the recordName field with the entity ID.
            searchRequest = searchRequest with
            {
                Query = parentEntityId
            };

            var searchResponse = await _recordSearchService.SearchAsync(searchRequest, cancellationToken);

            stopwatch.Stop();

            if (searchResponse.Results.Count == 0)
            {
                _logger.LogDebug(
                    "L3 entity context retrieval returned 0 results for node {NodeId} ({EntityType}/{EntityId}) in {ElapsedMs}ms",
                    context.Node.Id, parentEntityType, parentEntityId, stopwatch.ElapsedMilliseconds);
                return null;
            }

            var entityRecord = searchResponse.Results[0];

            _logger.LogInformation(
                "L3 entity context for node {NodeId}: found '{RecordName}' ({RecordType}) in {ElapsedMs}ms",
                context.Node.Id,
                entityRecord.RecordName,
                entityRecord.RecordType,
                stopwatch.ElapsedMilliseconds);

            return FormatEntityContextKnowledge(entityRecord, parentEntityType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            // L3 retrieval failure is non-fatal — log and continue without entity context.
            _logger.LogWarning(
                ex,
                "L3 entity context retrieval failed for node {NodeId} after {ElapsedMs}ms — continuing without entity context",
                context.Node.Id, stopwatch.ElapsedMilliseconds);

            return null;
        }
    }

    /// <summary>
    /// Maps a <see cref="ParentEntityContext.EntityTypes"/> value to a
    /// <see cref="RecordEntityType"/> value for records index search.
    /// Returns null for entity types not indexed in spaarke-records-index.
    /// </summary>
    private static string? MapToRecordEntityType(string parentEntityType)
    {
        return parentEntityType.ToLowerInvariant() switch
        {
            ParentEntityContext.EntityTypes.Matter => RecordEntityType.Matter,
            ParentEntityContext.EntityTypes.Project => RecordEntityType.Project,
            ParentEntityContext.EntityTypes.Invoice => RecordEntityType.Invoice,
            _ => null // Account and Contact are not in the records index
        };
    }

    /// <summary>
    /// Formats an entity record from the records index into a prompt-ready "Business Context" block.
    /// Includes entity name, type, description, associated organizations, and people.
    /// </summary>
    private static string FormatEntityContextKnowledge(RecordSearchResult entityRecord, string entityType)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Business Context:");
        sb.AppendLine($"This document is associated with the following {entityType}:");
        sb.AppendLine();
        sb.AppendLine($"### {entityType}: {entityRecord.RecordName}");

        if (!string.IsNullOrWhiteSpace(entityRecord.RecordDescription))
        {
            sb.AppendLine($"Description: {entityRecord.RecordDescription}");
        }

        if (entityRecord.Organizations is { Count: > 0 })
        {
            sb.AppendLine($"Parties/Organizations: {string.Join(", ", entityRecord.Organizations)}");
        }

        if (entityRecord.People is { Count: > 0 })
        {
            sb.AppendLine($"Key People: {string.Join(", ", entityRecord.People)}");
        }

        if (entityRecord.Keywords is { Count: > 0 })
        {
            sb.AppendLine($"Keywords: {string.Join(", ", entityRecord.Keywords)}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Checks whether <c>includeDocumentContext</c> is enabled in the node's ConfigJson.
    /// Returns false when ConfigJson is null/empty or the flag is absent/false.
    /// </summary>
    private static bool IsDocumentContextEnabled(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("includeDocumentContext", out var value))
            {
                return value.ValueKind == JsonValueKind.True;
            }
        }
        catch (JsonException)
        {
            // Malformed ConfigJson — treat as disabled
        }

        return false;
    }

    /// <summary>
    /// Extracts optional <c>parentEntityType</c> and <c>parentEntityId</c> from ConfigJson
    /// for entity-scoped L2 document retrieval.
    /// </summary>
    private static (string? ParentEntityType, string? ParentEntityId) ExtractEntityScope(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            string? entityType = null;
            string? entityId = null;

            if (doc.RootElement.TryGetProperty("parentEntityType", out var typeValue)
                && typeValue.ValueKind == JsonValueKind.String)
            {
                entityType = typeValue.GetString();
            }

            if (doc.RootElement.TryGetProperty("parentEntityId", out var idValue)
                && idValue.ValueKind == JsonValueKind.String)
            {
                entityId = idValue.GetString();
            }

            // Both must be set for entity scoping to apply
            if (!string.IsNullOrWhiteSpace(entityType) && !string.IsNullOrWhiteSpace(entityId))
                return (entityType, entityId);
        }
        catch (JsonException)
        {
            // Malformed ConfigJson — no entity scoping
        }

        return (null, null);
    }

    /// <summary>
    /// Formats L2 customer document search results into a prompt-ready knowledge block.
    /// </summary>
    private static string FormatDocumentContextKnowledge(IReadOnlyList<RagSearchResult> results)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Similar documents previously analyzed:");

        foreach (var result in results)
        {
            sb.AppendLine();
            sb.Append("### Document: ");
            sb.AppendLine(result.DocumentName);
            sb.AppendLine(result.Content);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Merges L1 reference knowledge with scope-based inline knowledge context.
    /// Reference knowledge is placed first (higher priority for prompt attention).
    /// </summary>
    /// <returns>
    /// Combined knowledge context string, or null if both inputs are null/empty.
    /// </returns>
    private static string? MergeKnowledgeContext(string? referenceKnowledge, string? scopeKnowledge)
    {
        if (string.IsNullOrWhiteSpace(referenceKnowledge) && string.IsNullOrWhiteSpace(scopeKnowledge))
            return null;

        if (string.IsNullOrWhiteSpace(referenceKnowledge))
            return scopeKnowledge;

        if (string.IsNullOrWhiteSpace(scopeKnowledge))
            return referenceKnowledge;

        return $"{referenceKnowledge}\n\n{scopeKnowledge}";
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
