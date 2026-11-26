using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Tests.Api.Ai;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Ai;

/// <summary>
/// <see cref="PlaybookEndpoints.ListPlaybooksForCallerAsync"/> (spaarkeai-mcp-bff-api-r2 task 024, spec FR-04, D-024-extract): THE rules of
/// <c>GET /api/ai/playbooks</c> and <c>GET /api/ai/playbooks/public</c> — the caller's identity, the owner as the caller's
/// <c>systemuserid</c>, the empty page when it cannot be resolved, <c>PublicOnly</c>, paging and sort — for a host that is not the routes
/// (the MCP tool <c>spaarke_list_playbooks</c>). Each case calls the method directly, as the tool does, then sends the same input to the
/// REAL route (<see cref="PlaybookAuthHost"/>) over the SAME substitutes: the route's status and body must be what the method's answer
/// maps to, and both must ask the playbook service the same questions with the same query. The route's bytes are frozen separately
/// (<c>PlaybookListRoutesFrozenBytesTests</c>).
/// </summary>
/// <remarks>
/// Real: the endpoint mapper, the handlers, the shared method, <see cref="PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync"/>.
/// Substituted only at the boundaries <see cref="PlaybookRouteAuthorizationContractTests"/> uses: the WhoAmI probe and the playbook
/// service interface (ADR-038).
/// </remarks>
public sealed class PlaybookListSharedMethodTests
{
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000124";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static PlaybookListResponse ServicePage(params Guid[] ids) => new()
    {
        Items = ids.Select((id, i) => new PlaybookSummary { Id = id, Name = $"Playbook {i}", Description = "A recipe", IsPublic = i % 2 == 0 })
            .ToArray(),
        TotalCount = ids.Length,
        Page = 1,
        PageSize = 20,
    };

    // ── Listed ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Owned and public: the method returns the route's rows and 200, from the same service call with the same query")]
    [InlineData(PlaybookListScope.Owned)]
    [InlineData(PlaybookListScope.Public)]
    public async Task ListPlaybooksForCaller_Listed_EqualsTheRoute_RowsStatusAndQuestions(PlaybookListScope scope)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var page = ServicePage(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var (direct, status, body) = await DirectThenRestAsync(
            host, scope, "page=2&pageSize=150&nameFilter=lease&sortBy=sprk_name&sortDescending=false",
            http => PlaybookEndpoints.ListPlaybooksForCallerAsync(http, scope, 2, 150, "lease", null, "sprk_name", false, CancellationToken.None));

        direct.Outcome.Should().Be(PlaybookListOutcome.Listed);
        direct.Status.Should().Be(StatusCodes.Status200OK);
        direct.ReasonCode.Should().BeNull();
        direct.OwnerSystemUserId.Should().Be(scope == PlaybookListScope.Owned ? host.Probe.SystemUserId : null,
            "an owned list carries the caller's resolved systemuserid (for a host that merges it with the public list); a public list has none");
        direct.Items.Select(i => i.Id).Should().Equal(page.Items.Select(i => i.Id));
        status.Should().Be((HttpStatusCode)direct.Status);
        JsonNode.DeepEquals(body, JsonSerializer.SerializeToNode(direct.Page, Web)).Should().BeTrue("the route's body is the method's page");

        var asked = host.Playbooks.Invocations.Select(i => i.Arguments[scope == PlaybookListScope.Owned ? 1 : 0]).OfType<PlaybookQueryParameters>().ToList();
        asked.Should().HaveCount(2).And.AllBeEquivalentTo(new PlaybookQueryParameters
        {
            Page = 2,
            PageSize = 150,
            NameFilter = "lease",
            SortBy = "sprk_name",
            SortDescending = false,
            PublicOnly = scope == PlaybookListScope.Public,
        });
        if (scope == PlaybookListScope.Owned)
        {
            host.Playbooks.Invocations.Select(i => i.Arguments[0]).Should().AllBeEquivalentTo(host.Probe.SystemUserId,
                "the owner is the caller's systemuserid, never the Entra oid");
        }
    }

    // ── Owned, systemuserid unresolvable (fail closed) ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Owned, the caller's systemuserid unresolvable: OwnerUnresolved with an empty page and no query; the route answers the same page with 200")]
    public async Task ListPlaybooksForCaller_OwnerUnresolved_IsAnEmptyPage_AndNothingIsQueried()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Probe.SystemUserId = null;

        var (direct, status, body) = await DirectThenRestAsync(
            host, PlaybookListScope.Owned, "page=3&pageSize=500",
            http => PlaybookEndpoints.ListPlaybooksForCallerAsync(http, PlaybookListScope.Owned, 3, 500, cancellationToken: CancellationToken.None));

        direct.Outcome.Should().Be(PlaybookListOutcome.OwnerUnresolved);
        direct.Status.Should().Be(StatusCodes.Status200OK);
        direct.Items.Should().BeEmpty();
        direct.Page!.Page.Should().Be(3);
        direct.Page.PageSize.Should().Be(100, "the empty page carries the normalized page size, as the route's always did");
        status.Should().Be(HttpStatusCode.OK);
        JsonNode.DeepEquals(body, JsonSerializer.SerializeToNode(direct.Page, Web)).Should().BeTrue();
        host.Playbooks.Invocations.Should().BeEmpty("no other owner's rows are listed in place of the caller's");
    }

    // ── Failed ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "A service fault is Failed (500, playbook.list-failed); the route answers its 500")]
    [InlineData(PlaybookListScope.Owned)]
    [InlineData(PlaybookListScope.Public)]
    public async Task ListPlaybooksForCaller_ServiceFault_IsFailed_AndTheRouteIs500(PlaybookListScope scope)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down"));
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down"));

        var (direct, status, _) = await DirectThenRestAsync(
            host, scope, string.Empty,
            http => PlaybookEndpoints.ListPlaybooksForCallerAsync(http, scope, cancellationToken: CancellationToken.None));

        direct.Should().BeSameAs(PlaybookListResult.Failed);
        direct.Status.Should().Be(StatusCodes.Status500InternalServerError);
        direct.ReasonCode.Should().Be(PlaybookListResult.ListFailedReasonCode);
        direct.Page.Should().BeNull();
        status.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact(DisplayName = "Only a cancellation of the caller's token propagates; nothing is turned into a page")]
    public async Task ListPlaybooksForCaller_Cancelled_Propagates()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .Returns((PlaybookQueryParameters _, CancellationToken ct) => Task.FromCanceled<PlaybookListResponse>(ct));

        var act = () => PlaybookEndpoints.ListPlaybooksForCallerAsync(DirectContext(host), PlaybookListScope.Public, cancellationToken: cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── No caller ───────────────────────────────────────────────────────────────────────────────────────────────────

    public enum NoCallerCase
    {
        /// <summary>No request context at all.</summary>
        NullContext,

        /// <summary>An identity with an oid but no authentication type.</summary>
        Unauthenticated,
    }

    [Theory(DisplayName = "No request context, or an unauthenticated principal, is UserContextRequired (401) for both lists, with nothing asked")]
    [InlineData(NoCallerCase.NullContext, PlaybookListScope.Owned)]
    [InlineData(NoCallerCase.NullContext, PlaybookListScope.Public)]
    [InlineData(NoCallerCase.Unauthenticated, PlaybookListScope.Owned)]
    [InlineData(NoCallerCase.Unauthenticated, PlaybookListScope.Public)]
    public async Task ListPlaybooksForCaller_NoSignedInCaller_IsUserContextRequired_AndNothingIsRead(NoCallerCase @case, PlaybookListScope scope)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var http = @case == NoCallerCase.NullContext
            ? null
            : DirectContext(host, new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", CallerOid) })));

        var result = await PlaybookEndpoints.ListPlaybooksForCallerAsync(http, scope, cancellationToken: CancellationToken.None);

        result.Should().BeSameAs(PlaybookListResult.UserContextRequired);
        result.Status.Should().Be(StatusCodes.Status401Unauthorized);
        result.ReasonCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        result.Items.Should().BeEmpty();
        host.Playbooks.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "Owned: a signed-in principal whose oid is not a GUID is UserContextRequired; the owned route answers it 401")]
    public async Task ListPlaybooksForCaller_OwnedWithANonGuidOid_IsUserContextRequired_AndTheRouteIs401()
    {
        await using var host = await PlaybookAuthHost.StartAsync();

        var direct = await PlaybookEndpoints.ListPlaybooksForCallerAsync(
            DirectContext(host, Principal("not-a-guid")), PlaybookListScope.Owned, cancellationToken: CancellationToken.None);
        using var rest = await host.SendAsync(Request(PlaybookListScope.Owned, string.Empty, "not-a-guid"));

        direct.Should().BeSameAs(PlaybookListResult.UserContextRequired);
        rest.StatusCode.Should().Be((HttpStatusCode)direct.Status);
        host.Playbooks.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "Public: a signed-in principal whose oid is not a GUID is still listed, as the public route always listed it")]
    public async Task ListPlaybooksForCaller_PublicWithANonGuidOid_IsListed_AsOnTheRoute()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServicePage(Guid.NewGuid()));

        var direct = await PlaybookEndpoints.ListPlaybooksForCallerAsync(
            DirectContext(host, Principal("not-a-guid")), PlaybookListScope.Public, cancellationToken: CancellationToken.None);
        using var rest = await host.SendAsync(Request(PlaybookListScope.Public, string.Empty, "not-a-guid"));

        direct.Outcome.Should().Be(PlaybookListOutcome.Listed);
        rest.StatusCode.Should().Be((HttpStatusCode)direct.Status);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the method directly, then sends the same input to the route, over the same substitutes. Asserts that both asked the
    /// playbook service the same questions with the same arguments (the route asks nothing more or less than the method).
    /// </summary>
    private static async Task<(PlaybookListResult Direct, HttpStatusCode Status, JsonNode? Body)> DirectThenRestAsync(
        PlaybookAuthHost host, PlaybookListScope scope, string queryString, Func<HttpContext, Task<PlaybookListResult>> direct)
    {
        var result = await direct(DirectContext(host));
        var directCalls = Questions(host).ToList();

        using var rest = await host.SendAsync(Request(scope, queryString));
        var restCalls = Questions(host).Skip(directCalls.Count).ToList();
        restCalls.Should().Equal(directCalls, "the method and the route ask the playbook service the same questions");

        var body = await rest.Content.ReadAsStringAsync();
        return (result, rest.StatusCode, rest.StatusCode == HttpStatusCode.OK ? JsonNode.Parse(body) : null);
    }

    /// <summary>Each service call as its method name and its non-token arguments (the query is a record, so it compares by value).</summary>
    private static IEnumerable<string> Questions(PlaybookAuthHost host) =>
        host.Playbooks.Invocations.Select(i =>
            i.Method.Name + "(" + string.Join(", ", i.Arguments.Where(a => a is not CancellationToken)) + ")");

    /// <summary>
    /// A request context as a non-HTTP host holds it: the caller's principal and bearer token, and request services carrying the host's
    /// own substitutes (so a seeding made on the host applies to both calls).
    /// </summary>
    private static HttpContext DirectContext(PlaybookAuthHost host, ClaimsPrincipal? principal = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<CallerRecordAccessProbe>(host.Probe);
        services.AddSingleton(host.Playbooks.Object);

        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider().CreateScope().ServiceProvider,
            User = principal ?? Principal(CallerOid),
        };
        http.Request.Headers.Authorization = "Bearer caller-token";
        return http;
    }

    private static ClaimsPrincipal Principal(string oid) =>
        new(new ClaimsIdentity(new[] { new Claim("oid", oid), new Claim("tid", PlaybookAuthzTestAuthHandler.TenantId) }, "Test"));

    private static HttpRequestMessage Request(PlaybookListScope scope, string queryString, string oid = CallerOid)
    {
        var path = scope == PlaybookListScope.Owned ? "/api/ai/playbooks" : "/api/ai/playbooks/public";
        var request = new HttpRequestMessage(HttpMethod.Get, string.IsNullOrEmpty(queryString) ? path : $"{path}?{queryString}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PlaybookAuthzTestAuthHandler.CallerHeader, oid);
        return request;
    }
}
