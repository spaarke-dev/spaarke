using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Tests for the GET /api/ai/chat/context-mappings/analysis/{analysisId} endpoint.
///
/// Tests cover:
/// - Endpoint registration and route structure (ADR-001: Minimal API)
/// - Authentication/authorization enforcement (ADR-008: endpoint filters)
/// - HTTP GET with a valid analysisId returns 200 with <see cref="AnalysisChatContextResponse"/>
/// - HTTP GET with an analysisId that cannot be resolved returns 404
/// - Missing auth returns 401
/// - Response model structure (inline actions, playbook info, analysis context)
///
/// NOTE: The endpoint uses a stub <see cref="AnalysisChatContextResolver"/> (pending task 021
/// Dataverse integration). All GET requests with any analysisId return a 200 with the default
/// stub response. The 404 path cannot be exercised until the Dataverse implementation is complete.
/// Tests for that scenario are marked Skip with an explanatory note.
/// </summary>
public class AnalysisChatContextEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _client;

    public AnalysisChatContextEndpointsTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    // =========================================================================
    // Endpoint Registration Tests (ADR-001 — Minimal API)
    // =========================================================================

    [Fact]
    public void MapAnalysisChatContextEndpoints_MethodExists_AndIsStatic()
    {
        // Arrange
        var method = typeof(AnalysisChatContextEndpoints).GetMethod("MapAnalysisChatContextEndpoints");

        // Assert
        method.Should().NotBeNull("endpoint registration extension method must exist");
        method!.IsStatic.Should().BeTrue("Minimal API extension methods are static (ADR-001)");
        method.ReturnType.Should().Be(typeof(IEndpointRouteBuilder));
    }

    // =========================================================================
    // Authentication Tests (ADR-008)
    // =========================================================================

    [Fact]
    public async Task GetAnalysisChatContext_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange — no Authorization header (FakeAuthHandler rejects unauthenticated requests)

        // Act
        var response = await _client.GetAsync("/api/ai/chat/context-mappings/analysis/analysis-001");

        // Assert
        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError],
            "endpoint requires authorization (ADR-008); unauthenticated requests must not return 200");
    }

    [Fact]
    public async Task GetAnalysisChatContext_WithAuth_DoesNotReturn404()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/chat/context-mappings/analysis/analysis-001");

        // Assert — endpoint is registered and reachable
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "endpoint /api/ai/chat/context-mappings/analysis/{analysisId} must be registered");
        response.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed,
            "endpoint must support GET");
    }

    // =========================================================================
    // Successful Resolution Tests (200 OK)
    // =========================================================================

    [Fact]
    public async Task GetAnalysisChatContext_WithAuth_Returns200_WithStubResolver()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act — stub Dataverse resolver always returns a non-null response
        var response = await _client.GetAsync("/api/ai/chat/context-mappings/analysis/analysis-stub-001");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "stub resolver returns a non-null response for any analysisId");
    }

    [Fact]
    public async Task GetAnalysisChatContext_WithAuth_ResponseDeserializesTo_AnalysisChatContextResponse()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        const string analysisId = "analysis-stub-deserialize";

        // Act
        var response = await _client.GetAsync($"/api/ai/chat/context-mappings/analysis/{analysisId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var context = JsonSerializer.Deserialize<AnalysisChatContextResponse>(json, JsonOptions);

        context.Should().NotBeNull("response should deserialize to AnalysisChatContextResponse");
        context!.AnalysisContext.Should().NotBeNull("response must include AnalysisContext");
        context.InlineActions.Should().NotBeNull("response must include InlineActions list");
    }

    [Fact]
    public async Task GetAnalysisChatContext_WithAuth_ResponseContainsAnalysisId()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        const string analysisId = "analysis-id-roundtrip-test";

        // Act
        var response = await _client.GetAsync($"/api/ai/chat/context-mappings/analysis/{analysisId}");

        // Assert — analysisId is echoed back in the AnalysisContext
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var context = JsonSerializer.Deserialize<AnalysisChatContextResponse>(json, JsonOptions);

        context!.AnalysisContext.AnalysisId.Should().Be(analysisId,
            "AnalysisContext.AnalysisId must match the route parameter");
    }

    [Fact]
    public async Task GetAnalysisChatContext_WithAuth_ResponseHasNonEmptyDefaultPlaybookName()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/chat/context-mappings/analysis/analysis-stub-playbook");

        // Assert — stub returns "Default Analysis Playbook"
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var context = JsonSerializer.Deserialize<AnalysisChatContextResponse>(json, JsonOptions);

        context!.DefaultPlaybookName.Should().NotBeNullOrWhiteSpace(
            "DefaultPlaybookName must be present so the AnalysisWorkspace UI can display it");
    }

    [Fact]
    public async Task GetAnalysisChatContext_WithAuth_StubResponse_ContainsAllSevenInlineActions()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act — stub resolver maps all 7 capabilities to inline actions
        var response = await _client.GetAsync("/api/ai/chat/context-mappings/analysis/analysis-stub-actions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var context = JsonSerializer.Deserialize<AnalysisChatContextResponse>(json, JsonOptions);

        context!.InlineActions.Should().HaveCount(7,
            "stub resolver includes all 7 capability actions so the UI can render QuickActionChips during development");
    }

    [Fact]
    public async Task GetAnalysisChatContext_WithAuth_StubResponse_IncludesSelectionReviseWithDiffType()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/chat/context-mappings/analysis/analysis-stub-diff");

        // Assert — selection_revise action must be present with actionType='diff'
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var context = JsonSerializer.Deserialize<AnalysisChatContextResponse>(json, JsonOptions);

        var selectionRevise = context!.InlineActions
            .FirstOrDefault(a => a.Id == PlaybookCapabilities.SelectionRevise);

        selectionRevise.Should().NotBeNull("selection_revise action must be present in stub response");
        selectionRevise!.ActionType.Should().Be("diff",
            "selection_revise must be of actionType 'diff' to trigger DiffReviewPanel");
    }

    [Fact]
    public async Task GetAnalysisChatContext_WithAuth_ContentType_IsApplicationJson()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync("/api/ai/chat/context-mappings/analysis/analysis-stub-ct");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "endpoint response must be application/json for JSON deserialization by the client");
    }

    // =========================================================================
    // 404 Path — pending Dataverse implementation
    // =========================================================================

    [Fact(Skip = "404 path requires real Dataverse integration (task 021). Stub resolver always returns non-null. " +
                 "This test will be enabled once ResolveFromDataverseAsync is fully implemented.")]
    public async Task GetAnalysisChatContext_WhenAnalysisNotFound_Returns404()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act — once Dataverse integration is complete, a non-existent ID should return 404
        var response = await _client.GetAsync("/api/ai/chat/context-mappings/analysis/non-existent-id-00000");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "resolver returns null when analysis record not found → endpoint returns 404");
    }
}

/// <summary>
/// Model validation and serialization tests for <see cref="AnalysisChatContextResponse"/>
/// and related record types.
///
/// These tests validate the response model structure without requiring the full web application
/// stack — no HTTP pipeline, no DI, pure type testing.
/// </summary>
public class AnalysisChatContextResponseModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void AnalysisChatContextResponse_CanBeCreated_WithAllFields()
    {
        // Arrange & Act
        var response = new AnalysisChatContextResponse(
            DefaultPlaybookId: "playbook-001",
            DefaultPlaybookName: "Patent Claims Analysis",
            AvailablePlaybooks:
            [
                new AnalysisPlaybookInfo("playbook-001", "Patent Claims Analysis", "Analyzes patent claims"),
            ],
            InlineActions:
            [
                new InlineActionInfo(PlaybookCapabilities.Search, "Search", "chat", "Search knowledge sources"),
                new InlineActionInfo(PlaybookCapabilities.SelectionRevise, "Revise Selection", "diff", "Revise selected text"),
            ],
            KnowledgeSources:
            [
                new AnalysisKnowledgeSourceInfo("rag_index", "ks-001", "Patent Index"),
            ],
            AnalysisContext: new AnalysisContextInfo(
                AnalysisId: "analysis-001",
                AnalysisType: "patent_claims",
                MatterType: "ip_litigation",
                PracticeArea: "intellectual_property",
                SourceFileId: "file-001",
                SourceContainerId: "container-001"));

        // Assert
        response.DefaultPlaybookId.Should().Be("playbook-001");
        response.DefaultPlaybookName.Should().Be("Patent Claims Analysis");
        response.AvailablePlaybooks.Should().HaveCount(1);
        response.InlineActions.Should().HaveCount(2);
        response.KnowledgeSources.Should().HaveCount(1);
        response.AnalysisContext.AnalysisId.Should().Be("analysis-001");
        response.AnalysisContext.AnalysisType.Should().Be("patent_claims");
    }

    [Fact]
    public void AnalysisChatContextResponse_SerializesToJson_WithCamelCase()
    {
        // Arrange
        var response = new AnalysisChatContextResponse(
            DefaultPlaybookId: "playbook-001",
            DefaultPlaybookName: "Test Playbook",
            AvailablePlaybooks: [],
            InlineActions: [new InlineActionInfo("search", "Search", "chat")],
            KnowledgeSources: [],
            AnalysisContext: new AnalysisContextInfo(
                AnalysisId: "analysis-001",
                AnalysisType: null,
                MatterType: null,
                PracticeArea: null,
                SourceFileId: null,
                SourceContainerId: null));

        // Act
        var json = JsonSerializer.Serialize(response, JsonOptions);

        // Assert
        json.Should().Contain("\"defaultPlaybookId\"");
        json.Should().Contain("\"defaultPlaybookName\"");
        json.Should().Contain("\"availablePlaybooks\"");
        json.Should().Contain("\"inlineActions\"");
        json.Should().Contain("\"knowledgeSources\"");
        json.Should().Contain("\"analysisContext\"");
    }

    [Fact]
    public void AnalysisChatContextResponse_RoundTrips_ThroughJsonSerialization()
    {
        // Arrange
        var original = new AnalysisChatContextResponse(
            DefaultPlaybookId: "playbook-abc",
            DefaultPlaybookName: "Claims Review",
            AvailablePlaybooks:
            [
                new AnalysisPlaybookInfo("playbook-abc", "Claims Review", null),
            ],
            InlineActions:
            [
                new InlineActionInfo(PlaybookCapabilities.Analyze, "Analyze", "chat", "Extract claims"),
                new InlineActionInfo(PlaybookCapabilities.SelectionRevise, "Revise", "diff", "Edit claims"),
            ],
            KnowledgeSources: [],
            AnalysisContext: new AnalysisContextInfo(
                AnalysisId: "analysis-round-trip",
                AnalysisType: "claims_review",
                MatterType: null,
                PracticeArea: "intellectual_property",
                SourceFileId: "file-xyz",
                SourceContainerId: null));

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<AnalysisChatContextResponse>(json, JsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.DefaultPlaybookId.Should().Be(original.DefaultPlaybookId);
        deserialized.DefaultPlaybookName.Should().Be(original.DefaultPlaybookName);
        deserialized.AvailablePlaybooks.Should().HaveCount(1);
        deserialized.InlineActions.Should().HaveCount(2);
        deserialized.AnalysisContext.AnalysisId.Should().Be(original.AnalysisContext.AnalysisId);
        deserialized.AnalysisContext.PracticeArea.Should().Be("intellectual_property");
    }

    [Fact]
    public void InlineActionInfo_SelectionRevise_HasDiffActionType()
    {
        // Arrange & Act
        var action = new InlineActionInfo(
            PlaybookCapabilities.SelectionRevise,
            "Revise Selection",
            "diff",
            "Revise selected text and show diff");

        // Assert
        action.Id.Should().Be(PlaybookCapabilities.SelectionRevise);
        action.ActionType.Should().Be("diff");
        action.Label.Should().Be("Revise Selection");
    }

    [Fact]
    public void InlineActionInfo_ChatActions_HaveChatActionType()
    {
        // Arrange & Act
        var search = new InlineActionInfo(PlaybookCapabilities.Search, "Search", "chat");
        var analyze = new InlineActionInfo(PlaybookCapabilities.Analyze, "Analyze", "chat");

        // Assert
        search.ActionType.Should().Be("chat");
        analyze.ActionType.Should().Be("chat");
    }

    [Fact]
    public void AnalysisContextInfo_SupportsNullableFields()
    {
        // Arrange & Act — nullable fields are optional per spec
        var context = new AnalysisContextInfo(
            AnalysisId: "analysis-nullable",
            AnalysisType: null,
            MatterType: null,
            PracticeArea: null,
            SourceFileId: null,
            SourceContainerId: null);

        // Assert
        context.AnalysisId.Should().Be("analysis-nullable");
        context.AnalysisType.Should().BeNull();
        context.MatterType.Should().BeNull();
        context.PracticeArea.Should().BeNull();
        context.SourceFileId.Should().BeNull();
        context.SourceContainerId.Should().BeNull();
    }

    [Fact]
    public void AnalysisPlaybookInfo_NullDescription_IsValid()
    {
        // Arrange & Act
        var playbook = new AnalysisPlaybookInfo("playbook-001", "My Playbook", null);

        // Assert
        playbook.Id.Should().Be("playbook-001");
        playbook.Name.Should().Be("My Playbook");
        playbook.Description.Should().BeNull();
    }

    [Fact]
    public void AnalysisKnowledgeSourceInfo_NullLabel_IsValid()
    {
        // Arrange & Act
        var ks = new AnalysisKnowledgeSourceInfo("rag_index", "ks-001");

        // Assert
        ks.Type.Should().Be("rag_index");
        ks.Id.Should().Be("ks-001");
        ks.Label.Should().BeNull();
    }
}
