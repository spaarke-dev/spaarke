using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Xunit;
using FluentAssertions;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="CapabilityManifest"/>.
///
/// Acceptance criteria (AIPU2-010):
///   - TryGet returns the correct entry for a known capability name.
///   - GetAll returns only enabled entries; disabled entries are excluded.
///   - Refresh atomically swaps the catalog (no stale reads observable after swap).
///   - TryGet and GetAll complete in under 1ms on a populated manifest.
///   - Case-insensitive lookup works (TryGet is case-insensitive).
/// </summary>
public class CapabilityManifestTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    private static CapabilityManifest CreateManifest() =>
        new(Mock.Of<ILogger<CapabilityManifest>>());

    private static CapabilityManifestEntry MakeEntry(
        string name,
        bool isEnabled = true,
        string description = "Test capability") =>
        new(
            CapabilityName: name,
            Description: description,
            KeywordHints: new[] { "hint1", "hint2" },
            PlaybookId: null,
            ToolNames: new[] { "ToolA" },
            IsEnabled: isEnabled,
            TenantRestrictions: Array.Empty<string>());

    private static IReadOnlyList<CapabilityManifestEntry> SampleEntries() =>
    [
        MakeEntry("web_search"),
        MakeEntry("legal_research"),
        MakeEntry("disabled_cap", isEnabled: false),
        MakeEntry("write_back"),
    ];

    // ── TryGet ────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGet_ReturnsTrue_WhenCapabilityIsEnabled()
    {
        // Arrange
        var manifest = CreateManifest();
        manifest.Refresh(SampleEntries());

        // Act
        var found = manifest.TryGet("web_search", out var entry);

        // Assert
        found.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.CapabilityName.Should().Be("web_search");
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenCapabilityDoesNotExist()
    {
        // Arrange
        var manifest = CreateManifest();
        manifest.Refresh(SampleEntries());

        // Act
        var found = manifest.TryGet("nonexistent_cap", out var entry);

        // Assert
        found.Should().BeFalse();
        entry.Should().BeNull();
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenCapabilityIsDisabled()
    {
        // Arrange
        var manifest = CreateManifest();
        manifest.Refresh(SampleEntries());

        // Act
        var found = manifest.TryGet("disabled_cap", out var entry);

        // Assert — disabled entries are excluded from the index
        found.Should().BeFalse();
        entry.Should().BeNull();
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        // Arrange
        var manifest = CreateManifest();
        manifest.Refresh(SampleEntries());

        // Act & Assert — should find regardless of case
        manifest.TryGet("WEB_SEARCH", out var upper).Should().BeTrue();
        manifest.TryGet("Web_Search", out var mixed).Should().BeTrue();
        manifest.TryGet("web_search", out var lower).Should().BeTrue();

        upper.Should().NotBeNull();
        mixed.Should().NotBeNull();
        lower.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGet_ReturnsFalse_WhenNameIsNullOrWhitespace(string? name)
    {
        // Arrange
        var manifest = CreateManifest();
        manifest.Refresh(SampleEntries());

        // Act
        var found = manifest.TryGet(name!, out var entry);

        // Assert
        found.Should().BeFalse();
        entry.Should().BeNull();
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_ReturnsOnlyEnabledEntries()
    {
        // Arrange
        var manifest = CreateManifest();
        manifest.Refresh(SampleEntries());

        // Act
        var all = manifest.GetAll();

        // Assert — 3 enabled: web_search, legal_research, write_back
        all.Should().HaveCount(3);
        all.Should().NotContain(e => e.CapabilityName == "disabled_cap");
        all.Should().Contain(e => e.CapabilityName == "web_search");
        all.Should().Contain(e => e.CapabilityName == "legal_research");
        all.Should().Contain(e => e.CapabilityName == "write_back");
    }

    [Fact]
    public void GetAll_ReturnsEmptyList_WhenManifestIsEmpty()
    {
        // Arrange
        var manifest = CreateManifest();
        // No Refresh called — starts from ManifestState.Empty

        // Act
        var all = manifest.GetAll();

        // Assert
        all.Should().BeEmpty();
    }

    [Fact]
    public void GetAll_ReturnsEmptyList_WhenAllEntriesAreDisabled()
    {
        // Arrange
        var manifest = CreateManifest();
        manifest.Refresh(new[]
        {
            MakeEntry("cap1", isEnabled: false),
            MakeEntry("cap2", isEnabled: false),
        });

        // Act
        var all = manifest.GetAll();

        // Assert
        all.Should().BeEmpty();
    }

    // ── Refresh / atomic swap ─────────────────────────────────────────────────

    [Fact]
    public void Refresh_ReplacesEntiresCatalog_Atomically()
    {
        // Arrange
        var manifest = CreateManifest();
        manifest.Refresh(new[] { MakeEntry("old_cap") });

        manifest.TryGet("old_cap", out _).Should().BeTrue();

        // Act — replace with a completely different set
        manifest.Refresh(new[] { MakeEntry("new_cap") });

        // Assert — old entry gone, new entry present
        manifest.TryGet("old_cap", out _).Should().BeFalse();
        manifest.TryGet("new_cap", out _).Should().BeTrue();
    }

    [Fact]
    public void Refresh_UpdatesLastRefreshedUtc()
    {
        // Arrange
        var manifest = CreateManifest();
        var before = DateTimeOffset.UtcNow;

        // Act
        manifest.Refresh(SampleEntries());

        // Assert
        var after = DateTimeOffset.UtcNow;
        manifest.LastRefreshedUtc.Should().BeOnOrAfter(before);
        manifest.LastRefreshedUtc.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void Refresh_WithEmptyList_ClearsAllEntries()
    {
        // Arrange
        var manifest = CreateManifest();
        manifest.Refresh(SampleEntries());
        manifest.GetAll().Should().NotBeEmpty();

        // Act
        manifest.Refresh(Array.Empty<CapabilityManifestEntry>());

        // Assert
        manifest.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Refresh_ThrowsArgumentNullException_WhenEntriesIsNull()
    {
        // Arrange
        var manifest = CreateManifest();

        // Act & Assert
        var act = () => manifest.Refresh(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── LastRefreshedUtc before first Refresh ─────────────────────────────────

    [Fact]
    public void LastRefreshedUtc_IsMinValue_BeforeFirstRefresh()
    {
        // Arrange
        var manifest = CreateManifest();

        // Assert
        manifest.LastRefreshedUtc.Should().Be(DateTimeOffset.MinValue);
    }

    // ── Performance ───────────────────────────────────────────────────────────

    [Fact]
    public void TryGet_CompletesUnderOneMillisecond_OnPopulatedManifest()
    {
        // Arrange — load 100 entries to simulate a realistic catalog
        var manifest = CreateManifest();
        var entries = Enumerable.Range(0, 100)
            .Select(i => MakeEntry($"capability_{i}"))
            .ToList();
        manifest.Refresh(entries);

        // Warm up (JIT)
        manifest.TryGet("capability_50", out _);

        // Act
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            manifest.TryGet("capability_50", out _);
        }
        sw.Stop();

        // Assert — 1000 iterations must complete in under 1 second (i.e., avg < 1ms each)
        var avgMs = sw.Elapsed.TotalMilliseconds / 1000;
        avgMs.Should().BeLessThan(1.0,
            because: $"TryGet must complete in under 1ms; average was {avgMs:F4}ms");
    }

    [Fact]
    public void GetAll_CompletesUnderOneMillisecond_OnPopulatedManifest()
    {
        // Arrange
        var manifest = CreateManifest();
        var entries = Enumerable.Range(0, 100)
            .Select(i => MakeEntry($"capability_{i}"))
            .ToList();
        manifest.Refresh(entries);

        // Warm up
        manifest.GetAll();

        // Act
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            manifest.GetAll();
        }
        sw.Stop();

        // Assert
        var avgMs = sw.Elapsed.TotalMilliseconds / 1000;
        avgMs.Should().BeLessThan(1.0,
            because: $"GetAll must complete in under 1ms; average was {avgMs:F4}ms");
    }
}
