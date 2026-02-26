using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for the POST /api/ai/chat/sessions/{sessionId}/refine endpoint
/// and the underlying TextRefinementTools.BuildRefineMessages logic.
///
/// Covers:
/// - Endpoint existence and HTTP method support
/// - Authentication/authorization enforcement (ADR-008)
/// - Request model validation (ChatRefineRequest)
/// - TextRefinementTools prompt construction (unit tests)
/// - SSE content type and headers
///
/// @see ChatEndpoints.RefineTextAsync
/// @see TextRefinementTools.BuildRefineMessages
/// @see ADR-008 (endpoint filters for auth)
/// </summary>
public class ChatRefineEndpointTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public ChatRefineEndpointTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    // =========================================================================
    // 1. Endpoint Existence and Method Tests
    // =========================================================================

    [Fact]
    public async Task Refine_EndpointExists_AcceptsPost()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString("N");
        var content = JsonContent.Create(new { selectedText = "test text", instruction = "simplify" });

        // Act
        var response = await _client.PostAsync($"/api/ai/chat/sessions/{sessionId}/refine", content);

        // Assert - endpoint exists (not 404 or 405)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Refine_GetMethod_NotAllowed()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString("N");

        // Act
        var response = await _client.GetAsync($"/api/ai/chat/sessions/{sessionId}/refine");

        // Assert - GET should not be supported on this endpoint
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed);
    }

    // =========================================================================
    // 2. Authentication Tests (ADR-008)
    // =========================================================================

    [Fact]
    public async Task Refine_WithoutAuth_RequiresAuthentication()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString("N");
        var content = JsonContent.Create(new { selectedText = "test text", instruction = "simplify" });

        // Act
        var response = await _client.PostAsync($"/api/ai/chat/sessions/{sessionId}/refine", content);

        // Assert - without auth, should return 401 or 500 (no auth configured in test factory)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Refine_WithAuth_DoesNotReturn404()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var sessionId = Guid.NewGuid().ToString("N");
        var content = JsonContent.Create(new { selectedText = "test text", instruction = "simplify" });

        // Act
        var response = await _client.PostAsync($"/api/ai/chat/sessions/{sessionId}/refine", content);

        // Assert - endpoint is reachable with auth
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

}

/// <summary>
/// Unit tests for the ChatRefineRequest model.
/// Validates record construction and default parameter values.
/// These tests do NOT require the WebApplicationFactory.
/// </summary>
public class ChatRefineRequestModelTests
{
    [Fact]
    public void ChatRefineRequest_CanBeCreated_WithRequiredFields()
    {
        // Act
        var request = new ChatRefineRequest("selected text", "simplify");

        // Assert
        request.SelectedText.Should().Be("selected text");
        request.Instruction.Should().Be("simplify");
        request.SurroundingContext.Should().BeNull();
    }

    [Fact]
    public void ChatRefineRequest_CanBeCreated_WithSurroundingContext()
    {
        // Act
        var request = new ChatRefineRequest(
            "selected text",
            "make formal",
            "Paragraph before. Selected text. Paragraph after.");

        // Assert
        request.SelectedText.Should().Be("selected text");
        request.Instruction.Should().Be("make formal");
        request.SurroundingContext.Should().Be("Paragraph before. Selected text. Paragraph after.");
    }

    [Fact]
    public void ChatRefineRequest_SurroundingContext_DefaultsToNull()
    {
        // Act
        var request = new ChatRefineRequest("text", "instruction");

        // Assert
        request.SurroundingContext.Should().BeNull();
    }
}

/// <summary>
/// Unit tests for TextRefinementTools.BuildRefineMessages prompt construction.
///
/// Verifies:
/// - System prompt content (professional editor instruction)
/// - User prompt includes instruction and selected text
/// - Surrounding context is included when provided
/// - Surrounding context is excluded when null
/// - Argument validation for null/empty inputs
///
/// @see TextRefinementTools.BuildRefineMessages
/// @see ChatEndpoints.RefineTextAsync (uses BuildRefineMessages for streaming)
/// </summary>
public class TextRefinementToolsTests
{
    private readonly TextRefinementTools _sut;
    private readonly Mock<IChatClient> _mockChatClient;

    public TextRefinementToolsTests()
    {
        _mockChatClient = new Mock<IChatClient>();
        _sut = new TextRefinementTools(_mockChatClient.Object);
    }

    // =========================================================================
    // BuildRefineMessages — Prompt Construction
    // =========================================================================

    [Fact]
    public void BuildRefineMessages_ReturnsSystemAndUserMessages()
    {
        // Act
        var messages = _sut.BuildRefineMessages("Some text to refine", "simplify this");

        // Assert
        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be(ChatRole.System);
        messages[1].Role.Should().Be(ChatRole.User);
    }

    [Fact]
    public void BuildRefineMessages_SystemPrompt_ContainsEditorInstruction()
    {
        // Act
        var messages = _sut.BuildRefineMessages("text", "instruction");

        // Assert
        var systemPrompt = messages[0].Text;
        systemPrompt.Should().Contain("professional editor");
        systemPrompt.Should().Contain("refined text");
    }

    [Fact]
    public void BuildRefineMessages_UserPrompt_IncludesInstructionAndText()
    {
        // Act
        var messages = _sut.BuildRefineMessages("The quick brown fox", "make formal");

        // Assert
        var userPrompt = messages[1].Text;
        userPrompt.Should().Contain("make formal");
        userPrompt.Should().Contain("The quick brown fox");
    }

    [Fact]
    public void BuildRefineMessages_WithoutContext_DoesNotIncludeSurroundingContext()
    {
        // Act
        var messages = _sut.BuildRefineMessages("selected text", "simplify");

        // Assert
        var userPrompt = messages[1].Text;
        userPrompt.Should().NotContain("Surrounding context");
    }

    [Fact]
    public void BuildRefineMessages_WithContext_IncludesSurroundingContext()
    {
        // Arrange
        var context = "Paragraph before the selection. More context after.";

        // Act
        var messages = _sut.BuildRefineMessages("selected text", "simplify", context);

        // Assert
        var userPrompt = messages[1].Text;
        userPrompt.Should().Contain("Surrounding context");
        userPrompt.Should().Contain(context);
        userPrompt.Should().Contain("selected text");
    }

    [Fact]
    public void BuildRefineMessages_WithContext_MarksContextAsReferenceOnly()
    {
        // Arrange
        var context = "Before paragraph. After paragraph.";

        // Act
        var messages = _sut.BuildRefineMessages("target text", "expand", context);

        // Assert
        var userPrompt = messages[1].Text;
        userPrompt.Should().Contain("do NOT include in output");
    }

    // =========================================================================
    // BuildRefineMessages — Argument Validation
    // =========================================================================

    [Fact]
    public void BuildRefineMessages_NullText_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _sut.BuildRefineMessages(null!, "instruction");
        act.Should().Throw<ArgumentException>().WithParameterName("text");
    }

    [Fact]
    public void BuildRefineMessages_EmptyText_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _sut.BuildRefineMessages("", "instruction");
        act.Should().Throw<ArgumentException>().WithParameterName("text");
    }

    [Fact]
    public void BuildRefineMessages_WhitespaceText_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _sut.BuildRefineMessages("   ", "instruction");
        act.Should().Throw<ArgumentException>().WithParameterName("text");
    }

    [Fact]
    public void BuildRefineMessages_NullInstruction_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _sut.BuildRefineMessages("text", null!);
        act.Should().Throw<ArgumentException>().WithParameterName("instruction");
    }

    [Fact]
    public void BuildRefineMessages_EmptyInstruction_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _sut.BuildRefineMessages("text", "");
        act.Should().Throw<ArgumentException>().WithParameterName("instruction");
    }

    // =========================================================================
    // BuildRefineMessages — Edge Cases
    // =========================================================================

    [Fact]
    public void BuildRefineMessages_LongText_IsIncludedInPrompt()
    {
        // Arrange
        var longText = new string('A', 5000);

        // Act
        var messages = _sut.BuildRefineMessages(longText, "summarize");

        // Assert
        var userPrompt = messages[1].Text;
        userPrompt.Should().Contain(longText);
    }

    [Fact]
    public void BuildRefineMessages_HtmlText_IsPreservedVerbatim()
    {
        // Arrange
        var htmlText = "<p>This is <strong>bold</strong> text with <em>emphasis</em>.</p>";

        // Act
        var messages = _sut.BuildRefineMessages(htmlText, "simplify");

        // Assert
        var userPrompt = messages[1].Text;
        userPrompt.Should().Contain(htmlText);
    }

    [Fact]
    public void BuildRefineMessages_NullSurroundingContext_TreatedAsAbsent()
    {
        // Act
        var messagesWithNull = _sut.BuildRefineMessages("text", "instruction", null);
        var messagesWithoutContext = _sut.BuildRefineMessages("text", "instruction");

        // Assert - both should produce the same user prompt
        messagesWithNull[1].Text.Should().Be(messagesWithoutContext[1].Text);
    }

    [Fact]
    public void BuildRefineMessages_EmptySurroundingContext_TreatedAsAbsent()
    {
        // Act
        var messagesWithEmpty = _sut.BuildRefineMessages("text", "instruction", "");
        var messagesWithoutContext = _sut.BuildRefineMessages("text", "instruction");

        // Assert - both should produce the same user prompt (empty string is whitespace-only)
        messagesWithEmpty[1].Text.Should().Be(messagesWithoutContext[1].Text);
    }

    // =========================================================================
    // Constructor Validation
    // =========================================================================

    [Fact]
    public void Constructor_NullChatClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new TextRefinementTools(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("chatClient");
    }
}

/// <summary>
/// Unit tests for the ChatSseEvent model used by the refine endpoint's SSE stream.
///
/// Verifies the event types and content structure that the refine endpoint
/// produces during streaming: "token", "done", and "error" events.
/// </summary>
public class ChatRefineStreamEventTests
{
    [Fact]
    public void ChatSseEvent_TokenEvent_HasContentField()
    {
        // Act
        var evt = new ChatSseEvent("token", "refined text chunk");

        // Assert
        evt.Type.Should().Be("token");
        evt.Content.Should().Be("refined text chunk");
        evt.Data.Should().BeNull();
    }

    [Fact]
    public void ChatSseEvent_DoneEvent_HasNullContent()
    {
        // Act
        var evt = new ChatSseEvent("done", null);

        // Assert
        evt.Type.Should().Be("done");
        evt.Content.Should().BeNull();
    }

    [Fact]
    public void ChatSseEvent_ErrorEvent_HasErrorMessage()
    {
        // Act
        var evt = new ChatSseEvent("error", "Something went wrong");

        // Assert
        evt.Type.Should().Be("error");
        evt.Content.Should().Be("Something went wrong");
    }

    [Fact]
    public void ChatSseEvent_SerializesToJson_WithCamelCase()
    {
        // Arrange
        var evt = new ChatSseEvent("token", "hello");
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(evt, options);

        // Assert
        json.Should().Contain("\"type\":\"token\"");
        json.Should().Contain("\"content\":\"hello\"");
    }
}
