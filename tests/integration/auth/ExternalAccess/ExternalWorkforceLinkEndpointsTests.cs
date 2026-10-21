// spaarke-SPA-external-access-platform-r3 task 048 (2026-10-16) — ADR-028 A9 stored link, POST /api/v1/external/workforce/link.
//
// Through the REAL route, the REAL SpaarkeGuestTokenValidator (over the app's own default-scheme JwtBearerOptions), the
// REAL binder and decision, over the in-memory row store (ADR-038: no HTTP doubles). Two things are test-side:
//   1. The BEARER comes from RegistrationTestAuthHandler, which stamps the identity's AuthenticationType from a header (as
//      the real WorkforceHome / Bearer / Ciam handlers do), so the WorkforceHome-only rule is exercised for real.
//   2. The default scheme's signing metadata is a StaticConfigurationManager over a local RSA key, and its issuer check is
//      the exact Spaarke-tenant issuer, v1 or v2 (the shared test host's AzureAd:TenantId is not a GUID, so Identity Web's
//      AadIssuerValidator cannot be pointed at a test tenant here; NO test runs that validator — the live check is task
//      074). Everything else is the host's real default-scheme configuration: Identity Web's audience validator and the AAD
//      signing-key issuer check (IDX40004) stay in the path, and the audiences are the shape AuthorizationModule leaves in
//      production (AzureAd:Audience = api://{app}, the bare {app} id added by task 077, plus the Copilot audience; set
//      here because the shared host's AzureAd:ClientId is not a GUID, so the 077 merge adds nothing). The validator's A9
//      inputs (AzureAd:TenantId / ClientId) come from an overlay configuration with a GUID tenant.
//   The guest token defaults to a v1 access token (aud api://{app}, iss sts.windows.net). ADR-028 A9 switches the BFF
//   app to requestedAccessTokenVersion 2 (aud = the bare {app} id, iss login.microsoftonline.com/{tid}/v2.0); one case
//   sends that shape through the route.

using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>r3 task 048 — the link route's contract, every refusal, and that the guest token is never logged or echoed.</summary>
public sealed class ExternalWorkforceLinkEndpointsTests
    : IClassFixture<ExternalWorkforceLinkEndpointsTests.LinkFixture>,
      IClassFixture<ExternalWorkforceLinkEndpointsTests.SigningMetadataOutageFixture>
{
    internal const string SpaarkeTenantId = "4a8b0c3e-7d21-4f5a-9e6b-048048048001";
    internal const string HomeTenantId = "4a8b0c3e-7d21-4f5a-9e6b-048048048002";
    internal const string OtherTenantId = "4a8b0c3e-7d21-4f5a-9e6b-048048048003";
    private const string HomeUpn = "mike.okafor@customer.example";

    private static readonly Guid HomeTenant = Guid.Parse(HomeTenantId);
    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "t048-not-entra" };

    private readonly LinkFixture _fixture;
    private readonly InMemoryContactIdentityStore _store;
    private readonly Guid _homeOid = Guid.NewGuid();
    private readonly Guid _guestOid = Guid.NewGuid();
    private readonly Guid _systemUser = Guid.NewGuid();

    private readonly SigningMetadataOutageFixture _outage;

    public ExternalWorkforceLinkEndpointsTests(LinkFixture fixture, SigningMetadataOutageFixture outage)
    {
        _fixture = fixture;
        _outage = outage;
        _store = fixture.Store;
        _store.Reset();
        fixture.Identity.Reset();
        fixture.Logs.Clear();
        outage.Store.Reset();
        outage.Identity.Reset();
    }

    // ── Success ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidHomeBearer_AndValidGuestToken_ForTheSamePerson_Returns200_WritesTheLink_AndResolutionFindsTheSystemUser()
    {
        var user = SeedGuestSystemUser();
        _fixture.Identity.Setup(x => x.ResolveAsync(_systemUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(_systemUser));

        var response = await Post(Body(GuestToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await Json(response);
        json.GetProperty("systemUserId").GetGuid().Should().Be(_systemUser);
        json.GetProperty("outcome").GetString().Should().Be("linked");
        user.HomeTenantId.Should().Be(HomeTenantId);
        user.HomeObjectId.Should().Be(_homeOid.ToString("D"));
        user.HomeTenantIdKey.Should().Be(HomeTenantId, "the key mirrors are written with the pair, in one write");
        user.HomeObjectIdKey.Should().Be(_homeOid.ToString("D"));
        _store.Writes.Select(w => w.Op).Should().Equal("home-link");

        // The 046 resolution of the same home identity now returns the systemuser.
        var resolver = _fixture.Services.GetRequiredService<IWorkforcePrincipalResolver>();
        var resolution = await resolver.ResolveAsync(HomePrincipal(), CancellationToken.None);
        resolution.IsResolved.Should().BeTrue(resolution.DenyCode);
        resolution.Principal!.SystemUserId.Should().Be(_systemUser);
    }

    [Fact]
    public async Task RepeatCall_Returns200AlreadyLinked_AndWritesNothing()
    {
        SeedGuestSystemUser();
        (await Post(Body(GuestToken()))).StatusCode.Should().Be(HttpStatusCode.OK);
        _store.Writes.Clear();

        var response = await Post(Body(GuestToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("outcome").GetString().Should().Be("already_linked");
        _store.Writes.Should().BeEmpty();
    }

    // ── Guest-token refusals (403, nothing read or written) ────────────────────────────────────────

    public static TheoryData<string, string> GuestTokenRefusals => new()
    {
        { "expired", GuestTokenDeny.Expired },
        { "bad-signature", GuestTokenDeny.Invalid },
        { "wrong-aud", GuestTokenDeny.Invalid },
        { "copilot-aud", GuestTokenDeny.Invalid },
        // A tid that is not the issuer's tenant fails the default scheme's own AAD signing-key issuer check (IDX40004)
        // before any A9 condition runs; the A9 tid condition itself is covered in SpaarkeGuestTokenValidatorTests.
        { "tid-not-issuer-tenant", GuestTokenDeny.Invalid },
        { "acct-0", GuestTokenDeny.NotGuest },
        { "acct-missing", GuestTokenDeny.NotGuest },
        { "idp-other-tenant", GuestTokenDeny.HomeTenantMismatch },
        { "upn-mismatch", GuestTokenDeny.UpnMismatch },
    };

    [Theory]
    [MemberData(nameof(GuestTokenRefusals))]
    public async Task GuestToken_FailingACheck_Is403WithItsReasonCode_AndWritesNothing(string defect, string reasonCode)
    {
        var user = SeedGuestSystemUser();
        var token = defect switch
        {
            "expired" => GuestToken(expires: DateTime.UtcNow.AddMinutes(-10)),
            "bad-signature" => GuestToken(key: OtherKey),
            "wrong-aud" => GuestToken(audience: Guid.NewGuid().ToString("D")),
            "copilot-aud" => GuestToken(audience: LinkFixture.CopilotAudience), // the default scheme accepts it; A9 does not
            "tid-not-issuer-tenant" => GuestToken(edit: c => c["tid"] = OtherTenantId),
            "acct-0" => GuestToken(edit: c => c["acct"] = 0),
            "acct-missing" => GuestToken(edit: c => c.Remove("acct")),
            "idp-other-tenant" => GuestToken(edit: c => c["idp"] = $"https://sts.windows.net/{OtherTenantId}/"),
            "upn-mismatch" => GuestToken(edit: c => c["upn"] = "someone.else_customer.example#EXT#@spaarke.onmicrosoft.com"),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };

        var response = await Post(Body(token));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, reasonCode);
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().BeEmpty("a refused guest token reads nothing from Dataverse");
        user.HomeObjectId.Should().BeNull();
    }

    // ── Systemuser refusals (403, nothing written) ─────────────────────────────────────────────────

    [Fact]
    public async Task GuestOid_WithNoSystemUser_Is403NoSystemUser_NoWrite()
    {
        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalWorkforceLinkEndpoints.ReasonNoSystemUser);
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task GuestOid_OfADisabledSystemUser_Is403SystemUserDisabled_NoWrite()
    {
        var user = SeedGuestSystemUser(disabled: true);

        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalWorkforceLinkEndpoints.ReasonSystemUserDisabled);
        _store.Writes.Should().BeEmpty();
        user.HomeObjectId.Should().BeNull();
    }

    [Fact]
    public async Task TwoEnabledSystemUsers_OnTheGuestOid_Is403SystemUserAmbiguous_NotRetryable_NoWrite()
    {
        SeedGuestSystemUser();
        _store.AddSystemUser(Guid.NewGuid(), _guestOid, "duplicate@spaarke.example");

        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalWorkforceLinkEndpoints.ReasonSystemUserAmbiguous);
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AConcurrentChangeToTheSystemUser_Is412_ReReadOnce_ThenLinked()
    {
        var user = SeedGuestSystemUser();
        _store.BeforeHomeLinkWrite = u => u.Version += 1000; // another writer touched the row after our read

        var response = await Post(Body(GuestToken()));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Json(response)).GetProperty("outcome").GetString().Should().Be("linked");
        _store.Reads.Count(r => r == "guest-systemuser").Should().Be(2, "the 412 re-reads before writing again");
        _store.Writes.Select(w => w.Op).Should().Equal("home-link");
        user.HomeObjectId.Should().Be(_homeOid.ToString("D"));
    }

    // ── Dataverse faults and an outage (retryable 503, or the environment's 500; nothing written) ────

    [Theory]
    [InlineData(LookupStatus.Failed, HttpStatusCode.ServiceUnavailable, ExternalWorkforceLinkEndpoints.ErrorUnavailable)]
    [InlineData(LookupStatus.ColumnMissing, HttpStatusCode.InternalServerError, ExternalWorkforceLinkEndpoints.ErrorEnvironment)]
    public async Task GuestSystemUserRead_NotReadable_IsItsStatus_AndWritesNothing(
        LookupStatus readStatus, HttpStatusCode expected, string reasonCode)
    {
        var user = SeedGuestSystemUser();
        _store.GuestSystemUserLookupStatus = readStatus;

        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, expected, reasonCode);
        _store.Writes.Should().BeEmpty();
        user.HomeObjectId.Should().BeNull();
    }

    [Theory]
    [InlineData(StoreWriteStatus.Failed, HttpStatusCode.ServiceUnavailable, ExternalWorkforceLinkEndpoints.ErrorUnavailable)]
    [InlineData(StoreWriteStatus.Forbidden, HttpStatusCode.InternalServerError, ExternalWorkforceLinkEndpoints.ErrorEnvironment)]
    [InlineData(StoreWriteStatus.KeyMissing, HttpStatusCode.InternalServerError, ExternalWorkforceLinkEndpoints.ErrorEnvironment)]
    public async Task HomeLinkWrite_Failing_IsRetryable503_OrTheEnvironments500_AndNothingIsLinked(
        StoreWriteStatus writeStatus, HttpStatusCode expected, string reasonCode)
    {
        var user = SeedGuestSystemUser();
        _store.HomeLinkWriteFailure = writeStatus;

        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, expected, reasonCode);
        _store.Writes.Should().BeEmpty();
        user.HomeObjectId.Should().BeNull();
        user.HomeObjectIdKey.Should().BeNull();
    }

    [Fact]
    public async Task TwoConsecutive412s_OnTheWrite_Is503Unavailable_AfterOneReRead_AndNothingIsLinked()
    {
        var user = SeedGuestSystemUser();
        _store.BeforeHomeLinkWrite = u => u.Version += 1000; // another writer touches the row before our first write
        var reads = 0;
        _store.AfterRead = name =>
        {
            // …and again before the second write, after the re-read.
            if (name == "guest-systemuser" && ++reads == 2)
            {
                _store.BeforeHomeLinkWrite = u => u.Version += 1000;
            }
        };

        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, HttpStatusCode.ServiceUnavailable, ExternalWorkforceLinkEndpoints.ErrorUnavailable);
        _store.Reads.Count(r => r == "guest-systemuser").Should().Be(2, "one re-read, then it stops");
        _store.Writes.Should().BeEmpty();
        user.HomeObjectId.Should().BeNull();
    }

    [Fact]
    public async Task SigningMetadata_Unreachable_Is503Unavailable_NotARefusal_AndReadsAndWritesNothing()
    {
        // The default scheme's signing metadata cannot be fetched: an outage the caller retries, not a verdict on the token.
        _outage.Store.AddSystemUser(_systemUser, _guestOid, "mike.okafor_customer.example#EXT#@spaarke.onmicrosoft.com");

        var response = await Post(Body(GuestToken()), host: _outage);

        await ShouldBeProblem(response, HttpStatusCode.ServiceUnavailable, ExternalWorkforceLinkEndpoints.ErrorUnavailable);
        _outage.Store.Reads.Should().BeEmpty();
        _outage.Store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task SystemUser_LinkedToADifferentPair_Is403LinkCollision_AndIsNotRePointed()
    {
        var user = SeedGuestSystemUser();
        var otherOid = Guid.NewGuid().ToString("D");
        user.HomeTenantId = user.HomeTenantIdKey = HomeTenantId;
        user.HomeObjectId = user.HomeObjectIdKey = otherOid;

        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalWorkforceLinkEndpoints.ReasonLinkCollision);
        _store.Writes.Should().BeEmpty();
        user.HomeObjectId.Should().Be(otherOid, "an existing different link is never re-pointed");
    }

    [Fact]
    public async Task Pair_AlreadyLinkedToAnotherSystemUser_Is403LinkCollision_NoWrite()
    {
        var user = SeedGuestSystemUser();
        var holder = _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "holder@spaarke.example");
        holder.HomeTenantId = holder.HomeTenantIdKey = HomeTenantId;
        holder.HomeObjectId = holder.HomeObjectIdKey = _homeOid.ToString("D");

        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalWorkforceLinkEndpoints.ReasonLinkCollision);
        _store.Writes.Should().BeEmpty();
        user.HomeObjectId.Should().BeNull();
    }

    [Fact]
    public async Task Pair_LinkedToAnotherSystemUserConcurrently_IsRefusedByTheKey_AsLinkCollision()
    {
        // The holders read saw nobody; another writer links the same pair before ours lands. The mirrors' alternate key
        // (task 044) is the backstop: the platform refuses our write, and the route answers collision, not a retry.
        var user = SeedGuestSystemUser();
        var racer = _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "racer@spaarke.example");
        _store.BeforeHomeLinkWrite = _ =>
        {
            racer.HomeTenantId = racer.HomeTenantIdKey = HomeTenantId;
            racer.HomeObjectId = racer.HomeObjectIdKey = _homeOid.ToString("D");
        };

        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalWorkforceLinkEndpoints.ReasonLinkCollision);
        user.HomeObjectId.Should().BeNull();
        _store.Writes.Select(w => w.Op).Should().Equal("refused-duplicate-key-home-link");
    }

    [Fact]
    public async Task MaskedHomeColumns_Is500Environment_AndNothingIsReadOrWritten()
    {
        SeedGuestSystemUser();
        var linked = _store.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "linked@spaarke.example");
        linked.HomeTenantId = linked.HomeTenantIdKey = OtherTenantId;
        linked.HomeObjectId = linked.HomeObjectIdKey = Guid.NewGuid().ToString("D");
        _store.MaskHomeColumns = true;

        var response = await Post(Body(GuestToken()));

        await ShouldBeProblem(response, HttpStatusCode.InternalServerError, ExternalWorkforceLinkEndpoints.ErrorEnvironment);
        _store.Writes.Should().BeEmpty();
        _store.Reads.Should().NotContain("guest-systemuser", "a masked pair would read as 'no link' — never decided on");
    }

    // ── Schemes and body ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bearer")]          // a Spaarke-tenant workforce token
    [InlineData(AuthSchemes.Ciam)]  // a CIAM partner token
    public async Task NonHomeBearer_Is403_AndNothingIsReadOrWritten(string scheme)
    {
        SeedGuestSystemUser();

        var response = await Post(Body(GuestToken()), scheme);

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, ExternalWorkforceLinkEndpoints.ReasonHomeIdentityRequired);
        _store.Reads.Should().BeEmpty();
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Anonymous_Is401()
    {
        using var client = _fixture.CreateClient();
        var response = await client.PostAsync(ExternalWorkforceLinkEndpoints.Route,
            new StringContent(Body(GuestToken()), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _store.Reads.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"guestAccessToken":"   "}""")]
    [InlineData("""{"guestAccessToken":"a.b.c","systemUserId":"00000000-0000-0000-0000-000000000001"}""")]
    [InlineData("""{"guestAccessToken":"a.b.c","homeObjectId":"00000000-0000-0000-0000-000000000001"}""")]
    public async Task MissingToken_OrAnyOtherProperty_Is400_NoWrite(string json)
    {
        SeedGuestSystemUser();

        var response = await Post(json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _store.Writes.Should().BeEmpty();
    }

    // ── The guest token is never logged or echoed ──────────────────────────────────────────────────

    [Fact]
    public async Task TheGuestToken_AppearsInNoLogLine_AndNoResponse_OnSuccessOrRefusal()
    {
        SeedGuestSystemUser();
        var good = GuestToken();
        var bad = GuestToken(key: OtherKey);
        var expired = GuestToken(expires: DateTime.UtcNow.AddMinutes(-10));

        var bodies = new List<string>();
        foreach (var token in new[] { bad, expired, good })
        {
            var response = await Post(Body(token));
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        _fixture.Logs.Entries.Should().NotBeEmpty();
        foreach (var token in new[] { good, bad, expired })
        {
            var signature = token[(token.LastIndexOf('.') + 1)..];
            var payload = token.Split('.')[1];
            bodies.Should().NotContain(b => b.Contains(payload) || b.Contains(signature));
            _fixture.Logs.Entries.Should().NotContain(e => e.Contains(payload) || e.Contains(signature),
                "no log line carries the guest token or any part of it");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    private InMemoryContactIdentityStore.SystemUser SeedGuestSystemUser(bool disabled = false)
        => _store.AddSystemUser(_systemUser, _guestOid, "mike.okafor_customer.example#EXT#@spaarke.onmicrosoft.com",
            disabled: disabled);

    [Fact]
    public async Task V2GuestToken_BareAppIdAudience_Returns200Linked()
    {
        // Task 077: after the BFF app switches to requestedAccessTokenVersion 2 the guest token carries aud = the bare
        // app id and the v2 issuer.
        SeedGuestSystemUser();
        _fixture.Identity.Setup(x => x.ResolveAsync(_systemUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(_systemUser));

        var response = await Post(Body(GuestToken(
            c => c["ver"] = "2.0", audience: LinkFixture.BffAppId, issuer: LinkFixture.SpaarkeIssuer)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Json(response)).GetProperty("outcome").GetString().Should().Be("linked");
    }

    private ClaimsPrincipal HomePrincipal()
        => new(new ClaimsIdentity(new[]
        {
            new Claim("scp", "user_impersonation"),
            new Claim("acct", "0"),
            new Claim("oid", _homeOid.ToString("D")),
            new Claim("tid", HomeTenantId),
        }, AuthSchemes.WorkforceHome));

    private string GuestToken(
        Action<Dictionary<string, object>>? edit = null, SecurityKey? key = null, DateTime? expires = null,
        string audience = LinkFixture.BffAudience, string issuer = LinkFixture.SpaarkeIssuerV1)
    {
        var claims = new Dictionary<string, object>
        {
            ["ver"] = "1.0",
            ["tid"] = SpaarkeTenantId,
            ["oid"] = _guestOid.ToString("D"),
            ["sub"] = Guid.NewGuid().ToString("D"),
            ["scp"] = "user_impersonation",
            ["idtyp"] = "user",
            ["acct"] = 1,
            ["idp"] = $"https://sts.windows.net/{HomeTenantId}/",
            ["upn"] = "mike.okafor_customer.example#EXT#@spaarke.onmicrosoft.com",
        };
        edit?.Invoke(claims);

        var exp = expires ?? DateTime.UtcNow.AddMinutes(60);
        var notBefore = exp.AddMinutes(-70);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = notBefore,
            NotBefore = notBefore,
            Expires = exp,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? LinkFixture.EntraKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private static string Body(string token) => JsonSerializer.Serialize(new { guestAccessToken = token });

    private async Task<HttpResponseMessage> Post(string json, string scheme = AuthSchemes.WorkforceHome, LinkFixture? host = null)
    {
        using var client = (host ?? _fixture).CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ExternalWorkforceLinkEndpoints.Route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer test");
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.SchemeHeader, scheme);
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.OidHeader, _homeOid.ToString("D"));
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.TidHeader, HomeTenantId);
        request.Headers.TryAddWithoutValidation(RegistrationTestAuthHandler.PreferredUsernameHeader, HomeUpn.ToUpperInvariant());
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string reasonCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        var root = JsonDocument.Parse(body).RootElement;
        (root.TryGetProperty("reasonCode", out var r) ? r.GetString() : root.GetProperty("errorCode").GetString())
            .Should().Be(reasonCode);
    }

    // ── Host ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The 047 registration host (the ExternalCollaboration policy on <see cref="RegistrationTestAuthHandler"/>, the in-memory
    /// store, the identity seam mocked) plus: the default scheme's signing metadata and issuer pinned to a local key and the
    /// Spaarke-tenant v2 issuer, the validator's A9 inputs from a GUID-tenant overlay, and a log capture.
    /// </summary>
    public class LinkFixture : ExternalRegistrationEndpointsTests.RegistrationFixtureBase
    {
        public const string SpaarkeIssuer = "https://login.microsoftonline.com/" + SpaarkeTenantId + "/v2.0";

        /// <summary>The Spaarke tenant's v1 issuer (a v1 access token's iss).</summary>
        public const string SpaarkeIssuerV1 = "https://sts.windows.net/" + SpaarkeTenantId + "/";

        /// <summary>The stamp BFF app (a GUID, as AzureAd:ClientId is in every environment).</summary>
        public const string BffAppId = "4a8b0c3e-7d21-4f5a-9e6b-048048048a00";

        /// <summary>The audience the guest token carries: the stamp BFF app's identifier URI.</summary>
        public const string BffAudience = "api://" + BffAppId;

        /// <summary>The Copilot audience the default scheme also accepts (AgentToken:CopilotAudience) — never a link.</summary>
        public const string CopilotAudience = "api://botid-4a8b0c3e-7d21-4f5a-9e6b-048048048c00";

        public static readonly RsaSecurityKey EntraKey = new(RSA.Create(2048)) { KeyId = "t048-entra" };

        public LogCapture Logs { get; } = new();

        protected override Guid? CustomerBusinessUnitId => null;

        /// <summary>The default scheme's signing metadata: here a static copy of <paramref name="metadata"/> (local key).</summary>
        protected virtual IConfigurationManager<OpenIdConnectConfiguration> CreateSigningMetadata(OpenIdConnectConfiguration metadata)
            => new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var metadata = new OpenIdConnectConfiguration { Issuer = SpaarkeIssuer, JsonWebKeySet = new JsonWebKeySet() };
                    var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(EntraKey);
                    jwk.AdditionalData["issuer"] = "https://login.microsoftonline.com/{tenantid}/v2.0";
                    metadata.JsonWebKeySet.Keys.Add(jwk);
                    metadata.SigningKeys.Add(EntraKey);
                    options.ConfigurationManager = CreateSigningMetadata(metadata);
                    // The audience shape AuthorizationModule leaves when AgentToken:CopilotAudience is set (the singular
                    // folded into the plural list); Identity Web's own audience validator stays in the path.
                    options.TokenValidationParameters.ValidAudience = null;
                    options.TokenValidationParameters.ValidAudiences = new[] { BffAudience, BffAppId, CopilotAudience };
                    options.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
                        string.Equals(issuer, SpaarkeIssuer, StringComparison.Ordinal)
                        || string.Equals(issuer, SpaarkeIssuerV1, StringComparison.Ordinal)
                            ? issuer
                            : throw new SecurityTokenInvalidIssuerException("Not the Spaarke tenant's issuer.");
                });

                services.RemoveAll<SpaarkeGuestTokenValidator>();
                services.AddSingleton(sp => new SpaarkeGuestTokenValidator(
                    sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(),
                    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AzureAd:TenantId"] = SpaarkeTenantId,
                        ["AzureAd:ClientId"] = BffAppId,
                    }).Build(),
                    sp.GetRequiredService<ILogger<SpaarkeGuestTokenValidator>>()));
            });
        }
    }

    /// <summary>
    /// The same host, except the default scheme's signing metadata cannot be fetched (the metadata endpoint is unreachable):
    /// the real <see cref="ConfigurationManager{T}"/> over a retriever that throws, as in
    /// <c>SpaarkeGuestTokenValidatorTests</c>.
    /// </summary>
    public sealed class SigningMetadataOutageFixture : LinkFixture
    {
        protected override IConfigurationManager<OpenIdConnectConfiguration> CreateSigningMetadata(OpenIdConnectConfiguration metadata)
            => new ConfigurationManager<OpenIdConnectConfiguration>(
                "https://metadata.invalid/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new UnreachableMetadata());

        private sealed class UnreachableMetadata : IDocumentRetriever
        {
            public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
                => throw new IOException("The metadata endpoint is unreachable.");
        }
    }

    /// <summary>Every formatted message and structured property value, as text.</summary>
    public sealed class LogCapture : ILoggerProvider
    {
        private readonly List<string> _entries = new();

        public IReadOnlyList<string> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public void Clear()
        {
            lock (_entries) _entries.Clear();
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(LogCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var text = new StringBuilder(formatter(state, exception));
                if (state is IEnumerable<KeyValuePair<string, object?>> properties)
                {
                    foreach (var p in properties) text.Append(' ').Append(p.Value);
                }

                if (exception is not null) text.Append(' ').Append(exception);
                lock (owner._entries) owner._entries.Add(text.ToString());
            }
        }
    }
}
