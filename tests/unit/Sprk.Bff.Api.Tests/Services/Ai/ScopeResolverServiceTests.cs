using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for ScopeResolverService - scope resolution service.
/// Tests Phase 1 stub behavior and action lookup.
/// </summary>
public class ScopeResolverServiceTests : IDisposable
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<ScopeResolverService>> _loggerMock;
    private readonly ScopeResolverService _service;

    // Known stub action IDs from ScopeResolverService
    private static readonly Guid SummarizeActionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ReviewAgreementActionId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public ScopeResolverServiceTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://test.crm.dynamics.com/api/data/v9.2/")
        };
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<ScopeResolverService>>();

        // Setup required configuration
        _configurationMock.Setup(c => c["Dataverse:ServiceUrl"]).Returns("https://test.crm.dynamics.com");
        _configurationMock.Setup(c => c["TENANT_ID"]).Returns("test-tenant-id");
        _configurationMock.Setup(c => c["API_APP_ID"]).Returns("test-app-id");
        _configurationMock.Setup(c => c["API_CLIENT_SECRET"]).Returns("test-secret");

        _service = new ScopeResolverService(
            _dataverseServiceMock.Object,
            _playbookServiceMock.Object,
            _httpClient,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    #region ResolveScopesAsync Tests

    [Fact]
    public async Task ResolveScopesAsync_Phase1_ReturnsEmptyScopes()
    {
        // Arrange
        var skillIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var knowledgeIds = new[] { Guid.NewGuid() };
        var toolIds = new[] { Guid.NewGuid() };

        // Act
        var result = await _service.ResolveScopesAsync(skillIds, knowledgeIds, toolIds, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveScopesAsync_EmptyArrays_ReturnsEmptyScopes()
    {
        // Arrange & Act
        var result = await _service.ResolveScopesAsync([], [], [], CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Skills.Should().BeEmpty();
    }

    #endregion

    #region ResolvePlaybookScopesAsync Tests

    [Fact]
    public async Task ResolvePlaybookScopesAsync_Phase1_ReturnsEmptyScopes()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var result = await _service.ResolvePlaybookScopesAsync(playbookId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    #endregion

    #region ResolveNodeScopesAsync Tests

    [Fact]
    public async Task ResolveNodeScopesAsync_Phase1_ReturnsEmptyScopes()
    {
        // Arrange
        var nodeId = Guid.NewGuid();

        // Act
        var result = await _service.ResolveNodeScopesAsync(nodeId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveNodeScopesAsync_AnyNodeId_ReturnsValidResolvedScopes()
    {
        // Arrange - different node IDs should all return valid (empty) scopes
        var nodeIds = new[] { Guid.NewGuid(), Guid.Empty, Guid.Parse("11111111-1111-1111-1111-111111111111") };

        foreach (var nodeId in nodeIds)
        {
            // Act
            var result = await _service.ResolveNodeScopesAsync(nodeId, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Skills.Should().NotBeNull();
            result.Knowledge.Should().NotBeNull();
            result.Tools.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ResolveNodeScopesAsync_LogsNodeId()
    {
        // Arrange
        var nodeId = Guid.NewGuid();

        // Act
        await _service.ResolveNodeScopesAsync(nodeId, CancellationToken.None);

        // Assert - Verify logging occurred
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Resolving scopes from node")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetActionAsync Tests

    [Fact]
    public async Task GetActionAsync_KnownSummarizeAction_ReturnsStubAction()
    {
        // Act
        var result = await _service.GetActionAsync(SummarizeActionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(SummarizeActionId);
        result.Name.Should().Be("Summarize Document");
        result.SystemPrompt.Should().Contain("summar"); // Matches "summaries" or "summary"
        result.SortOrder.Should().Be(1);
    }

    [Fact]
    public async Task GetActionAsync_KnownReviewAction_ReturnsStubAction()
    {
        // Act
        var result = await _service.GetActionAsync(ReviewAgreementActionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(ReviewAgreementActionId);
        result.Name.Should().Be("Review Agreement");
        result.SystemPrompt.Should().Contain("legal");
        result.SortOrder.Should().Be(2);
    }

    [Fact]
    public async Task GetActionAsync_UnknownAction_ReturnsDefaultAction()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act
        var result = await _service.GetActionAsync(unknownId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(unknownId);
        result.Name.Should().Be("Default Analysis");
        result.SystemPrompt.Should().Contain("AI assistant");
        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task GetActionAsync_DefaultAction_HasValidSystemPrompt()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act
        var result = await _service.GetActionAsync(unknownId, CancellationToken.None);

        // Assert
        result!.SystemPrompt.Should().NotBeNullOrEmpty();
        result.SystemPrompt.Should().Contain("thorough");
        result.SystemPrompt.Should().Contain("accurate");
    }

    [Fact]
    public async Task GetActionAsync_StubActions_HaveRequiredFields()
    {
        // Act
        var summarize = await _service.GetActionAsync(SummarizeActionId, CancellationToken.None);
        var review = await _service.GetActionAsync(ReviewAgreementActionId, CancellationToken.None);

        // Assert - All stub actions should have required fields
        summarize!.Id.Should().NotBeEmpty();
        summarize.Name.Should().NotBeNullOrEmpty();
        summarize.SystemPrompt.Should().NotBeNullOrEmpty();
        summarize.Description.Should().NotBeNullOrEmpty();

        review!.Id.Should().NotBeEmpty();
        review.Name.Should().NotBeNullOrEmpty();
        review.SystemPrompt.Should().NotBeNullOrEmpty();
        review.Description.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task ResolveScopesAsync_LogsRequestedCounts()
    {
        // Arrange
        var skillIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        // Act
        await _service.ResolveScopesAsync(skillIds, [], [], CancellationToken.None);

        // Assert - Verify logging occurred (using loose verification)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Resolving scopes")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region SearchScopesAsync Tests

    [Fact]
    public async Task SearchScopesAsync_NoFilters_ReturnsAllScopeTypes()
    {
        // Arrange
        var query = new ScopeSearchQuery();

        // Act
        var result = await _service.SearchScopesAsync(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Actions.Should().NotBeNull();
        result.Skills.Should().NotBeNull();
        result.Knowledge.Should().NotBeNull();
        result.Tools.Should().NotBeNull();
        result.TotalCount.Should().BeGreaterThan(0);
        result.CountsByType.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchScopesAsync_TextSearch_MatchesNameAndDescription()
    {
        // Arrange - Search for "Legal" which appears in skills
        var query = new ScopeSearchQuery { SearchText = "Legal" };

        // Act
        var result = await _service.SearchScopesAsync(query, CancellationToken.None);

        // Assert
        result.Skills.Should().NotBeEmpty();
        result.Skills.Should().Contain(s => s.Name.Contains("Legal", StringComparison.OrdinalIgnoreCase) ||
                                           (s.Description != null && s.Description.Contains("Legal", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task SearchScopesAsync_TypeFilter_ReturnsOnlySpecifiedTypes()
    {
        // Arrange - Filter for skills only
        var query = new ScopeSearchQuery { ScopeTypes = new[] { ScopeType.Skill } };

        // Act
        var result = await _service.SearchScopesAsync(query, CancellationToken.None);

        // Assert
        result.Skills.Should().NotBeEmpty();
        result.Actions.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
        result.CountsByType.Should().ContainKey(ScopeType.Skill);
        result.CountsByType.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchScopesAsync_MultipleTypeFilters_ReturnsSpecifiedTypes()
    {
        // Arrange - Filter for actions and tools
        var query = new ScopeSearchQuery { ScopeTypes = new[] { ScopeType.Action, ScopeType.Tool } };

        // Act
        var result = await _service.SearchScopesAsync(query, CancellationToken.None);

        // Assert
        result.Actions.Should().NotBeEmpty();
        result.Tools.Should().NotBeEmpty();
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchScopesAsync_TextSearchNoMatch_ReturnsEmptyResults()
    {
        // Arrange - Search for text that doesn't exist
        var query = new ScopeSearchQuery { SearchText = "XyzNonExistent123" };

        // Act
        var result = await _service.SearchScopesAsync(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(0);
        result.Actions.Should().BeEmpty();
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchScopesAsync_Pagination_RespectsPageAndPageSize()
    {
        // Arrange
        var query = new ScopeSearchQuery
        {
            ScopeTypes = new[] { ScopeType.Tool },
            Page = 1,
            PageSize = 2
        };

        // Act
        var result = await _service.SearchScopesAsync(query, CancellationToken.None);

        // Assert
        result.Tools.Length.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task SearchScopesAsync_CaseInsensitive_MatchesDifferentCases()
    {
        // Arrange - Search for "risk" (lowercase)
        var query = new ScopeSearchQuery { SearchText = "risk" };

        // Act
        var result = await _service.SearchScopesAsync(query, CancellationToken.None);

        // Assert - Should find "Risk Assessment" skill and "Risk Detector" tool
        var matchingItems = result.Skills.Concat<object>(result.Tools)
            .Count();
        matchingItems.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchScopesAsync_CountsByType_AccurateBreakdown()
    {
        // Arrange
        var query = new ScopeSearchQuery();

        // Act
        var result = await _service.SearchScopesAsync(query, CancellationToken.None);

        // Assert
        result.CountsByType.Should().ContainKey(ScopeType.Action);
        result.CountsByType.Should().ContainKey(ScopeType.Skill);
        result.CountsByType.Should().ContainKey(ScopeType.Knowledge);
        result.CountsByType.Should().ContainKey(ScopeType.Tool);

        // Total should match sum of counts
        var expectedTotal = result.CountsByType.Values.Sum();
        result.TotalCount.Should().Be(expectedTotal);
    }

    [Fact]
    public async Task SearchScopesAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = async () => await _service.SearchScopesAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SearchScopesAsync_PerformanceUnder1Second_ForTypicalQuery()
    {
        // Arrange
        var query = new ScopeSearchQuery { SearchText = "Document" };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = await _service.SearchScopesAsync(query, CancellationToken.None);
        stopwatch.Stop();

        // Assert
        result.Should().NotBeNull();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    #endregion
}

/// <summary>
/// Tests for scope resolution model types.
/// </summary>
public class ScopeModelsTests
{
    [Fact]
    public void ResolvedScopes_CanBeCreated()
    {
        // Arrange & Act
        var scopes = new ResolvedScopes([], [], []);

        // Assert
        scopes.Skills.Should().NotBeNull();
        scopes.Knowledge.Should().NotBeNull();
        scopes.Tools.Should().NotBeNull();
    }

    [Fact]
    public void ResolvedScopes_WithData_ContainsAllItems()
    {
        // Arrange
        var skills = new[] { new AnalysisSkill { Id = Guid.NewGuid(), Name = "Skill1", PromptFragment = "Do X" } };
        var knowledge = new[] { new AnalysisKnowledge { Id = Guid.NewGuid(), Name = "K1", Type = KnowledgeType.Inline } };
        var tools = new[] { new AnalysisTool { Id = Guid.NewGuid(), Name = "Tool1", Type = ToolType.EntityExtractor } };

        // Act
        var scopes = new ResolvedScopes(skills, knowledge, tools);

        // Assert
        scopes.Skills.Should().HaveCount(1);
        scopes.Knowledge.Should().HaveCount(1);
        scopes.Tools.Should().HaveCount(1);
    }

    [Fact]
    public void AnalysisAction_RequiredFieldsAreSet()
    {
        // Arrange & Act
        var action = new AnalysisAction
        {
            Id = Guid.NewGuid(),
            Name = "Test Action",
            SystemPrompt = "You are helpful.",
            SortOrder = 5
        };

        // Assert
        action.Id.Should().NotBeEmpty();
        action.Name.Should().Be("Test Action");
        action.SystemPrompt.Should().Be("You are helpful.");
        action.SortOrder.Should().Be(5);
        action.Description.Should().BeNull();
    }

    [Fact]
    public void KnowledgeType_HasExpectedValues()
    {
        // Assert
        ((int)KnowledgeType.Inline).Should().Be(0);
        ((int)KnowledgeType.Document).Should().Be(1);
        ((int)KnowledgeType.RagIndex).Should().Be(2);
    }

    [Fact]
    public void ToolType_HasExpectedValues()
    {
        // Assert
        ((int)ToolType.EntityExtractor).Should().Be(0);
        ((int)ToolType.ClauseAnalyzer).Should().Be(1);
        ((int)ToolType.Custom).Should().Be(2);
    }
}

/// <summary>
/// Tests for Dataverse Web API-based scope resolution methods (GetSkillAsync, GetKnowledgeAsync, GetActionAsync).
/// Uses mocked HttpClient to test Web API query behavior.
/// </summary>
public class ScopeResolverServiceDataverseWebApiTests : IDisposable
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<ScopeResolverService>> _loggerMock;
    private readonly ScopeResolverService _service;

    public ScopeResolverServiceDataverseWebApiTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://test.crm.dynamics.com/api/data/v9.2/")
        };
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<ScopeResolverService>>();

        // Setup configuration
        _configurationMock.Setup(c => c["Dataverse:ServiceUrl"]).Returns("https://test.crm.dynamics.com");
        _configurationMock.Setup(c => c["TENANT_ID"]).Returns("test-tenant-id");
        _configurationMock.Setup(c => c["API_APP_ID"]).Returns("test-app-id");
        _configurationMock.Setup(c => c["API_CLIENT_SECRET"]).Returns("test-secret");

        _service = new ScopeResolverService(
            _dataverseServiceMock.Object,
            _playbookServiceMock.Object,
            _httpClient,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, object? responseBody = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (responseBody != null)
        {
            response.Content = new StringContent(
                JsonSerializer.Serialize(responseBody),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    #region GetSkillAsync Tests

    [Fact]
    public async Task GetSkillAsync_ReturnsSkill_WhenSkillExistsInDataverse()
    {
        // Arrange
        var skillId = Guid.NewGuid();
        var skillResponse = new
        {
            sprk_promptfragmentid = skillId,
            sprk_name = "Test Skill",
            sprk_description = "A test skill description",
            sprk_promptfragment = "Do something specific",
            sprk_SkillTypeId = new { sprk_name = "Legal Analysis" }
        };
        SetupHttpResponse(HttpStatusCode.OK, skillResponse);

        // Act
        var result = await _service.GetSkillAsync(skillId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(skillId);
        result.Name.Should().Be("Test Skill");
        result.Description.Should().Be("A test skill description");
        result.PromptFragment.Should().Be("Do something specific");
        result.Category.Should().Be("Legal Analysis");
    }

    [Fact]
    public async Task GetSkillAsync_ReturnsNull_WhenSkillNotFound()
    {
        // Arrange
        var skillId = Guid.NewGuid();
        SetupHttpResponse(HttpStatusCode.NotFound);

        // Act
        var result = await _service.GetSkillAsync(skillId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSkillAsync_MapsCategory_FromSkillTypeIdName()
    {
        // Arrange
        var skillId = Guid.NewGuid();
        var skillResponse = new
        {
            sprk_promptfragmentid = skillId,
            sprk_name = "Risk Assessment Skill",
            sprk_promptfragment = "Identify risks",
            sprk_SkillTypeId = new { sprk_name = "Risk Management" }
        };
        SetupHttpResponse(HttpStatusCode.OK, skillResponse);

        // Act
        var result = await _service.GetSkillAsync(skillId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Category.Should().Be("Risk Management");
    }

    [Fact]
    public async Task GetSkillAsync_DefaultsCategory_WhenSkillTypeIdIsNull()
    {
        // Arrange
        var skillId = Guid.NewGuid();
        var skillResponse = new
        {
            sprk_promptfragmentid = skillId,
            sprk_name = "Generic Skill",
            sprk_promptfragment = "Do generic work"
        };
        SetupHttpResponse(HttpStatusCode.OK, skillResponse);

        // Act
        var result = await _service.GetSkillAsync(skillId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Category.Should().Be("General");
    }

    #endregion

    #region GetKnowledgeAsync Tests

    [Fact]
    public async Task GetKnowledgeAsync_ReturnsKnowledge_WhenKnowledgeExists()
    {
        // Arrange
        var knowledgeId = Guid.NewGuid();
        var knowledgeResponse = new
        {
            sprk_contentid = knowledgeId,
            sprk_name = "Test Knowledge",
            sprk_description = "A test knowledge description",
            sprk_content = "This is the knowledge content",
            sprk_KnowledgeTypeId = new { sprk_name = "Standards" }
        };
        SetupHttpResponse(HttpStatusCode.OK, knowledgeResponse);

        // Act
        var result = await _service.GetKnowledgeAsync(knowledgeId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(knowledgeId);
        result.Name.Should().Be("Test Knowledge");
        result.Description.Should().Be("A test knowledge description");
        result.Content.Should().Be("This is the knowledge content");
    }

    [Fact]
    public async Task GetKnowledgeAsync_ReturnsNull_WhenKnowledgeNotFound()
    {
        // Arrange
        var knowledgeId = Guid.NewGuid();
        SetupHttpResponse(HttpStatusCode.NotFound);

        // Act
        var result = await _service.GetKnowledgeAsync(knowledgeId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetKnowledgeAsync_MapsInlineType_WhenTypeNameIsStandards()
    {
        // Arrange
        var knowledgeId = Guid.NewGuid();
        var knowledgeResponse = new
        {
            sprk_contentid = knowledgeId,
            sprk_name = "Company Standards",
            sprk_content = "Standard content",
            sprk_KnowledgeTypeId = new { sprk_name = "Standards" }
        };
        SetupHttpResponse(HttpStatusCode.OK, knowledgeResponse);

        // Act
        var result = await _service.GetKnowledgeAsync(knowledgeId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(KnowledgeType.Inline);
    }

    [Fact]
    public async Task GetKnowledgeAsync_MapsRagIndexType_WhenTypeNameIsRegulations()
    {
        // Arrange
        var knowledgeId = Guid.NewGuid();
        var deploymentId = Guid.NewGuid();
        var knowledgeResponse = new
        {
            sprk_contentid = knowledgeId,
            sprk_name = "Industry Regulations",
            sprk_content = "Regulation content",
            sprk_deploymentid = deploymentId,
            sprk_KnowledgeTypeId = new { sprk_name = "Regulations" }
        };
        SetupHttpResponse(HttpStatusCode.OK, knowledgeResponse);

        // Act
        var result = await _service.GetKnowledgeAsync(knowledgeId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(KnowledgeType.RagIndex);
        result.DeploymentId.Should().Be(deploymentId);
    }

    [Fact]
    public async Task GetKnowledgeAsync_PopulatesDeploymentId_ForRagType()
    {
        // Arrange
        var knowledgeId = Guid.NewGuid();
        var deploymentId = Guid.NewGuid();
        var knowledgeResponse = new
        {
            sprk_contentid = knowledgeId,
            sprk_name = "RAG Knowledge",
            sprk_deploymentid = deploymentId,
            sprk_KnowledgeTypeId = new { sprk_name = "rag" }
        };
        SetupHttpResponse(HttpStatusCode.OK, knowledgeResponse);

        // Act
        var result = await _service.GetKnowledgeAsync(knowledgeId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DeploymentId.Should().Be(deploymentId);
    }

    #endregion

    #region GetActionAsync Tests

    [Fact]
    public async Task GetActionAsync_ReturnsAction_WhenActionExists()
    {
        // Arrange
        var actionId = Guid.NewGuid();
        var actionResponse = new
        {
            sprk_systempromptid = actionId,
            sprk_name = "Test Action",
            sprk_description = "A test action description",
            sprk_systemprompt = "You are an analysis assistant.",
            sprk_ActionTypeId = new { sprk_name = "01 - Extraction" }
        };
        SetupHttpResponse(HttpStatusCode.OK, actionResponse);

        // Act
        var result = await _service.GetActionAsync(actionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(actionId);
        result.Name.Should().Be("Test Action");
        result.Description.Should().Be("A test action description");
        result.SystemPrompt.Should().Be("You are an analysis assistant.");
    }

    [Fact]
    public async Task GetActionAsync_ReturnsNull_WhenActionNotFound()
    {
        // Arrange
        var actionId = Guid.NewGuid();
        SetupHttpResponse(HttpStatusCode.NotFound);

        // Act
        var result = await _service.GetActionAsync(actionId, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActionAsync_ExtractsSortOrder_FromTypeNamePrefix()
    {
        // Arrange
        var actionId = Guid.NewGuid();
        var actionResponse = new
        {
            sprk_systempromptid = actionId,
            sprk_name = "Extraction Action",
            sprk_systemprompt = "Extract entities",
            sprk_ActionTypeId = new { sprk_name = "05 - Classification" }
        };
        SetupHttpResponse(HttpStatusCode.OK, actionResponse);

        // Act
        var result = await _service.GetActionAsync(actionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.SortOrder.Should().Be(5);
    }

    [Fact]
    public async Task GetActionAsync_SetsDefaultSortOrder_WhenNoPrefix()
    {
        // Arrange
        var actionId = Guid.NewGuid();
        var actionResponse = new
        {
            sprk_systempromptid = actionId,
            sprk_name = "Custom Action",
            sprk_systemprompt = "Do custom work",
            sprk_ActionTypeId = new { sprk_name = "CustomType" }
        };
        SetupHttpResponse(HttpStatusCode.OK, actionResponse);

        // Act
        var result = await _service.GetActionAsync(actionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task GetActionAsync_SetsDefaultSortOrder_WhenActionTypeIdIsNull()
    {
        // Arrange
        var actionId = Guid.NewGuid();
        var actionResponse = new
        {
            sprk_systempromptid = actionId,
            sprk_name = "Action Without Type",
            sprk_systemprompt = "Analyze documents"
        };
        SetupHttpResponse(HttpStatusCode.OK, actionResponse);

        // Act
        var result = await _service.GetActionAsync(actionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task GetActionAsync_UsesDefaultSystemPrompt_WhenNull()
    {
        // Arrange
        var actionId = Guid.NewGuid();
        var actionResponse = new
        {
            sprk_systempromptid = actionId,
            sprk_name = "Action Without Prompt"
        };
        SetupHttpResponse(HttpStatusCode.OK, actionResponse);

        // Act
        var result = await _service.GetActionAsync(actionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.SystemPrompt.Should().Contain("AI assistant");
    }

    #endregion
}
