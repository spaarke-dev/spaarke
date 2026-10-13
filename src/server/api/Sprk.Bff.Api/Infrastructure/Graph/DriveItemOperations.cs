using System.Diagnostics;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Handles DriveItem operations for SharePoint Embedded files and folders.
/// Responsible for listing, downloading, deleting, and metadata retrieval.
/// </summary>
/// <remarks>
/// dotnet-10-upgrade-r1 task 033 (2026-08-13): all exception handling in this file was re-anchored
/// from <c>Microsoft.Graph.ServiceException</c> to <see cref="ODataError"/>. The Kiota-based Graph
/// SDK has thrown <see cref="ODataError"/> (never <c>ServiceException</c>) since the v4→v5 rewrite this
/// codebase already absorbed, so every <c>catch (ServiceException ...)</c> here was dead code — Graph
/// 404/403/429 responses fell through to the generic <c>catch (Exception)</c> and surfaced as opaque
/// errors instead of the typed null/UnauthorizedAccessException/InvalidOperationException outcomes the
/// call sites in this file were written to produce. Fixed in passing per
/// notes/graph6-kiota2-break-assessment.md §4 (behavior-preserving — this restores the handling that
/// was always intended to run, matching the pattern already correct in UploadSessionManager).
/// </remarks>
public class DriveItemOperations
{
    private readonly IGraphClientFactory _factory;
    private readonly SpeContainerOwnershipGuard _ownership;
    private readonly ILogger<DriveItemOperations> _logger;
    private readonly GraphMetadataCache? _metadataCache;

    /// <remarks>
    /// App-only methods get their Graph client from <see cref="SpeContainerOwnershipGuard"/>, which refuses a
    /// drive this stamp does not own before Graph is called (task 227d). <c>…AsUser…</c> methods stay on
    /// OBO, where Graph enforces container membership.
    /// </remarks>
    public DriveItemOperations(
        IGraphClientFactory factory,
        SpeContainerOwnershipGuard ownership,
        ILogger<DriveItemOperations> logger,
        GraphMetadataCache? metadataCache = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metadataCache = metadataCache; // Optional: cache can be null if not configured
    }

    public async Task<IList<FileHandleDto>> ListChildrenAsync(
        string driveId,
        string? itemId = null,
        CancellationToken ct = default)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "ListChildren");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        // Ownership before the cache and outside the try: a refusal is a 404 spe_container_not_owned, never a cached or empty result (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);

        // Cache-aside: check Redis first (ADR-009)
        if (_metadataCache != null)
        {
            var cached = await _metadataCache.GetFolderListingAsync(driveId, itemId);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached folder listing for drive {DriveId}, item {ItemId}", driveId, itemId);
                activity?.SetTag("cache.result", "hit");
                return cached;
            }
            activity?.SetTag("cache.result", "miss");
        }

        _logger.LogInformation("Listing children in drive {DriveId}, item {ItemId}", driveId, itemId);

        try
        {
            DriveItemCollectionResponse? page;

            if (string.IsNullOrEmpty(itemId))
            {
                // List root items - use Items collection directly
                page = await graphClient.Drives[driveId].Items
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter = "parentReference/path eq '/drive/root:'";
                    }, cancellationToken: ct);
            }
            else
            {
                // List items in specific folder
                page = await graphClient.Drives[driveId].Items[itemId].Children
                    .GetAsync(cancellationToken: ct);
            }

            if (page?.Value == null)
            {
                _logger.LogWarning("No children found in drive {DriveId}, item {ItemId}", driveId, itemId);
                return new List<FileHandleDto>();
            }

            var result = page.Value;
            _logger.LogInformation("Found {Count} children in drive {DriveId}", result.Count, driveId);

            var items = result.Select(item => new FileHandleDto(
                item.Id!,
                item.Name!,
                item.ParentReference?.Id,
                item.Size,
                item.CreatedDateTime ?? DateTimeOffset.UtcNow,
                item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                item.ETag,
                item.Folder != null,
                item.WebUrl,
                item.ParentReference?.DriveId)).ToList();

            // Cache the result (2min TTL)
            if (_metadataCache != null)
            {
                await _metadataCache.SetFolderListingAsync(driveId, itemId, items);
            }

            return items;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Drive {DriveId} or item {ItemId} not found", driveId, itemId);
            return new List<FileHandleDto>();
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "Graph API error listing children: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to list children: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing children: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// File content APP-ONLY (broker). Performs NO authorization: every caller authorizes the principal against
    /// Dataverse first and, when it follows a <c>sprk_document</c> row's pointer, runs the document-pointer check
    /// (<c>RecordContainerResolver.EnsureDocumentPointerContainerAsync</c>).
    /// </summary>
    public async Task<Stream?> DownloadFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
        // Ownership before the core's try: a refusal is a 404 spe_container_not_owned, never null (task 227d).
        => await DownloadFileCoreAsync(await _ownership.ForOwnedContainerAsync(driveId, ct), "app-only", driveId, itemId, ct)
            .ConfigureAwait(false);

    public async Task<bool> DeleteFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "DeleteFile");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Deleting file {ItemId} from drive {DriveId}", itemId, driveId);

        // Ownership outside the try: a refusal is a 404 spe_container_not_owned, never a cached or empty result (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);

        try
        {
            await graphClient.Drives[driveId].Items[itemId]
                .DeleteAsync(cancellationToken: ct);

            _logger.LogInformation("Successfully deleted file {ItemId}", itemId);

            // Invalidate caches after deletion
            if (_metadataCache != null)
            {
                await _metadataCache.InvalidateFileMetadataAsync(driveId, itemId);
                // Invalidate parent folder listing (root since we don't know parent here)
                await _metadataCache.InvalidateFolderListingAsync(driveId, null);
            }

            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
            return false;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "Graph API error deleting file: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to delete file: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting file: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<FileHandleDto?> GetFileMetadataAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "GetFileMetadata");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        // Ownership before the cache and outside the try: a refusal is a 404 spe_container_not_owned, never a cached or empty result (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);

        // Cache-aside: check Redis first (ADR-009)
        if (_metadataCache != null)
        {
            var cached = await _metadataCache.GetFileMetadataAsync(driveId, itemId);
            if (cached != null)
            {
                _logger.LogDebug("Returning cached metadata for file {ItemId} in drive {DriveId}", itemId, driveId);
                activity?.SetTag("cache.result", "hit");
                return cached;
            }
            activity?.SetTag("cache.result", "miss");
        }

        _logger.LogInformation("Getting metadata for file {ItemId} from drive {DriveId}", itemId, driveId);

        try
        {
            var item = await graphClient.Drives[driveId].Items[itemId]
                .GetAsync(cancellationToken: ct);

            if (item == null)
            {
                _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
                return null;
            }

            _logger.LogInformation("Successfully retrieved metadata for file {ItemId}", itemId);

            var metadata = new FileHandleDto(
                item.Id!,
                item.Name!,
                item.ParentReference?.Id,
                item.Size,
                item.CreatedDateTime ?? DateTimeOffset.UtcNow,
                item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                item.ETag,
                item.Folder != null,
                item.WebUrl,
                item.ParentReference?.DriveId);

            // Cache the result with ETag-versioned key (5min TTL)
            if (_metadataCache != null)
            {
                await _metadataCache.SetFileMetadataAsync(driveId, itemId, metadata);
            }

            return metadata;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
            return null;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "Graph API error getting file metadata: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to get file metadata: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting file metadata: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Who CREATED a drive item (app-only Graph read of <c>id,name,createdBy</c>) — the evidence the document-pointer
    /// check verifies before the BFF follows a <c>sprk_document</c> row's pointer as the application
    /// (unified-access-control-r2 task 166 r2, owner round 23 item 1). Returns <see langword="null"/> when the item is
    /// not in that drive (Graph 404). Deliberately NOT cached: it sits on an authorization path, so a stale or poisoned
    /// cache entry would be a security defect rather than a slow page; every other fault throws (the caller refuses).
    /// </summary>
    public async Task<SpeItemCreator?> GetItemCreatorAsync(string driveId, string itemId, CancellationToken ct = default)
    {
        // Ownership outside the try: the drive id comes from a sprk_document row; another customer's container is refused
        // (404 spe_container_not_owned, as for a missing one) before any read (task 227d) — the caller refuses.
        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);

        try
        {
            // task 166 f1: size, file (hashes) and webUrl ride on the same read — the server-side pointer attach and the
            // relocation copy verify against them; still one uncached call. lastModifiedDateTime (round 54 item 3) is the
            // time a relocation's witness records when Graph lists no version.
            var item = await graphClient.Drives[driveId].Items[itemId]
                .GetAsync(
                    req => req.QueryParameters.Select = new[] { "id", "name", "createdBy", "size", "file", "webUrl", "lastModifiedDateTime" },
                    cancellationToken: ct);

            if (item is null)
            {
                return null;
            }

            return new SpeItemCreator(
                item.Name,
                item.CreatedBy?.User?.Id,
                item.CreatedBy?.Application?.Id,
                item.Size,
                item.File?.Hashes?.QuickXorHash,
                item.WebUrl,
                item.LastModifiedDateTime);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Item {ItemId} not found in drive {DriveId} (creator read)", itemId, driveId);
            return null;
        }
    }

    // =============================================================================
    // ListChildrenAsUserAsync, DownloadFileWithRangeAsUserAsync, UpdateItemAsUserAsync and DeleteItemAsUserAsync were
    // DELETED 2026-10-06 by unified-access-control-r2 task 171: OBO byte methods with no caller since task 071 retired
    // their routes. Under the broker-only decision (owner round 69) a byte path runs app-only behind a Dataverse check;
    // an uncalled OBO byte method is only an invitation to reintroduce the "SPE decides" shape.
    // =============================================================================

    /// <summary>
    /// File metadata as the CALLER (OBO), uncached. Kept ONLY for Compose "Path B" — a document opened by drive+item
    /// that has no <c>sprk_document</c> row, so no Dataverse record exists to authorize and SPE's own answer for the
    /// caller is the decision (task 171, escalation trigger 2; see the task note). Every row-backed path uses
    /// <see cref="GetFileMetadataUncachedAsync"/> after its Dataverse check.
    /// </summary>
    public async Task<FileHandleDto?> GetFileMetadataAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => await GetFileMetadataCoreAsync(await _factory.ForUserAsync(ctx, ct), "OBO", driveId, itemId, ct)
            .ConfigureAwait(false);

    /// <summary>
    /// File metadata APP-ONLY (broker) and UNCACHED — the app-only twin of <see cref="GetFileMetadataAsUserAsync"/>
    /// (unified-access-control-r2 task 171). Unlike <see cref="GetFileMetadataAsync"/> it never reads the Redis
    /// metadata cache: a caller that sends the ETag back in <c>If-Match</c>, or compares it to detect an external edit,
    /// must see the item's CURRENT ETag. Performs NO authorization.
    /// </summary>
    public async Task<FileHandleDto?> GetFileMetadataUncachedAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => await GetFileMetadataCoreAsync(await _ownership.ForOwnedContainerAsync(driveId, ct), "app-only", driveId, itemId, ct)
            .ConfigureAwait(false);

    private async Task<FileHandleDto?> GetFileMetadataCoreAsync(
        GraphServiceClient graphClient,
        string identity,
        string driveId,
        string itemId,
        CancellationToken ct)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "GetFileMetadataUncached");
        activity?.SetTag("identity", identity);
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Getting metadata for file {ItemId} from drive {DriveId} ({Identity})",
            itemId, driveId, identity);

        try
        {
            var item = await graphClient.Drives[driveId].Items[itemId]
                .GetAsync(cancellationToken: ct);

            if (item == null)
            {
                _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
                return null;
            }

            return new FileHandleDto(
                item.Id!,
                item.Name!,
                item.ParentReference?.Id,
                item.Size,
                item.CreatedDateTime ?? DateTimeOffset.UtcNow,
                item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                item.ETag,
                item.Folder != null,
                item.WebUrl,
                item.ParentReference?.DriveId);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
            return null;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Access denied getting metadata for file {ItemId} ({Identity}): {Error}", itemId, identity, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to file {itemId}", ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "Graph API error getting file metadata: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to get file metadata: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// File content as the CALLER (OBO). Kept ONLY for Compose "Path B" (no <c>sprk_document</c> row — SPE's answer
    /// for the caller is the decision; task 171 escalation trigger 2). Every row-backed path uses
    /// <see cref="DownloadFileAsync"/> after its Dataverse check and the document-pointer check.
    /// </summary>
    public async Task<Stream?> DownloadFileAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => await DownloadFileCoreAsync(await _factory.ForUserAsync(ctx, ct), "OBO", driveId, itemId, ct)
            .ConfigureAwait(false);

    /// <summary>
    /// The one download body (task 171): 404 is null; 403 is <see cref="UnauthorizedAccessException"/>; 429 is a
    /// rate-limited <see cref="InvalidOperationException"/>; any other Graph error is <see cref="InvalidOperationException"/>.
    /// </summary>
    private async Task<Stream?> DownloadFileCoreAsync(
        GraphServiceClient graphClient,
        string identity,
        string driveId,
        string itemId,
        CancellationToken ct)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "DownloadFile");
        activity?.SetTag("identity", identity);
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Downloading file {ItemId} from drive {DriveId} ({Identity})", itemId, driveId, identity);

        try
        {
            var stream = await graphClient.Drives[driveId].Items[itemId].Content
                .GetAsync(cancellationToken: ct);

            if (stream == null)
            {
                _logger.LogWarning("Failed to download file {ItemId} - stream is null", itemId);
                return null;
            }

            return stream;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
            return null;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Access denied downloading file {ItemId} ({Identity}): {Error}", itemId, identity, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to file {itemId}", ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "Graph API error downloading file: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to download file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// A SPECIFIC version's content as the CALLER (OBO). Kept ONLY for Compose "Path B" (no <c>sprk_document</c> row;
    /// task 171 escalation trigger 2). FR-06 / Spike S4: the load-time SPE baseline for the Compose E1 delta save.
    /// </summary>
    public async Task<Stream?> DownloadFileVersionAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        string versionId,
        CancellationToken ct = default)
        => await DownloadFileVersionCoreAsync(await _factory.ForUserAsync(ctx, ct), "OBO", driveId, itemId, versionId, ct)
            .ConfigureAwait(false);

    /// <summary>
    /// A SPECIFIC prior version's content APP-ONLY (broker). Returns <see langword="null"/> when the item or that version
    /// is not found. Performs NO authorization: callers are the relocation's version replay (task 166, owner round 45
    /// item 1 — a server-derived move of a row's own file), the document version route and Compose's row-backed save,
    /// each of which authorized the caller against the document and verified its pointer first (task 171).
    /// </summary>
    public async Task<Stream?> DownloadFileVersionAsync(
        string driveId,
        string itemId,
        string versionId,
        CancellationToken ct = default)
        // Ownership before the core's try (task 227d): a relocation reads only this stamp's containers.
        => await DownloadFileVersionCoreAsync(
                await _ownership.ForOwnedContainerAsync(driveId, ct), "app-only", driveId, itemId, versionId, ct)
            .ConfigureAwait(false);

    private async Task<Stream?> DownloadFileVersionCoreAsync(
        GraphServiceClient graphClient,
        string identity,
        string driveId,
        string itemId,
        string versionId,
        CancellationToken ct)
    {
        // Not `using`: Activity.Current is the CALLER's span (ActivityCurrentDisposalGuardTests, #1084 follow-on).
        var activity = Activity.Current;
        activity?.SetTag("operation", "DownloadFileVersion");
        activity?.SetTag("identity", identity);
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);
        activity?.SetTag("versionId", versionId);

        _logger.LogInformation(
            "Downloading version {VersionId} of file {ItemId} from drive {DriveId} ({Identity})", versionId, itemId, driveId, identity);

        try
        {
            var stream = await graphClient.Drives[driveId].Items[itemId]
                .Versions[versionId].Content
                .GetAsync(cancellationToken: ct);

            if (stream == null)
            {
                _logger.LogWarning("Failed to download version {VersionId} of file {ItemId} - stream is null", versionId, itemId);
                return null;
            }

            return stream;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Version {VersionId} of file {ItemId} not found in drive {DriveId}", versionId, itemId, driveId);
            return null;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "Access denied downloading version {VersionId} of file {ItemId} ({Identity}): {Error}", versionId, itemId, identity, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to file {itemId}", ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "Graph API error downloading file version: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to download file version: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// The <see cref="VersionInfoDto"/> projection of one Graph version: id (= label), date, size and who wrote it
    /// (display name; the person's or application's id server-side only).
    /// </summary>
    private static VersionInfoDto ToVersionInfo(DriveItemVersion v)
        => new(
            Id: v.Id!,
            ETag: null,
            LastModifiedDateTime: v.LastModifiedDateTime ?? default,
            Size: v.Size ?? 0,
            LastModifiedBy: v.LastModifiedBy?.User?.DisplayName ?? v.LastModifiedBy?.Application?.DisplayName)
        {
            LastModifiedByUserId = v.LastModifiedBy?.User?.Id,
            LastModifiedByApplicationId = v.LastModifiedBy?.Application?.Id,
        };

    /// <summary>
    /// The CURRENT version id as the CALLER (OBO). Kept ONLY for Compose "Path B" (task 171 escalation trigger 2).
    /// </summary>
    public async Task<string?> GetCurrentVersionIdAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => await GetCurrentVersionIdCoreAsync(await _factory.ForUserAsync(ctx, ct), "OBO", driveId, itemId, ct)
            .ConfigureAwait(false);

    /// <summary>
    /// The CURRENT version id APP-ONLY (broker) — the twin of <see cref="GetCurrentVersionIdAsUserAsync"/> for a
    /// row-backed Compose load (task 171). Performs NO authorization.
    /// </summary>
    public async Task<string?> GetCurrentVersionIdAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => await GetCurrentVersionIdCoreAsync(await _ownership.ForOwnedContainerAsync(driveId, ct), "app-only", driveId, itemId, ct)
            .ConfigureAwait(false);

    private async Task<string?> GetCurrentVersionIdCoreAsync(
        GraphServiceClient graphClient,
        string identity,
        string driveId,
        string itemId,
        CancellationToken ct)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "GetCurrentVersionId");
        activity?.SetTag("identity", identity);
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        try
        {
            var versions = await graphClient.Drives[driveId].Items[itemId]
                .Versions.GetAsync(cancellationToken: ct);

            // Graph lists versions newest-first; take the most-recently-modified as the CURRENT version.
            // Best-effort: a null/empty list yields null (the caller degrades to the client-bytes fast-path).
            var current = versions?.Value?
                .OrderByDescending(v => v.LastModifiedDateTime ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            if (current?.Id is null)
            {
                _logger.LogWarning(
                    "No versions returned for file {ItemId} in drive {DriveId} — current version id unavailable",
                    itemId, driveId);
                return null;
            }

            return current.Id;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Drive-item {ItemId} not found in drive {DriveId} when resolving current version id",
                itemId, driveId);
            return null;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "Access denied resolving current version id for file {ItemId} ({Identity}): {Error}",
                itemId, identity, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to file {itemId}", ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "Graph API error resolving current version id: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to resolve current version id: {ex.Message}", ex);
        }
    }

    // ListFileVersionsAsUserAsync (OBO) DELETED 2026-10-06 by unified-access-control-r2 task 171: its one caller, the
    // document version-history route, now reads app-only (ListFileVersionsAsync) after its Dataverse check and the
    // document-pointer check — the same projection, so the list does not change shape.

    /// <summary>
    /// The most pages <see cref="ListFileVersionsAsync"/> follows. A history longer than this is reported as not fully
    /// enumerated (it throws), never returned cut: a relocation replays what it lists (task 166, owner round 54 item 4).
    /// </summary>
    internal const int MaxVersionPages = 500;

    /// <summary>
    /// Lists the versions of a file using APP-ONLY (broker) authentication.
    /// </summary>
    /// <remarks>
    /// Added by unified-access-control-r2 for the external-access surface; since task 171 also the document
    /// version-history route's read (the OBO sibling is deleted).
    ///
    /// ⚠️ This method performs NO authorization of its own — app-only means the broker identity can
    /// read any item in any container it owns. Every caller MUST authorize the principal against the
    /// owning record BEFORE calling it. The external document endpoints do exactly that (project
    /// participation + document→project scoping, uniform 403); the workforce version route authorizes Read on the
    /// document and verifies its pointer.
    /// </remarks>
    public async Task<IReadOnlyList<VersionInfoDto>?> ListFileVersionsAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "ListFileVersions");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation(
            "Listing versions of file {ItemId} in drive {DriveId} (app-only)", itemId, driveId);

        // Ownership outside the try: a refusal is a 404 spe_container_not_owned, never a cached or empty result (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);

        try
        {
            // Same container as the ownership check above: the follow-up pages are server-issued URLs of this listing.
            var versionsBuilder = graphClient.Drives[driveId].Items[itemId].Versions;
            var versions = await versionsBuilder.GetAsync(cancellationToken: ct);

            if (versions?.Value == null)
            {
                _logger.LogWarning(
                    "No versions returned for file {ItemId} in drive {DriveId}", itemId, driveId);
                return Array.Empty<VersionInfoDto>();
            }

            // Every page (task 166 f1-v2, owner round 45 item 1): a relocation replays the WHOLE history, so a long one
            // must never be cut silently at the first page. Each follow-up request is the URL the server handed back. A
            // listing that does not end within MaxVersionPages is never returned as if it were the whole history: it
            // throws, and the relocation that asked fails (owner round 54 item 4, Sd).
            var all = new List<DriveItemVersion>(versions.Value);
            var pages = 1;
            while (!string.IsNullOrEmpty(versions?.OdataNextLink))
            {
                if (pages >= MaxVersionPages)
                {
                    throw new InvalidOperationException(
                        $"The versions of {itemId} could not be fully enumerated ({pages} pages read, more remain).");
                }

                versions = await versionsBuilder.WithUrl(versions.OdataNextLink).GetAsync(cancellationToken: ct);
                all.AddRange(versions?.Value ?? []);
                pages++;
            }

            var mapped = all
                .Where(v => v.Id != null)
                .OrderByDescending(v => v.LastModifiedDateTime ?? DateTimeOffset.MinValue)
                .Select(ToVersionInfo)
                .ToList();

            _logger.LogInformation(
                "Listed {Count} versions of file {ItemId} (app-only)", mapped.Count, itemId);
            return mapped;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "File {ItemId} not found in drive {DriveId} when listing versions", itemId, driveId);
            return null;
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "Graph API error listing file versions (app-only): {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to list file versions: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get preview URL for a file using app-only authentication.
    /// Returns ephemeral URL that expires in ~10 minutes.
    /// Used for server-side file viewing with correlation ID tracking.
    /// </summary>
    public async Task<FilePreviewDto> GetPreviewUrlAsync(
        string driveId,
        string itemId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        // Task 093: tags go on the caller's request Activity, as always. No `using` — this method did not
        // start the Activity, and disposing it here ended the request's own trace span early (see
        // UploadSessionManager.UploadSmallAsync for the full explanation; same fix, all 20 sites).
        var activity = Activity.Current;
        activity?.SetTag("operation", "GetPreviewUrl");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        if (!string.IsNullOrEmpty(correlationId))
        {
            activity?.SetTag("correlationId", correlationId);
        }

        _logger.LogInformation("[{CorrelationId}] Getting preview URL for {DriveId}/{ItemId} (app-only)",
            correlationId ?? "N/A", driveId, itemId);

        // Ownership outside the try: a refusal is a 404 spe_container_not_owned, never a cached or empty result (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);

        try
        {
            // Call Graph API preview action with default viewer settings
            var previewRequest = new Microsoft.Graph.Drives.Item.Items.Item.Preview.PreviewPostRequestBody();

            var previewResult = await graphClient.Drives[driveId]
                .Items[itemId]
                .Preview
                .PostAsync(previewRequest, cancellationToken: ct);

            if (previewResult == null || string.IsNullOrEmpty(previewResult.GetUrl))
            {
                _logger.LogWarning("[{CorrelationId}] Preview URL not returned for {DriveId}/{ItemId}",
                    correlationId ?? "N/A", driveId, itemId);
                throw new InvalidOperationException($"Failed to get preview URL for item {itemId}");
            }

            _logger.LogInformation("[{CorrelationId}] Preview URL retrieved for {ItemId}, expires in ~10 minutes",
                correlationId ?? "N/A", itemId);

            return new FilePreviewDto(
                PreviewUrl: previewResult.GetUrl,
                PostUrl: previewResult.PostUrl,
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10), // Preview URLs typically expire in ~10 minutes
                ContentType: null // Will be enriched from Document metadata
            );
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogWarning("[{CorrelationId}] File not found: {DriveId}/{ItemId}",
                correlationId ?? "N/A", driveId, itemId);
            throw new FileNotFoundException($"File {itemId} not found in drive {driveId}", ex);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogWarning("[{CorrelationId}] Access denied to file {ItemId}: {Error}",
                correlationId ?? "N/A", itemId, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to file {itemId}", ex);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Graph API error getting preview URL: {Error}",
                correlationId ?? "N/A", ex.Message);
            throw new InvalidOperationException($"Failed to get preview URL: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Unexpected error getting preview URL for {ItemId}",
                correlationId ?? "N/A", itemId);
            throw;
        }
    }

    // =========================================================================
    // App-only (broker) helpers for FileAccessEndpoints (CICD-088b — ADR-007 §1). Each returns the URL or summary
    // the route needs WITHOUT exposing the Graph SDK request builders or DriveItem DTO. The Microsoft.Graph types stay
    // inside this file. unified-access-control-r2 task 171 (owner round 69) converted them from OBO: under OBO SPE
    // only answered a caller who holds a container ROLE, and per-record secure containers have none by design. Every
    // route that calls them authorizes the caller on the sprk_document first (DocumentAuthorizationFilter) and runs
    // the document-pointer check before following the row's pointer.
    // =========================================================================

    /// <summary>
    /// Posts an APP-ONLY preview request and returns the preview URL. <paramref name="additionalData"/> is forwarded to
    /// <c>PreviewPostRequestBody.AdditionalData</c> (e.g. <c>chromeless: true</c>, <c>viewer: "onedrive"</c>).
    /// </summary>
    /// <remarks>
    /// ⚠️ Graph: "anyone who accesses the URL acts as the caller with the caller's permissions" — a preview URL minted
    /// app-only carries the BFF identity's rights for whoever holds it, for its short lifetime. Return it only to the
    /// authorized caller; never log it, store it, or put it in a message. (Task 171 known limit: Microsoft recommends
    /// minting preview URLs with a read-only application identity.)
    /// </remarks>
    public async Task<string?> GetEmbedPreviewUrlAsync(
        string driveId,
        string itemId,
        IDictionary<string, object>? additionalData = null,
        CancellationToken ct = default)
    {
        var previewRequest = new Microsoft.Graph.Drives.Item.Items.Item.Preview.PreviewPostRequestBody
        {
            AdditionalData = additionalData ?? new Dictionary<string, object>()
        };

        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);   // task 227d
        var previewResponse = await graphClient.Drives[driveId]
            .Items[itemId]
            .Preview
            .PostAsync(previewRequest, cancellationToken: ct);

        return previewResponse?.GetUrl;
    }

    /// <summary>
    /// Creates a recipient-openable SPE sharing link for a DriveItem APP-ONLY (email-communication-solution-r5 R2 item
    /// 12 — the composer's "Link" attachments). Mirrors the Graph <c>createLink</c> body used by
    /// <see cref="SpeAdminGraphService.CreateSharingLinkAsync"/>, with the drive already resolved. Returns the sharing
    /// URL, or <see langword="null"/> when Graph returns no link. No Graph SDK types are returned.
    /// </summary>
    /// <remarks>
    /// The route's per-document <c>share</c> gate (task 072) is the authorization; it is unchanged. Task 171 only
    /// changes WHO mints the link: the BFF identity instead of the caller, so a caller with Share on the document no
    /// longer also needs a container role.
    /// </remarks>
    /// <param name="linkType">"view", "edit", or "embed".</param>
    /// <param name="scope">"anonymous", "organization", or "users".</param>
    public async Task<string?> CreateSharingLinkAsync(
        string driveId,
        string itemId,
        string linkType,
        string scope,
        DateTimeOffset? expiration = null,
        CancellationToken ct = default)
    {
        var requestBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateLink.CreateLinkPostRequestBody
        {
            Type = linkType,
            Scope = scope,
            ExpirationDateTime = expiration
        };

        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);   // task 227d
        var permission = await graphClient.Drives[driveId]
            .Items[itemId]
            .CreateLink
            .PostAsync(requestBody, cancellationToken: ct);

        return permission?.Link?.WebUrl;
    }

    /// <summary>
    /// Retrieves drive-item metadata APP-ONLY, projected into a Spaarke-domain <see cref="SpeDriveItemSummary"/> (no
    /// Graph SDK types returned). The <c>webUrl</c> / <c>webDavUrl</c> it returns are POINTERS: opening one in Office
    /// still needs the user's own role on the container (standing BU writer, or the JIT grant on a secure container).
    /// </summary>
    public async Task<SpeDriveItemSummary?> GetDriveItemAsync(
        string driveId,
        string itemId,
        IEnumerable<string>? selectFields = null,
        CancellationToken ct = default)
    {
        // Default select covers everything FileAccessEndpoints currently consumes.
        var fields = (selectFields ?? new[] { "id", "name", "size", "webUrl", "webDavUrl", "file", "parentReference", "lastModifiedDateTime", "createdDateTime" }).ToArray();

        // Ownership outside the try: a refusal is a 404 spe_container_not_owned, never a null summary (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);

        Microsoft.Graph.Models.DriveItem? item;
        try
        {
            item = await graphClient.Drives[driveId]
                .Items[itemId]
                .GetAsync(req =>
                {
                    req.QueryParameters.Select = fields;
                }, cancellationToken: ct);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (item is null) return null;

        return new SpeDriveItemSummary(
            Id: item.Id ?? string.Empty,
            Name: item.Name ?? string.Empty,
            Size: item.Size,
            WebUrl: item.WebUrl,
            WebDavUrl: item.WebDavUrl,
            MimeType: item.File?.MimeType,
            ParentReferencePath: item.ParentReference?.Path,
            LastModifiedDateTime: item.LastModifiedDateTime,
            CreatedDateTime: item.CreatedDateTime);
    }

    /// <summary>
    /// FR-01 (task 012): resolves an absolute document URL — in practice <c>Office.context.document.url</c> — to
    /// the SPE drive item it names, via Graph <c>GET /shares/u!{base64url}/driveItem</c>, AS THE CALLER (OBO).
    /// </summary>
    /// <remarks>
    /// <para><b>OBO, not app-only, deliberately.</b> Graph then resolves only files the caller can already reach, so
    /// the identity route cannot be used to look up files the caller has no access to. It is also the only kind of
    /// identity that can read SPE at all: the owning app or a container-type-REGISTERED app. A <c>/shares</c> call
    /// from az CLI or Graph Explorer 403s whatever the URL (spike-1 §20).</para>
    /// <para>Every Graph answer is classified by <see cref="ResolveAcrossFormsAsync"/>. OBO token-exchange failures
    /// propagate, exactly as they do for the sibling OBO helpers above.</para>
    /// </remarks>
    public async Task<SpeSharedItemResolution> ResolveSharedItemAsUserAsync(
        HttpContext ctx,
        Uri documentUrl,
        CancellationToken ct = default)
    {
        var graphClient = await _factory.ForUserAsync(ctx, ct);

        var resolution = await ResolveAcrossFormsAsync(
            SharingUrlToken.BuildCandidates(documentUrl),
            async (url, token) =>
            {
                var item = await graphClient.Shares[SharingUrlToken.Encode(url)]
                    .DriveItem
                    .GetAsync(req => req.QueryParameters.Select = new[] { "id", "parentReference" }, cancellationToken: token);
                return (item?.Id, item?.ParentReference?.DriveId);
            },
            _logger,
            ct);

        // One line per resolution, carrying each form's status — the running evidence for which encoding Graph
        // accepts over SPE paths. Logs the host, not the URL: the path carries the file name.
        _logger.LogInformation(
            "Graph /shares resolution {Outcome} | Host: {Host} | Attempts: {Attempts}",
            resolution.Outcome,
            documentUrl.Host,
            string.Join("; ", resolution.Attempts.Select(a =>
                $"{a.Form}={a.StatusCode?.ToString() ?? "none"}{(a.ErrorCode is null ? "" : "/" + a.ErrorCode)}")));

        return resolution;
    }

    /// <summary>
    /// Tries each encoding form in turn and classifies every answer. Separated from the Graph call so the
    /// classification is tested through its contract with a fake fetch — a transport mock is banned (ADR-038 B1).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>A drive item with both ids → <see cref="SpeSharedItemOutcome.Resolved"/>.</item>
    /// <item>400 / 404 → try the next form; on every form → <see cref="SpeSharedItemOutcome.NotFound"/>.</item>
    /// <item>403 → try the next form; if none resolves → <see cref="SpeSharedItemOutcome.AccessDenied"/>.</item>
    /// <item>401 (it describes the token, not the item), 429, 5xx, an error body Kiota could not parse as OData, a
    /// Polly timeout or open circuit, a transport failure, or an HttpClient timeout →
    /// <see cref="SpeSharedItemOutcome.Unavailable"/>, at once.</item>
    /// <item>A 200 without both ids → try the next form; if none resolves → Unavailable, not NotFound: Graph found
    /// something and would not describe it.</item>
    /// </list>
    /// A cancellation the caller requested propagates. So does any other exception: an unexpected fault is a defect
    /// to surface as a 500, not an outage to report as a 503.
    /// </remarks>
    public static async Task<SpeSharedItemResolution> ResolveAcrossFormsAsync(
        IReadOnlyList<(string Form, string Url)> candidates,
        Func<string, CancellationToken, Task<(string? ItemId, string? DriveId)>> fetch,
        ILogger logger,
        CancellationToken ct)
    {
        var attempts = new List<SpeSharedItemAttempt>();
        var accessDenied = false;
        var incomplete = false;

        foreach (var (form, url) in candidates)
        {
            try
            {
                var (itemId, driveId) = await fetch(url, ct);
                if (!string.IsNullOrEmpty(itemId) && !string.IsNullOrEmpty(driveId))
                {
                    attempts.Add(new SpeSharedItemAttempt(form, 200, null));
                    return new SpeSharedItemResolution(SpeSharedItemOutcome.Resolved, driveId, itemId, form, attempts);
                }

                incomplete = true;
                attempts.Add(new SpeSharedItemAttempt(form, 200, "incomplete_drive_item"));
            }
            // ODataError derives from ApiException; Kiota throws the base type when an error body is empty or not OData.
            catch (Microsoft.Kiota.Abstractions.ApiException ex)
            {
                attempts.Add(new SpeSharedItemAttempt(form, ex.ResponseStatusCode, (ex as ODataError)?.Error?.Code));

                if (ex.ResponseStatusCode is 400 or 404)
                    continue;

                if (ex.ResponseStatusCode == 403)
                {
                    accessDenied = true;
                    continue;
                }

                logger.LogWarning(ex, "Graph /shares resolution unavailable ({Form}, status {Status})",
                    form, ex.ResponseStatusCode);
                return Unresolved(SpeSharedItemOutcome.Unavailable, attempts);
            }
            catch (Exception ex) when (IsSharesOutage(ex, ct))
            {
                attempts.Add(new SpeSharedItemAttempt(form, null, ex.GetType().Name));
                logger.LogWarning(ex, "Graph /shares resolution unavailable ({Form}, {Failure})", form, ex.GetType().Name);
                return Unresolved(SpeSharedItemOutcome.Unavailable, attempts);
            }
        }

        var outcome = incomplete ? SpeSharedItemOutcome.Unavailable
            : accessDenied ? SpeSharedItemOutcome.AccessDenied
            : SpeSharedItemOutcome.NotFound;
        return Unresolved(outcome, attempts);
    }

    private static SpeSharedItemResolution Unresolved(SpeSharedItemOutcome outcome, List<SpeSharedItemAttempt> attempts)
        => new(outcome, null, null, null, attempts);

    /// <summary>
    /// Outages, as opposed to answers: a transport failure, the Graph pipeline's Polly timeout or open circuit
    /// (<c>GraphHttpMessageHandler</c>), or an HttpClient timeout — a cancellation the CALLER did not request.
    /// </summary>
    private static bool IsSharesOutage(Exception ex, CancellationToken ct) => ex switch
    {
        HttpRequestException => true,
        global::Polly.Timeout.TimeoutRejectedException => true,
        global::Polly.CircuitBreaker.BrokenCircuitException => true,
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false,
    };

    /// <summary>
    /// Reads the SharePoint Embedded <c>quickXorHash</c> content identity for a persisted drive item
    /// (app-only), via <c>GET /drives/{driveId}/items/{itemId}?$select=file</c> → <c>file.hashes.quickXorHash</c>.
    /// This keeps the <c>Microsoft.Graph</c> hash facet inside the ADR-007 Infrastructure boundary — callers
    /// (the content-dedup detector) receive only the hash string. FR-C3 content-dedup identity.
    /// </summary>
    /// <remarks>
    /// <para><c>quickXorHash</c> is the SPE content identity; <c>sha256Hash</c> is DEPRECATED on SPE and MUST NOT
    /// be used. <c>crc32</c>/<c>sha1</c> are consumer-OneDrive only.</para>
    /// <para>Best-effort / non-fatal (NFR-04): returns null when the item is missing, the hash facet is not yet
    /// populated (large/chunked uploads may lag), or any Graph call fails — the caller treats a null hash as
    /// "no dedup, proceed". Never throws — except the ownership refusal (404, task 227d), which is raised before
    /// the try. The content-dedup detector, which never throws (NFR-04), still reads that refusal as "no dedup"; it
    /// is harmless there because the item it hashes was written through the same guard.</para>
    /// </remarks>
    public async Task<string?> GetQuickXorHashAsync(string driveId, string itemId, CancellationToken ct = default)
    {
        // Ownership outside the try: a refusal is a 404 spe_container_not_owned, never a cached or empty result (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(driveId, ct);

        try
        {
            var item = await graphClient.Drives[driveId].Items[itemId]
                .GetAsync(req => req.QueryParameters.Select = new[] { "id", "file" }, cancellationToken: ct);

            var hash = item?.File?.Hashes?.QuickXorHash;
            if (string.IsNullOrWhiteSpace(hash))
            {
                _logger.LogDebug(
                    "quickXorHash not available for item {ItemId} in drive {DriveId} (absent or not yet populated).",
                    itemId, driveId);
                return null;
            }
            return hash;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read quickXorHash for item {ItemId} in drive {DriveId} (non-fatal — dedup skipped).",
                itemId, driveId);
            return null;
        }
    }
}
