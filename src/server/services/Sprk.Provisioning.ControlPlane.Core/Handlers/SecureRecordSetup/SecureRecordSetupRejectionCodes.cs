// -----------------------------------------------------------------------------
// SecureRecordSetupRejectionCodes.cs
//
// T256 (H7b) — the rejection codes of the Secure Record setup handler. The secure_setup.* codes are the ones
// unified-access-control-r2 proposed in INCOMING-145 §2.4, verbatim, plus the ones the steps it added later need
// (§6 T2/T4, §4 item 2's S10–S14, the sprk_noaccessentry prerequisite, the dry run, and S15–S18 — the contact
// identity-binding profiles' memberships, INCOMING-141 / T255). The infrastructure codes are the
// shared ones H7 uses (EnvVarValuesRejectionCodes) — one code for one condition across the Dataverse handlers.
//
// §4C CLASS (INCOMING-145 §2.3): every failure is Resumable except where an owner decision is needed before anything
// can change — users in the business unit, members of the owner team, a business unit under the wrong parent, a root
// default team that reaches the secure unit by depth, a field-security writer nobody named (on sprk_issecure or on
// an identity-binding column), and (T259, §6 T1/T3) a customer business unit that is missing or not under the root, or a
// BFF application user outside it. Those are
// QuarantineRequired: a retry cannot fix them, and the handler never moves a user, a team member or a business unit.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

namespace Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

/// <summary>Rejection codes returned by <see cref="H7bSecureRecordSetupHandler"/>.</summary>
public static class SecureRecordSetupRejectionCodes
{
    // ---- INCOMING-145 §2.4 (verbatim) ----

    /// <summary>S0: <c>organization.sharetopreviousowneronassign</c> is on — an org-wide operator decision (Resumable).</summary>
    public const string OrgShareToPreviousOwnerOn = "secure_setup.org_sharetopreviousowner_on";

    /// <summary>S1: more than one business unit carries the Secure Record name (Resumable).</summary>
    public const string BusinessUnitAmbiguous = "secure_setup.bu_ambiguous";

    /// <summary>S2: the Secure Record business unit holds a user (QuarantineRequired — moving users is an owner decision).</summary>
    public const string BusinessUnitHasUsers = "secure_setup.bu_has_users";

    /// <summary>S3: more than one named owner team (Resumable).</summary>
    public const string OwnerTeamAmbiguous = "secure_setup.owner_team_ambiguous";

    /// <summary>S3: the named owner team has a member of any kind (QuarantineRequired).</summary>
    public const string OwnerTeamHasMembers = "secure_setup.owner_team_has_members";

    /// <summary>S4: more than one <c>Secure Record Owner</c> role in the unit (Resumable).</summary>
    public const string RoleAmbiguous = "secure_setup.role_ambiguous";

    /// <summary>S4: the role found in the unit is a replica of a role created in an ancestor unit (Resumable).</summary>
    public const string RoleIsReplica = "secure_setup.role_is_replica";

    /// <summary>S5: metadata names a table's Read privilege differently from the file, or has none (Resumable).</summary>
    public const string PrivilegeNameMismatch = "secure_setup.privilege_name_mismatch";

    /// <summary>S5: a listed privilege is held at a depth other than Basic — never widened or narrowed (Resumable).</summary>
    public const string PrivilegeWrongDepth = "secure_setup.privilege_wrong_depth";

    /// <summary>S9: the re-read end state is not the codified one (Resumable).</summary>
    public const string VerifyFailed = "secure_setup.verify_failed";

    // ---- Steps added after §2.4 ----

    /// <summary>
    /// Prerequisite (UAC-r2 audit, #1364): the environment has no readable <c>sprk_noaccessentry</c> table with the
    /// columns the BFF's deny-list reader selects. The BFF fails closed on every read there, so the BFF deploy (H9, which
    /// waits for H7b) must not happen. The table ships in SpaarkeMaster ≥ 1.2.0.0 (H6) (Resumable).
    /// </summary>
    public const string NoAccessEntryMissing = "secure_setup.noaccessentry_missing";

    /// <summary>The environment does not report exactly one root business unit (Resumable).</summary>
    public const string RootBusinessUnitUnresolved = "secure_setup.root_bu_unresolved";

    /// <summary>§6 T2: the Secure Record unit exists under a parent other than the root (QuarantineRequired).</summary>
    public const string BusinessUnitWrongParent = "secure_setup.bu_wrong_parent";

    /// <summary>
    /// §6 T4: the root business unit's default team holds a role with Deep or Global Read on a table of the codified set,
    /// so every root-unit user reads every secure record by depth (QuarantineRequired).
    /// </summary>
    public const string RootDefaultTeamReachesSecureUnit = "secure_setup.root_default_team_reaches_secure_bu";

    /// <summary>
    /// T259 (§6 T1, ISS-010): the customer's business unit H10 recorded (InterStepState.CustomerBusinessUnitId) does not
    /// exist. H10 created it and the BFF's application users in it; something removed it since — an operator must find out
    /// what before anything is configured (QuarantineRequired).
    /// </summary>
    public const string CustomerBusinessUnitMissing = "secure_setup.customer_bu_missing";

    /// <summary>
    /// T259 (§6 T1, ISS-010): the customer's business unit is not a DIRECT child of the root (or is the root). Only as a
    /// sibling of the Secure Record unit does Deep depth there stop short of every secure record (QuarantineRequired).
    /// </summary>
    public const string CustomerBusinessUnitWrongParent = "secure_setup.customer_bu_wrong_parent";

    /// <summary>
    /// T259 (§6 T3, ISS-010): one of H10's two BFF application users is not in the customer's business unit (or does not
    /// exist). Moving it strips its roles — an owner decision (QuarantineRequired).
    /// </summary>
    public const string AppUserOutsideCustomerBusinessUnit = "secure_setup.app_user_outside_customer_bu";

    /// <summary>
    /// S10/S15: a field-security profile H7b maintains memberships of (the two BFF-managed profiles, the two identity-link
    /// profiles) is missing or ambiguous — every one ships in SpaarkeMaster (Resumable).
    /// </summary>
    public const string FieldProfileUnresolved = "secure_setup.field_profile_unresolved";

    /// <summary>S12: the writer profile has a member other than the BFF's application users (QuarantineRequired).</summary>
    public const string FieldWriterHasOtherMember = "secure_setup.field_writer_has_other_member";

    /// <summary>S14: the shipped lock on <c>sprk_issecure</c> is incomplete — not secured, or a profile grant missing (Resumable).</summary>
    public const string FieldLockIncomplete = "secure_setup.field_lock_incomplete";

    /// <summary>
    /// S14: a profile other than the BFF writer profile (and System Administrator) may write <c>sprk_issecure</c> — the
    /// reader profile included: every default team is its member (QuarantineRequired).
    /// </summary>
    public const string FieldLockOtherWriter = "secure_setup.field_lock_other_writer";

    // ---- Contact identity binding (unified-access-control-r2 INCOMING-141 / task 141; T255) ----

    /// <summary>
    /// S17: "Spaarke Identity Link Writers" has a member other than the BFF's application users. Every member could bind
    /// any contact to any identity, or point any systemuser at any contact — whose grants a caller inherits
    /// (QuarantineRequired — nothing is removed here).
    /// </summary>
    public const string IdentityLinkWriterHasOtherMember = "secure_setup.identity_link_writer_has_other_member";

    /// <summary>
    /// S18: the shipped lock on <c>contact.sprk_externalobjectid</c> / <c>systemuser.sprk_primarycontact</c> is
    /// incomplete — a column not secured, or a reader / writer grant missing (Resumable: import SpaarkeMaster, H6).
    /// </summary>
    public const string IdentityLinkLockIncomplete = "secure_setup.identity_link_lock_incomplete";

    /// <summary>
    /// S18: a profile other than "Spaarke Identity Link Writers" (and System Administrator) may create or update a binding
    /// column — the reader profile included (QuarantineRequired).
    /// </summary>
    public const string IdentityLinkLockOtherWriter = "secure_setup.identity_link_lock_other_writer";

    /// <summary>
    /// Dry run finished: the plan is in the <see cref="H7bSecureRecordSetupHandler.PlanGateId"/> gate evidence and nothing
    /// was written. The run stops here so no later handler runs against an environment the dry run left unconfigured.
    /// </summary>
    public const string DryRunComplete = "secure_setup.dry_run";

    /// <summary>
    /// The intake value <c>secureRecordSetupDryRun</c> is not <c>true</c> or <c>false</c>. POST /api/runs refuses it with
    /// this code (SecureRecordSetupIntake); the handler keeps the check as defence in depth (Resumable).
    /// </summary>
    public const string DryRunInvalid = "secure_setup.dry_run_invalid";

    /// <summary>The embedded codified set is missing or invalid — a build defect (Resumable after a fixed Worker deploy).</summary>
    public const string RoleSetInvalid = "secure_setup.role_set_invalid";

    // ---- Shared with H7 (EnvVarValuesRejectionCodes) ----

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

    /// <summary>Any other Dataverse or transport fault.</summary>
    public const string DataverseInvocationFailed = EnvVarValuesRejectionCodes.WriterInvocationFailed;

    /// <summary>A concurrent writer advanced the run between H7b's read and its state write.</summary>
    public const string ConcurrentWriteConflict = EnvVarValuesRejectionCodes.ConcurrentWriteConflict;

    /// <summary>The run row was deleted while H7b was in flight.</summary>
    public const string RunDeletedDuringWrite = EnvVarValuesRejectionCodes.RunDeletedDuringWrite;
}
