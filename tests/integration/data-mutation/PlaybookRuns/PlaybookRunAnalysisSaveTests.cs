// spaarkeai-mcp-bff-api-r2 task 037a (owner decision D-037-engine, option B; ADR-039 Path C, owner-approved).
//
// The REAL PlaybookOrchestrationService (the engine) runs a playbook with and without a PlaybookRunRequest.AnalysisId:
//   (a) legacy mode hands the id to the Analysis wrapper (the REAL AnalysisOrchestrationService), which writes the run's output to
//       that analysis (#3285: before, it wrote to an id it minted, a record that does not exist);
//   (b) node mode saves the Deliver Output to that analysis once, when the run ends, before the reader sees the run's end, through
//       the wrapper's own save (AnalysisResultPersistence.SaveNodeRunAsync).
// Without an AnalysisId, nothing is saved to an analysis by the engine, as before. The module boundaries are mocked: the node and
// scope readers, the node executor, the tool handler, and the sprk_analysis writer (IWorkingDocumentService).

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.PlaybookRuns;

/// <summary>
/// D-037-engine: a playbook run that carries an <c>AnalysisId</c> saves its output to that <c>sprk_analysis</c> (node and legacy mode);
/// a run without one writes nothing to an analysis through the engine.
/// </summary>
/// <remarks>KEEP data-mutation path (ADR-038): each test asserts the <c>sprk_analysis</c> writes a run makes, or that it makes none.</remarks>
public sealed class PlaybookRunAnalysisSaveTests
{
    private const string CallerOid = "0a5e0037-0000-4000-8000-000000000037";

    private static readonly Guid PlaybookId = Guid.Parse("9b000037-0000-4000-8000-000000000002");
    private static readonly Guid DocumentId = Guid.Parse("d0c00037-0000-4000-8000-000000000002");
    private static readonly Guid AnalysisId = Guid.Parse("a7a00037-0000-4000-8000-000000000002");
    private static readonly Guid CallerSystemUserId = Guid.Parse("5a5e0037-0000-4000-8000-000000000037");

    private readonly Mock<INodeService> _nodes = new();
    private readonly Mock<INodeExecutorRegistry> _executors = new();
    private readonly Mock<INodeExecutor> _executor = new();
    private readonly Mock<IScopeResolverService> _scopes = new();
    private readonly Mock<IInsightsActionRouter> _insightsRouter = new();
    private readonly Mock<IWorkingDocumentService> _workingDocument = new();
    private readonly Mock<IDataverseService> _dataverse = new();
    private readonly Mock<IPlaybookService> _playbooks = new();
    private readonly Mock<IToolHandlerRegistry> _toolHandlers = new();

    public PlaybookRunAnalysisSaveTests()
    {
        _insightsRouter
            .Setup(r => r.ResolveLayer1ActionAsync(It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, AnalysisAction action, CancellationToken _) => action);
        _insightsRouter
            .Setup(r => r.ResolveLayer2ActionAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, string? __, AnalysisAction action, CancellationToken _) => InsightsLayer2RoutingResult.PassThrough(action));
        _executor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>())).Returns(NodeValidationResult.Success());
        _executors.Setup(x => x.GetExecutor(ExecutorType.AiAnalysis)).Returns(_executor.Object);
        // The node run's save writes through the reporting write (task 037 review F-A); the legacy wrapper streams through the
        // swallowing one, as before.
        _workingDocument.Setup(x => x.TryUpdateWorkingDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _workingDocument.Setup(x => x.UpdateWorkingDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _workingDocument.Setup(x => x.FinalizeAnalysisAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ── (b) node mode ───────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "037a engine (b): a node run with an AnalysisId saves its Deliver Output to that analysis before the reader sees it end")]
    public async Task NodeRun_WithAnalysisId_SavesTheDeliverOutput_BeforeTheRunEnds()
    {
        OneDeliverNode("# Risk summary", tokensIn: 7, tokensOut: 8);
        var savedWhenTheReaderSawTheEnd = false;

        var events = await RunAsync(NewEngine(), AnalysisId, onEvent: evt =>
        {
            if (evt.Type == PlaybookEventType.RunCompleted)
            {
                savedWhenTheReaderSawTheEnd = _workingDocument.Invocations.Any(i => i.Method.Name == nameof(IWorkingDocumentService.FinalizeAnalysisAsync));
            }
        });

        events.Should().Contain(e => e.Type == PlaybookEventType.RunCompleted);
        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(AnalysisId, "# Risk summary", It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(AnalysisId, 7, 8, It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.VerifyNoOtherCalls();
        savedWhenTheReaderSawTheEnd.Should().BeTrue("the save runs before the run's end reaches its reader (the status a widget polls)");
    }

    [Fact(DisplayName = "037a engine (b): a node run without an AnalysisId writes nothing to an analysis (as before)")]
    public async Task NodeRun_WithoutAnalysisId_WritesNothingToAnAnalysis()
    {
        OneDeliverNode("# Risk summary", tokensIn: 7, tokensOut: 8);

        var events = await RunAsync(NewEngine(), analysisId: null);

        events.Select(e => e.Type).Should().Equal(
            PlaybookEventType.RunStarted, PlaybookEventType.NodeStarted, PlaybookEventType.NodeCompleted, PlaybookEventType.RunCompleted);
        _workingDocument.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "037a engine (b): a failed node run with an AnalysisId saves its streamed text and does not finalize")]
    public async Task NodeRun_WithAnalysisId_ThatFails_SavesTheStreamedText_AndDoesNotFinalize()
    {
        var node = Node("Summarize");
        _nodes.Setup(x => x.GetNodesAsync(PlaybookId, It.IsAny<CancellationToken>())).ReturnsAsync([node]);
        SetUpAction(node);
        _executor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (NodeExecutionContext ctx, CancellationToken _) =>
            {
                await ctx.OnTokenReceived!("partial text");
                return NodeOutput.Error(ctx.Node.Id, ctx.Node.OutputVariable, "model refused", NodeErrorCodes.ModelError);
            });

        var events = await RunAsync(NewEngine(), AnalysisId);

        events[^1].Type.Should().Be(PlaybookEventType.RunFailed);
        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(AnalysisId, "partial text", It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "037 review F-A: a failed working-document write (the REAL WorkingDocumentService) logs the outcome, skips the finalize and never changes the run")]
    public async Task NodeRun_WithAnalysisId_SaveFails_IsLogged_TheAnalysisIsNotFinalized_TheRunStillCompletes()
    {
        OneDeliverNode("# Risk summary", tokensIn: 1, tokensOut: 1);
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(x => x.UpdateAsync("sprk_analysis", AnalysisId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse refused the value '# Risk summary' (test)."));
        var logger = new CapturingLogger();
        _saveWriter = new WorkingDocumentService(dataverse.Object, Options.Create(new AnalysisOptions()), logger);

        var events = await RunAsync(NewEngine(logger), AnalysisId);

        events[^1].Type.Should().Be(PlaybookEventType.RunCompleted, "a failed save never changes the run");
        logger.Lines.Should().Contain(l => l.Contains("playbook_run_analysis_save_failed"), "the save's outcome code fires on a real write failure");
        logger.Lines.Should().Contain(l => l.Contains(WorkingDocumentService.WriteFailedOutcome) && l.Contains(nameof(InvalidOperationException)));
        logger.Lines.Should().NotContain(l => l.Contains("Risk summary"), "ADR-015: the exception type only, never the output it may quote");
        dataverse.Verify(x => x.UpdateAsync("sprk_analysis", AnalysisId,
            It.Is<Dictionary<string, object>>(f => f.ContainsKey("sprk_workingdocument")), It.IsAny<CancellationToken>()), Times.Once);
        dataverse.Verify(x => x.UpdateAsync("sprk_analysis", AnalysisId,
            It.Is<Dictionary<string, object>>(f => f.ContainsKey("statuscode")), It.IsAny<CancellationToken>()), Times.Never,
            "an analysis whose content was not written is not marked Completed");
    }

    [Fact(DisplayName = "037 review F-A: a Deliver Output past the column's 100,000 characters is saved clipped, with the visible marker, and finalized")]
    public async Task NodeRun_WithAnalysisId_OversizeOutput_IsSavedClippedWithTheMarker()
    {
        OneDeliverNode(new string('a', 150_000), tokensIn: 2, tokensOut: 3);

        await RunAsync(NewEngine(), AnalysisId);

        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(
            AnalysisId,
            It.Is<string>(s => s.Length == AnalysisResultPersistence.WorkingDocumentMaxChars
                               && s.EndsWith(AnalysisResultPersistence.WorkingDocumentTruncatedMarker, StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(AnalysisId, 2, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "037 review F-A: the clip never splits a surrogate pair and leaves text that fits unchanged")]
    public void ClipToWorkingDocumentLimit_KeepsSurrogatePairsWhole()
    {
        var keep = AnalysisResultPersistence.WorkingDocumentMaxChars - AnalysisResultPersistence.WorkingDocumentTruncatedMarker.Length;
        var straddling = new string('a', keep - 1) + "😀" + new string('b', 10_000);

        var clipped = AnalysisResultPersistence.ClipToWorkingDocumentLimit(straddling);

        clipped.Length.Should().BeLessThanOrEqualTo(AnalysisResultPersistence.WorkingDocumentMaxChars);
        clipped.Should().EndWith(AnalysisResultPersistence.WorkingDocumentTruncatedMarker);
        var body = clipped[..^AnalysisResultPersistence.WorkingDocumentTruncatedMarker.Length];
        char.IsHighSurrogate(body[^1]).Should().BeFalse("the cut never leaves half a surrogate pair");
        AnalysisResultPersistence.ClipToWorkingDocumentLimit("short").Should().Be("short");
        var exact = new string('x', AnalysisResultPersistence.WorkingDocumentMaxChars);
        AnalysisResultPersistence.ClipToWorkingDocumentLimit(exact).Should().BeSameAs(exact);
    }

    [Fact(DisplayName = "037 review F-B: a node run whose AI node throws an upstream timeout (not the run's token) saves its streamed text")]
    public async Task NodeRun_WithAnalysisId_UpstreamTimeout_SavesTheStreamedText()
    {
        var node = Node("Summarize");
        _nodes.Setup(x => x.GetNodesAsync(PlaybookId, It.IsAny<CancellationToken>())).ReturnsAsync([node]);
        SetUpAction(node);
        _executor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (NodeExecutionContext ctx, CancellationToken _) =>
            {
                await ctx.OnTokenReceived!("partial text");
                throw new TaskCanceledException("HttpClient.Timeout elapsed (test).");
            });

        var events = await RunAsync(NewEngine(), AnalysisId);

        events[^1].Type.Should().Be(PlaybookEventType.RunFailed);
        _workingDocument.Verify(x => x.TryUpdateWorkingDocumentAsync(AnalysisId, "partial text", It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── (a) legacy mode ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "037a engine (a): a legacy run with an AnalysisId hands it to the wrapper, whose output lands in that analysis (#3285)")]
    public async Task LegacyRun_WithAnalysisId_TheWrapperWritesToThatAnalysis()
    {
        LegacyPlaybookWithOneTool("Risk: low");

        var events = await RunAsync(NewEngine(), AnalysisId);

        events[^1].Type.Should().Be(PlaybookEventType.RunCompleted);
        _workingDocument.Verify(x => x.UpdateWorkingDocumentAsync(
            AnalysisId, It.Is<string>(s => s.Contains("Risk: low")), It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.Verify(x => x.FinalizeAnalysisAsync(AnalysisId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _workingDocument.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "037a engine (a): a legacy run without an AnalysisId leaves the wrapper to mint its own id (as before)")]
    public async Task LegacyRun_WithoutAnalysisId_TheWrapperMintsItsOwnId()
    {
        LegacyPlaybookWithOneTool("Risk: low");

        await RunAsync(NewEngine(), analysisId: null);

        var written = _workingDocument.Invocations.Single(i => i.Method.Name == nameof(IWorkingDocumentService.UpdateWorkingDocumentAsync));
        ((Guid)written.Arguments[0]).Should().NotBe(Guid.Empty).And.NotBe(AnalysisId);
    }

    [Fact(DisplayName = "037 re-check K3: a fault resolving the analysis persistence gives the run's own writer, so the run still ends")]
    public void SavingWriter_PersistenceResolutionFaults_ReturnsTheRunWriterAndLogsTheTypeOnly()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<PlaybookStreamEvent>();
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(AnalysisResultPersistence)))
            .Throws(new InvalidOperationException("secret detail that must not be logged"));
        var logger = new CapturingLogger();

        var writer = NodeRunAnalysisSave.SavingWriter(channel.Writer, AnalysisId, services.Object, Guid.NewGuid(), logger);

        writer.Should().BeSameAs(channel.Writer, "a resolution fault must not escape before the engine's try, or the channel never completes");
        logger.Lines.Should().Contain(l => l.Contains(NodeRunAnalysisSave.SaveUnavailableOutcome) && l.Contains(nameof(InvalidOperationException)));
        logger.Lines.Should().NotContain(l => l.Contains("secret detail"));
    }

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<List<PlaybookStreamEvent>> RunAsync(
        PlaybookOrchestrationService engine, Guid? analysisId, Action<PlaybookStreamEvent>? onEvent = null)
    {
        var request = new PlaybookRunRequest
        {
            PlaybookId = PlaybookId,
            DocumentIds = [DocumentId],
            RunUserId = CallerSystemUserId,
            AnalysisId = analysisId,
        };

        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in engine.ExecuteAsync(request, CallerContext(), CancellationToken.None))
        {
            onEvent?.Invoke(evt);
            events.Add(evt);
        }

        return events;
    }

    private PlaybookOrchestrationService NewEngine(ILogger<PlaybookOrchestrationService>? logger = null) =>
        new(
            _nodes.Object,
            _executors.Object,
            _scopes.Object,
            NewWrapper(),
            _insightsRouter.Object,
            new TemplateEngine(NullLogger<TemplateEngine>.Instance),
            logger ?? NullLogger<PlaybookOrchestrationService>.Instance,
            contextEventEmitter: null,
            runRecordStore: null,
            httpContextAccessor: new HttpContextAccessor { HttpContext = CallerContext() });

    /// <summary>The caller's request context: its services are the run scope's (the analysis persistence the save uses).</summary>
    private DefaultHttpContext CallerContext() => new()
    {
        TraceIdentifier = "trace-task-037a",
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", CallerOid)], "TestAuth")),
        RequestServices = new ServiceCollection().AddSingleton(NewPersistence()).BuildServiceProvider(),
    };

    /// <summary>The run save's writer when a test replaces the double (the REAL WorkingDocumentService over a failing Dataverse seam).</summary>
    private IWorkingDocumentService? _saveWriter;

    private AnalysisResultPersistence NewPersistence() => new(
        _dataverse.Object,
        _dataverse.Object,
        _saveWriter ?? _workingDocument.Object,
        new Mock<IStorageRetryPolicy>().Object,
        new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
        NullLogger<AnalysisResultPersistence>.Instance);

    /// <summary>The real Analysis wrapper, the engine's legacy executor.</summary>
    private AnalysisOrchestrationService NewWrapper()
    {
        var tenantCache = new Mock<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>();
        var accessor = new Mock<IHttpContextAccessor>();
        var options = Options.Create(new AnalysisOptions { Enabled = true, MaxChatHistoryMessages = 10 });

        return new AnalysisOrchestrationService(
            new Mock<IOpenAiClient>().Object,
            _scopes.Object,
            new Mock<IAnalysisContextBuilder>().Object,
            _playbooks.Object,
            new ServiceCollection().AddSingleton(_toolHandlers.Object).BuildServiceProvider(),
            _nodes.Object,
            new AnalysisDocumentLoader(
                _dataverse.Object,
                _dataverse.Object,
                new Mock<ISpeFileOperations>().Object,
                TestRecordContainerResolver.ForBusinessUnitContainers(c => true),
                new Mock<ITextExtractor>().Object,
                tenantCache.Object,
                accessor.Object,
                NullLogger<AnalysisDocumentLoader>.Instance),
            new AnalysisRagProcessor(
                new Mock<IRagService>().Object,
                new RagQueryBuilder(),
                tenantCache.Object,
                accessor.Object,
                PermitAllRetrievalAccessTrim.Instance,
                options,
                NullLogger<AnalysisRagProcessor>.Instance),
            NewPersistence(),
            NullLogger<AnalysisOrchestrationService>.Instance);
    }

    private void OneDeliverNode(string text, int tokensIn, int tokensOut)
    {
        var node = Node("Deliver");
        _nodes.Setup(x => x.GetNodesAsync(PlaybookId, It.IsAny<CancellationToken>())).ReturnsAsync([node]);
        SetUpAction(node);
        _executor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NodeExecutionContext ctx, CancellationToken _) => new NodeOutput
            {
                NodeId = ctx.Node.Id,
                OutputVariable = ctx.Node.OutputVariable,
                Success = true,
                TextContent = text,
                IsDeliverOutput = true,
                Metrics = new NodeExecutionMetrics { TokensIn = tokensIn, TokensOut = tokensOut },
            });
    }

    /// <summary>A playbook with no nodes (legacy mode) and one tool whose handler returns <paramref name="summary"/>.</summary>
    private void LegacyPlaybookWithOneTool(string summary)
    {
        var tool = new AnalysisTool { Id = Guid.NewGuid(), Name = "Risk scan", Type = ToolType.Custom };
        _nodes.Setup(x => x.GetNodesAsync(PlaybookId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _playbooks.Setup(x => x.GetPlaybookAsync(PlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse { Id = PlaybookId, Name = "Risk Scan" });
        _dataverse.Setup(x => x.GetDocumentAsync(DocumentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity { Id = DocumentId.ToString(), Name = "NDA.pdf", Status = DocumentStatus.Active });
        _scopes.Setup(x => x.ResolvePlaybookScopesAsync(PlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes([], [], [tool]));

        var handler = new Mock<IToolHandler>();
        handler.SetupGet(h => h.HandlerId).Returns("RiskScanHandler");
        handler.Setup(h => h.Validate(It.IsAny<ToolExecutionContext>(), tool)).Returns(ToolValidationResult.Success());
        handler.Setup(h => h.ExecuteAsync(It.IsAny<ToolExecutionContext>(), tool, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("RiskScanHandler", tool.Id, tool.Name, new { level = "low" }, summary));
        _toolHandlers.Setup(r => r.GetHandlersByType(tool.Type)).Returns([handler.Object]);
    }

    private void SetUpAction(PlaybookNodeDto node)
    {
        _scopes.Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new ResolvedScopes([], [], []));
        _scopes.Setup(x => x.GetActionAsync(node.ActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = node.ActionId, Name = node.Name, SystemPrompt = "Test." });
    }

    private static PlaybookNodeDto Node(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ActionId = Guid.NewGuid(),
        OutputVariable = name.ToLowerInvariant(),
        ExecutionOrder = 1,
        DependsOn = [],
        IsActive = true,
        NodeType = NodeType.AIAnalysis,
        SprkExecutortype = ExecutorType.AiAnalysis,
    };

    private sealed class CapturingLogger : ILogger<PlaybookOrchestrationService>, ILogger<WorkingDocumentService>
    {
        private readonly Lock _gate = new();
        private readonly List<string> _lines = new();

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_gate)
                {
                    return _lines.ToList();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception.Message));
            }
        }
    }
}
