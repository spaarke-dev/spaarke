// -----------------------------------------------------------------------------
// ArmResourceNameAvailabilityProbe.cs
//
// HANDLER-05 (Wave 2 pre-dispatch remediation 2026-08-27) — F10 verbatim
// absorption. Production <see cref="IResourceNameAvailabilityProbe"/>
// implementation. Uses:
//   - Azure.ResourceManager.Storage: StorageExtensions.CheckStorageAccountNameAvailabilityAsync
//   - Azure.ResourceManager.ServiceBus: ServiceBusExtensions.CheckServiceBusNamespaceNameAvailabilityAsync
// Both hit the subscription-scope `/providers/Microsoft.*/checkNameAvailability`
// endpoints — the ARM SDK returns a strongly-typed result with an
// `IsNameAvailable` bool + `Message` string + `Reason` code, mapped 1:1 onto
// our seam's <see cref="ResourceNameAvailabilityResult"/>.
//
// SHORT-CIRCUIT ON FIRST CONFLICT:
// The probe reports the FIRST conflict it observes, not all conflicts. The
// operator only needs one specific name to change to unblock the deploy;
// listing every conflict would produce an ambiguous "which of these did
// Azure actually block on" diagnostic.
//
// OWN-STAMP NAMES (T186 L23): checkNameAvailability reports a name as taken
// even when the taker is this stamp — every re-run of H2a after an earlier
// deploy. An unavailable name is therefore read at its expected resource ID
// in the stamp's resource group; if it exists there, it is ours and the
// (incremental, idempotent) redeploy proceeds. A 404 there means someone else
// holds the name — the genuine collision this probe exists to catch.
//
// CREDENTIAL REUSE (CLAUDE.md §11): reuses the shared platform ArmClient
// singleton registered by HandlersModule (task 120) — no second credential
// chain, no second SDK-client DI registration.
// -----------------------------------------------------------------------------

using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.ServiceBus;
using Azure.ResourceManager.ServiceBus.Models;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Real ARM-backed <see cref="IResourceNameAvailabilityProbe"/> — dispatches
/// per <see cref="ResourceNameKind"/> to the matching Azure.ResourceManager.*
/// SDK extension method.
/// </summary>
public sealed class ArmResourceNameAvailabilityProbe : IResourceNameAvailabilityProbe
{
    private readonly ArmClient _armClient;
    private readonly ILogger<ArmResourceNameAvailabilityProbe> _logger;

    /// <summary>Constructs the probe. Production DI reuses the shared platform ArmClient singleton.</summary>
    public ArmResourceNameAvailabilityProbe(
        ArmClient armClient,
        ILogger<ArmResourceNameAvailabilityProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ResourceNameAvailabilityResult> CheckAvailabilityAsync(
        ResourceNameAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ResourceGroupName);

        if (request.Names.Count == 0)
        {
            return new ResourceNameAvailabilityResult.AllAvailable();
        }

        var subscription = _armClient.GetSubscriptionResource(
            SubscriptionResource.CreateResourceIdentifier(request.SubscriptionId));

        foreach (var entry in request.Names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var conflict = await CheckOneAsync(subscription, entry, cancellationToken).ConfigureAwait(false);
                if (conflict is not null
                    && await ExistsInStampResourceGroupAsync(request, entry, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogInformation(
                        "H2a resource-name check: kind={Kind} name={Name} already belongs to this stamp's resource group {ResourceGroup} — not a collision",
                        entry.Kind, entry.RequestedName, request.ResourceGroupName);
                    continue;
                }
                if (conflict is not null)
                {
                    _logger.LogWarning(
                        "H2a resource-name check FAILED: kind={Kind} name={Name} reason={Reason}",
                        entry.Kind, entry.RequestedName, conflict);
                    return new ResourceNameAvailabilityResult.Conflict(entry.Kind, entry.RequestedName, conflict);
                }
            }
            catch (RequestFailedException ex)
            {
                // ARM 4xx while checking — surface as conflict so H2a fails
                // with a specific diagnostic rather than exception-throw-through.
                _logger.LogWarning(ex,
                    "H2a resource-name check ARM error: kind={Kind} name={Name} status={Status} errorCode={Code}",
                    entry.Kind, entry.RequestedName, ex.Status, ex.ErrorCode);
                return new ResourceNameAvailabilityResult.Conflict(
                    entry.Kind, entry.RequestedName,
                    $"ArmError-{ex.Status}-{ex.ErrorCode ?? "unknown"}: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "H2a resource-name check ALL AVAILABLE: subscriptionId={SubscriptionId} checked={Count}",
            request.SubscriptionId, request.Names.Count);
        return new ResourceNameAvailabilityResult.AllAvailable();
    }

    /// <summary>
    /// True when the named resource exists at its expected ID in the stamp's resource group (ARM GET 200);
    /// false on 404. Any other ARM error propagates to the caller's catch, which reports it as a conflict.
    /// </summary>
    private async Task<bool> ExistsInStampResourceGroupAsync(
        ResourceNameAvailabilityRequest request,
        ResourceNameCheckEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (entry.Kind)
            {
                case ResourceNameKind.StorageAccount:
                    await _armClient.GetStorageAccountResource(StorageAccountResource.CreateResourceIdentifier(
                            request.SubscriptionId, request.ResourceGroupName, entry.RequestedName))
                        .GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    return true;

                case ResourceNameKind.ServiceBusNamespace:
                    await _armClient.GetServiceBusNamespaceResource(ServiceBusNamespaceResource.CreateResourceIdentifier(
                            request.SubscriptionId, request.ResourceGroupName, entry.RequestedName))
                        .GetAsync(cancellationToken).ConfigureAwait(false);
                    return true;

                default:
                    return false;
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns <c>null</c> when the requested name is available; else returns
    /// the Azure-reported reason string. Kinds not covered by the production
    /// impl (currently only <see cref="ResourceNameKind.KeyVault"/>) return
    /// null (unknown-kind is treated as "available" so no false-positive
    /// blocks the deploy; H2a's precompute currently omits KV entries per
    /// the enum comment).
    /// </summary>
    private static async Task<string?> CheckOneAsync(
        SubscriptionResource subscription,
        ResourceNameCheckEntry entry,
        CancellationToken cancellationToken)
    {
        switch (entry.Kind)
        {
            case ResourceNameKind.StorageAccount:
                {
                    var content = new StorageAccountNameAvailabilityContent(entry.RequestedName);
                    var response = await subscription.CheckStorageAccountNameAvailabilityAsync(content, cancellationToken)
                        .ConfigureAwait(false);
                    var value = response.Value;
                    if (value.IsNameAvailable == true) return null;
                    var reason = value.Reason?.ToString() ?? "Unknown";
                    var message = value.Message ?? "(no message)";
                    return $"{reason}: {message}";
                }

            case ResourceNameKind.ServiceBusNamespace:
                {
                    var content = new ServiceBusNameAvailabilityContent(entry.RequestedName);
                    var response = await subscription.CheckServiceBusNamespaceNameAvailabilityAsync(content, cancellationToken)
                        .ConfigureAwait(false);
                    var value = response.Value;
                    if (value.IsNameAvailable == true) return null;
                    var reason = value.Reason?.ToString() ?? "Unknown";
                    var message = value.Message ?? "(no message)";
                    return $"{reason}: {message}";
                }

            case ResourceNameKind.KeyVault:
                // Wave-2 not covered — see IResourceNameAvailabilityProbe.cs enum comment.
                return null;

            default:
                return null;
        }
    }
}
