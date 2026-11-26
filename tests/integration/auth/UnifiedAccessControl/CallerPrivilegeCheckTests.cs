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

    // 095a review K2: the probe's tri-state answer maps an unreadable 2xx body to Faulted, so it must tell
    // "a privilege answer that lists no such privilege" (false) from "no privilege answer at all" (null).
    [Theory]
    [InlineData("""{"RolePrivileges":[]}""", false)]
    [InlineData("""{"RolePrivileges":[{"Depth":"Deep","PrivilegeName":"prvReadsprk_Invoice"}]}""", false)]
    [InlineData("""{"RolePrivileges":[{"Depth":"Deep","PrivilegeName":"prvCreatesprk_Invoice"}]}""", true)]
    [InlineData("""{"value":[]}""", null)]
    [InlineData("""{"RolePrivileges":"prvCreatesprk_Invoice"}""", null)]
    [InlineData("<html>proxy</html>", null)]
    [InlineData("", null)]
    public void ReadPrivilegeResponse_AnswersOnlyFromAPrivilegeArray(string body, bool? expected)
    {
        CallerRecordAccessProbe.ReadPrivilegeResponse(body, "prvCreatesprk_Invoice").Should().Be(expected);
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

    private static (IConfiguration Configuration, OrderedCredentialClientProvider Credentials) ConfiguredObo(IClientAssertionProvider assertions)
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

        return (configuration, credentials);
    }

    private static CallerRecordAccessProbe ConfiguredProbe(IClientAssertionProvider assertions)
    {
        var (configuration, credentials) = ConfiguredObo(assertions);

        return new CallerRecordAccessProbe(
            new HttpClient(new NoNetworkHandler()), configuration, NullLogger<CallerRecordAccessProbe>.Instance, credentials);
    }

    // ── The three-state answer (ebilling-r1 task 095a review F1: a fault is not a "no") ──

    private const string Privilege = "prvCreatesprk_Invoice";

    /// <summary>The exchange and WhoAmI seams answered by the test; the privilege read goes through the REAL code and a canned HTTP handler.</summary>
    private sealed class SeamProbe(
        HttpMessageHandler handler, IConfiguration configuration, OrderedCredentialClientProvider credentials, string? dataverseToken, Guid? caller)
        : CallerRecordAccessProbe(new HttpClient(handler), configuration, NullLogger<CallerRecordAccessProbe>.Instance, credentials)
    {
        protected override Task<string?> ExchangeForDataverseTokenAsync(string callerBearerToken, string context, CancellationToken ct) =>
            Task.FromResult(dataverseToken);

        protected override Task<Guid?> ResolveCallerSystemUserIdAsync(string dataverseToken, CancellationToken ct) => Task.FromResult(caller);
    }

    private static SeamProbe Seamed(HttpMessageHandler handler, string? dataverseToken = "dv-token", Guid? caller = null)
    {
        var (configuration, credentials) = ConfiguredObo(new RecordingAssertionProvider());
        return new SeamProbe(handler, configuration, credentials, dataverseToken, caller ?? Guid.Parse("095a0000-0000-4000-8000-0000000000c1"));
    }

    private sealed class CannedHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    private static HttpResponseMessage Json(System.Net.HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task PrivilegeAnswer_ListedInTheResponse_IsHeld()
    {
        var probe = Seamed(new CannedHandler(() => Json(System.Net.HttpStatusCode.OK, Held)));

        (await probe.CallerPrivilegeAnswerAsync("caller-token", Privilege)).Should().Be(CallerPrivilegeAnswer.Held);
        (await probe.CallerHoldsPrivilegeAsync("caller-token", Privilege)).Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"RolePrivileges":[]}""")]
    [InlineData("""{"RolePrivileges":[{"Depth":"Deep","PrivilegeName":"prvReadsprk_Invoice"}]}""")]
    public async Task PrivilegeAnswer_DataverseAnsweredWithoutThePrivilege_IsNotHeld_NotAFault(string body)
    {
        var probe = Seamed(new CannedHandler(() => Json(System.Net.HttpStatusCode.OK, body)));

        (await probe.CallerPrivilegeAnswerAsync("caller-token", Privilege)).Should().Be(CallerPrivilegeAnswer.NotHeld);
        (await probe.CallerHoldsPrivilegeAsync("caller-token", Privilege)).Should().BeFalse();
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.InternalServerError)]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests)]
    [InlineData(System.Net.HttpStatusCode.Unauthorized)]
    public async Task PrivilegeAnswer_ANonSuccessResponse_IsFaulted_AndTheBoolQuestionStaysFalse(System.Net.HttpStatusCode status)
    {
        var probe = Seamed(new CannedHandler(() => Json(status, "{}")));

        (await probe.CallerPrivilegeAnswerAsync("caller-token", Privilege)).Should().Be(CallerPrivilegeAnswer.Faulted);
        (await probe.CallerHoldsPrivilegeAsync("caller-token", Privilege)).Should().BeFalse("the bool question maps a fault to false, as before");
    }

    [Fact]
    public async Task PrivilegeAnswer_TheHttpCallThrows_IsFaulted()
    {
        var probe = Seamed(new ThrowingHandler(new HttpRequestException("connection reset")));

        (await probe.CallerPrivilegeAnswerAsync("caller-token", Privilege)).Should().Be(CallerPrivilegeAnswer.Faulted);
    }

    [Fact]
    public async Task PrivilegeAnswer_AFailedOboExchange_IsFaulted_WithoutAskingDataverse()
    {
        var probe = Seamed(new NoNetworkHandler(), dataverseToken: null);

        (await probe.CallerPrivilegeAnswerAsync("caller-token", Privilege)).Should().Be(CallerPrivilegeAnswer.Faulted);
    }

    [Fact]
    public async Task PrivilegeAnswer_AnUnresolvedCaller_IsFaulted_WithoutAskingDataverse()
    {
        var (configuration, credentials) = ConfiguredObo(new RecordingAssertionProvider());
        var probe = new SeamProbe(new NoNetworkHandler(), configuration, credentials, "dv-token", caller: null);

        (await probe.CallerPrivilegeAnswerAsync("caller-token", Privilege)).Should().Be(CallerPrivilegeAnswer.Faulted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task PrivilegeAnswer_NoCallerToken_IsFaulted_WithoutAnyExchange(string? token)
    {
        var assertions = new RecordingAssertionProvider();
        var probe = ConfiguredProbe(assertions);

        (await probe.CallerPrivilegeAnswerAsync(token, Privilege)).Should().Be(CallerPrivilegeAnswer.Faulted);
        assertions.MintCount.Should().Be(0);
    }

    [Fact]
    public async Task PrivilegeAnswer_OboNotConfigured_IsFaulted()
    {
        var probe = new CallerRecordAccessProbe(
            new HttpClient(new NoNetworkHandler()), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance);

        (await probe.CallerPrivilegeAnswerAsync("caller-token", Privilege)).Should().Be(CallerPrivilegeAnswer.Faulted);
    }

    [Fact]
    public async Task PrivilegeAnswer_ACancelledRequest_StillThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var probe = Seamed(new ThrowingHandler(new OperationCanceledException(cts.Token)));

        var act = () => probe.CallerPrivilegeAnswerAsync("caller-token", Privilege, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class ThrowingHandler(Exception fault) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(fault);
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
