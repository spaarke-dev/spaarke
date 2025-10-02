using Spe.Bff.Api.Services;
using Spe.Bff.Api.Models;
using Microsoft.Graph.Models;
using Microsoft.AspNetCore.Http;

namespace Spe.Bff.Api.Tests.Mocks;

public class MockOboSpeService : IOboSpeService
{
    public Task<UserInfoResponse?> GetUserInfoAsync(string userBearer, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<UserInfoResponse?>(null);

        var userInfo = new UserInfoResponse(
            "Test User",
            "test.user@test.com",
            "test-oid-123"
        );
        return Task.FromResult<UserInfoResponse?>(userInfo);
    }

    public Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(string userBearer, string containerId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            throw new UnauthorizedAccessException("Invalid bearer token");

        var capabilities = new UserCapabilitiesResponse(
            true,  // Read
            true,  // Write
            false, // Delete
            false  // CreateFolder
        );
        return Task.FromResult(capabilities);
    }

    public Task<ListingResponse> ListChildrenAsync(string userBearer, string containerId, ListingParameters parameters, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            throw new UnauthorizedAccessException("Invalid bearer token");

        var response = new ListingResponse(
            new List<DriveItemDto>(),
            null // NextLink
        );
        return Task.FromResult(response);
    }

    public Task<DriveItem?> UploadSmallAsync(string userBearer, string id, string path, Stream content, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<DriveItem?>(null);

        var item = new DriveItem
        {
            Id = "test-item-id",
            Name = Path.GetFileName(path),
            Size = content.Length
        };
        return Task.FromResult<DriveItem?>(item);
    }

    public Task<UploadSessionResponse?> CreateUploadSessionAsync(string userBearer, string driveId, string path, ConflictBehavior conflictBehavior, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<UploadSessionResponse?>(null);

        var session = new UploadSessionResponse(
            "https://fake-upload-url.com/session-123",
            DateTimeOffset.UtcNow.AddHours(1)
        );
        return Task.FromResult<UploadSessionResponse?>(session);
    }

    public Task<ChunkUploadResponse> UploadChunkAsync(string userBearer, string uploadSessionUrl, string contentRange, byte[] chunkData, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
        {
            return Task.FromResult(new ChunkUploadResponse(401, null));
        }

        // Simulate successful chunk upload
        var result = new ChunkUploadResponse(202, null); // More chunks expected
        return Task.FromResult(result);
    }

    public Task<DriveItemDto?> UpdateItemAsync(string userBearer, string driveId, string itemId, UpdateFileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<DriveItemDto?>(null);

        var item = new DriveItemDto(
            itemId,
            request.Name ?? "updated-item",
            1024L, // Size
            "mock-etag",
            DateTimeOffset.UtcNow,
            "application/octet-stream",
            null // Not a folder
        );
        return Task.FromResult<DriveItemDto?>(item);
    }

    public Task<FileContentResponse?> DownloadContentWithRangeAsync(string userBearer, string driveId, string itemId, RangeHeader? range, string? ifNoneMatch, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<FileContentResponse?>(null);

        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Mock file content"));
        var response = new FileContentResponse(
            content,
            content.Length,
            "text/plain",
            "mock-etag-123",
            range?.Start,
            range?.End,
            content.Length
        );
        return Task.FromResult<FileContentResponse?>(response);
    }

    public Task<bool> DeleteItemAsync(string userBearer, string driveId, string itemId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    public Task<IList<DriveItem>> ListChildrenAsync(string userBearer, string containerId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            throw new UnauthorizedAccessException("Invalid bearer token");

        var items = new List<DriveItem>();
        return Task.FromResult<IList<DriveItem>>(items);
    }

    public Task<IResult?> DownloadContentAsync(string userBearer, string driveId, string itemId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<IResult?>(null);

        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Mock file content"));
        var result = Results.File(content, "text/plain", "mock-file.txt");
        return Task.FromResult<IResult?>(result);
    }
}