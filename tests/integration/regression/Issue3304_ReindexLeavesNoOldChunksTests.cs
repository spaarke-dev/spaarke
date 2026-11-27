// Regression anchor for GitHub issue #3304. A re-index whose new text yields fewer chunks than before overwrote chunks 0..N-1 in
// place but left chunks N..M-1 as they were: the old text and the old parent record, still found by a search on that record. Only
// callers that asked for it removed that tail; a bulk sweep without force, /index-file and /enqueue did not. Every index now removes
// the file's chunks beyond the new count once the new chunks are written.
//
// The index is the assertion: FileIndexingService and RagService are real, the search client is the real Azure SDK client, and
// only its HTTP transport is a hand-rolled in-memory index (writes, the trim's filtered search, deletes) — never
// Mock<HttpMessageHandler>.

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

public sealed partial class Issue3304_ReindexLeavesNoOldChunksTests
{
    private const string Tenant = "tenant-3304";
    private const string Drive = "b!drive-3304";
    private const string Item = "01SHRUNK3304";

    private static readonly ParentEntityContext OldMatter = new("matter", "33040000-0000-4000-8000-000000000001", "Old Matter");
    private static readonly ParentEntityContext NewMatter = new("matter", "33040000-0000-4000-8000-000000000002", "New Matter");

    private readonly InMemoryIndex _index = new();
    private readonly Mock<ISpeFileOperations> _spe = new();
    private readonly Mock<ITextExtractor> _extractor = new();
    private int _chunkCount;

    private FileIndexingService CreateService()
    {
        var searchClient = new SearchClient(
            new Uri("https://test-search.search.windows.net"),
            "spaarke-files-index",
            new AzureKeyCredential(Guid.NewGuid().ToString("N")),
            new SearchClientOptions { Transport = new HttpClientTransport(new HttpClient(_index)), Retry = { MaxRetries = 0 } });

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

        var rag = new RagService(
            deployment.Object,
            openAi.Object,
            Mock.Of<IEmbeddingCache>(),
            Mock.Of<IPrivilegeGroupResolver>(),
            Options.Create(new AnalysisOptions()),
            new Mock<Azure.Search.Documents.Indexes.SearchIndexClient>(MockBehavior.Loose).Object,
            Options.Create(new AiSearchOptions { Endpoint = "https://test-search.search.windows.net", KnowledgeIndexName = "spaarke-files-index" }),
            NullLogger<RagService>.Instance);

        var chunker = new Mock<ITextChunkingService>();
        chunker
            .Setup(c => c.ChunkTextAsync(It.IsAny<string?>(), It.IsAny<ChunkingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Enumerable.Range(0, _chunkCount)
                .Select(i => new TextChunk { Content = $"text {_chunkCount}-{i}", Index = i, StartPosition = 0, EndPosition = 6 })
                .ToList());

        _spe.Setup(s => s.DownloadFileAsync(Drive, Item, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream("x"u8.ToArray()));
        _extractor
            .Setup(e => e.ExtractAsync(It.IsAny<Stream>(), "brief.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded("some content", TextExtractionMethod.Native));

        return new FileIndexingService(
            _spe.Object,
            _extractor.Object,
            chunker.Object,
            rag,
            NullLogger<FileIndexingService>.Instance,
            TestRecordContainerResolver.ForBusinessUnitContainers(Drive),
            new DocumentMatterTypeResolver(Mock.Of<Spaarke.Dataverse.IGenericEntityService>(), TestDocumentIndexParentResolver.Over(), NullLogger<DocumentMatterTypeResolver>.Instance));
    }

    /// <summary>A request as the paths named in the issue build it: nothing asks for a replacement.</summary>
    private static FileIndexRequest Request(ParentEntityContext parent) => new()
    {
        DriveId = Drive,
        ItemId = Item,
        FileName = "brief.txt",
        TenantId = Tenant,
        ParentEntity = parent,
    };

    [Fact]
    public async Task IndexFileAppOnly_ReindexWithFewerChunks_LeavesOnlyTheNewChunksUnderTheNewParent()
    {
        _chunkCount = 5;
        (await CreateService().IndexFileAppOnlyAsync(Request(OldMatter))).Success.Should().BeTrue();
        _index.Rows.Should().HaveCount(5);

        _chunkCount = 2;
        var result = await CreateService().IndexFileAppOnlyAsync(Request(NewMatter));

        result.Success.Should().BeTrue();
        _index.Rows.Keys.Should().BeEquivalentTo([$"{Item}_0", $"{Item}_1"],
            "a re-index leaves no chunk of the file from an earlier index");
        _index.Rows.Values.Should().OnlyContain(row => row["parentEntityId"]!.GetValue<string>() == NewMatter.EntityId,
            "no chunk is left findable under the record the file was filed under before");
        _index.Rows.Values.Should().OnlyContain(row => row["content"]!.GetValue<string>().StartsWith("text 2-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndexFileAppOnly_ReindexWithTheSameOrMoreChunks_DeletesNothingAndKeepsEveryNewChunk()
    {
        _chunkCount = 2;
        await CreateService().IndexFileAppOnlyAsync(Request(OldMatter));

        _chunkCount = 3;
        var result = await CreateService().IndexFileAppOnlyAsync(Request(OldMatter));

        result.Success.Should().BeTrue();
        _index.Rows.Should().HaveCount(3);
        _index.DeletedIds.Should().BeEmpty("there is no tail to remove");
    }

    [Fact]
    public async Task IndexFileAppOnly_FirstIndex_WritesEveryChunkAndDeletesNothing()
    {
        _chunkCount = 4;

        var result = await CreateService().IndexFileAppOnlyAsync(Request(OldMatter));

        result.Success.Should().BeTrue();
        _index.Rows.Should().HaveCount(4);
        _index.DeletedIds.Should().BeEmpty();
    }

    /// <summary>
    /// The files index's wire, in memory: applies each write and delete of an index batch, and answers the trim's search
    /// (<c>speFileId eq '…' and chunkIndex ge N</c>) from what it holds.
    /// </summary>
    private sealed partial class InMemoryIndex : HttpMessageHandler
    {
        public Dictionary<string, JsonObject> Rows { get; } = new(StringComparer.Ordinal);

        public List<string> DeletedIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            var root = JsonNode.Parse(body)!.AsObject();
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("search.post.search", StringComparison.Ordinal))
            {
                var filter = root["filter"]?.GetValue<string>() ?? string.Empty;
                var file = SpeFileIdClause().Match(filter);
                var from = ChunkIndexClause().Match(filter);
                var skip = root["skip"]?.GetValue<int>() ?? 0;
                var top = root["top"]?.GetValue<int>() ?? 50;

                var hits = Rows.Values
                    .Where(row => !file.Success || row["speFileId"]?.GetValue<string>() == file.Groups[1].Value)
                    .Where(row => !from.Success || (row["chunkIndex"]?.GetValue<int>() ?? -1) >= int.Parse(from.Groups[1].Value))
                    .OrderBy(row => row["id"]!.GetValue<string>(), StringComparer.Ordinal)
                    .Skip(skip)
                    .Take(top)
                    .Select(row => new JsonObject
                    {
                        ["@search.score"] = 1.0,
                        ["id"] = row["id"]!.GetValue<string>(),
                        ["chunkIndex"] = row["chunkIndex"]?.GetValue<int>(),
                    })
                    .ToArray<JsonNode?>();

                return Json(new JsonObject { ["value"] = new JsonArray(hits) });
            }

            var keys = new JsonArray();
            foreach (var action in root["value"]!.AsArray())
            {
                var document = action!.AsObject();
                var id = document["id"]!.GetValue<string>();
                var kind = document["@search.action"]?.GetValue<string>() ?? "upload";
                if (kind == "delete")
                {
                    Rows.Remove(id);
                    DeletedIds.Add(id);
                }
                else
                {
                    var merged = Rows.TryGetValue(id, out var existing) && kind != "upload" ? existing : new JsonObject();
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

        [GeneratedRegex(@"speFileId eq '([^']*)'")]
        private static partial Regex SpeFileIdClause();

        [GeneratedRegex(@"chunkIndex ge (\d+)")]
        private static partial Regex ChunkIndexClause();
    }
}
