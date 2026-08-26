// -----------------------------------------------------------------------------
// IBffArtifactDownloader.cs
//
// L2 abstraction over downloading the resolved BFF publish artifact zip from
// the `provisioning-artifacts` blob container (task 132, Wave G-3, DS-4 §5
// re-scope). Production impl (<see cref="BlobArtifactDownloader"/>) uses
// Azure.Storage.Blobs under UAMI RBAC — no stored storage account key.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="BlobArtifactDownloader"/>.
//     - Test: stubs injected per unit test that construct
//       <see cref="ArtifactDownloadResult"/> directly (see
//       H9BffDeployHandlerTests); BlobArtifactDownloaderTests exercises the
//       real SDK call path against a fake HTTP transport.
//   Interface earns its keep — no NIH.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Downloads the named artifact blob to local disk so the Kudu zip-deployer
/// can POST it. Production impl streams via
/// <c>BlobClient.DownloadToAsync(string, CancellationToken)</c>; test impls
/// return canned results.
/// </summary>
public interface IBffArtifactDownloader
{
    /// <summary>
    /// Downloads <paramref name="request"/>'s blob to local disk. Domain
    /// failures (blob not found) do NOT throw — carried in
    /// <see cref="ArtifactDownloadResult.Failure"/>. Infrastructure faults
    /// (container unreachable, auth failure, disk I/O error) MAY throw.
    /// </summary>
    Task<ArtifactDownloadResult> DownloadAsync(ArtifactDownloadRequest request, CancellationToken cancellationToken);
}

/// <summary>Inputs to a single artifact download.</summary>
/// <param name="ArtifactBlobName">Blob name resolved by <see cref="IArtifactManifestVerifier"/> (e.g. <c>bff-api-2026.08.19-1.zip</c>).</param>
public sealed record ArtifactDownloadRequest(string ArtifactBlobName);

/// <summary>
/// Discriminated result of <see cref="IBffArtifactDownloader.DownloadAsync"/>.
/// </summary>
public abstract record ArtifactDownloadResult
{
    private ArtifactDownloadResult() { }

    /// <summary>Download succeeded — <paramref name="LocalZipPath"/> is ready for Kudu zip-deploy + NFR-01 size measurement.</summary>
    public sealed record Success(string LocalZipPath, long SizeBytes) : ArtifactDownloadResult;

    /// <summary>Download failed (e.g. blob not found). <paramref name="Diagnostic"/> is the operator-facing message.</summary>
    public sealed record Failure(string Diagnostic) : ArtifactDownloadResult;
}
