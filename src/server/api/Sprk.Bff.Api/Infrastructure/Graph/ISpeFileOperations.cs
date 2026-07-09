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
