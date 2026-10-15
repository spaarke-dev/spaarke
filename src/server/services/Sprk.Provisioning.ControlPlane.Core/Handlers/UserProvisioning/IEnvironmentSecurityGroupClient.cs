// -----------------------------------------------------------------------------
// IEnvironmentSecurityGroupClient.cs
//
// Task 232 (D2, G10). The Dataverse environment's security group is the
// boundary that keeps one Model 1 customer's guests out of another customer's
// environment — every Model 1 environment lives in Spaarke's tenant, so without
// a group any user of that tenant is admitted on first sign-in. The operator
// creates the group (sprk-{customerId}-users) and sets it on the environment
// (PRQ-C-10); H11 reads it, refuses one that is not this customer's, and adds
// each guest to it.
//
// §11 justification — existing: GraphRestUserProvisioner (NativeAccount user
// create + licence) and GraphRestB2BInvitationClient (invitation) — neither
// touches groups. Extension: folding group reads/writes into either would give
// it a second reason to change; H11's seams are one Graph concern each.
// Cost of doing nothing: guests are invited but cannot open the environment (no
// group membership) or — without the name check — could be added to another
// customer's group by a mistyped id.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>Reads the customer environment's security group and adds members to it (Microsoft Graph).</summary>
public interface IEnvironmentSecurityGroupClient
{
    /// <summary>Reads the group's <c>displayName</c> and <c>securityEnabled</c>.</summary>
    Task<SecurityGroupReadOutcome> ReadAsync(string groupId, string tenantId, CancellationToken cancellationToken);

    /// <summary>Adds <paramref name="userId"/> to the group. Idempotent: an existing member is success.</summary>
    Task<SecurityGroupMembershipOutcome> AddMemberAsync(
        string groupId, string userId, string tenantId, CancellationToken cancellationToken);
}

/// <summary>Result of <see cref="IEnvironmentSecurityGroupClient.ReadAsync"/>.</summary>
public abstract record SecurityGroupReadOutcome
{
    private SecurityGroupReadOutcome() { }

    /// <summary>The group exists.</summary>
    public sealed record Found(string DisplayName, bool SecurityEnabled) : SecurityGroupReadOutcome;

    /// <summary>The group could not be read (not found, refused, transport error).</summary>
    public sealed record Failure(string Diagnostic) : SecurityGroupReadOutcome;
}

/// <summary>Result of <see cref="IEnvironmentSecurityGroupClient.AddMemberAsync"/>.</summary>
public abstract record SecurityGroupMembershipOutcome
{
    private SecurityGroupMembershipOutcome() { }

    /// <summary>The user is a member (added now, or already).</summary>
    public sealed record Success : SecurityGroupMembershipOutcome;

    /// <summary>The user could not be added.</summary>
    public sealed record Failure(string Diagnostic) : SecurityGroupMembershipOutcome;
}
