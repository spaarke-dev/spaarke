// -----------------------------------------------------------------------------
// H0PreflightHandlerTests.cs
//
// Unit tests over H0PreflightHandler (task 041 — first handler wave C4).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Cosmos / Service Bus / pwsh /
//   Azure API. Fakes replace the repository, enqueuer, and probes so the
//   handler orchestration logic is exercised in isolation. Live-resource
//   coverage lives in env-guarded smoke tests (Cosmos + SB round-trips;
//   the PowerShell probe itself is out of the CI unit suite by design —
//   integration coverage would ship alongside a real dev-env probe wiring).
//
// COVERAGE:
//   T1  Happy path: all four probes Pass → Success + Cosmos state advances
//                   to CurrentPhase=H0.5 + CompletedPhase for H0 recorded +
//                   H0.5 enqueued.
//   T2  Idempotent no-op: run already has an H0 CompletedPhase matching the
//                         computed idempotency key → Success (no state
//                         mutation, no enqueue).
//   T3  Missing tenantId (§ 4D I1): Failure(Resumable, missing-tenant-id) +
//                                   Cosmos marked Failed + no probe fires +
//                                   no enqueue.
//   T4  Azure OpenAI TPM headroom insufficient: Failure(Resumable,
//                                                quota-openai-tpm) with
//                                                distinct rejection code +
//                                                Cosmos Failed with gate
//                                                entry.
//   T5  Dataverse env-creation rate insufficient: Failure with distinct
//                                                  quota-dataverse-env-rate
//                                                  rejection code.
//   T6  Subscription vCPU insufficient: Failure with distinct
//                                        quota-subscription-vcpu rejection
//                                        code.
//   T7  SPE cert missing: Failure with distinct spe-cert-bootstrap-missing
//                          rejection code.
//   T8  Run not found in Cosmos partition: Failure(Resumable, run-not-found).
//   T9  HandlerId mismatch: throws InvalidOperationException (defensive
//                            dispatch bug detector).
//   T10 Idempotency key determinism: same customerId + parameters produce
//                                    the same idempotency key regardless of
//                                    dictionary insertion order.
//
//   FR-34 UPGRADE-MODE VERSION-COMPAT GATE (Wave G-8 Batch 10, defect #24):
//   T11 Upgrade + Green verdict: matrix queried once with the correct
//       current/target pairs; probes still fire; run succeeds.
//   T12 Upgrade + Red verdict: Failure(Resumable, upgrade-compat-red) BEFORE
//       any probe fires; Cosmos Failed + gate entry recorded; no enqueue.
//   T13 Upgrade + Yellow verdict: run proceeds (Success) with a Pending
//       `upgrade-compat-yellow` gate entry persisted on the success write.
//   T14 Upgrade + missing version parameters: Failure(Resumable,
//       upgrade-compat-missing-versions); matrix NOT queried; no probe fires.
//   T15 Upgrade + matrix source fault (throws): Failure(Resumable,
//       upgrade-compat-matrix-unavailable); no probe fires.
//   (Non-upgrade runs implicitly assert the matrix is NEVER queried: the
//   default matrix stub used by T1–T9 throws on any call.)
//
//   HANDLER-03 (F1) OpenAI pin-freshness — Wave 2.5 live-impl-replaces-scaffold:
//   H03-A Stub-probe test proves H0 rejection-code mapping:
//         PreflightCheckNames.OpenAiPinFreshness → "quota-openai-pin-stale".
//   H03-B Real-probe end-to-end test wires ArmOpenAiPinFreshnessProbe (real
//         Azure.ResourceManager.CognitiveServices SDK) against a fake ARM HTTP
//         transport returning lifecycleStatus="Deprecating" for gpt-4o@2024-08-06;
//         asserts H0 returns Failure(Resumable, "quota-openai-pin-stale") with
//         model name + pinned version in the diagnostic + evidence payload.
//         Proves the probe genuinely reaches ARM (URL asserted) — closes the
//         "verify probe is not stubbed" gap called out on the punchlist.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H0PreflightHandlerTests
{
    private const string CustomerId = "acme-corp";
    private const string RunId = "01j7q3zp-preflight-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";

    // ---------- T1 happy path ----------

    [Fact]
    public async Task HappyPath_AllProbesPass_ReturnsSuccessAndEnqueuesDownstream()
    {
        var run = BuildRunWithTenant();
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probes = new[]
        {
            FakeProbe.Pass(PreflightCheckNames.AzureOpenAiTpmHeadroom),
            FakeProbe.Pass(PreflightCheckNames.DataverseEnvCreationRate),
            FakeProbe.Pass(PreflightCheckNames.SubscriptionVCpuQuota),
            FakeProbe.Pass(PreflightCheckNames.SpeCertBootstrap),
        };
        var handler = CreateHandler(repo, enqueuer, probes);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        var success = (HandlerResult.Success)result;
        success.IdempotencyKey.Should().StartWith($"preflight-{CustomerId}-");

        // Cosmos state: CurrentPhase advanced to H0.5, CompletedPhase for H0 recorded, Status = Running.
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H0.5");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be("H0");
        repo.LastWrittenRun.CompletedPhases[0].IdempotencyKey.Should().Be(success.IdempotencyKey);

        // Downstream enqueue happened, with the H0.5 handler id.
        enqueuer.Sent.Should().ContainSingle();
        enqueuer.Sent[0].HandlerId.Should().Be("H0.5");
        enqueuer.Sent[0].RunId.Should().Be(RunId);
        enqueuer.Sent[0].CustomerId.Should().Be(CustomerId);

        // Every probe was called exactly once.
        probes.All(p => ((FakeProbe)p).CallCount == 1).Should().BeTrue();
    }

    // ---------- T2 idempotency ----------

    [Fact]
    public async Task Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRunWithTenant();
        var expectedKey = H0PreflightHandler.ComputeIdempotencyKey(CustomerId, run.Parameters);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H0",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-2");
        var enqueuer = new FakeEnqueuer();
        var probes = new[] { FakeProbe.Pass(PreflightCheckNames.AzureOpenAiTpmHeadroom) };
        var handler = CreateHandler(repo, enqueuer, probes);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        enqueuer.Sent.Should().BeEmpty("idempotent no-op does not re-enqueue");
        ((FakeProbe)probes[0]).CallCount.Should().Be(0, "idempotent no-op does not fire probes");
    }

    // ---------- T3 missing tenantId (§ 4D I1) ----------

    [Fact]
    public async Task MissingTenantId_FailsWithSpecificRejectionCode_NoProbeFires()
    {
        var run = BuildRun(); // No tenantId parameter.
        var repo = new FakeRepository(run, etag: "etag-3");
        var enqueuer = new FakeEnqueuer();
        var probes = new[] { FakeProbe.Pass(PreflightCheckNames.AzureOpenAiTpmHeadroom) };
        var handler = CreateHandler(repo, enqueuer, probes);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be("missing-tenant-id");
        failure.Diagnostic.Should().Contain("§ 4D I1");

        ((FakeProbe)probes[0]).CallCount.Should().Be(0, "§ 4D I1 guard fires before any probe");
        enqueuer.Sent.Should().BeEmpty();

        // Cosmos was marked Failed with the specific rejection code.
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        repo.LastWrittenRun.ErrorDetail.Should().Contain("missing-tenant-id");
        repo.LastWrittenRun.GateStates.Should().ContainKey("preflight-missing-tenant-id");
    }

    // ---------- T4-T7 per-probe failure paths → distinct rejection codes ----------

    [Theory]
    [InlineData(PreflightCheckNames.AzureOpenAiTpmHeadroom, "quota-openai-tpm")]
    [InlineData(PreflightCheckNames.DataverseEnvCreationRate, "quota-dataverse-env-rate")]
    [InlineData(PreflightCheckNames.SubscriptionVCpuQuota, "quota-subscription-vcpu")]
    [InlineData(PreflightCheckNames.SpeCertBootstrap, "spe-cert-bootstrap-missing")]
    public async Task ProbeFailure_ProducesDistinctRejectionCode_AndMarksCosmosFailed(
        string failingCheck,
        string expectedRejectionCode)
    {
        var run = BuildRunWithTenant();
        var repo = new FakeRepository(run, etag: "etag-4");
        var enqueuer = new FakeEnqueuer();
        var probes = new IPreflightQuotaProbe[]
        {
            failingCheck == PreflightCheckNames.AzureOpenAiTpmHeadroom
                ? FakeProbe.Fail(PreflightCheckNames.AzureOpenAiTpmHeadroom, "OpenAI TPM: observed 100/1000 required in eastus")
                : FakeProbe.Pass(PreflightCheckNames.AzureOpenAiTpmHeadroom),
            failingCheck == PreflightCheckNames.DataverseEnvCreationRate
                ? FakeProbe.Fail(PreflightCheckNames.DataverseEnvCreationRate, "Env-rate: 4/4 used this hour")
                : FakeProbe.Pass(PreflightCheckNames.DataverseEnvCreationRate),
            failingCheck == PreflightCheckNames.SubscriptionVCpuQuota
                ? FakeProbe.Fail(PreflightCheckNames.SubscriptionVCpuQuota, "vCPU: 0/8 required standardDv5Family in eastus")
                : FakeProbe.Pass(PreflightCheckNames.SubscriptionVCpuQuota),
            failingCheck == PreflightCheckNames.SpeCertBootstrap
                ? FakeProbe.Fail(PreflightCheckNames.SpeCertBootstrap, "SPE cert: secret 'spe-owner-cert-pfx' not found in vault spaarke-platform-kv")
                : FakeProbe.Pass(PreflightCheckNames.SpeCertBootstrap),
        };
        var handler = CreateHandler(repo, enqueuer, probes);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedRejectionCode);

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        repo.LastWrittenRun.ErrorDetail.Should().Contain(expectedRejectionCode);
        repo.LastWrittenRun.GateStates.Should().ContainKey($"preflight-{expectedRejectionCode}");
        enqueuer.Sent.Should().BeEmpty("H0 blocks the run; no downstream enqueue on preflight failure");
    }

    // ---------- T8 run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsResumableFailure_NoStateWriteNoEnqueue()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var enqueuer = new FakeEnqueuer();
        var probes = new[] { FakeProbe.Pass(PreflightCheckNames.AzureOpenAiTpmHeadroom) };
        var handler = CreateHandler(repo, enqueuer, probes);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be("run-not-found");
        repo.LastWrittenRun.Should().BeNull();
        enqueuer.Sent.Should().BeEmpty();
    }

    // ---------- T9 defensive dispatch check ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws()
    {
        var run = BuildRunWithTenant();
        var repo = new FakeRepository(run, etag: "etag-5");
        var enqueuer = new FakeEnqueuer();
        var handler = CreateHandler(repo, enqueuer, Array.Empty<IPreflightQuotaProbe>());

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H1", // Mismatch — H0 handler must not silently execute someone else's envelope.
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- T10 idempotency key determinism ----------

    [Fact]
    public void IdempotencyKey_IsDeterministicAcrossDictionaryOrder()
    {
        var params1 = new RunParameters();
        params1.NonSecret["tenantId"] = TenantId;
        params1.NonSecret["region"] = "eastus";
        params1.NonSecret["subscriptionId"] = "sub-1";

        var params2 = new RunParameters();
        params2.NonSecret["subscriptionId"] = "sub-1";
        params2.NonSecret["region"] = "eastus";
        params2.NonSecret["tenantId"] = TenantId;

        var key1 = H0PreflightHandler.ComputeIdempotencyKey(CustomerId, params1);
        var key2 = H0PreflightHandler.ComputeIdempotencyKey(CustomerId, params2);
        key1.Should().Be(key2, "canonical JSON hash is insensitive to dictionary insertion order");
        key1.Should().StartWith($"preflight-{CustomerId}-");
    }

    // ---------- T11-T15 FR-34 upgrade-mode version-compat gate ----------

    [Fact]
    public async Task Upgrade_GreenVerdict_QueriesMatrixWithCorrectPairs_AndProceeds()
    {
        var run = BuildUpgradeRun();
        var repo = new FakeRepository(run, etag: "etag-11");
        var enqueuer = new FakeEnqueuer();
        var probes = AllPassProbes();
        var matrix = new FakeMatrix(VersionCompatVerdict.Green);
        var handler = CreateHandler(repo, enqueuer, probes, matrix);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        matrix.CallCount.Should().Be(1);
        matrix.LastCurrent.Should().Be(new VersionPair("1.0.0-net10", "S2026.08"));
        matrix.LastTarget.Should().Be(new VersionPair("1.1.0-net10", "S2026.09"));
        probes.All(p => ((FakeProbe)p).CallCount == 1).Should().BeTrue("Green verdict does not block the probes");
        enqueuer.Sent.Should().ContainSingle();
        repo.LastWrittenRun!.GateStates.Should().NotContainKey(
            H0PreflightHandler.UpgradeCompatYellowGateId, "Green records no operator-ACK gate");
    }

    [Fact]
    public async Task Upgrade_RedVerdict_FailsFastBeforeAnyProbe_WithGateEntry()
    {
        var run = BuildUpgradeRun();
        var repo = new FakeRepository(run, etag: "etag-12");
        var enqueuer = new FakeEnqueuer();
        var probes = AllPassProbes();
        var matrix = new FakeMatrix(VersionCompatVerdict.Red, ucbClasses: new[] { "U-CB-1" });
        var handler = CreateHandler(repo, enqueuer, probes, matrix);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be("upgrade-compat-red");
        failure.Diagnostic.Should().Contain("stub-Red");

        probes.All(p => ((FakeProbe)p).CallCount == 0).Should().BeTrue("Red blocks BEFORE any quota probe fires");
        enqueuer.Sent.Should().BeEmpty();
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        repo.LastWrittenRun.ErrorDetail.Should().Contain("upgrade-compat-red");
        repo.LastWrittenRun.GateStates.Should().ContainKey("preflight-upgrade-compat-red");
    }

    [Fact]
    public async Task Upgrade_YellowVerdict_ProceedsWithPendingOperatorAckGate()
    {
        var run = BuildUpgradeRun();
        var repo = new FakeRepository(run, etag: "etag-13");
        var enqueuer = new FakeEnqueuer();
        var probes = AllPassProbes();
        var matrix = new FakeMatrix(VersionCompatVerdict.Yellow, ucbClasses: new[] { "U-CB-3" });
        var handler = CreateHandler(repo, enqueuer, probes, matrix);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>("Yellow warns but does NOT block");
        probes.All(p => ((FakeProbe)p).CallCount == 1).Should().BeTrue();
        enqueuer.Sent.Should().ContainSingle();

        // The operator-ACK obligation is persisted on the success write.
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        var gate = repo.LastWrittenRun.GateStates.Should()
            .ContainKey(H0PreflightHandler.UpgradeCompatYellowGateId).WhoseValue;
        gate.Status.Should().Be(GateState.Pending);
        gate.VerifierHandler.Should().Be("H0");
        gate.Evidence.Should().NotBeNull();
        gate.Evidence!.Value.GetProperty("ucbClasses")[0].GetString().Should().Be("U-CB-3");
        gate.Evidence.Value.GetProperty("targetBffVersion").GetString().Should().Be("1.1.0-net10");
    }

    [Fact]
    public async Task Upgrade_MissingVersionParameters_FailsWithoutQueryingMatrixOrProbes()
    {
        var run = BuildUpgradeRun(includeVersions: false);
        var repo = new FakeRepository(run, etag: "etag-14");
        var enqueuer = new FakeEnqueuer();
        var probes = AllPassProbes();
        var matrix = new FakeMatrix(VersionCompatVerdict.Green);
        var handler = CreateHandler(repo, enqueuer, probes, matrix);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be("upgrade-compat-missing-versions");
        failure.Diagnostic.Should().Contain(H0PreflightHandler.CurrentBffVersionParameterKey)
            .And.Contain(H0PreflightHandler.TargetSolutionVersionParameterKey);

        matrix.CallCount.Should().Be(0, "cannot query the matrix without the version pairs");
        probes.All(p => ((FakeProbe)p).CallCount == 0).Should().BeTrue();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        repo.LastWrittenRun.GateStates.Should().ContainKey("preflight-upgrade-compat-missing-versions");
    }

    [Fact]
    public async Task Upgrade_MatrixSourceFault_FailsResumable_NoProbeFires()
    {
        var run = BuildUpgradeRun();
        var repo = new FakeRepository(run, etag: "etag-15");
        var enqueuer = new FakeEnqueuer();
        var probes = AllPassProbes();
        var matrix = new FakeMatrix(
            VersionCompatVerdict.Green,
            throws: new InvalidOperationException("matrix file corrupt"));
        var handler = CreateHandler(repo, enqueuer, probes, matrix);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be("upgrade-compat-matrix-unavailable");
        failure.Diagnostic.Should().Contain("matrix file corrupt");
        probes.All(p => ((FakeProbe)p).CallCount == 0).Should().BeTrue();
        enqueuer.Sent.Should().BeEmpty();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- HANDLER-03 (F1) OpenAiPinFreshness → quota-openai-pin-stale ----------
    //
    // Wave 2.5 live-impl-replaces-scaffold coverage (pre-dispatch audit
    // 2026-08-27 punchlist HANDLER-03). Wave 2 landed the probe + the H0
    // BuildRejectionCode case + probe-level Evaluate() unit tests but had NO
    // handler-level integration proof that a Deprecating pin flows through
    // H0PreflightHandler as Failure(Resumable, "quota-openai-pin-stale", ...).
    // These two tests close that gap:
    //   * H03-A — stub-probe test proves the H0 rejection-code mapping
    //     verbatim (parity with the T4–T7 [Theory] but for the fifth probe).
    //     The stub returns Passed=false; H0 must map CheckName
    //     "OpenAiPinFreshness" → RejectionCode "quota-openai-pin-stale".
    //   * H03-B — real-probe + fake-ARM-transport end-to-end test proves
    //     ArmOpenAiPinFreshnessProbe genuinely calls the Azure Resource
    //     Manager `Microsoft.CognitiveServices/.../models` endpoint (not a
    //     hard-coded Passed), correctly parses lifecycleStatus="Deprecating"
    //     on a real ADR-020-pinned model, and that H0 surfaces the per-pin
    //     diagnostic (model name + pinned version) verbatim through the
    //     ErrorDetail + preflight-quota-openai-pin-stale gate-state evidence.
    //     Parity with ArmCognitiveServicesTpmProbeTests.CheckAsync_CallsRealArmUsageEndpoint;
    //     reuses the shared ArmSdkTestFakes (CLAUDE.md §11 — extend, don't
    //     duplicate).

    [Fact]
    public async Task OpenAiPinFreshnessProbeFailure_H0MapsToQuotaOpenAiPinStaleRejectionCode()
    {
        // Arrange — five probes, only the pin-freshness one fails. Fake stub
        // returns the exact per-pin diagnostic shape the real probe emits so
        // the assertion on ErrorDetail is meaningful.
        var run = BuildRunWithTenant();
        var repo = new FakeRepository(run, etag: "etag-h03a");
        var enqueuer = new FakeEnqueuer();
        var probes = new IPreflightQuotaProbe[]
        {
            FakeProbe.Pass(PreflightCheckNames.AzureOpenAiTpmHeadroom),
            FakeProbe.Pass(PreflightCheckNames.DataverseEnvCreationRate),
            FakeProbe.Pass(PreflightCheckNames.SubscriptionVCpuQuota),
            FakeProbe.Pass(PreflightCheckNames.SpeCertBootstrap),
            FakeProbe.Fail(
                PreflightCheckNames.OpenAiPinFreshness,
                "ADR-020 pinned OpenAI model freshness check FAILED for 1 of 3 pinned deployments in region 'westus3'. " +
                "gpt-4o@2024-08-06: lifecycleStatus='Deprecating' inferenceDeprecationOn=2026-11-30T00:00:00Z. " +
                "ADR-020 pin bump required BEFORE next provisioning run (H2a would otherwise fail with ServiceModelDeprecated)."),
        };
        var handler = CreateHandler(repo, enqueuer, probes);

        // Act
        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        // Assert — Failure(Resumable, quota-openai-pin-stale) with the probe
        // diagnostic surfaced verbatim + Cosmos state marked Failed with the
        // exact gate-state key.
        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be("quota-openai-pin-stale");
        failure.Diagnostic.Should().Contain("gpt-4o@2024-08-06")
            .And.Contain("Deprecating")
            .And.Contain("ADR-020 pin bump");

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        repo.LastWrittenRun.ErrorDetail.Should().Contain("quota-openai-pin-stale");
        repo.LastWrittenRun.GateStates.Should().ContainKey("preflight-quota-openai-pin-stale");
        enqueuer.Sent.Should().BeEmpty("H0 blocks the run on any preflight probe failure — no H0.5 dispatch");
    }

    [Fact]
    public async Task OpenAiPinFreshness_EndToEnd_RealProbeAgainstDeprecatingArmResponse_H0FailsWithQuotaOpenAiPinStale()
    {
        // Arrange — a run with region + subscriptionId + tenantId so the real
        // probe can dispatch its ARM SDK call.
        const string region = "westus3";
        const string subscriptionId = "22222222-3333-4444-5555-666666666666";

        var run = BuildRunWithTenant();
        run.Parameters.NonSecret["region"] = region;
        run.Parameters.NonSecret["subscriptionId"] = subscriptionId;
        var repo = new FakeRepository(run, etag: "etag-h03b");
        var enqueuer = new FakeEnqueuer();

        // Fake ARM transport returns a `Microsoft.CognitiveServices/models`
        // page where the gpt-4o@2024-08-06 pin is scheduled to deprecate and
        // its lifecycleStatus is "Deprecating". Inference-deprecation date is
        // set far in the future (500 days) so the "window-expired" branch
        // does NOT also fire — the failure is unambiguously the deprecating
        // status. Other two pins are healthy so exactly ONE pin fails.
        var pathHits = new List<string>();
        var armHandler = ArmSdkTestFakes.NewHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            pathHits.Add(path);
            path.Should().Contain("Microsoft.CognitiveServices",
                "the probe MUST call the real Azure.ResourceManager.CognitiveServices endpoint " +
                "(SubscriptionResource.GetModelsAsync), not a hard-coded stub");
            path.Should().Contain("/models",
                "the probe MUST call the models-list endpoint per Azure.ResourceManager.CognitiveServices " +
                "1.5.2 XML docs: `/subscriptions/{sub}/providers/Microsoft.CognitiveServices/locations/{location}/models`");

            var farFutureIso = DateTimeOffset.UtcNow.AddDays(500).ToString("O");
            return ArmSdkTestFakes.JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "value": [
                    {
                      "kind": "OpenAI",
                      "skuName": "Standard",
                      "model": {
                        "publisher": "OpenAI",
                        "format": "OpenAI",
                        "name": "gpt-4o",
                        "version": "2024-08-06",
                        "skus": [],
                        "deprecation": { "inference": "{{farFutureIso}}" },
                        "lifecycleStatus": "Deprecating"
                      }
                    },
                    {
                      "kind": "OpenAI",
                      "skuName": "Standard",
                      "model": {
                        "publisher": "OpenAI",
                        "format": "OpenAI",
                        "name": "gpt-4o-mini",
                        "version": "2024-07-18",
                        "skus": [],
                        "lifecycleStatus": "GenerallyAvailable"
                      }
                    },
                    {
                      "kind": "OpenAI",
                      "skuName": "Standard",
                      "model": {
                        "publisher": "OpenAI",
                        "format": "OpenAI",
                        "name": "text-embedding-3-large",
                        "version": "1",
                        "skus": [],
                        "lifecycleStatus": "GenerallyAvailable"
                      }
                    }
                  ]
                }
                """);
        });

        var realPinnedProbe = new ArmOpenAiPinFreshnessProbe(
            ArmSdkTestFakes.NewArmClient(armHandler),
            PinnedModelCatalog.Models,
            TimeProvider.System,
            NullLogger<ArmOpenAiPinFreshnessProbe>.Instance);

        // Compose alongside the four other probes as no-ops so the H0
        // orchestration decision is driven by the pin-freshness verdict
        // alone.
        var probes = new IPreflightQuotaProbe[]
        {
            FakeProbe.Pass(PreflightCheckNames.AzureOpenAiTpmHeadroom),
            FakeProbe.Pass(PreflightCheckNames.DataverseEnvCreationRate),
            FakeProbe.Pass(PreflightCheckNames.SubscriptionVCpuQuota),
            FakeProbe.Pass(PreflightCheckNames.SpeCertBootstrap),
            realPinnedProbe,
        };
        var handler = CreateHandler(repo, enqueuer, probes);

        // Act
        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        // Assert — the probe truly reached the ARM SDK, and the H0 handler
        // translated the probe verdict into the exact rejection code +
        // diagnostic + gate-state the punchlist mandates.
        pathHits.Should().NotBeEmpty(
            "the real ArmOpenAiPinFreshnessProbe must dispatch at least one HTTP call to Azure " +
            "Resource Manager — proving CheckAsync is NOT stubbed");

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be("quota-openai-pin-stale");
        // Diagnostic MUST carry the specific ADR-020 pinned model name +
        // pinned version that failed so the operator's remediation is
        // deterministic (F1 lesson: "F1 pin freshness probe" must tell the
        // operator WHICH pin to bump).
        failure.Diagnostic.Should().Contain("gpt-4o@2024-08-06")
            .And.Contain("Deprecating");

        // Cosmos state — Failed + gate-state keyed by rejection code, with
        // per-pin evidence payload the operator can inspect via the L2 GET
        // /api/runs/{id} without pulling logs.
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        repo.LastWrittenRun.ErrorDetail.Should().Contain("quota-openai-pin-stale");
        repo.LastWrittenRun.GateStates.Should().ContainKey("preflight-quota-openai-pin-stale");

        var evidence = repo.LastWrittenRun.GateStates["preflight-quota-openai-pin-stale"].Evidence;
        evidence.Should().NotBeNull("the H0 handler MUST persist the probe's headroom payload as gate-state evidence");
        var evidenceJson = evidence!.Value.GetRawText();
        evidenceJson.Should().Contain("gpt-4o@2024-08-06",
            "the per-pin breakdown must identify the specific failing pin by name + version");
        evidenceJson.Should().Contain("deprecating-status",
            "the machine-stable reason code must accompany the human-readable diagnostic");

        enqueuer.Sent.Should().BeEmpty("H0 blocks the run on any preflight probe failure — no H0.5 dispatch");
    }

    // ---------- helpers ----------

    /// <summary>
    /// Constructs the handler under test. When <paramref name="matrix"/> is
    /// omitted, a throwing stub is injected — non-upgrade tests therefore
    /// implicitly assert that first-install runs NEVER query the matrix.
    /// </summary>
    private static H0PreflightHandler CreateHandler(
        FakeRepository repo,
        FakeEnqueuer enqueuer,
        IPreflightQuotaProbe[] probes,
        IVersionCompatMatrix? matrix = null)
        => new(
            repo,
            enqueuer,
            probes,
            matrix ?? new FakeMatrix(
                VersionCompatVerdict.Green,
                throws: new InvalidOperationException(
                    "IVersionCompatMatrix must not be queried outside upgrade mode")),
            NullLogger<H0PreflightHandler>.Instance);

    private static IPreflightQuotaProbe[] AllPassProbes() => new IPreflightQuotaProbe[]
    {
        FakeProbe.Pass(PreflightCheckNames.AzureOpenAiTpmHeadroom),
        FakeProbe.Pass(PreflightCheckNames.DataverseEnvCreationRate),
        FakeProbe.Pass(PreflightCheckNames.SubscriptionVCpuQuota),
        FakeProbe.Pass(PreflightCheckNames.SpeCertBootstrap),
    };

    private static ProvisioningRun BuildUpgradeRun(bool includeVersions = true)
    {
        var run = BuildRunWithTenant();
        run.Parameters.NonSecret[H0PreflightHandler.ProvisionedOnParameterKey] = "2026-07-01T00:00:00Z";
        if (includeVersions)
        {
            run.Parameters.NonSecret[H0PreflightHandler.CurrentBffVersionParameterKey] = "1.0.0-net10";
            run.Parameters.NonSecret[H0PreflightHandler.CurrentSolutionVersionParameterKey] = "S2026.08";
            run.Parameters.NonSecret[H0PreflightHandler.TargetBffVersionParameterKey] = "1.1.0-net10";
            run.Parameters.NonSecret[H0PreflightHandler.TargetSolutionVersionParameterKey] = "S2026.09";
        }
        return run;
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H0PreflightHandler.HandlerIdentifier,
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
            Status = RunStatus.NotStarted,
            Profile = "spaarke-hosted-model2",
        };
        run.Parameters.NonSecret["region"] = "eastus";
        run.Parameters.NonSecret["subscriptionId"] = "sub-1";
        return run;
    }

    private static ProvisioningRun BuildRunWithTenant()
    {
        var run = BuildRun();
        run.Parameters.NonSecret[H0PreflightHandler.TenantIdParameterKey] = TenantId;
        return run;
    }

    /// <summary>
    /// In-memory <see cref="IProvisioningRunRepository"/> fake. Records the
    /// last written run so tests can assert on the post-handler state.
    /// </summary>
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
        {
            if (_run is null || _etag is null)
            {
                return Task.FromResult<ProvisioningRunReadResult?>(null);
            }
            return Task.FromResult<ProvisioningRunReadResult?>(new ProvisioningRunReadResult(_run, _etag));
        }

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException("H0 does not create runs.");

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }

    /// <summary>
    /// Verdict-forcing <see cref="IVersionCompatMatrix"/> stub. Records the
    /// pairs it was queried with; optionally throws to simulate a matrix
    /// source fault.
    /// </summary>
    private sealed class FakeMatrix : IVersionCompatMatrix
    {
        private readonly VersionCompatVerdict _verdict;
        private readonly IReadOnlyList<string> _ucbClasses;
        private readonly Exception? _throws;

        public int CallCount { get; private set; }
        public VersionPair? LastCurrent { get; private set; }
        public VersionPair? LastTarget { get; private set; }

        public FakeMatrix(
            VersionCompatVerdict verdict,
            IReadOnlyList<string>? ucbClasses = null,
            Exception? throws = null)
        {
            _verdict = verdict;
            _ucbClasses = ucbClasses ?? Array.Empty<string>();
            _throws = throws;
        }

        public Task<VersionCompatCheckResult> CheckPairAsync(
            VersionPair current, VersionPair target, CancellationToken cancellationToken)
        {
            CallCount++;
            LastCurrent = current;
            LastTarget = target;
            if (_throws is not null)
            {
                throw _throws;
            }
            return Task.FromResult(new VersionCompatCheckResult(
                _verdict, $"stub-{_verdict}", _ucbClasses));
        }
    }

    /// <summary>In-memory <see cref="IHandlerEnqueuer"/> fake that records sent envelopes.</summary>
    private sealed class FakeEnqueuer : IHandlerEnqueuer
    {
        public List<HandlerEnvelope> Sent { get; } = new();

        public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stub probe. Tests construct Pass or Fail instances directly + assert
    /// on <see cref="CallCount"/> to verify orchestration behavior (e.g.
    /// no-probe-fires-before-guard).
    /// </summary>
    private sealed class FakeProbe : IPreflightQuotaProbe
    {
        private readonly bool _pass;
        private readonly string _diagnostic;
        public int CallCount { get; private set; }
        public string CheckName { get; }

        private FakeProbe(string checkName, bool pass, string diagnostic)
        {
            CheckName = checkName;
            _pass = pass;
            _diagnostic = diagnostic;
        }

        public static FakeProbe Pass(string checkName) => new(checkName, pass: true, diagnostic: "Pass");
        public static FakeProbe Fail(string checkName, string diagnostic) => new(checkName, pass: false, diagnostic);

        public Task<PreflightCheckResult> CheckAsync(PreflightProbeInput input, CancellationToken cancellationToken)
        {
            CallCount++;
            using var doc = JsonDocument.Parse("""{"observed":"stub","required":"stub","region":"stub"}""");
            return Task.FromResult(new PreflightCheckResult(
                CheckName: CheckName,
                Passed: _pass,
                Headroom: doc.RootElement.Clone(),
                Diagnostic: _diagnostic));
        }
    }
}
