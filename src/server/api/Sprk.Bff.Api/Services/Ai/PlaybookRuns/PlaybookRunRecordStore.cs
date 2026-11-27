// spaarkeai-mcp-bff-api-r2 task 005 (FR-13) — durable playbook run status.

using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PlaybookRuns;

/// <summary>
/// Reads and writes the durable record of a playbook run: one <c>sprk_playbookrun</c> row per run and one
/// <c>sprk_playbooknoderun</c> row per node the run reached. This is the one writer for every AI run kind
/// (<see cref="PlaybookRunKind"/>: engine, prompted, coded; the owner's run-record decision, 2026-11-25), so one
/// component owns the row shape (ADR-002). <see cref="PlaybookOrchestrationService"/> writes the engine's runs on the
/// run's own execution path (start, each node, completion — no background work, ADR-052) and reads them back here when
/// a run is not in its in-memory map. Other kinds' callers belong to the projects that own those runs; they take the
/// same <see cref="PlaybookRunRecord"/> request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate class</b> (root CLAUDE.md §11): the orchestration service keeps ownership of run state; this type
/// only maps that state to the two existing tables and back. It goes through the app-only <see cref="IGenericEntityService"/>
/// (the BFF managed identity): the run record is the BFF's record of its own execution on organization-owned tables,
/// so a caller without create rights on them must not lose their run's status. The caller is recorded in
/// <c>sprk_triggeredby</c>. <see cref="IGenericEntityService"/> is the Dataverse seam tests double (ADR-038), so this
/// class needs no interface of its own (ADR-010).
/// </para>
/// <para>
/// <b>ADR-015.</b> Only identifiers, timings, counts, tokens, a node's numeric confidence score, the redacted error text
/// the REST run routes already return, and a summary built from counts are written. Document content, prompts, the
/// user's instructions and node output (text, structured data, tool results, warnings, previews) are never written.
/// </para>
/// <para>
/// Every method throws on failure. The caller decides what a failure means (for a write: log it and carry on).
/// Columns and choices: <c>projects/spaarkeai-mcp-bff-api-r2/notes/005-schema.md</c>.
/// </para>
/// </remarks>
public sealed class PlaybookRunRecordStore
{
    internal const string RunEntity = "sprk_playbookrun";
    internal const string RunIdColumn = "sprk_playbookrunid";
    internal const string NodeRunEntity = "sprk_playbooknoderun";

    internal const string NameColumn = "sprk_name";
    internal const string StatusColumn = "sprk_status";
    internal const string StartedOnColumn = "sprk_startedon";
    internal const string CompletedOnColumn = "sprk_completedon";
    internal const string ErrorMessageColumn = "sprk_errormessage";

    internal const string PlaybookColumn = "sprk_playbookid";
    internal const string TriggeredByColumn = "sprk_triggeredby";
    internal const string InputContextColumn = "sprk_inputcontextjson";
    internal const string CorrelationIdColumn = "sprk_correlationid";
    internal const string ErrorCodeColumn = "sprk_errorcode";
    internal const string RegardingEntityTypeColumn = "sprk_regardingentitytype";
    internal const string RegardingRecordIdColumn = "sprk_regardingrecordid";
    internal const string SummaryColumn = "sprk_summary";
    internal const string RunKindColumn = "sprk_runkind";
    internal const string ActionColumn = "sprk_analysisactionid";
    internal const string BindingColumn = "sprk_playbookconsumerid";
    internal const string ParentRunColumn = "sprk_parentrunid";

    internal const string NodeRunRunColumn = "sprk_playbookrunid";
    internal const string NodeRunNodeColumn = "sprk_playbooknodeid";
    internal const string SequenceColumn = "sprk_sequence";
    internal const string DurationColumn = "sprk_durationms";
    internal const string TokensInColumn = "sprk_tokensin";
    internal const string TokensOutColumn = "sprk_tokensout";
    internal const string ConfidenceColumn = "sprk_confidence";

    internal const string PlaybookEntity = "sprk_analysisplaybook";
    internal const string NodeEntity = "sprk_playbooknode";
    internal const string ActionEntity = "sprk_analysisaction";
    internal const string BindingEntity = "sprk_playbookconsumer";
    internal const string SystemUserEntity = "systemuser";
    internal const string SystemUserOidColumn = "azureactivedirectoryobjectid";

    /// <summary>The table a linked run is regarding (task 037a).</summary>
    internal const string AnalysisEntity = "sprk_analysis";

    private const int NameMaxLength = 200;
    private const int ShortTextMaxLength = 100;
    private const int SummaryMaxLength = 4000;
    private const int ErrorMessageMaxLength = 100000;

    /// <summary><c>sprk_confidence</c> is a Decimal column, precision 10, range ±1000 (Add-PlaybookRunColumns.ps1).</summary>
    private const int ConfidenceDecimals = 10;

    private const double ConfidenceMaxMagnitude = 1000d;

    private static readonly JsonSerializerOptions InputContextJson = new(JsonSerializerDefaults.Web);

    private readonly IGenericEntityService _dataverse;

    /// <summary>Task 037a: the analysis this scope's runs are regarding (<see cref="LinkRunsToAnalysis"/>).</summary>
    private Guid? _linkedAnalysis;

    public PlaybookRunRecordStore(IGenericEntityService dataverse)
    {
        _dataverse = dataverse;
    }

    /// <summary>
    /// Creates or updates the run's <c>sprk_playbookrun</c> row, keyed by the run id (an upsert on the primary key).
    /// </summary>
    /// <remarks>
    /// An upsert, so every write carries the whole run and any write can be repeated: a final write still lands when the
    /// start write failed, timed out after Dataverse committed it, or was never made.
    /// </remarks>
    public Task WriteRunAsync(PlaybookRunRecord run, CancellationToken cancellationToken)
    {
        var entity = new Entity(RunEntity, run.RunId)
        {
            [NameColumn] = Truncate($"{RunNamePrefix(run.Kind)} {run.RunId:D}", NameMaxLength),
            [RunKindColumn] = new OptionSetValue((int)run.Kind),
            [InputContextColumn] = JsonSerializer.Serialize(new InputContext(run.DocumentIds), InputContextJson),
        };

        // sprk_playbookid is optional since the run-record decision: a prompted or coded run has no playbook.
        if (run.PlaybookId is { } playbookId)
        {
            entity[PlaybookColumn] = new EntityReference(PlaybookEntity, playbookId);
        }

        if (run.ActionId is { } actionId)
        {
            entity[ActionColumn] = new EntityReference(ActionEntity, actionId);
        }

        if (run.BindingId is { } bindingId)
        {
            entity[BindingColumn] = new EntityReference(BindingEntity, bindingId);
        }

        if (run.ParentRunId is { } parentRunId)
        {
            entity[ParentRunColumn] = new EntityReference(RunEntity, parentRunId);
        }

        foreach (var (column, value) in RunStateFields(run))
        {
            entity[column] = value;
        }

        if (run.TriggeredBy is { } triggeredBy)
        {
            entity[TriggeredByColumn] = new EntityReference(SystemUserEntity, triggeredBy);
        }

        if (!string.IsNullOrEmpty(run.CorrelationId))
        {
            entity[CorrelationIdColumn] = Truncate(run.CorrelationId, ShortTextMaxLength);
        }

        // Task 037a: a run linked to its analysis is regarding that analysis, whatever record parameter it was given
        // (037-decisions §4, "both present": the analysis wins).
        if ((_linkedAnalysis is { } analysisId ? new PlaybookRunRegarding(AnalysisEntity, analysisId) : run.Regarding) is { } regarding)
        {
            entity[RegardingEntityTypeColumn] = Truncate(regarding.EntityLogicalName, ShortTextMaxLength);
            entity[RegardingRecordIdColumn] = regarding.RecordId.ToString("D");
        }

        return _dataverse.UpsertAsync(entity, cancellationToken);
    }

    /// <summary>
    /// spaarkeai-mcp-bff-api-r2 task 037a (D-037-engine): every run this store writes from now on, in this scope, is regarding the
    /// analysis <paramref name="analysisId"/> (<c>sprk_regardingentitytype</c> = <c>sprk_analysis</c>,
    /// <c>sprk_regardingrecordid</c> = its id), in place of the record the run's parameters name.
    /// </summary>
    /// <remarks>
    /// Only <c>DetachedPlaybookRunner</c> calls it, on the detached run's own scope, with the analysis it has just created for the caller
    /// (<c>PlaybookRunAnalysisIdGuardTests</c>). The store is scoped, so the link never reaches another request's runs; a run the engine
    /// starts inside the same run (the legacy wrapper's) is that analysis's run too. Set once: a second, different id is a programming
    /// error and throws.
    /// </remarks>
    internal void LinkRunsToAnalysis(Guid analysisId)
    {
        if (analysisId == Guid.Empty)
        {
            throw new ArgumentException("The analysis id is empty.", nameof(analysisId));
        }

        if (_linkedAnalysis is { } linked && linked != analysisId)
        {
            throw new InvalidOperationException("This scope's runs are already linked to another analysis.");
        }

        _linkedAnalysis = analysisId;
    }

    /// <summary>The analysis this scope's runs are linked to (<see cref="LinkRunsToAnalysis"/>), if any.</summary>
    internal Guid? LinkedAnalysis => _linkedAnalysis;

    /// <summary>Creates one <c>sprk_playbooknoderun</c> row for a node the run reached.</summary>
    public Task CreateNodeRunAsync(PlaybookNodeRunRecord node, CancellationToken cancellationToken)
    {
        var entity = new Entity(NodeRunEntity)
        {
            [NameColumn] = Truncate(node.Name, NameMaxLength),
            [NodeRunRunColumn] = new EntityReference(RunEntity, node.RunId),
            [StatusColumn] = new OptionSetValue(ToNodeOption(node.Outcome)),
            [SequenceColumn] = node.Sequence,
            [StartedOnColumn] = node.StartedAt.UtcDateTime,
            [CompletedOnColumn] = node.CompletedAt.UtcDateTime,
        };

        // sprk_playbooknodeid is optional since the run-record decision: an Action run's row has no playbook node.
        if (node.NodeId is { } nodeId && nodeId != Guid.Empty)
        {
            entity[NodeRunNodeColumn] = new EntityReference(NodeEntity, nodeId);
        }

        if (node.ActionId is { } actionId)
        {
            entity[ActionColumn] = new EntityReference(ActionEntity, actionId);
        }

        if (node.DurationMs is { } durationMs)
        {
            entity[DurationColumn] = (int)Math.Clamp(durationMs, 0, int.MaxValue);
        }

        if (node.TokensIn is { } tokensIn)
        {
            entity[TokensInColumn] = tokensIn;
        }

        if (node.TokensOut is { } tokensOut)
        {
            entity[TokensOutColumn] = tokensOut;
        }

        if (node.Confidence is { } confidence && double.IsFinite(confidence) && Math.Abs(confidence) <= ConfidenceMaxMagnitude)
        {
            entity[ConfidenceColumn] = Math.Round((decimal)confidence, ConfidenceDecimals);
        }

        var error = ProblemDetailsHelper.Redact(node.ErrorMessage);
        if (!string.IsNullOrEmpty(error))
        {
            entity[ErrorMessageColumn] = Truncate(error, ErrorMessageMaxLength);
        }

        return _dataverse.CreateAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Reads a stored run and its node rows for the caller who started it: <see langword="null"/> when no row has this
    /// run id, or when the run's starter (<c>sprk_triggeredby</c> → <c>systemuser.azureactivedirectoryobjectid</c>) is
    /// not <paramref name="callerOid"/> or is unknown. The owner check runs before the node rows are read, so another
    /// caller's request costs no node query.
    /// </summary>
    public async Task<StoredPlaybookRun?> GetOwnedRunAsync(Guid runId, string callerOid, CancellationToken cancellationToken)
    {
        var runQuery = new QueryExpression(RunEntity)
        {
            ColumnSet = new ColumnSet(
                PlaybookColumn, StatusColumn, StartedOnColumn, CompletedOnColumn, ErrorMessageColumn,
                TriggeredByColumn, InputContextColumn),
            TopCount = 1,
        };
        runQuery.Criteria.AddCondition(RunIdColumn, ConditionOperator.Equal, runId);

        var runs = await _dataverse.RetrieveMultipleAsync(runQuery, cancellationToken).ConfigureAwait(false);
        var row = runs.Entities.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        // A row with no playbook id is a prompted or coded run (the run-record decision made sprk_playbookid optional).
        // It has no PlaybookRunStatus shape (a playbook id is part of it), so this playbook run read does not return it:
        // the playbook run routes answer it like an unknown run id. A reader for the other kinds is a later addition to
        // this store. Checked before the owner lookup, so it costs no further query.
        if (row.GetAttributeValue<EntityReference>(PlaybookColumn) is not { } playbook || playbook.Id == Guid.Empty)
        {
            return null;
        }

        var startedByOid = await ReadTriggeredByOidAsync(
            row.GetAttributeValue<EntityReference>(TriggeredByColumn), cancellationToken).ConfigureAwait(false);
        if (!IsSameOid(startedByOid, callerOid))
        {
            return null;
        }

        var nodeQuery = new QueryExpression(NodeRunEntity)
        {
            ColumnSet = new ColumnSet(
                NameColumn, NodeRunNodeColumn, StatusColumn, SequenceColumn, StartedOnColumn, CompletedOnColumn,
                DurationColumn, TokensInColumn, TokensOutColumn, ConfidenceColumn, ErrorMessageColumn),
        };
        nodeQuery.Criteria.AddCondition(NodeRunRunColumn, ConditionOperator.Equal, runId);
        nodeQuery.AddOrder(SequenceColumn, OrderType.Ascending);

        var nodeRows = await _dataverse.RetrieveMultipleAsync(nodeQuery, cancellationToken).ConfigureAwait(false);
        var nodes = new List<StoredPlaybookNodeRun>(nodeRows.Entities.Count);
        foreach (var nodeRow in nodeRows.Entities)
        {
            if (TryFromNodeOption(nodeRow.GetAttributeValue<OptionSetValue>(StatusColumn)?.Value) is not { } outcome)
            {
                continue; // Pending / Running are never written by the orchestrator; ignore a foreign row.
            }

            nodes.Add(new StoredPlaybookNodeRun(
                NodeId: nodeRow.GetAttributeValue<EntityReference>(NodeRunNodeColumn)?.Id ?? Guid.Empty,
                Name: nodeRow.GetAttributeValue<string>(NameColumn) ?? string.Empty,
                Outcome: outcome,
                Sequence: nodeRow.GetAttributeValue<int?>(SequenceColumn) ?? 0,
                DurationMs: nodeRow.GetAttributeValue<int?>(DurationColumn),
                TokensIn: nodeRow.GetAttributeValue<int?>(TokensInColumn),
                TokensOut: nodeRow.GetAttributeValue<int?>(TokensOutColumn),
                Confidence: nodeRow.GetAttributeValue<decimal?>(ConfidenceColumn) is { } confidence ? (double)confidence : null,
                ErrorMessage: nodeRow.GetAttributeValue<string>(ErrorMessageColumn)));
        }

        nodes.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

        var statusValue = row.GetAttributeValue<OptionSetValue>(StatusColumn)?.Value;
        var state = TryFromRunOption(statusValue)
            ?? throw new InvalidOperationException($"sprk_playbookrun {runId} has an unknown sprk_status value {statusValue}.");

        return new StoredPlaybookRun(
            RunId: runId,
            PlaybookId: playbook.Id,
            State: state,
            StartedAt: ToOffset(row.GetAttributeValue<DateTime?>(StartedOnColumn)) ?? DateTimeOffset.MinValue,
            CompletedAt: ToOffset(row.GetAttributeValue<DateTime?>(CompletedOnColumn)),
            ErrorMessage: row.GetAttributeValue<string>(ErrorMessageColumn),
            DocumentIds: ReadDocumentIds(row.GetAttributeValue<string>(InputContextColumn)),
            StartedByOid: startedByOid,
            Nodes: nodes);
    }

    /// <summary>
    /// spaarkeai-mcp-bff-api-r2 task 037b: what a stored run is regarding and the documents it ran on, for the caller who started it
    /// (the same owner rule as <see cref="GetOwnedRunAsync"/>: <c>sprk_triggeredby</c>'s oid must be <paramref name="callerOid"/>), or
    /// <see langword="null"/> when there is no such run, it is not a playbook run, or it is another caller's. Identifiers only (ADR-015):
    /// no node row, no output. Read app-only, as every read of this store is; the results it points at are read as the caller.
    /// </summary>
    public async Task<PlaybookRunLink?> GetOwnedRunLinkAsync(Guid runId, string callerOid, CancellationToken cancellationToken)
    {
        var query = new QueryExpression(RunEntity)
        {
            ColumnSet = new ColumnSet(PlaybookColumn, TriggeredByColumn, InputContextColumn, RegardingEntityTypeColumn, RegardingRecordIdColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition(RunIdColumn, ConditionOperator.Equal, runId);

        var rows = await _dataverse.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
        var row = rows.Entities.FirstOrDefault();
        if (row?.GetAttributeValue<EntityReference>(PlaybookColumn) is not { } playbook || playbook.Id == Guid.Empty)
        {
            return null;
        }

        var startedByOid = await ReadTriggeredByOidAsync(
            row.GetAttributeValue<EntityReference>(TriggeredByColumn), cancellationToken).ConfigureAwait(false);
        if (!IsSameOid(startedByOid, callerOid))
        {
            return null;
        }

        var regardingType = row.GetAttributeValue<string>(RegardingEntityTypeColumn);
        var regarding = !string.IsNullOrWhiteSpace(regardingType)
                        && Guid.TryParse(row.GetAttributeValue<string>(RegardingRecordIdColumn), out var regardingId)
                        && regardingId != Guid.Empty
            ? new PlaybookRunRegarding(regardingType, regardingId)
            : null;

        return new PlaybookRunLink(regarding, ReadDocumentIds(row.GetAttributeValue<string>(InputContextColumn)));
    }

    /// <summary>Same Entra object id: both parse as GUIDs and are equal (case and format insensitive).</summary>
    internal static bool IsSameOid(string? storedOid, string callerOid) =>
        Guid.TryParse(storedOid, out var stored) && Guid.TryParse(callerOid, out var caller) && stored == caller;

    private async Task<string?> ReadTriggeredByOidAsync(EntityReference? triggeredBy, CancellationToken cancellationToken)
    {
        if (triggeredBy is null || triggeredBy.Id == Guid.Empty)
        {
            return null;
        }

        var query = new QueryExpression(SystemUserEntity)
        {
            ColumnSet = new ColumnSet(SystemUserOidColumn),
            TopCount = 1,
        };
        query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, triggeredBy.Id);

        var users = await _dataverse.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
        var oid = users.Entities.FirstOrDefault()?.GetAttributeValue<Guid?>(SystemUserOidColumn);
        return oid is { } value && value != Guid.Empty ? value.ToString("D") : null;
    }

    private static IEnumerable<(string Column, object Value)> RunStateFields(PlaybookRunRecord run)
    {
        yield return (StatusColumn, new OptionSetValue(ToRunOption(run.State)));
        yield return (StartedOnColumn, run.StartedAt.UtcDateTime);

        if (run.CompletedAt is { } completedAt)
        {
            yield return (CompletedOnColumn, completedAt.UtcDateTime);
        }

        if (!string.IsNullOrEmpty(run.ErrorMessage))
        {
            yield return (ErrorMessageColumn, Truncate(ProblemDetailsHelper.Redact(run.ErrorMessage)!, ErrorMessageMaxLength));
        }

        if (!string.IsNullOrEmpty(run.ErrorCode))
        {
            yield return (ErrorCodeColumn, Truncate(run.ErrorCode, ShortTextMaxLength));
        }

        if (!string.IsNullOrEmpty(run.Summary))
        {
            yield return (SummaryColumn, Truncate(run.Summary, SummaryMaxLength));
        }
    }

    private static string RunNamePrefix(PlaybookRunKind kind) => kind switch
    {
        PlaybookRunKind.Engine => "Playbook run",
        PlaybookRunKind.Prompted => "Prompted run",
        PlaybookRunKind.Coded => "Coded run",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown run kind."),
    };

    /// <summary><c>sprk_playbookrunstatus</c>: 0 Pending, 1 Running, 2 Completed, 3 Failed, 4 Cancelled (read on dev 2026-11-25).</summary>
    internal static int ToRunOption(PlaybookRunState state) => state switch
    {
        PlaybookRunState.Pending => 0,
        PlaybookRunState.Running => 1,
        PlaybookRunState.Completed => 2,
        PlaybookRunState.Failed => 3,
        PlaybookRunState.Cancelled => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown run state."),
    };

    private static PlaybookRunState? TryFromRunOption(int? value) => value switch
    {
        0 => PlaybookRunState.Pending,
        1 => PlaybookRunState.Running,
        2 => PlaybookRunState.Completed,
        3 => PlaybookRunState.Failed,
        4 => PlaybookRunState.Cancelled,
        _ => null,
    };

    /// <summary><c>sprk_noderunstatus</c>: 2 Completed, 3 Failed, 4 Skipped (0 Pending and 1 Running are not written).</summary>
    internal static int ToNodeOption(PlaybookNodeRunOutcome outcome) => outcome switch
    {
        PlaybookNodeRunOutcome.Completed => 2,
        PlaybookNodeRunOutcome.Failed => 3,
        PlaybookNodeRunOutcome.Skipped => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown node outcome."),
    };

    private static PlaybookNodeRunOutcome? TryFromNodeOption(int? value) => value switch
    {
        2 => PlaybookNodeRunOutcome.Completed,
        3 => PlaybookNodeRunOutcome.Failed,
        4 => PlaybookNodeRunOutcome.Skipped,
        _ => null,
    };

    private static Guid[] ReadDocumentIds(string? inputContextJson)
    {
        if (string.IsNullOrWhiteSpace(inputContextJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<InputContext>(inputContextJson, InputContextJson)?.DocumentIds?.ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is { } dateTime ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)) : null;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>The identifiers-only content of <c>sprk_inputcontextjson</c>.</summary>
    private sealed record InputContext(IReadOnlyList<Guid> DocumentIds);
}

/// <summary>Outcome of a node as stored on its <c>sprk_playbooknoderun</c> row.</summary>
public enum PlaybookNodeRunOutcome
{
    /// <summary>The node stored a successful output.</summary>
    Completed,

    /// <summary>The node stored a failed output.</summary>
    Failed,

    /// <summary>The node was skipped before it produced an output (branch not selected, dependency skip).</summary>
    Skipped,
}

/// <summary>The record subject of a run: a Dataverse table logical name and a row id.</summary>
public sealed record PlaybookRunRegarding(string EntityLogicalName, Guid RecordId);

/// <summary>
/// How a run executed (<c>sprk_runkind</c>, a choice on <c>sprk_playbookrun</c>). The numeric values are the choice's
/// option values (Spaarke's 100000000+ convention, <c>scripts/dataverse/Add-PlaybookRunColumns.ps1</c>).
/// </summary>
public enum PlaybookRunKind
{
    /// <summary>A playbook run on the node engine (<see cref="PlaybookOrchestrationService"/>).</summary>
    Engine = 100000000,

    /// <summary>A single prompted Action run.</summary>
    Prompted = 100000001,

    /// <summary>A coded-workflow run.</summary>
    Coded = 100000002,
}

/// <summary>
/// The run-level values written to <c>sprk_playbookrun</c>: the one write request for every run kind.
/// </summary>
/// <remarks>
/// <b>Extension point.</b> This is an object with init-only members, not a positional parameter list, so a later
/// addition (another column, or a write mode such as a "required" mode that fails closed for a gated side-effect run) is
/// one more member with a default. No existing caller changes. Today every write is fail-safe: the store throws, and its
/// caller logs <c>playbook_run_persist_failed</c> and carries on. The engine writes <see cref="Kind"/>
/// <see cref="PlaybookRunKind.Engine"/> and sets none of the Action, Binding or parent-run references.
/// </remarks>
public sealed record PlaybookRunRecord
{
    public required Guid RunId { get; init; }

    /// <summary>The playbook (engine runs); <c>null</c> for a prompted or coded run.</summary>
    public Guid? PlaybookId { get; init; }

    public PlaybookRunKind Kind { get; init; } = PlaybookRunKind.Engine;

    public required PlaybookRunState State { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorCode { get; init; }

    public Guid? TriggeredBy { get; init; }

    public string? CorrelationId { get; init; }

    public PlaybookRunRegarding? Regarding { get; init; }

    public IReadOnlyList<Guid> DocumentIds { get; init; } = [];

    public string? Summary { get; init; }

    /// <summary>The <c>sprk_analysisaction</c> a prompted or coded run executed.</summary>
    public Guid? ActionId { get; init; }

    /// <summary>The <c>sprk_playbookconsumer</c> (Binding) row the run was routed through.</summary>
    public Guid? BindingId { get; init; }

    /// <summary>The <c>sprk_playbookrun</c> that started this run (a nested run).</summary>
    public Guid? ParentRunId { get; init; }
}

/// <summary>The node-level values written to <c>sprk_playbooknoderun</c>.</summary>
public sealed record PlaybookNodeRunRecord
{
    public required Guid RunId { get; init; }

    /// <summary>The playbook node (engine runs); <c>null</c> (or empty) for an Action run's row.</summary>
    public Guid? NodeId { get; init; }

    public required string Name { get; init; }

    public required PlaybookNodeRunOutcome Outcome { get; init; }

    public required int Sequence { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public long? DurationMs { get; init; }

    public int? TokensIn { get; init; }

    public int? TokensOut { get; init; }

    public double? Confidence { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>The <c>sprk_analysisaction</c> the row stands for (a prompted or coded run).</summary>
    public Guid? ActionId { get; init; }
}

/// <summary>
/// Task 037b: what a stored run is regarding and the documents it ran on (identifiers only), from <c>sprk_regardingentitytype</c> /
/// <c>sprk_regardingrecordid</c> and <c>sprk_inputcontextjson</c>.
/// </summary>
public sealed record PlaybookRunLink(PlaybookRunRegarding? Regarding, Guid[] DocumentIds);

/// <summary>A node row read back from <c>sprk_playbooknoderun</c>.</summary>
public sealed record StoredPlaybookNodeRun(
    Guid NodeId,
    string Name,
    PlaybookNodeRunOutcome Outcome,
    int Sequence,
    int? DurationMs,
    int? TokensIn,
    int? TokensOut,
    double? Confidence,
    string? ErrorMessage);

/// <summary>
/// A run read back from Dataverse, mapped to the same response types the in-memory run produces
/// (<see cref="PlaybookRunStatus"/>, <see cref="PlaybookRunDetail"/>). Fields the store never holds (ADR-015) come back
/// null: <see cref="PlaybookRunStatus.Outputs"/>, <see cref="NodeRunDetail.OutputPreview"/> and
/// <see cref="PlaybookRunDetail.UserContext"/>.
/// </summary>
public sealed record StoredPlaybookRun(
    Guid RunId,
    Guid PlaybookId,
    PlaybookRunState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    Guid[] DocumentIds,
    string? StartedByOid,
    IReadOnlyList<StoredPlaybookNodeRun> Nodes)
{
    /// <summary>
    /// The run's metrics, derived the way <see cref="PlaybookRunContext.GetMetrics"/> counts them: every node that stored
    /// an output (Completed or Failed row) is in the total, tokens are summed over successful nodes, and skipped nodes
    /// are counted separately.
    /// </summary>
    public PlaybookRunMetrics GetMetrics(DateTimeOffset now)
    {
        var completed = Nodes.Where(n => n.Outcome == PlaybookNodeRunOutcome.Completed).ToList();
        var failed = Nodes.Count(n => n.Outcome == PlaybookNodeRunOutcome.Failed);

        return new PlaybookRunMetrics
        {
            TotalNodes = completed.Count + failed,
            CompletedNodes = completed.Count,
            FailedNodes = failed,
            SkippedNodes = Nodes.Count(n => n.Outcome == PlaybookNodeRunOutcome.Skipped),
            TotalTokensIn = completed.Sum(n => n.TokensIn ?? 0),
            TotalTokensOut = completed.Sum(n => n.TokensOut ?? 0),
            Duration = (CompletedAt ?? now) - StartedAt,
        };
    }

    /// <summary>The status the run status route serializes; <paramref name="now"/> ends the duration of an unfinished run.</summary>
    public PlaybookRunStatus ToStatus(DateTimeOffset now) => new()
    {
        RunId = RunId,
        PlaybookId = PlaybookId,
        State = State,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        CurrentNodeId = Nodes.Count == 0 ? null : Nodes[^1].NodeId,
        ErrorMessage = ErrorMessage,
        Metrics = GetMetrics(now),
        Outputs = null,
        StartedByOid = StartedByOid,
    };

    /// <summary>The detail the run detail route serializes.</summary>
    public PlaybookRunDetail ToDetail(DateTimeOffset now)
    {
        var metrics = GetMetrics(now);

        return new PlaybookRunDetail
        {
            RunId = RunId,
            PlaybookId = PlaybookId,
            State = State.ToString(),
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            DurationMs = CompletedAt.HasValue ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds : null,
            TotalNodes = metrics.TotalNodes,
            CompletedNodes = metrics.CompletedNodes,
            FailedNodes = metrics.FailedNodes,
            SkippedNodes = metrics.SkippedNodes,
            TotalTokensIn = metrics.TotalTokensIn,
            TotalTokensOut = metrics.TotalTokensOut,
            ErrorMessage = ErrorMessage,
            DocumentIds = DocumentIds,
            UserContext = null,
            NodeDetails = Nodes
                .Where(n => n.Outcome != PlaybookNodeRunOutcome.Skipped)
                .Select(n => new NodeRunDetail
                {
                    NodeId = n.NodeId,
                    NodeName = n.Name,
                    OutputVariable = n.Name,
                    Success = n.Outcome == PlaybookNodeRunOutcome.Completed,
                    DurationMs = n.DurationMs,
                    TokensIn = n.TokensIn,
                    TokensOut = n.TokensOut,
                    Confidence = n.Confidence,
                    ErrorMessage = ProblemDetailsHelper.Redact(n.ErrorMessage),
                    OutputPreview = null,
                })
                .ToArray(),
        };
    }
}
