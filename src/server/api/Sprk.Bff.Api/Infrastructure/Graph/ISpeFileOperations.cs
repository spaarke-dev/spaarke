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
    /// Resolve the CURRENT (most-recent) version id of a drive-item using the caller's OBO identity.
    /// Graph route <c>drives/{driveId}/items/{itemId}/versions</c> → the newest version's id. Returns
    /// <c>null</c> when the item has no version history or is not found (facade-translated — no
    /// <c>Microsoft.Graph</c> exception type crosses this boundary, ADR-007).
    /// </summary>
    /// <remarks>
    /// Compose R3 E1 baseline retrieval (FR-06): captured at Load and surfaced on
    /// <c>LoadComposeDocumentResult.VersionId</c> so a later dirty save that no longer holds the client
    /// bytes (e.g. after a page refresh) can re-fetch this LOAD-TIME version via
    /// <see cref="DownloadFileVersionAsUserAsync"/> — the load-time version stays addressable even after
    /// the save advances the item's current version. Additive; best-effort (Load never fails on a null).
    /// </remarks>
    Task<string?> GetCurrentVersionIdAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// List ALL versions of a drive-item using the caller's OBO identity (user-context,
    /// per-item delegated permission — NEVER app-only). Graph route
    /// <c>drives/{driveId}/items/{itemId}/versions</c>, mapped to the
    /// <see cref="VersionInfoDto"/> projection (id/label + lastModified timestamp + size),
    /// newest first. Returns <c>null</c> when the item is not found (facade-translated —
    /// no <c>Microsoft.Graph</c> type or exception crosses this boundary, ADR-007).
    /// Throws <see cref="UnauthorizedAccessException"/> when the calling user is not
    /// authorized to read the item (Graph 403 under the user's own token).
    /// </summary>
    /// <remarks>
    /// spaarkeai-compose-r6 task 050 (spec FR-07 / Success Criterion 4): the user-context
    /// version-history list backing <c>GET /api/documents/{documentId}/versions</c> (re-keyed from
    /// the drive-keyed route by unified-access-control-r2 task 079 — the caller names a document row
    /// and the drive/item below come off that row AFTER the per-document gate, so a caller can no
    /// longer address an arbitrary SPE item).
    /// Same Graph call shape as <see cref="GetCurrentVersionIdAsUserAsync"/>, but returns the
    /// FULL mapped list instead of just the newest id (per task 002's inventory,
    /// <c>notes/spe-versioning-verify.md</c> §3). Read-only — no restore/branch surface.
    /// </remarks>
    Task<IReadOnlyList<VersionInfoDto>?> ListFileVersionsAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists a file's versions using APP-ONLY (broker) authentication.
    /// </summary>
    /// <remarks>
    /// App-only sibling of <see cref="ListFileVersionsAsUserAsync"/> — same Graph route, same
    /// newest-first <see cref="VersionInfoDto"/> projection, read-only (no restore/branch surface).
    ///
    /// ⚠️ Performs NO authorization. The broker identity can read any item in a container it owns,
    /// so the CALLER must authorize the principal against the owning record first. Exists for the
    /// external-access surface, whose CIAM contacts are not Dataverse principals and therefore have
    /// no delegated permission to exchange for the AsUser variant. Prefer the AsUser overload
    /// wherever an acting Entra user is available. unified-access-control-r2.
    /// </remarks>
    Task<IReadOnlyList<VersionInfoDto>?> ListFileVersionsAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Create a NEW drive-item in a container/drive under the user's OBO
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
    /// 403 ACL denial. The concrete <c>SpeFileStore</c> already implements this; it is surfaced here
    /// so OBO callers can create drive-items through the same injected facade.
    ///
    /// ⚠️ <b>"Small" is a legacy name, NOT a 4 MB limit.</b> Corrected 2026-09-02: this text used to
    /// say "&lt;4 MB" and "throws ArgumentException on 413 (content &gt; 4 MB — use chunked upload)".
    /// Both were wrong. Graph's simple-upload boundary for SPE containers has been <b>250 MB</b>
    /// since October 2023; the 4 MB figure came from the retired OneDrive REST docs.
    /// <c>spaarkeai-compose-r8</c> task 015 (FR-S08) DELETED the 4 MB guard from the implementation
    /// precisely because it failed a Compose create-on-save of any document over 4 MB outright — but
    /// the guard's advice survived here in prose, where it kept reading as a live constraint and
    /// caused a later reviewer to propose capping an upload UI at 4 MB. Do not reintroduce either.
    /// The app-only twin below has no size guard at all.
    /// </remarks>
    Task<FileHandleDto?> UploadSmallAsUserAsync(
        HttpContext ctx,
        string containerId,
        string path,
        Stream content,
        CancellationToken ct = default);

    /// <summary>
    /// Create a NEW drive-item under the user's OBO identity with an EXPLICIT name-collision behaviour.
    /// </summary>
    /// <remarks>
    /// The 5-argument overload above is equivalent to passing
    /// <see cref="Sprk.Bff.Api.Models.ConflictBehavior.Replace"/> — which silently overwrites a
    /// same-named file. Any caller that has NOT already asked the user what to do on a collision
    /// should pass <see cref="Sprk.Bff.Api.Models.ConflictBehavior.Fail"/> instead: Graph then returns
    /// 409 and the existing item is untouched, which is recoverable. Overwriting first and reporting
    /// afterwards is not.
    ///
    /// Added by unified-access-control-r2 as an OVERLOAD rather than a parameter on the existing
    /// method so that the many Moq expectations pinning the 4-argument arity keep compiling.
    /// </remarks>
    Task<FileHandleDto?> UploadSmallAsUserAsync(
        HttpContext ctx,
        string containerId,
        string path,
        Stream content,
        Sprk.Bff.Api.Models.ConflictBehavior conflictBehavior,
        CancellationToken ct = default);

    /// <summary>
    /// Create a NEW drive-item in a container/drive under APP-ONLY (managed identity,
    /// ADR-028) auth — the background/server-side counterpart to
    /// <see cref="UploadSmallAsUserAsync(HttpContext, string, string, Stream, CancellationToken)"/>.
    /// PUTs the stream to <c>drives/{driveId}/root:/{path}:/content</c> and returns the created
    /// item's <see cref="FileHandleDto"/> (id + name + size + etag + resolved drive id).
    ///
    /// ⚠️ <b>"Small" is a legacy name.</b> This implementation has NO size guard whatsoever — see the
    /// OBO twin above for why the "&lt;4 MB" claim that used to sit here was wrong (250 MB is the real
    /// simple-upload boundary for SPE containers).
    ///
    /// ⚠️ <b>The path MUST be a bare file name.</b> Uploading to a path makes Graph implicitly create
    /// every folder segment in it, so any prefix mints folders nobody asked for. Enforced by
    /// <c>tests/Spaarke.ArchTests/SpeUploadPathIsFlatGuardTests.cs</c> (2026-08-28 flat-path decision).
    /// Sanitize via <c>SpeUploadPath.SanitizeFileName</c>.
    ///
    /// ⚠️ <b>THIS overload overwrites on a name collision</b> — it supplies
    /// <c>ConflictBehavior.Replace</c> to preserve the behaviour its existing callers depend on. Two
    /// uploads of the same file name collapse onto ONE drive-item (SharePoint retains the prior content
    /// as a version, so the bytes are recoverable, but the two uploads stop being two documents).
    /// Callers that need distinct documents either make the file name unique first — see
    /// <c>EmailAttachmentProcessor.GenerateUniqueFileName</c> and the collision-survival tests in
    /// <c>tests/integration/data-mutation/SpeUploadPaths/SpeFlatUploadPathTests.cs</c> — or use the
    /// <see cref="UploadSmallAsync(string,string,Stream,Sprk.Bff.Api.Models.ConflictBehavior,CancellationToken)"/>
    /// overload with <c>Fail</c> / <c>Rename</c>.
    ///
    /// 🔴 <b>Corrected 2026-09-02.</b> This remark used to assert the path-keyed simple PUT "takes no
    /// <c>@microsoft.graph.conflictBehavior</c> — not rename, not fail". <b>That is false and led to a
    /// wrong design conclusion more than once.</b> The REST API honours the parameter
    /// (<c>fail|replace|rename</c>); it is the Kiota SDK's generated <c>PutAsync</c> that does not
    /// expose it, which is exactly why <c>UploadSessionManager.PutContentWithConflictBehaviorAsync</c>
    /// appends it to the request URI by hand. Do not re-derive the old claim from this file's history.
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
    /// App-only small upload with an EXPLICIT name-collision behaviour.
    ///
    /// ⚠️ <b>The path MUST be a bare file name</b>, exactly as for the 4-arg overload — a prefix makes
    /// Graph mint folders nobody asked for. Enforced by <c>SpeUploadPathIsFlatGuardTests</c>.
    ///
    /// ⚠️ <b>Performs NO authorization.</b> App-only means broker identity: the caller MUST have
    /// authorized the acting principal against the owning record first, and MUST have derived the
    /// container server-side rather than accepting one from the client.
    /// </summary>
    /// <remarks>
    /// Added as an overload rather than by changing the 4-arg signature: ~a dozen app-only callers
    /// (Compose save, communication ingest, invoice extraction, …) depend on replace-in-place, and
    /// flipping the default under them would turn working saves into 409s.
    ///
    /// <para>With <see cref="Sprk.Bff.Api.Models.ConflictBehavior.Fail"/> a collision throws
    /// <see cref="SpaarkeStorageException"/> with status 409 and leaves the existing item untouched —
    /// translated inside <c>Infrastructure.Graph</c> so no <c>Microsoft.Graph</c> type crosses this
    /// facade (ADR-007).</para>
    /// </remarks>
    Task<FileHandleDto?> UploadSmallAsync(
        string driveId,
        string path,
        Stream content,
        Sprk.Bff.Api.Models.ConflictBehavior conflictBehavior,
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
