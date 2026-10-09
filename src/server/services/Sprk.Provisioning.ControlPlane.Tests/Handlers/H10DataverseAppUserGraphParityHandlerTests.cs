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
//   AC-16 L2GraphAppRolesRegistry (REAL, not faked) is the task 261 stamp set:
//         FileStorageContainer.Selected (Entra) + Mail.Read/ReadWrite/Send
//         (Exchange-scoped), all GUIDs populated, no directory-write role.
//   T261  (G31, owner 2026-10-09): H10 removes every Graph app role outside the
//         Entra-granted set (passing the stamp's client id so the granter can
//         check the target), logs it, and does not complete while one remains:
//         removal failure → RetryableWithCleanup; an extra still present after
//         removal → QuarantineRequired; the extras re-read failing → Resumable;
//         nothing extra → Success with the gate Verified.
//   T259 (ISS-010, owner 2026-10-09): the customer unit is resolved before any
//         App User and both App Users are requested IN it; its id lands in
//         InterStepState.CustomerBusinessUnitId; a unit not under the root and
//         an App User found in another unit are QuarantineRequired (nothing
//         moved); an ambiguous name, a read fault, and a missing/invalid
//         displayName (incl. the Secure Record unit's name) are Resumable with
//         no write.
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
    private const string CustomerName = "Acme Corporation";
    private static readonly Guid CustomerUnitId = Guid.Parse("dddddddd-3333-3333-3333-333333333333");

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
        success.IdempotencyKey.Should().Be(H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey(CustomerId, ThreeRoleFixture));

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
        creator.UnitNames.Should().Equal(CustomerName);
        creator.Requests.Should().OnlyContain(r => r.BusinessUnitId == CustomerUnitId,
            "T259: both App Users are created IN the customer's unit, never the root");
        repo.LastWrittenRun.InterStepState.CustomerBusinessUnitId.Should().Be(CustomerUnitId.ToString("D"));
        creator.Requests.Select(r => r.ApplicationId).Should().Contain(new[] { BffAppRegId, UamiClientId });

        // auth-v4 §10.4 (punch row A41) — the UAMI row's azureactivedirectoryobjectid
        // MUST be the UAMI's principalId (UamiObjectId), NEVER its clientId.
        var uamiRequest = creator.Requests.Should().ContainSingle(r => r.ApplicationId == UamiClientId).Subject;
        uamiRequest.AzureActiveDirectoryObjectId.Should().Be(UamiObjectId,
            "the UAMI row's azureactivedirectoryobjectid MUST be the principalId — the auth-v4 §10.4 " +
            "silent-fail trap fires when this is set to the clientId instead");
        uamiRequest.AzureActiveDirectoryObjectId.Should().NotBe(UamiClientId,
            "NEVER the clientId — this is the exact trap auth-v4 calls its 'single most-missed item'");

        // The BFF app-reg row deliberately does NOT set an explicit
        // azureactivedirectoryobjectid — Dataverse's applicationid-only
        // auto-resolution is reliable for a standard app registration.
        var bffRequest = creator.Requests.Should().ContainSingle(r => r.ApplicationId == BffAppRegId).Subject;
        bffRequest.AzureActiveDirectoryObjectId.Should().BeNull();

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
        var expectedKey = H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey(CustomerId, ThreeRoleFixture);
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
    public void AC15_IdempotencyKey_IsDeterministicByCustomerAndCatalog()
    {
        var k1 = H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey("acme", ThreeRoleFixture);
        var k2 = H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey("acme", ThreeRoleFixture.Reverse().ToArray());
        k1.Should().Be(k2, "role order does not matter");
        k1.Should().MatchRegex("^appuser-acme-g[0-9a-f]{8}$");

        H10DataverseAppUserGraphParityHandler.BuildIdempotencyKey("other", ThreeRoleFixture).Should().NotBe(k1);
    }

    [Fact]
    public async Task T261_ARunCompletedUnderAnOlderCatalog_ReRunsTheReconcile_NotANoOp()
    {
        // A stamp whose H10 completed before task 261 recorded the old key ("appuser-acme"); a re-dispatch must not
        // short-circuit on it, or the 11-role grant would never be removed.
        var run = BuildRun();
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H10", IdempotencyKey = "appuser-acme", StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
        });
        var granter = FakeGranter.Success(3, removed: new[] { "Directory.ReadWrite.All" });
        var handler = BuildHandler(new FakeRepository(run, etag: "etag-t261f"), FakeCreator.Success(),
            FakeVerifier.Verified(UamiSystemUserId), granter, FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        granter.RemovalCallCount.Should().Be(1);
    }

    [Fact]
    public async Task T261_ExtrasListedJustAfterARemoval_AreReReadWithBackoff_AndPassOnceGone()
    {
        // Graph is eventually consistent: the first re-read still lists the role H10 just deleted.
        var parity = FakeParityVerifier.Verified(3, extrasSequence: new GraphAppRoleExtrasResult[]
        {
            new GraphAppRoleExtrasResult.Found(new[] { "Directory.ReadWrite.All" }),
            new GraphAppRoleExtrasResult.Found(new[] { "Directory.ReadWrite.All" }),
            new GraphAppRoleExtrasResult.None(),
        });
        var handler = BuildHandler(new FakeRepository(BuildRun(), etag: "etag-t261g"), FakeCreator.Success(),
            FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3, removed: new[] { "Directory.ReadWrite.All" }),
            parity, FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        parity.ExtrasCallCount.Should().Be(3);
    }

    [Fact]
    public async Task T261_ExtrasStillListedAfterThisCallRemovedThem_IsResumable_NotQuarantine()
    {
        var parity = FakeParityVerifier.Verified(3, extras: new GraphAppRoleExtrasResult.Found(new[] { "Directory.ReadWrite.All" }));
        var repo = new FakeRepository(BuildRun(), etag: "etag-t261h");
        var handler = BuildHandler(repo, FakeCreator.Success(), FakeVerifier.Verified(UamiSystemUserId),
            FakeGranter.Success(3, removed: new[] { "Directory.ReadWrite.All" }), parity, FakeRegistry.WithRoles(ThreeRoleFixture));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.Resumable, "a healthy stamp must not be quarantined for replication lag");
        failure.RejectionCode.Should().Be(H10Rejections.GraphRoleExtrasUnverified);
        parity.ExtrasCallCount.Should().Be(5, "the bounded re-read ran to its limit before deciding");
        repo.LastWrittenRun!.CompletedPhases.Should().BeEmpty();
    }

    // ---------- AC-16 real L2GraphAppRolesRegistry mirror — all 15 GUIDs ----------

    [Fact]
    public void AC16_L2GraphAppRolesRegistry_IsTheTask261StampSet()
    {
        IGraphAppRolesRegistry registry = new L2GraphAppRolesRegistry();

        registry.GraphResourceAppId.Should().Be("00000003-0000-0000-c000-000000000000");

        var roles = registry.GetAll();
        roles.Select(r => r.Value).Should().BeEquivalentTo(
            new[] { "FileStorageContainer.Selected", "Mail.Read", "Mail.ReadWrite", "Mail.Send" },
            "task 261: the stamp identity's evidence-backed set (notes/t261-stamp-graph-least-privilege.md)");
        roles.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.AppRoleId),
            "a null AppRoleId would fire the H10 escalation gate for every run");
        roles.Select(r => r.AppRoleId).Should().OnlyHaveUniqueItems("no two roles share an AppRoleId GUID");
        registry.GetEntraGranted().Select(r => r.Value).Should().Equal(new[] { "FileStorageContainer.Selected" },
            "the mailbox roles are Exchange-scoped (H14a) — FileStorageContainer.Selected is H10's only Entra grant");
        roles.Select(r => r.Value).Should().NotContain(new[]
        {
            "Directory.ReadWrite.All", "User.ReadWrite.All", "GroupMember.ReadWrite.All", "User.Invite.All",
            "Files.Read.All", "Files.ReadWrite.All", "Sites.Read.All", "Sites.ReadWrite.All",
        }, "G31: a stamp in Spaarke's tenant holds no tenant-wide directory or SharePoint role");
    }

    // ---------- T261 — H10 removes Graph app roles outside the stamp set ----------

    [Fact]
    public async Task T261_ExtraRoles_AreRemoved_WithTheStampClientId_AndH10Completes()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-t261a");
        var granter = FakeGranter.Success(3, removed: new[] { "Directory.ReadWrite.All", "User.Invite.All" });
        var parity = FakeParityVerifier.Verified(3);
        var handler = BuildHandler(repo, FakeCreator.Success(), FakeVerifier.Verified(UamiSystemUserId), granter,
            parity, FakeRegistry.WithRoles(ThreeRoleFixture));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        granter.RemovalCallCount.Should().Be(1);
        granter.LastRemovalTarget.Should().Be((UamiObjectId, UamiClientId),
            "the removal names the stamp SP AND its client id, so the granter can refuse a drifted object id");
        granter.LastAllowedRoles.Should().BeEquivalentTo(ThreeRoleFixture,
            "the allowed set is exactly the Entra-granted set H10 just granted");
        parity.ExtrasCallCount.Should().Be(1, "T3 re-reads independently that nothing extra remains");
        repo.LastWrittenRun!.GateStates[H10Gates.GraphRoleParity].Status.Should().Be(GateState.Verified);
    }

    [Fact]
    public async Task T261_RemovalFailure_FailsRetryableWithCleanup_AndH10IsNotComplete()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-t261b");
        var granter = FakeGranter.Success(3, removalFailure: new GraphAppRoleRemovalOutcome.Failure(
            "DELETE appRoleAssignments/x failed: 403 Forbidden.", new[] { "Files.Read.All" }, new[] { "Directory.ReadWrite.All" }));
        var parity = FakeParityVerifier.Verified(3);
        var handler = BuildHandler(repo, FakeCreator.Success(), FakeVerifier.Verified(UamiSystemUserId), granter,
            parity, FakeRegistry.WithRoles(ThreeRoleFixture));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(H10Rejections.GraphRoleRemovalFailed);
        failure.Diagnostic.Should().Contain("Directory.ReadWrite.All");
        repo.LastWrittenRun!.CompletedPhases.Should().BeEmpty("H10 is not complete while an extra role remains");
        parity.CallCount.Should().Be(0, "T3 does not run before the removal succeeded");
    }

    [Fact]
    public async Task T261_ExtraListedThatTheRemovalPassDidNotSee_IsQuarantined()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-t261c");
        var parity = FakeParityVerifier.Verified(3, extras: new GraphAppRoleExtrasResult.Found(new[] { "Sites.ReadWrite.All" }));
        var handler = BuildHandler(repo, FakeCreator.Success(), FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            parity, FakeRegistry.WithRoles(ThreeRoleFixture));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H10Rejections.TrapT3UnexpectedRoles);
        failure.Diagnostic.Should().Contain("Sites.ReadWrite.All");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.CompletedPhases.Should().BeEmpty();
    }

    [Fact]
    public async Task T261_ExtrasReReadFails_IsResumable_NotSuccess()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-t261d");
        var parity = FakeParityVerifier.Verified(3, extras: new GraphAppRoleExtrasResult.Unknown("GET appRoleAssignments failed: 503"));
        var handler = BuildHandler(repo, FakeCreator.Success(), FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            parity, FakeRegistry.WithRoles(ThreeRoleFixture));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H10Rejections.GraphRoleExtrasUnverified);
        repo.LastWrittenRun!.CompletedPhases.Should().BeEmpty("no verdict is not a pass");
    }

    [Fact]
    public async Task T261_EscalationGate_StillFiresBeforeAnyRemoval()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-t261e");
        var granter = FakeGranter.Success(1);
        var handler = BuildHandler(repo, FakeCreator.Success(), FakeVerifier.Verified(UamiSystemUserId), granter,
            FakeParityVerifier.Verified(1),
            FakeRegistry.WithRoles(new[] { new GraphAppRoleEntry("FileStorageContainer.Selected", null) }));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.RejectionCode.Should().Be(H10Rejections.EscalationGateNullAppRoleId);
        granter.RemovalCallCount.Should().Be(0, "an incomplete catalog never drives a removal");
    }

    // ---------- T259 customer business unit (ISS-010) ----------

    [Fact]
    public async Task T259_CustomerUnitUnderAnotherParent_IsQuarantined_AndNoAppUserIsRegistered()
    {
        var run = BuildRun();
        var repo = new FakeRepository(run, etag: "etag-t259a");
        var creator = FakeCreator.Success(unit: new CustomerBusinessUnitOutcome.WrongParent(CustomerUnitId, Guid.NewGuid(), Guid.NewGuid()));
        var handler = BuildHandler(repo, creator, FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H10Rejections.CustomerBusinessUnitWrongParent);
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        creator.CallCount.Should().Be(0, "no App User is created before the customer unit is right");
    }

    [Fact]
    public async Task T259_TheRootCarryingTheCustomersName_IsQuarantined()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-t259b");
        var creator = FakeCreator.Success(unit: new CustomerBusinessUnitOutcome.WrongParent(CustomerUnitId, null, CustomerUnitId));
        var handler = BuildHandler(repo, creator, FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.RejectionCode.Should().Be(H10Rejections.CustomerBusinessUnitWrongParent);
        failure.Diagnostic.Should().Contain("it is the root");
        creator.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("failure")]
    public async Task T259_AmbiguousOrUnreadableCustomerUnit_IsResumable_AndNoAppUserIsRegistered(string kind)
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-t259c");
        CustomerBusinessUnitOutcome unit = kind == "ambiguous"
            ? new CustomerBusinessUnitOutcome.Ambiguous(2)
            : new CustomerBusinessUnitOutcome.Failure("GET businessunits failed: 503");
        var creator = FakeCreator.Success(unit: unit);
        var handler = BuildHandler(repo, creator, FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(kind == "ambiguous"
            ? H10Rejections.CustomerBusinessUnitAmbiguous
            : H10Rejections.CustomerBusinessUnitFailed);
        creator.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(BffAppRegId)]
    [InlineData(UamiClientId)]
    public async Task T259_AnAppUserAlreadyInAnotherUnit_IsQuarantined_NeverMoved(string applicationId)
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-t259d");
        var root = Guid.NewGuid();
        var creator = FakeCreator.ForeignUnitFor(applicationId, root);
        var granter = FakeGranter.Success(3);
        var handler = BuildHandler(repo, creator, FakeVerifier.Verified(UamiSystemUserId), granter,
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H10Rejections.AppUserInForeignBusinessUnit);
        failure.Diagnostic.Should().Contain(root.ToString()).And.Contain("never moves");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.InterStepState.CustomerBusinessUnitId.Should().BeNull("H10 did not complete");
        granter.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null, H10Rejections.CustomerDisplayNameRequired)]
    [InlineData("   ", H10Rejections.CustomerDisplayNameRequired)]
    [InlineData("Secure Record", H10Rejections.CustomerDisplayNameInvalid)]
    [InlineData("secure record", H10Rejections.CustomerDisplayNameInvalid)]
    [InlineData(" Acme", H10Rejections.CustomerDisplayNameInvalid)]
    [InlineData("Acme\u0007", H10Rejections.CustomerDisplayNameInvalid)]
    public async Task T259_MissingOrUnusableDisplayName_IsResumable_BeforeAnyDataverseCall(string? name, string code)
    {
        var run = BuildRun();
        if (name is null)
        {
            run.Parameters.NonSecret.Remove(IntakeParameterCatalog.DisplayName);
        }
        else
        {
            run.Parameters.NonSecret[IntakeParameterCatalog.DisplayName] = System.Text.RegularExpressions.Regex.Unescape(name);
        }
        var repo = new FakeRepository(run, etag: "etag-t259e");
        var creator = FakeCreator.Success();
        var handler = BuildHandler(repo, creator, FakeVerifier.Verified(UamiSystemUserId), FakeGranter.Success(3),
            FakeParityVerifier.Verified(3), FakeRegistry.WithRoles(ThreeRoleFixture));

        var failure = (await handler.HandleAsync(BuildEnvelope(), CancellationToken.None))
            .Should().BeOfType<HandlerResult.Failure>().Subject;

        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(code);
        creator.UnitNames.Should().BeEmpty();
        creator.CallCount.Should().Be(0);
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
        var options = Options.Create(new H10DataverseAppUserGraphParityOptions { ExtrasRecheckDelay = TimeSpan.Zero });
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
            TenancyModel = "Model2",
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        if (includeTenantId)
        {
            run.Parameters.NonSecret[H10DataverseAppUserGraphParityHandler.TenantIdParameterKey] = TenantId;
        }
        run.Parameters.NonSecret[IntakeParameterCatalog.DisplayName] = CustomerName;   // T259
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
        private readonly CustomerBusinessUnitOutcome _unit;
        public int CallCount { get; private set; }
        public List<DataverseAppUserCreationRequest> Requests { get; } = new();
        public List<string> UnitNames { get; } = new();

        private FakeCreator(
            Func<DataverseAppUserCreationRequest, DataverseAppUserCreationOutcome> behavior, CustomerBusinessUnitOutcome? unit = null)
        {
            _behavior = behavior;
            _unit = unit ?? new CustomerBusinessUnitOutcome.Success(CustomerUnitId, Created: true);
        }

        public static FakeCreator Success(CustomerBusinessUnitOutcome? unit = null) => new(req => req.ApplicationId == BffAppRegId
            ? new DataverseAppUserCreationOutcome.Success(BffSystemUserId)
            : new DataverseAppUserCreationOutcome.Success(UamiSystemUserId), unit);

        public static FakeCreator ForeignUnitFor(string applicationId, Guid unit) => new(req =>
            req.ApplicationId == applicationId
                ? new DataverseAppUserCreationOutcome.InForeignBusinessUnit(Guid.NewGuid().ToString(), unit)
                : req.ApplicationId == BffAppRegId
                    ? new DataverseAppUserCreationOutcome.Success(BffSystemUserId)
                    : new DataverseAppUserCreationOutcome.Success(UamiSystemUserId));

        public Task<CustomerBusinessUnitOutcome> EnsureCustomerBusinessUnitAsync(
            string environmentUrl, string tenantId, string name, CancellationToken ct)
        {
            UnitNames.Add(name);
            return Task.FromResult(_unit);
        }

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
        private readonly GraphAppRoleRemovalOutcome _removal;
        public int CallCount { get; private set; }
        public string? LastUamiObjectId { get; private set; }
        public int RemovalCallCount { get; private set; }
        public (string ObjectId, string ClientId)? LastRemovalTarget { get; private set; }
        public IReadOnlyList<GraphAppRoleEntry>? LastAllowedRoles { get; private set; }

        private FakeGranter(GraphAppRoleGrantOutcome outcome, GraphAppRoleRemovalOutcome? removal = null)
        {
            _outcome = outcome;
            _removal = removal ?? new GraphAppRoleRemovalOutcome.Success(Array.Empty<string>());
        }

        public static FakeGranter Success(
            int grantedCount, IReadOnlyList<string>? removed = null, GraphAppRoleRemovalOutcome? removalFailure = null)
            => new(new GraphAppRoleGrantOutcome.Success(grantedCount),
                removalFailure ?? new GraphAppRoleRemovalOutcome.Success(removed ?? Array.Empty<string>()));

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

        public Task<GraphAppRoleRemovalOutcome> RemoveUnexpectedRolesAsync(
            string uamiServicePrincipalObjectId, string uamiClientId, string tenantId,
            IReadOnlyList<GraphAppRoleEntry> allowedRoles, CancellationToken ct)
        {
            RemovalCallCount++;
            LastRemovalTarget = (uamiServicePrincipalObjectId, uamiClientId);
            LastAllowedRoles = allowedRoles;
            return Task.FromResult(_removal);
        }
    }

    private sealed class FakeParityVerifier : IGraphAppRoleParityVerifier
    {
        private readonly GraphAppRoleParityResult _result;
        private readonly GraphAppRoleExtrasResult _extras;
        private readonly Queue<GraphAppRoleExtrasResult>? _sequence;
        public int CallCount { get; private set; }
        public int ExtrasCallCount { get; private set; }

        private FakeParityVerifier(GraphAppRoleParityResult result, GraphAppRoleExtrasResult? extras = null,
            IEnumerable<GraphAppRoleExtrasResult>? sequence = null)
        {
            _result = result;
            _extras = extras ?? new GraphAppRoleExtrasResult.None();
            _sequence = sequence is null ? null : new Queue<GraphAppRoleExtrasResult>(sequence);
        }

        public static FakeParityVerifier Verified(int count, GraphAppRoleExtrasResult? extras = null,
            IEnumerable<GraphAppRoleExtrasResult>? extrasSequence = null)
            => new(new GraphAppRoleParityResult.Verified(count), extras, extrasSequence);

        public static FakeParityVerifier Partial(IReadOnlyList<string> missing, int granted, int expected)
            => new(new GraphAppRoleParityResult.Partial(missing, granted, expected));

        public Task<GraphAppRoleParityResult> VerifyAsync(
            string uamiServicePrincipalObjectId, string tenantId,
            IReadOnlyList<GraphAppRoleEntry> expectedRoles, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }

        public Task<GraphAppRoleExtrasResult> FindUnexpectedRolesAsync(
            string uamiServicePrincipalObjectId, string tenantId,
            IReadOnlyList<GraphAppRoleEntry> allowedRoles, CancellationToken ct)
        {
            ExtrasCallCount++;
            return Task.FromResult(_sequence is { Count: > 0 } ? _sequence.Dequeue() : _extras);
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
