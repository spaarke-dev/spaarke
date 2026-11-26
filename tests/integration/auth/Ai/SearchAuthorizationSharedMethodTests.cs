using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Ai;

/// <summary>
/// spaarkeai-mcp-bff-api-r2 task 027 (spec FR-02 prerequisite): the access decisions of the two REST search routes as shared
/// methods — <see cref="SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync"/> (<c>POST /api/ai/search</c>) and
/// <see cref="RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync"/> (<c>POST /api/ai/search/records</c>), which task 022's
/// MCP search tools call instead of copying checks. Each case calls the method directly, as a tool will, then sends the same input
/// to the REAL route over the SAME substitutes: an allowed answer must carry the rows, counts and warnings the route returns, a
/// refusal the route's status and code, and both must ask Dataverse the same questions in the same order.
/// </summary>
/// <remarks>
/// Real: both filters, both handlers, <see cref="AuthorizationService"/>, the external document narrowing (internal caller: no
/// call). Substituted only at module boundaries (ADR-038): the access-data source (what Dataverse answers for the caller), the two
/// search services (the index), and <see cref="IRetrievalAccessTrim"/> (the PublicContracts facade over the caller's Dataverse
/// read). The REST bytes themselves are pinned by the existing search contract and auth suites, which this task leaves unchanged.
/// </remarks>
[Trait("category", "authorization")]
public sealed class SearchAuthorizationSharedMethodTests
{
    private const string Matters = "sprk_matters";
    private const string Documents = "sprk_documents";
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000027";

    // ══ Document search ══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "scope=entity, readable parent: the method returns the route's rows and counts — off-parent rows dropped, SPE pointers stripped, an unreadable document trimmed")]
    public async Task SearchDocuments_EntityScope_AllowedCaller_MatchesTheRoute()
    {
        await using var host = await SearchAuthHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(Matters, parent, AccessRights.Read);
        var readable = Doc(parent);
        var offParent = Doc(Guid.NewGuid());
        var trimmed = Doc(parent);
        host.Trim.Unreadable.Add(trimmed.DocumentId!);
        host.Search.Returns(readable, offParent, trimmed);

        // Upper-cased, as Xrm sends it: the narrowed request carries the canonical form the filter computed.
        var request = new SemanticSearchRequest
        {
            Query = "indemnity",
            Scope = SearchScope.Entity,
            EntityType = "matter",
            EntityId = parent.ToString().ToUpperInvariant(),
        };

        var (direct, rest) = await host.DocumentsDirectThenRestAsync(request);

        direct.Outcome.Should().Be(SemanticSearchAccessOutcome.Allowed);
        direct.ReasonCode.Should().BeNull();
        direct.Response!.Results.Select(r => r.DocumentId).Should().Equal(readable.DocumentId);
        direct.Response.Results.Should().OnlyContain(r => r.DriveId == null && r.SpeFileId == null);
        direct.Response.Metadata.TotalResults.Should().Be(1);
        direct.Response.Metadata.ReturnedResults.Should().Be(1);
        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.AssertSameBodyAsync(rest, direct.Response);
        host.Search.Requests.Should().HaveCount(2);
        host.Search.Requests[0].Should().BeEquivalentTo(host.Search.Requests[1], "the method and the route search the same narrowed request");
        host.Search.Requests[0].EntityId.Should().Be(parent.ToString());
    }

    [Fact(DisplayName = "scope=all: rows under an unreadable parent are removed and the route's PARTIAL_RESULTS warning is on the method's answer too")]
    public async Task SearchDocuments_ScopeAll_UnreadableParent_IsRemoved_WithPartialResults()
    {
        await using var host = await SearchAuthHost.StartAsync();
        var readableParent = Guid.NewGuid();
        var unreadableParent = Guid.NewGuid();
        host.Access.Grant(Matters, readableParent, AccessRights.Read);

        // limit 2 → candidate pool 6; a saturated pool that still leaves the page short is PARTIAL_RESULTS.
        host.Search.Returns(
            Doc(readableParent),
            Doc(unreadableParent), Doc(unreadableParent), Doc(unreadableParent), Doc(unreadableParent), Doc(unreadableParent));
        var request = new SemanticSearchRequest
        {
            Query = "indemnity",
            Scope = SearchScope.All,
            Options = new SearchOptions { Limit = 2 },
        };

        var (direct, rest) = await host.DocumentsDirectThenRestAsync(request);

        direct.Outcome.Should().Be(SemanticSearchAccessOutcome.Allowed);
        direct.Response!.Results.Should().ContainSingle().Which.ParentEntityId.Should().Be(readableParent.ToString());
        direct.Response.Metadata.TotalResults.Should().Be(1);
        direct.Response.Metadata.Warnings.Should().ContainSingle().Which.Code.Should().Be(SearchWarningCode.PartialResults);
        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.AssertSameBodyAsync(rest, direct.Response);
        host.Search.Requests.Should().OnlyContain(r => r.Options!.Limit == 6, "both widen the fetch to the same candidate pool");
        host.Access.Calls.Select(c => c.Id).Distinct().Should().BeEquivalentTo(new[] { readableParent, unreadableParent },
            "each distinct parent is asked once per call");
    }

    [Fact(DisplayName = "scope=documentIds: only the readable named document is searched and returned, as on the route")]
    public async Task SearchDocuments_DocumentIdsScope_UnreadableDocument_IsRemoved()
    {
        await using var host = await SearchAuthHost.StartAsync();
        var readable = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        host.Access.Grant(Documents, readable, AccessRights.Read);
        host.Search.Returns(Doc(Guid.NewGuid(), readable), Doc(Guid.NewGuid(), unreadable));
        var request = new SemanticSearchRequest
        {
            Query = "indemnity",
            Scope = SearchScope.DocumentIds,
            DocumentIds = [readable.ToString(), unreadable.ToString()],
        };

        var (direct, rest) = await host.DocumentsDirectThenRestAsync(request);

        direct.Outcome.Should().Be(SemanticSearchAccessOutcome.Allowed);
        direct.Response!.Results.Select(r => r.DocumentId).Should().Equal(readable.ToString());
        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.AssertSameBodyAsync(rest, direct.Response);
        host.Search.Requests.Should().OnlyContain(r => r.DocumentIds!.SequenceEqual(new[] { readable.ToString() }),
            "the unreadable id never reaches the index");
    }

    public enum DocumentRefusal
    {
        /// <summary>scope=entity, no Read on the parent.</summary>
        UnreadableParent,

        /// <summary>scope=entity with a parent type that is not authorizable.</summary>
        UnauthorizableEntityType,

        /// <summary>scope=documentIds, none readable.</summary>
        NoReadableDocument,

        /// <summary>An unknown scope.</summary>
        UnknownScope,

        /// <summary>A signed-in caller with no bearer token.</summary>
        NoBearerToken,
    }

    [Theory(DisplayName = "An out-of-scope or unauthorized document search returns the route's refusal: same status, code and detail, the same Dataverse questions, and no search")]
    [InlineData(DocumentRefusal.UnreadableParent, SemanticSearchAccessOutcome.EntityAccessDenied, HttpStatusCode.Forbidden, SearchErrorCodes.EntityAccessDenied)]
    [InlineData(DocumentRefusal.UnauthorizableEntityType, SemanticSearchAccessOutcome.EntityTypeNotAuthorizable, HttpStatusCode.Forbidden, SearchErrorCodes.EntityTypeNotAuthorizable)]
    [InlineData(DocumentRefusal.NoReadableDocument, SemanticSearchAccessOutcome.NoReadableDocuments, HttpStatusCode.Forbidden, SearchErrorCodes.NoReadableDocuments)]
    [InlineData(DocumentRefusal.UnknownScope, SemanticSearchAccessOutcome.InvalidScope, HttpStatusCode.BadRequest, SearchErrorCodes.InvalidScope)]
    [InlineData(DocumentRefusal.NoBearerToken, SemanticSearchAccessOutcome.MissingCallerToken, HttpStatusCode.Unauthorized, SearchErrorCodes.MissingCallerToken)]
    public async Task SearchDocuments_Refusal_MatchesTheRoute(
        DocumentRefusal @case, SemanticSearchAccessOutcome expected, HttpStatusCode status, string code)
    {
        await using var host = await SearchAuthHost.StartAsync();
        host.Search.Returns(Doc(Guid.NewGuid()));
        var request = @case switch
        {
            DocumentRefusal.UnreadableParent or DocumentRefusal.NoBearerToken => new SemanticSearchRequest
            {
                Query = "q",
                Scope = SearchScope.Entity,
                EntityType = "matter",
                EntityId = Guid.NewGuid().ToString(),
            },
            DocumentRefusal.UnauthorizableEntityType => new SemanticSearchRequest
            {
                Query = "q",
                Scope = SearchScope.Entity,
                EntityType = "account",
                EntityId = Guid.NewGuid().ToString(),
            },
            DocumentRefusal.NoReadableDocument => new SemanticSearchRequest
            {
                Query = "q",
                Scope = SearchScope.DocumentIds,
                DocumentIds = [Guid.NewGuid().ToString()],
            },
            _ => new SemanticSearchRequest { Query = "q", Scope = "everything" },
        };

        var (direct, rest) = await host.DocumentsDirectThenRestAsync(request, withToken: @case != DocumentRefusal.NoBearerToken);

        direct.Outcome.Should().Be(expected);
        direct.ReasonCode.Should().Be(code);
        direct.Response.Should().BeNull();
        rest.StatusCode.Should().Be(status);
        var body = await BodyAsync(rest);
        body["code"]!.GetValue<string>().Should().Be(direct.ReasonCode);
        body["errorCode"]!.GetValue<string>().Should().Be(direct.ReasonCode);
        body["detail"]!.GetValue<string>().Should().Be(direct.Detail);
        host.Search.Requests.Should().BeEmpty("a refused request never reaches the index");
    }

    // ══ Record search ════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "A record the caller cannot read is removed and not counted, by the method as by the route")]
    public async Task SearchRecords_UnreadableRecord_IsRemoved_AndMatchesTheRoute()
    {
        await using var host = await SearchAuthHost.StartAsync();
        var readable = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        host.Access.Grant(Matters, readable, AccessRights.Read);
        host.Records.Returns(Record(readable, "Acme v. Globex"), Record(unreadable, "Confidential Matter"));

        var (direct, rest) = await host.RecordsDirectThenRestAsync(RecordRequest(RecordEntityType.Matter));

        direct.Outcome.Should().Be(RecordSearchAccessOutcome.Allowed);
        direct.ReasonCode.Should().BeNull();
        direct.Response!.Results.Select(r => r.RecordId).Should().Equal(readable.ToString());
        direct.Response.Metadata.TotalCount.Should().Be(1);
        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        await host.AssertSameBodyAsync(rest, direct.Response);
        host.Records.Requests.Should().HaveCount(2);
    }

    [Theory(DisplayName = "A record search the route refuses is refused by the method with the same status, code and detail, the same Dataverse questions, and no search")]
    [InlineData(false, RecordSearchAccessOutcome.InvalidRecordTypes, HttpStatusCode.BadRequest, SearchErrorCodes.InvalidRecordTypes)]
    [InlineData(true, RecordSearchAccessOutcome.MissingCallerToken, HttpStatusCode.Unauthorized, SearchErrorCodes.MissingCallerToken)]
    public async Task SearchRecords_Refusal_MatchesTheRoute(
        bool noToken, RecordSearchAccessOutcome expected, HttpStatusCode status, string code)
    {
        await using var host = await SearchAuthHost.StartAsync();
        host.Records.Returns(Record(Guid.NewGuid(), "Any"));
        var request = noToken ? RecordRequest(RecordEntityType.Matter) : RecordRequest("contract");

        var (direct, rest) = await host.RecordsDirectThenRestAsync(request, withToken: !noToken);

        direct.Outcome.Should().Be(expected);
        direct.ReasonCode.Should().Be(code);
        rest.StatusCode.Should().Be(status);
        var body = await BodyAsync(rest);
        body["code"]!.GetValue<string>().Should().Be(direct.ReasonCode);
        body["detail"]!.GetValue<string>().Should().Be(direct.Detail);
        if (expected == RecordSearchAccessOutcome.InvalidRecordTypes)
        {
            direct.InvalidValues.Should().Equal("contract");
            body["invalidValues"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal(direct.InvalidValues!);
        }

        host.Access.Calls.Should().BeEmpty();
        host.Records.Requests.Should().BeEmpty("a refused request never reaches the index");
    }

    [Fact(DisplayName = "D-027-2: an empty record page past the end carries no unfiltered tenant-wide count, by the method and by the route")]
    public async Task SearchRecords_EmptyPagePastTheEnd_DoesNotLeakTheUnfilteredCount()
    {
        await using var host = await SearchAuthHost.StartAsync();

        // The index holds 7 matching matters tenant-wide; the caller pages past the end (offset 1000, limit 1), so the
        // page is empty but the index still reports the unfiltered total. None of the 7 is known to the caller.
        host.Records.ReturnsEmptyPage(unfilteredTotal: 7);
        var request = RecordRequest(RecordEntityType.Matter) with
        {
            Options = new RecordSearchOptions { Offset = 1000, Limit = 1 },
        };

        var (direct, rest) = await host.RecordsDirectThenRestAsync(request);

        direct.Outcome.Should().Be(RecordSearchAccessOutcome.Allowed);
        direct.Response!.Results.Should().BeEmpty();
        direct.Response.Metadata.TotalCount.Should().Be(0, "an empty page reports the count of rows in the response, as a filtered page does");
        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(rest);
        body["results"]!.AsArray().Should().BeEmpty();
        body["metadata"]!["totalCount"]!.GetValue<int>().Should().Be(0,
            $"the route must not disclose how many matters match tenant-wide. body: {body.ToJsonString()}");
        await host.AssertSameBodyAsync(rest, direct.Response);
        host.Access.Calls.Should().BeEmpty("an empty page has no record to ask Dataverse about");
    }

    // ══ Check budgets ════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Record budget: rows past the 50-check budget are dropped, never served unchecked, and exactly 50 Dataverse questions are asked")]
    public async Task SearchRecords_MoreReadableRecordsThanTheBudget_DropsTheRowsPastIt()
    {
        await using var host = await SearchAuthHost.StartAsync();

        // 60 distinct records, every one readable by the caller: only the budget (RecordSearchOptions.MaxLimit == 50, the value
        // RecordSearchAuthorizationFilter.MaxRecordAuthorizationChecks is defined as) limits the page.
        var ids = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            host.Access.Grant(Matters, id, AccessRights.Read);
        }

        host.Records.Returns(ids.Select(id => Record(id, "Matter " + id)).ToArray());

        var direct = await RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync(
            host.DirectContext(), RecordRequest(RecordEntityType.Matter), host.Records.SearchAsync, CancellationToken.None);

        direct.Outcome.Should().Be(RecordSearchAccessOutcome.Allowed);
        direct.Response!.Results.Select(r => r.RecordId).Should().Equal(ids.Take(50).Select(id => id.ToString()),
            "the first 50 rows were checked and readable; rows past the budget are absent, not served unchecked");
        direct.Response.Metadata.TotalCount.Should().Be(50);
        host.Access.Calls.Should().HaveCount(50, "the budget caps the Dataverse questions asked");
        host.Access.Calls.Select(c => c.Id).Distinct().Should().HaveCount(50);

        // The route holds to the same budget and returns the same page.
        var rest = await host.RestRecordsAsync(RecordRequest(RecordEntityType.Matter));
        await host.AssertSameBodyAsync(rest, direct.Response);
        host.Access.Calls.Should().HaveCount(100, "50 for the method and 50 for the route");
    }

    [Fact(DisplayName = "Document budget: parents past the 25-check budget are dropped with PARTIAL_RESULTS, and exactly 25 Dataverse questions are asked")]
    public async Task SearchDocuments_MoreReadableParentsThanTheBudget_DropsTheRowsPastIt_AndWarns()
    {
        await using var host = await SearchAuthHost.StartAsync();

        // 40 documents under 40 distinct, readable parents; limit 50 so the caller's page is never full, and a pool of 150 so it
        // is never saturated by 40 rows: PARTIAL_RESULTS here can only come from the parent-check budget
        // (SemanticSearchAuthorizationFilter.MaxParentAuthorizationChecks == 25).
        var parents = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToList();
        foreach (var parent in parents)
        {
            host.Access.Grant(Matters, parent, AccessRights.Read);
        }

        var rows = parents.Select(p => Doc(p)).ToArray();
        host.Search.Returns(rows);
        var request = new SemanticSearchRequest
        {
            Query = "indemnity",
            Scope = SearchScope.All,
            Options = new SearchOptions { Limit = 50 },
        };

        var before = host.Access.Calls.Count;
        var direct = await SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync(
            host.DirectContext(), request, host.Search.SearchAsync, CancellationToken.None);

        direct.Outcome.Should().Be(SemanticSearchAccessOutcome.Allowed);
        direct.Response!.Results.Select(r => r.ParentEntityId).Should().Equal(parents.Take(25).Select(p => p.ToString()),
            "the first 25 parents were checked and readable; rows under later parents are absent, not served unchecked");
        direct.Response.Metadata.TotalResults.Should().Be(25);
        direct.Response.Metadata.Warnings.Should().ContainSingle().Which.Code.Should().Be(SearchWarningCode.PartialResults);
        (host.Access.Calls.Count - before).Should().Be(25, "the budget caps the Dataverse questions asked");

        // The route holds to the same budget and returns the same page.
        var rest = await host.RestDocumentsAsync(request);
        await host.AssertSameBodyAsync(rest, direct.Response);
        (host.Access.Calls.Count - before).Should().Be(50, "25 for the method and 25 for the route");
    }

    // ══ No caller (both methods) ═════════════════════════════════════════════════════════════════════════════════════

    public enum NoCaller
    {
        /// <summary>No request context at all.</summary>
        NullContext,

        /// <summary>A principal whose identity carries claims but no authentication type.</summary>
        Unauthenticated,
    }

    [Theory(DisplayName = "A null HttpContext or an unauthenticated principal is UserContextRequired for both methods, with no Dataverse call and no search")]
    [InlineData(NoCaller.NullContext)]
    [InlineData(NoCaller.Unauthenticated)]
    public async Task BothMethods_NoSignedInCaller_AreUserContextRequired_AndNothingRuns(NoCaller @case)
    {
        await using var host = await SearchAuthHost.StartAsync();
        var parent = Guid.NewGuid();
        host.Access.Grant(Matters, parent, AccessRights.Read);
        host.Search.Returns(Doc(parent));
        host.Records.Returns(Record(parent, "Matter"));
        var context = @case == NoCaller.NullContext
            ? null
            : host.DirectContext(new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("oid", CallerOid), new Claim("tid", PlaybookAuthzTestAuthHandler.TenantId) })));

        var documents = await SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync(
            context,
            new SemanticSearchRequest { Query = "q", Scope = SearchScope.Entity, EntityType = "matter", EntityId = parent.ToString() },
            host.Search.SearchAsync,
            CancellationToken.None);
        var records = await RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync(
            context, RecordRequest(RecordEntityType.Matter), host.Records.SearchAsync, CancellationToken.None);

        documents.Should().BeSameAs(SemanticSearchAccess.UserContextRequired);
        documents.ReasonCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        records.Should().BeSameAs(RecordSearchAccess.UserContextRequired);
        records.ReasonCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        host.Access.Calls.Should().BeEmpty("no rights question runs without a signed-in caller");
        host.Search.Requests.Should().BeEmpty();
        host.Records.Requests.Should().BeEmpty();
        host.Trim.Calls.Should().Be(0);
    }

    // ══ Helpers ══════════════════════════════════════════════════════════════════════════════════════════════════════

    private static SearchResult Doc(Guid parent, Guid? documentId = null) => new()
    {
        DocumentId = (documentId ?? Guid.NewGuid()).ToString(),
        Name = "Document",
        ParentEntityType = "matter",
        ParentEntityId = parent.ToString(),
        DriveId = "drive-1",
        SpeFileId = "item-1",
        CombinedScore = 0.9,
    };

    private static RecordSearchResult Record(Guid id, string name) => new()
    {
        RecordId = id.ToString(),
        RecordType = RecordEntityType.Matter,
        RecordName = name,
        ConfidenceScore = 0.8,
    };

    private static RecordSearchRequest RecordRequest(string recordType) => new()
    {
        Query = "acme",
        RecordTypes = [recordType],
    };

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

    /// <summary>
    /// Both search routes over the real filters, handlers and authorization stack, substituted only at the access-data source,
    /// the two search services and the retrieval trim. The direct calls use the SAME container, so a grant applies to both.
    /// </summary>
    private sealed class SearchAuthHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public RecordingAccessDataSource Access { get; } = new();

        public StubSemanticSearch Search { get; } = new();

        public StubRecordSearch Records { get; } = new();

        public StubTrim Trim { get; } = new();

        private AppOnlyDocumentWorld World { get; } = new();

        public static async Task<SearchAuthHost> StartAsync()
        {
            var host = new SearchAuthHost();
            await host.InitializeAsync();
            return host;
        }

        private async Task InitializeAsync()
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
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("ai-batch", _ => RateLimitPartition.GetNoLimiter("ai-batch-test")));

            builder.Services.AddSingleton<IAccessDataSource>(Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddStatedNarrowingCaller(() => NarrowingCallerMode.Internal, () => World);
            builder.Services.AddSingleton<ISemanticSearchService>(Search);
            builder.Services.AddSingleton<IRecordSearchService>(Records);
            builder.Services.AddSingleton<IRetrievalAccessTrim>(Trim);

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapSemanticSearchEndpoints();
            _app.MapRecordSearchEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        /// <summary>A request context as a non-HTTP host (an MCP tool) holds it: principal, bearer token, request services.</summary>
        public HttpContext DirectContext(ClaimsPrincipal? principal = null, bool withToken = true)
        {
            var http = new DefaultHttpContext
            {
                RequestServices = _app!.Services.CreateScope().ServiceProvider,
                User = principal ?? new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("oid", CallerOid), new Claim("tid", PlaybookAuthzTestAuthHandler.TenantId) }, "Test")),
            };
            if (withToken)
            {
                http.Request.Headers.Authorization = "Bearer caller-token";
            }

            return http;
        }

        /// <summary>Calls the method, then the route, and asserts both asked Dataverse the same questions in the same order.</summary>
        public async Task<(SemanticSearchAccess Direct, HttpResponseMessage Route)> DocumentsDirectThenRestAsync(
            SemanticSearchRequest request, bool withToken = true)
        {
            var before = Access.Calls.Count;
            var direct = await SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync(
                DirectContext(withToken: withToken), request, Search.SearchAsync, CancellationToken.None);
            var directCalls = Access.Calls.Skip(before).ToList();

            var middle = Access.Calls.Count;
            var rest = await _client!.SendAsync(Post("/api/ai/search", request, withToken));
            Access.Calls.Skip(middle).Should().Equal(directCalls,
                "the method and the route ask Dataverse the same questions in the same order");
            return (direct, rest);
        }

        public async Task<(RecordSearchAccess Direct, HttpResponseMessage Route)> RecordsDirectThenRestAsync(
            RecordSearchRequest request, bool withToken = true)
        {
            var before = Access.Calls.Count;
            var direct = await RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync(
                DirectContext(withToken: withToken), request, Records.SearchAsync, CancellationToken.None);
            var directCalls = Access.Calls.Skip(before).ToList();

            var middle = Access.Calls.Count;
            var rest = await _client!.SendAsync(Post("/api/ai/search/records", request, withToken));
            Access.Calls.Skip(middle).Should().Equal(directCalls,
                "the method and the route ask Dataverse the same questions in the same order");
            return (direct, rest);
        }

        public Task<HttpResponseMessage> RestRecordsAsync(RecordSearchRequest request) =>
            _client!.SendAsync(Post("/api/ai/search/records", request, withToken: true));

        public Task<HttpResponseMessage> RestDocumentsAsync(SemanticSearchRequest request) =>
            _client!.SendAsync(Post("/api/ai/search", request, withToken: true));

        /// <summary>The route's body equals the method's response, serialized with the host's own JSON options.</summary>
        public async Task AssertSameBodyAsync<T>(HttpResponseMessage rest, T directResponse)
        {
            var options = _app!.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
            var expected = JsonSerializer.SerializeToNode(directResponse, options);
            var actual = JsonNode.Parse(await rest.Content.ReadAsStringAsync());
            JsonNode.DeepEquals(actual, expected).Should().BeTrue(
                $"the route returns exactly the method's rows, counts and warnings.\nroute:  {actual?.ToJsonString()}\nmethod: {expected?.ToJsonString()}");
        }

        private static HttpRequestMessage Post<T>(string path, T body, bool withToken)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            message.Headers.Add(PlaybookAuthzTestAuthHandler.CallerHeader, CallerOid);
            if (withToken)
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            }

            return message;
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.DisposeAsync();
            }
        }
    }

    /// <summary>The document index at its module boundary: returns the seeded rows and records every request it is sent.</summary>
    private sealed class StubSemanticSearch : ISemanticSearchService
    {
        private SearchResult[] _rows = [];

        public List<SemanticSearchRequest> Requests { get; } = [];

        public void Returns(params SearchResult[] rows) => _rows = rows;

        public Task<SemanticSearchResponse> SearchAsync(
            SemanticSearchRequest request, string tenantId, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new SemanticSearchResponse
            {
                Results = _rows,
                Metadata = new SearchMetadata { TotalResults = _rows.Length, ReturnedResults = _rows.Length, ExecutedMode = "rrf" },
            });
        }

        public Task<SemanticSearchCountResponse> CountAsync(
            SemanticSearchRequest request, string tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Count is not part of the shared search method.");
    }

    /// <summary>The record index at its module boundary.</summary>
    private sealed class StubRecordSearch : IRecordSearchService
    {
        private RecordSearchResult[] _rows = [];
        private int? _unfilteredTotal;

        public List<RecordSearchRequest> Requests { get; } = [];

        public void Returns(params RecordSearchResult[] rows) => _rows = rows;

        /// <summary>An empty page whose metadata still carries the index's unfiltered total (a page past the end).</summary>
        public void ReturnsEmptyPage(int unfilteredTotal)
        {
            _rows = [];
            _unfilteredTotal = unfilteredTotal;
        }

        public Task<RecordSearchResponse> SearchAsync(RecordSearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new RecordSearchResponse
            {
                Results = _rows,
                Metadata = new RecordSearchMetadata
                {
                    TotalCount = _unfilteredTotal ?? _rows.Length,
                    SearchTime = 1,
                    HybridMode = RecordHybridSearchMode.Rrf,
                },
            });
        }
    }

    /// <summary>
    /// <see cref="IRetrievalAccessTrim"/> at its PublicContracts boundary: answers as the caller's Dataverse read would, keeping every
    /// row whose document is not listed as unreadable.
    /// </summary>
    private sealed class StubTrim : IRetrievalAccessTrim
    {
        public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Calls { get; private set; }

        public bool CanEvaluate(string? callerObjectId) => !string.IsNullOrEmpty(callerObjectId);

        public Task<RetrievalTrimResult<T>> TrimAsync<T>(
            IReadOnlyList<T> rows, Func<T, string?> documentIdOf, string? callerObjectId, CancellationToken cancellationToken = default)
        {
            Calls++;
            callerObjectId.Should().Be(CallerOid, "the trim is asked for the caller on the request");
            var kept = rows.Where(r => documentIdOf(r) is { } id && !Unreadable.Contains(id)).ToList();
            return Task.FromResult(new RetrievalTrimResult<T>(kept, RetrievalTrimOutcome.Evaluated, rows.Count - kept.Count));
        }

        public Task<RetrievalTrimResult<T>> TrimByRecordAsync<T>(
            IReadOnlyList<T> rows, Func<T, RetrievalRecordKey?> recordOf, string? callerObjectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Document search trims by document.");
    }
}
