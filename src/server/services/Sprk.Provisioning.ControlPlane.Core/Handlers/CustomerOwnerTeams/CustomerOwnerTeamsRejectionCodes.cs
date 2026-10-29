// -----------------------------------------------------------------------------
// CustomerOwnerTeamsRejectionCodes.cs
//
// #2429 (H7c) — the rejection codes of the customer owner teams handler, one per refusal of
// scripts/Set-CustomerOwnerTeams.ps1 (the reference implementation, unified-access-control-r2 task 194b). The
// infrastructure codes are the shared ones H7/H7b use (EnvVarValuesRejectionCodes) — one code for one condition across the
// Dataverse handlers.
//
// §4C CLASS: QuarantineRequired where the script says the answer is an owner decision — a customer unit that is missing,
// is the root, is the Secure Record unit or is not a direct child of the root (H10 recorded it; something changed it since);
// two teams of a declared name in the customer unit ("resolve it by hand (owner decision)"); and the drift -Apply refuses
// (OWNER DECISION NEEDED: a member, another role on the team, the wrong team type, a default team, a role assigned
// elsewhere). The handler never removes a member it did not add, moves a team, or takes a role off anyone. Every other
// failure is Resumable: every write is guarded by a read, so a resume picks up where the run stopped.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

namespace Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams;

/// <summary>Rejection codes returned by <see cref="H7cCustomerOwnerTeamsHandler"/>.</summary>
public static class CustomerOwnerTeamsRejectionCodes
{
    /// <summary>The environment does not report exactly one root business unit (Resumable).</summary>
    public const string RootBusinessUnitUnresolved = "owner_teams.root_bu_unresolved";

    /// <summary>
    /// No business unit, or more than one, carries the Secure Record unit's name (config/secure-record-owner-role.json). H7b
    /// creates it before H7c, so a name that matches nothing would let the Secure Record unit pass as a customer unit
    /// (the script's R4b-4 fail-closed rule) (Resumable).
    /// </summary>
    public const string SecureBusinessUnitUnresolved = "owner_teams.secure_bu_unresolved";

    /// <summary>The customer's business unit H10 recorded does not exist (QuarantineRequired).</summary>
    public const string CustomerBusinessUnitMissing = "owner_teams.customer_bu_missing";

    /// <summary>The customer's business unit is the ROOT unit, which holds only Spaarke product data (QuarantineRequired).</summary>
    public const string CustomerBusinessUnitIsRoot = "owner_teams.customer_bu_is_root";

    /// <summary>The customer's business unit is the Secure Record unit, never a customer unit (QuarantineRequired).</summary>
    public const string CustomerBusinessUnitIsSecure = "owner_teams.customer_bu_is_secure";

    /// <summary>
    /// The customer's business unit is not a DIRECT child of the root: the teams live in the customer's TOP unit, which the
    /// BFF walks up to from any unit below (QuarantineRequired).
    /// </summary>
    public const string CustomerBusinessUnitWrongParent = "owner_teams.customer_bu_wrong_parent";

    /// <summary>Metadata names a table's Read privilege differently from the file, or reports none or several (Resumable).</summary>
    public const string PrivilegeNameMismatch = "owner_teams.privilege_name_mismatch";

    /// <summary>More than one team of a declared name is in the customer unit — resolved by hand (QuarantineRequired).</summary>
    public const string TeamAmbiguous = "owner_teams.team_ambiguous";

    /// <summary>More than one role of a declared name is in the root unit (Resumable).</summary>
    public const string RoleAmbiguous = "owner_teams.role_ambiguous";

    /// <summary>OWNER DECISION: a declared team has a member — it must have none, ever (QuarantineRequired).</summary>
    public const string TeamHasMembers = "owner_teams.team_has_members";

    /// <summary>OWNER DECISION: a declared team is not an Owner team (teamtype 0), or is a unit's default team (QuarantineRequired).</summary>
    public const string TeamNotOwnerTeam = "owner_teams.team_not_owner_team";

    /// <summary>OWNER DECISION: a declared team holds a role other than its own role's copy (QuarantineRequired).</summary>
    public const string TeamHasOtherRole = "owner_teams.team_has_other_role";

    /// <summary>
    /// OWNER DECISION: a declared role (or a copy of it) is assigned to a user, or to a team other than a team of its name
    /// in a customer top unit holding that unit's copy (QuarantineRequired).
    /// </summary>
    public const string RoleAssignedElsewhere = "owner_teams.role_assigned_elsewhere";

    /// <summary>A declared role has no copy in the customer unit to assign after it was created or read (Resumable).</summary>
    public const string RoleCopyMissing = "owner_teams.role_copy_missing";

    /// <summary>The re-read end state still shows drift (Resumable).</summary>
    public const string VerifyFailed = "owner_teams.verify_failed";

    /// <summary>The embedded declared role files are missing or invalid — a build defect (Resumable after a fixed Worker deploy).</summary>
    public const string RoleSetInvalid = "owner_teams.role_set_invalid";

    // ---- Shared with H7 / H7b (EnvVarValuesRejectionCodes) ----

    /// <summary>The run row is not in the customer's partition.</summary>
    public const string RunNotFound = EnvVarValuesRejectionCodes.RunNotFound;

    /// <summary>A required upstream value (intake or another handler's output) is absent.</summary>
    public const string MissingUpstreamState = EnvVarValuesRejectionCodes.MissingUpstreamState;

    /// <summary>The FR-39 chain needs the BFF app-registration secret and none is configured.</summary>
    public const string MissingClientSecret = EnvVarValuesRejectionCodes.MissingClientSecret;

    /// <summary>Token acquisition failed, or Dataverse answered 401/403.</summary>
    public const string DataverseAuthFailure = EnvVarValuesRejectionCodes.DataverseAuthFailure;

    /// <summary>Dataverse answered 429.</summary>
    public const string RateLimited = EnvVarValuesRejectionCodes.RateLimited;

    /// <summary>Any other Dataverse or transport fault — a read that fails is a failure, never "absent".</summary>
    public const string DataverseInvocationFailed = EnvVarValuesRejectionCodes.WriterInvocationFailed;

    /// <summary>A concurrent writer advanced the run between H7c's read and its state write.</summary>
    public const string ConcurrentWriteConflict = EnvVarValuesRejectionCodes.ConcurrentWriteConflict;

    /// <summary>The run row was deleted while H7c was in flight.</summary>
    public const string RunDeletedDuringWrite = EnvVarValuesRejectionCodes.RunDeletedDuringWrite;
}
