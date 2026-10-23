using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>dataverse.read_query</c> tool — SQL SELECT over
/// Dataverse data via the user-OBO Web API (spaarke-ai-architecture-redesign-r1 task 008,
/// FR-P0-07 read half).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 contract freeze</b>: name + argument shape mirror the GA Dataverse MCP
/// <c>read_query</c> tool — a single required <c>querytext</c> string carrying a SQL SELECT
/// (see <see cref="DataverseToolNames"/> for the frozen-name citation). The accepted dialect
/// and rejection vocabulary are documented on
/// <see cref="DataverseSqlQueryTranslator"/>; SELECT-only parsing is the mechanical
/// enforcement of <c>side_effect_class = read</c>.
/// </para>
/// <para>
/// <b>User-OBO ONLY (spec MUST rule)</b>: every query executes through
/// <see cref="IDataverseUserClient"/> under the calling user's Dataverse security roles +
/// row-level security. Rows the user cannot read are absent from results — enforced by
/// Dataverse, not by BFF filtering. No app-only client is reachable (task-012 audit), except the one below.
/// </para>
/// <para>
/// <b>The "User-OBO ONLY" rule is AMENDED for ONE dependency — unified-access-control-r2 task 188, main-session decision
/// 2026-10-20 (CLAUDE.md §6.5 path B; APPROVED, owner round 111, 2026-10-21)</b>: an EXTERNAL caller's <c>sprk_document</c> rows
/// are decided by THE external document narrowing through <see cref="AppOnlyDocumentNarrowing"/>, which re-reads those
/// documents BY ID under the BFF's app-only identity (the external plane's reader) and returns ONLY a hide decision: a
/// link field security masks for the user can never read as "no link". It can only HIDE a row the user's own read returned;
/// no app-only data reaches the result, and nothing else of the app-only reader is reachable (a delegate). Every query whose
/// rows reach the model still runs as the user.
/// Task 188 round 3 slice C: an external caller's <c>sprk_analysis</c> and <c>sprk_analysisoutput</c> rows are hidden when
/// their source document or an analysis's output file is (or cannot be decided), through the same seam's hide-only overload
/// (<see cref="AppOnlyDocumentNarrowingExtensions.HiddenDocumentIdsAsync"/>); their source links are read as the user
/// (<see cref="ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync"/>).
/// </para>
/// <para>
/// <b>Grounding (ADR-039)</b>: every returned row carries the table's primary id (added to
/// the $select automatically when the model omits it) plus a GA-MCP-style citation path
/// (<c>tables/{t}/records/{id}</c>) so P2 citation enforcement can anchor answers to records.
/// </para>
/// <para>
/// <b>ADR-015 / NFR-07</b>: telemetry carries entity + column COUNT + row COUNT + duration —
/// never querytext (may embed user-authored literals), never row content.
/// </para>
/// <para>
/// <b>ISS-010 quick fix (external-access r3 task 078)</b>: for an EXTERNAL caller (systemuser flagged
/// <c>sprk_isexternal</c>, or unresolvable — fail closed) a <c>sprk_communication</c> query is ANDed with the task 065
/// rule (the Open-thread half through the thread navigation property) and re-checked after the read (flag and Open
/// thread); any other communication-family table (the prefix plus the content tables outside it) is refused before any
/// Dataverse request; a <c>sprk_document</c> whose related communication fails the rule is dropped after the read. See
/// <see cref="ExternalCallerCommunicationGuard"/>. Internal callers and other tables are unchanged.
/// </para>
/// </remarks>
public sealed class DataverseReadQueryHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DataverseReadQueryHandler);

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<DataverseReadQueryHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly ISystemUserIdentityResolver _identityResolver;
    /// <summary>Task 188: THE document narrowing, app-only by id (see the remarks: the one amended dependency).</summary>
    private readonly AppOnlyDocumentNarrowing _narrowDocuments;

    public DataverseReadQueryHandler(
        IDataverseUserClient dataverse,
        ILogger<DataverseReadQueryHandler> logger,
        IHttpContextAccessor httpContextAccessor,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        AppOnlyDocumentNarrowing narrowDocuments)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _callerResolver = callerResolver ?? throw new ArgumentNullException(nameof(callerResolver));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _narrowDocuments = narrowDocuments ?? throw new ArgumentNullException(nameof(narrowDocuments));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <summary>Task 188: a row's primary id (<see cref="Guid.Empty"/> when it is absent or not a GUID: never visible).</summary>
    private static Guid RecordIdOf(JsonElement record, string primaryIdAttribute) =>
        record.ValueKind == JsonValueKind.Object && record.TryGetProperty(primaryIdAttribute, out var id) &&
        id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var parsed)
            ? parsed
            : Guid.Empty;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Dataverse Read Query",
        // FR-A-01 (AIR2-020): mirror of the authored sprk_description in infra/dataverse/sprk_analysistool-dataverse-read-query-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: @"Executes a read-only SQL SELECT against Dataverse under the calling user's permissions and returns rows with record-id citations. Use dataverse.describe FIRST to get the table's column logical names and types, then construct a query that strictly follows the supported dialect: SELECT [TOP n] explicit column list FROM one table [WHERE simple column-to-literal predicates combined with AND/OR] [ORDER BY column ASC|DESC]. NOT supported: SELECT *, JOIN, GROUP BY, aggregates (COUNT/SUM/AVG/MIN/MAX), subqueries, DISTINCT, HAVING, UNION, OFFSET, GETDATE(). For relative-date filters (overdue, due today, due this week), use the current date already given to you in your system context as a literal value in the WHERE clause (for example, WHERE sprk_duedate < '2025-01-15') — dynamic date functions are not supported. For distributions/aggregations (counts by category), SELECT the category column across rows (TOP up to 200) and aggregate the values YOURSELF from the result. LOOKUP columns (e.g. sprk_practicearea on sprk_matter) ARE selectable and return the referenced record's GUID — query the referenced table (e.g. sprk_practicearea_ref) to map GUIDs to display names. Choice columns return numeric option values — map them to labels via dataverse.describe. Default row cap 50 (TOP max 200). Results respect the current user's Dataverse security roles and row-level security. Spaarke entity map (use these logical names): 'matter'/'case'/'deal' = sprk_matter (key columns sprk_mattername, sprk_matternumber, sprk_matterdescription, statuscode; lookups sprk_practicearea to sprk_practicearea_ref, sprk_mattertype to sprk_mattertype_ref, sprk_assignedattorney1 to contact, sprk_externalaccount to account). 'project'/'workstream' = sprk_project (sprk_projectname, sprk_projectnumber). 'document'/'contract'/'file' = sprk_document (sprk_documentname, sprk_filename, sprk_documenttype, sprk_filesummary; lookups sprk_matter, sprk_project). People = contact; client companies = account; law firms/vendors = sprk_organization.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "querytext",
                "The SELECT SQL query to execute. Use dataverse.describe to get table/column logical names and " +
                "types first, then construct a query that strictly follows the supported dialect.",
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
            "DataverseReadQueryHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "DataverseReadQueryHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed));

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (!TryParseArgs(context.ToolArgumentsJson, out var querytext, out var error))
            return ToolValidationResult.Failure(error!);

        // Fail fast on unsupported SQL at validation time so the adapter surfaces the
        // LLM-actionable message before any Dataverse round-trip.
        var translation = DataverseSqlQueryTranslator.Translate(querytext);
        if (!translation.Success)
            return ToolValidationResult.Failure(translation.Error!);

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

        if (!TryParseArgs(context.ToolArgumentsJson, out var querytext, out var parseError))
        {
            return Error(tool, parseError!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        var translation = DataverseSqlQueryTranslator.Translate(querytext);
        if (!translation.Success)
        {
            return Error(tool, translation.Error!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        try
        {
            // ISS-010 (task 078): externality is decided ONLY for a communication-family table or sprk_document, so every
            // other request is byte-identical. An external caller is refused any family table but sprk_communication
            // BEFORE any Dataverse request (the metadata GET included); sprk_document is row-filtered by its related
            // communication (review round 1, F1).
            var guardCommunications = false;
            var guardDocuments = false;
            var guardDerived = false;
            if ((ExternalCallerCommunicationGuard.IsCommunicationFamily(translation.EntityLogicalName) ||
                 ExternalCallerCommunicationGuard.IsDocumentTable(translation.EntityLogicalName) ||
                 ExternalCallerCommunicationGuard.IsDocumentDerivedTable(translation.EntityLogicalName)) &&
                await ExternalCallerCommunicationGuard.IsExternalCallerAsync(
                    _httpContextAccessor, _callerResolver, _identityResolver, cancellationToken).ConfigureAwait(false))
            {
                if (ExternalCallerCommunicationGuard.IsDocumentTable(translation.EntityLogicalName))
                {
                    guardDocuments = true;
                }
                else if (ExternalCallerCommunicationGuard.IsDocumentDerivedTable(translation.EntityLogicalName))
                {
                    // Task 188 round 3 slice C: decided after the read by row id; nothing is injected into the query.
                    guardDerived = true;
                }
                else if (!ExternalCallerCommunicationGuard.IsCommunicationTable(translation.EntityLogicalName))
                {
                    _logger.LogInformation(
                        "[dataverse.read_query] " + ExternalCallerCommunicationGuard.LogEvent + " entity={Entity} reason={Reason} decisionId={DecisionId}",
                        translation.EntityLogicalName, "family_table_refused", context.DecisionId);
                    return Error(tool, ExternalCallerCommunicationGuard.RefusalMessage,
                        DataverseUserClientErrorCodes.AccessDenied, startedAt);
                }
                else
                {
                    guardCommunications = true;
                }
            }

            // Resolve entity-set + primary id from metadata (under the user's token — a table
            // invisible to this user 404s here, which is the correct fail-closed behavior).
            // R5-C (2026-07-07): also fetch attribute types so LOOKUP columns can be rewritten
            // to their Web-API `_{name}_value` form — selecting a lookup by its logical name
            // (e.g. sprk_practicearea on sprk_matter) previously failed the whole query with
            // "property does not exist", which the model relayed as a broken column.
            var metaResponse = await _dataverse.GetAsync(
                $"EntityDefinitions(LogicalName='{translation.EntityLogicalName}')?$select=EntitySetName,PrimaryIdAttribute" +
                "&$expand=Attributes($select=LogicalName,AttributeType)",
                cancellationToken).ConfigureAwait(false);
            if (!metaResponse.IsSuccess)
            {
                return MapClientError(tool, metaResponse, startedAt);
            }

            var entitySetName = GetString(metaResponse.Body!.Value, "EntitySetName");
            var primaryIdAttribute = GetString(metaResponse.Body.Value, "PrimaryIdAttribute");
            if (entitySetName is null || primaryIdAttribute is null)
            {
                return Error(tool, $"Table '{translation.EntityLogicalName}' metadata is incomplete.", ToolErrorCodes.InternalError, startedAt);
            }

            // R5-C: rewrite referenced LOOKUP/Customer/Owner columns to `_{name}_value` in
            // $select / $filter / $orderby (metadata-aware; the pure translator can't know
            // column types). Row keys are mapped BACK to the requested logical names below,
            // so the model sees the columns it asked for (values = referenced-record GUIDs).
            var lookupAttributes = ExtractLookupAttributeNames(metaResponse.Body.Value);
            var mappedLookups = translation.Columns!
                .Where(lookupAttributes.Contains)
                .ToList();
            string odataQuery;
            var threadColumnInjected = false;
            var internalOnlyColumnInjected = false;
            if (guardCommunications || guardDocuments)
            {
                // ISS-010 (task 078): on sprk_communication the task 065 rule rides in the query: the model's whole
                // predicate in parentheses, ANDed with flag-false, thread-set and thread-Open (review K1). The thread and
                // flag columns are selected so both are re-checked after the read (review K4). A sprk_document row is decided
                // after the read by its id alone (task 188: the predicate re-reads it app-only), so nothing is injected.
                var guardedColumns = translation.Columns!
                    .Select(c => lookupAttributes.Contains(c) ? ToValueForm(c) : c)
                    .ToList();
                if (guardCommunications)
                {
                    threadColumnInjected = AddColumnIfAbsent(guardedColumns, CommunicationOpenThreadGate.MessageThreadLookupValue);
                    internalOnlyColumnInjected = AddColumnIfAbsent(guardedColumns, CommunicationInternalOnlyFloor.InternalOnlyField);
                }

                var rewrittenFilter = RewriteLookupReferences(translation.RawFilter, lookupAttributes);
                odataQuery = DataverseSqlQueryTranslator.AssembleODataQuery(
                    guardedColumns,
                    guardCommunications ? ExternalCallerCommunicationGuard.ApplyFilter(rewrittenFilter) : rewrittenFilter,
                    RewriteLookupReferences(translation.RawOrderBy, lookupAttributes),
                    translation.Top);
            }
            else if (mappedLookups.Count > 0 ||
                ContainsAnyLookup(translation.RawFilter, lookupAttributes) ||
                ContainsAnyLookup(translation.RawOrderBy, lookupAttributes))
            {
                var mappedColumns = translation.Columns!
                    .Select(c => lookupAttributes.Contains(c) ? ToValueForm(c) : c);
                odataQuery = DataverseSqlQueryTranslator.AssembleODataQuery(
                    mappedColumns,
                    RewriteLookupReferences(translation.RawFilter, lookupAttributes),
                    RewriteLookupReferences(translation.RawOrderBy, lookupAttributes),
                    translation.Top);
            }
            else
            {
                odataQuery = translation.ODataQuery!;
            }

            // ADR-039 grounding: guarantee the primary id is selected so every row is citable.
            var primaryIdInjected = false;
            if (!translation.Columns!.Contains(primaryIdAttribute, StringComparer.OrdinalIgnoreCase))
            {
                odataQuery = odataQuery.Replace("$select=", $"$select={primaryIdAttribute},", StringComparison.Ordinal);
                primaryIdInjected = true;
            }

            var response = await _dataverse.GetAsync($"{entitySetName}?{odataQuery}", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return MapClientError(tool, response, startedAt);
            }

            // ISS-010 (task 078): the Open-thread half of the rule, checked after the read (thread privacy lives on another
            // table). One batched read of the distinct threads; a fault throws into the error result below (no rows).
            HashSet<Guid>? openThreads = null;
            if (guardCommunications &&
                response.Body is { } guardedBody && guardedBody.TryGetProperty("value", out var guardedValue) &&
                guardedValue.ValueKind == JsonValueKind.Array)
            {
                openThreads = await CommunicationOpenThreadGate.ReadOpenThreadIdsAsync(
                    guardedValue.EnumerateArray().Select(ExternalCallerCommunicationGuard.ThreadIdOf).ToList(),
                    ExternalCallerCommunicationGuard.ThreadQueryOver(_dataverse),
                    cancellationToken).ConfigureAwait(false);
            }

            // Task 188 (D7, #2126): THE external document predicate decides each document row (own or ancestor Restricted;
            // the related communication of the document or, on a bounded parent walk, of its ancestor, under the 065 rule).
            // The rows are re-read BY ID APP-ONLY (main-session decision 2026-10-20), never decided on the columns the user's
            // read returned, so a link field security masks for the user cannot read as "no link". Batched reads; a fault
            // throws into the error result below (no rows). A row without a readable id is hidden.
            DocumentNarrowingResult? narrowing = null;
            if (guardDocuments &&
                response.Body is { } documentBody && documentBody.TryGetProperty("value", out var documentValue) &&
                documentValue.ValueKind == JsonValueKind.Array)
            {
                var documentIds = documentValue.EnumerateArray()
                    .Select(record => RecordIdOf(record, primaryIdAttribute))
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
                narrowing = await _narrowDocuments(documentIds, cancellationToken).ConfigureAwait(false);
            }

            // Task 188 round 3 slice C: a row that derives from a document (sprk_analysis; sprk_analysisoutput through its
            // analysis) is hidden when its source document is. The source links are read BY ROW ID as the user (one batched
            // read per level), then the hide-only overload decides the documents APP-ONLY in ONE call for the page. A row that
            // has a source the BFF cannot decide is hidden; a row with no source document is unaffected. Faults throw (no rows).
            IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? derivedSources = null;
            IReadOnlySet<Guid>? hiddenSources = null;
            if (guardDerived &&
                response.Body is { } derivedBody && derivedBody.TryGetProperty("value", out var derivedValue) &&
                derivedValue.ValueKind == JsonValueKind.Array)
            {
                var rowIds = derivedValue.EnumerateArray()
                    .Select(record => RecordIdOf(record, primaryIdAttribute))
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
                derivedSources = await ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync(
                    _dataverse, translation.EntityLogicalName!, rowIds, cancellationToken).ConfigureAwait(false);
                hiddenSources = await _narrowDocuments.HiddenDocumentIdsAsync(derivedSources.Values.SelectMany(documents => documents), cancellationToken)
                    .ConfigureAwait(false);
            }

            var rows = new List<Dictionary<string, object?>>();
            var citations = new List<ToolResultCitation>();
            var hiddenRows = 0;
            var hiddenDocuments = new List<Guid>();
            var hiddenDerivedSources = new List<Guid>();
            // Task 188 round 4a (F1-5): the row cap is measured on the RAW page Dataverse returned, before any row is
            // hidden — a page that hit TOP stays reported as capped even when the narrowing removed rows from it.
            var rawPageLength = 0;
            if (response.Body is { } body && body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            {
                rawPageLength = value.GetArrayLength();
                foreach (var record in value.EnumerateArray())
                {
                    if (guardCommunications &&
                        (!ExternalCallerCommunicationGuard.IsExplicitlyShareable(record) ||
                         openThreads is null || !openThreads.Contains(ExternalCallerCommunicationGuard.ThreadIdOf(record))))
                    {
                        hiddenRows++;
                        continue;
                    }

                    if (guardDocuments &&
                        RecordIdOf(record, primaryIdAttribute) is var documentId &&
                        (narrowing is null || !narrowing.IsVisible(documentId)))
                    {
                        hiddenDocuments.Add(documentId);
                        continue;
                    }

                    if (guardDerived &&
                        RecordIdOf(record, primaryIdAttribute) is var derivedId &&
                        ExternalCallerCommunicationGuard.IsDerivedRowHidden(derivedId, derivedSources, hiddenSources, out var source))
                    {
                        hiddenDerivedSources.Add(source);
                        continue;
                    }

                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    Guid recordId = Guid.Empty;
                    foreach (var property in record.EnumerateObject())
                    {
                        if (property.Name.StartsWith("@odata", StringComparison.OrdinalIgnoreCase)) continue;
                        // R5-C: map `_{col}_value` back to the logical name the model asked
                        // for — the rewrite is a transport detail, not the model's problem.
                        var propertyName = FromValueForm(property.Name, lookupAttributes);
                        row[propertyName] = property.Value.ValueKind switch
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

                    // The model did not ask for these columns; they were selected only for the guard.
                    if (threadColumnInjected)
                    {
                        row.Remove(FromValueForm(CommunicationOpenThreadGate.MessageThreadLookupValue, lookupAttributes));
                    }
                    if (internalOnlyColumnInjected)
                    {
                        row.Remove(CommunicationInternalOnlyFloor.InternalOnlyField);
                    }

                    if (recordId != Guid.Empty)
                    {
                        row["@citation.path"] = DataverseRecordCitations.RecordPath(translation.EntityLogicalName!, recordId);
                        citations.Add(DataverseRecordCitations.ForRecord(translation.EntityLogicalName!, recordId));
                    }
                    rows.Add(row);
                }
            }

            if (hiddenRows > 0)
            {
                // ADR-015: entity + reason code only — never query text or row content.
                _logger.LogInformation(
                    "[dataverse.read_query] " + ExternalCallerCommunicationGuard.LogEvent + " entity={Entity} reason={Reason} decisionId={DecisionId}",
                    translation.EntityLogicalName, "thread_not_open", context.DecisionId);
            }
            if (hiddenDocuments.Count > 0)
            {
                // ADR-015: entity + reason codes with counts only — never query text or row content.
                _logger.LogInformation(
                    "[dataverse.read_query] " + ExternalCallerCommunicationGuard.LogEvent + " entity={Entity} reason={Reason} decisionId={DecisionId}",
                    translation.EntityLogicalName,
                    narrowing?.HiddenSummary(hiddenDocuments) ?? DocumentNarrowingResult.CodeOf(DocumentNarrowingReason.ReadFailed),
                    context.DecisionId);
            }
            if (hiddenDerivedSources.Count > 0)
            {
                // ADR-015: entity + reason codes with counts only — never query text, ids or row content.
                _logger.LogInformation(
                    "[dataverse.read_query] " + ExternalCallerCommunicationGuard.LogEvent + " entity={Entity} reason={Reason} decisionId={DecisionId}",
                    translation.EntityLogicalName,
                    ExternalCallerCommunicationGuard.DerivedHiddenSummary(hiddenDerivedSources),
                    context.DecisionId);
            }

            stopwatch.Stop();
            // ADR-015 / NFR-07: entity + counts + duration ONLY. NEVER querytext (may embed
            // user-authored literals). NEVER row content.
            _logger.LogInformation(
                "[dataverse.read_query][ADR-015] entity={Entity} columns={ColumnCount} rows={RowCount} top={Top} decisionId={DecisionId} durationMs={DurationMs}",
                translation.EntityLogicalName, translation.Columns!.Count, rows.Count, translation.Top,
                context.DecisionId, stopwatch.ElapsedMilliseconds);

            var warnings = new List<string>();
            if (primaryIdInjected)
            {
                warnings.Add($"Primary id column '{primaryIdAttribute}' was added to the selection automatically so results are citable.");
            }
            if (mappedLookups.Count > 0)
            {
                // R5-C: transparency for the model — lookup values are GUIDs, not labels.
                warnings.Add(
                    $"Lookup column(s) {string.Join(", ", mappedLookups.Select(c => $"'{c}'"))} return the referenced " +
                    "record's GUID. To map GUIDs to display names, query the referenced table " +
                    "(dataverse.describe shows the related table per lookup column).");
            }
            if (rawPageLength >= translation.Top)
            {
                warnings.Add($"Result set reached the row cap ({translation.Top}). Narrow the WHERE clause or raise TOP (max {DataverseSqlQueryTranslator.MaxTop}).");
            }

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = DataverseToolNames.ReadQuery,
                    entity = translation.EntityLogicalName,
                    columns = translation.Columns,
                    rows,
                    rowCount = rows.Count,
                    citations = citations.Select(c => new { path = c.ChunkId, entity = c.SourceName })
                },
                summary: $"Query returned {rows.Count} row(s) from '{translation.EntityLogicalName}' under the calling user's permissions.",
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
            return Error(tool, "dataverse.read_query was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[dataverse.read_query] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "dataverse.read_query failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the logical names of LOOKUP-shaped attributes (Lookup / Customer / Owner)
    /// from the expanded EntityDefinitions metadata (R5-C, 2026-07-07). These columns are
    /// addressed as <c>_{name}_value</c> on the Web API — selecting/filtering by logical
    /// name fails with "property does not exist".
    /// </summary>
    internal static HashSet<string> ExtractLookupAttributeNames(JsonElement metadata)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!metadata.TryGetProperty("Attributes", out var attributes) ||
            attributes.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var attribute in attributes.EnumerateArray())
        {
            if (attribute.ValueKind != JsonValueKind.Object) continue;
            if (!attribute.TryGetProperty("AttributeType", out var typeProp) ||
                typeProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var type = typeProp.GetString();
            if (type is not ("Lookup" or "Customer" or "Owner")) continue;
            if (attribute.TryGetProperty("LogicalName", out var nameProp) &&
                nameProp.ValueKind == JsonValueKind.String &&
                nameProp.GetString() is { Length: > 0 } name)
            {
                result.Add(name);
            }
        }

        return result;
    }

    /// <summary>Adds <paramref name="column"/> to the select list when absent; true when it was added (strip it from rows).</summary>
    private static bool AddColumnIfAbsent(List<string> columns, string column)
    {
        if (columns.Contains(column, StringComparer.OrdinalIgnoreCase)) return false;
        columns.Add(column);
        return true;
    }

    /// <summary>The Web API address form of a lookup column (<c>_{name}_value</c>).</summary>
    internal static string ToValueForm(string logicalName) => $"_{logicalName}_value";

    /// <summary>
    /// Maps a response property back to the requested logical name when it is the
    /// <c>_{name}_value</c> form of a known lookup column; other names pass through.
    /// </summary>
    internal static string FromValueForm(string propertyName, HashSet<string> lookupAttributes)
    {
        if (propertyName.Length > 7 &&
            propertyName[0] == '_' &&
            propertyName.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = propertyName[1..^6];
            if (lookupAttributes.Contains(candidate))
            {
                return candidate;
            }
        }

        return propertyName;
    }

    /// <summary>True when the raw filter/orderby text references any lookup column as a whole word.</summary>
    internal static bool ContainsAnyLookup(string? rawClause, HashSet<string> lookupAttributes)
    {
        if (string.IsNullOrEmpty(rawClause) || lookupAttributes.Count == 0) return false;
        return lookupAttributes.Any(col =>
            System.Text.RegularExpressions.Regex.IsMatch(
                rawClause, $@"\b{System.Text.RegularExpressions.Regex.Escape(col)}\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Rewrites whole-word lookup column references to <c>_{name}_value</c> OUTSIDE quoted
    /// string literals (a literal like 'sprk_practicearea' must never be rewritten).
    /// Splitting on single quotes puts literals at ODD indices — only EVEN segments rewrite.
    /// </summary>
    internal static string? RewriteLookupReferences(string? rawClause, HashSet<string> lookupAttributes)
    {
        if (string.IsNullOrEmpty(rawClause) || lookupAttributes.Count == 0) return rawClause;

        var segments = rawClause.Split('\'');
        for (var i = 0; i < segments.Length; i += 2)
        {
            foreach (var col in lookupAttributes)
            {
                segments[i] = System.Text.RegularExpressions.Regex.Replace(
                    segments[i],
                    $@"\b{System.Text.RegularExpressions.Regex.Escape(col)}\b",
                    ToValueForm(col),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
        }

        return string.Join("'", segments);
    }

    internal static bool TryParseArgs(string? argsJson, out string querytext, out string? error)
    {
        querytext = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            error = "Tool arguments JSON is required (expected { \"querytext\": \"SELECT …\" }).";
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
            if (!doc.RootElement.TryGetProperty("querytext", out var queryProp) ||
                queryProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(queryProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'querytext' string carrying a SQL SELECT statement.";
                return false;
            }
            querytext = queryProp.GetString()!;
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
