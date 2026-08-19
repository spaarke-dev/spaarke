// -----------------------------------------------------------------------------
// IGraphSubscriptionCreator.cs
//
// Seam abstraction over Microsoft Graph change-notification subscriptions
// (POST/PATCH /v1.0/subscriptions). Owns idempotent create-or-renew semantics:
// list existing subscriptions matching (resource, notificationUrl); PATCH the
// expirationDateTime if one already exists, else POST create — so a repeat
// invocation is a safe renew, never a duplicate subscription.
//
// SEAM JUSTIFICATION (ADR-010): ≥2 implementations exist from day 1 —
// production GraphRestSubscriptionCreator (raw HttpClient + DefaultAzureCredential,
// same posture as GraphRestAppRoleGranter — NFR-09 Path-C rationale: L2 does
// not carry the Microsoft.Graph SDK dependency; see
// DataverseAppUserGraphParity/H10DataverseAppUserGraphParityHandler.cs file
// header for the full NFR-09 pivot-to-comply rationale, which applies
// identically here) + per-unit-test fakes.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Creates or renews a Microsoft Graph change-notification subscription.
/// Idempotent by construction (list-then-create-or-patch).
/// </summary>
public interface IGraphSubscriptionCreator
{
    /// <summary>
    /// Ensures a Graph subscription exists for <paramref name="request"/>'s
    /// resource + notification URL. If a matching subscription already
    /// exists, renews its <c>expirationDateTime</c> (PATCH); otherwise
    /// creates a new one (POST).
    /// </summary>
    Task<GraphSubscriptionOutcome> CreateOrUpdateAsync(
        GraphSubscriptionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// One Graph subscription create-or-renew invocation input.
/// </summary>
/// <param name="TenantId">Explicit Entra tenant id (§4D I1 + I5).</param>
/// <param name="ModuleName">Human-readable module label (e.g. <c>Communication</c>, <c>Email</c>) — logging + diagnostic only.</param>
/// <param name="Resource">Graph resource path to watch (e.g. <c>/communications/callRecords</c>).</param>
/// <param name="ChangeType">Comma-separated Graph change types (e.g. <c>created,updated</c>).</param>
/// <param name="NotificationUrl">HTTPS endpoint Graph POSTs change notifications to.</param>
/// <param name="ClientState">HMAC signing material echoed back on every notification for the receiver to verify (H4-provisioned <c>Communication-Webhook-SigningKey</c>).</param>
/// <param name="ExpirationMinutes">Requested subscription lifetime in minutes from now.</param>
public sealed record GraphSubscriptionRequest(
    string TenantId,
    string ModuleName,
    string Resource,
    string ChangeType,
    string NotificationUrl,
    string ClientState,
    int ExpirationMinutes);

/// <summary>
/// Discriminated outcome of <see cref="IGraphSubscriptionCreator.CreateOrUpdateAsync"/>.
/// Exhaustive: <see cref="Created"/> | <see cref="Renewed"/> | <see cref="Failure"/>.
/// </summary>
public abstract record GraphSubscriptionOutcome
{
    private GraphSubscriptionOutcome() { }

    /// <summary>A new subscription was created.</summary>
    public sealed record Created(string SubscriptionId) : GraphSubscriptionOutcome;

    /// <summary>An existing subscription's expiration was renewed (no new subscription created).</summary>
    public sealed record Renewed(string SubscriptionId) : GraphSubscriptionOutcome;

    /// <summary>The create/renew call failed.</summary>
    public sealed record Failure(string Diagnostic) : GraphSubscriptionOutcome;
}
