using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 188 round 4c (F1): two document decisions that bypassed THE external document
/// narrowing. Scenario: a systemuser flagged <c>sprk_isexternal</c> (a B2B guest) with Read on matter M's documents.
/// <list type="bullet">
/// <item><c>GET /api/documents/{id}/permissions</c> and <c>POST /api/documents/permissions/batch</c> answered from the
/// rights snapshot alone (<c>canPreview</c> / <c>canDownload</c> true for a Restricted document). Now a hidden document
/// gets the answer a document with no rights gets.</item>
/// <item><c>POST /api/workspace/ai/summary</c> with <c>entityType=sprk_document</c> was decided on the Record path (the
/// root grants only), so the playbook ran over a hidden document's name and type. Now the Document path decides: a hidden
/// document is the route's uniform 404, identical to an unreadable one, and the handler never runs.</item>
/// </list>
/// The REAL routes / declaration and filter, the REAL <see cref="AuthorizationService"/> over a stated access seam, and
/// the REAL narrowing over a stated caller and app-only world (<see cref="StatedNarrowingCaller"/>).
/// </summary>
public sealed class DocumentSnapshotAndSummaryNarrowingTests : IAsyncLifetime
{
    private const string CallerOid = "6f0c1a52-0000-4000-8000-0000000188c4";
    private const string Documents = "sprk_documents";

    private WebApplication? _app;
    private HttpClient? _client;

    private RecordingAccessDataSource Access { get; } = new();

    private AppOnlyDocumentWorld World { get; } = new();

    private NarrowingCallerMode Mode { get; set; } = NarrowingCallerMode.Internal;

    /// <summary>How many requests reached the summary handler (a denied one never does).</summary>
    private int _summariesRun;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = PlaybookAuthzTestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = PlaybookAuthzTestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, PlaybookAuthzTestAuthHandler>(PlaybookAuthzTestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(o => o.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("test")));
        builder.Services.AddSingleton<IAccessDataSource>(Access);
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddStatedNarrowingCaller(() => Mode, () => World);
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();

        _app.MapPermissionsEndpoints();
        // The production declaration and filter of POST /api/workspace/ai/summary (MapWorkspaceAiEndpoints wires exactly
        // these two; its route tests pin the wiring) over a handler that only counts.
        _app.MapPost("/api/workspace/ai/summary", (AiSummaryRequest _) =>
            {
                Interlocked.Increment(ref _summariesRun);
                return Results.Ok();
            })
            .RequireAuthorization()
            .AddFinanceAuthorizationFilter(WorkspaceAiEndpoints.ResolveSummaryTargets, FinanceDenial.UniformNotFound);

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add(PlaybookAuthzTestAuthHandler.CallerHeader, CallerOid);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "caller-token");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>A document the caller holds Read and Write on (so a capability answer would be true).</summary>
    private Guid ReadableDocument()
    {
        var id = Guid.NewGuid();
        Access.Grant(Documents, id, AccessRights.Read | AccessRights.Write);
        return id;
    }

    /// <summary>The boolean capability flags and the rights text: the part of the answer that describes access.</summary>
    private static string AccessShape(JsonElement capabilities) =>
        string.Join(",", capabilities.EnumerateObject()
            .Where(p => p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False || p.Name == "accessRights")
            .Select(p => $"{p.Name}={p.Value}"));

    private async Task<JsonElement> GetPermissionsAsync(Guid documentId)
    {
        var response = await _client!.GetAsync($"/api/documents/{documentId:D}/permissions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private async Task<JsonElement[]> GetBatchPermissionsAsync(params Guid[] documentIds)
    {
        var response = await _client!.PostAsJsonAsync("/api/documents/permissions/batch",
            new { documentIds = documentIds.Select(id => id.ToString("D")).ToList() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("permissions").EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    // ── GET /api/documents/{id}/permissions and POST /api/documents/permissions/batch ─────────────────────────────

    [Theory(DisplayName = "Task 188 r4c (F1): permissions on a document the narrowing hides are the answer a document with no rights gets")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task Permissions_ExternalCaller_HiddenDocument_IsTheNoRightsAnswer(string @case)
    {
        var hidden = ReadableDocument();
        Mode = HiddenDocumentCases.Arrange(World, hidden, @case);
        var noRights = Guid.NewGuid();

        var answer = await GetPermissionsAsync(hidden);

        answer.GetProperty("canPreview").GetBoolean().Should().BeFalse();
        answer.GetProperty("canDownload").GetBoolean().Should().BeFalse();
        AccessShape(answer).Should().Be(AccessShape(await GetPermissionsAsync(noRights)));
        World.Calls.Should().Be(1, "the no-rights document is never sent to the narrowing");
    }

    [Theory(DisplayName = "Task 188 r4c (F1): batch permissions — a hidden document is the no-rights answer beside a visible one, in ONE narrowing call")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task BatchPermissions_ExternalCaller_HiddenDocument_IsTheNoRightsAnswer(string @case)
    {
        var hidden = ReadableDocument();
        Mode = HiddenDocumentCases.Arrange(World, hidden, @case);
        var visible = ReadableDocument();
        World.Document(visible);
        var noRights = Guid.NewGuid();

        var answers = await GetBatchPermissionsAsync(hidden, visible, noRights);

        answers[0].GetProperty("documentId").GetString().Should().Be(hidden.ToString());
        AccessShape(answers[0]).Should().Be(AccessShape(answers[2]), "hidden is indistinguishable from no rights");
        answers[0].GetProperty("canDownload").GetBoolean().Should().BeFalse();
        if (@case is not HiddenDocumentCases.NarrowingFault)
        {
            answers[1].GetProperty("canDownload").GetBoolean().Should().BeTrue("the narrowing shows the visible document");
        }

        World.Calls.Should().Be(1);
    }

    [Fact(DisplayName = "Task 188 r4c (F1): must not fire — an internal caller's permissions are the rights; no narrowing call, no app-only read")]
    public async Task Permissions_InternalCaller_IsNotNarrowed()
    {
        var restricted = ReadableDocument();
        HiddenDocumentCases.Arrange(World, restricted, HiddenDocumentCases.OwnRestricted);
        Mode = NarrowingCallerMode.Internal;

        (await GetPermissionsAsync(restricted)).GetProperty("canDownload").GetBoolean().Should().BeTrue();
        (await GetBatchPermissionsAsync(restricted))[0].GetProperty("canDownload").GetBoolean().Should().BeTrue();
        World.Calls.Should().Be(0);
        World.AllReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 188 r4c (F1): must not fire — an external caller's permissions on a visible document are the rights")]
    public async Task Permissions_ExternalCaller_VisibleDocument_IsTheRights()
    {
        var visible = ReadableDocument();
        World.Document(visible);
        Mode = NarrowingCallerMode.External;

        var answer = await GetPermissionsAsync(visible);

        answer.GetProperty("canPreview").GetBoolean().Should().BeTrue();
        answer.GetProperty("canDownload").GetBoolean().Should().BeTrue();
        World.Calls.Should().Be(1);
    }

    // ── POST /api/workspace/ai/summary (entityType sprk_document) ──────────────────────────────────────────────────

    private Task<HttpResponseMessage> SummarizeAsync(string entityType, Guid entityId) =>
        _client!.PostAsJsonAsync("/api/workspace/ai/summary", new { entityType, entityId, context = (string?)null });

    [Theory(DisplayName = "Task 188 r4c (F1): a summary of a document the narrowing hides is the uniform 404 of an unreadable one, and the playbook never runs")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task Summary_ExternalCaller_HiddenDocument_IsTheUniform404(string @case)
    {
        var hidden = ReadableDocument();
        Mode = HiddenDocumentCases.Arrange(World, hidden, @case);

        var denied = await SummarizeAsync("sprk_document", hidden);
        var unreadable = await SummarizeAsync("sprk_document", Guid.NewGuid());

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(denied))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unreadable));
        _summariesRun.Should().Be(0);
        World.Calls.Should().Be(1);
        Access.Calls.Should().Contain(new AccessCall(AccessPath.Document, Documents, hidden, HasToken: true),
            "a document is decided on the Document path");
        Access.Calls.Should().NotContain(c => c.Path == AccessPath.Record, "never on the Record path");
    }

    [Fact(DisplayName = "Task 188 r4c (F1): must not fire — an internal caller's document summary runs; no narrowing call, no app-only read")]
    public async Task Summary_InternalCaller_IsNotNarrowed()
    {
        var restricted = ReadableDocument();
        HiddenDocumentCases.Arrange(World, restricted, HiddenDocumentCases.OwnRestricted);
        Mode = NarrowingCallerMode.Internal;

        (await SummarizeAsync("sprk_document", restricted)).StatusCode.Should().Be(HttpStatusCode.OK);
        _summariesRun.Should().Be(1);
        World.Calls.Should().Be(0);
        World.AllReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 188 r4c (F1): must not fire — an external caller's summary of a visible document runs; a matter stays on the Record path")]
    public async Task Summary_ExternalCaller_VisibleDocument_Runs_AndAMatterIsARecordCheck()
    {
        var visible = ReadableDocument();
        World.Document(visible);
        Mode = NarrowingCallerMode.External;
        var matter = Guid.NewGuid();
        Access.Grant("sprk_matters", matter, AccessRights.Read);

        (await SummarizeAsync("sprk_document", visible)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SummarizeAsync("sprk_matter", matter)).StatusCode.Should().Be(HttpStatusCode.OK);
        _summariesRun.Should().Be(2);
        World.Calls.Should().Be(1, "the matter is not a document");
        Access.Calls.Should().Contain(new AccessCall(AccessPath.Record, "sprk_matters", matter, HasToken: true));
    }
}
