// -----------------------------------------------------------------------------
// ArmSlotSwapper.cs
//
// Production <see cref="IAppServiceSlotSwapper"/> — task 132 (Wave G-3,
// Option D hybrid / DS-4 §5 re-scope) SDK port of
// <see cref="AzCliAppServiceSlotSwapper"/> (shelled out to `az webapp
// deployment slot swap`; RETIRED, kept on disk unregistered — see that
// file's retirement banner). IMPLEMENTS THE SAME <see cref="IAppServiceSlotSwapper"/>
// INTERFACE UNCHANGED — H9BffDeployHandler's swap + rollback-re-swap call
// sites are NOT modified by this task (DS-4 §5 point 4 + this project's
// binding constraint "PRESERVE the existing rollback-re-swap logic
// unchanged"); only the DI registration in Worker/Program.cs swaps from
// AzCliAppServiceSlotSwapper to this class.
//
// GROUND-TRUTHED SDK SHAPES (verified via reflection against the installed
// Azure.ResourceManager.AppService 1.5.0 package BEFORE writing this file,
// not guessed — parity with task 125's ArmAppServiceIdentityPatcher.cs header
// discipline):
//   - WebSiteResource.GetWebSiteSlotAsync(string slot, CancellationToken) ->
//     Task&lt;Response&lt;WebSiteSlotResource&gt;&gt; — same accessor
//     ArmAppServiceIdentityPatcher.cs already uses for the T1 PATCH.
//   - WebSiteSlotResource.SwapSlotAsync(WaitUntil, CsmSlotEntity, ct) ->
//     Task&lt;ArmOperation&gt; — a PROPER awaited LRO (not a fire-and-forget,
//     not a stdout-parsed CLI call). Called on the SOURCE slot's resource
//     (e.g. "staging"); CsmSlotEntity.TargetSlot names the slot to swap
//     WITH (e.g. "production"). "production" itself is NEVER resolved via
//     GetWebSiteSlotAsync — it is not a member of the site's Slots
//     collection (production IS the root WebSiteResource) — this port only
//     ever calls GetWebSiteSlotAsync on the STAGING slot name, matching
//     H9's SlotSwapRequest shape where SourceSlotName is always "staging"
//     for both the initial swap and the rollback re-swap (identical
//     request both times — a slot swap is self-inverse; re-invoking it
//     restores the prior state).
//   - CsmSlotEntity(string targetSlot, bool preserveVnet) — positional
//     record-style ctor, both properties read-only after construction.
// -----------------------------------------------------------------------------

using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Swaps App Service deployment slots via
/// <see cref="WebSiteSlotResource.SwapSlotAsync(WaitUntil, CsmSlotEntity, CancellationToken)"/>
/// — a proper long-running ARM operation, awaited to completion.
/// </summary>
public sealed class ArmSlotSwapper : IAppServiceSlotSwapper
{
    private readonly ArmClient _armClient;
    private readonly BffDeployOptions _options;
    private readonly ILogger<ArmSlotSwapper> _logger;

    public ArmSlotSwapper(
        ArmClient armClient,
        IOptions<BffDeployOptions> options,
        ILogger<ArmSlotSwapper> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SlotSwapResult> SwapAsync(SlotSwapRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AppServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceSlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetSlotName);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var siteResourceId = WebSiteResource.CreateResourceIdentifier(
            request.SubscriptionId, request.ResourceGroupName, request.AppServiceName);
        var siteResource = _armClient.GetWebSiteResource(siteResourceId);

        _logger.LogInformation(
            "H9 ARM slot swap starting: appService={AppService} {Source}=>{Target}",
            request.AppServiceName, request.SourceSlotName, request.TargetSlotName);

        try
        {
            var sourceSlotResponse = await WithTimeoutAsync(
                ct => siteResource.GetWebSiteSlotAsync(request.SourceSlotName, ct),
                _options.SlotSwapTimeout, cancellationToken).ConfigureAwait(false);

            var slotSwapEntity = new CsmSlotEntity(request.TargetSlotName, preserveVnet: true);

            await WithTimeoutAsync(
                ct => sourceSlotResponse.Value.SwapSlotAsync(WaitUntil.Completed, slotSwapEntity, ct),
                _options.SlotSwapTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "H9 ARM slot swap failed: appService={AppService} {Source}=>{Target} status={Status} errorCode={ErrorCode}",
                request.AppServiceName, request.SourceSlotName, request.TargetSlotName, ex.Status, ex.ErrorCode);
            return new SlotSwapResult.Failure(
                $"ARM slot swap ({request.SourceSlotName}=>{request.TargetSlotName}) failed for App Service " +
                $"'{request.AppServiceName}' (HTTP {ex.Status}, {ex.ErrorCode ?? "no-error-code"}): {ex.Message}");
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "H9 ARM slot swap succeeded: appService={AppService} {Source}=>{Target} durationMs={DurationMs}",
            request.AppServiceName, request.SourceSlotName, request.TargetSlotName, stopwatch.ElapsedMilliseconds);

        return new SlotSwapResult.Success(stopwatch.Elapsed);
    }

    private static async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await action(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"ARM App Service slot-swap invocation timed out after {timeout}.");
        }
    }
}
