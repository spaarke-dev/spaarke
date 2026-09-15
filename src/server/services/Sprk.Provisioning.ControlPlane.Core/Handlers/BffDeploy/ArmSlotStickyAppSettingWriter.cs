// -----------------------------------------------------------------------------
// ArmSlotStickyAppSettingWriter.cs
//
// Production <see cref="ISlotStickyAppSettingWriter"/> — GitHub #987 (ADR-036
// A1 rule 2). The ARM-SDK equivalent of `az webapp config appsettings set
// --slot S --slot-settings NAME=VALUE`, over the same
// Azure.ResourceManager.AppService client path <see cref="ArmSlotSwapper"/>
// uses (an ArmClient built from the shared UAMI-pinned TokenCredential —
// ADR-028 MI-outbound; no az CLI, no stored key).
//
// GROUND-TRUTHED SDK SHAPES (Azure.ResourceManager.AppService 1.5.0, the
// version Sprk.Provisioning.ControlPlane.Core.csproj references — member names
// checked against the package's XML documentation and by compiling; the HTTP
// verbs + paths they issue are exercised in ArmSlotStickyAppSettingWriterTests):
//   - WebSiteResource.GetSlotConfigNamesResource() -> SlotConfigNamesResource
//       .GetAsync(ct)                                  GET  .../sites/{app}/config/slotConfigNames
//       .CreateOrUpdateAsync(WaitUntil, SlotConfigNamesResourceData, ct)
//                                                      PUT  .../sites/{app}/config/slotConfigNames
//     SlotConfigNamesResourceData carries AppSettingNames, ConnectionStringNames
//     and AzureStorageConfigNames. The PUT replaces the whole object, so the
//     data the GET returned is written back with only AppSettingNames changed.
//   - WebSiteResource.GetWebSiteSlotAsync(slot, ct)    GET  .../sites/{app}/slots/{slot}
//     (the accessor ArmSlotSwapper uses — a missing slot fails here, clearly).
//   - WebSiteSlotResource.GetApplicationSettingsSlotAsync(ct)
//                                                      POST .../slots/{slot}/config/appsettings/list
//   - WebSiteSlotResource.UpdateApplicationSettingsSlotAsync(AppServiceConfigurationDictionary, ct)
//                                                      PUT  .../slots/{slot}/config/appsettings
//     The PUT replaces the whole dictionary — hence the merge.
//
// NAME COMPARISON: ordinal (case-sensitive), matching az CLI, whose merge is a
// Python dict assignment + list-membership test.
// -----------------------------------------------------------------------------

using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Sets one app setting on a deployment slot and marks it slot-sticky on the
/// parent site via Azure.ResourceManager.AppService — merge, never replace;
/// sticky name first; no write when already in place.
/// </summary>
/// <remarks>
/// <b>Component justification (CLAUDE.md §11).</b>
/// <i>Existing:</i> <see cref="IAppServiceSlotSwapper"/> / <see cref="ArmSlotSwapper"/> already reach the same
/// App Service through the same ArmClient and UAMI credential, and <c>infrastructure/bicep/modules/deployment-slot.bicep</c>
/// sets the same sticky guard — but only when it creates a slot; nothing in L2 writes a slot's app settings or
/// the site's sticky names. <i>Extension:</i> extending <see cref="IAppServiceSlotSwapper"/> was rejected — its
/// contract is the self-inverse swap, whose failure result promises "source + target unchanged" and whose test
/// fake is H9BffDeployHandlerTests' documented rollback-completeness proof; a read-merge-write of two config
/// resources is a different reason to change with a different result shape. The reuse is at the implementation
/// level instead: the same ArmClient-from-shared-TokenCredential registration, the same site/slot accessors and
/// the same per-call ceiling (<see cref="BffDeployOptions.SlotSwapTimeout"/>). <i>Cost of doing nothing:</i>
/// after an H9 deploy the staging slot runs the BFF's scheduled jobs against the customer's production data —
/// competing for the scheduler lease when it shares production's Redis, running every tick twice when it does
/// not (ADR-036 A1 rule 2, GitHub #987).
/// </remarks>
public sealed class ArmSlotStickyAppSettingWriter : ISlotStickyAppSettingWriter
{
    private readonly ArmClient _armClient;
    private readonly BffDeployOptions _options;
    private readonly ILogger<ArmSlotStickyAppSettingWriter> _logger;

    public ArmSlotStickyAppSettingWriter(
        ArmClient armClient,
        IOptions<BffDeployOptions> options,
        ILogger<ArmSlotStickyAppSettingWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SlotStickyAppSettingResult> EnsureAsync(
        SlotStickyAppSettingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AppServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SettingName);
        ArgumentNullException.ThrowIfNull(request.SettingValue);

        var siteResource = _armClient.GetWebSiteResource(WebSiteResource.CreateResourceIdentifier(
            request.SubscriptionId, request.ResourceGroupName, request.AppServiceName));
        var timeout = _options.SlotSwapTimeout;

        bool stickyNameAdded;
        bool settingWritten;
        try
        {
            // (1) STICKY FIRST (see ISlotStickyAppSettingWriter): make the name
            //     slot-sticky on the parent site before any value exists on the
            //     slot, so no intermediate state lets a swap carry it.
            var slotConfigNames = siteResource.GetSlotConfigNamesResource();
            var namesResponse = await WithTimeoutAsync(
                ct => slotConfigNames.GetAsync(ct), timeout, cancellationToken).ConfigureAwait(false);
            var namesData = namesResponse.Value.Data;

            var mergedNames = MergeStickyAppSettingNames(namesData.AppSettingNames, request.SettingName);
            stickyNameAdded = mergedNames.Changed;
            if (stickyNameAdded)
            {
                // Only AppSettingNames changes; ConnectionStringNames and
                // AzureStorageConfigNames go back exactly as read.
                namesData.AppSettingNames.Clear();
                foreach (var name in mergedNames.Names)
                {
                    namesData.AppSettingNames.Add(name);
                }
                await WithTimeoutAsync(
                    ct => slotConfigNames.CreateOrUpdateAsync(WaitUntil.Completed, namesData, ct),
                    timeout, cancellationToken).ConfigureAwait(false);
            }

            // (2) The value on the slot — read, merge, write back whole.
            var slotResponse = await WithTimeoutAsync(
                ct => siteResource.GetWebSiteSlotAsync(request.SlotName, ct), timeout, cancellationToken).ConfigureAwait(false);
            var slotResource = slotResponse.Value;

            var settingsResponse = await WithTimeoutAsync(
                ct => slotResource.GetApplicationSettingsSlotAsync(ct), timeout, cancellationToken).ConfigureAwait(false);

            var mergedSettings = MergeAppSetting(settingsResponse.Value.Properties, request.SettingName, request.SettingValue);
            settingWritten = mergedSettings.Changed;
            if (settingWritten)
            {
                var update = new AppServiceConfigurationDictionary();
                foreach (var (key, value) in mergedSettings.Settings)
                {
                    update.Properties[key] = value;
                }
                await WithTimeoutAsync(
                    ct => slotResource.UpdateApplicationSettingsSlotAsync(update, ct),
                    timeout, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "H9 slot-sticky app setting failed: appService={AppService} slot={Slot} setting={SettingName} status={Status} errorCode={ErrorCode}",
                request.AppServiceName, request.SlotName, request.SettingName, ex.Status, ex.ErrorCode);
            return new SlotStickyAppSettingResult.Failure(
                $"ARM rejected setting slot-sticky app setting '{request.SettingName}' on slot '{request.SlotName}' of " +
                $"App Service '{request.AppServiceName}' (HTTP {ex.Status}, {ex.ErrorCode ?? "no-error-code"}): {ex.Message}");
        }

        // Setting names + flags only — app-setting VALUES are never logged (they include Key Vault references).
        _logger.LogInformation(
            "H9 slot-sticky app setting in place: appService={AppService} slot={Slot} setting={SettingName} " +
            "stickyNameAdded={StickyNameAdded} settingWritten={SettingWritten}",
            request.AppServiceName, request.SlotName, request.SettingName, stickyNameAdded, settingWritten);

        return new SlotStickyAppSettingResult.Success(stickyNameAdded, settingWritten);
    }

    /// <summary>
    /// Pure merge of the site's sticky app-setting names: every existing name is
    /// kept, in order; <paramref name="settingName"/> is appended only when absent
    /// (ordinal). <c>Changed</c> is <c>false</c> when it was already sticky.
    /// </summary>
    internal static (IReadOnlyList<string> Names, bool Changed) MergeStickyAppSettingNames(
        IEnumerable<string>? currentNames,
        string settingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);

        var names = currentNames?.ToList() ?? new List<string>();
        if (names.Contains(settingName, StringComparer.Ordinal))
        {
            return (names, false);
        }

        names.Add(settingName);
        return (names, true);
    }

    /// <summary>
    /// Pure merge of a slot's app settings: every existing setting is kept;
    /// <paramref name="settingName"/> is set to <paramref name="settingValue"/>.
    /// <c>Changed</c> is <c>false</c> when it already held exactly that value
    /// (ordinal), so a re-run writes nothing.
    /// </summary>
    internal static (IReadOnlyDictionary<string, string> Settings, bool Changed) MergeAppSetting(
        IEnumerable<KeyValuePair<string, string>>? currentSettings,
        string settingName,
        string settingValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);
        ArgumentNullException.ThrowIfNull(settingValue);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (currentSettings is not null)
        {
            foreach (var (key, value) in currentSettings)
            {
                settings[key] = value;
            }
        }

        if (settings.TryGetValue(settingName, out var existing)
            && string.Equals(existing, settingValue, StringComparison.Ordinal))
        {
            return (settings, false);
        }

        settings[settingName] = settingValue;
        return (settings, true);
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
            throw new TimeoutException($"ARM App Service slot-sticky app-setting call timed out after {timeout}.");
        }
    }
}
