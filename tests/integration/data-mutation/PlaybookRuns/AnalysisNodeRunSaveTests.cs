// spaarkeai-mcp-bff-api-r2 task 037a (owner decision D-037-engine, option B).
//
// The node-branch save of the Analysis wrapper (AnalysisOrchestrationService.ExecutePlaybookAsync): Deliver Output, then
// IWorkingDocumentService.TryUpdateWorkingDocumentAsync (task 037 review F-A: a write that reports a failure; it was
// UpdateWorkingDocumentAsync), then FinalizeAnalysisAsync. These tests were written BEFORE that save
// moved into one shared method (the engine calls it too, for a run that carries an AnalysisId), and they pin the wrapper's
// output (its stream chunks) and its writes (sprk_workingdocument and the finalize) so the move changes neither.
//
// The real wrapper, AnalysisResultPersistence and AnalysisDocumentLoader run; the module boundaries are mocked: the engine
// (IPlaybookOrchestrationService, resolved from the request's services as in production, scripted to the stream of events a
// node run produces), the working-document writer (IWorkingDocumentService: the sprk_analysis writes), and the Dataverse and
// cache readers the wrapper loads the playbook and the document through.

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.PlaybookRuns;

/// <summary>
/// The Analysis wrapper's node-branch save: what it writes to <c>sprk_analysis</c> (the working document and the finalize) and the
/// chunks it streams, for each way a node run can end.
/// </summary>
/// <remarks>KEEP data-mutation path (ADR-038): each test asserts the <c>sprk_analysis</c> writes the run makes.</remarks>
public sealed class AnalysisNodeRunSaveTests
{
    private static readonly Guid PlaybookId = Guid.Parse("9b000037-0000-4000-8000-000000000001");
    private static readonly Guid DocumentId = Guid.Parse("d0c00037-0000-4000-8000-000000000001");
    private static readonly Guid AnalysisId = Guid.Parse("a7a00037-0000-4000-8000-000000000001");
    private static readonly Guid RunId = Guid.Parse("4a000037-0000-4000-8000-000000000001");
    private static readonly Guid NodeId = Guid.Parse("40de0037-0000-4000-8000-000000000001");

    private readonly Mock<IDataverseService> _dataverse = new();
    private readonly Mock<IPlaybookService> _playbooks = new();
    private readonly Mock<INodeService> _nodes = new();
    private readonly Mock<IWorkingDocumentService> _workingDocument = new();
    private readonly Mock<IPlaybookOrchestrationService> _engine = new();

    public AnalysisNodeRunSaveTests()
    {
        _playbooks.Setup(x => x.GetPlaybookAsync(PlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse { Id = PlaybookId, Name = "Summarize an NDA" });
        _dataverse.Setup(x => x.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity { Id = DocumentId.ToString(), Name = "NDA.pdf", Status = DocumentStatus.Active });
        _nodes.Setup(x => x.GetNodesAsync(PlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlaybookNodeDto { Id = NodeId, Name = "Deliver", ActionId = Guid.NewGuid(), OutputVariable = "deliver" }]);
        // Task 037 review F-A: the save writes through the reporting write; true = written.
        _workingDocument.Setup(x => x.TryUpdateWorkingDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _workingDocument.Setup(x => x.FinalizeAnalysisAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact(DisplayName = "037a wrapper node save: the Deliver Output text is the working document; the run's tokens finalize it")]
    public async Task DeliverOutput_IsTheWorkingDocument_AndTheRunIsFinalizedWithItsTokens()
    {
        var chunks = await RunWrapperAsync(
            Progress("streamed "),
            Progress("tokens"),
            Completed(Deliver("# NDA summary")),
            RunCompleted(tokensIn: 11, tokensOut: 22));

        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(AnalysisId, "# NDA summary", It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(AnalysisId, 11, 22, It.IsAny<CancellationToken>()), Times.Once);
        Snapshot(chunks).Should().Be(
            Expected(
                """{"Type":"chunk","Content":"[Playbook execution started: 0 nodes]\n","Done":false,"AnalysisId":null,"DocumentName":null,"TokenUsage":null,"Error":null,"PartialStorage":null,"StorageMessage":null,"Step":null,"ErrorCode":null,"CorrelationId":null}""",
                """{"Type":"chunk","Content":"streamed ","Done":false,"AnalysisId":null,"DocumentName":null,"TokenUsage":null,"Error":null,"PartialStorage":null,"StorageMessage":null,"Step":null,"ErrorCode":null,"CorrelationId":null}""",
                """{"Type":"chunk","Content":"tokens","Done":false,"AnalysisId":null,"DocumentName":null,"TokenUsage":null,"Error":null,"PartialStorage":null,"StorageMessage":null,"Step":null,"ErrorCode":null,"CorrelationId":null}""",
                """{"Type":"chunk","Content":"\n[Node 'Deliver' completed]\n\n","Done":false,"AnalysisId":null,"DocumentName":null,"TokenUsage":null,"Error":null,"PartialStorage":null,"StorageMessage":null,"Step":null,"ErrorCode":null,"CorrelationId":null}""",
                """{"Type":"done","Content":null,"Done":true,"AnalysisId":"a7a00037-0000-4000-8000-000000000001","DocumentName":null,"TokenUsage":{"Input":11,"Output":22},"Error":null,"PartialStorage":null,"StorageMessage":null,"Step":null,"ErrorCode":null,"CorrelationId":null}"""));
    }

    [Fact(DisplayName = "037a wrapper node save: with no Deliver Output, the streamed text is the working document")]
    public async Task NoDeliverOutput_TheStreamedTextIsTheWorkingDocument()
    {
        var chunks = await RunWrapperAsync(
            Progress("first "),
            Progress("second"),
            Completed(new NodeOutput { NodeId = NodeId, OutputVariable = "deliver", Success = true, TextContent = "not delivered", Metrics = new NodeExecutionMetrics() }),
            RunCompleted(tokensIn: 3, tokensOut: 4));

        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(AnalysisId, "first second", It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(AnalysisId, 3, 4, It.IsAny<CancellationToken>()), Times.Once);
        chunks[^1].Should().Be(AnalysisStreamChunk.Completed(AnalysisId, new TokenUsage(3, 4)));
    }

    [Fact(DisplayName = "037a wrapper node save: a failed node with output saves it and says the results are partial")]
    public async Task FailedNodeWithOutput_SavesIt_AndReportsPartialStorage()
    {
        var chunks = await RunWrapperAsync(
            Failed("Extract"),
            Completed(Deliver("# Partial")),
            RunCompleted(tokensIn: 5, tokensOut: 6));

        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(AnalysisId, "# Partial", It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(AnalysisId, 5, 6, It.IsAny<CancellationToken>()), Times.Once);
        chunks[^1].Should().Be(AnalysisStreamChunk.Completed(
            AnalysisId, new TokenUsage(5, 6), partialStorage: true,
            storageMessage: "Some nodes failed during execution but partial results are available."));
    }

    [Fact(DisplayName = "037a wrapper node save: no output writes no working document, still finalizes, and ends in the error chunk")]
    public async Task NoOutput_WritesNoWorkingDocument_FinalizesAndEndsInError()
    {
        var chunks = await RunWrapperAsync(RunCompleted(tokensIn: 1, tokensOut: 2));

        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(AnalysisId, 1, 2, It.IsAny<CancellationToken>()), Times.Once);
        chunks[^1].Should().Be(AnalysisStreamChunk.FromError("Node-based execution produced no output"));
    }

    [Fact(DisplayName = "037a wrapper node save: a failed run saves its streamed text, does not finalize, and ends in the run's error")]
    public async Task FailedRun_SavesTheStreamedText_DoesNotFinalize_AndEndsInTheRunError()
    {
        var chunks = await RunWrapperAsync(
            Progress("part"),
            PlaybookStreamEvent.RunFailed(RunId, PlaybookId, "Node 'Extract' failed: model refused"),
            RunCompleted(tokensIn: 9, tokensOut: 9));

        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(AnalysisId, "part", It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        chunks[^1].Should().Be(AnalysisStreamChunk.FromError("Node 'Extract' failed: model refused"));
    }

    [Fact(DisplayName = "037 review F-A wrapper node save: a working-document write that fails is not followed by the finalize; the chunks are unchanged")]
    public async Task WriteFails_TheAnalysisIsNotFinalized_AndTheChunksAreUnchanged()
    {
        _workingDocument.Setup(x => x.TryUpdateWorkingDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var chunks = await RunWrapperAsync(
            Completed(Deliver("# NDA summary")),
            RunCompleted(tokensIn: 11, tokensOut: 22));

        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(AnalysisId, "# NDA summary", It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
            "an analysis whose content was not written is not marked Completed");
        chunks[^1].Should().Be(AnalysisStreamChunk.Completed(AnalysisId, new TokenUsage(11, 22)));
    }

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<List<AnalysisStreamChunk>> RunWrapperAsync(params PlaybookStreamEvent[] engineEvents)
    {
        _engine.Setup(x => x.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns(Stream(engineEvents));

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(_engine.Object).BuildServiceProvider(),
        };

        var chunks = new List<AnalysisStreamChunk>();
        await foreach (var chunk in NewWrapper().ExecutePlaybookAsync(
                           new PlaybookExecuteRequest { PlaybookId = PlaybookId, DocumentIds = [DocumentId], AnalysisId = AnalysisId },
                           http,
                           CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // The wrapper's own run request never carries the analysis id: the engine must not save a second time.
        _engine.Verify(x => x.ExecuteAsync(
            It.Is<PlaybookRunRequest>(r => r.PlaybookId == PlaybookId), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once);

        // The chunks after the wrapper's own preamble (metadata, node-mode detection and text extraction lines).
        return chunks.SkipWhile(c => c.Type == "metadata" || (c.Content?.StartsWith("[Node-based", StringComparison.Ordinal) ?? false)
                                     || (c.Content?.StartsWith("[Extracting", StringComparison.Ordinal) ?? false)
                                     || (c.Content?.StartsWith("[Document text", StringComparison.Ordinal) ?? false))
            .ToList();
    }

    private AnalysisOrchestrationService NewWrapper()
    {
        var tenantCache = new Mock<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>();
        var accessor = new Mock<IHttpContextAccessor>();
        var options = Options.Create(new AnalysisOptions { Enabled = true, MaxChatHistoryMessages = 10 });

        var documentLoader = new AnalysisDocumentLoader(
            _dataverse.Object,
            _dataverse.Object,
            new Mock<ISpeFileOperations>().Object,
            TestRecordContainerResolver.ForBusinessUnitContainers(c => true),
            new Mock<ITextExtractor>().Object,
            tenantCache.Object,
            accessor.Object,
            NullLogger<AnalysisDocumentLoader>.Instance);

        var ragProcessor = new AnalysisRagProcessor(
            new Mock<IRagService>().Object,
            new RagQueryBuilder(),
            tenantCache.Object,
            accessor.Object,
            PermitAllRetrievalAccessTrim.Instance,
            options,
            NullLogger<AnalysisRagProcessor>.Instance);

        var resultPersistence = new AnalysisResultPersistence(
            _dataverse.Object,
            _dataverse.Object,
            _workingDocument.Object,
            new Mock<IStorageRetryPolicy>().Object,
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            NullLogger<AnalysisResultPersistence>.Instance);

        return new AnalysisOrchestrationService(
            new Mock<IOpenAiClient>().Object,
            new Mock<IScopeResolverService>().Object,
            new Mock<IAnalysisContextBuilder>().Object,
            _playbooks.Object,
            new ServiceCollection().AddSingleton(new Mock<IToolHandlerRegistry>().Object).BuildServiceProvider(),
            _nodes.Object,
            documentLoader,
            ragProcessor,
            resultPersistence,
            NullLogger<AnalysisOrchestrationService>.Instance);
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> Stream(IEnumerable<PlaybookStreamEvent> events)
    {
        yield return PlaybookStreamEvent.RunStarted(RunId, PlaybookId, 0);
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }

    private static PlaybookStreamEvent Progress(string text) => PlaybookStreamEvent.NodeProgress(RunId, PlaybookId, NodeId, text);

    private static PlaybookStreamEvent Completed(NodeOutput output) => PlaybookStreamEvent.NodeCompleted(RunId, PlaybookId, NodeId, "Deliver", output);

    private static PlaybookStreamEvent Failed(string node) => PlaybookStreamEvent.NodeFailed(RunId, PlaybookId, Guid.NewGuid(), node, "model refused");

    private static NodeOutput Deliver(string text) => new()
    {
        NodeId = NodeId,
        OutputVariable = "deliver",
        Success = true,
        TextContent = text,
        IsDeliverOutput = true,
        Metrics = new NodeExecutionMetrics(),
    };

    private static PlaybookStreamEvent RunCompleted(int tokensIn, int tokensOut) =>
        PlaybookStreamEvent.RunCompleted(RunId, PlaybookId, new PlaybookRunMetrics { TotalTokensIn = tokensIn, TotalTokensOut = tokensOut });

    /// <summary>Relaxed escaping, so the snapshot reads as the text the chunk carries (an apostrophe stays an apostrophe).</summary>
    private static readonly JsonSerializerOptions SnapshotJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Snapshot(IEnumerable<AnalysisStreamChunk> chunks) =>
        string.Join("\n", chunks.Select(c => JsonSerializer.Serialize(c, SnapshotJson)));

    private static string Expected(params string[] lines) => string.Join("\n", lines);
}
