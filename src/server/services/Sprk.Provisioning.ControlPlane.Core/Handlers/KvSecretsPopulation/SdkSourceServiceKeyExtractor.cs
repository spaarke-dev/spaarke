// -----------------------------------------------------------------------------
// SdkSourceServiceKeyExtractor.cs
//
// Task 200 — production <see cref="ISourceServiceKeyExtractor"/>. One branch
// per <see cref="SourceServiceType"/> enum member. Each branch:
//   1. Composes the ARM resource ID from subscription + rg + resource name.
//   2. Calls Azure.ResourceManager.* .GetKeysAsync() (or authorization-rule
//      shape for Service Bus).
//   3. Returns the cleartext value (or a composed connection string for
//      Storage / Redis where multiple ARM outputs must be combined).
//
// SDK RECIPES (validated live SESSION 2, codified here):
//
//   Search                → SearchServiceResource.GetAdminKeysAsync()
//                            .Value.PrimaryKey
//   CognitiveServices     → CognitiveServicesAccountResource.GetKeysAsync()
//                            .Value.Key1
//   ServiceBus            → ServiceBusNamespaceResource
//                            .GetServiceBusNamespaceAuthorizationRules()
//                            .Get("RootManageSharedAccessKey")
//                            .Value.GetKeysAsync()
//                            .Value.PrimaryConnectionString
//   Storage               → StorageAccountResource.GetKeysAsync() →
//                            find KeyName=="key1" → compose
//                            "DefaultEndpointsProtocol=https;AccountName={n};"
//                            "AccountKey={k};EndpointSuffix=core.windows.net"
//   Redis                 → RedisResource.Data.HostName + .Data.SslPort +
//                            .GetKeysAsync().Value.PrimaryKey → compose
//                            "{host}:{port},password={key},ssl=True,"
//                            "abortConnect=False"
//
// CLEARTEXT NO-LOG (ADR-028 MUST rule):
//   ZERO Log* calls in this file. Cleartext flows exclusively as return
//   values; no diagnostics ever include the extracted value.
//
// FAILURE PROPAGATION:
//   Azure.RequestFailedException is INTENTIONALLY not caught here — the
//   handler wrapping this seam has the FailureClass context (Quarantine vs
//   Resumable) and needs to see the raw exception to construct the
//   operator-facing rejection message. Only OperationCanceledException is
//   allowed to bubble unchanged (structural cancellation).
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.CognitiveServices;
using Azure.ResourceManager.Redis;
using Azure.ResourceManager.Search;
using Azure.ResourceManager.ServiceBus;
using Azure.ResourceManager.Storage;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Production <see cref="ISourceServiceKeyExtractor"/> — dispatches on
/// <see cref="SharedKvSecretSource.SourceType"/> to the matching
/// Azure.ResourceManager.* SDK recipe.
/// </summary>
public sealed class SdkSourceServiceKeyExtractor : ISourceServiceKeyExtractor
{
    private const string PublicCloudStorageEndpointSuffix = "core.windows.net";
    private const string RootManageSharedAccessKeyRuleName = "RootManageSharedAccessKey";

    private readonly ArmClient _armClient;

    /// <summary>
    /// Constructs the extractor. In production <paramref name="armClient"/>
    /// is a per-extractor-call instance built from the shared UAMI-pinned
    /// <see cref="TokenCredential"/> singleton (parity with the ArmClient
    /// factory-lambda registration pattern used by other H4 seams — see
    /// Sprk.Provisioning.ControlPlane.Worker/Program.cs).
    /// </summary>
    public SdkSourceServiceKeyExtractor(ArmClient armClient)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        _armClient = armClient;
    }

    /// <inheritdoc/>
    public async Task<string> ExtractAsync(
        SharedKvSecretSource source,
        string subscriptionId,
        string resourceGroupName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceGroupName);

        return source.SourceType switch
        {
            SourceServiceType.AiSearchAdminKey =>
                await ExtractSearchAdminKeyAsync(source.ResourceName, subscriptionId, resourceGroupName, cancellationToken)
                    .ConfigureAwait(false),
            SourceServiceType.CognitiveServicesKey1 =>
                await ExtractCognitiveServicesKey1Async(source.ResourceName, subscriptionId, resourceGroupName, cancellationToken)
                    .ConfigureAwait(false),
            SourceServiceType.ServiceBusRootSas =>
                await ExtractServiceBusRootSasAsync(source.ResourceName, subscriptionId, resourceGroupName, cancellationToken)
                    .ConfigureAwait(false),
            SourceServiceType.StorageConnectionString =>
                await ExtractStorageConnectionStringAsync(source.ResourceName, subscriptionId, resourceGroupName, cancellationToken)
                    .ConfigureAwait(false),
            SourceServiceType.RedisComposed =>
                await ExtractRedisConnectionStringAsync(source.ResourceName, subscriptionId, resourceGroupName, cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"SdkSourceServiceKeyExtractor has no branch for SourceServiceType '{source.SourceType}' — " +
                "add a new SDK recipe here + on SourceServiceType together (never manifest-only)."),
        };
    }

    private async Task<string> ExtractSearchAdminKeyAsync(
        string resourceName, string subscriptionId, string resourceGroupName, CancellationToken ct)
    {
        var resourceId = SearchServiceResource.CreateResourceIdentifier(
            subscriptionId, resourceGroupName, resourceName);
        var resource = _armClient.GetSearchServiceResource(resourceId);
        var response = await resource.GetAdminKeyAsync(cancellationToken: ct).ConfigureAwait(false);
        return response.Value.PrimaryKey;
    }

    private async Task<string> ExtractCognitiveServicesKey1Async(
        string resourceName, string subscriptionId, string resourceGroupName, CancellationToken ct)
    {
        var resourceId = CognitiveServicesAccountResource.CreateResourceIdentifier(
            subscriptionId, resourceGroupName, resourceName);
        var resource = _armClient.GetCognitiveServicesAccountResource(resourceId);
        var response = await resource.GetKeysAsync(ct).ConfigureAwait(false);
        return response.Value.Key1;
    }

    private async Task<string> ExtractServiceBusRootSasAsync(
        string resourceName, string subscriptionId, string resourceGroupName, CancellationToken ct)
    {
        var namespaceId = ServiceBusNamespaceResource.CreateResourceIdentifier(
            subscriptionId, resourceGroupName, resourceName);
        var namespaceResource = _armClient.GetServiceBusNamespaceResource(namespaceId);

        var authRules = namespaceResource.GetServiceBusNamespaceAuthorizationRules();
        var ruleResponse = await authRules.GetAsync(RootManageSharedAccessKeyRuleName, ct).ConfigureAwait(false);
        var keysResponse = await ruleResponse.Value.GetKeysAsync(ct).ConfigureAwait(false);
        return keysResponse.Value.PrimaryConnectionString;
    }

    private async Task<string> ExtractStorageConnectionStringAsync(
        string resourceName, string subscriptionId, string resourceGroupName, CancellationToken ct)
    {
        var resourceId = StorageAccountResource.CreateResourceIdentifier(
            subscriptionId, resourceGroupName, resourceName);
        var resource = _armClient.GetStorageAccountResource(resourceId);

        string? key1Value = null;
        await foreach (var accountKey in resource.GetKeysAsync(cancellationToken: ct).ConfigureAwait(false))
        {
            if (string.Equals(accountKey.KeyName, "key1", StringComparison.Ordinal))
            {
                key1Value = accountKey.Value;
                break;
            }
        }

        if (string.IsNullOrEmpty(key1Value))
        {
            throw new InvalidOperationException(
                $"Storage account '{resourceName}' returned no key named 'key1' from " +
                "StorageAccountResource.GetKeysAsync — expected the canonical primary key.");
        }

        return $"DefaultEndpointsProtocol=https;AccountName={resourceName};" +
               $"AccountKey={key1Value};EndpointSuffix={PublicCloudStorageEndpointSuffix}";
    }

    private async Task<string> ExtractRedisConnectionStringAsync(
        string resourceName, string subscriptionId, string resourceGroupName, CancellationToken ct)
    {
        var resourceId = RedisResource.CreateResourceIdentifier(
            subscriptionId, resourceGroupName, resourceName);
        var resource = _armClient.GetRedisResource(resourceId);

        // The typed Data.HostName + Data.SslPort require a GET on the Redis
        // resource (Data properties are lazy-loaded from the ARM response).
        var redisResponse = await resource.GetAsync(ct).ConfigureAwait(false);
        var data = redisResponse.Value.Data;
        var hostName = data.HostName ?? throw new InvalidOperationException(
            $"Redis resource '{resourceName}' returned null HostName from ARM.");
        var sslPort = data.SslPort ?? throw new InvalidOperationException(
            $"Redis resource '{resourceName}' returned null SslPort from ARM.");

        var keysResponse = await resource.GetKeysAsync(ct).ConfigureAwait(false);
        var primaryKey = keysResponse.Value.PrimaryKey;

        return $"{hostName}:{sslPort},password={primaryKey},ssl=True,abortConnect=False";
    }
}
