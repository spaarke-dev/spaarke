// -----------------------------------------------------------------------------
// H14IntegrationWiringHandlerTests.cs
//
// Unit tests over H14IntegrationWiringHandler — the H14 parent that dispatches
// H14a/H14b/H14c in parallel via Task.WhenAll (task 073 — wave C4 Batch 3F).
//
// ADR-038 CATEGORY: Path #1 — pure C# unit test. Constructs REAL sub-handler
// instances (H14aExchangePolicySubHandler / H14bGraphWebhookSubHandler /
// H14cDataverseWebhookSubHandler are sealed concrete types, not fakeable via
// an interface) wired with FAKE seam collaborators — this genuinely exercises
// the parent's dispatch + aggregation + partial-completion-skip + idempotency
// orchestration through the real sub-handler code, which is the right level
// of test for THIS handler's behavior (the per-substep business logic is
// covered by the sibling H14a/b/c*Tests.cs files).
//
// COVERAGE (task POML acceptance criteria):
//   AC-1  Happy path — all 3 sub-steps dispatched in parallel + succeed;
//         CompletedPhases gains 4 entries (H14a, H14b, H14c, H14); Success.
//   AC-2  Missing tenantId — Resumable, BEFORE any sub-handler seam invoked.
//   AC-3  Missing InterStepState.bffAppRegId — Resumable, seams never invoked.
//   AC-4  Partial failure — H14a drifts (QuarantineRequired) while H14b/H14c
//         succeed; overall Failure is QuarantineRequired (worst); H14b + H14c
//         CompletedPhase entries ARE persisted (partial success), H14a + the
//         H14 parent entry are NOT.
//   AC-5  Full idempotency — CompletedPhases already has "H14" — Success
//         no-op; repository ReplaceRunAsync never called.
//   AC-6  Partial-resume idempotency — CompletedPhases already has H14a's
//         expected key recorded — H14a's applier is NEVER invoked on
//         re-dispatch (parent-level skip), H14b/H14c seams ARE invoked.
//   AC-7  H14b no-targets-configured — overall failure includes H14b's
//         diagnostic; H14a + H14c still succeed + persist.
//   AC-8  Handler-id mismatch — throws.
//   AC-9  ExpectedSubStepCount invariant is exactly 3 (no S2S 4th sub-step).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H14IntegrationWiringHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h14-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string DataverseEnvUrl = "https://spaarke-acme.crm.dynamics.com";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string PolicyScopeGroupId = "77777777-8888-9999-0000-111111111111";
    private const string NotificationBaseUrl = "https://sprk-acme-prod.azurewebsites.net";
    private const string CommunicationResource = "communications/callRecords";
    private const string EmailResource = "users/mailbox-guid/messages";
    private const string SigningKey = "super-secret-hmac-key";

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_AllThreeSucceed_PersistsFourCompletedPhases()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-1");
        var applier = FakeApplier.Applied(2);
        var reader = FakeReader.Success(SigningKey);
        var graphCreator = FakeGraphCreator.Success();
        var dvRegistrar = FakeDvRegistrar.Created();
        var handler = BuildHandler(repo, applier, reader, graphCreator, dvRegistrar);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.CompletedPhases.Select(cp => cp.Phase).Should().BeEquivalentTo(new[] { "H14a", "H14b", "H14c", "H14" });
        repo.LastWrittenRun.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H14Gates.ExchangePolicyApplied);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H14Gates.GraphWebhooksWired);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H14Gates.DataverseWebhookWired);
        applier.CallCount.Should().Be(1);
        graphCreator.CallCount.Should().Be(2, "Communication + Email targets both configured");
        dvRegistrar.CallCount.Should().Be(1);
    }

    // ---------- AC-2 missing tenantId ----------

    [Fact]
    public async Task AC2_MissingTenantId_FailsResumable_NoSeamInvoked()
    {
        var run = BuildRun(includeTenantId: false);
        var repo = new FakeRepository(run, etag: "etag-2");
        var applier = FakeApplier.Applied(2);
        var graphCreator = FakeGraphCreator.Success();
        var handler = BuildHandler(repo, applier, FakeReader.Success(SigningKey), graphCreator, FakeDvRegistrar.Created());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H14Rejections.MissingTenantId);
        applier.CallCount.Should().Be(0);
        graphCreator.CallCount.Should().Be(0);
    }

    // ---------- AC-3 missing InterStepState.bffAppRegId ----------

    [Fact]
    public async Task AC3_MissingBffAppRegId_FailsResumable()
    {
        var run = BuildRun();
        run.InterStepState.BffAppRegId = null;
        var repo = new FakeRepository(run, etag: "etag-3");
        var applier = FakeApplier.Applied(2);
        var handler = BuildHandler(repo, applier, FakeReader.Success(SigningKey), FakeGraphCreator.Success(), FakeDvRegistrar.Created());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H14Rejections.MissingBffAppRegId);
        applier.CallCount.Should().Be(0);
    }

    // ---------- AC-4 partial failure (H14a drifts) ----------

    [Fact]
    public async Task AC4_H14aDrifts_OverallQuarantineRequired_H14bH14cPersistedDespiteH14aFailure()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-4");
        var applier = FakeApplier.Drift(new[] { BffAppRegId, "ffffffff-0000-0000-0000-000000000000" });
        var handler = BuildHandler(repo, applier, FakeReader.Success(SigningKey), FakeGraphCreator.Success(), FakeDvRegistrar.Created());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H14Rejections.SubStepFailed);
        failure.Diagnostic.Should().Contain("H14a");

        repo.LastWrittenRun.Should().NotBeNull();
        var phases = repo.LastWrittenRun!.CompletedPhases.Select(cp => cp.Phase).ToList();
        phases.Should().Contain("H14b").And.Contain("H14c");
        phases.Should().NotContain("H14a").And.NotContain("H14");
        repo.LastWrittenRun.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
    }

    // ---------- AC-5 full idempotency ----------

    [Fact]
    public async Task AC5_FullIdempotency_ParentPhaseAlreadyRecorded_IsNoOp_RepositoryNeverWritten()
    {
        var run = BuildRun();
        var expectedKey = "h14-acme-prerecorded-key";
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H14",
            IdempotencyKey = expectedKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-5");
        var applier = FakeApplier.Applied(2);
        var handler = BuildHandler(repo, applier, FakeReader.Success(SigningKey), FakeGraphCreator.Success(), FakeDvRegistrar.Created());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.ReplaceCallCount.Should().Be(0, "full parent-level idempotency short-circuits before any write");
        applier.CallCount.Should().Be(0);
    }

    // ---------- AC-6 partial-resume idempotency ----------

    [Fact]
    public async Task AC6_PartialResume_H14aAlreadyCompleted_SkipsH14aSeam_InvokesH14bH14c()
    {
        var run = BuildRun();
        var expectedH14aKey = H14aExchangePolicySubHandler.BuildIdempotencyKey(CustomerId, new[] { BffAppRegId, UamiClientId });
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = "H14a",
            IdempotencyKey = expectedH14aKey,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            JobId = "prior-run",
        });
        var repo = new FakeRepository(run, etag: "etag-6");
        var applier = FakeApplier.Applied(2);
        var graphCreator = FakeGraphCreator.Success();
        var dvRegistrar = FakeDvRegistrar.Created();
        var handler = BuildHandler(repo, applier, FakeReader.Success(SigningKey), graphCreator, dvRegistrar);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        applier.CallCount.Should().Be(0, "H14a's expected key already matches a recorded CompletedPhase — parent skips re-invoking it");
        graphCreator.CallCount.Should().Be(2);
        dvRegistrar.CallCount.Should().Be(1);

        var phases = repo.LastWrittenRun!.CompletedPhases.Select(cp => cp.Phase).ToList();
        phases.Should().Contain("H14a").And.Contain("H14b").And.Contain("H14c").And.Contain("H14");
        phases.Count(p => p == "H14a").Should().Be(1, "the pre-existing H14a entry must not be duplicated");
    }

    // ---------- AC-7 H14b no targets configured ----------

    [Fact]
    public async Task AC7_H14bNoTargetsConfigured_OverallFailureIncludesH14bDiagnostic_H14aH14cStillPersist()
    {
        var run = BuildRun(includeGraphResources: false);
        var repo = new FakeRepository(run, etag: "etag-7");
        var applier = FakeApplier.Applied(2);
        var handler = BuildHandler(repo, applier, FakeReader.Success(SigningKey), FakeGraphCreator.Success(), FakeDvRegistrar.Created());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Diagnostic.Should().Contain("H14b").And.Contain(H14bRejections.NoWebhookTargetsConfigured);

        var phases = repo.LastWrittenRun!.CompletedPhases.Select(cp => cp.Phase).ToList();
        phases.Should().Contain("H14a").And.Contain("H14c").And.NotContain("H14b").And.NotContain("H14");
    }

    // ---------- AC-8 handler-id mismatch ----------

    [Fact]
    public async Task AC8_HandlerIdMismatch_Throws()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-8");
        var handler = BuildHandler(repo, FakeApplier.Applied(2), FakeReader.Success(SigningKey), FakeGraphCreator.Success(), FakeDvRegistrar.Created());
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

    // ---------- AC-9 no S2S 4th sub-step ----------

    [Fact]
    public void AC9_ExpectedSubStepCount_IsExactlyThree_NoS2SFourthSubStep()
    {
        H14IntegrationWiringHandler.ExpectedSubStepCount.Should().Be(3);
        var act = () => H14IntegrationWiringHandler.AssertNoS2SSubStep();
        act.Should().NotThrow();
    }

    // ---------- helpers ----------

    private static H14IntegrationWiringHandler BuildHandler(
        FakeRepository repo, FakeApplier applier, FakeReader reader, FakeGraphCreator graphCreator, FakeDvRegistrar dvRegistrar)
    {
        var options = Options.Create(new IntegrationWiringOptions());
        var h14a = new H14aExchangePolicySubHandler(applier, NullLogger<H14aExchangePolicySubHandler>.Instance);
        var h14b = new H14bGraphWebhookSubHandler(reader, graphCreator, NullLogger<H14bGraphWebhookSubHandler>.Instance);
        var h14c = new H14cDataverseWebhookSubHandler(reader, dvRegistrar, options, NullLogger<H14cDataverseWebhookSubHandler>.Instance);
        return new H14IntegrationWiringHandler(repo, h14a, h14b, h14c, options, NullLogger<H14IntegrationWiringHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H14IntegrationWiringHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static ProvisioningRun BuildRun(bool includeTenantId = true, bool includeGraphResources = true)
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
            run.Parameters.NonSecret[H14IntegrationWiringHandler.TenantIdParameterKey] = TenantId;
        }
        run.Parameters.NonSecret[H14IntegrationWiringHandler.KeyVaultNameParameterKey] = KeyVaultName;
        run.Parameters.NonSecret[H14IntegrationWiringHandler.SubscriptionIdParameterKey] = SubscriptionId;
        run.Parameters.NonSecret[H14IntegrationWiringHandler.ExchangePolicyScopeGroupIdParameterKey] = PolicyScopeGroupId;
        run.Parameters.NonSecret[H14IntegrationWiringHandler.WebhookNotificationBaseUrlParameterKey] = NotificationBaseUrl;
        if (includeGraphResources)
        {
            run.Parameters.NonSecret[H14IntegrationWiringHandler.CommunicationGraphResourceParameterKey] = CommunicationResource;
            run.Parameters.NonSecret[H14IntegrationWiringHandler.EmailGraphResourceParameterKey] = EmailResource;
        }
        run.InterStepState.BffAppRegId = BffAppRegId;
        run.InterStepState.MiClientId = UamiClientId;
        run.InterStepState.DataverseEnvUrl = DataverseEnvUrl;
        return run;
    }

    /// <summary>Repository fake — records last written run + replace call count.</summary>
    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private ProvisioningRun? _run;
        private string? _etag;
        public ProvisioningRun? LastWrittenRun { get; private set; }
        public int ReplaceCallCount { get; private set; }

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
            ReplaceCallCount++;
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }

    private sealed class FakeApplier : IExchangePolicyApplier
    {
        private readonly ExchangePolicyApplyOutcome _outcome;
        public int CallCount { get; private set; }
        private FakeApplier(ExchangePolicyApplyOutcome outcome) => _outcome = outcome;

        public static FakeApplier Applied(int createdCount)
            => new(new ExchangePolicyApplyOutcome.Applied(createdCount, new[] { BffAppRegId, UamiClientId }));

        public static FakeApplier Drift(IReadOnlyList<string> observed)
            => new(new ExchangePolicyApplyOutcome.Drift(new[] { BffAppRegId, UamiClientId }, observed));

        public Task<ExchangePolicyApplyOutcome> ApplyAsync(ExchangePolicyApplyRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_outcome);
        }
    }

    private sealed class FakeReader : IKvSecretReader
    {
        private readonly KvSecretReadResult _result;
        private FakeReader(KvSecretReadResult result) => _result = result;
        public static FakeReader Success(string value) => new(new KvSecretReadResult.Success(value));

        public Task<KvSecretReadResult> ReadSecretAsync(string vaultName, string subscriptionId, string secretName, CancellationToken ct)
            => Task.FromResult(_result);
    }

    private sealed class FakeGraphCreator : IGraphSubscriptionCreator
    {
        public int CallCount { get; private set; }
        public static FakeGraphCreator Success() => new();

        public Task<GraphSubscriptionOutcome> CreateOrUpdateAsync(GraphSubscriptionRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult<GraphSubscriptionOutcome>(new GraphSubscriptionOutcome.Created($"sub-{request.ModuleName}"));
        }
    }

    private sealed class FakeDvRegistrar : IServiceEndpointWebhookRegistrar
    {
        public int CallCount { get; private set; }
        public static FakeDvRegistrar Created() => new();

        public Task<ServiceEndpointWebhookOutcome> RegisterAsync(ServiceEndpointWebhookRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult<ServiceEndpointWebhookOutcome>(new ServiceEndpointWebhookOutcome.Created("se-1"));
        }
    }
}
