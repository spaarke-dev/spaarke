// -----------------------------------------------------------------------------
// CustomerMailboxVerifierTests.cs
//
// Task 263: H13's read-only customer-mailbox check. Passed only when the shared mailbox exists with no conflict,
// Exchange authorizes every stamp mail role on it, and the stamp has exactly one active, shared, Verified row.
// Missing / foreign / out-of-scope / unverified → Failed (H13 quarantines); no input or an unreachable seam →
// Inconclusive (H13 Resumable). Never writes (the fakes record every call).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class CustomerMailboxVerifierTests
{
    private readonly FakeCustomerMailboxClient _mailbox = new();
    private readonly FakeCommunicationAccountStore _store = new();

    [Fact]
    public async Task MailboxInScope_AndOneVerifiedRow_Passes_WithoutWriting()
    {
        var outcome = await Verifier().VerifyAsync(Request(), CancellationToken.None);

        outcome.Should().BeOfType<CustomerMailboxVerificationOutcome.Passed>();
        var read = _mailbox.ReadCalls.Should().ContainSingle().Subject;
        read.Name.Should().Be("sprk-acme-mail");
        read.ExpectedScopeGroupName.Should().Be("Spaarke-AppAccess-acme");
        read.PrimarySmtpAddress.Should().Be("acme@contoso.com");
        _mailbox.EnsureCalls.Should().BeEmpty("the verifier must not alter what it verifies");
        _store.EnsureCalls.Should().BeEmpty();
        _store.ReadCalls.Should().ContainSingle().Which.Target.Should().Be(
            new CommunicationAccountTarget("https://spaarke-acme.crm.dynamics.com", "tenant", "bff-app"));
    }

    [Fact]
    public async Task MissingMailbox_Fails()
    {
        _mailbox.ReadOutcome = new CustomerMailboxReadOutcome.Success(false, Array.Empty<string>(), Array.Empty<CustomerMailboxAuthorization>(), "none");

        var failed = await ExpectFailed();

        failed.Problems.Should().ContainMatch("*does not exist*");
    }

    [Fact]
    public async Task ForeignOrOutOfScopeMailbox_Fails_WithTheSidecarsConflicts()
    {
        _mailbox.ReadOutcome = FakeCustomerMailboxClient.InPlace("Mailbox 'sprk-acme-mail' is also a member of: Spaarke-AppAccess-other — another stamp may reach it.");

        var failed = await ExpectFailed();

        failed.Problems.Should().ContainMatch("*Spaarke-AppAccess-other*");
    }

    [Fact]
    public async Task RoleNotInScope_Fails()
    {
        _mailbox.ReadOutcome = new CustomerMailboxReadOutcome.Success(true, Array.Empty<string>(),
            new[] { new CustomerMailboxAuthorization("Application Mail.Read", true) }, "partial");

        var failed = await ExpectFailed();

        failed.Problems.Should().ContainMatch("*Application Mail.Send*");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("two")]
    [InlineData("inactive")]
    [InlineData("unverified")]
    [InlineData("failed")]
    [InlineData("user-type")]
    public async Task RowNotExactlyOneActiveSharedVerified_Fails(string shape)
    {
        var rows = shape switch
        {
            "none" => Array.Empty<CommunicationAccountRow>(),
            "two" => new[] { FakeCommunicationAccountStore.VerifiedRow(), FakeCommunicationAccountStore.VerifiedRow() with { Id = Guid.NewGuid() } },
            "inactive" => new[] { FakeCommunicationAccountStore.VerifiedRow(state: 1) },
            "unverified" => new[] { FakeCommunicationAccountStore.VerifiedRow(status: null) },
            "failed" => new[] { FakeCommunicationAccountStore.VerifiedRow(status: CommunicationAccountValues.Failed) },
            _ => new[] { FakeCommunicationAccountStore.VerifiedRow(type: 100000002) },
        };
        _store.ReadOutcome = new CommunicationAccountReadOutcome.Found(rows);

        (await ExpectFailed()).Problems.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InactiveRowBesideOneActiveVerifiedRow_Passes()
    {
        _store.ReadOutcome = new CommunicationAccountReadOutcome.Found(new[]
        {
            FakeCommunicationAccountStore.VerifiedRow(state: 1) with { Id = Guid.NewGuid() },
            FakeCommunicationAccountStore.VerifiedRow(),
        });

        (await Verifier().VerifyAsync(Request(), CancellationToken.None)).Should().BeOfType<CustomerMailboxVerificationOutcome.Passed>();
    }

    [Fact]
    public async Task MissingInput_IsInconclusive_NoCall()
    {
        var outcome = await Verifier().VerifyAsync(Request() with { MailboxAddress = "" }, CancellationToken.None);

        outcome.Should().BeOfType<CustomerMailboxVerificationOutcome.Inconclusive>().Which.Diagnostic.Should().Contain("communicationDefaultMailbox");
        _mailbox.ReadCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task SidecarOrDataverseUnreachable_IsInconclusive()
    {
        _mailbox.ReadOutcome = new CustomerMailboxReadOutcome.Failure("timeout");
        (await Verifier().VerifyAsync(Request(), CancellationToken.None)).Should().BeOfType<CustomerMailboxVerificationOutcome.Inconclusive>();

        _mailbox.ReadOutcome = FakeCustomerMailboxClient.InPlace();
        _store.ReadOutcome = new CommunicationAccountReadOutcome.Failure("HTTP 503");
        (await Verifier().VerifyAsync(Request(), CancellationToken.None)).Should().BeOfType<CustomerMailboxVerificationOutcome.Inconclusive>();
    }

    private CustomerMailboxVerifier Verifier()
        => new(_mailbox, _store, new L2GraphAppRolesRegistry(), NullLogger<CustomerMailboxVerifier>.Instance);

    private static CustomerMailboxVerificationRequest Request() => new(
        CustomerId: "acme", RunId: "run-1", TenantId: "tenant", UamiClientId: "uami", ScopeGroupId: "group",
        DisplayName: "Acme Corporation", MailboxAddress: "acme@contoso.com",
        DataverseUrl: "https://spaarke-acme.crm.dynamics.com", BffAppRegId: "bff-app");

    private async Task<CustomerMailboxVerificationOutcome.Failed> ExpectFailed()
        => (await Verifier().VerifyAsync(Request(), CancellationToken.None)).Should().BeOfType<CustomerMailboxVerificationOutcome.Failed>().Subject;
}
