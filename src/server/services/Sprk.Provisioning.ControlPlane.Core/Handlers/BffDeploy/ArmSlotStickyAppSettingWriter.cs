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
//   - slotConfigNames is RAW JSON over ArmClient.Pipeline, not the typed
//     SlotConfigNamesResource (T186 L43): on a site with no sticky names ARM
//     answers `"id": null` and SDK 1.5.0's deserializer throws
//     ArgumentNullException('resourceId').
//                                                      GET  .../sites/{app}/config/slotConfigNames
//                                                      PUT  .../sites/{app}/config/slotConfigNames
//     The body carries appSettingNames, connectionStringNames and
//     azureStorageConfigNames (each may be null). The PUT replaces the whole
//     object, so every property the GET returned is written back with only
//     appSettingNames changed.
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

using System.Text.Json.Nodes;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Prepares a deployment slot's app settings for a deploy + swap via Azure.ResourceManager.AppService: marks
/// one setting slot-sticky on the parent site (sticky name first), then makes the slot carry production's
/// non-sticky settings plus its own sticky ones and that setting (T186 L46); no write when already in place.
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
/// not (ADR-036 A1 rule 2, GitHub #987). The production-settings mirror (T186 L46) extends the same
/// read-merge-write of the same slot rather than adding a collaborator: without it, settings only customer.bicep
/// writes (production site only — Redis, the UAMI pin, AI endpoints) are absent on staging, the BFF there aborts at
/// startup, and a swap would strip them from production. H4b's own staging write covers only H4b's settings.
/// </remarks>
public sealed class ArmSlotStickyAppSettingWriter : ISlotStickyAppSettingWriter
{
    private static readonly Uri ArmEndpoint = new("https://management.azure.com");

    /// <summary>Microsoft.Web api-version for the raw slotConfigNames calls (GA; read live against an ACME stamp site 2026-10-16).</summary>
    internal const string SlotConfigNamesApiVersion = "2024-11-01";

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
        IReadOnlyList<string> mirroredNames = [];
        try
        {
            // (1) STICKY FIRST (see ISlotStickyAppSettingWriter): make the name
            //     slot-sticky on the parent site before any value exists on the
            //     slot, so no intermediate state lets a swap carry it.
            //     Raw JSON, not SlotConfigNamesResource (T186 L43): on a site that
            //     has never had a sticky name ARM answers `"id": null`, and the
            //     SDK's SlotConfigNamesResourceData deserializer (1.5.0, the latest)
            //     builds a ResourceIdentifier from it and throws
            //     ArgumentNullException('resourceId') — so the typed GET can never
            //     succeed on a new customer site.
            var slotConfigNamesUri = new Uri(
                ArmEndpoint, $"{siteResource.Id}/config/slotConfigNames?api-version={SlotConfigNamesApiVersion}");
            var namesObject = await WithTimeoutAsync(
                ct => SendSlotConfigNamesAsync(siteResource.Id, RequestMethod.Get, slotConfigNamesUri, null, ct),
                timeout, cancellationToken).ConfigureAwait(false);
            var namesProperties = namesObject["properties"] as JsonObject ?? new JsonObject();

            var mergedNames = MergeStickyAppSettingNames(ReadStringArray(namesProperties["appSettingNames"]), request.SettingName);
            stickyNameAdded = mergedNames.Changed;
            if (stickyNameAdded)
            {
                // The PUT replaces the whole object: only appSettingNames changes;
                // connectionStringNames and azureStorageConfigNames go back exactly as read.
                var putProperties = new JsonObject();
                foreach (var property in namesProperties)
                {
                    putProperties[property.Key] = property.Value?.DeepClone();
                }
                putProperties["appSettingNames"] = new JsonArray(mergedNames.Names.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
                var putBody = new JsonObject { ["properties"] = putProperties };
                await WithTimeoutAsync(
                    ct => SendSlotConfigNamesAsync(siteResource.Id, RequestMethod.Put, slotConfigNamesUri, putBody, ct),
                    timeout, cancellationToken).ConfigureAwait(false);
            }

            // (2) The slot's settings: production's non-sticky settings (what a swap carries into production),
            //     the slot's own sticky settings, and the guard — read both, build, write back whole (T186 L46).
            var productionSettings = await WithTimeoutAsync(
                ct => siteResource.GetApplicationSettingsAsync(ct), timeout, cancellationToken).ConfigureAwait(false);

            var slotResponse = await WithTimeoutAsync(
                ct => siteResource.GetWebSiteSlotAsync(request.SlotName, ct), timeout, cancellationToken).ConfigureAwait(false);
            var slotResource = slotResponse.Value;

            var settingsResponse = await WithTimeoutAsync(
                ct => slotResource.GetApplicationSettingsSlotAsync(ct), timeout, cancellationToken).ConfigureAwait(false);

            var staged = BuildStagingSettings(
                productionSettings.Value.Properties, settingsResponse.Value.Properties,
                mergedNames.Names, request.SettingName, request.SettingValue);
            settingWritten = staged.ChangedNames.Count > 0;
            mirroredNames = staged.ChangedNames;
            if (settingWritten)
            {
                var update = new AppServiceConfigurationDictionary();
                foreach (var (key, value) in staged.Settings)
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
            "stickyNameAdded={StickyNameAdded} settingWritten={SettingWritten} changedSettingNames={ChangedSettingNames}",
            request.AppServiceName, request.SlotName, request.SettingName, stickyNameAdded, settingWritten,
            string.Join(",", mirroredNames));

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
    /// Pure build of the staging slot's app settings before a deploy + swap (T186 L46): every NON-sticky
    /// production setting with production's value (a swap carries the slot's non-sticky settings into
    /// production, so anything else changes production's config — live, settings only Bicep wrote reached
    /// production alone, and the BFF on staging refused to start without <c>Redis__Enabled</c>); every sticky
    /// setting the slot already holds, kept; and <paramref name="settingName"/> = <paramref name="settingValue"/>.
    /// A non-sticky setting only the slot holds is dropped. <c>ChangedNames</c> (added, changed or removed;
    /// ordinal) is empty when the slot already matches, so a re-run writes nothing.
    /// </summary>
    internal static (IReadOnlyDictionary<string, string> Settings, IReadOnlyList<string> ChangedNames) BuildStagingSettings(
        IEnumerable<KeyValuePair<string, string>>? productionSettings,
        IEnumerable<KeyValuePair<string, string>>? slotSettings,
        IReadOnlyCollection<string> stickyNames,
        string settingName,
        string settingValue)
    {
        ArgumentNullException.ThrowIfNull(stickyNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);
        ArgumentNullException.ThrowIfNull(settingValue);

        var sticky = new HashSet<string>(stickyNames, StringComparer.Ordinal);
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in slotSettings ?? [])
        {
            current[key] = value;
        }

        var target = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in productionSettings ?? [])
        {
            if (!sticky.Contains(key))
            {
                target[key] = value;
            }
        }
        foreach (var (key, value) in current)
        {
            if (sticky.Contains(key))
            {
                target[key] = value;
            }
        }
        target[settingName] = settingValue;

        var changed = target
            .Where(t => !current.TryGetValue(t.Key, out var existing) || !string.Equals(existing, t.Value, StringComparison.Ordinal))
            .Select(t => t.Key)
            .Concat(current.Keys.Where(k => !target.ContainsKey(k)))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        return (target, changed);
    }

    /// <summary>
    /// GET or PUT <c>.../config/slotConfigNames</c> as raw JSON over the ArmClient's
    /// own pipeline (same credential, retries and transport as every typed call),
    /// reached through <see cref="SitePipeline"/> because <c>ArmClient.Pipeline</c> is internal.
    /// A non-2xx answer throws <see cref="RequestFailedException"/>, which
    /// <see cref="EnsureAsync"/> maps to a <see cref="SlotStickyAppSettingResult.Failure"/>.
    /// </summary>
    private async Task<JsonObject> SendSlotConfigNamesAsync(
        ResourceIdentifier siteId, RequestMethod method, Uri uri, JsonObject? body, CancellationToken cancellationToken)
    {
        var pipeline = new SitePipeline(_armClient, siteId).HttpPipeline;
        using var message = pipeline.CreateMessage();
        message.Request.Method = method;
        message.Request.Uri.Reset(uri);
        message.Request.Headers.Add("Accept", "application/json");
        if (body is not null)
        {
            message.Request.Headers.Add("Content-Type", "application/json");
            message.Request.Content = RequestContent.Create(BinaryData.FromString(body.ToJsonString()));
        }

        await pipeline.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var response = message.Response;
        if (response.Status is < 200 or > 299)
        {
            throw new RequestFailedException(response);
        }

        return JsonNode.Parse(response.Content.ToString()) as JsonObject ?? new JsonObject();
    }

    /// <summary>
    /// The SDK's own extension point for a resource it has no usable client for:
    /// an <see cref="ArmResource"/> bound to the site, whose <c>Pipeline</c> is the
    /// ArmClient's pipeline (credential, retry and transport policies included).
    /// </summary>
    private sealed class SitePipeline(ArmClient client, ResourceIdentifier siteId) : ArmResource(client, siteId)
    {
        public Azure.Core.Pipeline.HttpPipeline HttpPipeline => Pipeline;
    }

    /// <summary>A JSON string array, or <c>null</c> for a JSON <c>null</c> / absent property (a site with no sticky names).</summary>
    internal static IReadOnlyList<string>? ReadStringArray(JsonNode? node)
        => node is JsonArray array
            ? array.Select(item => item?.GetValue<string>()).OfType<string>().ToList()
            : null;

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
