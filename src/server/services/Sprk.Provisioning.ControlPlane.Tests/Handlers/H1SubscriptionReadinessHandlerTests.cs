// -----------------------------------------------------------------------------
// H1SubscriptionReadinessHandlerTests.cs
//
// L2 CONTROL-PLANE H1 subscription-readiness handler unit tests (task 043).
//
// SCOPE:
//   Pure unit tests with in-memory fakes — no live Cosmos, no live Service
//   Bus, no live ARM SDK, no `az` shell-out. ADR-038 path #1 (pure C# unit
//   test — no external processes).
//
// COVERAGE (POML acceptance criteria mapping):
//   AC-1  SpaarkeOwned happy path       — Success + Cosmos advances to H2a
//   AC-2  CustomerOwned happy w/ LH     — Success + H2a enqueued
//   AC-3  CustomerOwned + LH missing    — Failure(Resumable, LighthouseDelegationMissing)
//   AC-4  Subscription unreachable      — Failure(Resumable, SubscriptionUnreachable)
//   AC-5  Idempotency (durable no-op)   — second call short-circuits Success
//   AC-6  Missing tenantId (§ 4D I1)    — Failure(Resumable, MissingTenantId); no ARM call
//   AC-7  DI + build green              — validated by build gate, not tested here
//
// Plus defensive negative branches:
//   - Missing subscriptionId (§ 4D I1)
//   - Invalid tenancyModel
//   - Run not found in Cosmos partition
//   - HandlerId mismatch (defensive dispatch bug detector)
//   - Optimistic-concurrency race on the success-write
//   - Enqueue failure — H1 still returns Success (reconciler re-emits)
//   - Idempotency key determinism (customerId → single key)
//   - Tenancy-model normalization (Model1Shared / SpaarkeOwned / Model2Dedicated / CustomerOwned)
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H1SubscriptionReadinessHandlerTests
{
    private const string CustomerId = "acme-corp";
    private const string RunId = "01j7q3zp-subready-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";

    // ---------- AC-1 SpaarkeOwned happy path ----------

    [Fact]
    public async Task AC1_SpaarkeOwnedHappyPath_ReturnsSuccessAndEnqueuesH2a()
    {
        var run = BuildRun(tenancy: "SpaarkeOwned");
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be($"subready-{CustomerId}");

        // Reachability probe called; Lighthouse NOT called (SpaarkeOwned skips).
        probe.ReachabilityCalls.Should().Be(1);
        probe.LighthouseCalls.Should().Be(0, "SpaarkeOwned tenancy MUST NOT invoke the Lighthouse-delegation branch.");

        // Cosmos state advanced.
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H2a");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle(cp => cp.Phase == "H1");
        repo.LastWrittenRun.GateStates.Should().ContainKey(SubscriptionReadinessGates.SubscriptionReachable);
        repo.LastWrittenRun.GateStates.Should().NotContainKey(SubscriptionReadinessGates.LighthouseDelegation,
            "SpaarkeOwned tenancy MUST NOT record a Lighthouse gate entry.");

        // H2a enqueued.
        enqueuer.Sent.Should().ContainSingle();
        var next = enqueuer.Sent[0];
        next.HandlerId.Should().Be("H2a");
        next.RunId.Should().Be(RunId);
        next.CustomerId.Should().Be(CustomerId);
    }

    // ---------- AC-2 CustomerOwned happy path w/ Lighthouse present ----------

    [Fact]
    public async Task AC2_CustomerOwnedHappyPath_LighthousePresent_ReturnsSuccess()
    {
        var run = BuildRun(tenancy: "CustomerOwned");
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        probe.ReachabilityCalls.Should().Be(1);
        probe.LighthouseCalls.Should().Be(1, "CustomerOwned tenancy MUST invoke the Lighthouse-delegation branch.");

        repo.LastWrittenRun!.GateStates.Should().ContainKey(SubscriptionReadinessGates.SubscriptionReachable);
        repo.LastWrittenRun.GateStates.Should().ContainKey(SubscriptionReadinessGates.LighthouseDelegation,
            "CustomerOwned tenancy MUST record a Lighthouse gate entry on success.");
        enqueuer.Sent.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Model2Dedicated")]  // structured design.md §6.2 name
    [InlineData("customerowned")]    // case-insensitive colloquial
    [InlineData("MODEL2DEDICATED")]  // case-insensitive structured
    public async Task AC2_CustomerOwned_TenancyNameVariants_AllInvokeLighthouseBranch(string tenancyValue)
    {
        var run = BuildRun(tenancy: tenancyValue);
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        probe.LighthouseCalls.Should().Be(1);
    }

    // ---------- AC-3 CustomerOwned + Lighthouse missing ----------

    [Fact]
    public async Task AC3_CustomerOwned_LighthouseMissing_ReturnsFailure_LighthouseDelegationMissing()
    {
        var run = BuildRun(tenancy: "CustomerOwned");
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probe = new FakeProbe
        {
            ReachabilityResult = new SubscriptionReadinessCheckResult(true, "ok", null),
            LighthouseResult = new SubscriptionReadinessCheckResult(false, "Lighthouse offer not accepted", null),
        };
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SubscriptionReadinessRejectionCodes.LighthouseDelegationMissing);

        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H1");
        enqueuer.Sent.Should().BeEmpty("no H2a enqueue on H1 failure.");
    }

    // ---------- AC-4 Subscription unreachable ----------

    [Fact]
    public async Task AC4_SubscriptionUnreachable_ReturnsFailure_SubscriptionUnreachable_NoLighthouseCall()
    {
        var run = BuildRun(tenancy: "CustomerOwned"); // Even in CustomerOwned, reachability fails first.
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probe = new FakeProbe
        {
            ReachabilityResult = new SubscriptionReadinessCheckResult(false, "Subscription not found", null),
            LighthouseResult = new SubscriptionReadinessCheckResult(true, "unused", null),
        };
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SubscriptionReadinessRejectionCodes.SubscriptionUnreachable);

        probe.LighthouseCalls.Should().Be(0,
            "Lighthouse check MUST short-circuit when reachability fails — no wasted ARM call.");
        enqueuer.Sent.Should().BeEmpty();
    }

    // ---------- HANDLER-04 provider-registration (Wave 2 pre-dispatch remediation 2026-08-27) ----------

    [Fact]
    public async Task Handler04_ProviderRegistration_Success_InvokedWithConfiguredProviders_AndProceeds()
    {
        var run = BuildRun(tenancy: "SpaarkeOwned");
        var repo = new FakeRepository(run, etag: "etag-h04-ok");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var options = new SubscriptionReadinessOptions
        {
            RequiredResourceProviders = new List<string> { "Microsoft.KeyVault", "Microsoft.Storage" },
            PollInterval = TimeSpan.FromSeconds(1),
            PollTotalTimeout = TimeSpan.FromSeconds(30),
        };
        var handler = NewHandler(repo, enqueuer, probe, options);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        probe.ReachabilityCalls.Should().Be(1);
        probe.ProviderRegistrationCalls.Should().Be(1, "HANDLER-04 provider-registration step MUST fire after reachability");
        probe.LastRequestedProviders.Should().BeEquivalentTo(new[] { "Microsoft.KeyVault", "Microsoft.Storage" });
    }

    [Fact]
    public async Task Handler04_ProviderRegistration_EmptyList_Skipped()
    {
        var run = BuildRun(tenancy: "SpaarkeOwned");
        var repo = new FakeRepository(run, etag: "etag-h04-skip");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var options = new SubscriptionReadinessOptions
        {
            RequiredResourceProviders = new List<string>(),
        };
        var handler = NewHandler(repo, enqueuer, probe, options);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        probe.ProviderRegistrationCalls.Should().Be(0, "empty required-provider list MUST short-circuit the step");
    }

    [Fact]
    public async Task Handler04_ProviderRegistration_ProbeReportsFailure_FailsResumable_ProviderRegistrationFailed()
    {
        var run = BuildRun(tenancy: "SpaarkeOwned");
        var repo = new FakeRepository(run, etag: "etag-h04-fail");
        var enqueuer = new FakeEnqueuer();
        var probe = new FakeProbe
        {
            ReachabilityResult = new SubscriptionReadinessCheckResult(true, "ok", null),
            ProviderRegistrationResult = new SubscriptionReadinessCheckResult(
                false,
                "Provider registration did NOT reach 'Registered' within 300s for 1 of 2 required providers: Microsoft.Cache=Registering.",
                null),
        };
        var options = new SubscriptionReadinessOptions
        {
            RequiredResourceProviders = new List<string> { "Microsoft.KeyVault", "Microsoft.Cache" },
        };
        var handler = NewHandler(repo, enqueuer, probe, options);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SubscriptionReadinessRejectionCodes.ProviderRegistrationFailed);
        failure.Diagnostic.Should().Contain("Microsoft.Cache=Registering");
        probe.LighthouseCalls.Should().Be(0, "provider-registration failure MUST short-circuit the Lighthouse branch");
        enqueuer.Sent.Should().BeEmpty();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task Handler04_ProviderRegistration_RunsBeforeLighthouse_OnCustomerOwned()
    {
        var run = BuildRun(tenancy: "CustomerOwned");
        var repo = new FakeRepository(run, etag: "etag-h04-order");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var options = new SubscriptionReadinessOptions
        {
            RequiredResourceProviders = new List<string> { "Microsoft.KeyVault" },
        };
        var handler = NewHandler(repo, enqueuer, probe, options);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        probe.ReachabilityCalls.Should().Be(1);
        probe.ProviderRegistrationCalls.Should().Be(1);
        probe.LighthouseCalls.Should().Be(1, "CustomerOwned Lighthouse check MUST still fire after provider-registration success");
    }

    // ---------- AC-5 Idempotency (durable no-op) ----------

    [Fact]
    public async Task AC5_Idempotency_SecondCall_IsDurableNoOp()
    {
        // Seed a completed H1 phase with the deterministic idempotency key.
        var run = BuildRun(tenancy: "SpaarkeOwned");
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H1",
            IdempotencyKey = $"subready-{CustomerId}",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-30),
            CompletedAt = DateTimeOffset.UtcNow.AddSeconds(-29),
            JobId = "prior-job",
        });
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be($"subready-{CustomerId}");

        probe.ReachabilityCalls.Should().Be(0, "durable no-op MUST NOT invoke probes.");
        probe.LighthouseCalls.Should().Be(0);
        repo.WriteCount.Should().Be(0, "durable no-op MUST NOT rewrite Cosmos.");
        enqueuer.Sent.Should().BeEmpty("durable no-op MUST NOT re-enqueue H2a.");
    }

    // ---------- AC-6 Missing tenantId (§ 4D I1) ----------

    [Fact]
    public async Task AC6_MissingTenantId_ReturnsFailure_MissingTenantId_NoProbeFires()
    {
        var run = BuildRun(tenancy: "SpaarkeOwned");
        run.Parameters.NonSecret.Remove("tenantId"); // §4D I1 — no fallback allowed.
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(SubscriptionReadinessRejectionCodes.MissingTenantId);

        probe.ReachabilityCalls.Should().Be(0, "no ARM call may fire against a default-tenant fallback (§4D I1).");
        probe.LighthouseCalls.Should().Be(0);
        enqueuer.Sent.Should().BeEmpty();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // ---------- Defensive: Missing subscriptionId ----------

    [Fact]
    public async Task MissingSubscriptionId_ReturnsFailure_MissingSubscriptionId_NoProbeFires()
    {
        var run = BuildRun(tenancy: "SpaarkeOwned");
        run.Parameters.NonSecret.Remove("subscriptionId");
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SubscriptionReadinessRejectionCodes.MissingSubscriptionId);
        probe.ReachabilityCalls.Should().Be(0);
    }

    // ---------- Defensive: Invalid tenancy model ----------

    [Theory]
    [InlineData("")]
    [InlineData("SomeFutureModel")]
    [InlineData("Model3")]
    public async Task InvalidTenancyModel_ReturnsFailure_InvalidTenancyModel(string tenancyValue)
    {
        var run = BuildRun(tenancy: tenancyValue);
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SubscriptionReadinessRejectionCodes.InvalidTenancyModel);
        probe.ReachabilityCalls.Should().Be(0,
            "unrecognized tenancy MUST short-circuit BEFORE any ARM call.");
    }

    // ---------- Defensive: Run not found ----------

    [Fact]
    public async Task RunNotFound_ReturnsFailure_RunNotFound()
    {
        var repo = new FakeRepository(runOrNull: null);
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SubscriptionReadinessRejectionCodes.RunNotFound);
    }

    // ---------- Defensive: HandlerId mismatch ----------

    [Fact]
    public async Task HandlerIdMismatch_Throws_InvalidOperationException()
    {
        var handler = NewHandler(new FakeRepository(BuildRun(), "etag-1"), new FakeEnqueuer(), FakeProbe.AllPass());
        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H2a", // Wrong — expected H1.
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mismatched HandlerId*");
    }

    // ---------- Defensive: Optimistic-concurrency race on success write ----------

    [Fact]
    public async Task ConcurrentWriteConflict_OnSuccessWrite_ReturnsFailure_ConcurrentWriteConflict()
    {
        var run = BuildRun(tenancy: "SpaarkeOwned");
        var winningRun = BuildRun(tenancy: "SpaarkeOwned");
        winningRun.Status = RunStatus.Cancelled;
        var repo = new FakeRepository(run, etag: "etag-1")
        {
            NextReplaceResult = new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(winningRun, "etag-2")),
        };
        var enqueuer = new FakeEnqueuer();
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(SubscriptionReadinessRejectionCodes.ConcurrentWriteConflict);
        enqueuer.Sent.Should().BeEmpty("no H2a enqueue when the success write LOST the ETag race.");
    }

    // ---------- Defensive: Enqueue failure still returns Success (reconciler re-emits) ----------

    [Fact]
    public async Task EnqueueFailure_StillReturnsSuccess_ReconcilerReEmits()
    {
        var run = BuildRun(tenancy: "SpaarkeOwned");
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer { ThrowOnNext = new InvalidOperationException("SB broker unreachable") };
        var probe = FakeProbe.AllPass();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>(
            "Cosmos state already records H1 complete; enqueue failure is a Wave-C5 reconciler concern.");
        repo.LastWrittenRun!.CurrentPhase.Should().Be("H2a",
            "Cosmos state MUST reflect H1-complete-CurrentPhase-H2a even when enqueue fails.");
    }

    // ---------- Idempotency-key determinism ----------

    [Theory]
    [InlineData("acme", "acme", true)]
    [InlineData("acme", "beta", false)]
    public void BuildIdempotencyKey_ChangesOnlyWhenCustomerIdDiffers(string c1, string c2, bool shouldBeEqual)
    {
        var k1 = H1SubscriptionReadinessHandler.BuildIdempotencyKey(c1);
        var k2 = H1SubscriptionReadinessHandler.BuildIdempotencyKey(c2);

        if (shouldBeEqual) k1.Should().Be(k2);
        else k1.Should().NotBe(k2);

        // POML constraint: subready-{customerId} — NO version token.
        k1.Should().Be($"subready-{c1}");
    }

    // ---------- Tenancy-model classification (internal API) ----------

    [Theory]
    [InlineData("SpaarkeOwned", true, false)]
    [InlineData("Model1Shared", true, false)]
    [InlineData("CustomerOwned", true, true)]
    [InlineData("Model2Dedicated", true, true)]
    [InlineData("customerowned", true, true)]     // case-insensitive
    [InlineData("SPAARKEOWNED", true, false)]     // case-insensitive
    [InlineData("Model3Future", false, false)]    // unknown
    [InlineData("", false, false)]                // empty
    public void ClassifyTenancy_MapsAllKnownVariants(
        string tenancyValue, bool expectedKnown, bool expectedRequiresLighthouse)
    {
        var requiresLighthouse = H1SubscriptionReadinessHandler.ClassifyTenancy(
            tenancyValue, out var known);

        known.Should().Be(expectedKnown);
        requiresLighthouse.Should().Be(expectedRequiresLighthouse);
    }

    [Fact]
    public void ClassifyTenancy_NullInput_ReturnsUnknown()
    {
        var requiresLighthouse = H1SubscriptionReadinessHandler.ClassifyTenancy(null, out var known);

        known.Should().BeFalse();
        requiresLighthouse.Should().BeFalse();
    }

    // ---------- HandlerId matches design constant ----------

    [Fact]
    public void HandlerId_MatchesDesignDocConstant()
    {
        var handler = NewHandler(new FakeRepository(BuildRun(), "etag-1"), new FakeEnqueuer(), FakeProbe.AllPass());
        handler.HandlerId.Should().Be("H1", "value MUST match design.md §4.1 handler-catalog verbatim.");
    }

    // ---------- Task 121: end-to-end with the REAL ArmSubscriptionReadinessProbe ----------
    //
    // These two tests replace FakeProbe with the production
    // ArmSubscriptionReadinessProbe (task 121, Wave G-2), wired against a fake
    // HttpClientTransport (ArmSdkTestFakes, defined in
    // ArmSubscriptionReadinessProbeTests.cs). They prove the FULL H1 flow —
    // not just the probe in isolation — genuinely reaches the reserved
    // rejection codes via a real ARM SDK call, closing the exact gap DS-4 §3
    // flagged: "POML 043 acceptance criteria required real ARM behavior...
    // Criteria were satisfied only via injected test fakes."

    [Fact]
    public async Task RealArmProbe_Reachable_ReturnsSuccess_ViaGenuineArmCall()
    {
        var armHandler = ArmSdkTestFakes.NewHandler(_ =>
            ArmSdkTestFakes.JsonResponse(
                System.Net.HttpStatusCode.OK,
                ArmSdkTestFakes.SubscriptionGetBody(SubscriptionId, TenantId, "Enabled")));
        var probe = new ArmSubscriptionReadinessProbe(
            ArmSdkTestFakes.NewArmClient(armHandler), NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var run = BuildRun(tenancy: "SpaarkeOwned");
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        armHandler.RequestedUris.Should().ContainSingle(
            uri => uri.AbsolutePath == $"/subscriptions/{SubscriptionId}",
            "the real ARM SDK call must have actually fired — not a hard-coded Passed");
    }

    [Fact]
    public async Task RealArmProbe_CustomerOwned_LighthouseMissing_ReturnsLighthouseDelegationMissingRejectionCode()
    {
        var armHandler = ArmSdkTestFakes.NewHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("registrationAssignments"))
            {
                return ArmSdkTestFakes.JsonResponse(
                    System.Net.HttpStatusCode.OK,
                    ArmSdkTestFakes.RegistrationAssignmentsBody(SubscriptionId, count: 0));
            }

            return ArmSdkTestFakes.JsonResponse(
                System.Net.HttpStatusCode.OK,
                ArmSdkTestFakes.SubscriptionGetBody(SubscriptionId, TenantId, "Enabled"));
        });
        var probe = new ArmSubscriptionReadinessProbe(
            ArmSdkTestFakes.NewArmClient(armHandler), NullLogger<ArmSubscriptionReadinessProbe>.Instance);

        var run = BuildRun(tenancy: "CustomerOwned");
        var repo = new FakeRepository(run, etag: "etag-1");
        var enqueuer = new FakeEnqueuer();
        var handler = NewHandler(repo, enqueuer, probe);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>()
            .Which.RejectionCode.Should().Be(SubscriptionReadinessRejectionCodes.LighthouseDelegationMissing);
        armHandler.RequestedUris.Should().Contain(
            uri => uri.AbsolutePath.Contains("registrationAssignments"),
            "the real ARM SDK Lighthouse-list call must have actually fired");
    }

    // -------------------------------------------------------------------------
    // Helpers + fakes
    // -------------------------------------------------------------------------

    private static H1SubscriptionReadinessHandler NewHandler(
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        ISubscriptionReadinessProbe probe,
        SubscriptionReadinessOptions? options = null)
    {
        // HANDLER-04 (Wave 2 pre-dispatch remediation 2026-08-27): default
        // to an EMPTY required-provider list so the existing test suite's
        // FakeProbe.RegisterAndPollRequiredProvidersAsync is never invoked
        // (handler skips the step when the list is empty). Tests that
        // exercise HANDLER-04's provider-registration branch supply a
        // populated list explicitly.
        var boundOptions = options ?? new SubscriptionReadinessOptions
        {
            RequiredResourceProviders = new List<string>(),
        };
        return new H1SubscriptionReadinessHandler(
            repository,
            enqueuer,
            probe,
            Microsoft.Extensions.Options.Options.Create(boundOptions),
            NullLogger<H1SubscriptionReadinessHandler>.Instance);
    }

    private static ProvisioningRun BuildRun(string tenancy = "SpaarkeOwned")
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-abc",
            TenancyModel = tenancy,
            Profile = "spaarke-hosted-model2",
            Status = RunStatus.Running,
            CurrentPhase = "H1",
        };
        run.Parameters.NonSecret["tenantId"] = TenantId;
        run.Parameters.NonSecret["subscriptionId"] = SubscriptionId;
        return run;
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H1SubscriptionReadinessHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    // --- FakeRepository ---
    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private readonly ProvisioningRun? _run;
        private readonly string _etag;

        public FakeRepository(ProvisioningRun run, string etag)
        {
            _run = run;
            _etag = etag;
        }

        public FakeRepository(ProvisioningRun? runOrNull)
        {
            _run = runOrNull;
            _etag = "etag-1";
        }

        public ProvisioningRun? LastWrittenRun { get; private set; }
        public int WriteCount { get; private set; }
        public ReplaceRunResult? NextReplaceResult { get; set; }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(
            string customerId, string runId, CancellationToken cancellationToken)
        {
            if (_run is null) return Task.FromResult<ProvisioningRunReadResult?>(null);
            return Task.FromResult<ProvisioningRunReadResult?>(
                new ProvisioningRunReadResult(_run, _etag));
        }

        public Task<ProvisioningRunReadResult> CreateRunAsync(
            ProvisioningRun run, CancellationToken cancellationToken)
            => throw new NotSupportedException("H1 tests do not exercise CreateRun.");

        public Task<ReplaceRunResult> ReplaceRunAsync(
            ProvisioningRun run, string ifMatchEtag, CancellationToken cancellationToken)
        {
            WriteCount++;
            LastWrittenRun = run;
            if (NextReplaceResult is not null)
            {
                var configured = NextReplaceResult;
                NextReplaceResult = null;
                return Task.FromResult(configured);
            }
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, "etag-next"));
        }
    }

    // --- FakeEnqueuer ---
    private sealed class FakeEnqueuer : IHandlerEnqueuer
    {
        public List<HandlerEnvelope> Sent { get; } = new();
        public Exception? ThrowOnNext { get; set; }

        public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
        {
            if (ThrowOnNext is not null)
            {
                var toThrow = ThrowOnNext;
                ThrowOnNext = null;
                throw toThrow;
            }
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }

    // --- FakeProbe ---
    private sealed class FakeProbe : ISubscriptionReadinessProbe
    {
        public SubscriptionReadinessCheckResult ReachabilityResult { get; set; }
            = new(true, "ok", null);
        public SubscriptionReadinessCheckResult LighthouseResult { get; set; }
            = new(true, "ok", null);
        // HANDLER-04 (Wave 2 pre-dispatch remediation 2026-08-27) — provider
        // registration result. Pass-through default so existing tests
        // exercise the unaffected happy path.
        public SubscriptionReadinessCheckResult ProviderRegistrationResult { get; set; }
            = new(true, "ok", null);

        public int ReachabilityCalls { get; private set; }
        public int LighthouseCalls { get; private set; }
        public int ProviderRegistrationCalls { get; private set; }
        public IReadOnlyList<string>? LastRequestedProviders { get; private set; }

        public static FakeProbe AllPass() => new();

        public Task<SubscriptionReadinessCheckResult> CheckSubscriptionReachableAsync(
            string subscriptionId, string tenantId, CancellationToken cancellationToken)
        {
            ReachabilityCalls++;
            return Task.FromResult(ReachabilityResult);
        }

        public Task<SubscriptionReadinessCheckResult> CheckLighthouseDelegationAsync(
            string subscriptionId, string tenantId, CancellationToken cancellationToken)
        {
            LighthouseCalls++;
            return Task.FromResult(LighthouseResult);
        }

        public Task<SubscriptionReadinessCheckResult> RegisterAndPollRequiredProvidersAsync(
            string subscriptionId,
            string tenantId,
            IReadOnlyList<string> requiredProviders,
            TimeSpan pollInterval,
            TimeSpan totalTimeout,
            CancellationToken cancellationToken)
        {
            ProviderRegistrationCalls++;
            LastRequestedProviders = requiredProviders;
            return Task.FromResult(ProviderRegistrationResult);
        }
    }
}
