using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Sse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Streaming;

/// <summary>
/// Unit tests for <see cref="SseEventValidator"/> (AIPU2-026).
///
/// Test matrix:
///   1. Valid payload for a known R2 event type passes through unchanged.
///   2. Invalid payload for a known R2 event type returns IsValid=false with FallbackPayload.
///   3. Unknown event type (R1 events: token, done, error) passes through as Valid.
///   4. Empty event type string returns IsValid=false (schema violation).
///   5. Fallback payload is valid JSON with the expected generic_widget errorCode.
/// </summary>
public class SseEventValidatorTests
{
    private readonly SseEventValidator _sut = new();

    // =========================================================================
    // 1. Valid payload — known R2 event type passes through
    // =========================================================================

    [Fact]
    public void Validate_ValidWorkspaceWidgetPayload_ReturnsValid()
    {
        // Arrange — minimal valid workspace_widget payload
        var json = """
            {
              "widgetId": "widget-001",
              "widgetType": "document-preview",
              "payload": { "documentId": "doc-abc" },
              "priority": 5
            }
            """;
        var payload = JsonDocument.Parse(json).RootElement;

        // Act
        var result = _sut.Validate(ChatSseR2EventTypes.WorkspaceWidget, payload);

        // Assert
        result.IsValid.Should().BeTrue();
        result.FallbackPayload.Should().BeNull();
        result.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ValidContextUpdatePayload_ReturnsValid()
    {
        // Arrange
        var json = """
            {
              "contextType": "document",
              "contextId": "ctx-001",
              "delta": { "key": "value" },
              "confidence": 0.85
            }
            """;
        var payload = JsonDocument.Parse(json).RootElement;

        // Act
        var result = _sut.Validate(ChatSseR2EventTypes.ContextUpdate, payload);

        // Assert
        result.IsValid.Should().BeTrue();
        result.FallbackPayload.Should().BeNull();
    }

    // =========================================================================
    // 2. Invalid payload — known R2 type returns failure with fallback
    // =========================================================================

    [Fact]
    public void Validate_MissingRequiredField_ReturnsFailureWithFallback()
    {
        // Arrange — workspace_widget missing widgetId, widgetType, payload, priority
        var json = """{ "someField": "someValue" }""";
        var payload = JsonDocument.Parse(json).RootElement;

        // Act
        var result = _sut.Validate(ChatSseR2EventTypes.WorkspaceWidget, payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().NotBeEmpty();
        result.FallbackPayload.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_InvalidEnumValue_ReturnsFailureWithFallback()
    {
        // Arrange — widgetType has an illegal enum value
        var json = """
            {
              "widgetId": "widget-001",
              "widgetType": "not-a-valid-type",
              "payload": {},
              "priority": 5
            }
            """;
        var payload = JsonDocument.Parse(json).RootElement;

        // Act
        var result = _sut.Validate(ChatSseR2EventTypes.WorkspaceWidget, payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainMatch("*widgetType*");
    }

    [Fact]
    public void Validate_InvalidPayload_FallbackPayloadIsValidJson()
    {
        // Arrange
        var json = """{ "broken": true }""";
        var payload = JsonDocument.Parse(json).RootElement;

        // Act
        var result = _sut.Validate(ChatSseR2EventTypes.ContextUpdate, payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FallbackPayload.Should().NotBeNullOrEmpty();

        // Fallback must be parseable JSON
        var act = () => JsonDocument.Parse(result.FallbackPayload!);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_InvalidPayload_FallbackPayloadContainsExpectedErrorCode()
    {
        // Arrange
        var json = """{}""";
        var payload = JsonDocument.Parse(json).RootElement;

        // Act
        var result = _sut.Validate(ChatSseR2EventTypes.WorkspaceWidget, payload);

        // Assert
        result.IsValid.Should().BeFalse();
        var fallback = JsonDocument.Parse(result.FallbackPayload!).RootElement;
        fallback.GetProperty("errorCode").GetString().Should().Be("SSE_SCHEMA_VIOLATION");
        fallback.GetProperty("type").GetString().Should().Be("generic_widget");
    }

    // =========================================================================
    // 3. Unknown event types (R1 events) pass through unchanged
    // =========================================================================

    [Theory]
    [InlineData("token")]
    [InlineData("done")]
    [InlineData("error")]
    [InlineData("citations")]
    [InlineData("plan_preview")]
    [InlineData("completely_unknown_future_event")]
    public void Validate_UnknownOrR1EventType_ReturnsValid(string eventType)
    {
        // Arrange — payload shape is irrelevant for unknown types
        var json = """{ "anything": "goes" }""";
        var payload = JsonDocument.Parse(json).RootElement;

        // Act
        var result = _sut.Validate(eventType, payload);

        // Assert
        result.IsValid.Should().BeTrue("unknown event types must always pass through for R1 backward compatibility");
        result.FallbackPayload.Should().BeNull();
    }

    // =========================================================================
    // 4. All seven R2 event types have registered schemas
    // =========================================================================

    [Theory]
    [InlineData(ChatSseR2EventTypes.WorkspaceWidget)]
    [InlineData(ChatSseR2EventTypes.ContextUpdate)]
    [InlineData(ChatSseR2EventTypes.ContextHighlight)]
    [InlineData(ChatSseR2EventTypes.WorkspaceAction)]
    [InlineData(ChatSseR2EventTypes.Suggestions)]
    [InlineData(ChatSseR2EventTypes.CapabilityChange)]
    [InlineData(ChatSseR2EventTypes.SafetyAnnotation)]
    public void Validate_EmptyPayload_ForAllKnownR2Types_ReturnsFailure(string eventType)
    {
        // Arrange — empty object satisfies no required fields
        var payload = JsonDocument.Parse("{}").RootElement;

        // Act
        var result = _sut.Validate(eventType, payload);

        // Assert — all known R2 types must have schema coverage (empty objects are invalid)
        result.IsValid.Should().BeFalse(
            $"event type '{eventType}' must have a registered schema that rejects empty payloads");
    }

    // =========================================================================
    // 5. SseEventValidationResult.Valid sentinel
    // =========================================================================

    [Fact]
    public void SseEventValidationResult_Valid_HasExpectedShape()
    {
        SseEventValidationResult.Valid.IsValid.Should().BeTrue();
        SseEventValidationResult.Valid.FallbackPayload.Should().BeNull();
        SseEventValidationResult.Valid.ValidationErrors.Should().BeEmpty();
    }

    // =========================================================================
    // 6. SseEventValidationResult.Failure factory
    // =========================================================================

    [Fact]
    public void SseEventValidationResult_Failure_HasExpectedShape()
    {
        var errors = new[] { "field X is required", "field Y must be string" };
        const string fallback = """{"type":"generic_widget"}""";

        var result = SseEventValidationResult.Failure(errors, fallback);

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().BeEquivalentTo(errors);
        result.FallbackPayload.Should().Be(fallback);
    }
}
