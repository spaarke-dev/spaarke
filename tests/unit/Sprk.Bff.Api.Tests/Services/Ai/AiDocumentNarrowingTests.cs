using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Visualization;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// unified-access-control-r2 task 188 round 4a (security pass 2, F1-1): <see cref="IAiAuthorizationService"/> answered
/// "may this caller Read this document" only. Its callers download content into the AI (chat document context, analysis
/// execute) or return it (RAG search chunks, visualization rows), so a systemuser flagged <c>sprk_isexternal</c> with Read
/// on a matter through a share got an internal-only email's .eml, a Restricted document or a document under a Restricted
/// parent. Now every id Read allows goes through THE external document narrowing for a caller who is not a resolved
/// internal systemuser.
/// </summary>
/// <remarks>
/// The REAL <see cref="AiAuthorizationService"/> (Read granted on every id), the REAL caller resolver over a stated
/// systemuser row, and the REAL predicate behind the real app-only seam (<see cref="AppOnlyDocumentWorld"/>). Each site is
/// driven through its own production code (filter, trim or handler), not a copy.
/// </remarks>
public class AiDocumentNarrowingTests
{
    private const int Restricted = 100000002;
    private const int OpenThread = 100000000;
    private const string UnreadableReason = "Insufficient permissions (Read access required)";

    private readonly AppOnlyDocumentWorld _world = new();
    private NarrowingCallerMode _mode = NarrowingCallerMode.External;

    // ── the world ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The must-fire cases: three hidden kinds, a narrowing fault and two lookup faults.</summary>
    private static readonly string[] Cases =
    {
        "own-restricted", "internal-only-communication", "restricted-parent", "narrowing-fault", "lookup-fault", "external-flag-fault",
    };

    public static TheoryData<string> MustFireCases()
    {
        var data = new TheoryData<string>();
        foreach (var @case in Cases)
            data.Add(@case);
        return data;
    }

    /// <summary>
    /// Arranges <paramref name="case"/> and returns the document the case must withhold. For <c>narrowing-fault</c> the
    /// document is visible in the world, but the app-only read faults, so every document is withheld.
    /// </summary>
    private Guid Arrange(string @case)
    {
        switch (@case)
        {
            case "own-restricted":
                return _world.Document(Guid.NewGuid(), permission: Restricted);
            case "internal-only-communication":
                var communication = Guid.NewGuid();
                var thread = Guid.NewGuid();
                _world.Thread(thread, OpenThread);
                _world.Communication(communication, internalOnly: true, thread);
                return _world.Document(Guid.NewGuid(), communication: communication);
            case "restricted-parent":
                var parent = _world.Document(Guid.NewGuid(), permission: Restricted);
                return _world.Document(Guid.NewGuid(), parent: parent);
            case "narrowing-fault":
                var visible = Visible();
                _world.Faults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;
                return visible;
            case "lookup-fault":
                _mode = NarrowingCallerMode.LookupFault;
                return _world.Document(Guid.NewGuid(), permission: Restricted);
            case "external-flag-fault":
                _mode = NarrowingCallerMode.ExternalFlagFault;
                return _world.Document(Guid.NewGuid(), permission: Restricted);
            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }
    }

    /// <summary>A visible document: Standard, an Open shareable communication, and a Standard parent.</summary>
    private Guid Visible()
    {
        var thread = Guid.NewGuid();
        var communication = Guid.NewGuid();
        _world.Thread(thread, OpenThread);
        _world.Communication(communication, internalOnly: false, thread);
        var parent = _world.Document(Guid.NewGuid());
        return _world.Document(Guid.NewGuid(), communication: communication, parent: parent);
    }

    /// <summary>A narrowing fault withholds every document, so a visible companion is withheld too.</summary>
    private static bool FaultsAll(string @case) => @case == "narrowing-fault";

    private DefaultHttpContext Caller(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddStatedNarrowingCaller(() => _mode, () => _world);
        extra?.Invoke(services);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("oid", "18800000-0000-0000-0000-0000000000a1"), new Claim("tid", "18800000-0000-0000-0000-0000000000f1") },
                "Test")),
            TraceIdentifier = "t188r4a",
        };
        httpContext.Request.Headers.Authorization = "Bearer caller-token";
        return httpContext;
    }

    /// <summary>The REAL service; Read is granted on every id except <paramref name="unreadable"/>.</summary>
    private static AiAuthorizationService Service(params Guid[] unreadable)
    {
        var access = new Mock<IAccessDataSource>();
        access.Setup(a => a.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string user, string resource, string? _, CancellationToken _) => new AccessSnapshot
            {
                UserId = user,
                ResourceId = resource,
                AccessRights = unreadable.Any(u => u.ToString() == resource) ? AccessRights.None : AccessRights.Read,
                CachedAt = DateTimeOffset.UtcNow,
            });
        return new AiAuthorizationService(access.Object, NullLogger<AiAuthorizationService>.Instance);
    }

    // ── IAiAuthorizationService.AuthorizeAsync ────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Task 188 r4a F1-1: AuthorizeAsync denies a flagged-external caller a document the narrowing hides, like an unreadable one")]
    [MemberData(nameof(MustFireCases))]
    public async Task AuthorizeAsync_ExternalCaller_HiddenDocument_IsDenied(string @case)
    {
        var hidden = Arrange(@case);

        var result = await Service().AuthorizeAsync(Caller().User, new[] { hidden }, Caller());

        result.Success.Should().BeFalse();
        result.AuthorizedDocumentIds.Should().BeEmpty();
        result.Reason.Should().Be($"{hidden}: {UnreadableReason}",
            "a hidden document is denied with the SAME shape as one the caller cannot Read");
    }

    [Theory(DisplayName = "Task 188 r4a F1-1: AuthorizeAsync keeps a visible document beside a hidden one (partial), except on a narrowing fault (deny all)")]
    [MemberData(nameof(MustFireCases))]
    public async Task AuthorizeAsync_ExternalCaller_HiddenAndVisible_KeepsOnlyTheVisible(string @case)
    {
        var hidden = Arrange(@case);
        var visible = Visible();
        var httpContext = Caller();

        var result = await Service().AuthorizeAsync(httpContext.User, new[] { hidden, visible }, httpContext);

        if (FaultsAll(@case))
        {
            result.Success.Should().BeFalse("a narrowing that cannot decide denies every document (fail closed)");
            result.AuthorizedDocumentIds.Should().BeEmpty();
        }
        else
        {
            result.AuthorizedDocumentIds.Should().Equal(visible);
            result.Reason.Should().Be("Access denied to 1 of 2 documents");
        }

        _world.Calls.Should().Be(1, "ONE narrowing call per request");
    }

    [Fact(DisplayName = "Task 188 r4a F1-1: a hidden id and an unreadable id get byte-identical denials")]
    public async Task AuthorizeAsync_HiddenAndUnreadable_AreIndistinguishable()
    {
        var hidden = Arrange("own-restricted");
        var unreadable = Visible();
        var service = Service(unreadable);

        var hiddenResult = await service.AuthorizeAsync(Caller().User, new[] { hidden }, Caller());
        var unreadableResult = await service.AuthorizeAsync(Caller().User, new[] { unreadable }, Caller());

        hiddenResult.Reason!.Replace(hidden.ToString(), "{id}").Should().Be(unreadableResult.Reason!.Replace(unreadable.ToString(), "{id}"));
        hiddenResult.Success.Should().Be(unreadableResult.Success);
    }

    [Fact(DisplayName = "Task 188 r4a F1-1: an internal user is not narrowed — a Restricted document stays authorized and nothing is read")]
    public async Task AuthorizeAsync_InternalUser_IsNotNarrowed()
    {
        var restricted = Arrange("own-restricted");
        _mode = NarrowingCallerMode.Internal;

        var result = await Service().AuthorizeAsync(Caller().User, new[] { restricted }, Caller());

        result.Success.Should().BeTrue();
        result.AuthorizedDocumentIds.Should().Equal(restricted);
        _world.Calls.Should().Be(0, "an internal systemuser makes no narrowing call");
        _world.AllReads.Should().BeEmpty("and no app-only read");
    }

    [Fact(DisplayName = "Task 188 r4a F1-1: an external caller keeps a visible document")]
    public async Task AuthorizeAsync_ExternalCaller_VisibleDocument_IsAuthorized()
    {
        var visible = Visible();

        var result = await Service().AuthorizeAsync(Caller().User, new[] { visible }, Caller());

        result.Success.Should().BeTrue();
        result.AuthorizedDocumentIds.Should().Equal(visible);
        _world.Calls.Should().Be(1);
    }

    [Fact(DisplayName = "Task 188 r4a F1-1: a document the caller cannot Read is never sent to the narrowing")]
    public async Task AuthorizeAsync_UnreadableIds_AreNotNarrowed()
    {
        var unreadable = Visible();

        var result = await Service(unreadable).AuthorizeAsync(Caller().User, new[] { unreadable }, Caller());

        result.Success.Should().BeFalse();
        _world.Calls.Should().Be(0, "the narrowing runs over the ids Read allowed, and there are none");
    }

    // ── site: chat document context (AiAuthorizationFilter) ───────────────────────────────────────────────────────

    public static TheoryData<string, string> ChatShapesByCase()
    {
        var data = new TheoryData<string, string>();
        foreach (var shape in new[] { "document-id", "additional-document-ids", "document-host" })
        foreach (var @case in Cases)
            data.Add(shape, @case);
        return data;
    }

    private static object ChatBody(string shape, Guid documentId) => shape switch
    {
        "document-id" => new ChatCreateSessionRequest(documentId.ToString()),
        "additional-document-ids" => new ChatSwitchContextRequest(null, null, AdditionalDocumentIds: new[] { documentId.ToString() }),
        "document-host" => new ChatCreateSessionRequest(null, HostContext: new ChatHostContext("sprk_document", documentId.ToString())),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    private async Task<(object? Result, bool NextRan)> InvokeChatAsync(string shape, Guid documentId)
    {
        var httpContext = Caller();
        var nextRan = false;
        var filter = new AiAuthorizationFilter(Service());
        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext, ChatBody(shape, documentId)),
            _ =>
            {
                nextRan = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });
        return (result, nextRan);
    }

    [Theory(DisplayName = "Task 188 r4a F1-1 (chat): a hidden document in the chat context is refused with the chat family's uniform 403")]
    [MemberData(nameof(ChatShapesByCase))]
    public async Task Chat_ExternalCaller_HiddenDocument_IsRefused(string shape, string @case)
    {
        var hidden = Arrange(@case);

        var (result, nextRan) = await InvokeChatAsync(shape, hidden);

        nextRan.Should().BeFalse("the handler never builds a context over a hidden document");
        (result as IStatusCodeHttpResult)!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory(DisplayName = "Task 188 r4a F1-1 (chat): must not fire — an internal user with a Restricted document, an external caller with a visible one")]
    [InlineData(NarrowingCallerMode.Internal)]
    [InlineData(NarrowingCallerMode.External)]
    public async Task Chat_MustNotFire(NarrowingCallerMode mode)
    {
        var id = mode == NarrowingCallerMode.Internal ? Arrange("own-restricted") : Visible();
        _mode = mode;

        var (_, nextRan) = await InvokeChatAsync("document-id", id);

        nextRan.Should().BeTrue();
        _world.Calls.Should().Be(mode == NarrowingCallerMode.Internal ? 0 : 1);
    }

    // ── site: analysis execute (AnalysisAuthorizationFilter, DocumentAccess) ──────────────────────────────────────

    private async Task<(object? Result, bool NextRan)> InvokeExecuteAsync(params Guid[] documentIds)
    {
        var httpContext = Caller();
        var nextRan = false;
        var filter = new AnalysisAuthorizationFilter(Service(), null, AuthorizationMode.DocumentAccess);
        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext, new AnalysisExecuteRequest { DocumentIds = documentIds }),
            _ =>
            {
                nextRan = true;
                return ValueTask.FromResult<object?>(Results.Ok());
            });
        return (result, nextRan);
    }

    [Theory(DisplayName = "Task 188 r4a F1-1 (analysis execute): a hidden document is refused 403 before anything is loaded")]
    [MemberData(nameof(MustFireCases))]
    public async Task AnalysisExecute_ExternalCaller_HiddenDocument_IsRefused(string @case)
    {
        var hidden = Arrange(@case);

        var (result, nextRan) = await InvokeExecuteAsync(hidden);

        nextRan.Should().BeFalse("the analysis loader never downloads a hidden document");
        (result as IStatusCodeHttpResult)!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory(DisplayName = "Task 188 r4a F1-1 (analysis execute): must not fire — an internal user with a Restricted document, an external caller with a visible one")]
    [InlineData(NarrowingCallerMode.Internal)]
    [InlineData(NarrowingCallerMode.External)]
    public async Task AnalysisExecute_MustNotFire(NarrowingCallerMode mode)
    {
        var id = mode == NarrowingCallerMode.Internal ? Arrange("own-restricted") : Visible();
        _mode = mode;

        var (_, nextRan) = await InvokeExecuteAsync(id);

        nextRan.Should().BeTrue();
        _world.Calls.Should().Be(mode == NarrowingCallerMode.Internal ? 0 : 1);
    }

    // ── site: POST /api/ai/rag/search (the per-row trim) ──────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<RagSearchResult>> TrimRagAsync(params Guid[] documentIds)
    {
        var httpContext = Caller();
        var rows = documentIds.Select((id, i) => new RagSearchResult
        {
            Id = $"chunk-{i}",
            DocumentId = id.ToString(),
            Content = $"chunk text of {id}",
        }).ToList();
        return await RagEndpoints.TrimToReadableDocumentsAsync(rows, Service(), httpContext, NullLogger.Instance, CancellationToken.None);
    }

    [Theory(DisplayName = "Task 188 r4a F1-1 (RAG search): a hidden document's chunks are never returned; a visible one's are")]
    [MemberData(nameof(MustFireCases))]
    public async Task RagSearch_ExternalCaller_HiddenDocumentRows_AreDropped(string @case)
    {
        var hidden = Arrange(@case);
        var visible = Visible();

        var kept = await TrimRagAsync(hidden, visible, hidden);

        kept.Select(r => r.DocumentId).Should().NotContain(hidden.ToString());
        kept.Select(r => r.DocumentId).Should().Equal(FaultsAll(@case) ? Array.Empty<string>() : new[] { visible.ToString() });
        _world.Calls.Should().Be(1, "ONE narrowing call for the page");
    }

    [Fact(DisplayName = "Task 188 r4a F1-1 (RAG search): must not fire — an internal user keeps a Restricted document's chunks")]
    public async Task RagSearch_InternalUser_IsNotNarrowed()
    {
        var restricted = Arrange("own-restricted");
        _mode = NarrowingCallerMode.Internal;

        var kept = await TrimRagAsync(restricted);

        kept.Should().ContainSingle();
        _world.Calls.Should().Be(0);
    }

    // ── site: GET /api/ai/visualization/related/{id} (the app-only related rows) ─────────────────────────────────

    private async Task<IResult> RelatedAsync(Guid source, params (Guid Id, string Relationship)[] related)
    {
        var graph = new DocumentGraphResponse
        {
            Nodes = new[] { new DocumentNode { Id = source.ToString(), Type = NodeTypes.Source, Depth = 0 } }
                .Concat(related.Select(r => new DocumentNode { Id = r.Id.ToString(), Type = NodeTypes.Related, Depth = 1 }))
                .ToList(),
            Edges = related.Select(r => new DocumentEdge
            {
                Id = $"{source}-{r.Id}",
                Source = source.ToString(),
                Target = r.Id.ToString(),
                Data = new DocumentEdgeData { RelationshipType = r.Relationship },
            }).ToList(),
            Metadata = new GraphMetadata { SourceDocumentId = source.ToString(), TotalResults = related.Length },
        };
        var visualization = new Mock<IVisualizationService>();
        visualization.Setup(v => v.GetRelatedDocumentsAsync(source, It.IsAny<VisualizationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);

        var httpContext = Caller();
        httpContext.Items[VisualizationAuthorization.HttpContextItemsKey] = new VisualizationAuthorization
        {
            RequiresPerRowDocumentAuthorization = true,
            Subject = VisualizationAuthorizationSubject.SourceDocument,
        };

        return await VisualizationEndpoints.GetRelatedDocuments(
            source, new VisualizationQueryParameters(), httpContext, visualization.Object, Service(),
            NullLogger<Program>.Instance, CancellationToken.None);
    }

    private static IReadOnlyList<string> NodeIds(IResult result) =>
        ((DocumentGraphResponse)((IValueHttpResult)result).Value!).Nodes.Select(n => n.Id).ToList();

    [Theory(DisplayName = "Task 188 r4a F1-1 (visualization): a hidden parent .eml and a hidden sibling are dropped from the related rows")]
    [MemberData(nameof(MustFireCases))]
    public async Task VisualizationRelated_ExternalCaller_HiddenParentEmlAndSibling_AreDropped(string @case)
    {
        var source = Visible();
        var parentEml = Arrange(@case);
        var sibling = _world.Document(Guid.NewGuid(), permission: Restricted);
        var visible = Visible();

        var result = await RelatedAsync(source,
            (parentEml, RelationshipTypes.SameEmail), (sibling, RelationshipTypes.SameEmail), (visible, RelationshipTypes.Semantic));

        var ids = NodeIds(result);
        ids.Should().NotContain(new[] { parentEml.ToString(), sibling.ToString() },
            "the app-only parent and sibling rows are decided by the same narrowing as everything else");
        ids.Should().Equal(FaultsAll(@case) ? new[] { source.ToString() } : new[] { source.ToString(), visible.ToString() });
        _world.Calls.Should().Be(1, "ONE narrowing call for the graph's rows");
    }

    [Fact(DisplayName = "Task 188 r4a F1-1 (visualization): must not fire — an internal user keeps a Restricted parent .eml")]
    public async Task VisualizationRelated_InternalUser_IsNotNarrowed()
    {
        var source = Visible();
        var parentEml = Arrange("own-restricted");
        _mode = NarrowingCallerMode.Internal;

        var result = await RelatedAsync(source, (parentEml, RelationshipTypes.SameEmail));

        NodeIds(result).Should().Equal(source.ToString(), parentEml.ToString());
        _world.Calls.Should().Be(0);
    }
}
