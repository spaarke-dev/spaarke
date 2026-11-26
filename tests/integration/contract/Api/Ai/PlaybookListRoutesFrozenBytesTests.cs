// CONTRACT — the bytes of GET /api/ai/playbooks and GET /api/ai/playbooks/public (spaarkeai-mcp-bff-api-r2 task 024, D-024-extract).
// KEEP path: endpoint-contract.
//
// WHAT RUNS: the REAL MapPlaybookEndpoints on PlaybookAuthHost (routing, RequireAuthorization, binding, the handlers, the JSON writer),
// substituted only at the IPlaybookService interface and the WhoAmI probe (ADR-038). Each case freezes status, Content-Type, body bytes
// (ProblemDetails' traceId removed: it differs per request) and the query the service was asked, as they were BEFORE task 024 moved
// the two handlers' rules into PlaybookEndpoints.ListPlaybooksForCallerAsync. Task 024 ran this file against the pre-change code
// (all green) and again after the change (all green): the extraction did not move a byte.

using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Ai;

public sealed class PlaybookListRoutesFrozenBytesTests
{
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000024";
    private static readonly Guid SystemUserId = Guid.Parse("0024aaaa-0000-4000-8000-000000000001");
    private static readonly Guid First = Guid.Parse("0024bbbb-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("0024bbbb-0000-4000-8000-000000000002");
    private static readonly Guid OutputType = Guid.Parse("0024cccc-0000-4000-8000-000000000001");

    private static PlaybookListResponse Page(int page, int pageSize) => new()
    {
        Items =
        [
            new PlaybookSummary
            {
                Id = First, Name = "NDA review", Description = "Reviews an NDA", OutputTypeId = OutputType, IsPublic = false,
                IsTemplate = false, OwnerId = SystemUserId, ModifiedOn = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc),
            },
            new PlaybookSummary
            {
                Id = Second, Name = "Lease \"abstract\" <v2>", Description = null, IsPublic = true, IsTemplate = true,
                OwnerId = Guid.Empty, ModifiedOn = new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc),
            },
        ],
        TotalCount = 41,
        Page = page,
        PageSize = pageSize,
    };

    private const string PageBody =
        "{\"items\":[{\"id\":\"0024bbbb-0000-4000-8000-000000000001\",\"name\":\"NDA review\",\"description\":\"Reviews an NDA\","
        + "\"outputTypeId\":\"0024cccc-0000-4000-8000-000000000001\",\"isPublic\":false,\"isTemplate\":false,"
        + "\"ownerId\":\"0024aaaa-0000-4000-8000-000000000001\",\"modifiedOn\":\"2026-11-20T09:30:00Z\"},"
        + "{\"id\":\"0024bbbb-0000-4000-8000-000000000002\",\"name\":\"Lease \\\"abstract\\\" <v2>\",\"description\":null,"
        + "\"outputTypeId\":null,\"isPublic\":true,\"isTemplate\":true,\"ownerId\":\"00000000-0000-0000-0000-000000000000\","
        + "\"modifiedOn\":\"2026-11-19T08:00:00Z\"}],\"totalCount\":41,\"page\":2,\"pageSize\":20,\"totalPages\":3,\"hasNextPage\":true,"
        + "\"hasPreviousPage\":true}";

    [Fact]
    public async Task OwnedList_Resolved_IsTheServicePage_ForTheCallersSystemUserId_AndTheQueryAsBound()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Probe.SystemUserId = SystemUserId;
        PlaybookQueryParameters? asked = null;
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(SystemUserId, It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, PlaybookQueryParameters q, CancellationToken _) => asked = q)
            .ReturnsAsync(Page(2, 20));

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks?page=2&nameFilter=NDA&sortBy=name&sortDescending=false&outputTypeId=" + OutputType);

        status.Should().Be(HttpStatusCode.OK);
        type.Should().Be("application/json; charset=utf-8");
        body.Should().Be(PageBody);
        asked.Should().Be(new PlaybookQueryParameters
        {
            Page = 2,
            PageSize = 20,
            NameFilter = "NDA",
            OutputTypeId = OutputType,
            PublicOnly = false,
            SortBy = "name",
            SortDescending = false,
        });
    }

    [Fact]
    public async Task OwnedList_UnresolvableSystemUserId_IsAnEmptyPage_WithTheRawPage_AndTheNormalizedPageSize_AndNoQuery()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Probe.SystemUserId = null;

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks?page=0&pageSize=500");

        status.Should().Be(HttpStatusCode.OK);
        type.Should().Be("application/json; charset=utf-8");
        body.Should().Be(
            "{\"items\":[],\"totalCount\":0,\"page\":0,\"pageSize\":100,\"totalPages\":0,\"hasNextPage\":false,\"hasPreviousPage\":false}");
        host.Playbooks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnedList_NonGuidOid_Is401_UserIdentityNotFound_AndNothingIsAsked()
    {
        await using var host = await PlaybookAuthHost.StartAsync();

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks", "not-a-guid");

        status.Should().Be(HttpStatusCode.Unauthorized);
        type.Should().Be("application/problem+json");
        body.Should().Be(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.2\",\"title\":\"Unauthorized\",\"status\":401,"
            + "\"detail\":\"User identity not found\"}");
        host.Playbooks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnedList_ServiceFault_Is500_FailedToListPlaybooks()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down: secret-internal-detail"));

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks");

        status.Should().Be(HttpStatusCode.InternalServerError);
        type.Should().Be("application/problem+json");
        body.Should().Be(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.6.1\",\"title\":\"Internal Server Error\",\"status\":500,"
            + "\"detail\":\"Failed to list playbooks\"}");
    }

    [Theory]
    [InlineData(CallerOid)]
    [InlineData("not-a-guid")] // the public list never read the oid
    public async Task PublicList_IsTheServicePage_ForTheQueryAsBound_WithPublicOnly(string oid)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        PlaybookQueryParameters? asked = null;
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .Callback((PlaybookQueryParameters q, CancellationToken _) => asked = q)
            .ReturnsAsync(Page(2, 20));

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks/public?page=2&pageSize=20&nameFilter=Lease", oid);

        status.Should().Be(HttpStatusCode.OK);
        type.Should().Be("application/json; charset=utf-8");
        body.Should().Be(PageBody);
        asked.Should().Be(new PlaybookQueryParameters { Page = 2, PageSize = 20, NameFilter = "Lease", PublicOnly = true });
        host.Playbooks.Verify(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task PublicList_ServiceFault_Is500_FailedToListPublicPlaybooks()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down: secret-internal-detail"));

        var (status, type, body) = await SendAsync(host, "/api/ai/playbooks/public");

        status.Should().Be(HttpStatusCode.InternalServerError);
        type.Should().Be("application/problem+json");
        body.Should().Be(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.6.1\",\"title\":\"Internal Server Error\",\"status\":500,"
            + "\"detail\":\"Failed to list public playbooks\"}");
    }

    /// <summary>Status, Content-Type and the body bytes, with a ProblemDetails' per-request <c>traceId</c> removed.</summary>
    private static async Task<(HttpStatusCode Status, string? ContentType, string Body)> SendAsync(
        PlaybookAuthHost host, string path, string oid = CallerOid)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PlaybookAuthzTestAuthHandler.CallerHeader, oid);
        using var response = await host.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            var node = JsonNode.Parse(body)!.AsObject();
            node.Remove("traceId");
            body = node.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
            });
        }

        return (response.StatusCode, response.Content.Headers.ContentType?.ToString(), body);
    }
}
