using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Safety;

/// <summary>
/// Unit tests for <see cref="ConversationHistorySanitizer"/>.
///
/// Test matrix:
///   1. No retrieval messages → WasModified = false, history unchanged
///   2. Retrieval messages within window → replaced with placeholder
///   3. Retrieval messages beyond the window → NOT replaced
///   4. User messages are always retained
///   5. Assistant conclusions (non-retrieval system messages) are retained
///   6. All retrieval messages stripped → RemovedDocumentCount matches
///   7. Mixed history (user + assistant + retrieval) → only retrieval stripped
///   8. NotificationMessage always present regardless of modification
///   9. Security test: no original document text appears in sanitized output
/// </summary>
public class ConversationHistorySanitizerTests
{
    private readonly ConversationHistorySanitizer _sut;

    public ConversationHistorySanitizerTests()
    {
        _sut = new ConversationHistorySanitizer(NullLogger<ConversationHistorySanitizer>.Instance);
    }

    // =========================================================================
    // No-op cases
    // =========================================================================

    [Fact]
    public void StripRetrievedContent_NoRetrievalMessages_WasModifiedFalse()
    {
        var history = BuildHistory(
            UserMessage("What are the key clauses?"),
            AssistantMessage("The key clauses are X, Y, and Z."));

        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 1);

        result.WasModified.Should().BeFalse();
        result.RemovedDocumentCount.Should().Be(0);
        result.Messages.Should().HaveCount(2);
    }

    [Fact]
    public void StripRetrievedContent_EmptyHistory_ReturnsEmptySanitizedHistory()
    {
        var result = _sut.StripRetrievedContent([], fromTurnIndex: 0);

        result.WasModified.Should().BeFalse();
        result.RemovedDocumentCount.Should().Be(0);
        result.Messages.Should().BeEmpty();
    }

    // =========================================================================
    // Stripping within the window
    // =========================================================================

    [Fact]
    public void StripRetrievedContent_RetrievalMessageWithinWindow_IsReplaced()
    {
        const string sensitiveContent = "CONFIDENTIAL: Matter A clause 4 states that...";
        var history = BuildHistory(
            RetrievalMessage(sensitiveContent),   // index 0 — within window
            UserMessage("Summarize this."),        // index 1
            AssistantMessage("The clause says...")); // index 2

        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 2);

        result.WasModified.Should().BeTrue();
        result.RemovedDocumentCount.Should().Be(1);
        result.Messages[0].Content.Should().Be(ConversationHistorySanitizer.PrivacyPlaceholder);
    }

    [Fact]
    public void StripRetrievedContent_MultipleRetrievalMessages_AllStrippedWithinWindow()
    {
        var history = BuildHistory(
            RetrievalMessage("Doc 1 content"),   // index 0
            RetrievalMessage("Doc 2 content"),   // index 1
            UserMessage("Compare these."),        // index 2
            AssistantMessage("Both documents..."));// index 3

        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 3);

        result.WasModified.Should().BeTrue();
        result.RemovedDocumentCount.Should().Be(2);
        result.Messages[0].Content.Should().Be(ConversationHistorySanitizer.PrivacyPlaceholder);
        result.Messages[1].Content.Should().Be(ConversationHistorySanitizer.PrivacyPlaceholder);
    }

    // =========================================================================
    // Messages beyond the window are NOT stripped
    // =========================================================================

    [Fact]
    public void StripRetrievedContent_RetrievalMessageBeyondWindow_IsRetained()
    {
        const string futureContent = "Matter B doc content";
        var history = BuildHistory(
            UserMessage("Old question"),          // index 0 — within window
            AssistantMessage("Old answer"),       // index 1 — within window
            RetrievalMessage(futureContent));     // index 2 — BEYOND window

        // Window ends at index 1 (the change was detected at turn 1).
        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 1);

        result.Messages[2].Content.Should().Be(
            ConversationHistorySanitizer.RetrievalContentMarker + futureContent);
        result.RemovedDocumentCount.Should().Be(0);
    }

    // =========================================================================
    // User and assistant messages are always retained
    // =========================================================================

    [Fact]
    public void StripRetrievedContent_UserMessages_AreAlwaysRetained()
    {
        const string userText = "Tell me about the indemnification clause.";
        var history = BuildHistory(
            UserMessage(userText),
            RetrievalMessage("Some doc content"),
            AssistantMessage("The indemnification clause..."));

        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 2);

        result.Messages[0].Content.Should().Be(userText);
        result.Messages[0].Role.Should().Be(ChatMessageRole.User);
    }

    [Fact]
    public void StripRetrievedContent_AssistantMessages_AreAlwaysRetained()
    {
        const string aiConclusion = "Based on the documents, the key risk is...";
        var history = BuildHistory(
            RetrievalMessage("Doc content"),
            AssistantMessage(aiConclusion));

        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 1);

        result.Messages[1].Content.Should().Be(aiConclusion);
        result.Messages[1].Role.Should().Be(ChatMessageRole.Assistant);
    }

    [Fact]
    public void StripRetrievedContent_NonRetrievalSystemMessages_AreRetained()
    {
        // System messages that are NOT retrieval results (e.g. matter markers) should be retained.
        const string markerContent = "__matter:matter-a__";
        var history = BuildHistory(
            SystemMessage(markerContent),
            RetrievalMessage("Privileged text"),
            AssistantMessage("Summary..."));

        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 2);

        // Matter marker retained, retrieval message stripped.
        result.Messages[0].Content.Should().Be(markerContent);
        result.Messages[1].Content.Should().Be(ConversationHistorySanitizer.PrivacyPlaceholder);
    }

    // =========================================================================
    // Notification message
    // =========================================================================

    [Fact]
    public void StripRetrievedContent_AlwaysPopulatesNotificationMessage()
    {
        // Even when nothing is stripped, the notification is populated.
        var history = BuildHistory(UserMessage("Hello"));

        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 0);

        result.NotificationMessage.Should().NotBeNullOrWhiteSpace();
        result.NotificationMessage.Should().Be(ConversationHistorySanitizer.UserNotificationMessage);
    }

    // =========================================================================
    // Security test: no original document text in sanitized output
    // =========================================================================

    [Fact]
    public void StripRetrievedContent_Security_NoOriginalDocumentTextInSanitizedHistory()
    {
        // Arrange: build a realistic history with sensitive content scattered across
        // multiple retrieval messages.
        const string sensitivePhrase1 = "PRIVILEGED AND CONFIDENTIAL — liability cap is $5M";
        const string sensitivePhrase2 = "ATTORNEY WORK PRODUCT — settlement strategy for Jones v. Smith";
        const string sensitivePhrase3 = "TRADE SECRET — algorithm details of proprietary valuation model";

        var history = BuildHistory(
            UserMessage("What is the liability cap?"),
            RetrievalMessage(sensitivePhrase1),
            AssistantMessage("The liability cap is $5M per the contract."),
            UserMessage("What is the settlement strategy?"),
            RetrievalMessage(sensitivePhrase2),
            AssistantMessage("The settlement strategy focuses on early resolution."),
            UserMessage("Describe the valuation model."),
            RetrievalMessage(sensitivePhrase3),
            AssistantMessage("The valuation uses a proprietary approach."));

        // Act: strip all retrieval messages (pivot detected at the last turn).
        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 8);

        // Assert: none of the sensitive phrases appear anywhere in the sanitized history.
        var allContent = string.Join(" ", result.Messages.Select(m => m.Content));
        allContent.Should().NotContain(sensitivePhrase1,
            because: "privileged document text must never appear in sanitized history sent to LLM");
        allContent.Should().NotContain(sensitivePhrase2,
            because: "attorney work product must never appear in sanitized history sent to LLM");
        allContent.Should().NotContain(sensitivePhrase3,
            because: "trade secret content must never appear in sanitized history sent to LLM");

        // All three retrieval messages stripped.
        result.RemovedDocumentCount.Should().Be(3);
        result.WasModified.Should().BeTrue();
    }

    [Fact]
    public void StripRetrievedContent_Security_PlaceholderContainsNoOriginalText()
    {
        const string originalContent = "Top secret litigation memo regarding Acme Corp.";
        var history = BuildHistory(RetrievalMessage(originalContent));

        var result = _sut.StripRetrievedContent(history, fromTurnIndex: 0);

        result.Messages[0].Content.Should().NotContain(originalContent);
        result.Messages[0].Content.Should().Be(ConversationHistorySanitizer.PrivacyPlaceholder);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static IReadOnlyList<ChatMessage> BuildHistory(params ChatMessage[] messages)
        => messages.ToList().AsReadOnly();

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

    private static ChatMessage RetrievalMessage(string documentContent) => new(
        MessageId: Guid.NewGuid().ToString("N"),
        SessionId: "session-1",
        Role: ChatMessageRole.System,
        // The retrieval marker prefix is prepended by DocumentSearchTools when storing results.
        Content: ConversationHistorySanitizer.RetrievalContentMarker + documentContent,
        TokenCount: 0,
        CreatedAt: DateTimeOffset.UtcNow);

    private static ChatMessage SystemMessage(string content) => new(
        MessageId: Guid.NewGuid().ToString("N"),
        SessionId: "session-1",
        Role: ChatMessageRole.System,
        Content: content,
        TokenCount: 0,
        CreatedAt: DateTimeOffset.UtcNow);
}
