// -----------------------------------------------------------------------------
// H14IntegrationWiringHandlerTests.cs
//
// Unit tests over H14IntegrationWiringHandler -- the H14 parent that dispatches
// the in-process H14a Exchange sub-step, then H14m the customer mailbox (task 073 --
// wave C4 Batch 3F; H14b/H14c and their tests removed under ISS-019 / #1560;
// H14m added by task 263).
//
// ADR-038 CATEGORY: Path #1 -- pure C# unit test. Constructs the REAL H14a
// sub-handler (a sealed concrete type) wired with a FAKE applier seam -- this
// exercises the parent's dispatch + idempotency + failure classification
// through the real sub-handler code.
//
// COVERAGE:
//   AC-1  Happy path -- H14a then H14m succeed; CompletedPhases gains H14a + H14m + H14; Success.
//   AC-2  Missing tenantId -- Resumable, BEFORE the applier is invoked.
//   AC-3  Missing InterStepState.miObjectId -- Resumable, applier never invoked.
//   AC-3e H14 no longer requires BffApiUrl / keyVaultName / subscriptionId /
//         Graph resources (only H14b/H14c used them). dataverseEnvUrl is required
//         again since task 263 (H14m writes the account row there).
//   AC-4  H14a drifts -- QuarantineRequired; neither H14a nor H14 is recorded; H14m not attempted.
//   AC-5  Full idempotency -- CompletedPhases already has "H14" -- Success
//         no-op; repository ReplaceRunAsync never called.
//   AC-6  Resume -- H14a's expected key already recorded -- applier NEVER invoked.
//   AC-8  Handler-id mismatch -- throws.
//   AC-9  ExpectedSubStepCount invariant is exactly 2 (no S2S sub-step).
//   AC-10 H14m drifts (foreign / out-of-scope mailbox) -- QuarantineRequired; H14a recorded, H14m + H14 not.
//   AC-11 H14m inputs missing (communicationDefaultMailbox, displayName, bffAppRegId, dataverseEnvUrl) -- Resumable, no seam called.
//   AC-12 Resume -- H14a and H14m both recorded -- neither seam invoked; parent recorded.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
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
    private const string UamiObjectId = "99999999-8888-7777-6666-555555555555";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string PolicyScopeGroupId = "77777777-8888-9999-0000-111111111111";
    private const string MailboxAddress = "acme@contoso.com";
    private const string DisplayName = "Acme Corporation";
    private const string DataverseUrl = "https://spaarke-acme.crm.dynamics.com";
    private const string BffAppRegId = "33333333-4444-5555-6666-777777777777";

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_H14aSucceeds_PersistsH14aAndH14()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-1");
        var applier = FakeApplier.Applied(2);
        var handler = BuildHandler(repo, applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.CompletedPhases.Select(cp => cp.Phase).Should().BeEquivalentTo(new[] { "H14a", "H14m", "H14" });
        repo.LastWrittenRun.Status.Should().Be(RunStatus.Running);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H14Gates.ExchangePolicyApplied);
        repo.LastWrittenRun.GateStates.Should().ContainKey(H14Gates.CustomerMailboxVerified);
        applier.CallCount.Should().Be(1);
        _mailbox.EnsureCalls.Should().ContainSingle();
        var request = _mailbox.EnsureCalls[0];
        request.Name.Should().Be("sprk-acme-mail");
        request.ExpectedScopeGroupName.Should().Be("Spaarke-AppAccess-acme");
        request.PrimarySmtpAddress.Should().Be(MailboxAddress, "the address is the intake communicationDefaultMailbox -- one producer");
        request.DisplayName.Should().Be(DisplayName);
        request.AppId.Should().Be(UamiClientId);
        _store.EnsureCalls.Should().ContainSingle().Which.Target.Should().Be(
            new CommunicationAccountTarget(DataverseUrl, TenantId, BffAppRegId));
    }

    // ---------- AC-2 missing tenantId ----------

    [Fact]
    public async Task AC2_MissingTenantId_FailsResumable_NoSeamInvoked()
    {
        var repo = new FakeRepository(BuildRun(includeTenantId: false), etag: "etag-2");
        var applier = FakeApplier.Applied(2);
        var handler = BuildHandler(repo, applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H14Rejections.MissingTenantId);
        applier.CallCount.Should().Be(0);
    }

    // ---------- AC-3 missing InterStepState.miObjectId ----------

    [Fact]
    public async Task AC3_MissingUamiObjectId_FailsResumable()
    {
        var run = BuildRun();
        run.InterStepState.MiObjectId = null;
        var repo = new FakeRepository(run, etag: "etag-3");
        var applier = FakeApplier.Applied(2);
        var handler = BuildHandler(repo, applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H14Rejections.MissingUamiObjectId);
        applier.CallCount.Should().Be(0);
    }

    // ---------- AC-3e H14b/H14c-only inputs are no longer required ----------

    [Fact]
    public async Task AC3e_WebhookOnlyInputs_AreNotRequired()
    {
        // BffApiUrl (H9), keyVaultName (H2a) and subscriptionId were read only to wire the webhooks
        // H14b/H14c registered; neither H14a nor H14m needs them.
        var run = BuildRun();
        run.InterStepState.BffApiUrl = null;
        run.InterStepState.KeyVaultName = null;
        run.Parameters.NonSecret.Remove("subscriptionId");
        var repo = new FakeRepository(run, etag: "etag-3e");
        var applier = FakeApplier.Applied(2);
        var handler = BuildHandler(repo, applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        applier.CallCount.Should().Be(1);
    }

    // ---------- AC-4 H14a drifts ----------

    [Fact]
    public async Task AC4_H14aDrifts_QuarantineRequired_NothingRecordedAsComplete()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-4");
        var applier = FakeApplier.Drift(new[] { "Assignment 'Spaarke-acme-MailSend' is scoped to 'other-group'." });
        var handler = BuildHandler(repo, applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H14Rejections.SubStepFailed);
        failure.Diagnostic.Should().Contain("H14a");

        repo.LastWrittenRun.Should().NotBeNull();
        repo.LastWrittenRun!.CompletedPhases.Select(cp => cp.Phase).Should().NotContain("H14a").And.NotContain("H14");
        repo.LastWrittenRun.Status.Should().Be(RunStatus.Quarantined);
        repo.LastWrittenRun.Quarantine.Should().NotBeNull();
        _mailbox.EnsureCalls.Should().BeEmpty("H14m's authorization test needs H14a's assignments -- it is not attempted");
        _store.EnsureCalls.Should().BeEmpty();
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
        var handler = BuildHandler(repo, applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(expectedKey);
        repo.ReplaceCallCount.Should().Be(0, "full parent-level idempotency short-circuits before any write");
        applier.CallCount.Should().Be(0);
    }

    // ---------- AC-6 resume idempotency ----------

    [Fact]
    public async Task AC6_Resume_H14aAlreadyCompleted_SkipsH14aSeam_RecordsParent()
    {
        var run = BuildRun();
        var expectedH14aKey = new H14aExchangePolicySubHandler(FakeApplier.Applied(0), new L2GraphAppRolesRegistry(), NullLogger<H14aExchangePolicySubHandler>.Instance)
            .ExpectedIdempotencyKey(CustomerId, UamiClientId, PolicyScopeGroupId, new IntegrationWiringOptions().ExchangeAssignmentNamePrefix);
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
        var handler = BuildHandler(repo, applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        applier.CallCount.Should().Be(0, "H14a's expected key already matches a recorded CompletedPhase -- parent skips re-invoking it");

        var phases = repo.LastWrittenRun!.CompletedPhases.Select(cp => cp.Phase).ToList();
        phases.Should().Contain("H14a").And.Contain("H14m").And.Contain("H14");
        phases.Count(p => p == "H14a").Should().Be(1, "the pre-existing H14a entry must not be duplicated");
        _mailbox.EnsureCalls.Should().ContainSingle("H14m was not recorded yet, so it runs");
    }

    // ---------- AC-8 handler-id mismatch ----------

    [Fact]
    public async Task AC8_HandlerIdMismatch_Throws()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-8");
        var handler = BuildHandler(repo, FakeApplier.Applied(2));
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

    // ---------- AC-9 exactly two sub-steps ----------

    [Fact]
    public void AC9_ExpectedSubStepCount_IsExactlyTwo_NoS2SSubStep()
    {
        H14IntegrationWiringHandler.ExpectedSubStepCount.Should().Be(2);
        var act = () => H14IntegrationWiringHandler.AssertNoS2SSubStep();
        act.Should().NotThrow();
    }

    // ---------- AC-10 H14m drifts ----------

    [Fact]
    public async Task AC10_H14mDrifts_QuarantineRequired_H14aRecorded_H14mAndParentNot()
    {
        var repo = new FakeRepository(BuildRun(), etag: "etag-10");
        _mailbox.EnsureOutcome = new CustomerMailboxEnsureOutcome.Drift(new[] { "Mailbox 'sprk-acme-mail' is not a member of the scope group." });
        var handler = BuildHandler(repo, FakeApplier.Applied(0));

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.Diagnostic.Should().Contain("H14m[h14m-mailbox-drift]");
        var phases = repo.LastWrittenRun!.CompletedPhases.Select(cp => cp.Phase).ToList();
        phases.Should().Contain("H14a").And.NotContain("H14m").And.NotContain("H14");
        repo.LastWrittenRun.Status.Should().Be(RunStatus.Quarantined);
        _store.EnsureCalls.Should().BeEmpty("no account row is written for a mailbox that is not this customer's");
    }

    // ---------- AC-11 H14m inputs ----------

    [Theory]
    [InlineData("communicationDefaultMailbox", "h14-missing-mailbox-address")]
    [InlineData("displayName", "h14-missing-display-name")]
    [InlineData("bffAppRegId", "h14-missing-bff-appreg-id")]
    [InlineData("dataverseEnvUrl", "h14-missing-dataverse-env-url")]
    public async Task AC11_MissingH14mInput_FailsResumable_NoSeamInvoked(string input, string expectedCode)
    {
        var run = BuildRun();
        switch (input)
        {
            case "bffAppRegId": run.InterStepState.BffAppRegId = null; break;
            case "dataverseEnvUrl": run.InterStepState.DataverseEnvUrl = null; break;
            default: run.Parameters.NonSecret.Remove(input); break;
        }
        var repo = new FakeRepository(run, etag: "etag-11");
        var applier = FakeApplier.Applied(2);
        var handler = BuildHandler(repo, applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedCode);
        applier.CallCount.Should().Be(0);
        _mailbox.EnsureCalls.Should().BeEmpty();
    }

    // ---------- AC-12 resume with both sub-steps recorded ----------

    [Fact]
    public async Task AC12_Resume_BothSubStepsRecorded_NeitherSeamInvoked_ParentRecorded()
    {
        var run = BuildRun();
        var h14aKey = new H14aExchangePolicySubHandler(FakeApplier.Applied(0), new L2GraphAppRolesRegistry(), NullLogger<H14aExchangePolicySubHandler>.Instance)
            .ExpectedIdempotencyKey(CustomerId, UamiClientId, PolicyScopeGroupId, new IntegrationWiringOptions().ExchangeAssignmentNamePrefix);
        var h14mKey = H14mCustomerMailboxSubHandler.ExpectedIdempotencyKey(CustomerId, UamiClientId, PolicyScopeGroupId, MailboxAddress, DataverseUrl);
        foreach (var (phase, key) in new[] { ("H14a", h14aKey), ("H14m", h14mKey) })
        {
            run.CompletedPhases.Add(new CompletedPhase
            {
                Phase = phase, IdempotencyKey = key, JobId = "prior-run",
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5), CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            });
        }
        var repo = new FakeRepository(run, etag: "etag-12");
        var applier = FakeApplier.Applied(2);
        var handler = BuildHandler(repo, applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        ((HandlerResult.Success)result).IdempotencyKey.Should().Be(H14IntegrationWiringHandler.BuildParentIdempotencyKey(CustomerId, h14aKey, h14mKey));
        applier.CallCount.Should().Be(0);
        _mailbox.EnsureCalls.Should().BeEmpty();
        _store.EnsureCalls.Should().BeEmpty();
    }

    // ---------- helpers ----------

    private readonly FakeCustomerMailboxClient _mailbox = new();
    private readonly FakeCommunicationAccountStore _store = new();

    private H14IntegrationWiringHandler BuildHandler(FakeRepository repo, FakeApplier applier)
    {
        var options = Options.Create(new IntegrationWiringOptions());
        var h14a = new H14aExchangePolicySubHandler(applier, new L2GraphAppRolesRegistry(), NullLogger<H14aExchangePolicySubHandler>.Instance);
        var h14m = new H14mCustomerMailboxSubHandler(_mailbox, _store, new L2GraphAppRolesRegistry(), NullLogger<H14mCustomerMailboxSubHandler>.Instance);
        return new H14IntegrationWiringHandler(repo, h14a, h14m, options, NullLogger<H14IntegrationWiringHandler>.Instance);
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H14IntegrationWiringHandler.HandlerIdentifier,
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
            run.Parameters.NonSecret[H14IntegrationWiringHandler.TenantIdParameterKey] = TenantId;
        }
        run.Parameters.NonSecret["subscriptionId"] = "22222222-3333-4444-5555-666666666666";
        run.Parameters.NonSecret[H14IntegrationWiringHandler.ExchangePolicyScopeGroupIdParameterKey] = PolicyScopeGroupId;
        run.Parameters.NonSecret["communicationDefaultMailbox"] = MailboxAddress;
        run.Parameters.NonSecret["displayName"] = DisplayName;
        run.InterStepState.BffAppRegId = BffAppRegId;
        run.InterStepState.MiClientId = UamiClientId;
        run.InterStepState.MiObjectId = UamiObjectId;
        run.InterStepState.BffApiUrl = "https://sprk-acme-prod.azurewebsites.net";
        run.InterStepState.DataverseEnvUrl = DataverseUrl;
        run.InterStepState.KeyVaultName = "sprk-acme-prod-kv";
        return run;
    }

    /// <summary>Repository fake -- records last written run + replace call count.</summary>
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
            => new(new ExchangePolicyApplyOutcome.Applied(createdCount, new[] { "Spaarke-acme-MailSend" }));

        public static FakeApplier Drift(IReadOnlyList<string> conflicts)
            => new(new ExchangePolicyApplyOutcome.Drift(conflicts));

        public Task<ExchangePolicyApplyOutcome> ApplyAsync(ExchangePolicyApplyRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_outcome);
        }
    }
}
