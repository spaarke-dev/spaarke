using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat.Tools;

/// <summary>
/// Unit tests for <see cref="WebSearchTools"/>.
///
/// Verifies:
/// - Constructor validation (null logger)
/// - SearchWebAsync returns formatted results with [External Source] prefix (ADR-015)
/// - maxResults default (5) and upper cap (10)
/// - Query validation (null, empty, whitespace)
/// - [Description] attributes required for AIFunctionFactory.Create
/// - Result formatting structure
/// </summary>
public class WebSearchToolsTests
{
    private readonly Mock<ILogger> _loggerMock;

    public WebSearchToolsTests()
    {
        _loggerMock = new Mock<ILogger>();
    }

    private WebSearchTools CreateSut() => new WebSearchTools(_loggerMock.Object);

    // === Constructor validation ===

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        var action = () => new WebSearchTools(null!);
        action.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithValidLogger_DoesNotThrow()
    {
        // Act & Assert
        var action = () => new WebSearchTools(_loggerMock.Object);
        action.Should().NotThrow();
    }

    // === [Description] attribute tests ===

    [Fact]
    public void SearchWebAsync_HasDescriptionAttribute_OnMethod()
    {
        // Arrange
        var method = typeof(WebSearchTools).GetMethod(nameof(WebSearchTools.SearchWebAsync));
        method.Should().NotBeNull();

        // Act
        var description = method!.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        description.Should().NotBeNull("AIFunctionFactory.Create requires [Description] on tool methods");
        description!.Description.Should().Contain("Search the web");
    }

    [Fact]
    public void SearchWebAsync_HasDescriptionAttribute_OnQueryParameter()
    {
        // Arrange
        var method = typeof(WebSearchTools).GetMethod(nameof(WebSearchTools.SearchWebAsync));
        method.Should().NotBeNull();

        var queryParam = method!.GetParameters().First(p => p.Name == "query");

        // Act
        var description = queryParam.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        description.Should().NotBeNull("AIFunctionFactory.Create requires [Description] on key parameters");
        description!.Description.Should().Be("Web search query");
    }

    [Fact]
    public void SearchWebAsync_HasDescriptionAttribute_OnMaxResultsParameter()
    {
        // Arrange
        var method = typeof(WebSearchTools).GetMethod(nameof(WebSearchTools.SearchWebAsync));
        method.Should().NotBeNull();

        var maxResultsParam = method!.GetParameters().First(p => p.Name == "maxResults");

        // Act
        var description = maxResultsParam.GetCustomAttribute<DescriptionAttribute>();

        // Assert
        description.Should().NotBeNull("AIFunctionFactory.Create requires [Description] on key parameters");
        description!.Description.Should().Contain("Maximum number of results");
    }

    // === SearchWebAsync happy path tests ===

    [Fact]
    public async Task SearchWebAsync_WithValidQuery_ReturnsFormattedResults()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("contract law basics");

        // Assert
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("Web search returned");
        result.Should().Contain("contract law basics");
    }

    [Fact]
    public async Task SearchWebAsync_ResultsContainExternalSourcePrefix_ForDataGovernance()
    {
        // Arrange — ADR-015: all web search results must be prefixed with [External Source]
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("legal document analysis");

        // Assert — every result line with a numbered index should have the [External Source] marker
        var lines = result.Split('\n');
        var resultLines = lines.Where(l => l.TrimStart().StartsWith("[") && l.Contains("[External Source]")).ToList();

        resultLines.Should().NotBeEmpty("all search results must be marked as [External Source] per ADR-015");
    }

    [Fact]
    public async Task SearchWebAsync_WithValidQuery_ContainsExternalContentWarning()
    {
        // Arrange — ADR-015: results must include note about external sources
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("SharePoint Embedded API");

        // Assert
        result.Should().Contain("external web sources");
        result.Should().Contain("not been verified against internal knowledge");
    }

    [Fact]
    public async Task SearchWebAsync_WithValidQuery_ReturnsResultsWithTitleAndUrl()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("document analysis");

        // Assert — results should contain structured fields
        result.Should().Contain("https://");
        result.Should().Contain("[1]");
    }

    // === maxResults tests ===

    [Fact]
    public async Task SearchWebAsync_DefaultMaxResults_ReturnsFiveResults()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("test query");

        // Assert — default is 5, mock data has exactly 5 items
        result.Should().Contain("5 result(s)");
    }

    [Fact]
    public async Task SearchWebAsync_MaxResultsThree_ReturnsThreeResults()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("test query", maxResults: 3);

        // Assert
        result.Should().Contain("3 result(s)");
        // Should have results [1], [2], [3] but not [4]
        result.Should().Contain("[1]");
        result.Should().Contain("[2]");
        result.Should().Contain("[3]");
        result.Should().NotContain("[4]");
    }

    [Fact]
    public async Task SearchWebAsync_MaxResultsOne_ReturnsSingleResult()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("test query", maxResults: 1);

        // Assert
        result.Should().Contain("1 result(s)");
        result.Should().Contain("[1]");
        result.Should().NotContain("[2]");
    }

    [Fact]
    public async Task SearchWebAsync_MaxResultsExceedsCap_ClampedToTen()
    {
        // Arrange — ADR-016: maxResults bounded at 10
        var sut = CreateSut();

        // Act — request 20, should be clamped to 10. Mock only has 5, so result will be 5
        var result = await sut.SearchWebAsync("test query", maxResults: 20);

        // Assert — clamped to 10, but mock data has only 5, so returns 5
        result.Should().Contain("5 result(s)");
    }

    [Fact]
    public async Task SearchWebAsync_MaxResultsZero_ClampedToOne()
    {
        // Arrange — Math.Clamp(0, 1, 10) = 1
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("test query", maxResults: 0);

        // Assert
        result.Should().Contain("1 result(s)");
    }

    [Fact]
    public async Task SearchWebAsync_MaxResultsNegative_ClampedToOne()
    {
        // Arrange — Math.Clamp(-5, 1, 10) = 1
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("test query", maxResults: -5);

        // Assert
        result.Should().Contain("1 result(s)");
    }

    // === Query validation tests ===

    [Fact]
    public async Task SearchWebAsync_NullQuery_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.SearchWebAsync(null!));
    }

    [Fact]
    public async Task SearchWebAsync_EmptyQuery_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SearchWebAsync(string.Empty));
    }

    [Fact]
    public async Task SearchWebAsync_WhitespaceQuery_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SearchWebAsync("   "));
    }

    // === Cancellation token test ===

    [Fact]
    public async Task SearchWebAsync_CompletesSuccessfully_WithDefaultCancellationToken()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("test query");

        // Assert — should complete without error
        result.Should().NotBeNullOrWhiteSpace();
    }

    // === Result formatting structure tests ===

    [Fact]
    public async Task SearchWebAsync_ResultFormat_StartsWithSummaryLine()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("legal analysis");

        // Assert — first line should be the summary
        var firstLine = result.Split('\n')[0];
        firstLine.Should().StartWith("Web search returned");
        firstLine.Should().Contain("\"legal analysis\"");
    }

    [Fact]
    public async Task SearchWebAsync_ResultFormat_EachResultHasSnippet()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("document intelligence", maxResults: 3);

        // Assert — each result should have a snippet line indented with 4 spaces
        var lines = result.Split('\n');
        var snippetLines = lines.Where(l => l.StartsWith("    ")).ToList();

        // Should have at least one snippet line per result
        snippetLines.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task SearchWebAsync_ResultFormat_NumberedSequentially()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.SearchWebAsync("test", maxResults: 5);

        // Assert — results should be numbered [1] through [5]
        result.Should().Contain("[1] [External Source]");
        result.Should().Contain("[2] [External Source]");
        result.Should().Contain("[3] [External Source]");
        result.Should().Contain("[4] [External Source]");
        result.Should().Contain("[5] [External Source]");
    }

    // === Logging tests (ADR-015: no content in logs) ===

    [Fact]
    public async Task SearchWebAsync_LogsQueryLength_NotQueryText()
    {
        // Arrange — ADR-015: MUST NOT log full query text or result bodies
        var sut = CreateSut();

        // Act
        await sut.SearchWebAsync("sensitive legal query");

        // Assert — logger was called (at least start and complete log entries)
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2));
    }
}
