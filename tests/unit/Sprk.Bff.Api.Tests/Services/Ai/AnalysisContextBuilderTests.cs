using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for AnalysisContextBuilder - prompt construction service.
/// </summary>
public class AnalysisContextBuilderTests
{
    private readonly Mock<ILogger<AnalysisContextBuilder>> _loggerMock;
    private readonly IOptions<AnalysisOptions> _options;
    private readonly AnalysisContextBuilder _builder;

    public AnalysisContextBuilderTests()
    {
        _loggerMock = new Mock<ILogger<AnalysisContextBuilder>>();
        _options = Options.Create(new AnalysisOptions
        {
            MaxChatHistoryMessages = 10
        });
        _builder = new AnalysisContextBuilder(_options, _loggerMock.Object);
    }

    #region BuildSystemPrompt Tests

    [Fact]
    public void BuildSystemPrompt_WithActionOnly_ReturnsActionPromptAndOutputFormat()
    {
        // Arrange
        var action = CreateAction("You are a legal analyst.");

        // Act
        var result = _builder.BuildSystemPrompt(action, []);

        // Assert
        result.Should().Contain("You are a legal analyst.");
        result.Should().Contain("## Output Format");
        result.Should().Contain("Markdown format");
        result.Should().NotContain("## Instructions");
    }

    [Fact]
    public void BuildSystemPrompt_WithSkills_IncludesSkillPromptFragments()
    {
        // Arrange
        var action = CreateAction("Analyze the document.");
        var skills = new[]
        {
            CreateSkill("Focus on contractual obligations."),
            CreateSkill("Identify potential risks.")
        };

        // Act
        var result = _builder.BuildSystemPrompt(action, skills);

        // Assert
        result.Should().Contain("## Instructions");
        result.Should().Contain("- Focus on contractual obligations.");
        result.Should().Contain("- Identify potential risks.");
    }

    [Fact]
    public void BuildSystemPrompt_MultipleSkills_ListsAllSkillsAsBulletPoints()
    {
        // Arrange
        var action = CreateAction("Base prompt");
        var skills = new[]
        {
            CreateSkill("Skill 1"),
            CreateSkill("Skill 2"),
            CreateSkill("Skill 3")
        };

        // Act
        var result = _builder.BuildSystemPrompt(action, skills);

        // Assert
        result.Should().Contain("- Skill 1");
        result.Should().Contain("- Skill 2");
        result.Should().Contain("- Skill 3");
    }

    #endregion

    #region BuildUserPromptAsync Tests

    [Fact]
    public async Task BuildUserPromptAsync_WithDocumentOnly_ReturnsDocumentSection()
    {
        // Arrange
        var documentText = "This is the contract text.";

        // Act
        var result = await _builder.BuildUserPromptAsync(documentText, [], CancellationToken.None);

        // Assert
        result.Should().Contain("# Document to Analyze");
        result.Should().Contain("This is the contract text.");
        result.Should().Contain("Please analyze the document above");
        result.Should().NotContain("# Reference Materials");
    }

    [Fact]
    public async Task BuildUserPromptAsync_WithInlineKnowledge_IncludesReferenceMaterials()
    {
        // Arrange
        var documentText = "Contract content.";
        var knowledge = new[]
        {
            CreateKnowledge("Guidelines", "Always check for compliance.", KnowledgeType.Inline),
            CreateKnowledge("Standards", "ISO compliance required.", KnowledgeType.Inline)
        };

        // Act
        var result = await _builder.BuildUserPromptAsync(documentText, knowledge, CancellationToken.None);

        // Assert
        result.Should().Contain("# Reference Materials");
        result.Should().Contain("## Guidelines");
        result.Should().Contain("Always check for compliance.");
        result.Should().Contain("## Standards");
        result.Should().Contain("ISO compliance required.");
    }

    [Fact]
    public async Task BuildUserPromptAsync_WithRagKnowledge_DoesNotIncludeRagContent()
    {
        // Arrange - RAG knowledge is not included inline (requires async retrieval)
        var documentText = "Document.";
        var knowledge = new[]
        {
            CreateKnowledge("RAG Source", "content", KnowledgeType.RagIndex)
        };

        // Act
        var result = await _builder.BuildUserPromptAsync(documentText, knowledge, CancellationToken.None);

        // Assert
        result.Should().NotContain("# Reference Materials");
        result.Should().NotContain("RAG Source");
    }

    [Fact]
    public async Task BuildUserPromptAsync_MixedKnowledge_OnlyIncludesInline()
    {
        // Arrange
        var documentText = "Document.";
        var knowledge = new[]
        {
            CreateKnowledge("Inline Ref", "Inline content", KnowledgeType.Inline),
            CreateKnowledge("RAG Ref", "RAG content", KnowledgeType.RagIndex),
            CreateKnowledge("Doc Ref", "Doc content", KnowledgeType.Document)
        };

        // Act
        var result = await _builder.BuildUserPromptAsync(documentText, knowledge, CancellationToken.None);

        // Assert
        result.Should().Contain("## Inline Ref");
        result.Should().Contain("Inline content");
        result.Should().NotContain("RAG Ref");
        result.Should().NotContain("Doc Ref");
    }

    #endregion

    #region BuildContinuationPrompt Tests

    [Fact]
    public void BuildContinuationPrompt_EmptyHistory_IncludesCurrentAndNewRequest()
    {
        // Arrange
        var workingDoc = "Current analysis output.";
        var userMessage = "Please add more detail.";

        // Act
        var result = _builder.BuildContinuationPrompt([], userMessage, workingDoc);

        // Assert
        result.Should().Contain("# Current Analysis");
        result.Should().Contain("Current analysis output.");
        result.Should().Contain("# New Request");
        result.Should().Contain("User: Please add more detail.");
        result.Should().NotContain("# Conversation History");
    }

    [Fact]
    public void BuildContinuationPrompt_WithHistory_IncludesConversationHistory()
    {
        // Arrange
        var history = new[]
        {
            CreateChatMessage("user", "First question"),
            CreateChatMessage("assistant", "First response")
        };
        var workingDoc = "Working document.";
        var userMessage = "Follow-up question";

        // Act
        var result = _builder.BuildContinuationPrompt(history, userMessage, workingDoc);

        // Assert
        result.Should().Contain("# Conversation History");
        result.Should().Contain("User: First question");
        result.Should().Contain("Assistant: First response");
        result.Should().Contain("User: Follow-up question");
    }

    [Fact]
    public void BuildContinuationPrompt_ExceedsMaxHistory_TruncatesToLimit()
    {
        // Arrange - options has MaxChatHistoryMessages = 10
        // Use format "Msg-XX-" to avoid substring matching issues (e.g., "Msg-1-" vs "Msg-11-")
        var history = Enumerable.Range(1, 20)
            .Select(i => CreateChatMessage(i % 2 == 0 ? "assistant" : "user", $"Msg-{i:D2}-end"))
            .ToArray();
        var workingDoc = "Doc.";
        var userMessage = "New message";

        // Act
        var result = _builder.BuildContinuationPrompt(history, userMessage, workingDoc);

        // Assert
        // Should only contain the last 10 messages (messages 11-20, by timestamp descending then reversed)
        result.Should().Contain("Msg-11-end");
        result.Should().Contain("Msg-20-end");
        result.Should().NotContain("Msg-01-end");  // First message should be truncated
        result.Should().NotContain("Msg-10-end");  // 10th message should be truncated
    }

    [Fact]
    public void BuildContinuationPrompt_InstructsFullUpdate()
    {
        // Arrange
        var result = _builder.BuildContinuationPrompt([], "Update", "Doc");

        // Assert - should instruct to provide complete updated analysis
        result.Should().Contain("complete updated analysis");
        result.Should().Contain("not just the changes");
    }

    #endregion

    #region Helper Methods

    private static AnalysisAction CreateAction(string systemPrompt) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Action",
        SystemPrompt = systemPrompt,
        SortOrder = 1
    };

    private static AnalysisSkill CreateSkill(string promptFragment) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Skill",
        PromptFragment = promptFragment
    };

    private static AnalysisKnowledge CreateKnowledge(string name, string content, KnowledgeType type) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Content = content,
        Type = type
    };

    private static ChatMessageModel CreateChatMessage(string role, string content) =>
        new(role, content, DateTime.UtcNow);

    #endregion
}
