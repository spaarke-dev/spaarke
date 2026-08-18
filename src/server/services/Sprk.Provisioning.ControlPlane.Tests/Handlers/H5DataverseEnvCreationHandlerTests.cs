// -----------------------------------------------------------------------------
// H5DataverseEnvCreationHandlerTests.cs
//
// Unit tests over H5DataverseEnvCreationHandler (task 048 — wave C4).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live pac CLI / Dataverse / HTTP / Azure.
//   Fakes replace the repository + creator + health probe seams so the
//   handler orchestration logic is exercised in isolation. Live-Dataverse
//   coverage belongs in env-guarded smoke tests (H5 is not exercised end-
//   to-end at CI time by design — a real env creation is 15–30 min per
//   design.md § 4.2).
//
// COVERAGE:
//   T1  Happy path — creator success + probe Reachable → Success + Cosmos
//       state advances + interStepState.DataverseEnvUrl populated + gate
//       Verified with url/region/tier evidence.
//   T2  Idempotent no-op — CompletedPhases already contains H5 with
//       matching key → Success (no creator call, no probe call, no state
//       mutation). Acceptance criterion #4.
//   T3  Missing tenantId (§4D I1) → Failure(Resumable, missing-tenant-id) +
//       NO creator call fired. Acceptance criterion #5.
//   T4  Missing region → Failure(Resumable, missing-region).
//   T5  Missing tier → Failure(Resumable, missing-tier).
//   T6  Creator returns Failure(RateLimited) → Failure(Resumable, rate-limited).
//   T7  Creator returns Failure(QuotaExhausted) → Failure(Resumable, quota-exhausted).
//   T8  Creator returns Failure(AuthFailure) → Failure(Resumable, pac-auth-failure).
//   T9  Creator returns Failure(PartialProvisioning) → Failure(QuarantineRequired,
//       partial-provisioning) + Cosmos marked Quarantined + Quarantine metadata.
//   T10 Creator returns Failure(Timeout) → Failure(Resumable, env-creation-timeout).
//   T11 Creator returns Success but env URL blank → Failure(QuarantineRequired,
//       partial-provisioning).
//   T12 Health probe returns Unreachable → Failure(Resumable, env-health-check-failed).
//   T13 Health probe returns InProgress until total-timeout → Failure(Resumable,
//       env-health-check-failed) with distinct diagnostic citing the timeout window.
//   T14 Health probe polling — InProgress on attempt 1, Reachable on attempt 2 →
//       Success (validates gate-then-resume semantics: the polling loop
//       correctly continues past InProgress signals).
//   T15 Creator infrastructure exception → Failure(Resumable, pac-invocation-failed).
//   T16 HandlerId mismatch → throws InvalidOperationException.
//   T17 Idempotency key format determinism — same customerId produces same key.
//   T18 Run not found → Failure(Resumable, run-not-found).
//   T19 MapCreatorFailure round-trip — every failure kind maps to (rejection, class).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H5DataverseEnvCreationHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h5-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string Region = "unitedstates";
    private const string Tier = "Sandbox";
    private const string ExpectedEnvUrl = "https://acme.crm.dynamics.com/";

    // ---------- T1 happy path ----------

    [Fact]
    public async Task HappyPath_CreatorSuccessAndProbeReachable_AdvancesStateAndWritesEnvUrl()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var creator = FakeDataverseEnvCreator.Success(ExpectedEnvUrl);
        var probe = new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable());
        var handler = BuildHandler(repo, creator, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H5DataverseEnvCreationHandler.BuildIdempotencyKey(CustomerId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H5");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle()
            .Which.Phase.Should().Be("H5");
        repo.LastWrittenRun.InterStepState.DataverseEnvUrl.Should().Be(ExpectedEnvUrl);

        var gate = repo.LastWrittenRun.GateStates[H5DataverseEnvCreationHandler.EnvProvisionedGateId];
        gate.Status.Should().Be(GateState.Verified);
        gate.VerifierHandler.Should().Be("H5");
        gate.Evidence.Should().NotBeNull();
        gate.Evidence!.Value.GetProperty("environmentUrl").GetString().Should().Be(ExpectedEnvUrl);
        gate.Evidence.Value.GetProperty("region").GetString().Should().Be(Region);
        gate.Evidence.Value.GetProperty("tier").GetString().Should().Be(Tier);

        creator.CallCount.Should().Be(1);
        probe.CallCount.Should().Be(1);
        creator.LastRequest!.TenantId.Should().Be(TenantId);
        creator.LastRequest.Region.Should().Be(Region);
        creator.LastRequest.Tier.Should().Be(Tier);
    }

    // ---------- T2 idempotency ----------

    [Fact]
    public async Task Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var expectedKey = H5DataverseEnvCreationHandler.BuildIdempotencyKey(CustomerId);
        // Pre-populate: env was already created (POML acceptance #4).
        run.InterStepState.DataverseEnvUrl = ExpectedEnvUrl;
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H5",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-25),
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-2");
        var creator = FakeDataverseEnvCreator.Success("would-not-be-called");
        var probe = new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable());
        var handler = BuildHandler(repo, creator, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        creator.CallCount.Should().Be(0, "no re-creation");
        probe.CallCount.Should().Be(0, "no re-probe");
        // The pre-populated env URL is unchanged (proves we did NOT re-create).
        run.InterStepState.DataverseEnvUrl.Should().Be(ExpectedEnvUrl);
    }

    // ---------- T3 missing tenantId (§4D I1) ----------

    [Fact]
    public async Task MissingTenantId_FailsResumable_NoCreatorCall()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-3");
        var creator = FakeDataverseEnvCreator.Success(ExpectedEnvUrl);
        var probe = new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable());
        var handler = BuildHandler(repo, creator, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.MissingTenantId);
        failure.Diagnostic.Should().Contain("§4D I1");
        creator.CallCount.Should().Be(0, "no pac admin call fired — acceptance #5");
        probe.CallCount.Should().Be(0);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- T4 missing region ----------

    [Fact]
    public async Task MissingRegion_FailsResumable()
    {
        var run = BuildRun(includeRegion: false);
        var repo = new FakeRepository(run, etag: "etag-4");
        var creator = FakeDataverseEnvCreator.Success(ExpectedEnvUrl);
        var handler = BuildHandler(repo, creator,
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.MissingRegion);
        creator.CallCount.Should().Be(0);
    }

    // ---------- T5 missing tier ----------

    [Fact]
    public async Task MissingTier_FailsResumable()
    {
        var run = BuildRun(includeTier: false);
        var repo = new FakeRepository(run, etag: "etag-5");
        var creator = FakeDataverseEnvCreator.Success(ExpectedEnvUrl);
        var handler = BuildHandler(repo, creator,
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.MissingTier);
        creator.CallCount.Should().Be(0);
    }

    // ---------- T6 creator RateLimited ----------

    [Fact]
    public async Task CreatorRateLimited_FailsResumable_WithRateLimitedCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-6");
        var creator = FakeDataverseEnvCreator.Failure(
            DataverseEnvCreationFailureKind.RateLimited,
            "pac exit 3: rate limited — try again in 1 hour");
        var handler = BuildHandler(repo, creator,
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.RateLimited);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- T7 creator QuotaExhausted ----------

    [Fact]
    public async Task CreatorQuotaExhausted_FailsResumable_WithQuotaExhaustedCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-7");
        var creator = FakeDataverseEnvCreator.Failure(
            DataverseEnvCreationFailureKind.QuotaExhausted,
            "tenant capacity exhausted");
        var handler = BuildHandler(repo, creator,
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.QuotaExhausted);
    }

    // ---------- T8 creator AuthFailure ----------

    [Fact]
    public async Task CreatorAuthFailure_FailsResumable_WithPacAuthFailureCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-8");
        var creator = FakeDataverseEnvCreator.Failure(
            DataverseEnvCreationFailureKind.AuthFailure,
            "not authorized to create environments");
        var handler = BuildHandler(repo, creator,
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.PacAuthFailure);
    }

    // ---------- T9 creator PartialProvisioning ----------

    [Fact]
    public async Task CreatorPartialProvisioning_FailsQuarantineRequired_MarksRunQuarantined()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-9");
        var creator = FakeDataverseEnvCreator.Failure(
            DataverseEnvCreationFailureKind.PartialProvisioning,
            "env already exists in inconsistent state");
        var handler = BuildHandler(repo, creator,
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.PartialProvisioning);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
        repo.LastWrittenRun.Quarantine!.QuarantinedByHandler.Should().Be("H5");
    }

    // ---------- T10 creator Timeout ----------

    [Fact]
    public async Task CreatorTimeout_FailsResumable_WithDistinctEnvCreationTimeoutCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-10");
        var creator = FakeDataverseEnvCreator.Failure(
            DataverseEnvCreationFailureKind.Timeout,
            "pac admin create-environment timed out after 45 minutes");
        var handler = BuildHandler(repo, creator,
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.EnvCreationTimeout,
            "distinct code — operator dashboards branch on timeout vs failure");
    }

    // ---------- T11 creator Success but URL blank ----------

    [Fact]
    public async Task CreatorSuccessWithBlankUrl_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-11");
        var creator = FakeDataverseEnvCreator.Success(environmentUrl: "   ");
        var handler = BuildHandler(repo, creator,
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.PartialProvisioning);
    }

    // ---------- T12 probe Unreachable ----------

    [Fact]
    public async Task HealthProbeUnreachable_FailsResumable_WithEnvHealthCheckFailedCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12");
        var creator = FakeDataverseEnvCreator.Success(ExpectedEnvUrl);
        var probe = new FakeDataverseHealthProbe(
            new DataverseHealthProbeResult.Unreachable("HTTP 401 Unauthorized"));
        var handler = BuildHandler(repo, creator, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.EnvHealthCheckFailed);
        failure.Diagnostic.Should().Contain("HTTP 401 Unauthorized");
        failure.Diagnostic.Should().Contain(ExpectedEnvUrl);
        creator.CallCount.Should().Be(1);
        probe.CallCount.Should().Be(1);
    }

    // ---------- T13 probe all-InProgress until total-timeout ----------

    [Fact]
    public async Task HealthProbeAllInProgress_ExitsOnTotalTimeout_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13");
        var creator = FakeDataverseEnvCreator.Success(ExpectedEnvUrl);
        var probe = new FakeDataverseHealthProbe(
            new DataverseHealthProbeResult.InProgress("env still replicating (503)"));

        // Tiny total-timeout with tiny interval — makes the poll loop iterate
        // a handful of times before hitting the deadline, without slowing the
        // suite (~200ms total wall time).
        var handler = BuildHandler(repo, creator, probe,
            healthProbeInterval: TimeSpan.FromMilliseconds(10),
            healthProbeTotalTimeout: TimeSpan.FromMilliseconds(150));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.EnvHealthCheckFailed);
        failure.Diagnostic.Should().Contain("exceeded", "the diagnostic cites the polling timeout window");
        probe.CallCount.Should().BeGreaterThanOrEqualTo(2, "at least two probes before timeout");
    }

    // ---------- T14 probe InProgress then Reachable (gate-then-resume) ----------

    [Fact]
    public async Task HealthProbeInProgressThenReachable_LoopContinues_AndSucceeds()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-14");
        var creator = FakeDataverseEnvCreator.Success(ExpectedEnvUrl);
        var probe = new SequencedFakeDataverseHealthProbe(
            new DataverseHealthProbeResult.InProgress("env still replicating"),
            new DataverseHealthProbeResult.InProgress("env still replicating"),
            new DataverseHealthProbeResult.Reachable());

        var handler = BuildHandler(repo, creator, probe,
            healthProbeInterval: TimeSpan.FromMilliseconds(1),
            healthProbeTotalTimeout: TimeSpan.FromSeconds(5));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        probe.CallCount.Should().Be(3, "loop iterated past two InProgress signals to the terminal Reachable");
        repo.LastWrittenRun!.InterStepState.DataverseEnvUrl.Should().Be(ExpectedEnvUrl);
    }

    // ---------- T15 creator infrastructure exception ----------

    [Fact]
    public async Task CreatorInfrastructureException_FailsResumable_WithPacInvocationFailedCode()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-15");
        var creator = new ThrowingDataverseEnvCreator(
            new InvalidOperationException("pac binary not found on PATH"));
        var handler = BuildHandler(repo, creator,
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.PacInvocationFailed);
        failure.Diagnostic.Should().Contain("InvalidOperationException");
        failure.Diagnostic.Should().Contain("pac binary not found");
    }

    // ---------- T16 handler-id mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-16");
        var handler = BuildHandler(repo,
            FakeDataverseEnvCreator.Success(ExpectedEnvUrl),
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H0",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- T17 idempotency key format ----------

    [Fact]
    public void IdempotencyKey_IsDeterministicByCustomerId_NoVersionToken()
    {
        var k1 = H5DataverseEnvCreationHandler.BuildIdempotencyKey("acme");
        var k2 = H5DataverseEnvCreationHandler.BuildIdempotencyKey("acme");
        k1.Should().Be(k2);
        k1.Should().Be("dvenv-acme");
    }

    // ---------- T18 run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo,
            FakeDataverseEnvCreator.Success(ExpectedEnvUrl),
            new FakeDataverseHealthProbe(new DataverseHealthProbeResult.Reachable()));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(DataverseEnvCreationRejectionCodes.RunNotFound);
    }

    // ---------- T19 failure-kind mapping table ----------

    [Theory]
    [InlineData(DataverseEnvCreationFailureKind.AuthFailure, DataverseEnvCreationRejectionCodes.PacAuthFailure, FailureClass.Resumable)]
    [InlineData(DataverseEnvCreationFailureKind.RateLimited, DataverseEnvCreationRejectionCodes.RateLimited, FailureClass.Resumable)]
    [InlineData(DataverseEnvCreationFailureKind.QuotaExhausted, DataverseEnvCreationRejectionCodes.QuotaExhausted, FailureClass.Resumable)]
    [InlineData(DataverseEnvCreationFailureKind.PartialProvisioning, DataverseEnvCreationRejectionCodes.PartialProvisioning, FailureClass.QuarantineRequired)]
    [InlineData(DataverseEnvCreationFailureKind.Timeout, DataverseEnvCreationRejectionCodes.EnvCreationTimeout, FailureClass.Resumable)]
    [InlineData(DataverseEnvCreationFailureKind.UnknownInvocationFailure, DataverseEnvCreationRejectionCodes.PacInvocationFailed, FailureClass.Resumable)]
    public void MapCreatorFailure_ProducesExpectedRejectionAndClass(
        DataverseEnvCreationFailureKind kind,
        string expectedRejection,
        FailureClass expectedClass)
    {
        var (rejection, cls) = H5DataverseEnvCreationHandler.MapCreatorFailure(kind);
        rejection.Should().Be(expectedRejection);
        cls.Should().Be(expectedClass);
    }

    // ---------- T20 pac-CLI stderr classifier (probe helper) ----------

    [Theory]
    [InlineData("rate limit exceeded", 1, DataverseEnvCreationFailureKind.RateLimited)]
    [InlineData("HTTP 429 too many requests", 1, DataverseEnvCreationFailureKind.RateLimited)]
    [InlineData("tenant quota exhausted", 1, DataverseEnvCreationFailureKind.QuotaExhausted)]
    [InlineData("no more environments allowed", 1, DataverseEnvCreationFailureKind.QuotaExhausted)]
    [InlineData("access denied to Power Platform", 1, DataverseEnvCreationFailureKind.AuthFailure)]
    [InlineData("not authorized to perform this action", 1, DataverseEnvCreationFailureKind.AuthFailure)]
    [InlineData("environment already exists", 1, DataverseEnvCreationFailureKind.PartialProvisioning)]
    [InlineData("Something went wrong on the server", 1, DataverseEnvCreationFailureKind.UnknownInvocationFailure)]
    [InlineData("timed out waiting", 124, DataverseEnvCreationFailureKind.Timeout)]
    public void PacAdminDataverseEnvCreator_ClassifyStderr_MapsExpectedFailureKind(
        string stderr,
        int exitCode,
        DataverseEnvCreationFailureKind expected)
    {
        var kind = PacAdminDataverseEnvCreator.ClassifyStderr(stderr, exitCode);
        kind.Should().Be(expected);
    }

    // ---------- helpers ----------

    private static H5DataverseEnvCreationHandler BuildHandler(
        FakeRepository repo,
        IDataverseEnvCreator creator,
        IDataverseHealthProbe probe,
        TimeSpan? healthProbeInterval = null,
        TimeSpan? healthProbeTotalTimeout = null)
    {
        var options = Options.Create(new DataverseEnvCreationOptions
        {
            HealthProbeInterval = healthProbeInterval ?? TimeSpan.FromMilliseconds(1),
            HealthProbeTotalTimeout = healthProbeTotalTimeout ?? TimeSpan.FromSeconds(5),
        });
        return new H5DataverseEnvCreationHandler(
            repo, creator, probe, options,
            TimeProvider.System,
            NullLogger<H5DataverseEnvCreationHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H5DataverseEnvCreationHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(
        bool includeTenantId = true,
        bool includeRegion = true,
        bool includeTier = true)
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
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H5DataverseEnvCreationHandler.TenantIdParameterKey] = TenantId;
        }
        if (includeRegion)
        {
            run.Parameters.NonSecret[H5DataverseEnvCreationHandler.RegionParameterKey] = Region;
        }
        if (includeTier)
        {
            run.Parameters.NonSecret[H5DataverseEnvCreationHandler.TierParameterKey] = Tier;
        }
        return run;
    }

    /// <summary>Repository fake — records last written run + last write etag.</summary>
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

    /// <summary>Env-creator fake — returns a fixed outcome + records the last request.</summary>
    private sealed class FakeDataverseEnvCreator : IDataverseEnvCreator
    {
        private readonly DataverseEnvCreationOutcome _outcome;
        public int CallCount { get; private set; }
        public DataverseEnvCreationRequest? LastRequest { get; private set; }

        private FakeDataverseEnvCreator(DataverseEnvCreationOutcome outcome) => _outcome = outcome;

        public static FakeDataverseEnvCreator Success(string environmentUrl)
            => new(new DataverseEnvCreationOutcome.Success(environmentUrl));

        public static FakeDataverseEnvCreator Failure(
            DataverseEnvCreationFailureKind kind, string diagnostic)
            => new(new DataverseEnvCreationOutcome.Failure(kind, diagnostic));

        public Task<DataverseEnvCreationOutcome> CreateEnvironmentAsync(
            DataverseEnvCreationRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }

    /// <summary>Health-probe fake — returns the same result every call.</summary>
    private sealed class FakeDataverseHealthProbe : IDataverseHealthProbe
    {
        private readonly DataverseHealthProbeResult _result;
        public int CallCount { get; private set; }

        public FakeDataverseHealthProbe(DataverseHealthProbeResult result) => _result = result;

        public Task<DataverseHealthProbeResult> CheckHealthAsync(
            string environmentUrl, string tenantId, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// Sequenced health-probe fake — returns each result in sequence. Last
    /// result repeats when the sequence exhausts (defensive). Used to test
    /// the polling loop's InProgress → Reachable transition.
    /// </summary>
    private sealed class SequencedFakeDataverseHealthProbe : IDataverseHealthProbe
    {
        private readonly DataverseHealthProbeResult[] _sequence;
        public int CallCount { get; private set; }

        public SequencedFakeDataverseHealthProbe(params DataverseHealthProbeResult[] sequence)
        {
            _sequence = sequence.Length == 0
                ? throw new ArgumentException("sequence must be non-empty", nameof(sequence))
                : sequence;
        }

        public Task<DataverseHealthProbeResult> CheckHealthAsync(
            string environmentUrl, string tenantId, CancellationToken ct)
        {
            var idx = Math.Min(CallCount, _sequence.Length - 1);
            CallCount++;
            return Task.FromResult(_sequence[idx]);
        }
    }

    /// <summary>Creator that always throws — models an infrastructure fault (missing pac binary, etc.).</summary>
    private sealed class ThrowingDataverseEnvCreator : IDataverseEnvCreator
    {
        private readonly Exception _exception;
        public ThrowingDataverseEnvCreator(Exception ex) => _exception = ex;

        public Task<DataverseEnvCreationOutcome> CreateEnvironmentAsync(
            DataverseEnvCreationRequest request, CancellationToken ct)
            => throw _exception;
    }
}
