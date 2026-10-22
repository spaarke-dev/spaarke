using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// unified-access-control-r2 task 188 round 4a (security pass 2, F1-3): <c>GET /api/ai/analysis/{analysisId}</c>, the chat
/// analysis host and <c>GET /api/ai/chat/context-mappings/analysis/{analysisId}</c> returned content DERIVED from a document
/// (WorkingDocument, FinalOutput, ChatHistory, the analysis's chat context) for an analysis whose source document or output
/// file the external document narrowing hides. A caller who is not a resolved internal systemuser now gets each route's
/// existing uniform not-found instead, through ONE helper
/// (<see cref="AnalysisAuthorizationFilter.IsAnalysisDocumentHiddenFromCallerAsync"/>).
/// </summary>
/// <remarks>
/// The real routes and filters over <see cref="AnalysisAuthHost"/> (the REAL authorization stack at the access-data
/// boundary), the REAL caller resolver over a stated systemuser row, and the REAL predicate behind the real app-only seam
/// (<see cref="AnalysisAuthHost.World"/>). Read is granted on both document anchors, so a 404 here is the narrowing.
/// </remarks>
public class AnalysisDerivedContentNarrowingTests
{
    private const string Documents = "sprk_documents";
    private const int Restricted = 100000002;
    private const int OpenThread = 100000000;

    public static TheoryData<string> MustFireCases()
    {
        var data = new TheoryData<string>();
        foreach (var @case in new[]
                 {
                     "source-own-restricted", "source-internal-only-communication", "source-restricted-parent",
                     "output-file-restricted", "regarding-document-restricted", "narrowing-fault", "lookup-fault", "link-read-fault",
                 })
            data.Add(@case);
        return data;
    }

    /// <summary>A visible document: Standard, an Open shareable communication, and a Standard parent.</summary>
    private static Guid Visible(AnalysisAuthHost host)
    {
        var world = host.World;
        var thread = Guid.NewGuid();
        var communication = Guid.NewGuid();
        world.Thread(thread, OpenThread);
        world.Communication(communication, internalOnly: false, thread);
        return world.Document(Guid.NewGuid(), communication: communication, parent: world.Document(Guid.NewGuid()));
    }

    /// <summary>
    /// Seeds an analysis whose source document and output file the caller may Read, shaped by <paramref name="case"/>, with
    /// a handler payload that carries derived content.
    /// </summary>
    private static Guid Arrange(AnalysisAuthHost host, string @case)
    {
        var world = host.World;
        Guid source;
        Guid? regarding = null;
        var output = Visible(host);
        switch (@case)
        {
            case "source-own-restricted":
                source = world.Document(Guid.NewGuid(), permission: Restricted);
                break;
            case "source-internal-only-communication":
                var communication = Guid.NewGuid();
                var thread = Guid.NewGuid();
                world.Thread(thread, OpenThread);
                world.Communication(communication, internalOnly: true, thread);
                source = world.Document(Guid.NewGuid(), communication: communication);
                break;
            case "source-restricted-parent":
                source = world.Document(Guid.NewGuid(), parent: world.Document(Guid.NewGuid(), permission: Restricted));
                break;
            case "output-file-restricted":
                source = Visible(host);
                output = world.Document(Guid.NewGuid(), permission: Restricted);
                break;
            case "regarding-document-restricted":
                // Task 188 round 4b: the typed regarding document (sprk_regardingdocument) is the third link.
                source = Visible(host);
                regarding = world.Document(Guid.NewGuid(), permission: Restricted);
                break;
            case "narrowing-fault":
                source = Visible(host);
                world.Faults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;
                break;
            case "lookup-fault":
                host.CallerMode = NarrowingCallerMode.LookupFault;
                source = world.Document(Guid.NewGuid(), permission: Restricted);
                break;
            case "link-read-fault":
            case "visible":
            case "internal-restricted":
                source = @case == "internal-restricted" ? world.Document(Guid.NewGuid(), permission: Restricted) : Visible(host);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }

        var analysisId = Guid.NewGuid();
        var links = new Dictionary<string, (string Target, Guid Id)>
        {
            ["sprk_documentid"] = ("sprk_document", source),
            ["sprk_outputfileid"] = ("sprk_document", output),
        };
        if (regarding is { } regardingId)
        {
            links["sprk_regardingdocument"] = ("sprk_document", regardingId);
            host.Access.Grant(Documents, regardingId, AccessRights.Read);
        }

        host.SeedAnalysis(analysisId, links);
        host.Access.Grant(Documents, source, AccessRights.Read);
        host.Access.Grant(Documents, output, AccessRights.Read);
        if (@case == "link-read-fault")
        {
            // The narrowing's own app-only read of the two links faults; the read rule's anchor read still succeeds.
            host.EntityService
                .Setup(e => e.RetrieveAsync("sprk_analysis", analysisId,
                    It.Is<string[]>(c => c.SequenceEqual(AnalysisAuthorizationFilter.AnalysisDocumentColumns)), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("simulated app-only read failure (test)"));
        }

        host.Orchestration
            .Setup(o => o.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult { Id = analysisId, Status = "Completed", WorkingDocument = "# derived" });
        return analysisId;
    }

    private static HttpRequestMessage Get(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(AnalysisAuthzTestAuthHandler.CallerHeader, "1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        return request;
    }

    private static async Task<AnalysisAuthHost> ExternalHostAsync()
    {
        var host = await AnalysisAuthHost.StartAsync();
        host.CallerMode = NarrowingCallerMode.External;
        return host;
    }

    private static void VerifyResolverNeverConsulted(AnalysisAuthHost host) =>
        host.EntityService.Verify(
            e => e.RetrieveAsync("sprk_analysisoutput", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "the chat-context resolver (and its cache) is never consulted for a hidden analysis");

    // ── GET /api/ai/analysis/{analysisId} ─────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 188 r4a F1-3 (GET analysis): a hidden source document or output file is the uniform 404; no derived content")]
    [MemberData(nameof(MustFireCases))]
    public async Task GetAnalysis_ExternalCaller_HiddenDocument_IsUniform404(string @case)
    {
        await using var host = await ExternalHostAsync();
        var analysisId = Arrange(host, @case);

        var response = await host.SendAsync(Get($"/api/ai/analysis/{analysisId}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("# derived");
        host.Orchestration.Verify(o => o.GetAnalysisAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory(DisplayName = "Task 188 r4a F1-3 (GET analysis): must not fire — an internal user with a Restricted source, an external caller with visible documents")]
    [InlineData(NarrowingCallerMode.Internal, "internal-restricted", 0)]
    // Round 4b (F1-2): the analysis-read rule's two anchor-document checks also go through the helper (one narrowing call
    // each), then the derived-content check: 3 calls for an external caller.
    [InlineData(NarrowingCallerMode.External, "visible", 3)]
    public async Task GetAnalysis_MustNotFire(NarrowingCallerMode mode, string @case, int narrowingCalls)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        host.CallerMode = mode;
        var analysisId = Arrange(host, @case);

        var response = await host.SendAsync(Get($"/api/ai/analysis/{analysisId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("# derived");
        host.World.Calls.Should().Be(narrowingCalls);
    }

    [Fact(DisplayName = "Task 188 r4a F1-3 (GET analysis): an internal user makes no app-only read of the analysis's document links")]
    public async Task GetAnalysis_InternalUser_ReadsNoLinks()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Arrange(host, "internal-restricted");

        (await host.SendAsync(Get($"/api/ai/analysis/{analysisId}"))).StatusCode.Should().Be(HttpStatusCode.OK);

        host.EntityService.Verify(e => e.RetrieveAsync("sprk_analysis", analysisId,
            It.Is<string[]>(c => c.SequenceEqual(AnalysisAuthorizationFilter.AnalysisDocumentColumns)), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "Task 188 r4a F1-3 (GET analysis): a caller the read rule refuses costs no narrowing call (the rule runs first)")]
    public async Task GetAnalysis_ReadRuleRefuses_NoNarrowing()
    {
        await using var host = await ExternalHostAsync();
        var analysisId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new Dictionary<string, (string Target, Guid Id)>
        {
            ["sprk_documentid"] = ("sprk_document", host.World.Document(Guid.NewGuid(), permission: Restricted)),
        });

        var response = await host.SendAsync(Get($"/api/ai/analysis/{analysisId}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.World.Calls.Should().Be(0);
    }

    // ── the chat analysis host (AnalysisAuthorizationFilter.IsAnalysisReadableAsync) ─────────────────────────────

    [Theory(DisplayName = "Task 188 r4a F1-3 (chat analysis host): an analysis with a hidden document is not readable as a chat host")]
    [MemberData(nameof(MustFireCases))]
    public async Task ChatAnalysisHost_ExternalCaller_HiddenDocument_IsNotReadable(string @case)
    {
        await using var host = await ExternalHostAsync();
        var analysisId = Arrange(host, @case);

        var readable = await host.WithCallerContextAsync(ctx => AnalysisAuthorizationFilter.IsAnalysisReadableAsync(ctx, analysisId, null));

        readable.Should().BeFalse();
    }

    [Theory(DisplayName = "Task 188 r4a F1-3 (chat analysis host): must not fire — an internal user with a Restricted source, an external caller with visible documents")]
    [InlineData(NarrowingCallerMode.Internal, "internal-restricted", 0)]
    // Round 4b (F1-2): as GetAnalysis_MustNotFire, the two anchor-document checks plus the derived-content check.
    [InlineData(NarrowingCallerMode.External, "visible", 3)]
    public async Task ChatAnalysisHost_MustNotFire(NarrowingCallerMode mode, string @case, int narrowingCalls)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        host.CallerMode = mode;
        var analysisId = Arrange(host, @case);

        var readable = await host.WithCallerContextAsync(ctx => AnalysisAuthorizationFilter.IsAnalysisReadableAsync(ctx, analysisId, null));

        readable.Should().BeTrue();
        host.World.Calls.Should().Be(narrowingCalls);
    }

    // ── GET /api/ai/chat/context-mappings/analysis/{analysisId} ───────────────────────────────────────────────────

    [Theory(DisplayName = "Task 188 r4a F1-3 (context-mappings): a hidden analysis answers the route's 404 before the resolver runs")]
    [MemberData(nameof(MustFireCases))]
    public async Task ContextMappings_ExternalCaller_HiddenDocument_Is404_ResolverNeverRuns(string @case)
    {
        await using var host = await ExternalHostAsync();
        var analysisId = Arrange(host, @case);

        var response = await host.SendAsync(Get($"/api/ai/chat/context-mappings/analysis/{analysisId}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        VerifyResolverNeverConsulted(host);
    }

    [Theory(DisplayName = "Task 188 r4a F1-3 (context-mappings): must not fire — the resolver is consulted for an internal user and for visible documents")]
    [InlineData(NarrowingCallerMode.Internal, "internal-restricted", 0)]
    [InlineData(NarrowingCallerMode.External, "visible", 1)]
    public async Task ContextMappings_MustNotFire(NarrowingCallerMode mode, string @case, int narrowingCalls)
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        host.CallerMode = mode;
        var analysisId = Arrange(host, @case);

        await host.SendAsync(Get($"/api/ai/chat/context-mappings/analysis/{analysisId}"));

        host.EntityService.Verify(
            e => e.RetrieveAsync("sprk_analysisoutput", analysisId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once, "the narrowing let the request through to the resolver");
        host.World.Calls.Should().Be(narrowingCalls);
    }
}
