using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// The Compose document a request was AUTHORIZED for against Dataverse — unified-access-control-r2 task 171 (owner round
/// 69, broker-only). Set by <c>ComposeDocumentAuthorizationFilter</c> after it found the <c>sprk_document</c> row whose
/// <c>sprk_graphitemid</c> is the route's <c>{documentSpeId}</c>, authorized the caller on that row
/// (<c>DocumentAuthorizationFilter</c> semantics) and verified the row's pointer. It names the ROW's drive and item —
/// never a client-supplied value.
/// </summary>
/// <param name="DocumentId">The authorized <c>sprk_document</c>.</param>
/// <param name="DriveId">The row's <c>sprk_graphdriveid</c>.</param>
/// <param name="ItemId">The row's <c>sprk_graphitemid</c>.</param>
public sealed record ComposeBrokeredDocument(Guid DocumentId, string DriveId, string ItemId)
{
    /// <summary>The <see cref="HttpContext.Items"/> key the filter stores the authorized document under.</summary>
    public const string ItemKey = "sprk.compose.brokered-document";

    /// <summary>The authorized document of this request, if the filter found and authorized one.</summary>
    public static ComposeBrokeredDocument? From(HttpContext? httpContext)
        => httpContext?.Items.TryGetValue(ItemKey, out var value) == true ? value as ComposeBrokeredDocument : null;

    /// <summary>
    /// Does this request's authorization cover EXACTLY (<paramref name="driveId"/>, <paramref name="itemId"/>)? Only then
    /// may a Compose byte call run app-only; anything else keeps the caller's own (OBO) identity, so SPE still decides
    /// for a drive item no Dataverse decision stands behind.
    /// </summary>
    public static bool Covers(HttpContext? httpContext, string? driveId, string? itemId)
        => From(httpContext) is { } authorized
           && string.Equals(authorized.DriveId, driveId?.Trim(), StringComparison.Ordinal)
           && string.Equals(authorized.ItemId, itemId?.Trim(), StringComparison.Ordinal);
}

/// <summary>
/// Compose's byte calls with the identity chosen by ONE rule (task 171): APP-ONLY when the request's Dataverse
/// authorization covers exactly this drive item (<see cref="ComposeBrokeredDocument.Covers"/>); otherwise the caller's
/// own OBO identity — Compose "Path B", a document opened by drive+item that has NO <c>sprk_document</c> row, where SPE's
/// answer for the caller is the only decision available (escalation trigger 2: reported, not converted).
/// </summary>
/// <remarks>
/// Why one helper and not a branch at each of the ~15 call sites: the identity decision must be the same everywhere a
/// Compose request touches the item (metadata, bytes, baseline version, replace) — a site that read app-only while its
/// neighbour wrote OBO would split one request across two identities. Metadata is read UNCACHED on both paths, because
/// Compose sends the ETag back in <c>If-Match</c>.
/// </remarks>
internal static class ComposeSpeAccess
{
    public static Task<FileHandleDto?> GetMetadataForComposeAsync(
        this ISpeFileOperations spe, HttpContext httpContext, string driveId, string itemId, CancellationToken ct)
        => ComposeBrokeredDocument.Covers(httpContext, driveId, itemId)
            ? spe.GetFileMetadataUncachedAsync(driveId, itemId, ct)
            : spe.GetFileMetadataAsUserAsync(httpContext, driveId, itemId, ct);

    public static Task<Stream?> DownloadForComposeAsync(
        this ISpeFileOperations spe, HttpContext httpContext, string driveId, string itemId, CancellationToken ct)
        => ComposeBrokeredDocument.Covers(httpContext, driveId, itemId)
            ? spe.DownloadFileAsync(driveId, itemId, ct)
            : spe.DownloadFileAsUserAsync(httpContext, driveId, itemId, ct);

    public static Task<Stream?> DownloadVersionForComposeAsync(
        this ISpeFileOperations spe, HttpContext httpContext, string driveId, string itemId, string versionId, CancellationToken ct)
        => ComposeBrokeredDocument.Covers(httpContext, driveId, itemId)
            ? spe.DownloadFileVersionAsync(driveId, itemId, versionId, ct)
            : spe.DownloadFileVersionAsUserAsync(httpContext, driveId, itemId, versionId, ct);

    public static Task<string?> GetCurrentVersionIdForComposeAsync(
        this ISpeFileOperations spe, HttpContext httpContext, string driveId, string itemId, CancellationToken ct)
        => ComposeBrokeredDocument.Covers(httpContext, driveId, itemId)
            ? spe.GetCurrentVersionIdAsync(driveId, itemId, ct)
            : spe.GetCurrentVersionIdAsUserAsync(httpContext, driveId, itemId, ct);

    public static Task<FileHandleDto?> ReplaceForComposeAsync(
        this ISpeFileOperations spe, HttpContext httpContext, string driveId, string itemId, Stream content, string? ifMatch, CancellationToken ct)
        => ComposeBrokeredDocument.Covers(httpContext, driveId, itemId)
            ? spe.ReplaceFileContentAsync(driveId, itemId, content, ifMatch, ct)
            : string.IsNullOrEmpty(ifMatch)
                ? spe.ReplaceFileContentAsUserAsync(httpContext, driveId, itemId, content, ct)
                : spe.ReplaceFileContentAsUserAsync(httpContext, driveId, itemId, content, ifMatch, ct);
}
