using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Safety;

/// <summary>
/// Unit tests for <see cref="MatterContextDetector"/>.
///
/// Test matrix:
///   1. No history → returns null (no marker to compare against)
///   2. Same matter as last marker → returns null (no pivot)
///   3. Different matter → returns MatterContextChange with correct fields
///   4. Empty incoming matter ID → returns null (nothing to compare)
///   5. Null incoming matter ID → returns null
///   6. History with marker but no incoming → returns null
///   7. Multiple markers in history — uses the LATEST one
///   8. ExtractMatterId parses correctly
///   9. ExtractMatterId returns null for non-marker content
/// </summary>
public class MatterContextDetectorTests
{
    private readonly MatterContextDetector _sut;

    public MatterContextDetectorTests()
    {
        _sut = new MatterContextDetector(NullLogger<MatterContextDetector>.Instance);
    }

    // =========================================================================
    // No pivot cases
    // =========================================================================

    [Fact]
    public void DetectChange_EmptyHistory_ReturnsNull()
    {
        var result = _sut.DetectChange([], "matter-b");

        result.Should().BeNull();
    }

    [Fact]
    public void DetectChange_SameMatter_ReturnsNull()
    {
        var history = BuildHistory(
            SystemMarker("matter-a"),
            UserMessage("What are the key clauses?"),
            AssistantMessage("The key clauses are..."));

        var result = _sut.DetectChange(history, "matter-a");

        result.Should().BeNull();
    }

    [Fact]
    public void DetectChange_EmptyIncomingMatterId_ReturnsNull()
    {
        var history = BuildHistory(SystemMarker("matter-a"), UserMessage("Hello"));

        var result = _sut.DetectChange(history, string.Empty);

        result.Should().BeNull();
    }

    [Fact]
    public void DetectChange_NullIncomingMatterId_ReturnsNull()
    {
        var history = BuildHistory(SystemMarker("matter-a"), UserMessage("Hello"));

        var result = _sut.DetectChange(history, null!);

        result.Should().BeNull();
    }

    [Fact]
    public void DetectChange_NoMarkerInHistory_ReturnsNull()
    {
        // History has only user/assistant messages — no matter marker has been embedded yet.
        var history = BuildHistory(
            UserMessage("Hello"),
            AssistantMessage("Hi!"));

        var result = _sut.DetectChange(history, "matter-b");

        result.Should().BeNull();
    }

    [Fact]
    public void DetectChange_WhitespaceOnlyIncomingMatterId_ReturnsNull()
    {
        var history = BuildHistory(SystemMarker("matter-a"));

        var result = _sut.DetectChange(history, "   ");

        result.Should().BeNull();
    }

    // =========================================================================
    // Pivot detected
    // =========================================================================

    [Fact]
    public void DetectChange_DifferentMatter_ReturnsMatterContextChange()
    {
        var history = BuildHistory(
            SystemMarker("matter-a"),
            UserMessage("Tell me about Matter A."),
            AssistantMessage("Matter A involves..."));

        var result = _sut.DetectChange(history, "matter-b");

        result.Should().NotBeNull();
        result!.PreviousMatterId.Should().Be("matter-a");
        result.NewMatterId.Should().Be("matter-b");
    }

    [Fact]
    public void DetectChange_DifferentMatter_SetsChangeDetectedAtTurnIndex_ToMarkerIndex()
    {
        // Marker is at index 0, followed by two more messages (index 1, 2).
        var history = BuildHistory(
            SystemMarker("matter-a"),          // index 0
            UserMessage("Question about A"),   // index 1
            AssistantMessage("Answer about A") // index 2
        );

        var result = _sut.DetectChange(history, "matter-b");

        result.Should().NotBeNull();
        result!.ChangeDetectedAtTurnIndex.Should().Be(0);
    }

    [Fact]
    public void DetectChange_MultipleMarkers_UsesLatestMarker()
    {
        // Two markers in history; the detector should use the LAST one (most recent context).
        var history = BuildHistory(
            SystemMarker("matter-a"),              // index 0 — old marker
            UserMessage("Question about A"),       // index 1
            AssistantMessage("Answer about A"),    // index 2
            SystemMarker("matter-b"),              // index 3 — newer marker
            UserMessage("Question about B"),       // index 4
            AssistantMessage("Answer about B")     // index 5
        );

        // Same as the latest marker → no pivot.
        var noPivot = _sut.DetectChange(history, "matter-b");
        noPivot.Should().BeNull();

        // Different from the latest marker → pivot from matter-b to matter-c.
        var pivot = _sut.DetectChange(history, "matter-c");
        pivot.Should().NotBeNull();
        pivot!.PreviousMatterId.Should().Be("matter-b");
        pivot.NewMatterId.Should().Be("matter-c");
        pivot.ChangeDetectedAtTurnIndex.Should().Be(3);
    }

    [Fact]
    public void DetectChange_MatterIdCaseInsensitive_SameMatterReturnNull()
    {
        var history = BuildHistory(SystemMarker("MATTER-A"));

        // Lower-case variant of the same matter ID should NOT trigger a pivot.
        var result = _sut.DetectChange(history, "matter-a");

        result.Should().BeNull();
    }

    // =========================================================================
    // BuildMatterMarker / ExtractMatterId helpers
    // =========================================================================

    [Fact]
    public void BuildMatterMarker_ProducesParseableMarker()
    {
        var marker = MatterContextDetector.BuildMatterMarker("matter-xyz");

        var extracted = MatterContextDetector.ExtractMatterId(marker);
        extracted.Should().Be("matter-xyz");
    }

    [Fact]
    public void ExtractMatterId_NullContent_ReturnsNull()
    {
        var result = MatterContextDetector.ExtractMatterId(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractMatterId_EmptyContent_ReturnsNull()
    {
        var result = MatterContextDetector.ExtractMatterId(string.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractMatterId_ContentWithoutMarker_ReturnsNull()
    {
        var result = MatterContextDetector.ExtractMatterId("This is a normal message");
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractMatterId_ValidMarkerEmbeddedInLongerString_ExtractsMatterId()
    {
        var content = $"Some prefix {MatterContextDetector.BuildMatterMarker("matter-42")} some suffix";
        var result = MatterContextDetector.ExtractMatterId(content);
        result.Should().Be("matter-42");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static IReadOnlyList<ChatMessage> BuildHistory(params ChatMessage[] messages)
        => messages.ToList().AsReadOnly();

    private static ChatMessage SystemMarker(string matterId) => new(
        MessageId: Guid.NewGuid().ToString("N"),
        SessionId: "session-1",
        Role: ChatMessageRole.System,
        Content: MatterContextDetector.BuildMatterMarker(matterId),
        TokenCount: 0,
        CreatedAt: DateTimeOffset.UtcNow);

    private static ChatMessage UserMessage(string content) => new(
        MessageId: Guid.NewGuid().ToString("N"),
        SessionId: "session-1",
        Role: ChatMessageRole.User,
        Content: content,
        TokenCount: 0,
        CreatedAt: DateTimeOffset.UtcNow);

    private static ChatMessage AssistantMessage(string content) => new(
        MessageId: Guid.NewGuid().ToString("N"),
        SessionId: "session-1",
        Role: ChatMessageRole.Assistant,
        Content: content,
        TokenCount: 0,
        CreatedAt: DateTimeOffset.UtcNow);
}
