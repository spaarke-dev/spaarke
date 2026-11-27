using System.Text;
using System.Threading.Channels;

namespace Sprk.Bff.Api.Services.Ai;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 037a (owner decision D-037-engine, option B; ADR-039 Path C, owner-approved).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md). In the BFF, beside the two callers it
// serves (the Analysis wrapper and the playbook engine, both Services/Ai): it is the wrapper's node-branch save, MOVED here, not
// new work. No DI registration, package, endpoint, store or background work; AnalysisResultPersistence (already registered) does
// the writes.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: AnalysisOrchestrationService.ExecutePlaybookAsync's node branch (the only code that turns a node run's events into
//     sprk_analysis.sprk_workingdocument and the finalize); AnalysisResultPersistence (the writes).
//   Extension: the engine has to save a run that carries an AnalysisId exactly as the wrapper does (D-037-engine); copying the
//     wrapper's accumulation and status rules would fork them (F2), so they move here and both call them.
//   Cost of doing nothing: a Copilot run's Deliver Output reaches no record the caller can read (037-decisions §1.2, kind A).
// =====================================================================================================================

/// <summary>
/// What a node run's event stream says its result is, as the Analysis wrapper has always read it: the Deliver Output node's text (the
/// last one that completed with text), the streamed text of every node (the fallback when no Deliver Output node produced text), whether
/// a node failed, the run's token counts and whether the run failed. Feed it every <see cref="PlaybookStreamEvent"/> of the run, in order.
/// </summary>
/// <remarks>Safe for the engine's parallel node writes (one lock). Holds output text in memory for the run only; never logs it.</remarks>
public sealed class NodeRunResultCollector
{
    private readonly Lock _gate = new();
    private readonly StringBuilder _streamed = new();
    private string? _deliverOutput;
    private bool _hasFailedNodes;
    private bool _runFailed;
    private int _tokensIn;
    private int _tokensOut;

    /// <summary>Reads one event of the run.</summary>
    public void Observe(PlaybookStreamEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        lock (_gate)
        {
            // Accumulate streamed content for the working document fallback.
            if (evt.Type == PlaybookEventType.NodeProgress && evt.Content != null)
            {
                _streamed.Append(evt.Content);
            }

            // The Deliver Output node's rendered markdown. IsDeliverOutput, not "any text": a side-effect node's message (for example
            // "Updated sprk_document record" from an Update Record node) is not content.
            if (evt.Type == PlaybookEventType.NodeCompleted
                && evt.NodeOutput is { IsDeliverOutput: true } deliverOutput
                && !string.IsNullOrEmpty(deliverOutput.TextContent))
            {
                _deliverOutput = deliverOutput.TextContent;
            }

            if (evt.Type == PlaybookEventType.NodeFailed)
            {
                _hasFailedNodes = true;
            }

            if (evt.Type == PlaybookEventType.RunCompleted && evt.Metrics != null)
            {
                _tokensIn = evt.Metrics.TotalTokensIn;
                _tokensOut = evt.Metrics.TotalTokensOut;
            }

            if (evt.Type == PlaybookEventType.RunFailed)
            {
                _runFailed = true;
            }
        }
    }

    /// <summary>True once the run's <see cref="PlaybookEventType.RunFailed"/> event was observed.</summary>
    public bool RunFailed
    {
        get
        {
            lock (_gate)
            {
                return _runFailed;
            }
        }
    }

    /// <summary>The run's result as the save reads it (a snapshot).</summary>
    internal (string? DeliverOutput, string Streamed, bool HasFailedNodes, bool RunFailed, int TokensIn, int TokensOut) Snapshot()
    {
        lock (_gate)
        {
            return (_deliverOutput, _streamed.ToString(), _hasFailedNodes, _runFailed, _tokensIn, _tokensOut);
        }
    }
}

/// <summary>
/// What <see cref="AnalysisResultPersistence.SaveNodeRunAsync"/> saved: the content (the working document), the analysis status the
/// wrapper reports (<c>Completed</c>, <c>CompletedWithWarnings</c> or <c>Failed</c>), the partial-storage flag and message the wrapper's
/// done chunk carries, and the run's token counts. <see cref="ContentSaved"/> (task 037 review F-A) is <c>false</c> when the working
/// document write failed: the analysis was then not finalized.
/// </summary>
public sealed record NodeRunSaveOutcome(
    string Content,
    string Status,
    bool? PartialStorage,
    string? StorageMessage,
    int TokensIn,
    int TokensOut,
    bool ContentSaved = true)
{
    /// <summary>The status of a run that saved its text and finished (no node failed, or none with content lost).</summary>
    public const string CompletedStatus = "Completed";

    /// <summary>The status of a finished run in which a node failed, saved with the content that was produced.</summary>
    public const string CompletedWithWarningsStatus = "CompletedWithWarnings";

    /// <summary>The status of a run that failed, or that finished with no content.</summary>
    public const string FailedStatus = "Failed";

    /// <summary>The message the wrapper's done chunk carries for <see cref="CompletedWithWarningsStatus"/>.</summary>
    public const string PartialResultsMessage = "Some nodes failed during execution but partial results are available.";
}

/// <summary>
/// The engine's side of D-037-engine (b): a node run that carries an <c>AnalysisId</c> saves through
/// <see cref="AnalysisResultPersistence.SaveNodeRunAsync"/> once, when the run ends (its <see cref="PlaybookEventType.RunCompleted"/> or
/// <see cref="PlaybookEventType.RunFailed"/> event), BEFORE that event reaches the run's reader, so a reader that sees the run finished
/// can already read the saved analysis.
/// </summary>
internal static class NodeRunAnalysisSave
{
    /// <summary>Outcome code logged when the analysis save fails. The run is not affected.</summary>
    internal const string SaveFailedOutcome = "playbook_run_analysis_save_failed";

    /// <summary>The <c>ErrorType</c> logged with <see cref="SaveFailedOutcome"/> when the working document write reported a failure.</summary>
    internal const string WriteFailedErrorType = "WorkingDocumentWriteFailed";

    /// <summary>Outcome code logged when the run carries an AnalysisId but this host has no analysis persistence. Nothing is saved.</summary>
    internal const string SaveUnavailableOutcome = "playbook_run_analysis_save_unavailable";

    /// <summary>
    /// The writer the engine's node mode writes to: <paramref name="writer"/> itself when <paramref name="analysisId"/> is <c>null</c>
    /// (every run but a Copilot start, unchanged), else a writer that observes each event and saves at the run's end. The persistence
    /// is the run's own scope's (<paramref name="services"/>, the request services of the run's <c>HttpContext</c>).
    /// </summary>
    internal static ChannelWriter<PlaybookStreamEvent> SavingWriter(
        ChannelWriter<PlaybookStreamEvent> writer, Guid? analysisId, IServiceProvider? services, Guid runId, ILogger logger)
    {
        if (analysisId is not { } id || id == Guid.Empty)
        {
            return writer;
        }

        if (services?.GetService(typeof(AnalysisResultPersistence)) is not AnalysisResultPersistence persistence)
        {
            logger.LogWarning(
                "Playbook run has an analysis to save to, but no analysis persistence is available - Outcome: {Outcome}, RunId: {RunId}",
                SaveUnavailableOutcome, runId);
            return writer;
        }

        return new SavingRunWriter(writer, persistence, id, runId, logger);
    }

    /// <summary>
    /// Forwards every event to the run's channel. On the first terminal event (completed or failed) it saves first, then forwards.
    /// A failed save is logged and never changes the run (as the run record's writes). The engine writes only through
    /// <see cref="WriteAsync"/>; <see cref="TryWrite"/> observes and forwards but cannot wait for a save.
    /// </summary>
    private sealed class SavingRunWriter : ChannelWriter<PlaybookStreamEvent>
    {
        private readonly ChannelWriter<PlaybookStreamEvent> _inner;
        private readonly AnalysisResultPersistence _persistence;
        private readonly Guid _analysisId;
        private readonly Guid _runId;
        private readonly ILogger _logger;
        private readonly NodeRunResultCollector _run = new();
        private int _saved;

        public SavingRunWriter(
            ChannelWriter<PlaybookStreamEvent> inner, AnalysisResultPersistence persistence, Guid analysisId, Guid runId, ILogger logger)
        {
            _inner = inner;
            _persistence = persistence;
            _analysisId = analysisId;
            _runId = runId;
            _logger = logger;
        }

        public override bool TryWrite(PlaybookStreamEvent item)
        {
            _run.Observe(item);
            return _inner.TryWrite(item);
        }

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) =>
            _inner.WaitToWriteAsync(cancellationToken);

        public override bool TryComplete(Exception? error = null) => _inner.TryComplete(error);

        public override async ValueTask WriteAsync(PlaybookStreamEvent item, CancellationToken cancellationToken = default)
        {
            _run.Observe(item);
            if (item.Type is PlaybookEventType.RunCompleted or PlaybookEventType.RunFailed
                && Interlocked.Exchange(ref _saved, 1) == 0)
            {
                await SaveAsync(cancellationToken).ConfigureAwait(false);
            }

            await _inner.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }

        private async Task SaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                var saved = await _persistence.SaveNodeRunAsync(_analysisId, _run, cancellationToken).ConfigureAwait(false);
                if (!saved.ContentSaved)
                {
                    // Task 037 review F-A: the working document write failed (the analysis was not finalized); the run is unchanged.
                    _logger.LogWarning(
                        "Playbook run analysis save failed; the run continues - Outcome: {Outcome}, RunId: {RunId}, AnalysisId: {AnalysisId}, ErrorType: {ErrorType}",
                        SaveFailedOutcome, _runId, _analysisId, WriteFailedErrorType);
                }
            }
            catch (Exception ex)
            {
                // ADR-015: the exception type only (a Dataverse message may quote content).
                _logger.LogWarning(
                    "Playbook run analysis save failed; the run continues - Outcome: {Outcome}, RunId: {RunId}, AnalysisId: {AnalysisId}, ErrorType: {ErrorType}",
                    SaveFailedOutcome, _runId, _analysisId, ex.GetType().Name);
            }
        }
    }
}
