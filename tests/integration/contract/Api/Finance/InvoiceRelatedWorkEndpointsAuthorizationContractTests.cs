using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// <c>GET /api/finance/invoices/{invoiceId}/lines/{lineId}/related-work</c> through the REAL <c>MapInvoiceRelatedWorkEndpoints</c>,
/// <see cref="FinanceAuthorizationFilter"/>, <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/>
/// (ebilling-r1 task 107, FR-34, ADR-008, ADR-019): 401 unauthenticated, 403 for a caller who cannot Read the invoice, the stable
/// <c>ebilling.disabled</c> 503 when the flag is off, the uniform 404 for a line of another invoice, and the HTTP shape of the 200 - including
/// that a document of another core record or one the caller cannot Read never appears in the body.
/// </summary>
/// <remarks>
/// The substitution is at the access-data boundary (a recording fake answering "what may this caller do to record X of set S"), at
/// <see cref="IGenericEntityService"/> and at <see cref="ISemanticSearchService"/> under the REAL <see cref="InvoiceRelatedWorkService"/>, so a
/// denied request is shown to read nothing from Dataverse and to search nothing. The service's own behaviour is <c>InvoiceRelatedWorkServiceTests</c>.
/// </remarks>
public sealed class InvoiceRelatedWorkEndpointsAuthorizationContractTests
{
    private const string InvoiceSet = "sprk_invoices";
    private const string DocumentSet = "sprk_documents";
    private const string Narrative = "Reviewed and revised the commercial lease amendment";

    private static readonly Guid InvoiceId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid LineId = Guid.Parse("00000000-0000-0000-0000-0000000001a1");
    private static readonly Guid TimekeeperId = Guid.Parse("00000000-0000-0000-0000-00000000aa01");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-00000000c001");
    private static readonly Guid MatterId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid OtherMatterId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Readable = Guid.Parse("00000000-0000-0000-0000-0000000d0001");
    private static readonly Guid Hidden = Guid.Parse("00000000-0000-0000-0000-0000000d0002");
    private static readonly Guid Foreign = Guid.Parse("00000000-0000-0000-0000-0000000d0003");

    private static string Url(Guid invoiceId, Guid lineId) => $"/api/finance/invoices/{invoiceId}/lines/{lineId}/related-work";

    // =========================================================================================
    // Authorization (ADR-008)
    // =========================================================================================

    [Fact]
    public async Task Unauthenticated_Returns401_AndReadsNothing()
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId), authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Access.Calls.Should().BeEmpty();
        host.Entities.VerifyNoOtherCalls();
        host.Search.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CallerWithNoRightsOnTheInvoice_Returns403_AndReadsAndSearchesNothing()
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, InvoiceId, HasToken: true));
        host.Entities.VerifyNoOtherCalls();
        host.Search.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> RefusedKinds => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    public static IEnumerable<object[]> InternalKinds => StatedCaller.InternalKinds.Select(k => new object[] { k });

    [Theory(DisplayName = "Task 099 must-fire: /related-work refuses a flagged-external, row-less or unreadable caller with 403, before any rights call, read or search, even when it holds Read")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_RefusesBeforeAnyRightsCallReadOrSearch(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read);
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Access.Calls.Should().BeEmpty();
        host.Entities.VerifyNoOtherCalls();
        host.Search.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "Task 099 must-not-fire: /related-work with a flag that reads false or blank reaches the record check as before")]
    [MemberData(nameof(InternalKinds))]
    public async Task StepZero_AdmitsAnInternalCaller(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, InvoiceId, HasToken: true));
    }

    [Fact]
    public async Task CallerWithoutReadOnTheInvoice_IsDenied_EvenWithOtherRights()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Write | AccessRights.Append);

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Entities.VerifyNoOtherCalls();
        host.Search.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadOnAnotherInvoice_IsNotReadOnThisOne_AndAQueryIdCannotSubstitute()
    {
        await using var host = await Host.StartAsync();
        var decoy = Guid.NewGuid();
        host.Access.Grant(InvoiceSet, decoy, AccessRights.Read);

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId) + $"?invoiceId={decoy}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(InvoiceId);
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FailedRightsCheck_IsDenied_NeverPassedThrough()
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
        host.Entities.VerifyNoOtherCalls();
        host.Search.VerifyNoOtherCalls();
    }

    // =========================================================================================
    // The eBilling kill switch (ADR-018): after authorization, never instead of it
    // =========================================================================================

    [Fact]
    public async Task FlagOff_AuthorizedCaller_Gets503EbillingDisabled_AndReadsNothing()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantRead();

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Entities.VerifyNoOtherCalls();
        host.Search.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FlagOff_UnauthorizedCaller_StillGets403_TheFlagNeverReplacesAuthorization()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================================
    // 200 and its shape
    // =========================================================================================

    [Fact]
    public async Task AuthorizedCaller_Gets200_RankedDocuments_NeverAnotherCoreRecordsNorAnUnreadableOne()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.AddDocument(Readable, "Lease amendment draft", OtherMatter: false);
        host.AddDocument(Hidden, "Privileged memo", OtherMatter: false);
        host.AddDocument(Foreign, "Other matter work", OtherMatter: true);
        host.Access.Grant(DocumentSet, Readable, AccessRights.Read);
        host.Access.Grant(DocumentSet, Hidden, AccessRights.Write);   // Write is not Read
        host.Access.Grant(DocumentSet, Foreign, AccessRights.Read);   // readable, but on another core record

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!.AsObject();
        json["status"]!.GetValue<string>().Should().Be("available");
        json["invoiceId"]!.GetValue<Guid>().Should().Be(InvoiceId);
        json["lineId"]!.GetValue<Guid>().Should().Be(LineId);
        json["windowDays"]!.GetValue<int>().Should().Be(7);
        var items = json["items"]!.AsArray();
        items.Select(i => i!["documentId"]!.GetValue<Guid>()).Should().Equal(new[] { Readable });
        items[0]!["name"]!.GetValue<string>().Should().Be("Lease amendment draft");
        items[0]!["score"]!.GetValue<double>().Should().BeApproximately(0.8, 1e-9);
        body.Should().NotContain(Hidden.ToString()).And.NotContain("Privileged memo")
            .And.NotContain(Foreign.ToString()).And.NotContain("Other matter work")
            .And.NotContain(Narrative);

        host.Access.Calls.Should().Contain(new FinAuth.AccessCall(FinAuth.AccessPath.Record, DocumentSet, Hidden, HasToken: true),
            "each candidate is put to the caller's own rights, with the caller's token");
        host.Access.Calls.Select(c => c.Id).Should().NotContain(Foreign, "a document of another core record is never even a candidate");
        host.SearchedIds.Should().Equal(new[] { Readable });
        host.SearchedQuery.Should().Be(Narrative);
    }

    [Fact]
    public async Task ExpenseLine_Returns200_EmptyWithReason()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.Line[ "sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Expense);

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["status"]!.GetValue<string>().Should().Be("empty");
        json["reason"]!.GetValue<string>().Should().Be("not-a-fee-line");
        json["items"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task RightsLookupFaultingOnADocument_Returns200_UnavailableWithStableCode_NoDocuments()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.AddDocument(Readable, "Lease amendment draft", OtherMatter: false);
        host.Access.ThrowForDocument = Readable;

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!.AsObject();
        json["status"]!.GetValue<string>().Should().Be("unavailable");
        json["code"]!.GetValue<string>().Should().Be("related-work.access-check-failed");
        json["items"]!.AsArray().Should().BeEmpty();
        body.Should().NotContain("Lease amendment draft");
        host.SearchedIds.Should().BeNull("a search is never run when the caller's rights could not be determined");
    }

    [Fact]
    public async Task LineOfAnotherInvoice_Returns404_TheUniformShape()
    {
        await using var host = await Host.StartAsync();
        host.GrantRead();
        host.Line["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());

        var response = await host.SendAsync(Request(Url(InvoiceId, LineId)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(InvoiceId.ToString()).And.NotContain(LineId.ToString());
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        host.Access.Calls.Select(c => c.Set).Should().OnlyContain(s => s == InvoiceSet);
    }

    // =========================================================================================
    // Host
    // =========================================================================================

    private static HttpRequestMessage Request(string url, bool authenticated = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        }

        return request;
    }

    private sealed class Host : IAsyncDisposable
    {
        /// <summary>The step-0 caller (task 099); internal unless a test changes <see cref="StatedCaller.Kind"/>.</summary>
        public StatedCaller StepZero { get; private set; } = null!;

        private WebApplication _app = null!;
        private HttpClient _client = null!;
        private readonly List<Entity> _documents = [];

        public AccessRecorder Access { get; } = new();
        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public Mock<ISemanticSearchService> Search { get; } = new(MockBehavior.Strict);
        public Entity Line { get; } = NewLine();
        public IReadOnlyList<Guid>? SearchedIds { get; private set; }
        public string? SearchedQuery { get; private set; }

        public static async Task<Host> StartAsync(bool ebillingEnabled = true)
        {
            var host = new Host();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TenantAwareAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TenantAwareAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TenantAwareAuthHandler>(TenantAwareAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            host.StepZero = StatedCaller.Register(builder.Services); // task 099: every /api/finance route runs step 0
            builder.Services.AddOptions<EbillingOptions>().Configure(o => o.Enabled = ebillingEnabled);

            // The REAL authorization stack, substituted only at the access-data boundary.
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();

            host.Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression q, CancellationToken _) => Task.FromResult(host.Answer(q)));
            host.Search
                .Setup(s => s.SearchAsync(It.IsAny<SemanticSearchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((SemanticSearchRequest r, string _, CancellationToken _) =>
                {
                    host.SearchedIds = r.DocumentIds!.Select(Guid.Parse).ToList();
                    host.SearchedQuery = r.Query;
                    var results = host.SearchedIds
                        .Select((id, i) => new SearchResult { DocumentId = id.ToString(), CombinedScore = 0.8 - i * 0.1 })
                        .ToList();
                    return Task.FromResult(new SemanticSearchResponse { Results = results, Metadata = new SearchMetadata() });
                });

            var settings = new Mock<IEbillingSettingsReader>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings());
            builder.Services.AddScoped(sp => new InvoiceRelatedWorkService(
                host.Entities.Object, settings.Object, sp.GetRequiredService<AuthorizationService>(), host.Search.Object,
                NullLogger<InvoiceRelatedWorkService>.Instance));

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapInvoiceRelatedWorkEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public void GrantRead() => Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read);

        public void AddDocument(Guid id, string name, bool OtherMatter)
        {
            var d = new Entity("sprk_document", id);
            d["sprk_documentname"] = name;
            d["createdon"] = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
            d["statecode"] = new OptionSetValue(0);
            d["sprk_matter"] = new EntityReference("sprk_matter", OtherMatter ? OtherMatterId : MatterId);
            d["sprk_createdbycontact"] = new EntityReference("contact", ContactId);
            _documents.Add(d);
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client.SendAsync(request);

        private static Entity NewLine()
        {
            var line = new Entity("sprk_billingevent", LineId);
            line["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId);
            line["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee);
            line["sprk_resolvedtimekeeper"] = new EntityReference("sprk_timekeeper", TimekeeperId);
            line["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
            line["sprk_eventdate"] = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
            line["sprk_description"] = Narrative;
            return line;
        }

        /// <summary>Evaluates the equality conditions of the query against the fixtures, as Dataverse would (the date window is the unit tests' concern).</summary>
        private EntityCollection Answer(QueryExpression q)
        {
            static bool Equal(Entity e, ConditionExpression c) => c.Operator != ConditionOperator.Equal || (
                c.AttributeName is "sprk_billingeventid" or "sprk_timekeeperid"
                    ? e.Id.Equals(c.Values[0])
                    : e.Contains(c.AttributeName) && e[c.AttributeName] switch
                    {
                        EntityReference r => r.Id.Equals(c.Values[0]),
                        OptionSetValue o => o.Value.Equals(c.Values[0]),
                        var v => v.Equals(c.Values[0]),
                    });

            IEnumerable<Entity> rows = q.EntityName switch
            {
                "sprk_billingevent" => [Line],
                "sprk_timekeeper" => [Timekeeper()],
                "sprk_document" => _documents,
                _ => throw new InvalidOperationException($"Unexpected query on {q.EntityName}."),
            };
            return new EntityCollection(rows.Where(e => q.Criteria.Conditions.All(c => Equal(e, c))).ToList());
        }

        private static Entity Timekeeper()
        {
            var tk = new Entity("sprk_timekeeper", TimekeeperId);
            tk["sprk_contact"] = new EntityReference("contact", ContactId);
            return tk;
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }

    /// <summary>The finance test caller plus the <c>tid</c> claim the tenant-isolated search needs (the shared handler carries only <c>oid</c>).</summary>
    private sealed class TenantAwareAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "RelatedWorkTest";

        public TenantAwareAuthHandler(
            Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(FinanceAuthzTestAuthHandler.CallerHeader))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim("oid", FinanceAuthzTestAuthHandler.CallerObjectId), new System.Security.Claims.Claim("tid", "tenant-1")], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(identity), SchemeName)));
        }
    }

    /// <summary>The shared recording fake plus a per-document fault, so the document path can fail while the invoice check succeeds.</summary>
    private sealed class AccessRecorder : IAccessDataSource
    {
        private readonly FinAuth.RecordingAccessDataSource _inner = new();

        public List<FinAuth.AccessCall> Calls => _inner.Calls;

        public Exception? ThrowOnEveryCall { get => _inner.ThrowOnEveryCall; set => _inner.ThrowOnEveryCall = value; }

        public Guid? ThrowForDocument { get; set; }

        public void Grant(string set, Guid id, AccessRights rights) => _inner.Grant(set, id, rights);

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
        {
            if (ThrowForDocument == recordId && entitySetName == DocumentSet)
            {
                Calls.Add(new FinAuth.AccessCall(FinAuth.AccessPath.Record, entitySetName, recordId, !string.IsNullOrEmpty(userAccessToken)));
                return Task.FromException<AccessSnapshot>(new InvalidOperationException("Dataverse unreachable"));
            }

            return _inner.GetRecordAccessAsync(userId, entitySetName, recordId, userAccessToken, ct);
        }

        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            _inner.GetUserAccessAsync(userId, resourceId, userAccessToken, ct);
    }
}
