using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
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
/// spaarke-SPA-external-access-platform-r4 task 041 (FR-25, #2192): the workforce plane's 403 <c>registration_required</c>
/// carries a <c>licensedUser</c> routing hint. It is <c>true</c> only when the caller's OWN home-token email (the email claim,
/// exactly one value: the registration route's rule) belongs to an ENABLED licensed user, decided by the same read and the
/// same decision the registration route's <c>licensed_user_link_required</c> refusal uses. The SPA then opens on "Link my
/// account" instead of a profile form that could never register the person.
/// </summary>
/// <remarks>
/// The hint is routing only. These tests pin that it never resolves, links, writes or names anything: the answer stays a
/// 403, nothing is written, and the body carries no field beyond the existing prefill and the boolean. Every doubt (no email,
/// two emails, a UPN only, a failed or throwing read, a disabled user) is <c>false</c>, the behaviour before FR-25.
/// Real <see cref="WorkforcePrincipalResolver"/> (home path) and <see cref="ContactIdentityBinder"/> over the in-memory
/// identity store (the module boundary, as in <c>WorkforceEmailNoHijackTests</c>); the last test goes through the real host,
/// so the strategy the BFF builds from DI is the one that reads the store.
/// </remarks>
[Trait("category", "external-access")]
public sealed class LicensedFirstSignInHintTests
{
    private static readonly Guid HomeTenant = Guid.Parse("99999999-eeee-4eee-8eee-999999999999");
    private static readonly Guid HomeOid = Guid.Parse("13131313-1313-4313-8313-131313131313");
    private const string Email = "mike@customer.example";

    private readonly InMemoryContactIdentityStore _store = new();

    // ── must fire ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegistrationRequired_TokenEmailIsAnEnabledLicensedUser_HintIsTrue_AndNothingIsResolvedOrWritten()
    {
        var licensedUser = Guid.NewGuid();
        _store.AddSystemUser(licensedUser, Guid.NewGuid(), Email);

        var (status, json) = await ResolveThroughTheStrategyAsync(HomeUser(email: Email));

        status.Should().Be(StatusCodes.Status403Forbidden, "the hint is routing only: the caller is still not signed in");
        json.GetProperty("reasonCode").GetString().Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired);
        json.GetProperty("licensedUser").GetBoolean().Should().BeTrue();
        json.GetProperty("email").GetString().Should().Be(Email, "the existing prefill is unchanged");
        _store.Writes.Should().BeEmpty("the hint links, binds and creates nothing");
        json.GetRawText().Should().NotContain(licensedUser.ToString("D"), "the licensed user is never named");
    }

    [Fact]
    public async Task RegistrationRequired_TheSameEmailAsBothClaimTypesDifferingInCase_IsOneEmail_HintIsTrue()
    {
        // TokenEmail de-duplicates `email` and ClaimTypes.Email case-insensitively: one mailbox, not "two emails".
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email);
        var user = HomeUser(email: "Mike@Customer.example");
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(ClaimTypes.Email, Email));

        var (_, json) = await ResolveThroughTheStrategyAsync(user);

        json.GetProperty("licensedUser").GetBoolean().Should().BeTrue();
    }

    // ── must not fire ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("no-systemuser")]
    [InlineData("disabled-systemuser")]
    [InlineData("another-email")]
    [InlineData("read-failed")]
    [InlineData("read-throws")]
    public async Task RegistrationRequired_TokenEmailNotProvablyALicensedUser_HintIsFalse(string world)
    {
        switch (world)
        {
            case "disabled-systemuser": _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email, disabled: true); break;
            case "another-email": _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "someone.else@customer.example"); break;
            case "read-failed":
                _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email);
                _store.FailLicensedUserLookup = true;
                break;
            case "read-throws":
                _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email);
                _store.AfterRead = path =>
                {
                    if (path == "licensed-email") throw new HttpRequestException("Dataverse unreachable");
                };
                break;
        }

        var (status, json) = await ResolveThroughTheStrategyAsync(HomeUser(email: Email));

        status.Should().Be(StatusCodes.Status403Forbidden);
        json.GetProperty("reasonCode").GetString().Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired);
        json.GetProperty("licensedUser").GetBoolean().Should().BeFalse("a doubt shows the form, as before FR-25");
        _store.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("upn-only")]
    [InlineData("two-emails")]
    public async Task RegistrationRequired_NoSingleEmailClaim_HintIsFalse_AndTheLicensedUserReadNeverRuns(string shape)
    {
        // The registration route's email rule: the email claim only, exactly one value. A UPN is not a verified mailbox.
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email);
        var user = shape == "upn-only"
            ? HomeUser(email: null, preferredUsername: Email)
            : HomeUser(email: Email, secondEmail: "other@customer.example");

        var (_, json) = await ResolveThroughTheStrategyAsync(user);

        json.GetProperty("licensedUser").GetBoolean().Should().BeFalse();
        _store.Reads.Should().NotContain("licensed-email");
    }

    [Fact]
    public async Task OtherWorkforceDenies_CarryNoHint_AndNeverReadTheLicensedUser()
    {
        // Only registration_required carries the hint: a link-blocked or ambiguous caller is never asked "are you licensed?".
        _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email);
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, WorkforcePrincipalResolver.DenySystemUserDisabled));

        var (status, json) = await ResolveThroughTheStrategyAsync(HomeUser(email: Email), resolver.Object);

        status.Should().Be(StatusCodes.Status403Forbidden);
        json.TryGetProperty("licensedUser", out _).Should().BeFalse();
        _store.Reads.Should().NotContain("licensed-email");
    }

    // ── through the real host: the DI-built strategy reads the registered store ──────────────────

    [Fact]
    public async Task MeOnTheWorkforcePlane_RegistrationRequiredForALicensedEmail_AnswersTheHintThroughTheHost()
    {
        using var fixture = new ExternalAccessContractFixture();
        fixture.IdentityStore.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), Email);
        using var host = fixture.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWorkforcePrincipalResolver>();
            services.AddSingleton<IWorkforcePrincipalResolver, RegistrationRequiredResolver>();
            services.AddSingleton<IClaimsTransformation, HeaderEmailClaim>();
        }));

        var licensed = await GetMeAsync(host, Email);
        var notLicensed = await GetMeAsync(host, "new.hire@customer.example");

        licensed.Status.Should().Be(HttpStatusCode.Forbidden);
        licensed.Json.GetProperty("reasonCode").GetString().Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired);
        licensed.Json.GetProperty("licensedUser").GetBoolean().Should().BeTrue();
        notLicensed.Status.Should().Be(HttpStatusCode.Forbidden);
        notLicensed.Json.GetProperty("licensedUser").GetBoolean().Should().BeFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The A9 home-token shape: customer tid, acct = 0, a user token, authenticated by the WorkforceHome scheme.</summary>
    private static ClaimsPrincipal HomeUser(string? email, string? preferredUsername = null, string? secondEmail = null)
    {
        var claims = new List<Claim>
        {
            new("scp", "user_impersonation"), new("sub", "pairwise"), new("acct", "0"),
            new("oid", HomeOid.ToString("D")), new("tid", HomeTenant.ToString("D")),
            new("given_name", "Mike"), new("family_name", "Okafor"),
        };
        if (email is not null) claims.Add(new Claim("email", email));
        if (secondEmail is not null) claims.Add(new Claim("email", secondEmail));
        if (preferredUsername is not null) claims.Add(new Claim("preferred_username", preferredUsername));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.WorkforceHome));
    }

    /// <summary>The real home resolver over the real binder and the in-memory store; nothing is linked (no stored link).</summary>
    private WorkforcePrincipalResolver HomeResolver()
    {
        var identity = new Mock<IIdentityNormalizationService>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ContactIdentityBinder.LinkWritesEnabledConfigKey] = "true",
            ["AzureAd:TenantId"] = SpaarkeTenant.ToString("D"),
        }).Build();
        return new WorkforcePrincipalResolver(identity.Object, new Mock<IDataverseService>().Object,
            new Mock<ITenantCache>().Object, Binder(_store), config, NullLogger<WorkforcePrincipalResolver>.Instance);
    }

    private async Task<(int Status, JsonElement Json)> ResolveThroughTheStrategyAsync(
        ClaimsPrincipal user, IWorkforcePrincipalResolver? resolver = null)
    {
        var strategy = new WorkforcePrincipalStrategy(
            resolver ?? HomeResolver(),
            new Mock<IAccessibleRecordSetService>(MockBehavior.Strict).Object,
            NullLogger<WorkforcePrincipalStrategy>.Instance,
            _store);
        var http = new DefaultHttpContext
        {
            User = user,
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        http.Response.Body = new MemoryStream();

        var result = await strategy.ResolveAsync(http, CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        await result.Failure!.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, doc.RootElement.Clone());
    }

    private static async Task<(HttpStatusCode Status, JsonElement Json)> GetMeAsync(
        WebApplicationFactory<Program> host, string email)
    {
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        client.DefaultRequestHeaders.Add("X-Test-Plane", "workforce");
        client.DefaultRequestHeaders.Add("X-Test-Email", email);
        using var response = await client.GetAsync("/api/v1/external/me");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, doc.RootElement.Clone());
    }

    /// <summary>The resolver's first-sign-in answer for every caller (the home path's own tests pin when it is given).</summary>
    private sealed class RegistrationRequiredResolver : IWorkforcePrincipalResolver
    {
        public Task<WorkforcePrincipalResolution> ResolveAsync(ClaimsPrincipal user, CancellationToken ct)
            => Task.FromResult(WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, WorkforcePrincipalResolver.DenyRegistrationRequired));
    }

    /// <summary>The fixture's test token carries no email; this adds the one named by <c>X-Test-Email</c>.</summary>
    private sealed class HeaderEmailClaim : IClaimsTransformation
    {
        private readonly IHttpContextAccessor _accessor;

        public HeaderEmailClaim(IHttpContextAccessor accessor) => _accessor = accessor;

        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var email = _accessor.HttpContext?.Request.Headers["X-Test-Email"].ToString();
            if (!string.IsNullOrEmpty(email) && principal.Identity is ClaimsIdentity identity && !identity.HasClaim(c => c.Type == "email"))
            {
                identity.AddClaim(new Claim("email", email));
            }

            return Task.FromResult(principal);
        }
    }
}
