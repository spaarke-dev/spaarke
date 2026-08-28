// -----------------------------------------------------------------------------
// BlobArtifactDownloaderTests.cs
//
// L2 CONTROL-PLANE unit tests for BlobArtifactDownloader (task 132, Wave
// G-3). Proves the real Azure.Storage.Blobs BlobClient.DownloadToAsync(string,
// CancellationToken) call path via the SAME <see cref="ArmSdkTestFakes.NewBlobContainerClient"/>
// fake-transport helper (extend, don't duplicate — CLAUDE.md §11). ADR-038
// path #1.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class BlobArtifactDownloaderTests : IDisposable
{
    private readonly string _tempDownloadDir;

    public BlobArtifactDownloaderTests()
    {
        _tempDownloadDir = Path.Combine(Path.GetTempPath(), $"h9-blob-download-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDownloadDir))
        {
            try { Directory.Delete(_tempDownloadDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private BffDeployOptions NewOptions() => new()
    {
        ProvisioningArtifactsContainerUri = "https://faketest.blob.core.windows.net/provisioning-artifacts",
        LocalArtifactDownloadDirectory = _tempDownloadDir,
    };

    // ---------- T1 successful download — real bytes reach disk ----------

    [Fact]
    public async Task DownloadAsync_BlobExists_WritesBytesToLocalDiskAndReturnsSuccess()
    {
        var zipBytes = Encoding.UTF8.GetBytes("fake-zip-content-for-test");
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().EndWith("/bff-api-2026.08.19-1.zip");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zipBytes),
            };
        });

        var downloader = new BlobArtifactDownloader(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<BlobArtifactDownloader>.Instance);

        var result = await downloader.DownloadAsync(
            new ArtifactDownloadRequest("bff-api-2026.08.19-1.zip"), CancellationToken.None);

        var success = result.Should().BeOfType<ArtifactDownloadResult.Success>().Subject;
        File.Exists(success.LocalZipPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(success.LocalZipPath)).Should().BeEquivalentTo(zipBytes,
            "the downloader must stream the REAL blob content to disk, not fabricate success");
        success.SizeBytes.Should().Be(zipBytes.Length);
    }

    // ---------- T2 blob not found — domain Failure, not throw ----------

    [Fact]
    public async Task DownloadAsync_BlobNotFound_ReturnsFailure_DoesNotThrow()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound, ArmSdkTestFakes.ArmErrorBody("BlobNotFound", "The specified blob does not exist.")));

        var downloader = new BlobArtifactDownloader(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<BlobArtifactDownloader>.Instance);

        var result = await downloader.DownloadAsync(
            new ArtifactDownloadRequest("bff-api-missing.zip"), CancellationToken.None);

        var failure = result.Should().BeOfType<ArtifactDownloadResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("not found");
    }

    // ---------- T3 infra fault propagates ----------

    [Fact]
    public async Task DownloadAsync_ServerError_ThrowsRequestFailedException()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.ServiceUnavailable, ArmSdkTestFakes.ArmErrorBody("ServerBusy", "busy")));

        var downloader = new BlobArtifactDownloader(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<BlobArtifactDownloader>.Instance);

        var act = async () => await downloader.DownloadAsync(
            new ArtifactDownloadRequest("bff-api-2026.08.19-1.zip"), CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>();
    }

    // ---------- T4 argument guard ----------

    [Fact]
    public async Task DownloadAsync_MissingArtifactBlobName_ThrowsWithoutCallingBlobStorage()
    {
        var downloader = new BlobArtifactDownloader(
            ArmSdkTestFakes.NewBlobContainerClient(ArmSdkTestFakes.NewHandler(_ => throw new InvalidOperationException("must not call storage"))),
            Options.Create(NewOptions()),
            NullLogger<BlobArtifactDownloader>.Instance);

        var act = async () => await downloader.DownloadAsync(new ArtifactDownloadRequest(string.Empty), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
