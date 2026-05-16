using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Ai.Foundry;

/// <summary>
/// Configuration options for Bing Grounding capability used by <see cref="Chat.Tools.LegalResearchTools"/>.
///
/// Bound from appsettings section <c>BingGrounding</c> in Program.cs / ConfigurationModule.
///
/// ADR-018: Kill switch — <see cref="Enabled"/> must be checked before any Bing Grounding call.
///          When false, tools return a user-readable degradation message immediately.
/// ADR-016: Concurrency — <see cref="MaxConcurrency"/> controls the SemaphoreSlim gate shared
///          across all LegalResearchTools instances.
/// ADR-015: Data governance — <see cref="BingConnectionName"/> identifies the AI Foundry
///          connection; no API key is stored here (auth uses Managed Identity through the
///          Azure AI Projects SDK, consistent with AgentServiceClient).
/// </summary>
public sealed class BingGroundingOptions
{
    /// <summary>Configuration section name used for binding.</summary>
    public const string SectionName = "BingGrounding";

    /// <summary>
    /// Kill switch (ADR-018). When <c>false</c>, all <see cref="Chat.Tools.LegalResearchTools"/>
    /// operations return a user-readable message without making any Bing API call.
    /// Default: <c>false</c> (opt-in — must be explicitly enabled in configuration).
    /// </summary>
    [Required]
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Name of the Bing Grounding connection registered in Azure AI Foundry.
    /// Used by the AgentsClient to locate the Bing connection when creating agent runs
    /// with <c>BingGroundingTool</c>. Retrieve from AI Foundry Studio > Connections.
    /// Required when <see cref="Enabled"/> is <c>true</c>.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string BingConnectionName { get; init; } = string.Empty;

    /// <summary>
    /// Maximum number of concurrent Bing Grounding operations per BFF instance (ADR-016).
    /// Enforced via a <see cref="System.Threading.SemaphoreSlim"/> shared across all
    /// LegalResearchTools instances. Operations that cannot acquire the semaphore within
    /// 30 seconds return a user-readable degradation message instead of throwing.
    /// Default: 3 concurrent operations (lighter than AgentService at 4 because Bing
    /// Grounding runs carry a full agent thread overhead).
    /// </summary>
    [Required]
    [Range(1, 32)]
    public int MaxConcurrency { get; init; } = 3;

    /// <summary>
    /// Maximum number of Bing search results to include per query (ADR-015: data minimisation).
    /// Capped at 10 to limit the volume of external content injected into the agent context.
    /// Default: 5.
    /// </summary>
    [Required]
    [Range(1, 10)]
    public int MaxResultsPerQuery { get; init; } = 5;
}
