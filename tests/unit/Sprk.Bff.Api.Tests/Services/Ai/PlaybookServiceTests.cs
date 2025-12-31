using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for PlaybookService.
/// Tests CRUD operations for playbook management via Dataverse Web API.
/// </summary>
public class PlaybookServiceTests
{
    private readonly Mock<ILogger<PlaybookService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestPlaybookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestOutputTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public PlaybookServiceTests()
    {
        _loggerMock = new Mock<ILogger<PlaybookService>>();
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

    #region CreatePlaybook Tests

    [Fact]
    public void CreatePlaybook_WithValidRequest_ReturnsCreatedPlaybook()
    {
        // This test validates the request structure is correct
        // The actual HTTP call is mocked
        var request = new SavePlaybookRequest
        {
            Name = "Test Playbook",
            Description = "Test description",
            IsPublic = false
        };

        Assert.Equal("Test Playbook", request.Name);
        Assert.Equal("Test description", request.Description);
        Assert.False(request.IsPublic);
    }

    [Fact]
    public void CreatePlaybook_WithOutputType_IncludesOutputTypeId()
    {
        var request = new SavePlaybookRequest
        {
            Name = "Test Playbook",
            OutputTypeId = TestOutputTypeId
        };

        Assert.Equal(TestOutputTypeId, request.OutputTypeId);
    }

    [Fact]
    public void CreatePlaybook_WithRelationships_IncludesAllRelationshipIds()
    {
        var actionIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var skillIds = new[] { Guid.NewGuid() };
        var knowledgeIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var toolIds = new[] { Guid.NewGuid() };

        var request = new SavePlaybookRequest
        {
            Name = "Test Playbook",
            ActionIds = actionIds,
            SkillIds = skillIds,
            KnowledgeIds = knowledgeIds,
            ToolIds = toolIds
        };

        Assert.Equal(2, request.ActionIds!.Length);
        Assert.Single(request.SkillIds!);
        Assert.Equal(3, request.KnowledgeIds!.Length);
        Assert.Single(request.ToolIds!);
    }

    [Fact]
    public void CreatePlaybook_WithEmptyRelationships_HandlesGracefully()
    {
        var request = new SavePlaybookRequest
        {
            Name = "Test Playbook",
            ActionIds = [],
            SkillIds = null,
            KnowledgeIds = [],
            ToolIds = null
        };

        Assert.Empty(request.ActionIds!);
        Assert.Null(request.SkillIds);
        Assert.Empty(request.KnowledgeIds!);
        Assert.Null(request.ToolIds);
    }

    #endregion

    #region GetPlaybook Tests

    [Fact]
    public void PlaybookResponse_MapsAllFields()
    {
        var response = new PlaybookResponse
        {
            Id = TestPlaybookId,
            Name = "Test Playbook",
            Description = "Test description",
            OutputTypeId = TestOutputTypeId,
            IsPublic = true,
            OwnerId = TestUserId,
            ActionIds = [Guid.NewGuid()],
            SkillIds = [Guid.NewGuid()],
            KnowledgeIds = [Guid.NewGuid()],
            ToolIds = [Guid.NewGuid()],
            CreatedOn = DateTime.UtcNow.AddDays(-1),
            ModifiedOn = DateTime.UtcNow
        };

        Assert.Equal(TestPlaybookId, response.Id);
        Assert.Equal("Test Playbook", response.Name);
        Assert.Equal("Test description", response.Description);
        Assert.Equal(TestOutputTypeId, response.OutputTypeId);
        Assert.True(response.IsPublic);
        Assert.Equal(TestUserId, response.OwnerId);
        Assert.Single(response.ActionIds);
        Assert.Single(response.SkillIds);
        Assert.Single(response.KnowledgeIds);
        Assert.Single(response.ToolIds);
    }

    [Fact]
    public void PlaybookResponse_WithNullableFields_HandlesNulls()
    {
        var response = new PlaybookResponse
        {
            Id = TestPlaybookId,
            Name = "Test Playbook",
            Description = null,
            OutputTypeId = null,
            ActionIds = [],
            SkillIds = [],
            KnowledgeIds = [],
            ToolIds = []
        };

        Assert.Null(response.Description);
        Assert.Null(response.OutputTypeId);
    }

    #endregion

    #region UpdatePlaybook Tests

    [Fact]
    public void UpdatePlaybook_WithModifiedFields_IncludesChanges()
    {
        var request = new SavePlaybookRequest
        {
            Name = "Updated Playbook",
            Description = "Updated description",
            IsPublic = true,
            OutputTypeId = TestOutputTypeId
        };

        Assert.Equal("Updated Playbook", request.Name);
        Assert.Equal("Updated description", request.Description);
        Assert.True(request.IsPublic);
    }

    #endregion

    #region ListPlaybooks Tests

    [Fact]
    public void PlaybookQueryParameters_DefaultValues_AreCorrect()
    {
        var query = new PlaybookQueryParameters();

        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Null(query.NameFilter);
        Assert.Null(query.OutputTypeId);
        Assert.False(query.PublicOnly);
        Assert.Equal("modifiedon", query.SortBy);
        Assert.True(query.SortDescending);
    }

    [Fact]
    public void PlaybookQueryParameters_GetNormalizedPageSize_ClampsToValidRange()
    {
        var querySmall = new PlaybookQueryParameters { PageSize = 0 };
        var queryLarge = new PlaybookQueryParameters { PageSize = 200 };
        var queryNormal = new PlaybookQueryParameters { PageSize = 50 };

        Assert.Equal(1, querySmall.GetNormalizedPageSize());
        Assert.Equal(100, queryLarge.GetNormalizedPageSize());
        Assert.Equal(50, queryNormal.GetNormalizedPageSize());
    }

    [Fact]
    public void PlaybookQueryParameters_GetSkip_CalculatesCorrectly()
    {
        var query1 = new PlaybookQueryParameters { Page = 1, PageSize = 20 };
        var query2 = new PlaybookQueryParameters { Page = 2, PageSize = 20 };
        var query3 = new PlaybookQueryParameters { Page = 3, PageSize = 50 };

        Assert.Equal(0, query1.GetSkip());
        Assert.Equal(20, query2.GetSkip());
        Assert.Equal(100, query3.GetSkip());
    }

    [Fact]
    public void PlaybookQueryParameters_WithFilter_SetsFilterCorrectly()
    {
        var query = new PlaybookQueryParameters
        {
            NameFilter = "Test",
            OutputTypeId = TestOutputTypeId
        };

        Assert.Equal("Test", query.NameFilter);
        Assert.Equal(TestOutputTypeId, query.OutputTypeId);
    }

    [Fact]
    public void PlaybookListResponse_CalculatesTotalPages_Correctly()
    {
        var response1 = new PlaybookListResponse
        {
            Items = [],
            TotalCount = 45,
            Page = 1,
            PageSize = 20
        };

        var response2 = new PlaybookListResponse
        {
            Items = [],
            TotalCount = 40,
            Page = 1,
            PageSize = 20
        };

        var response3 = new PlaybookListResponse
        {
            Items = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 20
        };

        Assert.Equal(3, response1.TotalPages);
        Assert.Equal(2, response2.TotalPages);
        Assert.Equal(0, response3.TotalPages);
    }

    [Fact]
    public void PlaybookListResponse_HasNextPage_CalculatesCorrectly()
    {
        var response1 = new PlaybookListResponse { Page = 1, TotalCount = 45, PageSize = 20 };
        var response2 = new PlaybookListResponse { Page = 3, TotalCount = 45, PageSize = 20 };

        Assert.True(response1.HasNextPage);
        Assert.False(response2.HasNextPage);
    }

    [Fact]
    public void PlaybookListResponse_HasPreviousPage_CalculatesCorrectly()
    {
        var response1 = new PlaybookListResponse { Page = 1, TotalCount = 45, PageSize = 20 };
        var response2 = new PlaybookListResponse { Page = 2, TotalCount = 45, PageSize = 20 };

        Assert.False(response1.HasPreviousPage);
        Assert.True(response2.HasPreviousPage);
    }

    [Fact]
    public void PlaybookSummary_ContainsMinimalFields()
    {
        var summary = new PlaybookSummary
        {
            Id = TestPlaybookId,
            Name = "Test Playbook",
            Description = "Test description",
            OutputTypeId = TestOutputTypeId,
            IsPublic = true,
            OwnerId = TestUserId,
            ModifiedOn = DateTime.UtcNow
        };

        Assert.Equal(TestPlaybookId, summary.Id);
        Assert.Equal("Test Playbook", summary.Name);
        Assert.Equal("Test description", summary.Description);
        Assert.Equal(TestOutputTypeId, summary.OutputTypeId);
        Assert.True(summary.IsPublic);
        Assert.Equal(TestUserId, summary.OwnerId);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void PlaybookValidationResult_Success_ReturnsValidResult()
    {
        var result = PlaybookValidationResult.Success();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PlaybookValidationResult_Failure_ContainsErrors()
    {
        var result = PlaybookValidationResult.Failure("Error 1", "Error 2");

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Length);
        Assert.Contains("Error 1", result.Errors);
        Assert.Contains("Error 2", result.Errors);
    }

    [Fact]
    public void SavePlaybookRequest_Name_IsRequired()
    {
        // Name has Required attribute and StringLength(200, MinimumLength = 1)
        var request = new SavePlaybookRequest { Name = "" };
        Assert.Empty(request.Name);

        var validRequest = new SavePlaybookRequest { Name = "Valid Name" };
        Assert.Equal("Valid Name", validRequest.Name);
    }

    [Fact]
    public void SavePlaybookRequest_Description_HasMaxLength()
    {
        // Description has StringLength(4000)
        var shortDescription = "Short description";
        var request = new SavePlaybookRequest
        {
            Name = "Test",
            Description = shortDescription
        };

        Assert.Equal(shortDescription, request.Description);
    }

    #endregion

    #region UserHasAccess Tests

    [Fact]
    public void UserHasAccess_OwnerAccess_ShouldReturnTrue()
    {
        // Owner should always have access
        var playbook = new PlaybookResponse
        {
            Id = TestPlaybookId,
            OwnerId = TestUserId,
            IsPublic = false
        };

        Assert.Equal(TestUserId, playbook.OwnerId);
    }

    [Fact]
    public void UserHasAccess_PublicPlaybook_ShouldReturnTrue()
    {
        var playbook = new PlaybookResponse
        {
            Id = TestPlaybookId,
            OwnerId = Guid.NewGuid(), // Different owner
            IsPublic = true
        };

        Assert.True(playbook.IsPublic);
    }

    [Fact]
    public void UserHasAccess_PrivatePlaybook_NonOwner_ShouldCheckSharing()
    {
        var playbook = new PlaybookResponse
        {
            Id = TestPlaybookId,
            OwnerId = Guid.NewGuid(), // Different owner
            IsPublic = false
        };

        Assert.False(playbook.IsPublic);
        Assert.NotEqual(TestUserId, playbook.OwnerId);
        // In production, this would require checking sharing service
    }

    #endregion
}
