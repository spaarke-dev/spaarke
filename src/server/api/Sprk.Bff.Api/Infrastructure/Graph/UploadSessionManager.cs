using System.Diagnostics;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Handles file upload operations including small files and chunked uploads.
/// Responsible for upload session management and chunk processing.
/// </summary>
public class UploadSessionManager
{
    private readonly IGraphClientFactory _factory;
    private readonly ILogger<UploadSessionManager> _logger;

    public UploadSessionManager(IGraphClientFactory factory, ILogger<UploadSessionManager> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FileHandleDto?> UploadSmallAsync(
        string driveId,
        string path,
        Stream content,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "UploadSmall");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("filePath", path);

        _logger.LogInformation("Uploading small file to drive {DriveId} at path {Path}",
            driveId, path);

        try
        {
            var graphClient = _factory.ForApp();

            // Upload the file using PUT to drive item content endpoint
            var item = await graphClient.Drives[driveId].Root
                .ItemWithPath(path)
                .Content
                .PutAsync(content, cancellationToken: ct);

            if (item == null)
            {
                _logger.LogError("Failed to upload file - Graph API returned null");
                return null;
            }

            _logger.LogInformation("Successfully uploaded file {ItemId} to drive {DriveId}",
                item.Id, driveId);

            return new FileHandleDto(
                item.Id!,
                item.Name!,
                item.ParentReference?.Id,
                item.Size,
                item.CreatedDateTime ?? DateTimeOffset.UtcNow,
                item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                item.ETag,
                item.Folder != null,
                item.WebUrl);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Drive {DriveId} not found", driveId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error uploading file: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to upload file: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading file: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<UploadSessionDto?> CreateUploadSessionAsync(
        string containerId,
        string path,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "CreateUploadSession");
        activity?.SetTag("containerId", containerId);
        activity?.SetTag("filePath", path);

        _logger.LogInformation("Creating upload session for container {ContainerId} at path {Path}",
            containerId, path);

        try
        {
            var graphClient = _factory.ForApp();

            // First, get the drive for this container
            var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive
                .GetAsync(cancellationToken: ct);

            if (drive?.Id == null)
            {
                _logger.LogError("Failed to get drive for container {ContainerId}", containerId);
                return null;
            }

            var createUploadSessionPostRequestBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "@microsoft.graph.conflictBehavior", "rename" }
                    }
                }
            };

            var session = await graphClient.Drives[drive.Id].Root
                .ItemWithPath(path)
                .CreateUploadSession
                .PostAsync(createUploadSessionPostRequestBody, cancellationToken: ct);

            if (session == null)
            {
                _logger.LogError("Failed to create upload session - Graph API returned null");
                return null;
            }

            _logger.LogInformation("Created upload session {UploadUrl} for file {Path}",
                session.UploadUrl, path);

            return new UploadSessionDto(
                session.UploadUrl!,
                session.ExpirationDateTime ?? DateTimeOffset.UtcNow.AddHours(24));
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Container {ContainerId} not found", containerId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error creating upload session: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to create upload session: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating upload session: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<HttpResponseMessage> UploadChunkAsync(
        UploadSessionDto session,
        Stream file,
        long start,
        long length,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "UploadChunk");
        activity?.SetTag("start", start);
        activity?.SetTag("length", length);

        _logger.LogInformation("Uploading chunk from {Start} to {End}", start, start + length - 1);

        try
        {
            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Put, session.UploadUrl);

            // Read chunk data
            var buffer = new byte[length];
            var bytesRead = await file.ReadAsync(buffer, 0, (int)length, ct);

            if (bytesRead != length)
            {
                _logger.LogWarning("Read {BytesRead} bytes but expected {Length}", bytesRead, length);
            }

            request.Content = new ByteArrayContent(buffer, 0, bytesRead);
            request.Content.Headers.ContentLength = bytesRead;
            request.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, start + bytesRead - 1);

            var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                _logger.LogWarning("Chunk upload returned status {StatusCode}", response.StatusCode);
            }
            else
            {
                _logger.LogInformation("Successfully uploaded chunk from {Start} to {End}", start, start + bytesRead - 1);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading chunk: {Error}", ex.Message);
            throw;
        }
    }

    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================

    /// <summary>
    /// Uploads a small file (< 4MB) as the user (OBO flow).
    /// </summary>
    public async Task<FileHandleDto?> UploadSmallAsUserAsync(
        HttpContext ctx,
        string containerId,
        string path,
        Stream content,
        CancellationToken ct = default)
    {
        try
        {
            var graphClient = await _factory.ForUserAsync(ctx, ct);

            _logger.LogInformation("Uploading file as user to container {ContainerId}, path {Path}", containerId, path);

            // Validate content size (small upload < 4MB)
            if (content.CanSeek && content.Length > 4 * 1024 * 1024)
            {
                _logger.LogWarning("Content too large for small upload: {Size} bytes (max 4MB)", content.Length);
                throw new ArgumentException("Content size exceeds 4MB limit for small uploads. Use chunked upload instead.");
            }

            // For SharePoint Embedded: Container ID = Drive ID (per Microsoft documentation)
            // Use container ID directly with OBO credentials (user has access, App-Only might not)
            // Reference: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/containertypes
            _logger.LogDebug("Using container ID as drive ID for SPE OBO upload");

            var uploadedItem = await graphClient.Drives[containerId].Root
                .ItemWithPath(path)
                .Content
                .PutAsync(content, cancellationToken: ct);

            if (uploadedItem == null)
            {
                _logger.LogError("Upload failed for path {Path} in container {ContainerId}", path, containerId);
                return null;
            }

            _logger.LogInformation("Successfully uploaded file to {Path} in container {ContainerId}, item ID: {ItemId}",
                path, containerId, uploadedItem.Id);

            // Map Graph SDK DriveItem to SDAP DTO (ADR-007 compliance)
            return new FileHandleDto(
                uploadedItem.Id!,
                uploadedItem.Name!,
                uploadedItem.ParentReference?.Id,
                uploadedItem.Size,
                uploadedItem.CreatedDateTime ?? DateTimeOffset.UtcNow,
                uploadedItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                uploadedItem.ETag,
                uploadedItem.Folder != null,
                uploadedItem.WebUrl);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogError(ex, "Access denied uploading to container {ContainerId}: HTTP {StatusCode} - {Message}",
                containerId, ex.ResponseStatusCode, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to container {containerId}: {ex.Message}", ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 413)
        {
            _logger.LogWarning("Content too large for path {Path}", path);
            throw new ArgumentException("Content size exceeds limit. Use chunked upload for large files.", ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
        {
            _logger.LogWarning("Graph API throttling, retry after {RetryAfter}s",
                ex.ResponseHeaders?.RetryAfter?.Delta?.TotalSeconds ?? 60);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error uploading file: HTTP {StatusCode} - {Message}",
                ex.ResponseStatusCode, ex.Message);
            throw new InvalidOperationException($"Failed to upload file: HTTP {ex.ResponseStatusCode} - {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to path {Path} in container {ContainerId}", path, containerId);
            throw;
        }
    }

    /// <summary>
    /// Creates an upload session for large files as the user (OBO flow).
    /// </summary>
    public async Task<UploadSessionResponse?> CreateUploadSessionAsUserAsync(
        HttpContext ctx,
        string driveId,
        string path,
        ConflictBehavior conflictBehavior,
        CancellationToken ct = default)
    {
        try
        {
            var graphClient = await _factory.ForUserAsync(ctx, ct);

            // Create upload session request
            var uploadSessionRequest = new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["@microsoft.graph.conflictBehavior"] = conflictBehavior.ToString().ToLowerInvariant()
                    }
                }
            };

            // Create upload session via Graph API
            var session = await graphClient.Drives[driveId].Root
                .ItemWithPath(path)
                .CreateUploadSession
                .PostAsync(uploadSessionRequest, cancellationToken: ct);

            if (session == null || string.IsNullOrEmpty(session.UploadUrl))
            {
                _logger.LogError("Failed to create upload session for path {Path}", path);
                return null;
            }

            _logger.LogInformation("Created upload session for drive {DriveId}, path {Path}, conflict behavior {ConflictBehavior}, expires at {ExpirationDateTime}",
                driveId, path, conflictBehavior, session.ExpirationDateTime);

            return new UploadSessionResponse(
                session.UploadUrl,
                session.ExpirationDateTime ?? DateTimeOffset.UtcNow.AddHours(1)
            );
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogWarning("Access denied creating upload session: {Error}", ex.Message);
            throw new UnauthorizedAccessException("Access denied", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create upload session for drive {DriveId}, path {Path}", driveId, path);
            throw;
        }
    }

    /// <summary>
    /// Uploads a chunk to an upload session as the user (OBO flow).
    /// </summary>
    public async Task<ChunkUploadResponse> UploadChunkAsUserAsync(
        string userToken,
        string uploadSessionUrl,
        string contentRange,
        byte[] chunkData,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
            throw new ArgumentException("User access token required", nameof(userToken));

        try
        {
            var range = ContentRangeHeader.Parse(contentRange);
            if (range == null || !range.IsValid)
            {
                _logger.LogWarning("Invalid Content-Range header: {ContentRange}", contentRange);
                return new ChunkUploadResponse(400);
            }

            // Validate chunk size (8-10 MiB as per Graph API requirements)
            const long minChunkSize = 8 * 1024 * 1024;
            const long maxChunkSize = 10 * 1024 * 1024;

            if (chunkData.Length < minChunkSize && (!range.Total.HasValue || range.End + 1 < range.Total.Value))
            {
                _logger.LogWarning("Chunk size {Size} below minimum {MinSize} (not final chunk)", chunkData.Length, minChunkSize);
                return new ChunkUploadResponse(400);
            }

            if (chunkData.Length > maxChunkSize)
            {
                _logger.LogWarning("Chunk size {Size} exceeds maximum {MaxSize}", chunkData.Length, maxChunkSize);
                return new ChunkUploadResponse(413);
            }

            if (chunkData.Length != range.ChunkSize)
            {
                _logger.LogWarning("Chunk data length {ActualSize} does not match Content-Range size {ExpectedSize}",
                    chunkData.Length, range.ChunkSize);
                return new ChunkUploadResponse(400);
            }

            // Upload chunk to Graph API using raw HTTP (SDK doesn't expose chunked upload directly)
            using var httpClient = new HttpClient();
            using var content = new ByteArrayContent(chunkData);
            content.Headers.Add("Content-Range", contentRange);
            content.Headers.ContentLength = chunkData.Length;

            var response = await httpClient.PutAsync(uploadSessionUrl, content, ct);

            // Handle response based on status code
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted) // 202 - more chunks expected
            {
                _logger.LogInformation("Uploaded chunk {Start}-{End} for session {UploadUrl}",
                    range.Start, range.End, uploadSessionUrl);
                return new ChunkUploadResponse(202);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Created ||
                     response.StatusCode == System.Net.HttpStatusCode.OK) // 201/200 - upload complete
            {
                var responseContent = await response.Content.ReadAsStringAsync(ct);
                var driveItem = System.Text.Json.JsonSerializer.Deserialize<DriveItem>(responseContent, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (driveItem == null)
                {
                    _logger.LogError("Failed to deserialize completed upload response");
                    return new ChunkUploadResponse(500);
                }

                var completedItemDto = new DriveItemDto(
                    Id: driveItem.Id!,
                    Name: driveItem.Name!,
                    Size: driveItem.Size,
                    ETag: driveItem.ETag,
                    LastModifiedDateTime: driveItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                    ContentType: driveItem.File?.MimeType,
                    Folder: null
                );

                _logger.LogInformation("Completed upload session {UploadUrl}, item ID: {ItemId}",
                    uploadSessionUrl, completedItemDto.Id);

                return new ChunkUploadResponse(201, completedItemDto);
            }
            else
            {
                _logger.LogWarning("Unexpected response from chunked upload: {StatusCode}", response.StatusCode);
                return new ChunkUploadResponse((int)response.StatusCode);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Upload chunk operation was cancelled");
            return new ChunkUploadResponse(499);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload chunk for session {UploadUrl}", uploadSessionUrl);
            return new ChunkUploadResponse(500);
        }
    }
}
