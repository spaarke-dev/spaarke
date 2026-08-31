using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.SpeAdmin;

/// <summary>
/// Unit tests for the SearchItems endpoint and the SearchItemsAsync service method.
///
/// Tests cover:
/// - Input validation (empty query → 400, missing configId → 400)
/// - Acceptance criteria: scoped search, unscoped search, empty results, pagination
/// </summary>
public class SearchItemsTests
{
    // =========================================================================
    // Integration-style tests via WebApplicationFactory
    // (Graph calls skipped due to sealed SDK types)
    // =========================================================================

    /// <summary>
    /// Verifies that POST /api/spe/search/items requires authentication (returns 401 without token).
    /// </summary>
    [Fact]
    public async Task SearchItems_WithoutAuthentication_Returns401()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "test",
            ContainerId: null,
            FileType: null,
            PageSize: null,
            SkipToken: null);

        // Act
        var response = await client.PostAsJsonAsync("/api/spe/search/items?configId=00000000-0000-0000-0000-000000000001", requestBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies that missing configId returns 401 (auth runs before validation in this route group).
    /// </summary>
    [Fact]
    public async Task SearchItems_MissingConfigId_Returns401WithoutToken()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest("test", null, null, null, null);

        // Act
        var response = await client.PostAsJsonAsync("/api/spe/search/items", requestBody);

        // Assert — auth runs first
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies endpoint route is registered at POST /api/spe/search/items.
    /// When authenticated, missing configId should return 400 (not 404).
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_MissingConfigId_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest("test", null, null, null, null);

        // Act — no configId provided
        var response = await client.PostAsJsonAsync("/api/spe/search/items", requestBody);

        // Assert — route exists; configId validation returns 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that an empty query string returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_EmptyQuery_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "",
            ContainerId: null,
            FileType: null,
            PageSize: null,
            SkipToken: null);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/spe/search/items?configId=00000000-0000-0000-0000-000000000001",
            requestBody);

        // Assert — empty query is rejected (per acceptance criteria)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that whitespace-only query string returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_WhitespaceQuery_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "   ",
            ContainerId: null,
            FileType: null,
            PageSize: null,
            SkipToken: null);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/spe/search/items?configId=00000000-0000-0000-0000-000000000001",
            requestBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that an invalid (non-GUID) configId returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_InvalidConfigId_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest("contract.pdf", null, null, null, null);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/spe/search/items?configId=not-a-guid",
            requestBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that a valid request with a non-existent configId returns 400
    /// (configId not found in Dataverse → ConfigNotFoundException → 400).
    /// In the test environment the Dataverse client is mocked and returns null for any configId.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_ValidConfigIdNotFound_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "contract.pdf",
            ContainerId: null,
            FileType: null,
            PageSize: null,
            SkipToken: null);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/spe/search/items?configId=00000000-0000-0000-0000-000000000001",
            requestBody);

        // Assert — configId not found in Dataverse → 400
        // (In test environment, Dataverse is not connected; SpeAdminGraphService will fail config resolution)
        //
        // AMBIGUOUS (task 042): this tolerates a 500, which may be masking a real defect rather than the
        // documented "config not found → 400" behavior. Left as-is per task 042 instructions (assertion
        // change needs a human call) — /test-diet at task 090 should decide whether to tighten this.
        //
        // 🔴 NEW EVIDENCE 2026-08-27 (task 025/042 follow-up). The problem is worse than a loose
        // assertion: **this test makes a REAL outbound call and its result depends on network state.**
        // It passed twice earlier the same session and then failed with
        //
        //     System.Threading.Tasks.TaskCanceledException : The operation was canceled.
        //     ---- HttpRequestException : Error while copying content to a stream.
        //
        // after ~100 s — i.e. the Dataverse config lookup went from failing fast to hanging until the
        // HttpClient timeout. Proven pre-existing (`git stash -u` → identical failure and duration) and
        // therefore NOT a regression from the work in flight, but it is non-deterministic by
        // construction: a test under `tests/unit/**` that reaches the network cannot have a stable
        // result, and a ~2-minute timeout makes the whole suite's runtime hostage to it.
        //
        // For /test-diet at 090 this is no longer only "tighten the assertion" — the choice is between
        // giving the factory an offline Dataverse double (making 400-vs-500 actually decidable) or
        // removing the test. Tightening the assertion alone would convert an intermittent pass into an
        // intermittent failure without establishing anything.
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError);
    }
}
