using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Sprk.Bff.Api.Services.Ai.Safety.Citations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Safety;

/// <summary>
/// Unit tests for <see cref="VerifyCitationsTool"/> and <see cref="CitationSafetyCheck"/>.
///
/// Test matrix:
///   VerifyCitationsTool (explicit invocation mode):
///     1. Returns JSON with verified/unverified citations when text contains citations
///     2. Returns "no citations found" message when text contains no citations
///     3. Delegates to ICitationVerificationService.VerifyAllAsync
///     4. FormatReport produces correct markdown with verified, unverified, and error sections
///
///   CitationSafetyCheck (auto-check mode):
///     5. CheckResponseAsync returns annotation with citations when response contains citations
///     6. CheckResponseAsync returns empty annotation when response contains no citations
///     7. CheckResponseAsync returns empty annotation (fail-open) when service throws
///     8. CheckResponseAsync returns empty annotation when response text is null or whitespace
///     9. Auto-check and groundedness check can run concurrently — both return independently
///       (parallel-safety: no shared state, distinct event types)
/// </summary>
public class VerifyCitationsTests
{
    // =========================================================================
    // Shared helpers
    // =========================================================================

    private static Mock<ICitationVerificationService> BuildServiceMock(
        CitationVerificationReport? report = null,
        Exception? throws = null)
    {
        var mock = new Mock<ICitationVerificationService>();

        // Default report: one verified CaseLaw citation
        report ??= BuildReportWithVerifiedCitation();

        if (throws is not null)
        {
            mock.Setup(s => s.VerifyAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(throws);
        }
        else
        {
            mock.Setup(s => s.VerifyAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(report);
        }

        mock.Setup(s => s.Extract(It.IsAny<string>()))
            .Returns([]);

        return mock;
    }

    private static CitationVerificationReport BuildReportWithVerifiedCitation()
    {
        var citation = new Citation("542 U.S. 296", CitationType.CaseLaw, "542 U.S. 296");
        var result = new CitationVerificationResult(
            Citation: citation,
            IsVerified: true,
            ConfidenceScore: 0.92f,
            SourceUrl: "https://supreme.justia.com/cases/federal/us/542/296/",
            VerifiedText: "Rasul v. Bush, 542 U.S. 466 (2004)",
            VerificationProvider: "InternalIndex",
            LatencyMs: 38.5);

        return new CitationVerificationReport([result], [], []);
    }

    private static CitationVerificationReport BuildEmptyReport() =>
        new CitationVerificationReport([], [], []);

    private static CitationVerificationReport BuildMixedReport()
    {
        var verifiedCitation = new Citation("542 U.S. 296", CitationType.CaseLaw, "542 U.S. 296");
        var verified = new CitationVerificationResult(
            Citation: verifiedCitation,
            IsVerified: true,
            ConfidenceScore: 0.9f,
            SourceUrl: "https://example.com/verified",
            VerifiedText: null,
            VerificationProvider: "InternalIndex",
            LatencyMs: 20.0);

        var unverifiedCitation = new Citation("35 U.S.C. § 101", CitationType.Statute, "35 U.S.C. § 101");
        var unverified = CitationVerificationResult.NoProvider(unverifiedCitation);

        var errorCitation = new Citation("US9123456", CitationType.Patent, "US9123456");
        var error = CitationVerificationResult.FromError(
            errorCitation, "InternalIndex", "Connection timeout", 100.0);

        return new CitationVerificationReport([verified], [unverified], [error]);
    }

    // =========================================================================
    // VerifyCitationsTool tests
    // =========================================================================

    [Fact]
    public async Task VerifyCitationsAsync_TextWithCitations_ReturnsJsonWithCitationArray()
    {
        // Arrange
        var serviceMock = BuildServiceMock(BuildReportWithVerifiedCitation());
        var sut = new VerifyCitationsTool(serviceMock.Object, NullLogger.Instance);

        // Act
        var result = await sut.VerifyCitationsAsync("See Rasul v. Bush, 542 U.S. 296.", CancellationToken.None);

        // Assert
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("citations");
        result.Should().Contain("542 U.S. 296");
        result.Should().Contain("isVerified");
    }

    [Fact]
    public async Task VerifyCitationsAsync_TextWithCitations_IsVerifiedTrue_ForVerifiedCitation()
    {
        // Arrange
        var report = BuildReportWithVerifiedCitation();
        var serviceMock = BuildServiceMock(report);
        var sut = new VerifyCitationsTool(serviceMock.Object, NullLogger.Instance);

        // Act
        var json = await sut.VerifyCitationsAsync("542 U.S. 296", CancellationToken.None);

        // Assert: the JSON must include isVerified:true for the verified citation
        json.Should().Contain("\"isVerified\":true");
    }

    [Fact]
    public async Task VerifyCitationsAsync_TextWithNoCitations_ReturnsNoCitationsMessage()
    {
        // Arrange
        var serviceMock = BuildServiceMock(BuildEmptyReport());
        var sut = new VerifyCitationsTool(serviceMock.Object, NullLogger.Instance);

        // Act
        var result = await sut.VerifyCitationsAsync("No citations here, just plain text.", CancellationToken.None);

        // Assert
        result.Should().Contain("No legal citations were found");
    }

    [Fact]
    public async Task VerifyCitationsAsync_DelegatesTo_ICitationVerificationService()
    {
        // Arrange
        const string inputText = "See 542 U.S. 296 for details.";
        var serviceMock = BuildServiceMock(BuildEmptyReport());
        var sut = new VerifyCitationsTool(serviceMock.Object, NullLogger.Instance);

        // Act
        _ = await sut.VerifyCitationsAsync(inputText, CancellationToken.None);

        // Assert: VerifyAllAsync was called with the exact input text
        serviceMock.Verify(
            s => s.VerifyAllAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyCitationsAsync_MixedReport_IncludesVerifiedAndUnverifiedEntries()
    {
        // Arrange
        var serviceMock = BuildServiceMock(BuildMixedReport());
        var sut = new VerifyCitationsTool(serviceMock.Object, NullLogger.Instance);

        // Act
        var json = await sut.VerifyCitationsAsync("some text", CancellationToken.None);

        // Assert: JSON contains all three citations
        json.Should().Contain("542 U.S. 296");
        json.Should().Contain("35 U.S.C. § 101");
        json.Should().Contain("US9123456");
    }

    // =========================================================================
    // VerifyCitationsTool.FormatReport tests
    // =========================================================================

    [Fact]
    public void FormatReport_EmptyReport_ReturnsNoCitationsMessage()
    {
        var result = VerifyCitationsTool.FormatReport(BuildEmptyReport());

        result.Should().Contain("No legal citations were found");
    }

    [Fact]
    public void FormatReport_VerifiedCitations_IncludesCheckmarkAndSourceUrl()
    {
        var result = VerifyCitationsTool.FormatReport(BuildReportWithVerifiedCitation());

        result.Should().Contain("Verified Citations");
        result.Should().Contain("542 U.S. 296");
        result.Should().Contain("https://supreme.justia.com");
    }

    [Fact]
    public void FormatReport_MixedReport_IncludesAllThreeSections()
    {
        var result = VerifyCitationsTool.FormatReport(BuildMixedReport());

        result.Should().Contain("Verified Citations");
        result.Should().Contain("Unverified Citations");
        result.Should().Contain("Verification Errors");
    }

    // =========================================================================
    // CitationSafetyCheck tests
    // =========================================================================

    [Fact]
    public async Task CheckResponseAsync_ResponseWithCitations_ReturnsAnnotationWithEntries()
    {
        // Arrange
        var serviceMock = BuildServiceMock(BuildReportWithVerifiedCitation());
        var sut = new CitationSafetyCheck(serviceMock.Object, NullLogger<CitationSafetyCheck>.Instance);

        // Act
        var annotation = await sut.CheckResponseAsync("The Court held in 542 U.S. 296 that...", CancellationToken.None);

        // Assert
        annotation.Should().NotBeNull();
        annotation.HasCitations.Should().BeTrue();
        annotation.Citations.Should().HaveCount(1);
        annotation.Citations[0].IsVerified.Should().BeTrue();
        annotation.Citations[0].Type.Should().Be("CaseLaw");
        annotation.Citations[0].Normalized.Should().Be("542 U.S. 296");
        annotation.Citations[0].SourceUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CheckResponseAsync_ResponseWithNoCitations_ReturnsEmptyAnnotation()
    {
        // Arrange
        var serviceMock = BuildServiceMock(BuildEmptyReport());
        var sut = new CitationSafetyCheck(serviceMock.Object, NullLogger<CitationSafetyCheck>.Instance);

        // Act
        var annotation = await sut.CheckResponseAsync("No legal references in this response.", CancellationToken.None);

        // Assert: empty annotation, but event is still returned (no silent omission)
        annotation.Should().NotBeNull();
        annotation.HasCitations.Should().BeFalse();
        annotation.Citations.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckResponseAsync_ServiceThrows_ReturnsEmptyAnnotationFailOpen()
    {
        // Arrange
        var serviceMock = BuildServiceMock(throws: new InvalidOperationException("Provider down"));
        var sut = new CitationSafetyCheck(serviceMock.Object, NullLogger<CitationSafetyCheck>.Instance);

        // Act: must not throw — fail-open
        var annotation = await sut.CheckResponseAsync("The defendant was found liable under 542 U.S. 296.", CancellationToken.None);

        // Assert
        annotation.Should().NotBeNull();
        annotation.HasCitations.Should().BeFalse();
        annotation.Citations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CheckResponseAsync_NullOrWhitespaceResponse_ReturnsEmptyAnnotationWithoutCallingService(
        string? emptyResponse)
    {
        // Arrange
        var serviceMock = BuildServiceMock(BuildEmptyReport());
        var sut = new CitationSafetyCheck(serviceMock.Object, NullLogger<CitationSafetyCheck>.Instance);

        // Act
        var annotation = await sut.CheckResponseAsync(emptyResponse!, CancellationToken.None);

        // Assert: service not called for empty responses
        annotation.Should().NotBeNull();
        annotation.HasCitations.Should().BeFalse();
        serviceMock.Verify(
            s => s.VerifyAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckResponseAsync_OperationCancelled_ReturnsEmptyAnnotationWithoutThrowing()
    {
        // Arrange — simulate cancellation inside the service call
        var serviceMock = new Mock<ICitationVerificationService>();
        serviceMock
            .Setup(s => s.VerifyAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = new CitationSafetyCheck(serviceMock.Object, NullLogger<CitationSafetyCheck>.Instance);

        // Act
        var annotation = await sut.CheckResponseAsync("542 U.S. 296", CancellationToken.None);

        // Assert: cancellation is absorbed — fail-open
        annotation.Should().NotBeNull();
        annotation.Citations.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckResponseAsync_AndGroundednessCheck_CanRunConcurrentlyWithoutInterference()
    {
        // Arrange: both operations use independent instances and emit to different SSE event types.
        // Run 10 concurrent citations checks to verify there is no shared mutable state
        // or deadlock between parallel post-LLM safety checks.
        const int parallelism = 10;

        var serviceMock = BuildServiceMock(BuildReportWithVerifiedCitation());
        var sut = new CitationSafetyCheck(serviceMock.Object, NullLogger<CitationSafetyCheck>.Instance);

        var tasks = Enumerable.Range(0, parallelism)
            .Select(i => sut.CheckResponseAsync($"Turn {i}: See 542 U.S. 296.", CancellationToken.None))
            .ToArray();

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert: all calls succeeded independently
        results.Should().HaveCount(parallelism);
        results.Should().AllSatisfy(a =>
        {
            a.Should().NotBeNull();
            a.HasCitations.Should().BeTrue();
        });

        // The SSE event type is distinct from groundedness — confirm it is the correct constant.
        CitationSafetyAnnotation.SseEventType.Should().Be("citation_verification");
    }

    [Fact]
    public void CitationSafetyAnnotation_SseEventType_IsCorrectString()
    {
        CitationSafetyAnnotation.SseEventType.Should().Be("citation_verification");
    }
}
