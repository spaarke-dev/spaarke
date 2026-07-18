using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Interface for SPE file operations needed by AI services.
/// Extracted from SpeFileStore to enable unit testing without complex mock setup.
/// </summary>
public interface ISpeFileOperations
{
    /// <summary>
    /// Get file metadata including name and size (app-only auth).
    /// </summary>
    Task<FileHandleDto?> GetFileMetadataAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Get file metadata using user OBO authentication.
    /// Use this when accessing files uploaded by a user in their context.
    /// </summary>
    Task<FileHandleDto?> GetFileMetadataAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Download file content as a stream (app-only auth).
    /// </summary>
    Task<Stream?> DownloadFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Download file content using user OBO authentication.
    /// Use this when accessing files uploaded by a user in their context.
    /// </summary>
    Task<Stream?> DownloadFileAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Download the content of a SPECIFIC prior version of a drive-item by <paramref name="versionId"/>,
    /// using the caller's OBO identity. Graph route
    /// <c>drives/{driveId}/items/{itemId}/versions/{versionId}/content</c>. Returns the version's byte
    /// stream, or <c>null</c> when the item or that version is not found (facade-translated — no
    /// <c>Microsoft.Graph</c> exception type crosses this boundary, ADR-007).
    /// </summary>
    /// <remarks>
    /// Compose R3 E1 baseline retrieval (FR-06, Spike S4): the delta save applies edits onto the
    /// LOAD-TIME SPE version captured by <paramref name="versionId"/> at Load, which stays addressable
    /// even after later dirty saves advance the item's CURRENT version. Mirrors
    /// <see cref="DownloadFileAsUserAsync(HttpContext, string, string, CancellationToken)"/>; additive —
    /// existing download callers and their mocks are untouched. Consumed by the E1 cutover (task 022).
    /// </remarks>
    Task<Stream?> DownloadFileVersionAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        string versionId,
        CancellationToken ct = default);

    /// <summary>
    /// Create a NEW small (&lt;4 MB) drive-item in a container/drive under the user's OBO
    /// identity. PUTs the stream to <c>drives/{driveId}/root:/{path}:/content</c>, minting a
    /// fresh drive-item, and returns its <see cref="FileHandleDto"/> (id + name + size + etag +
    /// resolved drive id). Used by the Compose create-on-save backbone (FR-05) when a transient
    /// draft has no <c>DocumentSpeId</c> yet — the drive-item must be created before the
    /// <c>sprk_document</c> row + indexing. Distinct from
    /// <see cref="ReplaceFileContentAsUserAsync(HttpContext, string, string, Stream, CancellationToken)"/>,
    /// which overwrites an EXISTING item.
    /// </summary>
    /// <remarks>
    /// ADR-007: no <c>Microsoft.Graph</c> type crosses this boundary — the facade returns the
    /// <see cref="FileHandleDto"/> shape only. Throws <see cref="UnauthorizedAccessException"/> on
    /// 403 ACL denial and <see cref="ArgumentException"/> on 413 (content &gt; 4 MB — use chunked
    /// upload). The concrete <c>SpeFileStore</c> already implements this; it is surfaced here so
    /// OBO callers can create drive-items through the same injected facade.
    /// </remarks>
    Task<FileHandleDto?> UploadSmallAsUserAsync(
        HttpContext ctx,
        string containerId,
        string path,
        Stream content,
        CancellationToken ct = default);

    /// <summary>
    /// Create a NEW small (&lt;4 MB) drive-item in a container/drive under APP-ONLY (managed identity,
    /// ADR-028) auth — the background/server-side counterpart to
    /// <see cref="UploadSmallAsUserAsync(HttpContext, string, string, Stream, CancellationToken)"/>.
    /// PUTs the stream to <c>drives/{driveId}/root:/{path}:/content</c> and returns the created
    /// item's <see cref="FileHandleDto"/> (id + name + size + etag + resolved drive id).
    /// </summary>
    /// <remarks>
    /// ADR-007: no <c>Microsoft.Graph</c> type crosses this boundary — the facade returns the
    /// <see cref="FileHandleDto"/> shape only. Surfaced on the interface (2026-07-16,
    /// messaging-communication-app-r1 task 070) so background materializers with no acting user
    /// (e.g. inbound message-attachment materialization) inject the same mockable SPE facade the
    /// email/AI/Compose services already do, rather than the concrete type. The concrete
    /// <c>SpeFileStore</c> already implements this exact signature — the addition is declaration-only.
    /// </remarks>
    Task<FileHandleDto?> UploadSmallAsync(
        string driveId,
        string path,
        Stream content,
        CancellationToken ct = default);

    /// <summary>
    /// Replace the content of an existing drive-item by itemId (OBO flow). PUTs the
    /// stream to the drive-item's /content endpoint, committing a new SPE version.
    /// Returns null when the drive-item doesn't exist. Throws
    /// <see cref="UnauthorizedAccessException"/> on ACL denial.
    /// </summary>
    Task<FileHandleDto?> ReplaceFileContentAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        Stream content,
        CancellationToken ct = default);

    /// <summary>
    /// Replace the content of an existing drive-item by itemId (OBO flow) with optimistic
    /// concurrency. Same as the etag-less overload, but sends an <c>If-Match</c> header when
    /// <paramref name="ifMatch"/> is non-empty so a drive-item that moved under the caller is
    /// rejected instead of blindly overwritten (FR-24 / Spike 7 gap G-1).
    /// </summary>
    /// <remarks>
    /// Throws <see cref="EtagPreconditionFailedException"/> on HTTP 412 (ETag moved) and
    /// <see cref="DocumentLockedByWordException"/> on HTTP 423 (open in Word for Web). ADR-007:
    /// no <c>Microsoft.Graph</c> type crosses this boundary. When <paramref name="ifMatch"/> is
    /// null/empty this behaves exactly like the etag-less overload (a blind PUT).
    /// </remarks>
    Task<FileHandleDto?> ReplaceFileContentAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        Stream content,
        string? ifMatch,
        CancellationToken ct = default);

    /// <summary>
    /// Resolve a container ID to its drive ID.
    /// Container IDs start with "b!" (base64-encoded SharePoint site ID).
    /// If the input is already a drive ID, returns it unchanged.
    /// </summary>
    /// <param name="containerOrDriveId">Container ID (b!xxx) or drive ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The drive ID for the container.</returns>
    Task<string> ResolveDriveIdAsync(string containerOrDriveId, CancellationToken ct = default);

    // =========================================================================
    // SPE change-detection facade (spaarkeai-compose-r2 FR-26, task 052)
    //
    // ADR-007: ALL Microsoft.Graph types (Subscription, DriveItem, GraphServiceClient)
    // stay behind this facade. Callers in Services/Compose/ (SpeSyncOrchestrator,
    // SpeWebhookRenewalHostedService) receive only the primitive/DTO shapes below —
    // they never see a Graph type. These calls run app-only via managed identity
    // (ADR-028) since a background renewal has no acting user.
    // =========================================================================

    /// <summary>
    /// Creates a Graph change-notification subscription on <c>drives/{driveId}/root</c>
    /// for <c>updated</c> events (app-only / managed identity). SPE driveItem
    /// subscriptions have a maximum lifespan of 4230 minutes; the caller supplies the
    /// desired expiration and owns renewal.
    /// </summary>
    Task<SpeSubscriptionDto> CreateDriveRootSubscriptionAsync(
        string driveId,
        string notificationUrl,
        string clientState,
        DateTimeOffset expirationDateTime,
        CancellationToken ct = default);

    /// <summary>
    /// Renews (PATCHes) an existing subscription's expiration (app-only). Throws when
    /// Graph rejects the renewal (e.g. 404 subscription-gone) — the caller degrades to
    /// the delta-poll fallback.
    /// </summary>
    Task<SpeSubscriptionDto> RenewSubscriptionAsync(
        string subscriptionId,
        DateTimeOffset newExpirationDateTime,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a subscription (app-only). Best-effort teardown; propagates Graph errors.
    /// </summary>
    Task DeleteSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Enumerates changed driveItems for <c>drives/{driveId}/root</c> since the supplied
    /// delta link (pass <c>null</c> for an initial full enumeration). Follows all
    /// <c>@odata.nextLink</c> pages and returns the terminal <c>@odata.deltaLink</c> as
    /// the advanced token for the next call (app-only).
    /// </summary>
    Task<SpeDeltaResult> EnumerateDriveDeltaAsync(
        string driveId,
        string? deltaLink,
        CancellationToken ct = default);
}

/// <summary>
/// Facade DTO for a Graph change-notification subscription. No Microsoft.Graph type
/// crosses the <see cref="ISpeFileOperations"/> boundary (ADR-007).
/// </summary>
public sealed record SpeSubscriptionDto(
    string SubscriptionId,
    string Resource,
    DateTimeOffset ExpirationDateTime);

/// <summary>
/// A single changed driveItem surfaced by a delta enumeration. <see cref="Deleted"/> is
/// true when Graph flagged the item with a <c>deleted</c> facet (tombstone).
/// </summary>
public sealed record SpeDriveChange(
    string ItemId,
    string? Name,
    string? ETag,
    bool Deleted);

/// <summary>
/// Result of a delta enumeration: the changed items plus the advanced delta link to
/// persist for the next round (null when Graph returned no deltaLink).
/// </summary>
public sealed record SpeDeltaResult(
    IReadOnlyList<SpeDriveChange> Changes,
    string? DeltaLink);
