using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;
using System.Diagnostics;

namespace Spe.Bff.Api.Infrastructure.Graph;

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
            var graphClient = _factory.CreateAppOnlyClient();

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
                item.Folder != null);
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
            var graphClient = _factory.CreateAppOnlyClient();

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
}
