using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sprk.Bff.Api.Services.Ai.Safety;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Safety;

/// <summary>
/// Unit tests for <see cref="GroundednessCheckService"/>.
///
/// All tests use a mocked <see cref="HttpMessageHandler"/> to intercept Content Safety API calls
/// without any real network dependency. <see cref="GroundednessCheckTelemetry"/> is a real
/// instance — OTEL Meter is thread-safe and cheap to construct in tests.
///
/// Test matrix:
///   1. Ungrounded segments detected → IsGrounded = false, UngroundedSegments populated
///   2. Fully grounded response → IsGrounded = true, UngroundedSegments empty
///   3. Empty source documents → check skipped, IsGrounded = true (no API call)
///   4. HTTP 429 → fail-open (IsGrounded = true, no exception)
///   5. HTTP 500 → fail-open (IsGrounded = true, no exception)
///   6. Timeout → fail-open (IsGrounded = true, no exception)
///   7. Request body shape — groundingSources array and QnA fields serialised correctly
///   8. LatencyMs always populated for API calls; zero when skipped
/// </summary>
public class GroundednessCheckServiceTests : IDisposable
{
    private readonly GroundednessCheckTelemetry _telemetry;

    public GroundednessCheckServiceTests()
    {
        _telemetry = new GroundednessCheckTelemetry();
    }

    public void Dispose() => _telemetry.Dispose();

    // =========================================================================
    // Happy path: ungrounded segments detected
    // =========================================================================

    [Fact]
    public async Task CheckAsync_UngroundedSegmentsDetected_ReturnsUngroundedResult()
    {
        // Arrange
        var responseJson = BuildApiResponse(
            ungroundedDetected: true,
            details: [("The defendant was convicted in 2019.", null)]);

        var service = BuildService(HttpStatusCode.OK, responseJson);
        var request = new GroundednessRequest(
            LlmResponse: "The defendant was convicted in 2019.",
            SourceDocuments: ["Court records show the case was filed in 2021."]);

        // Act
        var result = await service.CheckAsync(request);

        // Assert
        result.IsGrounded.Should().BeFalse();
        result.UngroundedSegments.Should().HaveCount(1);
        result.UngroundedSegments[0].Text.Should().Be("The defendant was convicted in 2019.");
        result.UngroundedSegments[0].Reason.Should().BeNull();
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CheckAsync_MultipleUngroundedSegments_AllSegmentsReturned()
    {
        // Arrange
        var responseJson = BuildApiResponse(
            ungroundedDetected: true,
            details:
            [
                ("Claim A that is not grounded.", null),
                ("Claim B also unsupported.", "No matching passage found."),
            ]);

        var service = BuildService(HttpStatusCode.OK, responseJson);
        var request = new GroundednessRequest(
            LlmResponse: "Claim A that is not grounded. Claim B also unsupported.",
            SourceDocuments: ["Document one.", "Document two."]);

        // Act
        var result = await service.CheckAsync(request);

        // Assert
        result.IsGrounded.Should().BeFalse();
        result.UngroundedSegments.Should().HaveCount(2);
        result.UngroundedSegments[1].Reason.Should().Be("No matching passage found.");
    }

    // =========================================================================
    // Happy path: fully grounded response
    // =========================================================================

    [Fact]
    public async Task CheckAsync_FullyGrounded_ReturnsGroundedResult_WithEmptySegments()
    {
        // Arrange
        var responseJson = BuildApiResponse(ungroundedDetected: false, details: null);
        var service = BuildService(HttpStatusCode.OK, responseJson);
        var request = new GroundednessRequest(
            LlmResponse: "The contract was signed on 1 January 2025.",
            SourceDocuments: ["Contract dated 1 January 2025 between Party A and Party B."]);

        // Act
        var result = await service.CheckAsync(request);

        // Assert
        result.IsGrounded.Should().BeTrue();
        result.UngroundedSegments.Should().BeEmpty();
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CheckAsync_ApiReturnsUngroundedDetectedFalse_WithDetails_ReturnsGrounded()
    {
        // UngroundedDetected=false takes precedence over any detail entries the API might return.
        var responseJson = BuildApiResponse(
            ungroundedDetected: false,
            details: [("Some text.", null)]);

        var service = BuildService(HttpStatusCode.OK, responseJson);
        var request = new GroundednessRequest(
            LlmResponse: "Some text.",
            SourceDocuments: ["Supporting document."]);

        // Act
        var result = await service.CheckAsync(request);

        // Assert — ungroundedDetected=false → grounded regardless of details
        result.IsGrounded.Should().BeTrue();
        result.UngroundedSegments.Should().BeEmpty();
    }

    // =========================================================================
    // Skip path: empty source documents
    // =========================================================================

    [Fact]
    public async Task CheckAsync_EmptySourceDocuments_SkipsApiCall_ReturnsAssumeGrounded()
    {
        // Arrange — the handler should never be called
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var service = BuildService(handlerMock);
        var request = new GroundednessRequest(
            LlmResponse: "The answer is 42.",
            SourceDocuments: []);

        // Act
        var result = await service.CheckAsync(request);

        // Assert
        result.IsGrounded.Should().BeTrue();
        result.UngroundedSegments.Should().BeEmpty();
        result.LatencyMs.Should().Be(0, "latency is zero when the API is not called");

        // Verify handler was never invoked
        handlerMock.Protected()
            .Verify<Task<HttpResponseMessage>>(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    // =========================================================================
    // Fail-open: service unavailable
    // =========================================================================

    [Fact]
    public async Task CheckAsync_Http429_FailsOpen_ReturnsAssumeGrounded()
    {
        // Arrange
        var service = BuildService(HttpStatusCode.TooManyRequests, "{}");
        var request = new GroundednessRequest(
            LlmResponse: "Some response.",
            SourceDocuments: ["Document passage."]);

        // Act
        var result = await service.CheckAsync(request);

        // Assert — fail-open: rate limited → assume grounded
        result.IsGrounded.Should().BeTrue("HTTP 429 should cause fail-open, not block the response");
        result.UngroundedSegments.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_Http500_FailsOpen_ReturnsAssumeGrounded()
    {
        // Arrange
        var service = BuildService(HttpStatusCode.InternalServerError, "Service error");
        var request = new GroundednessRequest(
            LlmResponse: "Some response.",
            SourceDocuments: ["Document passage."]);

        // Act
        var result = await service.CheckAsync(request);

        // Assert — fail-open: server error → assume grounded
        result.IsGrounded.Should().BeTrue("HTTP 5xx should cause fail-open, not block the response");
        result.UngroundedSegments.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_Timeout_FailsOpen_ReturnsAssumeGrounded()
    {
        // Arrange — handler blocks indefinitely; HttpClient timeout fires first
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken ct) =>
            {
                // Block until the HttpClient timeout fires (TaskCanceledException).
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        // Use a very short HttpClient timeout so the test completes quickly.
        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://fake-contentsafety.cognitiveservices.azure.com/"),
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        var service = BuildService(httpClient);
        var request = new GroundednessRequest(
            LlmResponse: "Some response.",
            SourceDocuments: ["Document passage."]);

        // Act
        var result = await service.CheckAsync(request);

        // Assert — fail-open: timeout → assume grounded
        result.IsGrounded.Should().BeTrue("Timeout should cause fail-open, not block the response");
        result.UngroundedSegments.Should().BeEmpty();
    }

    // =========================================================================
    // Request serialisation
    // =========================================================================

    [Fact]
    public async Task CheckAsync_SendsCorrectRequestBody()
    {
        // Arrange — capture the request body the service sends
        string? capturedBody = null;
        var responseJson = BuildApiResponse(ungroundedDetected: false, details: null);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = req.Content != null
                    ? await req.Content.ReadAsStringAsync()
                    : null;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var service = BuildService(handlerMock);
        var request = new GroundednessRequest(
            LlmResponse: "The interest rate is 5%.",
            SourceDocuments: ["Loan agreement section 4.", "Amendment dated March 2025."],
            Query: "What is the interest rate?");

        // Act
        await service.CheckAsync(request);

        // Assert — verify the body shape matches the Groundedness Detection API contract
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);

        doc.RootElement.GetProperty("domain").GetString()
            .Should().Be("Generic");
        doc.RootElement.GetProperty("task").GetString()
            .Should().Be("QnA");

        var qna = doc.RootElement.GetProperty("qna");
        qna.GetProperty("query").GetString().Should().Be("What is the interest rate?");
        qna.GetProperty("answer").GetString().Should().Be("The interest rate is 5%.");

        var sources = doc.RootElement.GetProperty("groundingSources").EnumerateArray().ToList();
        sources.Should().HaveCount(2);
        sources[0].GetString().Should().Be("Loan agreement section 4.");
        sources[1].GetString().Should().Be("Amendment dated March 2025.");

        doc.RootElement.GetProperty("reasoning").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_NoQuery_SendsEmptyQueryString()
    {
        // Arrange
        string? capturedBody = null;
        var responseJson = BuildApiResponse(ungroundedDetected: false, details: null);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = req.Content != null
                    ? await req.Content.ReadAsStringAsync()
                    : null;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var service = BuildService(handlerMock);
        var request = new GroundednessRequest(
            LlmResponse: "Some AI response.",
            SourceDocuments: ["Source doc."],
            Query: null);

        // Act
        await service.CheckAsync(request);

        // Assert — null Query maps to empty string in the API body
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("qna").GetProperty("query").GetString()
            .Should().BeEmpty();
    }

    // =========================================================================
    // LatencyMs
    // =========================================================================

    [Fact]
    public async Task CheckAsync_WhenApiReturns_LatencyMsIsPopulated()
    {
        // Arrange
        var responseJson = BuildApiResponse(ungroundedDetected: false, details: null);
        var service = BuildService(HttpStatusCode.OK, responseJson);
        var request = new GroundednessRequest("Response.", ["Source."]);

        // Act
        var result = await service.CheckAsync(request);

        // Assert
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CheckAsync_WhenSourcesEmpty_LatencyMsIsZero()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var service = BuildService(handlerMock);
        var request = new GroundednessRequest("Response.", []);

        // Act
        var result = await service.CheckAsync(request);

        // Assert
        result.LatencyMs.Should().Be(0, "no API call is made when sources are empty");
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Builds a <see cref="GroundednessCheckService"/> with a fixed HTTP status and response body.
    /// </summary>
    private GroundednessCheckService BuildService(HttpStatusCode statusCode, string responseBody)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });

        return BuildService(handlerMock);
    }

    /// <summary>
    /// Builds a <see cref="GroundednessCheckService"/> using a custom handler mock.
    /// </summary>
    private GroundednessCheckService BuildService(Mock<HttpMessageHandler> handlerMock)
    {
        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://fake-contentsafety.cognitiveservices.azure.com/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        return BuildService(httpClient);
    }

    /// <summary>
    /// Builds a <see cref="GroundednessCheckService"/> from a pre-configured <see cref="HttpClient"/>.
    /// </summary>
    private GroundednessCheckService BuildService(HttpClient httpClient)
    {
        return new GroundednessCheckService(
            httpClient,
            NullLogger<GroundednessCheckService>.Instance,
            _telemetry);
    }

    /// <summary>
    /// Builds a Groundedness Detection API response JSON string.
    /// </summary>
    /// <param name="ungroundedDetected">Whether any ungrounded text was detected.</param>
    /// <param name="details">
    /// Sequence of (text, reason?) tuples for ungroundedDetails. Null omits the array.
    /// </param>
    private static string BuildApiResponse(
        bool ungroundedDetected,
        IReadOnlyList<(string Text, string? Reason)>? details)
    {
        var detailsJson = details is null
            ? string.Empty
            : ", \"ungroundedDetails\": [" +
              string.Join(", ", details.Select(d =>
                  d.Reason is not null
                      ? $"{{\"text\": {JsonSerializer.Serialize(d.Text)}, \"reason\": {JsonSerializer.Serialize(d.Reason)}}}"
                      : $"{{\"text\": {JsonSerializer.Serialize(d.Text)}}}")) +
              "]";

        return $$"""
            {
                "ungroundedDetected": {{(ungroundedDetected ? "true" : "false")}}{{detailsJson}}
            }
            """;
    }
}
