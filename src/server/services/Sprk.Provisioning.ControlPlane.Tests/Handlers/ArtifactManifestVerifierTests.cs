// -----------------------------------------------------------------------------
// ArtifactManifestVerifierTests.cs
//
// L2 CONTROL-PLANE unit tests for ArtifactManifestVerifier (task 132, Wave
// G-3). Proves the real Azure.Storage.Blobs SDK download + JSON-parse + gate-
// verification path via the SAME <see cref="ArmSdkTestFakes.NewBlobContainerClient"/>
// fake-transport helper task 123's ArmDeploymentRunnerTests.cs already
// authored for the SAME `provisioning-artifacts` container (CLAUDE.md §11 —
// extend, don't duplicate the fake-transport plumbing). ADR-038 path #1.
// -----------------------------------------------------------------------------

using System.Net;
using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ArtifactManifestVerifierTests
{
    private static BffDeployOptions NewOptions() => new()
    {
        ProvisioningArtifactsContainerUri = "https://faketest.blob.core.windows.net/provisioning-artifacts",
        ArtifactManifestBlobName = "latest.json",
    };

    private static string AllGreenManifestJson(string buildId = "2026.08.19-1", string artifactBlobName = "bff-api-2026.08.19-1.zip") =>
        $$"""
        {
          "buildId": "{{buildId}}",
          "sha": "abc123def456",
          "sizeBytes": 44500000,
          "publishedAt": "2026-08-19T12:00:00Z",
          "artifactBlobName": "{{artifactBlobName}}",
          "gates": {
            "r3AnalyzersAsErrors": "Passed",
            "godClassRatchet": "Passed",
            "archTests": "Passed",
            "namingConformance": "Passed",
            "graphAppRoleParity": "Skipped"
          }
        }
        """;

    // ---------- T1 verified happy path ----------

    [Fact]
    public async Task VerifyAsync_AllGatesGreen_NoRequestedBuildId_ReturnsVerified()
    {
        var handler = ArmSdkTestFakes.NewHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().EndWith("/latest.json");
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, AllGreenManifestJson());
        });

        var verifier = new ArtifactManifestVerifier(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArtifactManifestVerifier>.Instance);

        var result = await verifier.VerifyAsync(new ArtifactManifestVerificationRequest(null), CancellationToken.None);

        var verified = result.Should().BeOfType<ArtifactManifestVerificationResult.Verified>().Subject;
        verified.Manifest.BuildId.Should().Be("2026.08.19-1");
        verified.Manifest.ArtifactBlobName.Should().Be("bff-api-2026.08.19-1.zip");
        verified.Manifest.SizeBytes.Should().Be(44500000L);
        verified.Manifest.Gates["graphAppRoleParity"].Should().Be("Skipped");
    }

    // ---------- T2 manifest blob not found ----------

    [Fact]
    public async Task VerifyAsync_ManifestNotFound_ReturnsRejected()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.NotFound, ArmSdkTestFakes.ArmErrorBody("BlobNotFound", "not found")));

        var verifier = new ArtifactManifestVerifier(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArtifactManifestVerifier>.Instance);

        var result = await verifier.VerifyAsync(new ArtifactManifestVerificationRequest(null), CancellationToken.None);

        var rejected = result.Should().BeOfType<ArtifactManifestVerificationResult.Rejected>().Subject;
        rejected.Diagnostic.Should().Contain("not found");
    }

    // ---------- T3 malformed manifest ----------

    [Fact]
    public async Task VerifyAsync_MalformedJson_ReturnsRejected()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, "{ this is not valid json"));

        var verifier = new ArtifactManifestVerifier(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArtifactManifestVerifier>.Instance);

        var result = await verifier.VerifyAsync(new ArtifactManifestVerificationRequest(null), CancellationToken.None);

        result.Should().BeOfType<ArtifactManifestVerificationResult.Rejected>();
    }

    // ---------- T4 missing gate key ----------

    [Fact]
    public async Task VerifyAsync_MissingGateKey_ReturnsRejected()
    {
        var json = """
            {
              "buildId": "2026.08.19-1",
              "sha": "abc123",
              "sizeBytes": 44500000,
              "artifactBlobName": "bff-api-2026.08.19-1.zip",
              "gates": {
                "r3AnalyzersAsErrors": "Passed",
                "godClassRatchet": "Passed",
                "archTests": "Passed",
                "namingConformance": "Passed"
              }
            }
            """;
        var handler = ArmSdkTestFakes.NewHandler(_ => ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, json));

        var verifier = new ArtifactManifestVerifier(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArtifactManifestVerifier>.Instance);

        var result = await verifier.VerifyAsync(new ArtifactManifestVerificationRequest(null), CancellationToken.None);

        var rejected = result.Should().BeOfType<ArtifactManifestVerificationResult.Rejected>().Subject;
        rejected.Diagnostic.Should().Contain("graphAppRoleParity");
    }

    // ---------- T5 red gate ----------

    [Fact]
    public async Task VerifyAsync_RedGate_ReturnsRejected_HardBlock()
    {
        var json = """
            {
              "buildId": "2026.08.19-1",
              "sha": "abc123",
              "sizeBytes": 44500000,
              "artifactBlobName": "bff-api-2026.08.19-1.zip",
              "gates": {
                "r3AnalyzersAsErrors": "Passed",
                "godClassRatchet": "Passed",
                "archTests": "Failed",
                "namingConformance": "Passed",
                "graphAppRoleParity": "Skipped"
              }
            }
            """;
        var handler = ArmSdkTestFakes.NewHandler(_ => ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, json));

        var verifier = new ArtifactManifestVerifier(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArtifactManifestVerifier>.Instance);

        var result = await verifier.VerifyAsync(new ArtifactManifestVerificationRequest(null), CancellationToken.None);

        var rejected = result.Should().BeOfType<ArtifactManifestVerificationResult.Rejected>().Subject;
        rejected.Diagnostic.Should().Contain("archTests");
        rejected.Diagnostic.Should().Contain("RED gate");
    }

    // ---------- T6 requested buildId mismatch ----------

    [Fact]
    public async Task VerifyAsync_RequestedBuildIdMismatch_ReturnsRejected()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, AllGreenManifestJson(buildId: "2026.08.19-1")));

        var verifier = new ArtifactManifestVerifier(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArtifactManifestVerifier>.Instance);

        var result = await verifier.VerifyAsync(new ArtifactManifestVerificationRequest("2026.08.18-3"), CancellationToken.None);

        var rejected = result.Should().BeOfType<ArtifactManifestVerificationResult.Rejected>().Subject;
        rejected.Diagnostic.Should().Contain("2026.08.18-3");
        rejected.Diagnostic.Should().Contain("2026.08.19-1");
    }

    // ---------- T7 requested buildId matches ----------

    [Fact]
    public async Task VerifyAsync_RequestedBuildIdMatches_ReturnsVerified()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, AllGreenManifestJson(buildId: "2026.08.19-1")));

        var verifier = new ArtifactManifestVerifier(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArtifactManifestVerifier>.Instance);

        var result = await verifier.VerifyAsync(new ArtifactManifestVerificationRequest("2026.08.19-1"), CancellationToken.None);

        result.Should().BeOfType<ArtifactManifestVerificationResult.Verified>();
    }

    // ---------- T8 infra fault propagates (not swallowed as Rejected) ----------

    [Fact]
    public async Task VerifyAsync_ServerError_ThrowsRequestFailedException()
    {
        var handler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(HttpStatusCode.InternalServerError, ArmSdkTestFakes.ArmErrorBody("InternalError", "boom")));

        var verifier = new ArtifactManifestVerifier(
            ArmSdkTestFakes.NewBlobContainerClient(handler),
            Options.Create(NewOptions()),
            NullLogger<ArtifactManifestVerifier>.Instance);

        var act = async () => await verifier.VerifyAsync(new ArtifactManifestVerificationRequest(null), CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>(
            "a 500 is an infrastructure fault, not a domain Rejected — the handler's outer try/catch classifies it Resumable");
    }
}
