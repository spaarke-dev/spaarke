using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// USER-CONTEXT (OBO) document version-history endpoints — spaarkeai-compose-r6 task 050
/// (spec FR-07 / Success Criterion 4: render-on-save's "version history is the safety net",
/// made reachable from the product).
///
/// Two READ-ONLY routes on the OBO SPE/Documents surface:
///   - GET /api/obo/drives/{driveId}/items/{itemId}/versions
///       Lists the item's SPE versions (id/label, lastModified timestamp, size) as
///       <see cref="VersionInfoDto"/> projections, newest first.
///   - GET /api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content
///       Streams the EXACT bytes of the named prior version.
///
/// Auth model (ADR-028): BOTH routes resolve the item and its versions entirely through the
/// CALLING USER's On-Behalf-Of token (`IGraphClientFactory.ForUserAsync` beneath the
/// <see cref="ISpeFileOperations"/> facade) — NEVER app-only elevation. Per-document
/// authorization is enforced by SharePoint Embedded itself under the user's delegated
/// permission: a caller not authorized for the item gets 403/404 from the SPE layer
/// (surfaced here as 403/404), never the bytes. This is deliberately NOT the admin
/// version-list surface (`ContainerItemEndpoints.cs` — app-only, config-scoped); do not
/// fold these routes into it.
///
/// SCOPE (binding, per task 050): open/read-only ONLY. No restore, no branch-from, no
/// version-state mutation of any kind is mapped here.
///
/// ADR-007: no Microsoft.Graph type appears here — the facade returns
/// <see cref="VersionInfoDto"/> / <see cref="Stream"/> only.
/// ADR-008: RequireAuthorization() on every route.
/// Registration symmetry (bff-extensions.md §F.1): mapped UNCONDITIONALLY from
/// EndpointMappingExtensions.MapDomainEndpoints; the backing ISpeFileOperations facade is
/// registered unconditionally in DocumentsModule.
/// </summary>
public static class DocumentVersionEndpoints
{
    public static IEndpointRouteBuilder MapDocumentVersionEndpoints(this IEndpointRouteBuilder app)
    {
        // GET: list an item's version history (as user)
        app.MapGet("/api/obo/drives/{driveId}/items/{itemId}/versions", async (
            string driveId,
            string itemId,
            HttpContext ctx,
            [FromServices] ISpeFileOperations speFileStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return ProblemDetailsHelper.ValidationError("itemId is required");
            }

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
                // The user's OBO token was not authorized for this item at the SPE layer.
                // 403 with NO version metadata — the SPE boundary IS the authorization check.
                return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied");
            }
            catch (SpaarkeStorageException ex)
            {
                return ex.ToProblemDetails();
            }
        })
        .RequireAuthorization()
        .RequireRateLimiting("graph-read")
        .WithTags("Documents")
        .WithName("ListDocumentVersionsAsUser")
        .WithSummary("List a document's SPE version history under the calling user's own (OBO) permission");

        // GET: stream the exact bytes of a specific PRIOR version, read-only (as user)
        app.MapGet("/api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content", async (
            string driveId,
            string itemId,
            string versionId,
            HttpContext ctx,
            [FromServices] ISpeFileOperations speFileStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(versionId))
            {
                return ProblemDetailsHelper.ValidationError("itemId and versionId are required");
            }

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
                // 403 and NEVER the bytes — enforced through the user's OBO token at the SPE layer.
                return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied");
            }
            catch (SpaarkeStorageException ex)
            {
                return ex.ToProblemDetails();
            }
        })
        .RequireAuthorization()
        .RequireRateLimiting("graph-read")
        .WithTags("Documents")
        .WithName("OpenPriorDocumentVersionAsUser")
        .WithSummary("Open (stream) the exact bytes of a specific prior SPE version, read-only, under the calling user's own (OBO) permission");

        return app;
    }
}
