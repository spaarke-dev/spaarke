using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 188 round 3 slice B (security pass 1): THE external document narrowing on the internal
/// plane's document routes — every route behind <c>DocumentAuthorizationFilter</c>, the bulk download and the two container
/// list routes. A caller who is not an internal systemuser (flagged external, unresolved, or a failed lookup) gets a
/// document only when the root grants allow it AND the narrowing shows it.
/// </summary>
/// <remarks>
/// The host is the real BFF (<see cref="DocumentRouteNarrowingTestFixture"/>): real filters, real
/// <c>AuthorizationService</c> over the token-stated rights, the real <see cref="CallerSystemUserResolver"/> and the REAL
/// predicate behind the real app-only seam (<see cref="AppOnlyDocumentWorld"/>). Only the caller's systemuser row and the
/// app-only Dataverse answers are stated. Every caller holds every right, so a 403 here is the narrowing.
/// </remarks>
public class DocumentRouteExternalNarrowingTests : IClassFixture<DocumentRouteNarrowingTestFixture>
{
    private const int Restricted = 100000002;
    private const int OpenThread = 100000000;
    private const string AllRights = "ReadAccess,WriteAccess,DeleteAccess,AppendAccess,AppendToAccess";

    private readonly DocumentRouteNarrowingTestFixture _fixture;

    public DocumentRouteExternalNarrowingTests(DocumentRouteNarrowingTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // ── the world ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A hidden document of <paramref name="kind"/>, seeded in the app-only world.</summary>
    private Guid Hidden(string kind)
    {
        var world = _fixture.World;
        var id = Guid.NewGuid();
        switch (kind)
        {
            case "own-restricted":
                world.Document(id, permission: Restricted);
                break;
            case "internal-only-communication":
                var communication = Guid.NewGuid();
                var thread = Guid.NewGuid();
                world.Thread(thread, OpenThread);
                world.Communication(communication, internalOnly: true, thread);
                world.Document(id, communication: communication);
                break;
            case "restricted-parent":
                var parent = world.Document(Guid.NewGuid(), permission: Restricted);
                world.Document(id, parent: parent);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return id;
    }

    /// <summary>A visible document: Standard, an Open shareable communication, and a Standard parent.</summary>
    private Guid Visible()
    {
        var world = _fixture.World;
        var thread = Guid.NewGuid();
        var communication = Guid.NewGuid();
        world.Thread(thread, OpenThread);
        world.Communication(communication, internalOnly: false, thread);
        var parent = world.Document(Guid.NewGuid());
        return world.Document(Guid.NewGuid(), communication: communication, parent: parent);
    }

    public static TheoryData<string, string> RoutesByHiddenKind()
    {
        var data = new TheoryData<string, string>();
        foreach (var route in new[] { "read", "download", "versions", "preview", "delete", "checkout" })
        foreach (var kind in new[] { "own-restricted", "internal-only-communication", "restricted-parent" })
            data.Add(route, kind);
        return data;
    }

    private static Task<HttpResponseMessage> CallAsync(HttpClient client, string route, Guid id) => route switch
    {
        "read" => client.GetAsync($"/api/v1/documents/{id}"),
        "download" => client.GetAsync($"/api/documents/{id}/download"),
        "versions" => client.GetAsync($"/api/documents/{id}/versions"),
        "preview" => client.GetAsync($"/api/documents/{id}/preview-url"),
        "delete" => client.DeleteAsync($"/api/v1/documents/{id}"),
        "checkout" => client.PostAsync($"/api/documents/{id}/checkout", content: null),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
    };

    private async Task AssertRefusedByTheNarrowingAsync(HttpResponseMessage response, string why)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, why);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sdap.access.deny.insufficient_rights",
            "a hidden document is refused with the route's existing deny shape, indistinguishable from no right");
        _fixture.DownloadedItemIds.Should().BeEmpty("nothing is streamed for a hidden document");
        _fixture.DeletedDataverseDocumentIds.Should().BeEmpty("nothing is written for a hidden document");
        _fixture.DeletedDocumentIds.Should().BeEmpty("nothing is written for a hidden document");
    }

    // ── must fire: single-document routes ─────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 188 r3 B: an external caller is refused a hidden document on every kind of single-document route")]
    [MemberData(nameof(RoutesByHiddenKind))]
    public async Task SingleDocumentRoute_ExternalCaller_HiddenDocument_IsRefused(string route, string kind)
    {
        var id = Hidden(kind);
        using var client = _fixture.CreateClient(AllRights, NarrowingCallerMode.External);

        var response = await CallAsync(client, route, id);

        await AssertRefusedByTheNarrowingAsync(response, $"{kind} hides the document on {route}");
        _fixture.World.Calls.Should().Be(1, "one narrowing call per request");
    }

    [Theory(DisplayName = "Task 188 r3 B: a failed externality lookup applies the narrowing — a hidden document is refused, a visible one is not")]
    [InlineData(NarrowingCallerMode.LookupFault)]
    [InlineData(NarrowingCallerMode.ExternalFlagFault)]
    public async Task SingleDocumentRoute_LookupFault_Narrows_ItDoesNotRefuseEverything(NarrowingCallerMode mode)
    {
        var hidden = Hidden("own-restricted");
        var visible = Visible();
        using var client = _fixture.CreateClient(AllRights, mode);

        var refused = await CallAsync(client, "read", hidden);
        var shown = await CallAsync(client, "read", visible);

        await AssertRefusedByTheNarrowingAsync(refused, "a failed lookup counts as external");
        shown.StatusCode.Should().Be(HttpStatusCode.OK, "the fault narrows; it does not refuse every request");
    }

    [Theory(DisplayName = "Task 188 r3 B: a narrowing that faults refuses the single document with the existing deny shape")]
    [InlineData("read")]
    [InlineData("download")]
    [InlineData("delete")]
    public async Task SingleDocumentRoute_NarrowingFault_IsRefused(string route)
    {
        var visible = Visible();
        _fixture.World.Faults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;
        using var client = _fixture.CreateClient(AllRights, NarrowingCallerMode.External);

        var response = await CallAsync(client, route, visible);

        await AssertRefusedByTheNarrowingAsync(response, "a narrowing that cannot decide refuses (fail closed)");
    }

    // ── must not fire: single-document routes ─────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 188 r3 B: an internal user is not narrowed — a Restricted document is served and nothing is narrowed")]
    public async Task SingleDocumentRoute_InternalUser_IsNotNarrowed()
    {
        var id = Hidden("own-restricted");
        using var client = _fixture.CreateClient(AllRights, NarrowingCallerMode.Internal);

        var read = await CallAsync(client, "read", id);
        var delete = await CallAsync(client, "delete", id);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.DeletedDataverseDocumentIds.Should().ContainSingle().Which.Should().Be(id.ToString());
        _fixture.World.Calls.Should().Be(0, "an internal systemuser makes no narrowing call");
        _fixture.World.AllReads.Should().BeEmpty("and no app-only read");
    }

    [Fact(DisplayName = "Task 188 r3 B: an external caller keeps a visible document (read and a write)")]
    public async Task SingleDocumentRoute_ExternalCaller_VisibleDocument_IsServed()
    {
        var id = Visible();
        using var client = _fixture.CreateClient(AllRights, NarrowingCallerMode.External);

        var read = await CallAsync(client, "read", id);
        var delete = await CallAsync(client, "delete", id);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _fixture.DeletedDataverseDocumentIds.Should().ContainSingle().Which.Should().Be(id.ToString());
        _fixture.World.Calls.Should().Be(2, "one narrowing call per request");
    }

    [Theory(DisplayName = "Task 188 r3 B: an external caller is not refused a visible document by the narrowing on the content routes")]
    [InlineData("download")]
    [InlineData("versions")]
    [InlineData("preview")]
    [InlineData("checkout")]
    public async Task SingleDocumentRoute_ExternalCaller_VisibleDocument_PassesTheFilter(string route)
    {
        var id = Visible();
        using var client = _fixture.CreateClient(AllRights, NarrowingCallerMode.External);

        var response = await CallAsync(client, route, id);

        // These handlers call real SPE/Graph paths that cannot succeed offline; what is pinned is the filter's verdict.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        _fixture.World.Calls.Should().Be(1);
    }

    [Theory(DisplayName = "Task 188 r3 B: the root grants still decide — no right is a 403 before any narrowing")]
    [InlineData(NarrowingCallerMode.External)]
    [InlineData(NarrowingCallerMode.Internal)]
    public async Task SingleDocumentRoute_NoRight_IsDenied_WithoutNarrowing(NarrowingCallerMode mode)
    {
        var id = Visible();
        using var client = _fixture.CreateClient("", mode);

        var response = await CallAsync(client, "read", id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.World.Calls.Should().Be(0, "the narrowing runs only after the root grants allowed");
    }

    // ── bulk download ─────────────────────────────────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> BulkAsync(HttpClient client, params Guid[] ids) =>
        client.PostAsJsonAsync("/api/documents/bulk-download", new { documentIds = ids.Select(i => i.ToString()).ToArray() });

    private static async Task<IReadOnlyList<string>> ZipEntriesAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    [Theory(DisplayName = "Task 188 r3 B: bulk download drops every hidden document for an external caller (or a failed lookup), in one narrowing call")]
    [InlineData(NarrowingCallerMode.External)]
    [InlineData(NarrowingCallerMode.LookupFault)]
    public async Task BulkDownload_ExternalCaller_DropsHiddenDocuments(NarrowingCallerMode mode)
    {
        var visible = Visible();
        var hidden = new[] { Hidden("own-restricted"), Hidden("internal-only-communication"), Hidden("restricted-parent") };
        using var client = _fixture.CreateClient(AllRights, mode);

        var response = await BulkAsync(client, hidden.Prepend(visible).ToArray());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await ZipEntriesAsync(response);
        entries.Should().Contain($"{visible}.pdf");
        foreach (var id in hidden)
            entries.Should().NotContain($"{id}.pdf");
        _fixture.DownloadedItemIds.Should().BeEquivalentTo(new[] { $"item-{visible}" },
            "only the visible document's bytes are read; a hidden id is dropped exactly as an unauthorized one");
        _fixture.World.Calls.Should().Be(1, "one batched narrowing call per request, not one per document");
    }

    [Fact(DisplayName = "Task 188 r3 B: a bulk-download narrowing that faults drops every document")]
    public async Task BulkDownload_NarrowingFault_DropsEveryDocument()
    {
        var visible = Visible();
        _fixture.World.Faults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;
        using var client = _fixture.CreateClient(AllRights, NarrowingCallerMode.External);

        var response = await BulkAsync(client, visible, Visible());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "nothing is left to zip");
        _fixture.DownloadedItemIds.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 188 r3 B: an internal user's bulk download is not narrowed")]
    public async Task BulkDownload_InternalUser_IsNotNarrowed()
    {
        var hidden = Hidden("own-restricted");
        var visible = Visible();
        using var client = _fixture.CreateClient(AllRights, NarrowingCallerMode.Internal);

        var response = await BulkAsync(client, hidden, visible);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.DownloadedItemIds.Should().BeEquivalentTo(new[] { $"item-{hidden}", $"item-{visible}" });
        _fixture.World.Calls.Should().Be(0);
    }

    [Fact(DisplayName = "Task 188 r3 B: bulk download — the root grants still decide; nothing is narrowed when nothing is authorized")]
    public async Task BulkDownload_NoRight_NothingIsNarrowed()
    {
        using var client = _fixture.CreateClient("", NarrowingCallerMode.External);

        var response = await BulkAsync(client, Visible());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fixture.World.Calls.Should().Be(0);
    }

    // ── the two container list routes ─────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> ListRoutes() => new() { "document-list", "container-document-list" };

    /// <summary>
    /// Runs the REAL list handler (<see cref="DataverseDocumentsEndpoints.ListDocumentsAsync"/> or
    /// <see cref="DataverseDocumentsEndpoints.ListContainerDocumentsAsync"/>) over <paramref name="rows"/>, as a caller in
    /// <paramref name="mode"/>. The container filter in front of it is task 078's and is tested there.
    /// </summary>
    private async Task<JsonElement> ListAsync(string route, NarrowingCallerMode mode, IReadOnlyList<Guid> rows, int skip, int take)
    {
        var containerId = Guid.NewGuid().ToString();
        var dataverse = new Mock<IDocumentDataverseService>(MockBehavior.Strict);
        dataverse.Setup(d => d.GetDocumentsByContainerAsync(containerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.Select(id => new DocumentEntity { Id = id.ToString(), Name = $"doc-{id}" }).ToList());

        var identity = new NarrowingStatedIdentityResolver(() => mode);
        var services = new ServiceCollection()
            .AddSingleton<ISystemUserIdentityResolver>(identity)
            .AddSingleton<ICallerSystemUserResolver>(new CallerSystemUserResolver(identity, NullLogger<CallerSystemUserResolver>.Instance))
            .AddSingleton<InternalRouteDocumentNarrowing>((ids, ct) => _fixture.World.Narrowing(ids, ct))
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString()) },
                "Test")),
        };

        var result = route == "document-list"
            ? await DataverseDocumentsEndpoints.ListDocumentsAsync(containerId, skip, take, dataverse.Object, NullLogger<Program>.Instance, context)
            : await DataverseDocumentsEndpoints.ListContainerDocumentsAsync(containerId, skip, take, dataverse.Object, NullLogger<Program>.Instance, context);

        result.Should().BeAssignableTo<IValueHttpResult>("the list answers 200 with its page");
        return JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value).GetProperty("data");
    }

    private static IReadOnlyList<string> Ids(JsonElement data) =>
        data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("Id").GetString()!).ToList();

    [Theory(DisplayName = "Task 188 r3 B: a container list removes hidden rows for an external caller, before paging (no total or next page counts them)")]
    [MemberData(nameof(ListRoutes))]
    public async Task List_ExternalCaller_RemovesHiddenRows_BeforePaging(string route)
    {
        var v1 = Visible();
        var v2 = Visible();
        var v3 = Visible();
        var rows = new[] { v1, Hidden("own-restricted"), v2, Hidden("internal-only-communication"), Hidden("restricted-parent"), v3 };

        var page1 = await ListAsync(route, NarrowingCallerMode.External, rows, skip: 0, take: 2);
        var page2 = await ListAsync(route, NarrowingCallerMode.External, rows, skip: 2, take: 2);

        Ids(page1).Should().Equal(v1.ToString(), v2.ToString());
        page1.GetProperty("totalCount").GetInt32().Should().Be(3, "the total counts only rows the caller may see");
        page1.GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
        Ids(page2).Should().Equal(v3.ToString());
        page2.GetProperty("totalCount").GetInt32().Should().Be(3);
        page2.GetProperty("hasNextPage").GetBoolean().Should().BeFalse("the next page skips no visible row and promises no hidden one");
        _fixture.World.Calls.Should().Be(2, "one batched narrowing call per request, never one per row");
    }

    [Theory(DisplayName = "Task 188 r3 B: a container list with a failed externality lookup narrows (it does not refuse)")]
    [MemberData(nameof(ListRoutes))]
    public async Task List_LookupFault_Narrows(string route)
    {
        var visible = Visible();
        var rows = new[] { Hidden("own-restricted"), visible, Hidden("restricted-parent") };

        var page = await ListAsync(route, NarrowingCallerMode.LookupFault, rows, skip: 0, take: 50);

        Ids(page).Should().Equal(visible.ToString());
        page.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Theory(DisplayName = "Task 188 r3 B: a container list whose narrowing faults drops every document row")]
    [MemberData(nameof(ListRoutes))]
    public async Task List_NarrowingFault_DropsEveryRow(string route)
    {
        var rows = new[] { Visible(), Visible() };
        _fixture.World.Faults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;

        var page = await ListAsync(route, NarrowingCallerMode.External, rows, skip: 0, take: 50);

        Ids(page).Should().BeEmpty();
        page.GetProperty("totalCount").GetInt32().Should().Be(0);
        page.GetProperty("hasNextPage").GetBoolean().Should().BeFalse();
    }

    [Theory(DisplayName = "Task 188 r3 B: an internal user's container list is unchanged and nothing is narrowed")]
    [MemberData(nameof(ListRoutes))]
    public async Task List_InternalUser_IsNotNarrowed(string route)
    {
        var rows = new[] { Hidden("own-restricted"), Visible(), Hidden("internal-only-communication") };

        var page = await ListAsync(route, NarrowingCallerMode.Internal, rows, skip: 0, take: 50);

        Ids(page).Should().Equal(rows.Select(r => r.ToString()));
        page.GetProperty("totalCount").GetInt32().Should().Be(3);
        _fixture.World.Calls.Should().Be(0);
        _fixture.World.AllReads.Should().BeEmpty();
    }
}

/// <summary>Who the caller is, as the systemuser lookup answers.</summary>
public enum NarrowingCallerMode
{
    /// <summary>A resolved systemuser, not flagged external.</summary>
    Internal,

    /// <summary>A resolved systemuser flagged <c>sprk_isexternal</c>.</summary>
    External,

    /// <summary>The oid → systemuser lookup throws (the resolver reports <c>lookup-failed</c>).</summary>
    LookupFault,

    /// <summary>The systemuser resolves, but its external flag cannot be read.</summary>
    ExternalFlagFault,
}

/// <summary>
/// The real <see cref="CallerSystemUserResolver"/>, asked with the principal's oid claim replaced by a fixed GUID (the shared
/// fixture's oid is not a GUID; production oids are).
/// </summary>
internal sealed class GuidOidCallerResolver(ICallerSystemUserResolver inner) : ICallerSystemUserResolver
{
    private const string OidClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public Task<CallerSystemUserResolution> ResolveAsync(ClaimsPrincipal? caller, CancellationToken ct) =>
        inner.ResolveAsync(
            caller is null
                ? null
                : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(OidClaim, "18800000-0000-0000-0000-00000000000a") }, "Test")),
            ct);
}

/// <summary>An <see cref="ISystemUserIdentityResolver"/> that answers as <see cref="NarrowingCallerMode"/> says.</summary>
internal sealed class NarrowingStatedIdentityResolver(Func<NarrowingCallerMode> mode) : ISystemUserIdentityResolver
{
    private static readonly Guid SystemUserId = Guid.Parse("18800000-0000-0000-0000-0000000000b0");

    public Task<string?> ResolveOidAsync(Guid systemUserId, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<Guid?> ResolveSystemUserIdAsync(string oid, CancellationToken ct = default) =>
        mode() == NarrowingCallerMode.LookupFault
            ? throw new InvalidOperationException("Simulated systemuser lookup failure (test).")
            : Task.FromResult<Guid?>(SystemUserId);

    public Task<bool> IsExternalAsync(Guid systemUserId, CancellationToken ct = default) => mode() switch
    {
        NarrowingCallerMode.External => Task.FromResult(true),
        NarrowingCallerMode.ExternalFlagFault => throw new InvalidOperationException("Simulated external-flag read failure (test)."),
        _ => Task.FromResult(false),
    };
}

/// <summary>
/// <see cref="DocumentDestroyAuthorizationTestFixture"/> (real routes, token-stated rights, recorded destroys and SPE
/// downloads) plus: the caller's systemuser stated per request (header <see cref="CallerHeader"/>), and the app-only
/// Dataverse the narrowing reads stated in <see cref="World"/> (the REAL predicate behind the real seam).
/// </summary>
public class DocumentRouteNarrowingTestFixture : DocumentDestroyAuthorizationTestFixture
{
    /// <summary>The request header carrying the <see cref="NarrowingCallerMode"/> (test host only).</summary>
    public const string CallerHeader = "X-Test-Caller-Mode";

    /// <summary>The app-only world the narrowing reads (replaced on <see cref="Reset"/>).</summary>
    internal AppOnlyDocumentWorld World { get; private set; } = new();

    /// <summary>Clears the recorders and the world.</summary>
    public new void Reset()
    {
        base.Reset();
        World = new AppOnlyDocumentWorld();
    }

    /// <summary>A caller holding <paramref name="rights"/> on every document, answering the systemuser lookup as <paramref name="mode"/>.</summary>
    public HttpClient CreateClient(string rights, NarrowingCallerMode mode)
    {
        var client = CreateClientWithRights(rights);
        client.DefaultRequestHeaders.Add(CallerHeader, mode.ToString());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            // The shared fixture's oid ("test-user-…") is not a GUID, so the real CallerSystemUserResolver would answer
            // no-oid-claim for every request. The caller resolver therefore delegates to the REAL one over the stated
            // systemuser row, with the oid claim replaced by a GUID (production oids are GUIDs). LookupFault reaches the
            // real resolver's own catch, which answers lookup-failed.
            services.RemoveAll<ISystemUserIdentityResolver>();
            services.AddSingleton<ISystemUserIdentityResolver>(sp =>
            {
                var accessor = sp.GetRequiredService<IHttpContextAccessor>();
                return new NarrowingStatedIdentityResolver(() =>
                    Enum.TryParse<NarrowingCallerMode>(accessor.HttpContext?.Request.Headers[CallerHeader].ToString(), out var mode)
                        ? mode
                        : NarrowingCallerMode.Internal);
            });
            services.RemoveAll<ICallerSystemUserResolver>();
            services.AddScoped<ICallerSystemUserResolver>(sp => new GuidOidCallerResolver(
                new CallerSystemUserResolver(
                    sp.GetRequiredService<ISystemUserIdentityResolver>(), NullLogger<CallerSystemUserResolver>.Instance)));

            services.RemoveAll<InternalRouteDocumentNarrowing>();
            services.AddScoped<InternalRouteDocumentNarrowing>(_ => (ids, ct) => World.Narrowing(ids, ct));
        });
    }
}
