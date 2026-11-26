// CONTRACT — frozen response bytes of POST /api/ai/analysis/create (spaarkeai-mcp-bff-api-r2 task 031, spec FR-06, ADR-008).
// KEEP path: contract.
//
// WHY: task 031 moves the route's create authorization (AnalysisAuthorizationFilter, AnalysisCreate mode) into one shared method that
// the MCP tool spaarke_start_analysis also calls, and moves the handler's RecordCreationService call into one shared create. This file
// was written and green BEFORE that move, and must pass unchanged after it: every status the route answers (201, 400, 403, 409) keeps
// its exact body (correlation ids removed), its content type and, for the 201, its Location and the row it writes.
//
// WHAT RUNS: the REAL MapAnalysisEndpoints in AnalysisAuthHost (the REAL AuthorizationService and RecordCreationService, doubles only at
// the access-data boundary, the privilege seam, the owner resolver and the app-only writer). There is no 404 on this route: no branch
// of the filter or the handler answers one (the POML listed 404; see notes/031-decisions.md).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

public sealed class AnalysisCreateContractBaselineTests
{
    private const string Documents = "sprk_documents";
    private const string Playbooks = "sprk_analysisplaybooks";
    private const AccessRights ReadAppendTo = AccessRights.Read | AccessRights.AppendTo;

    private const string ProblemJson = "application/problem+json";

    private static HttpRequestMessage Create(object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/analysis/create") { Content = JsonContent.Create(body) };
        request.Headers.Add(AnalysisAuthzTestAuthHandler.CallerHeader, "1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return request;
    }

    /// <summary>The body with its correlation ids removed (they differ per request), as compact JSON in the server's member order.</summary>
    private static async Task<string> NormalizedAsync(HttpResponseMessage response)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        node.Remove("traceId");
        node.Remove("correlationId");
        return node.ToJsonString();
    }

    private static async Task AssertFrozenAsync(HttpResponseMessage response, HttpStatusCode status, string contentType, string body)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType?.MediaType.Should().Be(contentType);
        (await NormalizedAsync(response)).Should().Be(body);
    }

    /// <summary>The Web API body the one create wrote, as sorted "key=value" lines, with the new id replaced by a marker.</summary>
    private static string RowOf(Dictionary<string, object?> fields, Guid createdId) =>
        string.Join("\n", fields
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={(f.Value is JsonElement e ? e.GetRawText() : JsonSerializer.Serialize(f.Value))}"))
            .Replace(createdId.ToString("D"), "{id}", StringComparison.OrdinalIgnoreCase);

    // ---- 201 ----------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 baseline 201: Location, body and the written row are frozen")]
    public async Task Create_Allowed_Is201_WithFrozenLocationBodyAndRow()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.Parse("0a0a0a0a-0031-4031-8031-000000000001");
        var playbookId = Guid.Parse("0a0a0a0a-0031-4031-8031-000000000002");
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.SetPlaybook(playbookId, new PlaybookResponse { Id = playbookId, IsPublic = true });

        var response = await host.SendAsync(Create(new { name = "Analysis - NDA.pdf", documentId, playbookId }));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var (table, createdId, fields) = host.AppOnlyCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_analysis");
        response.Headers.Location!.ToString().Should().Be($"/api/ai/analysis/{createdId:D}");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).Should().Be($$"""{"analysisId":"{{createdId:D}}"}""");
        RowOf(fields, createdId).Should().Be(FrozenRow);
    }

    /// <summary>The row the route writes for the 201 case (recorded on the unchanged code, 2026-11-26).</summary>
    private const string FrozenRow = """
        ownerid@odata.bind="/teams(0f0f0f0f-0080-4080-8080-000000000080)"
        sprk_CreatedByPerson@odata.bind="/systemusers(6f0c1a52-0000-4000-8000-000000000162)"
        sprk_DocumentId@odata.bind="/sprk_documents(0a0a0a0a-0031-4031-8031-000000000001)"
        sprk_Playbook@odata.bind="/sprk_analysisplaybooks(0a0a0a0a-0031-4031-8031-000000000002)"
        sprk_name="Analysis - NDA.pdf"
        statuscode=1
        """;

    private const string Forbidden403Prefix =
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"Forbidden","status":403,"detail":"Access denied","reasonCode":""";

    private static string Forbidden403(string reasonCode) => Forbidden403Prefix + "\"" + reasonCode + "\"}";

    // ---- 400 ----------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 baseline 400: a body with no document")]
    public async Task Create_NoDocument_Is400_Frozen()
    {
        await using var host = await AnalysisAuthHost.StartAsync();

        var response = await host.SendAsync(Create(new { name = "A" }));

        await AssertFrozenAsync(response, HttpStatusCode.BadRequest, ProblemJson,
            """{"type":"https://tools.ietf.org/html/rfc7231#section-6.5.1","title":"Bad Request","status":400,"detail":"No document identifier found in request"}""");
    }

    [Fact(DisplayName = "031 baseline 400: a blank name")]
    public async Task Create_BlankName_Is400_Frozen()
    {
        await using var host = await AnalysisAuthHost.StartAsync();

        var response = await host.SendAsync(Create(new { name = " ", documentId = Guid.NewGuid() }));

        await AssertFrozenAsync(response, HttpStatusCode.BadRequest, ProblemJson,
            """{"type":"https://tools.ietf.org/html/rfc7231#section-6.5.1","title":"Bad Request","status":400,"detail":"Analysis name is required."}""");
    }

    // ---- 403 ----------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 baseline 403: no Create privilege on sprk_analysis")]
    public async Task Create_NoCreatePrivilege_Is403_Frozen()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.Probe.Release("prvCreatesprk_analysis");

        var response = await host.SendAsync(Create(new { name = "A", documentId }));

        await AssertFrozenAsync(response, HttpStatusCode.Forbidden, ProblemJson, Forbidden403("sdap.access.deny.insufficient_privilege"));
    }

    [Fact(DisplayName = "031 baseline 403: a document the caller cannot Read and AppendTo (analysis.attach)")]
    public async Task Create_NoAttachRights_Is403_Frozen()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, AccessRights.Read);

        var response = await host.SendAsync(Create(new { name = "A", documentId }));

        await AssertFrozenAsync(response, HttpStatusCode.Forbidden, ProblemJson, Forbidden403("sdap.access.deny.insufficient_rights"));
    }

    [Fact(DisplayName = "031 baseline 403: a private playbook the caller cannot read")]
    public async Task Create_UnreadablePrivatePlaybook_Is403_Frozen()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.SetPlaybook(playbookId, new PlaybookResponse { Id = playbookId, IsPublic = false });

        var response = await host.SendAsync(Create(new { name = "A", documentId, playbookId }));

        await AssertFrozenAsync(response, HttpStatusCode.Forbidden, ProblemJson, Forbidden403("sdap.access.deny.insufficient_rights"));
        host.Access.Calls.Should().Contain(c => c.Set == Playbooks);
    }

    [Fact(DisplayName = "031 baseline 403: a fault while declaring the checks (the playbook lookup throws) is system_failure")]
    public async Task Create_DeclarationFault_Is403SystemFailure_Frozen()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.PlaybookService
            .Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("lookup failed: secret detail"));

        var response = await host.SendAsync(Create(new { name = "A", documentId, playbookId }));

        await AssertFrozenAsync(response, HttpStatusCode.Forbidden, ProblemJson, Forbidden403("sdap.access.error.system_failure"));
        (await response.Content.ReadAsStringAsync()).Should().NotContain("secret detail");
    }

    // ---- 409 ----------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 baseline 409: no owner could be resolved (task 146), nothing written")]
    public async Task Create_OwnerUnresolved_Is409_Frozen()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var documentId = Guid.NewGuid();
        host.Access.Grant(Documents, documentId, ReadAppendTo);
        host.Ownership.TeamId = null;

        var response = await host.SendAsync(Create(new { name = "A", documentId }));

        await AssertFrozenAsync(response, HttpStatusCode.Conflict, ProblemJson,
            """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Record owner unresolved","status":409,"detail":"The analysis was not saved: double: refused.","reasonCode":"record_owner_secure_parent_not_isolated"}""");
        host.AppOnlyCreates.Should().BeEmpty();
    }
}
