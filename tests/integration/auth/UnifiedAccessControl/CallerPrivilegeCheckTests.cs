using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.UnifiedAccessControl;

/// <summary>
/// The caller TABLE-privilege check behind invoice confirm (unified-access-control-r2 task 130, owner decision G5:
/// "the BFF checks AS THE USER: Create on sprk_invoice …").
/// </summary>
/// <remarks>
/// <para>The OBO exchange and the HTTP call cannot run in-process; what CAN go wrong silently is reading the
/// answer. <c>RetrieveUserSetOfPrivilegesByNames</c> answers with the privileges the user HOLDS among those asked
/// — an empty array means "not held", and that must never be read as "held". The response shape is the one
/// spaarkedev1 returned to a read-only call on 2026-10-01.</para>
/// </remarks>
public class CallerPrivilegeCheckTests
{
    private const string Held = """
        {"@odata.context":"…#Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNamesResponse",
         "RolePrivileges":[{"Depth":"Deep","PrivilegeId":"da03fe43-b23e-4031-baae-306ea3f59278",
         "BusinessUnitId":"cb15f587-baa0-f111-aaac-000d3a99d1d7","PrivilegeName":"prvCreatesprk_Invoice",
         "RecordFilterId":"00000000-0000-0000-0000-000000000000","RecordFilterUniqueName":""}]}
        """;

    [Fact]
    public void PrivilegeListedInTheResponse_IsHeld()
    {
        CallerRecordAccessProbe.ResponseGrantsPrivilege(Held, "prvCreatesprk_Invoice").Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"RolePrivileges":[]}""")]                                            // asked, not held
    [InlineData("""{"RolePrivileges":[{"Depth":"Deep","PrivilegeName":"prvReadsprk_Invoice"}]}""")] // a different privilege
    [InlineData("""{"value":[]}""")]                                                    // no RolePrivileges array
    [InlineData("""{"RolePrivileges":"prvCreatesprk_Invoice"}""")]                     // not an array
    [InlineData("not json")]
    [InlineData("")]
    public void AnythingElse_IsNotHeld(string body)
    {
        CallerRecordAccessProbe.ResponseGrantsPrivilege(body, "prvCreatesprk_Invoice").Should().BeFalse();
    }

    /// <summary>
    /// The no-token guard, ISOLATED: OBO is fully configured (tenant, client, credential provider, environment URL),
    /// so the token guard is the only condition that can stop the check before an exchange. Whether an exchange was
    /// attempted is observed at the credential provider — every exchange starts by asking it for a client, which
    /// mints an assertion.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoCallerToken_DeniesWithoutAnyExchange(string? token)
    {
        var assertions = new RecordingAssertionProvider();
        var probe = ConfiguredProbe(assertions);

        (await probe.CallerHoldsPrivilegeAsync(token, "prvCreatesprk_Invoice")).Should().BeFalse();

        assertions.MintCount.Should().Be(0, "with no caller token the check must stop before any OBO exchange");
    }

    /// <summary>
    /// Control for the test above: the SAME configuration with a token does reach the exchange (and is then denied
    /// because the stub credential is unusable — no network is ever touched). Without this, a probe that was
    /// accidentally unconfigured would pass the no-token test for the wrong reason.
    /// </summary>
    [Fact]
    public async Task Control_WithACallerToken_TheSameConfigurationReachesTheExchange()
    {
        var assertions = new RecordingAssertionProvider();
        var probe = ConfiguredProbe(assertions);

        (await probe.CallerHoldsPrivilegeAsync("caller-token", "prvCreatesprk_Invoice")).Should().BeFalse();

        assertions.MintCount.Should().BeGreaterThan(0, "the guard let a token through to the exchange");
    }

    private static CallerRecordAccessProbe ConfiguredProbe(IClientAssertionProvider assertions)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["AzureAd:TenantId"] = "00000000-0000-4000-8000-000000000001",
            ["AzureAd:ClientId"] = "00000000-0000-4000-8000-000000000002",
        }).Build();

        var credentials = new OrderedCredentialClientProvider(
            Options.Create(new CredentialSelectionOptions
            {
                Order = new List<string> { nameof(CredentialKind.ManagedIdentityFederated) },
            }),
            configuration,
            NullLogger<OrderedCredentialClientProvider>.Instance,
            assertions);

        return new CallerRecordAccessProbe(
            new HttpClient(new NoNetworkHandler()), configuration, NullLogger<CallerRecordAccessProbe>.Instance, credentials);
    }

    /// <summary>
    /// Records every mint and then fails it the way an unreachable managed identity does, so the provider has no
    /// usable credential and the exchange ends before MSAL could reach the network.
    /// </summary>
    private sealed class RecordingAssertionProvider : IClientAssertionProvider
    {
        private int _mintCount;

        public int MintCount => _mintCount;

        public Task<string> GetAssertionAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _mintCount);
            return Task.FromException<string>(
                new MsalServiceException("managed_identity_unreachable_network", "test stub: no managed identity"));
        }
    }

    /// <summary>Fails the test if anything reaches the network (hand-written, not a mocked handler).</summary>
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No request may be sent by this test.");
    }
}
