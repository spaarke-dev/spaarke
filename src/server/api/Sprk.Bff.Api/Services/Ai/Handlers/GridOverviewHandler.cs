using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>spaarke.grid_overview</c> tool — runs a surface's EXISTING
/// saved query (the <c>sprk_fetchxml</c> of a <c>sprk_gridconfiguration</c> row, addressed by
/// <c>configId</c>) server-side over the caller's OBO token, with the current date injected
/// deterministically, and returns grid-shaped rows + an accurate record count with record-id
/// citations (spaarkeai-assistant-enhancements-r3 task 020, FR-06 — the overview DoD driver).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this tool exists (§11 reuse-first, closed-set contract)</b>: the generic
/// <see cref="DataverseReadQueryHandler"/> (<c>dataverse.read_query</c>) rejects
/// <c>GETDATE()</c>/<c>COUNT</c>/aggregates by design, so the model hand-authored unsupported SQL
/// for "how many overdue tasks do I have?" (the R2 UAT defect: query error + duplicate tab +
/// narrated tab-names-not-data). This is ONE parameterized tool keyed by <c>configId</c> — NOT N
/// per-grid handlers and NOT a model-authored-SQL path. It reuses the query DEFINITION each
/// surface already owns (its grid's saved FetchXML) and only injects <c>today</c> + executes it.
/// Task 021 (FR-07) wires this same tool across all grids + Calendar via <c>configId</c>; the
/// contract here stays surface-agnostic. Daily Briefing is a Lane-3 composed service
/// (<see cref="Sprk.Bff.Api.Services.Workspace.BriefingService"/>, not a FetchXML-shaped saved
/// query) and is wired separately by <see cref="DailyBriefingOverviewHandler"/> — this handler
/// deliberately does NOT try to serve it.
/// </para>
/// <para>
/// <b>Task 021 configId wiring (FR-07)</b>: the per-tab workspace-state prompt block is trimmed
/// to <c>{type,label,active}</c> ONLY (task 011's binding ADR-015 invariant — no ambient widget
/// content), so the model cannot read a surface's configId off the open tab. Instead, the known
/// grid/Calendar configIds are published as static tool metadata in <see cref="Metadata"/>'s
/// <c>Description</c> (deterministic catalog DATA per ADR-039, not per-turn tab content, not a
/// classifier) — see the "Known configId values" sentence there, kept byte-equal with
/// <c>infra/dataverse/sprk_analysistool-grid-overview-row.json</c>'s <c>sprk_description</c>.
/// </para>
/// <para>
/// <b>User-OBO ONLY (ADR-028 / ADR-015)</b>: both the config read AND the query execution go
/// through <see cref="IDataverseUserClient"/> under the calling user's Dataverse security roles +
/// row-level security. Rows the user cannot read are absent — enforced by Dataverse, never by BFF
/// filtering, never app-only. The FetchXML is executed via the Web API <c>?fetchXml=</c> parameter
/// (which honors the OBO identity), NOT via the app-only <c>FetchService</c>/<c>ServiceClient</c>
/// path used by the DataGrid endpoints.
/// </para>
/// <para>
/// <b>today is injected SERVER-SIDE (project MUST rule)</b>: the tool computes the current date
/// from an injected <see cref="TimeProvider"/> (never a client-supplied value) and substitutes the
/// <c>{{today}}</c> / <c>{{today+N}}</c> / <c>{{today-N}}</c> placeholder tokens in the saved
/// FetchXML with an ISO-8601 date literal. A grid's "overdue" view carries
/// <c>&lt;condition attribute="sprk_duedate" operator="lt" value="{{today}}" /&gt;</c>; the derived
/// predicate (overdue = dueDate &lt; today) is therefore resolved server-side, deterministically,
/// with no date prompt. Saved queries that instead use FetchXML native relative-date operators
/// (<c>on-or-before</c>, <c>today</c>, …) still execute correctly (the substitution is a no-op).
/// </para>
/// <para>
/// <b>Grounding (ADR-039 / ADR-015)</b>: every returned row carries the table primary id plus a
/// GA-MCP-style citation path (<c>tables/{t}/records/{id}</c>) so P2 citation enforcement can
/// anchor the narrated answer to records. Telemetry carries configId + entity + counts + duration
/// ONLY — never the FetchXML body (may embed maker literals) and never row content.
/// </para>
/// </remarks>
public sealed class GridOverviewHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(GridOverviewHandler);

    /// <summary>
    /// LLM-facing tool id. NOT part of the frozen GA <c>dataverse.*</c> MCP namespace
    /// (<see cref="DataverseToolNames"/>) — this is a Spaarke-specific capability, so it carries a
    /// distinct <c>spaarke.*</c> id.
    /// </summary>
    public const string ToolId = "spaarke.grid_overview";

    /// <summary>Dataverse entity-set for <c>sprk_gridconfiguration</c> (deterministic plural form).</summary>
    private const string GridConfigEntitySet = "sprk_gridconfigurations";

    /// <summary>Row cap applied to the returned rows (the accurate total comes from the count, not the rows).</summary>
    internal const int MaxRows = 200;

    private static readonly Regex TodayTokenRegex = new(
        @"\{\{\s*today\s*(?:([+-])\s*(\d+))?\s*\}\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDataverseUserClient _dataverse;
    private readonly TimeProvider _clock;
    private readonly ILogger<GridOverviewHandler> _logger;

    public GridOverviewHandler(
        IDataverseUserClient dataverse,
        TimeProvider clock,
        ILogger<GridOverviewHandler> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Grid Overview",
        // Mirror of the authored sprk_description in
        // infra/dataverse/sprk_analysistool-grid-overview-row.json — keep byte-equal; edit the JSON.
        // The trailing "Known configId values" table is task 021 (FR-07) wiring: the per-tab
        // workspace-state prompt block is trimmed to {type,label,active} ONLY (ADR-015 — no
        // ambient widget content, task 011's binding invariant), so the model cannot learn a
        // surface's configId from the open tab. Instead the mapping is published as STATIC
        // catalog/tool metadata (this description), which is deterministic DATA the model always
        // sees regardless of turn (ADR-039 — not a classifier, not per-tab content). The GUIDs
        // mirror ENTITY_VIEW_CONFIG_IDS in register-workspace-widgets.ts (same sprk_gridconfiguration
        // rows the grids already use to fetch data — §11 reuse, zero new Dataverse rows for grids)
        // plus EVENT_CONFIG_ID from CalendarWorkspaceWidget.tsx (same sprk_event grid configuration
        // the standalone EventsPage and the Calendar workspace widget already share).
        Description: @"Runs a grid or workspace surface's EXISTING saved view (its configuration's FetchXML), addressed by configId, under the calling user's permissions, and returns an accurate record count plus grid-shaped rows with record-id citations. The current date is injected server-side automatically (overdue/due-today/due-this-week predicates resolve against today with no date needed from you) — do NOT ask the user for the date. This tool runs under the CALLING USER's identity (OBO) automatically — you already know who the user is; NEVER ask the user for their user id, name, or 'who they are' to scope results. Use this instead of dataverse.read_query whenever the user asks 'how many' / a status overview / an overdue-or-upcoming count for a known grid, because this reuses the grid's own saved query (which read_query cannot express: GETDATE(), COUNT, and aggregates are rejected there). It takes ONE required argument, configId (the sprk_gridconfiguration record id for the target grid). Returns { count, rows, entity } — narrate the count and cite the returned record ids; do not invent numbers. Known configId values for the currently-deployed workspace surfaces (reuse these directly — do not guess a GUID): Documents=1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6, Matters=113ad380-9e63-f111-ab0c-70a8a53ec687, Projects=97ee98e7-7a63-f111-ab0c-70a8a53ec687, Invoices=d021827b-9b5e-f111-ab0c-7c1e521545d7, Work Assignments=9c5b0ee7-7a63-f111-ab0c-000d3a4d8152, Communications/Messages=e1826c4c-9575-f111-ab0e-7ced8ddc4a05, My Tasks=ac05e4f1-8d85-f111-8075-7c1e5268570d, Calendar/Events=5294c28a-f078-f111-ab0e-7ced8ddc4a05. For Daily Briefing questions (portfolio counts + narrative), use the daily-briefing overview tool instead — do not pass a Briefing configId here.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "configId",
                "The sprk_gridconfiguration record id (GUID) whose saved view FetchXML defines the overview to run.",
                ToolParameterType.String,
                Required: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "GridOverviewHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "GridOverviewHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed));

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (!TryParseArgs(context.ToolArgumentsJson, out _, out var error))
            return ToolValidationResult.Failure(error!);

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        if (!TryParseArgs(context.ToolArgumentsJson, out var configId, out var parseError))
        {
            return Error(tool, parseError!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        try
        {
            // 1) Resolve the surface's saved query DEFINITION by configId — over OBO (a config
            //    the user cannot read 404s here, which is the correct fail-closed behavior).
            var configResponse = await _dataverse.GetAsync(
                $"{GridConfigEntitySet}({configId:D})?$select=sprk_entitylogicalname,sprk_configjson,sprk_name",
                cancellationToken).ConfigureAwait(false);
            if (!configResponse.IsSuccess)
            {
                return MapClientError(tool, configResponse, startedAt);
            }

            var config = configResponse.Body!.Value;
            var viewName = GetString(config, "sprk_name") ?? "overview";
            var entityLogicalName = GetString(config, "sprk_entitylogicalname");

            // Resolve the query DEFINITION from the config's `sprk_configjson` `source` block (the
            // DataGrid framework SourceConfig: savedquery | inline | savedquery-set). R4 UAT 2026-08-18
            // ROOT CAUSE: this handler previously $select'd + read a `sprk_fetchxml` column that does
            // NOT exist on sprk_gridconfiguration (the table stores the query in sprk_configjson), so
            // every grid_overview call errored and the advisory task-agenda answer had ZERO grounded
            // data — the P1 "you have no tasks" defect despite the grid widget showing them. Read the
            // real column + resolve the referenced savedquery's FetchXML over the caller's OBO token.
            var configJson = GetString(config, "sprk_configjson");
            var (savedFetchXml, resolveError) = await ResolveFetchXmlFromConfigAsync(
                configJson, configId, cancellationToken).ConfigureAwait(false);
            if (resolveError is not null)
            {
                return Error(tool, resolveError, ToolErrorCodes.ValidationFailed, startedAt);
            }
            if (string.IsNullOrWhiteSpace(savedFetchXml))
            {
                return Error(tool,
                    $"Grid configuration '{configId:D}' has no runnable saved query; there is nothing to run.",
                    ToolErrorCodes.ValidationFailed, startedAt);
            }

            // 2) Inject today SERVER-SIDE (never a client value). Deterministic via the injected clock.
            var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            var todayIso = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var (datedFetchXml, todayInjected) = InjectToday(savedFetchXml, today);

            // 3) Normalize the FetchXML: read the root entity name (fallback for the config's entity)
            //    and request an accurate total count for non-aggregate views.
            string finalFetchXml;
            string? fetchEntityName;
            bool isAggregate;
            try
            {
                finalFetchXml = NormalizeFetchXml(datedFetchXml, out fetchEntityName, out isAggregate);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or FormatException)
            {
                return Error(tool,
                    $"Grid configuration '{configId:D}' saved query FetchXML is malformed and cannot be executed.",
                    ToolErrorCodes.ValidationFailed, startedAt);
            }

            // The FetchXML ROOT entity is authoritative for execution: the Web API requires the
            // entity-set in the path to match the fetch root, and citations name the row's table.
            // The config column is a fallback for a headerless/edge FetchXML only.
            var targetEntity = !string.IsNullOrWhiteSpace(fetchEntityName) ? fetchEntityName! : entityLogicalName;
            if (string.IsNullOrWhiteSpace(targetEntity))
            {
                return Error(tool,
                    $"Grid configuration '{configId:D}' does not identify a target entity.",
                    ToolErrorCodes.InternalError, startedAt);
            }

            // 4) Resolve entity-set + primary id from metadata (under the user's token; a table the
            //    user cannot see 404s/403s here — same fail-closed pattern as dataverse.read_query).
            var metaResponse = await _dataverse.GetAsync(
                $"EntityDefinitions(LogicalName='{targetEntity}')?$select=EntitySetName,PrimaryIdAttribute",
                cancellationToken).ConfigureAwait(false);
            if (!metaResponse.IsSuccess)
            {
                return MapClientError(tool, metaResponse, startedAt);
            }

            var entitySetName = GetString(metaResponse.Body!.Value, "EntitySetName");
            var primaryIdAttribute = GetString(metaResponse.Body.Value, "PrimaryIdAttribute");
            if (entitySetName is null || primaryIdAttribute is null)
            {
                return Error(tool, $"Table '{targetEntity}' metadata is incomplete.", ToolErrorCodes.InternalError, startedAt);
            }

            // 5) Execute the saved query server-side over OBO via the Web API fetchXml parameter.
            var response = await _dataverse.GetAsync(
                $"{entitySetName}?fetchXml={Uri.EscapeDataString(finalFetchXml)}",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return MapClientError(tool, response, startedAt);
            }

            // 6) Shape rows + citations; derive the count.
            var rows = new List<Dictionary<string, object?>>();
            var citations = new List<ToolResultCitation>();
            var warnings = new List<string>();
            long? totalRecordCount = null;
            var truncatedRows = false;

            if (response.Body is { } body)
            {
                if (body.TryGetProperty("@Microsoft.Dynamics.CRM.totalrecordcount", out var totalProp) &&
                    totalProp.ValueKind == JsonValueKind.Number &&
                    totalProp.TryGetInt64(out var total) && total >= 0)
                {
                    totalRecordCount = total;
                }
                if (body.TryGetProperty("@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded", out var exceededProp) &&
                    exceededProp.ValueKind == JsonValueKind.True)
                {
                    warnings.Add("The exact total exceeds the count limit (>5000); the count is a lower bound.");
                }

                if (body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var record in value.EnumerateArray())
                    {
                        if (rows.Count >= MaxRows)
                        {
                            truncatedRows = true;
                            break;
                        }

                        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        var recordId = Guid.Empty;
                        foreach (var property in record.EnumerateObject())
                        {
                            // Skip OData annotations (@odata.*) and formatted-value annotations
                            // (name contains '@'): rows stay grid-shaped raw values.
                            if (property.Name.Contains('@', StringComparison.Ordinal)) continue;
                            row[property.Name] = property.Value.ValueKind switch
                            {
                                JsonValueKind.Null => null,
                                JsonValueKind.String => property.Value.GetString(),
                                JsonValueKind.Number => property.Value.GetRawText(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => property.Value.Clone()
                            };
                            if (property.Name.Equals(primaryIdAttribute, StringComparison.OrdinalIgnoreCase) &&
                                property.Value.ValueKind == JsonValueKind.String &&
                                Guid.TryParse(property.Value.GetString(), out var parsedId))
                            {
                                recordId = parsedId;
                            }
                        }

                        if (recordId != Guid.Empty)
                        {
                            row["@citation.path"] = DataverseRecordCitations.RecordPath(targetEntity, recordId);
                            citations.Add(DataverseRecordCitations.ForRecord(targetEntity, recordId));
                        }
                        rows.Add(row);
                    }
                }
            }

            // Count precedence: accurate server total (returntotalrecordcount) when available;
            // otherwise the number of rows returned (honest lower bound for aggregate/capped views).
            var count = totalRecordCount ?? rows.Count;

            if (!todayInjected)
            {
                warnings.Add(
                    "The saved query contains no {{today}} placeholder; results reflect the view's own filter " +
                    "(a native relative-date operator still evaluates server-side).");
            }
            if (isAggregate)
            {
                warnings.Add("This saved view is an aggregate query; the row(s) carry the computed aggregate value.");
            }
            if (truncatedRows)
            {
                warnings.Add($"Only the first {MaxRows} rows are returned; use 'count' for the accurate total.");
            }

            stopwatch.Stop();
            // ADR-015 / NFR-07: configId + entity + counts + duration ONLY. NEVER the FetchXML body
            // (may embed maker literals) and NEVER row content.
            _logger.LogInformation(
                "[spaarke.grid_overview][ADR-015] configId={ConfigId} entity={Entity} count={Count} rows={RowCount} today={Today} todayInjected={TodayInjected} decisionId={DecisionId} durationMs={DurationMs}",
                configId, targetEntity, count, rows.Count, todayIso, todayInjected,
                context.DecisionId, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = ToolId,
                    configId = configId.ToString("D"),
                    view = viewName,
                    entity = targetEntity,
                    today = todayIso,
                    count,
                    rowCount = rows.Count,
                    rows,
                    citations = citations.Select(c => new { path = c.ChunkId, entity = c.SourceName })
                },
                summary: $"The '{viewName}' overview returned {count} record(s) for '{targetEntity}' as of {todayIso}, under the calling user's permissions.",
                confidence: 1.0,
                execution: Timed(startedAt),
                warnings: warnings) with
            {
                Metadata = citations.Count > 0
                    ? new Dictionary<string, object?> { [ToolResultMetadataKeys.Citations] = citations }
                    : null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(tool, "spaarke.grid_overview was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[spaarke.grid_overview] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "spaarke.grid_overview failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the FetchXML to run from a grid config's <c>sprk_configjson</c> <c>source</c> block
    /// (DataGrid framework <c>SourceConfig</c>). <c>savedquery</c> → the referenced savedquery's
    /// FetchXML (read over OBO from the <c>savedqueries</c> set); <c>inline</c> → the config's own
    /// <c>fetchXml</c>. Returns <c>(fetchXml, error)</c> with exactly one non-null. <c>savedquery-set</c>
    /// (auto-discover multiple sibling views) is ambiguous for a single overview run and is reported as
    /// an unsupported source. Added R4 UAT 2026-08-18 — see the ROOT CAUSE note at the call site.
    /// </summary>
    private async Task<(string? FetchXml, string? Error)> ResolveFetchXmlFromConfigAsync(
        string? configJson, Guid configId, CancellationToken cancellationToken)
    {
        var parsed = ParseGridSource(configJson, configId);
        if (parsed.Error is not null)
        {
            return (null, parsed.Error);
        }

        // inline source carries its own FetchXML — no Dataverse hop.
        if (parsed.InlineFetchXml is not null)
        {
            return (parsed.InlineFetchXml, null);
        }

        // savedquery source — read the referenced savedquery's FetchXML over the caller's OBO token
        // (a savedquery the user cannot read 404s here — same fail-closed grounding as the config read).
        var sqResponse = await _dataverse.GetAsync(
            $"savedqueries({parsed.SavedQueryId})?$select=fetchxml,name",
            cancellationToken).ConfigureAwait(false);
        if (!sqResponse.IsSuccess)
        {
            return (null, $"Grid configuration '{configId:D}' references savedquery '{parsed.SavedQueryId}', which could not be read.");
        }

        var sqFetch = GetString(sqResponse.Body!.Value, "fetchxml");
        return string.IsNullOrWhiteSpace(sqFetch)
            ? (null, $"Savedquery '{parsed.SavedQueryId}' has no FetchXML.")
            : (sqFetch, null);
    }

    /// <summary>
    /// Pure parse of a grid config's <c>sprk_configjson</c> <c>source</c> block (no I/O). Returns the
    /// resolved shape: for an <c>inline</c> source, <see cref="GridSourceParse.InlineFetchXml"/> is set;
    /// for a <c>savedquery</c> source, <see cref="GridSourceParse.SavedQueryId"/> is set (the caller does
    /// the OBO read); otherwise <see cref="GridSourceParse.Error"/> explains why. Exactly one of the three
    /// is non-null. Testable without a Dataverse double — the regression guard for the R4 UAT root cause.
    /// </summary>
    internal static GridSourceParse ParseGridSource(string? configJson, Guid configId)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return GridSourceParse.Fail($"Grid configuration '{configId:D}' has no configuration JSON (sprk_configjson is empty).");
        }

        JsonElement source;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("source", out var sourceEl)
                || sourceEl.ValueKind != JsonValueKind.Object)
            {
                return GridSourceParse.Fail($"Grid configuration '{configId:D}' configuration JSON has no 'source' block.");
            }

            source = sourceEl.Clone();
        }
        catch (JsonException)
        {
            return GridSourceParse.Fail($"Grid configuration '{configId:D}' configuration JSON is malformed.");
        }

        var sourceType = source.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
            ? typeEl.GetString()
            : null;

        switch (sourceType)
        {
            case "inline":
                var inlineFetch = source.TryGetProperty("fetchXml", out var fx) && fx.ValueKind == JsonValueKind.String
                    ? fx.GetString()
                    : null;
                return string.IsNullOrWhiteSpace(inlineFetch)
                    ? GridSourceParse.Fail($"Grid configuration '{configId:D}' inline source has no fetchXml.")
                    : GridSourceParse.Inline(inlineFetch!);

            case "savedquery":
                var savedQueryId = source.TryGetProperty("savedQueryId", out var sq) && sq.ValueKind == JsonValueKind.String
                    ? sq.GetString()
                    : null;
                return string.IsNullOrWhiteSpace(savedQueryId)
                    ? GridSourceParse.Fail($"Grid configuration '{configId:D}' savedquery source has no savedQueryId.")
                    : GridSourceParse.SavedQuery(savedQueryId!);

            default:
                return GridSourceParse.Fail(
                    $"Grid configuration '{configId:D}' source type '{sourceType ?? "(none)"}' is not supported by the overview tool " +
                    "(expected 'savedquery' or 'inline').");
        }
    }

    /// <summary>Result of <see cref="ParseGridSource"/>: exactly one of the three fields is non-null.</summary>
    internal readonly record struct GridSourceParse(string? InlineFetchXml, string? SavedQueryId, string? Error)
    {
        internal static GridSourceParse Inline(string fetchXml) => new(fetchXml, null, null);
        internal static GridSourceParse SavedQuery(string savedQueryId) => new(null, savedQueryId, null);
        internal static GridSourceParse Fail(string error) => new(null, null, error);
    }

    /// <summary>
    /// Substitutes the server date into the saved FetchXML's <c>{{today}}</c> / <c>{{today+N}}</c> /
    /// <c>{{today-N}}</c> placeholder tokens (whitespace-tolerant, case-insensitive). Returns the
    /// rewritten FetchXML plus whether any token was substituted. The offset form supports range
    /// predicates ("due this week" = <c>&gt;= {{today}}</c> AND <c>&lt; {{today+7}}</c>).
    /// </summary>
    internal static (string fetchXml, bool injected) InjectToday(string fetchXml, DateOnly today)
    {
        var injected = false;
        var result = TodayTokenRegex.Replace(fetchXml, match =>
        {
            injected = true;
            var date = today;
            if (match.Groups[2].Success)
            {
                var days = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                date = match.Groups[1].Value == "-" ? today.AddDays(-days) : today.AddDays(days);
            }
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        });
        return (result, injected);
    }

    /// <summary>
    /// Parses the FetchXML, reads the root entity name, detects an aggregate query, and (for
    /// non-aggregate queries only — <c>returntotalrecordcount</c> is incompatible with aggregates)
    /// ensures <c>returntotalrecordcount="true"</c> so the response carries an accurate total.
    /// </summary>
    internal static string NormalizeFetchXml(string fetchXml, out string? rootEntityName, out bool isAggregate)
    {
        var doc = XDocument.Parse(fetchXml);
        var fetch = doc.Root;
        if (fetch is null || !string.Equals(fetch.Name.LocalName, "fetch", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("FetchXML root element must be <fetch>.");
        }

        isAggregate = string.Equals(fetch.Attribute("aggregate")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        if (!isAggregate && fetch.Attribute("returntotalrecordcount") is null)
        {
            fetch.SetAttributeValue("returntotalrecordcount", "true");
        }

        var entity = fetch.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "entity", StringComparison.OrdinalIgnoreCase));
        rootEntityName = entity?.Attribute("name")?.Value;

        return fetch.ToString(SaveOptions.DisableFormatting);
    }

    internal static bool TryParseArgs(string? argsJson, out Guid configId, out string? error)
    {
        configId = Guid.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            error = "Tool arguments JSON is required (expected { \"configId\": \"<guid>\" }).";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must be a JSON object.";
                return false;
            }
            if (!doc.RootElement.TryGetProperty("configId", out var configProp) ||
                configProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(configProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'configId' string (the grid configuration GUID).";
                return false;
            }
            if (!Guid.TryParse(configProp.GetString(), out configId))
            {
                error = "'configId' must be a valid GUID identifying a sprk_gridconfiguration record.";
                return false;
            }
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }
    }

    private ToolResult MapClientError(AnalysisTool tool, DataverseUserResponse response, DateTimeOffset startedAt) =>
        ToolResult.Error(HandlerId, tool.Id, tool.Name,
            response.ErrorMessage ?? "Dataverse request failed.",
            response.ErrorCode,
            Timed(startedAt));

    private ToolResult Error(AnalysisTool tool, string message, string code, DateTimeOffset startedAt) =>
        ToolResult.Error(HandlerId, tool.Id, tool.Name, message, code, Timed(startedAt));

    private static ToolExecutionMetadata Timed(DateTimeOffset startedAt) =>
        new() { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow };

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
