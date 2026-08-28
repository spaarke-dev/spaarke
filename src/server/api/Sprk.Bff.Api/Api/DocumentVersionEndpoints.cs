using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// USER-CONTEXT (OBO) document version-history endpoints — originally spaarkeai-compose-r6 task 050
/// (spec FR-07 / Success Criterion 4: render-on-save's "version history is the safety net").
///
/// Two READ-ONLY routes, both keyed by the <c>sprk_document</c> ROW and both gated per-document:
///   - GET /api/documents/{documentId}/versions
///       Lists the document's SPE versions (id/label, lastModified timestamp, size) as
///       <see cref="VersionInfoDto"/> projections, newest first.
///   - GET /api/documents/{documentId}/versions/{versionId}/content
///       Streams the EXACT bytes of the named prior version.
///
/// ════════════════════════════════════════════════════════════════════════════════════════════════
/// unified-access-control-r2 task 079 — WHY THIS FILE WAS RESHAPED (read before adding a route here)
/// ════════════════════════════════════════════════════════════════════════════════════════════════
///
/// These two routes used to be keyed by <c>(driveId, itemId)</c>:
///
///     GET /api/obo/drives/{driveId}/items/{itemId}/versions                      -- DELETED
///     GET /api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content  -- DELETED
///
/// carrying only <c>RequireAuthorization()</c> ("are you anyone?") and rate limiting. The header this
/// text replaces asserted that *"per-document authorization is enforced by SharePoint Embedded itself
/// under the user's delegated permission"*. Under the broker-only decision
/// (<c>SECURE-DOCUMENTS-BUILD-PLAN.md</c> §1) that claim is the bypass-by-construction pattern, not a
/// control: SPE permission is CONTAINER-scoped and coarser than per-document Dataverse rights, so any
/// caller holding a container ACL could read the version history — and the PRIOR-VERSION BYTES — of
/// every document in that container, including a secure matter's, with no per-document check. Prior
/// versions are exactly as disclosing as current content, and frequently contain material later
/// redacted from the current version.
///
/// Task 071 DELETED the four sibling drive-keyed OBO routes in <c>OBOEndpoints.cs</c>. It could not
/// delete these two because they have a LIVE caller. So they were re-keyed instead, which fixes the
/// defect at its root rather than patching it:
///
///   • THE RESOURCE DOMAIN IS NOW CORRECT BY CONSTRUCTION. <c>DocumentAuthorizationFilter</c>
///     authorizes an <c>sprk_document</c> ROW. Its <c>ExtractResourceId</c> treats
///     <c>containerId</c>/<c>driveId</c>/<c>documentId</c>/<c>id</c> interchangeably, so bolting the
///     filter onto a DRIVE-keyed route hands a drive id to <c>RetrievePrincipalAccess</c>, which
///     answers None — denying 100% of callers, legitimate ones included. That is fail-closed but it
///     is not authorization; it is a broken route. It is also the exact "wrong resource domain" trap
///     recorded against the task-073 waivers. Keying on <c>{documentId}</c> removes the mismatch
///     instead of working around it.
///
///   • THE SPE POINTER IS NOW SERVER-DERIVED. The caller no longer NAMES the drive/item to read;
///     it names a document row, and the drive/item are read off that row after the caller has been
///     authorized for it. A caller therefore cannot address an arbitrary SPE item at all — which is
///     strictly stronger than authorizing a caller-supplied pair, and it closes a second hole the
///     drive-keyed shape had even when gated: the only unique index available for a
///     <c>(driveId, itemId)</c> → document lookup is <c>sprk_graphitemid_uk</c>, which is keyed on
///     the ITEM alone, leaving the supplied <c>driveId</c> unvalidated.
///
///   • IT REMOVES THE STANDING INVITATION. A drive-keyed route — even a gated one — keeps inviting
///     "why not just grant the user container access?", the question broker-only exists to foreclose.
///     Task 071's reasoning for preferring deletion over gating applies unchanged.
///
/// DO NOT re-add a drive- or container-keyed version route. If a caller has a <c>(driveId, itemId)</c>
/// pair and no document id, that is a modelling gap to escalate (task 071 §4), not a route to add.
///
/// Operation is <c>"read"</c> on BOTH routes, including the byte stream. This is PARITY with the
/// current-version download on this same group — <c>GET /api/documents/{documentId}/download</c> and
/// <c>/content</c> are both <c>"read"</c> — and parity is the correct calibration: a prior version is
/// the same confidential content as the current one, so gating history MORE strictly than the current
/// bytes would deny legitimate readers while stopping no attacker, who would simply take the current
/// version instead. (The <c>driveitem.content.download</c>/<c>download_file</c> = Write entries in
/// <c>OperationAccessPolicy</c> belong to the legacy SPE-RESOURCE family, which describes an SPE item
/// rather than a Dataverse row; the record-scoped surface deliberately uses the bare <c>"read"</c>
/// key — see the task-003 and task-072 rationale on those entries.) No new operation key was added,
/// because a new key carrying the same required right changes no decision (CLAUDE.md §11).
///
/// Auth model: the per-document Dataverse decision is the boundary and runs BEFORE any Graph call.
/// The SPE read itself remains OBO (<c>IGraphClientFactory.ForUserAsync</c> beneath the
/// <see cref="ISpeFileOperations"/> facade) — never app-only elevation — so SPE's own answer stays in
/// place behind the gate as defence in depth. This is deliberately NOT the admin version surface
/// (<c>ContainerItemEndpoints.cs</c> — app-only, config-scoped); do not fold these routes into it.
///
/// SCOPE (binding, per task 050): open/read-only ONLY. No restore, no branch-from, no version-state
/// mutation of any kind is mapped here.
///
/// ADR-007: no Microsoft.Graph type appears here — the facade returns
/// <see cref="VersionInfoDto"/> / <see cref="Stream"/> only.
/// ADR-008: per-resource endpoint filter on every route.
/// Registration symmetry (bff-extensions.md §F.1): mapped UNCONDITIONALLY from
/// EndpointMappingExtensions.MapDomainEndpoints; the backing ISpeFileOperations facade and
/// IDocumentDataverseService are both registered unconditionally (DocumentsModule / DataverseModule).
/// </summary>
public static class DocumentVersionEndpoints
{
    public static IEndpointRouteBuilder MapDocumentVersionEndpoints(this IEndpointRouteBuilder app)
    {
        var docs = app.MapGroup("/api/documents").RequireAuthorization();

        // GET: list a document's version history (per-document gate, then OBO read)
        docs.MapGet("/{documentId}/versions", async (
            string documentId,
            HttpContext ctx,
            [FromServices] IDocumentDataverseService dataverseService,
            [FromServices] ISpeFileOperations speFileStore,
            CancellationToken ct) =>
        {
            var (driveId, itemId) = await ResolveSpePointerAsync(documentId, dataverseService, ct);

            try
            {
                var versions = await GraphCallScope.Run(
                    () => speFileStore.ListFileVersionsAsUserAsync(ctx, driveId, itemId, ct),
                    "obo.versions.list");

                return versions == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(versions);
            }
            catch (UnauthorizedAccessException)
            {
                // SPE also refused under the caller's delegated permission. Defence in depth behind
                // the per-document gate above — no longer the boundary, but still honoured.
                return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied");
            }
            catch (SpaarkeStorageException ex)
            {
                return ex.ToProblemDetails();
            }
        })
        .AddDocumentAuthorizationFilter("read")
        .RequireRateLimiting("graph-read")
        .WithTags("Documents")
        .WithName("ListDocumentVersionsAsUser")
        .WithSummary("List a document's SPE version history. Requires Read on the document; the SPE "
                   + "read then runs under the calling user's own (OBO) permission.")
        .Produces<IReadOnlyList<VersionInfoDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        // GET: stream the exact bytes of a specific PRIOR version, read-only
        docs.MapGet("/{documentId}/versions/{versionId}/content", async (
            string documentId,
            string versionId,
            HttpContext ctx,
            [FromServices] IDocumentDataverseService dataverseService,
            [FromServices] ISpeFileOperations speFileStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(versionId))
            {
                return ProblemDetailsHelper.ValidationError("versionId is required");
            }

            var (driveId, itemId) = await ResolveSpePointerAsync(documentId, dataverseService, ct);

            try
            {
                var stream = await GraphCallScope.Run(
                    () => speFileStore.DownloadFileVersionAsUserAsync(ctx, driveId, itemId, versionId, ct),
                    "obo.versions.download");

                return stream == null
                    ? TypedResults.NotFound()
                    : TypedResults.Stream(stream, "application/octet-stream");
            }
            catch (UnauthorizedAccessException)
            {
                // 403 and NEVER the bytes.
                return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied");
            }
            catch (SpaarkeStorageException ex)
            {
                return ex.ToProblemDetails();
            }
        })
        .AddDocumentAuthorizationFilter("read")
        .RequireRateLimiting("graph-read")
        .WithTags("Documents")
        .WithName("OpenPriorDocumentVersionAsUser")
        .WithSummary("Open (stream) the exact bytes of a specific prior SPE version, read-only. "
                   + "Requires Read on the document — the same gate as the current-version download, "
                   + "because a prior version is the same confidential content.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// Resolves the authorized document's OWN SPE pointer. Runs AFTER
    /// <c>DocumentAuthorizationFilter</c>, so by the time this executes the caller is known to hold
    /// Read on <paramref name="documentId"/> — this method's job is only to find the bytes that row
    /// points at, never to make an access decision.
    /// </summary>
    /// <remarks>
    /// Fails closed on every path: an unparseable id, a missing row, or an unusable pointer all
    /// throw before any Graph call, and there is NO fallback to a caller-supplied drive/item or to
    /// container permission (ADR-003; task 079 constraint).
    ///
    /// The error codes deliberately match <c>FileAccessEndpoints.ValidateSpePointers</c> so the two
    /// routes in this file report pointer problems with the SAME contract as their eight siblings on
    /// the <c>/api/documents</c> group. That helper is <c>private static</c> in another file; hoisting
    /// it into a shared utility would edit a hot shared surface mid-wave for no behavioural gain, so
    /// the checks this route actually needs are asserted here and the divergence risk is bounded by
    /// the shared error codes.
    /// </remarks>
    private static async Task<(string DriveId, string ItemId)> ResolveSpePointerAsync(
        string documentId,
        IDocumentDataverseService dataverseService,
        CancellationToken ct)
    {
        if (!Guid.TryParse(documentId, out _))
        {
            throw new SdapProblemException(
                "invalid_id",
                "Invalid Document ID",
                $"Document ID '{documentId}' is not a valid GUID format",
                400);
        }

        var document = await dataverseService.GetDocumentAsync(documentId, ct);
        if (document == null)
        {
            throw new SdapProblemException(
                "document_not_found",
                "Document Not Found",
                $"Document with ID '{documentId}' does not exist",
                404);
        }

        if (string.IsNullOrWhiteSpace(document.GraphDriveId) || string.IsNullOrWhiteSpace(document.GraphItemId))
        {
            throw new SdapProblemException(
                document.HasFile ? "mapping_missing_drive" : "no_file_attached",
                document.HasFile ? "SPE Pointer Missing" : "No File Attached",
                document.HasFile
                    ? $"Document {documentId} is marked as having a file but its Graph drive/item id is empty. "
                      + "The upload may still be in progress or did not complete successfully."
                    : $"Document {documentId} has no file attached yet, so it has no version history.",
                409);
        }

        // SharePoint Embedded drive ids always start with "b!". A value that does not is a data
        // defect, and guessing at it would mean issuing a Graph call against an unknown drive.
        if (!document.GraphDriveId.StartsWith("b!", StringComparison.Ordinal))
        {
            throw new SdapProblemException(
                "invalid_drive_id",
                "Invalid SPE Drive ID Format",
                $"Drive ID '{document.GraphDriveId}' does not start with 'b!' (expected SharePoint Embedded container format)",
                400);
        }

        return (document.GraphDriveId, document.GraphItemId);
    }
}
