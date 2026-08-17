// -----------------------------------------------------------------------------
// H14cDataverseWebhookSubHandler.cs
//
// L2 CONTROL-PLANE H14c Dataverse service-endpoint webhook sub-handler (task
// 073, wave C4 Batch 3F).
//
// PURPOSE:
//   One of H14's 3 DAG-parallel sub-steps (spec.md FR-19). Registers a
//   Dataverse `serviceendpoint` webhook record pointing at the customer's
//   BFF webhook receiver, using the SAME H4-provisioned HMAC signing key
//   (`Communication-Webhook-SigningKey`) H14b consumes for Graph subscriptions
//   — one canonical signing secret shared across both webhook consumers per
//   the StaticKvSecretManifest comment ("FR-19 (H14) — Communication module
//   webhook signing key").
//
// PARENT-OWNS-COSMOS DESIGN: see H14aExchangePolicySubHandler.cs's file
// header for the full rationale — identical here. This sub-handler touches
// ZERO Cosmos state; H14 (parent) owns the single read + single write.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-19 (H14
//     acceptance: "Dataverse service-endpoint webhooks... fire with correct
//     HMAC").
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H14 row.
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H14cDataverseWebhookSubHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = "H14c";

    /// <summary>Sub-step token used in the idempotency key format h14-{customerId}-{subStep}-{hash}.</summary>
    public const string SubStep = "dataverse";

    /// <summary>Canonical serviceendpoint display name registered by H14c (find-by-name upsert key).</summary>
    public const string ServiceEndpointName = "Spaarke-Communication-Webhook";

    /// <summary>Canonical KV secret name H4 provisions (task 047) — same signing key H14b consumes.</summary>
    public const string SigningKeySecretName = H14bGraphWebhookSubHandler.SigningKeySecretName;

    private readonly IKvSecretReader _secretReader;
    private readonly IServiceEndpointWebhookRegistrar _registrar;
    private readonly IntegrationWiringOptions _options;
    private readonly ILogger<H14cDataverseWebhookSubHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H14cDataverseWebhookSubHandler(
        IKvSecretReader secretReader,
        IServiceEndpointWebhookRegistrar registrar,
        IOptions<IntegrationWiringOptions> options,
        ILogger<H14cDataverseWebhookSubHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(secretReader);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _secretReader = secretReader;
        _registrar = registrar;
        _options = options.Value;
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
                $"H14cDataverseWebhookSubHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
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
                FailureClass.Resumable, H14cRejections.RegistrationFailed,
                $"H14c ParametersJson deserialization failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(parameters.TenantId)
            || string.IsNullOrWhiteSpace(parameters.DataverseEnvUrl)
            || string.IsNullOrWhiteSpace(parameters.KeyVaultName)
            || string.IsNullOrWhiteSpace(parameters.SubscriptionId)
            || string.IsNullOrWhiteSpace(parameters.WebhookUrl))
        {
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14cRejections.RegistrationFailed,
                "H14c ParametersJson missing one of tenantId/dataverseEnvUrl/keyVaultName/subscriptionId/webhookUrl.");
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
                    FailureClass.Resumable, H14cRejections.MissingSigningKey,
                    $"KV secret '{SigningKeySecretName}' not found on vault '{parameters.KeyVaultName}' — " +
                    "H4 (task 047) must populate it before H14c can register the Dataverse webhook.");
            case KvSecretReadResult.Failure kvFailure:
                return new HandlerResult.Failure(
                    FailureClass.Resumable, H14cRejections.SigningKeyReadFailed,
                    $"KV signing-key read failed: {kvFailure.Diagnostic}");
            default:
                throw new InvalidOperationException($"Unhandled {nameof(KvSecretReadResult)} type '{signingKeyResult.GetType().Name}'.");
        }

        var outcome = await _registrar.RegisterAsync(
            new ServiceEndpointWebhookRequest(
                parameters.DataverseEnvUrl, parameters.TenantId, ServiceEndpointName, parameters.WebhookUrl, signingKey,
                _options.ServiceEndpointContractValue, _options.ServiceEndpointMessageFormatValue, _options.ServiceEndpointAuthTypeValue),
            cancellationToken).ConfigureAwait(false);

        switch (outcome)
        {
            case ServiceEndpointWebhookOutcome.Created created:
                _logger.LogInformation(
                    "H14c Dataverse service-endpoint webhook created: customerId={CustomerId} serviceEndpointId={ServiceEndpointId}",
                    envelope.CustomerId, created.ServiceEndpointId);
                break;
            case ServiceEndpointWebhookOutcome.Updated updated:
                _logger.LogInformation(
                    "H14c Dataverse service-endpoint webhook updated: customerId={CustomerId} serviceEndpointId={ServiceEndpointId}",
                    envelope.CustomerId, updated.ServiceEndpointId);
                break;
            case ServiceEndpointWebhookOutcome.Failure failure:
                return new HandlerResult.Failure(
                    FailureClass.RetryableWithCleanup, H14cRejections.RegistrationFailed,
                    $"Dataverse serviceendpoint registration failed: {failure.Diagnostic}. Registration is an " +
                    "idempotent upsert (find-by-name-then-create-or-patch) — a full re-run safely completes.");
            default:
                throw new InvalidOperationException($"Unhandled {nameof(ServiceEndpointWebhookOutcome)} type.");
        }

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, parameters.DataverseEnvUrl, parameters.WebhookUrl);
        return new HandlerResult.Success(idempotencyKey);
    }

    /// <summary>
    /// Computes the deterministic H14c idempotency key:
    /// <c>h14-{customerId}-dataverse-{hash}</c> where hash is SHA-256 over
    /// (dataverseEnvUrl, webhookUrl). Exposed internal so the parent handler
    /// + unit tests can construct the expected key.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string dataverseEnvUrl, string webhookUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataverseEnvUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookUrl);
        var payload = $"{dataverseEnvUrl}|{webhookUrl}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"h14-{customerId}-{SubStep}-{hash}";
    }

    /// <summary>
    /// Builds the opaque ParametersJson payload H14 (parent) embeds in the
    /// sub-envelope it hands to this sub-handler.
    /// </summary>
    internal static string BuildParametersJson(
        string tenantId, string dataverseEnvUrl, string keyVaultName, string subscriptionId, string webhookUrl)
        => JsonSerializer.Serialize(new Parameters(tenantId, dataverseEnvUrl, keyVaultName, subscriptionId, webhookUrl));

    /// <summary>H14c's typed ParametersJson shape. Public for STJ reflection resolution parity with sibling sub-handlers.</summary>
    public sealed record Parameters(
        [property: JsonPropertyName("tenantId")] string TenantId,
        [property: JsonPropertyName("dataverseEnvUrl")] string DataverseEnvUrl,
        [property: JsonPropertyName("keyVaultName")] string KeyVaultName,
        [property: JsonPropertyName("subscriptionId")] string SubscriptionId,
        [property: JsonPropertyName("webhookUrl")] string WebhookUrl);
}
