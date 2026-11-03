using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// The invite stamp's email hash (<see cref="InviteEmailStamp"/>) and the <c>xms_edov</c> claim as the email bind reads it
/// (<see cref="WorkforceCallerClaims.EmailDomainOwnerVerifiedOf"/>). Rules pinned: the stored value is
/// <c>v1:{salt-b64}:{hash-b64}</c>, SHA-256 over a fresh salt of at least 16 bytes followed by the trimmed, lower-cased
/// address; an unknown version or a value that does not parse is no stamp; only a single boolean-true <c>xms_edov</c> counts.
/// </summary>
public class InviteEmailStampTests
{
    private const string Email = "person@customer.example";

    [Fact]
    public void TheStoredValue_IsV1_SaltAndSha256OfTheNormalisedAddress()
    {
        var stored = InviteEmailStamp.Create("  Person@Customer.EXAMPLE ");

        var parts = stored.Split(':');
        parts.Should().HaveCount(3);
        parts[0].Should().Be("v1");
        var salt = Convert.FromBase64String(parts[1]);
        salt.Should().HaveCount(InviteEmailStamp.SaltBytes);
        InviteEmailStamp.SaltBytes.Should().BeGreaterThanOrEqualTo(16);
        var expected = SHA256.HashData(salt.Concat(Encoding.UTF8.GetBytes(Email)).ToArray());
        Convert.FromBase64String(parts[2]).Should().Equal(expected, "the digest is SHA-256(salt || trimmed lower-case address)");
        stored.Should().NotContain("person", "the address is never stored");
    }

    [Fact]
    public void EveryStamp_HasItsOwnSalt()
    {
        var first = InviteEmailStamp.Create(Email);
        var second = InviteEmailStamp.Create(Email);

        first.Should().NotBe(second);
        InviteEmailStamp.Matches(first, Email).Should().BeTrue();
        InviteEmailStamp.Matches(second, Email).Should().BeTrue();
    }

    [Theory]
    [InlineData(Email, true)]
    [InlineData("PERSON@CUSTOMER.EXAMPLE", true)]
    [InlineData("  person@customer.example\t", true)]
    [InlineData("person+tag@customer.example", false)] // normalisation is trim + lower-case only
    [InlineData("per.son@customer.example", false)]
    [InlineData("person@customer.example.", false)]
    [InlineData("someone@customer.example", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Matches_OnlyTheNormalisedSameAddress(string? candidate, bool matches)
        => InviteEmailStamp.Matches(InviteEmailStamp.Create(Email), candidate).Should().Be(matches);

    /// <summary>A value that is not a stamp this version writes is no stamp: it is not well formed and matches nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1:")]
    [InlineData("v1:only-one-part")]
    [InlineData("v1:!!notbase64!!:AAAA")]
    [InlineData("v1:a:b:c")]
    [InlineData("V1:{salt}:{digest}")]      // the version prefix is exact
    [InlineData("v2:{salt}:{digest}")]      // an unknown version
    [InlineData("{salt}:{digest}")]         // no version
    [InlineData("v1:{shortsalt}:{digest}")] // a salt under 16 bytes
    [InlineData("v1:{salt}:{shortdigest}")] // not a SHA-256 digest
    [InlineData("v1.{salt}.{digest}")]      // the invite code's separator, not this one's
    public void AnythingButAV1Stamp_IsNoStamp(string? template)
    {
        var real = InviteEmailStamp.Create(Email).Split(':');
        var value = template?
            .Replace("{salt}", real[1])
            .Replace("{digest}", real[2])
            .Replace("{shortsalt}", Convert.ToBase64String(new byte[15]))
            .Replace("{shortdigest}", Convert.ToBase64String(new byte[31]));

        InviteEmailStamp.IsWellFormed(value).Should().BeFalse(template);
        InviteEmailStamp.Matches(value, Email).Should().BeFalse(template);
    }

    [Fact]
    public void Create_RefusesABlankAddress()
    {
        var act = () => InviteEmailStamp.Create("  ");
        act.Should().Throw<ArgumentException>();
    }

    // ── xms_edov: only a single boolean true counts ──────────────────────────────────────────────────────

    private static ClaimsPrincipal With(params Claim[] claims) => new(new ClaimsIdentity(claims, "Test"));

    private static Claim Edov(string value, string valueType = ClaimValueTypes.Boolean)
        => new(WorkforceCallerClaims.EmailDomainOwnerVerifiedClaim, value, valueType);

    [Theory]
    [InlineData("true", ClaimValueTypes.Boolean, true)]
    [InlineData("True", ClaimValueTypes.Boolean, true)]
    [InlineData("false", ClaimValueTypes.Boolean, false)]
    [InlineData("true", ClaimValueTypes.String, false)]   // the text "true", not a JSON boolean
    [InlineData("1", ClaimValueTypes.Integer, false)]
    [InlineData("yes", ClaimValueTypes.Boolean, false)]
    [InlineData("", ClaimValueTypes.Boolean, false)]
    public void EmailDomainOwnerVerified_IsTrueOnlyForABooleanTrue(string value, string valueType, bool verified)
    {
        var user = With(new Claim("oid", Guid.NewGuid().ToString()), Edov(value, valueType));

        WorkforceCallerClaims.EmailDomainOwnerVerifiedOf(user).Should().Be(verified ? true : null);
        WorkforceCallerClaims.From(user).EmailDomainOwnerVerified.Should().Be(verified ? true : null);
    }

    [Fact]
    public void EmailDomainOwnerVerified_AbsentOrRepeated_IsNotVerified()
    {
        WorkforceCallerClaims.EmailDomainOwnerVerifiedOf(With(new Claim("oid", "x"))).Should().BeNull();
        WorkforceCallerClaims.EmailDomainOwnerVerifiedOf(With(Edov("true"), Edov("true"))).Should().BeNull();
        WorkforceCallerClaims.EmailDomainOwnerVerifiedOf(With(Edov("true"), Edov("false"))).Should().BeNull();
    }
}
