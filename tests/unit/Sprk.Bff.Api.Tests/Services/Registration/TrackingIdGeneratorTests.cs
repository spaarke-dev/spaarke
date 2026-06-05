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
        // RB-T013-01 flake repair (2026-06-01): the prior assertion `HaveCount(100)`
        // was probabilistically weak. With 100 IDs from 4-char × 30-char-alphabet ID
        // space, birthday-paradox collision probability ≈ N²/(2·30⁴) ≈ 0.6% per run.
        // Task 013 (Phase 1 exit triple-run) surfaced the flake; it pre-dates r2.
        // The fix tolerates 1 collision pair: still detects real duplication bugs
        // (which would produce many collisions) while eliminating the probabilistic
        // tail. See projects/sdap.bff.api-test-suite-repair-r2/baseline/phase1-exit-triple-run-2026-06-01.md.
        var ids = new HashSet<string>();

        for (var i = 0; i < 100; i++)
        {
            ids.Add(_sut.Generate());
        }

        ids.Should().HaveCountGreaterThanOrEqualTo(99,
            "100 IDs from a 4-char × 30-char-alphabet space have ~0.6% birthday-paradox " +
            "collision probability; tolerating 1 collision pair eliminates the flake while " +
            "still catching real duplication bugs (which would produce many collisions)");
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
        // Flake repair (2026-06-04): same root cause + same pattern as
        // Generate_ProducesUniqueIdsAcrossMultipleCalls above. With 50 IDs from
        // a 4-char × 30-char-alphabet space, birthday-paradox collision probability
        // ≈ 50²/(2·30⁴) ≈ 0.15% per run. Real thread-safety failure would manifest
        // as concurrent exceptions or many collisions, not a single random clash.
        // Assert: no thrown exceptions (Task.WhenAll succeeded), full count, and
        // tolerate up to 1 collision pair (≥ 49 unique) for the same reason as the
        // sibling test.
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => ids.Add(_sut.Generate())))
            .ToArray();

        await Task.WhenAll(tasks);

        ids.Should().HaveCount(50, "concurrent generation must not lose entries to a torn data structure");
        ids.Distinct().Should().HaveCountGreaterThanOrEqualTo(49,
            "50 IDs from a 4-char × 30-char-alphabet space have ~0.15% birthday-paradox " +
            "collision probability; tolerating 1 collision pair eliminates the flake while " +
            "still catching real concurrency / duplication bugs (which would produce many collisions)");
    }
}
