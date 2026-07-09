using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="SpeSyncOrchestrator"/> — the FR-26 SPE change-detection
/// state machine (create/renew subscription + delta enumeration, Redis-backed).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> — orchestration over a mocked module
/// boundary (<see cref="ISpeFileOperations"/>, a legitimate Spaarke facade) plus a REAL
/// in-memory <see cref="IDistributedCache"/> (<see cref="MemoryDistributedCache"/>) so the
/// Redis serialize/round-trip is exercised for real rather than mocked. No
/// <c>Mock&lt;HttpMessageHandler&gt;</c> (B1), no DI-registration tests (B3), no ctor
/// null-check tests (B4). Every assertion captures observable behavior: renewal decisions,
/// persisted state, fallback flagging, net delta output.
/// </para>
///
/// <para>
/// <b>Time control</b>: a local <see cref="MutableTimeProvider"/> is used (the
/// <c>Microsoft.Extensions.Time.Testing</c> NuGet is intentionally NOT referenced by this
/// project per ADR-029 publish-size — matching the existing local-fake pattern in
/// <c>EventPathUserStateTests</c>). No wall-clock sleeps (NFR-08).
/// </para>
/// </summary>
public class SpeSyncOrchestratorTests
{
    private const string ContainerId = "b!container-xyz";
    private const string DriveId = "b!drive-xyz";
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-07-08T12:00:00Z");

    private readonly Mock<ISpeFileOperations> _facade = new(MockBehavior.Loose);
    private readonly IDistributedCache _cache =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly MutableTimeProvider _time = new(T0);

    private SpeSyncOrchestrator CreateSut() => new(
        _facade.Object,
        _cache,
        BuildConfig(notificationUrl: "https://bff.example.com/api/compose/webhooks/spe-doc-changed"),
        NullLogger<SpeSyncOrchestrator>.Instance,
        _time);

    // =========================================================================
    // EnsureSubscriptionAsync — create + Redis persistence
    // =========================================================================

    [Fact]
    public async Task EnsureSubscriptionAsync_WhenCreated_PersistsStateToRedisAndRoundTrips()
    {
        // Arrange
        _facade.Setup(f => f.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveId);
        _facade.Setup(f => f.CreateDriveRootSubscriptionAsync(
                DriveId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string drive, string url, string cs, DateTimeOffset exp, CancellationToken _) =>
                new SpeSubscriptionDto("sub-1", $"drives/{drive}/root", exp));

        var sut = CreateSut();

        // Act
        var state = await sut.EnsureSubscriptionAsync(ContainerId);

        // Assert — returned state carries the new subscription, no fallback
        state.SubscriptionId.Should().Be("sub-1");
        state.ExpiresAtUtc.Should().Be(T0.Add(SpeSyncOrchestrator.SubscriptionLifetime));
        state.FallbackToPolling.Should().BeFalse();

        // Assert — state actually round-trips through the (Redis) distributed cache
        var reloaded = await sut.GetStateAsync(ContainerId);
        reloaded.Should().NotBeNull();
        reloaded!.SubscriptionId.Should().Be("sub-1");
        reloaded.DriveId.Should().Be(DriveId);
        reloaded.ExpiresAtUtc.Should().Be(state.ExpiresAtUtc);
    }

    [Fact]
    public async Task EnsureSubscriptionAsync_WhenAlreadyHealthy_DoesNotRecreate()
    {
        // Arrange — first ensure creates the subscription
        _facade.Setup(f => f.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveId);
        _facade.Setup(f => f.CreateDriveRootSubscriptionAsync(
                DriveId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string drive, string url, string cs, DateTimeOffset exp, CancellationToken _) =>
                new SpeSubscriptionDto("sub-1", $"drives/{drive}/root", exp));

        var sut = CreateSut();
        await sut.EnsureSubscriptionAsync(ContainerId);

        // Act — a second ensure while the subscription is healthy
        await sut.EnsureSubscriptionAsync(ContainerId);

        // Assert — create was NOT called a second time (idempotent)
        _facade.Verify(f => f.CreateDriveRootSubscriptionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureSubscriptionAsync_WhenCreateThrows_FlagsFallbackAndDoesNotThrow()
    {
        // Arrange — Graph rejects the create; system must degrade, not crash
        _facade.Setup(f => f.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveId);
        _facade.Setup(f => f.CreateDriveRootSubscriptionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph 403"));

        var sut = CreateSut();

        // Act
        var act = async () => await sut.EnsureSubscriptionAsync(ContainerId);

        // Assert — no throw; persisted state flagged for delta-poll fallback
        var state = await act.Should().NotThrowAsync();
        state.Subject.FallbackToPolling.Should().BeTrue();
        state.Subject.SubscriptionId.Should().BeNull();

        var reloaded = await sut.GetStateAsync(ContainerId);
        reloaded!.FallbackToPolling.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSubscriptionAsync_WhenNotificationUrlMissing_FlagsFallbackWithoutCallingGraph()
    {
        // Arrange — no webhook URL configured
        _facade.Setup(f => f.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveId);

        var sut = new SpeSyncOrchestrator(
            _facade.Object, _cache, BuildConfig(notificationUrl: null),
            NullLogger<SpeSyncOrchestrator>.Instance, _time);

        // Act
        var state = await sut.EnsureSubscriptionAsync(ContainerId);

        // Assert — degrades to polling; never attempted a Graph create
        state.FallbackToPolling.Should().BeTrue();
        _facade.Verify(f => f.CreateDriveRootSubscriptionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // RenewDueSubscriptionsAsync — renewal-window logic via TimeProvider
    // =========================================================================

    [Fact]
    public async Task RenewDueSubscriptionsAsync_WhenWithinRenewalMargin_RenewsAndAdvancesExpiry()
    {
        // Arrange — create at T0 (expiry T0+4230min)
        var sut = await ArrangeHealthySubscriptionAsync();

        // Advance to inside the 120-min renewal margin (30 min before expiry)
        _time.Advance(SpeSyncOrchestrator.SubscriptionLifetime - TimeSpan.FromMinutes(30));
        var renewalNow = _time.GetUtcNow();

        _facade.Setup(f => f.RenewSubscriptionAsync("sub-1", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, DateTimeOffset exp, CancellationToken _) =>
                new SpeSubscriptionDto(id, $"drives/{DriveId}/root", exp));

        // Act
        await sut.RenewDueSubscriptionsAsync();

        // Assert — renewed exactly once, expiry advanced to now+lifetime
        _facade.Verify(f => f.RenewSubscriptionAsync("sub-1", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        var reloaded = await sut.GetStateAsync(ContainerId);
        reloaded!.ExpiresAtUtc.Should().Be(renewalNow.Add(SpeSyncOrchestrator.SubscriptionLifetime));
        reloaded.FallbackToPolling.Should().BeFalse();
    }

    [Fact]
    public async Task RenewDueSubscriptionsAsync_WhenFarFromExpiry_DoesNotRenew()
    {
        // Arrange — create at T0; time not advanced, so expiry is ~4230min away (>> margin)
        var sut = await ArrangeHealthySubscriptionAsync();

        // Act
        await sut.RenewDueSubscriptionsAsync();

        // Assert — nothing renewed (renewal window not yet entered)
        _facade.Verify(f => f.RenewSubscriptionAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenewDueSubscriptionsAsync_WhenRenewalThrows_FlagsFallbackAndDoesNotThrow()
    {
        // Arrange — subscription is due for renewal, but Graph rejects it (e.g. 404 gone)
        var sut = await ArrangeHealthySubscriptionAsync();
        _time.Advance(SpeSyncOrchestrator.SubscriptionLifetime - TimeSpan.FromMinutes(10));

        _facade.Setup(f => f.RenewSubscriptionAsync("sub-1", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph 404 subscription gone"));

        // Act
        var act = async () => await sut.RenewDueSubscriptionsAsync();

        // Assert — the host is never crashed; the subscription is flagged for poll fallback
        await act.Should().NotThrowAsync();
        var reloaded = await sut.GetStateAsync(ContainerId);
        reloaded!.FallbackToPolling.Should().BeTrue();
    }

    // =========================================================================
    // EnumerateChangesAsync — delta enumeration + token advance + etag suppression
    // =========================================================================

    [Fact]
    public async Task EnumerateChangesAsync_ReturnsChangedItemsAndAdvancesDeltaLink()
    {
        // Arrange
        var sut = await ArrangeHealthySubscriptionAsync();
        _facade.Setup(f => f.EnumerateDriveDeltaAsync(DriveId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeDeltaResult(
                new[] { new SpeDriveChange("item-1", "contract.docx", "etag-1", Deleted: false) },
                DeltaLink: "https://graph/delta?token=T1"));

        // Act
        var changes = await sut.EnumerateChangesAsync(ContainerId);

        // Assert — the changed item is returned and the delta token is persisted for next round
        changes.Should().ContainSingle(c => c.ItemId == "item-1");
        var reloaded = await sut.GetStateAsync(ContainerId);
        reloaded!.DeltaLink.Should().Be("https://graph/delta?token=T1");
        reloaded.ItemEtags.Should().ContainKey("item-1").WhoseValue.Should().Be("etag-1");
    }

    [Fact]
    public async Task EnumerateChangesAsync_WhenSameEtagReturnedAgain_SuppressesNoOpEcho()
    {
        // Arrange — first delta returns item-1@etag-1 and advances to token T1
        var sut = await ArrangeHealthySubscriptionAsync();
        _facade.Setup(f => f.EnumerateDriveDeltaAsync(DriveId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeDeltaResult(
                new[] { new SpeDriveChange("item-1", "contract.docx", "etag-1", false) }, "T1"));
        await sut.EnumerateChangesAsync(ContainerId);

        // Second delta (resumed from token T1) echoes the SAME item + etag — no net change
        _facade.Setup(f => f.EnumerateDriveDeltaAsync(DriveId, "T1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeDeltaResult(
                new[] { new SpeDriveChange("item-1", "contract.docx", "etag-1", false) }, "T2"));

        // Act
        var secondRound = await sut.EnumerateChangesAsync(ContainerId);

        // Assert — echo suppressed, but the delta token still advances to T2
        secondRound.Should().BeEmpty("an unchanged etag is a no-op echo, not a real change");
        var reloaded = await sut.GetStateAsync(ContainerId);
        reloaded!.DeltaLink.Should().Be("T2");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<SpeSyncOrchestrator> ArrangeHealthySubscriptionAsync()
    {
        _facade.Setup(f => f.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveId);
        _facade.Setup(f => f.CreateDriveRootSubscriptionAsync(
                DriveId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string drive, string url, string cs, DateTimeOffset exp, CancellationToken _) =>
                new SpeSubscriptionDto("sub-1", $"drives/{drive}/root", exp));

        var sut = CreateSut();
        await sut.EnsureSubscriptionAsync(ContainerId);
        return sut;
    }

    private static IConfiguration BuildConfig(string? notificationUrl) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Compose:Webhook:NotificationUrl"] = notificationUrl,
            ["Compose:Webhook:ClientState"] = "test-client-state",
        })
        .Build();

    /// <summary>
    /// Local deterministic <see cref="TimeProvider"/> fake (the
    /// <c>Microsoft.Extensions.Time.Testing</c> NuGet is not referenced by this test project).
    /// </summary>
    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public MutableTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
