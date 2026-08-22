using System.Net;
using FluentAssertions;
using Microsoft.Graph;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration;

/// <summary>
/// Integration tests for Graph API operations using WireMock to simulate responses.
/// Tests retry logic, error handling, and various HTTP status codes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Skip reason on these tests was wrong, and the wrong diagnosis kept them dark.</b> They did
/// not fail because of "WireMock.Net path matching" or a missing configuration. WireMock.Net 1.5.45
/// loads <c>MimeKitLite</c> at runtime while mapping an incoming request, and this test project
/// carried <c>&lt;ExcludeAssets&gt;all&lt;/ExcludeAssets&gt;</c> on that package — which strips the
/// runtime asset, not just the compile-time one it was added to suppress. Every request died with
/// <c>FileNotFoundException</c> inside WireMock's <c>GlobalExceptionMiddleware</c> and came back 500.
/// Fixed in the csproj by excluding only the <c>compile</c> asset (task 040,
/// <c>sdap-SPE-admin-app-r2</c>).
/// </para>
/// <para>
/// <b>They remain skipped for a different reason.</b> Each one points a bare <see cref="HttpClient"/>
/// at WireMock and asserts WireMock returned the body WireMock was just told to return. No production
/// code is on the path, so they cannot fail for any reason that matters — ADR-038 B7/B10 scaffolding.
/// Un-skipping them would add green tests that defend nothing. Retiring them is task 042's scope, not
/// this task's.
/// </para>
/// <para>
/// <b>Use this instead:</b> <c>tests/integration/contract/SpeAdmin/GraphWireMockFixture.cs</c>, which
/// drives real <c>SpeAdminGraphService</c> methods over the same fake endpoint and asserts both the
/// outgoing request shape and the response mapping.
/// </para>
/// </remarks>
public class GraphApiWireMockTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;

    public GraphApiWireMockTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact(Skip = "Superseded — see class remarks. The 500s are FIXED (MimeKitLite runtime asset); these tests assert only that WireMock echoes its own stub. Retirement is task 042's call.")]
    public async Task ListChildren_Success_ReturnsItems()
    {
        // Arrange
        var driveId = "test-drive-id";
        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/root/children")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(@"{
                    ""value"": [
                        {
                            ""id"": ""item-1"",
                            ""name"": ""Document1.txt"",
                            ""size"": 1024,
                            ""lastModifiedDateTime"": ""2024-01-01T00:00:00Z""
                        },
                        {
                            ""id"": ""item-2"",
                            ""name"": ""Document2.pdf"",
                            ""size"": 2048,
                            ""lastModifiedDateTime"": ""2024-01-02T00:00:00Z""
                        }
                    ]
                }"));

        // Act
        var response = await _httpClient.GetAsync($"/drives/{driveId}/root/children");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Document1.txt");
        content.Should().Contain("Document2.pdf");
    }

    [Fact(Skip = "Superseded — see class remarks. The 500s are FIXED (MimeKitLite runtime asset); these tests assert only that WireMock echoes its own stub. Retirement is task 042's call.")]
    public async Task ListChildren_Throttled_RetriesWithBackoff()
    {
        // Arrange
        var driveId = "test-drive-id";

        // Set up initial throttled response (429)
        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/root/children")
                .UsingGet())
            .InScenario("Retry Scenario")
            .WillSetStateTo("First Retry")
            .RespondWith(Response.Create()
                .WithStatusCode(429)
                .WithHeader("Retry-After", "2"));

        // Second throttled response
        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/root/children")
                .UsingGet())
            .InScenario("Retry Scenario")
            .WhenStateIs("First Retry")
            .WillSetStateTo("Second Retry")
            .RespondWith(Response.Create()
                .WithStatusCode(429)
                .WithHeader("Retry-After", "2"));

        // Third call succeeds
        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/root/children")
                .UsingGet())
            .InScenario("Retry Scenario")
            .WhenStateIs("Second Retry")
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(@"{""value"": []}"));

        // Act
        // Note: This requires GraphHttpMessageHandler with retry logic
        // Test will pass once Task 4.1 is complete
        var response = await _httpClient.GetAsync($"/drives/{driveId}/root/children");

        // Assert
        // Without retry logic, only the first request will be made
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact(Skip = "Superseded — see class remarks. The 500s are FIXED (MimeKitLite runtime asset); these tests assert only that WireMock echoes its own stub. Retirement is task 042's call.")]
    public async Task DownloadContent_NotFound_Returns404()
    {
        // Arrange
        var driveId = "test-drive-id";
        var itemId = "non-existent-item";

        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/items/{itemId}/content")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithBody(@"{
                    ""error"": {
                        ""code"": ""itemNotFound"",
                        ""message"": ""The resource could not be found.""
                    }
                }"));

        // Act
        var response = await _httpClient.GetAsync($"/drives/{driveId}/items/{itemId}/content");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(Skip = "Superseded — see class remarks. The 500s are FIXED (MimeKitLite runtime asset); these tests assert only that WireMock echoes its own stub. Retirement is task 042's call.")]
    public async Task UploadSmall_Forbidden_Returns403()
    {
        // Arrange
        var driveId = "test-drive-id";
        var path = "/test/file.txt";

        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/root:/{path}:/content")
                .UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(403)
                .WithBody(@"{
                    ""error"": {
                        ""code"": ""accessDenied"",
                        ""message"": ""Access denied""
                    }
                }"));

        // Act
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        var response = await _httpClient.PutAsync($"/drives/{driveId}/root:/{path}:/content", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(Skip = "Superseded — see class remarks. The 500s are FIXED (MimeKitLite runtime asset); these tests assert only that WireMock echoes its own stub. Retirement is task 042's call.")]
    public async Task DeleteItem_Success_Returns204()
    {
        // Arrange
        var driveId = "test-drive-id";
        var itemId = "item-to-delete";

        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/items/{itemId}")
                .UsingDelete())
            .RespondWith(Response.Create()
                .WithStatusCode(204)); // No Content

        // Act
        var response = await _httpClient.DeleteAsync($"/drives/{driveId}/items/{itemId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact(Skip = "Superseded — see class remarks. The 500s are FIXED (MimeKitLite runtime asset); these tests assert only that WireMock echoes its own stub. Retirement is task 042's call.")]
    public async Task DownloadContent_RangeRequest_ReturnsPartialContent()
    {
        // Arrange
        var driveId = "test-drive-id";
        var itemId = "large-file";
        var fileContent = "This is a test file with some content for range testing";
        var rangeStart = 0;
        var rangeEnd = 9;

        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/items/{itemId}/content")
                .WithHeader("Range", $"bytes={rangeStart}-{rangeEnd}")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(206) // Partial Content
                .WithHeader("Content-Range", $"bytes {rangeStart}-{rangeEnd}/{fileContent.Length}")
                .WithBody(fileContent.Substring(rangeStart, rangeEnd - rangeStart + 1)));

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"/drives/{driveId}/items/{itemId}/content");
        request.Headers.Add("Range", $"bytes={rangeStart}-{rangeEnd}");
        var response = await _httpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("This is a ");
    }

    public void Dispose()
    {
        _mockServer?.Stop();
        _mockServer?.Dispose();
        _httpClient?.Dispose();
    }
}
