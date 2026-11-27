// spaarkeai-mcp-bff-api-r2 task 005 (FR-13) — durable playbook run status.
//
// The REAL PlaybookOrchestrationService runs a playbook and writes through the REAL PlaybookRunRecordStore; only the
// Dataverse seam (IGenericEntityService) is doubled, by an in-memory row store that interprets the store's queries.
// The node executor, node/scope readers and the Insights router are the run's other module boundaries and are mocked
// as in PlaybookOrchestrationServiceTests. A "restart" is a NEW service instance over the same rows (empty _activeRuns).
//
// Scope beyond the POML's named cases (task-005 notes): the owner-scope tests prove the read fallback keeps the route
// ledger's CallerScopedOnly property; the System-run test pins the decision that only Person runs are recorded.

using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PlaybookRuns;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.PlaybookRuns;

/// <summary>
/// FR-13: a playbook run writes <c>sprk_playbookrun</c> on start and completion and one <c>sprk_playbooknoderun</c> per
/// node; a fresh service instance (simulated restart) answers status and detail from those rows for the run's starter;
/// a write failure never changes the run.
/// </summary>
/// <remarks>KEEP data-mutation path (ADR-038): the tests assert the rows the run writes, or that a failed write leaves the run unchanged.</remarks>
public sealed class PlaybookRunPersistenceTests
{
    private const string CallerOid = "0a5e0005-0000-4000-8000-000000000005";
    private const string OtherOid = "0b5e0005-0000-4000-8000-000000000006";
    private const string TraceId = "trace-task-005";

    private static readonly Guid CallerSystemUserId = Guid.Parse("5a5e0005-0000-4000-8000-000000000005");
    private static readonly Guid MatterId = Guid.Parse("3a77e705-0000-4000-8000-000000000005");
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly InMemoryDataverse _dataverse = new();
    private readonly Mock<INodeService> _nodeService = new();
    private readonly Mock<INodeExecutorRegistry> _executorRegistry = new();
    private readonly Mock<IScopeResolverService> _scopeResolver = new();
    private readonly Mock<IInsightsActionRouter> _insightsRouter = new();
    private readonly Mock<INodeExecutor> _executor = new();
    private readonly Guid _playbookId = Guid.NewGuid();

    public PlaybookRunPersistenceTests()
    {
        _insightsRouter
            .Setup(r => r.ResolveLayer1ActionAsync(It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, AnalysisAction action, CancellationToken _) => action);
        _insightsRouter
            .Setup(r => r.ResolveLayer2ActionAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, string? __, AnalysisAction action, CancellationToken _) => InsightsLayer2RoutingResult.PassThrough(action));
        _executor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>())).Returns(NodeValidationResult.Success());
        _executorRegistry.Setup(x => x.GetExecutor(ExecutorType.AiAnalysis)).Returns(_executor.Object);

        // The caller's systemuser row: sprk_triggeredby → azureactivedirectoryobjectid rehydrates StartedByOid.
        _dataverse.Seed(new Entity("systemuser", CallerSystemUserId) { ["azureactivedirectoryobjectid"] = Guid.Parse(CallerOid) });
    }

    // ── Writes ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PersonRun_WritesRunRowAtStartAndCompletion_AndOneNodeRowPerNode()
    {
        var (first, second) = TwoSequentialNodes();
        var service = NewService(_dataverse);

        var runId = await RunAsync(service);

        var runWrites = _dataverse.Writes.Where(e => e.LogicalName == "sprk_playbookrun").ToList();
        runWrites.Should().HaveCount(2, "one write at start, one at completion");
        var created = runWrites[0];
        created.Id.Should().Be(runId);
        created.GetAttributeValue<OptionSetValue>("sprk_status").Value.Should().Be(1, "the row is created at start, Running");
        created.GetAttributeValue<EntityReference>("sprk_triggeredby").Id.Should().Be(CallerSystemUserId);
        created.GetAttributeValue<EntityReference>("sprk_playbookid").Id.Should().Be(_playbookId);
        created.GetAttributeValue<string>("sprk_correlationid").Should().Be(TraceId);
        created.GetAttributeValue<string>("sprk_regardingentitytype").Should().Be("sprk_matter");
        created.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(MatterId.ToString("D"));
        created.GetAttributeValue<OptionSetValue>("sprk_runkind").Value.Should().Be(100000000, "the node engine's rows are Engine runs");
        created.Contains("sprk_analysisactionid").Should().BeFalse("the engine path sets none of the prompted/coded references");
        created.Contains("sprk_playbookconsumerid").Should().BeFalse();
        created.Contains("sprk_parentrunid").Should().BeFalse();
        created.Contains("sprk_completedon").Should().BeFalse();

        var row = _dataverse.Row("sprk_playbookrun", runId);
        row.GetAttributeValue<OptionSetValue>("sprk_status").Value.Should().Be(2, "completion updates the row to Completed");
        row.GetAttributeValue<DateTime?>("sprk_completedon").Should().NotBeNull();
        row.GetAttributeValue<string>("sprk_summary").Should().Be("Completed. Nodes: 2 completed, 0 failed, 0 skipped. Tokens: 20 in, 10 out.");
        row.Contains("sprk_errorcode").Should().BeFalse("only a failed run carries an error code");

        var nodes = _dataverse.Rows("sprk_playbooknoderun").OrderBy(n => n.GetAttributeValue<int>("sprk_sequence")).ToList();
        nodes.Select(n => n.GetAttributeValue<EntityReference>("sprk_playbooknodeid").Id).Should().Equal(first.Id, second.Id);
        nodes.Select(n => n.GetAttributeValue<int>("sprk_sequence")).Should().Equal(1, 2);
        nodes.Should().OnlyContain(n =>
            n.GetAttributeValue<EntityReference>("sprk_playbookrunid").Id == runId
            && n.GetAttributeValue<OptionSetValue>("sprk_status").Value == 2
            && n.GetAttributeValue<DateTime>("sprk_startedon") <= n.GetAttributeValue<DateTime>("sprk_completedon")
            && n.GetAttributeValue<decimal?>("sprk_confidence") == 0.87m);
    }

    [Fact]
    public async Task ExecuteAsync_NodeFails_RunRowIsFailedWithErrorCode_AndTheNodeRowIsFailed()
    {
        var node = Node("Extract");
        _nodeService.Setup(x => x.GetNodesAsync(_playbookId, It.IsAny<CancellationToken>())).ReturnsAsync([node]);
        SetUpAction(node);
        _executor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NodeOutput.Error(node.Id, node.OutputVariable, "model refused", NodeErrorCodes.ModelError));

        var runId = await RunAsync(NewService(_dataverse));

        var row = _dataverse.Row("sprk_playbookrun", runId);
        row.GetAttributeValue<OptionSetValue>("sprk_status").Value.Should().Be(3);
        row.GetAttributeValue<string>("sprk_errorcode").Should().Be("playbook_node_failed");
        row.GetAttributeValue<string>("sprk_errormessage").Should().Be("Node 'Extract' failed: model refused");
        row.GetAttributeValue<DateTime?>("sprk_completedon").Should().NotBeNull();

        var nodeRow = _dataverse.Rows("sprk_playbooknoderun").Single();
        nodeRow.GetAttributeValue<OptionSetValue>("sprk_status").Value.Should().Be(3);
        nodeRow.GetAttributeValue<string>("sprk_errormessage").Should().Be("model refused");
    }

    // ── Task 037a (D-037-engine): a run linked to its analysis ───────────────────────────────────────────────────

    [Fact(DisplayName = "037a regarding: a run whose scope is linked to an analysis is regarding sprk_analysis on every write, even with a matterId parameter (the analysis wins)")]
    public async Task ExecuteAsync_LinkedToAnalysis_RegardingIsTheAnalysis_OverTheRecordParameter()
    {
        TwoSequentialNodes();
        var analysisId = Guid.Parse("a7a1a5e5-0037-4037-8037-000000000037");
        var service = NewService(_dataverse, configureStore: store => store.LinkRunsToAnalysis(analysisId));

        var runId = await RunAsync(service); // the request carries matterId = MatterId

        var runWrites = _dataverse.Writes.Where(e => e.LogicalName == "sprk_playbookrun").ToList();
        runWrites.Should().HaveCount(2);
        runWrites.Should().OnlyContain(w =>
            w.GetAttributeValue<string>("sprk_regardingentitytype") == "sprk_analysis"
            && w.GetAttributeValue<string>("sprk_regardingrecordid") == analysisId.ToString("D"));
        var row = _dataverse.Row("sprk_playbookrun", runId);
        row.GetAttributeValue<string>("sprk_regardingentitytype").Should().Be("sprk_analysis");
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(analysisId.ToString("D"));
    }

    [Fact(DisplayName = "037a regarding: a store with no link writes the run's own regarding, and none when the run has none (unchanged)")]
    public async Task WriteRunAsync_NoLink_WritesTheRunsOwnRegarding()
    {
        var store = new PlaybookRunRecordStore(_dataverse);
        store.LinkedAnalysis.Should().BeNull();
        var withRecord = Guid.NewGuid();
        var withoutRecord = Guid.NewGuid();

        await store.WriteRunAsync(Record(withRecord) with { Regarding = new PlaybookRunRegarding("sprk_matter", MatterId) }, CancellationToken.None);
        await store.WriteRunAsync(Record(withoutRecord), CancellationToken.None);

        _dataverse.Row("sprk_playbookrun", withRecord).GetAttributeValue<string>("sprk_regardingentitytype").Should().Be("sprk_matter");
        _dataverse.Row("sprk_playbookrun", withRecord).GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(MatterId.ToString("D"));
        _dataverse.Row("sprk_playbookrun", withoutRecord).Contains("sprk_regardingentitytype").Should().BeFalse();
        _dataverse.Row("sprk_playbookrun", withoutRecord).Contains("sprk_regardingrecordid").Should().BeFalse();
    }

    [Fact(DisplayName = "037a regarding: a linked store writes sprk_analysis for a run with no regarding too; the link is set once (same id again is a no-op, another id or empty throws)")]
    public async Task LinkRunsToAnalysis_SetOnce()
    {
        var store = new PlaybookRunRecordStore(_dataverse);
        var analysisId = Guid.NewGuid();
        store.LinkRunsToAnalysis(analysisId);
        store.LinkRunsToAnalysis(analysisId);

        var runId = Guid.NewGuid();
        await store.WriteRunAsync(Record(runId), CancellationToken.None);

        _dataverse.Row("sprk_playbookrun", runId).GetAttributeValue<string>("sprk_regardingentitytype").Should().Be("sprk_analysis");
        _dataverse.Row("sprk_playbookrun", runId).GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(analysisId.ToString("D"));
        store.Invoking(s => s.LinkRunsToAnalysis(Guid.NewGuid())).Should().Throw<InvalidOperationException>();
        new PlaybookRunRecordStore(_dataverse).Invoking(s => s.LinkRunsToAnalysis(Guid.Empty)).Should().Throw<ArgumentException>();
        store.LinkedAnalysis.Should().Be(analysisId);
    }

    private PlaybookRunRecord Record(Guid runId) => new()
    {
        RunId = runId,
        PlaybookId = _playbookId,
        State = PlaybookRunState.Running,
        StartedAt = new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero),
        DocumentIds = [],
    };

    [Fact]
    public async Task ExecuteAppOnlyAsync_SystemRun_WritesNoRows()
    {
        var node = Node("Notify");
        _nodeService.Setup(x => x.GetNodesAsync(_playbookId, It.IsAny<CancellationToken>())).ReturnsAsync([node]);
        SetUpAction(node);
        SucceedWith(10, 5);
        var service = NewService(_dataverse);
        var request = new PlaybookRunRequest
        {
            PlaybookId = _playbookId,
            DocumentIds = [],
            StartedBy = PlaybookRunStartedBy.System,
            Parameters = new Dictionary<string, string> { ["userId"] = CallerSystemUserId.ToString() },
        };

        await foreach (var _ in service.ExecuteAppOnlyAsync(request, "tenant-005", CancellationToken.None))
        {
        }

        _dataverse.WriteAttempts.Should().Be(0, "a System run has no starter who could read its record");
    }

    // ── Reads after a restart ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRunStatusAndDetail_AfterRestart_ReturnTheStoredState_SerializedLikeTheInMemoryRun()
    {
        TwoSequentialNodes();
        var original = NewService(_dataverse);
        var runId = await RunAsync(original);
        var inMemoryStatus = await original.GetRunStatusAsync(runId, CancellationToken.None);
        var inMemoryDetail = await original.GetRunDetailAsync(runId, CancellationToken.None);

        var restarted = NewService(_dataverse, reader: CallerOid);
        var storedStatus = await restarted.GetRunStatusAsync(runId, CancellationToken.None);
        var storedDetail = await restarted.GetRunDetailAsync(runId, CancellationToken.None);

        storedStatus.Should().NotBeNull();
        storedDetail.Should().NotBeNull();
        storedStatus!.State.Should().Be(PlaybookRunState.Completed);
        storedDetail!.NodeDetails.Should().HaveCount(2);

        // The documented differences (task-005 notes §4), asserted explicitly:
        // (1) ADR-015: node output is never stored, so a stored run has no outputs.
        storedStatus.Outputs.Should().BeNull();
        inMemoryStatus!.Outputs.Should().NotBeNull();
        // (2) Dataverse keeps DateTime to the second, so the run's timestamps are the in-memory ones truncated, and the
        //     durations derived from them are computed from the truncated values.
        storedStatus.StartedAt.Should().Be(ToSecond(inMemoryStatus.StartedAt));
        storedStatus.CompletedAt.Should().Be(ToSecond(inMemoryStatus.CompletedAt!.Value));
        storedStatus.Metrics!.Duration.Should().Be(storedStatus.CompletedAt!.Value - storedStatus.StartedAt);
        storedDetail.DurationMs.Should().Be((long)(storedDetail.CompletedAt!.Value - storedDetail.StartedAt).TotalMilliseconds);

        // Every other byte of the route bodies is the same (node durations, tokens and confidence are stored exactly).
        Json(storedStatus, without: StoredTimeFields.Append("outputs"))
            .Should().Be(Json(inMemoryStatus, without: StoredTimeFields.Append("outputs")));
        Json(storedDetail, without: StoredTimeFields, orderNodesBy: "outputVariable")
            .Should().Be(Json(inMemoryDetail!, without: StoredTimeFields, orderNodesBy: "outputVariable"));
        storedDetail.NodeDetails.Should().OnlyContain(n => n.Confidence == 0.87 && n.DurationMs == 1234);
    }

    [Fact]
    public async Task GetRunStatus_StoredRun_RehydratesStartedByOid_ThatNeverReachesTheResponseBody()
    {
        TwoSequentialNodes();
        var runId = await RunAsync(NewService(_dataverse));

        var stored = await NewService(_dataverse, reader: CallerOid).GetRunStatusAsync(runId, CancellationToken.None);

        stored!.StartedByOid.Should().Be(CallerOid);
        var body = JsonSerializer.Serialize(stored, WebJson);
        body.Should().NotContain(CallerOid).And.NotContainEquivalentOf("startedByOid");
    }

    [Fact]
    public async Task GetRunStatusAndDetail_UnknownRunId_ReturnNull()
    {
        var service = NewService(_dataverse, reader: CallerOid);

        (await service.GetRunStatusAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
        (await service.GetRunDetailAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull(
            "null is what the run routes answer with 404 (PlaybookRunEndpoints GetRunStatus / GetRunDetail)");
    }

    [Theory]
    [InlineData(OtherOid)]
    [InlineData(null)]
    public async Task GetRunStatusAndDetail_StoredRunOfAnotherCaller_ReturnNull(string? readerOid)
    {
        TwoSequentialNodes();
        var runId = await RunAsync(NewService(_dataverse));

        var otherReader = NewService(_dataverse, reader: readerOid, readerHasHttpContext: readerOid is not null);

        var nodeQueriesBefore = _dataverse.QueriedEntities.Count(e => e == "sprk_playbooknoderun");

        (await otherReader.GetRunStatusAsync(runId, CancellationToken.None)).Should().BeNull(
            "a stored run is returned only to the caller who started it (the route ledger's CallerScopedOnly basis)");
        (await otherReader.GetRunDetailAsync(runId, CancellationToken.None)).Should().BeNull();
        _dataverse.QueriedEntities.Count(e => e == "sprk_playbooknoderun").Should().Be(
            nodeQueriesBefore, "the owner check runs before the node rows are read");
    }

    [Fact]
    public async Task GetRunStatusAndDetail_StoreReadFails_Throw_SoTheRouteAnswers500NotA404()
    {
        var failingReads = new InMemoryDataverse { FailReads = true };
        var service = NewService(failingReads, reader: CallerOid);

        var status = () => service.GetRunStatusAsync(Guid.NewGuid(), CancellationToken.None);
        var detail = () => service.GetRunDetailAsync(Guid.NewGuid(), CancellationToken.None);

        (await status.Should().ThrowAsync<InvalidOperationException>()).WithMessage("playbook_run_read_failed*");
        (await detail.Should().ThrowAsync<InvalidOperationException>()).WithMessage("playbook_run_read_failed*");
    }

    // ── Other run kinds (D-run-record-decided): the store is the one writer; the engine's reads skip them ──────

    [Fact]
    public async Task Store_PromptedRun_WritesKindAndActionBindingParent_WithNoPlaybook_AndANodeRowWithoutANode()
    {
        var store = new PlaybookRunRecordStore(_dataverse);
        var runId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var parentRunId = Guid.NewGuid();

        await store.WriteRunAsync(
            new PlaybookRunRecord
            {
                RunId = runId,
                Kind = PlaybookRunKind.Prompted,
                State = PlaybookRunState.Completed,
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
                CompletedAt = DateTimeOffset.UtcNow,
                TriggeredBy = CallerSystemUserId,
                ActionId = actionId,
                BindingId = bindingId,
                ParentRunId = parentRunId,
            },
            CancellationToken.None);
        await store.CreateNodeRunAsync(
            new PlaybookNodeRunRecord
            {
                RunId = runId,
                Name = "action",
                Outcome = PlaybookNodeRunOutcome.Completed,
                Sequence = 1,
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
                CompletedAt = DateTimeOffset.UtcNow,
                ActionId = actionId,
            },
            CancellationToken.None);

        var row = _dataverse.Row("sprk_playbookrun", runId);
        row.GetAttributeValue<OptionSetValue>("sprk_runkind").Value.Should().Be(100000001);
        row.Contains("sprk_playbookid").Should().BeFalse("a prompted run has no playbook (sprk_playbookid is optional)");
        row.GetAttributeValue<EntityReference>("sprk_analysisactionid").Should().Match<EntityReference>(
            r => r.Id == actionId && r.LogicalName == "sprk_analysisaction");
        row.GetAttributeValue<EntityReference>("sprk_playbookconsumerid").Should().Match<EntityReference>(
            r => r.Id == bindingId && r.LogicalName == "sprk_playbookconsumer");
        row.GetAttributeValue<EntityReference>("sprk_parentrunid").Should().Match<EntityReference>(
            r => r.Id == parentRunId && r.LogicalName == "sprk_playbookrun");

        var node = _dataverse.Rows("sprk_playbooknoderun").Single();
        node.Contains("sprk_playbooknodeid").Should().BeFalse("an Action run's row has no playbook node (sprk_playbooknodeid is optional)");
        node.GetAttributeValue<EntityReference>("sprk_analysisactionid").Id.Should().Be(actionId);
    }

    [Theory]
    [InlineData(PlaybookRunKind.Prompted)]
    [InlineData(PlaybookRunKind.Coded)]
    public async Task GetRunStatusAndDetail_StoredRunWithNoPlaybookId_ReturnNullWithoutThrowing(PlaybookRunKind kind)
    {
        // A prompted or coded run written by another project's code: no playbook id, so no PlaybookRunStatus shape.
        // The playbook run read skips it (the routes answer it like an unknown run id) rather than returning a
        // status with an empty playbook id, and reading it never throws.
        var runId = Guid.NewGuid();
        await new PlaybookRunRecordStore(_dataverse).WriteRunAsync(
            new PlaybookRunRecord
            {
                RunId = runId,
                Kind = kind,
                State = PlaybookRunState.Completed,
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
                CompletedAt = DateTimeOffset.UtcNow,
                TriggeredBy = CallerSystemUserId,
                ActionId = Guid.NewGuid(),
            },
            CancellationToken.None);
        var service = NewService(_dataverse, reader: CallerOid);
        var queriesBefore = _dataverse.QueriedEntities.Count;

        var status = await service.GetRunStatusAsync(runId, CancellationToken.None);
        var detail = await service.GetRunDetailAsync(runId, CancellationToken.None);

        status.Should().BeNull();
        detail.Should().BeNull();
        _dataverse.QueriedEntities.Skip(queriesBefore).Should().OnlyContain(
            e => e == "sprk_playbookrun", "the playbook-id check runs before the owner lookup and the node query");
    }

    // ── Fail-safe ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StoreThrowsOnEveryWrite_RunHasTheSameOutcomeAndEvents_AndLogsPersistFailed()
    {
        TwoSequentialNodes();
        var baseline = await RunCollectingAsync(NewService(dataverse: null));

        var failing = new InMemoryDataverse { FailWrites = true };
        var logger = new CapturingLogger();
        var service = NewService(failing, logger: logger);
        var withFailingStore = await RunCollectingAsync(service);

        withFailingStore.Events.Select(e => (e.Type, e.NodeId)).Should().Equal(baseline.Events.Select(e => (e.Type, e.NodeId)));
        withFailingStore.Events.Last().Type.Should().Be(PlaybookEventType.RunCompleted);
        Comparable(withFailingStore.Events.Last().Metrics).Should().Be(Comparable(baseline.Events.Last().Metrics));
        (await service.GetRunStatusAsync(withFailingStore.RunId, CancellationToken.None))!.State.Should().Be(PlaybookRunState.Completed);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && Equals(e.Properties.GetValueOrDefault("Outcome"), "playbook_run_persist_failed")
            && Equals(e.Properties.GetValueOrDefault("RunId"), withFailingStore.RunId));
        failing.WriteAttempts.Should().Be(
            2, "the start write fails, so the node rows are skipped; the final upsert is still attempted, once");
    }

    [Fact]
    public async Task ExecuteAsync_StartWriteCommitsButNeverReturns_RunIsBoundedAndUnchanged_AndTheFinalUpsertLands()
    {
        TwoSequentialNodes();
        var baseline = await RunCollectingAsync(NewService(dataverse: null));

        // Dataverse commits the start write but its call never completes (a hung client that ignores its token).
        var hanging = new InMemoryDataverse { HangAfterFirstRunWrite = true };
        var logger = new CapturingLogger();
        var service = NewService(hanging, logger: logger);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var run = await RunCollectingAsync(service);
        clock.Stop();

        run.Events.Select(e => (e.Type, e.NodeId)).Should().Equal(baseline.Events.Select(e => (e.Type, e.NodeId)));
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20), "each record call is bounded at 5 s even if Dataverse ignores the token");
        logger.Entries.Should().Contain(e =>
            Equals(e.Properties.GetValueOrDefault("Outcome"), "playbook_run_persist_failed")
            && Equals(e.Properties.GetValueOrDefault("ErrorType"), nameof(TimeoutException)));

        var row = hanging.Row("sprk_playbookrun", run.RunId);
        row.GetAttributeValue<OptionSetValue>("sprk_status").Value.Should().Be(
            2, "the final write is an upsert, so it lands on the row the timed-out start write committed");
        row.GetAttributeValue<DateTime?>("sprk_completedon").Should().NotBeNull();
        hanging.Rows("sprk_playbooknoderun").Should().BeEmpty("node rows are skipped once the start write is not known to have landed");
    }

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────────────────

    private PlaybookOrchestrationService NewService(
        InMemoryDataverse? dataverse,
        string? reader = null,
        bool readerHasHttpContext = true,
        ILogger<PlaybookOrchestrationService>? logger = null,
        Action<PlaybookRunRecordStore>? configureStore = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = readerHasHttpContext ? CallerContext(reader ?? CallerOid) : null };

        return new PlaybookOrchestrationService(
            _nodeService.Object,
            _executorRegistry.Object,
            _scopeResolver.Object,
            Mock.Of<IAnalysisOrchestrationService>(),
            _insightsRouter.Object,
            new TemplateEngine(NullLogger<TemplateEngine>.Instance),
            logger ?? NullLogger<PlaybookOrchestrationService>.Instance,
            contextEventEmitter: null,
            runRecordStore: dataverse is null ? null : NewStore(dataverse, configureStore),
            httpContextAccessor: accessor);
    }

    private static PlaybookRunRecordStore NewStore(InMemoryDataverse dataverse, Action<PlaybookRunRecordStore>? configure)
    {
        var store = new PlaybookRunRecordStore(dataverse);
        configure?.Invoke(store);
        return store;
    }

    private async Task<Guid> RunAsync(PlaybookOrchestrationService service) => (await RunCollectingAsync(service)).RunId;

    private async Task<(Guid RunId, List<PlaybookStreamEvent> Events)> RunCollectingAsync(PlaybookOrchestrationService service)
    {
        var request = new PlaybookRunRequest
        {
            PlaybookId = _playbookId,
            DocumentIds = [Guid.Parse("d0c00005-0000-4000-8000-000000000005")],
            RunUserId = CallerSystemUserId,
            Parameters = new Dictionary<string, string> { ["matterId"] = MatterId.ToString() },
        };

        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in service.ExecuteAsync(request, CallerContext(CallerOid), CancellationToken.None))
        {
            events.Add(evt);
        }

        return (events.First(e => e.Type == PlaybookEventType.RunStarted).RunId, events);
    }

    private static DefaultHttpContext CallerContext(string oid) => new()
    {
        TraceIdentifier = TraceId,
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", oid)], "TestAuth")),
    };

    private (PlaybookNodeDto First, PlaybookNodeDto Second) TwoSequentialNodes()
    {
        var first = Node("Extract");
        var second = Node("Summarize", dependsOn: first.Id);
        _nodeService.Setup(x => x.GetNodesAsync(_playbookId, It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);
        SetUpAction(first);
        SetUpAction(second);
        SucceedWith(10, 5);
        return (first, second);
    }

    private void SucceedWith(int tokensIn, int tokensOut)
    {
        var started = new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
        _executor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NodeExecutionContext ctx, CancellationToken _) => NodeOutput.Ok(
                ctx.Node.Id,
                ctx.Node.OutputVariable,
                data: null,
                confidence: 0.87,
                metrics: new NodeExecutionMetrics
                {
                    StartedAt = started,
                    CompletedAt = started.AddMilliseconds(1234),
                    TokensIn = tokensIn,
                    TokensOut = tokensOut,
                }));
    }

    private void SetUpAction(PlaybookNodeDto node)
    {
        _scopeResolver.Setup(x => x.ResolveNodeScopesAsync(node.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new ResolvedScopes([], [], []));
        _scopeResolver.Setup(x => x.GetActionAsync(node.ActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = node.ActionId, Name = node.Name, SystemPrompt = "Test." });
    }

    private static PlaybookNodeDto Node(string name, params Guid[] dependsOn) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ActionId = Guid.NewGuid(),
        OutputVariable = name.ToLowerInvariant(),
        ExecutionOrder = dependsOn.Length + 1,
        DependsOn = dependsOn,
        IsActive = true,
        NodeType = NodeType.AIAnalysis,
        SprkExecutortype = ExecutorType.AiAnalysis,
    };

    /// <summary>Run-level fields a stored run holds to the second (Dataverse DateTime) or derives from those.</summary>
    private static readonly string[] StoredTimeFields = ["startedAt", "completedAt", "durationMs", "metrics.duration"];

    private static DateTimeOffset ToSecond(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), value.Offset);

    /// <summary>
    /// The route body (web JSON defaults, as <c>Results.Ok</c>), with properties removed (a dotted path reaches one level
    /// into an object) or node details ordered.
    /// </summary>
    private static string Json(object value, IEnumerable<string>? without = null, string? orderNodesBy = null)
    {
        var node = JsonSerializer.SerializeToNode(value, value.GetType(), WebJson)!.AsObject();
        foreach (var path in without ?? [])
        {
            var parts = path.Split('.');
            if (parts.Length == 1)
            {
                node.Remove(parts[0]);
            }
            else if (node[parts[0]] is JsonObject inner)
            {
                inner.Remove(parts[1]);
            }
        }

        if (orderNodesBy is not null && node["nodeDetails"] is JsonArray details)
        {
            // In memory the details come in ConcurrentDictionary hash order; stored, in start order (task-005 notes §4).
            var ordered = details.Select(d => d!.DeepClone()).OrderBy(d => d![orderNodesBy]!.GetValue<string>()).ToArray();
            node["nodeDetails"] = new JsonArray(ordered);
        }

        return node.ToJsonString();
    }

    private static string Comparable(PlaybookRunMetrics? metrics) =>
        metrics is null ? "null" : JsonSerializer.Serialize(metrics with { Duration = TimeSpan.Zero }, WebJson);

    /// <summary>
    /// The Dataverse seam: an in-memory row store that interprets the store's <see cref="QueryExpression"/> conditions
    /// (Equal on a column, a lookup, or the primary key), honours a caller-set primary key on create and upserts on the
    /// primary key, as Dataverse does. Like Dataverse it keeps a DateTime to the second (task-005 notes §4).
    /// </summary>
    private sealed class InMemoryDataverse : IGenericEntityService
    {
        private readonly Dictionary<(string Entity, Guid Id), Entity> _rows = new();
        private readonly object _gate = new();
        private bool _runWriteSeen;

        public bool FailWrites { get; init; }

        public bool FailReads { get; init; }

        /// <summary>The first <c>sprk_playbookrun</c> write commits, then its call never completes (ignores its token).</summary>
        public bool HangAfterFirstRunWrite { get; init; }

        public int WriteAttempts { get; private set; }

        /// <summary>Every successful write, as sent (creates and upserts), in order.</summary>
        public List<Entity> Writes { get; } = new();

        public List<string> QueriedEntities { get; } = new();

        public void Seed(Entity row) => _rows[(row.LogicalName, row.Id)] = row;

        public Entity Row(string entity, Guid id) => _rows[(entity, id)];

        public IReadOnlyList<Entity> Rows(string entity) => _rows.Values.Where(r => r.LogicalName == entity).ToList();

        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) =>
            Write(entity, upsert: false).ContinueWith(t => t.Result.Id, TaskScheduler.Default);

        public Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default) =>
            Write(entity, upsert: true);

        private Task<(Guid Id, bool Created)> Write(Entity entity, bool upsert)
        {
            lock (_gate)
            {
                WriteAttempts++;
                if (FailWrites)
                {
                    throw new InvalidOperationException("Dataverse unavailable (test).");
                }

                var id = entity.Id != Guid.Empty ? entity.Id : Guid.NewGuid();
                var created = !_rows.TryGetValue((entity.LogicalName, id), out var row);
                if (!created && !upsert)
                {
                    throw new InvalidOperationException("Duplicate key (test).");
                }

                row ??= new Entity(entity.LogicalName, id);
                var snapshot = new Entity(entity.LogicalName, id);
                foreach (var attribute in entity.Attributes)
                {
                    var stored = attribute.Value is DateTime dateTime
                        ? new DateTime(dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerSecond), dateTime.Kind)
                        : attribute.Value;
                    row[attribute.Key] = stored;
                    snapshot[attribute.Key] = stored;
                }

                _rows[(entity.LogicalName, id)] = row;
                Writes.Add(snapshot);

                if (HangAfterFirstRunWrite && entity.LogicalName == "sprk_playbookrun" && !_runWriteSeen)
                {
                    _runWriteSeen = true;
                    return new TaskCompletionSource<(Guid, bool)>().Task;
                }

                _runWriteSeen |= entity.LogicalName == "sprk_playbookrun";
                return Task.FromResult((id, created));
            }
        }

        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
        {
            lock (_gate)
            {
                QueriedEntities.Add(query.EntityName);
                if (FailReads)
                {
                    throw new InvalidOperationException("Dataverse unavailable (test).");
                }

                var matches = _rows.Values
                    .Where(r => r.LogicalName == query.EntityName)
                    .Where(r => query.Criteria.Conditions.All(c => Matches(r, query.EntityName, c)))
                    .ToList();
                if (query.TopCount is { } top)
                {
                    matches = matches.Take(top).ToList();
                }

                return Task.FromResult(new EntityCollection(matches));
            }
        }

        private static bool Matches(Entity row, string entityName, Microsoft.Xrm.Sdk.Query.ConditionExpression condition)
        {
            if (condition.Operator != ConditionOperator.Equal)
            {
                throw new NotSupportedException($"InMemoryDataverse does not model {condition.Operator}.");
            }

            var expected = condition.Values[0];
            if (condition.AttributeName == entityName + "id")
            {
                return Equals(row.Id, expected);
            }

            var actual = row.Contains(condition.AttributeName) ? row[condition.AttributeName] : null;
            return actual is EntityReference reference ? Equals(reference.Id, expected) : Equals(actual, expected);
        }

        // ── Not used by PlaybookRunRecordStore ──
        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateIfRowVersionMatchesAsync(string entityLogicalName, Guid id, string rowVersion, Dictionary<string, object> fields, CancellationToken ct = default) => throw new NotImplementedException();
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed record LogEntry(LogLevel Level, IReadOnlyDictionary<string, object?> Properties);

    private sealed class CapturingLogger : ILogger<PlaybookOrchestrationService>
    {
        private readonly object _gate = new();
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToList();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?>();
            lock (_gate)
            {
                _entries.Add(new LogEntry(logLevel, properties));
            }
        }
    }
}
