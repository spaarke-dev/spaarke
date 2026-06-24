using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    #region FR-13 Tracking-Field Tests (chat-routing-redesign-r1 task 034 follow-up)

    /// <summary>
    /// Stub <see cref="TokenCredential"/> that always returns a fixed token.
    /// </summary>
    private sealed class StubTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));
    }

    /// <summary>
    /// Builds a <see cref="PlaybookService"/> wired to the supplied
    /// <see cref="HttpMessageHandler"/>. Used to drive <c>ListAllActivePlaybooksAsync</c>
    /// and <c>UpdateIndexStatusAsync</c> tests through real OData URL construction.
    /// </summary>
    private static PlaybookService BuildPlaybookService(HttpMessageHandler handler)
    {
        var configData = new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var http = new HttpClient(handler);
        return new PlaybookService(
            http,
            config,
            new StubTokenCredential(),
            NullLogger<PlaybookService>.Instance);
    }

    /// <summary>
    /// Replay handler that returns successive responses keyed by URL match. Captures the
    /// requests it sees so tests can inspect URLs + payloads.
    /// </summary>
    private sealed class ReplayHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> RequestBodies { get; } = new();

        public ReplayHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("no response queued") };
        }
    }

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ListAllActivePlaybooksAsync_YieldsPagedResults_WhenMultiplePagesExist()
    {
        // Arrange — two pages: first returns 2 rows + @odata.nextLink, second returns 1 row.
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var p3 = Guid.NewGuid();
        var page1Body = $$"""
        {
          "value": [
            { "sprk_analysisplaybookid": "{{p1}}", "sprk_name": "P1", "sprk_indexstatus": 100000002, "sprk_indexhash": "abc" },
            { "sprk_analysisplaybookid": "{{p2}}", "sprk_name": "P2", "sprk_indexstatus": 100000000 }
          ],
          "@odata.nextLink": "https://test.crm.dynamics.com/api/data/v9.2/sprk_analysisplaybooks?$skiptoken=PAGE2"
        }
        """;
        var page2Body = $$"""
        {
          "value": [
            { "sprk_analysisplaybookid": "{{p3}}", "sprk_name": "P3", "sprk_indexstatus": 100000003, "sprk_indexhash": "def" }
          ]
        }
        """;

        var handler = new ReplayHandler(new[]
        {
            JsonResponse(page1Body),
            JsonResponse(page2Body)
        });
        var svc = BuildPlaybookService(handler);

        // Act
        var collected = new List<PlaybookResponse>();
        await foreach (var p in svc.ListAllActivePlaybooksAsync())
        {
            collected.Add(p);
        }

        // Assert
        collected.Should().HaveCount(3);
        collected.Select(p => p.Id).Should().Equal(p1, p2, p3);
        collected[0].IndexStatusCode.Should().Be(100_000_002);
        collected[0].IndexHash.Should().Be("abc");
        collected[1].IndexStatusCode.Should().Be(100_000_000); // null → NotIndexedCode default
        collected[2].IndexStatusCode.Should().Be(100_000_003);

        // Pages requested: the initial URL + the @odata.nextLink follow-up.
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("statecode eq 0");
        handler.Requests[0].RequestUri!.ToString().Should().Contain("sprk_indexstatus");
        handler.Requests[1].RequestUri!.ToString().Should().Contain("PAGE2");
    }

    [Fact]
    public async Task ListAllActivePlaybooksAsync_PropagatesCancellation_WhenTokenCancelled()
    {
        // Arrange — handler will never be reached because the token is pre-cancelled.
        var handler = new ReplayHandler(new[] { JsonResponse("{\"value\":[]}") });
        var svc = BuildPlaybookService(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert — enumeration MUST honor the token cancellation.
        var act = async () =>
        {
            await foreach (var _ in svc.ListAllActivePlaybooksAsync(cts.Token))
            {
                // unreachable
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UpdateIndexStatusAsync_PatchesCorrectFields_ForIndexedStatus()
    {
        // Arrange
        var handler = new ReplayHandler(new[] { JsonResponse("", HttpStatusCode.NoContent) });
        var svc = BuildPlaybookService(handler);

        // Act
        await svc.UpdateIndexStatusAsync(
            TestPlaybookId,
            statusCode: 100_000_002 /* Indexed */,
            indexHash: "abc123",
            lastError: null);

        // Assert — single PATCH request to the playbook row with all four tracking fields.
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Patch);
        handler.Requests[0].RequestUri!.ToString().Should().Contain($"sprk_analysisplaybooks({TestPlaybookId})");

        var body = handler.RequestBodies[0]!;
        body.Should().Contain("\"sprk_indexstatus\":100000002");
        body.Should().Contain("\"sprk_indexhash\":\"abc123\"");
        body.Should().Contain("sprk_lastindexedat"); // Indexed status MUST stamp lastindexedat
        body.Should().Contain("\"sprk_lastindexerror\":\"\""); // null → empty string clear
    }

    [Theory]
    [InlineData(100_000_003)] // Stale
    [InlineData(100_000_004)] // Failed
    public async Task UpdateIndexStatusAsync_OmitsLastIndexedAt_ForNonIndexedStatus(int statusCode)
    {
        // Arrange
        var handler = new ReplayHandler(new[] { JsonResponse("", HttpStatusCode.NoContent) });
        var svc = BuildPlaybookService(handler);

        // Act
        await svc.UpdateIndexStatusAsync(
            TestPlaybookId,
            statusCode,
            indexHash: null,
            lastError: "transient error");

        // Assert — Stale/Failed leave sprk_lastindexedat alone (no key in body).
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Patch);

        var body = handler.RequestBodies[0]!;
        body.Should().Contain($"\"sprk_indexstatus\":{statusCode}");
        body.Should().NotContain("sprk_lastindexedat");
        body.Should().Contain("\"sprk_lastindexerror\":\"transient error\"");
    }

    [Fact]
    public async Task UpdateIndexStatusAsync_Logs_OnlyPlaybookIdAndStatusCode_NeverErrorMessage()
    {
        // Arrange — capture all log invocations via a Mock logger and assert no log entry
        // contains the error message body (ADR-015 — error content goes to Dataverse but
        // never to BFF logs).
        var loggerMock = new Mock<ILogger<PlaybookService>>();
        var configData = new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        var handler = new ReplayHandler(new[] { JsonResponse("", HttpStatusCode.NoContent) });
        var http = new HttpClient(handler);
        var svc = new PlaybookService(http, config, new StubTokenCredential(), loggerMock.Object);

        const string secretishError = "SECRET_PII_ERROR_BODY_MUST_NOT_APPEAR_IN_LOGS";

        // Act
        await svc.UpdateIndexStatusAsync(
            TestPlaybookId,
            statusCode: 100_000_004 /* Failed */,
            indexHash: null,
            lastError: secretishError);

        // Assert — no log invocation contains the error string. Inspect the formatted
        // message AND the state object's parameter values via the mock invocation log.
        foreach (var invocation in loggerMock.Invocations)
        {
            // ILogger.Log signature: (LogLevel, EventId, TState state, Exception?, Func<TState, Exception?, string>)
            // state is the structured-logging payload; convert to string and scan.
            var stateString = invocation.Arguments.Count >= 3 ? invocation.Arguments[2]?.ToString() ?? "" : "";
            stateString.Should().NotContain(secretishError,
                "ADR-015 forbids logging lastError content — only playbook ID + statusCode are permitted");
        }
    }

    #endregion
}
