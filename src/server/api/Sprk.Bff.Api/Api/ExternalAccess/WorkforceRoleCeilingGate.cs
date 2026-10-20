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
/// <para><b>The route map</b> (design note §0, owner round 101; review round 1 F1 extended it to every workforce-reachable
/// <c>/api/v1/external</c> route) is the requirement sets below. Each route DECLARES its set as endpoint metadata
/// (<see cref="WorkforceCeilingEndpointExtensions.RequireWorkforceCeiling{TBuilder}(TBuilder, IReadOnlyList{WorkforceCeilingRequirement})"/>),
/// and the one <see cref="WorkforceCeilingEndpointFilter"/> on the group calls <see cref="DenyAsync"/> before the handler.
/// Three surfaces decide in the handler, through this gate, because the table is known only there: the module-data seam
/// (Read on the requested module's table), the notification feed (items filtered to the tables the role can Read) and
/// <c>/me/entitlements</c> (module codes intersected with the ceiling).</para>
/// <para>CLAUDE.md §11. <b>Existing:</b> <see cref="IDataversePrivilegeChecker"/> (the ceiling read is added there, not
/// here) and <see cref="ProblemDetailsHelper"/>. <b>Extension:</b> the gate is endpoint glue only (who it applies to, the
/// route map, the 403 shape); it reads nothing itself, and putting the route map in the Dataverse service would couple that
/// service to <see cref="CallerPrincipal"/> and the external routes. <b>Cost of doing nothing:</b> the endpoint filter,
/// the module-data seam, the feed and the entitlements would each repeat the plane test and the two deny shapes.</para>
/// </remarks>
internal static class WorkforceRoleCeilingGate
{
    /// <summary>The Workforce role is missing, ambiguous or could not be read: every workforce-contact operation is denied.</summary>
    internal const string DenyBaselineUnavailable = "sdap.access.deny.workforce_baseline_unavailable";

    /// <summary>The Workforce role does not hold a privilege this operation needs.</summary>
    internal const string DenyBaselinePrivilegeMissing = "sdap.access.deny.workforce_baseline_privilege_missing";

    internal const string ServiceRequestTable = "sprk_servicerequest";
    internal const string DocumentTable = "sprk_document";
    internal const string ProjectTable = "sprk_project";
    internal const string MatterTable = "sprk_matter";
    internal const string WorkAssignmentTable = "sprk_workassignment";
    internal const string InvoiceTable = "sprk_invoice";
    internal const string TodoTable = "sprk_todo";
    internal const string EventTable = "sprk_event";
    internal const string ContactTable = "contact";
    internal const string AccountTable = "account";
    internal const string CommunicationTable = "sprk_communication";
    internal const string GrantTable = "sprk_externalrecordaccess";
    internal const string InvoiceSubmissionTable = "sprk_invoicesubmission";

    // ── The route map (design note §0, owner round 101; review round 1 F1: every workforce-reachable route) ──────────
    // Each set is derived from what the route reads or writes: a read needs Read on every table whose rows it returns (the
    // route's record and the child rows); a create needs Create on the new row and AppendTo on the business record it is
    // filed under (the owner's convention for the service-request upload); an update needs Write; a delete, Delete. The
    // routes name their set with RequireWorkforceCeiling (WorkforceCeilingEndpointFilter.cs).

    /// <summary><c>POST /servicerequests</c> (intake): Create on the service request.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> SubmitServiceRequest = Set(
        (ServiceRequestTable, AccessRights.Create));

    /// <summary>Reading own service requests through the module-data seam (<c>/api/dataverse/fetch</c>, <c>/record</c>).</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadServiceRequests = Set(
        (ServiceRequestTable, AccessRights.Read));

    /// <summary><c>GET /servicerequests/{id}/documents</c>, its <c>/content</c> and <c>/versions</c>.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadServiceRequestDocuments =
        ReadDocumentsOf(ServiceRequestTable);

    /// <summary><c>POST /servicerequests/{id}/documents</c> (upload): Create the document, AppendTo the request.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> UploadServiceRequestDocument =
        UploadDocumentTo(ServiceRequestTable);

    /// <summary><c>DELETE /servicerequests/{id}</c> (delete own): Delete on the service request.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> DeleteServiceRequest = Set(
        (ServiceRequestTable, AccessRights.Delete));

    /// <summary><c>GET /projects</c> and <c>GET /projects/{id}</c>: Read on the project.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadProjects = Set((ProjectTable, AccessRights.Read));

    /// <summary><c>GET /projects/{id}/contacts</c>: the project and the contacts it returns.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadProjectContacts = Set(
        (ProjectTable, AccessRights.Read), (ContactTable, AccessRights.Read));

    /// <summary><c>GET /projects/{id}/organizations</c>: the project, its contacts (read to find the accounts) and the accounts.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadProjectOrganizations = Set(
        (ProjectTable, AccessRights.Read), (ContactTable, AccessRights.Read), (AccountTable, AccessRights.Read));

    /// <summary>Documents of a record (list, <c>/content</c>, <c>/versions</c>): Read on the record and on the document.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadProjectDocuments = ReadDocumentsOf(ProjectTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadMatterDocuments = ReadDocumentsOf(MatterTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadWorkAssignmentDocuments = ReadDocumentsOf(WorkAssignmentTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadInvoiceDocuments = ReadDocumentsOf(InvoiceTable);

    /// <summary>A document upload to a record: Create on the document, AppendTo on the record (its parent lookup).</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> UploadProjectDocument = UploadDocumentTo(ProjectTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> UploadMatterDocument = UploadDocumentTo(MatterTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> UploadWorkAssignmentDocument = UploadDocumentTo(WorkAssignmentTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> UploadInvoiceDocument = UploadDocumentTo(InvoiceTable);

    /// <summary>To-dos regarding a root: Read on the root and on <c>sprk_todo</c>; a create is Create + AppendTo the root.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadProjectTodos = ReadChildrenOf(ProjectTable, TodoTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadMatterTodos = ReadChildrenOf(MatterTable, TodoTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadWorkAssignmentTodos = ReadChildrenOf(WorkAssignmentTable, TodoTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> CreateProjectTodo = CreateChildUnder(ProjectTable, TodoTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> CreateMatterTodo = CreateChildUnder(MatterTable, TodoTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> CreateWorkAssignmentTodo = CreateChildUnder(WorkAssignmentTable, TodoTable);

    /// <summary>Calendar events regarding a root: Read on the root and on <c>sprk_event</c>; the project create is Create + AppendTo.</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadProjectEvents = ReadChildrenOf(ProjectTable, EventTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadMatterEvents = ReadChildrenOf(MatterTable, EventTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadWorkAssignmentEvents = ReadChildrenOf(WorkAssignmentTable, EventTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> CreateProjectEvent = CreateChildUnder(ProjectTable, EventTable);

    /// <summary><c>PATCH /todos/{id}</c>: Write on the to-do (its root is read only to gate it; nothing of it is returned).</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> UpdateTodo = Set((TodoTable, AccessRights.Write));

    /// <summary><c>DELETE /documents/{id}</c> (delete own; the SPE file goes with the row).</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> DeleteDocument = Set((DocumentTable, AccessRights.Delete));

    /// <summary><c>DELETE /todos/{id}</c> (delete own).</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> DeleteTodo = Set((TodoTable, AccessRights.Delete));

    /// <summary><c>DELETE /events/{id}</c> (delete own).</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> DeleteEvent = Set((EventTable, AccessRights.Delete));

    /// <summary>
    /// <c>GET /contact-grants</c>: the grant rows the caller issued on a record, each with the grantee contact's name and
    /// email (review round 2 F1: the response reads <c>contact</c> too).
    /// </summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ListIssuedGrants = Set(
        (GrantTable, AccessRights.Read), (ContactTable, AccessRights.Read));

    /// <summary><c>POST /contact-grants/revoke</c>: the grant rows are deactivated (an update).</summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> RevokeIssuedGrant = Set((GrantTable, AccessRights.Write));

    /// <summary>
    /// <c>POST /contact-grants</c>: the grant row is created, or the colleague's existing row is renewed in place
    /// (<c>GrantExternalAccessEndpoint.CreateGrantAsync</c>), and it is filed under the record, so AppendTo on the record's
    /// table (<paramref name="recordType"/>, from the request). The handler also reads the colleague's <c>contact</c> row (by
    /// id or by email) and names the colleague by that row's email in its response label and its <c>managed_elsewhere</c> 409
    /// detail, so the set carries Read on <c>contact</c> (review round 3 F1). An unparseable record type yields
    /// <see langword="null"/>: the handler refuses it with its own 400 before anything is read or written.
    /// </summary>
    internal static IReadOnlyList<WorkforceCeilingRequirement>? GrantAsContact(string? recordType) =>
        ExternalGrantRoot.TryParse(recordType, out var root)
            ? Set(
                (GrantTable, AccessRights.Create | AccessRights.Write),
                (ExternalGrantRoot.LogicalNameFor(root), AccessRights.AppendTo),
                (ContactTable, AccessRights.Read))
            : null;

    /// <summary><c>GET /{root}/{id}/messages</c>: the root record and the messages (<c>sprk_communication</c>) regarding it.</summary>
    internal static IReadOnlyList<WorkforceCeilingRequirement> ReadMessagesOn(string rootTable) =>
        Set((rootTable, AccessRights.Read), (CommunicationTable, AccessRights.Read));

    /// <summary>
    /// <c>POST /{root}/{id}/messages</c>: Read on the root (the post is gated on Read and returns the message), AppendTo on
    /// the root it is filed regarding, and Create on <c>sprk_communication</c>.
    /// </summary>
    internal static IReadOnlyList<WorkforceCeilingRequirement> PostMessageOn(string rootTable) =>
        Set((rootTable, AccessRights.Read | AccessRights.AppendTo), (CommunicationTable, AccessRights.Create));

    /// <summary>
    /// <c>POST /{matters|projects|workassignments}/{id}/invoice-submissions</c> (ebilling-r1 task 044): one set per core table
    /// (<paramref name="coreTable"/>). Create on the new <c>sprk_invoicesubmission</c> row; the submission is filed under the
    /// document and under the core record (<c>SecureChildLineage</c>), so Create + AppendTo on <c>sprk_document</c> (the route
    /// creates the PDF's document row) and AppendTo on the core table. No AppendTo on <c>sprk_invoicesubmission</c>: nothing
    /// points at it.
    /// <para>
    /// There is deliberately NO Write on <c>sprk_document</c>. <c>InvoiceSubmissionService.CompleteDocumentRowAsync</c> updates
    /// the document's type and MIME type, but that update only finishes the server's own create, no upload set has Write, and
    /// the dev Workforce role has no Write on <c>sprk_document</c>, so requiring it would refuse every submit.
    /// </para>
    /// <para>
    /// Known limit: the ceiling filter runs on the group, before the route's <c>RequireEbillingEnabled</c>. With the flag off, a
    /// workforce contact whose role lacks one of these privileges gets the ceiling's 403 instead of the flag's answer. The order
    /// is not changed.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<WorkforceCeilingRequirement> SubmitInvoiceTo(string coreTable) =>
        Set((InvoiceSubmissionTable, AccessRights.Create),
            (DocumentTable, AccessRights.Create | AccessRights.AppendTo),
            (coreTable, AccessRights.AppendTo));

    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> SubmitInvoiceToMatter = SubmitInvoiceTo(MatterTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> SubmitInvoiceToProject = SubmitInvoiceTo(ProjectTable);
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> SubmitInvoiceToWorkAssignment = SubmitInvoiceTo(WorkAssignmentTable);

    /// <summary>
    /// <c>GET /invoice-submissions/{id}</c> and <c>GET /invoice-submissions</c>: Read on <c>sprk_invoicesubmission</c>. The view
    /// returns only the ids of the document and the core record, so neither table is read for the caller.
    /// </summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ReadInvoiceSubmissions = Set(
        (InvoiceSubmissionTable, AccessRights.Read));

    /// <summary>
    /// <c>GET /invoice-submissions/firm-records</c>: Read on the three core tables whose ids it returns, and Create on
    /// <c>sprk_invoicesubmission</c>. The handler offers only the records the caller can submit on, so this mirrors
    /// <c>/me/entitlements</c> offering a service-request type only when the role holds Create on it.
    /// </summary>
    internal static readonly IReadOnlyList<WorkforceCeilingRequirement> ListInvoiceFirmRecords = Set(
        (MatterTable, AccessRights.Read), (ProjectTable, AccessRights.Read), (WorkAssignmentTable, AccessRights.Read),
        (InvoiceSubmissionTable, AccessRights.Create));

    /// <summary>
    /// The table whose rows a <c>/me/entitlements</c> module code shows (review round 1 F2; design note §5: "never shows a
    /// tab the role forbids"). A code missing here shows no record data the BFF can name (for example <c>policy-library</c>,
    /// a placeholder tab) and is not intersected.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> ModuleTables =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ModuleEntitlementResolver.LegalFrontDoorModule] = ServiceRequestTable,
            ["assigned-work"] = WorkAssignmentTable,
        };

    private static IReadOnlyList<WorkforceCeilingRequirement> ReadDocumentsOf(string parentTable) =>
        Set((parentTable, AccessRights.Read), (DocumentTable, AccessRights.Read));

    private static IReadOnlyList<WorkforceCeilingRequirement> UploadDocumentTo(string parentTable) =>
        Set((DocumentTable, AccessRights.Create), (parentTable, AccessRights.AppendTo));

    private static IReadOnlyList<WorkforceCeilingRequirement> ReadChildrenOf(string rootTable, string childTable) =>
        Set((rootTable, AccessRights.Read), (childTable, AccessRights.Read));

    private static IReadOnlyList<WorkforceCeilingRequirement> CreateChildUnder(string rootTable, string childTable) =>
        Set((childTable, AccessRights.Create), (rootTable, AccessRights.AppendTo));

    private static IReadOnlyList<WorkforceCeilingRequirement> Set(params (string Table, AccessRights Rights)[] entries) =>
        entries.Select(e => new WorkforceCeilingRequirement(e.Table, e.Rights)).ToArray();

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

    /// <summary>
    /// The ceiling a caller is held to: <see langword="null"/> when the ceiling does not apply (a systemuser, a CIAM partner),
    /// otherwise the role read, where a checker that answers nothing is an unavailable role (fail closed). For the surfaces
    /// that FILTER rather than refuse (the notification feed, <c>/me/entitlements</c>).
    /// </summary>
    internal static async Task<WorkforceRoleCeiling?> CeilingForAsync(
        CallerPrincipal principal, IDataversePrivilegeChecker privileges, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(privileges);

        if (!Applies(principal))
            return null;

        return await privileges.GetWorkforceRoleCeilingAsync(ct).ConfigureAwait(false)
               ?? WorkforceRoleCeiling.Unavailable("no_answer");
    }

    /// <summary>
    /// True when <paramref name="ceiling"/> (from <see cref="CeilingForAsync"/>) lets the caller read rows of
    /// <paramref name="table"/>: no ceiling (it does not apply), or an available role that holds Read at any depth. An
    /// unavailable role reads nothing.
    /// </summary>
    internal static bool CanRead(WorkforceRoleCeiling? ceiling, string table) =>
        ceiling is null || (ceiling.IsAvailable && ceiling.Holds(table, AccessRights.Read));

    /// <summary>The module-data seam's requirement for a module: Read on the table it returns rows of.</summary>
    internal static IReadOnlyList<WorkforceCeilingRequirement> ReadModuleRecords(string recordEntity) =>
        Set((recordEntity, AccessRights.Read));
}
