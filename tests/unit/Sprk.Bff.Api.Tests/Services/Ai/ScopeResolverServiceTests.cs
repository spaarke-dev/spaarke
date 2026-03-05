using System.Net;
using System.Reflection;
using System.Text.Json;
using Azure.Core;
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
/// Unit tests for ScopeResolverService.ResolveScopesAsync().
/// Tests the full resolution pipeline: empty arrays, single IDs, multiple IDs,
/// missing/not-found IDs, and mixed found/not-found scenarios.
/// Uses mocked HttpMessageHandler to intercept Dataverse Web API calls.
/// </summary>
public class ScopeResolverServiceResolveScopesTests : IDisposable
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<ScopeResolverService>> _loggerMock;
    private readonly ScopeResolverService _service;

    public ScopeResolverServiceResolveScopesTests()
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

        // Bypass Azure AD authentication by setting _currentToken via reflection
        // to a non-expired fake token. This prevents the ClientSecretCredential
        // from making real calls to Azure AD during unit tests.
        var tokenField = typeof(ScopeResolverService)
            .GetField("_currentToken", BindingFlags.NonPublic | BindingFlags.Instance)!;
        tokenField.SetValue(_service, new AccessToken("fake-token", DateTimeOffset.UtcNow.AddHours(1)));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>
    /// Sets up URL-based routing for the mock HTTP handler.
    /// Maps request URIs (containing entity set names and IDs) to response status codes and bodies.
    /// </summary>
    private void SetupHttpResponses(Dictionary<string, (HttpStatusCode StatusCode, object? Body)> urlResponses)
    {
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var uri = request.RequestUri!.ToString();
                foreach (var (urlFragment, (statusCode, body)) in urlResponses)
                {
                    if (uri.Contains(urlFragment))
                    {
                        var response = new HttpResponseMessage(statusCode);
                        if (body != null)
                        {
                            response.Content = new StringContent(
                                JsonSerializer.Serialize(body),
                                System.Text.Encoding.UTF8,
                                "application/json");
                        }
                        return response;
                    }
                }
                // Default: return 404 for unmatched URLs
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
    }

    #region ResolveScopesAsync Tests — Empty Input

    [Fact]
    public async Task ResolveScopesAsync_EmptyArrays_ReturnsEmptyScopes()
    {
        // Arrange & Act
        var result = await _service.ResolveScopesAsync([], [], [], CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveScopesAsync_AllEmptyArrays_DoesNotMakeHttpCalls()
    {
        // Arrange & Act
        await _service.ResolveScopesAsync([], [], [], CancellationToken.None);

        // Assert — no HTTP calls should be made
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region ResolveScopesAsync Tests — Single IDs

    [Fact]
    public async Task ResolveScopesAsync_SingleSkillId_ReturnsResolvedSkill()
    {
        // Arrange
        var skillId = Guid.NewGuid();
        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_promptfragments({skillId})"] = (HttpStatusCode.OK, new
            {
                sprk_promptfragmentid = skillId,
                sprk_name = "Contract Analysis",
                sprk_description = "Analyzes contract clauses",
                sprk_promptfragment = "You are a contract analysis expert",
                sprk_SkillTypeId = new { sprk_name = "Legal Analysis" }
            })
        });

        // Act
        var result = await _service.ResolveScopesAsync(new[] { skillId }, [], [], CancellationToken.None);

        // Assert
        result.Skills.Should().HaveCount(1);
        result.Skills[0].Id.Should().Be(skillId);
        result.Skills[0].Name.Should().Be("Contract Analysis");
        result.Skills[0].PromptFragment.Should().Be("You are a contract analysis expert");
        result.Skills[0].Category.Should().Be("Legal Analysis");
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveScopesAsync_SingleKnowledgeId_ReturnsResolvedKnowledge()
    {
        // Arrange
        var knowledgeId = Guid.NewGuid();
        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_analysisknowledges({knowledgeId})"] = (HttpStatusCode.OK, new
            {
                sprk_analysisknowledgeid = knowledgeId,
                sprk_name = "Company Standards",
                sprk_description = "Internal compliance standards",
                sprk_content = "All contracts must include indemnification clauses.",
                sprk_KnowledgeTypeId = new { sprk_name = "Standards" }
            })
        });

        // Act
        var result = await _service.ResolveScopesAsync([], new[] { knowledgeId }, [], CancellationToken.None);

        // Assert
        result.Knowledge.Should().HaveCount(1);
        result.Knowledge[0].Id.Should().Be(knowledgeId);
        result.Knowledge[0].Name.Should().Be("Company Standards");
        result.Knowledge[0].Content.Should().Contain("indemnification");
        result.Skills.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveScopesAsync_SingleToolId_ReturnsResolvedTool()
    {
        // Arrange
        var toolId = Guid.NewGuid();
        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_analysistools({toolId})"] = (HttpStatusCode.OK, new
            {
                sprk_analysistoolid = toolId,
                sprk_name = "Entity Extractor",
                sprk_description = "Extracts named entities from documents",
                sprk_handlerclass = "EntityExtractorHandler",
                sprk_configuration = "{\"maxEntities\": 50}",
                sprk_ToolTypeId = new { sprk_name = "Extraction" }
            })
        });

        // Act
        var result = await _service.ResolveScopesAsync([], [], new[] { toolId }, CancellationToken.None);

        // Assert
        result.Tools.Should().HaveCount(1);
        result.Tools[0].Id.Should().Be(toolId);
        result.Tools[0].Name.Should().Be("Entity Extractor");
        result.Tools[0].HandlerClass.Should().Be("EntityExtractorHandler");
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
    }

    #endregion

    #region ResolveScopesAsync Tests — Multiple IDs

    [Fact]
    public async Task ResolveScopesAsync_MultipleKnowledgeIds_ReturnsAllResolved()
    {
        // Arrange
        var knowledgeId1 = Guid.NewGuid();
        var knowledgeId2 = Guid.NewGuid();
        var knowledgeId3 = Guid.NewGuid();

        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_analysisknowledges({knowledgeId1})"] = (HttpStatusCode.OK, new
            {
                sprk_analysisknowledgeid = knowledgeId1,
                sprk_name = "Knowledge A",
                sprk_content = "Content A",
                sprk_KnowledgeTypeId = new { sprk_name = "Standards" }
            }),
            [$"sprk_analysisknowledges({knowledgeId2})"] = (HttpStatusCode.OK, new
            {
                sprk_analysisknowledgeid = knowledgeId2,
                sprk_name = "Knowledge B",
                sprk_content = "Content B",
                sprk_KnowledgeTypeId = new { sprk_name = "Standards" }
            }),
            [$"sprk_analysisknowledges({knowledgeId3})"] = (HttpStatusCode.OK, new
            {
                sprk_analysisknowledgeid = knowledgeId3,
                sprk_name = "Knowledge C",
                sprk_content = "Content C",
                sprk_KnowledgeTypeId = new { sprk_name = "Regulations" }
            })
        });

        // Act
        var result = await _service.ResolveScopesAsync([], new[] { knowledgeId1, knowledgeId2, knowledgeId3 }, [], CancellationToken.None);

        // Assert
        result.Knowledge.Should().HaveCount(3);
        result.Knowledge.Select(k => k.Name).Should().Contain(new[] { "Knowledge A", "Knowledge B", "Knowledge C" });
    }

    #endregion

    #region ResolveScopesAsync Tests — Missing/Not-Found IDs

    [Fact]
    public async Task ResolveScopesAsync_MissingSkillId_FilteredOutFromResults()
    {
        // Arrange
        var missingSkillId = Guid.NewGuid();
        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_promptfragments({missingSkillId})"] = (HttpStatusCode.NotFound, null)
        });

        // Act
        var result = await _service.ResolveScopesAsync(new[] { missingSkillId }, [], [], CancellationToken.None);

        // Assert — missing IDs filtered out, no nulls in result
        result.Skills.Should().BeEmpty();
        result.Skills.Should().NotContainNulls();
    }

    [Fact]
    public async Task ResolveScopesAsync_MissingKnowledgeId_FilteredOutFromResults()
    {
        // Arrange
        var missingKnowledgeId = Guid.NewGuid();
        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_analysisknowledges({missingKnowledgeId})"] = (HttpStatusCode.NotFound, null)
        });

        // Act
        var result = await _service.ResolveScopesAsync([], new[] { missingKnowledgeId }, [], CancellationToken.None);

        // Assert
        result.Knowledge.Should().BeEmpty();
        result.Knowledge.Should().NotContainNulls();
    }

    [Fact]
    public async Task ResolveScopesAsync_MissingToolId_FilteredOutFromResults()
    {
        // Arrange
        var missingToolId = Guid.NewGuid();
        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_analysistools({missingToolId})"] = (HttpStatusCode.NotFound, null)
        });

        // Act
        var result = await _service.ResolveScopesAsync([], [], new[] { missingToolId }, CancellationToken.None);

        // Assert
        result.Tools.Should().BeEmpty();
        result.Tools.Should().NotContainNulls();
    }

    #endregion

    #region ResolveScopesAsync Tests — Mixed Found/Not-Found

    [Fact]
    public async Task ResolveScopesAsync_MixedFoundAndMissing_ReturnsOnlyFoundRecords()
    {
        // Arrange
        var foundSkillId = Guid.NewGuid();
        var missingSkillId = Guid.NewGuid();
        var foundKnowledgeId = Guid.NewGuid();
        var missingKnowledgeId = Guid.NewGuid();

        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_promptfragments({foundSkillId})"] = (HttpStatusCode.OK, new
            {
                sprk_promptfragmentid = foundSkillId,
                sprk_name = "Found Skill",
                sprk_promptfragment = "Found skill fragment",
                sprk_SkillTypeId = new { sprk_name = "General" }
            }),
            [$"sprk_promptfragments({missingSkillId})"] = (HttpStatusCode.NotFound, null),
            [$"sprk_analysisknowledges({foundKnowledgeId})"] = (HttpStatusCode.OK, new
            {
                sprk_analysisknowledgeid = foundKnowledgeId,
                sprk_name = "Found Knowledge",
                sprk_content = "Found knowledge content",
                sprk_KnowledgeTypeId = new { sprk_name = "Standards" }
            }),
            [$"sprk_analysisknowledges({missingKnowledgeId})"] = (HttpStatusCode.NotFound, null)
        });

        // Act
        var result = await _service.ResolveScopesAsync(
            new[] { foundSkillId, missingSkillId },
            new[] { foundKnowledgeId, missingKnowledgeId },
            [],
            CancellationToken.None);

        // Assert — only found records returned, no nulls
        result.Skills.Should().HaveCount(1);
        result.Skills[0].Id.Should().Be(foundSkillId);
        result.Skills[0].Name.Should().Be("Found Skill");
        result.Skills.Should().NotContainNulls();

        result.Knowledge.Should().HaveCount(1);
        result.Knowledge[0].Id.Should().Be(foundKnowledgeId);
        result.Knowledge[0].Name.Should().Be("Found Knowledge");
        result.Knowledge.Should().NotContainNulls();
    }

    #endregion

    #region ResolveScopesAsync Tests — All Three Arrays Populated

    [Fact]
    public async Task ResolveScopesAsync_AllThreeArraysPopulated_ResolvesInParallel()
    {
        // Arrange
        var skillId = Guid.NewGuid();
        var knowledgeId = Guid.NewGuid();
        var toolId = Guid.NewGuid();

        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_promptfragments({skillId})"] = (HttpStatusCode.OK, new
            {
                sprk_promptfragmentid = skillId,
                sprk_name = "Parallel Skill",
                sprk_promptfragment = "Skill prompt",
                sprk_SkillTypeId = new { sprk_name = "Legal Analysis" }
            }),
            [$"sprk_analysisknowledges({knowledgeId})"] = (HttpStatusCode.OK, new
            {
                sprk_analysisknowledgeid = knowledgeId,
                sprk_name = "Parallel Knowledge",
                sprk_content = "Knowledge content",
                sprk_KnowledgeTypeId = new { sprk_name = "Standards" }
            }),
            [$"sprk_analysistools({toolId})"] = (HttpStatusCode.OK, new
            {
                sprk_analysistoolid = toolId,
                sprk_name = "Parallel Tool",
                sprk_description = "Tool for parallel test",
                sprk_handlerclass = "GenericAnalysisHandler",
                sprk_ToolTypeId = new { sprk_name = "Analysis" }
            })
        });

        // Act
        var result = await _service.ResolveScopesAsync(
            new[] { skillId },
            new[] { knowledgeId },
            new[] { toolId },
            CancellationToken.None);

        // Assert
        result.Skills.Should().HaveCount(1);
        result.Skills[0].Name.Should().Be("Parallel Skill");

        result.Knowledge.Should().HaveCount(1);
        result.Knowledge[0].Name.Should().Be("Parallel Knowledge");

        result.Tools.Should().HaveCount(1);
        result.Tools[0].Name.Should().Be("Parallel Tool");
    }

    [Fact]
    public async Task ResolveScopesAsync_AllThreeArraysWithMixed_ReturnsCorrectCounts()
    {
        // Arrange — 2 skills (1 found, 1 missing), 1 knowledge (found), 2 tools (both found)
        var skill1 = Guid.NewGuid();
        var skill2Missing = Guid.NewGuid();
        var knowledge1 = Guid.NewGuid();
        var tool1 = Guid.NewGuid();
        var tool2 = Guid.NewGuid();

        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_promptfragments({skill1})"] = (HttpStatusCode.OK, new
            {
                sprk_promptfragmentid = skill1,
                sprk_name = "Skill One",
                sprk_promptfragment = "Fragment one"
            }),
            [$"sprk_promptfragments({skill2Missing})"] = (HttpStatusCode.NotFound, null),
            [$"sprk_analysisknowledges({knowledge1})"] = (HttpStatusCode.OK, new
            {
                sprk_analysisknowledgeid = knowledge1,
                sprk_name = "Knowledge One",
                sprk_content = "Content one",
                sprk_KnowledgeTypeId = new { sprk_name = "Standards" }
            }),
            [$"sprk_analysistools({tool1})"] = (HttpStatusCode.OK, new
            {
                sprk_analysistoolid = tool1,
                sprk_name = "Tool One",
                sprk_handlerclass = "HandlerA",
                sprk_ToolTypeId = new { sprk_name = "Extraction" }
            }),
            [$"sprk_analysistools({tool2})"] = (HttpStatusCode.OK, new
            {
                sprk_analysistoolid = tool2,
                sprk_name = "Tool Two",
                sprk_handlerclass = "HandlerB",
                sprk_ToolTypeId = new { sprk_name = "Analysis" }
            })
        });

        // Act
        var result = await _service.ResolveScopesAsync(
            new[] { skill1, skill2Missing },
            new[] { knowledge1 },
            new[] { tool1, tool2 },
            CancellationToken.None);

        // Assert
        result.Skills.Should().HaveCount(1, "one of two skills was not found");
        result.Knowledge.Should().HaveCount(1);
        result.Tools.Should().HaveCount(2);
    }

    #endregion

    #region ResolveScopesAsync Tests — Logging

    [Fact]
    public async Task ResolveScopesAsync_LogsRequestedCounts()
    {
        // Arrange
        var skillId = Guid.NewGuid();
        SetupHttpResponses(new Dictionary<string, (HttpStatusCode, object?)>
        {
            [$"sprk_promptfragments({skillId})"] = (HttpStatusCode.OK, new
            {
                sprk_promptfragmentid = skillId,
                sprk_name = "Logged Skill",
                sprk_promptfragment = "Fragment"
            })
        });

        // Act
        await _service.ResolveScopesAsync(new[] { skillId }, [], [], CancellationToken.None);

        // Assert — Verify logging occurred
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
}

/// <summary>
/// Unit tests for ScopeResolverService - stub action lookup and search functionality.
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

    // Note: ResolveScopesAsync logging tests moved to ScopeResolverServiceResolveScopesTests class
    // which has proper HTTP mocking and auth bypass for the full resolution pipeline.

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
        ((int)ToolType.Custom).Should().Be(99);
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

        // Bypass Azure AD authentication by setting _currentToken via reflection
        var tokenField = typeof(ScopeResolverService)
            .GetField("_currentToken", BindingFlags.NonPublic | BindingFlags.Instance)!;
        tokenField.SetValue(_service, new AccessToken("fake-token", DateTimeOffset.UtcNow.AddHours(1)));
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
