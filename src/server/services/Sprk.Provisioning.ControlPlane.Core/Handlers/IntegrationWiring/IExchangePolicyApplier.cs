// -----------------------------------------------------------------------------
// IExchangePolicyApplier.cs
//
// Seam over H14a's Exchange step (spec.md T4). Since task 251 (owner D26) the step is Exchange
// "RBAC for Applications": grant ONE app — the customer stamp's managed identity — the Exchange
// "Application Mail.*" roles, scoped to the customer's mail-enabled security group, so the
// stamp's Graph mail calls reach that group's mailboxes and no others. It replaces
// ApplicationAccessPolicy, which Microsoft now calls legacy (and caps at a few hundred policies
// per tenant — every Model 1 stamp lives in Spaarke's tenant).
//
// The FULL T4 dance is one call: inspect every expected assignment; if anything differs (a named
// assignment with another role / app / scope, or the app holding one of these roles under another
// name) return Drift WITHOUT creating anything; otherwise create the missing ones and read back.
//
// SEAM JUSTIFICATION (ADR-010): production ExchangePolicySidecarClient (HTTP to the sidecar, which
// owns the Exchange PowerShell module) + per-test fakes.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Grants + verifies the group-scoped Exchange application roles for a customer's managed identity
/// (RBAC for Applications). Encapsulates the T4 action-and-verify semantics (spec.md FR-33 T4).
/// </summary>
public interface IExchangePolicyApplier
{
    /// <summary>
    /// Inspects, then creates any missing assignment, then reads back. MUST NOT change an existing
    /// assignment that differs from the request — returns <see cref="ExchangePolicyApplyOutcome.Drift"/> instead.
    /// </summary>
    Task<ExchangePolicyApplyOutcome> ApplyAsync(ExchangePolicyApplyRequest request, CancellationToken cancellationToken);
}

/// <summary>One Exchange role assignment H14a expects: its deterministic name and the application role.</summary>
/// <param name="Name">Assignment name (≤ 64 chars) — H14a's idempotency key in Exchange.</param>
/// <param name="Role">Exchange application role, e.g. <c>Application Mail.Send</c>.</param>
public sealed record ExchangeRoleAssignmentSpec(string Name, string Role);

/// <summary>One H14a apply.</summary>
/// <param name="TenantId">Explicit Entra tenant id (§4D I1 — never an ambient default tenant).</param>
/// <param name="AppId">Client id of the app being granted access — the stamp's managed identity.</param>
/// <param name="ServicePrincipalObjectId">That app's Entra service-principal object id (New-ServicePrincipal -ObjectId).</param>
/// <param name="DisplayName">Name the sidecar gives the Exchange service principal when it registers it.</param>
/// <param name="ScopeGroupId">Entra object id of the customer's mail-enabled security group (operator intake).</param>
/// <param name="Assignments">The expected assignments (one per application role).</param>
/// <param name="CorrelationId">ProvisioningRun id — logged by the sidecar so its lines interleave with the Worker's.</param>
public sealed record ExchangePolicyApplyRequest(
    string TenantId,
    string AppId,
    string ServicePrincipalObjectId,
    string DisplayName,
    string ScopeGroupId,
    IReadOnlyList<ExchangeRoleAssignmentSpec> Assignments,
    string CorrelationId);

/// <summary>Discriminated outcome of <see cref="IExchangePolicyApplier.ApplyAsync"/>: Applied | Drift | Failure.</summary>
public abstract record ExchangePolicyApplyOutcome
{
    private ExchangePolicyApplyOutcome() { }

    /// <summary>Every expected assignment is present and limited to the scope group.</summary>
    /// <param name="CreatedCount">Assignments created by this call (0 = already compliant).</param>
    /// <param name="AssignmentNames">The assignments read back.</param>
    public sealed record Applied(int CreatedCount, IReadOnlyList<string> AssignmentNames) : ExchangePolicyApplyOutcome;

    /// <summary>
    /// T4 SILENT-FAIL TRAP: existing assignments differ from the expected set. Nothing was created or
    /// changed; the caller classifies this as Quarantine-required.
    /// </summary>
    public sealed record Drift(IReadOnlyList<string> Conflicts) : ExchangePolicyApplyOutcome;

    /// <summary>No conclusive result (sign-in, sidecar, Exchange or transport failure).</summary>
    public sealed record Failure(string Diagnostic) : ExchangePolicyApplyOutcome;
}
