using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.Integration.Auth.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r4 task 043 (FR-28, #2269): when the contact lookup, create or bind FAILS (Dataverse
/// could not answer), the caller-principal resolver answers a retryable 503, not a 403. ADR-028: an indeterminate access
/// result is a retryable 503, never a "no access" answer. Exactly the three codes <c>contact_lookup_failed</c>,
/// <c>contact_create_failed</c> and <c>contact_bind_failed</c> change; every other deny keeps its status, body and detail.
/// </summary>
/// <remarks>
/// The 503 is ProblemDetails with the same deny code as <c>reasonCode</c> (what the 403 carried) and as <c>errorCode</c>, and
/// the fixed <see cref="CallerPrincipalResolution.BinderFaultDetail"/>. The SPA retries it (task 043's SPA half).
/// Planes: the workforce strategy takes the code from <see cref="IWorkforcePrincipalResolver"/> (the module boundary; a real
/// home resolver over the real binder for the lookup case); the CIAM strategy runs the real <see cref="ContactIdentityBinder"/>
/// over the in-memory identity store. The CIAM plane never creates a contact (only creating planes reach CreateByOid), so
/// <c>contact_create_failed</c> cannot arise there; the mapping is the same <see cref="CallerPrincipalResolution.IsBinderFault"/>
/// on both planes. The last two tests go through the real host's <c>GET /me</c>.
/// </remarks>
[Trait("category", "external-access")]
public sealed class BinderFaultStatusTests
{
    private static readonly Guid HomeTenant = Guid.Parse("99999999-eeee-4eee-8eee-999999999999");
    private static readonly Guid CallerOid = Guid.Parse("14141414-1414-4414-8414-141414141414");
    private const string Email = "dana@firm.example";

    private readonly InMemoryContactIdentityStore _store = new();

    public static TheoryData<string> BinderFaultCodes() => new()
    {
        ContactBindingDecision.DenyContactLookupFailed,
        ContactBindingDecision.DenyContactCreateFailed,
        ContactBindingDecision.DenyContactBindFailed,
    };

    // ── must fire: workforce plane ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(BinderFaultCodes))]
    public async Task WorkforceStrategy_ABinderFault_Answers503_WithTheSameCode_AndTheRetryMessage(string code)
    {
        var (status, json) = await WorkforceAnswerAsync(ResolverDenying(code), HomeUser());

        ShouldBeTheBinderFault(status, json, code);
    }

    [Fact]
    public async Task WorkforceStrategy_TheHomeContactLookupFails_TheRealResolverAndBinder_Answer503LookupFailed()
    {
        _store.HomeLinkLookupStatus = LookupStatus.Failed;
        _store.HomeContactLookupStatus = LookupStatus.Failed;

        var (status, json) = await WorkforceAnswerAsync(HomeResolver(), HomeUser());

        ShouldBeTheBinderFault(status, json, ContactBindingDecision.DenyContactLookupFailed);
        _store.Writes.Should().BeEmpty("nothing is created or bound when the lookup cannot be read");
    }

    // ── must not fire: workforce plane ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ContactBindingDecision.DenyContactNotInvited)]
    [InlineData(ContactBindingDecision.DenyContactRemoved)]
    public async Task WorkforceStrategy_NotInvitedOrRemoved_StaysThe403NotInvitedAnswer(string code)
    {
        var (status, json) = await WorkforceAnswerAsync(ResolverDenying(code), HomeUser());

        status.Should().Be(StatusCodes.Status403Forbidden);
        json.GetProperty("reasonCode").GetString().Should().Be(ContactBindingDecision.DenyContactNotInvited);
        json.GetProperty("detail").GetString().Should().Be(WorkforcePrincipalStrategy.WorkforceNotInvitedDetail);
    }

    [Fact]
    public async Task WorkforceStrategy_RegistrationRequired_Stays403()
    {
        var (status, json) = await WorkforceAnswerAsync(
            ResolverDenying(WorkforcePrincipalResolver.DenyRegistrationRequired), HomeUser());

        status.Should().Be(StatusCodes.Status403Forbidden);
        json.GetProperty("reasonCode").GetString().Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired);
    }

    [Fact]
    public async Task WorkforceStrategy_OwnerUnconfigured_Stays500()
    {
        var (status, json) = await WorkforceAnswerAsync(
            ResolverDenying(ContactBindingDecision.DenyOwnerUnconfigured), HomeUser());

        status.Should().Be(StatusCodes.Status500InternalServerError);
        json.GetProperty("reasonCode").GetString().Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
    }

    [Theory]
    [InlineData(WorkforcePrincipalResolver.DenySystemUserDisabled)]
    [InlineData(ContactBindingDecision.DenyContactOidAmbiguous)]
    [InlineData(ContactBindingDecision.DenyContactCreateUnavailable)]
    public async Task WorkforceStrategy_AnyOtherDeny_Stays403WithItsOwnCode(string code)
    {
        // contact_create_unavailable is a missing alternate key (a schema fault an operator must fix), not a transient
        // failure: retrying cannot help, so it is not one of the three.
        var (status, json) = await WorkforceAnswerAsync(ResolverDenying(code), HomeUser());

        status.Should().Be(StatusCodes.Status403Forbidden);
        json.GetProperty("reasonCode").GetString().Should().Be(code);
    }

    // ── CIAM plane (real binder) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CiamStrategy_TheContactLookupFails_Answers503LookupFailed()
    {
        _store.OidLookupStatus = LookupStatus.Failed;

        var (status, json) = await CiamAnswerAsync(inviteCode: null);

        ShouldBeTheBinderFault(status, json, ContactBindingDecision.DenyContactLookupFailed);
    }

    [Fact]
    public async Task CiamStrategy_TheFirstSignInBindFails_Answers503BindFailed()
    {
        var invited = Guid.NewGuid();
        var invite = InviteCode.Create(invited);
        _store.AddContact(invited, email: Email, invitedOn: Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(invited, Email);
        _store.FailBind = true;

        var (status, json) = await CiamAnswerAsync(invite.Code);

        ShouldBeTheBinderFault(status, json, ContactBindingDecision.DenyContactBindFailed);
    }

    [Fact]
    public async Task CiamStrategy_NotInvited_Stays403NotInvited()
    {
        _store.AddContact(Guid.NewGuid(), email: Email);

        var (status, json) = await CiamAnswerAsync(inviteCode: null);

        status.Should().Be(StatusCodes.Status403Forbidden);
        json.GetProperty("reasonCode").GetString().Should().Be(ContactBindingDecision.DenyContactNotInvited);
    }

    [Fact]
    public async Task CiamStrategy_NoContact_Stays403NotFound()
    {
        var (status, json) = await CiamAnswerAsync(inviteCode: null);

        status.Should().Be(StatusCodes.Status403Forbidden);
        json.GetProperty("reasonCode").GetString().Should().Be(ContactBindingDecision.DenyContactNotFound);
    }

    // ── through the real host ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MeOnTheCiamPlane_TheContactLookupFails_Answers503ProblemJsonThroughTheHost()
    {
        using var fixture = new ExternalAccessContractFixture();
        fixture.IdentityStore.OidLookupStatus = LookupStatus.Failed;

        var (status, mediaType, json) = await GetMeAsync(fixture, plane: null);

        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        mediaType.Should().Be("application/problem+json");
        json.GetProperty("reasonCode").GetString().Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        json.GetProperty("errorCode").GetString().Should().Be(ContactBindingDecision.DenyContactLookupFailed);
    }

    [Fact]
    public async Task MeOnTheWorkforcePlane_ABindFault_Answers503ProblemJsonThroughTheHost()
    {
        using var fixture = new ExternalAccessContractFixture();
        using var host = fixture.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWorkforcePrincipalResolver>();
            services.AddSingleton<IWorkforcePrincipalResolver>(ResolverDenying(ContactBindingDecision.DenyContactBindFailed));
        }));

        var (status, mediaType, json) = await GetMeAsync(host, plane: "workforce");

        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        mediaType.Should().Be("application/problem+json");
        json.GetProperty("reasonCode").GetString().Should().Be(ContactBindingDecision.DenyContactBindFailed);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static void ShouldBeTheBinderFault(int status, JsonElement json, string code)
    {
        status.Should().Be(StatusCodes.Status503ServiceUnavailable, $"{code} is a failed lookup/create/bind, not a refusal");
        json.GetProperty("reasonCode").GetString().Should().Be(code, "the same code the 403 carried");
        json.GetProperty("errorCode").GetString().Should().Be(code);
        json.GetProperty("title").GetString().Should().Be("Service Unavailable");
        json.GetProperty("detail").GetString().Should().Be(CallerPrincipalResolution.BinderFaultDetail);
        var raw = json.GetRawText();
        raw.Should().NotContain(CallerOid.ToString("D"), "NFR-04: no oid in the answer");
        raw.Should().NotContain(Email, "NFR-04: no email in the answer");
        raw.Should().NotContain(HomeTenant.ToString("D"), "NFR-04: no tenant in the answer");
    }

    private static IWorkforcePrincipalResolver ResolverDenying(string code)
    {
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.Denied(WorkforceDenyReason.PrincipalNotResolved, code));
        return resolver.Object;
    }

    /// <summary>The A9 home-token shape (customer tid, acct = 0, a user token, the WorkforceHome scheme).</summary>
    private static ClaimsPrincipal HomeUser() => new(new ClaimsIdentity(new List<Claim>
    {
        new("scp", "user_impersonation"), new("sub", "pairwise"), new("acct", "0"),
        new("oid", CallerOid.ToString("D")), new("tid", HomeTenant.ToString("D")),
        new("given_name", "Dana"), new("family_name", "Reyes"), new("email", Email),
    }, AuthSchemes.WorkforceHome));

    /// <summary>The real home resolver over the real binder and the in-memory store (as in LicensedFirstSignInHintTests).</summary>
    private WorkforcePrincipalResolver HomeResolver()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ContactIdentityBinder.LinkWritesEnabledConfigKey] = "true",
            ["AzureAd:TenantId"] = SpaarkeTenant.ToString("D"),
        }).Build();
        return new WorkforcePrincipalResolver(new Mock<IIdentityNormalizationService>().Object,
            new Mock<IDataverseService>().Object, new Mock<ITenantCache>().Object, Binder(_store), config,
            NullLogger<WorkforcePrincipalResolver>.Instance);
    }

    private async Task<(int Status, JsonElement Json)> WorkforceAnswerAsync(
        IWorkforcePrincipalResolver resolver, ClaimsPrincipal user)
    {
        // Strict + no setups: a refused caller never reaches the evaluator.
        var strategy = new WorkforcePrincipalStrategy(resolver,
            new Mock<IAccessibleRecordSetService>(MockBehavior.Strict).Object,
            NullLogger<WorkforcePrincipalStrategy>.Instance, _store);
        return await AnswerAsync(strategy, NewContext(user));
    }

    private async Task<(int Status, JsonElement Json)> CiamAnswerAsync(string? inviteCode)
    {
        var strategy = new CiamContactPrincipalStrategy(Binder(_store),
            new Mock<IAccessibleRecordSetService>(MockBehavior.Strict).Object,
            NullLogger<CiamContactPrincipalStrategy>.Instance);
        var http = NewContext(new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
        {
            new("oid", CallerOid.ToString("D")), new("preferred_username", Email),
        }, "Test")));
        if (inviteCode is not null)
        {
            http.Request.Headers[CiamContactPrincipalStrategy.InviteCodeHeader] = inviteCode;
        }

        return await AnswerAsync(strategy, http);
    }

    private static DefaultHttpContext NewContext(ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext
        {
            User = user,
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static async Task<(int Status, JsonElement Json)> AnswerAsync(ICallerPrincipalStrategy strategy, HttpContext http)
    {
        var result = await strategy.ResolveAsync(http, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        await result.Failure!.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, doc.RootElement.Clone());
    }

    private static async Task<(HttpStatusCode Status, string? MediaType, JsonElement Json)> GetMeAsync(
        WebApplicationFactory<Program> host, string? plane)
    {
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        if (plane is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Plane", plane);
        }

        using var response = await client.GetAsync("/api/v1/external/me");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, doc.RootElement.Clone());
    }
}
