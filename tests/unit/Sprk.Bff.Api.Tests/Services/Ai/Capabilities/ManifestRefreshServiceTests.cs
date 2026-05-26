using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Capabilities;

/// <summary>
/// Unit tests for <see cref="ManifestRefreshService"/>.
///
/// Coverage:
///   - Stale manifest is retained when <see cref="ICapabilityManifestLoader"/> throws
///   - Webhook trigger wakes the background loop (IManifestRefreshTrigger.TriggerRefresh)
///   - Successful refresh calls Manifest.Refresh with loader results
/// </summary>
public sealed class ManifestRefreshServiceTests : IDisposable
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="CapabilityManifest"/> pre-seeded with <paramref name="entries"/>.
    /// </summary>
    private static CapabilityManifest SeedManifest(params CapabilityManifestEntry[] entries)
    {
        var manifest = new CapabilityManifest(NullLogger<CapabilityManifest>.Instance);
        manifest.Refresh(entries);
        return manifest;
    }

    /// <summary>
    /// Builds a <see cref="ManifestRefreshService"/> with the given dependencies.
    /// Uses a very long interval (1 hour) so the PeriodicTimer never fires automatically
    /// during tests — tests control refreshes explicitly via TriggerRefresh or cancellation.
    /// </summary>
    private static ManifestRefreshService BuildService(
        CapabilityManifest manifest,
        ICapabilityManifestLoader loader,
        int intervalMinutes = 60)
    {
        var options = Options.Create(new ManifestRefreshOptions
        {
            RefreshIntervalMinutes = intervalMinutes,
            WebhookSecret = "test-secret"
        });

        return new ManifestRefreshService(
            manifest,
            loader,
            NullLogger<ManifestRefreshService>.Instance,
            options);
    }

    private static CapabilityManifestEntry MakeEntry(string name, bool enabled = true) =>
        new(
            CapabilityName: name,
            Description: $"Description of {name}",
            KeywordHints: Array.Empty<string>(),
            PlaybookId: null,
            ToolNames: Array.Empty<string>(),
            IsEnabled: enabled,
            TenantRestrictions: Array.Empty<string>());

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose() { }

    // ── Tests: stale-on-error ─────────────────────────────────────────────────

    [Fact]
    public async Task WhenLoaderThrows_ExistingManifestIsRetained()
    {
        // Arrange: manifest pre-seeded with one entry
        var existing = MakeEntry("search");
        var manifest = SeedManifest(existing);
        manifest.GetAll().Should().HaveCount(1, "precondition: manifest is seeded");

        var loader = new Mock<ICapabilityManifestLoader>();
        loader
            .Setup(l => l.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unreachable"));

        using var service = BuildService(manifest, loader.Object);

        // Act: trigger a webhook refresh and let the service process it
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        service.TriggerRefresh();

        // Start the background service, give it time to process the trigger, then cancel.
        var executeTask = service.StartAsync(cts.Token);

        // Wait long enough for the loop to pick up the trigger signal and attempt the refresh.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await cts.CancelAsync();

        // StopAsync completes gracefully after cancellation.
        await service.StopAsync(CancellationToken.None);

        // Assert: manifest still has the original entry despite the loader failure.
        manifest.GetAll().Should().HaveCount(1,
            "stale-on-error policy: failed refresh must not clear the existing manifest");
        manifest.TryGet("search", out var found).Should().BeTrue();
        found!.CapabilityName.Should().Be("search");
    }

    // ── Tests: successful refresh ─────────────────────────────────────────────

    [Fact]
    public async Task WhenLoaderSucceeds_ManifestIsUpdatedWithNewEntries()
    {
        // Arrange: manifest starts empty
        var manifest = SeedManifest(); // no entries
        manifest.GetAll().Should().BeEmpty("precondition: manifest is empty");

        var newEntries = new[]
        {
            MakeEntry("web_search"),
            MakeEntry("summarize")
        };

        var loader = new Mock<ICapabilityManifestLoader>();
        loader
            .Setup(l => l.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(newEntries);

        using var service = BuildService(manifest, loader.Object);

        // Act: trigger a refresh
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        service.TriggerRefresh();
        _ = service.StartAsync(cts.Token);

        // Wait for the loop to process the trigger
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert
        manifest.GetAll().Should().HaveCount(2);
        manifest.TryGet("web_search", out _).Should().BeTrue();
        manifest.TryGet("summarize", out _).Should().BeTrue();
    }

    // ── Tests: IManifestRefreshTrigger ────────────────────────────────────────

    [Fact]
    public void TriggerRefresh_DoesNotThrow_WhenCalledMultipleTimes()
    {
        // Arrange
        var manifest = SeedManifest();
        var loader = new Mock<ICapabilityManifestLoader>();
        using var service = BuildService(manifest, loader.Object);

        // Act: calling TriggerRefresh multiple times before the loop is running
        // should not throw — the bounded channel drops extra signals.
        var triggerAction = () =>
        {
            for (var i = 0; i < 10; i++)
                service.TriggerRefresh();
        };

        // Assert
        triggerAction.Should().NotThrow(
            "bounded channel with DropOldest mode silently discards extra signals");
    }

    [Fact]
    public void TriggerRefresh_IsExposedViaInterface()
    {
        // Arrange
        var manifest = SeedManifest();
        var loader = new Mock<ICapabilityManifestLoader>();
        using var service = BuildService(manifest, loader.Object);

        // Assert: ManifestRefreshService implements IManifestRefreshTrigger
        IManifestRefreshTrigger trigger = service;

        var triggerAction = () => trigger.TriggerRefresh();
        triggerAction.Should().NotThrow(
            "IManifestRefreshTrigger.TriggerRefresh must be callable via the interface");
    }

    // ── Tests: disabled entries filtered ─────────────────────────────────────

    [Fact]
    public async Task WhenLoaderReturnsDisabledEntries_TheyAreExcludedFromManifest()
    {
        // Arrange
        var manifest = SeedManifest();

        var entries = new[]
        {
            MakeEntry("enabled_cap", enabled: true),
            MakeEntry("disabled_cap", enabled: false)
        };

        var loader = new Mock<ICapabilityManifestLoader>();
        loader
            .Setup(l => l.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        using var service = BuildService(manifest, loader.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        service.TriggerRefresh();
        _ = service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Assert: only enabled entries visible on the read path
        manifest.GetAll().Should().HaveCount(1);
        manifest.TryGet("enabled_cap", out _).Should().BeTrue();
        manifest.TryGet("disabled_cap", out _).Should().BeFalse(
            "disabled capabilities must be filtered by CapabilityManifest.Refresh");
    }
}
