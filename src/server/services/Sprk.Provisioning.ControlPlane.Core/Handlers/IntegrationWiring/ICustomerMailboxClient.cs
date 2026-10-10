// -----------------------------------------------------------------------------
// ICustomerMailboxClient.cs
//
// Task 263 (owner decision 2026-10-10, #1562): seam over the Exchange sidecar's customer-mailbox routes. Under Model 1
// each customer gets ONE Spaarke-tenant shared mailbox, sprk-{customerId}-mail, a DIRECT member of the customer's scope
// group (Spaarke-AppAccess-{customerId}) so H14a's group-scoped "Application Mail.*" roles reach it.
//
//   EnsureAsync (H14m)  POST /ensure-customer-mailbox — get-before-set: inspect, create + join only when nothing
//                       exists, read back, then Test-ServicePrincipalAuthorization for every role.
//   ReadAsync   (H13)   POST /read-customer-mailbox  — read-only; never creates, changes or removes anything.
//
// The sidecar holds the single classifier (Get-CustomerMailboxFacts): every reason a mailbox is not this customer's,
// or sits outside the scope group, comes back as a conflict line — the Worker never re-derives it.
//
// SEAM JUSTIFICATION (ADR-010; CLAUDE.md §11): Existing — IExchangePolicyApplier / IExchangePolicyReadClient cover the
// stamp identity's ROLE assignments, not a mailbox; Extension — the production implementation IS the existing
// ExchangePolicySidecarClient (same transport, shared secret and Exchange token), only the contract is new;
// Cost of doing nothing — no step creates the stamp's mailbox, so mail stays a hand step (#1562).
// Design: projects/customer-provisioning-orchestration-r1/notes/t263-customer-shared-mailbox.md.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Core.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>Ensures (H14m) and reads (H13) the customer's Spaarke-tenant shared mailbox through the Exchange sidecar.</summary>
public interface ICustomerMailboxClient
{
    /// <summary>
    /// Get-before-set ensure. MUST NOT change or adopt a foreign mailbox — returns
    /// <see cref="CustomerMailboxEnsureOutcome.Drift"/> instead. MUST NOT throw for transport, sign-in or remote errors.
    /// </summary>
    Task<CustomerMailboxEnsureOutcome> EnsureAsync(CustomerMailboxRequest request, CancellationToken cancellationToken);

    /// <summary>Read-only. MUST NOT throw for transport, sign-in or remote errors — returns Failure.</summary>
    Task<CustomerMailboxReadOutcome> ReadAsync(CustomerMailboxRequest request, CancellationToken cancellationToken);
}

/// <summary>One ensure or read.</summary>
/// <param name="TenantId">Explicit Entra tenant id (§4D I1).</param>
/// <param name="AppId">The stamp's managed identity (client id) whose group-scoped roles must reach the mailbox.</param>
/// <param name="ScopeGroupId">Intake <c>exchangePolicyScopeGroupId</c>.</param>
/// <param name="ExpectedScopeGroupName"><c>Spaarke-AppAccess-{customerId}</c> — the group must carry this name.</param>
/// <param name="Name">Exchange Name and Alias: <c>sprk-{customerId}-mail</c>.</param>
/// <param name="DisplayName">Intake <c>displayName</c>.</param>
/// <param name="PrimarySmtpAddress">Intake <c>communicationDefaultMailbox</c> (one producer — H4 writes the same value to KV).</param>
/// <param name="Roles">The Exchange application roles that must be in scope (H14a's set).</param>
/// <param name="CorrelationId">ProvisioningRun id.</param>
public sealed record CustomerMailboxRequest(
    string TenantId,
    string AppId,
    string ScopeGroupId,
    string ExpectedScopeGroupName,
    string Name,
    string DisplayName,
    string PrimarySmtpAddress,
    IReadOnlyList<string> Roles,
    string CorrelationId);

/// <summary>Exchange's answer for one role (Test-ServicePrincipalAuthorization).</summary>
public sealed record CustomerMailboxAuthorization(string Role, bool InScope);

/// <summary>Outcome of <see cref="ICustomerMailboxClient.EnsureAsync"/>: Ensured | Drift | Failure.</summary>
public abstract record CustomerMailboxEnsureOutcome
{
    private CustomerMailboxEnsureOutcome() { }

    /// <summary>The mailbox exists, is shared and in the scope group only.</summary>
    /// <param name="Created">Created by this call (false = already in place, nothing written).</param>
    /// <param name="Verified">Every role reported InScope.</param>
    public sealed record Ensured(bool Created, bool Verified, IReadOnlyList<CustomerMailboxAuthorization> Authorization, string Diagnostic)
        : CustomerMailboxEnsureOutcome;

    /// <summary>Something foreign or out of scope exists; nothing was created or changed.</summary>
    public sealed record Drift(IReadOnlyList<string> Conflicts) : CustomerMailboxEnsureOutcome;

    /// <summary>No conclusive result (sign-in, sidecar, Exchange, permission or transport failure).</summary>
    public sealed record Failure(string Diagnostic) : CustomerMailboxEnsureOutcome;
}

/// <summary>Outcome of <see cref="ICustomerMailboxClient.ReadAsync"/>.</summary>
public abstract record CustomerMailboxReadOutcome
{
    private CustomerMailboxReadOutcome() { }

    /// <summary>The read reached Exchange. Conflicts empty + Exists + every role in scope = the mailbox is right.</summary>
    public sealed record Success(bool Exists, IReadOnlyList<string> Conflicts, IReadOnlyList<CustomerMailboxAuthorization> Authorization, string Diagnostic)
        : CustomerMailboxReadOutcome;

    /// <summary>No conclusive answer; the caller classifies Resumable.</summary>
    public sealed record Failure(string Diagnostic) : CustomerMailboxReadOutcome;
}

/// <summary>The customer mailbox's derived names — one derivation for H14m and H13.</summary>
public static class CustomerMailboxNaming
{
    /// <summary>Exchange Name and Alias of the customer's shared mailbox: <c>sprk-{customerId}-mail</c>.</summary>
    public static string MailboxName(string customerId) => $"sprk-{Checked(customerId)}-mail";

    /// <summary>The name the customer's scope group must carry (PRQ-C-08): <c>Spaarke-AppAccess-{customerId}</c>.</summary>
    public static string ScopeGroupName(string customerId) => $"Spaarke-AppAccess-{Checked(customerId)}";

    private static string Checked(string customerId)
    {
        if (!CustomerIdStandard.IsValid(customerId))
        {
            throw new ArgumentException($"customerId '{customerId}' does not match {CustomerIdStandard.Pattern}.", nameof(customerId));
        }
        return customerId;
    }
}
