using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// HTTP contract for <c>POST /api/spe/search/items</c>: route, auth precedence and input validation.
/// </summary>
/// <remarks>
/// Moved 2026-10-07 from <c>tests/unit/Sprk.Bff.Api.Tests/SpeAdmin/SearchItemsTests.cs</c> (not a KEEP
/// path) to this contract path, per <c>projects/sdap-SPE-admin-app-r2/notes/test-diet-report.md</c>.
/// The Graph search request/response shapes are pinned separately in
/// <c>SpeAdminSearchContractTests</c>.
/// </remarks>
public class SpeAdminSearchItemsContractTests
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
    /// Verifies that a well-formed configId that does not exist returns 400
    /// (config not found → ConfigNotFoundException → 400) — and NOT 500.
    /// </summary>
    /// <remarks>
    /// <para><b>Was network-flaky; now offline (2026-10-07).</b> This test used to reach the REAL Dataverse
    /// endpoint for the config lookup, so its outcome depended on the network: it passed on some runs and
    /// timed out after ~100 s on others, and its assertion tolerated 400 <i>or</i> 500 — establishing
    /// nothing. The lookup now goes to <see cref="NotFoundDataverseClient"/>, which answers "no such
    /// record" without a network call, so the assertion can be exact.</para>
    /// </remarks>
    [Fact]
    public async Task SearchItems_WithToken_ValidConfigIdNotFound_Returns400()
    {
        // Arrange — the config lookup answers "not found" offline (see remarks).
        var factory = new CustomWebAppFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<DataverseWebApiClient>(_ => new NotFoundDataverseClient())));
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

        // Assert — exactly 400. A 500 here would be a real defect (an unknown config is a caller error),
        // which the old network-dependent version could not distinguish from a timeout.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Offline Dataverse client: every single-record lookup answers "not found" (null), the same thing
    /// the real client returns on a 404, without making a network call.
    /// </summary>
    private sealed class NotFoundDataverseClient : DataverseWebApiClient
    {
        public NotFoundDataverseClient()
            : base(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dataverse:ServiceUrl"] = "https://offline.invalid",
                    })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                new UnusableCredential())
        {
        }

        public override Task<T?> RetrieveAsync<T>(
            string entitySetName, Guid id, string? select = null, CancellationToken cancellationToken = default)
            where T : default
            => Task.FromResult<T?>(default);
    }

    /// <summary>Throws if anything tries to acquire a token — proof no real Dataverse call is attempted.</summary>
    private sealed class UnusableCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Dataverse must not be reached from this contract test.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext r, CancellationToken c)
            => throw new InvalidOperationException("Dataverse must not be reached from this contract test.");
    }
}
