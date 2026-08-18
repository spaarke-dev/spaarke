// -----------------------------------------------------------------------------
// H9BffDeployHandlerTests.cs
//
// Unit tests over H9BffDeployHandler (task 052 — wave C4 Batch 4B).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live pwsh / az CLI / HTTP / Azure. Fakes
//   replace the repository + all FIVE collaborator seams (r3-gate verifier,
//   deploy runner, slot swapper, health probe, publish-size reporter) so the
//   handler orchestration + §4C rollback classification + blue-green swap +
//   rollback logic is exercised in isolation. Live-Azure coverage belongs in
//   env-guarded smoke tests (parity with H2a/H8 — a real BFF slot-swap
//   deploy requires an actual customer stamp + deploy artifact + Azure
//   round-trip).
//
// COVERAGE (POML acceptance criteria mapped to test cases):
//   AC-1  Happy path — all r3 gates green, deploy runner Success, publish-size
//         under threshold, slot swap Success, /health probe Success → handler
//         Success + CompletedPhase(H9) + NFR-01 gate Verified.
//   AC-2a Missing tenantId (§4D I1) → Resumable + MissingTenantId.
//   AC-2b Missing subscriptionId → Resumable + MissingSubscriptionId.
//   AC-2c Missing resourceGroupName → Resumable + MissingResourceGroupName.
//   AC-2d Missing appServiceName → Resumable + MissingAppServiceName.
//   AC-2e Missing buildId (idempotency) → Resumable + MissingBuildId.
//   AC-3  Spaarkedev1 hardcode detected in Deploy-Release.ps1 → QuarantineRequired
//         + Spaarkedev1HardcodeDetected (POML criterion 5 Gap 2 assertion);
//         verifier + runner NEVER called.
//   AC-4a r3-gate Analyzers Failed → Resumable + R3GateFailedAnalyzers;
//         runner + swapper NEVER called.
//   AC-4b r3-gate GodClassRatchet Failed → Resumable + R3GateFailedGodClassRatchet.
//   AC-4c r3-gate ArchTests Failed → Resumable + R3GateFailedArchTests.
//   AC-4d r3-gate NamingConformance Failed → Resumable + R3GateFailedNamingConformance.
//   AC-4e r3-gate GraphAppRoleParity Failed → Resumable + R3GateFailedGraphAppRoleParity.
//   AC-4f r3-gates all Skipped → treated as green (proceeds through the deploy).
//   AC-5  r3-gate verifier throws → Resumable + R3GateFailedAnalyzers.
//   AC-6  Deploy runner Failure → Resumable + BffDeployFailed;
//         publish-size reporter + swapper + probe NEVER called.
//   AC-7  Deploy runner throws → Resumable + BffDeployInfraFault.
//   AC-8  Deploy runner Success but outputs incomplete → Resumable +
//         BffDeployOutputsIncomplete.
//   AC-9  Publish-size reporter throws → Resumable + PublishSizeReporterInfraFault.
//   AC-10a Publish-size delta > threshold → QuarantineRequired +
//          PublishSizeDeltaExceeded; swapper NEVER called.
//   AC-10b Publish-size absolute > ceiling → QuarantineRequired +
//          PublishSizeDeltaExceeded.
//   AC-11 Slot-swap Failure → RetryableWithCleanup + SlotSwapFailed.
//   AC-12 Slot-swap throws → RetryableWithCleanup + SlotSwapInfraFault.
//   AC-13a Smoke test failure + rollback success → RetryableWithCleanup +
//          SmokeTestFailedRolledBack (POML criterion 3).
//   AC-13b Smoke test failure + rollback failure → QuarantineRequired +
//          SmokeTestFailedRollbackAlsoFailed (POML escalation trigger 2).
//   AC-13c Smoke test failure + rollback throws → QuarantineRequired +
//          RollbackInfraFault.
//   AC-13d Smoke test throws → treated as smoke test failure → rollback flow;
//          RetryableWithCleanup + SmokeTestInfraFault on rollback success.
//   AC-14 Idempotency — two invocations for same customerId+buildId; second
//         call is a no-op (bff-{customerId}-{buildId} match) — NO r3-verifier /
//         runner / swapper / probe calls.
//   AC-15 Idempotency-key format determinism — bff-{customerId}-{buildId}.
//   AC-16 Run not found → Resumable + RunNotFound.
//   AC-17 HandlerId mismatch → throws InvalidOperationException.
//   AC-18 Rejection code mapping — every R3GateKind maps to its distinct
//         rejection code (POML criterion 2).
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
    private const string StagingSlotName = "staging";
    private const string HealthCheckPath = "/healthz";

    private readonly string _tempPublishZip;

    public H9BffDeployHandlerTests()
    {
        // Real zero-byte temp zip so the publish-size reporter fake can be
        // driven by canned reports without a File.Exists coupling to a real
        // artifact; the reporter fake ignores the path anyway.
        _tempPublishZip = Path.Combine(Path.GetTempPath(), $"h9-test-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(_tempPublishZip, Array.Empty<byte>());
    }

    public void Dispose()
    {
        if (File.Exists(_tempPublishZip))
        {
            try { File.Delete(_tempPublishZip); } catch { /* best-effort cleanup */ }
        }
    }

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_AllGreen_SucceedsAndAdvancesState()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var verifier = FakeR3GateVerifier.AllGreen();
        var runner = FakeDeployRunner.Success(_tempPublishZip);
        var swapper = FakeSlotSwapper.Success();
        var probe = FakeHealthProbe.Success();
        var sizer = FakeSizeReporter.Ok(bytes: 44_000_000L);
        var handler = BuildHandler(repo, verifier, runner, swapper, probe, sizer);

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
        runner.CallCount.Should().Be(1);
        swapper.CallCount.Should().Be(1, "swap runs exactly once on the happy path");
        probe.CallCount.Should().Be(1);
        sizer.CallCount.Should().Be(1);

        swapper.LastRequests.Should().ContainSingle();
        swapper.LastRequests[0].SourceSlotName.Should().Be(StagingSlotName);
        swapper.LastRequests[0].TargetSlotName.Should().Be("production");
    }

    // ---------- AC-2 parameter guards ----------

    [Fact]
    public async Task AC2a_MissingTenantId_FailsResumable_NoDeploy()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.TenantIdParameterKey);
        var (verifier, runner, swapper, probe, sizer) = FreshGreenSeams();
        var repo = new FakeRepository(run, etag: "etag-2a");
        var handler = BuildHandler(repo, verifier, runner, swapper, probe, sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.MissingTenantId);
        verifier.CallCount.Should().Be(0);
        runner.CallCount.Should().Be(0);
        swapper.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC2b_MissingSubscriptionId_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.SubscriptionIdParameterKey);
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-2b"), out _);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.MissingSubscriptionId);
    }

    [Fact]
    public async Task AC2c_MissingResourceGroupName_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.ResourceGroupNameParameterKey);
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-2c"), out _);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.MissingResourceGroupName);
    }

    [Fact]
    public async Task AC2d_MissingAppServiceName_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.AppServiceNameParameterKey);
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-2d"), out _);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.MissingAppServiceName);
    }

    [Fact]
    public async Task AC2e_MissingBuildId_FailsResumable()
    {
        var run = BuildRun();
        run.Parameters.NonSecret.Remove(H9BffDeployHandler.BuildIdParameterKey);
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-2e"), out _);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.MissingBuildId);
    }

    // ---------- AC-3 Gap 2 spaarkedev1 hardcode detected ----------

    [Fact]
    public async Task AC3_Spaarkedev1HardcodeInDeployReleaseScript_FailsQuarantine_NoDeploy()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-3");
        var (verifier, runner, swapper, probe, sizer) = FreshGreenSeams();

        // Write a fixture Deploy-Release.ps1 containing the regressed literal.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"h9-test-deploy-release-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, "# a regressed deploy script\n$env = 'spaarkedev1'\n");

        try
        {
            var handler = BuildHandler(repo, verifier, runner, swapper, probe, sizer,
                configureOptions: o => o.DeployReleaseScriptPath = scriptPath);

            var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

            var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
            failure.Class.Should().Be(FailureClass.QuarantineRequired);
            failure.RejectionCode.Should().Be(BffDeployRejectionCodes.Spaarkedev1HardcodeDetected);
            failure.Diagnostic.Should().Contain("spaarkedev1");
            repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);

            verifier.CallCount.Should().Be(0, "spaarkedev1 pre-flight blocks the r3-gate verifier + everything downstream");
            runner.CallCount.Should().Be(0);
            swapper.CallCount.Should().Be(0);
        }
        finally
        {
            if (File.Exists(scriptPath)) { try { File.Delete(scriptPath); } catch { } }
        }
    }

    // ---------- AC-4 r3-era gate failures (POML criterion 2 distinct code per gate) ----------

    [Theory]
    [InlineData(R3GateKind.Analyzers, BffDeployRejectionCodes.R3GateFailedAnalyzers)]
    [InlineData(R3GateKind.GodClassRatchet, BffDeployRejectionCodes.R3GateFailedGodClassRatchet)]
    [InlineData(R3GateKind.ArchTests, BffDeployRejectionCodes.R3GateFailedArchTests)]
    [InlineData(R3GateKind.NamingConformance, BffDeployRejectionCodes.R3GateFailedNamingConformance)]
    [InlineData(R3GateKind.GraphAppRoleParity, BffDeployRejectionCodes.R3GateFailedGraphAppRoleParity)]
    public async Task AC4_R3GateFailed_BlocksSwap_WithDistinctRejectionCode(R3GateKind failingKind, string expectedCode)
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: $"etag-4-{failingKind}");
        var verifier = FakeR3GateVerifier.WithFailure(failingKind, "gate diagnostic");
        var runner = FakeDeployRunner.Success(_tempPublishZip);
        var swapper = FakeSlotSwapper.Success();
        var probe = FakeHealthProbe.Success();
        var sizer = FakeSizeReporter.Ok();
        var handler = BuildHandler(repo, verifier, runner, swapper, probe, sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable, "gate failures are fixable via code+build cycle");
        failure.RejectionCode.Should().Be(expectedCode);
        runner.CallCount.Should().Be(0, "runner never runs when a gate has failed");
        swapper.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AC4f_AllGatesSkipped_TreatedAsGreen_ProceedsThroughDeploy()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-4f");
        var verifier = FakeR3GateVerifier.AllSkipped();
        var runner = FakeDeployRunner.Success(_tempPublishZip);
        var swapper = FakeSlotSwapper.Success();
        var probe = FakeHealthProbe.Success();
        var sizer = FakeSizeReporter.Ok();
        var handler = BuildHandler(repo, verifier, runner, swapper, probe, sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        runner.CallCount.Should().Be(1);
        swapper.CallCount.Should().Be(1);
    }

    // ---------- AC-5 r3-gate verifier throws ----------

    [Fact]
    public async Task AC5_R3GateVerifierThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-5");
        var verifier = FakeR3GateVerifier.Throws(new InvalidOperationException("dotnet not on PATH"));
        var runner = FakeDeployRunner.Success(_tempPublishZip);
        var handler = BuildHandler(repo, verifier, runner,
            FakeSlotSwapper.Success(), FakeHealthProbe.Success(), FakeSizeReporter.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.R3GateFailedAnalyzers);
        failure.Diagnostic.Should().Contain("InvalidOperationException");
    }

    // ---------- AC-6 deploy runner Failure ----------

    [Fact]
    public async Task AC6_DeployRunnerFailure_FailsResumable_NoSwap()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var runner = FakeDeployRunner.Failure("Deploy-BffApi.ps1 exit 1: staging deploy failed");
        var swapper = FakeSlotSwapper.Success();
        var probe = FakeHealthProbe.Success();
        var sizer = FakeSizeReporter.Ok();
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(), runner, swapper, probe, sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.BffDeployFailed);
        failure.Diagnostic.Should().Contain("staging deploy failed");
        swapper.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
        sizer.CallCount.Should().Be(0);
    }

    // ---------- AC-7 deploy runner throws ----------

    [Fact]
    public async Task AC7_DeployRunnerThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-7");
        var runner = FakeDeployRunner.Throws(new FileNotFoundException("script not found"));
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(), runner,
            FakeSlotSwapper.Success(), FakeHealthProbe.Success(), FakeSizeReporter.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.BffDeployInfraFault);
    }

    // ---------- AC-8 deploy runner outputs incomplete ----------

    [Fact]
    public async Task AC8_DeployRunnerOutputsIncomplete_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var runner = FakeDeployRunner.SuccessWithBlankUrls();
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(), runner,
            FakeSlotSwapper.Success(), FakeHealthProbe.Success(), FakeSizeReporter.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.BffDeployOutputsIncomplete);
    }

    // ---------- AC-9 publish-size reporter throws ----------

    [Fact]
    public async Task AC9_PublishSizeReporterThrows_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-9");
        var sizer = FakeSizeReporter.Throws(new FileNotFoundException("zip missing"));
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            FakeSlotSwapper.Success(), FakeHealthProbe.Success(), sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.PublishSizeReporterInfraFault);
    }

    // ---------- AC-10 publish-size thresholds ----------

    [Fact]
    public async Task AC10a_PublishSizeDeltaExceeded_FailsQuarantine_NoSwap()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-10a");
        var sizer = FakeSizeReporter.WithFlags(bytes: 55_000_000L, deltaBytes: 10_000_000L,
            exceedsDelta: true, exceedsAbsolute: false);
        var swapper = FakeSlotSwapper.Success();
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            swapper, FakeHealthProbe.Success(), sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.PublishSizeDeltaExceeded);
        swapper.CallCount.Should().Be(0, "swap never runs when NFR-01 threshold is exceeded");
    }

    [Fact]
    public async Task AC10b_PublishSizeAbsoluteCeilingExceeded_FailsQuarantine()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-10b");
        var sizer = FakeSizeReporter.WithFlags(bytes: 65_000_000L, deltaBytes: 20_000_000L,
            exceedsDelta: true, exceedsAbsolute: true);
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            FakeSlotSwapper.Success(), FakeHealthProbe.Success(), sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.PublishSizeDeltaExceeded);
    }

    // ---------- AC-11 slot-swap Failure ----------

    [Fact]
    public async Task AC11_SlotSwapFailure_FailsRetryableWithCleanup()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-11");
        var swapper = FakeSlotSwapper.Failure("ARM 403 AuthorizationFailed");
        var probe = FakeHealthProbe.Success();
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            swapper, probe, FakeSizeReporter.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SlotSwapFailed);
        probe.CallCount.Should().Be(0, "smoke test never runs when swap failed");
    }

    // ---------- AC-12 slot-swap throws ----------

    [Fact]
    public async Task AC12_SlotSwapThrows_FailsRetryableWithCleanup()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12");
        var swapper = FakeSlotSwapper.Throws(new InvalidOperationException("az not on PATH"), thenSuccess: true);
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            swapper, FakeHealthProbe.Success(), FakeSizeReporter.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SlotSwapInfraFault);
    }

    // ---------- AC-13a smoke test failure + rollback success (POML criterion 3) ----------

    [Fact]
    public async Task AC13a_SmokeTestFailure_RollbackSuccess_ReSwapsAndFailsRetryable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13a");
        var swapper = FakeSlotSwapper.SuccessThenSuccess();
        var probe = FakeHealthProbe.Failure("HTTP 500 InternalServerError");
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            swapper, probe, FakeSizeReporter.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SmokeTestFailedRolledBack);
        failure.Diagnostic.Should().Contain("ROLLED BACK");
        swapper.CallCount.Should().Be(2, "one initial swap + one rollback re-swap");
    }

    // ---------- AC-13b smoke test failure + rollback failure (POML escalation trigger 2) ----------

    [Fact]
    public async Task AC13b_SmokeTestFailure_RollbackFailure_FailsQuarantineWithEscalation()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13b");
        var swapper = FakeSlotSwapper.SuccessThenFailure("ARM 500 InternalServerError");
        var probe = FakeHealthProbe.Failure("HTTP 500 InternalServerError");
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            swapper, probe, FakeSizeReporter.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SmokeTestFailedRollbackAlsoFailed);
        failure.Diagnostic.Should().Contain("BOTH SLOTS BAD");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-13c smoke test failure + rollback throws ----------

    [Fact]
    public async Task AC13c_SmokeTestFailure_RollbackThrows_FailsQuarantineWithRollbackInfraFault()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13c");
        var swapper = FakeSlotSwapper.SuccessThenThrows(new InvalidOperationException("az CLI died"));
        var probe = FakeHealthProbe.Failure("HTTP 503 ServiceUnavailable");
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            swapper, probe, FakeSizeReporter.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.RollbackInfraFault);
        failure.Diagnostic.Should().Contain("BOTH SLOTS MAY BE BAD");
    }

    // ---------- AC-13d smoke test throws → rollback flow → SmokeTestInfraFault ----------

    [Fact]
    public async Task AC13d_SmokeTestThrows_TreatedAsFailure_RollbackSuccessMapsToSmokeTestInfraFault()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13d");
        var swapper = FakeSlotSwapper.SuccessThenSuccess();
        var probe = FakeHealthProbe.Throws(new HttpRequestException("dns failure"));
        var handler = BuildHandler(repo, FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            swapper, probe, FakeSizeReporter.Ok());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.SmokeTestInfraFault);
    }

    // ---------- AC-14 idempotency (level-3 durable no-op) ----------

    [Fact]
    public async Task AC14_Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
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
        var repo = new FakeRepository(run, etag: "etag-14");
        var (verifier, runner, swapper, probe, sizer) = FreshGreenSeams();
        var handler = BuildHandler(repo, verifier, runner, swapper, probe, sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        verifier.CallCount.Should().Be(0);
        runner.CallCount.Should().Be(0);
        swapper.CallCount.Should().Be(0);
        probe.CallCount.Should().Be(0);
        sizer.CallCount.Should().Be(0);
    }

    // ---------- AC-15 idempotency-key format determinism ----------

    [Fact]
    public void AC15_IdempotencyKey_Deterministic_CustomerIdAndBuildId()
    {
        var k1 = H9BffDeployHandler.BuildIdempotencyKey("acme", "ci-42");
        var k2 = H9BffDeployHandler.BuildIdempotencyKey("acme", "ci-42");
        k1.Should().Be(k2);
        k1.Should().Be("bff-acme-ci-42");

        H9BffDeployHandler.BuildIdempotencyKey("acme", "ci-43").Should().NotBe(k1,
            "new build => new key => re-deploy");
        H9BffDeployHandler.BuildIdempotencyKey("other", "ci-42").Should().NotBe(k1);
    }

    // ---------- AC-16 run not found ----------

    [Fact]
    public async Task AC16_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var (verifier, runner, swapper, probe, sizer) = FreshGreenSeams();
        var handler = BuildHandler(repo, verifier, runner, swapper, probe, sizer);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(BffDeployRejectionCodes.RunNotFound);
    }

    // ---------- AC-17 handler-id mismatch ----------

    [Fact]
    public async Task AC17_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-17");
        var (verifier, runner, swapper, probe, sizer) = FreshGreenSeams();
        var handler = BuildHandler(repo, verifier, runner, swapper, probe, sizer);

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

    // ---------- AC-18 rejection code mapping ----------

    [Theory]
    [InlineData(R3GateKind.Analyzers, BffDeployRejectionCodes.R3GateFailedAnalyzers)]
    [InlineData(R3GateKind.GodClassRatchet, BffDeployRejectionCodes.R3GateFailedGodClassRatchet)]
    [InlineData(R3GateKind.ArchTests, BffDeployRejectionCodes.R3GateFailedArchTests)]
    [InlineData(R3GateKind.NamingConformance, BffDeployRejectionCodes.R3GateFailedNamingConformance)]
    [InlineData(R3GateKind.GraphAppRoleParity, BffDeployRejectionCodes.R3GateFailedGraphAppRoleParity)]
    public void AC18_RejectionCodeMapping_IsDistinctPerGate(R3GateKind kind, string expectedCode)
    {
        H9BffDeployHandler.MapGateKindToRejectionCode(kind).Should().Be(expectedCode);
    }

    // ---------- helpers ----------

    private H9BffDeployHandler BuildHandler(
        IProvisioningRunRepository repo,
        IR3GateVerifier verifier,
        IBffDeployRunner runner,
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
            BffPublishZipPath = _tempPublishZip,
            BaselinePublishSizeBytes = 44_960_000L,
            PublishSizeDeltaThresholdBytes = 5L * 1024L * 1024L,
            AbsolutePublishSizeCeilingBytes = 60L * 1024L * 1024L,
        };
        configureOptions?.Invoke(options);

        return new H9BffDeployHandler(
            repo, verifier, runner, swapper, probe, sizer,
            Options.Create(options),
            NullLogger<H9BffDeployHandler>.Instance);
    }

    private H9BffDeployHandler BuildHandler(
        IProvisioningRunRepository repo,
        out FakeR3GateVerifier verifier)
    {
        var (v, runner, swapper, probe, sizer) = FreshGreenSeams();
        verifier = v;
        return BuildHandler(repo, v, runner, swapper, probe, sizer);
    }

    private (FakeR3GateVerifier, FakeDeployRunner, FakeSlotSwapper, FakeHealthProbe, FakeSizeReporter) FreshGreenSeams()
        => (FakeR3GateVerifier.AllGreen(),
            FakeDeployRunner.Success(_tempPublishZip),
            FakeSlotSwapper.Success(),
            FakeHealthProbe.Success(),
            FakeSizeReporter.Ok());

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

    private sealed class FakeR3GateVerifier : IR3GateVerifier
    {
        private readonly R3GateVerificationResult? _result;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }

        private FakeR3GateVerifier(R3GateVerificationResult? result, Exception? throwOnCall)
        {
            _result = result;
            _throwOnCall = throwOnCall;
        }

        public static FakeR3GateVerifier AllGreen()
            => new(new R3GateVerificationResult(new[]
            {
                new R3GateOutcome(R3GateKind.Analyzers, R3GateStatus.Passed, ""),
                new R3GateOutcome(R3GateKind.GodClassRatchet, R3GateStatus.Passed, ""),
                new R3GateOutcome(R3GateKind.ArchTests, R3GateStatus.Passed, ""),
                new R3GateOutcome(R3GateKind.NamingConformance, R3GateStatus.Passed, ""),
                new R3GateOutcome(R3GateKind.GraphAppRoleParity, R3GateStatus.Passed, ""),
            }), null);

        public static FakeR3GateVerifier AllSkipped()
            => new(new R3GateVerificationResult(new[]
            {
                new R3GateOutcome(R3GateKind.Analyzers, R3GateStatus.Skipped, "not landed"),
                new R3GateOutcome(R3GateKind.GodClassRatchet, R3GateStatus.Skipped, "not landed"),
                new R3GateOutcome(R3GateKind.ArchTests, R3GateStatus.Skipped, "not landed"),
                new R3GateOutcome(R3GateKind.NamingConformance, R3GateStatus.Skipped, "not landed"),
                new R3GateOutcome(R3GateKind.GraphAppRoleParity, R3GateStatus.Skipped, "not landed"),
            }), null);

        public static FakeR3GateVerifier WithFailure(R3GateKind failingKind, string diagnostic)
        {
            var outcomes = new List<R3GateOutcome>();
            foreach (var k in Enum.GetValues<R3GateKind>())
            {
                outcomes.Add(k == failingKind
                    ? new R3GateOutcome(k, R3GateStatus.Failed, diagnostic)
                    : new R3GateOutcome(k, R3GateStatus.Passed, ""));
            }
            return new(new R3GateVerificationResult(outcomes), null);
        }

        public static FakeR3GateVerifier Throws(Exception ex) => new(null, ex);

        public Task<R3GateVerificationResult> VerifyAsync(R3GateVerificationRequest request, CancellationToken ct)
        {
            CallCount++;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
        }
    }

    private sealed class FakeDeployRunner : IBffDeployRunner
    {
        private readonly BffDeployRunnerOutcome? _outcome;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }

        private FakeDeployRunner(BffDeployRunnerOutcome? outcome, Exception? throwOnCall)
        {
            _outcome = outcome;
            _throwOnCall = throwOnCall;
        }

        public static FakeDeployRunner Success(string publishZipPath)
            => new(new BffDeployRunnerOutcome.Success(new BffDeployRunnerOutputs
            {
                PublishZipPath = publishZipPath,
                StagingSlotUrl = "https://spaarke-bff-acme-staging.azurewebsites.net",
                ProductionSlotUrl = "https://spaarke-bff-acme.azurewebsites.net",
                Duration = TimeSpan.FromSeconds(60),
            }), null);

        public static FakeDeployRunner SuccessWithBlankUrls()
            => new(new BffDeployRunnerOutcome.Success(new BffDeployRunnerOutputs
            {
                PublishZipPath = "path",
                StagingSlotUrl = "",
                ProductionSlotUrl = "",
                Duration = TimeSpan.Zero,
            }), null);

        public static FakeDeployRunner Failure(string diagnostic)
            => new(new BffDeployRunnerOutcome.Failure(diagnostic), null);

        public static FakeDeployRunner Throws(Exception ex) => new(null, ex);

        public Task<BffDeployRunnerOutcome> DeployAsync(BffDeployRunnerRequest request, CancellationToken ct)
        {
            CallCount++;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_outcome!);
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
        private readonly HealthProbeResult? _result;
        private readonly Exception? _throwOnCall;
        public int CallCount { get; private set; }

        private FakeHealthProbe(HealthProbeResult? result, Exception? throwOnCall)
        {
            _result = result;
            _throwOnCall = throwOnCall;
        }

        public static FakeHealthProbe Success()
            => new(new HealthProbeResult.Success(1, TimeSpan.FromSeconds(2)), null);

        public static FakeHealthProbe Failure(string diagnostic)
            => new(new HealthProbeResult.Failure(24, diagnostic), null);

        public static FakeHealthProbe Throws(Exception ex) => new(null, ex);

        public Task<HealthProbeResult> ProbeAsync(HealthProbeRequest request, CancellationToken ct)
        {
            CallCount++;
            if (_throwOnCall is not null) throw _throwOnCall;
            return Task.FromResult(_result!);
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
