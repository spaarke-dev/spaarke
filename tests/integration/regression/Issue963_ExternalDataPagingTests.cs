using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Types;
using WireMock.Util;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression;

/// <summary>
/// GitHub #963 (register ISS-002, unified-access-control-r2 task 105): the external app's lists were cut at 200 rows
/// with nothing saying so. <c>ExternalDataService.GetCollectionAsync</c> read one page and never followed
/// <c>@odata.nextLink</c>, and the four list reads (documents, to-dos, events, the participant grant rows behind the
/// contact list) pinned <c>$top=200</c> — which makes Dataverse return no <c>nextLink</c> at all. A project with 250
/// documents showed 200, indistinguishable from a complete list (NFR-03: a cap is never silent).
/// </summary>
/// <remarks>
/// <para><b>The boundary.</b> A fake Dataverse Web API on loopback (WireMock) that pages the way the real service does,
/// verified live on spaarkedev1 2026-10-08: it honours <c>Prefer: odata.maxpagesize</c> and emits
/// <c>@odata.nextLink</c> with a <c>$skiptoken</c>; a <c>$top</c> query gets NO <c>nextLink</c>, and <c>$top</c> with
/// <c>maxpagesize</c> gets the smaller of the two, also with no <c>nextLink</c>. The service under test is the real
/// one, with a real <see cref="HttpClient"/> — nothing between it and the wire is substituted.</para>
/// <para><b>Each read is driven through its public method</b>, so the request the code really builds is what the fake
/// answers.</para>
/// </remarks>
[Trait("status", "new")]
public sealed class Issue963_ExternalDataPagingTests : IDisposable
{
    private static readonly Guid Root = Guid.Parse("96300000-0000-0000-0000-000000000105");

    private readonly FakeDataverse _dataverse = new();
    private readonly CapturingLogger _logger = new();
    private readonly ExternalDataService _sut;

    public Issue963_ExternalDataPagingTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = _dataverse.ServiceUrl })
            .Build();
        _sut = new ExternalDataService(new HttpClient(), configuration, new FakeCredential(), _logger);
    }

    public void Dispose() => _dataverse.Dispose();

    // ── The four reads, each through its public method ─────────────────────────────────────────────────────────

    /// <summary>The four list reads of ISS-002 and the entity set each one pages.</summary>
    public static TheoryData<string> FourReads() => new() { "documents", "todos", "events", "contacts" };

    private static string PagedSetOf(string read) => read switch
    {
        "documents" => "sprk_documents",
        "todos" => "sprk_todos",
        "events" => "sprk_events",
        "contacts" => "sprk_externalrecordaccesses",
        _ => throw new ArgumentOutOfRangeException(nameof(read)),
    };

    /// <summary>Seeds <paramref name="count"/> children of <see cref="Root"/> for <paramref name="read"/>.</summary>
    private void Seed(string read, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var id = IdFor(i);
            switch (read)
            {
                case "documents":
                    _dataverse.Add("sprk_documents", new JsonObject
                    {
                        ["sprk_documentid"] = id, ["sprk_documentname"] = $"Doc {i}", ["_sprk_project_value"] = Root.ToString(),
                    });
                    break;
                case "todos":
                    _dataverse.Add("sprk_todos", new JsonObject { ["sprk_todoid"] = id, ["sprk_name"] = $"To-do {i}" });
                    break;
                case "events":
                    _dataverse.Add("sprk_events", new JsonObject { ["sprk_eventid"] = id, ["sprk_eventname"] = $"Event {i}" });
                    break;
                case "contacts":
                    _dataverse.Add("sprk_externalrecordaccesses", new JsonObject { ["_sprk_contact_value"] = id });
                    _dataverse.Add("contacts", new JsonObject { ["contactid"] = id, ["fullname"] = $"Contact {i:D5}" });
                    break;
            }
        }
    }

    private async Task<(IReadOnlyList<string> Ids, bool Truncated)> ReadAsync(string read)
    {
        switch (read)
        {
            case "documents":
                var docs = await _sut.GetDocumentsAsync(Root);
                return (docs.Value.Select(d => d.SprkDocumentid).ToList(), docs.Truncated);
            case "todos":
                var todos = await _sut.GetTodosAsync(ExternalDataService.TodoRootKind.Project, Root);
                return (todos.Value.Select(t => t.SprkTodoid).ToList(), todos.Truncated);
            case "events":
                var events = await _sut.GetEventsAsync(Root);
                return (events.Value.Select(e => e.SprkEventid).ToList(), events.Truncated);
            case "contacts":
                var contacts = await _sut.GetContactsAsync(Root);
                return (contacts.Value.Select(c => c.Contactid).ToList(), contacts.Truncated);
            default:
                throw new ArgumentOutOfRangeException(nameof(read));
        }
    }

    private static string IdFor(int i) => $"00000000-0000-0000-0000-{i:D12}";

    // ── Acceptance criteria ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(FourReads))]
    public async Task ARootWith250Children_ReturnsAll250_ByFollowingTheSecondPage(string read)
    {
        Seed(read, 250);

        var (ids, truncated) = await ReadAsync(read);

        ids.Should().HaveCount(250, "the second page is followed");
        ids.Should().OnlyHaveUniqueItems("each page's rows are added exactly once (the ISS-001 defect is not copied)");
        ids.Should().BeEquivalentTo(Enumerable.Range(0, 250).Select(IdFor));
        truncated.Should().BeFalse("the read ran to the last page");

        var pages = _dataverse.RequestsTo(PagedSetOf(read));
        pages.Should().HaveCount(2);
        pages[1].Query.Should().Contain("$skiptoken", "page 2 is the nextLink Dataverse returned, used unchanged");
    }

    [Theory]
    [MemberData(nameof(FourReads))]
    public async Task ARootBeyondTheCap_ReturnsTheCappedRowsFlaggedTruncated_AndLogsTheUrlAndCount(string read)
    {
        Seed(read, ExternalDataService.MaxCollectionRows + 1);

        var (ids, truncated) = await ReadAsync(read);

        ids.Should().HaveCount(ExternalDataService.MaxCollectionRows);
        ids.Should().OnlyHaveUniqueItems();
        truncated.Should().BeTrue("the cap was hit with a nextLink still present");
        _dataverse.RequestsTo(PagedSetOf(read)).Should().HaveCount(ExternalDataService.MaxCollectionPages,
            "the read stops at the cap and never reads past it");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("collection_truncated")
            && e.Message.Contains($"/api/data/v9.2/{PagedSetOf(read)}?")
            && e.Message.Contains($"Returning {ExternalDataService.MaxCollectionRows} rows"),
            "hitting the cap is logged with the url and the row count");
    }

    /// <summary>The control for the cap: exactly the cap, with no further page, is a COMPLETE list.</summary>
    [Fact]
    public async Task ARootOfExactlyTheCap_IsComplete_NotTruncated()
    {
        Seed("documents", ExternalDataService.MaxCollectionRows);

        var (ids, truncated) = await ReadAsync("documents");

        ids.Should().HaveCount(ExternalDataService.MaxCollectionRows);
        truncated.Should().BeFalse("the last page carried no nextLink; the count landing on the cap proves nothing");
    }

    [Theory]
    [MemberData(nameof(FourReads))]
    public async Task ASecondPageThatFails_ReturnsTruncated_NeverTheFirstPageAsACompleteList(string read)
    {
        Seed(read, 250);
        _dataverse.FailPage(PagedSetOf(read), page: 2);

        var (ids, truncated) = await ReadAsync(read);

        truncated.Should().BeTrue("the rows read before the failure are a prefix of the set");
        ids.Should().HaveCount(ExternalDataService.CollectionPageSize, "page 1's rows are still returned — flagged");
        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error && e.Message.Contains("collection_truncated") && e.Message.Contains("page 2 failed"));
    }

    [Theory]
    [MemberData(nameof(FourReads))]
    public async Task ARootWithLessThanOnePage_MakesExactlyOneRequest_AndIsNotTruncated(string read)
    {
        Seed(read, 37);

        var (ids, truncated) = await ReadAsync(read);

        ids.Should().HaveCount(37);
        truncated.Should().BeFalse();
        _dataverse.RequestsTo(PagedSetOf(read)).Should().HaveCount(1, "a single page needs a single request");
    }

    [Theory]
    [MemberData(nameof(FourReads))]
    public async Task EveryRequest_AsksForServerPaging_AndNeverCarriesTop(string read)
    {
        Seed(read, 250);

        await ReadAsync(read);

        _dataverse.Requests.Should().NotBeEmpty();
        _dataverse.Requests.Should().OnlyContain(r => r.Prefer == $"odata.maxpagesize={ExternalDataService.CollectionPageSize}",
            "Dataverse emits a nextLink only under server-driven paging");
        _dataverse.Requests.Should().OnlyContain(r => !r.Query.Contains("$top"),
            "$top suppresses the nextLink — the cliff ISS-002 was");
    }

    /// <summary>A first-page failure keeps its behaviour from before task 105 (out of the task's scope): empty, logged.</summary>
    [Fact]
    public async Task AFirstPageThatFails_KeepsTheEarlierBehaviour_EmptyAndNotTruncated()
    {
        Seed("documents", 250);
        _dataverse.FailPage("sprk_documents", page: 1);

        var (ids, truncated) = await ReadAsync("documents");

        ids.Should().BeEmpty();
        truncated.Should().BeFalse();
        _dataverse.RequestsTo("sprk_documents").Should().HaveCount(1);
    }

    /// <summary>A nextLink that leaves the Dataverse Web API base is not followed: the bearer token never goes there.</summary>
    [Fact]
    public async Task ANextLinkOffTheDataverseHost_IsNotFollowed_AndTheListIsFlaggedTruncated()
    {
        Seed("documents", 250);
        using var elsewhere = new FakeDataverse(); // a second loopback host that would answer, and records any request
        _dataverse.NextLinkHost = elsewhere.ServiceUrl;

        var (ids, truncated) = await ReadAsync("documents");

        elsewhere.Requests.Should().BeEmpty("the token is never sent to a host outside the Dataverse Web API base");
        truncated.Should().BeTrue("the next page was not read, so the list is cut short");
        ids.Should().HaveCount(ExternalDataService.CollectionPageSize);
        _dataverse.RequestsTo("sprk_documents").Should().HaveCount(1);
    }

    // ── The contact list's second read: the details, by id, in chunks ──────────────────────────────────────────

    [Fact]
    public async Task ContactDetails_AreReadInChunks_SoTheUrlStaysUnderTheLimit()
    {
        Seed("contacts", 250);

        var contacts = await _sut.GetContactsAsync(Root);

        contacts.Value.Should().HaveCount(250);
        contacts.Truncated.Should().BeFalse();
        var chunks = _dataverse.RequestsTo("contacts");
        chunks.Should().HaveCount(3, "250 ids in chunks of 100");
        chunks.Should().OnlyContain(r => r.Url.Length < 16_000, "one OR-filter over every id would overrun the URL limit");
        contacts.Value.Select(c => c.Fullname).Should().BeInAscendingOrder("a multi-chunk list is re-sorted by name");
    }

    [Fact]
    public async Task AContactDetailChunkThatFails_FlagsTheListTruncated()
    {
        Seed("contacts", 250);
        _dataverse.FailRequest("contacts", requestNumber: 2);

        var contacts = await _sut.GetContactsAsync(Root);

        contacts.Value.Should().HaveCount(150, "chunks 1 and 3 were read");
        contacts.Truncated.Should().BeTrue("a chunk is missing, so the list is a partial set");
    }

    [Fact]
    public async Task Organizations_DerivedFromATruncatedContactList_AreFlaggedTruncated()
    {
        Seed("contacts", 250);
        foreach (var contact in _dataverse.Rows("contacts"))
            contact["_parentcustomerid_value"] = "a0000000-0000-0000-0000-000000000001";
        _dataverse.Add("accounts", new JsonObject { ["accountid"] = "a0000000-0000-0000-0000-000000000001", ["name"] = "Acme" });
        _dataverse.FailPage("sprk_externalrecordaccesses", page: 2);

        var organizations = await _sut.GetOrganizationsAsync(Root);

        organizations.Value.Should().ContainSingle();
        organizations.Truncated.Should().BeTrue("the contacts it was derived from were cut short");
    }

    [Fact]
    public async Task TheProjectList_IsReadInChunks_AndReturnsEveryProject()
    {
        var ids = Enumerable.Range(0, 250).Select(i => Guid.Parse(IdFor(i))).ToList();
        foreach (var id in ids)
            _dataverse.Add("sprk_projects", new JsonObject
            {
                ["sprk_projectid"] = id.ToString(), ["sprk_projectname"] = $"Project {id}", ["sprk_issecure"] = false,
            });

        var projects = await _sut.GetProjectsAsync(ids);

        projects.Value.Should().HaveCount(250);
        projects.Truncated.Should().BeFalse();
        _dataverse.RequestsTo("sprk_projects").Should().HaveCount(3);
    }

    // ── Found in passing (task 105): the record-type lookup named a column that does not exist ─────────────────

    /// <summary>
    /// The external to-do create binds <c>sprk_RegardingRecordType</c> — the fourth ADR-024 resolver field. Its lookup
    /// filtered on <c>sprk_recordentitylogicalname</c>, which <c>sprk_recordtype_ref</c> does not have (live: 400), so
    /// the bind was never written. The fake answers only the live column, <c>sprk_recordlogicalname</c>.
    /// </summary>
    [Fact]
    public async Task AnExternalTodoCreate_BindsTheRecordTypeRef_FoundThroughItsLiveColumn()
    {
        var typeRef = Guid.Parse("ca68b3bb-8600-f111-8407-7c1e520aa4df");
        _dataverse.Add("sprk_recordtype_refs", new JsonObject
        {
            ["sprk_recordtype_refid"] = typeRef.ToString(), ["sprk_recorddisplayname"] = "Project",
            ["sprk_recordlogicalname"] = "sprk_project",
        });

        await _sut.CreateTodoAsync(
            ExternalDataService.TodoRootKind.Project, Root,
            new Sprk.Bff.Api.Api.ExternalAccess.Dtos.CreateExternalTodoRequest { SprkName = "Review" },
            owningTeamId: Guid.Parse("70000000-0000-0000-0000-000000000105"), callerContactId: null);

        var body = _dataverse.Posted("sprk_todos").Should().ContainSingle().Subject;
        body["sprk_RegardingRecordType@odata.bind"]!.GetValue<string>()
            .Should().Be($"/sprk_recordtype_refs({typeRef})", "all four ADR-024 resolver fields are written");
    }

    // =========================================================================================================
    // The fake Dataverse Web API
    // =========================================================================================================

    private sealed record SeenRequest(string Set, string Url, string Query, string? Prefer);

    /// <summary>
    /// A loopback Dataverse Web API that pages like the real one (see the class remarks). Rows are served in the order
    /// they were added. A filter on a set's KEY column (<c>contactid eq … or contactid eq …</c>) selects those rows;
    /// any other filter is the root scope, which every seeded row already satisfies.
    /// </summary>
    private sealed class FakeDataverse : IDisposable
    {
        private const string ApiPath = "/api/data/v9.2/";
        private static readonly Regex KeyClause = new(@"(\w+) eq ([0-9a-fA-F-]{36})", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> KeyColumns = new()
        {
            ["contacts"] = "contactid",
            ["accounts"] = "accountid",
            ["sprk_projects"] = "sprk_projectid",
        };

        private readonly WireMockServer _server = WireMockServer.Start();
        private readonly Dictionary<string, List<JsonObject>> _sets = new();
        private readonly HashSet<(string Set, int Page)> _failPages = new();
        private readonly HashSet<(string Set, int Request)> _failRequests = new();
        private readonly List<SeenRequest> _requests = new();
        private readonly object _gate = new();

        public FakeDataverse()
        {
            _server.Given(WireMock.RequestBuilders.Request.Create().UsingAnyMethod()).RespondWith(Response.Create().WithCallback(Respond));
        }

        public string ServiceUrl => _server.Urls[0];

        /// <summary>When set, every nextLink points at this host instead of the fake's own.</summary>
        public string? NextLinkHost { get; set; }

        public IReadOnlyList<SeenRequest> Requests { get { lock (_gate) return _requests.ToList(); } }

        public IReadOnlyList<SeenRequest> RequestsTo(string set) => Requests.Where(r => r.Set == set).ToList();

        public IReadOnlyList<JsonObject> Rows(string set) => _sets.TryGetValue(set, out var rows) ? rows : [];

        public void Add(string set, JsonObject row)
        {
            if (!_sets.TryGetValue(set, out var rows)) _sets[set] = rows = new List<JsonObject>();
            rows.Add(row);
        }

        public void FailPage(string set, int page) => _failPages.Add((set, page));

        public void FailRequest(string set, int requestNumber) => _failRequests.Add((set, requestNumber));

        /// <summary>The JSON bodies POSTed to <paramref name="set"/>, in order.</summary>
        public IReadOnlyList<JsonObject> Posted(string set) { lock (_gate) return _posts.Where(p => p.Set == set).Select(p => p.Body).ToList(); }

        private readonly List<(string Set, JsonObject Body)> _posts = new();
        private static readonly Regex StringClause = new(@"(\w+) eq '([^']*)'", RegexOptions.Compiled);

        private ResponseMessage Respond(IRequestMessage request)
        {
            var set = request.Path.StartsWith(ApiPath, StringComparison.Ordinal) ? request.Path[ApiPath.Length..] : request.Path;
            var rawQuery = request.RawQuery ?? string.Empty;
            var query = QueryHelpers.ParseQuery(rawQuery);
            var prefer = request.Headers is not null && request.Headers.TryGetValue("Prefer", out var p) ? p.FirstOrDefault() : null;

            if (request.Method == "POST")
            {
                var posted = JsonNode.Parse(request.Body ?? "{}")!.AsObject();
                lock (_gate) _posts.Add((set, posted));
                return Json(201, new JsonObject { ["sprk_todoid"] = Guid.NewGuid().ToString(), ["sprk_name"] = "created" });
            }

            // A single record by key, e.g. sprk_projects(<id>) — the root display-name read of a to-do create.
            if (set.EndsWith(')'))
                return Json(200, new JsonObject { ["sprk_projectname"] = "Project 105" });

            // sprk_recordtype_ref: only the LIVE column filters (metadata, spaarkedev1 2026-10-08); any other property is
            // the 400 0x80060888 Dataverse answers — never a double that agrees with the code's own column name (G-13).
            if (set == "sprk_recordtype_refs")
            {
                var clause = StringClause.Match(query.TryGetValue("$filter", out var f) ? f.ToString() : string.Empty);
                if (!clause.Success || clause.Groups[1].Value != "sprk_recordlogicalname")
                    return Json(400, new JsonObject { ["error"] = new JsonObject { ["code"] = "0x80060888" } });
                var match = Rows(set).Where(r => r["sprk_recordlogicalname"]!.GetValue<string>() == clause.Groups[2].Value);
                return Json(200, new JsonObject { ["value"] = ToArray(match) });
            }

            int requestNumber;
            lock (_gate)
            {
                _requests.Add(new SeenRequest(set, request.Url, Uri.UnescapeDataString(rawQuery), prefer));
                requestNumber = _requests.Count(r => r.Set == set);
            }

            var page = query.TryGetValue("$skiptoken", out var token) ? int.Parse(token!) : 1;
            if (_failPages.Contains((set, page)) || _failRequests.Contains((set, requestNumber)))
                return Json(503, new JsonObject { ["error"] = new JsonObject { ["message"] = "Service unavailable" } });

            IEnumerable<JsonObject> rows = Rows(set);
            if (query.TryGetValue("$filter", out var filter) && KeyColumns.TryGetValue(set, out var keyColumn))
            {
                var wanted = KeyClause.Matches(filter!)
                    .Where(m => m.Groups[1].Value == keyColumn)
                    .Select(m => m.Groups[2].Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                rows = rows.Where(r => wanted.Contains(r[keyColumn]!.GetValue<string>()));
            }
            var all = rows.ToList();

            var pageSize = prefer is not null && prefer.StartsWith("odata.maxpagesize=", StringComparison.Ordinal)
                ? int.Parse(prefer["odata.maxpagesize=".Length..])
                : 5000;

            var body = new JsonObject();
            if (query.TryGetValue("$top", out var top))
            {
                // Live behaviour: $top returns no nextLink; with maxpagesize, the smaller of the two.
                body["value"] = ToArray(all.Take(Math.Min(int.Parse(top!), pageSize)));
                return Json(200, body);
            }

            var slice = all.Skip((page - 1) * pageSize).Take(pageSize);
            body["value"] = ToArray(slice);
            if (all.Count > page * pageSize)
            {
                var withoutToken = Regex.Replace(rawQuery.TrimStart('?'), @"&?\$skiptoken=[^&]*", string.Empty);
                var host = NextLinkHost ?? ServiceUrl;
                body["@odata.nextLink"] = $"{host}{ApiPath}{set}?{withoutToken}&$skiptoken={page + 1}";
            }

            return Json(200, body);
        }

        private static JsonArray ToArray(IEnumerable<JsonObject> rows) =>
            new(rows.Select(r => (JsonNode)JsonNode.Parse(r.ToJsonString())!).ToArray());

        private static ResponseMessage Json(int status, JsonObject body) => new()
        {
            StatusCode = status,
            Headers = new Dictionary<string, WireMockList<string>> { ["Content-Type"] = new("application/json") },
            BodyData = new BodyData
            {
                BodyAsString = body.ToJsonString(),
                DetectedBodyType = BodyType.String,
                Encoding = System.Text.Encoding.UTF8,
            },
        };

        public void Dispose() => _server.Stop();
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }

    private sealed class CapturingLogger : ILogger<ExternalDataService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries { get { lock (_entries) return _entries.ToList(); } }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
