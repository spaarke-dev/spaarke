// Regression anchor for GitHub issue #3321 (split from #3306). Record search kept returning matters (and other records) deleted in
// Dataverse: the record sync job only added and updated records-index rows, so the row of a deleted record stayed for good (DEV: about
// 200–250 matter rows for 54 matters). The job now removes the rows whose Dataverse record is gone, in this tenant only, and removes
// nothing when Dataverse cannot be read or returns no records of a type the index holds.
//
// The job is real and runs through its scheduled-job entry point. The records index is the real Azure SDK client over an in-memory
// index transport (writes, deletes, the filtered listing), and Dataverse is an in-memory Web API (record reads) — hand-rolled
// handlers, never Mock<HttpMessageHandler>.

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

    [Fact]
    public async Task ExecuteAsync_AMatterDeletedInDataverse_LosesItsRowAndEveryOtherRowStays()
    {
        var result = await CreateJob().ExecuteAsync(Run(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _index.Rows.Keys.Should().BeEquivalentTo(
            [$"sprk_matter_{MatterA}", $"sprk_matter_{MatterB}", $"sprk_matter_{OtherTenantMatter}"],
            "the deleted matter's row goes; the live matters and the other tenant's row stay");
        _index.DeletedIds.Should().Equal($"sprk_matter_{DeletedMatter}");
    }

    [Fact]
    public async Task ExecuteAsync_AMatterDeletedAfterThePass_GoesWithTheNextPassOnceItIsDue()
    {
        await CreateJob().ExecuteAsync(Run(), CancellationToken.None);
        _dataverse.Remove(MatterB);

        await CreateJob().ExecuteAsync(Run(), CancellationToken.None);
        _index.Rows.Should().ContainKey($"sprk_matter_{MatterB}", "the pass runs at most once per DeletedRecordSweepHours");

        _time.Advance(TimeSpan.FromHours(6));
        await CreateJob().ExecuteAsync(Run(), CancellationToken.None);
        _index.Rows.Should().NotContainKey($"sprk_matter_{MatterB}");
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

        var result = await CreateJob().ExecuteAsync(Run(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _index.DeletedIds.Should().BeEmpty("no records at all looks like a lost read permission, not three deletions");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRemovalIsTurnedOff_RemovesNothing()
    {
        await CreateJob(deletedRecordSweepHours: 0).ExecuteAsync(Run(), CancellationToken.None);

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

    /// <summary>The Dataverse Web API, in memory: every GET of an entity set answers with the set's records (no paging).</summary>
    private sealed class InMemoryDataverse : HttpMessageHandler
    {
        private readonly Dictionary<string, List<JsonObject>> _sets = new(StringComparer.Ordinal);

        public bool Failing { get; set; }

        public void AddMatter(Guid id, string name)
        {
            if (!_sets.TryGetValue("sprk_matters", out var matters))
            {
                _sets["sprk_matters"] = matters = [];
            }

            matters.Add(new JsonObject
            {
                ["sprk_matterid"] = id.ToString(),
                ["sprk_mattername"] = name,
                ["modifiedon"] = "2026-11-01T10:00:00Z",
            });
        }

        public void Remove(Guid id) =>
            _sets["sprk_matters"].RemoveAll(record => record["sprk_matterid"]!.GetValue<string>() == id.ToString());

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Failing)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
            }

            var set = request.RequestUri!.AbsolutePath.Split('/')[^1];
            var records = _sets.TryGetValue(set, out var found) ? found : [];
            var body = new JsonObject { ["value"] = new JsonArray(records.Select(r => (JsonNode?)r.DeepClone()).ToArray()) };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// The records index's wire, in memory: applies each write and delete of an index batch, and answers the job's filtered listing
    /// (<c>recordType eq '…' and tenantId eq '…'</c>, with skip and top) from what it holds.
    /// </summary>
    private sealed partial class InMemoryRecordsIndex : HttpMessageHandler
    {
        public Dictionary<string, JsonObject> Rows { get; } = new(StringComparer.Ordinal);

        public List<string> DeletedIds { get; } = [];

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
                var filter = RecordTypeAndTenant().Match(root["filter"]?.GetValue<string>() ?? string.Empty);
                filter.Success.Should().BeTrue("the listing is always narrowed to one record type in one tenant");
                var skip = root["skip"]?.GetValue<int>() ?? 0;
                var top = root["top"]?.GetValue<int>() ?? 50;

                var hits = Rows.Values
                    .Where(row => row["recordType"]?.GetValue<string>() == filter.Groups[1].Value
                        && row["tenantId"]?.GetValue<string>() == filter.Groups[2].Value)
                    .OrderBy(row => row["id"]!.GetValue<string>(), StringComparer.Ordinal)
                    .Skip(skip)
                    .Take(top)
                    .Select(row => (JsonNode?)new JsonObject
                    {
                        ["@search.score"] = 1.0,
                        ["id"] = row["id"]!.GetValue<string>(),
                        ["dataverseRecordId"] = row["dataverseRecordId"]?.GetValue<string>(),
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

        [GeneratedRegex(@"recordType eq '([^']*)' and tenantId eq '([^']*)'")]
        private static partial Regex RecordTypeAndTenant();
    }
}
