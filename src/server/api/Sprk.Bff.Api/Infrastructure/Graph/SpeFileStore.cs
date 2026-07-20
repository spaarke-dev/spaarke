using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Facade for SharePoint Embedded file operations.
/// Delegates to specialized operation classes for better maintainability.
/// Refactored from 604-line god class to cohesive modules (Task 3.2).
/// </summary>
public class SpeFileStore : ISpeFileOperations
{
    private readonly ContainerOperations _containerOps;
    private readonly DriveItemOperations _driveItemOps;
    private readonly UploadSessionManager _uploadManager;
    private readonly UserOperations _userOps;

    // Optional so existing 4-arg construction (unit tests, legacy callers) keeps compiling.
    // The DI container (DocumentsModule: AddScoped&lt;SpeFileStore&gt;) resolves the registered
    // IGraphClientFactory into this slot, enabling the FR-26 subscription/delta facade.
    private readonly IGraphClientFactory? _graphClientFactory;

    public SpeFileStore(
        ContainerOperations containerOps,
        DriveItemOperations driveItemOps,
        UploadSessionManager uploadManager,
        UserOperations userOps,
        IGraphClientFactory? graphClientFactory = null)
    {
        _containerOps = containerOps ?? throw new ArgumentNullException(nameof(containerOps));
        _driveItemOps = driveItemOps ?? throw new ArgumentNullException(nameof(driveItemOps));
        _uploadManager = uploadManager ?? throw new ArgumentNullException(nameof(uploadManager));
        _userOps = userOps ?? throw new ArgumentNullException(nameof(userOps));
        _graphClientFactory = graphClientFactory;
    }

    // Container Operations - delegate to ContainerOperations
    public Task<ContainerDto?> CreateContainerAsync(
        Guid containerTypeId,
        string displayName,
        string? description = null,
        CancellationToken ct = default)
        => _containerOps.CreateContainerAsync(containerTypeId, displayName, description, ct);

    public Task<ContainerDto?> GetContainerDriveAsync(string containerId, CancellationToken ct = default)
        => _containerOps.GetContainerDriveAsync(containerId, ct);

    public Task<IList<ContainerDto>?> ListContainersAsync(Guid containerTypeId, CancellationToken ct = default)
        => _containerOps.ListContainersAsync(containerTypeId, ct);

    // Upload Operations - delegate to UploadSessionManager.
    // `virtual` enables module-boundary test doubles (Moq) of this concrete facade — the established
    // codebase idiom for non-mockable facades (cf. DocumentCheckoutService, ChatSessionManager seams).
    public virtual Task<FileHandleDto?> UploadSmallAsync(
        string driveId,
        string path,
        Stream content,
        CancellationToken ct = default)
        => _uploadManager.UploadSmallAsync(driveId, path, content, ct);

    public Task<UploadSessionDto?> CreateUploadSessionAsync(
        string containerId,
        string path,
        CancellationToken ct = default)
        => _uploadManager.CreateUploadSessionAsync(containerId, path, ct);

    public Task<HttpResponseMessage> UploadChunkAsync(
        UploadSessionDto session,
        Stream file,
        long start,
        long length,
        CancellationToken ct = default)
        => _uploadManager.UploadChunkAsync(session, file, start, length, ct);

    // Drive Item Operations - delegate to DriveItemOperations
    public Task<IList<FileHandleDto>> ListChildrenAsync(
        string driveId,
        string? itemId = null,
        CancellationToken ct = default)
        => _driveItemOps.ListChildrenAsync(driveId, itemId, ct);

    // `virtual` enables module-boundary test doubles (Moq) of this concrete facade (see UploadSmallAsync).
    public virtual Task<Stream?> DownloadFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.DownloadFileAsync(driveId, itemId, ct);

    public Task<bool> DeleteFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.DeleteFileAsync(driveId, itemId, ct);

    public Task<FileHandleDto?> GetFileMetadataAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.GetFileMetadataAsync(driveId, itemId, ct);

    public Task<FileHandleDto?> GetFileMetadataAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.GetFileMetadataAsUserAsync(ctx, driveId, itemId, ct);

    public Task<Stream?> DownloadFileAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.DownloadFileAsUserAsync(ctx, driveId, itemId, ct);

    public Task<Stream?> DownloadFileVersionAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        string versionId,
        CancellationToken ct = default)
        => _driveItemOps.DownloadFileVersionAsUserAsync(ctx, driveId, itemId, versionId, ct);

    public Task<FilePreviewDto> GetPreviewUrlAsync(
        string driveId,
        string itemId,
        string? correlationId = null,
        CancellationToken ct = default)
        => _driveItemOps.GetPreviewUrlAsync(driveId, itemId, correlationId, ct);

    /// <summary>
    /// Resolve a container ID to its drive ID.
    /// Drive IDs start with "b!" (base64-encoded SharePoint site reference).
    /// Container IDs are GUIDs like "a1234567-89ab-cdef-0123-456789abcdef".
    /// If the input is already a drive ID, returns it unchanged.
    /// </summary>
    public async Task<string> ResolveDriveIdAsync(string containerOrDriveId, CancellationToken ct = default)
    {
        // Drive IDs from SharePoint typically start with "b!" (base64-encoded site reference)
        // If it already starts with "b!", it's a drive ID - return as-is
        if (containerOrDriveId.StartsWith("b!", StringComparison.OrdinalIgnoreCase))
        {
            return containerOrDriveId;
        }

        // Otherwise, it might be a container ID (GUID format) - try to resolve it
        var containerDrive = await _containerOps.GetContainerDriveAsync(containerOrDriveId, ct);
        if (containerDrive == null)
        {
            throw new InvalidOperationException($"Could not resolve container {containerOrDriveId} to drive ID");
        }

        return containerDrive.Id;
    }

    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================
    // All methods delegate to specialized operation classes.
    // These methods accept userToken and use OBO authentication flow.

    // Container Operations (user context)
    public Task<IList<ContainerDto>> ListContainersAsUserAsync(
        HttpContext ctx,
        Guid containerTypeId,
        CancellationToken ct = default)
        => _containerOps.ListContainersAsUserAsync(ctx, containerTypeId, ct);

    // Drive Item Operations (user context)
    public Task<ListingResponse> ListChildrenAsUserAsync(
        HttpContext ctx,
        string containerId,
        ListingParameters parameters,
        CancellationToken ct = default)
        => _driveItemOps.ListChildrenAsUserAsync(ctx, containerId, parameters, ct);

    public Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        RangeHeader? range,
        string? ifNoneMatch,
        CancellationToken ct = default)
        => _driveItemOps.DownloadFileWithRangeAsUserAsync(ctx, driveId, itemId, range, ifNoneMatch, ct);

    public Task<DriveItemDto?> UpdateItemAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        UpdateFileRequest request,
        CancellationToken ct = default)
        => _driveItemOps.UpdateItemAsUserAsync(ctx, driveId, itemId, request, ct);

    public Task<bool> DeleteItemAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.DeleteItemAsUserAsync(ctx, driveId, itemId, ct);

    // Upload Operations (user context)
    public Task<FileHandleDto?> UploadSmallAsUserAsync(
        HttpContext ctx,
        string containerId,
        string path,
        Stream content,
        CancellationToken ct = default)
        => _uploadManager.UploadSmallAsUserAsync(ctx, containerId, path, content, ct);

    public Task<FileHandleDto?> ReplaceFileContentAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        Stream content,
        CancellationToken ct = default)
        => _uploadManager.ReplaceFileContentAsUserAsync(ctx, driveId, itemId, content, ct);

    public Task<FileHandleDto?> ReplaceFileContentAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        Stream content,
        string? ifMatch,
        CancellationToken ct = default)
        => _uploadManager.ReplaceFileContentAsUserAsync(ctx, driveId, itemId, content, ifMatch, ct);

    public Task<UploadSessionResponse?> CreateUploadSessionAsUserAsync(
        HttpContext ctx,
        string driveId,
        string path,
        ConflictBehavior conflictBehavior,
        CancellationToken ct = default)
        => _uploadManager.CreateUploadSessionAsUserAsync(ctx, driveId, path, conflictBehavior, ct);

    public Task<ChunkUploadResponse> UploadChunkAsUserAsync(
        string userToken,
        string uploadSessionUrl,
        string contentRange,
        byte[] chunkData,
        CancellationToken ct = default)
        => _uploadManager.UploadChunkAsUserAsync(userToken, uploadSessionUrl, contentRange, chunkData, ct);

    // User Operations
    public Task<UserInfoResponse?> GetUserInfoAsync(
        HttpContext ctx,
        CancellationToken ct = default)
        => _userOps.GetUserInfoAsync(ctx, ct);

    public Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(
        HttpContext ctx,
        string containerId,
        CancellationToken ct = default)
        => _userOps.GetUserCapabilitiesAsync(ctx, containerId, ct);

    // =========================================================================
    // OBO-context facades for FileAccessEndpoints (CICD-088b — ADR-007 §1)
    // Delegate to DriveItemOperations so the Microsoft.Graph types stay in
    // Infrastructure.Graph and never appear in endpoint IL.
    // Added 2026-06-26 by ci-cd-unit-test-remediation-r1 task CICD-088b.
    // =========================================================================

    public Task<string?> GetPreviewUrlAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        IDictionary<string, object>? additionalData = null,
        CancellationToken ct = default)
        => _driveItemOps.GetPreviewUrlAsUserAsync(ctx, driveId, itemId, additionalData, ct);

    public Task<SpeDriveItemSummary?> GetDriveItemAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        IEnumerable<string>? selectFields = null,
        CancellationToken ct = default)
        => _driveItemOps.GetDriveItemAsUserAsync(ctx, driveId, itemId, selectFields, ct);

    public Task<Stream?> GetContentStreamAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.GetContentStreamAsUserAsync(ctx, driveId, itemId, ct);

    // =========================================================================
    // SPE change-detection facade (spaarkeai-compose-r2 FR-26, task 052)
    //
    // ADR-007: this is the ONLY place the Graph subscription/delta SDK types live.
    // Callers above the facade (Services/Compose/SpeSyncOrchestrator) receive DTOs.
    // App-only (managed identity, ADR-028) via IGraphClientFactory.ForApp().
    // API shape mirrors the proven Services/Communication/GraphSubscriptionManager.
    // =========================================================================

    /// <inheritdoc />
    public async Task<SpeSubscriptionDto> CreateDriveRootSubscriptionAsync(
        string driveId,
        string notificationUrl,
        string clientState,
        DateTimeOffset expirationDateTime,
        CancellationToken ct = default)
    {
        var graph = RequireGraphForApp();
        var subscription = new Subscription
        {
            ChangeType = "updated",
            NotificationUrl = notificationUrl,
            Resource = $"drives/{driveId}/root",
            ExpirationDateTime = expirationDateTime,
            ClientState = clientState
        };

        var created = await graph.Subscriptions.PostAsync(subscription, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Graph returned a null subscription creating a webhook for drive {driveId}.");

        return MapSubscription(created);
    }

    /// <inheritdoc />
    public async Task<SpeSubscriptionDto> RenewSubscriptionAsync(
        string subscriptionId,
        DateTimeOffset newExpirationDateTime,
        CancellationToken ct = default)
    {
        var graph = RequireGraphForApp();
        var renewal = new Subscription { ExpirationDateTime = newExpirationDateTime };

        var updated = await graph.Subscriptions[subscriptionId]
            .PatchAsync(renewal, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Graph returned a null subscription renewing {subscriptionId}.");

        return MapSubscription(updated);
    }

    /// <inheritdoc />
    public async Task DeleteSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        var graph = RequireGraphForApp();
        await graph.Subscriptions[subscriptionId].DeleteAsync(cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SpeDeltaResult> EnumerateDriveDeltaAsync(
        string driveId,
        string? deltaLink,
        CancellationToken ct = default)
    {
        var graph = RequireGraphForApp();
        var changes = new List<SpeDriveChange>();

        // Initial call: /drives/{id}/items/root/delta. Subsequent rounds replay the stored
        // @odata.deltaLink (which carries the token). Page through @odata.nextLink until the
        // response carries a terminal @odata.deltaLink — that becomes the advanced token.
        var response = deltaLink is null
            ? await graph.Drives[driveId].Items["root"].Delta
                .GetAsDeltaGetResponseAsync(cancellationToken: ct).ConfigureAwait(false)
            : await graph.Drives[driveId].Items["root"].Delta
                .WithUrl(deltaLink)
                .GetAsDeltaGetResponseAsync(cancellationToken: ct).ConfigureAwait(false);

        string? advancedDeltaLink = null;

        while (response is not null)
        {
            if (response.Value is not null)
            {
                foreach (var item in response.Value)
                {
                    if (!string.IsNullOrEmpty(item.Id))
                    {
                        changes.Add(new SpeDriveChange(
                            ItemId: item.Id!,
                            Name: item.Name,
                            ETag: item.ETag,
                            Deleted: item.Deleted is not null));
                    }
                }
            }

            if (!string.IsNullOrEmpty(response.OdataDeltaLink))
            {
                advancedDeltaLink = response.OdataDeltaLink;
                break;
            }

            if (string.IsNullOrEmpty(response.OdataNextLink))
            {
                break;
            }

            response = await graph.Drives[driveId].Items["root"].Delta
                .WithUrl(response.OdataNextLink)
                .GetAsDeltaGetResponseAsync(cancellationToken: ct).ConfigureAwait(false);
        }

        return new SpeDeltaResult(changes, advancedDeltaLink);
    }

    private GraphServiceClient RequireGraphForApp()
        => (_graphClientFactory ?? throw new InvalidOperationException(
                "SpeFileStore was constructed without an IGraphClientFactory; SPE subscription/delta " +
                "operations require app-only Graph access. Resolve SpeFileStore from DI."))
            .ForApp();

    private static SpeSubscriptionDto MapSubscription(Subscription subscription)
        => new(
            SubscriptionId: subscription.Id
                ?? throw new InvalidOperationException("Graph subscription is missing its id."),
            Resource: subscription.Resource ?? string.Empty,
            ExpirationDateTime: subscription.ExpirationDateTime
                ?? throw new InvalidOperationException("Graph subscription is missing its expirationDateTime."));
}
