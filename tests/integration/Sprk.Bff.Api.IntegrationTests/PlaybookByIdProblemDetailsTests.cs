using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests;

/// <summary>
/// Integration tests asserting that <c>GET /api/ai/playbooks/by-id/{id}</c> returns a full
/// RFC 7807 ProblemDetails payload on 404 per ADR-019 (task 011 / FR-01).
/// Per Q&amp;A 2026-06-22 Q1: stable-ID lookup uses the <c>sprk_playbookid</c> alt-key.
/// </summary>
/// <remarks>
/// <para>
/// Acceptance criteria (per task 011 POML):
/// </para>
/// <list type="bullet">
///   <item>HTTP 404 status.</item>
///   <item>Content-type is <c>application/problem+json</c>.</item>
///   <item>Body has fields: <c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c>, <c>instance</c>.</item>
///   <item><c>detail</c> includes the requested id string (ADR-015 — id is user-supplied input, not memory content).</item>
///   <item><c>type</c> is a non-empty URI (Spaarke convention <c>https://spaarke.com/problems/...</c>).</item>
/// </list>
/// </remarks>
public class PlaybookByIdProblemDetailsTests : IClassFixture<PlaybookByIdIntegrationTestFixture>
{
    private readonly PlaybookByIdIntegrationTestFixture _fixture;

    public PlaybookByIdProblemDetailsTests(PlaybookByIdIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.PlaybookLookup.Reset();
    }

    [Fact]
    public async Task GetById_404_HasRfc7807ProblemDetailsContentType()
    {
        // Arrange — id NOT configured in stub; stub will throw PlaybookNotFoundException.
        using var client = _fixture.CreateAuthenticatedClient(PlaybookByIdTestConstants.TenantA);

        // Act
        var response = await client.GetAsync(
            $"/api/ai/playbooks/by-id/{PlaybookByIdTestConstants.KnownMissingId}");

        // Assert — status + content-type per RFC 7807 (ADR-019).
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json",
                "RFC 7807 mandates application/problem+json for ProblemDetails responses (ADR-019)");
    }

    [Fact]
    public async Task GetById_404_BodyHasAllRfc7807Fields()
    {
        // Arrange — id NOT configured; stub throws PlaybookNotFoundException.
        const string missingId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
        using var client = _fixture.CreateAuthenticatedClient(PlaybookByIdTestConstants.TenantA);

        // Act
        var response = await client.GetAsync($"/api/ai/playbooks/by-id/{missingId}");
        var body = await response.Content.ReadAsStringAsync();

        // Assert — 404 + parse body as JSON and assert all 5 RFC 7807 fields are present.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("type", out var typeProp).Should().BeTrue("RFC 7807 requires 'type' field");
        root.TryGetProperty("title", out var titleProp).Should().BeTrue("RFC 7807 requires 'title' field");
        root.TryGetProperty("status", out var statusProp).Should().BeTrue("RFC 7807 requires 'status' field");
        root.TryGetProperty("detail", out var detailProp).Should().BeTrue("RFC 7807 requires 'detail' field");
        root.TryGetProperty("instance", out var instanceProp).Should().BeTrue("RFC 7807 requires 'instance' field");

        // Field values.
        typeProp.GetString().Should().NotBeNullOrWhiteSpace("type must be a non-empty URI");
        Uri.TryCreate(typeProp.GetString(), UriKind.Absolute, out _).Should().BeTrue(
            "type field should be a valid absolute URI per RFC 7807");

        titleProp.GetString().Should().Be("Playbook Not Found");
        statusProp.GetInt32().Should().Be(404);
        detailProp.GetString().Should().Contain(missingId,
            "detail must include the requested id (user-supplied input, permitted by ADR-015)");
        instanceProp.GetString().Should().Be($"/api/ai/playbooks/by-id/{missingId}",
            "instance must identify the specific request URI per RFC 7807");
    }

    [Fact]
    public async Task GetById_404_TypeUriFollowsSpaarkeProblemsConvention()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient(PlaybookByIdTestConstants.TenantA);

        // Act
        var response = await client.GetAsync(
            $"/api/ai/playbooks/by-id/{PlaybookByIdTestConstants.KnownMissingId}");
        var body = await response.Content.ReadAsStringAsync();

        // Assert — Spaarke convention is `https://spaarke.com/problems/<slug>` (cf. OwnershipValidator.cs).
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var doc = JsonDocument.Parse(body);
        var typeValue = doc.RootElement.GetProperty("type").GetString();
        typeValue.Should().Be("https://spaarke.com/problems/playbook-not-found",
            "404 'type' URI should follow the Spaarke `https://spaarke.com/problems/<slug>` convention");
    }
}
