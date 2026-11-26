using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// Regression for #2449 (spaarke-SPA-external-access-platform-r4 task 042, FR-27): the profile form showed an email the
/// registration then refused. The workforce 403 <c>registration_required</c> prefilled its <c>email</c> from
/// <see cref="WorkforceCallerClaims.TokenEmail"/>, which falls back to <c>preferred_username</c> and <c>upn</c>; the
/// registration route uses <see cref="ExternalRegistrationEndpoints.TokenEmail"/> (the email claim only, exactly one value,
/// no control characters) and answers 403 <c>registration_email_missing</c> otherwise. A UPN-only sign-in therefore saw its
/// UPN on the form and was refused on Continue.
/// </summary>
/// <remarks>
/// Pinned here: for every claim shape the prefill email IS the email the registration would use, and both are null when
/// the registration would refuse. <see cref="WorkforceCallerClaims.TokenEmail"/> itself is unchanged (principal
/// composition relies on its fallback). The first test runs the claim-shape table through the real
/// <see cref="WorkforcePrincipalStrategy"/>; the last two go through the real host, the real <c>GET /me</c> and the real
/// <c>POST /registration</c> with the same token, for the shapes the header-driven test scheme can express.
/// </remarks>
[Trait("category", "external-access")]
public sealed class Issue2449_RegistrationEmailRuleTests
    : IClassFixture<ExternalRegistrationEndpointsTests.ConfiguredFixture>
{
    private const string Email = "mike.okafor@customer.example";
    private const string Upn = "mike.okafor@customer.onmicrosoft.com";
    private static readonly Guid HomeTenant = Guid.Parse("99999999-eeee-4eee-8eee-999999999999");

    private readonly ExternalRegistrationEndpointsTests.ConfiguredFixture _fixture;

    public Issue2449_RegistrationEmailRuleTests(ExternalRegistrationEndpointsTests.ConfiguredFixture fixture)
    {
        _fixture = fixture;
        _fixture.Store.Reset();
        _fixture.Store.DefaultTeams[ExternalRegistrationEndpointsTests.BusinessUnit] =
            new List<Guid> { ExternalRegistrationEndpointsTests.OwnerTeam };
        _fixture.Identity.Reset();
    }

    /// <summary>
    /// The claim-shape table: a name, the token's address claims as <c>type=value</c>, and the email the registration uses
    /// (null = it refuses <c>registration_email_missing</c>).
    /// </summary>
    public static TheoryData<string, string[], string?> ClaimShapes() => new()
    {
        { "email only", new[] { $"email={Email}" }, Email },
        { "preferred_username only", new[] { $"preferred_username={Upn}" }, null },
        { "upn only", new[] { $"upn={Upn}" }, null },
        { "two different email values", new[] { $"email={Email}", "email=other@customer.example" }, null },
        { "an email with a control character", new[] { "email=mike\u0007okafor@customer.example" }, null },
        // The original report's shape: a real email and a different UPN. The form shows the email, never the UPN.
        { "email and a different preferred_username", new[] { $"email={Email}", $"preferred_username={Upn}" }, Email },
    };

    [Theory]
    [MemberData(nameof(ClaimShapes))]
    public async Task RegistrationRequiredPrefill_ForEveryClaimShape_IsTheEmailTheRegistrationUses(
        string shape, string[] addressClaims, string? registrationEmail)
    {
        var home = HomeUser(addressClaims);

        var (status, json) = await PrefillThroughTheStrategyAsync(home);

        status.Should().Be(StatusCodes.Status403Forbidden, shape);
        json.GetProperty("reasonCode").GetString().Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired);
        EmailOf(json).Should().Be(registrationEmail, $"{shape}: the prefill shows exactly the email registration uses");
        ExternalRegistrationEndpoints.TokenEmail(home).Should().Be(
            registrationEmail, $"{shape}: the registration route's rule (its accept/refuse behaviour is unchanged)");
    }

    [Theory]
    [InlineData("preferred_username")]
    [InlineData("upn")]
    public void WorkforceCallerClaimsTokenEmail_KeepsItsUpnFallback_ForItsOtherCallers(string claimType)
    {
        // FR-27 changed only the prefill's email source. Principal composition still reads this fallback.
        WorkforceCallerClaims.TokenEmail(HomeUser(new[] { $"{claimType}={Upn}" })).Should().Be(Upn);
    }

    // ── through the real host: the same token at GET /me and POST /registration ───────────────────────────

    [Fact]
    public async Task MeThenRegistration_EmailClaim_ThePrefilledEmailIsTheOneRegistered()
    {
        var oid = Guid.NewGuid();

        var me = await GetMeAsync(oid, email: Email, preferredUsername: Upn);
        var prefilled = EmailOf(await JsonOf(me));
        var registration = await ExternalRegistrationEndpointsTests.PostAs(
            _fixture, Body, oid, email: Email, preferredUsername: Upn);

        me.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        prefilled.Should().Be(Email, "the form shows the email claim, not the UPN");
        registration.StatusCode.Should().Be(HttpStatusCode.Created);
        var contactId = (await JsonOf(registration)).GetProperty("contactId").GetGuid();
        _fixture.Store.Contacts[contactId].Email.Should().Be(prefilled, "the registered email is the one the form showed");
    }

    [Fact]
    public async Task MeThenRegistration_PreferredUsernameOnly_NoPrefilledEmail_AndTheRegistrationRefusesEmailMissing()
    {
        var oid = Guid.NewGuid();

        var me = await GetMeAsync(oid, email: null, preferredUsername: Upn);
        var meJson = await JsonOf(me);
        var registration = await ExternalRegistrationEndpointsTests.PostAs(
            _fixture, Body, oid, email: null, preferredUsername: Upn);

        me.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        meJson.GetProperty("reasonCode").GetString().Should().Be(WorkforcePrincipalResolver.DenyRegistrationRequired);
        EmailOf(meJson).Should().BeNull("before #2449 this was the UPN, which the registration then refused");
        meJson.GetRawText().Should().NotContain(Upn, "the UPN is never shown as an email");
        await ExternalRegistrationEndpointsTests.ShouldBeProblem(
            registration, HttpStatusCode.Forbidden, ExternalRegistrationEndpoints.ReasonEmailMissing);
        _fixture.Store.Writes.Should().BeEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private const string Body = """{"firstName":"Mike","lastName":"Okafor"}""";

    /// <summary>The A9 home-token shape (customer tid, acct = 0, a user token, the WorkforceHome scheme) plus the address claims.</summary>
    private static ClaimsPrincipal HomeUser(string[] addressClaims)
    {
        var claims = new List<Claim>
        {
            new("scp", "user_impersonation"), new("sub", "pairwise"), new("acct", "0"),
            new("oid", Guid.NewGuid().ToString("D")), new("tid", HomeTenant.ToString("D")),
            new("given_name", "Mike"), new("family_name", "Okafor"),
        };
        claims.AddRange(addressClaims.Select(c => c.Split('=', 2)).Select(p => new Claim(p[0], p[1])));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.WorkforceHome));
    }

    /// <summary>The prefill as the real strategy builds it for a first sign-in (the resolver answers registration_required).</summary>
    private static async Task<(int Status, JsonElement Json)> PrefillThroughTheStrategyAsync(ClaimsPrincipal user)
    {
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, WorkforcePrincipalResolver.DenyRegistrationRequired));
        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object,
            new Mock<IAccessibleRecordSetService>(MockBehavior.Strict).Object,
            NullLogger<WorkforcePrincipalStrategy>.Instance,
            new InMemoryContactIdentityStore());
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

    /// <summary>The prefill's email: null when the extension is absent or JSON null.</summary>
    private static string? EmailOf(JsonElement json)
        => json.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String ? email.GetString() : null;

    private async Task<HttpResponseMessage> GetMeAsync(Guid oid, string? email, string? preferredUsername)
    {
        using var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/external/me");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer test");
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.SchemeHeader, AuthSchemes.WorkforceHome);
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.OidHeader, oid.ToString("D"));
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.TidHeader, HomeTenant.ToString("D"));
        if (email is not null) request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.EmailHeader, email);
        if (preferredUsername is not null)
        {
            request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.PreferredUsernameHeader, preferredUsername);
        }

        var response = await client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
