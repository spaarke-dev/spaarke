// -----------------------------------------------------------------------------
// IOpenAiDeploymentSetRecomposer.cs
//
// HANDLER-13 (Wave 2 pre-dispatch remediation 2026-08-27) — F5 verbatim.
// Fresh Azure subs auto-grant mini + embedding TPM generously but zero
// TPM for frontier tiers (gpt-5.4, gpt-5-pro). Deploying the full
// canonical model set on a fresh sub without frontier TPM fails H2a's
// OpenAI-scoped ARM deploy 20 min into the run. This seam gives H2a
// the option (per <c>OpenAiDeploymentSetPolicy.AutoRecompose</c>) of
// dropping zero-TPM models from the deploy set BEFORE H2a fires.
//
// STRICT POLICY (default) — the seam is not invoked; H2a deploys the full
// canonical set. Matches pre-Wave-2 behavior; operator waits for a
// support ticket to grant frontier TPM on the fresh sub before H2a can
// succeed.
//
// AUTO-RECOMPOSE POLICY — H2a invokes the recomposer BEFORE the deploy
// runner fires. The recomposer reads per-model TPM (via the shared
// ArmClient CognitiveServices usage API — parity with
// ArmCognitiveServicesTpmProbe) and returns a filtered model list
// dropping zero-TPM entries. H2a logs the drop as an operator-visible
// note.
//
// PRODUCTION IMPL:
//   <see cref="ArmOpenAiDeploymentSetRecomposer"/> is the LIVE production
//   implementation — queries the Azure.ResourceManager.CognitiveServices
//   regional usage endpoint via <c>SubscriptionResource.GetUsagesAsync</c>
//   and drops zero-TPM pinned models from the requested set. (The Wave-2
//   log-and-return scaffold was superseded 2026-08-27 in the same session
//   as the scaffold commit 74197c02e per the "Wave 2 scope + deviation"
//   paragraph that explicitly anticipated this follow-on.)
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Reads auto-granted TPM per model and DROPS zero-TPM entries from the
/// deployment set so H2a does not fail on frontier-tier gaps on a fresh
/// subscription. Invoked ONLY when
/// <see cref="BicepInfraDeployOptions.OpenAiDeploymentSetPolicy"/> is
/// <see cref="OpenAiDeploymentSetPolicy.AutoRecompose"/>.
/// </summary>
public interface IOpenAiDeploymentSetRecomposer
{
    /// <summary>
    /// Filters <paramref name="fullPinnedSet"/> to only models with
    /// non-zero auto-granted TPM in the target region. Returns the
    /// preserved set + a diagnostic string listing any dropped models.
    /// Domain outcomes never throw; infrastructure faults propagate to
    /// the caller who logs + proceeds with the full set (fail-safe).
    /// </summary>
    Task<OpenAiDeploymentSetRecomposeResult> RecomposeAsync(
        OpenAiDeploymentSetRecomposeRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Input for <see cref="IOpenAiDeploymentSetRecomposer.RecomposeAsync"/>.</summary>
public sealed record OpenAiDeploymentSetRecomposeRequest(
    string SubscriptionId,
    string Region,
    IReadOnlyList<PinnedModel> FullPinnedSet);

/// <summary>Result of <see cref="IOpenAiDeploymentSetRecomposer.RecomposeAsync"/>.</summary>
/// <param name="PreservedSet">Filtered model list — only models with non-zero auto-granted TPM.</param>
/// <param name="DroppedModelIds">Ids of models dropped due to zero TPM (empty when all preserved).</param>
/// <param name="OperatorNote">
/// Human-readable note describing what was dropped + why, suitable for
/// writing to the run's Cosmos notes / operator log. Non-empty when
/// <see cref="DroppedModelIds"/> is non-empty.
/// </param>
public sealed record OpenAiDeploymentSetRecomposeResult(
    IReadOnlyList<PinnedModel> PreservedSet,
    IReadOnlyList<string> DroppedModelIds,
    string OperatorNote);
