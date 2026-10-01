using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Visualization;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// D-032-2 (spaarkeai-word-add-in-r1 task 033) — <c>VisualizationEndpoints.AuthorizeRowsAsync</c> drops
/// result rows past <c>MaxDocumentAuthorizationChecks</c> (100) UNEVALUATED, which is the correct
/// fail-closed behaviour (task 032 §5). What task 032 explicitly deferred (§6 item 5, "filed for
/// notes/defer-issues.md") is that the drop was SILENT: <c>GraphMetadata</c> carried no warning, so a
/// page truncated by the budget is indistinguishable from "these are all the related documents there
/// are" — the same shape of problem cross-record document search already solved with a
/// <c>PARTIAL_RESULTS</c> warning (<c>SemanticSearchEndpoints.AuthorizeRowsByParentAsync</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reachable, not theoretical.</b> <c>notes/032-authorization-hardening.md</c> §5: five hardcoded
/// relationship queries at <c>TopCount = 50</c> each can present up to 250 candidate result rows against
/// this 100-check budget — comfortably over the budget on a well-connected source document.
/// </para>
/// <para>
/// <b>Why the handler is exercised directly.</b> Same reasoning as
/// <see cref="VisualizationRowAuthorizationContractTests"/> (the class this file deliberately does NOT
/// reuse members from, per the project's "configure test classes locally" boundary — this file owns its
/// own graph builder, caller context, and stub boundaries): <c>VisualizationEndpoints.GetRelatedDocuments</c>
/// is <c>internal</c> precisely so a test proves something about the SHIPPED handler, not a
/// re-implementation of its branches. Only <see cref="IVisualizationService"/> (the search engine) and
/// <see cref="IAccessDataSource"/> (what Dataverse would answer) are substituted — the real
/// <c>AiAuthorizationService</c>, the real fail-closed budget/trim logic in
/// <c>VisualizationEndpoints.AuthorizeRowsAsync</c>, and the real warning-construction code all run.
/// </para>
/// <para>
/// <b>Fail-closed drop is UNCHANGED — only the silence is the assertion here.</b> Every test below
/// grants Read on every candidate document, so any row missing from the response is proof of the
/// (correct, unchanged) budget drop — not of a denial — and the warning is what makes that drop legible
/// rather than looking like "nothing else matched".
/// </para>
/// </remarks>
[Trait("category", "authorization")]
public sealed class VisualizationBudgetWarningContractTests
{
    private const string TenantId = "aaaaaaaa-2222-3333-4444-555555555555";
    private const string CallerOid = "cccccccc-8888-7777-6666-555555555555";
    private static readonly Guid SourceDocument = Guid.Parse("11111111-0000-0000-0000-0000000000f1");

    /// <summary>
    /// One over MaxDocumentAuthorizationChecks (100) — the minimal reproduction of the reachable
    /// scenario in notes/032-authorization-hardening.md §5 (up to 250 candidates against a 100 budget).
    /// </summary>
    private const int CandidateRowCount = 101;

    [Fact]
    public async Task WhenCandidateRowsExceedTheBudget_MetadataCarriesAPartialResultsWarning()
    {
        var access = new AllowAllAccessDataSource();
        var graph = GraphWithManyCandidates(CandidateRowCount);

        var result = await InvokeRelatedAsync(graph, access);
        var body = OkBody(result);

        body.Metadata.Warnings.Should().NotBeNull(
            "a page truncated by the authorization budget must not look identical to a complete result");
        body.Metadata.Warnings.Should().ContainSingle(w => w.Code == SearchWarningCode.PartialResults,
            "the warning must use the SAME code cross-record document search's AuthorizeRowsByParentAsync "
            + "emits, so a client that already recognizes PARTIAL_RESULTS handles this surface too");

        // The fail-closed drop itself is unchanged: with 101 candidates and a 100-check budget, at least
        // one permitted document is still withheld purely because it was never evaluated.
        body.Nodes.Count(n => n.Type == NodeTypes.Related).Should().BeLessThan(CandidateRowCount,
            "the budget drop must still happen — this test proves the WARNING, not a budget increase");
    }

    [Fact]
    public async Task WhenCandidateRowsAreWithinTheBudget_NoWarningIsAdded()
    {
        // Control: same shape, comfortably under the 100-check budget. A warning here would be noise —
        // exactly the false-positive SemanticSearchEndpoints' own remarks warn against for an
        // "any row withheld" definition, which this endpoint does not use either.
        var access = new AllowAllAccessDataSource();
        var graph = GraphWithManyCandidates(candidateCount: 10);

        var result = await InvokeRelatedAsync(graph, access);
        var body = OkBody(result);

        body.Metadata.Warnings.Should().BeNull(
            "the budget was never reached, so nothing was left unevaluated — a warning here would be noise");
        body.Nodes.Count(n => n.Type == NodeTypes.Related).Should().Be(10,
            "every candidate was within budget and permitted, so none should have been dropped");
    }

    [Fact]
    public async Task WhenSomeRowsAreDeniedButTheBudgetIsNotExhausted_NoWarningIsAdded()
    {
        // Control: proves the warning is keyed to BUDGET EXHAUSTION, not to "any row was withheld at
        // all" (a denial, unlike a never-evaluated row, is a definite answer, not an unknown).
        var access = new AllowAllAccessDataSource();
        access.DenyDocument(DeniedDocument);
        var graph = GraphWithExplicitDocuments([DeniedDocument, PermittedDocument]);

        var result = await InvokeRelatedAsync(graph, access);
        var body = OkBody(result);

        body.Nodes.Select(n => n.Id).Should().NotContain(DeniedDocument.ToString());
        body.Nodes.Select(n => n.Id).Should().Contain(PermittedDocument.ToString());
        body.Metadata.Warnings.Should().BeNull(
            "both candidates were evaluated and one was correctly denied — that is a complete, honest "
            + "answer, not a truncated one, so no PARTIAL_RESULTS warning belongs here");
    }

    private static readonly Guid DeniedDocument = Guid.Parse("22222222-0000-0000-0000-0000000000d1");
    private static readonly Guid PermittedDocument = Guid.Parse("33333333-0000-0000-0000-0000000000f1");

    // =====================================================================================
    // Helpers — local to this file (project boundary rule: no shared fixture/factory edited)
    // =====================================================================================

    private static DocumentGraphResponse GraphWithManyCandidates(int candidateCount)
    {
        var nodes = new List<DocumentNode> { Node(SourceDocument.ToString(), NodeTypes.Source, 0) };
        var edges = new List<DocumentEdge>();

        for (var i = 0; i < candidateCount; i++)
        {
            // Deterministic, distinct GUIDs: 44444444-0000-0000-0000-{i:D12}.
            var id = new Guid($"44444444-0000-0000-0000-{i:D12}");
            nodes.Add(Node(id.ToString(), NodeTypes.Related, 1));
            edges.Add(Edge($"{SourceDocument}-{id}", SourceDocument.ToString(), id.ToString()));
        }

        return new DocumentGraphResponse
        {
            Nodes = nodes,
            Edges = edges,
            Metadata = new GraphMetadata
            {
                SourceDocumentId = SourceDocument.ToString(),
                TenantId = TenantId,
                TotalResults = candidateCount,
                NodesPerLevel = [1, 0, candidateCount],
                MaxDepthReached = 1,
            },
        };
    }

    private static DocumentGraphResponse GraphWithExplicitDocuments(IReadOnlyList<Guid> documentIds)
    {
        var nodes = new List<DocumentNode> { Node(SourceDocument.ToString(), NodeTypes.Source, 0) };
        var edges = new List<DocumentEdge>();

        foreach (var id in documentIds)
        {
            nodes.Add(Node(id.ToString(), NodeTypes.Related, 1));
            edges.Add(Edge($"{SourceDocument}-{id}", SourceDocument.ToString(), id.ToString()));
        }

        return new DocumentGraphResponse
        {
            Nodes = nodes,
            Edges = edges,
            Metadata = new GraphMetadata
            {
                SourceDocumentId = SourceDocument.ToString(),
                TenantId = TenantId,
                TotalResults = documentIds.Count,
                NodesPerLevel = [1, 0, documentIds.Count],
                MaxDepthReached = 1,
            },
        };
    }

    private static DocumentNode Node(string id, string type, int depth) => new()
    {
        Id = id,
        Type = type,
        Depth = depth,
        Data = new DocumentNodeData { Label = id, DocumentType = "Contract" },
    };

    private static DocumentEdge Edge(string id, string source, string target) => new()
    {
        Id = id,
        Source = source,
        Target = target,
        Data = new DocumentEdgeData { RelationshipType = RelationshipTypes.Semantic },
    };

    private static async Task<IResult> InvokeRelatedAsync(
        DocumentGraphResponse graph, AllowAllAccessDataSource access, VisualizationQueryParameters? query = null)
    {
        var service = new StubVisualizationService(graph);

        return await VisualizationEndpoints.GetRelatedDocuments(
            SourceDocument,
            query ?? new VisualizationQueryParameters(),
            CallerContext(),
            service,
            new AiAuthorizationService(access, NullLogger<AiAuthorizationService>.Instance),
            NullLogger<Program>.Instance,
            CancellationToken.None);
    }

    private static DefaultHttpContext CallerContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", TenantId), new Claim("oid", CallerOid)], "Test")),
        };
        httpContext.Request.Headers.Authorization = "Bearer test-caller-token";
        httpContext.Items[VisualizationAuthorization.HttpContextItemsKey] = new VisualizationAuthorization
        {
            RequiresPerRowDocumentAuthorization = true,
            Subject = VisualizationAuthorizationSubject.SourceDocument,
        };
        return httpContext;
    }

    private static DocumentGraphResponse OkBody(IResult result)
    {
        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(200,
            "the budget drop is a trim, not a request-level denial");
        return result.Should().BeAssignableTo<IValueHttpResult>().Which.Value
            .Should().BeOfType<DocumentGraphResponse>().Subject;
    }

    /// <summary>
    /// Grants Read on every document by default — every row missing from a response in this file is
    /// therefore proof of the budget drop, never of a denial the stub introduced. One document may be
    /// explicitly denied via <see cref="DenyDocument"/> for the control test that distinguishes
    /// "denied" from "never evaluated".
    /// </summary>
    private sealed class AllowAllAccessDataSource : IAccessDataSource
    {
        private readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);

        public void DenyDocument(Guid documentId) => _denied.Add(documentId.ToString());

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = _denied.Contains(resourceId) ? AccessRights.None : AccessRights.Read,
            });

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken,
            CancellationToken ct = default) =>
            throw new NotSupportedException(
                "Visualization rows are sprk_document rows and must be authorized as documents.");
    }

    /// <summary>Stands in for the search engine at the module boundary — returns the seeded graph as-is.</summary>
    private sealed class StubVisualizationService(DocumentGraphResponse graph) : IVisualizationService
    {
        public Task<DocumentGraphResponse> GetRelatedDocumentsAsync(
            Guid documentId, VisualizationOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(graph);

        public Task<ContentUploadResult> IndexTemporaryContentAsync(
            Stream fileStream, string fileName, string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContentUploadResult { Success = true, DocumentId = Guid.NewGuid().ToString() });
    }
}
