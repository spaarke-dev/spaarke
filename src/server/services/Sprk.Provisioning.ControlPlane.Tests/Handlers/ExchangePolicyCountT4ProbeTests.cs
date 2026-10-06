// -----------------------------------------------------------------------------
// ExchangePolicyCountT4ProbeTests.cs
//
// Unit tests over ExchangePolicyCountT4Probe (H13 T4; RBAC for Applications since task 251).
// The probe reads the stamp identity's Exchange role assignments through IExchangePolicyReadClient
// (fake here) and verifies every Exchange-scoped mailbox role (from the REAL
// L2GraphAppRolesRegistry) is held within the customer's group and none outside it.
//
// COVERAGE: Passed; not registered; a role missing; a role granted outside the group;
// read failure / throwing client -> InfraFault; missing inputs -> InfraFault without a read.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ExchangePolicyCountT4ProbeTests
{
    private const string RunId = "01j7q3zp-h13-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string UamiClientId = "11111111-2222-3333-4444-555555555555";
    private const string ScopeGroupId = "77777777-8888-9999-0000-111111111111";

    private static readonly string[] MailboxRoles =
        { "Application Mail.Read", "Application Mail.ReadWrite", "Application Mail.Send", "Application MailboxSettings.Read" };

    [Fact]
    public async Task EveryRoleInScope_NothingOutside_Passes_AndReadsTheRightApp()
    {
        var fake = new FakeReadClient { Outcome = Registered(MailboxRoles.Select(InScope).ToArray()) };

        var outcome = await BuildProbe(fake).ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Passed>().Which.Kind.Should().Be(TrapKind.T4ExchangePolicyCount);
        fake.LastRequest!.AppId.Should().Be(UamiClientId);
        fake.LastRequest.ScopeGroupId.Should().Be(ScopeGroupId);
        fake.LastRequest.CorrelationId.Should().Be(RunId);
        fake.LastRequest.Roles.Should().BeEquivalentTo(MailboxRoles);
    }

    [Fact]
    public async Task NotRegisteredInExchange_Fails()
    {
        var fake = new FakeReadClient { Outcome = new ExchangePolicyReadOutcome.Success(false, Array.Empty<ExchangeRoleAssignmentView>()) };

        var outcome = await BuildProbe(fake).ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Which.Diagnostic.Should().Contain("not registered in Exchange");
    }

    [Fact]
    public async Task RoleMissing_Fails_NamingIt()
    {
        var fake = new FakeReadClient { Outcome = Registered(MailboxRoles.Skip(1).Select(InScope).ToArray()) };

        var outcome = await BuildProbe(fake).ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Which.Diagnostic.Should().Contain("Missing in scope: Application Mail.Read");
    }

    [Fact]
    public async Task RoleGrantedOutsideTheGroup_Fails_EvenWhenEveryRoleIsAlsoInScope()
    {
        var assignments = MailboxRoles.Select(InScope)
            .Append(new ExchangeRoleAssignmentView("manual-grant", "Application Mail.Read", "", InExpectedScope: false)).ToArray();
        var fake = new FakeReadClient { Outcome = Registered(assignments) };

        var outcome = await BuildProbe(fake).ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.Failed>().Which.Diagnostic
            .Should().Contain("OUTSIDE the customer's group").And.Contain("manual-grant").And.Contain("organization-wide");
    }

    [Fact]
    public async Task ReadFailure_IsInfraFault()
    {
        var fake = new FakeReadClient { Outcome = new ExchangePolicyReadOutcome.Failure("sidecar 503") };

        var outcome = await BuildProbe(fake).ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Which.Diagnostic.Should().Contain("sidecar 503");
    }

    [Fact]
    public async Task ThrowingClient_IsInfraFault()
    {
        var fake = new FakeReadClient { Throw = new InvalidOperationException("boom") };

        var outcome = await BuildProbe(fake).ProbeAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>().Which.Diagnostic.Should().Contain("boom");
    }

    [Theory]
    [InlineData("", UamiClientId, ScopeGroupId)]
    [InlineData(TenantId, "", ScopeGroupId)]
    [InlineData(TenantId, UamiClientId, "")]
    public async Task MissingInput_IsInfraFault_WithoutARead(string tenantId, string uamiClientId, string scopeGroupId)
    {
        var fake = new FakeReadClient { Outcome = Registered(MailboxRoles.Select(InScope).ToArray()) };

        var outcome = await BuildProbe(fake).ProbeAsync(BuildRequest(tenantId, uamiClientId, scopeGroupId), CancellationToken.None);

        outcome.Should().BeOfType<TrapVerificationOutcome.InfraFault>();
        fake.CallCount.Should().Be(0);
    }

    // ---------- helpers ----------

    private static TrapVerificationRequest BuildRequest(string tenantId = TenantId, string uamiClientId = UamiClientId, string scopeGroupId = ScopeGroupId) => new(
        CustomerId: "acme",
        RunId: RunId,
        TenantId: tenantId,
        SubscriptionId: "22222222-3333-4444-5555-666666666666",
        DataverseUrl: "https://sprk-acme.crm.dynamics.com",
        BffAppRegId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        UamiClientId: uamiClientId,
        KeyVaultName: "sprk-acme-prod-kv",
        AppServiceName: "sprk-acme-prod-bff",
        ResourceGroupName: "rg-spaarke-acme-prod",
        ExchangeScopeGroupId: scopeGroupId);

    private static ExchangePolicyCountT4Probe BuildProbe(IExchangePolicyReadClient client)
        => new(client, new L2GraphAppRolesRegistry(), NullLogger<ExchangePolicyCountT4Probe>.Instance);

    private static ExchangeRoleAssignmentView InScope(string role)
        => new($"Spaarke-acme-{role.Replace("Application ", "").Replace(".", "")}", role, "CN=acme-scope", InExpectedScope: true);

    private static ExchangePolicyReadOutcome.Success Registered(ExchangeRoleAssignmentView[] assignments) => new(true, assignments);

    private sealed class FakeReadClient : IExchangePolicyReadClient
    {
        public ExchangePolicyReadOutcome? Outcome { get; init; }
        public Exception? Throw { get; init; }
        public int CallCount { get; private set; }
        public ExchangePolicyReadRequest? LastRequest { get; private set; }

        public Task<ExchangePolicyReadOutcome> ReadAsync(ExchangePolicyReadRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Throw is null ? Task.FromResult(Outcome!) : throw Throw;
        }
    }
}
