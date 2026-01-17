using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Prompts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for generating build plans from user requirements.
/// Uses AI (o1-mini) for complex reasoning to create structured execution plans.
/// </summary>
/// <remarks>
/// Build plan generation is used for CREATE_PLAYBOOK intent where the user
/// describes a complete playbook they want to build. The service:
/// - Analyzes the user's goal and document context
/// - Generates a structured plan with ordered execution steps
/// - Identifies required scopes (Actions, Skills, Knowledge, Tools)
/// - Validates the plan structure before returning
/// </remarks>
public class BuildPlanGenerationService : IBuildPlanGenerationService
{
    private readonly IOpenAiClient _openAiClient;
    private readonly IModelSelector _modelSelector;
    private readonly ILogger<BuildPlanGenerationService> _logger;

    // Confidence threshold for requiring user confirmation
    private const double ConfirmationThreshold = 0.75;

    // JSON serialization options
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BuildPlanGenerationService(
        IOpenAiClient openAiClient,
        IModelSelector modelSelector,
        ILogger<BuildPlanGenerationService> logger)
    {
        _openAiClient = openAiClient;
        _modelSelector = modelSelector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BuildPlanGenerationResult> GenerateBuildPlanAsync(
        BuildPlanGenerationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Goal);

        _logger.LogInformation(
            "Generating build plan for goal: {Goal}",
            context.Goal.Length > 100 ? context.Goal[..100] + "..." : context.Goal);

        try
        {
            // Build the prompt with context
            var prompt = BuildGenerationPrompt(context);

            // Select the model for plan generation (o1-mini for complex reasoning)
            var model = _modelSelector.SelectModel(OperationType.PlanGeneration);

            _logger.LogDebug("Using model {Model} for build plan generation", model);

            // Call AI for plan generation
            var response = await _openAiClient.GetCompletionAsync(
                prompt,
                model,
                cancellationToken);

            // Parse and validate the response
            var plan = ParseBuildPlanResponse(response);

            // Validate the plan structure
            var validationResult = ValidatePlan(plan);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning(
                    "Generated plan failed validation: {Errors}",
                    string.Join("; ", validationResult.Errors));

                return BuildPlanGenerationResult.Failed(
                    $"Plan validation failed: {string.Join("; ", validationResult.Errors)}");
            }

            _logger.LogInformation(
                "Successfully generated build plan with {StepCount} steps, {NodeCount} estimated nodes",
                plan.Steps.Length,
                plan.EstimatedNodeCount);

            // Determine if confirmation is needed
            var requiresConfirmation = plan.Confidence < ConfirmationThreshold
                || plan.EstimatedNodeCount > 10
                || validationResult.Warnings.Count > 0;

            var confirmationMessage = requiresConfirmation
                ? BuildConfirmationMessage(plan)
                : null;

            return BuildPlanGenerationResult.Successful(
                plan,
                requiresConfirmation,
                confirmationMessage,
                validationResult.Warnings.Count > 0 ? validationResult.Warnings.ToArray() : null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse build plan response");
            return BuildPlanGenerationResult.Failed(
                "Failed to parse AI response. Please try again with more specific requirements.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating build plan");
            throw;
        }
    }

    /// <summary>
    /// Build the complete prompt for plan generation.
    /// </summary>
    private static string BuildGenerationPrompt(BuildPlanGenerationContext context)
    {
        var systemPrompt = PlaybookBuilderSystemPrompt.BuildPlanGeneration;
        var canvasSection = BuildCanvasSection(context.CurrentCanvas);
        var scopesSection = BuildAvailableScopesSection(context.AvailableScopes);

        var documentTypesInfo = context.DocumentTypes?.Length > 0
            ? $"Target document types: {string.Join(", ", context.DocumentTypes)}"
            : "No specific document types specified";

        var matterTypesInfo = context.MatterTypes?.Length > 0
            ? $"Matter types: {string.Join(", ", context.MatterTypes)}"
            : "";

        return $$"""
            {{systemPrompt}}

            ## Input Context

            ### User's Goal
            {{context.Goal}}

            ### Document Context
            {{documentTypesInfo}}
            {{matterTypesInfo}}

            ### Current Canvas State
            {{canvasSection}}

            ### Available Scopes
            {{scopesSection}}

            ## Instructions

            Generate a complete build plan for the playbook described above.
            Return ONLY valid JSON matching the BuildPlan format. No markdown code blocks.

            Required JSON structure:
            {
              "summary": "Brief description",
              "description": "Detailed description (optional)",
              "documentTypes": ["TYPE1", "TYPE2"],
              "matterTypes": ["MATTER1"],
              "estimatedNodeCount": 8,
              "confidence": 0.85,
              "pattern": "lease-analysis",
              "reasoning": "Why this structure",
              "steps": [
                {
                  "order": 1,
                  "action": "addNode",
                  "description": "What this step does",
                  "nodeSpec": {
                    "type": "aiAnalysis",
                    "label": "Node Label",
                    "position": { "x": 200, "y": 100 },
                    "configuration": {
                      "outputVariable": "varName"
                    }
                  },
                  "dependsOn": []
                },
                {
                  "order": 2,
                  "action": "createEdge",
                  "description": "Connect step 1 to step 2",
                  "edgeSpec": {
                    "sourceRef": "step_1",
                    "targetRef": "step_2"
                  },
                  "dependsOn": ["step_1"]
                },
                {
                  "order": 3,
                  "action": "linkScope",
                  "description": "Link action to node",
                  "scopeReference": {
                    "type": "action",
                    "name": "TL;DR Summary",
                    "isExisting": true,
                    "searchQuery": "summary action"
                  },
                  "parameters": { "nodeRef": "step_1" },
                  "dependsOn": ["step_1"]
                }
              ],
              "scopeRequirements": {
                "actions": [{ "name": "TL;DR Summary", "exists": true }],
                "skills": [{ "name": "Legal Analysis", "exists": true }],
                "knowledge": [],
                "tools": []
              }
            }

            Guidelines:
            - Start with core analysis nodes, then add edges, then link scopes
            - Use step_N references (step_1, step_2) for dependsOn and edge references
            - Position nodes in a logical flow (top to bottom, 200px spacing)
            - Include all nodes needed for the analysis goal
            - End with a deliverOutput node for final results
            """;
    }

    /// <summary>
    /// Build canvas context section.
    /// </summary>
    private static string BuildCanvasSection(CanvasStateSummary? canvas)
    {
        if (canvas == null || canvas.NodeCount == 0)
        {
            return "Canvas is empty (new playbook). Start fresh.";
        }

        var nodeList = canvas.ExistingNodes?.Length > 0
            ? string.Join("\n", canvas.ExistingNodes.Select(n =>
                $"  - {n.Id}: {n.Type} \"{n.Label ?? "unnamed"}\""))
            : "  (no node details)";

        return $"""
            Existing canvas with {canvas.NodeCount} nodes and {canvas.EdgeCount} edges.
            {canvas.Description ?? ""}

            Existing nodes:
            {nodeList}

            Consider extending or modifying existing structure.
            """;
    }

    /// <summary>
    /// Build available scopes section.
    /// </summary>
    private static string BuildAvailableScopesSection(AvailableScopes? scopes)
    {
        if (scopes == null)
        {
            return "No scope information available. Suggest creating new scopes as needed.";
        }

        var sections = new List<string>();

        if (scopes.Actions?.Length > 0)
        {
            var list = string.Join("\n", scopes.Actions.Take(10).Select(s =>
                $"  - {s.Id}: {s.Name}" + (s.Description != null ? $" - {s.Description}" : "")));
            sections.Add($"Actions:\n{list}");
        }

        if (scopes.Skills?.Length > 0)
        {
            var list = string.Join("\n", scopes.Skills.Take(10).Select(s =>
                $"  - {s.Id}: {s.Name}" + (s.Description != null ? $" - {s.Description}" : "")));
            sections.Add($"Skills:\n{list}");
        }

        if (scopes.Knowledge?.Length > 0)
        {
            var list = string.Join("\n", scopes.Knowledge.Take(10).Select(s =>
                $"  - {s.Id}: {s.Name}"));
            sections.Add($"Knowledge:\n{list}");
        }

        if (scopes.Tools?.Length > 0)
        {
            var list = string.Join("\n", scopes.Tools.Take(10).Select(s =>
                $"  - {s.Id}: {s.Name}"));
            sections.Add($"Tools:\n{list}");
        }

        return sections.Count > 0
            ? string.Join("\n\n", sections)
            : "No scopes available. Suggest creating new scopes.";
    }

    /// <summary>
    /// Parse the AI response into a BuildPlan.
    /// </summary>
    private BuildPlan ParseBuildPlanResponse(string response)
    {
        // Clean up response (remove markdown code blocks if present)
        var cleanResponse = CleanJsonResponse(response);

        // Parse the JSON
        var plan = JsonSerializer.Deserialize<BuildPlan>(cleanResponse, JsonOptions);

        if (plan == null)
        {
            throw new JsonException("Failed to parse build plan from AI response");
        }

        return plan;
    }

    /// <summary>
    /// Clean JSON response by removing markdown code blocks.
    /// </summary>
    private static string CleanJsonResponse(string response)
    {
        var cleaned = response.Trim();

        // Remove markdown code block if present
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[7..];
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned[3..];
        }

        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned[..^3];
        }

        return cleaned.Trim();
    }

    /// <summary>
    /// Validate the generated build plan.
    /// </summary>
    private static PlanValidationResult ValidatePlan(BuildPlan plan)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Check required fields
        if (string.IsNullOrWhiteSpace(plan.Summary))
        {
            errors.Add("Plan summary is required");
        }

        if (plan.Steps == null || plan.Steps.Length == 0)
        {
            errors.Add("Plan must have at least one execution step");
        }
        else
        {
            // Validate each step
            var stepRefs = new HashSet<string>();
            foreach (var step in plan.Steps)
            {
                var stepRef = $"step_{step.Order}";
                stepRefs.Add(stepRef);

                // Validate action type
                if (!ExecutionStepActions.IsValidAction(step.Action))
                {
                    errors.Add($"Step {step.Order}: Unknown action '{step.Action}'");
                }

                // Validate node spec for addNode
                if (step.Action == ExecutionStepActions.AddNode)
                {
                    if (step.NodeSpec == null)
                    {
                        errors.Add($"Step {step.Order}: addNode requires nodeSpec");
                    }
                    else if (!PlaybookNodeTypes.IsValidNodeType(step.NodeSpec.Type))
                    {
                        warnings.Add($"Step {step.Order}: Unknown node type '{step.NodeSpec.Type}'");
                    }
                }

                // Validate edge spec for createEdge
                if (step.Action == ExecutionStepActions.CreateEdge)
                {
                    if (step.EdgeSpec == null)
                    {
                        errors.Add($"Step {step.Order}: createEdge requires edgeSpec");
                    }
                }

                // Validate scope reference for linkScope
                if (step.Action == ExecutionStepActions.LinkScope)
                {
                    if (step.ScopeReference == null)
                    {
                        errors.Add($"Step {step.Order}: linkScope requires scopeReference");
                    }
                    else if (!ScopeTypes.IsValidScopeType(step.ScopeReference.Type))
                    {
                        warnings.Add($"Step {step.Order}: Unknown scope type '{step.ScopeReference.Type}'");
                    }
                }

                // Check dependencies reference valid steps
                if (step.DependsOn != null)
                {
                    foreach (var dep in step.DependsOn)
                    {
                        // Dependencies should reference earlier steps
                        if (dep.StartsWith("step_", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(dep[5..], out var depOrder) && depOrder >= step.Order)
                            {
                                errors.Add($"Step {step.Order}: Cannot depend on step {depOrder} (circular or forward dependency)");
                            }
                        }
                    }
                }
            }

            // Check for at least one addNode action
            if (!plan.Steps.Any(s => s.Action == ExecutionStepActions.AddNode))
            {
                warnings.Add("Plan does not create any new nodes");
            }

            // Check for deliverOutput at end (warning only)
            var lastAddNode = plan.Steps
                .Where(s => s.Action == ExecutionStepActions.AddNode)
                .OrderByDescending(s => s.Order)
                .FirstOrDefault();

            if (lastAddNode?.NodeSpec?.Type != PlaybookNodeTypes.DeliverOutput)
            {
                warnings.Add("Consider adding a deliverOutput node at the end of the playbook");
            }
        }

        // Validate confidence range
        if (plan.Confidence < 0 || plan.Confidence > 1)
        {
            warnings.Add($"Confidence score {plan.Confidence} is out of expected range [0,1]");
        }

        // Validate estimated node count
        var actualNodeCount = plan.Steps?.Count(s => s.Action == ExecutionStepActions.AddNode) ?? 0;
        if (plan.EstimatedNodeCount != actualNodeCount)
        {
            warnings.Add($"Estimated node count ({plan.EstimatedNodeCount}) doesn't match actual addNode steps ({actualNodeCount})");
        }

        return new PlanValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Build a confirmation message for the user.
    /// </summary>
    private static string BuildConfirmationMessage(BuildPlan plan)
    {
        var nodeCount = plan.Steps.Count(s => s.Action == ExecutionStepActions.AddNode);
        var edgeCount = plan.Steps.Count(s => s.Action == ExecutionStepActions.CreateEdge);
        var scopeCount = plan.Steps.Count(s =>
            s.Action == ExecutionStepActions.LinkScope ||
            s.Action == ExecutionStepActions.CreateScope);

        var nodeTypes = plan.Steps
            .Where(s => s.Action == ExecutionStepActions.AddNode && s.NodeSpec != null)
            .Select(s => s.NodeSpec!.Label)
            .ToArray();

        var nodeList = nodeTypes.Length <= 5
            ? string.Join(", ", nodeTypes)
            : string.Join(", ", nodeTypes.Take(4)) + $", and {nodeTypes.Length - 4} more";

        return $"""
            I'll create a playbook with {nodeCount} nodes: {nodeList}.

            This includes {edgeCount} connections and {scopeCount} scope operations.

            Would you like me to proceed with this plan?
            """;
    }

    /// <summary>
    /// Result of plan validation.
    /// </summary>
    private record PlanValidationResult
    {
        public bool IsValid { get; init; }
        public List<string> Errors { get; init; } = [];
        public List<string> Warnings { get; init; } = [];
    }
}

/// <summary>
/// Interface for build plan generation service.
/// </summary>
public interface IBuildPlanGenerationService
{
    /// <summary>
    /// Generate a build plan from user requirements.
    /// </summary>
    /// <param name="context">The generation context including goal and available scopes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generation result containing the plan or error.</returns>
    Task<BuildPlanGenerationResult> GenerateBuildPlanAsync(
        BuildPlanGenerationContext context,
        CancellationToken cancellationToken = default);
}
