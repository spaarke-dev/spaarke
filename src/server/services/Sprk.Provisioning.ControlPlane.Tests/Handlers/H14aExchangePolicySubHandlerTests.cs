// -----------------------------------------------------------------------------
// H14aExchangePolicySubHandlerTests.cs
//
// Unit tests over H14aExchangePolicySubHandler (task 073; RBAC for Applications since task 251).
// T4 silent-fail trap owner.
//
// ADR-038: pure C# unit tests — a fake IExchangePolicyApplier replaces the sidecar; the REAL
// L2GraphAppRolesRegistry supplies the mailbox roles, so these tests pin the production catalog.
//
// COVERAGE:
//   - Happy path: ONE app (the stamp UAMI) is granted exactly the Exchange-scoped mailbox roles,
//     with deterministic names, scoped to the intake group; Success carries the expected key.
//   - T4 drift -> QuarantineRequired with every conflict in the diagnostic.
//   - Applier failure -> Resumable.
//   - Handler-id mismatch throws; missing/malformed parameters -> Resumable, applier never called.
//   - Idempotency key: deterministic; changes with the scope group.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H14aExchangePolicySubHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h14a-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string UamiObjectId = "99999999-8888-7777-6666-555555555555";
    private const string ScopeGroupId = "77777777-8888-9999-0000-111111111111";
    private const string Prefix = "Spaarke";

    [Fact]
    public async Task HappyPath_GrantsTheStampIdentityTheMailboxRoles_ScopedToTheGroup()
    {
        var applier = FakeApplier.Returning(new ExchangePolicyApplyOutcome.Applied(4, new[] { "a", "b", "c", "d" }));

        var result = await BuildHandler(applier).HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>().Which.IdempotencyKey.Should().Be(
            BuildHandler(applier).ExpectedIdempotencyKey(CustomerId, UamiClientId, ScopeGroupId, Prefix));
        var request = applier.LastRequest!;
        request.TenantId.Should().Be(TenantId);
        request.AppId.Should().Be(UamiClientId);
        request.ServicePrincipalObjectId.Should().Be(UamiObjectId);
        request.ScopeGroupId.Should().Be(ScopeGroupId);
        request.CorrelationId.Should().Be(RunId);
        request.Assignments.Should().BeEquivalentTo(new[]
        {
            new ExchangeRoleAssignmentSpec("Spaarke-acme-MailRead", "Application Mail.Read"),
            new ExchangeRoleAssignmentSpec("Spaarke-acme-MailReadWrite", "Application Mail.ReadWrite"),
            new ExchangeRoleAssignmentSpec("Spaarke-acme-MailSend", "Application Mail.Send"),
        }, "task 261: MailboxSettings.Read has no caller, so H14a no longer grants it");
    }

    [Fact]
    public async Task Drift_FailsQuarantineRequired_ListingEveryConflict()
    {
        var applier = FakeApplier.Returning(new ExchangePolicyApplyOutcome.Drift(new[] { "conflict-one.", "conflict-two." }));

        var result = await BuildHandler(applier).HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H14aRejections.TrapT4Drift);
        failure.Diagnostic.Should().Contain("conflict-one.").And.Contain("conflict-two.");
    }

    [Fact]
    public async Task ApplierFailure_FailsResumable()
    {
        var applier = FakeApplier.Returning(new ExchangePolicyApplyOutcome.Failure("EXO throttled: 429"));

        var result = await BuildHandler(applier).HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H14aRejections.ApplyFailed);
        failure.Diagnostic.Should().Contain("EXO throttled");
    }

    [Fact]
    public async Task HandlerIdMismatch_Throws()
    {
        var envelope = BuildEnvelope() with { HandlerId = "H0" };

        var act = async () => await BuildHandler(FakeApplier.Returning(null!)).HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mismatched HandlerId*");
    }

    [Theory]
    [InlineData("", UamiClientId, UamiObjectId, ScopeGroupId, H14aRejections.ApplyFailed)]
    [InlineData(TenantId, "", UamiObjectId, ScopeGroupId, H14aRejections.ApplyFailed)]
    [InlineData(TenantId, UamiClientId, "", ScopeGroupId, H14aRejections.ApplyFailed)]
    [InlineData(TenantId, UamiClientId, UamiObjectId, "", H14aRejections.MissingPolicyScopeGroupId)]
    public async Task MissingParameter_FailsResumable_ApplierNeverCalled(
        string tenantId, string uamiClientId, string uamiObjectId, string scopeGroupId, string expectedCode)
    {
        var applier = FakeApplier.Returning(null!);
        var envelope = BuildEnvelope(H14aExchangePolicySubHandler.BuildParametersJson(tenantId, uamiClientId, uamiObjectId, scopeGroupId, Prefix));

        var result = await BuildHandler(applier).HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(expectedCode);
        applier.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MalformedParametersJson_FailsResumable_ApplierNeverCalled()
    {
        var applier = FakeApplier.Returning(null!);

        var result = await BuildHandler(applier).HandleAsync(BuildEnvelope("{not-valid-json"), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.Class.Should().Be(FailureClass.Resumable);
        applier.CallCount.Should().Be(0);
    }

    [Fact]
    public void IdempotencyKey_IsDeterministic_AndChangesWithTheScopeGroup()
    {
        var handler = BuildHandler(FakeApplier.Returning(null!));

        var k1 = handler.ExpectedIdempotencyKey(CustomerId, UamiClientId, ScopeGroupId, Prefix);

        k1.Should().Be(handler.ExpectedIdempotencyKey(CustomerId, UamiClientId.ToUpperInvariant(), ScopeGroupId, Prefix));
        k1.Should().StartWith($"h14-{CustomerId}-exchange-");
        handler.ExpectedIdempotencyKey(CustomerId, UamiClientId, "00000000-0000-0000-0000-000000000001", Prefix).Should().NotBe(k1);
    }

    // ---------- helpers ----------

    private static H14aExchangePolicySubHandler BuildHandler(FakeApplier applier)
        => new(applier, new L2GraphAppRolesRegistry(), NullLogger<H14aExchangePolicySubHandler>.Instance);

    private static HandlerEnvelope BuildEnvelope(string? parametersJson = null) => new()
    {
        HandlerId = H14aExchangePolicySubHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = parametersJson ?? H14aExchangePolicySubHandler.BuildParametersJson(TenantId, UamiClientId, UamiObjectId, ScopeGroupId, Prefix),
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private sealed class FakeApplier : IExchangePolicyApplier
    {
        private readonly ExchangePolicyApplyOutcome _outcome;
        public int CallCount { get; private set; }
        public ExchangePolicyApplyRequest? LastRequest { get; private set; }

        private FakeApplier(ExchangePolicyApplyOutcome outcome) => _outcome = outcome;

        public static FakeApplier Returning(ExchangePolicyApplyOutcome outcome) => new(outcome);

        public Task<ExchangePolicyApplyOutcome> ApplyAsync(ExchangePolicyApplyRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }
}
