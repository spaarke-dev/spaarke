using System.ServiceModel;
using System.Text.RegularExpressions;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Issue #3165 (Matter Type search filter): <see cref="MatterTypeIndexSyncJob"/> carries a changed matter or project type onto the
/// chunks of its documents and onto its records-index document, and resolves documents indexed before the fields existed.
/// </summary>
/// <remarks>
/// The doubles are the seams: Dataverse (<see cref="IGenericEntityService"/>), the two Azure AI Search clients, and the clock. The files
/// index is a small in-memory fake that answers the job's filters over a list of chunks and applies the merges it receives, so the
/// assertions are on the index's end state (which chunks hold which type), and on every query's tenant filter. The job,
/// <see cref="DocumentMatterTypeResolver"/>, the watermark store (an in-memory <see cref="IDistributedCache"/>) are real.
/// </remarks>
public sealed class MatterTypeIndexSyncJobTests
{
    private const string Tenant = "tenant-3165";
    private const string OtherTenant = "tenant-other";

    private static readonly Guid MatterM = Guid.Parse("3165e000-0000-4000-8000-00000000000a");
    private static readonly Guid MatterN = Guid.Parse("3165e000-0000-4000-8000-00000000000b");
    private static readonly Guid ProjectP = Guid.Parse("3165e000-0000-4000-8000-00000000000c");
    private static readonly Guid OldType = Guid.Parse("3165e000-0000-4000-8000-0000000000a1");
    private static readonly Guid NewType = Guid.Parse("3165e000-0000-4000-8000-0000000000b2");

    private readonly List<KnowledgeDocument> _chunks = [];
    private readonly List<SearchOptions> _fileQueries = [];
    private readonly List<SearchDocument> _recordMerges = [];
    private readonly Mock<SearchClient> _files = new();
    private readonly Mock<SearchClient> _records = new();
    private readonly Mock<IGenericEntityService> _dataverse = new();
    private readonly Dictionary<(string Table, Guid Id), Entity> _rows = new();
    private readonly Dictionary<string, List<Entity>> _changed = new() { ["sprk_matter"] = [], ["sprk_project"] = [] };
    private readonly List<QueryExpression> _changeQueries = [];
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-25T10:00:00Z"));
    private readonly MatterTypeIndexSyncOptions _options = new();
    private readonly Dictionary<string, KnowledgeDocument> _byId = new(StringComparer.Ordinal);
    private Func<string, Guid, bool> _failRead = (_, _) => false;
    private Func<string, Guid, bool> _rowGone = (_, _) => false;
    private int _recordMergeStatus = 200;
    private bool _failChangeQuery;
    private string? _tenantConfigured = Tenant;
    private bool _aiAvailable = true;

    public MatterTypeIndexSyncJobTests()
    {
        _files
            .Setup(c => c.SearchAsync<KnowledgeDocument>(It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, SearchOptions options, CancellationToken _) =>
            {
                _fileQueries.Add(options);
                var matches = Predicate(options.Filter);
                var matching = _chunks.Where(matches).ToList();
                var page = matching.Skip(options.Skip ?? 0).Take(options.Size ?? 50)
                    .Select(c => SearchModelFactory.SearchResult(c, 1.0, null)).ToList();
                return Response.FromValue(SearchModelFactory.SearchResults(page, matching.Count, null, null, null!), null!);
            });

        _files
            .Setup(c => c.MergeDocumentsAsync(It.IsAny<IEnumerable<SearchDocument>>(), It.IsAny<IndexDocumentsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<SearchDocument> docs, IndexDocumentsOptions _, CancellationToken _) =>
            {
                var results = new List<Azure.Search.Documents.Models.IndexingResult>();
                foreach (var doc in docs)
                {
                    var chunk = _byId[(string)doc["id"]];
                    if (doc.ContainsKey("matterTypeId")) chunk.MatterTypeId = (string?)doc["matterTypeId"];
                    if (doc.ContainsKey("matterTypeSourceId")) chunk.MatterTypeSourceId = (string?)doc["matterTypeSourceId"];
                    results.Add(SearchModelFactory.IndexingResult(chunk.Id, null, true, 200));
                }

                return Response.FromValue(SearchModelFactory.IndexDocumentsResult(results), null!);
            });

        _records
            .Setup(c => c.MergeDocumentsAsync(It.IsAny<IEnumerable<SearchDocument>>(), It.IsAny<IndexDocumentsOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<SearchDocument> docs, IndexDocumentsOptions _, CancellationToken _) =>
            {
                var batch = docs.ToList();
                _recordMerges.AddRange(batch);
                return Response.FromValue(
                    SearchModelFactory.IndexDocumentsResult(
                        batch.Select(d => SearchModelFactory.IndexingResult((string)d["id"], null, _recordMergeStatus < 300, _recordMergeStatus)).ToList()),
                    null!);
            });

        _dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                if (_failChangeQuery)
                {
                    throw new TimeoutException("Dataverse did not answer");
                }

                _changeQueries.Add(query);
                return new EntityCollection(_changed[query.EntityName]);
            });

        _dataverse
            .Setup(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
            {
                if (_failRead(table, id))
                {
                    throw new TimeoutException("Dataverse did not answer");
                }

                if (_rowGone(table, id))
                {
                    // What the live service does for a deleted row: the SDK throws 0x80040217; it never answers null.
                    throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 }, new FaultReason("does not exist"));
                }

                return _rows.TryGetValue((table, id), out var row) ? row : null!;
            });
    }

    // ── the fake files index ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Answers the filters the job issues; a filter shape it does not know fails the test rather than matching everything.</summary>
    private static Func<KnowledgeDocument, bool> Predicate(string? filter)
    {
        filter.Should().NotBeNull();
        var f = filter!;
        var match = Regex.Match(f, "tenantId eq '([^']*)'");
        match.Success.Should().BeTrue("every files-index query carries the tenant filter");
        var tenant = match.Groups[1].Value;

        var checks = new List<Func<KnowledgeDocument, bool>> { c => c.TenantId == tenant };
        if (f.Contains("matterTypeSourceId eq null"))
        {
            checks.Add(c => c.MatterTypeSourceId is null);
        }

        if (Regex.Match(f, "matterTypeSourceId eq '([^']*)'") is { Success: true } source)
        {
            var sourceId = source.Groups[1].Value;
            checks.Add(c => c.MatterTypeSourceId == sourceId);
        }

        if (Regex.Match(f, @"\(matterTypeId ne '([^']*)' or matterTypeId eq null\)") is { Success: true } differs)
        {
            var typeId = differs.Groups[1].Value;
            checks.Add(c => c.MatterTypeId != typeId);
        }

        if (f.Contains("(matterTypeId ne null and matterTypeId ne '')"))
        {
            checks.Add(c => !string.IsNullOrEmpty(c.MatterTypeId));
        }
        else if (f.Contains("matterTypeId ne null"))
        {
            checks.Add(c => c.MatterTypeId is not null);
        }

        if (Regex.Match(f, "documentId eq '([^']*)'") is { Success: true } document)
        {
            var documentId = document.Groups[1].Value;
            checks.Add(c => c.DocumentId == documentId);
        }

        if (Regex.Match(f, "speFileId eq '([^']*)'") is { Success: true } file)
        {
            var speFileId = file.Groups[1].Value;
            checks.Add(c => c.SpeFileId == speFileId);
        }

        checks.Count.Should().BeGreaterThan(1, $"the fake index does not know the filter '{f}'");
        return c => checks.All(check => check(c));
    }
    // ── scenario helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static string Id(Guid id) => id.ToString("D");

    private KnowledgeDocument Chunk(string id, string tenant = Tenant, string? documentId = null, string? sourceId = null,
        Guid? typeId = null, string? parentType = null, Guid? parentId = null)
    {
        var chunk = new KnowledgeDocument
        {
            Id = id,
            TenantId = tenant,
            DocumentId = documentId,
            SpeFileId = "spe-" + id,
            MatterTypeSourceId = sourceId,
            MatterTypeId = typeId is null ? null : Id(typeId.Value),
            ParentEntityType = parentType,
            ParentEntityId = parentId is null ? null : Id(parentId.Value),
        };
        _chunks.Add(chunk);
        _byId[id] = chunk;
        return chunk;
    }

    private void Source(string table, Guid id, Guid? type, DateTime? modified = null)
    {
        var row = new Entity(table, id);
        if (type is not null)
        {
            row["sprk_mattertype"] = new EntityReference("sprk_mattertype", type.Value);
        }

        _rows[(table, id)] = row;
        if (modified is not null)
        {
            var changed = new Entity(table, id) { ["modifiedon"] = modified.Value };
            if (type is not null)
            {
                changed["sprk_mattertype"] = new EntityReference("sprk_mattertype", type.Value);
            }

            _changed[table].Add(changed);
        }
    }

    private MatterTypeIndexSyncJob CreateJob(int? maxMergeRounds = null)
    {
        var deployments = new Mock<IKnowledgeDeploymentService>();
        deployments.Setup(d => d.GetSearchClientAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(_files.Object);

        var indexClient = new Mock<SearchIndexClient>();
        indexClient.Setup(c => c.GetSearchClient("spaarke-records-index")).Returns(_records.Object);

        var services = new ServiceCollection();
        services.AddSingleton(_dataverse.Object);
        services.AddSingleton(indexClient.Object);
        services.AddSingleton(new DocumentMatterTypeResolver(_dataverse.Object, TestDocumentIndexParentResolver.Over(_dataverse.Object), NullLogger<DocumentMatterTypeResolver>.Instance));
        if (_aiAvailable)
        {
            services.AddSingleton(deployments.Object);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = _tenantConfigured })
            .Build();

        return new MatterTypeIndexSyncJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _cache,
            configuration,
            Mock.Of<IOptionsMonitor<MatterTypeIndexSyncOptions>>(o => o.CurrentValue == _options),
            _time,
            NullLogger<MatterTypeIndexSyncJob>.Instance)
        {
            MaxMergeRounds = maxMergeRounds ?? MatterTypeIndexSyncJob.DefaultMaxMergeRounds,
        };
    }

    private static JobRunContext Context() =>
        new(Guid.NewGuid(), "corr-3165", JobRunTrigger.Scheduled, new Dictionary<string, object>());

    private KnowledgeDocument Stored(string id) => _chunks.Single(c => c.Id == id);

    // ── (a) changes ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MatterTypeChanged_MergesTheNewTypeOntoItsChunksOnlyAndOntoTheRecordsDocument()
    {
        Source("sprk_matter", MatterM, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        Chunk("m1", documentId: "d1", sourceId: Id(MatterM), typeId: OldType);
        Chunk("m2", documentId: "d1", sourceId: Id(MatterM), typeId: OldType);
        Chunk("n1", documentId: "d2", sourceId: Id(MatterN), typeId: OldType);
        Chunk("x1", tenant: OtherTenant, documentId: "d3", sourceId: Id(MatterM), typeId: OldType);

        var result = await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        result.Success.Should().BeTrue();
        Stored("m1").MatterTypeId.Should().Be(Id(NewType));
        Stored("m2").MatterTypeId.Should().Be(Id(NewType));
        Stored("n1").MatterTypeId.Should().Be(Id(OldType), "a chunk filed under another matter keeps its type");
        Stored("x1").MatterTypeId.Should().Be(Id(OldType), "another tenant's chunk is never read or written");
        _recordMerges.Should().ContainSingle();
        _recordMerges[0]["id"].Should().Be($"sprk_matter_{Id(MatterM)}");
        _recordMerges[0]["matterTypeId"].Should().Be(Id(NewType));
        _recordMerges[0].Keys.Should().BeEquivalentTo("id", "matterTypeId");
    }

    [Fact]
    public async Task ExecuteAsync_ProjectTypeChanged_MergesTheNewTypeOntoItsChunks()
    {
        Source("sprk_project", ProjectP, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        Chunk("p1", documentId: "d1", sourceId: Id(ProjectP), typeId: OldType);

        await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        Stored("p1").MatterTypeId.Should().Be(Id(NewType));
        _recordMerges.Should().ContainSingle().Which["id"].Should().Be($"sprk_project_{Id(ProjectP)}");
    }

    [Fact]
    public async Task ExecuteAsync_MatterTypeRemoved_ClearsTheTypeOnItsChunks()
    {
        Source("sprk_matter", MatterM, type: null, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        Chunk("m1", documentId: "d1", sourceId: Id(MatterM), typeId: OldType);

        await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        Stored("m1").MatterTypeId.Should().BeNull();
        Stored("m1").MatterTypeSourceId.Should().Be(Id(MatterM), "the source stays so a type assigned later still finds the chunks");
    }

    [Fact]
    public async Task ExecuteAsync_ChunksAlreadyCurrent_WritesNothing()
    {
        Source("sprk_matter", MatterM, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        Chunk("m1", documentId: "d1", sourceId: Id(MatterM), typeId: NewType);

        await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        _files.Verify(
            c => c.MergeDocumentsAsync(It.IsAny<IEnumerable<SearchDocument>>(), It.IsAny<IndexDocumentsOptions>(), It.IsAny<CancellationToken>()),
            Times.Never, "a re-run over current data is a no-op (ADR-036 A1-3)");
    }

    [Fact]
    public async Task ExecuteAsync_SecondRun_ReadsOnlyRecordsModifiedSinceTheFirst()
    {
        Source("sprk_matter", MatterM, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        var job = CreateJob();

        await job.ExecuteAsync(Context(), CancellationToken.None);
        _changeQueries.Clear();
        await job.ExecuteAsync(Context(), CancellationToken.None);

        var matterQuery = _changeQueries.Single(q => q.EntityName == "sprk_matter");
        matterQuery.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "modifiedon" && c.Operator == ConditionOperator.GreaterEqual);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnUpdateFaults_TheWatermarkStaysSoTheNextRunRetriesIt()
    {
        Source("sprk_matter", MatterM, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        Chunk("m1", documentId: "d1", sourceId: Id(MatterM), typeId: OldType);
        _files
            .Setup(c => c.MergeDocumentsAsync(It.IsAny<IEnumerable<SearchDocument>>(), It.IsAny<IndexDocumentsOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(503, "unavailable"));
        var job = CreateJob();

        var result = await job.ExecuteAsync(Context(), CancellationToken.None);
        _changeQueries.Clear();
        await job.ExecuteAsync(Context(), CancellationToken.None);

        result.Success.Should().BeTrue("a unit fault is counted and the run completes (A1-4)");
        result.ErrorMessage.Should().Contain("faulted");
        _changeQueries.Single(q => q.EntityName == "sprk_matter").Criteria.Conditions
            .Should().NotContain(c => c.AttributeName == "modifiedon", "the faulted record must be read again");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheChangeQueryFaults_Throws_SoTheHostRetries()
    {
        _failChangeQuery = true;

        var act = () => CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    // ── (b) backfill ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Backfill_ResolvesEachDocumentOnceAndMergesBothFieldsOntoAllItsChunks()
    {
        Source("sprk_project", ProjectP, NewType);
        Chunk("a1", documentId: "doc-a", parentType: "project", parentId: ProjectP);
        Chunk("a2", documentId: "doc-a", parentType: "project", parentId: ProjectP);
        Chunk("a3", documentId: "doc-a", parentType: "project", parentId: ProjectP);
        var docB = Guid.Parse("3165e000-0000-4000-8000-0000000000d2");
        _rows[("sprk_document", docB)] = new Entity("sprk_document", docB);
        Chunk("b1", documentId: Id(docB));
        Chunk("b2", documentId: Id(docB));
        Chunk("o1", tenant: OtherTenant, documentId: "doc-other");

        var result = await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        result.Success.Should().BeTrue();
        new[] { "a1", "a2", "a3" }.Select(Stored).Should().OnlyContain(c => c.MatterTypeId == Id(NewType) && c.MatterTypeSourceId == Id(ProjectP));
        new[] { "b1", "b2" }.Select(Stored).Should().OnlyContain(c => c.MatterTypeId == null && c.MatterTypeSourceId == string.Empty,
            "a document that belongs to no matter or project is marked resolved so it leaves the backfill");
        Stored("o1").MatterTypeSourceId.Should().BeNull("another tenant's chunk is never touched");
        _files.Invocations.Count(i => i.Method.Name == "MergeDocumentsAsync").Should().Be(2, "one merge per document, not per chunk");
    }

    [Fact]
    public async Task ExecuteAsync_Backfill_LeavesADocumentWhoseReadFailedForTheNextRun()
    {
        Source("sprk_project", ProjectP, NewType);
        Chunk("a1", documentId: "doc-a", parentType: "project", parentId: ProjectP);
        _failRead = (table, _) => table == "sprk_project";
        var job = CreateJob();

        var first = await job.ExecuteAsync(Context(), CancellationToken.None);

        first.Success.Should().BeTrue();
        Stored("a1").MatterTypeSourceId.Should().BeNull("an unresolved document is not stamped as 'no source'");

        _failRead = (_, _) => false;
        await job.ExecuteAsync(Context(), CancellationToken.None);

        Stored("a1").MatterTypeId.Should().Be(Id(NewType));
        Stored("a1").MatterTypeSourceId.Should().Be(Id(ProjectP));
    }

    [Fact]
    public async Task ExecuteAsync_Backfill_ResolvesAtMostTheConfiguredNumberOfDocumentsPerRun()
    {
        Source("sprk_project", ProjectP, NewType);
        _options.BackfillDocumentsPerRun = 1;
        Chunk("a1", documentId: "doc-a", parentType: "project", parentId: ProjectP);
        Chunk("b1", documentId: "doc-b", parentType: "project", parentId: ProjectP);
        var job = CreateJob();

        await job.ExecuteAsync(Context(), CancellationToken.None);
        _chunks.Count(c => c.MatterTypeSourceId is not null).Should().Be(1);

        await job.ExecuteAsync(Context(), CancellationToken.None);
        _chunks.Should().OnlyContain(c => c.MatterTypeSourceId == Id(ProjectP), "the next run drains the rest");
    }

    // ── completeness, record merges ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MoreChunksThanTheOldTwentyRoundCap_UpdatesEveryOneAndAdvancesTheWatermark()
    {
        Source("sprk_matter", MatterM, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        for (var i = 0; i < 20_500; i++)
        {
            Chunk($"m{i}", documentId: "d1", sourceId: Id(MatterM), typeId: OldType);
        }

        var job = CreateJob();
        var result = await job.ExecuteAsync(Context(), CancellationToken.None);
        _changeQueries.Clear();
        await job.ExecuteAsync(Context(), CancellationToken.None);

        _chunks.Should().OnlyContain(c => c.MatterTypeId == Id(NewType), "a matter with more chunks than one run's old bound must not keep the old type");
        result.ErrorMessage.Should().BeNull();
        _changeQueries.Single(q => q.EntityName == "sprk_matter").Criteria.Conditions
            .Should().ContainSingle(c => c.AttributeName == "modifiedon", "the whole record was processed, so the watermark moved");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRoundBoundIsReachedWithChunksLeft_FaultsAndHoldsTheWatermark()
    {
        Source("sprk_matter", MatterM, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        for (var i = 0; i < 2_500; i++)
        {
            Chunk($"m{i}", documentId: "d1", sourceId: Id(MatterM), typeId: OldType);
        }

        var job = CreateJob(maxMergeRounds: 2);
        var result = await job.ExecuteAsync(Context(), CancellationToken.None);
        _changeQueries.Clear();
        await job.ExecuteAsync(Context(), CancellationToken.None);

        result.ErrorMessage.Should().Contain("faulted", "stopping with chunks left is a fault, not a clean finish");
        _changeQueries.Single(q => q.EntityName == "sprk_matter").Criteria.Conditions
            .Should().NotContain(c => c.AttributeName == "modifiedon", "the record must be read again");
        _chunks.Should().OnlyContain(c => c.MatterTypeId == Id(NewType), "the second run finishes the remainder");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRecordsIndexMergeIsRejected_FaultsAndHoldsTheWatermark()
    {
        Source("sprk_matter", MatterM, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        _recordMergeStatus = 503;
        var job = CreateJob();

        var result = await job.ExecuteAsync(Context(), CancellationToken.None);
        _changeQueries.Clear();
        await job.ExecuteAsync(Context(), CancellationToken.None);

        result.ErrorMessage.Should().Contain("faulted");
        _changeQueries.Single(q => q.EntityName == "sprk_matter").Criteria.Conditions
            .Should().NotContain(c => c.AttributeName == "modifiedon");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRecordWasNeverIndexed_TheRecordsMergeIs404AndIsNotAFault()
    {
        Source("sprk_matter", MatterM, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        _recordMergeStatus = 404;

        var result = await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_MatterWithoutAType_LeavesChunksThatHoldTheEmptyMarkerAlone()
    {
        Source("sprk_matter", MatterM, type: null, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        Chunk("m1", documentId: "d1", sourceId: Id(MatterM)).MatterTypeId = string.Empty;
        Chunk("m2", documentId: "d1", sourceId: Id(MatterM), typeId: OldType);

        await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        Stored("m1").MatterTypeId.Should().Be(string.Empty, "'' is the same answer as null: already correct, nothing to write");
        Stored("m2").MatterTypeId.Should().BeNull("a chunk still holding the removed type is cleared");
        _files.Invocations.Where(i => i.Method.Name == "MergeDocumentsAsync")
            .SelectMany(i => ((IEnumerable<SearchDocument>)i.Arguments[0]).Select(d => (string)d["id"]))
            .Should().BeEquivalentTo(["m2"]);
    }

    // ── backfill of chunks stored without a parent, and of rows that are gone ─────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Backfill_ChunksWithoutAParentTakeTheTypeOfTheRecordTheirDocumentNames()
    {
        Source("sprk_project", ProjectP, NewType);
        var doc = Guid.Parse("3165e000-0000-4000-8000-0000000000d3");
        _rows[("sprk_document", doc)] = new Entity("sprk_document", doc) { ["sprk_project"] = new EntityReference("sprk_project", ProjectP) };
        Chunk("a1", documentId: Id(doc));
        Chunk("a2", documentId: Id(doc));

        await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        new[] { "a1", "a2" }.Select(Stored).Should().OnlyContain(c => c.MatterTypeId == Id(NewType) && c.MatterTypeSourceId == Id(ProjectP));
        new[] { "a1", "a2" }.Select(Stored).Should().OnlyContain(c => c.ParentEntityType == null && c.ParentEntityId == null,
            "the parent is used to derive the type only; no parent field is written");
    }

    [Fact]
    public async Task ExecuteAsync_Backfill_AParentlessDocumentWhoseRowCannotBeReadIsNotSealedAndIsRetried()
    {
        var doc = Guid.Parse("3165e000-0000-4000-8000-0000000000d4");
        Source("sprk_project", ProjectP, NewType);
        _rows[("sprk_document", doc)] = new Entity("sprk_document", doc) { ["sprk_project"] = new EntityReference("sprk_project", ProjectP) };
        Chunk("a1", documentId: Id(doc));
        _failRead = (table, _) => table == "sprk_document";
        var job = CreateJob();

        var first = await job.ExecuteAsync(Context(), CancellationToken.None);

        first.Success.Should().BeTrue();
        Stored("a1").MatterTypeSourceId.Should().BeNull("an undecided parent must not be sealed as 'belongs to nothing'");

        _failRead = (_, _) => false;
        await job.ExecuteAsync(Context(), CancellationToken.None);

        Stored("a1").MatterTypeSourceId.Should().Be(Id(ProjectP));
        Stored("a1").MatterTypeId.Should().Be(Id(NewType));
    }

    [Fact]
    public async Task ExecuteAsync_Backfill_ADeletedDocumentIsResolvedAsBelongingToNothing()
    {
        var doc = Guid.Parse("3165e000-0000-4000-8000-0000000000d5");
        Chunk("a1", documentId: Id(doc));
        _rowGone = (table, id) => table == "sprk_document" && id == doc;

        var result = await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        result.ErrorMessage.Should().BeNull();
        Stored("a1").MatterTypeSourceId.Should().Be(string.Empty, "a deleted row is an answer; leaving it unresolved would re-read it every run");
    }

    [Fact]
    public async Task ExecuteAsync_Backfill_AChunkWhoseParentMatterWasDeletedIsResolvedAsBelongingToNothing()
    {
        Chunk("a1", documentId: "doc-a", parentType: "matter", parentId: MatterM);
        _rowGone = (table, id) => table == "sprk_matter" && id == MatterM;

        var result = await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        result.ErrorMessage.Should().BeNull();
        Stored("a1").MatterTypeSourceId.Should().Be(string.Empty);
        Stored("a1").MatterTypeId.Should().BeNull();
    }

    // ── tenant isolation, skips ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EveryFilesIndexQueryCarriesTheTenantFilter()
    {
        Source("sprk_matter", MatterM, NewType, modified: DateTime.Parse("2026-11-25T09:00:00Z").ToUniversalTime());
        Chunk("m1", documentId: "d1", sourceId: Id(MatterM), typeId: OldType);
        Source("sprk_project", ProjectP, NewType);
        Chunk("a1", documentId: "doc-a", parentType: "project", parentId: ProjectP);

        await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        _fileQueries.Should().NotBeEmpty();
        _fileQueries.Should().OnlyContain(q => q.Filter!.StartsWith($"tenantId eq '{Tenant}' and "));
    }

    [Fact]
    public async Task ExecuteAsync_WithNoTenantConfigured_SkipsWithoutTouchingTheIndex()
    {
        _tenantConfigured = null;

        var result = await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        result.Skipped.Should().BeTrue();
        _fileQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenAiSearchIsNotRegistered_Skips()
    {
        _aiAvailable = false;

        var result = await CreateJob().ExecuteAsync(Context(), CancellationToken.None);

        result.Skipped.Should().BeTrue();
        result.Success.Should().BeTrue();
    }
}
