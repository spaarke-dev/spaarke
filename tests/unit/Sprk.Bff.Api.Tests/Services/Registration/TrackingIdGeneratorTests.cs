using FluentAssertions;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

public class TrackingIdGeneratorTests
{
    private readonly TrackingIdGenerator _sut = new();

    [Fact]
    public void Generate_ReturnsCorrectFormat()
    {
        var result = _sut.Generate();

        // Format: REG-YYYYMMDD-XXXX
        result.Should().MatchRegex(@"^REG-\d{8}-[A-Z2-9]{4}$");
    }

    [Fact]
    public void Generate_WithDate_IncludesCorrectDatePart()
    {
        var date = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);

        var result = _sut.Generate(date);

        result.Should().StartWith("REG-20260403-");
    }

    [Fact]
    public void Generate_WithDate_HasFourCharRandomSuffix()
    {
        var date = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

        var result = _sut.Generate(date);

        var parts = result.Split('-');
        parts.Should().HaveCount(3);
        parts[0].Should().Be("REG");
        parts[1].Should().Be("20260115");
        parts[2].Should().HaveLength(4);
    }

    [Fact]
    public void Generate_ProducesUniqueIdsAcrossMultipleCalls()
    {
        var ids = new HashSet<string>();

        for (var i = 0; i < 100; i++)
        {
            ids.Add(_sut.Generate());
        }

        // With 4 alphanumeric chars from a 30-char alphabet, collision in 100 is extremely unlikely
        ids.Should().HaveCount(100, "100 generated tracking IDs should all be unique");
    }

    [Fact]
    public void Generate_DoesNotContainAmbiguousCharacters()
    {
        // Generate many IDs and verify none contain ambiguous characters (0, O, 1, I, L)
        var ambiguousChars = new[] { '0', 'O', '1', 'I', 'L' };

        for (var i = 0; i < 50; i++)
        {
            var result = _sut.Generate();
            var randomPart = result.Split('-')[2];

            foreach (var c in ambiguousChars)
            {
                randomPart.Should().NotContain(c.ToString(),
                    $"random part should not contain ambiguous character '{c}'");
            }
        }
    }

    [Fact]
    public void Generate_UsesTodaysDate_WhenCalledWithoutParameter()
    {
        var result = _sut.Generate();
        var expectedDatePart = DateTimeOffset.UtcNow.ToString("yyyyMMdd");

        result.Should().Contain(expectedDatePart);
    }

    [Fact]
    public async Task Generate_IsThreadSafe()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => ids.Add(_sut.Generate())))
            .ToArray();

        await Task.WhenAll(tasks);

        ids.Should().HaveCount(50);
        ids.Distinct().Should().HaveCount(50, "all IDs generated concurrently should be unique");
    }
}
