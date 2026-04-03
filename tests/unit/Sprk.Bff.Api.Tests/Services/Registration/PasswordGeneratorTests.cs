using FluentAssertions;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// Unit tests for <see cref="PasswordGenerator"/>.
/// Validates length, complexity requirements, and uniqueness of generated passwords.
/// </summary>
public class PasswordGeneratorTests
{
    private readonly PasswordGenerator _sut = new();

    [Fact]
    public void Generate_DefaultLength_Returns16Characters()
    {
        var password = _sut.Generate();

        password.Length.Should().Be(16);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(32)]
    [InlineData(64)]
    public void Generate_CustomLength_ReturnsRequestedLength(int length)
    {
        var password = _sut.Generate(length);

        password.Length.Should().Be(length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(15)]
    public void Generate_TooShort_ThrowsArgumentOutOfRangeException(int length)
    {
        var act = () => _sut.Generate(length);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("length");
    }

    [Fact]
    public void Generate_ContainsUppercase()
    {
        var password = _sut.Generate();

        password.Should().MatchRegex("[A-Z]", "password must contain at least one uppercase letter");
    }

    [Fact]
    public void Generate_ContainsLowercase()
    {
        var password = _sut.Generate();

        password.Should().MatchRegex("[a-z]", "password must contain at least one lowercase letter");
    }

    [Fact]
    public void Generate_ContainsDigit()
    {
        var password = _sut.Generate();

        password.Should().MatchRegex("[0-9]", "password must contain at least one digit");
    }

    [Fact]
    public void Generate_ContainsSymbol()
    {
        var password = _sut.Generate();

        password.Should().MatchRegex(@"[!@#$%\^&\*\(\)\-_=\+\[\]\{\}\|;:,\.<>\?]",
            "password must contain at least one symbol");
    }

    [Fact]
    public void Generate_MultipleComplexityRequirements_AllMet()
    {
        // Run multiple times to account for the shuffle randomness
        for (var i = 0; i < 100; i++)
        {
            var password = _sut.Generate();

            password.Should().MatchRegex("[A-Z]", "uppercase required");
            password.Should().MatchRegex("[a-z]", "lowercase required");
            password.Should().MatchRegex("[0-9]", "digit required");
            password.Should().MatchRegex(@"[!@#$%\^&\*\(\)\-_=\+\[\]\{\}\|;:,\.<>\?]", "symbol required");
        }
    }

    [Fact]
    public void Generate_ProducesUniquePasswords()
    {
        var passwords = new HashSet<string>();

        for (var i = 0; i < 1000; i++)
        {
            var password = _sut.Generate();
            passwords.Add(password);
        }

        // With 16-char passwords from a large character set,
        // 1000 passwords should all be unique (collision probability is negligible)
        passwords.Count.Should().Be(1000, "all generated passwords should be unique");
    }

    [Fact]
    public void Generate_DoesNotStartWithPredictablePattern()
    {
        // Verify the Fisher-Yates shuffle distributes required characters randomly.
        // If the first 4 characters were always uppercase, lowercase, digit, symbol
        // in that order, the shuffle is broken.
        var firstChars = new HashSet<char>();

        for (var i = 0; i < 100; i++)
        {
            var password = _sut.Generate();
            firstChars.Add(password[0]);
        }

        // With proper shuffling, the first character should vary across runs
        firstChars.Count.Should().BeGreaterThan(1,
            "the first character should vary, indicating proper shuffling");
    }
}
