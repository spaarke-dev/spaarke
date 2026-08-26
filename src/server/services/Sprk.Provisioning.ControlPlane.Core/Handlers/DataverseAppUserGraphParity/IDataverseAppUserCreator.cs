// -----------------------------------------------------------------------------
// IDataverseAppUserCreator.cs
//
// L2 abstraction over Dataverse Application User registration (H10 step 3 —
// "Register BFF app-reg AND UAMI as Dataverse System Administrator App Users
// via Dataverse Web API systemusers upsert on target env"). Domain outcomes
// (success with the resulting systemuserid, or a diagnostic failure) return
// typed results; only unexpected infrastructure faults should throw.
//
// SPEC / DESIGN references:
//   - spec.md FR-13 (H10 acceptance) + §9.3 Dataverse Security table row:
//     "UAMI service principal (by UAMI app ID) | System Administrator | Root".
//   - design.md §9.3 R2 note: v3.2 (M-10) interim automates the App User
//     registration step (superseding the manual PPAC-UI-only v2 behavior);
//     the v3 DESIGN TARGET (TF `powerplatform_user`, fully automated) is
//     deferred per M-10 to first-customer engagement.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>Request to ensure one Dataverse Application User exists with a given security role.</summary>
/// <param name="EnvironmentUrl">Target Dataverse environment URL.</param>
/// <param name="TenantId">Target Entra tenant id (§4D I1 — mandatory, no default).</param>
/// <param name="ApplicationId">The Entra application (client) ID to register as the App User's <c>applicationid</c>.</param>
/// <param name="SecurityRoleName">Security role name to ensure is associated (e.g. "System Administrator").</param>
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
}

/// <summary>
/// Result of one <see cref="IDataverseAppUserCreator.EnsureAppUserAsync"/>
/// invocation. Exhaustive: <see cref="Success"/> | <see cref="Failure"/>.
/// </summary>
public abstract record DataverseAppUserCreationOutcome
{
    private DataverseAppUserCreationOutcome() { }

    /// <summary>App User exists (created or already present) with the requested role associated.</summary>
    /// <param name="SystemUserId">The Dataverse <c>systemuserid</c> GUID.</param>
    public sealed record Success(string SystemUserId) : DataverseAppUserCreationOutcome;

    /// <summary>Registration failed. <paramref name="Diagnostic"/> is operator-facing.</summary>
    public sealed record Failure(string Diagnostic) : DataverseAppUserCreationOutcome;
}
