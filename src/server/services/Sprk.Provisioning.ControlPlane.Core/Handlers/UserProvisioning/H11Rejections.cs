// -----------------------------------------------------------------------------
// H11Rejections.cs
//
// Machine-stable rejection codes emitted by H11UserProvisioningHandler (task
// 054). Every distinct failure mode gets its own code so the reconciler +
// operator UI can branch on the exact reason WITHOUT string-matching the
// human-readable Diagnostic.
//
// PATTERN PARITY: mirrors Handlers/EntraAppReg/EntraAppRegRejectionCodes.cs
// and Handlers/DataverseAppUserGraphParity/H10Rejections.cs — one const per
// failure branch + lowercase kebab-case for greppability.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>
/// Machine-stable rejection codes for <see cref="H11UserProvisioningHandler"/>
/// failures.
/// </summary>
public static class H11Rejections
{
    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "userprov-run-not-found";

    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "userprov-missing-tenant-id";

    /// <summary>Run parameter <c>identityPreset</c> missing (design.md D6).</summary>
    public const string MissingIdentityPreset = "userprov-missing-identity-preset";

    /// <summary>Run parameter <c>identityPreset</c> is neither <c>B2BGuest</c> nor <c>NativeAccount</c>.</summary>
    public const string InvalidIdentityPreset = "userprov-invalid-identity-preset";

    /// <summary>Run parameter <c>usersJson</c> missing, or deserialized to an empty array.</summary>
    public const string MissingUsers = "userprov-missing-users";

    /// <summary>Run parameter <c>usersJson</c> could not be deserialized as a user array.</summary>
    public const string MalformedUsersPayload = "userprov-malformed-users-payload";

    /// <summary>
    /// NativeAccount branch: Graph user creation failed for a specific user
    /// (diagnostic names the user).
    /// </summary>
    public const string UserCreationFailed = "userprov-user-creation-failed";

    /// <summary>
    /// NativeAccount branch: license assignment failed for a specific user
    /// (diagnostic names the user) — DISTINCT rejection code per acceptance
    /// criterion; classified RetryableWithCleanup (user account already
    /// exists; assignLicense is itself idempotent).
    /// </summary>
    public const string LicenseAssignmentFailed = "userprov-license-assignment-failed";

    /// <summary>B2BGuest branch: Graph invitation failed for a specific user (diagnostic names the user).</summary>
    public const string B2BInvitationFailed = "userprov-b2b-invitation-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "userprov-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H11 was in flight.</summary>
    public const string RunDeletedDuringProvisioning = "userprov-run-deleted-during-provisioning";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c> by
/// H11 user provisioning. Kept as string constants so grep across the
/// codebase finds every read/write of the same gate name.
/// </summary>
public static class H11Gates
{
    /// <summary>
    /// The gate H11 owns for B2B guest consent (invitation acceptance) under
    /// the D6 B2BGuest identity preset. Flipped from <c>Pending</c> to
    /// <c>Verified</c> when <see cref="IB2BConsentVerifier"/> confirms every
    /// invited guest has accepted.
    /// </summary>
    public const string B2BConsent = "b2b-consent";
}
