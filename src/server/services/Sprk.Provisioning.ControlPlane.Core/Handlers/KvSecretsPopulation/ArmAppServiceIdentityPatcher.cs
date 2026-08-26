// -----------------------------------------------------------------------------
// ArmAppServiceIdentityPatcher.cs
//
// Production <see cref="IAppServiceIdentityPatcher"/> — task 125 (Wave G-2,
// Option D hybrid) SDK port of <see cref="AzCliAppServiceIdentityPatcher"/>
// (shelled out to `az webapp update --set keyVaultReferenceIdentity=<UAMI-RID>`
// / `az webapp deployment slot update --set keyVaultReferenceIdentity=...`;
// RETIRED, kept on disk unregistered — see that file's retirement banner).
// PATCHes `keyVaultReferenceIdentity` on BOTH production + staging slots via
// Azure.ResourceManager.AppService — the T1 silent-fail trap PATCH H4 owns
// (design.md §4B T1 + spec.md FR-33). H4 also VERIFIES the PATCH landed via
// the EXISTING <see cref="BicepInfraDeploy.IArmKeyVaultRefProbe"/> seam
// (task 123's ArmKeyVaultRefProbe — reused unmodified by this task; see
// H4KvSecretsPopulationHandler.cs step (8) "T1 VERIFY").
//
// GROUND-TRUTHED SDK SHAPES (verified via reflection against the installed
// Azure.ResourceManager.AppService 1.5.0 package BEFORE writing this file,
// not guessed — parity with task 123's ArmKeyVaultRefProbe.cs header):
//   - WebSiteResource.UpdateAsync(SitePatchInfo, CancellationToken) ->
//     Task&lt;Response&lt;WebSiteResource&gt;&gt; — production slot PATCH.
//   - WebSiteSlotResource.UpdateAsync(SitePatchInfo, CancellationToken) ->
//     Task&lt;Response&lt;WebSiteSlotResource&gt;&gt; — staging slot PATCH
//     (obtained via WebSiteResource.GetWebSiteSlotAsync(slotName, ct), the
//     SAME accessor ArmKeyVaultRefProbe.cs already uses for the T1 READ).
//   - SitePatchInfo has a parameterless constructor + a settable
//     `string? KeyVaultReferenceIdentity` property — `new SitePatchInfo {
//     KeyVaultReferenceIdentity = uamiResourceId }` is the complete PATCH
//     payload; no other SitePatchInfo field needs to be set (ARM PATCH
//     semantics — unset properties are left unchanged server-side).
//
// BOTH-SLOTS INVARIANT (this project's MUST rule — CLAUDE.md + spec.md FR-33
// T1): both PATCH attempts are made UNCONDITIONALLY — a production-slot
// failure does NOT short-circuit the staging-slot attempt (parity with the
// retired collaborator's try/catch-per-slot shape), and ANY slot failure
// fails the WHOLE call (Failure, not a partial Success) so the handler
// classifies QuarantineRequired per §4C — see
// ArmAppServiceIdentityPatcherTests.cs's completeness test.
// -----------------------------------------------------------------------------

using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// PATCHes <c>keyVaultReferenceIdentity</c> to the UAMI resource id on BOTH
/// production + staging App Service slots via
/// <see cref="WebSiteResource.UpdateAsync(SitePatchInfo, CancellationToken)"/> /
/// <see cref="WebSiteSlotResource.UpdateAsync(SitePatchInfo, CancellationToken)"/>.
/// Constructed with an <see cref="ArmClient"/> so tests inject one built
/// against a fake HTTP transport — parity with <see cref="BicepInfraDeploy.ArmKeyVaultRefProbe"/>
/// (task 123), the sibling collaborator this class's PATCH pairs with for
/// the T1 read-after-write verification.
/// </summary>
public sealed class ArmAppServiceIdentityPatcher : IAppServiceIdentityPatcher
{
    private readonly ArmClient _armClient;
    private readonly KvSecretsPopulationOptions _options;
    private readonly ILogger<ArmAppServiceIdentityPatcher> _logger;

    /// <summary>Constructs the patcher. Production DI reuses the shared UAMI-pinned ArmClient factory pattern.</summary>
    public ArmAppServiceIdentityPatcher(
        ArmClient armClient,
        IOptions<KvSecretsPopulationOptions> options,
        ILogger<ArmAppServiceIdentityPatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AppServiceIdentityPatchResult> PatchKeyVaultReferenceIdentityAsync(
        AppServiceIdentityPatchInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ResourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.AppServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.StagingSlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.UserAssignedIdentityResourceId);

        var siteResourceId = WebSiteResource.CreateResourceIdentifier(
            input.SubscriptionId, input.ResourceGroupName, input.AppServiceName);
        var siteResource = _armClient.GetWebSiteResource(siteResourceId);

        var failures = new List<string>(capacity: 2);

        // (1) Production slot — UNCONDITIONAL, does not depend on staging.
        try
        {
            _logger.LogInformation(
                "H4 T1 PATCH prod slot: appService={AppService} rg={ResourceGroup} uami={UamiRid}",
                input.AppServiceName, input.ResourceGroupName, input.UserAssignedIdentityResourceId);
            var patch = new SitePatchInfo { KeyVaultReferenceIdentity = input.UserAssignedIdentityResourceId };
            await WithTimeoutAsync(
                ct => siteResource.UpdateAsync(patch, ct),
                _options.T1PatchTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"prod slot: {ex.GetType().Name}: {ex.Message}");
        }

        // (2) Staging slot — UNCONDITIONAL (attempted even if prod failed above)
        //     per this project's MUST rule: "do not patch production only".
        try
        {
            _logger.LogInformation(
                "H4 T1 PATCH staging slot: appService={AppService} slot={Slot} rg={ResourceGroup} uami={UamiRid}",
                input.AppServiceName, input.StagingSlotName, input.ResourceGroupName, input.UserAssignedIdentityResourceId);
            var slotResponse = await WithTimeoutAsync(
                ct => siteResource.GetWebSiteSlotAsync(input.StagingSlotName, ct),
                _options.T1PatchTimeout,
                cancellationToken).ConfigureAwait(false);
            var patch = new SitePatchInfo { KeyVaultReferenceIdentity = input.UserAssignedIdentityResourceId };
            await WithTimeoutAsync(
                ct => slotResponse.Value.UpdateAsync(patch, ct),
                _options.T1PatchTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"staging slot '{input.StagingSlotName}': {ex.GetType().Name}: {ex.Message}");
        }

        if (failures.Count > 0)
        {
            return new AppServiceIdentityPatchResult.Failure(
                $"T1 PATCH failed for App Service '{input.AppServiceName}': " +
                string.Join(" | ", failures));
        }

        return new AppServiceIdentityPatchResult.Success();
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
            throw new TimeoutException($"ARM App Service PATCH invocation timed out after {timeout}.");
        }
    }
}
