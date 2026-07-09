using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract tests for the FR-P3-04 Daily Briefing coded-composite endpoints —
/// <c>POST /api/ai/daily-briefing/email</c> (NEW at task 043) and the re-pointed
/// <c>POST /api/ai/daily-briefing/render</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosting approach</b> (mirrors <see cref="SummarizeSessionEndpointContractTests"/>):
/// a minimal in-process <see cref="WebApplication"/> maps
/// <see cref="DailyBriefingEndpoints.MapDailyBriefingEndpoints"/> with a fake bearer
/// scheme, a no-op <c>ai-batch</c> rate-limit policy, a stub
/// <see cref="DailyBriefingCompositeService"/> (the module boundary — its own dispatch
/// internals are covered by <c>DailyBriefingCompositeServiceTests</c>), and a stubbed
/// Dataverse systemuser lookup.
/// </para>
/// <para>
/// <b>Coverage</b> (tests/CLAUDE.md "every new endpoint → ≥1 integration test"):
/// /email 200 happy path with recipient resolved from claims + acting systemuserid
/// forwarded; /email 401 unauthenticated; /email 400 when no email-shaped claim exists;
/// /email 503 when dispatch is unconfigured (no Binding row); /render 200 via the
/// composite. Route registration is implied by every 2xx/4xx (a missing route would 404).
/// </para>
/// </remarks>
public class DailyBriefingEmailEndpointContractTests : IClassFixture<DailyBriefingEndpointTestFixture>
{
    private readonly DailyBriefingEndpointTestFixture _fx;

    public DailyBriefingEmailEndpointContractTests(DailyBriefingEndpointTestFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task PostEmail_NoBody_Returns200_AndDeliversToCallerFromClaims()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        // No body (the scheduled trigger's shape) → recipient defaults to the caller's claim.
        var response = await client.PostAsync("/api/ai/daily-briefing/email", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fx.Composite.EmailCalls.Should().ContainSingle();
        var call = _fx.Composite.EmailCalls[0];
        call.RecipientEmail.Should().Be(DailyBriefingEndpointTestFixture.UserEmail,
            "with no body-supplied recipient, delivery defaults to the ACTING USER resolved from token claims");
        call.SystemUserId.Should().Be(DailyBriefingEndpointTestFixture.SystemUserId,
            "the acting Dataverse identity resolves from the oid claim via the systemuser lookup");
    }

    [Fact]
    public async Task PostEmail_WithInternalColleagueRecipient_Returns200_AndDeliversCallersBriefingToColleague()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsync(
            "/api/ai/daily-briefing/email", JsonBody($$"""{"recipientEmail":"{{DailyBriefingEndpointTestFixture.ColleagueEmail}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fx.Composite.EmailCalls.Should().ContainSingle();
        var call = _fx.Composite.EmailCalls[0];
        call.RecipientEmail.Should().Be(DailyBriefingEndpointTestFixture.ColleagueEmail,
            "the colleague-share path delivers to the body-supplied internal recipient");
        call.SystemUserId.Should().Be(DailyBriefingEndpointTestFixture.SystemUserId,
            "the briefing content is the CALLER's own — systemuserid stays token-derived, never body-supplied (no data-source spoofing)");
    }

    [Fact]
    public async Task PostEmail_WithExternalRecipient_Returns400_WithoutDispatch()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        // A well-formed address that is NOT an active internal systemuser — the egress guard blocks it.
        var response = await client.PostAsync(
            "/api/ai/daily-briefing/email", JsonBody("""{"recipientEmail":"stranger@external-domain.com"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the briefing may carry confidential data — it may only be forwarded to an internal user (egress guard)");
        _fx.Composite.EmailCalls.Should().BeEmpty("an external recipient must never trigger a send");
    }

    [Fact]
    public async Task PostEmail_WithMalformedRecipient_Returns400_WithoutDispatch()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsync(
            "/api/ai/daily-briefing/email", JsonBody("""{"recipientEmail":"not-an-email"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fx.Composite.EmailCalls.Should().BeEmpty("a malformed recipient is rejected before dispatch");
    }

    private static StringContent JsonBody(string json) =>
        new(json, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task PostEmail_Unauthenticated_Returns401()
    {
        _fx.Reset();
        var client = _fx.CreateUnauthenticatedClient();

        var response = await client.PostAsync("/api/ai/daily-briefing/email", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fx.Composite.EmailCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task PostEmail_NoEmailClaim_Returns400_WithoutDispatch()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient(includeEmailClaims: false);

        var response = await client.PostAsync("/api/ai/daily-briefing/email", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fx.Composite.EmailCalls.Should().BeEmpty("no resolvable recipient → nothing may dispatch or send");
    }

    [Fact]
    public async Task PostEmail_DispatchUnconfigured_Returns503ProblemDetails()
    {
        _fx.Reset();
        _fx.Composite.ThrowOnEmail =
            new DailyBriefingDispatchUnconfiguredException("no Binding row for daily-briefing-narrate/email");
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsync("/api/ai/daily-briefing/email", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "an unconfigured Binding is an admin error surfaced as 503 — never a silent fallback (NFR-08)");
        (await response.Content.ReadAsStringAsync()).Should().Contain("daily-briefing-narrate");
    }

    [Fact]
    public async Task PostRender_HappyPath_Returns200ViaCompositeDispatch()
    {
        _fx.Reset();
        var client = _fx.CreateAuthenticatedClient();

        var response = await client.PostAsync("/api/ai/daily-briefing/render", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fx.Composite.RenderCalls.Should().ContainSingle()
            .Which.Should().Be(DailyBriefingEndpointTestFixture.SystemUserId,
                "the render leg dispatches through the coded composite with the caller's systemuserid");
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"tldr\"",
            "the widget-consumable camelCase contract is preserved (AC-12b)");
    }
}

/// <summary>
/// Fixture hosting a minimal WebApplication with ONLY the daily-briefing endpoint group.
/// </summary>
public sealed class DailyBriefingEndpointTestFixture : IAsyncLifetime, IDisposable
{
    public const string UserEmail = "briefing.user@contoso.com";
    public const string ColleagueEmail = "colleague@contoso.com";
    public static readonly Guid SystemUserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    public static readonly Guid AadOid = Guid.Parse("12345678-1234-1234-1234-123456789012");

    /// <summary>
    /// Emails the egress guard treats as active internal users (mirrors the systemuser table).
    /// Seeded with the caller + one colleague; a recipient NOT in this set resolves to empty and
    /// the endpoint rejects it as external. Reset re-seeds between tests.
    /// </summary>
    public HashSet<string> KnownInternalEmails { get; } =
        new(StringComparer.OrdinalIgnoreCase) { UserEmail, ColleagueEmail };

    public StubComposite Composite { get; } = new();

    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();

        builder.Services
            .AddSingleton(new BriefingFakeAuthOptions())
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = BriefingFakeAuthHandler.SchemeName;
                o.DefaultChallengeScheme = BriefingFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, BriefingFakeAuthHandler>(
                BriefingFakeAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        // The group declares RequireRateLimiting("ai-batch") — register a no-op policy.
        builder.Services.AddRateLimiter(opt =>
        {
            opt.AddPolicy("ai-batch", _ =>
                System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ai-batch-test"));
        });

        // Module boundary: the stub composite (endpoints inject the concrete type; the stub
        // is a subclass over the protected logger-only ctor — the ADR-032 Null-peer shape).
        builder.Services.AddSingleton<DailyBriefingCompositeService>(Composite);

        // The sibling /summarize route in the same group injects IBriefingAi? — parameter
        // inference requires a registration (prod always registers a real or Null impl).
        builder.Services.AddSingleton(Mock.Of<Sprk.Bff.Api.Services.Ai.PublicContracts.IBriefingAi>());

        // Dataverse systemuser lookups: (a) oid claim → systemuserid (caller identity), and
        // (b) internalemailaddress → active-user existence (r5 colleague-share egress guard).
        // The mock branches on the FetchXML so the egress guard can MISS for an unknown/external
        // recipient while the oid lookup still resolves the caller.
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FetchExpression fe, CancellationToken _) =>
            {
                var q = fe.Query ?? string.Empty;
                if (q.Contains("internalemailaddress", StringComparison.OrdinalIgnoreCase))
                {
                    // Egress-guard lookup: match only KNOWN internal emails (mirrors the UI's
                    // systemuser picker). Unknown/external → empty → endpoint returns 400.
                    var isKnownInternal = KnownInternalEmails.Any(e =>
                        q.Contains(e, StringComparison.OrdinalIgnoreCase));
                    return isKnownInternal
                        ? new EntityCollection(new List<Entity> { new("systemuser") { Id = Guid.NewGuid() } })
                        : new EntityCollection(new List<Entity>());
                }

                // oid → systemuserid (caller identity) lookup.
                var user = new Entity("systemuser") { Id = SystemUserId };
                user["systemuserid"] = SystemUserId;
                return new EntityCollection(new List<Entity> { user });
            });
        builder.Services.AddSingleton(entityService.Object);

        builder.WebHost.UseTestServer();
        _app = builder.Build();

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapDailyBriefingEndpoints();

        await _app.StartAsync();
    }

    public Task DisposeAsync() => _app?.StopAsync() ?? Task.CompletedTask;

    public void Dispose() => _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public void Reset()
    {
        Composite.RenderCalls.Clear();
        Composite.EmailCalls.Clear();
        Composite.ThrowOnEmail = null;
        KnownInternalEmails.Clear();
        KnownInternalEmails.Add(UserEmail);
        KnownInternalEmails.Add(ColleagueEmail);
    }

    public HttpClient CreateAuthenticatedClient(bool includeEmailClaims = true)
    {
        _app!.Services.GetRequiredService<BriefingFakeAuthOptions>().IncludeEmailClaims = includeEmailClaims;
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");
        return client;
    }

    public HttpClient CreateUnauthenticatedClient()
    {
        _app!.Services.GetRequiredService<BriefingFakeAuthOptions>().IncludeEmailClaims = true;
        return _app.GetTestClient(); // no Authorization header → challenge → 401
    }

    /// <summary>Recording stub over the composite's protected-ctor + virtual production shape.</summary>
    public sealed class StubComposite : DailyBriefingCompositeService
    {
        public StubComposite() : base(NullLogger<DailyBriefingCompositeService>.Instance)
        {
        }

        public List<Guid> RenderCalls { get; } = new();
        public List<(Guid SystemUserId, string TenantId, string RecipientEmail)> EmailCalls { get; } = new();
        public Exception? ThrowOnEmail { get; set; }

        public override Task<DailyBriefingNarrateResponse> RenderAsync(
            Guid systemUserId, string tenantId, CancellationToken cancellationToken)
        {
            RenderCalls.Add(systemUserId);
            return Task.FromResult(BuildResponse());
        }

        public override Task<DailyBriefingNarrateResponse> EmailAsync(
            Guid systemUserId, string tenantId, string recipientEmail, CancellationToken cancellationToken)
        {
            if (ThrowOnEmail is not null)
            {
                throw ThrowOnEmail;
            }
            EmailCalls.Add((systemUserId, tenantId, recipientEmail));
            return Task.FromResult(BuildResponse());
        }

        private static DailyBriefingNarrateResponse BuildResponse() => new()
        {
            Tldr = new TldrResult { Summary = "contract summary", KeyTakeaways = ["k"], TopAction = "a" },
            ChannelNarratives = [],
            GeneratedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}

public sealed class BriefingFakeAuthOptions
{
    public bool IncludeEmailClaims { get; set; } = true;
}

/// <summary>Fake bearer scheme: succeeds on any Authorization header; claims per options.</summary>
public sealed class BriefingFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "BriefingFakeBearer";

    private readonly BriefingFakeAuthOptions _fakeOptions;

    public BriefingFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        BriefingFakeAuthOptions fakeOptions)
        : base(options, logger, encoder)
    {
        _fakeOptions = fakeOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("oid", DailyBriefingEndpointTestFixture.AadOid.ToString()),
            new("tid", "00000000-0000-0000-0000-000000000abc"),
        };
        if (_fakeOptions.IncludeEmailClaims)
        {
            claims.Add(new Claim("preferred_username", DailyBriefingEndpointTestFixture.UserEmail));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
