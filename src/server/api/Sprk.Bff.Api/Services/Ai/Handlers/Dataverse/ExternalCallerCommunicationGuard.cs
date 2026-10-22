using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

/// <summary>
/// The ISS-010 quick fix (external-access r3 task 078, owner decision 2026-10-20, #1864) for the assistant's GENERIC
/// Dataverse read tools (<c>dataverse.read_query</c>, <c>dataverse.search_data</c>, <c>dataverse.describe</c> record
/// paths). Those tools run under the caller's OBO token, so without this guard Dataverse security alone decides, and a
/// licensed systemuser flagged <c>sprk_isexternal</c> (owner 2026-10-14: treated like a partner for Internal Only) could
/// read an internal-only or Private-thread communication that task 065 hides on the BFF's own reads.
/// </summary>
/// <remarks>
/// <para><b>The rule (task 065, reused, never redefined).</b> An external caller sees a <c>sprk_communication</c> only
/// when <see cref="CommunicationInternalOnlyFloor.InternalOnlyField"/> is <c>false</c> (true or unset hides) AND its
/// thread lookup (<see cref="CommunicationOpenThreadGate.MessageThreadLookupValue"/>) is set AND that thread reads Open
/// now (<see cref="CommunicationOpenThreadGate.ReadOpenThreadIdsAsync"/>). Every other communication-family table (logical
/// name starting <see cref="FamilyPrefix"/>: threads, attachments, participants, accounts, rules, channel refs) and every
/// table in <see cref="RefusedContentTables"/> (email or message content copied OUTSIDE the prefix) is refused outright
/// for an external caller. A <c>sprk_document</c> is row-filtered by THE external document predicate
/// (<see cref="ExternalDocumentNarrowing"/>, unified-access-control-r2 task 188, which replaced this class's own
/// related-communication check): own or ancestor Restricted, or a communication the caller may not see on the bounded
/// parent-document walk, hides it. The handlers reach it only through <see cref="AppOnlyDocumentNarrowing"/>, which
/// re-reads the documents by id app-only, so none of its reads runs over the caller's user client. A row that DERIVES from a
/// document (<see cref="AnalysisTable"/>, and <see cref="AnalysisOutputTable"/> through its analysis; task 188 round 3 slice C)
/// is hidden when its source document or its output file is, or when it has one that cannot be decided (all three tools,
/// <c>describe</c> included): its links are read as the user
/// (<see cref="ReadSourceDocumentIdsAsync"/>), the documents are decided by the hide-only overload
/// (<see cref="AppOnlyDocumentNarrowingExtensions.HiddenDocumentIdsAsync"/>). A row with no source document is unaffected.</para>
/// <para><b>Who is external.</b> Decided only when the request touches the communication family, <c>sprk_document</c> or a
/// document-derived table,
/// so other requests are byte-identical. The request principal → <see cref="ICallerSystemUserResolver"/> →
/// <see cref="ISystemUserIdentityResolver.IsExternalAsync"/>. A caller with no principal, no oid or no active systemuser
/// is EXTERNAL (fail closed); a caller whose lookup FAILED (a swallowed Dataverse or cache fault) makes the tool call fail
/// with no data, never "internal".</para>
/// <para><b>Logs (ADR-015).</b> Callers log the entity and a reason code only; this class logs nothing.</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — the row rule lives in
/// <see cref="CommunicationInternalOnlyFloor"/> / <see cref="CommunicationOpenThreadGate"/> and caller externality in
/// <see cref="ICallerSystemUserResolver"/> + <see cref="ISystemUserIdentityResolver"/>; all are reused as they are, no
/// name is redefined. (2) Extension — those read the communication services' own query seams; the generic tools hold only
/// an <see cref="IDataverseUserClient"/> and build OData from model SQL, so the family predicate, the query clause and
/// the user-client reads the three handlers share sit here beside the other <c>dataverse.*</c> helpers.
/// (3) Cost of doing nothing — a licensed user flagged external reads internal-only email and message bodies through the
/// assistant (ISS-010), which the owner decided must not happen.</para>
/// </remarks>
internal static class ExternalCallerCommunicationGuard
{
    /// <summary>Every table whose logical name starts with this is in the communication family.</summary>
    internal const string FamilyPrefix = "sprk_communication";

    /// <summary>The one family table an external caller may read (row-filtered).</summary>
    internal const string CommunicationTable = "sprk_communication";

    /// <summary>
    /// The communication → thread single-valued navigation property (solution XML
    /// <c>Other/Relationships/sprk_CommunicationThread.xml</c>, relationship
    /// <c>sprk_communication_CommunicationThread_sprk_communicationthread</c>, referencing role). Puts the Open-thread
    /// half of the rule into the query (review K1).
    /// </summary>
    internal const string ThreadNavigationProperty = "sprk_CommunicationThread";

    /// <summary>The document table: row-filtered by <see cref="ExternalDocumentNarrowing"/>, never refused.</summary>
    internal const string DocumentTable = ExternalDocumentNarrowing.DocumentTable;

    /// <summary>
    /// <see cref="CallerSystemUserResolution.UnresolvedReason"/> for a lookup fault the resolver swallowed (pinned by a
    /// test against the real <see cref="CallerSystemUserResolver"/>).
    /// </summary>
    internal const string LookupFailedReason = CallerExternality.LookupFailedReason;

    /// <summary>
    /// Tables OUTSIDE <see cref="FamilyPrefix"/> that hold copies of email or message content and carry no link this guard
    /// filters on, so they are refused like the family (review round 1, F1): <c>sprk_emailartifact</c> (Outlook add-in
    /// save: body preview, sender, recipients — <c>UploadFinalizationWorker</c>), <c>sprk_attachmentartifact</c> (that
    /// save's attachment file names), <c>sprk_emailreviewlog</c> (AI suggestions and source refs derived from the email —
    /// <c>CommunicationEnrichmentService</c>) and <c>sprk_affinity</c> (sender addresses, subject keywords and participant
    /// sets learned from filed email — <c>AffinityStore</c>).
    /// </summary>
    internal static readonly IReadOnlySet<string> RefusedContentTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_emailartifact",
        "sprk_attachmentartifact",
        "sprk_emailreviewlog",
        "sprk_affinity",
    };

    /// <summary>Log event name for every guard decision (entity + reason code only, ADR-015).</summary>
    internal const string LogEvent = "sdap.ai.tool.external_communication_guard";

    /// <summary>The model-actionable refusal for a guarded table other than <see cref="CommunicationTable"/>.</summary>
    internal const string RefusalMessage =
        "This table is not available to the calling user through the assistant. Communications (emails and messages) " +
        "can be read from the sprk_communication table only.";

    /// <summary>The model-facing answer for a hidden record AND for a record the user client cannot find (review K4).</summary>
    internal const string RecordNotVisibleMessage = "The record was not found or is not visible to the calling user.";

    /// <summary>
    /// True when <paramref name="logicalName"/> is in the communication family: the <see cref="FamilyPrefix"/> or one of
    /// the <see cref="RefusedContentTables"/>. The ONE predicate all three handlers use. <c>sprk_document</c> is not in it
    /// (it is row-filtered, see <see cref="IsDocumentTable"/>).
    /// </summary>
    internal static bool IsCommunicationFamily(string? logicalName) =>
        logicalName is not null &&
        (logicalName.StartsWith(FamilyPrefix, StringComparison.OrdinalIgnoreCase) || RefusedContentTables.Contains(logicalName));

    /// <summary>True for the <c>sprk_communication</c> table itself.</summary>
    internal static bool IsCommunicationTable(string? logicalName) =>
        string.Equals(logicalName, CommunicationTable, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the <c>sprk_document</c> table (row-filtered by <see cref="ExternalDocumentNarrowing"/>).</summary>
    internal static bool IsDocumentTable(string? logicalName) =>
        string.Equals(logicalName, DocumentTable, StringComparison.OrdinalIgnoreCase);

    // ── Task 188 round 3 slice C: rows that DERIVE from a document ────────────────────────────────────────────────

    /// <summary>The analysis table: an AI analysis of ONE source document (<c>sprk_documentid</c>, NOT NULL).</summary>
    internal const string AnalysisTable = "sprk_analysis";

    /// <summary>An analysis's outputs (<c>sprk_value</c>: the text the analysis produced from its source document).</summary>
    internal const string AnalysisOutputTable = "sprk_analysisoutput";

    /// <summary>
    /// How a derived table reaches its documents: its entity set and primary id, the lookups it is read through, and the
    /// derived table those lookups target (<c>null</c>: each lookup IS a document). A row is hidden when ANY of its documents
    /// is hidden or cannot be decided.
    /// </summary>
    private sealed record DerivedTable(string EntitySet, string IdColumn, IReadOnlyList<string> LinkValues, string? Via);

    /// <summary>
    /// Tables whose rows carry a document's content or text derived from it, keyed to the documents they derive from (task 188
    /// notes §16, the surface classification). An analysis follows its SOURCE document (<c>sprk_documentid</c>) AND its saved
    /// OUTPUT FILE (<c>sprk_outputfileid</c>, the same text as a document; coordinator decision OD-C1) AND its typed regarding
    /// document (<c>sprk_regardingdocument</c>, task 188 round 4b: an analysis ABOUT a document); an output follows its
    /// analysis. Entity sets and lookups as read live (<c>SecureChildLineage</c>: note <c>sprk_analysises</c>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, DerivedTable> DocumentDerivedTables =
        new Dictionary<string, DerivedTable>(StringComparer.OrdinalIgnoreCase)
        {
            [AnalysisTable] = new("sprk_analysises", "sprk_analysisid",
                new[] { "_sprk_documentid_value", "_sprk_outputfileid_value", "_sprk_regardingdocument_value" }, Via: null),
            [AnalysisOutputTable] = new("sprk_analysisoutputs", "sprk_analysisoutputid",
                new[] { "_sprk_analysisid_value" }, Via: AnalysisTable),
        };

    /// <summary>
    /// True for a table whose rows derive from a document (<see cref="AnalysisTable"/>, <see cref="AnalysisOutputTable"/>):
    /// an external caller's row is hidden when one of its documents is (<see cref="ReadSourceDocumentIdsAsync"/>, then
    /// <see cref="AppOnlyDocumentNarrowingExtensions.HiddenDocumentIdsAsync"/>).
    /// </summary>
    internal static bool IsDocumentDerivedTable(string? logicalName) =>
        logicalName is not null && DocumentDerivedTables.ContainsKey(logicalName);

    /// <summary>
    /// The documents each derived row in <paramref name="rowIds"/> derives from, read BY ID over the caller's own user client
    /// (the rows are the user's; only the hide decision on the documents runs app-only). An EMPTY list: the row has no
    /// document (it is unaffected). <see cref="ExternalDocumentNarrowing.UnreadableLink"/> in the list: the row HAS a document
    /// that cannot be decided — the row (or, for an output, its analysis) does not come back, or a lookup is present but not a
    /// GUID — so the caller hides it (fail closed). One batched read per level (OR filter, chunks of
    /// <see cref="CommunicationOpenThreadGate.ChunkSize"/>); an output adds the level of its analyses. A failed read THROWS.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ReadSourceDocumentIdsAsync(
        IDataverseUserClient dataverse, string logicalName, IReadOnlyCollection<Guid> rowIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(rowIds);
        if (!DocumentDerivedTables.TryGetValue(logicalName, out var table))
            throw new ArgumentOutOfRangeException(nameof(logicalName), logicalName, "Not a document-derived table.");

        var distinct = rowIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var links = new Dictionary<Guid, IReadOnlyList<Guid>>();
        var chunks = await CommunicationOpenThreadGate.ReadChunksAsync(distinct, (chunk, c) => ReadRowsAsync(dataverse,
            $"{table.EntitySet}?$select={table.IdColumn},{string.Join(",", table.LinkValues)}" +
            $"&$filter={string.Join(" or ", chunk.Select(id => $"{table.IdColumn} eq {id:D}"))}&$top={chunk.Length}",
            $"{logicalName} source link", c), ct).ConfigureAwait(false);
        foreach (var (chunk, rows) in chunks)
        {
            var asked = chunk.ToHashSet();
            foreach (var row in rows)
            {
                if (LinkOf(row, table.IdColumn) is { } id && id != ExternalDocumentNarrowing.UnreadableLink && asked.Contains(id))
                {
                    links[id] = table.LinkValues
                        .Select(column => LinkOf(row, column))
                        .Where(link => link is not null)
                        .Select(link => link!.Value)
                        .Distinct()
                        .ToList();
                }
            }
        }

        // A row that did not come back has a document the caller cannot decide.
        IReadOnlyList<Guid> undecidable = new[] { ExternalDocumentNarrowing.UnreadableLink };
        var result = distinct.ToDictionary(id => id, id => links.TryGetValue(id, out var found) ? found : undecidable);
        if (table.Via is null)
            return result;

        var viaIds = result.Values.SelectMany(v => v).Where(IsUsableId).Distinct().ToList();
        var viaSources = await ReadSourceDocumentIdsAsync(dataverse, table.Via, viaIds, ct).ConfigureAwait(false);
        return result.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<Guid>)kv.Value
                .SelectMany(via => IsUsableId(via) ? viaSources[via] : new[] { via })
                .Distinct()
                .ToList());
    }

    /// <summary>
    /// Whether the derived row <paramref name="rowId"/> is hidden, given its documents (<see cref="ReadSourceDocumentIdsAsync"/>)
    /// and the hidden set (<see cref="AppOnlyDocumentNarrowingExtensions.HiddenDocumentIdsAsync"/>): hidden when ANY of its
    /// documents is. A row with no document is not; a row that was not resolved (no usable id, or no decision at all) is, as
    /// undecidable (fail closed). <paramref name="source"/> is what hid it, for the log summary.
    /// </summary>
    internal static bool IsDerivedRowHidden(
        Guid rowId, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? sources, IReadOnlySet<Guid>? hidden, out Guid source)
    {
        source = ExternalDocumentNarrowing.UnreadableLink;
        if (sources is null || hidden is null || !sources.TryGetValue(rowId, out var documents))
            return true;

        foreach (var document in documents)
        {
            if (hidden.Contains(document))
            {
                source = document;
                return true;
            }
        }

        return false;
    }

    private static bool IsUsableId(Guid id) => id != Guid.Empty && id != ExternalDocumentNarrowing.UnreadableLink;

    /// <summary>A lookup value of one OData row: <c>null</c> when absent or null (not set); the sentinel when present but not a
    /// usable GUID.</summary>
    private static Guid? LinkOf(Dictionary<string, JsonElement> row, string column) =>
        !row.TryGetValue(column, out var v) || v.ValueKind == JsonValueKind.Null
            ? null
            : v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var id) && id != Guid.Empty
                ? id
                : ExternalDocumentNarrowing.UnreadableLink;

    /// <summary>
    /// The log reason of the derived rows a handler hid (ADR-015: codes and counts only): <c>source_document_hidden</c> for a
    /// source document THE narrowing hides, <c>source_document_undecidable</c> for one that cannot be decided.
    /// </summary>
    internal static string DerivedHiddenSummary(IEnumerable<Guid> hiddenSources)
    {
        var sources = hiddenSources.ToList();
        var undecidable = sources.Count(id => id == ExternalDocumentNarrowing.UnreadableLink);
        return string.Join(",", new[]
        {
            sources.Count - undecidable > 0 ? $"source_document_hidden={sources.Count - undecidable}" : null,
            undecidable > 0 ? $"source_document_undecidable={undecidable}" : null,
        }.Where(s => s is not null));
    }

    /// <summary>
    /// The query-side rule: flag explicitly false, a thread set, and that thread's privacy explicitly Open (through
    /// <see cref="ThreadNavigationProperty"/>, review K1, so TOP counts only rows the caller may see). The Open-thread half
    /// is still re-checked after the read (<see cref="CommunicationOpenThreadGate.ReadOpenThreadIdsAsync"/>).
    /// </summary>
    internal static string FilterClause =>
        $"{CommunicationInternalOnlyFloor.InternalOnlyField} eq false and {CommunicationOpenThreadGate.MessageThreadLookupValue} ne null" +
        $" and {ThreadNavigationProperty}/{CommunicationOpenThreadGate.OpenOnlyThreadFilterClause}";

    /// <summary>
    /// The model's filter ANDed with <see cref="FilterClause"/>. The model's whole predicate is parenthesised so an OR in
    /// it can never widen the guard (the translator only emits balanced parentheses).
    /// </summary>
    internal static string ApplyFilter(string? modelFilter) =>
        string.IsNullOrEmpty(modelFilter) ? FilterClause : $"({modelFilter}) and {FilterClause}";

    /// <summary>
    /// True when the request's caller is external: flagged <c>sprk_isexternal</c>, or not resolvable to an active
    /// systemuser (no principal, no oid, no matching active row). A lookup that FAILED (<see cref="LookupFailedReason"/>: a
    /// Dataverse or cache fault the resolver swallowed) THROWS, as does a failing externality read, so the caller's tool
    /// call fails with no data — a transient fault never decides who the caller is. Delegates to the one definition,
    /// <see cref="CallerExternality.IsExternalCallerAsync"/> (ebilling-r1 task 099 moved it there unchanged).
    /// </summary>
    internal static Task<bool> IsExternalCallerAsync(
        IHttpContextAccessor httpContextAccessor,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        CancellationToken ct) =>
        CallerExternality.IsExternalCallerAsync(httpContextAccessor.HttpContext?.User, callerResolver, identityResolver, ct);

    /// <summary>
    /// The thread-privacy read for <see cref="CommunicationOpenThreadGate.ReadOpenThreadIdsAsync"/> over the caller's own
    /// user client. A failed read THROWS, so a fault becomes the tool's error result with no rows — never "show it".
    /// </summary>
    internal static CommunicationOpenThreadGate.ThreadRowQuery ThreadQueryOver(IDataverseUserClient dataverse) =>
        (odata, ct) => ReadRowsAsync(dataverse, $"{CommunicationOpenThreadGate.ThreadSet}?{odata}", "communication thread privacy", ct);

    /// <summary>The thread id of one OData communication row (raw <c>_sprk_communicationthread_value</c>), or empty.</summary>
    internal static Guid ThreadIdOf(JsonElement record) =>
        record.TryGetProperty(CommunicationOpenThreadGate.MessageThreadLookupValue, out var v)
        && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var id)
            ? id
            : Guid.Empty;

    /// <summary>True only when the row's <c>sprk_isinternalonly</c> reads explicitly <c>false</c>.</summary>
    internal static bool IsExplicitlyShareable(JsonElement record) =>
        record.TryGetProperty(CommunicationInternalOnlyFloor.InternalOnlyField, out var v) && v.ValueKind == JsonValueKind.False;

    private static async Task<IReadOnlyList<Dictionary<string, JsonElement>>> ReadRowsAsync(
        IDataverseUserClient dataverse, string path, string what, CancellationToken ct)
    {
        var response = await dataverse.GetAsync(path, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The {what} read failed ({response.ErrorCode}); the rows stay hidden (fail closed).");
        }

        var rows = new List<Dictionary<string, JsonElement>>();
        if (response.Body is { } body && body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var record in value.EnumerateArray())
            {
                if (record.ValueKind != JsonValueKind.Object) continue;
                var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in record.EnumerateObject())
                {
                    row[property.Name] = property.Value.Clone();
                }
                rows.Add(row);
            }
        }

        return rows;
    }
}
