using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

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
    private readonly ILogger<AiPlaybookBuilderService> _logger;

    // Confidence thresholds per spec
    private const double IntentConfidenceThreshold = 0.75;
    private const double EntityConfidenceThreshold = 0.80;

    public AiPlaybookBuilderService(
        IOpenAiClient openAiClient,
        IScopeResolverService scopeResolver,
        ILogger<AiPlaybookBuilderService> logger)
    {
        _openAiClient = openAiClient;
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BuilderStreamChunk> ProcessMessageAsync(
        BuilderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing builder message: {Message}", request.Message);

        // 1. Classify intent
        var canvasContext = new CanvasContext
        {
            NodeCount = request.CanvasState.Nodes.Length,
            NodeTypes = request.CanvasState.Nodes.Select(n => n.Type).Distinct().ToArray(),
            IsSaved = request.PlaybookId.HasValue
        };

        var classification = await ClassifyIntentAsync(
            request.Message, canvasContext, cancellationToken);

        _logger.LogDebug(
            "Intent classified: {Intent} with confidence {Confidence}",
            classification.Intent, classification.Confidence);

        // 2. Check if clarification needed
        if (classification.NeedsClarification)
        {
            yield return BuilderStreamChunk.Clarification(
                classification.ClarificationQuestion ?? "Could you please clarify what you'd like to do?");
            yield return BuilderStreamChunk.Complete();
            yield break;
        }

        // 3. Process based on intent
        yield return BuilderStreamChunk.Message($"I understand you want to {GetIntentDescription(classification.Intent)}.");

        // 4. Generate and execute operations based on intent
        await foreach (var chunk in ExecuteIntentAsync(
            classification, request, cancellationToken))
        {
            yield return chunk;
        }

        yield return BuilderStreamChunk.Complete();
    }

    /// <inheritdoc />
    public async Task<BuildPlan> GenerateBuildPlanAsync(
        BuildPlanRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Generating build plan for goal: {Goal}", request.Goal);

        // Build prompt for plan generation
        var systemPrompt = BuildPlanGenerationSystemPrompt();
        var userPrompt = $"""
            Goal: {request.Goal}
            Document Type: {request.DocumentType ?? "general"}

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

        // Build classification prompt
        var systemPrompt = BuildIntentClassificationSystemPrompt();
        var userPrompt = $"""
            Message: {message}
            Canvas State: {canvasContext?.NodeCount ?? 0} nodes, types: {string.Join(", ", canvasContext?.NodeTypes ?? [])}

            Classify the intent and extract any entities.
            """;

        // Call AI for classification (using faster model per spec)
        var response = await _openAiClient.GetCompletionAsync(
            $"{systemPrompt}\n\n{userPrompt}",
            cancellationToken: cancellationToken);

        // Parse response (simplified for skeleton)
        // Full implementation will parse structured JSON with intent, confidence, entities
        var intent = ParseIntent(message);
        var confidence = 0.85; // Placeholder

        var needsClarification = confidence < IntentConfidenceThreshold;

        return new IntentClassification
        {
            Intent = intent,
            Confidence = confidence,
            Entities = ExtractEntities(message),
            NeedsClarification = needsClarification,
            ClarificationQuestion = needsClarification
                ? "I'm not sure I understand. Could you please rephrase or provide more details?"
                : null
        };
    }

    /// <summary>
    /// Execute operations based on classified intent.
    /// </summary>
    private async IAsyncEnumerable<BuilderStreamChunk> ExecuteIntentAsync(
        IntentClassification classification,
        BuilderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        switch (classification.Intent)
        {
            case BuilderIntent.CreatePlaybook:
                yield return BuilderStreamChunk.Message("Let me create a playbook structure for you.");
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

                yield return BuilderStreamChunk.Message($"Playbook created with {createdNodeIds.Count} nodes connected in sequence.");
                break;

            case BuilderIntent.AddNode:
                var nodeType = classification.Entities?.GetValueOrDefault("nodeType") ?? "action";
                yield return BuilderStreamChunk.Operation(new CanvasPatch
                {
                    Operation = CanvasPatchOperation.AddNode,
                    Node = new CanvasNode
                    {
                        Id = Guid.NewGuid().ToString("N")[..8],
                        Type = nodeType,
                        Label = $"New {nodeType} node",
                        Position = new NodePosition(100, 100)
                    }
                });
                yield return BuilderStreamChunk.Message($"Added a new {nodeType} node.");
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
                    yield return BuilderStreamChunk.Message("Removed the node.");
                }
                break;

            case BuilderIntent.ConnectNodes:
                yield return BuilderStreamChunk.Message("I'll connect those nodes for you.");
                // Would extract source/target from entities and create edge
                break;

            case BuilderIntent.ConfigureNode:
                yield return BuilderStreamChunk.Message("Updating the node configuration.");
                break;

            case BuilderIntent.SearchScopes:
                yield return BuilderStreamChunk.Message("Searching for matching scopes...");
                // Would call _scopeResolver.SearchScopesAsync
                break;

            case BuilderIntent.TestPlaybook:
                yield return BuilderStreamChunk.Message("I'll help you test the playbook.");
                break;

            case BuilderIntent.SavePlaybook:
                yield return BuilderStreamChunk.Message("Saving your playbook.");
                break;

            case BuilderIntent.AskQuestion:
                yield return BuilderStreamChunk.Message("Let me help you with that question.");
                // Would generate helpful response about playbook building
                break;

            default:
                yield return BuilderStreamChunk.Message(
                    "I'm not sure how to help with that. Try asking me to add a node, " +
                    "connect nodes, or create a playbook.");
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

        // Simple extraction - full implementation uses AI
        if (message.Contains("action", StringComparison.OrdinalIgnoreCase))
            entities["nodeType"] = "action";
        else if (message.Contains("skill", StringComparison.OrdinalIgnoreCase))
            entities["nodeType"] = "skill";
        else if (message.Contains("tool", StringComparison.OrdinalIgnoreCase))
            entities["nodeType"] = "tool";
        else if (message.Contains("knowledge", StringComparison.OrdinalIgnoreCase))
            entities["nodeType"] = "knowledge";

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
    /// Build system prompt for intent classification.
    /// </summary>
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

        var startTime = DateTime.UtcNow;
        var nodesExecuted = 0;
        var nodesSkipped = 0;
        var nodesFailed = 0;
        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        // Get canvas state (either from PlaybookId lookup or directly from request)
        var canvasState = request.CanvasJson;
        if (canvasState == null && request.PlaybookId.HasValue)
        {
            // In production, would load from Dataverse
            // For now, yield error if no canvas provided
            yield return new TestExecutionEvent
            {
                Type = TestEventTypes.Error,
                Data = new { message = "Playbook loading not yet implemented. Provide CanvasJson for testing." },
                Done = true
            };
            yield break;
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

        var nodes = canvasState.Nodes;
        var totalSteps = request.Options?.MaxNodes ?? nodes.Length;
        totalSteps = Math.Min(totalSteps, nodes.Length);

        _logger.LogDebug("Test execution will process {NodeCount} nodes", totalSteps);

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

            // Simulate node execution based on mode
            var nodeStartTime = DateTime.UtcNow;
            object? nodeOutput = null;
            var nodeSuccess = true;
            string? nodeError = null;
            var nodeTokens = new TokenUsageData();

            try
            {
                switch (request.Mode)
                {
                    case TestMode.Mock:
                        // Mock mode: Generate sample output without calling AI
                        (nodeOutput, nodeTokens) = await ExecuteMockNodeAsync(node, cancellationToken);
                        break;

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
