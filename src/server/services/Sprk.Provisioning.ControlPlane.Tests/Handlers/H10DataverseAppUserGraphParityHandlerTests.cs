// -----------------------------------------------------------------------------
// H10DataverseAppUserGraphParityHandlerTests.cs
//
// Unit tests over H10DataverseAppUserGraphParityHandler (task 053 — wave C4
// Batch 3E). T2 + T3 silent-fail trap owner.
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Graph / Dataverse Web API calls.
//   Fakes replace the repository + all five collaborator seams (creator,
//   verifier, granter, parity verifier, roles registry) so the handler
//   orchestration logic is exercised in isolation. Live-Graph/Dataverse
//   coverage belongs in env-guarded smoke tests (parity with
//   DataverseWebApiHealthProbe / RestApiAiSearchIndexVerifier — H10's real
//   collaborators are "NOT under test in the CI unit suite" per their own
//   file headers).
//
// COVERAGE (task POML acceptance criteria + dispatcher-required cases):
//   AC-1  Happy path — all GraphAppRoles.cs GUIDs (task 005 note taken:
//         this handler operates over WHATEVER the registry returns, so the
//         happy-path fixture uses a representative 3-role registry; a
//         SEPARATE test (AC-16) exercises the REAL L2GraphAppRolesRegistry
//         mirror to assert it enumerates exactly 15 populated GUIDs — 14 per
//         r1 task 005 + 1 added by task 144, H11 verification).
//   AC-2  T2 verify fail (CountMismatch) — QuarantineRequired, trap-T2 code,
//         diagnostic cites the observed count.
//   AC-3  T3 verify fail (Partial, 2 of 3 present) — QuarantineRequired,
//         trap-T3 code, diagnostic names the missing role.
//   AC-4  H10 escalation gate (one role has a null AppRoleId) — Resumable,
//         BEFORE any Dataverse/Graph write (creator/verifier/granter/parity
//         verifier all CallCount == 0).
//   AC-5  Idempotency — second invocation with a matching CompletedPhase
//         entry short-circuits Success no-op; no collaborator calls.
//   AC-6  Missing tenantId (§4D I1) — Resumable, no collaborator calls.
//   AC-7..10  Missing bffAppRegId / miClientId / miObjectId / dataverseEnvUrl
//         (upstream handler not yet run) — Resumable, distinct codes each.
//   AC-11 Run not found — Resumable.
//   AC-12 Handler-id mismatch — throws InvalidOperationException.
//   AC-13 BFF App User creation fails — Resumable, BFF-specific code; UAMI
//         creator NOT called (fail-fast on first failure).
//   AC-13b UAMI App User creation fails — Resumable, UAMI-specific code.
//   AC-14 Graph role grant fails (RetryableWithCleanup classification) —
//         distinct from T3's QuarantineRequired classification (dispatcher-
//         required: both classifications exercised).
//   AC-15 Idempotency key format determinism.
//   AC-16 L2GraphAppRolesRegistry (REAL, not faked) enumerates all 15 roles
//         with non-null AppRoleId + the well-known Graph resource appId
//         (14 per r1 task 005 + 1 added by task 144 -- H11 verification).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H10DataverseAppUserGraphParityHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h10-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string UamiObjectId = "66666666-7777-8888-9999-000000000000";
    private const string DataverseEnvUrl = "https://spaarke-acme.crm.dynamics.com";
    private const string BffSystemUserId = "bbbbbbbb-1111-1111-1111-111111111111";
    private const string UamiSystemUserId = "cccccccc-2222-2222-2222-222222222222";

    private static readonly IReadOnlyList<GraphAppRoleEntry> ThreeRoleFixture = new[]
    {
        new GraphAppRoleEntry("Files.Read.All", "01d4889c-1287-42c6-ac1f-5d1e02578ef6"),
        new GraphAppRoleEntry("Sites.Read.All", "332a536c-c7ef-4017-ab91-336970924f0d"),
        new GraphAppRoleEntry("User.Read.All", "df021288-bdef-4463-88db-98f22de89214"),
    };

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_AllCollaboratorsSucceed_SucceedsAndAdvancesState()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-1");
        var creator = FakeCreator.Success();
        var verifier = FakeVerifier.Verified(UamiSystemUserId);
        var granter = FakeGranter.Success(ThreeRoleFixture.Count);
        var parityVerifier = FakeParityVerifier.Verified(ThreeRoleFixture.Count);
        var registry = FakeRegistry.WithRoles(ThreeRoleFixture);
        var handler = BuildHandler(repo, creator, verifier, granter, parityVerifier, registry);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey(CustomerId));

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.CurrentPhase.Should().Be("H10");
        repo.LastWrittenRun.CompletedPhases.Should().ContainSingle().Which.Phase.Should().Be("H10");
        repo.LastWrittenRun.InterStepState.SystemUserId.Should().Be(UamiSystemUserId);
        repo.LastWrittenRun.InterStepState.BffAppRegSystemUserId.Should().Be(BffSystemUserId);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H10Gates.AppUserCreated)
            .WhoseValue.Status.Should().Be(GateState.Verified);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H10Gates.GraphRoleParity)
            .WhoseValue.Status.Should().Be(GateState.Verified);

        creator.CallCount.Should().Be(2, "both BFF app-reg and UAMI App Users are registered");
        creator.Requests.Select(r => r.ApplicationId).Should().Contain(new[] { BffAppRegId, UamiClientId });
        verifier.CallCount.Should().Be(1);
        verifier.LastApplicationId.Should().Be(UamiClientId, "T2 verifies the UAMI specifically");
        granter.CallCount.Should().Be(1);
        granter.LastUamiObjectId.Should().Be(UamiObjectId);
        parityVerifier.CallCount.Should().Be(1);
    }

    // ---------- AC-2 T2 verify fail ----------

    [Fact]
    public async Task AC2_T2VerificationCountMismatch_FailsQuarantineRequired()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-2");
        var creator = FakeCreator.Success();
        var verifier = FakeVerifier.Mismatch(observedCount: 0);
        var granter = FakeGranter.Success(ThreeRoleFixture.Count);
        var parityVerifier = FakeParityVerifier.Verified(ThreeRoleFixture.Count);
        var registry = FakeRegistry.WithRoles(ThreeRoleFixture);
        var handler = BuildHandler(repo, creator, verifier, granter, parityVerifier, registry);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H10Rejections.TrapT2VerificationFailed);
        failure.Diagnostic.Should().Contain("count=0");
        granter.CallCount.Should().Be(0, "T2 failure short-circuits before Graph role grant");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
    }

    // ---------- AC-3 T3 verify fail (partial) ----------

    [Fact]
    public async Task AC3_T3VerificationPartial_FailsQuarantineRequired_NamesMissingRoles()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-3");
        var creator = FakeCreator.Success();
        var verifier = FakeVerifier.Verified(UamiSystemUserId);
        var granter = FakeGranter.Success(ThreeRoleFixture.Count);
        var parityVerifier = FakeParityVerifier.Partial(
            missing: new[] { "User.Read.All" }, granted: 2, expected: 3);
        var registry = FakeRegistry.WithRoles(ThreeRoleFixture);
        var handler = BuildHandler(repo, creator, verifier, granter, parityVerifier, registry);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H10Rejections.TrapT3VerificationFailed);
        failure.Diagnostic.Should().Contain("User.Read.All");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
    }

    // ---------- AC-4 H10 escalation gate ----------

    [Fact]
    public async Task AC4_EscalationGate_NullAppRoleId_FailsResumable_BeforeAnyWrite()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-4");
        var creator = FakeCreator.Success();
        var verifier = FakeVerifier.Verified(UamiSystemUserId);
        var granter = FakeGranter.Success(ThreeRoleFixture.Count);
        var parityVerifier = FakeParityVerifier.Verified(ThreeRoleFixture.Count);
        var rolesWithNullGuid = new[]
        {
            ThreeRoleFixture[0],
            new GraphAppRoleEntry("Group.Read.All", null),
            ThreeRoleFixture[2],
        };
        var registry = FakeRegistry.WithRoles(rolesWithNullGuid);
        var handler = BuildHandler(repo, creator, verifier, granter, parityVerifier, registry);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H10Rejections.EscalationGateNullAppRoleId);
        failure.Diagnostic.Should().Contain("Group.Read.All");

        creator.CallCount.Should().Be(0, "escalation gate fires before ANY Dataverse write");
        verifier.CallCount.Should().Be(0);
        granter.CallCount.Should().Be(0, "escalation gate fires before ANY Graph write");
        parityVerifier.CallCount.Should().Be(0);
    }

    // ---------- AC-5 idempotency ----------

    [Fact]
    public async Task AC5_Idempotent_SecondInvocationWithMatchingCompletedPhase_IsNoOp()
    {
        var run = BuildRun();
        var expectedKey = H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey(CustomerId);
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H10",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-5");
        var creator = FakeCreator.Success();
        var verifier = FakeVerifier.Verified(UamiSystemUserId);
        var granter = FakeGranter.Success(ThreeRoleFixture.Count);
        var parityVerifier = FakeParityVerifier.Verified(ThreeRoleFixture.Count);
        var registry = FakeRegistry.WithRoles(ThreeRoleFixture);
        var handler = BuildHandler(repo, creator, verifier, granter, parityVerifier, registry);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.LastWrittenRun.Should().BeNull("idempotent no-op does not mutate state");
        creator.CallCount.Should().Be(0);
        verifier.CallCount.Should().Be(0);
        granter.CallCount.Should().Be(0);
        parityVerifier.CallCount.Should().Be(0);
    }

    // ---------- AC-6 missing tenantId ----------

    [Fact]
    public async Task AC6_MissingTenantId_FailsResumable_NoCollaboratorCalls()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-6");
        var creator = FakeCreator.Success();
        var handler = BuildHandler(repo, creator,
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H10Rejections.MissingTenantId);
        creator.CallCount.Should().Be(0);
    }

    // ---------- AC-7..10 missing InterStepState guards ----------

    [Fact]
    public async Task AC7_MissingBffAppRegId_FailsResumable()
    {
        var run = BuildRun();
        run.InterStepState.BffAppRegId = null;
        var repo = new FakeRepository(run, etag: "etag-7");
        var handler = BuildHandler(repo, FakeCreator.Success(),
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H10Rejections.MissingBffAppRegId);
        failure.Class.Should().Be(FailureClass.Resumable);
    }

    [Fact]
    public async Task AC8_MissingUamiClientId_FailsResumable()
    {
        var run = BuildRun();
        run.InterStepState.MiClientId = null;
        var repo = new FakeRepository(run, etag: "etag-8");
        var handler = BuildHandler(repo, FakeCreator.Success(),
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H10Rejections.MissingUamiClientId);
    }

    [Fact]
    public async Task AC9_MissingUamiObjectId_FailsResumable()
    {
        var run = BuildRun();
        run.InterStepState.MiObjectId = null;
        var repo = new FakeRepository(run, etag: "etag-9");
        var handler = BuildHandler(repo, FakeCreator.Success(),
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H10Rejections.MissingUamiObjectId);
    }

    [Fact]
    public async Task AC10_MissingDataverseEnvUrl_FailsResumable()
    {
        var run = BuildRun();
        run.InterStepState.DataverseEnvUrl = null;
        var repo = new FakeRepository(run, etag: "etag-10");
        var handler = BuildHandler(repo, FakeCreator.Success(),
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H10Rejections.MissingDataverseEnvUrl);
    }

    // ---------- AC-11 run not found ----------

    [Fact]
    public async Task AC11_RunNotFound_ReturnsResumableFailure()
    {
        var repo = new FakeRepository(run: null, etag: null);
        var handler = BuildHandler(repo, FakeCreator.Success(),
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H10Rejections.RunNotFound);
    }

    // ---------- AC-12 handler-id mismatch ----------

    [Fact]
    public async Task AC12_HandlerIdMismatch_Throws()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-12");
        var handler = BuildHandler(repo, FakeCreator.Success(),
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H0",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mismatched HandlerId*");
    }

    // ---------- AC-13 BFF / UAMI App User creation failures ----------

    [Fact]
    public async Task AC13_BffAppUserCreationFails_FailsResumable_UamiCreatorNotCalled()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13a");
        var creator = FakeCreator.FailForApplicationId(BffAppRegId, "Dataverse 500");
        var handler = BuildHandler(repo, creator,
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H10Rejections.BffAppUserCreationFailed);
        creator.CallCount.Should().Be(1, "handler fails fast — UAMI creator call never attempted");
    }

    [Fact]
    public async Task AC13b_UamiAppUserCreationFails_FailsResumable()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-13b");
        var creator = FakeCreator.FailForApplicationId(UamiClientId, "Dataverse 429");
        var handler = BuildHandler(repo, creator,
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H10Rejections.UamiAppUserCreationFailed);
        creator.CallCount.Should().Be(2, "BFF succeeded first; UAMI call attempted second and failed");
    }

    // ---------- AC-14 Graph role grant failure (RetryableWithCleanup — distinct from T3's QuarantineRequired) ----------

    [Fact]
    public async Task AC14_GraphRoleGrantFails_FailsRetryableWithCleanup_DistinctFromT3Quarantine()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-14");
        var granter = FakeGranter.Failure("POST appRoleAssignments failed: 403 Forbidden", new[] { "Sites.Read.All" });
        var handler = BuildHandler(repo, FakeCreator.Success(),
            FakeVerifier.Verified(UamiSystemUserId), granter,
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup,
            "grant-call failure is retryable (individually idempotent grants) — DISTINCT from T3's QuarantineRequired");
        failure.RejectionCode.Should().Be(H10Rejections.GraphRoleGrantFailed);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed, "RetryableWithCleanup maps to Failed, not Quarantined");
    }

    // ---------- AC-15 idempotency key format determinism ----------

    [Fact]
    public void AC15_IdempotencyKey_IsDeterministicByCustomerOnly()
    {
        var k1 = H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey("acme");
        var k2 = H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey("acme");
        k1.Should().Be(k2);
        k1.Should().Be("appuser-acme");

        var k3 = H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey("other-customer");
        k3.Should().NotBe(k1);
    }

    // ---------- AC-16 real L2GraphAppRolesRegistry mirror — all 15 GUIDs ----------

    [Fact]
    public void AC16_L2GraphAppRolesRegistry_EnumeratesAllPopulatedGuids()
    {
        var registry = new L2GraphAppRolesRegistry();

        registry.GraphResourceAppId.Should().Be("00000003-0000-0000-c000-000000000000");

        var roles = registry.GetAll();
        roles.Should().HaveCount(15,
            "GraphAppRoles.cs r3 task 062 catalog is 14 entries + 1 added by task 144 (User.Invite.All, " +
            "H11 B2BGuest preset)");
        roles.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.AppRoleId),
            "all AppRoleId GUIDs must be populated (14 by r1 task 005 on 2026-08-17, +1 by task 144 on " +
            "2026-08-20) — a null here would silently fire the H10 escalation gate for every future run");
        roles.Select(r => r.Value).Should().OnlyHaveUniqueItems();
        roles.Select(r => r.AppRoleId).Should().OnlyHaveUniqueItems("no two roles share an AppRoleId GUID");
    }

    // ---------- helpers ----------

    private static H10DataverseAppUserGraphParityHandler BuildHandler(
        FakeRepository repo,
        FakeCreator creator,
        FakeVerifier verifier,
        FakeGranter granter,
        FakeParityVerifier parityVerifier,
        FakeRegistry registry)
    {
        var options = Options.Create(new H10DataverseAppUserGraphParityOptions());
        return new H10DataverseAppUserGraphParityHandler(
            repo, creator, verifier, granter, parityVerifier, registry, options,
            NullLogger<H10DataverseAppUserGraphParityHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H10DataverseAppUserGraphParityHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(bool includeTenantId = true)
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
            run.Parameters.NonSecret[H10DataverseAppUserGraphParityHandler.TenantIdParameterKey] = TenantId;
        }
        run.InterStepState.BffAppRegId = BffAppRegId;
        run.InterStepState.MiClientId = UamiClientId;
        run.InterStepState.MiObjectId = UamiObjectId;
        run.InterStepState.DataverseEnvUrl = DataverseEnvUrl;
        return run;
    }

    /// <summary>Repository fake — records last written run.</summary>
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
            => Task.FromResult(_run is null || _etag is null ? null : new ProvisioningRunReadResult(_run, _etag));

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

    private sealed class FakeCreator : IDataverseAppUserCreator
    {
        private readonly Func<DataverseAppUserCreationRequest, DataverseAppUserCreationOutcome> _behavior;
        public int CallCount { get; private set; }
        public List<DataverseAppUserCreationRequest> Requests { get; } = new();

        private FakeCreator(Func<DataverseAppUserCreationRequest, DataverseAppUserCreationOutcome> behavior)
            => _behavior = behavior;

        public static FakeCreator Success() => new(req => req.ApplicationId == BffAppRegId
            ? new DataverseAppUserCreationOutcome.Success(BffSystemUserId)
            : new DataverseAppUserCreationOutcome.Success(UamiSystemUserId));

        public static FakeCreator FailForApplicationId(string applicationId, string diagnostic) => new(req =>
            req.ApplicationId == applicationId
                ? new DataverseAppUserCreationOutcome.Failure(diagnostic)
                : req.ApplicationId == BffAppRegId
                    ? new DataverseAppUserCreationOutcome.Success(BffSystemUserId)
                    : new DataverseAppUserCreationOutcome.Success(UamiSystemUserId));

        public Task<DataverseAppUserCreationOutcome> EnsureAppUserAsync(
            DataverseAppUserCreationRequest request, CancellationToken ct)
        {
            CallCount++;
            Requests.Add(request);
            return Task.FromResult(_behavior(request));
        }
    }

    private sealed class FakeVerifier : IDataverseAppUserVerifier
    {
        private readonly DataverseAppUserVerificationResult _result;
        public int CallCount { get; private set; }
        public string? LastApplicationId { get; private set; }

        private FakeVerifier(DataverseAppUserVerificationResult result) => _result = result;

        public static FakeVerifier Verified(string systemUserId)
            => new(new DataverseAppUserVerificationResult.Verified(systemUserId));

        public static FakeVerifier Mismatch(int observedCount)
            => new(new DataverseAppUserVerificationResult.CountMismatch(observedCount));

        public Task<DataverseAppUserVerificationResult> VerifyAsync(
            string environmentUrl, string tenantId, string applicationId, CancellationToken ct)
        {
            CallCount++;
            LastApplicationId = applicationId;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeGranter : IGraphAppRoleGranter
    {
        private readonly GraphAppRoleGrantOutcome _outcome;
        public int CallCount { get; private set; }
        public string? LastUamiObjectId { get; private set; }

        private FakeGranter(GraphAppRoleGrantOutcome outcome) => _outcome = outcome;

        public static FakeGranter Success(int grantedCount) => new(new GraphAppRoleGrantOutcome.Success(grantedCount));

        public static FakeGranter Failure(string diagnostic, IReadOnlyList<string> failedRoles)
            => new(new GraphAppRoleGrantOutcome.Failure(diagnostic, failedRoles));

        public Task<GraphAppRoleGrantOutcome> GrantRolesAsync(
            string uamiServicePrincipalObjectId, string tenantId,
            IReadOnlyList<GraphAppRoleEntry> expectedRoles, CancellationToken ct)
        {
            CallCount++;
            LastUamiObjectId = uamiServicePrincipalObjectId;
            return Task.FromResult(_outcome);
        }
    }

    private sealed class FakeParityVerifier : IGraphAppRoleParityVerifier
    {
        private readonly GraphAppRoleParityResult _result;
        public int CallCount { get; private set; }

        private FakeParityVerifier(GraphAppRoleParityResult result) => _result = result;

        public static FakeParityVerifier Verified(int count) => new(new GraphAppRoleParityResult.Verified(count));

        public static FakeParityVerifier Partial(IReadOnlyList<string> missing, int granted, int expected)
            => new(new GraphAppRoleParityResult.Partial(missing, granted, expected));

        public Task<GraphAppRoleParityResult> VerifyAsync(
            string uamiServicePrincipalObjectId, string tenantId,
            IReadOnlyList<GraphAppRoleEntry> expectedRoles, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeRegistry : IGraphAppRolesRegistry
    {
        private readonly IReadOnlyList<GraphAppRoleEntry> _roles;
        public string GraphResourceAppId => "00000003-0000-0000-c000-000000000000";

        private FakeRegistry(IReadOnlyList<GraphAppRoleEntry> roles) => _roles = roles;

        public static FakeRegistry WithRoles(IReadOnlyList<GraphAppRoleEntry> roles) => new(roles);

        public IReadOnlyList<GraphAppRoleEntry> GetAll() => _roles;
    }
}
