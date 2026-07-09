using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="SpeWebhookRenewalHostedService"/> — the FR-26 BackgroundService
/// that keeps SPE subscriptions alive under the 4230-min lifespan (ADR-001, modeled on
/// <see cref="StaleCheckoutSweeperHostedService"/>).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> — the hosted service is pure delegation
/// to <see cref="SpeSyncOrchestrator"/>; tested via the internal
/// <see cref="SpeWebhookRenewalHostedService.RenewDueOnceAsync"/> seam (avoids the
/// <see cref="SpeWebhookRenewalHostedService.ScanInterval"/> loop). The orchestrator is mocked
/// at its <c>virtual RenewDueSubscriptionsAsync</c> seam (same convention as the sweeper
/// mocking <c>DocumentCheckoutService</c>). No <c>Mock&lt;HttpMessageHandler&gt;</c> (B1),
/// no DI-registration tests (B3), no ctor null-check tests (B4).
/// </para>
///
/// <para>The renewal-window decision + failure→fallback resilience live in the orchestrator
/// and are covered by <see cref="SpeSyncOrchestratorTests"/>; here we assert delegation and
/// the cadence forcing-functions.</para>
/// </summary>
public class SpeWebhookRenewalHostedServiceTests
{
    // =========================================================================
    // Delegation
    // =========================================================================

    [Fact]
    public async Task RenewDueOnceAsync_InvokesOrchestratorRenewal()
    {
        // Arrange
        var orchestrator = BuildOrchestratorMock();
        orchestrator.Setup(o => o.RenewDueSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new SpeWebhookRenewalHostedService(
            serviceProvider: EmptyProvider(),
            logger: NullLogger<SpeWebhookRenewalHostedService>.Instance,
            timeProvider: TimeProvider.System);

        // Act
        await sut.RenewDueOnceAsync(orchestrator.Object, CancellationToken.None);

        // Assert — observable behavior: the single iteration drives one renewal sweep
        orchestrator.Verify(o => o.RenewDueSubscriptionsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Cadence forcing-functions — a renewal that scans too infrequently misses the window
    // =========================================================================

    [Fact]
    public void ScanInterval_IsFarBelow_SubscriptionLifespan()
    {
        // If the scan cadence ever creeps toward the 4230-min lifespan, subscriptions would
        // expire between scans. This forcing-function locks the safety gap.
        SpeWebhookRenewalHostedService.ScanInterval
            .Should().BeLessThan(SpeSyncOrchestrator.SubscriptionLifetime);
    }

    [Fact]
    public void ScanInterval_IsBelow_RenewalMargin()
    {
        // The scan interval MUST be below the renewal margin so the renewal window is never
        // skipped between two consecutive scans.
        SpeWebhookRenewalHostedService.ScanInterval
            .Should().BeLessThan(SpeSyncOrchestrator.RenewalMargin);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static Mock<SpeSyncOrchestrator> BuildOrchestratorMock()
    {
        // Mock the concrete orchestrator at its virtual RenewDueSubscriptionsAsync seam.
        // The ctor args are inert here (the virtual is .Setup-overridden), mirroring the
        // sweeper test's Mock<DocumentCheckoutService>(...) construction.
        var facade = new Mock<ISpeFileOperations>().Object;
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var config = new ConfigurationBuilder().Build();

        return new Mock<SpeSyncOrchestrator>(
            facade,
            cache,
            config,
            NullLogger<SpeSyncOrchestrator>.Instance,
            TimeProvider.System)
        {
            CallBase = false,
        };
    }

    private static IServiceProvider EmptyProvider() => new ServiceCollection().BuildServiceProvider();
}
