using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Tests.Contract.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, owner round 20 items 1-3 — the SPE admin plane authorizes PER CONTAINER: every
/// container, item, column, custom-property, permission and recycle-bin route, the container lists and searches, and
/// the app-only container-TYPE routes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> One container TYPE serves several customers (Model 1). Before round 20 a route was bound only to
/// the config the request named and to that config's container type, so a leaf admin of Unit A, holding a config of
/// the shared type, could act on Unit B's containers of the same type. Every container now carries its owning business
/// unit (<see cref="SpeContainerBusinessUnitStamp"/>), and the decision is: a container bound to a unit the caller
/// reaches; an UNBOUND (or malformed) container by NO admin route, root-unit admins included (owner round 35 item 2 —
/// under Model 1 a root admin of any environment whose config names a shared type would otherwise reach another
/// customer's unbound containers), logged with reason <c>unbound</c>; an unreadable binding fails closed.
/// </para>
/// <para>
/// <b>The tenant.</b> Root → {Unit A → {Unit A-sub}, Unit B}. Config A (Unit A) and Config B (Unit B) share container
/// type T — Model 1, allowed since round 20 item 3 — Config N has no unit. The caller is a leaf SPE admin of Unit A
/// unless a test says otherwise. Containers (fake Graph): <c>c-own</c> (A), <c>c-sub</c> (A-sub), <c>c-other</c> (B),
/// <c>c-unbound</c>, <c>c-type</c> (another type, bound to A), <c>c-gone</c> (404), <c>c-fault</c> (read fails).
/// </para>
/// <para>
/// Every request goes through the real BFF pipeline (<see cref="AdminSurfaceHostFixture"/>) and the REAL
/// <c>SpeAdminGraphService</c> against a WireMock Graph (<see cref="GraphWireMockFixture"/>) — so "the handler never
/// ran" is observable twice: Dataverse records only the filter's config read, and Graph records only the binding read.
/// ADR-038 §2 path #1 (security-auth KEEP); ADR-008: the decision is the group filter's.
/// </para>
/// </remarks>
public sealed class SpeAdminPerContainerScopeTests : IClassFixture<AdminSurfaceHostFixture>
{
    private const string ConfigSet = "sprk_specontainertypeconfigs";
    private const string ContainersPath = "/storage/fileStorage/containers";
    private const string DeletedPath = "/storage/fileStorage/deletedContainers";
    private const string ContainerCode = "spe.admin.deny.container_out_of_scope";
    private const string UnverifiableCode = "spe.admin.deny.scope_unverifiable";

    private static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitA = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid UnitASub = Guid.Parse("1a100000-0000-0000-0000-000000000000");
    private static readonly Guid UnitB = Guid.Parse("1b000000-0000-0000-0000-000000000000");

    private static readonly Guid ConfigA = Guid.Parse("ca000000-0000-0000-0000-00000000000a");
    private static readonly Guid ConfigB = Guid.Parse("cb000000-0000-0000-0000-00000000000b");
    private static readonly Guid ConfigN = Guid.Parse("c0000000-0000-0000-0000-00000000000e");

    private const string TypeT = "77777777-0000-0000-0000-000000000077";
    private const string TypeX = "88888888-0000-0000-0000-000000000088";
    private const string TypeN = "99999999-0000-0000-0000-000000000099";

    private readonly AdminSurfaceHostFixture _fixture;

    public SpeAdminPerContainerScopeTests(AdminSurfaceHostFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        SeedTenant(callerUnit: UnitA);
        StubContainers();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The route table — every /api/spe route that names ONE container
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>(method, path with <c>{c}</c> for the container, body kind). <see cref="TheTableCoversEveryContainerRoute"/> pins it.</summary>
    public static TheoryData<string, string, string> ActiveContainerRoutes() => new()
    {
        { "GET",    "/api/spe/containers/{c}", "" },
        { "PATCH",  "/api/spe/containers/{c}", "patch-container" },
        { "POST",   "/api/spe/containers/{c}/activate", "" },
        { "POST",   "/api/spe/containers/{c}/lock", "" },
        { "POST",   "/api/spe/containers/{c}/unlock", "" },
        { "POST",   "/api/spe/containers/{c}/archive", "" },
        { "POST",   "/api/spe/containers/{c}/unarchive", "" },
        { "GET",    "/api/spe/containers/{c}/items", "" },
        { "GET",    "/api/spe/containers/{c}/items/i1/versions", "" },
        { "GET",    "/api/spe/containers/{c}/items/i1/thumbnails", "" },
        { "POST",   "/api/spe/containers/{c}/items/i1/share", "share" },
        { "GET",    "/api/spe/containers/{c}/items/i1/content", "" },
        { "GET",    "/api/spe/containers/{c}/items/i1/preview", "" },
        { "DELETE", "/api/spe/containers/{c}/items/i1", "" },
        { "POST",   "/api/spe/containers/{c}/folders", "folder" },
        { "POST",   "/api/spe/containers/{c}/items/upload", "upload" },
        { "GET",    "/api/spe/containers/{c}/columns", "" },
        { "POST",   "/api/spe/containers/{c}/columns", "column" },
        { "PATCH",  "/api/spe/containers/{c}/columns/col1", "patch-column" },
        { "DELETE", "/api/spe/containers/{c}/columns/col1", "" },
        { "GET",    "/api/spe/containers/{c}/customproperties", "" },
        { "PUT",    "/api/spe/containers/{c}/customproperties", "properties" },
        { "GET",    "/api/spe/containers/{c}/permissions", "" },
        { "POST",   "/api/spe/containers/{c}/permissions", "grant" },
        { "PATCH",  "/api/spe/containers/{c}/permissions/p1", "role" },
        { "DELETE", "/api/spe/containers/{c}/permissions/p1", "" },
        { "GET",    "/api/spe/containers/{c}/recyclebin/items", "" },
        { "POST",   "/api/spe/containers/{c}/recyclebin/items/restore", "ids" },
        { "POST",   "/api/spe/containers/{c}/recyclebin/items/delete", "ids" },
    };

    /// <summary>The routes that act on a container in the RECYCLE BIN.</summary>
    public static TheoryData<string, string, string> RecycleBinContainerRoutes() => new()
    {
        { "POST",   "/api/spe/recyclebin/{c}/restore", "" },
        { "DELETE", "/api/spe/recyclebin/{c}", "" },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Another customer's container of the SAME type — the round 20 defect
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ActiveContainerRoutes))]
    public async Task ALeafAdmin_OnAnotherCustomersContainerOfTheSharedType_GetsTheUniform404_AndTheHandlerNeverRuns(
        string method, string path, string body)
    {
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(Request(method, path, "c-other", body)));

        AssertUniformContainerNotFound(problem, "c-other", recycleBin: false);
        HandlerConfigReads().Should().Be(1, "only the filter resolved the config — the handler never ran");
        _fixture.Graph.AllRequests.Should().ContainSingle("only the container's binding was read; nothing was acted on")
            .Which.RawQuery.Should().Contain("customProperties");
    }

    [Theory]
    [MemberData(nameof(RecycleBinContainerRoutes))]
    public async Task ALeafAdmin_OnAnotherCustomersDeletedContainer_GetsTheUniform404_AndTheHandlerNeverRuns(
        string method, string path, string body)
    {
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(Request(method, path, "c-other", body)));

        AssertUniformContainerNotFound(problem, "c-other", recycleBin: true);
        HandlerConfigReads().Should().Be(1);
        _fixture.Graph.AllRequests.Should().ContainSingle("the binding is read from the recycle bin, and nothing else")
            .Which.Path.Should().StartWith($"{DeletedPath}/c-other");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The caller's own subtree — served
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ActiveContainerRoutes))]
    public async Task ALeafAdmin_OnItsOwnUnitsContainer_ReachesTheHandler(string method, string path, string body)
    {
        using var client = Admin();

        var response = await client.SendAsync(Request(method, path, "c-own", body));

        await AssertReachedTheHandler(response);
    }

    [Theory]
    [MemberData(nameof(ActiveContainerRoutes))]
    public async Task ALeafAdmin_OnADescendantUnitsContainer_ReachesTheHandler(string method, string path, string body)
    {
        using var client = Admin();

        var response = await client.SendAsync(Request(method, path, "c-sub", body));

        await AssertReachedTheHandler(response);
    }

    [Theory]
    [MemberData(nameof(RecycleBinContainerRoutes))]
    public async Task ALeafAdmin_OnItsOwnDeletedContainer_ReachesTheHandler(string method, string path, string body)
    {
        using var client = Admin();

        var response = await client.SendAsync(Request(method, path, "c-own", body));

        await AssertReachedTheHandler(response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Unbound containers: reached by NO admin route (owner round 35 item 2 — amends round 20's "root only")
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ActiveContainerRoutes))]
    public async Task ALeafAdmin_OnAnUnboundContainer_GetsTheUniform404(string method, string path, string body)
    {
        using var client = Admin();

        AssertUniformContainerNotFound(
            await Problem(await client.SendAsync(Request(method, path, "c-unbound", body))), "c-unbound", recycleBin: false);
        HandlerConfigReads().Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(ActiveContainerRoutes))]
    public async Task ARootAdmin_OnAnUnboundContainer_GetsTheUniform404_AndTheHandlerNeverRuns(string method, string path, string body)
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(Request(method, path, "c-unbound", body)));

        AssertUniformContainerNotFound(problem, "c-unbound", recycleBin: false);
        HandlerConfigReads().Should().Be(1, "only the filter resolved the config — no admin route reaches an unbound container");
        _fixture.Graph.AllRequests.Should().ContainSingle("only the binding was read; nothing was acted on")
            .Which.RawQuery.Should().Contain("customProperties");
    }

    [Theory]
    [MemberData(nameof(RecycleBinContainerRoutes))]
    public async Task ARootAdmin_OnAnUnboundDeletedContainer_GetsTheUniform404(string method, string path, string body)
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        StubDeleted("c-unbound", null);
        using var client = Admin();

        AssertUniformContainerNotFound(
            await Problem(await client.SendAsync(Request(method, path, "c-unbound", body))), "c-unbound", recycleBin: true);
        HandlerConfigReads().Should().Be(1);
    }

    [Fact]
    public async Task AnUnboundContainer_IsRefusedWithReasonUnbound_InTheLog_WhileTheCallerSeesTheUniform404()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        using var client = Admin();

        var unbound = await Problem(await client.GetAsync(Url("/api/spe/containers/{c}", "c-unbound")));
        var foreign = await Problem(await client.GetAsync(Url("/api/spe/containers/{c}", "c-gone")));

        unbound.Keys.Should().BeEquivalentTo(foreign.Keys, "the caller cannot tell an unbound container from an absent one");
        _fixture.Logs.Lines.Should().Contain(l =>
            l.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && l.Message.Contains("c-unbound", StringComparison.Ordinal)
            && l.Message.Contains("reason unbound", StringComparison.Ordinal),
            "round 35 item 2: the refusal is logged with reason 'unbound'");
        _fixture.Logs.Lines.Should().Contain(l =>
            l.Message.Contains("c-gone", StringComparison.Ordinal) && l.Message.Contains("reason absent", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1a000000-0000-0000-0000-000000000000")]   // a leaf admin of Unit A
    [InlineData("10000000-0000-0000-0000-000000000000")]   // a root-unit admin
    public async Task AContainerWithAMalformedStamp_IsTheUniform404_ForEveryCaller(string callerUnit)
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Guid.Parse(callerUnit));
        StubContainers();
        StubContainer("c-malformed", TypeT, "not-a-business-unit");
        using var client = Admin();

        AssertUniformContainerNotFound(
            await Problem(await client.GetAsync(Url("/api/spe/containers/{c}", "c-malformed"))), "c-malformed", recycleBin: false);
        _fixture.Logs.Lines.Should().Contain(l =>
            l.Message.Contains("c-malformed", StringComparison.Ordinal) && l.Message.Contains("reason malformed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARootAdmin_OnAContainerBoundToAUnitThisEnvironmentDoesNotKnow_GetsTheUniform404()
    {
        // Model 1 shares a consuming tenant: another environment's container carries ITS business unit.
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        StubContainer("c-foreign", TypeT, Guid.Parse("f0000000-0000-0000-0000-0000000000ff").ToString());
        using var client = Admin();

        AssertUniformContainerNotFound(
            await Problem(await client.GetAsync(Url("/api/spe/containers/{c}", "c-foreign"))), "c-foreign", recycleBin: false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Not found / another type / unreadable — no oracle, and fail closed
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AContainerThatDoesNotExist_AndOneOfAnotherType_AnswerExactlyLikeAnotherCustomersContainer()
    {
        using var client = Admin();

        var outOfScope = await Problem(await client.GetAsync(Url("/api/spe/containers/{c}/items", "c-other")));
        var missing = await Problem(await client.GetAsync(Url("/api/spe/containers/{c}/items", "c-gone")));
        var otherType = await Problem(await client.GetAsync(Url("/api/spe/containers/{c}/items", "c-type")));

        AssertUniformContainerNotFound(missing, "c-gone", recycleBin: false);
        AssertUniformContainerNotFound(otherType, "c-type", recycleBin: false);
        missing.Keys.Should().BeEquivalentTo(outOfScope.Keys);
        otherType.Keys.Should().BeEquivalentTo(outOfScope.Keys);
    }

    [Theory]
    [MemberData(nameof(ActiveContainerRoutes))]
    public async Task WhenTheContainersBindingCannotBeRead_EveryRouteIs503_AndNothingIsActedOn(
        string method, string path, string body)
    {
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(Request(method, path, "c-fault", body)), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        HandlerConfigReads().Should().Be(1);
        _fixture.Graph.AllRequests.Should().OnlyContain(r => r.Method == "GET" && r.RawQuery.Contains("customProperties"),
            "a binding that cannot be read never falls through to the action (ADR-003)");
    }

    [Fact]
    public async Task WhenTheCallersScopeCannotBeRead_AContainerRouteIs503_AndNoGraphCallIsMade()
    {
        // Through the UNIT-LESS Config N: the configId rule admits it without reading the hierarchy (the compatibility
        // rule), so the 503 can only come from the per-container rule's own read of the caller's scope.
        _fixture.Dataverse.FaultQueriesOn("businessunits");
        using var client = Admin();

        var problem = await Problem(
            await client.GetAsync($"/api/spe/containers/c-own/items?configId={ConfigN}"), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET", "/api/spe/containers/c-own")]
    [InlineData("GET", "/api/spe/containers/c-own/permissions")]
    [InlineData("POST", "/api/spe/recyclebin/c-own/restore")]
    public async Task AContainerRouteWithoutAConfigId_IsTheHandlers400_AndNoContainerIsReadOrActedOn(string method, string url)
    {
        using var client = Admin();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Graph.AllRequests.Should().BeEmpty("without a config no Graph client exists — no container can be reached");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The handlers' own not-found paths give the SAME answer (the race: deleted after the filter read it)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/spe/containers/{c}")]
    [InlineData("GET", "/api/spe/containers/{c}/customproperties")]
    [InlineData("PATCH", "/api/spe/containers/{c}")]
    [InlineData("POST", "/api/spe/containers/{c}/activate")]
    [InlineData("POST", "/api/spe/containers/{c}/archive")]
    public async Task AContainerGoneBetweenTheFilterAndTheHandler_GetsTheFiltersExact404(string method, string path)
    {
        // First GET (the filter's binding read) finds it; the handler's own call then gets Graph's 404.
        const string Gone = """{"error":{"code":"itemNotFound","message":"gone"}}""";
        _fixture.Graph.StubGetSequence($"{ContainersPath}/c-race", (ContainerJson("c-race", TypeT, UnitA.ToString()), 200), (Gone, 404));
        _fixture.Graph.StubPatch($"{ContainersPath}/c-race", Gone, 404);
        _fixture.Graph.StubPost($"{ContainersPath}/c-race", Gone, 404);
        using var client = Admin();

        var fromHandler = await Problem(await client.SendAsync(Request(method, path, "c-race", method == "PATCH" ? "patch-container" : "")));
        HandlerConfigReads().Should().BeGreaterThanOrEqualTo(2, "the handler ran — this is its own not-found path");
        var fromFilter = await Problem(await client.GetAsync(Url("/api/spe/containers/{c}", "c-other")));

        AssertUniformContainerNotFound(fromHandler, "c-race", recycleBin: false);
        fromHandler.Keys.Should().BeEquivalentTo(fromFilter.Keys, "the handler's 404 must be the filter's, key for key");
    }

    [Fact]
    public async Task ADeletedContainerGoneBetweenTheFilterAndTheRestore_GetsTheFiltersExact404()
    {
        _fixture.Graph.StubGet($"{DeletedPath}/c-race", ContainerJson("c-race", TypeT, UnitA.ToString()));
        _fixture.Graph.StubPost($"{DeletedPath}/c-race", """{"error":{"code":"itemNotFound","message":"gone"}}""", 404);
        using var client = Admin();

        var fromHandler = await Problem(await client.PostAsync(Url("/api/spe/recyclebin/{c}/restore", "c-race"), null));
        HandlerConfigReads().Should().BeGreaterThanOrEqualTo(2);
        var fromFilter = await Problem(await client.PostAsync(Url("/api/spe/recyclebin/{c}/restore", "c-other"), null));
        AssertUniformContainerNotFound(fromHandler, "c-race", recycleBin: true);
        fromHandler.Keys.Should().BeEquivalentTo(fromFilter.Keys);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A body that names a container (search items) is judged the same way
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchItems_ScopedToAnotherCustomersContainer_IsTheUniform404_AndNoSearchRuns()
    {
        using var client = Admin();

        var problem = await Problem(await client.PostAsJsonAsync(
            $"/api/spe/search/items?configId={ConfigA}", new { query = "contract", containerId = "c-other" }));

        AssertUniformContainerNotFound(problem, "c-other", recycleBin: false);
        _fixture.Graph.RequestsFor("/search").Should().BeEmpty();
    }

    /// <summary>
    /// A route value and a bound body that name DIFFERENT containers are refused before any container is read — the
    /// filter must never authorize one container and let the handler act on another. No shipped route carries both
    /// today, so the filter is driven directly (the config, scope and Graph reads are the host's real ones).
    /// </summary>
    [Fact]
    public async Task ARequestWhoseRouteAndBodyNameDifferentContainers_Is400_BeforeAnyContainerIsRead()
    {
        using var scope = _fixture.Services.CreateScope();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("oid", AdminSurfaceHostFixture.CallerOid.ToString()) }, "test"));
        http.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString($"?configId={ConfigA}");
        http.Request.RouteValues["containerId"] = "c-own";
        var body = new Sprk.Bff.Api.Api.SpeAdmin.SearchItemsEndpoints.SearchItemsRequest("q", "c-other", null, null, null);

        var filter = new Sprk.Bff.Api.Api.Filters.SpeAdminTenantScopeFilter(
            scope.ServiceProvider.GetRequiredService<SpeAdminTenantScope>());
        var result = await filter.InvokeAsync(
            Microsoft.AspNetCore.Http.EndpointFilterInvocationContext.Create(http, body),
            _ => ValueTask.FromResult<object?>("PASSED-THROUGH"));

        var problem = result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .Which.ProblemDetails.Extensions["errorCode"].Should().Be("spe.admin.deny.container_id_ambiguous");
        _fixture.Graph.AllRequests.Should().BeEmpty("neither container was read");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Lists and searches are trimmed to the containers the caller reaches
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ContainerList_ForALeafAdmin_HoldsOnlyItsSubtreesContainers()
    {
        StubContainerList("c-own", "c-sub", "c-other", "c-unbound");
        using var client = Admin();

        var body = await Json(await client.GetAsync($"/api/spe/containers?configId={ConfigA}"));

        Ids(body.GetProperty("items")).Should().BeEquivalentTo("c-own", "c-sub");
        body.GetProperty("count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ContainerList_ForARootAdmin_HoldsEveryBoundContainer_ButNoUnboundOne()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        StubContainerList("c-own", "c-other", "c-unbound");
        using var client = Admin();

        var body = await Json(await client.GetAsync($"/api/spe/containers?configId={ConfigA}"));

        Ids(body.GetProperty("items")).Should().BeEquivalentTo(new[] { "c-own", "c-other" },
            "round 35 item 2: an unbound container is listed for nobody — it must be bound (backfill -Bind) first");
    }

    [Fact]
    public async Task ContainerList_WhenOneBindingCannotBeRead_Is503_NeverTheUntrimmedList()
    {
        StubContainerList("c-own", "c-fault");
        using var client = Admin();

        var problem = await Problem(
            await client.GetAsync($"/api/spe/containers?configId={ConfigA}"), HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
    }

    [Fact]
    public async Task RecycleBinList_ForALeafAdmin_HoldsOnlyItsOwnDeletedContainers_ReadFromTheRecycleBin()
    {
        _fixture.Graph.StubGetExact(DeletedPath, DeletedListJson("c-own", "c-other"));
        StubDeleted("c-own", UnitA.ToString());
        StubDeleted("c-other", UnitB.ToString());
        using var client = Admin();

        var body = await Json(await client.GetAsync($"/api/spe/recyclebin?configId={ConfigA}"));

        Ids(body.GetProperty("items")).Should().BeEquivalentTo("c-own");
    }

    [Fact]
    public async Task SearchContainers_ForALeafAdmin_DropsOtherCustomersHits_AndReportsNoTotal()
    {
        // Container search filters the type's containers collection by name — in Model 1 other customers' too.
        StubContainerList("c-own", "c-other");
        using var client = Admin();

        var body = await Json(await client.PostAsJsonAsync($"/api/spe/search/containers?configId={ConfigA}", new { query = "x" }));

        Ids(body.GetProperty("items")).Should().BeEquivalentTo("c-own");
        body.GetProperty("totalCount").ValueKind.Should().Be(JsonValueKind.Null,
            "a total that counted other customers' containers must never be reported");
    }

    [Fact]
    public async Task SearchItems_ForALeafAdmin_DropsHitsInOtherCustomersContainers()
    {
        _fixture.Graph.StubPost("/search/query", SearchJson(
            ("driveItem", "item-own", "c-own"), ("driveItem", "item-other", "c-other")));
        using var client = Admin();

        var body = await Json(await client.PostAsJsonAsync($"/api/spe/search/items?configId={ConfigA}", new { query = "x" }));

        Ids(body.GetProperty("items")).Should().BeEquivalentTo("item-own");
        body.GetProperty("totalCount").GetInt32().Should().Be(1, "Graph's total counts the other customer's hit");
    }

    [Fact]
    public async Task SearchItems_AHitThatNamesNoContainer_IsDropped_EvenForARootAdmin()
    {
        // An unscoped hit whose container Graph does not report (no containerId, no parentReference.driveId) cannot be
        // judged by any binding — like an unbound container, it is shown to nobody (round 20 item 2; round 35 item 2).
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        _fixture.Graph.StubPost("/search/query", SearchJsonRaw(total: 2, moreResultsAvailable: false,
            DriveItemHit("item-own", "c-own"), DriveItemHit("item-orphan", driveId: null)));
        using var client = Admin();

        var body = await Json(await client.PostAsJsonAsync($"/api/spe/search/items?configId={ConfigA}", new { query = "x" }));

        Ids(body.GetProperty("items")).Should().BeEquivalentTo(new[] { "item-own" },
            "a hit attributable to no container is never shown");
        body.GetProperty("totalCount").GetInt32().Should().Be(1, "a page from which a hit was removed never reports Graph's total");
    }

    [Theory]
    [InlineData(false, 7)]   // the result is complete: Graph's total is the operator's to see
    [InlineData(true, 1)]    // a further page exists that nobody here judged: never Graph's total
    public async Task SearchItems_ForARootAdmin_ReportsGraphsTotal_OnlyWhenThereIsNoFurtherPage(bool moreResults, int expectedTotal)
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        _fixture.Graph.StubPost("/search/query", SearchJsonRaw(total: 7, moreResultsAvailable: moreResults,
            DriveItemHit("item-own", "c-own")));
        using var client = Admin();

        var body = await Json(await client.PostAsJsonAsync($"/api/spe/search/items?configId={ConfigA}", new { query = "x" }));

        Ids(body.GetProperty("items")).Should().BeEquivalentTo("item-own");
        body.GetProperty("totalCount").GetInt32().Should().Be(expectedTotal,
            "round 35 item 2: a later page may hold hits in unbound or another environment's containers, which no admin reaches");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The stamp is server-owned
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("spaarkeBusinessUnitId")]
    [InlineData(" SPAARKEBUSINESSUNITID ")]
    public async Task CustomProperties_ThatWouldWriteTheStamp_Are403_AndNothingIsPatched(string name)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PutAsJsonAsync(
                Url("/api/spe/containers/{c}/customproperties", "c-own"),
                new { properties = new[] { new { name, value = UnitB.ToString(), isSearchable = false } } }),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be("spe.admin.deny.container_binding_server_owned");
        _fixture.Graph.AllRequests.Should().NotContain(r => r.Method == "PATCH");
    }

    [Fact]
    public async Task CustomProperties_StampingAnUnboundContainer_IsTheUniform404_ForARootAdminToo_AndNothingIsPatched()
    {
        // Round 35 item 2: no admin route reaches an unbound container at all, so the filter refuses it before the
        // handler's own server-owned-stamp rule is even asked. Binding is the creation path's and the backfill's job.
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        using var client = Admin();

        var problem = await Problem(
            await client.PutAsJsonAsync(
                Url("/api/spe/containers/{c}/customproperties", "c-unbound"),
                new { properties = new[] { new { name = SpeContainerBusinessUnitStamp.PropertyName, value = UnitA.ToString(), isSearchable = false } } }));

        AssertUniformContainerNotFound(problem, "c-unbound", recycleBin: false);
        _fixture.Graph.AllRequests.Should().NotContain(r => r.Method == "PATCH");
    }

    [Fact]
    public async Task CustomProperties_TheEditorsFullMapWithTheUnchangedStamp_IsSaved_WithoutRewritingTheStamp()
    {
        // The shipped CustomPropertyEditor re-sends every property it loaded — the stamp included.
        _fixture.Graph.StubPatch($"{ContainersPath}/c-own/customProperties", "{}");
        using var client = Admin();

        var response = await client.PutAsJsonAsync(
            Url("/api/spe/containers/{c}/customproperties", "c-own"),
            new
            {
                properties = new[]
                {
                    new { name = SpeContainerBusinessUnitStamp.PropertyName, value = UnitA.ToString("N"), isSearchable = false },
                    new { name = "Region", value = "EU", isSearchable = true },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var patch = _fixture.Graph.PatchRequestsFor($"{ContainersPath}/c-own/customProperties").Should().ContainSingle().Subject;
        using var body = JsonDocument.Parse(patch.Body!);
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(new[] { "Region" },
            "the stamp is never written by this route — an unchanged one is simply left in place");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Container-role grant markers are server-owned (task 171, adversarial finding 7)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SprkStd0123456789abcdef0123456789abcdef")]
    [InlineData("SprkJit0123456789abcdef0123456789abcdef")]
    [InlineData(" sprkjitANYTHING ")]
    public async Task CustomProperties_ThatWouldWriteAGrantMarker_Are403_AndNothingIsPatched(string name)
    {
        using var client = Admin();

        var problem = await Problem(
            await client.PutAsJsonAsync(
                Url("/api/spe/containers/{c}/customproperties", "c-own"),
                new { properties = new[] { new { name, value = "perm-forged", isSearchable = false } } }),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be("spe.admin.deny.container_grant_marker_server_owned",
            "a forged marker would make a hand-granted role removable by the sync, and a deleted one would strand a grant");
        _fixture.Graph.AllRequests.Should().NotContain(r => r.Method == "PATCH");
    }

    [Fact]
    public async Task CustomProperties_GrantMarkers_AreNeverShown()
    {
        var properties =
            "{\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + UnitA + "\",\"isSearchable\":false},"
            + "\"SprkStd0123456789abcdef0123456789abcdef\":{\"value\":\"perm-1\",\"isSearchable\":false},"
            + "\"SprkJitfedcba9876543210fedcba9876543210\":{\"value\":\"perm-2\",\"isSearchable\":false},"
            + "\"Region\":{\"value\":\"EU\",\"isSearchable\":true}}";
        _fixture.Graph.StubGet($"{ContainersPath}/c-marked",
            "{\"id\":\"c-marked\",\"displayName\":\"Marked\",\"containerTypeId\":\"" + TypeT
            + "\",\"status\":\"active\",\"customProperties\":" + properties + "}");
        using var client = Admin();

        var response = await client.GetAsync(Url("/api/spe/containers/{c}/customproperties", "c-marked"));

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        raw.Should().Contain("Region");
        raw.Should().NotContain("SprkStd", "the grant markers are server-owned and never shown");
        raw.Should().NotContain("SprkJit");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Containers are stamped at creation (owner round 20 item 1)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ca000000-0000-0000-0000-00000000000a", "1a000000-0000-0000-0000-000000000000")]   // Config A → its unit
    [InlineData("c0000000-0000-0000-0000-00000000000e", "1a000000-0000-0000-0000-000000000000")]   // unit-less Config N → the creator's unit
    public async Task CreateContainer_StampsTheOwningBusinessUnit_AndReadsItBack(string configId, string expectedUnit)
    {
        StubCreate("c-new", TypeT);
        _fixture.Graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _fixture.Graph.StubGet($"{ContainersPath}/c-new", ContainerJson("c-new", TypeT, expectedUnit));
        using var client = Admin();

        var response = await client.PostAsJsonAsync($"/api/spe/containers?configId={configId}", new { displayName = "New" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var patch = _fixture.Graph.PatchRequestsFor($"{ContainersPath}/c-new/customProperties").Should().ContainSingle().Subject;
        using var stamp = JsonDocument.Parse(patch.Body!);
        stamp.RootElement.GetProperty(SpeContainerBusinessUnitStamp.PropertyName).GetProperty("value").GetString()
            .Should().Be(expectedUnit);
        _fixture.Graph.AllRequests.Should().NotContain(r => r.Method == "DELETE");
    }

    [Theory]
    [InlineData(500, false)]   // the stamp write fails
    [InlineData(200, true)]    // the write "succeeds" but the stamp does not read back
    public async Task CreateContainer_WhoseStampCannotBeWritten_IsRemovedAgain_And503(int patchStatus, bool readBackWithoutStamp)
    {
        StubCreate("c-new", TypeT);
        _fixture.Graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}", patchStatus);
        _fixture.Graph.StubGet($"{ContainersPath}/c-new", ContainerJson("c-new", TypeT, readBackWithoutStamp ? null : UnitA.ToString()));
        _fixture.Graph.StubDelete($"{ContainersPath}/c-new");
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync($"/api/spe/containers?configId={ConfigA}", new { displayName = "New" }),
            HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be("spe.containers.business_unit_stamp_failed");
        _fixture.Graph.RequestsFor($"{ContainersPath}/c-new").Should().Contain(r => r.Method == "DELETE",
            "an unbound container is never left active");
    }

    [Fact]
    public async Task CreateContainer_WhoseOwnerCannotBeDetermined_Is403_AndCreatesNothing()
    {
        // An unresolvable caller passes the filter for a unit-less config (the compatibility rule) — but no owner exists.
        _fixture.Reset();
        SeedTenant(callerUnit: null);
        StubContainers();
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync($"/api/spe/containers?configId={ConfigN}", new { displayName = "New" }),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be("spe.admin.deny.container_owner_unresolved");
        _fixture.Graph.AllRequests.Should().NotContain(r => r.Method == "POST");
    }

    [Fact]
    public async Task CreateContainer_WhoseOwnerCannotBeRead_Is503_AndCreatesNothing()
    {
        // A unit-less config passes the filter without the hierarchy; the owner is then the creator's unit, whose read
        // faults. Refuse — never create a container the owner of which was not established.
        _fixture.Dataverse.FaultQueriesOn("businessunits");
        using var client = Admin();

        var problem = await Problem(
            await client.PostAsJsonAsync($"/api/spe/containers?configId={ConfigN}", new { displayName = "New" }),
            HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Graph.AllRequests.Should().BeEmpty("no container is created, so nothing is stamped or removed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // App-only container-TYPE routes (round 20 item 3's consequence)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/spe/containertypes/{t}/permissions")]
    [InlineData("GET", "/api/spe/containertypes/{t}/consumers")]
    [InlineData("POST", "/api/spe/containertypes/{t}/consumers")]
    [InlineData("PUT", "/api/spe/containertypes/{t}/consumers/app1")]
    [InlineData("DELETE", "/api/spe/containertypes/{t}/consumers/app1")]
    [InlineData("POST", "/api/spe/containertypes/{t}/register")]
    public async Task ATypeRouteNamingATypeThatIsNotTheConfigsOwn_IsTheUniformTypeNotFound(string method, string path)
    {
        // Judged for the only caller the type-wide routes admit — a root admin of a Spaarke-operated environment (owner
        // round 49 item 1): the route's type must still be the config's own.
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(TypeRequest(method, path, TypeX, ConfigA)));

        problem["errorCode"].GetString().Should().Be("spe.admin.deny.container_type_out_of_scope");
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("POST", "/api/spe/containertypes/{t}/consumers")]
    [InlineData("PUT", "/api/spe/containertypes/{t}/consumers/app1")]
    [InlineData("DELETE", "/api/spe/containertypes/{t}/consumers/app1")]
    [InlineData("POST", "/api/spe/containertypes/{t}/register")]
    public async Task ALeafAdmin_WritingAContainerTypeSharedWithAnotherCustomer_Is403_AndNothingIsSent(string method, string path)
    {
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(TypeRequest(method, path, TypeT, ConfigA)), HttpStatusCode.Forbidden);

        // Owner round 49 item 1: a leaf admin is refused by the operator rule, before the type is even looked at.
        problem["reasonCode"].GetString().Should().Be("spe.admin.deny.platform_operator_required");
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET", "/api/spe/containertypes/{t}/permissions")]
    [InlineData("GET", "/api/spe/containertypes/{t}/consumers")]
    public async Task ALeafAdmin_ReadingAContainerTypeSharedWithAnotherCustomer_Is403_AndNothingIsSent(string method, string path)
    {
        // Round 35 item 5: these lists name every customer's consuming app and registrations — the per-customer values
        // round 20 item 3 keeps exclusive — so a read needs what a write needs: every config of the type reachable.
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(TypeRequest(method, path, TypeT, ConfigA)), HttpStatusCode.Forbidden);

        // Owner round 49 item 1: a leaf admin is refused by the operator rule, before the type is even looked at.
        problem["reasonCode"].GetString().Should().Be("spe.admin.deny.platform_operator_required");
        HandlerConfigReads().Should().Be(0, "the handler never ran");
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET", "/api/spe/containertypes/{t}/permissions")]
    [InlineData("POST", "/api/spe/containertypes/{t}/consumers")]
    public async Task ARootAdmin_OnATypeAConfigOutsideTheHierarchyAlsoCarries_IsStill403Shared(string method, string path)
    {
        // Round 35 item 5's every-config rule keeps its own reach for the root admin: a config whose business unit is not
        // in this environment's hierarchy (a removed or foreign unit) is one even the root does not reach.
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        _fixture.Dataverse.Add(ConfigSet, ConfigRow(Guid.Parse("cf000000-0000-0000-0000-0000000000f0"),
            Guid.Parse("1f000000-0000-0000-0000-0000000000ff"), TypeT));
        StubContainers();
        using var client = Admin();

        var problem = await Problem(await client.SendAsync(TypeRequest(method, path, TypeT, ConfigA)), HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be("spe.admin.deny.container_type_shared");
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    public static TheoryData<string, string, string> AdminsWhoReachEveryConfigOfTheType() => new()
    {
        // (caller unit, Config B's type, why) — owner round 49 item 1: only a root admin of a Spaarke-operated environment
        // reaches the type-wide routes at all.
        { "10000000-0000-0000-0000-000000000000", TypeT, "a root-unit admin reaches both configs of the shared type" },
        { "10000000-0000-0000-0000-000000000000", TypeX, "a root-unit admin reaches the type only Unit A's config carries" },
    };

    [Theory]
    [MemberData(nameof(AdminsWhoReachEveryConfigOfTheType))]
    public async Task AnAdminWhoReachesEveryConfigOfTheType_ReadsItsPermissionsAndConsumers(string callerUnit, string configBType, string why)
    {
        foreach (var path in new[] { "/api/spe/containertypes/{t}/permissions", "/api/spe/containertypes/{t}/consumers" })
        {
            _fixture.Reset();
            SeedTenant(callerUnit: Guid.Parse(callerUnit), configBType: configBType);
            StubContainers();
            using var client = Admin();

            var response = await client.SendAsync(TypeRequest("GET", path, TypeT, ConfigA));

            (await response.Content.ReadAsStringAsync()).Should().NotContain("spe.admin.deny.container_type", why);
            HandlerConfigReads().Should().BeGreaterThanOrEqualTo(1, "the handler resolved its config — " + why);
        }
    }

    [Fact]
    public async Task ARootAdmin_WritingASharedContainerType_PassesTheTypeRule()
    {
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        using var client = Admin();

        var response = await client.SendAsync(TypeRequest("POST", "/api/spe/containertypes/{t}/consumers", TypeT, ConfigA));

        (await response.Content.ReadAsStringAsync()).Should().NotContain("spe.admin.deny.container_type");
    }

    [Fact]
    public async Task ALeafAdmin_EvenOnATypeOnlyItsOwnConfigsCarry_IsRefused_TypeWideRoutesAreForTheOperatorsRootAdmin()
    {
        // Config B moves to its own type: Unit A's type T is no longer shared — and a leaf admin is still refused (owner
        // round 49 item 1 supersedes round 35 item 5's leaf-admin path: type-wide routes are platform-operator routes).
        _fixture.Reset();
        SeedTenant(callerUnit: UnitA, configBType: TypeX);
        StubContainers();
        using var client = Admin();

        var problem = await Problem(
            await client.SendAsync(TypeRequest("POST", "/api/spe/containertypes/{t}/consumers", TypeT, ConfigA)),
            HttpStatusCode.Forbidden);

        problem["reasonCode"].GetString().Should().Be("spe.admin.deny.platform_operator_required");
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET", "/api/spe/containertypes/{t}/consumers")]
    [InlineData("POST", "/api/spe/containertypes/{t}/consumers")]
    public async Task ATypeRoute_WhenTheScopeCannotBeRead_Is503_AndNothingIsSent(string method, string path)
    {
        // Through the UNIT-LESS Config N. The operator rule (owner round 49 item 1) reads the caller's scope first, so the
        // fault is met there — the answer is the same 503, refused before anything is sent.
        _fixture.Dataverse.FaultQueriesOn("businessunits");
        using var client = Admin();

        var problem = await Problem(
            await client.SendAsync(TypeRequest(method, path, TypeN, ConfigN)),
            HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET", "/api/spe/containertypes/{t}/consumers")]
    [InlineData("POST", "/api/spe/containertypes/{t}/consumers")]
    public async Task ATypeRoute_WhenTheTypeRulesOwnConfigTableReadFaults_Is503_AndNothingIsSent(string method, string path)
    {
        // Owner round 57 item 3: the test above meets its fault in the operator rule. This one reaches the container-type
        // rule's OWN read fault: a root admin of the Spaarke-operated host passes the operator rule and the configId rule
        // (both read filtered rows only); the type rule alone reads the WHOLE config table, and that read faults.
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        StubContainers();
        _fixture.Dataverse.FaultUnfilteredQueriesOn(ConfigSet);
        using var client = Admin();

        var problem = await Problem(
            await client.SendAsync(TypeRequest(method, path, TypeT, ConfigA)),
            HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Logs.Lines.Should().Contain(l =>
            l.Message.Contains("could not read the scope for a container-type route", StringComparison.Ordinal),
            "the container-type rule's own fault path answered, not an earlier rule");
        HandlerConfigReads().Should().Be(0, "the handler never ran");
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET", "/api/spe/containertypes/{t}/permissions")]
    [InlineData("POST", "/api/spe/containertypes/{t}/consumers")]
    public async Task ATypeRoute_WhenTheConfigTableReadIsAFullPage_Is503_NeverJudgedOnATruncatedTable(string method, string path)
    {
        // Owner round 57 item 3: the container-type rule judges "every config of the type is reachable" over the WHOLE table.
        // A read that returns the read limit (5000 rows) may be truncated. Here the row the cap drops is exactly the one
        // that must refuse: a config of type T whose business unit is outside this environment's hierarchy. Judged on the
        // 5000 rows it sees, the root admin would reach every config of T and act on a type another customer also carries.
        _fixture.Reset();
        SeedTenant(callerUnit: Root);
        for (var i = 3; i < SpeAdminTenantScope.WholeTableReadLimit; i++)
        {
            _fixture.Dataverse.Add(ConfigSet, ConfigRow(Guid.Parse($"f0000000-0000-0000-0000-{i:D12}"), null, TypeX));
        }

        _fixture.Dataverse.Add(ConfigSet, ConfigRow(Guid.Parse("cf000000-0000-0000-0000-0000000000f0"),
            Guid.Parse("1f000000-0000-0000-0000-0000000000ff"), TypeT));
        StubContainers();
        using var client = Admin();

        var problem = await Problem(
            await client.SendAsync(TypeRequest(method, path, TypeT, ConfigA)),
            HttpStatusCode.ServiceUnavailable);

        problem["errorCode"].GetString().Should().Be(UnverifiableCode);
        _fixture.Logs.Lines.Should().Contain(l =>
            l.Message.Contains("rows (the read limit)", StringComparison.Ordinal)
            && l.Message.Contains("container-type route", StringComparison.Ordinal),
            "the container-type rule's own full-page refusal answered");
        HandlerConfigReads().Should().Be(0, "the handler never ran");
        _fixture.Graph.AllRequests.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Census — every container route is in the table; none spells the container any other way
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheTableCoversEveryContainerRoute_AndNoRouteNamesAContainerOtherThanAsContainerId()
    {
        var patterns = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/spe/", StringComparison.Ordinal) == true)
            .Select(e => (Method: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "", Raw: e.RoutePattern.RawText!))
            .ToList();

        patterns.Should().NotBeEmpty();

        // A container named by any other parameter would carry only the config check.
        patterns.Where(p => System.Text.RegularExpressions.Regex.IsMatch(p.Raw, @"/(containers|recyclebin)/\{(?!containerId[}:])"))
            .Should().BeEmpty("every /api/spe route that names a container must name it {containerId}");

        var routed = patterns
            .Where(p => p.Raw.Contains("{containerId}", StringComparison.Ordinal))
            .Select(p => $"{p.Method} {Normalise(p.Raw)}")
            .ToHashSet();

        var table = ActiveContainerRoutes().Concat(RecycleBinContainerRoutes())
            .Select(row => $"{row[0]} {Normalise(((string)row[1]).Replace("{c}", "{containerId}"))}")
            .ToHashSet();

        routed.Should().BeEquivalentTo(table, "a container route missing from the table is a route nothing proves is judged per container");

        static string Normalise(string raw) => System.Text.RegularExpressions.Regex.Replace(raw, @"/(i1|col1|p1)(?=/|$)", "/{x}")
            .Replace("{itemId}", "{x}").Replace("{columnId}", "{x}").Replace("{permissionId}", "{x}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private HttpClient Admin() => _fixture.CreateCaller(new[] { "Admin" });

    private int HandlerConfigReads() => _fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Count(c => c.Id == ConfigA);

    private static async Task AssertReachedTheHandlerCore(HttpResponseMessage response, AdminSurfaceHostFixture fixture)
    {
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(UnverifiableCode);
        fixture.Dataverse.CallsOn(ConfigSet, "Retrieve").Count(c => c.Id == ConfigA).Should().BeGreaterThanOrEqualTo(2,
            "the filter resolved the config to read the binding, and the HANDLER resolved it again — it ran. " + body);
    }

    private Task AssertReachedTheHandler(HttpResponseMessage response) => AssertReachedTheHandlerCore(response, _fixture);

    private static string Url(string path, string containerId) =>
        path.Replace("{c}", containerId) + $"?configId={ConfigA}";

    private static HttpRequestMessage Request(string method, string path, string containerId, string body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), Url(path, containerId));
        request.Content = body switch
        {
            "patch-container" => JsonContent.Create(new { displayName = "Renamed" }),
            "share" => JsonContent.Create(new { linkType = "view", scope = "organization" }),
            "folder" => JsonContent.Create(new { name = "Folder" }),
            "column" => JsonContent.Create(new { name = "col", displayName = "Col", columnType = "text" }),
            "patch-column" => JsonContent.Create(new { displayName = "Col" }),
            "properties" => JsonContent.Create(new { properties = new[] { new { name = "Region", value = "EU", isSearchable = false } } }),
            "grant" => JsonContent.Create(new { userId = "u-1", role = "reader" }),
            "role" => JsonContent.Create(new { role = "writer" }),
            "ids" => JsonContent.Create(new { ids = new[] { "i1" } }),
            "upload" => Upload(),
            _ => method is "POST" or "PUT" or "PATCH" ? JsonContent.Create(new { }) : null,
        };
        return request;

        static HttpContent Upload()
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(new byte[] { 1, 2, 3 });
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", "a.bin");
            return form;
        }
    }

    private static HttpRequestMessage TypeRequest(string method, string path, string typeId, Guid configId)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path.Replace("{t}", typeId) + $"?configId={configId}");
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new
            {
                appId = "f0f0f0f0-0000-0000-0000-00000000000f",
                displayName = "Consumer",
                sharePointAdminUrl = "https://contoso-admin.sharepoint.com",
                delegatedPermissions = new[] { "ReadContent" },
                applicationPermissions = Array.Empty<string>(),
            });
        }

        return request;
    }

    private void SeedTenant(Guid? callerUnit, string configBType = TypeT)
    {
        var dv = _fixture.Dataverse;
        dv.Add("sprk_speenvironments", AdminSurfaceHostFixture.BffEnvironmentRow()); // master's tenant guard (round 65 merge)
        dv.Add("businessunits", new() { ["businessunitid"] = Root, ["_parentbusinessunitid_value"] = null });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitA, ["_parentbusinessunitid_value"] = Root });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitASub, ["_parentbusinessunitid_value"] = UnitA });
        dv.Add("businessunits", new() { ["businessunitid"] = UnitB, ["_parentbusinessunitid_value"] = Root });

        if (callerUnit is { } unit)
        {
            dv.Add("systemusers", new()
            {
                ["azureactivedirectoryobjectid"] = AdminSurfaceHostFixture.CallerOid,
                ["systemuserid"] = Guid.Parse("5e5e5e5e-0000-0000-0000-000000000001"),
                ["_businessunitid_value"] = unit,
            });
        }

        dv.Add(ConfigSet, ConfigRow(ConfigA, UnitA, TypeT));
        dv.Add(ConfigSet, ConfigRow(ConfigB, UnitB, configBType));
        dv.Add(ConfigSet, ConfigRow(ConfigN, null, TypeN));

    }

    private static Dictionary<string, object?> ConfigRow(Guid id, Guid? unit, string type) => new()
    {
        ["sprk_specontainertypeconfigid"] = id,
        ["_sprk_environment_value"] = AdminSurfaceHostFixture.BffEnvironmentId,
        ["sprk_name"] = $"Config {id.ToString()[..2]}",
        ["_sprk_businessunit_value"] = unit,
        ["sprk_containertypeid"] = type,
        ["sprk_owningappid"] = "a0a0a0a0-0000-0000-0000-00000000000a",
        ["sprk_keyvaultsecretname"] = "spe-owning-app-shared",
        ["statecode"] = 0,
    };

    private void StubContainers()
    {
        StubContainer("c-own", TypeT, UnitA.ToString());
        StubContainer("c-sub", TypeT, UnitASub.ToString());
        StubContainer("c-other", TypeT, UnitB.ToString());
        StubContainer("c-unbound", TypeT, null);
        StubContainer("c-type", TypeX, UnitA.ToString());
        _fixture.Graph.StubGet($"{ContainersPath}/c-gone", """{"error":{"code":"itemNotFound","message":"x"}}""", 404);
        _fixture.Graph.StubGet($"{ContainersPath}/c-fault", """{"error":{"code":"serviceNotAvailable","message":"x"}}""", 500);
        StubDeleted("c-own", UnitA.ToString());
        StubDeleted("c-other", UnitB.ToString());
    }

    private void StubContainer(string id, string type, string? stamp) =>
        _fixture.Graph.StubGet($"{ContainersPath}/{id}", ContainerJson(id, type, stamp));

    private void StubDeleted(string id, string? stamp) =>
        _fixture.Graph.StubGet($"{DeletedPath}/{id}", ContainerJson(id, TypeT, stamp));

    private void StubCreate(string id, string type) =>
        _fixture.Graph.StubPostExact(ContainersPath, ContainerJson(id, type, null), 201);

    private void StubContainerList(params string[] ids) =>
        _fixture.Graph.StubGetExact(ContainersPath,
            "{\"value\":[" + string.Join(",", ids.Select(id =>
                "{\"id\":\"" + id + "\",\"displayName\":\"Container " + id + "\",\"containerTypeId\":\"" + TypeT + "\"}")) + "]}");

    private static string ContainerJson(string id, string type, string? stamp)
    {
        var properties = stamp is null
            ? "{}"
            : "{\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + stamp + "\",\"isSearchable\":false}}";
        return "{\"id\":\"" + id + "\",\"displayName\":\"Container " + id + "\",\"containerTypeId\":\"" + type +
               "\",\"status\":\"active\",\"customProperties\":" + properties + "}";
    }

    private static string DeletedListJson(params string[] ids) =>
        "{\"value\":[" + string.Join(",", ids.Select(id =>
            "{\"id\":\"" + id + "\",\"displayName\":\"Deleted " + id + "\",\"containerTypeId\":\"" + TypeT + "\"}")) + "]}";

    private static string SearchJson(params (string Kind, string Id, string? DriveId)[] hits) =>
        "{\"value\":[{\"hitsContainers\":[{\"total\":" + hits.Length + ",\"moreResultsAvailable\":false,\"hits\":[" +
        string.Join(",", hits.Select(h => h.Kind == "driveItem"
            ? "{\"hitId\":\"" + h.Id + "\",\"resource\":{\"@odata.type\":\"#microsoft.graph.driveItem\",\"id\":\"" + h.Id +
              "\",\"name\":\"" + h.Id + "\",\"parentReference\":{\"driveId\":\"" + h.DriveId + "\"}}}"
            : "{\"hitId\":\"" + h.Id + "\",\"resource\":{\"@odata.type\":\"#microsoft.graph.fileStorageContainer\",\"id\":\"" + h.Id +
              "\",\"displayName\":\"" + h.Id + "\",\"containerTypeId\":\"" + TypeT + "\"}}")) +
        "]}]}]}";

    /// <summary>A driveItem search hit; <paramref name="driveId"/> null = the hit names no container at all.</summary>
    private static string DriveItemHit(string id, string? driveId) =>
        "{\"hitId\":\"" + id + "\",\"resource\":{\"@odata.type\":\"#microsoft.graph.driveItem\",\"id\":\"" + id +
        "\",\"name\":\"" + id + "\"" + (driveId is null ? "" : ",\"parentReference\":{\"driveId\":\"" + driveId + "\"}") + "}}";

    private static string SearchJsonRaw(int total, bool moreResultsAvailable, params string[] hits) =>
        "{\"value\":[{\"hitsContainers\":[{\"total\":" + total + ",\"moreResultsAvailable\":" +
        (moreResultsAvailable ? "true" : "false") + ",\"hits\":[" + string.Join(",", hits) + "]}]}]}";

    private static IEnumerable<string> Ids(JsonElement items) =>
        items.EnumerateArray().Select(i => i.GetProperty("id").GetString()!);

    private static void AssertUniformContainerNotFound(Dictionary<string, JsonElement> problem, string containerId, bool recycleBin)
    {
        problem["status"].GetInt32().Should().Be(404);
        problem["title"].GetString().Should().Be("Not Found");
        problem["detail"].GetString().Should().Be(recycleBin
            ? $"Container '{containerId}' was not found in the recycle bin."
            : $"Container '{containerId}' was not found.");
        problem["errorCode"].GetString().Should().Be(ContainerCode);
        problem.Keys.Except(new[] { "type", "title", "status", "detail" })
            .Should().BeEquivalentTo(new[] { "errorCode", "traceId" });
    }

    private static async Task<Dictionary<string, JsonElement>> Problem(
        HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.NotFound)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement;
    }
}
