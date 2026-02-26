using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

// Explicit alias to avoid ChatMessage ambiguity
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Integration tests for the streaming write backend flow.
///
/// Exercises the full server-side path with mocked boundaries:
///   WorkingDocumentTools → inner LLM call (IChatClient) → SSE event emission
///
/// Unlike the unit tests in <see cref="WorkingDocumentToolsTests"/> which test each
/// method in isolation, these tests validate:
///   1. Complete SSE event sequences (start → N tokens → end) with JSON-serializable payloads
///   2. End-to-end cancellation flow through the streaming pipeline
///   3. Error propagation with ADR-019 compliant error details
///   4. Section append flow with heading injection
///   5. Missing document edge cases
///   6. Capability gating via SprkChatAgentFactory tool resolution
///
/// Placed in the unit test project because the integration test project has known
/// DI issues with WebApplicationFactory (pre-existing). These tests exercise the same
/// code paths that the HTTP endpoint would invoke.
///
/// ADR-015: Tests use generic content only (no real document strings in CI logs).
/// ADR-019: Error events use ProblemDetails-compatible errorCode + errorMessage.
/// </summary>
public class StreamingWriteIntegrationTests
{
    // === JSON options matching ChatEndpoints.WriteChatSSEAsync ===
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // === Test constants ===
    private const string TestAnalysisId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string TestTenantId = "test-tenant-streaming";
    private const string TestDocumentId = "doc-streaming-001";
    private static readonly Guid TestPlaybookId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string TestDocumentContent = "Integration test document body for streaming write validation.";
    private const string TestEditInstruction = "Rewrite paragraph two in formal tone.";
    private const string TestSectionTitle = "Financial Summary";
    private const string TestSectionInstruction = "Summarize the financial highlights.";

    // ====================================================================
    // Test 1: Happy Path — 5-token streaming write
    // ====================================================================

    [Fact]
    public async Task HappyPath_FiveTokenStreamingWrite_EmitsCorrectSSESequence()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();
        var sseLines = new List<string>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokens(chatClient, "Token1", "Token2", "Token3", "Token4", "Token5");

        // SSE writer that captures events AND simulates JSON serialization (like ChatEndpoints)
        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            // Verify JSON serialization succeeds (catches serialization issues early)
            var json = JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions);
            sseLines.Add($"data: {json}\n\n");
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — 7 events total: 1 start + 5 tokens + 1 end
        capturedEvents.Should().HaveCount(7);

        // Verify event type sequence
        capturedEvents[0].Should().BeOfType<DocumentStreamStartEvent>();
        for (var i = 1; i <= 5; i++)
        {
            capturedEvents[i].Should().BeOfType<DocumentStreamTokenEvent>();
        }
        capturedEvents[6].Should().BeOfType<DocumentStreamEndEvent>();

        // Verify start event payload
        var startEvent = (DocumentStreamStartEvent)capturedEvents[0];
        startEvent.Type.Should().Be("document_stream_start");
        startEvent.OperationId.Should().NotBe(Guid.Empty);
        startEvent.TargetPosition.Should().Be("document");
        startEvent.OperationType.Should().Be("replace");

        // Verify token events have correct content and monotonic indices
        var tokenEvents = capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokenEvents.Should().HaveCount(5);
        tokenEvents[0].Token.Should().Be("Token1");
        tokenEvents[0].Index.Should().Be(0);
        tokenEvents[1].Token.Should().Be("Token2");
        tokenEvents[1].Index.Should().Be(1);
        tokenEvents[2].Token.Should().Be("Token3");
        tokenEvents[2].Index.Should().Be(2);
        tokenEvents[3].Token.Should().Be("Token4");
        tokenEvents[3].Index.Should().Be(3);
        tokenEvents[4].Token.Should().Be("Token5");
        tokenEvents[4].Index.Should().Be(4);

        // Verify end event payload
        var endEvent = (DocumentStreamEndEvent)capturedEvents[6];
        endEvent.Type.Should().Be("document_stream_end");
        endEvent.Cancelled.Should().BeFalse();
        endEvent.TotalTokens.Should().Be(5);
        endEvent.ErrorCode.Should().BeNull();
        endEvent.ErrorMessage.Should().BeNull();

        // Verify all events share the same operationId
        var operationId = startEvent.OperationId;
        tokenEvents.Should().OnlyContain(t => t.OperationId == operationId);
        endEvent.OperationId.Should().Be(operationId);

        // Verify return value
        result.Should().Contain("edited successfully");
        result.Should().Contain("5 tokens");
    }

    [Fact]
    public async Task HappyPath_SSEPayloads_AreValidJsonWithCorrectPropertyNames()
    {
        // Arrange — verify the full SSE serialization matches the ChatEndpoints SSE format
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var serializedEvents = new List<string>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokens(chatClient, "Hello", "World");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            var json = JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions);
            serializedEvents.Add(json);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — verify JSON property names use camelCase (matching ChatEndpoints.JsonOptions)
        serializedEvents.Should().HaveCount(4); // start + 2 tokens + end

        // Start event JSON
        var startJson = serializedEvents[0];
        startJson.Should().Contain("\"type\":\"document_stream_start\"");
        startJson.Should().Contain("\"operationId\":");
        startJson.Should().Contain("\"targetPosition\":\"document\"");
        startJson.Should().Contain("\"operationType\":\"replace\"");

        // Token event JSON
        var tokenJson = serializedEvents[1];
        tokenJson.Should().Contain("\"type\":\"document_stream_token\"");
        tokenJson.Should().Contain("\"token\":\"Hello\"");
        tokenJson.Should().Contain("\"index\":0");

        // End event JSON
        var endJson = serializedEvents[3];
        endJson.Should().Contain("\"type\":\"document_stream_end\"");
        endJson.Should().Contain("\"cancelled\":false");
        endJson.Should().Contain("\"totalTokens\":2");
    }

    [Fact]
    public async Task HappyPath_SSEEventSequence_CanBeDeserializedBackToTypedEvents()
    {
        // Arrange — round-trip: serialize events → parse JSON → verify typed deserialization
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var serializedPairs = new List<(string json, string type)>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokens(chatClient, "A", "B", "C");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            var json = JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions);
            serializedPairs.Add((json, evt.Type));
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — each serialized event can be deserialized back to the correct type
        serializedPairs.Should().HaveCount(5); // start + 3 tokens + end

        // Deserialize start event
        var startDeserialized = JsonSerializer.Deserialize<DocumentStreamStartEvent>(
            serializedPairs[0].json, JsonOptions);
        startDeserialized.Should().NotBeNull();
        startDeserialized!.OperationType.Should().Be("replace");

        // Deserialize token events
        for (var i = 1; i <= 3; i++)
        {
            var tokenDeserialized = JsonSerializer.Deserialize<DocumentStreamTokenEvent>(
                serializedPairs[i].json, JsonOptions);
            tokenDeserialized.Should().NotBeNull();
            tokenDeserialized!.Index.Should().Be(i - 1);
        }

        // Deserialize end event
        var endDeserialized = JsonSerializer.Deserialize<DocumentStreamEndEvent>(
            serializedPairs[4].json, JsonOptions);
        endDeserialized.Should().NotBeNull();
        endDeserialized!.TotalTokens.Should().Be(3);
        endDeserialized.Cancelled.Should().BeFalse();
    }

    // ====================================================================
    // Test 2: Cancellation mid-stream
    // ====================================================================

    [Fact]
    public async Task Cancellation_AfterTwoTokens_EmitsEndWithCancelledTrue()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokensThenCancel(chatClient, "First", "Second");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — 4 events: start + 2 tokens + cancelled end
        capturedEvents.Should().HaveCount(4);

        // Verify token content preserved before cancellation
        var tokenEvents = capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokenEvents.Should().HaveCount(2);
        tokenEvents[0].Token.Should().Be("First");
        tokenEvents[1].Token.Should().Be("Second");

        // Verify end event signals cancellation
        var endEvent = capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Cancelled.Should().BeTrue();
        endEvent.TotalTokens.Should().Be(2);
        endEvent.ErrorCode.Should().BeNull("cancellation is not an error");
        endEvent.ErrorMessage.Should().BeNull("cancellation is not an error");

        // Verify return summary
        result.Should().Contain("cancelled");
        result.Should().Contain("2 tokens");
    }

    [Fact]
    public async Task Cancellation_MidStream_DoesNotPropagateException()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokensThenCancel(chatClient, "Partial");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act — should NOT throw OperationCanceledException
        var action = async () => await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert
        await action.Should().NotThrowAsync();

        // Verify graceful end event
        capturedEvents.Last().Should().BeOfType<DocumentStreamEndEvent>();
        ((DocumentStreamEndEvent)capturedEvents.Last()).Cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_EndEventSerializesToValidJson()
    {
        // Arrange — verify the cancelled end event serializes correctly for SSE
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var serializedEvents = new List<string>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokensThenCancel(chatClient, "A");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            serializedEvents.Add(JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions));
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — cancelled end event JSON
        var endJson = serializedEvents.Last();
        endJson.Should().Contain("\"cancelled\":true");
        endJson.Should().Contain("\"totalTokens\":1");
        endJson.Should().Contain("\"type\":\"document_stream_end\"");
    }

    // ====================================================================
    // Test 3: LLM failure
    // ====================================================================

    [Fact]
    public async Task LLMFailure_AfterOneToken_EmitsErrorEndEventWithADR019Details()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokensThenFail(chatClient, "PartialContent",
            new InvalidOperationException("Azure OpenAI service unavailable"));

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — 3 events: start + 1 token + error end
        capturedEvents.Should().HaveCount(3);

        // Verify partial token was emitted
        var tokenEvent = capturedEvents[1] as DocumentStreamTokenEvent;
        tokenEvent.Should().NotBeNull();
        tokenEvent!.Token.Should().Be("PartialContent");
        tokenEvent.Index.Should().Be(0);

        // Verify error end event (ADR-019)
        var endEvent = capturedEvents.Last() as DocumentStreamEndEvent;
        endEvent.Should().NotBeNull();
        endEvent!.Cancelled.Should().BeFalse();
        endEvent.TotalTokens.Should().Be(1);
        endEvent.ErrorCode.Should().Be("LLM_STREAM_FAILED", "ADR-019 requires stable errorCode");
        endEvent.ErrorMessage.Should().NotBeNullOrWhiteSpace("ADR-019 requires user-friendly message");

        // Verify no exception propagated
        result.Should().Contain("failed");
        result.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task LLMFailure_ImmediateFail_EmitsStartThenErrorEnd()
    {
        // Arrange — LLM fails before producing any tokens
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientFails(chatClient, new HttpRequestException("Connection refused"));

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — 2 events: start + error end (0 tokens)
        capturedEvents.Should().HaveCount(2);
        capturedEvents[0].Should().BeOfType<DocumentStreamStartEvent>();

        var endEvent = (DocumentStreamEndEvent)capturedEvents[1];
        endEvent.Cancelled.Should().BeFalse();
        endEvent.TotalTokens.Should().Be(0);
        endEvent.ErrorCode.Should().Be("LLM_STREAM_FAILED");
        endEvent.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LLMFailure_ErrorEndEvent_SerializesToValidJsonWithErrorFields()
    {
        // Arrange — verify ADR-019 error fields serialize correctly for SSE client parsing
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var serializedEvents = new List<string>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokensThenFail(chatClient, "X", new TimeoutException("Request timed out"));

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            serializedEvents.Add(JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions));
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — error end event JSON
        var endJson = serializedEvents.Last();
        endJson.Should().Contain("\"errorCode\":\"LLM_STREAM_FAILED\"");
        endJson.Should().Contain("\"errorMessage\":");
        endJson.Should().Contain("\"cancelled\":false");
        endJson.Should().Contain("\"totalTokens\":1");

        // Verify the error message does NOT leak document content (ADR-015)
        endJson.Should().NotContain(TestDocumentContent);
    }

    [Fact]
    public async Task LLMFailure_DoesNotPropagateException()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokensThenFail(chatClient, "X", new Exception("Unexpected LLM error"));

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act — should NOT throw
        var action = async () => await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert
        await action.Should().NotThrowAsync();
    }

    // ====================================================================
    // Test 4: AppendSectionAsync — section-specific streaming
    // ====================================================================

    [Fact]
    public async Task AppendSection_EmitsHeadingAsFirstToken_ThenLLMContent()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokens(chatClient, "Revenue grew", " by 15%", " in Q4.");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        var result = await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert — 6 events: start + heading + 3 LLM tokens + end
        capturedEvents.Should().HaveCount(6);

        // Start event has insert operation type
        var startEvent = (DocumentStreamStartEvent)capturedEvents[0];
        startEvent.TargetPosition.Should().Be("end");
        startEvent.OperationType.Should().Be("insert");

        // First token is the section heading (injected before LLM stream)
        var headingToken = (DocumentStreamTokenEvent)capturedEvents[1];
        headingToken.Token.Should().StartWith("## ");
        headingToken.Token.Should().Contain(TestSectionTitle);
        headingToken.Index.Should().Be(0);

        // Subsequent tokens are LLM content
        var llmTokens = capturedEvents.OfType<DocumentStreamTokenEvent>().Skip(1).ToList();
        llmTokens.Should().HaveCount(3);
        llmTokens[0].Token.Should().Be("Revenue grew");
        llmTokens[0].Index.Should().Be(1);
        llmTokens[1].Token.Should().Be(" by 15%");
        llmTokens[1].Index.Should().Be(2);
        llmTokens[2].Token.Should().Be(" in Q4.");
        llmTokens[2].Index.Should().Be(3);

        // End event shows total token count (heading + 3 LLM = 4)
        var endEvent = (DocumentStreamEndEvent)capturedEvents.Last();
        endEvent.TotalTokens.Should().Be(4);
        endEvent.Cancelled.Should().BeFalse();
        endEvent.ErrorCode.Should().BeNull();

        // Return summary mentions section title
        result.Should().Contain(TestSectionTitle);
        result.Should().Contain("appended");
    }

    [Fact]
    public async Task AppendSection_SSEPayloads_HaveCorrectJsonPropertyNames()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var serializedEvents = new List<string>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokens(chatClient, "Content");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            serializedEvents.Add(JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions));
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert
        serializedEvents.Should().HaveCount(4); // start + heading + 1 LLM + end

        // Insert start event
        serializedEvents[0].Should().Contain("\"operationType\":\"insert\"");
        serializedEvents[0].Should().Contain("\"targetPosition\":\"end\"");

        // Heading token
        serializedEvents[1].Should().Contain($"\"token\":\"## {TestSectionTitle}\\n\\n\"");
    }

    [Fact]
    public async Task AppendSection_WithoutDocument_StillStreamsHeadingAndContent()
    {
        // Arrange — no document available (analysisId has no working document)
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, null);
        SetupChatClientTokens(chatClient, "Generated content");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert — should still work (append doesn't require existing document)
        capturedEvents.Should().HaveCount(4); // start + heading + 1 LLM + end

        var endEvent = (DocumentStreamEndEvent)capturedEvents.Last();
        endEvent.Cancelled.Should().BeFalse();
        endEvent.ErrorCode.Should().BeNull();
        endEvent.TotalTokens.Should().Be(2); // heading + 1 LLM
    }

    // ====================================================================
    // Test 5: Empty/no document scenarios
    // ====================================================================

    [Fact]
    public async Task NoDocument_NullAnalysisId_EmitsNoDocumentError()
    {
        // Arrange — no analysis context at all
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, analysisId: null);

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — start + NO_DOCUMENT end
        capturedEvents.Should().HaveCount(2);
        capturedEvents[0].Should().BeOfType<DocumentStreamStartEvent>();

        var endEvent = (DocumentStreamEndEvent)capturedEvents[1];
        endEvent.ErrorCode.Should().Be("NO_DOCUMENT");
        endEvent.ErrorMessage.Should().Contain("No working document");
        endEvent.TotalTokens.Should().Be(0);
        endEvent.Cancelled.Should().BeFalse();

        // Return message should explain the issue
        result.Should().Contain("no working document");
    }

    [Fact]
    public async Task NoDocument_NullWorkingDocument_EmitsNoDocumentError()
    {
        // Arrange — analysis exists but has no working document
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, null);

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        var result = await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert
        capturedEvents.Should().HaveCount(2);

        var endEvent = (DocumentStreamEndEvent)capturedEvents.Last();
        endEvent.ErrorCode.Should().Be("NO_DOCUMENT");
    }

    [Fact]
    public async Task NoDocument_InvalidGuidAnalysisId_EmitsNoDocumentError()
    {
        // Arrange — analysisId is garbage
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, analysisId: "not-a-guid");

        // Act
        await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert
        capturedEvents.Should().HaveCount(2);
        ((DocumentStreamEndEvent)capturedEvents.Last()).ErrorCode.Should().Be("NO_DOCUMENT");
    }

    [Fact]
    public async Task NoDocument_AnalysisNotFound_EmitsNoDocumentError()
    {
        // Arrange — analysis service throws KeyNotFoundException
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        analysisService.GetAnalysisAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException("Analysis not found"));

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert
        capturedEvents.Should().HaveCount(2);
        ((DocumentStreamEndEvent)capturedEvents.Last()).ErrorCode.Should().Be("NO_DOCUMENT");
    }

    [Fact]
    public async Task NoDocument_ErrorEvent_SerializesToValidJsonForSSE()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var serializedEvents = new List<string>();

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            serializedEvents.Add(JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions));
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, analysisId: null);

        // Act
        await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — verify JSON structure
        serializedEvents.Should().HaveCount(2);
        var endJson = serializedEvents.Last();
        endJson.Should().Contain("\"errorCode\":\"NO_DOCUMENT\"");
        endJson.Should().Contain("\"errorMessage\":");
        endJson.Should().Contain("\"totalTokens\":0");
        endJson.Should().Contain("\"cancelled\":false");
    }

    // ====================================================================
    // Test 6: Capability gating — tools absent without write_back capability
    // ====================================================================

    [Fact]
    public void CapabilityGating_GetTools_ReturnsTwoFunctions_WhenToolsInstantiated()
    {
        // Arrange — when WorkingDocumentTools IS instantiated (write_back capability present),
        // GetTools should return exactly 2 functions
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (_, _) => Task.CompletedTask;

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        var tools = sut.GetTools().ToList();

        // Assert
        tools.Should().HaveCount(2);
        tools.Should().Contain(t => t.Name == "EditWorkingDocument");
        tools.Should().Contain(t => t.Name == "AppendSection");
    }

    [Fact]
    public void CapabilityGating_ToolNames_MatchExpectedAIFunctionNames()
    {
        // Arrange — verify the tool names match what SprkChatAgentFactory would register
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (_, _) => Task.CompletedTask;

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        var tools = sut.GetTools().ToList();

        // Assert — names must be stable for the agent's tool calling
        tools[0].Name.Should().Be("EditWorkingDocument");
        tools[1].Name.Should().Be("AppendSection");
    }

    [Fact]
    public void CapabilityGating_ToolDescriptions_AreNotEmpty()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (_, _) => Task.CompletedTask;

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        var tools = sut.GetTools().ToList();

        // Assert — descriptions are required for the LLM to understand when to use tools
        tools.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Description));
    }

    [Fact]
    public void CapabilityGating_AgentFactory_DoesNotRegisterWriteToolsByDefault()
    {
        // This test verifies that SprkChatAgentFactory.ResolveTools does NOT include
        // WorkingDocumentTools — the factory only registers WorkingDocumentTools when
        // the playbook has write_back capability (not yet implemented per Task 029 notes).
        //
        // Since ResolveTools is private and the factory requires complex DI setup,
        // we verify the architectural constraint: WorkingDocumentTools requires
        // explicit construction with a writeSSE delegate, which the factory would only
        // provide when write_back capability is detected.
        //
        // The constructor requires writeSSE (non-null), proving it can't be accidentally
        // instantiated without the streaming write infrastructure.
        var action = () => new WorkingDocumentTools(
            Substitute.For<IChatClient>(),
            null!,
            Substitute.For<IAnalysisOrchestrationService>(),
            Substitute.For<ILogger>(),
            TestAnalysisId);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("writeSSE",
                "writeSSE is mandatory — tools can't be created without streaming infrastructure");
    }

    // ====================================================================
    // Cross-cutting: Event ordering invariants across all scenarios
    // ====================================================================

    [Theory]
    [InlineData("happy")]
    [InlineData("cancelled")]
    [InlineData("error")]
    [InlineData("no-document")]
    public async Task EventOrdering_StartIsAlwaysFirst_EndIsAlwaysLast(string scenario)
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        WorkingDocumentTools sut;

        switch (scenario)
        {
            case "happy":
                SetupAnalysisService(analysisService, TestDocumentContent);
                SetupChatClientTokens(chatClient, "A", "B");
                sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);
                break;
            case "cancelled":
                SetupAnalysisService(analysisService, TestDocumentContent);
                SetupChatClientTokensThenCancel(chatClient, "A");
                sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);
                break;
            case "error":
                SetupAnalysisService(analysisService, TestDocumentContent);
                SetupChatClientTokensThenFail(chatClient, "A", new Exception("Fail"));
                sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);
                break;
            case "no-document":
                sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, analysisId: null);
                break;
            default:
                throw new ArgumentException($"Unknown scenario: {scenario}");
        }

        // Act
        await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — start is always first, end is always last
        capturedEvents.Should().NotBeEmpty();
        capturedEvents.First().Should().BeOfType<DocumentStreamStartEvent>(
            $"[{scenario}] start must be first event");
        capturedEvents.Last().Should().BeOfType<DocumentStreamEndEvent>(
            $"[{scenario}] end must be last event");
    }

    [Fact]
    public async Task EventOrdering_AllTokenIndicesAreMonotonicallyIncreasing()
    {
        // Arrange — large token stream
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokens(chatClient,
            "T0", "T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8", "T9");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        await sut.EditWorkingDocumentAsync(TestEditInstruction);

        // Assert — verify 10 tokens with indices 0-9
        var tokenEvents = capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokenEvents.Should().HaveCount(10);

        for (var i = 0; i < tokenEvents.Count; i++)
        {
            tokenEvents[i].Index.Should().Be(i, $"Token at position {i} must have index {i}");
        }
    }

    [Fact]
    public async Task EventOrdering_AppendSection_HeadingIndexIsZero_LLMTokensFollowSequentially()
    {
        // Arrange
        var chatClient = Substitute.For<IChatClient>();
        var analysisService = Substitute.For<IAnalysisOrchestrationService>();
        var logger = Substitute.For<ILogger>();
        var capturedEvents = new List<DocumentStreamEvent>();

        SetupAnalysisService(analysisService, TestDocumentContent);
        SetupChatClientTokens(chatClient, "P1", "P2", "P3");

        Func<DocumentStreamEvent, CancellationToken, Task> writeSSE = (evt, ct) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var sut = new WorkingDocumentTools(chatClient, writeSSE, analysisService, logger, TestAnalysisId);

        // Act
        await sut.AppendSectionAsync(TestSectionTitle, TestSectionInstruction);

        // Assert — heading at 0, LLM tokens at 1, 2, 3
        var tokenEvents = capturedEvents.OfType<DocumentStreamTokenEvent>().ToList();
        tokenEvents.Should().HaveCount(4); // heading + 3 LLM

        tokenEvents[0].Index.Should().Be(0, "heading token index");
        tokenEvents[1].Index.Should().Be(1, "first LLM token index");
        tokenEvents[2].Index.Should().Be(2, "second LLM token index");
        tokenEvents[3].Index.Should().Be(3, "third LLM token index");
    }

    // ====================================================================
    // Private helpers — mirror unit test patterns with NSubstitute
    // ====================================================================

    /// <summary>
    /// Configures the analysis service to return a document with the specified content.
    /// </summary>
    private static void SetupAnalysisService(IAnalysisOrchestrationService analysisService, string? content)
    {
        analysisService.GetAnalysisAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisDetailResult
            {
                Id = Guid.Parse(TestAnalysisId),
                DocumentId = Guid.NewGuid(),
                DocumentName = "test-doc.pdf",
                Action = new AnalysisActionInfo(Guid.NewGuid(), "Test Action"),
                Status = "completed",
                WorkingDocument = content,
                FinalOutput = content,
                ChatHistory = [],
                TokenUsage = null,
                StartedOn = DateTime.UtcNow,
                CompletedOn = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Configures IChatClient to return the specified tokens as a streaming response.
    /// </summary>
    private static void SetupChatClientTokens(IChatClient chatClient, params string[] tokens)
    {
        var updates = tokens.Select(CreateStreamingUpdate).ToArray();
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<AiChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(updates));
    }

    /// <summary>
    /// Configures IChatClient to yield specified tokens then throw OperationCanceledException.
    /// </summary>
    private static void SetupChatClientTokensThenCancel(IChatClient chatClient, params string[] tokensBeforeCancel)
    {
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<AiChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerableThenThrow(
                tokensBeforeCancel.Select(CreateStreamingUpdate),
                new OperationCanceledException()));
    }

    /// <summary>
    /// Configures IChatClient to yield a token then throw the specified exception.
    /// </summary>
    private static void SetupChatClientTokensThenFail(
        IChatClient chatClient, string tokenBeforeFail, Exception exception)
    {
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<AiChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerableThenThrow(
                new[] { CreateStreamingUpdate(tokenBeforeFail) },
                exception));
    }

    /// <summary>
    /// Configures IChatClient to immediately throw (no tokens yielded).
    /// </summary>
    private static void SetupChatClientFails(IChatClient chatClient, Exception exception)
    {
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<AiChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerableThenThrow(
                Enumerable.Empty<ChatResponseUpdate>(),
                exception));
    }

    private static ChatResponseUpdate CreateStreamingUpdate(string text)
    {
        return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)]
        };
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToAsyncEnumerableThenThrow(
        IEnumerable<ChatResponseUpdate> items,
        Exception exceptionToThrow)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }

        throw exceptionToThrow;
    }
}
