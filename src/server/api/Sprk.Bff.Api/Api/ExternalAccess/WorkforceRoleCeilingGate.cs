using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using AccessRights = Spaarke.Dataverse.AccessRights;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>One operation a route needs on one table, checked against the workforce ceiling (task 186).</summary>
internal readonly record struct WorkforceCeilingRequirement(string Table, AccessRights Rights);

/// <summary>
/// The workforce privilege ceiling at the route (unified-access-control-r2 task 186, ADR-028 A9, owner round 101 option
/// (a)): a WORKFORCE-CONTACT caller (<see cref="CallerPrincipalPlane.Workforce"/> with no systemuser) may perform an
/// operation only when the role <see cref="UserPrivilegeChecker.WorkforceRoleName"/> holds the privilege at any depth
/// (<see cref="IDataversePrivilegeChecker.GetWorkforceRoleCeilingAsync"/>) AND UAC's record scope allows it. This gate is
/// the first half; the handler's existing requester/record checks, which run after it, are the second. Both must pass.
/// </summary>
/// <remarks>
/// <para><b>Who it applies to.</b> Only <see cref="CallerPrincipal.Plane"/> = Workforce with
/// <see cref="CallerPrincipal.SystemUserId"/> null. A licensed systemuser keeps its own Dataverse roles, and a CIAM
/// partner contact is unaffected: for both the gate returns <see langword="null"/> without reading the role. The test is
/// "no systemuser", not <see cref="CallerPrincipal.IsContactPrincipal"/>, so a workforce principal with an empty contact
/// id still meets the ceiling (fail closed).</para>
/// <para><b>Fail closed.</b> A role that is missing, ambiguous or unreadable denies with
/// <see cref="DenyBaselineUnavailable"/>; a privilege the role does not hold denies with
/// <see cref="DenyBaselinePrivilegeMissing"/>. Both are 403 and are decided before any record is read, so neither tells
/// the caller whether a record exists.</para>
/// <para><b>The route map</b> (design note §0, owner round 101) is the static requirement sets below; each route names its
/// set at the call site.</para>
/// <para>CLAUDE.md §11. <b>Existing:</b> <see cref="IDataversePrivilegeChecker"/> (the ceiling read is added there, not
/// here) and <see cref="ProblemDetailsHelper"/>. <b>Extension:</b> the gate is endpoint glue only (who it applies to, the
/// route map, the 403 shape); it reads nothing itself, and putting the route map in the Dataverse service would couple that
/// service to <see cref="CallerPrincipal"/> and the external routes. <b>Cost of doing nothing:</b> five route handlers
/// and the module-data seam would each repeat the plane test and the two deny shapes.</para>
/// </remarks>
internal static class WorkforceRoleCeilingGate
{
    /// <summary>The Workforce role is missing, ambiguous or could not be read: every workforce-contact operation is denied.</summary>
    internal const string DenyBaselineUnavailable = "sdap.access.deny.workforce_baseline_unavailable";

    /// <summary>The Workforce role does not hold a privilege this operation needs.</summary>
    internal const string DenyBaselinePrivilegeMissing = "sdap.access.deny.workforce_baseline_privilege_missing";

    internal const string ServiceRequestTable = "sprk_servicerequest";
    internal const string DocumentTable = "sprk_document";

    /// <summary><c>POST /servicerequests</c> (intake): Create on the service request.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> SubmitServiceRequest = new[]
    {
        new WorkforceCeilingRequirement(ServiceRequestTable, AccessRights.Create),
    };

    /// <summary>Reading own service requests through the module-data seam (<c>/api/dataverse/fetch</c>, <c>/record</c>).</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadServiceRequests = new[]
    {
        new WorkforceCeilingRequirement(ServiceRequestTable, AccessRights.Read),
    };

    /// <summary><c>GET /servicerequests/{id}/documents</c>, its <c>/content</c> and <c>/versions</c>.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadServiceRequestDocuments = new[]
    {
        new WorkforceCeilingRequirement(ServiceRequestTable, AccessRights.Read),
        new WorkforceCeilingRequirement(DocumentTable, AccessRights.Read),
    };

    /// <summary><c>POST /servicerequests/{id}/documents</c> (upload): Create the document, AppendTo the request.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> UploadServiceRequestDocument = new[]
    {
        new WorkforceCeilingRequirement(DocumentTable, AccessRights.Create),
        new WorkforceCeilingRequirement(ServiceRequestTable, AccessRights.AppendTo),
    };

    /// <summary><c>DELETE /servicerequests/{id}</c> (delete own): Delete on the service request.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> DeleteServiceRequest = new[]
    {
        new WorkforceCeilingRequirement(ServiceRequestTable, AccessRights.Delete),
    };

    /// <summary>True for a workforce caller that is not a systemuser: the only caller the ceiling applies to.</summary>
    internal static bool Applies(CallerPrincipal principal) =>
        principal.Plane == CallerPrincipalPlane.Workforce && principal.SystemUserId is null;

    /// <summary>
    /// Returns <see langword="null"/> when the ceiling does not apply or holds every requirement; otherwise the 403 to
    /// return. An empty requirement set is a programming error and denies.
    /// </summary>
    internal static async Task<IResult?> DenyAsync(
        CallerPrincipal principal,
        IDataversePrivilegeChecker privileges,
        IReadOnlyList<WorkforceCeilingRequirement> required,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(privileges);
        ArgumentNullException.ThrowIfNull(required);

        if (!Applies(principal))
            return null;

        // A null answer (a checker that returned nothing) is treated exactly like an unreadable role: fail closed.
        var ceiling = await privileges.GetWorkforceRoleCeilingAsync(ct).ConfigureAwait(false);
        if (ceiling is not { IsAvailable: true })
        {
            logger.LogWarning(
                "[WF-CEILING] DENY contact {ContactId}: the workforce role is unavailable ({Reason}).",
                principal.ContactId, ceiling?.UnavailableReason ?? "no_answer");
            return ProblemDetailsHelper.Forbidden(
                DenyBaselineUnavailable, "Access for this account cannot be determined right now.");
        }

        var missing = required.Where(r => !ceiling.Holds(r.Table, r.Rights)).ToList();
        if (required.Count == 0 || missing.Count > 0)
        {
            logger.LogWarning(
                "[WF-CEILING] DENY contact {ContactId}: the workforce role lacks [{Missing}].",
                principal.ContactId, string.Join(", ", missing.Select(m => $"{m.Rights} {m.Table}")));
            return ProblemDetailsHelper.Forbidden(
                DenyBaselinePrivilegeMissing, "Your account's access does not include this action.");
        }

        return null;
    }
}
