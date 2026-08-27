// -----------------------------------------------------------------------------
// ArmOpenAiDeploymentSetRecomposer.cs
//
// HANDLER-13 (Wave 2 pre-dispatch remediation 2026-08-27) — F5 verbatim.
// Production <see cref="IOpenAiDeploymentSetRecomposer"/>. Wave 2
// scaffold: logs the intended recompose call + returns the full set
// unchanged. Live TPM-read + drop logic lands as a follow-on incremental
// change without touching H2a or the seam.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>Production <see cref="IOpenAiDeploymentSetRecomposer"/> — Wave 2 scaffold.</summary>
public sealed class ArmOpenAiDeploymentSetRecomposer : IOpenAiDeploymentSetRecomposer
{
    private readonly ILogger<ArmOpenAiDeploymentSetRecomposer> _logger;

    /// <summary>Constructs the recomposer.</summary>
    public ArmOpenAiDeploymentSetRecomposer(ILogger<ArmOpenAiDeploymentSetRecomposer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<OpenAiDeploymentSetRecomposeResult> RecomposeAsync(
        OpenAiDeploymentSetRecomposeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "HANDLER-13 scaffold: OpenAI deployment-set auto-recompose requested for region={Region} " +
            "{ModelCount} pinned models. Wave 2 scaffold returns the full set unchanged — the live " +
            "TPM-read + drop logic lands as a follow-on incremental change.",
            request.Region, request.FullPinnedSet.Count);

        return Task.FromResult(new OpenAiDeploymentSetRecomposeResult(
            PreservedSet: request.FullPinnedSet,
            DroppedModelIds: Array.Empty<string>(),
            OperatorNote: string.Empty));
    }
}
