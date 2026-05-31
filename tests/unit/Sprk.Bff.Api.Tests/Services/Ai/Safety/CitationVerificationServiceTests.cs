using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Safety;

/// <summary>
/// Unit tests for <see cref="CitationVerificationService"/>.
///
/// Test matrix:
///   1. Provider selected per type — CaseLaw citation routed to CaseLaw provider
///   2. Multiple providers — each citation routes to the correct provider
///   3. No provider for type — returns unverified (not an exception)
///   4. Empty provider list — all citations return NoProvider
///   5. Provider exception — caught per-citation, returns error result; others continue
///   6. OperationCanceledException — re-thrown (not swallowed)
///   7. Empty text — returns empty report
///   8. Extract() delegates to CitationExtractor
///   9. Verified / Unverified / Errors partitioned correctly in report
/// </summary>
[Trait("status", "repaired")]
public class CitationVerificationServiceTests
{
    private readonly ILogger<CitationVerificationService> _logger =
        NullLogger<CitationVerificationService>.Instance;

    // =========================================================================
    // Helper: build a stub IVerificationProvider
    // =========================================================================

    private static Mock<IVerificationProvider> BuildProvider(
        string name,
        CitationType[] supportedTypes,
        CitationVerificationResult? fixedResult = null,
        Exception? throws = null)
    {
        var mock = new Mock<IVerificationProvider>();
        mock.SetupGet(p => p.ProviderName).Returns(name);
        mock.SetupGet(p => p.SupportedTypes).Returns(supportedTypes);
        mock.Setup(p => p.CanVerify(It.IsAny<CitationType>()))
            .Returns<CitationType>(t => supportedTypes.Contains(t));

        if (throws is not null)
        {
            mock.Setup(p => p.VerifyAsync(It.IsAny<Citation>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(throws);
        }
        else
        {
            mock.Setup(p => p.VerifyAsync(It.IsAny<Citation>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Citation c, CancellationToken _) =>
                    fixedResult ?? new CitationVerificationResult(
                        Citation: c,
                        IsVerified: true,
                        ConfidenceScore: 0.95f,
                        SourceUrl: "https://example.com/cite",
                        VerifiedText: "Verified excerpt.",
                        VerificationProvider: name,
                        LatencyMs: 42.0));
        }

        return mock;
    }

    // =========================================================================
    // 1. Provider selected by type
    // =========================================================================

    [Fact]
    public async Task VerifyAllAsync_CaseLawCitation_RoutedToCaseLawProvider()
    {
        var caseLawProvider = BuildProvider("CourtListener", [CitationType.CaseLaw]);
        var statuteProvider = BuildProvider("StatuteProvider", [CitationType.Statute]);

        var sut = new CitationVerificationService(
            [caseLawProvider.Object, statuteProvider.Object], _logger);

        // Text contains a case law citation only.
        const string text = "See Roe v. Wade, 410 U.S. 113 (1973).";

        var report = await sut.VerifyAllAsync(text, CancellationToken.None);

        report.Verified.Should().HaveCount(1);
        report.Verified[0].VerificationProvider.Should().Be("CourtListener");

        caseLawProvider.Verify(
            p => p.VerifyAsync(It.IsAny<Citation>(), It.IsAny<CancellationToken>()),
            Times.Once);
        statuteProvider.Verify(
            p => p.VerifyAsync(It.IsAny<Citation>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================================
    // 2. Multiple providers — each routes correctly
    // =========================================================================

    [Fact]
    public async Task VerifyAllAsync_MultipleTypes_EachRoutedToCorrectProvider()
    {
        var caseLawProvider = BuildProvider("CourtListener", [CitationType.CaseLaw]);
        var statuteProvider = BuildProvider("LexisStatute",  [CitationType.Statute]);

        var sut = new CitationVerificationService(
            [caseLawProvider.Object, statuteProvider.Object], _logger);

        // Text contains both a statute and a case law citation.
        const string text =
            "Under 35 U.S.C. § 101 (statute) and Alice Corp. v. CLS Bank Int'l, 573 U.S. 208 (2014) (case law).";

        var report = await sut.VerifyAllAsync(text, CancellationToken.None);

        report.Verified.Should().HaveCountGreaterThanOrEqualTo(2);
        report.Verified.Should().Contain(r => r.VerificationProvider == "CourtListener");
        report.Verified.Should().Contain(r => r.VerificationProvider == "LexisStatute");
    }

    // =========================================================================
    // 3. No provider for type — unverified, not exception
    // =========================================================================

    [Fact]
    public async Task VerifyAllAsync_NoProviderForType_ReturnsUnverifiedNotException()
    {
        // Only a CaseLaw provider registered; text contains a Patent citation.
        var caseLawProvider = BuildProvider("CourtListener", [CitationType.CaseLaw]);
        var sut = new CitationVerificationService([caseLawProvider.Object], _logger);

        const string text = "The patent U.S. Patent No. 9,123,456 was asserted.";

        var report = await sut.VerifyAllAsync(text, CancellationToken.None);

        report.Errors.Should().BeEmpty();
        report.Unverified.Should().ContainSingle();
        report.Unverified[0].VerificationProvider.Should().Be("none");
        report.Unverified[0].IsVerified.Should().BeFalse();
    }

    // =========================================================================
    // 4. Empty provider list — all NoProvider
    // =========================================================================

    [Fact]
    public async Task VerifyAllAsync_NoProviders_AllCitationsReturnNoProvider()
    {
        var sut = new CitationVerificationService([], _logger);
        const string text = "See Roe v. Wade, 410 U.S. 113 (1973) and 35 U.S.C. § 101.";

        var report = await sut.VerifyAllAsync(text, CancellationToken.None);

        report.Verified.Should().BeEmpty();
        report.Errors.Should().BeEmpty();
        report.Unverified.Should().NotBeEmpty();
        report.Unverified.Should().AllSatisfy(r =>
        {
            r.IsVerified.Should().BeFalse();
            r.VerificationProvider.Should().Be("none");
        });
    }

    // =========================================================================
    // 5. Provider exception — per-citation error, others continue
    // =========================================================================

    [Fact]
    public async Task VerifyAllAsync_ProviderThrows_ReturnsErrorResult_OthersContinue()
    {
        var faultyProvider = BuildProvider(
            "FaultyProvider",
            [CitationType.CaseLaw],
            throws: new InvalidOperationException("Upstream unavailable"));

        var goodProvider = BuildProvider("StatuteProvider", [CitationType.Statute]);

        var sut = new CitationVerificationService(
            [faultyProvider.Object, goodProvider.Object], _logger);

        const string text =
            "See Roe v. Wade, 410 U.S. 113 (1973). Also see 35 U.S.C. § 101.";

        var report = await sut.VerifyAllAsync(text, CancellationToken.None);

        // CaseLaw → error (provider threw)
        report.Errors.Should().ContainSingle();
        report.Errors[0].VerificationProvider.Should().Be("error");
        report.Errors[0].IsVerified.Should().BeFalse();
        report.Errors[0].ErrorMessage.Should().Contain("FaultyProvider");

        // Statute → verified (good provider succeeded)
        report.Verified.Should().ContainSingle();
        report.Verified[0].VerificationProvider.Should().Be("StatuteProvider");
    }

    // =========================================================================
    // 6. OperationCanceledException is re-thrown
    // =========================================================================

    [Fact]
    public async Task VerifyAllAsync_ProviderCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancellingProvider = BuildProvider(
            "SlowProvider",
            [CitationType.CaseLaw],
            throws: new OperationCanceledException(cts.Token));

        var sut = new CitationVerificationService([cancellingProvider.Object], _logger);
        const string text = "See Roe v. Wade, 410 U.S. 113 (1973).";

        Func<Task> act = () => sut.VerifyAllAsync(text, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================
    // 7. Empty text — empty report
    // =========================================================================

    [Fact]
    public async Task VerifyAllAsync_EmptyText_ReturnsEmptyReport()
    {
        var provider = BuildProvider("CourtListener", [CitationType.CaseLaw]);
        var sut = new CitationVerificationService([provider.Object], _logger);

        var report = await sut.VerifyAllAsync(string.Empty, CancellationToken.None);

        report.TotalCitations.Should().Be(0);
        report.Verified.Should().BeEmpty();
        report.Unverified.Should().BeEmpty();
        report.Errors.Should().BeEmpty();
    }

    // =========================================================================
    // 8. Extract() delegates to CitationExtractor
    // =========================================================================

    [Fact]
    public void Extract_DelegatesToCitationExtractor()
    {
        var sut = new CitationVerificationService([], _logger);
        const string text = "See Roe v. Wade, 410 U.S. 113 (1973).";

        var citations = sut.Extract(text);

        citations.Should().NotBeEmpty();
        citations.Should().Contain(c => c.CitationType == CitationType.CaseLaw);
    }

    // =========================================================================
    // 9. Report partitioning
    // =========================================================================

    [Fact]
    public async Task VerifyAllAsync_Report_All_ContainsUnionOfAllBuckets()
    {
        // One provider for CaseLaw (will verify), none for Patent (→ unverified).
        var caseLawProvider = BuildProvider("CourtListener", [CitationType.CaseLaw]);
        var sut = new CitationVerificationService([caseLawProvider.Object], _logger);

        const string text =
            "See Roe v. Wade, 410 U.S. 113 (1973). Patent No. U.S. Patent No. 9,123,456.";

        var report = await sut.VerifyAllAsync(text, CancellationToken.None);

        // `report.All` must be the union of Verified + Unverified + Errors.
        // Per-bucket assertions guard each non-empty bucket separately; the empty bucket
        // (Errors, in this scenario — no provider throws) is asserted to be empty rather than
        // via `Contain([])`, which FluentAssertions rejects with ArgumentException.
        report.All.Should().HaveCount(report.TotalCitations);
        report.Verified.Should().NotBeEmpty();
        report.All.Should().Contain(report.Verified);
        report.Unverified.Should().NotBeEmpty();
        report.All.Should().Contain(report.Unverified);
        report.Errors.Should().BeEmpty(
            because: "no provider throws in this scenario");
    }
}
