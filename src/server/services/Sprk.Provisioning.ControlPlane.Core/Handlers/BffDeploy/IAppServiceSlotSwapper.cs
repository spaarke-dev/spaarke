// -----------------------------------------------------------------------------
// IAppServiceSlotSwapper.cs
//
// L2 abstraction over the Azure App Service slot-swap ARM operation. The
// production impl (<see cref="AzCliAppServiceSlotSwapper"/>) shells out to
// <c>az webapp deployment slot swap</c>. Unit tests inject stubs to avoid
// az CLI + Azure round-trips.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="AzCliAppServiceSlotSwapper"/> shells out to
//       az CLI with a per-invocation exit-code + stderr interpretation.
//     - Test: stubs injected per unit test that construct
//       <see cref="SlotSwapResult"/> directly (see H9BffDeployHandlerTests).
//   Interface earns its keep — no NIH.
//
// SWAP + ROLLBACK SEMANTICS (blue-green per POML acceptance):
//   H9's happy path:
//     1. IBffDeployRunner deploys the new build to the STAGING slot.
//     2. H9 verifies staging /health.
//     3. H9 invokes SwapAsync(source=staging, target=production) — the
//        currently-running production instance moves to staging (the
//        "prior version"), and the newly-deployed staging instance moves
//        to production (the "new version").
//     4. H9 verifies production /health.
//     5a. If post-swap /health passes → happy path, deploy Success.
//     5b. If post-swap /health fails → H9 invokes SwapAsync(source=staging,
//         target=production) AGAIN — the prior version (currently on
//         staging) moves back to production; the failing new version
//         (currently on production) moves to staging. Deploy Failure with
//         SmokeTestFailedRolledBack.
//     5c. If both post-swap /health AND the rollback re-swap fail → deploy
//         Failure with SmokeTestFailedRollbackAlsoFailed (POML escalation
//         trigger 2: BOTH SLOTS BAD; do NOT retry).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Performs an Azure App Service deployment slot swap for H9. Production impl
/// shells out to <c>az webapp deployment slot swap</c>; test impls return
/// canned <see cref="SlotSwapResult"/>s.
/// </summary>
public interface IAppServiceSlotSwapper
{
    /// <summary>
    /// Swaps the source slot's contents INTO the target slot. Domain failures
    /// (ARM 4xx/5xx) do NOT throw — the outcome is carried in
    /// <see cref="SlotSwapResult"/>. Infrastructure faults (az CLI not on
    /// PATH, timeout, IO fault) MAY throw.
    /// </summary>
    /// <param name="request">Swap inputs (subscription id, resource group, App Service name, source + target slot names).</param>
    /// <param name="cancellationToken">Cancellation token — a long-running swap MUST honor it.</param>
    Task<SlotSwapResult> SwapAsync(SlotSwapRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single App Service slot-swap invocation.
/// </summary>
/// <param name="SubscriptionId">Target subscription id (az CLI scoping — ADR-027 D4).</param>
/// <param name="ResourceGroupName">Azure resource group hosting the App Service.</param>
/// <param name="AppServiceName">App Service name (the swap operates on this App Service's slots).</param>
/// <param name="SourceSlotName">Slot to swap FROM (typically <c>staging</c> for the initial swap and for the rollback).</param>
/// <param name="TargetSlotName">Slot to swap INTO (typically <c>production</c>).</param>
public sealed record SlotSwapRequest(
    string SubscriptionId,
    string ResourceGroupName,
    string AppServiceName,
    string SourceSlotName,
    string TargetSlotName);

/// <summary>
/// Discriminated result of <see cref="IAppServiceSlotSwapper.SwapAsync"/>.
/// Success = swap complete; Failure = swap not complete (source + target
/// unchanged).
/// </summary>
public abstract record SlotSwapResult
{
    private SlotSwapResult() { }

    /// <summary>Swap succeeded — the source slot's contents are now in the target slot (and vice versa).</summary>
    public sealed record Success(TimeSpan Duration) : SlotSwapResult;

    /// <summary>
    /// Swap failed. <paramref name="Diagnostic"/> is the operator-facing
    /// message (e.g. "az webapp deployment slot swap exit 1:
    /// AuthorizationFailed"). Source + target are UNCHANGED.
    /// </summary>
    public sealed record Failure(string Diagnostic) : SlotSwapResult;
}
