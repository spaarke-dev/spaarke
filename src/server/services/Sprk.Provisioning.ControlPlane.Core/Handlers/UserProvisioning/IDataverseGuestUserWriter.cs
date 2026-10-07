// -----------------------------------------------------------------------------
// IDataverseGuestUserWriter.cs
//
// Task 232 (D2, G10). Makes a redeemed B2B guest a USER of the customer's
// Dataverse environment holding the configured Spaarke role(s). Before T232
// H11 stopped at the invitation: nothing created the guest's systemuser or gave
// it a role, so a guest could sign in to Entra and still not open the app (and
// DagAdvancer's "its users get the solution's roles" was not true).
//
// Acts as the L2 Worker identity, which the operator makes a System
// Administrator application user of the environment (PRQ-C-09, T228) — the same
// identity H5's WhoAmI proves. No Power Platform admin role is needed: the
// documented path for an app-only caller is the systemuser alternate key, which
// adds a security-group member on demand (research 2026-10-07).
//
// Access is paid pay-as-you-go (owner 2026-10-07, PRQ-C-11): Microsoft's PAYG FAQ
// states guests can use apps in a PAYG environment without licences.
//
// §11 justification — existing: H10's IDataverseAppUserCreator (APPLICATION
// users by applicationid). Extension: a human user is keyed by
// azureactivedirectoryobjectid, takes no application id, and must be in the
// environment's security group first; sharing one writer would branch on every
// step. The role lookup idiom (root business unit, by name) is copied, not
// re-invented. Cost of doing nothing: every Model 1 guest is invited and still
// cannot use the product.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>Ensures a guest is a Dataverse user of the environment holding the given roles. Idempotent.</summary>
public interface IDataverseGuestUserWriter
{
    /// <summary>
    /// Reads the systemuser for <see cref="DataverseGuestUserRequest.EntraObjectId"/> — Dataverse adds a member of the
    /// environment security group on that read (root business unit) — and associates every role in
    /// <see cref="DataverseGuestUserRequest.SecurityRoleNames"/> it does not hold yet. A missing role writes nothing.
    /// </summary>
    Task<DataverseGuestUserOutcome> EnsureGuestUserAsync(DataverseGuestUserRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the environment's <c>restrictguestuseraccess</c> setting (organization table). New environments default to
    /// restricted, which blocks guests from Dataverse entirely — the operator turns it off (PRQ-C-12); H11 checks it
    /// before inviting anyone.
    /// </summary>
    Task<GuestAccessOutcome> ReadGuestAccessAsync(string environmentUrl, string tenantId, CancellationToken cancellationToken);
}

/// <summary>Result of <see cref="IDataverseGuestUserWriter.ReadGuestAccessAsync"/>.</summary>
public abstract record GuestAccessOutcome
{
    private GuestAccessOutcome() { }

    /// <summary>Guests may use the environment (<c>restrictguestuseraccess</c> = false).</summary>
    public sealed record Allowed : GuestAccessOutcome;

    /// <summary>Guests are blocked (<c>restrictguestuseraccess</c> = true).</summary>
    public sealed record Restricted : GuestAccessOutcome;

    /// <summary>The setting could not be read.</summary>
    public sealed record Failure(string Diagnostic) : GuestAccessOutcome;
}

/// <summary>Input to <see cref="IDataverseGuestUserWriter.EnsureGuestUserAsync"/>.</summary>
/// <param name="EnvironmentUrl">The adopted environment (InterStepState.DataverseEnvUrl, H5).</param>
/// <param name="TenantId">The run's tenant (§4D I1 / I5).</param>
/// <param name="EntraObjectId">The guest's Entra object id.</param>
/// <param name="SecurityRoleNames">Root-business-unit role names to hold (H11UserProvisioningOptions.GuestSecurityRoleNames).</param>
public sealed record DataverseGuestUserRequest(
    string EnvironmentUrl, string TenantId, string EntraObjectId, IReadOnlyList<string> SecurityRoleNames);

/// <summary>Result of <see cref="IDataverseGuestUserWriter.EnsureGuestUserAsync"/>.</summary>
public abstract record DataverseGuestUserOutcome
{
    private DataverseGuestUserOutcome() { }

    /// <summary>The guest is a user of the environment holding every role.</summary>
    public sealed record Success(string SystemUserId) : DataverseGuestUserOutcome;

    /// <summary>A configured role is not in the environment — nothing was written for this guest.</summary>
    public sealed record RoleNotFound(string RoleName) : DataverseGuestUserOutcome;

    /// <summary>The user or a role association could not be written.</summary>
    public sealed record Failure(string Diagnostic) : DataverseGuestUserOutcome;
}
