namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Response model for AI model deployment listing.
/// Maps to sprk_aimodeldeployment entity.
/// </summary>
public record ModelDeploymentDto
{
    /// <summary>
    /// Model deployment ID (primary key).
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Display name of the deployment.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// AI provider (AzureOpenAI, OpenAI, Anthropic).
    /// </summary>
    public AiProvider Provider { get; init; }

    /// <summary>
    /// Model capability (Chat, Completion, Embedding).
    /// </summary>
    public AiCapability Capability { get; init; }

    /// <summary>
    /// Model identifier (e.g., gpt-4, gpt-4o-mini).
    /// </summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>
    /// Maximum context window size in tokens.
    /// </summary>
    public int ContextWindow { get; init; }

    /// <summary>
    /// Optional description of the deployment.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this deployment is active and available for use.
    /// </summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// AI provider types.
/// Maps to sprk_aimodeldeployment.sprk_aiprovider choice values.
/// </summary>
public enum AiProvider
{
    /// <summary>Azure OpenAI Service.</summary>
    AzureOpenAI = 0,

    /// <summary>OpenAI API directly.</summary>
    OpenAI = 1,

    /// <summary>Anthropic Claude.</summary>
    Anthropic = 2
}

/// <summary>
/// AI model capability types.
/// Maps to sprk_aimodeldeployment.sprk_aicapability choice values.
/// </summary>
public enum AiCapability
{
    /// <summary>Chat completion (conversational).</summary>
    Chat = 0,

    /// <summary>Text completion (single-turn).</summary>
    Completion = 1,

    /// <summary>Text embedding generation.</summary>
    Embedding = 2
}
