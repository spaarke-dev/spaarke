using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// REGRESSION GUARD — unified-access-control-r2 task 163 (owner round 10 item 1).
///
/// <para>Nine routes were DELETED rather than gated, because each had no caller anywhere in the repository
/// and appeared in no published API description (the Copilot agent's OpenAPI document and plugin manifest
/// were checked). Each let ANY signed-in caller act, as the BFF's own identity, on a record, chunk or index
/// it chose:</para>
/// <list type="bullet">
///   <item><c>POST /api/ai/rag/index/batch</c> — wrote caller-built chunks; only the FIRST item's tenant was
///   checked, so items 2..N could land in another tenant's partition (finding #6);</item>
///   <item><c>DELETE /api/ai/rag/source/{sourceDocumentId}</c> — deleted every chunk of any document (#30);</item>
///   <item><c>GET /api/ai/knowledge/indexes/{indexName}/documents</c> — listed every indexed file in the tenant (#26);</item>
///   <item><c>DELETE /api/ai/knowledge/indexes/{indexName}/documents/{documentId}</c> — deleted any document's chunks (#27);</item>
///   <item><c>POST /api/ai/knowledge/indexes/reindex/{documentId}</c> — made the app read ANY drive item the caller
///   named, app-only, and index it (#28);</item>
///   <item><c>POST /api/ai/knowledge/test-search</c> — returned the raw text of every indexed document (#3);</item>
///   <item><c>POST /api/admin/knowledge/index-references</c>, <c>POST</c> and <c>DELETE
///   /api/admin/knowledge/index-reference/{knowledgeSourceId}</c> — re-embedded, overwrote or wiped the SHARED
///   reference grounding index behind a bare sign-in despite the /admin prefix (#49, #20, #21).</item>
/// </list>
///
/// <para><b>WHAT WOULD BREAK IF THIS FILE WERE DELETED:</b> someone restores one of these routes — most
/// plausibly from the stale tables in docs/guides/RAG-ARCHITECTURE.md — and the unauthorized write or read
/// silently returns. A restored route must arrive with a per-record decision or the SystemAdmin policy, not
/// a bare RequireAuthorization().</para>
///
/// <para><b>Why the assertions are shaped this way</b> (task 073's precedent): enumerating the endpoint table
/// proves no handler exists at all, which no status-code mapping can fake; the HTTP half shows absence over
/// the wire (routing precedes authorization, so a present route answers 401 without a bearer, an absent one
/// 404), and the positive controls show the surviving siblings ARE still routed, so a 404 here is not a
/// fixture artefact. The host enables DocumentIntelligence and Analysis — the flags the admin routes were
/// mapped under.</para>
/// </summary>
[Trait("status", "repaired")]
public class KnowledgeAndRagRouteRetirementTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    private static readonly (string Verb, string Pattern)[] RetiredRoutes =
    {
        ("POST", "/api/ai/rag/index/batch"),
        ("DELETE", "/api/ai/rag/source/{sourceDocumentId}"),
        ("GET", "/api/ai/knowledge/indexes/{indexName}/documents"),
        ("DELETE", "/api/ai/knowledge/indexes/{indexName}/documents/{documentId}"),
        ("POST", "/api/ai/knowledge/indexes/reindex/{documentId}"),
        ("POST", "/api/ai/knowledge/test-search"),
        ("POST", "/api/admin/knowledge/index-references"),
        ("POST", "/api/admin/knowledge/index-reference/{knowledgeSourceId}"),
        ("DELETE", "/api/admin/knowledge/index-reference/{knowledgeSourceId}"),
    };

    public KnowledgeAndRagRouteRetirementTests(CustomWebAppFactory factory)
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
            var normalized = pattern.TrimStart('/');
            foreach (var match in endpoints.Where(e =>
                         string.Equals(e.RoutePattern.RawText?.TrimStart('/'), normalized, StringComparison.OrdinalIgnoreCase)))
            {
                var verbs = match.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>();
                if (verbs.Count == 0 || verbs.Contains(verb, StringComparer.OrdinalIgnoreCase))
                {
                    survivors.Add($"{verb} {pattern}  (registered as: {match.DisplayName})");
                }
            }
        }

        survivors.Should().BeEmpty(
            "these routes were RETIRED by unified-access-control-r2 task 163 (no caller, not published). "
            + "Re-registered routes:\n  " + string.Join("\n  ", survivors));
    }

    [Theory]
    [InlineData("POST", "/api/ai/rag/index/batch")]
    [InlineData("DELETE", "/api/ai/rag/source/16300000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/ai/knowledge/indexes/spaarke-knowledge-index-v2/documents")]
    [InlineData("DELETE", "/api/ai/knowledge/indexes/spaarke-knowledge-index-v2/documents/doc-1")]
    [InlineData("POST", "/api/ai/knowledge/indexes/reindex/16300000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/ai/knowledge/test-search")]
    [InlineData("POST", "/api/admin/knowledge/index-references")]
    [InlineData("POST", "/api/admin/knowledge/index-reference/kb-1")]
    [InlineData("DELETE", "/api/admin/knowledge/index-reference/kb-1")]
    public async Task RetiredRoute_WithAValidBearer_Is404NotRouted(string verb, string path)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        using var request = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb != "GET" && verb != "DELETE")
        {
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"{verb} {path} was retired by task 163; anything else means a handler exists again");
    }

    [Theory]
    [InlineData("GET", "/api/ai/knowledge/indexes/health")]
    [InlineData("POST", "/api/ai/rag/index")]
    [InlineData("DELETE", "/api/ai/rag/chunk-key-1")]
    [InlineData("POST", "/api/ai/rag/index-file")]
    public void SurvivingSiblings_AreStillMapped(string verb, string path)
    {
        // The positive control for the 404s above. An ANONYMOUS 401 no longer proves a route exists: the BFF's
        // authorization FallbackPolicy (UAC-r2 task 167) answers 401 for a request that matches no route too.
        // The endpoint table is the evidence that a surviving sibling is still routed.
        EndpointTable.AssertMapped(_factory, verb, path);
    }
}
