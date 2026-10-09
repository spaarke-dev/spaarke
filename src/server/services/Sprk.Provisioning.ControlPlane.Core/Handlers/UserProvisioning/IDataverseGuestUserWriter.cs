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
// Three calls, in H11's order: ReadGuestAccessAsync and ResolveRolesAsync run
// BEFORE anyone is invited (a restricted environment or a missing role writes
// nothing — not even an invitation); EnsureGuestUserAsync runs per redeemed guest
// with the role ids resolved once.
//
// T259 (ISS-010 / #1486, owner decision 2026-10-09 — INCOMING-145 §6 T5): guests belong in the CUSTOMER's business
// unit (H10's InterStepState.CustomerBusinessUnitId — a direct child of the root, sibling of the Secure Record unit),
// never the root: Spaarke Basic User holds Deep read on project/matter/work assignment, and Deep at the root reaches the
// Secure Record unit. Roles are resolved IN the customer unit (its inherited copies); a guest Dataverse added to the root
// on the alternate-key read is moved to the customer unit BEFORE any role is associated (a business-unit change strips
// roles), and the move is read back. A guest found in any other unit is never moved (InForeignBusinessUnit).
//
// §11 justification — existing: H10's IDataverseAppUserCreator (APPLICATION
// users by applicationid) and H8's IDataverseRootBusinessUnitReader (root
// business unit). Extension: the root-unit lookup is REUSED (injected reader);
// a human user is keyed by azureactivedirectoryobjectid, takes no application
// id and must be in the environment's security group first, so folding it into
// H10's creator would branch on every step. Cost of doing nothing: every Model 1
// guest is invited and still cannot use the product.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>Makes guests Dataverse users of the environment holding the given roles. Idempotent.</summary>
public interface IDataverseGuestUserWriter
{
    /// <summary>
    /// Reads the environment's <c>restrictguestuseraccess</c> setting (organization table). New environments default to
    /// restricted, which blocks guests from Dataverse entirely — the operator turns it off (PRQ-C-12); H11 checks it
    /// before inviting anyone.
    /// </summary>
    Task<GuestAccessOutcome> ReadGuestAccessAsync(string environmentUrl, string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves each role name to the ONE role of that name in <paramref name="businessUnitId"/> — the customer's unit
    /// (T259; Dataverse copies every root role into each unit, and assigns a user only its own unit's roles). Run before
    /// anyone is invited: a missing role, or a name that matches more than one role, writes nothing.
    /// </summary>
    Task<GuestRoleResolution> ResolveRolesAsync(
        string environmentUrl, string tenantId, Guid businessUnitId, IReadOnlyList<string> roleNames,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the systemuser for <see cref="DataverseGuestUserRequest.EntraObjectId"/> — Dataverse adds a member of the
    /// environment security group on that read (root business unit) — moves it from the ROOT unit to
    /// <see cref="DataverseGuestUserRequest.BusinessUnitId"/> (read back), and associates every role in
    /// <see cref="DataverseGuestUserRequest.RoleIds"/> it does not hold yet. A user in any other unit is not moved:
    /// <see cref="DataverseGuestUserOutcome.InForeignBusinessUnit"/>, nothing written.
    /// </summary>
    Task<DataverseGuestUserOutcome> EnsureGuestUserAsync(DataverseGuestUserRequest request, CancellationToken cancellationToken);
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

/// <summary>Result of <see cref="IDataverseGuestUserWriter.ResolveRolesAsync"/>.</summary>
public abstract record GuestRoleResolution
{
    private GuestRoleResolution() { }

    /// <summary>Every name resolved, in order.</summary>
    public sealed record Resolved(IReadOnlyList<Guid> RoleIds) : GuestRoleResolution;

    /// <summary>A configured role is not in the customer's business unit.</summary>
    public sealed record RoleNotFound(string RoleName) : GuestRoleResolution;

    /// <summary>The roles could not be read, or a name is ambiguous.</summary>
    public sealed record Failure(string Diagnostic) : GuestRoleResolution;
}

/// <summary>Input to <see cref="IDataverseGuestUserWriter.EnsureGuestUserAsync"/>.</summary>
/// <param name="EnvironmentUrl">The adopted environment (InterStepState.DataverseEnvUrl, H5).</param>
/// <param name="TenantId">The run's tenant (§4D I1 / I5).</param>
/// <param name="EntraObjectId">The guest's Entra object id.</param>
/// <param name="BusinessUnitId">T259: the customer's business unit (InterStepState.CustomerBusinessUnitId, H10).</param>
/// <param name="RoleIds">Role ids IN <paramref name="BusinessUnitId"/> to hold (<see cref="IDataverseGuestUserWriter.ResolveRolesAsync"/>).</param>
public sealed record DataverseGuestUserRequest(
    string EnvironmentUrl, string TenantId, string EntraObjectId, Guid BusinessUnitId, IReadOnlyList<Guid> RoleIds);

/// <summary>Result of <see cref="IDataverseGuestUserWriter.EnsureGuestUserAsync"/>.</summary>
public abstract record DataverseGuestUserOutcome
{
    private DataverseGuestUserOutcome() { }

    /// <summary>The guest is a user of the environment holding every role.</summary>
    public sealed record Success(string SystemUserId) : DataverseGuestUserOutcome;

    /// <summary>The user or a role association could not be written.</summary>
    public sealed record Failure(string Diagnostic) : DataverseGuestUserOutcome;

    /// <summary>
    /// T259: the guest is a user in <paramref name="BusinessUnitId"/> — neither the customer's unit nor the root. Nothing
    /// was written: moving a user out of an arbitrary unit (the Secure Record unit included) is an owner decision.
    /// </summary>
    public sealed record InForeignBusinessUnit(string SystemUserId, Guid BusinessUnitId) : DataverseGuestUserOutcome;

    /// <summary>
    /// T259: the guest (in the customer's unit) holds <paramref name="RoleId"/>, a role of another unit
    /// (<paramref name="BusinessUnitId"/>) — a root role's Deep read would reach the Secure Record unit. Nothing removed.
    /// </summary>
    public sealed record HoldsRoleOutsideBusinessUnit(string SystemUserId, Guid RoleId, Guid BusinessUnitId) : DataverseGuestUserOutcome;
}
