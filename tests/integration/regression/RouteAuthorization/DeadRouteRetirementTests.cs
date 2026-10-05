using System.Net;
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
/// <para><b>WHY 404 / 405 AND NOT 401.</b> ASP.NET Core routes BEFORE it authorizes. An unauthenticated request to a
/// route that EXISTS and requires authorization answers 401; to a path that does not exist, 404; to a path that exists
/// only for OTHER verbs, 405. So these assertions prove absence, and the positive controls below — surviving
/// siblings answering 401 to the same unauthenticated client — keep that discrimination honest: a fixture change that
/// made every request 404 would turn them red. The route-table assertion is the second, independent instrument: it
/// reads the BUILT host's endpoint data sources, so a re-added route fails even if its auth posture changed.</para>
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
        // Deliberately NO Authorization header — see the class summary.
        _client = factory.CreateClient();
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
            $"{verb} {path} was deleted by uac-r2 task 166 (no caller, not published). A 401 means it was re-added; "
            + "if that is deliberate it needs a per-record authorization decision, not a waiver.");
    }

    // ── Positive controls: surviving siblings, same unauthenticated client → 401 ─────────────────────────

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
    public async Task SurvivingSibling_WithoutBearer_Returns401(string verb, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { });
        }

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the positive control: a route that EXISTS answers 401 to this client, which is what makes the 404s above mean absence");
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
