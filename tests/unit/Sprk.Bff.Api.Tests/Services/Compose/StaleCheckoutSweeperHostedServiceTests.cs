using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="StaleCheckoutSweeperHostedService"/> — the Compose R1
/// Spike #3 §4.3 background sweeper that releases SPE checkouts whose client-side
/// heartbeat has gone stale.
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> (orchestration logic over a
/// mocked module boundary — the virtual methods on <see cref="DocumentCheckoutService"/>
/// that the sweeper consumes). The sweeper itself is pure orchestration: scan probe →
/// per-row release → telemetry. Per <c>docs/standards/TEST-ARCHITECTURE.md</c> §3 row 6
/// the unit-domain bucket covers handler-internal orchestration where the boundary is a
/// legitimate Spaarke-defined facade (here: <c>DocumentCheckoutService</c>, mocked at
/// its two virtual methods <see cref="DocumentCheckoutService.GetStaleCheckedOutDocumentsAsync"/>
/// and <see cref="DocumentCheckoutService.ReleaseCheckoutSystemAsync"/>).
/// </para>
///
/// <para>
/// <b>Mocking strategy</b>: <see cref="Mock{T}"/> against <see cref="DocumentCheckoutService"/>
/// itself, leveraging the two <c>virtual</c> seams added in task 052. No
/// <c>Mock&lt;HttpMessageHandler&gt;</c> (B1 banned per ADR-038 §4 + <c>tests/CLAUDE.md</c>).
/// No DI-registration tests (B3 banned). No ctor null-check tests (B4 banned). No
/// interaction-shape-only Verify.Once() assertions (B7 banned) — every assertion
/// captures observable behavior: release-method call list, release-count totals,
/// resilience-under-failure.
/// </para>
///
/// <para>
/// <b>Tested via</b> <see cref="StaleCheckoutSweeperHostedService.ScanAndReleaseStaleOnceAsync(DocumentCheckoutService, DateTime, CancellationToken)"/>
/// — the internal pure-orchestration variant that takes the resolved service directly
/// (avoids touching the DI container or the 2-min Task.Delay loop).
/// </para>
/// </summary>
public class StaleCheckoutSweeperHostedServiceTests
{
    private readonly Mock<DocumentCheckoutService> _checkoutServiceMock;
    private readonly StaleCheckoutSweeperHostedService _sut;

    public StaleCheckoutSweeperHostedServiceTests()
    {
        // Build a Mock<DocumentCheckoutService> that calls into the base constructor.
        // The base ctor takes HttpClient + SpeFileStore + IConfiguration + TokenCredential
        // + ILogger; none are touched at test time because we only invoke the two virtual
        // methods, which are .Setup-overridden below per-test.
        var httpClient = new HttpClient();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://example.crm.dynamics.com/",
            })
            .Build();
        var credential = new Mock<TokenCredential>().Object;
        var loggerMock = NullLogger<DocumentCheckoutService>.Instance;

        // SpeFileStore is unused by the two methods we exercise; the base ctor doesn't
        // touch it either. Pass null! since the base ctor merely assigns it to a field.
        _checkoutServiceMock = new Mock<DocumentCheckoutService>(
            httpClient,
            null!, // SpeFileStore - not touched by virtual seams under test
            config,
            credential,
            loggerMock)
        {
            CallBase = false, // override all virtuals; we drive observation through .Setup
        };

        _sut = new StaleCheckoutSweeperHostedService(
            serviceProvider: BuildEmptyServiceProvider(),
            logger: NullLogger<StaleCheckoutSweeperHostedService>.Instance,
            timeProvider: TimeProvider.System);
    }

    // =========================================================================
    // ScanAndReleaseStaleOnceAsync — mark-and-sweep flow
    // =========================================================================

    [Fact]
    public async Task ScanAndReleaseStaleOnceAsync_WhenNoStaleCandidates_DoesNotCallRelease()
    {
        // Arrange — probe returns empty list (steady state, no stale checkouts)
        var cutoffUtc = new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
        _checkoutServiceMock
            .Setup(x => x.GetStaleCheckedOutDocumentsAsync(cutoffUtc, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        // Act
        await _sut.ScanAndReleaseStaleOnceAsync(_checkoutServiceMock.Object, cutoffUtc, CancellationToken.None);

        // Assert — observable behavior: zero release calls when no stale candidates
        _checkoutServiceMock.Verify(
            x => x.ReleaseCheckoutSystemAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanAndReleaseStaleOnceAsync_WhenStaleCandidatesFound_ReleasesEachExactlyOnce()
    {
        // Arrange — probe returns 3 stale doc ids
        var cutoffUtc = new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
        var staleIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        _checkoutServiceMock
            .Setup(x => x.GetStaleCheckedOutDocumentsAsync(cutoffUtc, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleIds);

        _checkoutServiceMock
            .Setup(x => x.ReleaseCheckoutSystemAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.ScanAndReleaseStaleOnceAsync(_checkoutServiceMock.Object, cutoffUtc, CancellationToken.None);

        // Assert — observable behavior: each stale id was released exactly once
        foreach (var id in staleIds)
        {
            // Each stale candidate must be released exactly once per scan iteration.
            _checkoutServiceMock.Verify(
                x => x.ReleaseCheckoutSystemAsync(id, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task ScanAndReleaseStaleOnceAsync_WhenOneReleaseFails_ContinuesToReleaseRemaining()
    {
        // Arrange — probe returns 3 ids; second release throws, sweeper must continue.
        // This is the resilience invariant from Spike #3 §4.3: "each individual stale-row
        // release is also try-wrapped so a single bad row doesn't abort the whole iteration."
        var cutoffUtc = new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid(); // this one will throw
        var idC = Guid.NewGuid();

        _checkoutServiceMock
            .Setup(x => x.GetStaleCheckedOutDocumentsAsync(cutoffUtc, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { idA, idB, idC });

        _checkoutServiceMock
            .Setup(x => x.ReleaseCheckoutSystemAsync(idA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _checkoutServiceMock
            .Setup(x => x.ReleaseCheckoutSystemAsync(idB, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse transient failure"));
        _checkoutServiceMock
            .Setup(x => x.ReleaseCheckoutSystemAsync(idC, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act — the sweeper MUST swallow the per-row exception and continue
        var iteration = async () => await _sut.ScanAndReleaseStaleOnceAsync(
            _checkoutServiceMock.Object, cutoffUtc, CancellationToken.None);
        await iteration.Should().NotThrowAsync("the sweeper must swallow per-row failures and continue");

        // Assert — observable behavior: idA AND idC were released despite idB throwing
        _checkoutServiceMock.Verify(
            x => x.ReleaseCheckoutSystemAsync(idA, It.IsAny<CancellationToken>()),
            Times.Once);
        _checkoutServiceMock.Verify(
            x => x.ReleaseCheckoutSystemAsync(idC, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanAndReleaseStaleOnceAsync_WhenReleaseReturnsFalse_DoesNotThrow()
    {
        // Arrange — probe returns 1 id, but the doc has already been released (race
        // with a concurrent check-in). The sweeper must treat false as a benign skip,
        // not an error. From Spike #3 §4.3 + DocumentCheckoutService.ReleaseCheckoutSystemAsync:
        // "false if doc gone / not checked out (already released)".
        var cutoffUtc = new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
        var id = Guid.NewGuid();

        _checkoutServiceMock
            .Setup(x => x.GetStaleCheckedOutDocumentsAsync(cutoffUtc, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { id });
        _checkoutServiceMock
            .Setup(x => x.ReleaseCheckoutSystemAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // already released by a concurrent check-in

        // Act
        var iteration = async () => await _sut.ScanAndReleaseStaleOnceAsync(
            _checkoutServiceMock.Object, cutoffUtc, CancellationToken.None);

        // Assert — observable: completes cleanly, the false is logged but doesn't throw
        await iteration.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ScanAndReleaseStaleOnceAsync_PassesMaxRowsCapToProbe()
    {
        // Arrange — sweeper MUST cap per-iteration scan at MaxRowsPerIteration (100).
        // Verifies the cap is propagated to the probe — bounds the cost of a single
        // sweep pass after a long BFF outage backlog (Spike #3 §4.3 cost analysis).
        var cutoffUtc = new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
        _checkoutServiceMock
            .Setup(x => x.GetStaleCheckedOutDocumentsAsync(cutoffUtc, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        // Act
        await _sut.ScanAndReleaseStaleOnceAsync(_checkoutServiceMock.Object, cutoffUtc, CancellationToken.None);

        // Assert — observable: probe was called with the locked cap value (100).
        _checkoutServiceMock.Verify(
            x => x.GetStaleCheckedOutDocumentsAsync(
                cutoffUtc,
                StaleCheckoutSweeperHostedService.MaxRowsPerIteration,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanAndReleaseStaleOnceAsync_WhenCancelled_StopsProcessingRemaining()
    {
        // Arrange — probe returns 5 ids; we cancel after the first release succeeds.
        // Sweeper must honor cancellation between releases (per Spike #3 §4.3 — the
        // foreach checks ct.IsCancellationRequested before each iteration).
        var cutoffUtc = new DateTime(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var cts = new CancellationTokenSource();

        _checkoutServiceMock
            .Setup(x => x.GetStaleCheckedOutDocumentsAsync(cutoffUtc, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids);

        var releasedCount = 0;
        _checkoutServiceMock
            .Setup(x => x.ReleaseCheckoutSystemAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                releasedCount++;
                if (releasedCount == 1)
                {
                    cts.Cancel(); // cancel after first release completes
                }
                await Task.Yield();
                return true;
            });

        // Act
        await _sut.ScanAndReleaseStaleOnceAsync(_checkoutServiceMock.Object, cutoffUtc, cts.Token);

        // Assert — observable: NOT all 5 were released. The exact count is sensitive to
        // scheduling, but we verify at minimum the cancellation broke the loop early.
        releasedCount.Should().BeLessThan(ids.Length,
            because: "cancellation must break the foreach loop before all candidates are processed");
    }

    // =========================================================================
    // Configuration invariants — Spike #3 §4.3 locked constants
    // =========================================================================

    [Fact]
    public void StaleThreshold_IsLockedAt_15_Minutes()
    {
        // Spike #3 §1 + spec FR-17 + design.md §14 row 4 — locked at 15 min.
        // This test is a forcing function: if the constant changes without a spike
        // amendment, this test fails and forces the change to surface.
        StaleCheckoutSweeperHostedService.StaleThreshold
            .Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void ScanInterval_IsLockedAt_2_Minutes()
    {
        // Spike #3 §4.3 — locked at 2 min. Same forcing-function rationale as above.
        StaleCheckoutSweeperHostedService.ScanInterval
            .Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void MaxOrphanLifetime_DerivedFromConstants_DoesNotExceed_17_Minutes()
    {
        // Spike #3 §1 contract — "≤17-min max orphan lifetime" (15 stale + 2 scan).
        // If either constant is tweaked, this contract test enforces that the sum
        // stays within the locked ceiling.
        var maxOrphan = StaleCheckoutSweeperHostedService.StaleThreshold
                      + StaleCheckoutSweeperHostedService.ScanInterval;

        maxOrphan.Should().BeLessOrEqualTo(TimeSpan.FromMinutes(17),
            because: "Spike #3 §1 locks the max orphan lifetime ceiling at 17 minutes");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static IServiceProvider BuildEmptyServiceProvider()
    {
        // Used only for the ctor; tests drive ScanAndReleaseStaleOnceAsync directly,
        // never the DI-resolving ScanAndReleaseStaleAsync, so this is unused at runtime.
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }
}
