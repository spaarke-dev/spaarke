// spaarke-SPA-external-access-platform-r3 task 048 (2026-10-16) — ADR-028 A9 link proof, the guest half.
//
// The A9 conditions SpaarkeGuestTokenValidator applies AFTER the default scheme's parameters have validated the token
// (DenyReason, pure), the guest home-UPN decoding, and the fail-closed guard on the default scheme's parameters. The
// route-level behaviour over the app's real default-scheme parameters is ExternalWorkforceLinkEndpointsTests; this file
// covers the branches the route cannot reach — the default scheme in every environment is single-tenant, so a token from
// another tenant never gets past it to the A9 tid check.

using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>r3 task 048 — the A9 guest-token conditions, the UPN decoding, and the fail-closed parameter guard.</summary>
public sealed class SpaarkeGuestTokenValidatorTests
{
    private const string SpaarkeTenant = "4a8b0c3e-7d21-4f5a-9e6b-048048048101";
    private const string HomeTenantText = "4a8b0c3e-7d21-4f5a-9e6b-048048048102";
    private const string AppId = "4a8b0c3e-7d21-4f5a-9e6b-048048048a01";
    private const string HomeUpn = "mike.okafor@customer.example";

    private static readonly Guid HomeTenant = Guid.Parse(HomeTenantText);
    private static readonly Guid GuestOid = Guid.Parse("4a8b0c3e-7d21-4f5a-9e6b-0480480481f0");
    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "t048-unit" };

    // ── DenyReason: every A9 condition, each perturbed alone ──────────────────────────────────────

    [Fact]
    public void DenyReason_AGuestOfTheSpaarkeTenant_FromTheBearersHomeTenant_IsAccepted_WithItsOid()
    {
        Deny(Token(), out var oid).Should().BeNull();
        oid.Should().Be(GuestOid);
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "aud-copilot", GuestTokenDeny.Invalid },
        { "aud-other-app", GuestTokenDeny.Invalid },
        { "idtyp-app", GuestTokenDeny.Invalid },
        { "scp-missing", GuestTokenDeny.Invalid },
        { "scp-other", GuestTokenDeny.Invalid },
        { "tid-other", GuestTokenDeny.TenantNotSpaarke },
        { "spaarke-tenant-unset", GuestTokenDeny.TenantNotSpaarke },
        { "acct-0", GuestTokenDeny.NotGuest },
        { "acct-missing", GuestTokenDeny.NotGuest },
        { "idp-missing", GuestTokenDeny.HomeTenantMismatch },
        { "idp-other-tenant", GuestTokenDeny.HomeTenantMismatch },
        { "idp-not-a-tenant", GuestTokenDeny.HomeTenantMismatch },
        { "oid-missing", GuestTokenDeny.Invalid },
        { "oid-empty", GuestTokenDeny.Invalid },
        { "upn-other-person", GuestTokenDeny.UpnMismatch },
        { "home-preferred-username-missing", GuestTokenDeny.UpnMismatch },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void DenyReason_OneConditionFailing_RefusesWithItsReason_AndNoOid(string defect, string reason)
    {
        string? spaarke = SpaarkeTenant;
        string? homeUpn = HomeUpn;
        var token = defect switch
        {
            "aud-copilot" => Token(audience: "api://botid-4a8b0c3e-7d21-4f5a-9e6b-048048048c01"),
            "aud-other-app" => Token(audience: "api://4a8b0c3e-7d21-4f5a-9e6b-048048048a99"),
            "idtyp-app" => Token(c => c["idtyp"] = "app"),
            "scp-missing" => Token(c => c.Remove("scp")),
            "scp-other" => Token(c => c["scp"] = "Files.Read"),
            "tid-other" => Token(c => c["tid"] = HomeTenantText),
            "spaarke-tenant-unset" => Token(),
            "acct-0" => Token(c => c["acct"] = 0),
            "acct-missing" => Token(c => c.Remove("acct")),
            "idp-missing" => Token(c => c.Remove("idp")),
            "idp-other-tenant" => Token(c => c["idp"] = "https://sts.windows.net/4a8b0c3e-7d21-4f5a-9e6b-048048048199/"),
            "idp-not-a-tenant" => Token(c => c["idp"] = "live.com"),
            "oid-missing" => Token(c => c.Remove("oid")),
            "oid-empty" => Token(c => c["oid"] = Guid.Empty.ToString("D")),
            "upn-other-person" => Token(c => c["upn"] = "someone.else_customer.example#EXT#@spaarke.onmicrosoft.com"),
            "home-preferred-username-missing" => Token(),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        if (defect == "spaarke-tenant-unset") spaarke = null;
        if (defect == "home-preferred-username-missing") homeUpn = null;

        SpaarkeGuestTokenValidator.DenyReason(token, HomeTenant, homeUpn, spaarke, AppId, out var oid).Should().Be(reason);
        oid.Should().Be(Guid.Empty);
    }

    [Theory]
    [InlineData("api://" + AppId)]
    [InlineData(AppId)]
    public void DenyReason_EitherFormOfTheStampAppAudience_IsAccepted(string audience)
        => Deny(Token(audience: audience), out _).Should().BeNull();

    [Theory]
    [InlineData("https://sts.windows.net/" + HomeTenantText + "/")]
    [InlineData("https://login.microsoftonline.com/" + HomeTenantText + "/v2.0")]
    public void DenyReason_TheIdpInV1OrV2Form_NamesTheHomeTenant(string idp)
        => Deny(Token(c => c["idp"] = idp), out _).Should().BeNull();

    // ── GuestHomeUpn: the #EXT# decoding ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("upn", "mike.okafor_customer.example#EXT#@spaarke.onmicrosoft.com", "mike.okafor@customer.example")]
    [InlineData("upn", "first_last_customer.example#EXT#@spaarke.onmicrosoft.com", "first_last@customer.example")]
    [InlineData("unique_name", "mike_customer.example#EXT#@spaarke.onmicrosoft.com", "mike@customer.example")]
    [InlineData("preferred_username", "mike_customer.example#ext#@spaarke.onmicrosoft.com", "mike@customer.example")]
    [InlineData("email", "Mike.Okafor@Customer.Example", "Mike.Okafor@Customer.Example")]
    [InlineData("preferred_username", "mike.okafor@customer.example", "mike.okafor@customer.example")]
    [InlineData("upn", "nounderscore#EXT#@spaarke.onmicrosoft.com", null)]
    public void GuestHomeUpn_ReadsTheHomeAddress_FromTheFirstUsableClaim(string claim, string value, string? expected)
    {
        var token = Token(c =>
        {
            c.Remove("upn");
            c[claim] = value;
        });

        SpaarkeGuestTokenValidator.GuestHomeUpn(token).Should().Be(expected);
    }

    [Fact]
    public void GuestHomeUpn_AnExtUpn_WinsOverAnEmailClaimNamingSomeoneElse()
    {
        var token = Token(c => c["email"] = "someone.else@customer.example");

        SpaarkeGuestTokenValidator.GuestHomeUpn(token).Should().Be(HomeUpn);
    }

    // ── ValidateAsync: the default scheme's parameters must not be weakened ──────────────────────

    [Theory]
    [InlineData(null)] // positive control: fully validating parameters accept the same token
    [InlineData(nameof(TokenValidationParameters.RequireSignedTokens))]
    [InlineData(nameof(TokenValidationParameters.ValidateLifetime))]
    [InlineData(nameof(TokenValidationParameters.ValidateAudience))]
    [InlineData(nameof(TokenValidationParameters.ValidateIssuer))]
    public async Task ValidateAsync_DefaultSchemeWithoutSignatureLifetimeAudienceOrIssuerValidation_RefusesEveryToken(
        string? weakened)
    {
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = "api://" + AppId,
            IssuerSigningKey = Key,
        };
        switch (weakened)
        {
            case nameof(TokenValidationParameters.RequireSignedTokens): parameters.RequireSignedTokens = false; break;
            case nameof(TokenValidationParameters.ValidateLifetime): parameters.ValidateLifetime = false; break;
            case nameof(TokenValidationParameters.ValidateAudience): parameters.ValidateAudience = false; break;
            case nameof(TokenValidationParameters.ValidateIssuer): parameters.ValidateIssuer = false; break;
        }

        var options = new Mock<IOptionsMonitor<JwtBearerOptions>>();
        options.Setup(o => o.Get(JwtBearerDefaults.AuthenticationScheme))
            .Returns(new JwtBearerOptions { TokenValidationParameters = parameters });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = SpaarkeTenant,
            ["AzureAd:ClientId"] = AppId,
        }).Build();
        var sut = new SpaarkeGuestTokenValidator(options.Object, configuration, NullLogger<SpaarkeGuestTokenValidator>.Instance);

        var result = await sut.ValidateAsync(RawToken(), HomeTenant, HomeUpn, CancellationToken.None);

        if (weakened is null)
        {
            result.Should().Be(new GuestTokenValidation(GuestOid, null));
        }
        else
        {
            result.Should().Be(new GuestTokenValidation(null, GuestTokenDeny.Invalid));
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenTheSigningMetadataCannotBeFetched_IsUnavailable_NotARefusal()
    {
        // The token handler would swallow this fetch failure and refuse a good token as invalid (403); an outage is 503.
        var manager = new Microsoft.IdentityModel.Protocols.ConfigurationManager<
            Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
            "https://metadata.invalid/.well-known/openid-configuration",
            new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever(),
            new UnreachableMetadata());
        var options = new Mock<IOptionsMonitor<JwtBearerOptions>>();
        options.Setup(o => o.Get(JwtBearerDefaults.AuthenticationScheme)).Returns(new JwtBearerOptions
        {
            TokenValidationParameters = new TokenValidationParameters { ValidIssuer = Issuer, ValidAudience = "api://" + AppId },
            ConfigurationManager = manager,
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = SpaarkeTenant,
            ["AzureAd:ClientId"] = AppId,
        }).Build();
        var sut = new SpaarkeGuestTokenValidator(options.Object, configuration, NullLogger<SpaarkeGuestTokenValidator>.Instance);

        var result = await sut.ValidateAsync(RawToken(), HomeTenant, HomeUpn, CancellationToken.None);

        result.Should().Be(new GuestTokenValidation(null, GuestTokenDeny.Unavailable));
    }

    private sealed class UnreachableMetadata : Microsoft.IdentityModel.Protocols.IDocumentRetriever
    {
        public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
            => throw new IOException("The metadata endpoint is unreachable.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private const string Issuer = "https://login.microsoftonline.com/" + SpaarkeTenant + "/v2.0";

    private static string? Deny(JsonWebToken token, out Guid oid)
        => SpaarkeGuestTokenValidator.DenyReason(token, HomeTenant, HomeUpn.ToUpperInvariant(), SpaarkeTenant, AppId, out oid);

    private static JsonWebToken Token(Action<Dictionary<string, object>>? edit = null, string audience = "api://" + AppId)
        => new(RawToken(edit, audience));

    private static string RawToken(Action<Dictionary<string, object>>? edit = null, string audience = "api://" + AppId)
    {
        var claims = new Dictionary<string, object>
        {
            ["ver"] = "2.0",
            ["tid"] = SpaarkeTenant,
            ["oid"] = GuestOid.ToString("D"),
            ["scp"] = "user_impersonation",
            ["idtyp"] = "user",
            ["acct"] = 1,
            ["idp"] = $"https://sts.windows.net/{HomeTenantText}/",
            ["upn"] = "mike.okafor_customer.example#EXT#@spaarke.onmicrosoft.com",
        };
        edit?.Invoke(claims);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(30),
            Claims = claims,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
        });
    }
}
