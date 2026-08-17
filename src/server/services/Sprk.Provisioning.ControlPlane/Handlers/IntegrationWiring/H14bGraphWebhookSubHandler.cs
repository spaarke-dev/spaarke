// -----------------------------------------------------------------------------
// H14bGraphWebhookSubHandler.cs
//
// L2 CONTROL-PLANE H14b Graph webhook-subscription sub-handler (task 073,
// wave C4 Batch 3F).
//
// PURPOSE:
//   One of H14's 3 DAG-parallel sub-steps (spec.md FR-19). Creates/renews
//   Microsoft Graph change-notification subscriptions for the Communication
//   + Email modules, using the HMAC signing key H4 (task 047) provisioned to
//   KV under the canonical name `Communication-Webhook-SigningKey` as the
//   subscription's `clientState`.
//
// TARGET RESOURCE SCOPE (interim, wave C4 — Path C documented decision):
//   The exact Graph resource path per module (e.g. which mailbox / which
//   communications scope) is CUSTOMER-tenant-specific configuration, not a
//   Spaarke-wide constant (unlike H2b's 7 canonical AI Search index names,
//   which ARE identical across every customer). So H14b takes the
//   `communicationGraphResource` / `emailGraphResource` run parameters
//   (NonSecret) — either or both may be supplied; at least one is required.
//   A future task can promote these into a Dataverse-configured catalog once
//   the Communication/Email module's exact subscription targets are
//   finalized; the seam (IGraphSubscriptionCreator) + idempotent
//   create-or-renew mechanics do not change when that lands.
//
// PARENT-OWNS-COSMOS DESIGN: see H14aExchangePolicySubHandler.cs's file
// header for the full rationale — identical here. This sub-handler touches
// ZERO Cosmos state; H14 (parent) owns the single read + single write.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-19 (H14
//     acceptance: "Graph webhook subscriptions per Communication/Email module
//     with HMAC signing keys from H4").
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H14 row.
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Provisioning.ControlPlane.Enqueue;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H14bGraphWebhookSubHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = "H14b";

    /// <summary>Sub-step token used in the idempotency key format h14-{customerId}-{subStep}-{hash}.</summary>
    public const string SubStep = "graph";

    /// <summary>Canonical KV secret name H4 provisions (task 047, StaticKvSecretManifest) that H14b reads back as the Graph subscription clientState.</summary>
    public const string SigningKeySecretName = "Communication-Webhook-SigningKey";

    private readonly IKvSecretReader _secretReader;
    private readonly IGraphSubscriptionCreator _subscriptionCreator;
    private readonly ILogger<H14bGraphWebhookSubHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H14bGraphWebhookSubHandler(
        IKvSecretReader secretReader,
        IGraphSubscriptionCreator subscriptionCreator,
        ILogger<H14bGraphWebhookSubHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(secretReader);
        ArgumentNullException.ThrowIfNull(subscriptionCreator);
        ArgumentNullException.ThrowIfNull(logger);
        _secretReader = secretReader;
        _subscriptionCreator = subscriptionCreator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);

        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"H14bGraphWebhookSubHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        Parameters parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<Parameters>(envelope.ParametersJson)
                ?? throw new JsonException("Deserialized to null.");
        }
        catch (JsonException ex)
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14bRejections.SubscriptionCreateFailed,
                $"H14b ParametersJson deserialization failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(parameters.TenantId)
            || string.IsNullOrWhiteSpace(parameters.KeyVaultName)
            || string.IsNullOrWhiteSpace(parameters.SubscriptionId)
            || string.IsNullOrWhiteSpace(parameters.NotificationBaseUrl))
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14bRejections.SubscriptionCreateFailed,
                "H14b ParametersJson missing one of tenantId/keyVaultName/subscriptionId/notificationBaseUrl.");
        }

        var targets = new List<(string ModuleName, string Resource)>();
        if (!string.IsNullOrWhiteSpace(parameters.CommunicationResource))
        {
            targets.Add(("Communication", parameters.CommunicationResource));
        }
        if (!string.IsNullOrWhiteSpace(parameters.EmailResource))
        {
            targets.Add(("Email", parameters.EmailResource));
        }

        if (targets.Count == 0)
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14bRejections.NoWebhookTargetsConfigured,
                "Neither run parameter 'communicationGraphResource' nor 'emailGraphResource' was supplied — " +
                "H14b has no Graph webhook target to subscribe to. Operator must configure at least one.");
        }

        var signingKeyResult = await _secretReader.ReadSecretAsync(
            parameters.KeyVaultName, parameters.SubscriptionId, SigningKeySecretName, cancellationToken)
            .ConfigureAwait(false);

        string signingKey;
        switch (signingKeyResult)
        {
            case KvSecretReadResult.Success success:
                signingKey = success.Value;
                break;
            case KvSecretReadResult.NotFound:
                return new HandlerResult.Failure(
                    FailureClass.Resumable, H14bRejections.MissingSigningKey,
                    $"KV secret '{SigningKeySecretName}' not found on vault '{parameters.KeyVaultName}' — " +
                    "H4 (task 047) must populate it before H14b can wire Graph subscriptions.");
            case KvSecretReadResult.Failure kvFailure:
                return new HandlerResult.Failure(
                    FailureClass.Resumable, H14bRejections.SigningKeyReadFailed,
                    $"KV signing-key read failed: {kvFailure.Diagnostic}");
            default:
                throw new InvalidOperationException($"Unhandled {nameof(KvSecretReadResult)} type '{signingKeyResult.GetType().Name}'.");
        }

        var failures = new List<string>();
        var createdOrRenewed = new List<string>();
        foreach (var (moduleName, resource) in targets)
        {
            var outcome = await _subscriptionCreator.CreateOrUpdateAsync(
                new GraphSubscriptionRequest(
                    parameters.TenantId, moduleName, resource, ChangeType: "created,updated",
                    NotificationUrl: BuildNotificationUrl(parameters.NotificationBaseUrl, moduleName),
                    ClientState: signingKey, ExpirationMinutes: parameters.ExpirationMinutes),
                cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case GraphSubscriptionOutcome.Created created:
                    createdOrRenewed.Add($"{moduleName}:created:{created.SubscriptionId}");
                    break;
                case GraphSubscriptionOutcome.Renewed renewed:
                    createdOrRenewed.Add($"{moduleName}:renewed:{renewed.SubscriptionId}");
                    break;
                case GraphSubscriptionOutcome.Failure failure:
                    failures.Add($"{moduleName}: {failure.Diagnostic}");
                    break;
            }
        }

        if (failures.Count > 0)
        {
            return new HandlerResult.Failure(
                FailureClass.RetryableWithCleanup, H14bRejections.SubscriptionCreateFailed,
                $"{failures.Count} of {targets.Count} Graph webhook subscription(s) failed: {string.Join("; ", failures)}. " +
                "Subscription create-or-renew is individually idempotent — a full re-run safely completes the partial state.");
        }

        _logger.LogInformation(
            "H14b Graph webhook subscriptions wired: customerId={CustomerId} targets={Targets}",
            envelope.CustomerId, string.Join(",", createdOrRenewed));

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, targets.Select(t => t.Resource).ToList(), parameters.NotificationBaseUrl);
        return new HandlerResult.Success(idempotencyKey);
    }

    private static string BuildNotificationUrl(string baseUrl, string moduleName)
        => $"{baseUrl.TrimEnd('/')}/api/webhooks/graph/{moduleName.ToLowerInvariant()}";

    /// <summary>
    /// Computes the deterministic H14b idempotency key:
    /// <c>h14-{customerId}-graph-{hash}</c> where hash is SHA-256 over the
    /// sorted resource list + notification base URL. Exposed internal so the
    /// parent handler + unit tests can construct the expected key.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, IReadOnlyList<string> resources, string notificationBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationBaseUrl);
        var payload = string.Join("|", resources.OrderBy(r => r, StringComparer.Ordinal)) + "|" + notificationBaseUrl;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"h14-{customerId}-{SubStep}-{hash}";
    }

    /// <summary>
    /// Builds the opaque ParametersJson payload H14 (parent) embeds in the
    /// sub-envelope it hands to this sub-handler.
    /// </summary>
    internal static string BuildParametersJson(
        string tenantId, string keyVaultName, string subscriptionId, string notificationBaseUrl,
        string? communicationResource, string? emailResource, int expirationMinutes)
        => JsonSerializer.Serialize(new Parameters(
            tenantId, keyVaultName, subscriptionId, notificationBaseUrl,
            communicationResource, emailResource, expirationMinutes));

    /// <summary>H14b's typed ParametersJson shape. Public for STJ reflection resolution parity with <see cref="H14aExchangePolicySubHandler.Parameters"/>.</summary>
    public sealed record Parameters(
        [property: JsonPropertyName("tenantId")] string TenantId,
        [property: JsonPropertyName("keyVaultName")] string KeyVaultName,
        [property: JsonPropertyName("subscriptionId")] string SubscriptionId,
        [property: JsonPropertyName("notificationBaseUrl")] string NotificationBaseUrl,
        [property: JsonPropertyName("communicationResource")] string? CommunicationResource,
        [property: JsonPropertyName("emailResource")] string? EmailResource,
        [property: JsonPropertyName("expirationMinutes")] int ExpirationMinutes);
}
