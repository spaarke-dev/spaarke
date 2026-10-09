// -----------------------------------------------------------------------------
// IDataverseAppUserCreator.cs
//
// L2 abstraction over Dataverse Application User registration (H10 step 3 —
// "Register BFF app-reg AND UAMI as Dataverse System Administrator App Users
// via Dataverse Web API systemusers upsert on target env"). Domain outcomes
// (success with the resulting systemuserid, or a diagnostic failure) return
// typed results; only unexpected infrastructure faults should throw.
//
// T259 (ISS-010 / #1486, owner 2026-10-09): the App Users are CREATED in the customer's own business unit (a direct
// child of the root, sibling of the Secure Record unit), which EnsureCustomerBusinessUnitAsync finds or creates first.
// §11 — existing: H7b's ISecureRecordSetupDataverse reads/creates business units, but it signs in as the BFF app
// registration, which becomes an application user only in THIS step; extending H10's own seam is the reuse path.
//
// SPEC / DESIGN references:
//   - spec.md FR-13 (H10 acceptance) + §9.3 Dataverse Security table row:
//     "UAMI service principal (by UAMI app ID) | System Administrator | Root" — superseded by T259 (ISS-010, owner
//     2026-10-09): both App Users now live in the customer's own unit, a direct child of the root.
//   - design.md §9.3 R2 note: v3.2 (M-10) interim automates the App User
//     registration step (superseding the manual PPAC-UI-only v2 behavior);
//     the v3 DESIGN TARGET (TF `powerplatform_user`, fully automated) is
//     deferred per M-10 to first-customer engagement.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>
/// Result of one <see cref="IDataverseAppUserCreator.EnsureAppUserAsync"/>
/// invocation. Exhaustive: <see cref="Success"/> | <see cref="Failure"/> | <see cref="InForeignBusinessUnit"/> (T259).
/// </summary>
public abstract record DataverseAppUserCreationOutcome
{
    private DataverseAppUserCreationOutcome() { }

    /// <summary>App User exists (created or already present) with the requested role associated.</summary>
    /// <param name="SystemUserId">The Dataverse <c>systemuserid</c> GUID.</param>
    public sealed record Success(string SystemUserId) : DataverseAppUserCreationOutcome;

    /// <summary>Registration failed. <paramref name="Diagnostic"/> is operator-facing.</summary>
    public sealed record Failure(string Diagnostic) : DataverseAppUserCreationOutcome;

    /// <summary>
    /// T259: the App User already exists in <paramref name="BusinessUnitId"/>, not the requested unit. Nothing was written
    /// — a business-unit change strips every role of the user, so it is never moved here.
    /// </summary>
    public sealed record InForeignBusinessUnit(string SystemUserId, Guid BusinessUnitId) : DataverseAppUserCreationOutcome;
}

/// <summary>Result of <see cref="IDataverseAppUserCreator.EnsureCustomerBusinessUnitAsync"/> (T259).</summary>
public abstract record CustomerBusinessUnitOutcome
{
    private CustomerBusinessUnitOutcome() { }

    /// <summary>The unit exists directly under the root (<paramref name="Created"/>: by this call).</summary>
    public sealed record Success(Guid BusinessUnitId, bool Created) : CustomerBusinessUnitOutcome;

    /// <summary>More than one unit carries the name. Nothing was written.</summary>
    public sealed record Ambiguous(int Count) : CustomerBusinessUnitOutcome;

    /// <summary>
    /// The one unit carrying the name is not a direct child of the root (<paramref name="ParentId"/> null: it IS the root).
    /// Nothing was written.
    /// </summary>
    public sealed record WrongParent(Guid BusinessUnitId, Guid? ParentId, Guid RootBusinessUnitId) : CustomerBusinessUnitOutcome;

    /// <summary>A read or the create failed. <paramref name="Diagnostic"/> is operator-facing.</summary>
    public sealed record Failure(string Diagnostic) : CustomerBusinessUnitOutcome;
}

/// <summary>Request to ensure one Dataverse Application User exists with a given security role.</summary>
/// <param name="EnvironmentUrl">Target Dataverse environment URL.</param>
/// <param name="TenantId">Target Entra tenant id (§4D I1 — mandatory, no default).</param>
/// <param name="ApplicationId">The Entra application (client) ID to register as the App User's <c>applicationid</c>.</param>
/// <param name="SecurityRoleName">
/// Security role name to ensure is associated (e.g. "System Administrator") — the copy of that role IN
/// <paramref name="BusinessUnitId"/> (Dataverse assigns a user only roles of the user's own unit).
/// </param>
/// <param name="BusinessUnitId">
/// T259: the customer's business unit (<see cref="IDataverseAppUserCreator.EnsureCustomerBusinessUnitAsync"/>). A new App
/// User is CREATED in it; an existing one elsewhere is <see cref="DataverseAppUserCreationOutcome.InForeignBusinessUnit"/>.
/// </param>
/// <param name="AzureActiveDirectoryObjectId">
/// OPTIONAL explicit value for the App User row's <c>azureactivedirectoryobjectid</c> field.
/// Null (default) leaves Dataverse to auto-resolve the field from <paramref name="ApplicationId"/> —
/// the correct behavior for a standard Entra app registration (BFF app-reg OBO row), whose Dataverse
/// AAD sync reliably finds the enterprise application's service principal object id.
/// </param>
/// <remarks>
/// <b>auth-v4 §10.4 silent-fail trap (task 205d / punch row A41, BINDING)</b>: for the UAMI row, this
/// value MUST be supplied explicitly and MUST be the UAMI's <b>principalId (service principal object
/// id)</b> — NEVER the UAMI's clientId. Both are valid-shaped GUIDs; a row created with the wrong one
/// still passes T2's existence + count check, but the app-only Dataverse token's <c>oid</c> claim
/// (the UAMI's principalId) will never match a row whose <c>azureactivedirectoryobjectid</c> holds the
/// clientId instead — every app-only Dataverse call for that customer 401s at first use. This is
/// auth-v4's documented "single most-missed item" (see <c>mi-proof-dataverse-side.md</c>). Do NOT rely
/// on Dataverse's applicationid-only auto-resolution for a Managed Identity row — set this field
/// explicitly instead.
/// </remarks>
public sealed record DataverseAppUserCreationRequest(
    string EnvironmentUrl,
    string TenantId,
    string ApplicationId,
    string SecurityRoleName,
    Guid BusinessUnitId,
    string? AzureActiveDirectoryObjectId = null);

/// <summary>
/// Ensures a Dataverse Application User exists (upsert semantics — creates if
/// absent, reuses if present) for a given Entra <c>applicationId</c>, and
/// ensures the requested security role is associated. Idempotent by
/// construction: re-invoking with the same request is a safe no-op once the
/// App User + role association exist.
/// </summary>
public interface IDataverseAppUserCreator
{
    /// <summary>
    /// Ensures the App User + role association exist. Domain failures
    /// (Dataverse API error, missing root business unit, role not found)
    /// return <see cref="DataverseAppUserCreationOutcome.Failure"/>; only
    /// unexpected infrastructure faults (network fault outside HTTP status
    /// handling) should throw.
    /// </summary>
    Task<DataverseAppUserCreationOutcome> EnsureAppUserAsync(
        DataverseAppUserCreationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// T259 (ISS-010, INCOMING-145 §6 T1): finds the business unit named <paramref name="name"/> directly under the root,
    /// or creates it there. Read-then-write: an ambiguous name or a unit of that name under another parent writes nothing.
    /// </summary>
    Task<CustomerBusinessUnitOutcome> EnsureCustomerBusinessUnitAsync(
        string environmentUrl,
        string tenantId,
        string name,
        CancellationToken cancellationToken);
}
