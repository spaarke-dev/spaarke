using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 185: the invite code and its stored value (design note §1 and §2.1). Pure, no doubles.
/// </summary>
public class InviteCodeTests
{
    private static readonly Guid ContactA = Guid.Parse("cccccccc-0000-4000-8000-00000000000a");
    private static readonly Guid ContactB = Guid.Parse("cccccccc-0000-4000-8000-00000000000b");

    [Fact]
    public void ACode_Is43Base64UrlCharacters_From32RandomBytes()
    {
        var (code, _) = InviteCode.Create(ContactA);

        code.Should().HaveLength(InviteCode.CodeLength).And.MatchRegex("^[A-Za-z0-9_-]{43}$");
        Base64Url.DecodeFromChars(code).Should().HaveCount(32, "256 bits (owner round 98, Q7)");
        InviteCode.IsWellFormed(code).Should().BeTrue();
    }

    [Fact]
    public void TwoCodes_ForTheSameContact_Differ_AndSoDoTheirSalts()
    {
        var first = InviteCode.Create(ContactA);
        var second = InviteCode.Create(ContactA);

        first.Code.Should().NotBe(second.Code);
        first.StoredValue[..25].Should().NotBe(second.StoredValue[..25], "each invite gets its own salt");
    }

    [Fact]
    public void TheStoredValue_IsV1_SaltDotDigest_AndNeverContainsTheCode()
    {
        var (code, stored) = InviteCode.Create(ContactA);

        stored.Should().HaveLength(InviteCode.StoredValueLength).And.HaveLength(69)
            .And.MatchRegex(@"^v1\.[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]{43}$");
        stored.Should().NotContain(code);
    }

    [Fact]
    public void TheDigest_IsSha256_OverTheDomain_TheContactId_TheSalt_AndTheCode()
    {
        // Known-answer check of design §2.1, recomputed independently from the stored salt.
        var (code, stored) = InviteCode.Create(ContactA);
        var salt = Base64Url.DecodeFromChars(stored.AsSpan(3, 22));
        var digest = Base64Url.DecodeFromChars(stored.AsSpan(26, 43));

        var input = Encoding.UTF8.GetBytes("spaarke.invite-code.v1\n" + ContactA.ToString("D") + "\n")
            .Concat(salt).Concat(Base64Url.DecodeFromChars(code)).ToArray();

        digest.Should().Equal(SHA256.HashData(input));
    }

    [Fact]
    public void TheCode_MatchesItsOwnStoredValue_ForItsOwnContact()
    {
        var (code, stored) = InviteCode.Create(ContactA);

        InviteCode.Matches(stored, code, ContactA).Should().BeTrue();
    }

    [Fact]
    public void ACode_NeverMatches_AnotherContact()
    {
        // The contact id is in the hash input: a stored value copied onto another contact never verifies.
        var (code, stored) = InviteCode.Create(ContactA);

        InviteCode.Matches(stored, code, ContactB).Should().BeFalse();
        InviteCode.Matches(stored, code, Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public void AWrongCode_OrASupersededCode_NeverMatches()
    {
        var superseded = InviteCode.Create(ContactA);
        var current = InviteCode.Create(ContactA);

        InviteCode.Matches(current.StoredValue, superseded.Code, ContactA).Should().BeFalse("a re-invite replaced it");
        InviteCode.Matches(current.StoredValue, current.Code, ContactA).Should().BeTrue();
    }

    [Fact]
    public void ACodeDifferingInOneCharacter_NeverMatches()
    {
        var (code, stored) = InviteCode.Create(ContactA);
        var flipped = (code[10] == 'A' ? 'B' : 'A') + "";
        var altered = code[..10] + flipped + code[11..];

        InviteCode.Matches(stored, altered, ContactA).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // 44 chars, padded
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]  // '+' is not base64url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]  // whitespace
    public void AMalformedPresentedCode_IsFalse_NeverThrows(string? presented)
    {
        var (_, stored) = InviteCode.Create(ContactA);

        InviteCode.IsWellFormed(presented).Should().BeFalse();
        InviteCode.Matches(stored, presented, ContactA).Should().BeFalse();
    }

    public static TheoryData<string?> MalformedStoredValues()
    {
        var (_, stored) = InviteCode.Create(ContactA);
        return new TheoryData<string?>
        {
            null,
            "",
            "   ",
            "v2" + stored[2..],                        // an unknown version
            stored[..^1],                              // truncated
            stored + "A",                              // too long
            stored[..25] + "_" + stored[26..],         // the separator replaced
            stored[..30] + "+" + stored[31..],         // a non-base64url character in the digest
            "v1." + new string('*', 66),               // the right length, garbage
        };
    }

    [Theory]
    [MemberData(nameof(MalformedStoredValues))]
    public void AMalformedOrAbsentStoredValue_IsFalse_NeverThrows(string? storedValue)
    {
        var (code, _) = InviteCode.Create(ContactA);

        var act = () => InviteCode.Matches(storedValue, code, ContactA);

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    [Fact]
    public void ACodeCannotBeCreated_ForNoContact()
        => FluentActions.Invoking(() => InviteCode.Create(Guid.Empty)).Should().Throw<ArgumentException>();
}
