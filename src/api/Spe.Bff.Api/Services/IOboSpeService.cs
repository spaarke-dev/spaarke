using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;

namespace Services;

public interface IOboSpeService
{
    Task<IList<DriveItem>> ListChildrenAsync(string userBearer, string containerId, CancellationToken ct);
    Task<ListingResponse> ListChildrenAsync(string userBearer, string containerId, ListingParameters parameters, CancellationToken ct);
    Task<IResult?> DownloadContentAsync(string userBearer, string driveId, string itemId, CancellationToken ct);
    Task<DriveItem?> UploadSmallAsync(string userBearer, string containerId, string path, Stream content, CancellationToken ct);
    Task<UserInfoResponse?> GetUserInfoAsync(string userBearer, CancellationToken ct);
    Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(string userBearer, string containerId, CancellationToken ct);
    Task<UploadSessionResponse?> CreateUploadSessionAsync(string userBearer, string driveId, string path, ConflictBehavior conflictBehavior, CancellationToken ct);
    Task<ChunkUploadResponse> UploadChunkAsync(string userBearer, string uploadSessionUrl, string contentRange, byte[] chunkData, CancellationToken ct);
    Task<DriveItemDto?> UpdateItemAsync(string userBearer, string driveId, string itemId, UpdateFileRequest request, CancellationToken ct);
    Task<bool> DeleteItemAsync(string userBearer, string driveId, string itemId, CancellationToken ct);
    Task<FileContentResponse?> DownloadContentWithRangeAsync(string userBearer, string driveId, string itemId, RangeHeader? range, string? ifNoneMatch, CancellationToken ct);
}