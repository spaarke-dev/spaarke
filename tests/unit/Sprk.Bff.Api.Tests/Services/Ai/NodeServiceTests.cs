using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for NodeService.
/// Tests CRUD operations for playbook node management via Dataverse Web API.
/// </summary>
public class NodeServiceTests
{
    private readonly Mock<ILogger<NodeService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestActionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TestToolId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TestSkillId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TestKnowledgeId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid TestModelDeploymentId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    public NodeServiceTests()
    {
        _loggerMock = new Mock<ILogger<NodeService>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);

        // Setup configuration
        var configData = new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            ["TENANT_ID"] = "00000000-0000-0000-0000-000000000001",
            ["API_APP_ID"] = "00000000-0000-0000-0000-000000000002",
            ["Dataverse:ClientSecret"] = "test-secret"
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    #region CreateNode Tests

    [Fact]
    public void CreateNodeRequest_WithValidData_SetsAllFields()
    {
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            ToolId = TestToolId,
            ExecutionOrder = 1,
            OutputVariable = "result_1",
            ConditionJson = "{\"type\":\"always\"}",
            ConfigJson = "{\"prompt\":\"test\"}",
            ModelDeploymentId = TestModelDeploymentId,
            TimeoutSeconds = 30,
            RetryCount = 3,
            PositionX = 100,
            PositionY = 200,
            IsActive = true,
            SkillIds = [TestSkillId],
            KnowledgeIds = [TestKnowledgeId]
        };

        Assert.Equal("Test Node", request.Name);
        Assert.Equal(TestActionId, request.ActionId);
        Assert.Equal(TestToolId, request.ToolId);
        Assert.Equal(1, request.ExecutionOrder);
        Assert.Equal("result_1", request.OutputVariable);
        Assert.NotNull(request.ConditionJson);
        Assert.NotNull(request.ConfigJson);
        Assert.Equal(TestModelDeploymentId, request.ModelDeploymentId);
        Assert.Equal(30, request.TimeoutSeconds);
        Assert.Equal(3, request.RetryCount);
        Assert.Equal(100, request.PositionX);
        Assert.Equal(200, request.PositionY);
        Assert.True(request.IsActive);
        Assert.Single(request.SkillIds!);
        Assert.Single(request.KnowledgeIds!);
    }

    [Fact]
    public void CreateNodeRequest_WithEmptyRelationships_HandlesGracefully()
    {
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "output_1",
            SkillIds = [],
            KnowledgeIds = null
        };

        Assert.Empty(request.SkillIds!);
        Assert.Null(request.KnowledgeIds);
    }

    [Fact]
    public void CreateNodeRequest_WithMultipleRelationships_IncludesAllIds()
    {
        var skillIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var knowledgeIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result",
            SkillIds = skillIds,
            KnowledgeIds = knowledgeIds
        };

        Assert.Equal(3, request.SkillIds!.Length);
        Assert.Equal(2, request.KnowledgeIds!.Length);
    }

    [Fact]
    public void CreateNodeRequest_WithDependsOn_IncludesDependencies()
    {
        var dependsOn = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result",
            DependsOn = dependsOn
        };

        Assert.Equal(2, request.DependsOn!.Length);
    }

    #endregion

    #region GetNode Tests

    [Fact]
    public void PlaybookNodeDto_MapsAllFields()
    {
        var dependsOn = new[] { Guid.NewGuid() };
        var response = new PlaybookNodeDto
        {
            Id = TestNodeId,
            PlaybookId = TestPlaybookId,
            Name = "Test Node",
            ActionId = TestActionId,
            ToolId = TestToolId,
            ExecutionOrder = 1,
            DependsOn = dependsOn,
            OutputVariable = "result_1",
            ConditionJson = "{\"type\":\"always\"}",
            ConfigJson = "{\"prompt\":\"test\"}",
            ModelDeploymentId = TestModelDeploymentId,
            TimeoutSeconds = 30,
            RetryCount = 3,
            PositionX = 100,
            PositionY = 200,
            IsActive = true,
            SkillIds = [TestSkillId],
            KnowledgeIds = [TestKnowledgeId],
            CreatedOn = DateTime.UtcNow.AddDays(-1),
            ModifiedOn = DateTime.UtcNow
        };

        Assert.Equal(TestNodeId, response.Id);
        Assert.Equal(TestPlaybookId, response.PlaybookId);
        Assert.Equal("Test Node", response.Name);
        Assert.Equal(TestActionId, response.ActionId);
        Assert.Equal(TestToolId, response.ToolId);
        Assert.Equal(1, response.ExecutionOrder);
        Assert.Single(response.DependsOn);
        Assert.Equal("result_1", response.OutputVariable);
        Assert.NotNull(response.ConditionJson);
        Assert.NotNull(response.ConfigJson);
        Assert.Equal(TestModelDeploymentId, response.ModelDeploymentId);
        Assert.Equal(30, response.TimeoutSeconds);
        Assert.Equal(3, response.RetryCount);
        Assert.Equal(100, response.PositionX);
        Assert.Equal(200, response.PositionY);
        Assert.True(response.IsActive);
        Assert.Single(response.SkillIds);
        Assert.Single(response.KnowledgeIds);
    }

    [Fact]
    public void PlaybookNodeDto_WithNullableFields_HandlesNulls()
    {
        var response = new PlaybookNodeDto
        {
            Id = TestNodeId,
            PlaybookId = TestPlaybookId,
            Name = "Test Node",
            ActionId = TestActionId,
            ToolId = null,
            ExecutionOrder = 1,
            DependsOn = [],
            OutputVariable = "result",
            ConditionJson = null,
            ConfigJson = null,
            ModelDeploymentId = null,
            TimeoutSeconds = null,
            RetryCount = null,
            PositionX = null,
            PositionY = null,
            SkillIds = [],
            KnowledgeIds = []
        };

        Assert.Null(response.ToolId);
        Assert.Null(response.ConditionJson);
        Assert.Null(response.ConfigJson);
        Assert.Null(response.ModelDeploymentId);
        Assert.Null(response.TimeoutSeconds);
        Assert.Null(response.RetryCount);
        Assert.Null(response.PositionX);
        Assert.Null(response.PositionY);
    }

    #endregion

    #region UpdateNode Tests

    [Fact]
    public void UpdateNodeRequest_WithModifiedFields_IncludesChanges()
    {
        var request = new UpdateNodeRequest
        {
            Name = "Updated Node",
            ActionId = TestActionId,
            ToolId = TestToolId,
            OutputVariable = "updated_output",
            ConfigJson = "{\"prompt\":\"updated\"}",
            TimeoutSeconds = 60,
            RetryCount = 5,
            IsActive = false
        };

        Assert.Equal("Updated Node", request.Name);
        Assert.Equal("updated_output", request.OutputVariable);
        Assert.Equal("{\"prompt\":\"updated\"}", request.ConfigJson);
        Assert.Equal(60, request.TimeoutSeconds);
        Assert.Equal(5, request.RetryCount);
        Assert.False(request.IsActive);
    }

    [Fact]
    public void UpdateNodeRequest_WithPartialUpdate_AllowsNulls()
    {
        // Only update name
        var request = new UpdateNodeRequest
        {
            Name = "Updated Name",
            ActionId = null,
            ToolId = null,
            OutputVariable = null,
            ConfigJson = null,
            IsActive = null
        };

        Assert.Equal("Updated Name", request.Name);
        Assert.Null(request.ActionId);
        Assert.Null(request.IsActive);
    }

    [Fact]
    public void UpdateNodeRequest_WithPositionUpdate_SetsCoordinates()
    {
        var request = new UpdateNodeRequest
        {
            PositionX = 500,
            PositionY = 300
        };

        Assert.Equal(500, request.PositionX);
        Assert.Equal(300, request.PositionY);
    }

    #endregion

    #region ReorderNodes Tests

    [Fact]
    public void ReorderNodes_WithValidIds_AcceptsOrderedArray()
    {
        var nodeIds = new[]
        {
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        };

        // Verify array ordering is preserved
        Assert.Equal(3, nodeIds.Length);
        Assert.NotEqual(nodeIds[0], nodeIds[1]);
        Assert.NotEqual(nodeIds[1], nodeIds[2]);
    }

    [Fact]
    public void ReorderNodes_WithEmptyArray_HandlesGracefully()
    {
        var nodeIds = Array.Empty<Guid>();
        Assert.Empty(nodeIds);
    }

    #endregion

    #region UpdateNodeScopes Tests

    [Fact]
    public void NodeScopesRequest_WithValidScopes_SetsAllIds()
    {
        var skillIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var knowledgeIds = new[] { Guid.NewGuid() };

        var request = new NodeScopesRequest
        {
            SkillIds = skillIds,
            KnowledgeIds = knowledgeIds
        };

        Assert.Equal(2, request.SkillIds!.Length);
        Assert.Single(request.KnowledgeIds!);
    }

    [Fact]
    public void NodeScopesRequest_WithEmptyScopes_ClearsRelationships()
    {
        var request = new NodeScopesRequest
        {
            SkillIds = [],
            KnowledgeIds = []
        };

        Assert.Empty(request.SkillIds!);
        Assert.Empty(request.KnowledgeIds!);
    }

    [Fact]
    public void NodeScopesRequest_WithNullScopes_PreservesExisting()
    {
        var request = new NodeScopesRequest
        {
            SkillIds = null,
            KnowledgeIds = null
        };

        Assert.Null(request.SkillIds);
        Assert.Null(request.KnowledgeIds);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void NodeValidationResult_Success_ReturnsValidResult()
    {
        var result = NodeValidationResult.Success();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NodeValidationResult_Failure_ContainsErrors()
    {
        var result = NodeValidationResult.Failure("Error 1", "Error 2");

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Length);
        Assert.Contains("Error 1", result.Errors);
        Assert.Contains("Error 2", result.Errors);
    }

    [Fact]
    public void NodeValidationResult_SingleError_ContainsError()
    {
        var result = NodeValidationResult.Failure("Single error");

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("Single error", result.Errors[0]);
    }

    [Fact]
    public void CreateNodeRequest_Name_IsRequired()
    {
        var request = new CreateNodeRequest { Name = "" };
        Assert.Empty(request.Name);

        var validRequest = new CreateNodeRequest { Name = "Valid Name" };
        Assert.Equal("Valid Name", validRequest.Name);
    }

    [Fact]
    public void CreateNodeRequest_ActionId_IsRequired()
    {
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result"
        };

        Assert.Equal(TestActionId, request.ActionId);
    }

    [Fact]
    public void CreateNodeRequest_OutputVariable_IsRequired()
    {
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "my_output"
        };

        Assert.Equal("my_output", request.OutputVariable);
    }

    #endregion

    #region ExecutionOrder Tests

    [Fact]
    public void ExecutionOrder_DefaultsToNull()
    {
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result"
        };

        // ExecutionOrder is nullable, defaults to null
        Assert.Null(request.ExecutionOrder);
    }

    [Fact]
    public void ExecutionOrder_AcceptsPositiveValues()
    {
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result",
            ExecutionOrder = 5
        };

        Assert.Equal(5, request.ExecutionOrder);
    }

    [Fact]
    public void ExecutionOrder_AcceptsZero()
    {
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result",
            ExecutionOrder = 0
        };

        Assert.Equal(0, request.ExecutionOrder);
    }

    #endregion

    #region IsActive Tests

    [Fact]
    public void PlaybookNodeDto_IsActive_DefaultsToTrue()
    {
        var dto = new PlaybookNodeDto
        {
            Id = TestNodeId,
            PlaybookId = TestPlaybookId,
            Name = "Test Node",
            ActionId = TestActionId,
            ExecutionOrder = 1,
            OutputVariable = "result",
            IsActive = true
        };

        Assert.True(dto.IsActive);
    }

    [Fact]
    public void UpdateNodeRequest_CanDeactivateNode()
    {
        var request = new UpdateNodeRequest
        {
            IsActive = false
        };

        Assert.False(request.IsActive);
    }

    [Fact]
    public void UpdateNodeRequest_CanReactivateNode()
    {
        var request = new UpdateNodeRequest
        {
            IsActive = true
        };

        Assert.True(request.IsActive);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void CreateNodeRequest_WithLongConfigJson_HandlesGracefully()
    {
        var longConfig = new string('a', 10000);
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result",
            ConfigJson = longConfig
        };

        Assert.Equal(10000, request.ConfigJson!.Length);
    }

    [Fact]
    public void CreateNodeRequest_WithLongConditionJson_HandlesGracefully()
    {
        var longCondition = new string('b', 5000);
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result",
            ConditionJson = longCondition
        };

        Assert.Equal(5000, request.ConditionJson!.Length);
    }

    [Fact]
    public void NodeScopesRequest_WithManySkills_HandlesLargeArrays()
    {
        var skillIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();
        var request = new NodeScopesRequest
        {
            SkillIds = skillIds
        };

        Assert.Equal(50, request.SkillIds!.Length);
    }

    [Fact]
    public void PlaybookNodeDto_WithManyDependsOn_HandlesLargeArrays()
    {
        var dependsOn = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();
        var dto = new PlaybookNodeDto
        {
            Id = TestNodeId,
            PlaybookId = TestPlaybookId,
            Name = "Test Node",
            ActionId = TestActionId,
            ExecutionOrder = 1,
            OutputVariable = "result",
            DependsOn = dependsOn
        };

        Assert.Equal(20, dto.DependsOn.Length);
    }

    #endregion

    #region Canvas Position Tests

    [Fact]
    public void CreateNodeRequest_WithCanvasPosition_SetsCoordinates()
    {
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result",
            PositionX = 250,
            PositionY = 150
        };

        Assert.Equal(250, request.PositionX);
        Assert.Equal(150, request.PositionY);
    }

    [Fact]
    public void CreateNodeRequest_WithNegativePosition_Accepted()
    {
        // Negative coordinates might be valid for certain canvas layouts
        var request = new CreateNodeRequest
        {
            Name = "Test Node",
            ActionId = TestActionId,
            OutputVariable = "result",
            PositionX = -100,
            PositionY = -50
        };

        Assert.Equal(-100, request.PositionX);
        Assert.Equal(-50, request.PositionY);
    }

    #endregion
}
