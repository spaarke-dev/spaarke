using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// GOLDEN BYTES of the two search routes' access refusals (spaarkeai-mcp-bff-api-r2 task 027, second-review follow-up 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Task 027 moved the access decisions of <c>POST /api/ai/search</c>, <c>/count</c> and <c>/records</c> into shared
/// methods that answer with an outcome, a reason code and a detail, and a <c>ToProblem</c> in each filter renders it back to
/// the REST response. The other suites assert that the method and the route agree with EACH OTHER (so a changed
/// <c>Detail</c> moves both and passes) or only the status and code. Nothing pinned the literal title and detail the shipped
/// clients receive. This theory does: each case sends one input to the REAL route and asserts the status, title, detail,
/// <c>code</c> and <c>errorCode</c> as literals.
/// </para>
/// <para>
/// <b>Where the literals come from.</b> They were copied from the code BEFORE task 027
/// (<c>git show a90a8ac5a2^1:src/server/api/Sprk.Bff.Api/Api/Filters/SemanticSearchAuthorizationFilter.cs</c> and
/// <c>RecordSearchAuthorizationFilter.cs</c>, and <c>Api/Ai/SemanticSearchEndpoints.cs</c> for the count route's 403), not from the
/// current code, so this file anchors the old bytes. They are deliberately NOT built from <c>SearchErrorCodes</c> constants: a
/// renamed constant is a wire change, and that must fail here.
/// </para>
/// <para>
/// <b>Observation point.</b> The real filters, handlers, <see cref="AuthorizationService"/> and ProblemDetails serialization over
/// a <c>TestServer</c>, substituted only at the access-data source, the two search services and the retrieval trim (ADR-038).
/// A refused request must never reach the index; the host's counters prove it.
/// </para>
/// <para>
/// <b>Not REST-reachable, and why</b> (the coverage test below fails if an outcome is in neither list):
/// <c>UserContextRequired</c> needs no signed-in caller, which the route's authorization refuses first;
/// (a request body of <c>null</c> IS reachable: the filter, not model binding, refuses it, so it has a golden row);
/// <c>RecordTypeNotAuthorizable</c> needs a searchable record type with no entity-set mapping, and every searchable type is
/// mapped today. <c>CallerContextUnavailable</c> on the record route is not reachable through the filter (it reads the same
/// caller the handler does, in order), so it is pinned by running the shipped handler directly.
/// </para>
/// </remarks>
[Trait("category", "contract")]
public sealed class SearchRefusalGoldenBytesContractTests
{
    private const string Documents = "/api/ai/search";
    private const string Count = "/api/ai/search/count";
    private const string Records = "/api/ai/search/records";
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000027";
    private const string TenantId = "6f0c1a52-0000-4000-8000-0000000000aa";

    private static readonly string ParentId = Guid.Parse("11111111-2222-3333-4444-555555555555").ToString();
    private static readonly string DocumentId = Guid.Parse("66666666-7777-8888-9999-000000000000").ToString();

    /// <summary>
    /// One REST refusal: the request, who sends it, and the golden response.
    /// <paramref name="Extra"/> is any further <c>ProblemDetails</c> extension to pin (record type lists).
    /// </summary>
    private sealed record Golden(
        string Outcome,
        string Route,
        string Body,
        Who Caller,
        int Status,
        string Title,
        string Detail,
        string Code,
        Action<JsonObject>? Extra = null);

    private enum Who
    {
        /// <summary>oid + tid + bearer token.</summary>
        Full,

        NoTenant,
        NoOid,
        NoToken,
    }

    private static string Json(string scope, string? entityType = null, string? entityId = null, IEnumerable<string>? documentIds = null)
    {
        var o = new JsonObject { ["query"] = "indemnity", ["scope"] = scope };
        if (entityType is not null) { o["entityType"] = entityType; }
        if (entityId is not null) { o["entityId"] = entityId; }
        if (documentIds is not null)
        {
            var a = new JsonArray();
            foreach (var d in documentIds) { a.Add(d); }
            o["documentIds"] = a;
        }

        return o.ToJsonString();
    }

    private static string RecordJson(params string[] recordTypes)
    {
        var a = new JsonArray();
        foreach (var t in recordTypes) { a.Add(t); }
        return new JsonObject { ["query"] = "acme", ["recordTypes"] = a }.ToJsonString();
    }

    // The golden table. Titles, details and codes are the bytes of the code before task 027 (see the class remarks).
    private static readonly Golden[] Table =
    [
        // ── POST /api/ai/search: request phase ────────────────────────────────────────────────────────────────────
        new("MissingTenantIdentity", Documents, Json("all"), Who.NoTenant, 401, "Unauthorized",
            "Tenant identity not found in authentication token", "MISSING_TENANT_IDENTITY"),
        new("RequestBodyRequired", Documents, "null", Who.Full, 400, "Bad Request",
            "A search request body is required.", "REQUEST_BODY_REQUIRED"),
        new("MissingCallerIdentity", Documents, Json("all"), Who.NoOid, 401, "Unauthorized",
            "Caller identity not found in authentication token", "MISSING_CALLER_IDENTITY"),
        new("MissingCallerToken", Documents, Json("all"), Who.NoToken, 401, "Unauthorized",
            "A caller bearer token is required to evaluate access.", "MISSING_CALLER_TOKEN"),
        new("InvalidScope", Documents, Json("everything"), Who.Full, 400, "Invalid Scope",
            "Invalid scope value 'everything'. Valid values: all, entity, documentIds.", "INVALID_SCOPE"),
        new("EntityTypeRequired", Documents, Json("entity", entityId: ParentId), Who.Full, 400, "Bad Request",
            "entityType and entityId are required when scope=entity.", "ENTITY_TYPE_REQUIRED"),
        new("EntityIdRequired", Documents, Json("entity", entityType: "matter"), Who.Full, 400, "Bad Request",
            "entityType and entityId are required when scope=entity.", "ENTITY_ID_REQUIRED"),
        new("EntityTypeNotAuthorizable", Documents, Json("entity", "account", ParentId), Who.Full, 403, "Forbidden",
            "entityType 'account' is not supported for scoped search.", "ENTITY_TYPE_NOT_AUTHORIZABLE"),
        new("InvalidEntityId", Documents, Json("entity", "matter", "not-a-guid"), Who.Full, 400, "Bad Request",
            "entityId must be a non-empty GUID.", "INVALID_ENTITY_ID"),
        new("EntityAccessDenied", Documents, Json("entity", "matter", ParentId), Who.Full, 403, "Forbidden",
            "You do not have access to this record.", "ENTITY_ACCESS_DENIED"),
        new("DocumentIdsRequired", Documents, Json("documentIds", documentIds: []), Who.Full, 400, "Bad Request",
            "documentIds is required and must not be empty when scope=documentIds.", "DOCUMENT_IDS_REQUIRED"),
        new("TooManyDocumentIds", Documents,
            Json("documentIds", documentIds: Enumerable.Range(1, 101).Select(i => Guid.NewGuid().ToString())), Who.Full,
            400, "Too Many Document Ids", "documentIds is limited to 100 entries per request.", "TOO_MANY_DOCUMENT_IDS"),
        new("InvalidDocumentIds", Documents, Json("documentIds", documentIds: ["abc", DocumentId]), Who.Full,
            400, "Invalid Document Ids", "documentIds must be GUIDs. Invalid: abc", "INVALID_DOCUMENT_IDS"),
        new("InvalidDocumentIds", Documents,
            Json("documentIds", documentIds: ["a", "b", "c", "d", "e", "f", "g"]), Who.Full,
            400, "Invalid Document Ids", "documentIds must be GUIDs. Invalid: a, b, c, d, e (+2 more)", "INVALID_DOCUMENT_IDS"),
        new("NoReadableDocuments", Documents, Json("documentIds", documentIds: [DocumentId]), Who.Full, 403, "Forbidden",
            "You do not have access to any of the requested documents.", "NO_READABLE_DOCUMENTS"),

        // ── POST /api/ai/search/count ─────────────────────────────────────────────────────────────────────────────
        new("ScopeAllNotPermitted", Count, Json("all"), Who.Full, 403, "Forbidden",
            "scope=all is not supported for count. Count within a specific parent record "
            + "(scope=entity) or a specific set of documents (scope=documentIds).", "SCOPE_ALL_NOT_PERMITTED"),

        // ── POST /api/ai/search/records ───────────────────────────────────────────────────────────────────────────
        new("MissingTenantIdentity", Records, RecordJson("matter"), Who.NoTenant, 401, "Unauthorized",
            "Tenant identity not found in authentication token", "MISSING_TENANT_IDENTITY"),
        new("RequestBodyRequired", Records, "null", Who.Full, 400, "Bad Request",
            "A record search request body is required.", "REQUEST_BODY_REQUIRED"),
        new("MissingCallerIdentity", Records, RecordJson("matter"), Who.NoOid, 401, "Unauthorized",
            "Caller identity not found in authentication token", "MISSING_CALLER_IDENTITY"),
        new("MissingCallerToken", Records, RecordJson("matter"), Who.NoToken, 401, "Unauthorized",
            "A caller bearer token is required to evaluate access.", "MISSING_CALLER_TOKEN"),
        new("InvalidRecordTypes", Records, RecordJson("contract"), Who.Full, 400, "Invalid Record Types",
            "Invalid recordTypes value(s): 'contract'. Valid values: sprk_matter, sprk_project, sprk_invoice.", "INVALID_RECORD_TYPES",
            body =>
            {
                body["invalidValues"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal("contract");
                body["validValues"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal("sprk_matter", "sprk_project", "sprk_invoice");
            }),
        new("RecordTypesRequired", Records, RecordJson(), Who.Full, 400, "Record Types Required",
            "recordTypes is required and must contain at least one record type.", "INVALID_RECORD_TYPES"),
    ];

    /// <summary>Outcomes no REST request can produce (or that are pinned another way), with the reason. See the class remarks.</summary>
    private static readonly string[] SemanticNotRestReachable =
    [
        "UserContextRequired",   // no signed-in caller: the route's authorization refuses first
        "CallerContextUnavailable", // pinned by its own test: the 500 with no code
        "Allowed",
    ];

    private static readonly string[] RecordNotRestReachable =
    [
        "UserContextRequired",
        "RecordTypeNotAuthorizable", // every searchable record type has an entity-set mapping today
        "CallerContextUnavailable",  // pinned by its own test, through the shipped handler
        "Allowed",
    ];

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        for (var i = 0; i < Table.Length; i++)
        {
            data.Add(i.ToString());
        }

        return data;
    }

    [Theory(DisplayName = "Search refusals over REST carry the golden status, title, detail, code and errorCode, and never reach the index")]
    [MemberData(nameof(Cases))]
    public async Task Refusal_CarriesTheGoldenBytes(string index)
    {
        var golden = Table[int.Parse(index)];
        await using var host = await GoldenHost.StartAsync();

        using var response = await host.PostAsync(golden.Route, golden.Body, golden.Caller);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonNode.Parse(raw)!.AsObject();

        ((int)response.StatusCode).Should().Be(golden.Status, $"{golden.Outcome}: {raw}");
        body["status"]!.GetValue<int>().Should().Be(golden.Status, golden.Outcome);
        body["title"]!.GetValue<string>().Should().Be(golden.Title, golden.Outcome);
        body["detail"]!.GetValue<string>().Should().Be(golden.Detail, golden.Outcome);
        body["code"]!.GetValue<string>().Should().Be(golden.Code, golden.Outcome);
        body["errorCode"]!.GetValue<string>().Should().Be(golden.Code, golden.Outcome);
        body["correlationId"]!.GetValue<string>().Should().NotBeNullOrEmpty(golden.Outcome);
        golden.Extra?.Invoke(body);
        host.IndexCalls.Should().Be(0, "a refused request never reaches the index");
    }

    [Fact(DisplayName = "scope=all search whose caller token vanishes after the request phase: the 500 'Caller context not available.' with no code, never the unfiltered answer")]
    public async Task DocumentSearch_CallerContextUnavailable_Is500_WithNoCode()
    {
        await using var host = await GoldenHost.StartAsync();
        host.OnSearch = http => http.Request.Headers.Remove("Authorization");

        using var response = await host.PostAsync(Documents, Json("all"), Who.Full);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonNode.Parse(raw)!.AsObject();

        ((int)response.StatusCode).Should().Be(500, raw);
        body["title"]!.GetValue<string>().Should().Be("Internal Server Error");
        body["detail"]!.GetValue<string>().Should().Be("Caller context not available.");
        body.ContainsKey("code").Should().BeFalse("this 500 has never carried a code");
        body.ContainsKey("errorCode").Should().BeFalse("this 500 has never carried a code");
        body.ContainsKey("results").Should().BeFalse("never the unfiltered cross-record answer");
        host.IndexCalls.Should().Be(1, "the document path searches first and checks the caller on the way back");
    }

    [Fact(DisplayName = "Record search handler reached with no caller token: the 500 'Caller context not available.' with no code, and the search does not run")]
    public async Task RecordSearch_CallerContextUnavailable_Is500_WithNoCode()
    {
        var search = new CountingRecordSearch();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", TenantId), new Claim(CallerResolution.ObjectIdClaim, CallerOid)], "Test")),
        };

        // What RecordSearchAuthorizationFilter hands the handler; no Authorization header, which the filter proved present.
        http.Items[RecordSearchAuthorization.HttpContextItemsKey] = new RecordSearchAuthorization
        {
            RequiresPerRowRecordAuthorization = true,
            RequestedEntitySets = new Dictionary<string, string> { [RecordEntityType.Matter] = "sprk_matters" },
        };

        var result = await RecordSearchEndpoints.PostRecordSearch(
            new RecordSearchRequest { Query = "acme", RecordTypes = [RecordEntityType.Matter] },
            search,
            new AuthorizationService(new PlaybookRouteAuthorizationContractTests.RecordingAccessDataSource(), Array.Empty<IAuthorizationRule>(), NullLogger<AuthorizationService>.Instance),
            http,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
        problem.ProblemDetails.Title.Should().Be("Internal Server Error");
        problem.ProblemDetails.Detail.Should().Be("Caller context not available.");
        problem.ProblemDetails.Extensions.Should().NotContainKey("code");
        problem.ProblemDetails.Extensions.Should().NotContainKey("errorCode");
        search.Calls.Should().Be(0, "no caller, no search");
    }

    [Fact(DisplayName = "Every document-search and record-search refusal outcome is either pinned by a golden row or listed as not REST-reachable")]
    public void EveryRefusalOutcome_IsPinnedOrAccountedFor()
    {
        var documentRows = Table.Where(g => g.Route != Records).Select(g => g.Outcome).ToHashSet();
        var recordRows = Table.Where(g => g.Route == Records).Select(g => g.Outcome).ToHashSet();

        var unaccountedDocuments = Enum.GetNames<SemanticSearchAccessOutcome>()
            .Where(n => !documentRows.Contains(n) && !SemanticNotRestReachable.Contains(n)).ToList();
        var unaccountedRecords = Enum.GetNames<RecordSearchAccessOutcome>()
            .Where(n => !recordRows.Contains(n) && !RecordNotRestReachable.Contains(n)).ToList();

        unaccountedDocuments.Should().BeEmpty("a new SemanticSearchAccessOutcome needs a golden row (or a stated reason it is not REST-reachable)");
        unaccountedRecords.Should().BeEmpty("a new RecordSearchAccessOutcome needs a golden row (or a stated reason it is not REST-reachable)");
        documentRows.Concat(recordRows).Should().OnlyContain(
            n => Enum.GetNames<SemanticSearchAccessOutcome>().Contains(n) || Enum.GetNames<RecordSearchAccessOutcome>().Contains(n),
            "a golden row names a real outcome");
    }

    // ── host ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class CountingRecordSearch : IRecordSearchService
    {
        public int Calls { get; private set; }

        public Task<RecordSearchResponse> SearchAsync(RecordSearchRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new RecordSearchResponse
            {
                Results = [],
                Metadata = new RecordSearchMetadata { TotalCount = 0, SearchTime = 1, HybridMode = RecordHybridSearchMode.Rrf },
            });
        }
    }

    /// <summary>Authenticates a request that sends <c>X-Test-Auth</c>, with exactly the claims the request's headers name.</summary>
    private sealed class GoldenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "GoldenSearch";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Auth"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new("sub", "pairwise-subject-not-an-oid") };
            if (Request.Headers.TryGetValue("X-Test-Oid", out var oid)) { claims.Add(new Claim("oid", oid.ToString())); }
            if (Request.Headers.TryGetValue("X-Test-Tid", out var tid)) { claims.Add(new Claim("tid", tid.ToString())); }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class GoldenHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;
        private int _indexCalls;

        public int IndexCalls => Volatile.Read(ref _indexCalls);

        /// <summary>Runs inside the document index call, with the request's own context (lets a test change the request mid-flight).</summary>
        public Action<HttpContext>? OnSearch { get; set; }

        public static async Task<GoldenHost> StartAsync()
        {
            var host = new GoldenHost();
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
                    o.DefaultAuthenticateScheme = GoldenAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = GoldenAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, GoldenAuthHandler>(GoldenAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("ai-batch", _ => RateLimitPartition.GetNoLimiter("ai-batch-test")));

            // Nobody holds any right: every access question the refusals reach is answered "none".
            var world = new AppOnlyDocumentWorld();
            builder.Services.AddSingleton<IAccessDataSource>(new PlaybookRouteAuthorizationContractTests.RecordingAccessDataSource());
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddStatedNarrowingCaller(() => NarrowingCallerMode.Internal, () => world);
            builder.Services.AddSingleton<ISemanticSearchService>(sp => new StubSemanticSearch(this, sp.GetRequiredService<IHttpContextAccessor>()));
            builder.Services.AddSingleton<IRecordSearchService>(sp => new StubRecordSearch(this));
            builder.Services.AddSingleton<IRetrievalAccessTrim>(new KeepAllTrim());

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

        public Task<HttpResponseMessage> PostAsync(string route, string json, Who caller)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            message.Headers.Add("X-Test-Auth", "1");
            if (caller != Who.NoOid) { message.Headers.Add("X-Test-Oid", CallerOid); }
            if (caller != Who.NoTenant) { message.Headers.Add("X-Test-Tid", TenantId); }
            if (caller != Who.NoToken) { message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token"); }
            return _client!.SendAsync(message);
        }

        internal void CountIndexCall() => Interlocked.Increment(ref _indexCalls);

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.DisposeAsync();
            }
        }
    }

    private sealed class StubSemanticSearch(GoldenHost host, IHttpContextAccessor accessor) : ISemanticSearchService
    {
        public Task<SemanticSearchResponse> SearchAsync(
            SemanticSearchRequest request, string tenantId, CancellationToken cancellationToken = default)
        {
            host.CountIndexCall();
            if (accessor.HttpContext is { } http)
            {
                host.OnSearch?.Invoke(http);
            }

            return Task.FromResult(new SemanticSearchResponse
            {
                Results = [],
                Metadata = new SearchMetadata { TotalResults = 0, ReturnedResults = 0, ExecutedMode = "rrf" },
            });
        }

        public Task<SemanticSearchCountResponse> CountAsync(
            SemanticSearchRequest request, string tenantId, CancellationToken cancellationToken = default)
        {
            host.CountIndexCall();
            return Task.FromResult(new SemanticSearchCountResponse());
        }
    }

    private sealed class StubRecordSearch(GoldenHost host) : IRecordSearchService
    {
        public Task<RecordSearchResponse> SearchAsync(RecordSearchRequest request, CancellationToken cancellationToken = default)
        {
            host.CountIndexCall();
            return Task.FromResult(new RecordSearchResponse
            {
                Results = [],
                Metadata = new RecordSearchMetadata { TotalCount = 0, SearchTime = 1, HybridMode = RecordHybridSearchMode.Rrf },
            });
        }
    }

    private sealed class KeepAllTrim : IRetrievalAccessTrim
    {
        public bool CanEvaluate(string? callerObjectId) => !string.IsNullOrEmpty(callerObjectId);

        public Task<RetrievalTrimResult<T>> TrimAsync<T>(
            IReadOnlyList<T> rows, Func<T, string?> documentIdOf, string? callerObjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RetrievalTrimResult<T>(rows.ToList(), RetrievalTrimOutcome.Evaluated, 0));

        public Task<RetrievalTrimResult<T>> TrimByRecordAsync<T>(
            IReadOnlyList<T> rows, Func<T, RetrievalRecordKey?> recordOf, string? callerObjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RetrievalTrimResult<T>(rows.ToList(), RetrievalTrimOutcome.Evaluated, 0));
    }
}
