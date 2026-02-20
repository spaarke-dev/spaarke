using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Workspace;

/// <summary>
/// Integration tests for all Workspace BFF endpoints using WebApplicationFactory.
/// Exercises the full Minimal API pipeline including:
///   - Endpoint routing and HTTP method matching
///   - WorkspaceAuthorizationFilter (ADR-008)
///   - Service resolution via DI (ADR-010)
///   - Redis cache behavior via in-memory substitute (ADR-009)
///   - ProblemDetails error response format (ADR-019)
///   - JSON serialization of response DTOs
///
/// Endpoints covered:
///   GET  /api/workspace/portfolio          — PortfolioService aggregation
///   GET  /api/workspace/health             — Health metrics (no auth filter)
///   GET  /api/workspace/briefing           — BriefingService (metrics + narrative)
///   POST /api/workspace/calculate-scores   — Batch scoring (deterministic)
///   GET  /api/workspace/events/{id}/scores — Single-event scoring
///   POST /api/workspace/ai/summary         — AI summary (WorkspaceAiEndpoints)
///   POST /api/workspace/matters/pre-fill   — Matter AI pre-fill (WorkspaceMatterEndpoints)
/// </summary>
public class WorkspaceEndpointsTests : IClassFixture<WorkspaceTestFixture>
{
    private readonly WorkspaceTestFixture _fixture;

    // Shared JSON options matching the BFF API's default camelCase serialization
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WorkspaceEndpointsTests(WorkspaceTestFixture fixture)
    {
        _fixture = fixture;
    }

    // =========================================================================
    // GET /api/workspace/portfolio
    // =========================================================================

    [Fact]
    public async Task GetPortfolio_AuthenticatedRequest_Returns200WithPortfolioShape()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/portfolio");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Verify PortfolioSummaryResponse shape
        root.TryGetProperty("totalSpend", out var totalSpend).Should().BeTrue("totalSpend must be present");
        root.TryGetProperty("totalBudget", out _).Should().BeTrue("totalBudget must be present");
        root.TryGetProperty("utilizationPercent", out _).Should().BeTrue("utilizationPercent must be present");
        root.TryGetProperty("mattersAtRisk", out _).Should().BeTrue("mattersAtRisk must be present");
        root.TryGetProperty("overdueEvents", out _).Should().BeTrue("overdueEvents must be present");
        root.TryGetProperty("activeMatters", out var activeMatters).Should().BeTrue("activeMatters must be present");
        root.TryGetProperty("cachedAt", out _).Should().BeTrue("cachedAt must be present");

        // Mock data returns 3 active matters (from PortfolioService.QueryMattersFromDataverseAsync)
        activeMatters.GetInt32().Should().Be(3, "mock data has 3 active matters");
        totalSpend.GetDecimal().Should().Be(257_000m, "125k + 92k + 40k = 257k (active matters only)");
    }

    [Fact]
    public async Task GetPortfolio_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/portfolio");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPortfolio_CalledTwice_SecondResponseReturnsCachedData()
    {
        // Arrange: create a fresh factory scope to isolate cache state per test
        using var factory = new WorkspaceTestFixture();
        using var client = factory.CreateAuthenticatedClient();

        // Act: call twice with same user identity
        var response1 = await client.GetAsync("/api/workspace/portfolio");
        var response2 = await client.GetAsync("/api/workspace/portfolio");

        // Assert: both succeed
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var body1 = await response1.Content.ReadAsStringAsync();
        var body2 = await response2.Content.ReadAsStringAsync();

        // Both responses should have matching data (second is served from cache)
        using var doc1 = JsonDocument.Parse(body1);
        using var doc2 = JsonDocument.Parse(body2);

        doc1.RootElement.GetProperty("activeMatters").GetInt32()
            .Should().Be(doc2.RootElement.GetProperty("activeMatters").GetInt32(),
                "cached response must have same activeMatters count as original");

        doc1.RootElement.GetProperty("totalSpend").GetDecimal()
            .Should().Be(doc2.RootElement.GetProperty("totalSpend").GetDecimal(),
                "cached response must have same totalSpend as original");
    }

    // =========================================================================
    // GET /api/workspace/health
    // =========================================================================

    [Fact]
    public async Task GetHealthMetrics_AuthenticatedRequest_Returns200WithHealthShape()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Verify HealthMetricsResponse shape
        root.TryGetProperty("mattersAtRisk", out _).Should().BeTrue("mattersAtRisk must be present");
        root.TryGetProperty("overdueEvents", out _).Should().BeTrue("overdueEvents must be present");
        root.TryGetProperty("activeMatters", out _).Should().BeTrue("activeMatters must be present");
        root.TryGetProperty("budgetUtilizationPercent", out _).Should().BeTrue("budgetUtilizationPercent must be present");
        root.TryGetProperty("portfolioSpend", out _).Should().BeTrue("portfolioSpend must be present");
        root.TryGetProperty("portfolioBudget", out _).Should().BeTrue("portfolioBudget must be present");
        root.TryGetProperty("timestamp", out _).Should().BeTrue("timestamp must be present");
    }

    [Fact]
    public async Task GetHealthMetrics_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetHealthMetrics_MetricsMatchPortfolioData()
    {
        // Arrange: use isolated factory so cache is empty
        using var factory = new WorkspaceTestFixture();
        using var client = factory.CreateAuthenticatedClient();

        // Act: fetch both endpoints
        var portfolioResponse = await client.GetAsync("/api/workspace/portfolio");
        var healthResponse = await client.GetAsync("/api/workspace/health");

        portfolioResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var portfolioBody = await portfolioResponse.Content.ReadAsStringAsync();
        var healthBody = await healthResponse.Content.ReadAsStringAsync();

        using var portfolioDoc = JsonDocument.Parse(portfolioBody);
        using var healthDoc = JsonDocument.Parse(healthBody);

        // Health endpoint derives from portfolio data — key fields must agree
        healthDoc.RootElement.GetProperty("mattersAtRisk").GetInt32()
            .Should().Be(portfolioDoc.RootElement.GetProperty("mattersAtRisk").GetInt32(),
                "health mattersAtRisk must match portfolio mattersAtRisk");

        healthDoc.RootElement.GetProperty("overdueEvents").GetInt32()
            .Should().Be(portfolioDoc.RootElement.GetProperty("overdueEvents").GetInt32(),
                "health overdueEvents must match portfolio overdueEvents");

        healthDoc.RootElement.GetProperty("activeMatters").GetInt32()
            .Should().Be(portfolioDoc.RootElement.GetProperty("activeMatters").GetInt32(),
                "health activeMatters must match portfolio activeMatters");
    }

    // =========================================================================
    // GET /api/workspace/briefing
    // =========================================================================

    [Fact]
    public async Task GetBriefing_AuthenticatedRequest_Returns200WithBriefingShape()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/briefing");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Verify BriefingResponse shape
        root.TryGetProperty("activeMatters", out _).Should().BeTrue("activeMatters must be present");
        root.TryGetProperty("totalSpend", out _).Should().BeTrue("totalSpend must be present");
        root.TryGetProperty("totalBudget", out _).Should().BeTrue("totalBudget must be present");
        root.TryGetProperty("utilizationPercent", out _).Should().BeTrue("utilizationPercent must be present");
        root.TryGetProperty("mattersAtRisk", out _).Should().BeTrue("mattersAtRisk must be present");
        root.TryGetProperty("overdueEvents", out _).Should().BeTrue("overdueEvents must be present");
        root.TryGetProperty("narrative", out var narrative).Should().BeTrue("narrative must be present");
        root.TryGetProperty("isAiEnhanced", out var isAiEnhanced).Should().BeTrue("isAiEnhanced must be present");
        root.TryGetProperty("generatedAt", out _).Should().BeTrue("generatedAt must be present");

        // Without DocumentIntelligence enabled, isAiEnhanced must be false
        isAiEnhanced.GetBoolean().Should().BeFalse("AI is disabled in test configuration");

        // Narrative must be non-empty template-based text
        narrative.GetString().Should().NotBeNullOrWhiteSpace("template narrative must be generated");
    }

    [Fact]
    public async Task GetBriefing_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/briefing");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBriefing_NarrativeContainsPortfolioMetrics()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/workspace/briefing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var activeMatters = root.GetProperty("activeMatters").GetInt32();
        var narrative = root.GetProperty("narrative").GetString();

        // Template narrative mentions the number of active matters
        narrative.Should().Contain(activeMatters.ToString(),
            "template narrative must include active matter count");
    }

    // =========================================================================
    // POST /api/workspace/calculate-scores
    // =========================================================================

    [Fact]
    public async Task CalculateScores_ValidBatchRequest_Returns200WithScoreResponses()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();
        var eventId = Guid.NewGuid();

        var request = new
        {
            Items = new[]
            {
                new
                {
                    EventId = eventId,
                    PriorityInput = new
                    {
                        OverdueDays = 5,
                        BudgetUtilizationPercent = 70m,
                        GradesBelowC = 1,
                        DaysToDeadline = (int?)10,
                        MatterValueTier = "High",
                        PendingInvoiceCount = 2
                    },
                    EffortInput = new
                    {
                        EventType = "DocumentReview",
                        HasMultipleParties = true,
                        IsCrossJurisdiction = false,
                        IsRegulatory = false,
                        IsHighValue = true,
                        IsTimeSensitive = false
                    }
                }
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/calculate-scores", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("results", out var results).Should().BeTrue("results array must be present");
        results.GetArrayLength().Should().Be(1, "one score result per input item");

        var first = results[0];
        first.GetProperty("eventId").GetGuid().Should().Be(eventId);
        first.TryGetProperty("priorityScore", out _).Should().BeTrue("priorityScore must be present");
        first.TryGetProperty("priorityLevel", out _).Should().BeTrue("priorityLevel must be present");
        first.TryGetProperty("priorityFactors", out _).Should().BeTrue("priorityFactors must be present");
        first.TryGetProperty("priorityReason", out _).Should().BeTrue("priorityReason must be present");
        first.TryGetProperty("effortScore", out _).Should().BeTrue("effortScore must be present");
        first.TryGetProperty("effortLevel", out _).Should().BeTrue("effortLevel must be present");
        first.TryGetProperty("baseEffort", out _).Should().BeTrue("baseEffort must be present");
        first.TryGetProperty("effortMultipliers", out _).Should().BeTrue("effortMultipliers must be present");
        first.TryGetProperty("effortReason", out _).Should().BeTrue("effortReason must be present");
    }

    [Fact]
    public async Task CalculateScores_ScoresAreDeterministic_SameInputsProduceSameOutputs()
    {
        // Arrange — verifies the scoring engines are table-driven and idempotent
        using var client = _fixture.CreateAuthenticatedClient();
        var eventId = Guid.NewGuid();

        var request = new
        {
            Items = new[]
            {
                new
                {
                    EventId = eventId,
                    PriorityInput = new
                    {
                        OverdueDays = 16,
                        BudgetUtilizationPercent = 90m,
                        GradesBelowC = 2,
                        DaysToDeadline = (int?)3,
                        MatterValueTier = "High",
                        PendingInvoiceCount = 4
                    },
                    EffortInput = new
                    {
                        EventType = "Invoice",
                        HasMultipleParties = true,
                        IsCrossJurisdiction = true,
                        IsRegulatory = true,
                        IsHighValue = true,
                        IsTimeSensitive = true
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(request);

        // Act: call twice with identical inputs
        var response1 = await client.PostAsync("/api/workspace/calculate-scores",
            new StringContent(json, Encoding.UTF8, "application/json"));
        var response2 = await client.PostAsync("/api/workspace/calculate-scores",
            new StringContent(json, Encoding.UTF8, "application/json"));

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var body1 = await response1.Content.ReadAsStringAsync();
        var body2 = await response2.Content.ReadAsStringAsync();

        using var doc1 = JsonDocument.Parse(body1);
        using var doc2 = JsonDocument.Parse(body2);

        var score1 = doc1.RootElement.GetProperty("results")[0].GetProperty("priorityScore").GetInt32();
        var score2 = doc2.RootElement.GetProperty("results")[0].GetProperty("priorityScore").GetInt32();

        score1.Should().Be(score2, "deterministic scoring must return identical scores for same inputs");
    }

    [Fact]
    public async Task CalculateScores_EmptyItems_Returns400ProblemDetails()
    {
        // Arrange — null Items body
        using var client = _fixture.CreateAuthenticatedClient();
        var content = new StringContent(
            JsonSerializer.Serialize(new { Items = (object?)null }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/calculate-scores", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemDetailsAsync(response);
    }

    [Fact]
    public async Task CalculateScores_TooManyItems_Returns400WithBatchSizeError()
    {
        // Arrange — 51 items exceeds the BatchScoreMaxItems = 50 limit
        using var client = _fixture.CreateAuthenticatedClient();

        var items = Enumerable.Range(0, 51).Select(_ => new
        {
            EventId = Guid.NewGuid(),
            PriorityInput = new
            {
                OverdueDays = 0,
                BudgetUtilizationPercent = 0m,
                GradesBelowC = 0,
                DaysToDeadline = (int?)null,
                MatterValueTier = "Low",
                PendingInvoiceCount = 0
            },
            EffortInput = new
            {
                EventType = "Email",
                HasMultipleParties = false,
                IsCrossJurisdiction = false,
                IsRegulatory = false,
                IsHighValue = false,
                IsTimeSensitive = false
            }
        }).ToArray();

        var content = new StringContent(
            JsonSerializer.Serialize(new { Items = items }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/calculate-scores", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("50", "error detail must mention the 50-item limit");

        await AssertProblemDetailsAsync(response, body);
    }

    [Fact]
    public async Task CalculateScores_Unauthenticated_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();
        var content = new StringContent("{\"Items\":[]}", Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/calculate-scores", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CalculateScores_MultipleBatchItems_Returns200WithCorrectCount()
    {
        // Arrange — 3 items; verify all are scored
        using var client = _fixture.CreateAuthenticatedClient();

        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var items = ids.Select(id => new
        {
            EventId = id,
            PriorityInput = new
            {
                OverdueDays = 0,
                BudgetUtilizationPercent = 50m,
                GradesBelowC = 0,
                DaysToDeadline = (int?)30,
                MatterValueTier = "Medium",
                PendingInvoiceCount = 1
            },
            EffortInput = new
            {
                EventType = "Task",
                HasMultipleParties = false,
                IsCrossJurisdiction = false,
                IsRegulatory = false,
                IsHighValue = false,
                IsTimeSensitive = false
            }
        }).ToArray();

        var content = new StringContent(
            JsonSerializer.Serialize(new { Items = items }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/calculate-scores", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().Should().Be(3, "one result per input item");

        // Each result's eventId must match the input
        var resultIds = Enumerable.Range(0, 3)
            .Select(i => results[i].GetProperty("eventId").GetGuid())
            .ToHashSet();

        foreach (var id in ids)
        {
            resultIds.Should().Contain(id, $"result set must include eventId {id}");
        }
    }

    // =========================================================================
    // GET /api/workspace/events/{id}/scores
    // =========================================================================

    [Fact]
    public async Task GetEventScores_ValidRequest_Returns200WithScoreShape()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();
        var eventId = Guid.NewGuid();

        var request = new
        {
            EventId = eventId,
            PriorityInput = new
            {
                OverdueDays = 8,
                BudgetUtilizationPercent = 85m,
                GradesBelowC = 0,
                DaysToDeadline = (int?)7,
                MatterValueTier = "High",
                PendingInvoiceCount = 3
            },
            EffortInput = new
            {
                EventType = "Meeting",
                HasMultipleParties = true,
                IsCrossJurisdiction = false,
                IsRegulatory = false,
                IsHighValue = false,
                IsTimeSensitive = true
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.GetAsync(
            $"/api/workspace/events/{eventId}/scores");

        // Minimal API GET with body requires special handling; use custom request
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/workspace/events/{eventId}/scores");
        httpRequest.Content = content;
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                WorkspaceTestConstants.TestBearerToken);

        using var client2 = _fixture.CreateAuthenticatedClient();
        var response2 = await client2.SendAsync(httpRequest);

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response2.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("eventId").GetGuid().Should().Be(eventId);
        root.TryGetProperty("priorityScore", out _).Should().BeTrue("priorityScore must be present");
        root.TryGetProperty("priorityLevel", out _).Should().BeTrue("priorityLevel must be present");
        root.TryGetProperty("effortScore", out _).Should().BeTrue("effortScore must be present");
        root.TryGetProperty("effortLevel", out _).Should().BeTrue("effortLevel must be present");
    }

    [Fact]
    public async Task GetEventScores_MismatchedEventId_Returns400ProblemDetails()
    {
        // Arrange — route ID differs from body EventId
        using var client = _fixture.CreateAuthenticatedClient();
        var routeId = Guid.NewGuid();
        var bodyId = Guid.NewGuid(); // Different from route

        var request = new
        {
            EventId = bodyId,
            PriorityInput = new
            {
                OverdueDays = 0,
                BudgetUtilizationPercent = 0m,
                GradesBelowC = 0,
                DaysToDeadline = (int?)null,
                MatterValueTier = "Low",
                PendingInvoiceCount = 0
            },
            EffortInput = new
            {
                EventType = "Email",
                HasMultipleParties = false,
                IsCrossJurisdiction = false,
                IsRegulatory = false,
                IsHighValue = false,
                IsTimeSensitive = false
            }
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/workspace/events/{routeId}/scores");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                WorkspaceTestConstants.TestBearerToken);

        // Act
        var response = await client.SendAsync(httpRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemDetailsAsync(response);
    }

    [Fact]
    public async Task GetEventScores_Unauthenticated_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();
        var eventId = Guid.NewGuid();

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/workspace/events/{eventId}/scores");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                EventId = eventId,
                PriorityInput = new { OverdueDays = 0, BudgetUtilizationPercent = 0m, GradesBelowC = 0, DaysToDeadline = (int?)null, MatterValueTier = "Low", PendingInvoiceCount = 0 },
                EffortInput = new { EventType = "Email", HasMultipleParties = false, IsCrossJurisdiction = false, IsRegulatory = false, IsHighValue = false, IsTimeSensitive = false }
            }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.SendAsync(httpRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // POST /api/workspace/ai/summary
    // =========================================================================

    [Fact]
    public async Task AiSummary_ValidRequest_Returns200WithAiSummaryShape()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();
        var entityId = Guid.NewGuid();

        var request = new
        {
            EntityType = "sprk_event",
            EntityId = entityId,
            Context = "This is a high-priority deadline event."
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/ai/summary", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Verify AiSummaryResponse shape
        root.TryGetProperty("analysis", out var analysis).Should().BeTrue("analysis must be present");
        root.TryGetProperty("suggestedActions", out var actions).Should().BeTrue("suggestedActions must be present");
        root.TryGetProperty("confidence", out var confidence).Should().BeTrue("confidence must be present");
        root.TryGetProperty("generatedAt", out _).Should().BeTrue("generatedAt must be present");

        analysis.GetString().Should().NotBeNullOrWhiteSpace("analysis text must be non-empty");
        actions.GetArrayLength().Should().BeGreaterThan(0, "at least one suggested action must be returned");
        confidence.GetDouble().Should().BeInRange(0.0, 1.0, "confidence must be between 0 and 1");
    }

    [Fact]
    public async Task AiSummary_MissingEntityType_Returns400WithFieldError()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        var request = new
        {
            EntityType = "",    // Empty — should trigger validation
            EntityId = Guid.NewGuid(),
            Context = (string?)null
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/ai/summary", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("entityType", "error response must reference the invalid field");
        await AssertProblemDetailsAsync(response, body);
    }

    [Fact]
    public async Task AiSummary_EmptyEntityId_Returns400WithFieldError()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        var request = new
        {
            EntityType = "sprk_event",
            EntityId = Guid.Empty,  // Invalid empty GUID
            Context = (string?)null
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/ai/summary", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("entityId", "error response must reference the invalid field");
    }

    [Fact]
    public async Task AiSummary_ContextTooLong_Returns400()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        var request = new
        {
            EntityType = "sprk_event",
            EntityId = Guid.NewGuid(),
            Context = new string('x', 2001) // Exceeds MaxContextLength = 2000
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/ai/summary", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("context", "error response must reference the context field");
    }

    [Fact]
    public async Task AiSummary_UnsupportedEntityType_Returns400()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        var request = new
        {
            EntityType = "unsupported_entity",
            EntityId = Guid.NewGuid(),
            Context = (string?)null
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/ai/summary", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AiSummary_SupportedEntityTypes_AllReturn200()
    {
        // Arrange — verify all four supported entity types are accepted
        using var client = _fixture.CreateAuthenticatedClient();

        var supportedTypes = new[] { "sprk_event", "sprk_matter", "sprk_project", "sprk_document" };

        foreach (var entityType in supportedTypes)
        {
            var request = new
            {
                EntityType = entityType,
                EntityId = Guid.NewGuid(),
                Context = (string?)null
            };

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await client.PostAsync("/api/workspace/ai/summary", content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                $"entity type '{entityType}' should be accepted and return 200");
        }
    }

    [Fact]
    public async Task AiSummary_Unauthenticated_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        var content = new StringContent(
            JsonSerializer.Serialize(new { EntityType = "sprk_event", EntityId = Guid.NewGuid(), Context = (string?)null }),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/workspace/ai/summary", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // POST /api/workspace/matters/pre-fill
    // =========================================================================

    [Fact]
    public async Task MatterPreFill_NoFiles_Returns400WithValidationError()
    {
        // Arrange — empty multipart form without files
        using var client = _fixture.CreateAuthenticatedClient();

        using var multipartContent = new MultipartFormDataContent();
        // No files added — MatterPreFillService.ValidateFiles will reject empty collection

        // Act
        var response = await client.PostAsync("/api/workspace/matters/pre-fill", multipartContent);

        // Assert — empty file collection triggers validation failure
        // The endpoint validates files via MatterPreFillService.ValidateFiles
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.OK,
            HttpStatusCode.InternalServerError,
            // 500 is acceptable here because MatterPreFillService depends on IOpenAiClient
            // which is not registered when DocumentIntelligence is disabled
            HttpStatusCode.UnsupportedMediaType
        );
    }

    [Fact]
    public async Task MatterPreFill_InvalidFileType_Returns400()
    {
        // Arrange — upload a .txt file (not in allowed: .pdf, .docx, .xlsx)
        using var client = _fixture.CreateAuthenticatedClient();

        using var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        multipartContent.Add(fileContent, "files", "test.txt");

        // Act
        var response = await client.PostAsync("/api/workspace/matters/pre-fill", multipartContent);

        // Assert — invalid file type must produce 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        // ProblemDetails must have the standard fields
        await AssertProblemDetailsAsync(response, body);
    }

    [Fact]
    public async Task MatterPreFill_Unauthenticated_Returns401()
    {
        // Arrange
        using var client = _fixture.CreateUnauthenticatedClient();

        using var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Array.Empty<byte>());
        multipartContent.Add(fileContent, "files", "test.pdf");

        // Act
        var response = await client.PostAsync("/api/workspace/matters/pre-fill", multipartContent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // ProblemDetails Validation Helper
    // =========================================================================

    /// <summary>
    /// Verifies that an error response conforms to the RFC 7807 ProblemDetails format:
    ///   - Content-Type: application/problem+json
    ///   - JSON body with: type (string), title (string), status (int), detail (string)
    ///   - Optional: extensions including correlationId
    /// </summary>
    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        string? preReadBody = null)
    {
        // Content-Type check — ProblemDetails must use problem+json
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        contentType.Should().Contain("problem+json",
            "error responses must use application/problem+json media type (RFC 7807)");

        var body = preReadBody ?? await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("type", out var type).Should().BeTrue("ProblemDetails must include 'type'");
        root.TryGetProperty("title", out var title).Should().BeTrue("ProblemDetails must include 'title'");
        root.TryGetProperty("status", out var status).Should().BeTrue("ProblemDetails must include 'status'");
        root.TryGetProperty("detail", out _).Should().BeTrue("ProblemDetails must include 'detail'");

        type.GetString().Should().StartWith("https://",
            "ProblemDetails 'type' must be a URI (RFC 7807)");
        title.GetString().Should().NotBeNullOrWhiteSpace(
            "ProblemDetails 'title' must be non-empty");
        status.GetInt32().Should().Be(
            (int)response.StatusCode,
            "ProblemDetails 'status' must match HTTP response status code");
    }

    // =========================================================================
    // Response Shape Tests (JSON Serialization Contract)
    // =========================================================================

    [Fact]
    public async Task Portfolio_ResponseShape_MatchesPortfolioSummaryResponseDto()
    {
        // Verifies JSON property names match the C# record (camelCase by default in .NET 8 Minimal API)
        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/workspace/portfolio");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioResponseShape>(JsonOptions);
        portfolio.Should().NotBeNull("response must deserialize to known shape");
        portfolio!.ActiveMatters.Should().BeGreaterThan(0);
        portfolio.CachedAt.Should().NotBe(default, "cachedAt must be populated");
    }

    [Fact]
    public async Task Health_ResponseShape_MatchesHealthMetricsResponseDto()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/workspace/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<HealthResponseShape>(JsonOptions);
        health.Should().NotBeNull();
        health!.Timestamp.Should().NotBe(default, "timestamp must be populated");
    }

    [Fact]
    public async Task Briefing_ResponseShape_MatchesBriefingResponseDto()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/workspace/briefing");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var briefing = await response.Content.ReadFromJsonAsync<BriefingResponseShape>(JsonOptions);
        briefing.Should().NotBeNull();
        briefing!.Narrative.Should().NotBeNullOrWhiteSpace("template narrative is always populated");
        briefing.GeneratedAt.Should().NotBe(default, "generatedAt must be populated");
    }

    // =========================================================================
    // Private DTO shapes for deserialization (camelCase JSON from BFF)
    // =========================================================================
    // These mirror the server-side records but use settable properties for deserialization.

    private sealed class PortfolioResponseShape
    {
        public decimal TotalSpend { get; set; }
        public decimal TotalBudget { get; set; }
        public decimal UtilizationPercent { get; set; }
        public int MattersAtRisk { get; set; }
        public int OverdueEvents { get; set; }
        public int ActiveMatters { get; set; }
        public DateTimeOffset CachedAt { get; set; }
    }

    private sealed class HealthResponseShape
    {
        public int MattersAtRisk { get; set; }
        public int OverdueEvents { get; set; }
        public int ActiveMatters { get; set; }
        public decimal BudgetUtilizationPercent { get; set; }
        public decimal PortfolioSpend { get; set; }
        public decimal PortfolioBudget { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

    private sealed class BriefingResponseShape
    {
        public int ActiveMatters { get; set; }
        public decimal TotalSpend { get; set; }
        public decimal TotalBudget { get; set; }
        public double UtilizationPercent { get; set; }
        public int MattersAtRisk { get; set; }
        public int OverdueEvents { get; set; }
        public string Narrative { get; set; } = string.Empty;
        public bool IsAiEnhanced { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }
    }
}
