// -----------------------------------------------------------------------------
// ArmAppServiceSettingsWriter.cs
//
// Task 253 (G38) — production <see cref="IAppServiceSettingsWriter"/>. The
// ARM-SDK equivalent of the two `az webapp config appsettings set --settings
// @settings` calls (production, then `--slot staging`) the generated
// Configure-AppServiceSettings script made, over the same
// Azure.ResourceManager.AppService client path H9's ArmSlotStickyAppSettingWriter
// and H13's CustomerIdentityT7Probe use: an ArmClient built from the shared
// UAMI-pinned TokenCredential (ADR-028 MI-outbound — the L2 identity holds
// Owner on the customer subscription, T228). No process, no az CLI.
//
// SDK SHAPES (Azure.ResourceManager.AppService 1.5.0):
//   - WebSiteResource.GetApplicationSettingsAsync(ct)          POST .../sites/{app}/config/appsettings/list
//   - WebSiteResource.UpdateApplicationSettingsAsync(dict, ct) PUT  .../sites/{app}/config/appsettings
//   - WebSiteResource.GetWebSiteSlotAsync(slot, ct)            GET  .../sites/{app}/slots/{slot}
//   - WebSiteSlotResource.GetApplicationSettingsSlotAsync(ct)  POST .../slots/{slot}/config/appsettings/list
//   - WebSiteSlotResource.UpdateApplicationSettingsSlotAsync(dict, ct)
//                                                              PUT  .../slots/{slot}/config/appsettings
//   The PUT replaces the WHOLE dictionary — hence read, merge, write back
//   whole (az CLI's `appsettings set` does the same internally).
//
// NAME COMPARISON: ordinal, as az CLI's merge (a Python dict) compared them.
// -----------------------------------------------------------------------------

using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>
/// Merges app settings into an App Service's production site and <c>staging</c> slot through
/// Azure.ResourceManager.AppService — read, merge, write back whole; no write when nothing changes.
/// </summary>
/// <remarks>
/// <b>Component justification (CLAUDE.md §11).</b>
/// <i>Existing:</i> <c>BffDeploy.ArmSlotStickyAppSettingWriter</c> (H9) read-merge-writes ONE setting on one slot and
/// marks it slot-sticky; H13's probes only read. <i>Extension:</i> its contract is one sticky setting on a non-production
/// slot with a sticky-name write first — a different result shape and reason to change; the reuse is the merge rule
/// itself (<see cref="MergeAppSettings"/>, which that writer now calls) and the same ArmClient registration.
/// <i>Cost of doing nothing:</i> H4b ran <c>pwsh -File scripts/…/Configure-AppServiceSettings.generated.ps1</c> on a host
/// with no pwsh and no <c>scripts/</c> folder, so every live run failed at H4b (G38).
/// </remarks>
public sealed class ArmAppServiceSettingsWriter : IAppServiceSettingsWriter
{
    /// <summary>The deployment slot H4b writes besides production — the slot customer.bicep creates and H9 deploys to.</summary>
    internal const string StagingSlotName = "staging";

    /// <summary>Name used for the production site in results and diagnostics.</summary>
    internal const string ProductionSlotName = "production";

    private readonly ArmClient _armClient;
    private readonly ILogger<ArmAppServiceSettingsWriter> _logger;

    /// <summary>Constructs the writer over an ArmClient bound to the L2 identity.</summary>
    public ArmAppServiceSettingsWriter(ArmClient armClient, ILogger<ArmAppServiceSettingsWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AppServiceSettingsWriteResult> MergeAsync(
        AppServiceSettingsWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AppServiceName);
        ArgumentNullException.ThrowIfNull(request.Settings);

        var site = _armClient.GetWebSiteResource(WebSiteResource.CreateResourceIdentifier(
            request.SubscriptionId, request.ResourceGroupName, request.AppServiceName));
        var written = new List<string>(2);
        var slot = ProductionSlotName;
        try
        {
            // (1) Production first — the order the generated script used.
            var productionSettings = await site.GetApplicationSettingsAsync(cancellationToken).ConfigureAwait(false);
            var production = MergeAppSettings(productionSettings.Value.Properties, request.Settings, request.ExclusiveListKeys);
            if (production.ChangedNames.Count > 0)
            {
                await site.UpdateApplicationSettingsAsync(ToDictionary(production.Settings), cancellationToken)
                    .ConfigureAwait(false);
                written.Add(ProductionSlotName);
            }
            LogSlot(request, ProductionSlotName, production.ChangedNames);

            // (2) The staging slot — so a staging → production swap cannot drop a setting (T238 / T7).
            slot = StagingSlotName;
            var stagingSlot = (await site.GetWebSiteSlotAsync(StagingSlotName, cancellationToken).ConfigureAwait(false)).Value;
            var stagingSettings = await stagingSlot.GetApplicationSettingsSlotAsync(cancellationToken).ConfigureAwait(false);
            var staging = MergeAppSettings(stagingSettings.Value.Properties, request.Settings, request.ExclusiveListKeys);
            if (staging.ChangedNames.Count > 0)
            {
                await stagingSlot.UpdateApplicationSettingsSlotAsync(ToDictionary(staging.Settings), cancellationToken)
                    .ConfigureAwait(false);
                written.Add(StagingSlotName);
            }
            LogSlot(request, StagingSlotName, staging.ChangedNames);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "H4b app-settings merge failed: appService={AppService} slot={Slot} status={Status} errorCode={ErrorCode}",
                request.AppServiceName, slot, ex.Status, ex.ErrorCode);
            return new AppServiceSettingsWriteResult.Failure(
                $"ARM refused the app-settings merge on the {slot} slot of App Service '{request.AppServiceName}' " +
                $"(resource group '{request.ResourceGroupName}', HTTP {ex.Status}, {ex.ErrorCode ?? "no-error-code"}): " +
                $"{FirstLine(ex.Message)}" +
                (written.Count > 0 ? $" Already written: {string.Join(", ", written)} — a re-run converges." : string.Empty));
        }

        return new AppServiceSettingsWriteResult.Success(written);
    }

    /// <summary>
    /// Pure merge of a slot's app settings with the requested ones: every existing setting is kept; each requested
    /// setting takes its requested value. <c>ChangedNames</c> lists the requested settings that were absent or held
    /// another value (ordinal) — empty means the slot already matches and nothing needs writing.
    /// Task 255: an existing setting under one of <paramref name="exclusiveListKeys"/> (see
    /// <see cref="IsUnderListKey"/>) that <paramref name="requested"/> does not name is removed, and listed in
    /// <c>ChangedNames</c>.
    /// </summary>
    internal static (IReadOnlyDictionary<string, string> Settings, IReadOnlyList<string> ChangedNames) MergeAppSettings(
        IEnumerable<KeyValuePair<string, string>>? currentSettings,
        IReadOnlyDictionary<string, string> requested,
        IReadOnlyList<string>? exclusiveListKeys = null)
    {
        ArgumentNullException.ThrowIfNull(requested);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (currentSettings is not null)
        {
            foreach (var (key, value) in currentSettings)
            {
                settings[key] = value;
            }
        }

        var changed = new List<string>();
        foreach (var listKey in exclusiveListKeys ?? [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(listKey);
            foreach (var stale in settings.Keys
                         .Where(k => IsUnderListKey(k, listKey) && !requested.ContainsKey(k))
                         .OrderBy(k => k, StringComparer.Ordinal)
                         .ToList())
            {
                settings.Remove(stale);
                changed.Add(stale);
            }
        }

        foreach (var (key, value) in requested.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            if (settings.TryGetValue(key, out var existing) && string.Equals(existing, value, StringComparison.Ordinal))
            {
                continue;
            }
            settings[key] = value;
            changed.Add(key);
        }

        return (settings, changed);
    }

    /// <summary>
    /// Task 255: true when <paramref name="settingName"/> binds into the .NET configuration list
    /// <paramref name="listKey"/> — the bare key or any child of it (<c>{listKey}__*</c>). Case-insensitive and with
    /// <c>:</c> read as <c>__</c>, because that is how .NET configuration (the BFF) binds an app setting; the list
    /// binder adds EVERY child, numeric or not.
    /// </summary>
    internal static bool IsUnderListKey(string settingName, string listKey)
    {
        var name = settingName.Replace(":", "__", StringComparison.Ordinal);
        var key = listKey.Replace(":", "__", StringComparison.Ordinal);
        return string.Equals(name, key, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(key + "__", StringComparison.OrdinalIgnoreCase);
    }

    private static AppServiceConfigurationDictionary ToDictionary(IReadOnlyDictionary<string, string> settings)
    {
        var update = new AppServiceConfigurationDictionary();
        foreach (var (key, value) in settings)
        {
            update.Properties[key] = value;
        }
        return update;
    }

    // Setting NAMES and counts only — values include Key Vault references and endpoints and are never logged.
    private void LogSlot(AppServiceSettingsWriteRequest request, string slot, IReadOnlyList<string> changedNames)
        => _logger.LogInformation(
            "H4b app settings merged: appService={AppService} slot={Slot} requested={RequestedCount} changed={ChangedCount} changedNames={ChangedNames}",
            request.AppServiceName, slot, request.Settings.Count, changedNames.Count, string.Join(",", changedNames));

    private static string FirstLine(string message)
    {
        var newline = message.IndexOfAny(['\r', '\n']);
        return newline < 0 ? message : message[..newline];
    }
}
