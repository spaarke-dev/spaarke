using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Sprk.Bff.Api.Services.Ai.Testing;

/// <summary>
/// Service for temporary blob storage used in Quick Test mode.
/// Documents are stored with a 24-hour TTL and auto-deleted via Azure lifecycle policy.
/// </summary>
/// <remarks>
/// Key features:
/// - Upload test documents to isolated container
/// - Generate time-limited SAS URLs for secure access
/// - Automatic cleanup via Azure Blob lifecycle management
/// - 50MB max file size enforcement
/// </remarks>
public class TempBlobStorageService : ITempBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<TempBlobStorageService> _logger;

    private const string ContainerName = "test-documents";
    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50MB
    private const int DefaultSasExpiryHours = 24;

    public TempBlobStorageService(
        BlobServiceClient blobServiceClient,
        ILogger<TempBlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TempDocumentInfo> UploadTestDocumentAsync(
        Stream documentStream,
        string fileName,
        string contentType,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        // Validate file size
        if (documentStream.Length > MaxFileSizeBytes)
        {
            throw new ArgumentException(
                $"File size ({documentStream.Length / (1024 * 1024):F2}MB) exceeds maximum allowed size (50MB).",
                nameof(documentStream));
        }

        // Ensure container exists
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync(
            PublicAccessType.None,
            cancellationToken: cancellationToken);

        // Generate blob name with session isolation
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var sanitizedFileName = SanitizeFileName(fileName);
        var blobName = $"{sessionId}/{timestamp}_{sanitizedFileName}";

        var blobClient = containerClient.GetBlobClient(blobName);

        _logger.LogInformation(
            "Uploading test document: {FileName} ({Size} bytes) to {BlobName}",
            fileName, documentStream.Length, blobName);

        // Upload with metadata
        var headers = new BlobHttpHeaders
        {
            ContentType = contentType
        };

        var metadata = new Dictionary<string, string>
        {
            ["originalFileName"] = fileName,
            ["sessionId"] = sessionId.ToString(),
            ["uploadedAt"] = DateTime.UtcNow.ToString("O"),
            ["isTestDocument"] = "true"
        };

        await blobClient.UploadAsync(
            documentStream,
            new BlobUploadOptions
            {
                HttpHeaders = headers,
                Metadata = metadata
            },
            cancellationToken);

        // Generate SAS URL
        var sasUri = GenerateSasUrl(blobClient, TimeSpan.FromHours(DefaultSasExpiryHours));

        _logger.LogInformation(
            "Test document uploaded successfully: {BlobName}, SAS expires at {Expiry}",
            blobName, DateTime.UtcNow.AddHours(DefaultSasExpiryHours));

        return new TempDocumentInfo
        {
            BlobName = blobName,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = documentStream.Length,
            SasUrl = sasUri.ToString(),
            ExpiresAt = DateTime.UtcNow.AddHours(DefaultSasExpiryHours),
            SessionId = sessionId
        };
    }

    /// <inheritdoc />
    public async Task<string> GenerateSasUrlAsync(
        string blobName,
        TimeSpan? validFor = null,
        CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        // Verify blob exists
        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Blob '{blobName}' does not exist.");
        }

        var duration = validFor ?? TimeSpan.FromHours(DefaultSasExpiryHours);
        var sasUri = GenerateSasUrl(blobClient, duration);

        return sasUri.ToString();
    }

    /// <inheritdoc />
    public async Task<Stream?> DownloadAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            _logger.LogWarning("Blob not found: {BlobName}", blobName);
            return null;
        }

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var deleted = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        if (deleted)
        {
            _logger.LogInformation("Deleted test document: {BlobName}", blobName);
        }
    }

    /// <inheritdoc />
    public async Task DeleteSessionDocumentsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var prefix = $"{sessionId}/";

        var deletedCount = 0;
        await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            var blobClient = containerClient.GetBlobClient(blob.Name);
            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            deletedCount++;
        }

        _logger.LogInformation(
            "Deleted {Count} test documents for session {SessionId}",
            deletedCount, sessionId);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateFileSizeAsync(Stream stream, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // No async operation needed
        return stream.Length <= MaxFileSizeBytes;
    }

    private Uri GenerateSasUrl(BlobClient blobClient, TimeSpan validFor)
    {
        if (!blobClient.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                "Cannot generate SAS URL. Ensure the BlobServiceClient was created with account credentials.");
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = ContainerName,
            BlobName = blobClient.Name,
            Resource = "b", // blob
            ExpiresOn = DateTimeOffset.UtcNow.Add(validFor)
        };

        // Read-only access
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blobClient.GenerateSasUri(sasBuilder);
    }

    private static string SanitizeFileName(string fileName)
    {
        // Remove or replace invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Limit length
        if (sanitized.Length > 100)
        {
            var extension = Path.GetExtension(sanitized);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(sanitized);
            sanitized = nameWithoutExtension[..(100 - extension.Length)] + extension;
        }

        return sanitized;
    }
}

/// <summary>
/// Interface for temporary blob storage operations.
/// </summary>
public interface ITempBlobStorageService
{
    /// <summary>
    /// Upload a test document to temporary storage.
    /// </summary>
    Task<TempDocumentInfo> UploadTestDocumentAsync(
        Stream documentStream,
        string fileName,
        string contentType,
        Guid sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generate a SAS URL for an existing blob.
    /// </summary>
    Task<string> GenerateSasUrlAsync(
        string blobName,
        TimeSpan? validFor = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a blob as a stream.
    /// </summary>
    Task<Stream?> DownloadAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>
    /// Delete a specific blob.
    /// </summary>
    Task DeleteAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>
    /// Delete all documents for a session.
    /// </summary>
    Task DeleteSessionDocumentsAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Validate file size before upload.
    /// </summary>
    Task<bool> ValidateFileSizeAsync(Stream stream, CancellationToken cancellationToken);
}

/// <summary>
/// Information about an uploaded test document.
/// </summary>
public record TempDocumentInfo
{
    public required string BlobName { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public required string SasUrl { get; init; }
    public DateTime ExpiresAt { get; init; }
    public Guid SessionId { get; init; }
}
