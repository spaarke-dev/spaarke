// -----------------------------------------------------------------------------
// H14mCustomerMailboxSubHandlerTests.cs
//
// Task 263 (#1562): H14m — the customer's Spaarke-tenant shared mailbox + its verified sprk_communicationaccount row.
// ADR-038 Path #1 — pure unit test over the REAL sub-handler with hand-written fakes for its two seams.
//
// COVERAGE (POML acceptance criteria):
//   AC-1  A run creates the mailbox (name/alias sprk-{customerId}-mail, the intake address and display name, the
//         customer's Spaarke-AppAccess group, H14a's three roles) and one verified account row → Success.
//   AC-2  A second run writes nothing: Exchange already compliant (Created=false), row already Verified (Written=false)
//         → Success with the same idempotency key.
//   AC-3  A same-named mailbox outside the scope group / owned by another customer → Drift → QuarantineRequired,
//         and NO account row is written.
//   AC-4  Mailbox in place but a role not yet in scope → Resumable, no row written.
//   AC-5  Ensure Failure (e.g. PRQ-E-16 not applied) → Resumable, no row written.
//   AC-6  Row Conflict → QuarantineRequired; row VerificationFailed → Resumable (never overwritten); row Failure → Resumable.
//   AC-7  Missing ParametersJson field → Resumable, no seam called.
//   AC-8  The idempotency key changes with the address, the scope group, the identity and the Dataverse URL.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H14mCustomerMailboxSubHandlerTests
{
    private const string CustomerId = "acme";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string ScopeGroupId = "77777777-8888-9999-0000-111111111111";
    private const string Address = "acme@contoso.com";
    private const string DisplayName = "Acme Corporation";
    private const string DataverseUrl = "https://spaarke-acme.crm.dynamics.com";
    private const string BffAppRegId = "33333333-4444-5555-6666-777777777777";

    private readonly FakeCustomerMailboxClient _mailbox = new();
    private readonly FakeCommunicationAccountStore _store = new();

    [Fact]
    public async Task AC1_CreatesTheMailboxAndOneVerifiedRow()
    {
        var result = await Handler().HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>().Which.IdempotencyKey
            .Should().Be(H14mCustomerMailboxSubHandler.ExpectedIdempotencyKey(CustomerId, UamiClientId, ScopeGroupId, Address, DataverseUrl));
        var request = _mailbox.EnsureCalls.Should().ContainSingle().Subject;
        request.Should().BeEquivalentTo(new
        {
            TenantId,
            AppId = UamiClientId,
            ScopeGroupId,
            ExpectedScopeGroupName = "Spaarke-AppAccess-acme",
            Name = "sprk-acme-mail",
            DisplayName,
            PrimarySmtpAddress = Address,
            CorrelationId = "run-1",
        });
        request.Roles.Should().BeEquivalentTo(
            ((IGraphAppRolesRegistry)new L2GraphAppRolesRegistry()).GetExchangeScoped().Select(r => IGraphAppRolesRegistry.ToExchangeApplicationRole(r.Value)),
            "the mailbox must be reachable by exactly H14a's roles");
        var (target, spec) = _store.EnsureCalls.Should().ContainSingle().Subject;
        target.Should().Be(new CommunicationAccountTarget(DataverseUrl, TenantId, BffAppRegId));
        spec.EmailAddress.Should().Be(Address);
        spec.Name.Should().Be(DisplayName);
        spec.SecurityGroupId.Should().Be(ScopeGroupId);
        spec.VerificationMessage.Should().Contain("Test-ServicePrincipalAuthorization");
    }

    [Fact]
    public async Task AC2_SecondRunWritesNothing_SameKey()
    {
        _mailbox.EnsureOutcome = FakeCustomerMailboxClient.Ensured(created: false);
        _store.EnsureOutcome = new CommunicationAccountEnsureOutcome.Ready(FakeCommunicationAccountStore.AccountId, Written: false);

        var first = await Handler().HandleAsync(Envelope(), CancellationToken.None);
        var second = await Handler().HandleAsync(Envelope(), CancellationToken.None);

        ((HandlerResult.Success)second).IdempotencyKey.Should().Be(((HandlerResult.Success)first).IdempotencyKey);
    }

    [Theory]
    [InlineData("Mailbox 'sprk-acme-mail' exists (created 2026-10-10 UTC) but is not a member of the scope group 'Spaarke-AppAccess-acme'.")]
    [InlineData("Recipient 'sprk-other-mail' has Name 'sprk-other-mail', expected 'sprk-acme-mail' — not this customer's mailbox.")]
    [InlineData("Scope group '77777777-8888-9999-0000-111111111111' is named 'Spaarke-AppAccess-other', expected 'Spaarke-AppAccess-acme'.")]
    public async Task AC3_ForeignOrOutOfScopeMailbox_Quarantines_NoRowWritten(string conflict)
    {
        _mailbox.EnsureOutcome = new CustomerMailboxEnsureOutcome.Drift(new[] { conflict });

        var failure = await HandleExpectingFailure();

        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H14mRejections.MailboxDrift);
        failure.Diagnostic.Should().Contain(conflict).And.Contain("Nothing was created or changed");
        _store.EnsureCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AC4_RoleNotYetInScope_Resumable_NoRowWritten()
    {
        _mailbox.EnsureOutcome = FakeCustomerMailboxClient.Ensured(created: true, verified: false);

        var failure = await HandleExpectingFailure();

        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H14mRejections.MailboxUnverified);
        _store.EnsureCalls.Should().BeEmpty("a row is only written Verified");
    }

    [Fact]
    public async Task AC5_EnsureFailure_Resumable_NoRowWritten()
    {
        _mailbox.EnsureOutcome = new CustomerMailboxEnsureOutcome.Failure("Spaarke Exchange Admin cannot run New-Mailbox -Shared — prerequisite PRQ-E-16 (owner decision D32) is not applied.");

        var failure = await HandleExpectingFailure();

        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H14mRejections.MailboxEnsureFailed);
        failure.Diagnostic.Should().Contain("PRQ-E-16");
        _store.EnsureCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AC5b_MailboxClientThrows_IsResumableNotAnEscape()
    {
        var handler = new H14mCustomerMailboxSubHandler(new ThrowingMailboxClient(), _store, new L2GraphAppRolesRegistry(),
            NullLogger<H14mCustomerMailboxSubHandler>.Instance);

        var result = await handler.HandleAsync(Envelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Failure>().Which.RejectionCode.Should().Be(H14mRejections.MailboxEnsureFailed);
    }

    [Fact]
    public async Task AC6_RowConflict_Quarantines()
    {
        _store.EnsureOutcome = new CommunicationAccountEnsureOutcome.Conflict("2 sprk_communicationaccounts rows carry 'acme@contoso.com'.");

        var failure = await HandleExpectingFailure();

        failure.Class.Should().Be(FailureClass.QuarantineRequired);
        failure.RejectionCode.Should().Be(H14mRejections.AccountRowConflict);
    }

    [Fact]
    public async Task AC6b_RowVerificationFailed_ResumableAndNotOverwritten()
    {
        _store.EnsureOutcome = new CommunicationAccountEnsureOutcome.VerificationFailed(FakeCommunicationAccountStore.AccountId, "Send test failed: ErrorAccessDenied");

        var failure = await HandleExpectingFailure();

        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H14mRejections.AccountVerificationFailed);
        failure.Diagnostic.Should().Contain("ErrorAccessDenied").And.Contain("never overwrites");
    }

    [Fact]
    public async Task AC6c_RowFailure_Resumable()
    {
        _store.EnsureOutcome = new CommunicationAccountEnsureOutcome.Failure("HTTP 503");

        (await HandleExpectingFailure()).RejectionCode.Should().Be(H14mRejections.AccountRowFailed);
    }

    [Fact]
    public async Task AC7_MissingParameter_Resumable_NoSeamCalled()
    {
        var envelope = Envelope() with
        {
            ParametersJson = H14mCustomerMailboxSubHandler.BuildParametersJson(TenantId, UamiClientId, ScopeGroupId, DisplayName, "", DataverseUrl, BffAppRegId),
        };

        var result = await Handler().HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H14mRejections.InvalidParameters);
        failure.Diagnostic.Should().Contain("mailboxAddress");
        _mailbox.EnsureCalls.Should().BeEmpty();
    }

    [Fact]
    public void AC8_KeyChangesWithEveryInputThatChangesTheWork()
    {
        var baseKey = H14mCustomerMailboxSubHandler.ExpectedIdempotencyKey(CustomerId, UamiClientId, ScopeGroupId, Address, DataverseUrl);

        baseKey.Should().StartWith("h14-acme-mailbox-");
        new[]
        {
            H14mCustomerMailboxSubHandler.ExpectedIdempotencyKey(CustomerId, UamiClientId, ScopeGroupId, "other@contoso.com", DataverseUrl),
            H14mCustomerMailboxSubHandler.ExpectedIdempotencyKey(CustomerId, UamiClientId, Guid.NewGuid().ToString(), Address, DataverseUrl),
            H14mCustomerMailboxSubHandler.ExpectedIdempotencyKey(CustomerId, Guid.NewGuid().ToString(), ScopeGroupId, Address, DataverseUrl),
            H14mCustomerMailboxSubHandler.ExpectedIdempotencyKey(CustomerId, UamiClientId, ScopeGroupId, Address, "https://spaarke-acme2.crm.dynamics.com"),
        }.Should().NotContain(baseKey);
        H14mCustomerMailboxSubHandler.ExpectedIdempotencyKey(CustomerId, UamiClientId.ToUpperInvariant(), ScopeGroupId, "ACME@contoso.com", DataverseUrl + "/")
            .Should().Be(baseKey, "case and a trailing slash are the same work");
    }

    [Fact]
    public void Naming_IsDerivedFromCustomerId_AndRejectsAnInvalidOne()
    {
        CustomerMailboxNaming.MailboxName("acme").Should().Be("sprk-acme-mail");
        CustomerMailboxNaming.ScopeGroupName("acme").Should().Be("Spaarke-AppAccess-acme");
        var act = () => CustomerMailboxNaming.MailboxName("Acme Corp");
        act.Should().Throw<ArgumentException>();
    }

    private H14mCustomerMailboxSubHandler Handler()
        => new(_mailbox, _store, new L2GraphAppRolesRegistry(), NullLogger<H14mCustomerMailboxSubHandler>.Instance);

    private static HandlerEnvelope Envelope() => new()
    {
        HandlerId = H14mCustomerMailboxSubHandler.HandlerIdentifier,
        RunId = "run-1",
        CustomerId = CustomerId,
        ParametersJson = H14mCustomerMailboxSubHandler.BuildParametersJson(TenantId, UamiClientId, ScopeGroupId, DisplayName, Address, DataverseUrl, BffAppRegId),
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private async Task<HandlerResult.Failure> HandleExpectingFailure()
        => (await Handler().HandleAsync(Envelope(), CancellationToken.None)).Should().BeOfType<HandlerResult.Failure>().Subject;

    private sealed class ThrowingMailboxClient : ICustomerMailboxClient
    {
        public Task<CustomerMailboxEnsureOutcome> EnsureAsync(CustomerMailboxRequest request, CancellationToken cancellationToken)
            => throw new HttpRequestException("boom");

        public Task<CustomerMailboxReadOutcome> ReadAsync(CustomerMailboxRequest request, CancellationToken cancellationToken)
            => throw new HttpRequestException("boom");
    }
}
