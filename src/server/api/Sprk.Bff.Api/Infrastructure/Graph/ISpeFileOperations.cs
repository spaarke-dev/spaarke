using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Interface for SPE file operations needed by AI services.
/// Extracted from SpeFileStore to enable unit testing without complex mock setup.
/// </summary>
/// <remarks>
/// <para><b>Identity (unified-access-control-r2 task 171, owner round 69 — broker-only).</b> Every byte path runs
/// APP-ONLY after the caller has been authorized against Dataverse and, when it follows a <c>sprk_document</c> row's
/// pointer, after the document-pointer check (<c>RecordContainerResolver.EnsureDocumentPointerContainerAsync</c>).
/// Under OBO, SharePoint Embedded only answers a caller who holds a container ROLE — and per-record secure containers
/// have none by design — so an OBO byte path works only where someone happened to hand-add the user.</para>
/// <para>The remaining <c>*AsUserAsync</c> members exist ONLY for paths with no Dataverse record behind them, where
/// SPE's own answer for the caller IS the decision (task 171 escalation trigger 2): Compose "Path B" (a document
/// opened by drive+item that has no <c>sprk_document</c> row), the chat-session check that authorizes those sessions,
/// and RAG indexing of an item named without a document. Do not add a new caller with a record behind it — authorize
/// the record and use the app-only member. <c>SpeBrokerOnlyByteIdentityGuardTests</c> pins the caller set.</para>
/// </remarks>
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
    /// Get file metadata APP-ONLY, never from the metadata cache (task 171). Use where the ETag is sent back in
    /// <c>If-Match</c> or compared to detect an external edit. Performs NO authorization.
    /// </summary>
    Task<FileHandleDto?> GetFileMetadataUncachedAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Get file metadata as the CALLER (OBO). Only for a path with no Dataverse record behind it (see the interface
    /// remarks); a row-backed path uses <see cref="GetFileMetadataUncachedAsync"/>.
    /// </summary>
    Task<FileHandleDto?> GetFileMetadataAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Who created the item (app-only; Graph <c>createdBy</c>), or <see langword="null"/> when the item is not in that
    /// drive. Uncached — it is evidence on an authorization path (unified-access-control-r2 task 166 r2: the
    /// document-pointer check verifies the ITEM, owner round 23 item 1).
    /// </summary>
    Task<SpeItemCreator?> GetItemCreatorAsync(
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
    /// Download file content as the CALLER (OBO). Only for a path with no Dataverse record behind it (see the
    /// interface remarks); a row-backed path uses <see cref="DownloadFileAsync"/>.
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
    /// Download a SPECIFIC prior version APP-ONLY — the twin of <see cref="DownloadFileVersionAsUserAsync"/> for the
    /// document version route and a row-backed Compose save (task 171). Performs NO authorization.
    /// </summary>
    Task<Stream?> DownloadFileVersionAsync(
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
    /// The CURRENT version id APP-ONLY — the twin of <see cref="GetCurrentVersionIdAsUserAsync"/> for a row-backed
    /// Compose load (task 171). Performs NO authorization.
    /// </summary>
    Task<string?> GetCurrentVersionIdAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists a file's versions using APP-ONLY (broker) authentication.
    /// </summary>
    /// <remarks>
    /// Newest-first <see cref="VersionInfoDto"/> projection, read-only (no restore/branch surface).
    ///
    /// ⚠️ Performs NO authorization. The broker identity can read any item in a container it owns,
    /// so the CALLER must authorize the principal against the owning record first. Serves the
    /// external-access surface and, since task 171, the workforce version route (the OBO list was
    /// deleted: it worked only for a caller holding a container role).
    /// </remarks>
    Task<IReadOnlyList<VersionInfoDto>?> ListFileVersionsAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);

    // UploadSmallAsUserAsync (both overloads) DELETED 2026-10-06 by unified-access-control-r2 task 171: every caller
    // derives its container server-side behind a Dataverse decision and now writes app-only (owner round 69).

    /// <summary>
    /// Create a NEW drive-item in a container/drive under APP-ONLY (managed identity,
    /// ADR-028) auth. PUTs the stream to <c>drives/{driveId}/root:/{path}:/content</c> and returns the created
    /// item's <see cref="FileHandleDto"/> (id + name + size + etag + resolved drive id).
    ///
    /// ⚠️ <b>"Small" is a legacy name, NOT a 4 MB limit.</b> This implementation has NO size guard: Graph's
    /// simple-upload boundary for SPE containers has been 250 MB since October 2023 (spaarkeai-compose-r8 task 015
    /// deleted a 4 MB guard that failed every larger Compose create-on-save). Do not reintroduce one.
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
    /// Replace the content of an existing drive-item APP-ONLY, with optional <c>If-Match</c> (task 171): commits a new
    /// SPE version; null when the item does not exist; <see cref="EtagPreconditionFailedException"/> on 412,
    /// <see cref="DocumentLockedByWordException"/> on 423 / locked, <see cref="GraphThrottledException"/> on 429.
    /// ⚠️ Performs NO authorization: the caller must hold WRITE on the <c>sprk_document</c> whose (verified) pointer
    /// names this item.
    /// </summary>
    Task<FileHandleDto?> ReplaceFileContentAsync(
        string driveId,
        string itemId,
        Stream content,
        string? ifMatch,
        CancellationToken ct = default);

    /// <summary>
    /// Replace the content of an existing drive-item as the CALLER (OBO). Only for Compose "Path B" (no
    /// <c>sprk_document</c> row — see the interface remarks). Returns null when the drive-item doesn't exist. Throws
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
