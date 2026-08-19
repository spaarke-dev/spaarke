// -----------------------------------------------------------------------------
// IServiceEndpointWebhookRegistrar.cs
//
// Seam abstraction over Dataverse `serviceendpoint` webhook registration.
// Owns idempotent upsert semantics: find-by-name, PATCH if exists (url +
// signing material may have changed), else POST create.
//
// SEAM JUSTIFICATION (ADR-010): ≥2 implementations exist from day 1 —
// production DataverseWebApiServiceEndpointWebhookRegistrar (raw HttpClient +
// DefaultAzureCredential, same posture as DataverseWebApiAppUserCreator) +
// per-unit-test fakes.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Registers (or updates) a Dataverse <c>serviceendpoint</c> webhook record.
/// Idempotent by construction (find-by-name-then-create-or-patch).
/// </summary>
public interface IServiceEndpointWebhookRegistrar
{
    /// <summary>
    /// Ensures a <c>serviceendpoint</c> record named
    /// <paramref name="request"/>'s <see cref="ServiceEndpointWebhookRequest.Name"/>
    /// exists on the target Dataverse environment, pointing at
    /// <see cref="ServiceEndpointWebhookRequest.Url"/> with the given HMAC
    /// signing material.
    /// </summary>
    Task<ServiceEndpointWebhookOutcome> RegisterAsync(
        ServiceEndpointWebhookRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// One service-endpoint webhook registration invocation input.
/// </summary>
/// <param name="EnvironmentUrl">Target Dataverse environment base URL.</param>
/// <param name="TenantId">Explicit Entra tenant id (§4D I1 + I5).</param>
/// <param name="Name">Unique service-endpoint display name (find-by-name key).</param>
/// <param name="Url">HTTPS receiver endpoint Dataverse posts plugin-step webhook payloads to.</param>
/// <param name="SigningKey">HMAC signing material the receiver verifies (H4-provisioned <c>Communication-Webhook-SigningKey</c>).</param>
/// <param name="Contract">Dataverse <c>serviceendpoint.contract</c> option-set value (Options-configurable — see IntegrationWiringOptions.ServiceEndpointContractValue).</param>
/// <param name="MessageFormat">Dataverse <c>serviceendpoint.messageformat</c> option-set value.</param>
/// <param name="AuthType">Dataverse <c>serviceendpoint.authtype</c> option-set value.</param>
public sealed record ServiceEndpointWebhookRequest(
    string EnvironmentUrl,
    string TenantId,
    string Name,
    string Url,
    string SigningKey,
    int Contract,
    int MessageFormat,
    int AuthType);

/// <summary>
/// Discriminated outcome of <see cref="IServiceEndpointWebhookRegistrar.RegisterAsync"/>.
/// Exhaustive: <see cref="Created"/> | <see cref="Updated"/> | <see cref="Failure"/>.
/// </summary>
public abstract record ServiceEndpointWebhookOutcome
{
    private ServiceEndpointWebhookOutcome() { }

    /// <summary>A new serviceendpoint record was created.</summary>
    public sealed record Created(string ServiceEndpointId) : ServiceEndpointWebhookOutcome;

    /// <summary>An existing serviceendpoint record was updated (url/signing key changed).</summary>
    public sealed record Updated(string ServiceEndpointId) : ServiceEndpointWebhookOutcome;

    /// <summary>The create/update call failed.</summary>
    public sealed record Failure(string Diagnostic) : ServiceEndpointWebhookOutcome;
}
