// -----------------------------------------------------------------------------
// H9BffDeployHandlerTests.cs
//
// Unit tests over H9BffDeployHandler (task 052 — wave C4 Batch 4B; RE-SCOPED
// task 132, Wave G-3, per DS-4 §5's artifact-based design).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live HTTP / Azure. Fakes replace the
//   repository + all SIX collaborator seams (artifact manifest verifier,
//   artifact downloader, Kudu zip-deployer, slot swapper, health probe,
//   publish-size reporter) so the handler orchestration + §4C rollback
//   classification + blue-green swap + rollback logic is exercised in
//   isolation. Live-Azure coverage belongs in the dedicated collaborator
//   test files (ArtifactManifestVerifierTests / BlobArtifactDownloaderTests /
//   KuduZipDeployerTests / ArmSlotSwapperTests) which exercise the real SDK
//   call path against fake HTTP transports — parity with H2a/H4's split
//   between handler-orchestration tests and collaborator-SDK-shape tests.
//
// COMPLETENESS PROOF FOR THE ROLLBACK PATH (task 132 dispatch directive #5):
//   AC-15a below is the load-bearing "rollback re-swap is preserved
//   unchanged" proof — FakeSlotSwapper is the IDENTICAL fake class this file
//   used before task 132 (byte-for-byte unchanged), and the assertion that
//   BOTH swap calls carry an IDENTICAL SlotSwapRequest (Source=staging,
//   Target=production) demonstrates the re-swap-is-self-inverse invariant the
//   handler's rollback branch depends on, now exercised against the new
//   SDK-based ArmSlotSwapper via DI only (no handler-code change).
//
// COVERAGE (POML acceptance criteria mapped to test cases):
//   AC-1   Happy path — manifest Verified, download Success, Kudu Success,
//          staging /health Success, size under threshold, slot swap Success,
//          prod /health Success → handler Success + CompletedPhase(H9) +
//          NFR-01 gate Verified.
//   AC-2a  Missing tenantId (§4D I1) → Resumable + MissingTenantId.
//   AC-2b  Missing subscriptionId → Resumable + MissingSubscriptionId.
//   AC-2c  Missing resourceGroupName → Resumable + MissingResourceGroupName.
//   AC-2d  Missing appServiceName → Resumable + MissingAppServiceName.
//   AC-2e  buildId ABSENT (now optional, task 132 DS-4 §5 item 1) → resolves
//          from manifest.BuildId; deploy proceeds + idempotency key uses the
//          RESOLVED buildId, not a run parameter.
//   AC-3   Spaarkedev1 hardcode detected in Deploy-Release.ps1 → QuarantineRequired
//          + Spaarkedev1HardcodeDetected (POML criterion 5 Gap 2 assertion);
//          manifest verifier + downstream collaborators NEVER called.
//   AC-4   Manifest Rejected (missing/red gate, buildId mismatch) → Resumable
//          + ArtifactManifestRejected; downloader/kudu/swapper NEVER called.
//   AC-5   Manifest verifier throws → Resumable + ManifestVerifierInfraFault.
//   AC-6   Artifact download Failure → Resumable + ArtifactDownloadFailed;
//          kudu/swapper NEVER called.
//   AC-7   Artifact download throws → Resumable + ArtifactDownloadInfraFault.
//   AC-8   Kudu zip-deploy Failure → Resumable + KuduZipDeployFailed; swapper
//          NEVER called.
//   AC-9   Kudu zip-deploy throws → Resumable + KuduZipDeployInfraFault.
//   AC-10  Staging /health probe Failure → Resumable + StagingHealthCheckFailed;
//          size reporter + swapper NEVER called.
//   AC-11  Publish-size reporter throws → Resumable + PublishSizeReporterInfraFault.
//   AC-12a Publish-size delta > threshold → QuarantineRequired +
//          PublishSizeDeltaExceeded; swapper NEVER called.
//   AC-12b Publish-size absolute > ceiling → QuarantineRequired + PublishSizeDeltaExceeded.
//   AC-13  Slot-swap Failure → RetryableWithCleanup + SlotSwapFailed.
//   AC-14  Slot-swap throws → RetryableWithCleanup + SlotSwapInfraFault.
//   AC-15a Smoke test (production) failure + rollback success →
//          RetryableWithCleanup + SmokeTestFailedRolledBack — COMPLETENESS:
//          swapper called exactly twice with an IDENTICAL request both times.
//   AC-15b Smoke test failure + rollback failure → QuarantineRequired +
//          SmokeTestFailedRollbackAlsoFailed (POML escalation trigger 2).
//   AC-15c Smoke test failure + rollback throws → QuarantineRequired +
//          RollbackInfraFault.
//   AC-15d Smoke test throws → treated as smoke test failure → rollback flow;
//          RetryableWithCleanup + SmokeTestInfraFault on rollback success.
//   AC-16a Idempotency (buildId supplied) — EARLY no-op, before ANY
//          collaborator call (manifest verifier included).
//   AC-16b Idempotency (buildId ABSENT) — LATE no-op: manifest verifier IS
//          called (to resolve buildId) but downloader/kudu/swapper are NOT.
//   AC-17  Idempotency-key format determinism — bff-{customerId}-{buildId}.
//   AC-18  Run not found → Resumable + RunNotFound.
//   AC-19  HandlerId mismatch → throws InvalidOperationException.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H9BffDeployHandlerTests : IDisposable
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h9-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "sub-cus-acme-prod";
    private const string ResourceGroupName = "rg-spaarke-acme-prod";
    private const string AppServiceName = "spaarke-bff-acme";
    private const string BuildId = "ci-2026.08.17.1";
    private const string ResolvedManifestOnlyBuildId = "ci-2026.08.19-1";
    private const string ArtifactBlobName = "bff-api-ci-2026.08.17.1.zip";
    private const string StagingSlotName = "staging";
    private const string HealthCheckPath = "/healthz";

    private readonly string _tempLocalZip;

    public H9BffDeployHandlerTests()
    {
        // Real zero-byte temp zip so the publish-size reporter fake can be
        // driven by canned reports without a File.Exists coupling to a real
        // artifact; the reporter fake ignores the path anyway.
        _tempLocalZip = Path.Combine(Path.GetTempPath(), $"h9-test-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(_tempLocalZip, Array.Empty<byte>());
    }

    public void Dispose()
    {
        if (File.Exists(_tempLocalZip))
        {
            try { File.Delete(_tempLocalZip); } catch { /* best-effort cleanup */ }
        }
    }

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_AllGreen_SucceedsAndAdvancesState()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var verifier = FakeManifestVerifier.Verified(BuildId, ArtifactBlobName);
        var downloader = FakeArtifactDownloader.Success(_tempLocalZip);
        var kudu = FakeKuduDeployer.Success();
        var swapper = FakeSlotSwapper.Success();
        var probe = FakeHealthProbe.Success();
        var sizer = FakeSizeReporter.Ok(bytes: 44_000_000L);
        var handler = BuildHandler(repo, verifier, downloader, kudu, swapper, probe, sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H9BffDeployHandler.BuildIdempotencyKey(CustomerId, BuildId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H9");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H9");
        repo.LastWrittenRun.GateStates.Should().ContainKey("h9-nfr01-publish-size");
        repo.LastWrittenRun.GateStates["h9-nfr01-publish-size"].Status.Should().Be(GateState.Verified);

        verifier.CallCount.Should().Be(1);
        downloader.CallCount.Should().Be(1);
        kudu.CallCount.Should().Be(1);
        probe.CallCount.Should().Be(2, "staging probe + production probe both fire on the happy path");
        swapper.CallCount.Should().Be(1, "swap runs exactly once on the happy path");
        sizer.CallCount.Should().Be(1);

        swapper.LastRequests.Should().ContainSingle();
        swapper.LastRequests[0].SourceSlotName.Should().Be(StagingSlotName);
        swapper.LastRequests[0].TargetSlotName.Should().Be("production");

        probe.RequestedUrls[0].Should().Be($"https://{AppServiceName}-{StagingSlotName}.azurewebsites.net{HealthCheckPath}");
        probe.RequestedUrls[1].Should().Be($"https://{AppServiceName}.azurewebsites.net{HealthCheckPath}");
    }

    // ---------- AC-2 parameter guards ----------

    [Fact]
    public async Task AC2a_MissingTenantId_FailsResumable_NoDeploy()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.TenantIdParameterKey);
        var seams = FreshGreenSeams();
        var repo = new FakeRepository(run, etag: "etag-2a");
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.MissingTenantId);
        seams.Verifier.CallCount.Should().Be(0);
        seams.Downloader.CallCount.Should().Be(0);
        seams.Swapper.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC2b_MissingSubscriptionId_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.SubscriptionIdParameterKey);
        var seams = FreshGreenSeams();
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-2b"), seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.MissingSubscriptionId);
    }

    [Fact]
    public async Task AC2c_MissingResourceGroupName_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.ResourceGroupNameParameterKey);
        var seams = FreshGreenSeams();
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-2c"), seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.MissingResourceGroupName);
    }

    [Fact]
    public async Task AC2d_MissingAppServiceName_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.AppServiceNameParameterKey);
        var seams = FreshGreenSeams();
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-2d"), seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.MissingAppServiceName);
    }

    [Fact]
    public async Task AC2e_MissingBuildId_ResolvesFromManifest_SucceedsWithResolvedIdempotencyKey()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.BuildIdParameterKey);
        var repo = new FakeRepository(run, etag: "etag-2e");
        var verifier = FakeManifestVerifier.Verified(ResolvedManifestOnlyBuildId, ArtifactBlobName);
        var downloader = FakeArtifactDownloader.Success(_tempLocalZip);
        var kudu = FakeKuduDeployer.Success();
        var swapper = FakeSlotSwapper.Success();
        var probe = FakeHealthProbe.Success();
        var sizer = FakeSizeReporter.Ok();
        var handler = BuildHandler(repo, verifier, downloader, kudu, swapper, probe, sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H9BffDeployHandler.BuildIdempotencyKey(CustomerId, ResolvedManifestOnlyBuildId));
        verifier.LastRequestedBuildId.Should().BeNull("buildId run parameter was absent — resolve-from-manifest path");
        downloader.CallCount.Should().Be(1);
        kudu.CallCount.Should().Be(1);
        swapper.CallCount.Should().Be(1);
    }

    // ---------- AC-3 Gap 2 spaarkedev1 hardcode detected ----------

    [Fact]
    public async Task AC3_Spaarkedev1HardcodeInDeployReleaseScript_FailsQuarantine_NoDeploy()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-3");
        var seams = FreshGreenSeams();

        // Write a fixture Deploy-Release.ps1 containing the regressed literal.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"h9-test-deploy-release-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, "# a regressed deploy script\n$env = 'spaarkedev1'\n");

        try
        {
            var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer,
                configureOptions: o => o.DeployReleaseScriptPath = scriptPath);

            var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

            var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
            failure.Class.Should().Be(FailureClass.QuarantineRequired);
            failure.RejectionCode.Should().Be(BffDeployRejectionCodes.Spaarkedev1HardcodeDetected);
            failure.Diagnostic.Should().Contain("spaarkedev1");
            repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);

            seams.Verifier.CallCount.Should().Be(0, "spaarkedev1 pre-flight blocks the manifest verifier + everything downstream");
            seams.Downloader.CallCount.Should().Be(0);
            seams.Swapper.CallCount.Should().Be(0);
        }
        finally
        {
            if (File.Exists(scriptPath)) { try { File.Delete(scriptPath); } catch { } }
        }
    }

    // ---------- AC-4 manifest rejected ----------

    [Fact]
    public async Task AC4_ManifestRejected_BlocksDeploy_Resumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-4");
        var verifier = FakeManifestVerifier.Rejected("Manifest reports RED gate(s): archTests.");
        var seams = FreshGreenSeams(overrideVerifier: verifier);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable, "manifest issues are fixable via a new build + resume");
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.ArtifactManifestRejected);
        failure.Diagnostic.Should().Contain("RED gate");
        seams.Downloader.CallCount.Should().Be(0, "no download when manifest is rejected");
        seams.Kudu.CallCount.Should().Be(0);
        seams.Swapper.CallCount.Should().Be(0);
    }

    // ---------- AC-5 manifest verifier throws ----------

    [Fact]
    public async Task AC5_ManifestVerifierThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-5");
        var verifier = FakeManifestVerifier.Throws(new InvalidOperationException("blob container unreachable"));
        var seams = FreshGreenSeams(overrideVerifier: verifier);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.ManifestVerifierInfraFault);
        failure.Diagnostic.Should().Contain("InvalidOperationException");
    }

    // ---------- AC-6 artifact download Failure ----------

    [Fact]
    public async Task AC6_ArtifactDownloadFailure_FailsResumable_NoKuduOrSwap()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var downloader = FakeArtifactDownloader.Failure("Artifact blob not found (HTTP 404)");
        var seams = FreshGreenSeams(overrideDownloader: downloader);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.ArtifactDownloadFailed);
        failure.Diagnostic.Should().Contain("not found");
        seams.Kudu.CallCount.Should().Be(0);
        seams.Probe.CallCount.Should().Be(0);
        seams.Swapper.CallCount.Should().Be(0);
    }

    // ---------- AC-7 artifact download throws ----------

    [Fact]
    public async Task AC7_ArtifactDownloadThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-7");
        var downloader = FakeArtifactDownloader.Throws(new IOException("disk full"));
        var seams = FreshGreenSeams(overrideDownloader: downloader);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.ArtifactDownloadInfraFault);
    }

    // ---------- AC-8 Kudu zip-deploy Failure ----------

    [Fact]
    public async Task AC8_KuduZipDeployFailure_FailsResumable_NoSwap()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var kudu = FakeKuduDeployer.Failure("Kudu zip-deploy returned HTTP 500");
        var seams = FreshGreenSeams(overrideKudu: kudu);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.KuduZipDeployFailed);
        seams.Probe.CallCount.Should().Be(0, "staging probe never runs when the Kudu deploy itself failed");
        seams.Swapper.CallCount.Should().Be(0);
    }

    // ---------- AC-9 Kudu zip-deploy throws ----------

    [Fact]
    public async Task AC9_KuduZipDeployThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-9");
        var kudu = FakeKuduDeployer.Throws(new TimeoutException("kudu POST timed out"));
        var seams = FreshGreenSeams(overrideKudu: kudu);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.KuduZipDeployInfraFault);
    }

    // ---------- AC-10 staging health-check failure ----------

    [Fact]
    public async Task AC10_StagingHealthCheckFailure_FailsResumable_NoSizeOrSwap()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-10");
        var probe = FakeHealthProbe.StagingFailure("HTTP 500 staging unhealthy");
        var seams = FreshGreenSeams(overrideProbe: probe);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.StagingHealthCheckFailed);
        seams.Sizer.CallCount.Should().Be(0, "no size measurement when staging never came up healthy");
        seams.Swapper.CallCount.Should().Be(0);
    }

    // ---------- AC-11 publish-size reporter throws ----------

    [Fact]
    public async Task AC11_PublishSizeReporterThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-11");
        var sizer = FakeSizeReporter.Throws(new FileNotFoundException("zip missing"));
        var seams = FreshGreenSeams(overrideSizer: sizer);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.PublishSizeReporterInfraFault);
    }

    // ---------- AC-12 publish-size thresholds ----------

    [Fact]
    public async Task AC12a_PublishSizeDeltaExceeded_FailsQuarantine_NoSwap()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12a");
        var sizer = FakeSizeReporter.WithFlags(bytes: 55_000_000L, deltaBytes: 10_000_000L,
            exceedsDelta: true, exceedsAbsolute: false);
        var seams = FreshGreenSeams(overrideSizer: sizer);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.PublishSizeDeltaExceeded);
        seams.Swapper.CallCount.Should().Be(0, "swap never runs when NFR-01 threshold is exceeded");
    }

    [Fact]
    public async Task AC12b_PublishSizeAbsoluteCeilingExceeded_FailsQuarantine()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12b");
        var sizer = FakeSizeReporter.WithFlags(bytes: 65_000_000L, deltaBytes: 20_000_000L,
            exceedsDelta: true, exceedsAbsolute: true);
        var seams = FreshGreenSeams(overrideSizer: sizer);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.PublishSizeDeltaExceeded);
    }

    // ---------- AC-13 slot-swap Failure ----------

    [Fact]
    public async Task AC13_SlotSwapFailure_FailsRetryableWithCleanup()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13");
        var swapper = FakeSlotSwapper.Failure("ARM 403 AuthorizationFailed");
        var seams = FreshGreenSeams(overrideSwapper: swapper);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SlotSwapFailed);
        seams.Probe.CallCount.Should().Be(1, "only the staging probe ran — production smoke test never runs when swap failed");
    }

    // ---------- AC-14 slot-swap throws ----------

    [Fact]
    public async Task AC14_SlotSwapThrows_FailsRetryableWithCleanup()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-14");
        var swapper = FakeSlotSwapper.Throws(new InvalidOperationException("ARM call faulted"), thenSuccess: true);
        var seams = FreshGreenSeams(overrideSwapper: swapper);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SlotSwapInfraFault);
    }

    // ---------- AC-15a smoke test failure + rollback success (completeness proof) ----------

    [Fact]
    public async Task AC15a_SmokeTestFailure_RollbackSuccess_ReSwapsWithIdenticalRequest_FailsRetryable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-15a");
        var swapper = FakeSlotSwapper.SuccessThenSuccess();
        var probe = FakeHealthProbe.SuccessThenFailure("HTTP 500 InternalServerError");
        var seams = FreshGreenSeams(overrideSwapper: swapper, overrideProbe: probe);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SmokeTestFailedRolledBack);
        failure.Diagnostic.Should().Contain("ROLLED BACK");

        // COMPLETENESS PROOF: exactly two swap calls, and the rollback
        // re-swap is a BYTE-IDENTICAL request to the initial swap (a slot
        // swap is self-inverse — re-invoking it restores the prior state).
        swapper.CallCount.Should().Be(2, "one initial swap + one rollback re-swap");
        swapper.LastRequests.Should().HaveCount(2);
        swapper.LastRequests[0].Should().Be(swapper.LastRequests[1],
            "the rollback re-swap MUST be the identical request to the initial swap — this is what makes re-swap a valid rollback");
        swapper.LastRequests[0].SourceSlotName.Should().Be(StagingSlotName);
        swapper.LastRequests[0].TargetSlotName.Should().Be("production");
    }

    // ---------- AC-15b smoke test failure + rollback failure (POML escalation trigger 2) ----------

    [Fact]
    public async Task AC15b_SmokeTestFailure_RollbackFailure_FailsQuarantineWithEscalation()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-15b");
        var swapper = FakeSlotSwapper.SuccessThenFailure("ARM 500 InternalServerError");
        var probe = FakeHealthProbe.SuccessThenFailure("HTTP 500 InternalServerError");
        var seams = FreshGreenSeams(overrideSwapper: swapper, overrideProbe: probe);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SmokeTestFailedRollbackAlsoFailed);
        failure.Diagnostic.Should().Contain("BOTH SLOTS BAD");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-15c smoke test failure + rollback throws ----------

    [Fact]
    public async Task AC15c_SmokeTestFailure_RollbackThrows_FailsQuarantineWithRollbackInfraFault()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-15c");
        var swapper = FakeSlotSwapper.SuccessThenThrows(new InvalidOperationException("ARM call died mid-swap"));
        var probe = FakeHealthProbe.SuccessThenFailure("HTTP 503 ServiceUnavailable");
        var seams = FreshGreenSeams(overrideSwapper: swapper, overrideProbe: probe);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.RollbackInfraFault);
        failure.Diagnostic.Should().Contain("BOTH SLOTS MAY BE BAD");
    }

    // ---------- AC-15d smoke test throws → rollback flow → SmokeTestInfraFault ----------

    [Fact]
    public async Task AC15d_SmokeTestThrows_TreatedAsFailure_RollbackSuccessMapsToSmokeTestInfraFault()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-15d");
        var swapper = FakeSlotSwapper.SuccessThenSuccess();
        var probe = FakeHealthProbe.SuccessThenThrows(new HttpRequestException("dns failure"));
        var seams = FreshGreenSeams(overrideSwapper: swapper, overrideProbe: probe);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SmokeTestInfraFault);
    }

    // ---------- AC-16 idempotency ----------

    [Fact]
    public async Task AC16a_IdempotentEarly_BuildIdSupplied_NoCollaboratorCalled()
    {
        var run = BuildRun();
        var expectedKey = H9BffDeployHandler.BuildIdempotencyKey(CustomerId, BuildId);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H9",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-16a");
        var seams = FreshGreenSeams();
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        seams.Verifier.CallCount.Should().Be(0, "the EARLY idempotency check short-circuits before any network call");
        seams.Downloader.CallCount.Should().Be(0);
        seams.Kudu.CallCount.Should().Be(0);
        seams.Swapper.CallCount.Should().Be(0);
        seams.Probe.CallCount.Should().Be(0);
        seams.Sizer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC16b_IdempotentLate_BuildIdAbsent_ManifestVerifierCalledButNothingElse()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.BuildIdParameterKey);
        var expectedKey = H9BffDeployHandler.BuildIdempotencyKey(CustomerId, ResolvedManifestOnlyBuildId);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H9",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-16b");
        var verifier = FakeManifestVerifier.Verified(ResolvedManifestOnlyBuildId, ArtifactBlobName);
        var seams = FreshGreenSeams(overrideVerifier: verifier);
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        seams.Verifier.CallCount.Should().Be(1, "buildId was absent — manifest resolution is required to compute the idempotency key");
        seams.Downloader.CallCount.Should().Be(0, "no download once the resolved key proves this build already completed");
        seams.Swapper.CallCount.Should().Be(0);
    }

    // ---------- AC-17 idempotency-key format determinism ----------

    [Fact]
    public void AC17_IdempotencyKey_Deterministic_CustomerIdAndBuildId()
    {
        var k1 = H9BffDeployHandler.BuildIdempotencyKey("acme", "ci-42");
        var k2 = H9BffDeployHandler.BuildIdempotencyKey("acme", "ci-42");
        k1.Should().Be(k2);
        k1.Should().Be("bff-acme-ci-42");

        H9BffDeployHandler.BuildIdempotencyKey("acme", "ci-43").Should().NotBe(k1,
            "new build => new key => re-deploy");
        H9BffDeployHandler.BuildIdempotencyKey("other", "ci-42").Should().NotBe(k1);
    }

    // ---------- AC-18 run not found ----------

    [Fact]
    public async Task AC18_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var seams = FreshGreenSeams();
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.RunNotFound);
    }

    // ---------- AC-19 handler-id mismatch ----------

    [Fact]
    public async Task AC19_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-19");
        var seams = FreshGreenSeams();
        var handler = BuildHandler(repo, seams.Verifier, seams.Downloader, seams.Kudu, seams.Swapper, seams.Probe, seams.Sizer);

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H2a",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- helpers ----------

    private H9BffDeployHandler BuildHandler(
        IProvisioningRunRepository repo,
        IArtifactManifestVerifier verifier,
        IBffArtifactDownloader downloader,
        IKuduZipDeployer kudu,
        IAppServiceSlotSwapper swapper,
        IHealthProbe probe,
        IBffPublishSizeReporter sizer,
        Action<BffDeployOptions>? configureOptions = null)
    {
        var options = new BffDeployOptions
        {
            // Point every filesystem-touching option at a path that intentionally
            // does not exist so no test accidentally reads the real repo tree.
            DeployReleaseScriptPath = Path.Combine(Path.GetTempPath(), "h9-test-nonexistent-deploy-release.ps1"),
            BaselinePublishSizeBytes = 44_960_000L,
            PublishSizeDeltaThresholdBytes = 5L * 1024L * 1024L,
            AbsolutePublishSizeCeilingBytes = 60L * 1024L * 1024L,
            ProvisioningArtifactsContainerUri = "https://faketest.blob.core.windows.net/provisioning-artifacts",
        };
        configureOptions?.Invoke(options);

        return new H9BffDeployHandler(
            repo, verifier, downloader, kudu, swapper, probe, sizer,
            Options.Create(options),
            NullLogger<H9BffDeployHandler>.Instance);
    }

    private sealed record GreenSeams(
        FakeManifestVerifier Verifier,
        FakeArtifactDownloader Downloader,
        FakeKuduDeployer Kudu,
        FakeSlotSwapper Swapper,
        FakeHealthProbe Probe,
        FakeSizeReporter Sizer);

    private GreenSeams FreshGreenSeams(
        FakeManifestVerifier? overrideVerifier = null,
        FakeArtifactDownloader? overrideDownloader = null,
        FakeKuduDeployer? overrideKudu = null,
        FakeSlotSwapper? overrideSwapper = null,
        FakeHealthProbe? overrideProbe = null,
        FakeSizeReporter? overrideSizer = null)
        => new(
            overrideVerifier ?? FakeManifestVerifier.Verified(BuildId, ArtifactBlobName),
            overrideDownloader ?? FakeArtifactDownloader.Success(_tempLocalZip),
            overrideKudu ?? FakeKuduDeployer.Success(),
            overrideSwapper ?? FakeSlotSwapper.Success(),
            overrideProbe ?? FakeHealthProbe.Success(),
            overrideSizer ?? FakeSizeReporter.Ok());

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H9BffDeployHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun()
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = "Model2Dedicated",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        run.Parameters.NonSecret[H9BffDeployHandler.TenantIdParameterKey] = TenantId;
        run.Parameters.NonSecret[H9BffDeployHandler.SubscriptionIdParameterKey] = SubscriptionId;
        run.Parameters.NonSecret[H9BffDeployHandler.ResourceGroupNameParameterKey] = ResourceGroupName;
        run.Parameters.NonSecret[H9BffDeployHandler.AppServiceNameParameterKey] = AppServiceName;
        run.Parameters.NonSecret[H9BffDeployHandler.BuildIdParameterKey] = BuildId;
        run.Parameters.NonSecret[H9BffDeployHandler.StagingSlotNameParameterKey] = StagingSlotName;
        run.Parameters.NonSecret[H9BffDeployHandler.HealthCheckPathParameterKey] = HealthCheckPath;
        return run;
    }

    // ---------- fakes ----------

    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private ProvisioningRun? _run;
        private string? _etag;
        public ProvisioningRun? LastWrittenRun { get; private set; }

        public FakeRepository(ProvisioningRun? run, string? etag)
        {
            _run = run;
            _etag = etag;
        }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_run is null || _etag is null
                ? null
                : new ProvisioningRunReadResult(_run, _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }

    private sealed class FakeManifestVerifier : IArtifactManifestVerifier
    {
        private readonly ArtifactManifestVerificationResult? _result;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }
        public string? LastRequestedBuildId { get; private set; }

        private FakeManifestVerifier(ArtifactManifestVerificationResult? result, Exception? throwOnCall)
        {
            _result = result;
            _throwOnCall = throwOnCall;
        }

        public static FakeManifestVerifier Verified(string buildId, string artifactBlobName)
            => new(new ArtifactManifestVerificationResult.Verified(
                new ArtifactManifest(
                    buildId,
                    "sha-test-abc123",
                    44_500_000L,
                    artifactBlobName,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["r3AnalyzersAsErrors"] = "Passed",
                        ["godClassRatchet"] = "Passed",
                        ["archTests"] = "Passed",
                        ["namingConformance"] = "Passed",
                        ["graphAppRoleParity"] = "Skipped",
                    })), null);

        public static FakeManifestVerifier Rejected(string diagnostic)
            => new(new ArtifactManifestVerificationResult.Rejected(diagnostic), null);

        public static FakeManifestVerifier Throws(Exception ex) => new(null, ex);

        public Task<ArtifactManifestVerificationResult> VerifyAsync(ArtifactManifestVerificationRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequestedBuildId = request.RequestedBuildId;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
        }
    }

    private sealed class FakeArtifactDownloader : IBffArtifactDownloader
    {
        private readonly ArtifactDownloadResult? _result;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }

        private FakeArtifactDownloader(ArtifactDownloadResult? result, Exception? throwOnCall)
        {
            _result = result;
            _throwOnCall = throwOnCall;
        }

        public static FakeArtifactDownloader Success(string localZipPath)
            => new(new ArtifactDownloadResult.Success(localZipPath, 44_500_000L), null);

        public static FakeArtifactDownloader Failure(string diagnostic)
            => new(new ArtifactDownloadResult.Failure(diagnostic), null);

        public static FakeArtifactDownloader Throws(Exception ex) => new(null, ex);

        public Task<ArtifactDownloadResult> DownloadAsync(ArtifactDownloadRequest request, CancellationToken ct)
        {
            CallCount++;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
        }
    }

    private sealed class FakeKuduDeployer : IKuduZipDeployer
    {
        private readonly KuduZipDeployResult? _result;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }

        private FakeKuduDeployer(KuduZipDeployResult? result, Exception? throwOnCall)
        {
            _result = result;
            _throwOnCall = throwOnCall;
        }

        public static FakeKuduDeployer Success() => new(new KuduZipDeployResult.Success(TimeSpan.FromSeconds(45)), null);

        public static FakeKuduDeployer Failure(string diagnostic) => new(new KuduZipDeployResult.Failure(diagnostic), null);

        public static FakeKuduDeployer Throws(Exception ex) => new(null, ex);

        public Task<KuduZipDeployResult> DeployAsync(KuduZipDeployRequest request, CancellationToken ct)
        {
            CallCount++;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
        }
    }

    private sealed class FakeSlotSwapper : IAppServiceSlotSwapper
    {
        private readonly Queue<Func<SlotSwapResult>> _plannedOutcomes;
        private readonly Exception? _firstCallThrows;
        private readonly Exception? _secondCallThrows;
        public int CallCount { get; private set; }
        public List<SlotSwapRequest> LastRequests { get; } = new();

        private FakeSlotSwapper(Queue<Func<SlotSwapResult>> plan, Exception? firstThrows, Exception? secondThrows)
        {
            _plannedOutcomes = plan;
            _firstCallThrows = firstThrows;
            _secondCallThrows = secondThrows;
        }

        public static FakeSlotSwapper Success()
        {
            var q = new Queue<Func<SlotSwapResult>>();
            q.Enqueue(() => new SlotSwapResult.Success(TimeSpan.FromSeconds(30)));
            return new(q, null, null);
        }

        public static FakeSlotSwapper Failure(string diagnostic)
        {
            var q = new Queue<Func<SlotSwapResult>>();
            q.Enqueue(() => new SlotSwapResult.Failure(diagnostic));
            return new(q, null, null);
        }

        public static FakeSlotSwapper SuccessThenSuccess()
        {
            var q = new Queue<Func<SlotSwapResult>>();
            q.Enqueue(() => new SlotSwapResult.Success(TimeSpan.FromSeconds(30)));
            q.Enqueue(() => new SlotSwapResult.Success(TimeSpan.FromSeconds(30)));
            return new(q, null, null);
        }

        public static FakeSlotSwapper SuccessThenFailure(string secondFailureDiagnostic)
        {
            var q = new Queue<Func<SlotSwapResult>>();
            q.Enqueue(() => new SlotSwapResult.Success(TimeSpan.FromSeconds(30)));
            q.Enqueue(() => new SlotSwapResult.Failure(secondFailureDiagnostic));
            return new(q, null, null);
        }

        public static FakeSlotSwapper Throws(Exception firstCallEx, bool thenSuccess)
            => new(new Queue<Func<SlotSwapResult>>(), firstCallEx,
                thenSuccess ? null : null);

        public static FakeSlotSwapper SuccessThenThrows(Exception secondCallEx)
        {
            var q = new Queue<Func<SlotSwapResult>>();
            q.Enqueue(() => new SlotSwapResult.Success(TimeSpan.FromSeconds(30)));
            return new(q, null, secondCallEx);
        }

        public Task<SlotSwapResult> SwapAsync(SlotSwapRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequests.Add(request);
            if (CallCount == 1 && _firstCallThrows is not null) throw _firstCallThrows;
            if (CallCount == 2 && _secondCallThrows is not null) throw _secondCallThrows;
            if (_plannedOutcomes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"FakeSlotSwapper exhausted its planned outcomes after {CallCount} calls.");
            }
            return Task.FromResult(_plannedOutcomes.Dequeue()());
        }
    }

    private sealed class FakeHealthProbe : IHealthProbe
    {
        private readonly Queue<Func<HealthProbeResult>> _plannedOutcomes;
        private readonly Exception? _firstCallThrows;
        private readonly Exception? _secondCallThrows;
        public int CallCount { get; private set; }
        public List<string> RequestedUrls { get; } = new();

        private FakeHealthProbe(Queue<Func<HealthProbeResult>> plan, Exception? firstThrows, Exception? secondThrows)
        {
            _plannedOutcomes = plan;
            _firstCallThrows = firstThrows;
            _secondCallThrows = secondThrows;
        }

        /// <summary>Every call (staging + production) returns 200.</summary>
        public static FakeHealthProbe Success()
        {
            var q = new Queue<Func<HealthProbeResult>>();
            q.Enqueue(() => new HealthProbeResult.Success(1, TimeSpan.FromSeconds(1)));
            q.Enqueue(() => new HealthProbeResult.Success(1, TimeSpan.FromSeconds(1)));
            return new(q, null, null);
        }

        /// <summary>First call (staging) fails — handler must not call again.</summary>
        public static FakeHealthProbe StagingFailure(string diagnostic)
        {
            var q = new Queue<Func<HealthProbeResult>>();
            q.Enqueue(() => new HealthProbeResult.Failure(24, diagnostic));
            return new(q, null, null);
        }

        /// <summary>Staging succeeds, production (post-swap) fails — triggers rollback.</summary>
        public static FakeHealthProbe SuccessThenFailure(string diagnostic)
        {
            var q = new Queue<Func<HealthProbeResult>>();
            q.Enqueue(() => new HealthProbeResult.Success(1, TimeSpan.FromSeconds(1)));
            q.Enqueue(() => new HealthProbeResult.Failure(24, diagnostic));
            return new(q, null, null);
        }

        /// <summary>Staging succeeds, production (post-swap) throws — triggers rollback.</summary>
        public static FakeHealthProbe SuccessThenThrows(Exception secondCallEx)
        {
            var q = new Queue<Func<HealthProbeResult>>();
            q.Enqueue(() => new HealthProbeResult.Success(1, TimeSpan.FromSeconds(1)));
            return new(q, null, secondCallEx);
        }

        public Task<HealthProbeResult> ProbeAsync(HealthProbeRequest request, CancellationToken ct)
        {
            CallCount++;
            RequestedUrls.Add(request.TargetUrl);
            if (CallCount == 1 && _firstCallThrows is not null) throw _firstCallThrows;
            if (CallCount == 2 && _secondCallThrows is not null) throw _secondCallThrows;
            if (_plannedOutcomes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"FakeHealthProbe exhausted its planned outcomes after {CallCount} calls.");
            }
            return Task.FromResult(_plannedOutcomes.Dequeue()());
        }
    }

    private sealed class FakeSizeReporter : IBffPublishSizeReporter
    {
        private readonly PublishSizeReport? _report;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }

        private FakeSizeReporter(PublishSizeReport? report, Exception? throwOnCall)
        {
            _report = report;
            _throwOnCall = throwOnCall;
        }

        public static FakeSizeReporter Ok(long bytes = 44_500_000L)
            => new(new PublishSizeReport(
                AbsoluteBytes: bytes,
                DeltaBytes: bytes - 44_960_000L,
                ExceedsDeltaThreshold: false,
                ExceedsAbsoluteCeiling: false,
                Summary: $"OK ({bytes} bytes)"), null);

        public static FakeSizeReporter WithFlags(long bytes, long deltaBytes, bool exceedsDelta, bool exceedsAbsolute)
            => new(new PublishSizeReport(
                AbsoluteBytes: bytes,
                DeltaBytes: deltaBytes,
                ExceedsDeltaThreshold: exceedsDelta,
                ExceedsAbsoluteCeiling: exceedsAbsolute,
                Summary: $"{(exceedsAbsolute ? "ABSOLUTE-CEILING-EXCEEDED " : "")}{(exceedsDelta ? "DELTA-EXCEEDED " : "")}({bytes} bytes)"), null);

        public static FakeSizeReporter Throws(Exception ex) => new(null, ex);

        public Task<PublishSizeReport> MeasureAsync(PublishSizeReportRequest request, CancellationToken ct)
        {
            CallCount++;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_report!);
        }
    }
}
