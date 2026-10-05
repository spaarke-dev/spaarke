using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.RouteAuthorization;

/// <summary>
/// REGRESSION GUARD — unified-access-control-r2 task 166 (route-authorization sweep, owner round 10 item 1).
///
/// <para>Five routes were DELETED rather than gated, each because it had no caller in the repository AND is in no
/// published API description (the evidence per route is in
/// <c>projects/unified-access-control-r2/notes/task-166-memory-compose-remaining-route-authorization.md</c>):</para>
/// <list type="bullet">
/// <item><c>POST /api/compose/documents/{documentSpeId}/promote</c> — body tenant + body session, no owner check (S-63).</item>
/// <item><c>POST /api/v1/work-assignments</c> — app-only create with caller-named parent lookups.</item>
/// <item><c>GET /api/memory/records/{entityLogicalName}/{id}</c> — record memory read (S-42).</item>
/// <item><c>GET /api/workspace/state</c> — legacy workspace state read.</item>
/// <item><c>GET /healthz/dataverse/doc/{id}</c> — an ANONYMOUS app-only read of any document row (amendment a).</item>
/// </list>
///
/// <para><b><c>PUT /api/v1/documents/{id}</c> is NOT among them.</b> Task 166's branch retired it too (S-36 / F0: the
/// whole-entity body could re-point a row's SPE pointers), but at the batch-4 integration it already had a caller —
/// task 147 r1's Compose document association re-files through it — so it was KEPT, behind task 146's AppendTo check
/// and refusing the storage-pointer fields (task 166's own condition for a body-bound document update; pinned by
/// <c>DocumentRefileRestampRouteTests.Put_NamingAStoragePointerField_IsRefusedAndWritesNothing</c>). It is a surviving
/// sibling below.</para>
///
/// <para><b>WHY A SIGNED-IN 404.</b> Since unified-access-control-r2 task 167 the BFF's authorization FallbackPolicy
/// challenges an ANONYMOUS request whether or not a route matches it, so an anonymous 404-vs-401 no longer tells a
/// deleted route from a live one (task 167 note §18.5; reconciled at the batch-4 integration of 166 and 167). The
/// retired-route probe therefore carries a bearer: signed in, a path that does not exist answers 404. The positive
/// controls below read the BUILT host's endpoint table for the surviving siblings, so a fixture change that made every
/// request 404 would turn them red; the route-table assertion at the end is the second, independent instrument for the
/// retired routes themselves.</para>
///
/// <para>Same instrument as <c>OboDriveKeyedRouteRetirementTests</c> (task 071) and
/// <c>DriveKeyedWriteRouteRetirementTests</c>.</para>
/// </summary>
[Trait("status", "repaired")]
public class DeadRouteRetirementTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;
    private readonly HttpClient _client;

    public DeadRouteRetirementTests(CustomWebAppFactory factory)
    {
        _factory = factory;
        // SIGNED IN — see the class summary: an anonymous request is challenged 401 whether or not its route exists.
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    }

    public static TheoryData<string, string> RetiredRoutes => new()
    {
        { "POST", "/api/compose/documents/spe-item-1/promote" },
        { "POST", "/api/v1/work-assignments" },
        { "GET", "/api/memory/records/sprk_matter/11111111-1111-1111-1111-111111111111" },
        { "GET", "/api/workspace/state" },
        { "GET", "/healthz/dataverse/doc/11111111-1111-1111-1111-111111111111" },
    };

    [Theory]
    [MemberData(nameof(RetiredRoutes))]
    public async Task RetiredRoute_WhenRequested_Returns404NotRouted(string verb, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"{verb} {path} was deleted by uac-r2 task 166 (no caller, not published). Anything else, signed in, means it "
            + "was re-added; if that is deliberate it needs a per-record authorization decision, not a waiver.");
    }

    // ── Positive controls: the surviving siblings are still in the endpoint table ───────────────────────────

    public static TheoryData<string, string> SurvivingSiblings => new()
    {
        { "POST", "/api/compose/documents/spe-item-1/save" },
        { "GET", "/api/memory/user" },
        { "GET", "/api/v1/documents/11111111-1111-1111-1111-111111111111" },
        { "PUT", "/api/v1/documents/11111111-1111-1111-1111-111111111111" },
        { "GET", "/api/workspace/layouts" },
    };

    [Theory]
    [MemberData(nameof(SurvivingSiblings))]
    public void SurvivingSibling_IsStillMapped(string verb, string path)
    {
        // The positive control for the 404s above. An ANONYMOUS 401 no longer proves a route exists (the FallbackPolicy
        // answers 401 for an unmatched request too), so the endpoint table is the evidence the sibling is still routed.
        EndpointTable.AssertMapped(_factory, verb, path);
    }

    // ── The built host's route table ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheBuiltRouteTable_CarriesNoneOfTheRetiredRoutes_AndStillCarriesTheirSiblings()
    {
        var routes = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => $"{m} {(e.RoutePattern.RawText ?? string.Empty).TrimStart('/')}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        routes.Should().NotContain(new[]
        {
            "POST api/compose/documents/{documentSpeId}/promote",
            "POST api/v1/work-assignments/",
            "POST api/v1/work-assignments",
            "GET api/memory/records/{entityLogicalName}/{id:guid}",
            "GET api/workspace/state",
            "GET healthz/dataverse/doc/{id}",
        });

        routes.Should().Contain(new[]
        {
            "POST api/compose/documents/{documentSpeId}/save",
            "GET api/v1/documents/{id}",
            "DELETE api/v1/documents/{id}",
            "PUT api/v1/documents/{id}",
            "GET healthz/dataverse",
        }, "the siblings must be found by the SAME normalization, or the absence half above would be vacuous");
    }
}
