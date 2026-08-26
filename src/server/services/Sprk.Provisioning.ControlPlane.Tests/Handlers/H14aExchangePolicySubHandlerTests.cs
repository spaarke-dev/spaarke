// -----------------------------------------------------------------------------
// H14aExchangePolicySubHandlerTests.cs
//
// Unit tests over H14aExchangePolicySubHandler (task 073 — wave C4 Batch 3F).
// T4 silent-fail trap owner.
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live Exchange Online / PS calls. A fake
//   IExchangePolicyApplier replaces the seam so the sub-handler's parameter
//   deserialization + outcome-mapping + idempotency-key-computation logic is
//   exercised in isolation. Live-Exchange coverage belongs to a future
//   env-guarded smoke test (parity with every other H-series live-REST/script
//   collaborator's "NOT under test in the CI unit suite" posture).
//
// COVERAGE:
//   AC-1  Happy path (Applied, createdCount=2) — Success with the deterministic key.
//   AC-2  T4 drift — QuarantineRequired, trap-T4 code, diagnostic cites BOTH expected + observed.
//   AC-3  Applier Failure (infra) — Resumable.
//   AC-4  Handler-id mismatch — throws InvalidOperationException.
//   AC-5  Missing tenantId/bffAppRegId/uamiClientId in ParametersJson — Resumable, applier never called.
//   AC-6  Missing policyScopeGroupId — Resumable, distinct code, applier never called.
//   AC-7  Idempotency key format determinism + order-independence.
//   AC-8  Malformed ParametersJson — Resumable, applier never called.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H14aExchangePolicySubHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h14a-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string PolicyScopeGroupId = "77777777-8888-9999-0000-111111111111";

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_Applied_SucceedsWithDeterministicKey()
    {
        var applier = FakeApplier.Applied(createdCount: 2, new[] { BffAppRegId, UamiClientId });
        var handler = BuildHandler(applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(
            H14aExchangePolicySubHandler.BuildIdempotencyKey(CustomerId, new[] { BffAppRegId, UamiClientId }));
        applier.CallCount.Should().Be(1);
        applier.LastRequest!.ExpectedAppIds.Should().BeEquivalentTo(new[] { BffAppRegId, UamiClientId });
        applier.LastRequest.PolicyScopeGroupId.Should().Be(PolicyScopeGroupId);
    }

    // ---------- AC-2 T4 drift ----------

    [Fact]
    public async Task AC2_T4Drift_FailsQuarantineRequired_DiagnosticCitesBothSets()
    {
        var observed = new[] { BffAppRegId, "ffffffff-0000-0000-0000-000000000000" };
        var applier = FakeApplier.Drift(new[] { BffAppRegId, UamiClientId }, observed);
        var handler = BuildHandler(applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H14aRejections.TrapT4Drift);
        failure.Diagnostic.Should().Contain(BffAppRegId).And.Contain(UamiClientId).And.Contain("ffffffff-0000-0000-0000-000000000000");
    }

    // ---------- AC-3 applier infra failure ----------

    [Fact]
    public async Task AC3_ApplierFailure_FailsResumable()
    {
        var applier = FakeApplier.Failure("EXO throttled: 429");
        var handler = BuildHandler(applier);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H14aRejections.ApplyFailed);
        failure.Diagnostic.Should().Contain("EXO throttled");
    }

    // ---------- AC-4 handler-id mismatch ----------

    [Fact]
    public async Task AC4_HandlerIdMismatch_Throws()
    {
        var handler = BuildHandler(FakeApplier.Applied(0, new[] { BffAppRegId, UamiClientId }));
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

    // ---------- AC-5 missing required fields ----------

    [Theory]
    [InlineData("", BffAppRegId, UamiClientId, PolicyScopeGroupId)]
    [InlineData(TenantId, "", UamiClientId, PolicyScopeGroupId)]
    [InlineData(TenantId, BffAppRegId, "", PolicyScopeGroupId)]
    public async Task AC5_MissingRequiredParameter_FailsResumable_ApplierNeverCalled(
        string tenantId, string bffAppRegId, string uamiClientId, string policyScopeGroupId)
    {
        var applier = FakeApplier.Applied(0, new[] { BffAppRegId, UamiClientId });
        var handler = BuildHandler(applier);
        var envelope = BuildEnvelope(H14aExchangePolicySubHandler.BuildParametersJson(
            tenantId, bffAppRegId, uamiClientId, policyScopeGroupId, "prefix"));

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        applier.CallCount.Should().Be(0);
    }

    // ---------- AC-6 missing policyScopeGroupId ----------

    [Fact]
    public async Task AC6_MissingPolicyScopeGroupId_FailsResumable_DistinctCode()
    {
        var applier = FakeApplier.Applied(0, new[] { BffAppRegId, UamiClientId });
        var handler = BuildHandler(applier);
        var envelope = BuildEnvelope(H14aExchangePolicySubHandler.BuildParametersJson(
            TenantId, BffAppRegId, UamiClientId, "", "prefix"));

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H14aRejections.MissingPolicyScopeGroupId);
        applier.CallCount.Should().Be(0);
    }

    // ---------- AC-7 idempotency key determinism ----------

    [Fact]
    public void AC7_IdempotencyKey_IsDeterministic_OrderIndependent()
    {
        var k1 = H14aExchangePolicySubHandler.BuildIdempotencyKey(CustomerId, new[] { BffAppRegId, UamiClientId });
        var k2 = H14aExchangePolicySubHandler.BuildIdempotencyKey(CustomerId, new[] { UamiClientId, BffAppRegId });
        k1.Should().Be(k2, "the hash sorts inputs before hashing — order of the 2-entry set must not matter");
        k1.Should().StartWith($"h14-{CustomerId}-exchange-");

        var k3 = H14aExchangePolicySubHandler.BuildIdempotencyKey("other-customer", new[] { BffAppRegId, UamiClientId });
        k3.Should().NotBe(k1);
    }

    // ---------- AC-8 malformed ParametersJson ----------

    [Fact]
    public async Task AC8_MalformedParametersJson_FailsResumable_ApplierNeverCalled()
    {
        var applier = FakeApplier.Applied(0, new[] { BffAppRegId, UamiClientId });
        var handler = BuildHandler(applier);
        var envelope = BuildEnvelope("{not-valid-json");

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>();
        applier.CallCount.Should().Be(0);
    }

    // ---------- helpers ----------

    private static H14aExchangePolicySubHandler BuildHandler(FakeApplier applier)
        => new(applier, NullLogger<H14aExchangePolicySubHandler>.Instance);

    private static HandlerEnvelope BuildEnvelope(string? parametersJson = null) => new()
    {
        HandlerId = H14aExchangePolicySubHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = parametersJson ?? H14aExchangePolicySubHandler.BuildParametersJson(
            TenantId, BffAppRegId, UamiClientId, PolicyScopeGroupId, "Spaarke-Provisioning-AppAccessPolicy"),
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private sealed class FakeApplier : IExchangePolicyApplier
    {
        private readonly ExchangePolicyApplyOutcome _outcome;
        public int CallCount { get; private set; }
        public ExchangePolicyApplyRequest? LastRequest { get; private set; }

        private FakeApplier(ExchangePolicyApplyOutcome outcome) => _outcome = outcome;

        public static FakeApplier Applied(int createdCount, IReadOnlyList<string> observedAppIds)
            => new(new ExchangePolicyApplyOutcome.Applied(createdCount, observedAppIds));

        public static FakeApplier Drift(IReadOnlyList<string> expected, IReadOnlyList<string> observed)
            => new(new ExchangePolicyApplyOutcome.Drift(expected, observed));

        public static FakeApplier Failure(string diagnostic) => new(new ExchangePolicyApplyOutcome.Failure(diagnostic));

        public Task<ExchangePolicyApplyOutcome> ApplyAsync(ExchangePolicyApplyRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }
}
