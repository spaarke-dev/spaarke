using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// REGRESSION GUARD — unified-access-control-r2 task 164 (owner round 10 item 1).
///
/// <para>Ten routes were DELETED rather than gated, because each had no caller anywhere in the repository and
/// appears in no published API description (src/solutions/CopilotAgent/spaarke-bff-openapi.yaml,
/// spaarke-api-plugin.json and declarativeAgent.json were checked; no other OpenAPI, ai-plugin or
/// declarativeAgent file exists outside knowledge/ samples). Each let ANY signed-in caller act, as the BFF's own
/// identity, on a record the caller chose:</para>
/// <list type="bullet">
///   <item>the six <c>/api/ai/prompts</c> routes — read, overwrite, delete, render or plant another user's or
///   team's prompt templates (sweep findings #56, #57);</item>
///   <item><c>GET /api/ai/playbooks/by-name/{name}</c> — read any private playbook's definition by name; its 500
///   echoed exception text (#55);</item>
///   <item><c>PUT /api/ai/playbooks/{id:guid}/nodes/reorder</c> — re-order nodes of OTHER playbooks, because it
///   never checked that a body node belonged to the route playbook (#54);</item>
///   <item><c>POST /api/ai/document-intelligence/match-records</c> — find secure matters' names by party name
///   (#31), and <c>/associate-record</c> — re-parent any document onto any record (#32).</item>
/// </list>
///
/// <para><b>WHAT WOULD BREAK IF THIS FILE WERE DELETED:</b> someone restores one of these routes — for example
/// from the prompt-library rows in docs or the record-matching walkthroughs — and the unauthorized read or write
/// silently returns. A restored route must arrive with a per-record decision, not a bare RequireAuthorization().</para>
///
/// <para><b>Why the assertions are shaped this way</b> (tasks 073 and 163's precedent): enumerating the endpoint
/// table proves no handler exists at all; the HTTP half shows absence over the wire (routing precedes
/// authorization, so a present route answers 401 without a bearer and an absent one 404); the positive controls
/// show the surviving siblings ARE still routed — including the record-matching admin routes mapped under the
/// same <c>DocumentIntelligence:RecordMatchingEnabled</c> flag the deleted pair sat under, which this host turns on.</para>
/// </summary>
[Trait("status", "repaired")]
public class AiPlaybookPromptRecordMatchRouteRetirementTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    private static readonly (string Verb, string Pattern)[] RetiredRoutes =
    {
        ("GET", "/api/ai/prompts/"),
        ("POST", "/api/ai/prompts/"),
        ("GET", "/api/ai/prompts/{id}"),
        ("PUT", "/api/ai/prompts/{id}"),
        ("DELETE", "/api/ai/prompts/{id}"),
        ("POST", "/api/ai/prompts/{id}/render"),
        ("GET", "/api/ai/playbooks/by-name/{name}"),
        ("PUT", "/api/ai/playbooks/{id:guid}/nodes/reorder"),
        ("POST", "/api/ai/document-intelligence/match-records"),
        ("POST", "/api/ai/document-intelligence/associate-record"),
    };

    public AiPlaybookPromptRecordMatchRouteRetirementTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void RetiredRoutes_AreAbsentFromTheEndpointTable()
    {
        var endpoints = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToList();

        endpoints.Should().NotBeEmpty("an empty table would make every absence check below trivially true");

        var survivors = new List<string>();
        foreach (var (verb, pattern) in RetiredRoutes)
        {
            var normalized = pattern.Trim('/');
            foreach (var match in endpoints.Where(e =>
                         string.Equals(e.RoutePattern.RawText?.Trim('/'), normalized, StringComparison.OrdinalIgnoreCase)))
            {
                var verbs = match.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>();
                if (verbs.Count == 0 || verbs.Contains(verb, StringComparer.OrdinalIgnoreCase))
                {
                    survivors.Add($"{verb} {pattern}  (registered as: {match.DisplayName})");
                }
            }
        }

        survivors.Should().BeEmpty(
            "these routes were RETIRED by unified-access-control-r2 task 164 (no caller, not published). "
            + "Re-registered routes:\n  " + string.Join("\n  ", survivors));
    }

    [Theory]
    [InlineData("GET", "/api/ai/prompts")]
    [InlineData("POST", "/api/ai/prompts")]
    [InlineData("GET", "/api/ai/prompts/16400000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/ai/prompts/16400000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/ai/prompts/16400000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/ai/prompts/16400000-0000-0000-0000-000000000001/render")]
    [InlineData("GET", "/api/ai/playbooks/by-name/Document%20Profile")]
    [InlineData("PUT", "/api/ai/playbooks/16400000-0000-0000-0000-000000000002/nodes/reorder")]
    [InlineData("POST", "/api/ai/document-intelligence/match-records")]
    [InlineData("POST", "/api/ai/document-intelligence/associate-record")]
    public async Task RetiredRoute_WithAValidBearer_Is404NotRouted(string verb, string path)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        using var request = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"{verb} {path} was retired by task 164; anything else means a handler exists again");
    }

    [Theory]
    [InlineData("GET", "/api/ai/playbooks/by-id/18cf3cc8-02ec-f011-8406-7c1e520aa4df")]
    [InlineData("GET", "/api/ai/playbooks/16400000-0000-0000-0000-000000000002/nodes")]
    [InlineData("POST", "/api/admin/record-matching/sync")]
    [InlineData("GET", "/api/admin/record-matching/status")]
    public void SurvivingSiblings_AreStillMapped(string verb, string path)
    {
        // The positive control for the 404s above. An ANONYMOUS 401 no longer proves a route exists: the BFF's
        // authorization FallbackPolicy (UAC-r2 task 167) answers 401 for a request that matches no route too.
        // The endpoint table is the evidence that a surviving sibling is still routed.
        EndpointTable.AssertMapped(_factory, verb, path);
    }
}
