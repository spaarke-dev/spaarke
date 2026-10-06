using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// REGRESSION GUARD — unified-access-control-r2 task 162 (owner round 10 item 1), pinned at the task-167 integration.
///
/// <para>Three analysis routes were DELETED rather than gated, because each had no caller in the repository and is in no
/// published API description (task 162 notes §2):</para>
/// <list type="bullet">
///   <item><c>POST /api/ai/analysis/{analysisId}/export</c> — exported any analysis's working document or output, as a
///   download or by Email/Teams to destinations the caller chose (sweep S-01, critical);</item>
///   <item><c>POST /api/ai/analysis/{analysisId}/save</c> — an app-only write of the working document into a
///   caller-chosen place (S-50);</item>
///   <item><c>POST /api/ai/analysis/fork</c> — an app-only sprk_analysis created against any document id (S-51).</item>
/// </list>
///
/// <para><b>Why this file exists.</b> The route-authorization sweep ledger (RouteAuthorizationGuardTests) resolves a
/// deleted route only with a ProofTest that pins its ABSENCE; task 162 deleted these three with no such pin (its contract
/// test proves the SURVIVING routes deny). <b>WHAT WOULD BREAK IF THIS FILE WERE DELETED:</b> one of the three is restored
/// and the unauthorized read or write silently returns.</para>
///
/// <para><b>Shape</b> (task 163's <see cref="KnowledgeAndRagRouteRetirementTests"/>): the endpoint table proves no handler
/// exists at all; the HTTP half is SIGNED IN, because since task 167 the authorization FallbackPolicy answers an anonymous
/// request 401 whether or not a route matches it; and the positive control reads the surviving siblings from the same
/// table, so an empty or mis-built host cannot make the absence pass.</para>
/// </summary>
[Trait("status", "repaired")]
public class AnalysisRouteRetirementTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AnalysisRouteRetirementTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("POST", "/api/ai/analysis/16200000-0000-0000-0000-000000000001/export")]
    [InlineData("POST", "/api/ai/analysis/16200000-0000-0000-0000-000000000001/save")]
    [InlineData("POST", "/api/ai/analysis/fork")]
    public void RetiredRoute_IsAbsentFromTheEndpointTable(string verb, string path)
    {
        EndpointTable.Endpoints(_factory).Should().NotBeEmpty("an empty table would make the absence check trivially true");

        EndpointTable.Maps(_factory, verb, path).Should().BeFalse(
            $"{verb} {path} was RETIRED by unified-access-control-r2 task 162 (no caller, not published); a restored route "
            + "needs a per-record decision, not a bare RequireAuthorization()");
    }

    [Theory]
    [InlineData("POST", "/api/ai/analysis/16200000-0000-0000-0000-000000000001/export")]
    [InlineData("POST", "/api/ai/analysis/16200000-0000-0000-0000-000000000001/save")]
    [InlineData("POST", "/api/ai/analysis/fork")]
    public async Task RetiredRoute_WithAValidBearer_Is404NotRouted(string verb, string path)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        using var request = new HttpRequestMessage(new HttpMethod(verb), path)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed },
            $"{verb} {path} was retired by task 162; anything else means a handler exists again");
    }

    [Theory]
    [InlineData("POST", "/api/ai/analysis/create")]
    [InlineData("POST", "/api/ai/analysis/execute")]
    [InlineData("POST", "/api/ai/analysis/promote")]
    [InlineData("GET", "/api/ai/analysis/16200000-0000-0000-0000-000000000001")]
    public void SurvivingSiblings_AreStillMapped(string verb, string path)
    {
        // The positive control for the absences above: the same host maps the routes task 162 kept and gated.
        EndpointTable.AssertMapped(_factory, verb, path);
    }
}
