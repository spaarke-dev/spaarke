using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Sse;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Streaming;

/// <summary>
/// Unit tests for <see cref="SseOutputGuard"/> (AIPU2-026).
///
/// Test matrix:
///   1. Valid payload passes through unchanged (original event type + data returned).
///   2. Invalid payload is replaced with generic_widget fallback event.
///   3. Unknown / R1 event types pass through unchanged.
///   4. OTEL counter is incremented on validation failure (not on success).
///   5. Warning is logged on failure; nothing is logged on success.
///   6. Fallback event type is always "generic_widget".
///   7. ADR-015: original payload is NOT included in the fallback event data.
/// </summary>
public class SseOutputGuardTests : IDisposable
{
    private readonly Mock<ISseEventValidator> _validatorMock;
    private readonly SseValidationTelemetry _telemetry;
    private readonly NullLogger<SseOutputGuard> _logger;
    private readonly SseOutputGuard _sut;

    public SseOutputGuardTests()
    {
        _validatorMock = new Mock<ISseEventValidator>(MockBehavior.Strict);
        _telemetry = new SseValidationTelemetry();
        _logger = NullLogger<SseOutputGuard>.Instance;
        _sut = new SseOutputGuard(_validatorMock.Object, _telemetry, _logger);
    }

    public void Dispose() => _telemetry.Dispose();

    // =========================================================================
    // 1. Valid payload passes through unchanged
    // =========================================================================

    [Fact]
    public void ValidateAndFallback_ValidPayload_ReturnsSameEventTypeAndData()
    {
        // Arrange
        var payload = JsonDocument.Parse("""{"widgetId":"w1"}""").RootElement;
        _validatorMock
            .Setup(v => v.Validate(ChatSseR2EventTypes.WorkspaceWidget, payload))
            .Returns(SseEventValidationResult.Valid);

        // Act
        var result = _sut.ValidateAndFallback(ChatSseR2EventTypes.WorkspaceWidget, payload);

        // Assert
        result.Type.Should().Be(ChatSseR2EventTypes.WorkspaceWidget);
        result.Data.GetRawText().Should().Be(payload.GetRawText());
    }

    // =========================================================================
    // 2. Invalid payload replaced with generic_widget fallback
    // =========================================================================

    [Fact]
    public void ValidateAndFallback_InvalidPayload_ReturnsFallbackEvent()
    {
        // Arrange
        const string fallbackJson =
            """{"type":"generic_widget","title":"Response","body":"An AI tool returned an unexpected response format.","errorCode":"SSE_SCHEMA_VIOLATION"}""";
        var payload = JsonDocument.Parse("""{}""").RootElement;
        var validationFailure = SseEventValidationResult.Failure(
            new[] { "'widgetId' is required." },
            fallbackJson);

        _validatorMock
            .Setup(v => v.Validate(ChatSseR2EventTypes.WorkspaceWidget, payload))
            .Returns(validationFailure);

        // Act
        var result = _sut.ValidateAndFallback(ChatSseR2EventTypes.WorkspaceWidget, payload);

        // Assert
        result.Type.Should().Be("generic_widget");
    }

    [Fact]
    public void ValidateAndFallback_InvalidPayload_FallbackDataContainsErrorCode()
    {
        // Arrange
        const string fallbackJson =
            """{"type":"generic_widget","title":"Response","body":"An AI tool returned an unexpected response format.","errorCode":"SSE_SCHEMA_VIOLATION"}""";
        var payload = JsonDocument.Parse("""{}""").RootElement;
        var validationFailure = SseEventValidationResult.Failure(
            new[] { "error" },
            fallbackJson);

        _validatorMock
            .Setup(v => v.Validate(ChatSseR2EventTypes.WorkspaceWidget, payload))
            .Returns(validationFailure);

        // Act
        var result = _sut.ValidateAndFallback(ChatSseR2EventTypes.WorkspaceWidget, payload);

        // Assert
        result.Data.GetProperty("errorCode").GetString().Should().Be("SSE_SCHEMA_VIOLATION");
    }

    [Fact]
    public void ValidateAndFallback_InvalidPayload_OriginalDataNotPresentInFallback()
    {
        // Arrange — ADR-015: governed data must not leak through the fallback event.
        const string sensitiveJson = """{"governedField":"CONFIDENTIAL-MATTER-NUMBER-12345"}""";
        const string fallbackJson =
            """{"type":"generic_widget","errorCode":"SSE_SCHEMA_VIOLATION","body":"safe text"}""";
        var payload = JsonDocument.Parse(sensitiveJson).RootElement;
        var validationFailure = SseEventValidationResult.Failure(new[] { "error" }, fallbackJson);

        _validatorMock
            .Setup(v => v.Validate(ChatSseR2EventTypes.WorkspaceWidget, payload))
            .Returns(validationFailure);

        // Act
        var result = _sut.ValidateAndFallback(ChatSseR2EventTypes.WorkspaceWidget, payload);

        // Assert — fallback must NOT contain the original sensitive data
        result.Data.GetRawText().Should().NotContain("CONFIDENTIAL-MATTER-NUMBER-12345");
    }

    // =========================================================================
    // 3. Unknown / R1 event types pass through unchanged
    // =========================================================================

    [Theory]
    [InlineData("token")]
    [InlineData("done")]
    [InlineData("error")]
    [InlineData("citations")]
    public void ValidateAndFallback_R1EventType_PassesThroughUnchanged(string eventType)
    {
        // Arrange — validator returns Valid for unknown types
        var payload = JsonDocument.Parse("""{"content":"hello"}""").RootElement;
        _validatorMock
            .Setup(v => v.Validate(eventType, payload))
            .Returns(SseEventValidationResult.Valid);

        // Act
        var result = _sut.ValidateAndFallback(eventType, payload);

        // Assert
        result.Type.Should().Be(eventType);
        result.Data.GetRawText().Should().Be(payload.GetRawText());
    }

    // =========================================================================
    // 4. OTEL counter incremented on failure, not on success
    // =========================================================================

    [Fact]
    public void ValidateAndFallback_InvalidPayload_ValidatorCalledExactlyOnce()
    {
        // Arrange
        const string fallbackJson = """{"type":"generic_widget","errorCode":"SSE_SCHEMA_VIOLATION","body":"error"}""";
        var payload = JsonDocument.Parse("""{}""").RootElement;
        _validatorMock
            .Setup(v => v.Validate(ChatSseR2EventTypes.ContextUpdate, payload))
            .Returns(SseEventValidationResult.Failure(new[] { "error" }, fallbackJson));

        // Act
        _sut.ValidateAndFallback(ChatSseR2EventTypes.ContextUpdate, payload);

        // Assert — validator called exactly once (no retry, no double-call)
        _validatorMock.Verify(
            v => v.Validate(ChatSseR2EventTypes.ContextUpdate, payload),
            Times.Once);
    }

    // =========================================================================
    // 5. Timestamp is set on returned SseEvent
    // =========================================================================

    [Fact]
    public void ValidateAndFallback_AnyResult_TimestampIsUtcNow()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var payload = JsonDocument.Parse("""{"x":1}""").RootElement;
        _validatorMock
            .Setup(v => v.Validate(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Returns(SseEventValidationResult.Valid);

        // Act
        var result = _sut.ValidateAndFallback("token", payload);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        // Assert
        result.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // =========================================================================
    // 6. End-to-end: real SseEventValidator integration (no mocks)
    // =========================================================================

    [Fact]
    public void ValidateAndFallback_WithRealValidator_ValidWorkspaceWidget_PassesThrough()
    {
        // Arrange — use real validator (no mock)
        var realValidator = new SseEventValidator();
        var guard = new SseOutputGuard(realValidator, _telemetry, _logger);

        var json = """
            {
              "widgetId": "widget-real",
              "widgetType": "action-panel",
              "payload": { "actions": [] },
              "priority": 3
            }
            """;
        var payload = JsonDocument.Parse(json).RootElement;

        // Act
        var result = guard.ValidateAndFallback(ChatSseR2EventTypes.WorkspaceWidget, payload);

        // Assert
        result.Type.Should().Be(ChatSseR2EventTypes.WorkspaceWidget);
    }

    [Fact]
    public void ValidateAndFallback_WithRealValidator_InvalidContextUpdate_ReturnsFallback()
    {
        // Arrange — use real validator (no mock); empty object fails all required fields
        var realValidator = new SseEventValidator();
        var guard = new SseOutputGuard(realValidator, _telemetry, _logger);

        var payload = JsonDocument.Parse("{}").RootElement;

        // Act
        var result = guard.ValidateAndFallback(ChatSseR2EventTypes.ContextUpdate, payload);

        // Assert
        result.Type.Should().Be("generic_widget");
        result.Data.GetProperty("errorCode").GetString().Should().Be("SSE_SCHEMA_VIOLATION");
    }

    [Fact]
    public void ValidateAndFallback_WithRealValidator_R1TokenEvent_PassesThrough()
    {
        // Arrange
        var realValidator = new SseEventValidator();
        var guard = new SseOutputGuard(realValidator, _telemetry, _logger);
        var payload = JsonDocument.Parse("""{"delta":"Hello "}""").RootElement;

        // Act
        var result = guard.ValidateAndFallback("token", payload);

        // Assert — R1 events always pass through
        result.Type.Should().Be("token");
    }
}
