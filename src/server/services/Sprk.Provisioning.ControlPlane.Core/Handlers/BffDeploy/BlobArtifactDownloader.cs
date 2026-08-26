// -----------------------------------------------------------------------------
// BlobArtifactDownloader.cs
//
// Production <see cref="IBffArtifactDownloader"/> — task 132 (Wave G-3, Option
// D hybrid / DS-4 §5 re-scope). Downloads the resolved BFF publish zip from
// the `provisioning-artifacts` blob container via
// BlobClient.DownloadToAsync(string, CancellationToken) — GROUND-TRUTHED via
// reflection against the installed Azure.Storage.Blobs 12.29.1 package before
// writing this file (parity with task 123's ArmDeploymentRunner header
// discipline). UAMI RBAC only (Storage Blob Data Contributor/Reader on the
// platform storage account) — no stored account key, no SAS token.
// -----------------------------------------------------------------------------

using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Streams the named artifact blob to <see cref="BffDeployOptions.LocalArtifactDownloadDirectory"/>.
/// </summary>
public sealed class BlobArtifactDownloader : IBffArtifactDownloader
{
    private readonly BlobContainerClient _artifactsContainer;
    private readonly BffDeployOptions _options;
    private readonly ILogger<BlobArtifactDownloader> _logger;

    public BlobArtifactDownloader(
        BlobContainerClient artifactsContainer,
        IOptions<BffDeployOptions> options,
        ILogger<BlobArtifactDownloader> logger)
    {
        ArgumentNullException.ThrowIfNull(artifactsContainer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _artifactsContainer = artifactsContainer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ArtifactDownloadResult> DownloadAsync(
        ArtifactDownloadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ArtifactBlobName);

        Directory.CreateDirectory(_options.LocalArtifactDownloadDirectory);
        var localPath = Path.Combine(_options.LocalArtifactDownloadDirectory, request.ArtifactBlobName);

        var blob = _artifactsContainer.GetBlobClient(request.ArtifactBlobName);

        _logger.LogInformation(
            "H9 artifact download starting: blobName={BlobName} localPath={LocalPath}",
            request.ArtifactBlobName, localPath);

        try
        {
            await blob.DownloadToAsync(localPath, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new ArtifactDownloadResult.Failure(
                $"Artifact blob '{request.ArtifactBlobName}' not found in the provisioning-artifacts " +
                $"container (HTTP 404). The manifest referenced a blob that no longer exists — verify " +
                "blob-container retention has not expired.");
        }

        var sizeBytes = new FileInfo(localPath).Length;
        _logger.LogInformation(
            "H9 artifact download complete: blobName={BlobName} sizeBytes={SizeBytes}",
            request.ArtifactBlobName, sizeBytes);

        return new ArtifactDownloadResult.Success(localPath, sizeBytes);
    }
}
