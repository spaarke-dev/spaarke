using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Records;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Xunit;
using FamilyKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.FamilyKind;

namespace Sprk.Bff.Api.Tests.Domain.RecordFamilyEndpoint;

/// <summary>
/// field-mapping-server-write-path-r1 task 072 (FR-31; design §12.3 C) — <c>GET /api/v1/records/{table}/{id}/family</c>
/// through the REAL <c>MapRecordFamilyEndpoints</c> and the REAL <see cref="RecordFamilyReader"/>, on a minimal host (the
/// push contract tests' shape). Substituted at module boundaries only: <see cref="CallerRecordAccessProbe"/> (its virtual
/// rights and WhoAmI seams) and <see cref="IImpersonatedCommunicationQuery"/> (an in-memory caller-scoped Dataverse that
/// records every call). Contract cases only (ADR-038 KEEP path, tests/unit/domain/**).
/// </summary>
public class RecordFamilyEndpointsTests
{
    private const string Matter = "sprk_matter";
    private const string Document = "sprk_document";

    private static readonly Guid MatterId = Guid.Parse("07207207-0000-4000-8000-000000000001");
    private static readonly Guid DocumentId = Guid.Parse("07207207-0000-4000-8000-000000000002");
    private static readonly Guid UnknownId = Guid.Parse("07207207-0000-4000-8000-0000000000ff");
    private static readonly Guid CallerOid = Guid.Parse("07207207-0000-4000-8000-0000000000a1");
    private static readonly Guid CallerSystemUserId = Guid.Parse("07207207-0000-4000-8000-0000000000a2");

    private static string Route(string table, Guid id, string? query = null) =>
        $"/api/v1/records/{table}/{id}/family{(query is null ? string.Empty : "?" + query)}";

    private static HttpRequestMessage Authenticated(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(FamilyTestAuthHandler.CallerHeader, "1");
        return request;
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(FamilyHost host, string url)
    {
        using var response = await host.SendAsync(Authenticated(url));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A ProblemDetails body without the per-request ids, so two answers can be compared byte for byte.</summary>
    private static string Normalize(string body)
    {
        var json = JsonNode.Parse(body)!.AsObject();
        json.Remove("traceId");
        json.Remove("correlationId");
        return json.ToJsonString();
    }

    private static FamilyHost ReadableFamily(FamilyHost host)
    {
        host.Dataverse.Add(Matter, MatterId, ("sprk_mattername", "Matter A"), ("sprk_matternumber", "MAT-001"));
        host.Dataverse.Add(Document, DocumentId, ("sprk_documentname", "Document A"), ("sprk_document_number", "DOC-001"),
            ("_sprk_matter_value", MatterId.ToString()));
        host.Probe.Grant("sprk_documents", DocumentId, AccessRights.Read);
        host.Probe.Grant("sprk_matters", MatterId, AccessRights.Read);
        host.SeedTableDisplayName(Matter, "Matter");
        host.SeedTableDisplayName(Document, "Document");
        return host;
    }

    // ── recordType: the table's display name, and never a reason to fail the read ──

    [Fact]
    public async Task Family_AMetadataFailureForOneTable_GivesNullForThatTablesMembers_AndTheReadStillReturns200()
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());
        host.UnseedTableDisplayName(Document); // the metadata read for sprk_document now falls through to Dataverse and faults

        var (status, body) = await GetAsync(host, Route(Document, DocumentId));

        status.Should().Be(HttpStatusCode.OK, body);
        var members = JsonNode.Parse(body)!["members"]!.AsArray().Select(m => m!.AsObject()).ToList();
        members.Should().HaveCount(2);
        members.Single(m => m["table"]!.GetValue<string>() == Matter)["recordType"]!.GetValue<string>().Should().Be("Matter");
        members.Single(m => m["table"]!.GetValue<string>() == Document)["recordType"].Should().BeNull();
    }

    // ── 200: the documented shape ──

    [Fact]
    public async Task Family_ForAReadableAnchor_Returns200WithTheDocumentedShape_AndTheAnchorIsAmongTheMembers()
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());

        var (status, body) = await GetAsync(host, Route(Document, DocumentId));

        status.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!.AsObject();
        json.Select(p => p.Key).Should().BeEquivalentTo(new[] { "anchor", "root", "truncated", "continuation", "members" },
            "no counts, no access metadata");
        Ref(json["anchor"]).Should().Be((DocumentId.ToString(), Document, "Document A"));
        Ref(json["root"]).Should().Be((MatterId.ToString(), Matter, "Matter A"), "the highest readable ancestor, on the full view");
        json["truncated"]!.GetValue<bool>().Should().BeFalse();
        json["continuation"].Should().BeNull();

        var members = json["members"]!.AsArray().Select(m => m!.AsObject()).ToList();
        members.Select(m => m["id"]!.GetValue<string>()).Should().Equal(MatterId.ToString(), DocumentId.ToString());
        members.Select(m => m["id"]!.GetValue<string>()).Should().Contain(DocumentId.ToString(), "the first page always holds the anchor");
        foreach (var member in members)
        {
            member.Select(p => p.Key).Should().BeEquivalentTo(new[]
            {
                "id", "table", "name", "recordNumber", "recordType", "status", "modifiedOn", "kind", "edgeKind", "parentId", "depth",
            });
            member["kind"]!.GetValue<string>().Should().Be("Filing");
            member["recordType"]!.GetValue<string>().Should().Be(member["table"]!.GetValue<string>() == Matter ? "Matter" : "Document",
                "the display name of the member's TABLE");
        }

        var top = members[0];
        top["table"]!.GetValue<string>().Should().Be(Matter);
        top["name"]!.GetValue<string>().Should().Be("Matter A");
        top["recordNumber"]!.GetValue<string>().Should().Be("MAT-001");
        top["parentId"].Should().BeNull();
        top["depth"]!.GetValue<int>().Should().Be(0);
        (top["edgeKind"] is null || top["edgeKind"]!.GetValue<string>() == string.Empty)
            .Should().BeTrue("the top has no edge; the client reads null or '' as none");

        var child = members[1];
        child["table"]!.GetValue<string>().Should().Be(Document);
        child["recordNumber"]!.GetValue<string>().Should().Be("DOC-001");
        child["parentId"]!.GetValue<string>().Should().Be(MatterId.ToString());
        child["depth"]!.GetValue<int>().Should().Be(1);
        Enum.TryParse<RecordFamilyEdges.EdgeKind>(child["edgeKind"]!.GetValue<string>(), out _)
            .Should().BeTrue("a member under a parent names the kind of filing edge");

        host.Dataverse.Calls.Should().NotBeEmpty().And.OnlyContain(c => c.Caller == CallerSystemUserId,
            "every family read impersonates the caller");
    }

    [Theory]
    [InlineData("direction=down")]
    [InlineData("direction=DOWN&kind=filing")]
    public async Task Family_Down_OmitsTheRoot_AndListsTheAnchorOnTop(string query)
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());

        var (status, body) = await GetAsync(host, Route(Document, DocumentId, query));

        status.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!.AsObject();
        json["root"].Should().BeNull("down never reaches above the anchor, and nothing hints at what is there");
        json["members"]!.AsArray().Select(m => m!["id"]!.GetValue<string>()).Should().Equal(DocumentId.ToString());
        Ref(json["anchor"]).Id.Should().Be(DocumentId.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("direction=full")]
    [InlineData("direction=FULL&kind=Filing")]
    public async Task Family_FullIsTheDefault_AndCaseInsensitive_AndCarriesTheRoot(string? query)
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());

        var (status, body) = await GetAsync(host, Route(Document, DocumentId, query));

        status.Should().Be(HttpStatusCode.OK, body);
        Ref(JsonNode.Parse(body)!["root"]).Id.Should().Be(MatterId.ToString());
    }

    // ── 404: one answer for unknown, unreadable, not a family table, and every probe fault ──

    [Fact]
    public async Task Family_UnknownAnchorAndUnreadableAnchor_AreTheSame404_BothAskedThroughTheEqualisedProbe()
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());
        var unreadable = Guid.Parse("07207207-0000-4000-8000-0000000000e1");
        host.Dataverse.Add(Document, unreadable, ("sprk_documentname", "Secret"), ("_sprk_matter_value", MatterId.ToString()));
        // The caller holds nothing on `unreadable`; `UnknownId` has no row at all.

        var unknown = await GetAsync(host, Route(Document, UnknownId));
        var denied = await GetAsync(host, Route(Document, unreadable));

        unknown.Status.Should().Be(HttpStatusCode.NotFound);
        denied.Status.Should().Be(HttpStatusCode.NotFound);
        Normalize(denied.Body).Should().Be(Normalize(unknown.Body), "an unreadable anchor answers exactly as one that does not exist");
        denied.Body.Should().NotContain(unreadable.ToString()).And.NotContain("Secret");
        unknown.Body.Should().NotContain(UnknownId.ToString());

        host.Probe.Asked.Should().Equal(
            new[] { ("sprk_documents", UnknownId, true), ("sprk_documents", unreadable, true) },
            "both went through GetCallerRightsForAuthorizationGateAsync (#2137), the equalised schedule");
        host.Dataverse.Calls.Should().BeEmpty("nothing is read for a record the caller may not read");
    }

    [Theory]
    [InlineData("probe-throws")]
    [InlineData("caller-unresolvable")]
    [InlineData("vanished-after-probe")]
    public async Task Family_AProbeFaultAnUnresolvableCallerOrAVanishedAnchor_IsTheSame404(string shape)
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());
        switch (shape)
        {
            case "probe-throws": host.Probe.Fault = new HttpRequestException("RetrievePrincipalAccess unavailable"); break;
            case "caller-unresolvable": host.Probe.SystemUserId = null; break;
            case "vanished-after-probe": host.Probe.Grant("sprk_documents", UnknownId, AccessRights.Read); break; // Read, but no row
        }

        var unknown = await GetAsync(host, Route(Document, UnknownId));
        var answered = await GetAsync(host, Route(Document, shape == "vanished-after-probe" ? UnknownId : DocumentId));

        answered.Status.Should().Be(HttpStatusCode.NotFound);
        Normalize(answered.Body).Should().Be(Normalize(unknown.Body));
        answered.Body.Should().NotContain(DocumentId.ToString());
    }

    [Theory]
    [InlineData("contact")] // authorizable elsewhere (EntityAccessFilter's map), not a family table
    [InlineData("sprk_notatable")]
    [InlineData("matter")] // an alias: only logical names are family tables
    public async Task Family_ATableThatIsNotAFamilyTable_IsTheSame404_WithNoProbeAndNoDataverseCall(string table)
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());
        host.Probe.Grant("contacts", DocumentId, AccessRights.Read);

        var known = await GetAsync(host, Route(Document, UnknownId));
        host.Probe.Asked.Clear();
        var (status, body) = await GetAsync(host, Route(table, DocumentId));

        status.Should().Be(HttpStatusCode.NotFound);
        Normalize(body).Should().Be(Normalize(known.Body));
        host.Probe.Asked.Should().BeEmpty("table names are public; the gate needs no probe");
        host.Dataverse.Calls.Should().BeEmpty();
    }

    // ── 400: a bad query is refused before any probe or Dataverse call ──

    [Theory]
    [InlineData("kind=Priority")]
    [InlineData("kind=")]
    [InlineData("kind=Filing&kind=Filing")]
    [InlineData("direction=sideways")]
    [InlineData("direction=")]
    [InlineData("direction=down&direction=full")]
    public async Task Family_ANotSupportedKindOrDirection_Is400_BeforeAnyProbeOrDataverseCall_AndEchoesNothing(string query)
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());

        var (status, body) = await GetAsync(host, Route(Document, DocumentId, query));

        status.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().NotContain("Priority").And.NotContain("sideways").And.NotContain(DocumentId.ToString());
        host.Probe.Asked.Should().BeEmpty();
        host.Probe.WhoAmICalls.Should().Be(0);
        host.Dataverse.Calls.Should().BeEmpty();
    }

    // ── 400: ONE answer for a malformed, tampered or re-bound continuation ──

    [Fact]
    public async Task Family_AnInvalidOrTamperedContinuation_IsTheSame400AsAMalformedOne()
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());

        // A token this reader minted for ANOTHER caller (a re-bound one), a tampered copy of it, plain garbage, and an
        // oversize value: the reader refuses all of them with one outcome, and the endpoint gives it one body.
        var foreign = RecordFamilyReader.ProtectContinuation(
            host.Protector, new RecordFamilyReader.ContinuationState(
                1, Guid.NewGuid(), Document, DocumentId, (int)FamilyKind.Filing, (int)RecordFamilyDirection.Full, Matter, MatterId, 500));
        var bound = RecordFamilyReader.ProtectContinuation(
            host.Protector, new RecordFamilyReader.ContinuationState(
                1, CallerOid, Document, DocumentId, (int)FamilyKind.Filing, (int)RecordFamilyDirection.Full, Matter, MatterId, 500));
        var middle = bound.Length / 2;
        var tampered = bound[..middle] + (bound[middle] == 'A' ? "B" : "A") + bound[(middle + 1)..];

        var answers = new List<(HttpStatusCode Status, string Body)>();
        foreach (var token in new[] { foreign, tampered, "not-a-token", new string('A', 5000) })
        {
            answers.Add(await GetAsync(host, Route(Document, DocumentId, "continuation=" + Uri.EscapeDataString(token))));
        }

        answers.Should().OnlyContain(a => a.Status == HttpStatusCode.BadRequest);
        answers.Select(a => Normalize(a.Body)).Distinct().Should().HaveCount(1, "one fixed body for every invalid continuation");
        answers.Should().OnlyContain(a => !a.Body.Contains(foreign) && !a.Body.Contains("not-a-token"));
    }

    // ── 401 ──

    [Fact]
    public async Task Family_Unauthenticated_Is401_AndNothingIsTouched()
    {
        await using var host = ReadableFamily(await FamilyHost.StartAsync());

        using var response = await host.SendAsync(new HttpRequestMessage(HttpMethod.Get, Route(Document, DocumentId)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Probe.Asked.Should().BeEmpty();
        host.Probe.WhoAmICalls.Should().Be(0);
        host.Dataverse.Calls.Should().BeEmpty();
    }

    private static (string? Id, string? Table, string? Name) Ref(JsonNode? node) =>
        node is null ? default : (node["id"]?.GetValue<string>(), node["table"]?.GetValue<string>(), node["name"]?.GetValue<string>());

    // ── fixtures ──

    /// <summary>The probe's two virtual seams: rights per (entity set, id), and WhoAmI. Records whether each rights question
    /// came through the equalised gate method (its AsyncLocal is set while the gate asks).</summary>
    private sealed class SeamProbe : CallerRecordAccessProbe
    {
        private static readonly FieldInfo EqualizeField = typeof(CallerRecordAccessProbe).GetField(
            "EqualizeNoAccessWithNotFound", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CallerRecordAccessProbe no longer carries the gate's AsyncLocal; update this test.");

        private readonly Dictionary<(string, Guid), AccessRights> _granted = new();

        public SeamProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public List<(string EntitySet, Guid Id, bool ViaGate)> Asked { get; } = new();
        public int WhoAmICalls { get; private set; }
        public Guid? SystemUserId { get; set; } = CallerSystemUserId;
        public Exception? Fault { get; set; }

        public void Grant(string entitySet, Guid id, AccessRights rights) => _granted[(entitySet, id)] = rights;

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            Asked.Add((entitySet, recordId, ((AsyncLocal<bool>)EqualizeField.GetValue(null)!).Value));
            if (Fault is not null)
                return Task.FromException<AccessRights>(Fault);
            return Task.FromResult(
                !string.IsNullOrEmpty(callerBearerToken) && _granted.TryGetValue((entitySet, recordId), out var rights) ? rights : AccessRights.None);
        }

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
        {
            WhoAmICalls++;
            return Task.FromResult(string.IsNullOrEmpty(callerBearerToken) ? null : SystemUserId);
        }
    }

    /// <summary>An in-memory caller-scoped Dataverse for two tables. Answers `{table}id eq {id}` and `_{lookup}_value eq {id}`
    /// filters; every other entity set answers no rows. Records every call with the caller it impersonated.</summary>
    private sealed class FakeCallerDataverse : IImpersonatedCommunicationQuery
    {
        private static readonly IReadOnlyDictionary<string, string> TableBySet = new Dictionary<string, string>
        {
            ["sprk_matters"] = Matter,
            ["sprk_documents"] = Document,
        };

        private readonly List<(string Table, Dictionary<string, JsonElement> Row)> _rows = new();

        public List<(string Set, string Query, Guid Caller)> Calls { get; } = new();

        public void Add(string table, Guid id, params (string Column, string Value)[] values)
        {
            var row = new Dictionary<string, JsonElement>
            {
                [table + "id"] = JsonSerializer.SerializeToElement(id.ToString()),
                ["statuscode@OData.Community.Display.V1.FormattedValue"] = JsonSerializer.SerializeToElement("Active"),
                ["modifiedon"] = JsonSerializer.SerializeToElement("2026-10-01T12:00:00Z"),
            };
            foreach (var (column, value) in values)
                row[column] = JsonSerializer.SerializeToElement(value);
            _rows.Add((table, row));
        }

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            Calls.Add((entitySetName, odataQuery ?? string.Empty, callerSystemUserId));
            var matches = new List<Dictionary<string, JsonElement>>();
            if (TableBySet.TryGetValue(entitySetName, out var table))
            {
                var filter = (odataQuery ?? string.Empty).Split('&').FirstOrDefault(p => p.StartsWith("$filter=", StringComparison.Ordinal)) ?? string.Empty;
                foreach (var (rowTable, row) in _rows.Where(r => r.Table == table))
                {
                    var id = row[table + "id"].GetString()!;
                    var parents = row.Where(c => c.Key.StartsWith('_') && c.Key.EndsWith("_value", StringComparison.Ordinal))
                        .Select(c => c.Key + " eq " + c.Value.GetString());
                    if (filter.Contains($"{table}id eq {id}", StringComparison.OrdinalIgnoreCase)
                        || parents.Any(p => filter.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        matches.Add(row);
                    }
                }
            }

            return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(matches);
        }
    }

    private sealed class FamilyTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "FamilyTest";
        public const string CallerHeader = "X-Test-Caller";

        public FamilyTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(CallerHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(new[] { new Claim("oid", CallerOid.ToString()) }, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class FamilyHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public SeamProbe Probe { get; } = new();
        public FakeCallerDataverse Dataverse { get; } = new();
        private EphemeralDataProtectionProvider Keys { get; } = new();
        public IDataProtector Protector => Keys.CreateProtector(RecordFamilyReader.ContinuationPurpose);
        private readonly MemoryDistributedCache _metadataCache = new(Options.Create(new MemoryDistributedCacheOptions()));

        /// <summary>The cached metadata of <paramref name="table"/> as MetadataService wrote it (key and camelCase JSON).</summary>
        public void SeedTableDisplayName(string table, string displayName) =>
            _metadataCache.SetString("sdap:dv:entitymetadata:" + table, JsonSerializer.Serialize(
                new EntityMetadataDto(table, table + "id", "name", Array.Empty<AttributeDto>(), displayName),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        /// <summary>No cache entry: MetadataService goes to Dataverse, which the substituted service cannot reach (a fault).</summary>
        public void UnseedTableDisplayName(string table) => _metadataCache.Remove("sdap:dv:entitymetadata:" + table);

        public static async Task<FamilyHost> StartAsync()
        {
            var host = new FamilyHost();
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
                    o.DefaultAuthenticateScheme = FamilyTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = FamilyTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, FamilyTestAuthHandler>(FamilyTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));
            builder.Services.AddSingleton<CallerRecordAccessProbe>(Probe);
            builder.Services.AddSingleton<IImpersonatedCommunicationQuery>(Dataverse);
            builder.Services.AddSingleton(new RecordFamilyReader(Dataverse, Keys, NullLogger<RecordFamilyReader>.Instance));
            builder.Services.AddSingleton(new MetadataService(
                new Mock<IDataverseService>().Object, _metadataCache, NullLogger<MetadataService>.Instance));

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            _app.MapRecordFamilyEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client!.SendAsync(request);

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
}
