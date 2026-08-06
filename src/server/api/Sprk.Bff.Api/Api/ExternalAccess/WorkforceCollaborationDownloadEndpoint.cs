// teams-app-r1 Task 030 (2026-08-04) — Broker-only workforce collaboration document download
// (authz-before-stream, ALL principal planes).
//
// GET /api/v1/collab/projects/{projectId}/documents/{documentId}/content
//
// The workforce-plane analog of the CIAM external download (ExternalProjectDataEndpoints
// .DownloadDocumentContent). Serves a Secure Project document's bytes to a workforce-authenticated
// collaboration caller — a systemuser, a contact-with-grant, or a contact-with-standing-grant — with
// the SAME broker-only, authz-before-stream, no-Graph-pointer guarantees, for EVERY principal:
//
//   • authz-before-stream (highest-consequence property) — project-level access
//     (record ∈ accessible(principal), task 022) is enforced by the AddAccessibleRecordSetAuthorization
//     Filter ENDPOINT FILTER, which runs to completion BEFORE this handler executes. The handler then
//     enforces document→project scoping BEFORE any SPE pointer resolution or Graph read. A denied
//     request returns 403 with NO bytes and NO Graph call — never a partial stream then abort.
//   • broker-only (ADR-028 A2 / NFR-02) — bytes stream via the app-only
//     SpeFileStore.DownloadFileAsync (ISpeFileOperations), NEVER the OBO DownloadFileAsUserAsync path,
//     for EVERY principal — including a workforce systemuser who holds their own SPE permissions. The
//     caller's token authenticates to the BFF only; it is never exchanged downstream.
//   • no Graph pointer — driveId/itemId are resolved server-side and NEVER surfaced to the client in
//     ANY response (success or error). The download name is the document's display name (or the
//     opaque document GUID), never a Graph pointer.
//
// ADR-007: all SPE access via the SpeFileStore facade (ISpeFileOperations) — no Graph SDK types here.
// ADR-008: authorization is applied by per-endpoint filters, not global middleware.
// ADR-019: ProblemDetails on every non-2xx.

using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Handler for <c>GET /api/v1/collab/projects/{projectId}/documents/{documentId}/content</c> — the
/// broker-only, authz-before-stream download gate for the workforce collaboration surface.
/// </summary>
public static class WorkforceCollaborationDownloadEndpoint
{
    /// <summary>Deny code (auth.md <c>{domain}.{area}.{action}.{reason}</c>) for a document that is
    /// not part of the requested (already-authorized) project.</summary>
    public const string DenyDocumentNotInProject = "sdap.access.deny.document_not_in_project";

    /// <summary>
    /// Streams a Secure Project document's content to an authorized workforce collaboration caller.
    ///
    /// <para><b>Authz-before-stream.</b> By the time this handler runs, the endpoint filters have
    /// already (1) resolved the caller to a <see cref="WorkforcePrincipal"/> and (2) enforced
    /// <c>project ∈ accessible(principal)</c> — the accessible-record-set gate (task 022). This
    /// handler adds the final authorization step — document→project scoping — and only THEN resolves
    /// SPE pointers and streams app-only. Nothing below reads SPE/Graph until scoping passes.</para>
    /// </summary>
    public static async Task<IResult> DownloadDocumentContent(
        Guid projectId,
        Guid documentId,
        HttpContext httpContext,
        ExternalDataService dataService,
        IDocumentStorageResolver storageResolver,
        ISpeFileOperations fileStore,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // The WorkforceCallerAuthorizationFilter (task 020) + AccessibleRecordSetAuthorizationFilter
        // (task 022) run BEFORE this handler and short-circuit an unresolved/unauthorized caller.
        // Reaching here without a resolved principal is a pipeline misconfiguration — fail closed.
        if (httpContext.Items[WorkforcePrincipal.HttpContextItemsKey] is not WorkforcePrincipal principal)
        {
            logger.LogError(
                "[WF-DOWNLOAD] No resolved workforce principal on the request — ensure " +
                "AddWorkforceCallerAuthorizationFilter() + AddAccessibleRecordSetAuthorizationFilter() " +
                "are applied. Failing closed (no bytes served).");
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Authorization context not available for this request.");
        }

        // ── AUTHORIZATION (document→project scoping) — BEFORE any SPE pointer resolution ──
        // The accessible-record-set filter already proved project ∈ accessible(principal). Here we
        // prove the requested document belongs to THAT project — otherwise an authorized member of
        // project A could pass projectId=A with a documentId belonging to project B. A mismatch OR a
        // non-existent document is a uniform 403 (do not leak document existence). This is an app-only
        // Dataverse authorization read — it resolves NO Graph pointer.
        var (documentProjectId, documentName) = await dataService
            .GetDocumentProjectAndNameAsync(documentId, ct)
            .ConfigureAwait(false);

        if (documentProjectId is null || documentProjectId.Value != projectId)
        {
            logger.LogWarning(
                "[WF-DOWNLOAD] DENY {Kind} (systemuser={SystemUserId}, contact={ContactId}) — document " +
                "{DocumentId} not in project {ProjectId}.",
                principal.Kind, principal.SystemUserId, principal.ContactId, documentId, projectId);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "This document is not part of the requested project.",
                extensions: new Dictionary<string, object?> { ["reasonCode"] = DenyDocumentNotInProject });
        }

        // ── AUTHORIZED — only now resolve SPE pointers (server-side) and stream APP-ONLY ──
        try
        {
            // Graph pointers are resolved here and stay server-side — they are NEVER returned to the
            // client (success or error) so the client can never bypass the broker on a later call.
            var (driveId, itemId) = await storageResolver
                .GetSpePointersAsync(documentId, ct)
                .ConfigureAwait(false);

            // App-only broker download (ADR-028 A2 / NFR-02). MUST NOT be the OBO
            // DownloadFileAsUserAsync path — app-only even for a workforce systemuser principal.
            var stream = await fileStore.DownloadFileAsync(driveId, itemId, ct).ConfigureAwait(false);
            if (stream is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: "Document content is not available.");
            }

            logger.LogInformation(
                "[WF-DOWNLOAD] ALLOW {Kind} — downloaded document {DocumentId} from project {ProjectId} " +
                "(app-only broker).",
                principal.Kind, documentId, projectId);

            // application/octet-stream + attachment: force download of collaboration content (no inline
            // render). The download name is the document's display name (or the opaque GUID) — never a
            // Graph pointer.
            var downloadName = string.IsNullOrWhiteSpace(documentName) ? documentId.ToString() : documentName;
            return Results.File(stream, "application/octet-stream", fileDownloadName: downloadName);
        }
        catch (SdapProblemException ex)
        {
            // Post-authorization storage-state problems (document_not_found / mapping_missing_*) —
            // surface the resolver's stable code + status. No driveId/itemId is included.
            return Results.Problem(
                statusCode: ex.StatusCode, title: ex.Title, detail: ex.Detail,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
    }
}
