using Services;
using Spe.Bff.Api.Models;
using Microsoft.Graph.Models;

namespace Spe.Bff.Api.Tests.Mocks;

public class MockOboSpeService : IOboSpeService
{
    public Task<UserInfoResponse?> GetUserInfoAsync(string userBearer, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<UserInfoResponse?>(null);

        var userInfo = new UserInfoResponse
        {
            DisplayName = "Test User",
            UserPrincipalName = "test.user@test.com",
            Oid = "test-oid-123"
        };
        return Task.FromResult<UserInfoResponse?>(userInfo);
    }

    public Task<UserCapabilitiesResponse?> GetUserCapabilitiesAsync(string userBearer, string containerId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<UserCapabilitiesResponse?>(null);

        var capabilities = new UserCapabilitiesResponse
        {
            CanRead = true,
            CanWrite = true,
            CanDelete = false,
            CanManage = false
        };
        return Task.FromResult<UserCapabilitiesResponse?>(capabilities);
    }

    public Task<ListingResponse?> ListChildrenAsync(string userBearer, string id, ListingParameters parameters, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<ListingResponse?>(null);

        var response = new ListingResponse
        {
            Items = new List<DriveItem>(),
            TotalCount = 0,
            HasNextPage = false,
            NextPageToken = null
        };
        return Task.FromResult<ListingResponse?>(response);
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

    public Task<UploadSession?> CreateUploadSessionAsync(string userBearer, string driveId, string path, ConflictBehavior behavior, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<UploadSession?>(null);

        var session = new UploadSession
        {
            UploadUrl = "https://fake-upload-url.com/session-123"
        };
        return Task.FromResult<UploadSession?>(session);
    }

    public Task<UploadChunkResult> UploadChunkAsync(string userBearer, string uploadUrl, string contentRange, byte[] chunkData, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
        {
            return Task.FromResult(new UploadChunkResult { StatusCode = 401 });
        }

        // Simulate successful chunk upload
        var result = new UploadChunkResult
        {
            StatusCode = 202, // More chunks expected
            CompletedItem = null
        };
        return Task.FromResult(result);
    }

    public Task<DriveItem?> UpdateItemAsync(string userBearer, string driveId, string itemId, UpdateFileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<DriveItem?>(null);

        var item = new DriveItem
        {
            Id = itemId,
            Name = request.Name ?? "updated-item",
            ParentReference = new ItemReference { Id = request.ParentReferenceId }
        };
        return Task.FromResult<DriveItem?>(item);
    }

    public Task<FileContentResponse?> DownloadContentWithRangeAsync(string userBearer, string driveId, string itemId, RangeHeader? range, string? ifNoneMatch, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult<FileContentResponse?>(null);

        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Mock file content"));
        var response = new FileContentResponse
        {
            Content = content,
            ContentType = "text/plain",
            ContentLength = content.Length,
            ETag = "mock-etag-123",
            IsRangeRequest = range != null,
            ContentRangeHeader = range != null ? $"bytes 0-{content.Length - 1}/{content.Length}" : null
        };
        return Task.FromResult<FileContentResponse?>(response);
    }

    public Task<bool> DeleteItemAsync(string userBearer, string driveId, string itemId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userBearer) || userBearer == "invalid-token")
            return Task.FromResult(false);

        return Task.FromResult(true);
    }
}