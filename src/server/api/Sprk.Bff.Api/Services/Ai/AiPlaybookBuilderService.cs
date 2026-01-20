using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Testing;
using IntentClarificationContext = Sprk.Bff.Api.Models.Ai.ClarificationContext;
using IntentClarificationRequest = Sprk.Bff.Api.Models.Ai.ClarificationRequest;
using IntentClarificationType = Sprk.Bff.Api.Models.Ai.ClarificationType;
// Type aliases to resolve namespace conflicts with Services.Ai types
using IntentOperationType = Sprk.Bff.Api.Models.Ai.OperationType;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates AI-assisted playbook building operations.
/// Handles intent classification, build plan generation, and canvas operations.
/// Implements ADR-001 (BFF orchestration pattern) and ADR-013 (AI Architecture).
/// </summary>
/// <remarks>
/// This service coordinates:
/// 1. Intent classification from user messages
/// 2. Entity resolution with confidence scoring
/// 3. Build plan generation
/// 4. Canvas patch creation and streaming
/// </remarks>
public class AiPlaybookBuilderService : IAiPlaybookBuilderService
{
    private readonly IOpenAiClient _openAiClient;
    private readonly IScopeResolverService _scopeResolver;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiPlaybookBuilderService> _logger;

    // Test Executors for the three test modes (Mock, Quick, Production)
    private readonly IMockTestExecutor _mockTestExecutor;
    private readonly IPlaybookService _playbookService;

    // Confidence thresholds per spec (from IntentConfidenceThresholds in AiIntentClassificationSchema.cs)
    private const double HighConfidenceThreshold = 0.80;   // Execute immediately
    private const double MediumConfidenceThreshold = 0.60; // Execute with confirmation
    private const double EntityConfidenceThreshold = 0.80; // Entity resolution threshold

    // Default model for intent classification (fast and cost-effective)
    private const string DefaultClassificationModel = "gpt-4o-mini";

    #region Builder Scope Constants

    /// <summary>
    /// Builder scope IDs for meta-playbook operations.
    /// These scopes define how the builder itself behaves.
    /// </summary>
    public static class BuilderScopeIds
    {
        // Actions (ACT-BUILDER-*)
        /// <summary>Intent classification system prompt.</summary>
        public const string IntentClassification = "ACT-BUILDER-001";

        /// <summary>Node configuration guidance.</summary>
        public const string NodeConfiguration = "ACT-BUILDER-002";

        /// <summary>Scope selection assistance.</summary>
        public const string ScopeSelection = "ACT-BUILDER-003";

        /// <summary>Scope creation guidance.</summary>
        public const string ScopeCreation = "ACT-BUILDER-004";

        /// <summary>Build plan generation.</summary>
        public const string BuildPlanGeneration = "ACT-BUILDER-005";

        // Skills (SKL-BUILDER-*)
        /// <summary>Lease analysis pattern.</summary>
        public const string LeaseAnalysisPattern = "SKL-BUILDER-001";

        /// <summary>Contract review pattern.</summary>
        public const string ContractReviewPattern = "SKL-BUILDER-002";

        /// <summary>Risk assessment pattern.</summary>
        public const string RiskAssessmentPattern = "SKL-BUILDER-003";

        /// <summary>Node type guide for node generation.</summary>
        public const string NodeTypeGuide = "SKL-BUILDER-004";

        /// <summary>Scope matching guidance.</summary>
        public const string ScopeMatching = "SKL-BUILDER-005";
    }

    // Cache keys and TTL for builder scopes
    private const string BuilderScopeCacheKeyPrefix = "builder_scope:";
    private static readonly TimeSpan BuilderScopeCacheTtl = TimeSpan.FromMinutes(30);

    #endregion

    // JSON serialization options for parsing AI responses
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            // Use case-insensitive enum parsing with naming policy
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper)
        }
    };

    public AiPlaybookBuilderService(
        IOpenAiClient openAiClient,
        IScopeResolverService scopeResolver,
        IMemoryCache cache,
        IMockTestExecutor mockTestExecutor,
        IPlaybookService playbookService,
        ILogger<AiPlaybookBuilderService> logger)
    {
        _openAiClient = openAiClient;
        _scopeResolver = scopeResolver;
        _cache = cache;
        _mockTestExecutor = mockTestExecutor;
        _playbookService = playbookService;
        _logger = logger;
    }

    #region Builder Scope Loading

    /// <summary>
    /// Load a builder scope by its ID (e.g., ACT-BUILDER-001).
    /// Uses caching with 30-minute TTL to avoid repeated Dataverse calls.
    /// Falls back to hardcoded prompts if scope not found in Dataverse.
    /// </summary>
    /// <param name="scopeId">The builder scope ID (e.g., ACT-BUILDER-001).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The scope's system prompt or prompt fragment.</returns>
    public async Task<string> GetBuilderScopePromptAsync(
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{BuilderScopeCacheKeyPrefix}{scopeId}";

        // Check cache first
        if (_cache.TryGetValue<string>(cacheKey, out var cachedPrompt) && !string.IsNullOrEmpty(cachedPrompt))
        {
            _logger.LogDebug("Builder scope {ScopeId} retrieved from cache", scopeId);
            return cachedPrompt;
        }

        _logger.LogDebug("Loading builder scope {ScopeId} from Dataverse", scopeId);

        try
        {
            // Determine scope type from prefix and load accordingly
            string? prompt = null;

            if (scopeId.StartsWith("ACT-BUILDER-", StringComparison.OrdinalIgnoreCase))
            {
                // Load as Action scope - search by name
                var searchResult = await _scopeResolver.SearchScopesAsync(
                    new ScopeSearchQuery
                    {
                        SearchText = scopeId,
                        ScopeTypes = new[] { ScopeType.Action },
                        PageSize = 1
                    },
                    cancellationToken);

                if (searchResult.Actions.Length > 0)
                {
                    prompt = searchResult.Actions[0].SystemPrompt;
                    _logger.LogInformation(
                        "Loaded builder action scope {ScopeId}: {Name}",
                        scopeId, searchResult.Actions[0].Name);
                }
            }
            else if (scopeId.StartsWith("SKL-BUILDER-", StringComparison.OrdinalIgnoreCase))
            {
                // Load as Skill scope - search by name
                var searchResult = await _scopeResolver.SearchScopesAsync(
                    new ScopeSearchQuery
                    {
                        SearchText = scopeId,
                        ScopeTypes = new[] { ScopeType.Skill },
                        PageSize = 1
                    },
                    cancellationToken);

                if (searchResult.Skills.Length > 0)
                {
                    prompt = searchResult.Skills[0].PromptFragment;
                    _logger.LogInformation(
                        "Loaded builder skill scope {ScopeId}: {Name}",
                        scopeId, searchResult.Skills[0].Name);
                }
            }

            // If found, cache and return
            if (!string.IsNullOrEmpty(prompt))
            {
                _cache.Set(cacheKey, prompt, BuilderScopeCacheTtl);
                return prompt;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load builder scope {ScopeId} from Dataverse, using fallback", scopeId);
        }

        // Fallback to hardcoded prompts
        var fallbackPrompt = GetFallbackBuilderPrompt(scopeId);
        _logger.LogInformation("Using fallback prompt for builder scope {ScopeId}", scopeId);

        // Cache the fallback with shorter TTL to allow for Dataverse recovery
        _cache.Set(cacheKey, fallbackPrompt, TimeSpan.FromMinutes(5));

        return fallbackPrompt;
    }

    /// <summary>
    /// Get fallback prompt for builder scope when Dataverse is unavailable.
    /// These prompts ensure the builder continues to function even without Dataverse.
    /// </summary>
    private static string GetFallbackBuilderPrompt(string scopeId)
    {
        return scopeId switch
        {
            BuilderScopeIds.IntentClassification => FallbackPrompts.IntentClassification,
            BuilderScopeIds.NodeConfiguration => FallbackPrompts.NodeConfiguration,
            BuilderScopeIds.ScopeSelection => FallbackPrompts.ScopeSelection,
            BuilderScopeIds.ScopeCreation => FallbackPrompts.ScopeCreation,
            BuilderScopeIds.BuildPlanGeneration => FallbackPrompts.BuildPlanGeneration,
            BuilderScopeIds.LeaseAnalysisPattern => FallbackPrompts.LeaseAnalysisPattern,
            BuilderScopeIds.ContractReviewPattern => FallbackPrompts.ContractReviewPattern,
            BuilderScopeIds.RiskAssessmentPattern => FallbackPrompts.RiskAssessmentPattern,
            BuilderScopeIds.NodeTypeGuide => FallbackPrompts.NodeTypeGuide,
            BuilderScopeIds.ScopeMatching => FallbackPrompts.ScopeMatching,
            _ => "You are an AI assistant helping to build document analysis playbooks."
        };
    }

    /// <summary>
    /// Invalidate cached builder scope to force reload from Dataverse.
    /// </summary>
    /// <param name="scopeId">The builder scope ID to invalidate.</param>
    public void InvalidateBuilderScopeCache(string scopeId)
    {
        var cacheKey = $"{BuilderScopeCacheKeyPrefix}{scopeId}";
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated cache for builder scope {ScopeId}", scopeId);
    }

    /// <summary>
    /// Invalidate all cached builder scopes.
    /// </summary>
    public void InvalidateAllBuilderScopeCaches()
    {
        // Invalidate all known builder scope IDs
        var scopeIds = new[]
        {
            BuilderScopeIds.IntentClassification,
            BuilderScopeIds.NodeConfiguration,
            BuilderScopeIds.ScopeSelection,
            BuilderScopeIds.ScopeCreation,
            BuilderScopeIds.BuildPlanGeneration,
            BuilderScopeIds.LeaseAnalysisPattern,
            BuilderScopeIds.ContractReviewPattern,
            BuilderScopeIds.RiskAssessmentPattern,
            BuilderScopeIds.NodeTypeGuide,
            BuilderScopeIds.ScopeMatching
        };

        foreach (var scopeId in scopeIds)
        {
            InvalidateBuilderScopeCache(scopeId);
        }

        _logger.LogInformation("Invalidated all builder scope caches");
    }

    #endregion

    /// <inheritdoc />
    public async IAsyncEnumerable<BuilderStreamChunk> ProcessMessageAsync(
        BuilderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing builder message: {Message}", request.Message);

        // Build canvas context
        var canvasContext = new CanvasContext
        {
            NodeCount = request.CanvasState.Nodes.Length,
            NodeTypes = request.CanvasState.Nodes.Select(n => n.Type).Distinct().ToArray(),
            IsSaved = request.PlaybookId.HasValue,
            SelectedNodeId = request.CanvasState.Nodes.FirstOrDefault()?.Id
        };

        IntentClassification classification;

        // Check if this is a clarification response - handle differently
        if (request.IsClarificationResponse && request.ClarificationResponse != null)
        {
            _logger.LogDebug(
                "Processing clarification response: {ResponseType}",
                request.ClarificationResponse.ResponseType);

            // Handle cancellation
            if (request.ClarificationResponse.ResponseType == ClarificationResponseType.Cancelled)
            {
                yield return BuilderStreamChunk.Message("Understood. Let me know when you're ready to continue.");
                yield return BuilderStreamChunk.Complete();
                yield break;
            }

            // Re-classify with the additional context from clarification
            var reClassifiedResult = await ReClassifyWithContextAsync(
                request.ClarificationResponse.OriginalMessage ?? request.Message,
                request.ClarificationResponse,
                canvasContext,
                cancellationToken);

            // If still needs clarification (e.g., user rejected), handle that
            if (reClassifiedResult.Operation == IntentOperationType.Clarify)
            {
                var newClarification = await GenerateClarificationAsync(
                    request.Message,
                    canvasContext,
                    reClassifiedResult,
                    cancellationToken);

                yield return CreateClarificationChunk(newClarification, request.Message);
                yield return BuilderStreamChunk.Complete();
                yield break;
            }

            // Convert re-classified result to IntentClassification for processing
            classification = ConvertAiIntentToClassification(reClassifiedResult, request.Message);

            _logger.LogDebug(
                "Re-classified intent: {Intent} with confidence {Confidence}",
                classification.Intent, classification.Confidence);
        }
        else
        {
            // Standard flow: classify intent from the message
            classification = await ClassifyIntentAsync(
                request.Message, canvasContext, cancellationToken);

            _logger.LogDebug(
                "Intent classified: {Intent} with confidence {Confidence}",
                classification.Intent, classification.Confidence);

            // Check if clarification needed
            if (classification.NeedsClarification)
            {
                // Use AI to generate more helpful clarification questions
                var aiClassificationResult = await ClassifyIntentWithAiAsync(
                    request.Message,
                    canvasContext,
                    null,
                    cancellationToken);

                // Generate AI-powered clarification
                var clarification = await GenerateClarificationAsync(
                    request.Message,
                    canvasContext,
                    aiClassificationResult,
                    cancellationToken);

                yield return CreateClarificationChunk(clarification, request.Message);
                yield return BuilderStreamChunk.Complete();
                yield break;
            }
        }

        // Process based on intent - use AI's conversational message if available
        var introMessage = !string.IsNullOrEmpty(classification.Message)
            ? classification.Message
            : $"I understand you want to {GetIntentDescription(classification.Intent)}.";
        yield return BuilderStreamChunk.Message(introMessage);

        // Generate and execute operations based on intent
        await foreach (var chunk in ExecuteIntentAsync(
            classification, request, cancellationToken))
        {
            yield return chunk;
        }

        yield return BuilderStreamChunk.Complete();
    }

    /// <summary>
    /// Create a clarification stream chunk from a ClarificationQuestion.
    /// </summary>
    private static BuilderStreamChunk CreateClarificationChunk(
        ClarificationQuestion clarification,
        string originalMessage)
    {
        // Format the clarification with metadata for the frontend
        var questionWithOptions = clarification.Text;

        // Add options if available
        if (clarification.Options != null && clarification.Options.Length > 0)
        {
            var optionsList = string.Join("\n", clarification.Options.Select((o, i) =>
                $"  {i + 1}. {o.Label}" + (o.Description != null ? $" - {o.Description}" : "")));
            questionWithOptions = $"{clarification.Text}\n\n{optionsList}";
        }

        // Add suggestions if available
        if (clarification.Suggestions != null && clarification.Suggestions.Length > 0)
        {
            var suggestionsList = string.Join(", ", clarification.Suggestions.Take(3).Select(s => $"\"{s}\""));
            questionWithOptions += $"\n\nSuggestions: {suggestionsList}";
        }

        return new BuilderStreamChunk
        {
            Type = BuilderChunkType.Clarification,
            Text = questionWithOptions,
            Metadata = new Dictionary<string, object?>
            {
                ["clarificationId"] = clarification.Id,
                ["clarificationType"] = clarification.Type.ToString(),
                ["options"] = clarification.Options,
                ["suggestions"] = clarification.Suggestions,
                ["allowFreeText"] = clarification.AllowFreeText,
                ["freeTextPlaceholder"] = clarification.FreeTextPlaceholder,
                ["originalMessage"] = originalMessage,
                ["understoodContext"] = clarification.UnderstoodContext,
                ["ambiguityReason"] = clarification.AmbiguityReason
            }
        };
    }

    /// <summary>
    /// Convert an AiIntentResult to the legacy IntentClassification format.
    /// Used for compatibility with ExecuteIntentAsync.
    /// </summary>
    private static IntentClassification ConvertAiIntentToClassification(
        AiIntentResult aiResult,
        string originalMessage)
    {
        var legacyIntent = aiResult.Action switch
        {
            IntentAction.CreatePlaybook => BuilderIntent.CreatePlaybook,
            IntentAction.AddNode => BuilderIntent.AddNode,
            IntentAction.RemoveNode => BuilderIntent.RemoveNode,
            IntentAction.CreateEdge => BuilderIntent.ConnectNodes,
            IntentAction.ConfigureNode => BuilderIntent.ConfigureNode,
            IntentAction.LinkScope => BuilderIntent.LinkScope,
            IntentAction.CreateScope => BuilderIntent.CreateScope,
            IntentAction.SearchScopes => BuilderIntent.SearchScopes,
            IntentAction.TestPlaybook => BuilderIntent.TestPlaybook,
            IntentAction.SavePlaybook => BuilderIntent.SavePlaybook,
            IntentAction.AnswerQuestion => BuilderIntent.AskQuestion,
            IntentAction.DescribeState => BuilderIntent.AskQuestion,
            IntentAction.ProvideGuidance => BuilderIntent.AskQuestion,
            _ => BuilderIntent.Unknown
        };

        // Extract entities from parameters if available
        Dictionary<string, string>? entities = null;
        if (aiResult.Parameters != null)
        {
            entities = new Dictionary<string, string>();
            if (aiResult.Parameters.AddNode != null)
            {
                entities["nodeType"] = aiResult.Parameters.AddNode.NodeType ?? "aiAnalysis";
                if (aiResult.Parameters.AddNode.Label != null)
                    entities["nodeLabel"] = aiResult.Parameters.AddNode.Label;
            }
            if (aiResult.Parameters.RemoveNode != null)
            {
                entities["nodeId"] = aiResult.Parameters.RemoveNode.NodeReference ?? "selected";
            }
            if (aiResult.Parameters.ConfigureNode != null)
            {
                entities["nodeId"] = aiResult.Parameters.ConfigureNode.NodeReference ?? "selected";
                if (aiResult.Parameters.ConfigureNode.Property != null)
                    entities["configKey"] = aiResult.Parameters.ConfigureNode.Property;
                if (aiResult.Parameters.ConfigureNode.Value != null)
                    entities["configValue"] = aiResult.Parameters.ConfigureNode.Value;
            }
            if (aiResult.Parameters.SearchScopes != null)
            {
                entities["scopeName"] = aiResult.Parameters.SearchScopes.Query ?? "";
            }
        }

        return new IntentClassification
        {
            Intent = legacyIntent,
            Confidence = aiResult.Confidence,
            Entities = entities,
            NeedsClarification = false, // Already handled clarification at this point
            Message = aiResult.Message // Pass through the AI's conversational message
        };
    }

    /// <inheritdoc />
    public async Task<BuildPlan> GenerateBuildPlanAsync(
        BuildPlanRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating build plan for goal: {Goal}", request.Goal);

        // Load build plan generation prompt from builder scope (ACT-BUILDER-005)
        var systemPrompt = await GetBuilderScopePromptAsync(
            BuilderScopeIds.BuildPlanGeneration,
            cancellationToken);

        // Load node type guide from builder scope (SKL-BUILDER-004)
        // This provides guidance on which node types to use
        var nodeTypeGuide = await GetBuilderScopePromptAsync(
            BuilderScopeIds.NodeTypeGuide,
            cancellationToken);

        var userPrompt = $"""
            Goal: {request.Goal}
            Document Type: {request.DocumentType ?? "general"}

            ## Node Type Reference
            {nodeTypeGuide}

            Generate a build plan with specific steps to create this playbook.
            """;

        // Call AI for plan generation
        var response = await _openAiClient.GetCompletionAsync(
            $"{systemPrompt}\n\n{userPrompt}",
            cancellationToken: cancellationToken);

        // Generate a meaningful playbook structure based on the goal
        // For now, create a standard document analysis pipeline
        // Full implementation will use AI to customize based on goal
        var steps = new List<ExecutionStep>
        {
            new()
            {
                Order = 1,
                Action = ExecutionStepActions.AddNode,
                Description = "Analyze document content",
                NodeSpec = new NodeSpec
                {
                    Type = PlaybookNodeTypes.AiAnalysis,
                    Label = "Document Analysis",
                    Position = new BuildPlanNodePosition { X = 100, Y = 200 }
                }
            },
            new()
            {
                Order = 2,
                Action = ExecutionStepActions.AddNode,
                Description = "Extract key information based on goal",
                NodeSpec = new NodeSpec
                {
                    Type = PlaybookNodeTypes.AiAnalysis,
                    Label = "Extract Key Info",
                    Position = new BuildPlanNodePosition { X = 400, Y = 200 }
                }
            },
            new()
            {
                Order = 3,
                Action = ExecutionStepActions.AddNode,
                Description = "Generate structured output",
                NodeSpec = new NodeSpec
                {
                    Type = PlaybookNodeTypes.AiAnalysis,
                    Label = "Generate Output",
                    Position = new BuildPlanNodePosition { X = 700, Y = 200 }
                }
            },
            new()
            {
                Order = 4,
                Action = ExecutionStepActions.AddNode,
                Description = "Deliver results",
                NodeSpec = new NodeSpec
                {
                    Type = PlaybookNodeTypes.DeliverOutput,
                    Label = "Deliver Results",
                    Position = new BuildPlanNodePosition { X = 1000, Y = 200 }
                }
            }
        };

        var plan = new BuildPlan
        {
            Summary = $"Build plan for: {request.Goal}",
            Steps = steps.ToArray(),
            EstimatedNodeCount = steps.Count,
            Confidence = 0.85
        };

        _logger.LogInformation("Generated build plan with {StepCount} steps", plan.Steps.Length);

        return plan;
    }

    /// <inheritdoc />
    public async Task<IntentClassification> ClassifyIntentAsync(
        string message,
        CanvasContext? canvasContext,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Classifying intent for message: {Message}", message);

        // Use AI-powered classification
        var aiResult = await ClassifyIntentWithAiAsync(
            message,
            canvasContext,
            DefaultClassificationModel,
            cancellationToken);

        // Convert AiIntentResult to IntentClassification for backward compatibility
        return ConvertToIntentClassification(aiResult, message);
    }

    /// <summary>
    /// Classify user intent using Azure OpenAI structured output.
    /// This is the new AI-powered implementation that replaces rule-based ParseIntent().
    /// </summary>
    /// <param name="message">The user message to classify.</param>
    /// <param name="canvasContext">Current canvas context for disambiguation.</param>
    /// <param name="model">AI model to use (default: gpt-4o-mini).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>AI classification result with structured output.</returns>
    public async Task<AiIntentResult> ClassifyIntentWithAiAsync(
        string message,
        CanvasContext? canvasContext,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var selectedModel = model ?? DefaultClassificationModel;
        _logger.LogDebug(
            "AI intent classification for message: {Message}, Model: {Model}",
            message, selectedModel);

        // Load intent classification prompt from builder scope (ACT-BUILDER-001)
        // Falls back to hardcoded prompt if Dataverse unavailable
        var systemPrompt = await GetBuilderScopePromptAsync(
            BuilderScopeIds.IntentClassification,
            cancellationToken);
        var userPrompt = BuildAiIntentClassificationUserPrompt(message, canvasContext);

        try
        {
            // Call AI for classification
            var response = await _openAiClient.GetCompletionAsync(
                $"{systemPrompt}\n\n{userPrompt}",
                model: selectedModel,
                cancellationToken: cancellationToken);

            // Parse structured JSON output
            var aiResult = ParseAiIntentResult(response, message);

            _logger.LogInformation(
                "AI classified intent: Operation={Operation}, Action={Action}, Confidence={Confidence:F2}",
                aiResult.Operation, aiResult.Action, aiResult.Confidence);

            // Apply confidence threshold logic
            return ApplyConfidenceThresholds(aiResult, canvasContext);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI intent classification response, falling back to rule-based");
            return CreateFallbackResult(message, canvasContext);
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            _logger.LogWarning(ex, "OpenAI circuit breaker open, using rule-based fallback");
            return CreateFallbackResult(message, canvasContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI intent classification failed, using rule-based fallback");
            return CreateFallbackResult(message, canvasContext);
        }
    }

    /// <summary>
    /// Build the system prompt for AI intent classification.
    /// Provides comprehensive instructions for structured output.
    /// </summary>
    private static string BuildAiIntentClassificationSystemPrompt()
    {
        return """
            You are an intent classification system for an AI-assisted playbook builder.
            Your task is to classify user messages and extract relevant parameters.

            ## Available Operations and Actions

            **BUILD Operations** (create new artifacts):
            - CREATE_PLAYBOOK: User wants to create a new playbook from scratch
            - ADD_NODE: User wants to add a node to the canvas
            - CREATE_EDGE: User wants to connect two nodes
            - CREATE_SCOPE: User wants to create a custom scope (action, skill, knowledge, tool)

            **MODIFY Operations** (change existing artifacts):
            - REMOVE_NODE: User wants to remove a node
            - REMOVE_EDGE: User wants to remove a connection
            - CONFIGURE_NODE: User wants to configure a node's settings
            - LINK_SCOPE: User wants to link an existing scope to a node
            - UNLINK_SCOPE: User wants to unlink a scope from a node
            - MODIFY_LAYOUT: User wants to auto-arrange the canvas
            - UNDO: User wants to undo the last operation
            - REDO: User wants to redo an undone operation
            - SAVE_PLAYBOOK: User wants to save the playbook

            **TEST Operations** (execute or validate):
            - TEST_PLAYBOOK: User wants to test/run the playbook (modes: mock, quick, production)
            - VALIDATE_PLAYBOOK: User wants to validate without running

            **EXPLAIN Operations** (provide information):
            - ANSWER_QUESTION: User is asking a question
            - DESCRIBE_STATE: User wants to know the current playbook state
            - PROVIDE_GUIDANCE: User wants suggestions or help

            **SEARCH Operations** (query resources):
            - SEARCH_SCOPES: User wants to find available scopes
            - BROWSE_CATALOG: User wants to browse the scope catalog

            **CLARIFY Operations** (need more information):
            - REQUEST_CLARIFICATION: Intent is unclear, need to ask for clarification
            - CONFIRM_UNDERSTANDING: Need confirmation before destructive action

            ## Node Types
            - aiAnalysis: AI analysis node
            - aiCompletion: AI completion/generation node
            - condition: Conditional branching node
            - deliverOutput: Output delivery node
            - createTask: Task creation node
            - sendEmail: Email sending node
            - wait: Wait/delay node

            ## Scope Types
            - action: Defines what analysis to perform
            - skill: Reusable analysis patterns
            - knowledge: Reference documents and examples
            - tool: External tool integrations

            ## Response Format
            You MUST respond with valid JSON matching this structure:
            {
              "operation": "BUILD|MODIFY|TEST|EXPLAIN|SEARCH|CLARIFY",
              "action": "<ACTION_NAME>",
              "confidence": <0.0-1.0>,
              "parameters": { <action-specific parameters> },
              "clarification": { <only if needed> },
              "reasoning": "<brief explanation>"
            }

            ## Confidence Guidelines
            - >= 0.80: Clear intent with sufficient context
            - 0.60-0.79: Likely intent but may need confirmation
            - < 0.60: Ambiguous, trigger clarification

            ## Important Rules
            1. Always provide confidence based on clarity of the request
            2. Extract relevant parameters when possible (node types, labels, scope references)
            3. Use canvas context to resolve references like "it", "that node", "the last one"
            4. For destructive actions (remove, delete), consider requesting confirmation
            5. For ambiguous pronouns without selected node, request clarification
            6. Return CLARIFY operation when intent cannot be determined
            """;
    }

    /// <summary>
    /// Build the user prompt with message and canvas context.
    /// </summary>
    private static string BuildAiIntentClassificationUserPrompt(
        string message,
        CanvasContext? canvasContext)
    {
        var nodeInfo = canvasContext != null
            ? $"Nodes: {canvasContext.NodeCount}, Types: [{string.Join(", ", canvasContext.NodeTypes)}]"
            : "Canvas: empty";

        var selectedInfo = !string.IsNullOrEmpty(canvasContext?.SelectedNodeId)
            ? $"Selected Node: {canvasContext.SelectedNodeId}"
            : "No node selected";

        var savedInfo = canvasContext?.IsSaved == true ? "Playbook is saved" : "Playbook has unsaved changes";

        return $"""
            ## User Message
            "{message}"

            ## Canvas Context
            {nodeInfo}
            {selectedInfo}
            {savedInfo}

            ## Task
            Classify the intent and extract parameters. Respond with JSON only.
            """;
    }

    /// <summary>
    /// Parse the AI response into AiIntentResult.
    /// Handles JSON parsing with error recovery.
    /// </summary>
    private AiIntentResult ParseAiIntentResult(string response, string originalMessage)
    {
        // Try to extract JSON from the response (AI might include markdown code blocks)
        var jsonContent = ExtractJsonFromResponse(response);

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            _logger.LogWarning("Empty or null JSON content from AI response");
            throw new JsonException("Empty JSON response from AI");
        }

        try
        {
            var result = JsonSerializer.Deserialize<AiIntentResult>(jsonContent, JsonOptions);

            if (result == null)
            {
                throw new JsonException("Deserialized result was null");
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize AI response: {Response}", jsonContent);
            throw;
        }
    }

    /// <summary>
    /// Extract JSON content from AI response, handling markdown code blocks.
    /// </summary>
    private static string ExtractJsonFromResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        var trimmed = response.Trim();

        // Check if wrapped in markdown code block
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            var endIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (endIndex > 7)
            {
                return trimmed[7..endIndex].Trim();
            }
        }
        else if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var endIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (endIndex > 3)
            {
                return trimmed[3..endIndex].Trim();
            }
        }

        // Assume raw JSON
        return trimmed;
    }

    /// <summary>
    /// Apply confidence thresholds and enhance result with clarification if needed.
    /// </summary>
    private AiIntentResult ApplyConfidenceThresholds(
        AiIntentResult result,
        CanvasContext? canvasContext)
    {
        // If already a clarification, return as-is
        if (result.Operation == IntentOperationType.Clarify)
        {
            return result;
        }

        // Low confidence: convert to clarification
        if (result.Confidence < MediumConfidenceThreshold)
        {
            _logger.LogDebug(
                "Low confidence {Confidence:F2} for {Action}, requesting clarification",
                result.Confidence, result.Action);

            return result with
            {
                Operation = IntentOperationType.Clarify,
                Action = IntentAction.RequestClarification,
                Clarification = new IntentClarificationRequest
                {
                    Question = GenerateClarificationQuestion(result),
                    Type = IntentClarificationType.General,
                    AllowFreeform = true,
                    Suggestions = GenerateSuggestions(result),
                    Context = new IntentClarificationContext
                    {
                        Understood = result.Reasoning,
                        Unclear = "Intent confidence is below threshold"
                    }
                }
            };
        }

        // Medium confidence: add confirmation request for destructive actions
        if (result.Confidence < HighConfidenceThreshold && IsDestructiveAction(result.Action))
        {
            _logger.LogDebug(
                "Medium confidence {Confidence:F2} for destructive action {Action}, requesting confirmation",
                result.Confidence, result.Action);

            return result with
            {
                Clarification = new IntentClarificationRequest
                {
                    Question = $"Are you sure you want to {GetActionDescription(result.Action)}?",
                    Type = IntentClarificationType.Confirmation,
                    Options = new ClarificationOption[]
                    {
                        new() { Id = "confirm", Label = "Yes, proceed" },
                        new() { Id = "cancel", Label = "Cancel" }
                    },
                    AllowFreeform = false
                }
            };
        }

        return result;
    }

    /// <summary>
    /// Generate AI-powered clarification questions when intent classification has low confidence.
    /// Uses the AI to generate contextually relevant questions based on the ambiguous message
    /// and current canvas state.
    /// </summary>
    /// <param name="message">The original user message that needs clarification.</param>
    /// <param name="canvasContext">Current canvas context for generating relevant options.</param>
    /// <param name="lowConfidenceResult">The initial low-confidence classification result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured clarification question with options and suggestions.</returns>
    public async Task<ClarificationQuestion> GenerateClarificationAsync(
        string message,
        CanvasContext? canvasContext,
        AiIntentResult? lowConfidenceResult,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Generating clarification for message: {Message}, Initial result: {Action}",
            message, lowConfidenceResult?.Action);

        var systemPrompt = BuildClarificationSystemPrompt();
        var userPrompt = BuildClarificationUserPrompt(message, canvasContext, lowConfidenceResult);

        try
        {
            // Call AI for clarification generation
            var response = await _openAiClient.GetCompletionAsync(
                $"{systemPrompt}\n\n{userPrompt}",
                model: DefaultClassificationModel,
                cancellationToken: cancellationToken);

            // Parse the clarification response
            return ParseClarificationResponse(response, message, lowConfidenceResult);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate AI clarification, using rule-based fallback");
            return CreateFallbackClarification(message, canvasContext, lowConfidenceResult);
        }
    }

    /// <summary>
    /// Re-classify the user's intent with additional context from their clarification response.
    /// This combines the original message with the clarification to provide a more confident classification.
    /// </summary>
    /// <param name="originalMessage">The original user message.</param>
    /// <param name="clarificationResponse">The user's response to the clarification question.</param>
    /// <param name="canvasContext">Current canvas context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new classification result with higher confidence.</returns>
    public async Task<AiIntentResult> ReClassifyWithContextAsync(
        string originalMessage,
        ClarificationResponse clarificationResponse,
        CanvasContext? canvasContext,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Re-classifying with clarification context. ResponseType: {ResponseType}",
            clarificationResponse.ResponseType);

        // Handle cancellation
        if (clarificationResponse.ResponseType == ClarificationResponseType.Cancelled)
        {
            return new AiIntentResult
            {
                Operation = IntentOperationType.Clarify,
                Action = IntentAction.RequestClarification,
                Confidence = 1.0,
                Reasoning = "User cancelled the operation"
            };
        }

        // Handle option selection - map directly to intent
        if (clarificationResponse.ResponseType == ClarificationResponseType.OptionSelected &&
            !string.IsNullOrEmpty(clarificationResponse.SelectedOptionId))
        {
            return await HandleOptionSelectionAsync(
                originalMessage,
                clarificationResponse,
                canvasContext,
                cancellationToken);
        }

        // Handle confirmation/rejection for destructive actions
        if (clarificationResponse.ResponseType == ClarificationResponseType.Confirmed &&
            clarificationResponse.OriginalClassification != null)
        {
            // Return the original classification with boosted confidence
            return clarificationResponse.OriginalClassification with
            {
                Confidence = Math.Max(clarificationResponse.OriginalClassification.Confidence, HighConfidenceThreshold),
                Reasoning = $"Confirmed by user: {clarificationResponse.OriginalClassification.Reasoning}"
            };
        }

        if (clarificationResponse.ResponseType == ClarificationResponseType.Rejected)
        {
            // Need to ask again differently
            return new AiIntentResult
            {
                Operation = IntentOperationType.Clarify,
                Action = IntentAction.RequestClarification,
                Confidence = 0.5,
                Reasoning = "User rejected the suggested action, requesting new clarification",
                Clarification = new IntentClarificationRequest
                {
                    Question = "What would you like to do instead?",
                    Type = IntentClarificationType.General,
                    AllowFreeform = true
                }
            };
        }

        // Handle free text - combine with original for re-classification
        if (clarificationResponse.ResponseType == ClarificationResponseType.FreeText &&
            !string.IsNullOrEmpty(clarificationResponse.FreeTextResponse))
        {
            return await ReClassifyWithFreeTextAsync(
                originalMessage,
                clarificationResponse.FreeTextResponse,
                canvasContext,
                clarificationResponse.ClarificationContext,
                cancellationToken);
        }

        // Fallback - re-classify the original message
        return await ClassifyIntentWithAiAsync(originalMessage, canvasContext, null, cancellationToken);
    }

    /// <summary>
    /// Handle option selection from clarification.
    /// </summary>
    private async Task<AiIntentResult> HandleOptionSelectionAsync(
        string originalMessage,
        ClarificationResponse response,
        CanvasContext? canvasContext,
        CancellationToken cancellationToken)
    {
        var optionId = response.SelectedOptionId!;

        // Check if this is a resolved intent option (from ClarificationOptionExtended)
        if (response.OriginalClassification?.Clarification?.Options != null)
        {
            var selectedOption = response.OriginalClassification.Clarification.Options
                .FirstOrDefault(o => o.Id == optionId);

            if (selectedOption != null)
            {
                // Create result based on the selected option context
                var enhancedMessage = $"{originalMessage}. User selected: {selectedOption.Label}";

                return await ClassifyIntentWithAiAsync(
                    enhancedMessage,
                    canvasContext,
                    null,
                    cancellationToken);
            }
        }

        // Fallback: combine original message with option selection for re-classification
        var combinedMessage = $"{originalMessage}. Selection: {optionId}";
        return await ClassifyIntentWithAiAsync(combinedMessage, canvasContext, null, cancellationToken);
    }

    /// <summary>
    /// Re-classify with free text response combined with original message.
    /// </summary>
    private async Task<AiIntentResult> ReClassifyWithFreeTextAsync(
        string originalMessage,
        string freeTextResponse,
        CanvasContext? canvasContext,
        ClarificationContext? clarificationContext,
        CancellationToken cancellationToken)
    {
        // Build enhanced prompt with both messages
        var systemPrompt = BuildAiIntentClassificationSystemPrompt();

        var contextInfo = clarificationContext != null
            ? $"\nPreviously understood: {clarificationContext.Understood}\nUnclear aspect: {clarificationContext.Unclear}"
            : "";

        var userPrompt = $"""
            ## Conversation Context

            Original message: "{originalMessage}"
            Clarification response: "{freeTextResponse}"
            {contextInfo}

            ## Canvas Context
            {BuildAiIntentClassificationUserPrompt("", canvasContext)}

            ## Task
            Classify the user's intent considering both the original message and their clarification.
            The clarification provides additional context to disambiguate the original message.
            Return JSON only.
            """;

        try
        {
            var response = await _openAiClient.GetCompletionAsync(
                $"{systemPrompt}\n\n{userPrompt}",
                model: DefaultClassificationModel,
                cancellationToken: cancellationToken);

            var result = ParseAiIntentResult(response, originalMessage);

            _logger.LogInformation(
                "Re-classified with context: Operation={Operation}, Action={Action}, Confidence={Confidence:F2}",
                result.Operation, result.Action, result.Confidence);

            // Apply confidence thresholds but allow higher confidence since we have context
            return ApplyConfidenceThresholds(result with { Confidence = Math.Min(result.Confidence * 1.1, 1.0) }, canvasContext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-classify with context, using combined message");
            var combinedMessage = $"{originalMessage}. {freeTextResponse}";
            return await ClassifyIntentWithAiAsync(combinedMessage, canvasContext, null, cancellationToken);
        }
    }

    /// <summary>
    /// Build the system prompt for clarification generation.
    /// </summary>
    private static string BuildClarificationSystemPrompt()
    {
        return """
            You are a clarification assistant for an AI-powered playbook builder.
            Your task is to generate helpful clarification questions when user intent is unclear.

            ## Guidelines

            1. Ask specific questions that will disambiguate the user's intent
            2. Provide relevant options based on the canvas context
            3. Keep questions concise and user-friendly
            4. Offer suggestions that help guide the user
            5. Explain what you understood vs. what is unclear

            ## Response Format

            Return valid JSON with this structure:
            {
              "question": "The clarification question to ask",
              "type": "INTENT_DISAMBIGUATION|ENTITY_DISAMBIGUATION|MISSING_PARAMETER|CONFIRMATION|SELECTION|GENERAL",
              "options": [
                { "id": "opt1", "label": "Option 1", "description": "Description" }
              ],
              "suggestions": ["Suggested response 1", "Suggested response 2"],
              "understoodContext": "What you understood from the message",
              "ambiguityReason": "Why clarification is needed"
            }
            """;
    }

    /// <summary>
    /// Build the user prompt for clarification generation.
    /// </summary>
    private static string BuildClarificationUserPrompt(
        string message,
        CanvasContext? canvasContext,
        AiIntentResult? initialResult)
    {
        var canvasInfo = canvasContext != null
            ? $"Canvas: {canvasContext.NodeCount} nodes, Types: [{string.Join(", ", canvasContext.NodeTypes)}], Selected: {canvasContext.SelectedNodeId ?? "none"}"
            : "Canvas: empty";

        var initialClassification = initialResult != null
            ? $"Initial classification: {initialResult.Operation}/{initialResult.Action} (confidence: {initialResult.Confidence:F2})\nReasoning: {initialResult.Reasoning}"
            : "No initial classification";

        return $"""
            ## User Message
            "{message}"

            ## Canvas Context
            {canvasInfo}

            ## Initial Classification Attempt
            {initialClassification}

            ## Task
            Generate a clarification question to help understand what the user wants to do.
            Return JSON only.
            """;
    }

    /// <summary>
    /// Parse the AI clarification response into a ClarificationQuestion.
    /// </summary>
    private ClarificationQuestion ParseClarificationResponse(
        string response,
        string originalMessage,
        AiIntentResult? initialResult)
    {
        var jsonContent = ExtractJsonFromResponse(response);

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return CreateFallbackClarification(originalMessage, null, initialResult);
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            var questionText = root.TryGetProperty("question", out var q) ? q.GetString() : null;
            var typeStr = root.TryGetProperty("type", out var t) ? t.GetString() : "GENERAL";
            var understood = root.TryGetProperty("understoodContext", out var u) ? u.GetString() : null;
            var ambiguity = root.TryGetProperty("ambiguityReason", out var a) ? a.GetString() : null;

            // Parse options
            var options = new List<ClarificationOption>();
            if (root.TryGetProperty("options", out var optionsElement) && optionsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var opt in optionsElement.EnumerateArray())
                {
                    var id = opt.TryGetProperty("id", out var idProp) ? idProp.GetString() : Guid.NewGuid().ToString("N")[..8];
                    var label = opt.TryGetProperty("label", out var labelProp) ? labelProp.GetString() : "";
                    var desc = opt.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(label))
                    {
                        options.Add(new ClarificationOption { Id = id, Label = label, Description = desc });
                    }
                }
            }

            // Parse suggestions
            var suggestions = new List<string>();
            if (root.TryGetProperty("suggestions", out var suggestionsElement) && suggestionsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var sug in suggestionsElement.EnumerateArray())
                {
                    var sugText = sug.GetString();
                    if (!string.IsNullOrEmpty(sugText))
                    {
                        suggestions.Add(sugText);
                    }
                }
            }

            // Parse clarification type
            var clarificationType = ParseClarificationType(typeStr);

            return new ClarificationQuestion
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Text = questionText ?? GenerateClarificationQuestion(initialResult ?? new AiIntentResult
                {
                    Operation = IntentOperationType.Clarify,
                    Action = IntentAction.RequestClarification,
                    Confidence = 0.5
                }),
                Type = clarificationType,
                Options = options.Count > 0 ? options.ToArray() : null,
                AllowFreeText = true,
                FreeTextPlaceholder = "Or describe what you'd like to do in your own words",
                Suggestions = suggestions.Count > 0 ? suggestions.ToArray() : null,
                AmbiguityReason = ambiguity,
                UnderstoodContext = understood
            };
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse clarification JSON response");
            return CreateFallbackClarification(originalMessage, null, initialResult);
        }
    }

    /// <summary>
    /// Parse clarification type string to enum.
    /// </summary>
    private static IntentClarificationType ParseClarificationType(string? typeStr)
    {
        return typeStr?.ToUpperInvariant() switch
        {
            "INTENT_DISAMBIGUATION" => IntentClarificationType.IntentDisambiguation,
            "ENTITY_DISAMBIGUATION" => IntentClarificationType.EntityDisambiguation,
            "MISSING_PARAMETER" => IntentClarificationType.MissingParameter,
            "CONFIRMATION" => IntentClarificationType.Confirmation,
            "SELECTION" => IntentClarificationType.Selection,
            _ => IntentClarificationType.General
        };
    }

    /// <summary>
    /// Create a fallback clarification question when AI fails.
    /// </summary>
    private static ClarificationQuestion CreateFallbackClarification(
        string message,
        CanvasContext? canvasContext,
        AiIntentResult? initialResult)
    {
        var action = initialResult?.Action ?? IntentAction.RequestClarification;
        var question = GenerateClarificationQuestion(initialResult ?? new AiIntentResult
        {
            Operation = IntentOperationType.Clarify,
            Action = IntentAction.RequestClarification,
            Confidence = 0.5
        });
        var suggestions = GenerateSuggestions(initialResult ?? new AiIntentResult
        {
            Operation = IntentOperationType.Clarify,
            Action = IntentAction.RequestClarification,
            Confidence = 0.5
        });

        // Generate context-aware options based on action
        var options = GenerateContextualOptions(action, canvasContext);

        return new ClarificationQuestion
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Text = question,
            Type = IntentClarificationType.General,
            Options = options,
            AllowFreeText = true,
            FreeTextPlaceholder = "Describe what you'd like to do",
            Suggestions = suggestions,
            AmbiguityReason = "Unable to determine intent with sufficient confidence",
            UnderstoodContext = initialResult?.Reasoning
        };
    }

    /// <summary>
    /// Generate contextual options based on the action and canvas state.
    /// </summary>
    private static ClarificationOption[]? GenerateContextualOptions(
        IntentAction action,
        CanvasContext? canvasContext)
    {
        var options = new List<ClarificationOption>();

        switch (action)
        {
            case IntentAction.AddNode:
                options.Add(new ClarificationOption { Id = "aiAnalysis", Label = "AI Analysis", Description = "Add an AI analysis node" });
                options.Add(new ClarificationOption { Id = "condition", Label = "Condition", Description = "Add a conditional branch" });
                options.Add(new ClarificationOption { Id = "deliverOutput", Label = "Output Delivery", Description = "Add an output delivery node" });
                break;

            case IntentAction.RemoveNode:
                if (canvasContext?.SelectedNodeId != null)
                {
                    options.Add(new ClarificationOption { Id = "selected", Label = "Selected Node", Description = $"Remove the currently selected node ({canvasContext.SelectedNodeId})" });
                }
                if (canvasContext?.NodeCount > 0)
                {
                    options.Add(new ClarificationOption { Id = "last", Label = "Last Node", Description = "Remove the most recently added node" });
                }
                break;

            case IntentAction.TestPlaybook:
                options.Add(new ClarificationOption { Id = "mock", Label = "Mock Test", Description = "Quick validation with sample data (fastest)" });
                options.Add(new ClarificationOption { Id = "quick", Label = "Quick Test", Description = "Test with real document, no persistence" });
                options.Add(new ClarificationOption { Id = "production", Label = "Production Test", Description = "Full end-to-end test with persistence" });
                break;

            case IntentAction.LinkScope:
                options.Add(new ClarificationOption { Id = "action", Label = "Link Action", Description = "Link an Action scope to define what to analyze" });
                options.Add(new ClarificationOption { Id = "skill", Label = "Link Skill", Description = "Link a Skill scope for analysis patterns" });
                options.Add(new ClarificationOption { Id = "knowledge", Label = "Link Knowledge", Description = "Link a Knowledge scope for context" });
                break;
        }

        return options.Count > 0 ? options.ToArray() : null;
    }

    /// <summary>
    /// Check if an action is destructive (remove, delete, etc.).
    /// </summary>
    private static bool IsDestructiveAction(IntentAction action)
    {
        return action switch
        {
            IntentAction.RemoveNode => true,
            IntentAction.RemoveEdge => true,
            IntentAction.UnlinkScope => true,
            _ => false
        };
    }

    /// <summary>
    /// Generate a clarification question based on the low-confidence result.
    /// </summary>
    private static string GenerateClarificationQuestion(AiIntentResult result)
    {
        return result.Action switch
        {
            IntentAction.RemoveNode => "Which node would you like to remove?",
            IntentAction.ConfigureNode => "Which node would you like to configure, and what setting should I change?",
            IntentAction.AddNode => "What type of node would you like to add?",
            IntentAction.LinkScope => "Which scope would you like to link, and to which node?",
            IntentAction.TestPlaybook => "Which test mode would you like to use (mock, quick, or production)?",
            _ => "I'm not sure I understand. Could you please clarify what you'd like to do?"
        };
    }

    /// <summary>
    /// Generate suggestion prompts for the user.
    /// </summary>
    private static string[]? GenerateSuggestions(AiIntentResult result)
    {
        return result.Action switch
        {
            IntentAction.AddNode => new[]
            {
                "Add an AI analysis node",
                "Add a condition node",
                "Add an output delivery node"
            },
            IntentAction.RemoveNode => new[]
            {
                "Remove the selected node",
                "Remove the last node",
                "Remove the 'Analysis' node"
            },
            IntentAction.TestPlaybook => new[]
            {
                "Run a mock test",
                "Run a quick test",
                "Run in production mode"
            },
            _ => null
        };
    }

    /// <summary>
    /// Get a human-readable description of an action.
    /// </summary>
    private static string GetActionDescription(IntentAction action)
    {
        return action switch
        {
            IntentAction.CreatePlaybook => "create a new playbook",
            IntentAction.AddNode => "add a node",
            IntentAction.RemoveNode => "remove a node",
            IntentAction.RemoveEdge => "remove a connection",
            IntentAction.CreateEdge => "connect nodes",
            IntentAction.ConfigureNode => "configure a node",
            IntentAction.LinkScope => "link a scope",
            IntentAction.UnlinkScope => "unlink a scope",
            IntentAction.CreateScope => "create a custom scope",
            IntentAction.SavePlaybook => "save the playbook",
            IntentAction.TestPlaybook => "test the playbook",
            IntentAction.ValidatePlaybook => "validate the playbook",
            IntentAction.SearchScopes => "search for scopes",
            IntentAction.BrowseCatalog => "browse the catalog",
            IntentAction.AnswerQuestion => "answer your question",
            IntentAction.DescribeState => "describe the playbook state",
            IntentAction.ProvideGuidance => "provide guidance",
            IntentAction.ModifyLayout => "rearrange the layout",
            IntentAction.Undo => "undo the last action",
            IntentAction.Redo => "redo the last action",
            _ => "perform this action"
        };
    }

    /// <summary>
    /// Create a fallback result using rule-based parsing when AI fails.
    /// </summary>
    private AiIntentResult CreateFallbackResult(string message, CanvasContext? canvasContext)
    {
        var legacyIntent = ParseIntent(message);
        var entities = ExtractEntities(message);

        // Map legacy BuilderIntent to new schema
        var (operation, action) = MapLegacyIntentToNewSchema(legacyIntent);

        return new AiIntentResult
        {
            Operation = operation,
            Action = action,
            Confidence = 0.70, // Lower confidence for rule-based fallback
            Parameters = MapEntitiesToParameters(action, entities),
            Reasoning = "Classified using rule-based fallback due to AI service unavailability"
        };
    }

    /// <summary>
    /// Map legacy BuilderIntent enum to new OperationType and IntentAction.
    /// </summary>
    private static (IntentOperationType Operation, IntentAction Action) MapLegacyIntentToNewSchema(BuilderIntent intent)
    {
        return intent switch
        {
            BuilderIntent.CreatePlaybook => (IntentOperationType.Build, IntentAction.CreatePlaybook),
            BuilderIntent.AddNode => (IntentOperationType.Build, IntentAction.AddNode),
            BuilderIntent.RemoveNode => (IntentOperationType.Modify, IntentAction.RemoveNode),
            BuilderIntent.ConnectNodes => (IntentOperationType.Build, IntentAction.CreateEdge),
            BuilderIntent.ConfigureNode => (IntentOperationType.Modify, IntentAction.ConfigureNode),
            BuilderIntent.SearchScopes => (IntentOperationType.Search, IntentAction.SearchScopes),
            BuilderIntent.CreateScope => (IntentOperationType.Build, IntentAction.CreateScope),
            BuilderIntent.LinkScope => (IntentOperationType.Modify, IntentAction.LinkScope),
            BuilderIntent.TestPlaybook => (IntentOperationType.Test, IntentAction.TestPlaybook),
            BuilderIntent.SavePlaybook => (IntentOperationType.Modify, IntentAction.SavePlaybook),
            BuilderIntent.AskQuestion => (IntentOperationType.Explain, IntentAction.AnswerQuestion),
            _ => (IntentOperationType.Clarify, IntentAction.RequestClarification)
        };
    }

    /// <summary>
    /// Map extracted entities to IntentParameters based on action type.
    /// </summary>
    private static IntentParameters? MapEntitiesToParameters(
        IntentAction action,
        Dictionary<string, string>? entities)
    {
        if (entities == null || entities.Count == 0)
            return null;

        return action switch
        {
            IntentAction.AddNode => new IntentParameters
            {
                AddNode = new AddNodeParams
                {
                    NodeType = entities.GetValueOrDefault("nodeType") ?? "aiAnalysis",
                    Label = entities.GetValueOrDefault("nodeLabel")
                }
            },
            IntentAction.RemoveNode => new IntentParameters
            {
                RemoveNode = new RemoveNodeParams
                {
                    NodeReference = entities.GetValueOrDefault("nodeId") ?? "selected"
                }
            },
            IntentAction.ConfigureNode => new IntentParameters
            {
                ConfigureNode = entities.TryGetValue("configKey", out var key) && entities.TryGetValue("configValue", out var value)
                    ? new ConfigureNodeParams
                    {
                        NodeReference = entities.GetValueOrDefault("nodeId") ?? "selected",
                        Property = key,
                        Value = value
                    }
                    : null
            },
            IntentAction.SearchScopes => new IntentParameters
            {
                SearchScopes = new SearchScopesParams
                {
                    Query = entities.GetValueOrDefault("scopeName") ?? "",
                    ScopeTypes = entities.TryGetValue("scopeType", out var scopeType)
                        ? new[] { scopeType }
                        : null
                }
            },
            _ => null
        };
    }

    /// <summary>
    /// Convert AiIntentResult to legacy IntentClassification for backward compatibility.
    /// </summary>
    private IntentClassification ConvertToIntentClassification(
        AiIntentResult aiResult,
        string originalMessage)
    {
        var legacyIntent = MapNewSchemaToLegacyIntent(aiResult.Action);
        var needsClarification = aiResult.Operation == IntentOperationType.Clarify ||
                                  aiResult.Confidence < MediumConfidenceThreshold;

        return new IntentClassification
        {
            Intent = legacyIntent,
            Confidence = aiResult.Confidence,
            Entities = ExtractEntitiesFromAiResult(aiResult),
            NeedsClarification = needsClarification,
            ClarificationQuestion = aiResult.Clarification?.Question,
            Message = aiResult.Message // Pass through the AI's conversational message
        };
    }

    /// <summary>
    /// Map new IntentAction to legacy BuilderIntent enum.
    /// </summary>
    private static BuilderIntent MapNewSchemaToLegacyIntent(IntentAction action)
    {
        return action switch
        {
            IntentAction.CreatePlaybook => BuilderIntent.CreatePlaybook,
            IntentAction.AddNode => BuilderIntent.AddNode,
            IntentAction.RemoveNode => BuilderIntent.RemoveNode,
            IntentAction.CreateEdge => BuilderIntent.ConnectNodes,
            IntentAction.ConfigureNode => BuilderIntent.ConfigureNode,
            IntentAction.SearchScopes => BuilderIntent.SearchScopes,
            IntentAction.BrowseCatalog => BuilderIntent.SearchScopes,
            IntentAction.CreateScope => BuilderIntent.CreateScope,
            IntentAction.LinkScope => BuilderIntent.LinkScope,
            IntentAction.UnlinkScope => BuilderIntent.LinkScope,
            IntentAction.TestPlaybook => BuilderIntent.TestPlaybook,
            IntentAction.ValidatePlaybook => BuilderIntent.TestPlaybook,
            IntentAction.SavePlaybook => BuilderIntent.SavePlaybook,
            IntentAction.AnswerQuestion => BuilderIntent.AskQuestion,
            IntentAction.DescribeState => BuilderIntent.AskQuestion,
            IntentAction.ProvideGuidance => BuilderIntent.AskQuestion,
            IntentAction.ModifyLayout => BuilderIntent.ConfigureNode,
            IntentAction.Undo => BuilderIntent.Unknown,
            IntentAction.Redo => BuilderIntent.Unknown,
            IntentAction.RemoveEdge => BuilderIntent.ConnectNodes,
            IntentAction.RequestClarification => BuilderIntent.Unknown,
            IntentAction.ConfirmUnderstanding => BuilderIntent.Unknown,
            _ => BuilderIntent.Unknown
        };
    }

    /// <summary>
    /// Extract entities dictionary from AiIntentResult parameters.
    /// </summary>
    private static Dictionary<string, string>? ExtractEntitiesFromAiResult(AiIntentResult result)
    {
        var entities = new Dictionary<string, string>();

        var parameters = result.Parameters;
        if (parameters == null)
            return entities.Count > 0 ? entities : null;

        // Extract from AddNode parameters
        if (parameters.AddNode != null)
        {
            entities["nodeType"] = parameters.AddNode.NodeType;
            if (parameters.AddNode.Label != null)
                entities["nodeLabel"] = parameters.AddNode.Label;
            if (parameters.AddNode.ConnectFrom != null)
                entities["connectFrom"] = parameters.AddNode.ConnectFrom;
        }

        // Extract from RemoveNode parameters
        if (parameters.RemoveNode != null)
        {
            entities["nodeId"] = parameters.RemoveNode.NodeReference;
        }

        // Extract from ConfigureNode parameters
        if (parameters.ConfigureNode != null)
        {
            entities["nodeId"] = parameters.ConfigureNode.NodeReference;
            entities["configKey"] = parameters.ConfigureNode.Property;
            entities["configValue"] = parameters.ConfigureNode.Value;
        }

        // Extract from CreateEdge parameters
        if (parameters.CreateEdge != null)
        {
            entities["sourceNode"] = parameters.CreateEdge.SourceNode;
            entities["targetNode"] = parameters.CreateEdge.TargetNode;
        }

        // Extract from LinkScope parameters
        if (parameters.LinkScope != null)
        {
            entities["nodeId"] = parameters.LinkScope.NodeReference;
            if (parameters.LinkScope.ScopeReference.Id != null)
                entities["scopeId"] = parameters.LinkScope.ScopeReference.Id;
            if (parameters.LinkScope.ScopeReference.Name != null)
                entities["scopeName"] = parameters.LinkScope.ScopeReference.Name;
            entities["scopeType"] = parameters.LinkScope.ScopeReference.Type;
        }

        // Extract from SearchScopes parameters
        if (parameters.SearchScopes != null)
        {
            entities["scopeName"] = parameters.SearchScopes.Query;
            if (parameters.SearchScopes.ScopeTypes?.Length > 0)
                entities["scopeType"] = parameters.SearchScopes.ScopeTypes[0];
        }

        // Extract from CreateScope parameters
        if (parameters.CreateScope != null)
        {
            entities["scopeType"] = parameters.CreateScope.ScopeType;
            entities["scopeName"] = parameters.CreateScope.Name;
            if (parameters.CreateScope.Description != null)
                entities["description"] = parameters.CreateScope.Description;
        }

        // Extract from TestPlaybook parameters
        if (parameters.TestPlaybook != null)
        {
            entities["testMode"] = parameters.TestPlaybook.Mode;
        }

        // Extract from CreatePlaybook parameters
        if (parameters.CreatePlaybook != null)
        {
            entities["goal"] = parameters.CreatePlaybook.Goal;
            if (parameters.CreatePlaybook.DocumentTypes?.Length > 0)
                entities["documentType"] = parameters.CreatePlaybook.DocumentTypes[0];
        }

        return entities.Count > 0 ? entities : null;
    }

    /// <summary>
    /// Execute operations based on classified intent.
    /// NOTE: The AI's conversational message is already displayed before this method is called.
    /// This method only emits canvas operations and progress updates, NOT intro messages.
    /// </summary>
    private async IAsyncEnumerable<BuilderStreamChunk> ExecuteIntentAsync(
        IntentClassification classification,
        BuilderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        switch (classification.Intent)
        {
            case BuilderIntent.CreatePlaybook:
                // Generate build plan
                var plan = await GenerateBuildPlanAsync(
                    new BuildPlanRequest { Goal = request.Message },
                    cancellationToken);
                yield return BuilderStreamChunk.Message($"Creating a playbook with {plan.EstimatedNodeCount} nodes...");

                // Execute the plan steps - yield canvas patches for each node
                var createdNodeIds = new Dictionary<int, string>();
                foreach (var step in plan.Steps)
                {
                    if (step.Action == ExecutionStepActions.AddNode && step.NodeSpec != null)
                    {
                        var newNodeId = Guid.NewGuid().ToString("N")[..8];
                        createdNodeIds[step.Order] = newNodeId;

                        yield return BuilderStreamChunk.Operation(new CanvasPatch
                        {
                            Operation = CanvasPatchOperation.AddNode,
                            Node = new CanvasNode
                            {
                                Id = newNodeId,
                                Type = step.NodeSpec.Type ?? PlaybookNodeTypes.AiAnalysis,
                                Label = step.NodeSpec.Label ?? step.Description,
                                Position = new NodePosition(
                                    step.NodeSpec.Position?.X ?? 200 + (step.Order * 250),
                                    step.NodeSpec.Position?.Y ?? 200)
                            }
                        });
                        yield return BuilderStreamChunk.Message($"Added: {step.NodeSpec.Label ?? step.Description}");
                    }
                }

                // Connect nodes in sequence
                var nodeIdsList = createdNodeIds.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();
                for (int i = 0; i < nodeIdsList.Count - 1; i++)
                {
                    yield return BuilderStreamChunk.Operation(new CanvasPatch
                    {
                        Operation = CanvasPatchOperation.AddEdge,
                        Edge = new CanvasEdge
                        {
                            Id = $"edge-{i}",
                            SourceId = nodeIdsList[i],
                            TargetId = nodeIdsList[i + 1]
                        }
                    });
                }

                yield return BuilderStreamChunk.Message($"Done! Created {createdNodeIds.Count} nodes connected in sequence.");
                break;

            case BuilderIntent.AddNode:
                var nodeType = classification.Entities?.GetValueOrDefault("nodeType") ?? PlaybookNodeTypes.AiAnalysis;
                var nodeLabel = classification.Entities?.GetValueOrDefault("nodeLabel") ?? GetDefaultNodeLabel(nodeType);
                yield return BuilderStreamChunk.Operation(new CanvasPatch
                {
                    Operation = CanvasPatchOperation.AddNode,
                    Node = new CanvasNode
                    {
                        Id = Guid.NewGuid().ToString("N")[..8],
                        Type = nodeType,
                        Label = nodeLabel,
                        Position = new NodePosition(100, 100)
                    }
                });
                // Operation message already shown by AI - just emit the operation
                break;

            case BuilderIntent.RemoveNode:
                var nodeId = classification.Entities?.GetValueOrDefault("nodeId");
                if (!string.IsNullOrEmpty(nodeId))
                {
                    yield return BuilderStreamChunk.Operation(new CanvasPatch
                    {
                        Operation = CanvasPatchOperation.RemoveNode,
                        NodeId = nodeId
                    });
                }
                break;

            case BuilderIntent.ConnectNodes:
                // Would extract source/target from entities and create edge
                // Operation message already shown by AI
                break;

            case BuilderIntent.ConfigureNode:
                // Would update node configuration
                // Operation message already shown by AI
                break;

            case BuilderIntent.SearchScopes:
                // Would call _scopeResolver.SearchScopesAsync
                // Operation message already shown by AI
                break;

            case BuilderIntent.TestPlaybook:
                // Would initiate test execution
                // Operation message already shown by AI
                break;

            case BuilderIntent.SavePlaybook:
                // Would save playbook to Dataverse
                // Operation message already shown by AI
                break;

            case BuilderIntent.AskQuestion:
                // AI's conversational response already displayed - nothing more to do
                break;

            default:
                // AI's response already shown - nothing more to do
                break;
        }
    }

    /// <summary>
    /// Parse intent from message (simplified rule-based for skeleton).
    /// Full implementation will use AI classification.
    /// </summary>
    private static BuilderIntent ParseIntent(string message)
    {
        var lower = message.ToLowerInvariant();

        if (lower.Contains("create") && lower.Contains("playbook"))
            return BuilderIntent.CreatePlaybook;
        if (lower.Contains("add") && (lower.Contains("node") || lower.Contains("step")))
            return BuilderIntent.AddNode;
        if (lower.Contains("remove") || lower.Contains("delete"))
            return BuilderIntent.RemoveNode;
        if (lower.Contains("connect") || lower.Contains("link"))
            return BuilderIntent.ConnectNodes;
        if (lower.Contains("configure") || lower.Contains("set"))
            return BuilderIntent.ConfigureNode;
        if (lower.Contains("search") || lower.Contains("find"))
            return BuilderIntent.SearchScopes;
        if (lower.Contains("test") || lower.Contains("run"))
            return BuilderIntent.TestPlaybook;
        if (lower.Contains("save"))
            return BuilderIntent.SavePlaybook;
        if (lower.Contains("?") || lower.Contains("how") || lower.Contains("what"))
            return BuilderIntent.AskQuestion;

        return BuilderIntent.Unknown;
    }

    /// <summary>
    /// Extract entities from message (simplified for skeleton).
    /// Full implementation will use AI entity extraction.
    /// </summary>
    private static Dictionary<string, string>? ExtractEntities(string message)
    {
        var entities = new Dictionary<string, string>();
        var lower = message.ToLowerInvariant();

        // Extract node types (from PlaybookNodeTypes)
        // Check multi-word patterns first, then single-word patterns
        if (lower.Contains("ai analysis") || lower.Contains("ai-analysis") ||
            lower.Contains("aianalysis") || lower.Contains("analysis node"))
        {
            entities["nodeType"] = PlaybookNodeTypes.AiAnalysis;
            entities["nodeLabel"] = "AI Analysis";
        }
        else if (lower.Contains("ai completion") || lower.Contains("ai-completion") ||
                 lower.Contains("aicompletion") || lower.Contains("completion node"))
        {
            entities["nodeType"] = PlaybookNodeTypes.AiCompletion;
            entities["nodeLabel"] = "AI Completion";
        }
        else if (lower.Contains("deliver output") || lower.Contains("deliver-output") ||
                 lower.Contains("deliveroutput") || lower.Contains("output node") ||
                 lower.Contains("delivery node"))
        {
            entities["nodeType"] = PlaybookNodeTypes.DeliverOutput;
            entities["nodeLabel"] = "Deliver Output";
        }
        else if (lower.Contains("create task") || lower.Contains("createtask") ||
                 lower.Contains("task node"))
        {
            entities["nodeType"] = PlaybookNodeTypes.CreateTask;
            entities["nodeLabel"] = "Create Task";
        }
        else if (lower.Contains("send email") || lower.Contains("sendemail") ||
                 lower.Contains("email node"))
        {
            entities["nodeType"] = PlaybookNodeTypes.SendEmail;
            entities["nodeLabel"] = "Send Email";
        }
        else if (lower.Contains("condition") || lower.Contains("conditional") ||
                 lower.Contains("branch") || lower.Contains("if"))
        {
            entities["nodeType"] = PlaybookNodeTypes.Condition;
            entities["nodeLabel"] = "Condition";
        }
        else if (lower.Contains("wait") || lower.Contains("pause") || lower.Contains("delay"))
        {
            entities["nodeType"] = PlaybookNodeTypes.Wait;
            entities["nodeLabel"] = "Wait";
        }
        // Also support scope type keywords as default
        else if (lower.Contains("action"))
        {
            entities["nodeType"] = PlaybookNodeTypes.AiAnalysis;
            entities["nodeLabel"] = "Action";
        }
        else if (lower.Contains("skill"))
        {
            entities["nodeType"] = PlaybookNodeTypes.AiAnalysis;
            entities["nodeLabel"] = "Skill";
        }
        else if (lower.Contains("tool"))
        {
            entities["nodeType"] = PlaybookNodeTypes.AiAnalysis;
            entities["nodeLabel"] = "Tool";
        }
        else if (lower.Contains("knowledge"))
        {
            entities["nodeType"] = PlaybookNodeTypes.AiAnalysis;
            entities["nodeLabel"] = "Knowledge";
        }

        return entities.Count > 0 ? entities : null;
    }

    /// <summary>
    /// Get human-readable description of an intent.
    /// </summary>
    private static string GetIntentDescription(BuilderIntent intent)
    {
        return intent switch
        {
            BuilderIntent.CreatePlaybook => "create a new playbook",
            BuilderIntent.AddNode => "add a node",
            BuilderIntent.RemoveNode => "remove a node",
            BuilderIntent.ConnectNodes => "connect nodes",
            BuilderIntent.ConfigureNode => "configure a node",
            BuilderIntent.SearchScopes => "search for scopes",
            BuilderIntent.CreateScope => "create a custom scope",
            BuilderIntent.LinkScope => "link a scope",
            BuilderIntent.TestPlaybook => "test the playbook",
            BuilderIntent.SavePlaybook => "save the playbook",
            BuilderIntent.AskQuestion => "get help",
            _ => "perform an action"
        };
    }

    /// <summary>
    /// Get human-readable default label for a node type.
    /// </summary>
    private static string GetDefaultNodeLabel(string nodeType)
    {
        return nodeType switch
        {
            PlaybookNodeTypes.AiAnalysis => "AI Analysis",
            PlaybookNodeTypes.AiCompletion => "AI Completion",
            PlaybookNodeTypes.Condition => "Condition",
            PlaybookNodeTypes.DeliverOutput => "Deliver Output",
            PlaybookNodeTypes.CreateTask => "Create Task",
            PlaybookNodeTypes.SendEmail => "Send Email",
            PlaybookNodeTypes.Wait => "Wait",
            _ => nodeType // fallback to the type name itself
        };
    }

    /// <summary>
    /// Build system prompt for intent classification.
    /// LEGACY: This method is kept for reference but is no longer used.
    /// AI classification now uses BuildAiIntentClassificationSystemPrompt().
    /// </summary>
    [Obsolete("Use BuildAiIntentClassificationSystemPrompt instead")]
    private static string BuildIntentClassificationSystemPrompt()
    {
        return """
            You are an intent classification system for a playbook builder assistant.

            Classify the user's message into one of these intents:
            - CreatePlaybook: User wants to create a new playbook from scratch
            - AddNode: User wants to add a node to the canvas
            - RemoveNode: User wants to remove a node
            - ConnectNodes: User wants to connect two nodes
            - ConfigureNode: User wants to configure a node's settings
            - SearchScopes: User wants to find available scopes
            - CreateScope: User wants to create a custom scope
            - LinkScope: User wants to link a scope to a node
            - TestPlaybook: User wants to test the playbook
            - SavePlaybook: User wants to save the playbook
            - AskQuestion: User is asking a question
            - Unknown: Cannot determine intent

            Extract any relevant entities:
            - nodeType: action, skill, tool, knowledge
            - nodeId: ID of a referenced node
            - scopeName: Name of a referenced scope

            Return JSON with: intent, confidence (0-1), entities
            """;
    }

    /// <summary>
    /// Build system prompt for plan generation.
    /// </summary>
    private static string BuildPlanGenerationSystemPrompt()
    {
        return """
            You are a playbook architect that creates build plans for document analysis workflows.

            A playbook consists of:
            - Input nodes: Receive document content
            - Action nodes: Define analysis actions
            - Skill nodes: Apply specific analysis skills
            - Tool nodes: Execute specific tools
            - Knowledge nodes: Provide context and examples
            - Output nodes: Format and return results

            Create a structured build plan with specific steps.
            Each step should have: action, description, parameters.

            Consider:
            - Document type being analyzed
            - Required extraction fields
            - Processing order and dependencies
            - Best practices for the analysis type
            """;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TestExecutionEvent> ExecuteTestAsync(
        TestPlaybookRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing playbook test, Mode={Mode}, PlaybookId={PlaybookId}",
            request.Mode, request.PlaybookId);

        // Get canvas state (either from PlaybookId lookup or directly from request)
        var canvasState = request.CanvasJson;
        if (canvasState == null && request.PlaybookId.HasValue)
        {
            // Load canvas from PlaybookService
            var canvasLayout = await _playbookService.GetCanvasLayoutAsync(request.PlaybookId.Value, cancellationToken);
            if (canvasLayout?.Layout != null)
            {
                canvasState = ConvertLayoutToCanvasState(canvasLayout.Layout);
                _logger.LogDebug(
                    "Loaded canvas from playbook {PlaybookId}: {NodeCount} nodes",
                    request.PlaybookId, canvasState.Nodes.Length);
            }
            else
            {
                yield return new TestExecutionEvent
                {
                    Type = TestEventTypes.Error,
                    Data = new { message = $"Playbook {request.PlaybookId} not found or has no canvas." },
                    Done = true
                };
                yield break;
            }
        }

        if (canvasState?.Nodes == null || canvasState.Nodes.Length == 0)
        {
            yield return new TestExecutionEvent
            {
                Type = TestEventTypes.Error,
                Data = new { message = "Canvas has no nodes to execute." },
                Done = true
            };
            yield break;
        }

        // For Mock mode, delegate to the dedicated MockTestExecutor
        // This ensures Mock mode makes NO external calls (constraint from task)
        if (request.Mode == TestMode.Mock)
        {
            _logger.LogDebug("Delegating to MockTestExecutor for Mock mode execution");
            await foreach (var evt in _mockTestExecutor.ExecuteAsync(canvasState, request.Options, cancellationToken))
            {
                yield return evt;
            }
            yield break;
        }

        // For Quick and Production modes, use inline execution
        // (Full Quick/Production modes with document context use dedicated endpoints)
        var startTime = DateTime.UtcNow;
        var nodesExecuted = 0;
        var nodesSkipped = 0;
        var nodesFailed = 0;
        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        var nodes = canvasState.Nodes;
        var totalSteps = request.Options?.MaxNodes ?? nodes.Length;
        totalSteps = Math.Min(totalSteps, nodes.Length);

        _logger.LogDebug("Test execution will process {NodeCount} nodes in {Mode} mode", totalSteps, request.Mode);

        // Execute nodes in order (simplified - full implementation would follow edges)
        for (var i = 0; i < totalSteps && !cancellationToken.IsCancellationRequested; i++)
        {
            var node = nodes[i];
            var stepNumber = i + 1;

            // Emit node_start event
            yield return new TestExecutionEvent
            {
                Type = TestEventTypes.NodeStart,
                Data = new NodeStartData
                {
                    NodeId = node.Id,
                    Label = node.Label ?? $"Node {stepNumber}",
                    NodeType = node.Type,
                    StepNumber = stepNumber,
                    TotalSteps = totalSteps
                }
            };

            // Execute node based on mode
            var nodeStartTime = DateTime.UtcNow;
            object? nodeOutput = null;
            var nodeSuccess = true;
            string? nodeError = null;
            var nodeTokens = new TokenUsageData();

            try
            {
                switch (request.Mode)
                {
                    case TestMode.Quick:
                        // Quick mode: Execute with real AI but no persistence
                        (nodeOutput, nodeTokens) = await ExecuteQuickNodeAsync(node, cancellationToken);
                        break;

                    case TestMode.Production:
                        // Production mode: Full execution with persistence
                        (nodeOutput, nodeTokens) = await ExecuteProductionNodeAsync(node, cancellationToken);
                        break;
                }

                nodesExecuted++;
                totalInputTokens += nodeTokens.InputTokens;
                totalOutputTokens += nodeTokens.OutputTokens;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Node {NodeId} execution failed", node.Id);
                nodeSuccess = false;
                nodeError = ex.Message;
                nodesFailed++;
            }

            var nodeDuration = (int)(DateTime.UtcNow - nodeStartTime).TotalMilliseconds;

            // Emit node_output event
            yield return new TestExecutionEvent
            {
                Type = TestEventTypes.NodeOutput,
                Data = new NodeOutputData
                {
                    NodeId = node.Id,
                    Output = nodeOutput,
                    DurationMs = nodeDuration,
                    TokenUsage = nodeTokens
                }
            };

            // Emit node_complete event
            yield return new TestExecutionEvent
            {
                Type = TestEventTypes.NodeComplete,
                Data = new NodeCompleteData
                {
                    NodeId = node.Id,
                    Success = nodeSuccess,
                    Error = nodeError,
                    OutputVariable = node.OutputVariable
                }
            };

            // Small delay between nodes to allow UI updates
            await Task.Delay(50, cancellationToken);
        }

        // Calculate total duration
        var totalDuration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        // Emit test_complete event
        yield return new TestExecutionEvent
        {
            Type = TestEventTypes.Complete,
            Data = new TestCompleteData
            {
                Success = nodesFailed == 0,
                NodesExecuted = nodesExecuted,
                NodesSkipped = nodesSkipped,
                NodesFailed = nodesFailed,
                TotalDurationMs = totalDuration,
                TotalTokenUsage = new TokenUsageData
                {
                    InputTokens = totalInputTokens,
                    OutputTokens = totalOutputTokens,
                    Model = GetModelForMode(request.Mode)
                }
            },
            Done = true
        };

        _logger.LogInformation(
            "Test execution completed: {NodesExecuted} executed, {NodesFailed} failed, {Duration}ms",
            nodesExecuted, nodesFailed, totalDuration);
    }

    /// <summary>
    /// Convert a CanvasLayoutDto to CanvasState for test execution.
    /// </summary>
    private static CanvasState ConvertLayoutToCanvasState(CanvasLayoutDto layout)
    {
        return new CanvasState
        {
            Nodes = layout.Nodes.Select(n => new CanvasNode
            {
                Id = n.Id,
                Type = n.Type,
                Position = new NodePosition(n.X, n.Y),
                Label = n.Data?.TryGetValue("label", out var label) == true ? label?.ToString() : null,
                Config = n.Data,
                OutputVariable = n.Data?.TryGetValue("outputVariable", out var outputVar) == true ? outputVar?.ToString() : null,
                ConditionJson = n.Data?.TryGetValue("conditionJson", out var condJson) == true ? condJson?.ToString() : null
            }).ToArray(),
            Edges = layout.Edges.Select(e => new CanvasEdge
            {
                Id = e.Id,
                SourceId = e.Source,
                TargetId = e.Target,
                SourceHandle = e.SourceHandle,
                TargetHandle = e.TargetHandle
            }).ToArray()
        };
    }

    /// <summary>
    /// Execute a node in mock mode (sample data, no AI calls).
    /// </summary>
    private async Task<(object? Output, TokenUsageData Tokens)> ExecuteMockNodeAsync(
        CanvasNode node,
        CancellationToken cancellationToken)
    {
        // Simulate processing delay
        await Task.Delay(100, cancellationToken);

        object output = node.Type switch
        {
            "aiAnalysis" => new { summary = "Mock analysis result for testing", confidence = 0.95 },
            "aiCompletion" => new { text = "Mock completion text for testing" },
            "condition" => new { result = true, branch = "true" },
            "deliverOutput" => new { delivered = true, format = "json" },
            _ => new { type = node.Type, status = "completed" }
        };

        return (output, new TokenUsageData { InputTokens = 0, OutputTokens = 0, Model = "mock" });
    }

    /// <summary>
    /// Execute a node in quick mode (real AI, ephemeral storage).
    /// </summary>
    private async Task<(object? Output, TokenUsageData Tokens)> ExecuteQuickNodeAsync(
        CanvasNode node,
        CancellationToken cancellationToken)
    {
        // For nodes that require AI, make a simple call
        if (node.Type is "aiAnalysis" or "aiCompletion")
        {
            var prompt = $"Generate a sample {node.Type} output for a node labeled '{node.Label}'";
            var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);

            return (
                new { text = response, nodeType = node.Type },
                new TokenUsageData { InputTokens = 50, OutputTokens = 100, Model = "gpt-4o-mini" }
            );
        }

        // Non-AI nodes use mock execution
        return await ExecuteMockNodeAsync(node, cancellationToken);
    }

    /// <summary>
    /// Execute a node in production mode (full execution with persistence).
    /// </summary>
    private async Task<(object? Output, TokenUsageData Tokens)> ExecuteProductionNodeAsync(
        CanvasNode node,
        CancellationToken cancellationToken)
    {
        // Production mode uses the same logic as quick mode for now
        // Full implementation would:
        // 1. Load actual scopes from Dataverse
        // 2. Execute with proper tool handlers
        // 3. Persist results to Dataverse
        return await ExecuteQuickNodeAsync(node, cancellationToken);
    }

    /// <summary>
    /// Get the AI model name for the test mode.
    /// </summary>
    private static string GetModelForMode(TestMode mode) => mode switch
    {
        TestMode.Mock => "mock",
        TestMode.Quick => "gpt-4o-mini",
        TestMode.Production => "gpt-4o",
        _ => "unknown"
    };
}
