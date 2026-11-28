// Regression anchor for GitHub issue #3223. Re-indexing a document whose parent answer is now "no parent" (it is no longer filed under a
// record, or the parent decision is withheld) wrote its chunks with a merge-or-upload that OMITTED the null parent fields, so the chunks
// kept the old parent and the document stayed findable under a record it no longer belongs to. The re-index now replaces the parent
// fields (an explicit null on the wire) and every other writer of the same model is left as it was.
//
// The wire is the assertion: FileIndexingService and RagService are real, the search client is the real Azure SDK client, and only its
// HTTP transport is a hand-rolled fake that records the JSON documents the files index receives (the SDK's own test seam, as in the
// provisioning tests; never Mock<HttpMessageHandler>).

using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Security;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class Issue3223_ReindexReplacesParentEntityTests
{
    private const string Tenant = "tenant-3223";
    private const string SpeFileId = "01REINDEXED3223";
    private const string Drive = "b!drive-3223";

    private readonly IndexWire _wire = new();
    private readonly Mock<ISpeFileOperations> _spe = new();
    private readonly Mock<ITextExtractor> _extractor = new();

    private RagService CreateRag()
    {
        var searchClient = new SearchClient(
            new Uri("https://test-search.search.windows.net"),
            "spaarke-files-index",
            new AzureKeyCredential(Guid.NewGuid().ToString("N")),
            new SearchClientOptions { Transport = new HttpClientTransport(new HttpClient(_wire)), Retry = { MaxRetries = 0 } });

        var deployment = new Mock<IKnowledgeDeploymentService>();
        deployment.Setup(d => d.GetSearchClientAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(searchClient);
        deployment
            .Setup(d => d.GetDeploymentConfigAsync(Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeDeploymentConfig { TenantId = Tenant, Name = "d", Model = RagDeploymentModel.Shared, IndexName = "spaarke-files-index" });

        var openAi = new Mock<IOpenAiClient>();
        openAi
            .Setup(o => o.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, string? _, int? _, CancellationToken _) =>
                (IReadOnlyList<ReadOnlyMemory<float>>)texts.Select(_ => new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f])).ToList());

        return new RagService(
            deployment.Object,
            openAi.Object,
            Mock.Of<IEmbeddingCache>(),
            Mock.Of<IPrivilegeGroupResolver>(),
            Options.Create(new AnalysisOptions()),
            new Mock<Azure.Search.Documents.Indexes.SearchIndexClient>(MockBehavior.Loose).Object,
            Options.Create(new AiSearchOptions { Endpoint = "https://test-search.search.windows.net", KnowledgeIndexName = "spaarke-files-index" }),
            NullLogger<RagService>.Instance);
    }

    private FileIndexingService CreateService()
    {
        var chunker = new Mock<ITextChunkingService>();
        chunker
            .Setup(c => c.ChunkTextAsync(It.IsAny<string?>(), It.IsAny<ChunkingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 2)
                .Select(i => new TextChunk { Content = $"chunk {i}", Index = i, StartPosition = 0, EndPosition = 7 })
                .ToList());

        return new FileIndexingService(
            _spe.Object,
            _extractor.Object,
            chunker.Object,
            CreateRag(),
            NullLogger<FileIndexingService>.Instance,
            TestRecordContainerResolver.ForBusinessUnitContainers(Drive),
            new DocumentMatterTypeResolver(Mock.Of<Spaarke.Dataverse.IGenericEntityService>(), TestDocumentIndexParentResolver.Over(), NullLogger<DocumentMatterTypeResolver>.Instance));
    }

    // The production path: a file index request through IndexFileAppOnlyAsync (the app-only job handlers) / IndexFileAsync (the OBO
    // seams) both build the chunk documents in the same place, so the request's own flag is what the wire shows.
    private async Task<FileIndexingResult> IndexAsync(FileIndexRequest request)
    {
        _spe.Setup(s => s.DownloadFileAsync("drive", "item-3223", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream("x"u8.ToArray()));
        _extractor
            .Setup(e => e.ExtractAsync(It.IsAny<Stream>(), "doc.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded("some content", TextExtractionMethod.Native));
        return await CreateService().IndexFileAppOnlyAsync(request);
    }

    /// <summary>A request as a producer builds it: only what it decided. <paramref name="replaceParent"/> null leaves the flag at its default.</summary>
    private static FileIndexRequest Request(ParentEntityContext? parent, bool? replaceParent) => replaceParent is { } replace
        ? new FileIndexRequest { DriveId = "drive", ItemId = "item-3223", FileName = "doc.txt", TenantId = Tenant, ParentEntity = parent, ReplaceParentEntity = replace }
        : new FileIndexRequest { DriveId = "drive", ItemId = "item-3223", FileName = "doc.txt", TenantId = Tenant, ParentEntity = parent };

    private static readonly ParentEntityContext Matter = new("matter", "3223b000-0000-4000-8000-000000000001", "Old Matter");

    [Fact]
    public async Task Reindex_WhoseParentIsDecidedNone_SendsTheParentFieldsAsExplicitNullsSoTheOldParentIsCleared()
    {
        (await IndexAsync(Request(Matter, replaceParent: true))).Success.Should().BeTrue();
        _wire.Documents.Should().NotBeEmpty();
        _wire.Documents.Should().OnlyContain(d => d.GetProperty("parentEntityType").GetString() == "matter");
        _wire.Documents.Clear();

        // The same file again, now filed under nothing (left its record, or the parent decision is withheld): a decided answer.
        var result = await IndexAsync(Request(parent: null, replaceParent: true));

        result.Success.Should().BeTrue();
        _wire.Documents.Should().HaveCount(2);
        foreach (var document in _wire.Documents)
        {
            // A property ABSENT from a merge-or-upload keeps the stored value; an explicit null clears it.
            foreach (var field in new[] { "parentEntityType", "parentEntityId", "parentEntityname" })
            {
                document.TryGetProperty(field, out var value).Should().BeTrue($"{field} must be on the wire for the re-index to replace it");
                value.ValueKind.Should().Be(JsonValueKind.Null, $"{field} is cleared, not set to a sentinel");
            }
        }
    }

    [Fact]
    public async Task Reindex_WithADefaultRequestThatNamesNoParent_KeepsTheParentFieldsOffTheWire()
    {
        // A request that never decided a parent (/index-file with neither a document nor a parent, the Office add-in worker, the
        // queue route) must not read its silence as "no parent": the default request leaves the stored parent alone.
        var result = await IndexAsync(Request(parent: null, replaceParent: null));

        result.Success.Should().BeTrue();
        _wire.Documents.Should().NotBeEmpty();
        _wire.Documents.Should().OnlyContain(d => !NamesAParentField(d));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Reindex_UnderAnotherRecord_ReplacesAllThreeParentFieldsWhateverTheFlag(bool? replaceParent)
    {
        await IndexAsync(Request(new ParentEntityContext("project", "3223b000-0000-4000-8000-000000000002", "New Project"), replaceParent));

        _wire.Documents.Should().NotBeEmpty();
        _wire.Documents.Should().OnlyContain(d =>
            d.GetProperty("parentEntityType").GetString() == "project"
            && d.GetProperty("parentEntityId").GetString() == "3223b000-0000-4000-8000-000000000002"
            && d.GetProperty("parentEntityname").GetString() == "New Project");
    }

    [Fact]
    public async Task Reindex_ClearingTheParent_LeavesTheMatterTypeFieldsAsTheyWereWritten()
    {
        await IndexAsync(Request(parent: null, replaceParent: true));

        // Matter type keeps its own contract (#3165): a resolved "none" is the empty string, never a null.
        _wire.Documents.Should().NotBeEmpty();
        _wire.Documents.Should().OnlyContain(d =>
            d.GetProperty("matterTypeId").GetString() == string.Empty && d.GetProperty("matterTypeSourceId").GetString() == string.Empty);
    }

    private static bool NamesAParentField(JsonElement document) =>
        document.TryGetProperty("parentEntityType", out _)
        || document.TryGetProperty("parentEntityId", out _)
        || document.TryGetProperty("parentEntityname", out _);

    [Fact]
    public async Task OtherWritersOfTheModel_StillOmitNullParentFieldsSoAnIndexWithoutThemAcceptsTheWrite()
    {
        // RagIndexingPipeline's chunks (and the session-files ones, whose index has no parent fields and answers 400 to a
        // document that names them) never set the flag, so a null parent stays off the wire as before.
        var rag = CreateRag();

        await rag.IndexDocumentsBatchAsync(
        [
            new KnowledgeDocument
            {
                Id = "doc_k_0", TenantId = Tenant, SpeFileId = SpeFileId, Content = "c",
                ContentVector = new ReadOnlyMemory<float>([0.1f]), Tags = [],
            },
        ]);

        _wire.Documents.Should().ContainSingle();
        _wire.Documents[0].TryGetProperty("parentEntityType", out _).Should().BeFalse();
        _wire.Documents[0].TryGetProperty("parentEntityId", out _).Should().BeFalse();
        _wire.Documents[0].TryGetProperty("parentEntityname", out _).Should().BeFalse();
    }

    [Fact]
    public void IndexRow_WithParentFields_ReadsThemBackIncludingAnExplicitNull()
    {
        var filed = JsonSerializer.Deserialize<KnowledgeDocument>(
            "{\"id\":\"c1\",\"parentEntityType\":\"matter\",\"parentEntityId\":\"3223b000-0000-4000-8000-000000000001\",\"parentEntityname\":\"M\"}")!;
        var cleared = JsonSerializer.Deserialize<KnowledgeDocument>(
            "{\"id\":\"c2\",\"parentEntityType\":null,\"parentEntityId\":null,\"parentEntityname\":null}")!;

        filed.ParentEntityType.Should().Be("matter");
        filed.ParentEntityId.Should().Be("3223b000-0000-4000-8000-000000000001");
        filed.ParentEntityName.Should().Be("M");
        cleared.ParentEntityType.Should().BeNull();
        cleared.ParentEntityId.Should().BeNull();
        cleared.ParentEntityName.Should().BeNull();
    }

    /// <summary>The files index's wire: records each JSON document of every index request and answers "all succeeded".</summary>
    private sealed class IndexWire : HttpMessageHandler
    {
        public List<JsonElement> Documents { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // GitHub #3304: every file index is followed by the leftover-chunk trim's search; this wire holds nothing, so no tail.
            if (request.RequestUri!.AbsolutePath.Contains("search.post.search", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}", Encoding.UTF8, "application/json") };
            }

            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var parsed = JsonDocument.Parse(body);
            var keys = new List<string>();
            foreach (var document in parsed.RootElement.GetProperty("value").EnumerateArray())
            {
                Documents.Add(document.Clone());
                keys.Add(document.GetProperty("id").GetString()!);
            }

            var reply = JsonSerializer.Serialize(new { value = keys.Select(key => new { key, status = true, statusCode = 200 }) });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }
}
