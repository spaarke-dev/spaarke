namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Resolves a consumer-type key to the <see cref="AnalysisAction"/> row that
/// drives its LLM call.
/// </summary>
/// <remarks>
/// <para>
/// R7 Wave 12 (2026-07-02). See
/// <c>docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md</c>.
/// The AnalysisAction row is the source of truth for
/// {SystemPrompt, OutputSchemaJson, Temperature, ModelDeploymentId} — the
/// linear-path equivalent of what a playbook node's ConfigJson used to
/// contribute plus overrides.
/// </para>
/// </remarks>
public interface IActionResolver
{
    /// <summary>
    /// Resolve the AnalysisAction for the given consumer type. Throws
    /// <see cref="InvalidOperationException"/> when no Action id is configured
    /// or the row is not found in Dataverse (both are Should-Not-Happen at
    /// runtime for a properly wired consumer).
    /// </summary>
    Task<AnalysisAction> ResolveAsync(string consumerType, CancellationToken cancellationToken);
}
