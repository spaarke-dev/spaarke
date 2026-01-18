using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Types of AI operations performed by the Playbook Builder.
/// Each operation type maps to an optimal model for cost/quality balance.
/// </summary>
public enum OperationType
{
    /// <summary>
    /// Classify user intent from natural language input.
    /// Uses fast, cheap model - structured output sufficient.
    /// </summary>
    IntentClassification,

    /// <summary>
    /// Resolve entities (nodes, scopes, playbooks) from user input.
    /// Uses fast model - quick lookups, pattern matching.
    /// </summary>
    EntityResolution,

    /// <summary>
    /// Generate multi-step execution plans for complex operations.
    /// Uses reasoning model - complex reasoning, multi-step planning.
    /// </summary>
    PlanGeneration,

    /// <summary>
    /// Generate scope content (Action prompts, Skill fragments, etc.).
    /// Uses capable model - high-quality text generation.
    /// </summary>
    ScopeGeneration,

    /// <summary>
    /// Validate operations and canvas state before execution.
    /// Uses fast model - quick checks, simple outputs.
    /// </summary>
    Validation,

    /// <summary>
    /// Generate explanations for user queries about operations or decisions.
    /// Uses fast model - quick, simple outputs.
    /// </summary>
    Explanation
}

/// <summary>
/// Configuration options for model selection in the Playbook Builder.
/// Allows overriding default model assignments per operation type.
/// </summary>
public class ModelSelectorOptions
{
    public const string SectionName = "ModelSelector";

    /// <summary>
    /// Model for intent classification. Default: gpt-4o-mini
    /// </summary>
    public string IntentClassificationModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Model for entity resolution. Default: gpt-4o-mini
    /// </summary>
    public string EntityResolutionModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Model for plan generation. Default: o1-mini
    /// </summary>
    public string PlanGenerationModel { get; set; } = "o1-mini";

    /// <summary>
    /// Model for scope content generation. Default: gpt-4o
    /// </summary>
    public string ScopeGenerationModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Model for validation operations. Default: gpt-4o-mini
    /// </summary>
    public string ValidationModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Model for explanation generation. Default: gpt-4o-mini
    /// </summary>
    public string ExplanationModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Default model when operation type is unknown. Default: gpt-4o
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-4o";
}

/// <summary>
/// Interface for model selection service.
/// </summary>
public interface IModelSelector
{
    /// <summary>
    /// Select the appropriate AI model for the given operation type.
    /// </summary>
    /// <param name="operationType">The type of AI operation to perform.</param>
    /// <returns>The model deployment name (e.g., "gpt-4o-mini", "o1-mini", "gpt-4o").</returns>
    string SelectModel(OperationType operationType);
}

/// <summary>
/// Selects the appropriate AI model based on operation type.
/// Implements tiered model selection for cost optimization:
/// - Fast/cheap models (gpt-4o-mini) for classification, validation, entity resolution
/// - Reasoning models (o1-mini) for complex multi-step planning
/// - Capable models (gpt-4o) for high-quality content generation
/// </summary>
public class ModelSelector : IModelSelector
{
    private readonly ModelSelectorOptions _options;
    private readonly ILogger<ModelSelector> _logger;

    public ModelSelector(
        IOptions<ModelSelectorOptions> options,
        ILogger<ModelSelector> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string SelectModel(OperationType operationType)
    {
        var model = operationType switch
        {
            OperationType.IntentClassification => _options.IntentClassificationModel,
            OperationType.EntityResolution => _options.EntityResolutionModel,
            OperationType.PlanGeneration => _options.PlanGenerationModel,
            OperationType.ScopeGeneration => _options.ScopeGenerationModel,
            OperationType.Validation => _options.ValidationModel,
            OperationType.Explanation => _options.ExplanationModel,
            _ => _options.DefaultModel
        };

        _logger.LogDebug(
            "Selected model {Model} for operation type {OperationType}",
            model,
            operationType);

        return model;
    }
}
