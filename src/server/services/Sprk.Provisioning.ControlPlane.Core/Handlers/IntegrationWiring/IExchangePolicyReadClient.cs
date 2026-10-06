// -----------------------------------------------------------------------------
// IExchangePolicyReadClient.cs
//
// READ-ONLY seam over the sidecar's POST /read-mailbox-access route (task 251): the Exchange
// application-role assignments one app holds, each marked with whether it is limited to the
// customer's scope group. Consumed by the H13 T4 probe; never mutates anything (H13's "assert
// effects, not intentions" — the verifier must not alter what it verifies).
// Production implementation: ExchangePolicySidecarClient (same transport, shared secret and
// Exchange token as the apply route).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>Read-only view of an app's group-scoped Exchange application roles (H13 T4).</summary>
public interface IExchangePolicyReadClient
{
    /// <summary>
    /// Lists the app's assignments of the given roles. MUST NOT throw for transport, sign-in or
    /// remote errors — returns <see cref="ExchangePolicyReadOutcome.Failure"/> so the caller can classify Resumable.
    /// </summary>
    Task<ExchangePolicyReadOutcome> ReadAsync(ExchangePolicyReadRequest request, CancellationToken cancellationToken);
}

/// <summary>One read.</summary>
/// <param name="TenantId">Customer Entra tenant id (§4D I1).</param>
/// <param name="AppId">The app whose assignments are read — the stamp's managed identity.</param>
/// <param name="ScopeGroupId">The expected scope group (each assignment is marked in/out of it).</param>
/// <param name="Roles">The Exchange application roles to read.</param>
/// <param name="CorrelationId">ProvisioningRun id.</param>
public sealed record ExchangePolicyReadRequest(
    string TenantId,
    string AppId,
    string ScopeGroupId,
    IReadOnlyList<string> Roles,
    string CorrelationId);

/// <summary>One Exchange role assignment as the sidecar reports it.</summary>
public sealed record ExchangeRoleAssignmentView(string Name, string Role, string Scope, bool InExpectedScope);

/// <summary>Discriminated outcome of <see cref="IExchangePolicyReadClient.ReadAsync"/>.</summary>
public abstract record ExchangePolicyReadOutcome
{
    private ExchangePolicyReadOutcome() { }

    /// <summary>The read reached Exchange. An app Exchange has never registered has no assignments.</summary>
    public sealed record Success(bool ServicePrincipalRegistered, IReadOnlyList<ExchangeRoleAssignmentView> Assignments) : ExchangePolicyReadOutcome;

    /// <summary>No conclusive answer; the caller classifies Resumable.</summary>
    public sealed record Failure(string Diagnostic) : ExchangePolicyReadOutcome;
}
