// Regression anchor for GitHub issue #3321 (split from #3306). Record search kept returning matters (and other records) deleted in
// Dataverse: the record sync job only added and updated records-index rows, so the row of a deleted record stayed for good (DEV: about
// 200–250 matter rows for 54 matters). The job now removes the rows whose Dataverse record is gone, in this tenant only. It removes a
// row only after reading its record back and getting "not found", and removes nothing when Dataverse cannot be read, returns no
// records of a type the index holds, returns an incomplete list, or refuses a read-back.
//
// The job is real and runs through its scheduled-job entry point. The records index is the real Azure SDK client over an in-memory
// index transport (writes, deletes, the ordered and filtered listing), and Dataverse is an in-memory Web API (paged listings and
// single-record reads) — hand-rolled handlers, never Mock<HttpMessageHandler>.

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed partial class Issue3321_RecordSyncRemovesDeletedRecordsTests
{
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private const string OtherTenant = "0b5d7c1e-3321-4000-8000-00000000000f";

    private static readonly Guid MatterA = Guid.Parse("33210000-0000-4000-8000-00000000000a");
    private static readonly Guid MatterB = Guid.Parse("33210000-0000-4000-8000-00000000000b");
    private static readonly Guid DeletedMatter = Guid.Parse("33210000-0000-4000-8000-00000000000c");
    private static readonly Guid OtherTenantMatter = Guid.Parse("33210000-0000-4000-8000-00000000000d");

    private readonly InMemoryRecordsIndex _index = new();
    private readonly InMemoryDataverse _dataverse = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    public Issue3321_RecordSyncRemovesDeletedRecordsTests()
    {
        _dataverse.AddMatter(MatterA, "Alpha");
        _dataverse.AddMatter(MatterB, "Bravo");

        // The index as DEV has it: rows for both live matters, a row for a matter since deleted in Dataverse, and a row of another
        // tenant that this tenant's Dataverse knows nothing of.
        _index.Add($"sprk_matter_{MatterA}", "sprk_matter", Tenant, MatterA.ToString());
        _index.Add($"sprk_matter_{MatterB}", "sprk_matter", Tenant, MatterB.ToString());
        _index.Add($"sprk_matter_{DeletedMatter}", "sprk_matter", Tenant, DeletedMatter.ToString());
        _index.Add($"sprk_matter_{OtherTenantMatter}", "sprk_matter", OtherTenant, OtherTenantMatter.ToString());
    }

    private RecordSyncJob CreateJob(int deletedRecordSweepHours = 6) => new IndexedRecordSyncJob(
        _index,
        _cache,
        new DataverseClients(_dataverse),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = Tenant }).Build(),
        Options.Create(new RecordSyncOptions
        {
            Enabled = true,
            AiSearchEndpoint = "https://test-search.search.windows.net",
            DataverseEnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
            DeletedRecordSweepHours = deletedRecordSweepHours,
        }),
        _time);

    private static JobRunContext Run() => new(Guid.NewGuid(), "corr-3321", JobRunTrigger.Scheduled, new Dictionary<string, object>());

    private async Task<JobRunResult> RunJobAsync(int deletedRecordSweepHours = 6)
    {
        var result = await CreateJob(deletedRecordSweepHours).ExecuteAsync(Run(), CancellationToken.None);
        _index.UnmatchedQueries.Should().BeEmpty("every listing the job sends is one record type in one tenant, in record-id order");
        return result;
    }

    // ── removal ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AMatterDeletedInDataverse_LosesItsRowAndEveryOtherRowStays()
    {
        var result = await RunJobAsync();

        result.Success.Should().BeTrue();
        _index.Rows.Keys.Should().BeEquivalentTo(
            [$"sprk_matter_{MatterA}", $"sprk_matter_{MatterB}", $"sprk_matter_{OtherTenantMatter}"],
            "the deleted matter's row goes; the live matters and the other tenant's row stay");
        _index.DeletedIds.Should().Equal($"sprk_matter_{DeletedMatter}");
    }

    [Fact]
    public async Task ExecuteAsync_AMatterDeletedAfterThePass_GoesWithTheNextPassOnceItIsDue()
    {
        await RunJobAsync();
        _dataverse.Remove(MatterB);

        await RunJobAsync();
        _index.Rows.Should().ContainKey($"sprk_matter_{MatterB}", "the pass runs at most once per DeletedRecordSweepHours");

        _time.Advance(TimeSpan.FromHours(6));
        await RunJobAsync();
        _index.Rows.Should().NotContainKey($"sprk_matter_{MatterB}");
    }

    [Fact]
    public async Task ExecuteAsync_MoreRowsThanOnePageOnBothSides_RemovesOnlyTheDeletedOnes()
    {
        // 2,400 live matters: the index listing takes three pages of 1,000 and the Dataverse listing three pages of 1,000.
        _dataverse.PageSize = 1000;
        var deleted = new List<string>();
        for (var i = 0; i < 2400; i++)
        {
            var id = Guid.Parse($"33210000-0000-4000-9000-{i:D12}");
            _dataverse.AddMatter(id, $"Matter {i}");
            _index.Add($"sprk_matter_{id}", "sprk_matter", Tenant, id.ToString());
        }

        // Deleted matters spread over the listing, including one past the 1,000th and one past the 2,000th row.
        foreach (var i in new[] { 5, 1500, 2399 })
        {
            var id = Guid.Parse($"33210000-0000-4000-9000-{i:D12}");
            _dataverse.Remove(id);
            deleted.Add($"sprk_matter_{id}");
        }

        await RunJobAsync();

        _index.DeletedIds.Should().BeEquivalentTo([.. deleted, $"sprk_matter_{DeletedMatter}"]);
        _index.Rows.Should().HaveCount(2 + 2400 - 3 + 1, "both live seed matters, the live paged matters and the other tenant's row stay");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheIndexRejectsADelete_TheRowStaysAndTheNextRunRemovesIt()
    {
        _index.RejectDeleteOnce.Add($"sprk_matter_{DeletedMatter}");

        await RunJobAsync();
        _index.Rows.Should().ContainKey($"sprk_matter_{DeletedMatter}", "the index refused the delete");

        // No time passes: the pass was not marked done, so the next run repeats it.
        await RunJobAsync();
        _index.Rows.Should().NotContainKey($"sprk_matter_{DeletedMatter}");
    }

    // ── what is never removed ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ALiveRecordWhoseRowCarriesAnUppercaseId_KeepsItsRow()
    {
        var upper = $"sprk_matter_{MatterA.ToString().ToUpperInvariant()}";
        _index.Add(upper, "sprk_matter", Tenant, MatterA.ToString().ToUpperInvariant());

        await RunJobAsync();

        _index.Rows.Should().ContainKey(upper, "the record exists; the id differs only in letter case");
        _index.DeletedIds.Should().Equal($"sprk_matter_{DeletedMatter}");
    }

    [Fact]
    public async Task ExecuteAsync_ARowWhoseRecordIdIsNotAGuid_IsNeverRemoved()
    {
        _index.Add("sprk_matter_legacy-7", "sprk_matter", Tenant, "legacy-7");

        await RunJobAsync();

        _index.Rows.Should().ContainKey("sprk_matter_legacy-7");
        _index.DeletedIds.Should().Equal($"sprk_matter_{DeletedMatter}");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheDataverseListLeavesOutARecordThatExists_RemovesNothing()
    {
        // A matter the listing does not return (the identity lost read access to part of the table) but a direct read finds.
        var unlisted = Guid.Parse("33210000-0000-4000-8000-0000000000ff");
        _dataverse.AddUnlistedMatter(unlisted);
        _index.Add($"sprk_matter_{unlisted}", "sprk_matter", Tenant, unlisted.ToString());

        var result = await RunJobAsync();

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().Contain("1 failed the deleted-record pass", "an incomplete list is reported, not only logged");
        _index.DeletedIds.Should().BeEmpty("an incomplete list is no proof of deletion, so not even the deleted matter goes");
        _index.Rows.Should().ContainKeys($"sprk_matter_{unlisted}", $"sprk_matter_{DeletedMatter}");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAReadBackIsRefused_RemovesNothing()
    {
        _dataverse.RefuseSingleRecordReads = true;

        var result = await RunJobAsync();

        result.Success.Should().BeTrue("the sync itself succeeded; the refused pass is counted and the next run repeats it");
        result.ErrorMessage.Should().Contain("1 failed the deleted-record pass");
        _index.DeletedIds.Should().BeEmpty("a refused read is not \"not found\"");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDataverseCannotBeRead_RemovesNothing()
    {
        _dataverse.Failing = true;

        var act = () => CreateJob().ExecuteAsync(Run(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("no record type could be synced, so the host retries the run");
        _index.DeletedIds.Should().BeEmpty("a failed read is never taken for an empty table");
        _index.Rows.Should().ContainKey($"sprk_matter_{DeletedMatter}");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDataverseReturnsNoMattersWhileTheIndexHoldsSome_RemovesNothing()
    {
        _dataverse.Remove(MatterA);
        _dataverse.Remove(MatterB);

        var result = await RunJobAsync();

        result.Success.Should().BeTrue();
        _index.DeletedIds.Should().BeEmpty("no records at all looks like a lost read permission, not three deletions");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRemovalIsTurnedOff_RemovesNothing()
    {
        await RunJobAsync(deletedRecordSweepHours: 0);

        _index.DeletedIds.Should().BeEmpty();
    }

    // ── the job, pointed at the in-memory index ─────────────────────────────────────────────────────────────────────

    private sealed class IndexedRecordSyncJob(
        InMemoryRecordsIndex index,
        IDistributedCache cache,
        IHttpClientFactory clients,
        IConfiguration configuration,
        IOptions<RecordSyncOptions> options,
        TimeProvider time)
        : RecordSyncJob(cache, clients, configuration, options, new StaticTokenCredential(), time, NullLogger<RecordSyncJob>.Instance)
    {
        protected override SearchClient BuildSearchClient() => new(
            new Uri("https://test-search.search.windows.net"),
            "spaarke-records-index",
            new AzureKeyCredential(Guid.NewGuid().ToString("N")),
            new SearchClientOptions { Transport = new HttpClientTransport(new HttpClient(index)), Retry = { MaxRetries = 0 } });

        public override TimeSpan GetRetryDelay(int attempt) => TimeSpan.Zero;
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("dataverse-token-3321", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(Token);
    }

    private sealed class DataverseClients(InMemoryDataverse dataverse) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(dataverse, disposeHandler: false);
    }

    /// <summary>
    /// The Dataverse Web API, in memory. A GET of an entity set answers with the set's listed records, in pages of
    /// <see cref="PageSize"/> linked by <c>@odata.nextLink</c>; a GET of <c>set(id)</c> answers 200 for a listed or unlisted record
    /// and 404 otherwise.
    /// </summary>
    private sealed partial class InMemoryDataverse : HttpMessageHandler
    {
        private readonly Dictionary<string, List<JsonObject>> _sets = new(StringComparer.Ordinal);
        private readonly HashSet<Guid> _unlisted = [];

        public bool Failing { get; set; }

        public bool RefuseSingleRecordReads { get; set; }

        public int PageSize { get; set; } = 5000;

        public void AddMatter(Guid id, string name) => Matters().Add(new JsonObject
        {
            ["sprk_matterid"] = id.ToString(),
            ["sprk_mattername"] = name,
            ["modifiedon"] = "2026-11-01T10:00:00Z",
        });

        public void AddUnlistedMatter(Guid id) => _unlisted.Add(id);

        public void Remove(Guid id) =>
            Matters().RemoveAll(record => record["sprk_matterid"]!.GetValue<string>() == id.ToString());

        private List<JsonObject> Matters()
        {
            if (!_sets.TryGetValue("sprk_matters", out var matters))
            {
                _sets["sprk_matters"] = matters = [];
            }

            return matters;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Failing)
            {
                return Task.FromResult(Status(HttpStatusCode.ServiceUnavailable));
            }

            var uri = request.RequestUri!;
            var segment = Uri.UnescapeDataString(uri.AbsolutePath.Split('/')[^1]);

            var single = SingleRecord().Match(segment);
            if (single.Success)
            {
                if (RefuseSingleRecordReads)
                {
                    return Task.FromResult(Status(HttpStatusCode.Forbidden));
                }

                var id = Guid.Parse(single.Groups[2].Value);
                var exists = _unlisted.Contains(id)
                    || (_sets.TryGetValue(single.Groups[1].Value, out var set)
                        && set.Any(record => Guid.Parse(record.First(p => p.Key.EndsWith("id", StringComparison.Ordinal)).Value!.GetValue<string>()) == id));
                return Task.FromResult(exists ? Json(new JsonObject { ["id"] = id.ToString() }) : Status(HttpStatusCode.NotFound));
            }

            var records = _sets.TryGetValue(segment, out var found) ? found : [];
            var from = Regex.Match(uri.Query, @"[?&]page=(\d+)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
            var page = records.Skip(from).Take(PageSize).Select(r => (JsonNode?)r.DeepClone()).ToArray();
            var body = new JsonObject { ["value"] = new JsonArray(page) };
            if (from + PageSize < records.Count)
            {
                body["@odata.nextLink"] = $"{uri.GetLeftPart(UriPartial.Path)}?page={from + PageSize}";
            }

            return Task.FromResult(Json(body));
        }

        private static HttpResponseMessage Json(JsonObject body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("{}") };

        [GeneratedRegex(@"^(\w+)\(([0-9a-fA-F-]{36})\)$")]
        private static partial Regex SingleRecord();
    }

    /// <summary>
    /// The records index's wire, in memory: applies each write and delete of an index batch, and answers the job's listing — one
    /// record type in one tenant, rows with a record id, ordered by record id, from a given id onward — from what it holds. A query of
    /// any other shape is recorded in <see cref="UnmatchedQueries"/> and answered with nothing.
    /// </summary>
    private sealed partial class InMemoryRecordsIndex : HttpMessageHandler
    {
        public Dictionary<string, JsonObject> Rows { get; } = new(StringComparer.Ordinal);

        public List<string> DeletedIds { get; } = [];

        public HashSet<string> RejectDeleteOnce { get; } = new(StringComparer.Ordinal);

        public List<string> UnmatchedQueries { get; } = [];

        public void Add(string id, string recordType, string tenantId, string dataverseRecordId) => Rows[id] = new JsonObject
        {
            ["id"] = id,
            ["recordType"] = recordType,
            ["tenantId"] = tenantId,
            ["dataverseRecordId"] = dataverseRecordId,
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            var root = JsonNode.Parse(body)!.AsObject();

            if (request.RequestUri!.AbsolutePath.Contains("search.post.search", StringComparison.Ordinal))
            {
                var filterText = root["filter"]?.GetValue<string>() ?? string.Empty;
                var orderBy = root["orderby"]?.GetValue<string>();
                var filter = Listing().Match(filterText);
                if (!filter.Success || orderBy != "dataverseRecordId asc" || root["skip"] is not null)
                {
                    UnmatchedQueries.Add($"filter={filterText}; orderby={orderBy}; skip={root["skip"]}");
                    return Json(new JsonObject { ["value"] = new JsonArray() });
                }

                var from = filter.Groups[3].Success ? filter.Groups[3].Value : null;
                var top = root["top"]?.GetValue<int>() ?? 50;
                var hits = Rows.Values
                    .Where(row => row["recordType"]?.GetValue<string>() == filter.Groups[1].Value
                        && row["tenantId"]?.GetValue<string>() == filter.Groups[2].Value
                        && row["dataverseRecordId"] is not null)
                    .Where(row => from is null || string.CompareOrdinal(row["dataverseRecordId"]!.GetValue<string>(), from) >= 0)
                    .OrderBy(row => row["dataverseRecordId"]!.GetValue<string>(), StringComparer.Ordinal)
                    .ThenBy(row => row["id"]!.GetValue<string>(), StringComparer.Ordinal)
                    .Take(top)
                    .Select(row => (JsonNode?)new JsonObject
                    {
                        ["@search.score"] = 1.0,
                        ["id"] = row["id"]!.GetValue<string>(),
                        ["dataverseRecordId"] = row["dataverseRecordId"]!.GetValue<string>(),
                    })
                    .ToArray();

                return Json(new JsonObject { ["value"] = new JsonArray(hits) });
            }

            var keys = new JsonArray();
            foreach (var action in root["value"]!.AsArray())
            {
                var document = action!.AsObject();
                var id = document["id"]!.GetValue<string>();
                if (document["@search.action"]?.GetValue<string>() == "delete")
                {
                    if (RejectDeleteOnce.Remove(id))
                    {
                        keys.Add(new JsonObject { ["key"] = id, ["status"] = false, ["statusCode"] = 503, ["errorMessage"] = "busy" });
                        continue;
                    }

                    Rows.Remove(id);
                    DeletedIds.Add(id);
                }
                else
                {
                    var merged = Rows.TryGetValue(id, out var existing) ? existing : new JsonObject();
                    foreach (var (name, value) in document)
                    {
                        if (name != "@search.action")
                        {
                            merged[name] = value?.DeepClone();
                        }
                    }

                    Rows[id] = merged;
                }

                keys.Add(new JsonObject { ["key"] = id, ["status"] = true, ["statusCode"] = 200 });
            }

            return Json(new JsonObject { ["value"] = keys });
        }

        private static HttpResponseMessage Json(JsonObject body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

        [GeneratedRegex(@"^recordType eq '([^']*)' and tenantId eq '([^']*)' and dataverseRecordId ne null(?: and dataverseRecordId ge '([^']*)')?$")]
        private static partial Regex Listing();
    }
}
