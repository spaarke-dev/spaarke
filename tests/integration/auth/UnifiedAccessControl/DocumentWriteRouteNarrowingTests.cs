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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.AccessControl;

// unified-access-control-r2 task 188 round 4d. Scenario for every class: a systemuser flagged sprk_isexternal (a B2B guest)
// holding Write / AppendTo / Read on a document through a share. Under D7 (every BFF surface, owner round 113) a document
// THE external document narrowing hides refuses writes and counts too, with the answer the route gives for no access.
// Must fire over HiddenDocumentCases (own Restricted, internal-only communication, Restricted parent, narrowing fault,
// lookup fault); must not fire for an internal caller (no narrowing call, no app-only read) or a visible document.

/// <summary>
/// #2200: <c>POST /api/ai/rag/index-file</c> — the production declaration (<see cref="RagEndpoints.ResolveIndexFileTargets"/>)
/// and filter over a handler that only counts; the REAL <see cref="AuthorizationService"/> over a stated access seam and the
/// REAL narrowing over a stated caller and app-only world.
/// </summary>
public sealed class IndexFileDocumentNarrowingTests : IAsyncLifetime
{
    private const string CallerOid = "6f0c1a52-0000-4000-8000-0000000188d1";
    private const string Documents = "sprk_documents";

    private WebApplication? _app;
    private HttpClient? _client;
    private int _indexed;

    private RecordingAccessDataSource Access { get; } = new();

    private AppOnlyDocumentWorld World { get; } = new();

    private NarrowingCallerMode Mode { get; set; } = NarrowingCallerMode.Internal;

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
        builder.Services.AddSingleton<IAccessDataSource>(Access);
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddStatedNarrowingCaller(() => Mode, () => World);
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();

        // The production declaration of POST /api/ai/rag/index-file, behind the same FinanceAuthorizationFilter and the same
        // uniform 404 the route's AddTargetedRecordAuthorizationFilter builds (its route tests pin that wiring).
        _app.MapPost("/api/ai/rag/index-file", (FileIndexRequest _) =>
            {
                Interlocked.Increment(ref _indexed);
                return Results.Ok();
            })
            .RequireAuthorization()
            .AddFinanceAuthorizationFilter(RagEndpoints.ResolveIndexFileTargets, FinanceDenial.UniformNotFound);

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

    private Guid WritableDocument()
    {
        var id = Guid.NewGuid();
        Access.Grant(Documents, id, AccessRights.Read | AccessRights.Write);
        return id;
    }

    private Task<HttpResponseMessage> IndexAsync(Guid documentId, ParentEntityContext? parent = null) =>
        _client!.PostAsJsonAsync("/api/ai/rag/index-file", new FileIndexRequest
        {
            DriveId = "b!drive",
            ItemId = "01ITEM",
            FileName = "brief.pdf",
            TenantId = PlaybookAuthzTestAuthHandler.TenantId,
            DocumentId = documentId.ToString(),
            ParentEntity = parent,
        });

    [Theory(DisplayName = "Task 188 r4d (#2200): index-file over a document the narrowing hides is the uniform 404 of an unwritable one; nothing is indexed")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task IndexFile_ExternalCaller_HiddenDocument_IsTheUniform404(string @case)
    {
        var hidden = WritableDocument();
        Mode = HiddenDocumentCases.Arrange(World, hidden, @case);
        var unwritable = Guid.NewGuid();
        Access.Grant(Documents, unwritable, AccessRights.Read);

        var denied = await IndexAsync(hidden);
        var refused = await IndexAsync(unwritable);

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(denied))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(refused));
        _indexed.Should().Be(0);
        World.Calls.Should().Be(1, "the unwritable document is never sent to the narrowing");
        Access.Calls.Should().Contain(new AccessCall(AccessPath.Document, Documents, hidden, HasToken: true));
        Access.Calls.Should().NotContain(c => c.Path == AccessPath.Record, "a document is never decided on the Record path");
    }

    [Fact(DisplayName = "Task 188 r4d (#2200): must not fire — an internal caller indexes a Restricted document; no narrowing call, no app-only read")]
    public async Task IndexFile_InternalCaller_IsNotNarrowed()
    {
        var restricted = WritableDocument();
        HiddenDocumentCases.Arrange(World, restricted, HiddenDocumentCases.OwnRestricted);
        Mode = NarrowingCallerMode.Internal;

        (await IndexAsync(restricted)).StatusCode.Should().Be(HttpStatusCode.OK);
        _indexed.Should().Be(1);
        World.Calls.Should().Be(0);
        World.AllReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 188 r4d (#2200): must not fire — an external caller indexes a visible document; its parent stays a Record check")]
    public async Task IndexFile_ExternalCaller_VisibleDocument_IsIndexed_AndTheParentIsARecordCheck()
    {
        var visible = WritableDocument();
        World.Document(visible);
        Mode = NarrowingCallerMode.External;
        var workAssignment = Guid.NewGuid();
        Access.Grant("sprk_workassignments", workAssignment, AccessRights.AppendTo);

        var response = await IndexAsync(visible, new ParentEntityContext("workassignment", workAssignment.ToString(), "Draft brief"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _indexed.Should().Be(1);
        World.Calls.Should().Be(1, "only the document is narrowed");
        Access.Calls.Should().Contain(new AccessCall(AccessPath.Record, "sprk_workassignments", workAssignment, HasToken: true));
    }
}

/// <summary>
/// #2200: <c>POST /api/ai/rag/send-to-index</c> — the REAL route and handler (<see cref="SendToIndexAuthorizationFixture"/>:
/// the real <c>Program</c>, the real <see cref="AuthorizationService"/>, the real narrowing over the fixture's stated caller
/// and world). A hidden document is refused in place exactly like one the caller may not write.
/// </summary>
[Trait("category", "authorization")]
public sealed class SendToIndexDocumentNarrowingTests : IClassFixture<SendToIndexAuthorizationFixture>
{
    private readonly SendToIndexAuthorizationFixture _fixture;

    public SendToIndexDocumentNarrowingTests(SendToIndexAuthorizationFixture fixture)
    {
        _fixture = fixture;
        _fixture.ResetBoundaries();
    }

    private Guid WritableIndexableDocument()
    {
        var id = Guid.NewGuid();
        _fixture.DataverseMock
            .Setup(d => d.GetDocumentAsync(id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = id.ToString(),
                Name = $"doc-{id}.docx",
                FileName = $"doc-{id}.docx",
                GraphDriveId = "b!test-drive-id",
                GraphItemId = $"01ITEM{id:N}",
                MatterId = Guid.NewGuid().ToString(),
                MatterName = "Acme v. Zenith",
            });
        _fixture.DataverseMock
            .Setup(d => d.UpdateDocumentAsync(id.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _fixture.Access.Grant(id, AccessRights.Read | AccessRights.Write);
        return id;
    }

    private async Task<HttpResponseMessage> PostAsync(params Guid[] documentIds)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.PostAsJsonAsync("/api/ai/rag/send-to-index", new SendToIndexRequest
        {
            DocumentIds = documentIds.Select(id => id.ToString()).ToList(),
            TenantId = SendToIndexAuthorizationFixture.CallerTenantId,
        });
    }

    private void VerifyNeverTouched(Guid documentId)
    {
        _fixture.DataverseMock.Verify(d => d.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()), Times.Never);
        _fixture.DataverseMock.Verify(
            d => d.UpdateDocumentAsync(documentId.ToString(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fixture.CapturedIndexRequests.Should().NotContain(r => r.DocumentId == documentId.ToString());
    }

    [Theory(DisplayName = "Task 188 r4d (#2200): send-to-index of only a hidden document is the 403 of an unwritable one; never read, indexed or stamped")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task SendToIndex_ExternalCaller_OnlyAHiddenDocument_IsTheNoWrite403(string @case)
    {
        var hidden = WritableIndexableDocument();
        _fixture.Mode = HiddenDocumentCases.Arrange(_fixture.World, hidden, @case);
        var unwritable = Guid.NewGuid();
        _fixture.Access.Grant(unwritable, AccessRights.Read);

        var denied = await PostAsync(hidden);
        var refused = await PostAsync(unwritable);

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(denied))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(refused));
        VerifyNeverTouched(hidden);
        _fixture.World.Calls.Should().Be(1, "one narrowing call; the unwritable document is never sent");
    }

    [Theory(DisplayName = "Task 188 r4d (#2200): beside a visible document, a hidden one is refused in place like an unwritable one (no parent); one narrowing call")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task SendToIndex_ExternalCaller_HiddenBesideVisible_IsRefusedInPlace(string @case)
    {
        var hidden = WritableIndexableDocument();
        _fixture.Mode = HiddenDocumentCases.Arrange(_fixture.World, hidden, @case);
        var visible = WritableIndexableDocument();
        _fixture.World.Document(visible);
        var unwritable = Guid.NewGuid();
        _fixture.Access.Grant(unwritable, AccessRights.Read);

        var response = await PostAsync(hidden, visible, unwritable);

        VerifyNeverTouched(hidden);
        _fixture.World.Calls.Should().Be(1);
        _fixture.World.AllReads.Should().NotContain(r => r.Contains(unwritable.ToString(), StringComparison.OrdinalIgnoreCase),
            "an id the caller may not write is never sent to the narrowing");
        if (@case is HiddenDocumentCases.NarrowingFault)
        {
            // A fault hides every id, so nothing is writable: the route's whole-request 403.
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            _fixture.CapturedIndexRequests.Should().BeEmpty();
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendToIndexResponse>();
        var hiddenResult = body!.Results.Single(r => r.DocumentId == hidden.ToString());
        var unwritableResult = body.Results.Single(r => r.DocumentId == unwritable.ToString());
        (hiddenResult with { DocumentId = "x" }).Should().Be(unwritableResult with { DocumentId = "x" },
            "a hidden document cannot be told from one the caller may not write");
        hiddenResult.ParentEntityId.Should().BeNull();
        body.Results.Single(r => r.DocumentId == visible.ToString()).Success.Should().BeTrue();
        _fixture.CapturedIndexRequests.Should().ContainSingle().Which.DocumentId.Should().Be(visible.ToString());
    }

    [Fact(DisplayName = "Task 188 r4d (#2200): must not fire — an internal caller indexes a Restricted document; no narrowing call, no app-only read")]
    public async Task SendToIndex_InternalCaller_IsNotNarrowed()
    {
        var restricted = WritableIndexableDocument();
        HiddenDocumentCases.Arrange(_fixture.World, restricted, HiddenDocumentCases.OwnRestricted);
        _fixture.Mode = NarrowingCallerMode.Internal;

        var response = await PostAsync(restricted);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.CapturedIndexRequests.Should().ContainSingle().Which.DocumentId.Should().Be(restricted.ToString());
        _fixture.World.Calls.Should().Be(0);
        _fixture.World.AllReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 188 r4d (#2200): must not fire — an external caller indexes a visible document")]
    public async Task SendToIndex_ExternalCaller_VisibleDocument_IsIndexed()
    {
        var visible = WritableIndexableDocument();
        _fixture.World.Document(visible);
        _fixture.Mode = NarrowingCallerMode.External;

        var response = await PostAsync(visible);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fixture.CapturedIndexRequests.Should().ContainSingle().Which.DocumentId.Should().Be(visible.ToString());
        _fixture.World.Calls.Should().Be(1);
    }
}

/// <summary>
/// #2200: <c>PUT /api/v1/documents/{id}</c> re-filing a document under a parent DOCUMENT — the REAL mapped route. The
/// route's own <c>DocumentAuthorizationFilter("write")</c> narrows the re-filed document (slice B) through
/// <see cref="DocumentRefileTargetNarrowingFixture.RouteWorld"/>, where it is always visible; the TARGET is narrowed through
/// <see cref="DocumentRouteNarrowingTestFixture.World"/>, so each case reaches the target check (a narrowing fault there
/// faults the target's narrowing only).
/// </summary>
public sealed class DocumentRefileTargetNarrowingTests : IClassFixture<DocumentRefileTargetNarrowingFixture>
{
    private readonly DocumentRefileTargetNarrowingFixture _fixture;

    public DocumentRefileTargetNarrowingTests(DocumentRefileTargetNarrowingFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private const string AllRights = "ReadAccess,WriteAccess,AppendToAccess";

    private async Task<HttpResponseMessage> RefileAsync(string rights, NarrowingCallerMode mode, object body)
    {
        using var client = _fixture.CreateClient(rights, mode);
        return await client.PutAsJsonAsync($"/api/v1/documents/{DocumentRefileTargetNarrowingFixture.Refiled}", body);
    }

    [Theory(DisplayName = "Task 188 r4d (#2200): re-filing under a parent document the narrowing hides is the 403 of a target without AppendTo; nothing is written")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task Refile_ExternalCaller_UnderAHiddenDocument_IsTheNoAppendTo403(string @case)
    {
        var target = Guid.NewGuid();
        var mode = HiddenDocumentCases.Arrange(_fixture.World, target, @case);

        var denied = await RefileAsync(AllRights, mode, new { parentDocumentLookup = target });
        var noAppendTo = await RefileAsync("ReadAccess,WriteAccess", mode, new { parentDocumentLookup = target });

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denied.Content.ReadAsStringAsync());
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(denied))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(noAppendTo));
        _fixture.UpdatedDataverseDocumentIds.Should().BeEmpty("a refused re-file writes nothing");
        _fixture.World.Calls.Should().Be(1, "the target is narrowed once; a target without AppendTo is never narrowed");
        _fixture.RouteWorld.Calls.Should().Be(2, "the re-filed document is narrowed by the route's filter, once per request");
    }

    [Fact(DisplayName = "Task 188 r4d (#2200): must not fire — an internal caller files under a Restricted document; no narrowing call, no app-only read")]
    public async Task Refile_InternalCaller_UnderARestrictedDocument_IsNotNarrowed()
    {
        var target = Guid.NewGuid();
        HiddenDocumentCases.Arrange(_fixture.World, target, HiddenDocumentCases.OwnRestricted);

        var response = await RefileAsync(AllRights, NarrowingCallerMode.Internal, new { parentDocumentLookup = target });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.UpdatedDataverseDocumentIds.Should().Contain(DocumentRefileTargetNarrowingFixture.Refiled.ToString());
        _fixture.World.Calls.Should().Be(0);
        _fixture.World.AllReads.Should().BeEmpty();
        _fixture.RouteWorld.Calls.Should().Be(0);
    }

    [Fact(DisplayName = "Task 188 r4d (#2200): must not fire — an external caller files under a visible document")]
    public async Task Refile_ExternalCaller_UnderAVisibleDocument_IsWritten()
    {
        var target = Guid.NewGuid();
        _fixture.World.Document(target);

        var response = await RefileAsync(AllRights, NarrowingCallerMode.External, new { parentDocumentLookup = target });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.UpdatedDataverseDocumentIds.Should().Contain(DocumentRefileTargetNarrowingFixture.Refiled.ToString());
        _fixture.World.Calls.Should().Be(1);
    }

    [Fact(DisplayName = "Task 188 r4d (#2200): must not fire — an external caller's re-file under a matter makes no target narrowing call")]
    public async Task Refile_ExternalCaller_UnderAMatter_IsNotADocumentDecision()
    {
        var response = await RefileAsync(AllRights, NarrowingCallerMode.External, new { matterLookup = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.World.Calls.Should().Be(0, "a matter is not a document");
        _fixture.RouteWorld.Calls.Should().Be(1);
    }
}

/// <summary>
/// <see cref="DocumentRouteNarrowingTestFixture"/> plus: the re-filed document's narrowing answered by a separate
/// <see cref="RouteWorld"/> (always visible), and the doubles the re-file's success path writes through (the ownership
/// resolver and the restamp world, as <see cref="DocumentRefileRestampFixture"/>).
/// </summary>
public sealed class DocumentRefileTargetNarrowingFixture : DocumentRouteNarrowingTestFixture
{
    /// <summary>The document every test re-files (the restamp world's document).</summary>
    public static readonly Guid Refiled = Guid.Parse("15600000-0000-0000-0000-000000000d01");

    /// <summary>The narrowing world of the re-filed document itself (the route filter's question).</summary>
    internal AppOnlyDocumentWorld RouteWorld { get; private set; } = NewRouteWorld();

    private StampWorld Stamps { get; } = RefilePathRestampTests.DocumentWorld();

    private RecordOwnershipResolverDouble Ownership { get; } = new();

    private static AppOnlyDocumentWorld NewRouteWorld()
    {
        var world = new AppOnlyDocumentWorld();
        world.Document(Refiled);
        return world;
    }

    /// <summary>Clears the recorders and both worlds.</summary>
    public new void Reset()
    {
        base.Reset();
        RouteWorld = NewRouteWorld();
        Stamps.ReplaceWith(RefilePathRestampTests.DocumentWorld());
        Ownership.FaultAfterChange = null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<InternalRouteDocumentNarrowing>();
            services.AddScoped<InternalRouteDocumentNarrowing>(_ => (ids, ct) =>
                (ids.Contains(Refiled) ? RouteWorld : World).Narrowing(ids, ct));

            services.RemoveAll<CoreAncestorRestamper>();
            services.AddSingleton(_ => Stamps.Restamper);
            services.RemoveAll<IRecordOwnershipResolver>();
            services.AddSingleton<IRecordOwnershipResolver>(Ownership);
        });
    }
}

/// <summary>
/// #2199: <c>POST /api/ai/search/count</c> with <c>scope=documentIds</c> — the REAL mapped routes
/// (<see cref="SemanticSearchEndpoints.MapSemanticSearchEndpoints"/>) and filter, the REAL <see cref="AuthorizationService"/>
/// over a stated access seam, and the REAL narrowing over a stated caller and world. The count service answers the number
/// of ids it was asked to count, so the count IS the set the route let through.
/// </summary>
public sealed class SemanticSearchCountNarrowingTests : IAsyncLifetime
{
    private const string CallerOid = "6f0c1a52-0000-4000-8000-0000000188d2";
    private const string Documents = "sprk_documents";

    private WebApplication? _app;
    private HttpClient? _client;

    private RecordingAccessDataSource Access { get; } = new();

    private AppOnlyDocumentWorld World { get; } = new();

    private NarrowingCallerMode Mode { get; set; } = NarrowingCallerMode.Internal;

    /// <summary>Every id set the count service was asked to count.</summary>
    private List<IReadOnlyList<string>> Counted { get; } = new();

    public async Task InitializeAsync()
    {
        var search = new Mock<ISemanticSearchService>(MockBehavior.Strict);
        search
            .Setup(s => s.CountAsync(It.IsAny<SemanticSearchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SemanticSearchRequest request, string _, CancellationToken _) =>
            {
                var ids = request.DocumentIds ?? Array.Empty<string>();
                Counted.Add(ids);
                return new SemanticSearchCountResponse { Count = ids.Count };
            });

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
        builder.Services.AddRateLimiter(o => o.AddPolicy("ai-batch", _ => RateLimitPartition.GetNoLimiter("test")));
        builder.Services.AddSingleton<IAccessDataSource>(Access);
        builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
        builder.Services.AddScoped<AuthorizationService>();
        builder.Services.AddSingleton(search.Object);
        builder.Services.AddSingleton(new Mock<IRetrievalAccessTrim>(MockBehavior.Strict).Object);
        builder.Services.AddStatedNarrowingCaller(() => Mode, () => World);
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseRateLimiter();
        _app.MapSemanticSearchEndpoints();

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

    private Guid ReadableDocument()
    {
        var id = Guid.NewGuid();
        Access.Grant(Documents, id, AccessRights.Read);
        return id;
    }

    private Task<HttpResponseMessage> CountAsync(params Guid[] documentIds) =>
        _client!.PostAsJsonAsync("/api/ai/search/count", new
        {
            query = "privileged",
            scope = "documentIds",
            documentIds = documentIds.Select(id => id.ToString()).ToList(),
        });

    private static async Task<long> CountOf(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt64();
    }

    [Theory(DisplayName = "Task 188 r4d (#2199): a count over a hidden document beside a visible one counts only the visible one; the hidden id never reaches the index")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task Count_ExternalCaller_HiddenBesideVisible_CountsOnlyTheVisible(string @case)
    {
        var hidden = ReadableDocument();
        Mode = HiddenDocumentCases.Arrange(World, hidden, @case);
        var visible = ReadableDocument();
        World.Document(visible);

        var response = await CountAsync(hidden, visible);

        World.Calls.Should().Be(1);
        Counted.SelectMany(c => c).Should().NotContain(hidden.ToString(), "a hidden document is never counted");
        if (@case is HiddenDocumentCases.NarrowingFault)
        {
            // Fails closed: a fault hides every id, so there is nothing readable — never the untrimmed count.
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Counted.Should().BeEmpty();
            return;
        }

        (await CountOf(response)).Should().Be(1);
        Counted.Should().ContainSingle().Which.Should().Equal(visible.ToString());
    }

    [Theory(DisplayName = "Task 188 r4d (#2199): a count over only a hidden document is the 403 of an unreadable one; nothing is counted")]
    [MemberData(nameof(HiddenDocumentCases.All), MemberType = typeof(HiddenDocumentCases))]
    public async Task Count_ExternalCaller_OnlyAHiddenDocument_IsTheNoReadable403(string @case)
    {
        var hidden = ReadableDocument();
        Mode = HiddenDocumentCases.Arrange(World, hidden, @case);

        var denied = await CountAsync(hidden);
        var unreadable = await CountAsync(Guid.NewGuid());

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RouteSweepAuthorizationFixture.NormalizedProblemAsync(denied))
            .Should().Be(await RouteSweepAuthorizationFixture.NormalizedProblemAsync(unreadable));
        Counted.Should().BeEmpty();
        World.Calls.Should().Be(1, "the unreadable document is never sent to the narrowing");
    }

    [Fact(DisplayName = "Task 188 r4d (#2199): must not fire — an internal caller's count includes a Restricted document; no narrowing call, no app-only read")]
    public async Task Count_InternalCaller_IsNotNarrowed()
    {
        var restricted = ReadableDocument();
        HiddenDocumentCases.Arrange(World, restricted, HiddenDocumentCases.OwnRestricted);
        var other = ReadableDocument();
        Mode = NarrowingCallerMode.Internal;

        (await CountOf(await CountAsync(restricted, other))).Should().Be(2);
        World.Calls.Should().Be(0);
        World.AllReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 188 r4d (#2199): must not fire — an external caller's count over visible documents counts them all")]
    public async Task Count_ExternalCaller_VisibleDocuments_AreCounted()
    {
        var first = ReadableDocument();
        var second = ReadableDocument();
        World.Document(first);
        World.Document(second);
        Mode = NarrowingCallerMode.External;

        (await CountOf(await CountAsync(first, second))).Should().Be(2);
        World.Calls.Should().Be(1);
    }
}
