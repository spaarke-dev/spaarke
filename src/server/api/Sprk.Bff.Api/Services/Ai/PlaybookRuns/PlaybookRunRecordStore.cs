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
/// <c>sprk_playbooknoderun</c> row per node the run reached. <see cref="PlaybookOrchestrationService"/> is the only
/// writer; it writes on the run's own execution path (start, each node, completion — no background work, ADR-052) and
/// reads back here when a run is not in its in-memory map.
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
/// <b>ADR-015.</b> Only identifiers, timings, counts, tokens, the redacted error text the REST run routes already return,
/// and a summary built from counts are written. Document content, prompts, the user's instructions and node output
/// (text, structured data, tool results, warnings, previews, confidence) are never written.
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

    internal const string NodeRunRunColumn = "sprk_playbookrunid";
    internal const string NodeRunNodeColumn = "sprk_playbooknodeid";
    internal const string SequenceColumn = "sprk_sequence";
    internal const string DurationColumn = "sprk_durationms";
    internal const string TokensInColumn = "sprk_tokensin";
    internal const string TokensOutColumn = "sprk_tokensout";

    internal const string PlaybookEntity = "sprk_analysisplaybook";
    internal const string NodeEntity = "sprk_playbooknode";
    internal const string SystemUserEntity = "systemuser";
    internal const string SystemUserOidColumn = "azureactivedirectoryobjectid";

    private const int NameMaxLength = 200;
    private const int ShortTextMaxLength = 100;
    private const int SummaryMaxLength = 4000;
    private const int ErrorMessageMaxLength = 100000;

    private static readonly JsonSerializerOptions InputContextJson = new(JsonSerializerDefaults.Web);

    private readonly IGenericEntityService _dataverse;

    public PlaybookRunRecordStore(IGenericEntityService dataverse)
    {
        _dataverse = dataverse;
    }

    /// <summary>Creates the run's <c>sprk_playbookrun</c> row, keyed by the run id.</summary>
    public Task CreateRunAsync(PlaybookRunRecord run, CancellationToken cancellationToken)
    {
        var entity = new Entity(RunEntity, run.RunId)
        {
            [NameColumn] = Truncate($"Playbook run {run.RunId:D}", NameMaxLength),
            [PlaybookColumn] = new EntityReference(PlaybookEntity, run.PlaybookId),
            [InputContextColumn] = JsonSerializer.Serialize(new InputContext(run.DocumentIds), InputContextJson),
        };

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

        if (run.Regarding is { } regarding)
        {
            entity[RegardingEntityTypeColumn] = Truncate(regarding.EntityLogicalName, ShortTextMaxLength);
            entity[RegardingRecordIdColumn] = regarding.RecordId.ToString("D");
        }

        return _dataverse.CreateAsync(entity, cancellationToken);
    }

    /// <summary>Writes the run's current state (status, completion, error, summary) onto its existing row.</summary>
    public Task UpdateRunAsync(PlaybookRunRecord run, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, object>();
        foreach (var (column, value) in RunStateFields(run))
        {
            fields[column] = value;
        }

        return _dataverse.UpdateAsync(RunEntity, run.RunId, fields, cancellationToken);
    }

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

        if (node.NodeId != Guid.Empty)
        {
            entity[NodeRunNodeColumn] = new EntityReference(NodeEntity, node.NodeId);
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

        var error = ProblemDetailsHelper.Redact(node.ErrorMessage);
        if (!string.IsNullOrEmpty(error))
        {
            entity[ErrorMessageColumn] = Truncate(error, ErrorMessageMaxLength);
        }

        return _dataverse.CreateAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Reads a stored run and its node rows, or <see langword="null"/> when no row has this run id. Performs no owner
    /// check: the caller compares <see cref="StoredPlaybookRun.StartedByOid"/> with its caller.
    /// </summary>
    public async Task<StoredPlaybookRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
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

        var startedByOid = await ReadTriggeredByOidAsync(
            row.GetAttributeValue<EntityReference>(TriggeredByColumn), cancellationToken).ConfigureAwait(false);

        var nodeQuery = new QueryExpression(NodeRunEntity)
        {
            ColumnSet = new ColumnSet(
                NameColumn, NodeRunNodeColumn, StatusColumn, SequenceColumn, StartedOnColumn, CompletedOnColumn,
                DurationColumn, TokensInColumn, TokensOutColumn, ErrorMessageColumn),
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
                ErrorMessage: nodeRow.GetAttributeValue<string>(ErrorMessageColumn)));
        }

        nodes.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

        var statusValue = row.GetAttributeValue<OptionSetValue>(StatusColumn)?.Value;
        var state = TryFromRunOption(statusValue)
            ?? throw new InvalidOperationException($"sprk_playbookrun {runId} has an unknown sprk_status value {statusValue}.");

        return new StoredPlaybookRun(
            RunId: runId,
            PlaybookId: row.GetAttributeValue<EntityReference>(PlaybookColumn)?.Id ?? Guid.Empty,
            State: state,
            StartedAt: ToOffset(row.GetAttributeValue<DateTime?>(StartedOnColumn)) ?? DateTimeOffset.MinValue,
            CompletedAt: ToOffset(row.GetAttributeValue<DateTime?>(CompletedOnColumn)),
            ErrorMessage: row.GetAttributeValue<string>(ErrorMessageColumn),
            DocumentIds: ReadDocumentIds(row.GetAttributeValue<string>(InputContextColumn)),
            StartedByOid: startedByOid,
            Nodes: nodes);
    }

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

/// <summary>The run-level values written to <c>sprk_playbookrun</c>.</summary>
public sealed record PlaybookRunRecord(
    Guid RunId,
    Guid PlaybookId,
    PlaybookRunState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    string? ErrorCode,
    Guid? TriggeredBy,
    string? CorrelationId,
    PlaybookRunRegarding? Regarding,
    IReadOnlyList<Guid> DocumentIds,
    string? Summary);

/// <summary>The node-level values written to <c>sprk_playbooknoderun</c>.</summary>
public sealed record PlaybookNodeRunRecord(
    Guid RunId,
    Guid NodeId,
    string Name,
    PlaybookNodeRunOutcome Outcome,
    int Sequence,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    long? DurationMs,
    int? TokensIn,
    int? TokensOut,
    string? ErrorMessage);

/// <summary>A node row read back from <c>sprk_playbooknoderun</c>.</summary>
public sealed record StoredPlaybookNodeRun(
    Guid NodeId,
    string Name,
    PlaybookNodeRunOutcome Outcome,
    int Sequence,
    int? DurationMs,
    int? TokensIn,
    int? TokensOut,
    string? ErrorMessage);

/// <summary>
/// A run read back from Dataverse, mapped to the same response types the in-memory run produces
/// (<see cref="PlaybookRunStatus"/>, <see cref="PlaybookRunDetail"/>). Fields the store never holds (ADR-015) come back
/// null: <see cref="PlaybookRunStatus.Outputs"/>, <see cref="NodeRunDetail.OutputPreview"/>,
/// <see cref="NodeRunDetail.Confidence"/> and <see cref="PlaybookRunDetail.UserContext"/>.
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
                    Confidence = null,
                    ErrorMessage = ProblemDetailsHelper.Redact(n.ErrorMessage),
                    OutputPreview = null,
                })
                .ToArray(),
        };
    }
}
