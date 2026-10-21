using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// THE document narrowing for a caller who is not an internal systemuser (unified-access-control-r2 task 188, D7, #2126 /
/// ISS-015; owner direction 2026-10-20: "document access follows its parent, unless more restricted"). It runs AFTER the
/// root rule (<see cref="ExternalMultiRootRule"/>) on every external document surface and in the assistant's
/// <c>dataverse.*</c> tools, and it only ever hides: a document is visible only when the root rule allows it AND none of the
/// legs below fires.
/// </summary>
/// <remarks>
/// <para><b>The legs</b> (each hides the document):</para>
/// <list type="number">
/// <item><b>Own Restricted.</b> The document's own <c>sprk_accesspermission</c> reads Restricted (100000002), or a value
/// that is not a known level (unreadable). Null or absent counts as Standard, as everywhere else
/// (<see cref="InheritedAccessPermission.MostRestrictive"/>). Because visibility is "root AND own", an own value wider than
/// the root changes nothing: the root still decides.</item>
/// <item><b>An internal-only or non-Open communication.</b> The document's communication is its own
/// <c>_sprk_relatedcommunication_value</c>; when that is empty, its parent document's (<c>sprk_parentdocument</c>), then the
/// grandparent's: at most <see cref="MaxHops"/> parent hops. The first link found decides, through THE task 065 rule
/// (<see cref="CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync"/>, reused, never copied: flag JSON
/// <c>false</c>, a thread set, the thread explicitly Open now). Derived at read time, never stamped, so lowering the flag
/// (<c>CommunicationInternalOnlyFloor.LowerWhenSafeAsync</c>) shows the documents with nothing else written.</item>
/// <item><b>A Restricted ancestor</b> (main-session scope addition, 2026-10-20). On the SAME walk, an ancestor document whose
/// own value reads Restricted (or unreadable) hides the descendant: an attachment under a Restricted .eml is hidden. The
/// walk stops at the first link, so only the ancestors it visits are checked (no extra read).</item>
/// <item><b>An unknowable chain.</b> A link that is present but not a GUID, a parent that does not come back, a cycle, and
/// a chain that would need a fourth hop.</item>
/// </list>
/// <para><b>Fail closed.</b> <see cref="DecideAsync"/> THROWS on a failed read (the assistant's tools fail the call with no
/// data); <see cref="DecideOrFailClosedAsync"/> turns a failed read into "nothing visible" and says so
/// (<see cref="DocumentNarrowingResult.Faulted"/>: a list then reports <c>Truncated</c>). A document id the decision was
/// not asked about is not visible.</para>
/// <para><b>Cost</b> (task 188 notes, "Query cost"). No read for a row that decides on its own columns (no link needed, or
/// own Restricted). Otherwise one batched read per hop level over the DISTINCT parents still undecided (chunks of
/// <see cref="ChunkSize"/>; a parent already on the page is not read again), then the 065 rule's two batched reads
/// (communications, threads) over the DISTINCT communications. Never one read per row. No cache: an own Access Permission
/// change or a flag change applies on the next request.</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <see cref="ExternalMultiRootRule"/> decides by
/// ROOTS; the 065 rule decides a communication; r3 task 078's <c>ExternalCallerCommunicationGuard</c> applied one leg in the
/// assistant only. All are reused; 078's document leg is REMOVED in favour of this class. (2) Extension — the multi-root
/// rule's input is a row's root links and the caller's grants, read through the secure-flag and live-parent seams; this
/// predicate's input is the document's own columns and its communication chain, read through a different seam (one OData
/// reader shared with the assistant's user client), so it sits beside the rule rather than inside it. (3) Cost of doing
/// nothing — a partner with a grant on a matter downloads the .eml of an internal-only email and its attachments, and a
/// document a user marked Restricted (ISS-015), which the owner decided must not happen.</para>
/// </remarks>
internal static class ExternalDocumentNarrowing
{
    /// <summary>The document table.</summary>
    internal const string DocumentTable = "sprk_document";

    /// <summary>The document entity set the hop reads target.</summary>
    internal const string DocumentSet = "sprk_documents";

    /// <summary>The document primary key.</summary>
    internal const string DocumentPkField = "sprk_documentid";

    /// <summary>The document's own Access Permission (a choice column).</summary>
    internal const string AccessPermissionField = InheritedAccessPermission.Column;

    /// <summary>The document → communication lookup (logical name: the FetchXML attribute).</summary>
    internal const string RelatedCommunicationAttribute = "sprk_relatedcommunication";

    /// <summary>The document → parent document lookup (logical name: the FetchXML attribute).</summary>
    internal const string ParentDocumentAttribute = "sprk_parentdocument";

    /// <summary>The document → communication lookup as it reads in an OData row.</summary>
    internal const string RelatedCommunicationValue = "_sprk_relatedcommunication_value";

    /// <summary>The document → parent document lookup as it reads in an OData row.</summary>
    internal const string ParentDocumentValue = "_sprk_parentdocument_value";

    /// <summary>The most parent hops the communication walk takes (parent, grandparent, great-grandparent).</summary>
    internal const int MaxHops = 3;

    /// <summary>Ids per batched read: the 065 rule's chunk, so one OR filter stays well inside the URL limit.</summary>
    internal const int ChunkSize = CommunicationOpenThreadGate.ChunkSize;

    /// <summary>A link that is present but unreadable: never read and never visible (the 065 rule's sentinel).</summary>
    internal static readonly Guid UnreadableLink = CommunicationOpenThreadGate.UnreadableLink;

    /// <summary>
    /// The Web API <c>$select</c> columns every surface that decides a document must read (pinned by test: a dropped column
    /// reads as "not set" and would open the leg it feeds).
    /// </summary>
    internal static IReadOnlyList<string> SelectColumns { get; } =
        new[] { AccessPermissionField, RelatedCommunicationValue, ParentDocumentValue };

    /// <summary>
    /// The FetchXML attributes a document module projects server-side (the SDK omits a null attribute, so an unprojected
    /// one reads as "not set"), with the primary id the decision is keyed on.
    /// </summary>
    internal static IReadOnlyList<string> FetchAttributes { get; } =
        new[] { DocumentPkField, AccessPermissionField, RelatedCommunicationAttribute, ParentDocumentAttribute };

    /// <summary>The OData query of one hop read (and of a by-id read of search hits).</summary>
    private static string HopQuery(IReadOnlyCollection<Guid> ids) =>
        $"$select={DocumentPkField},{string.Join(",", SelectColumns)}" +
        $"&$filter={string.Join(" or ", ids.Select(id => $"{DocumentPkField} eq {id:D}"))}&$top={ids.Count}";

    // ── Row parsing ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One OData document row (its <c>$select</c> must carry <see cref="SelectColumns"/>), keyed by column.</summary>
    internal static DocumentNarrowingRow FromWebApi(Guid documentId, IReadOnlyDictionary<string, JsonElement>? row) =>
        FromJson(documentId, column => row is not null && row.TryGetValue(column, out var v) ? v : null);

    /// <summary>One OData document record whose id is <paramref name="documentId"/> (a single-record read).</summary>
    internal static DocumentNarrowingRow FromWebApi(Guid documentId, JsonElement record) =>
        FromJson(documentId, column => record.ValueKind == JsonValueKind.Object && record.TryGetProperty(column, out var v) ? v : null);

    /// <summary>One OData document record of a collection read: the id is its <see cref="DocumentPkField"/> (none: hidden).</summary>
    internal static DocumentNarrowingRow FromWebApi(JsonElement record) =>
        FromWebApi(
            record.ValueKind == JsonValueKind.Object && record.TryGetProperty(DocumentPkField, out var id)
            && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var parsed)
                ? parsed
                : Guid.Empty,
            record);

    /// <summary>
    /// One FetchXML (module) row, keyed by LOGICAL name as the SDK projects it: a lookup is an <see cref="EntityReference"/>,
    /// a GUID or a GUID string; the choice is an <see cref="OptionSetValue"/> or an int. Absent or null is "not set" (the SDK
    /// omits a null attribute); anything else is unreadable.
    /// </summary>
    internal static DocumentNarrowingRow FromFetch(IReadOnlyDictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var id = ExternalModuleDescriptor.TryGetAttributeId(row, DocumentPkField, out var parsed) ? parsed : Guid.Empty;
        var own = !row.TryGetValue(AccessPermissionField, out var raw) || raw is null
            ? DocumentPermission.NotNarrowed
            : raw switch
            {
                OptionSetValue option => PermissionOf(option.Value),
                int value => PermissionOf(value),
                _ => DocumentPermission.Unreadable,
            };
        return new DocumentNarrowingRow(id, own, FetchLink(row, RelatedCommunicationAttribute), FetchLink(row, ParentDocumentAttribute));
    }

    private static Guid FetchLink(IReadOnlyDictionary<string, object?> row, string attribute)
    {
        if (!row.TryGetValue(attribute, out var raw) || raw is null)
            return Guid.Empty; // not set
        return ExternalModuleDescriptor.TryGetAttributeId(row, attribute, out var id) ? id : UnreadableLink;
    }

    private static DocumentNarrowingRow FromJson(Guid documentId, Func<string, JsonElement?> valueOf)
    {
        var own = valueOf(AccessPermissionField) is { } permission
            ? permission.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => DocumentPermission.NotNarrowed,
                JsonValueKind.Number when permission.TryGetInt32(out var value) => PermissionOf(value),
                _ => DocumentPermission.Unreadable,
            }
            : DocumentPermission.NotNarrowed;
        return new DocumentNarrowingRow(
            documentId, own, JsonLink(valueOf(RelatedCommunicationValue)), JsonLink(valueOf(ParentDocumentValue)));
    }

    /// <summary>Absent or null: <see cref="Guid.Empty"/> (not set); a non-empty GUID string: that id; anything else unreadable.</summary>
    private static Guid JsonLink(JsonElement? value) => value is not { } v
        ? Guid.Empty
        : v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => Guid.Empty,
            JsonValueKind.String when Guid.TryParse(v.GetString(), out var id) && id != Guid.Empty => id,
            _ => UnreadableLink,
        };

    /// <summary>A known level other than Restricted narrows nothing; an unknown option is never ranked by guess.</summary>
    private static DocumentPermission PermissionOf(int value) => value switch
    {
        InheritedAccessPermission.Standard or InheritedAccessPermission.Limited => DocumentPermission.NotNarrowed,
        InheritedAccessPermission.Restricted => DocumentPermission.Restricted,
        _ => DocumentPermission.Unreadable,
    };

    // ── The decision ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decides <paramref name="rows"/> (each as read by its surface, with <see cref="SelectColumns"/> /
    /// <see cref="FetchAttributes"/>). A failed read THROWS (the caller's request fails with no data).
    /// </summary>
    /// <param name="rows">The rows that passed the root rule.</param>
    /// <param name="query">The reader, under the identity the surface reads with (app-only on the external plane).</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task<DocumentNarrowingResult> DecideAsync(
        IEnumerable<DocumentNarrowingRow> rows, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(query);

        // Keyed by the row AS READ (a value): a row is decided on its own columns and chain, so one that came back without its
        // id is still decided, and two copies of one row decide alike.
        var decided = new Dictionary<DocumentNarrowingRow, DocumentNarrowingReason>();
        var communicationOf = new Dictionary<DocumentNarrowingRow, Guid>();
        var pending = new Dictionary<DocumentNarrowingRow, (Guid Next, HashSet<Guid> Visited)>();
        // Every row already in hand: a parent that is on the same page is not read again.
        var known = new Dictionary<Guid, DocumentNarrowingRow>();

        var input = rows.Distinct().ToList();
        foreach (var row in input.Where(r => r.DocumentId != Guid.Empty && r.DocumentId != UnreadableLink))
            known.TryAdd(row.DocumentId, row);

        foreach (var row in input)
        {
            if (row.Own != DocumentPermission.NotNarrowed)
            {
                decided[row] = row.Own == DocumentPermission.Restricted
                    ? DocumentNarrowingReason.OwnRestricted
                    : DocumentNarrowingReason.OwnUnreadable;
                continue;
            }

            var visited = new HashSet<Guid>();
            if (row.DocumentId != Guid.Empty)
                visited.Add(row.DocumentId);
            Step(row, row, hop: 0, visited);
        }

        for (var hop = 1; hop <= MaxHops && pending.Count > 0; hop++)
        {
            var unread = pending.Values.Select(p => p.Next).Where(id => !known.ContainsKey(id)).Distinct().ToList();
            foreach (var chunk in unread.Chunk(ChunkSize))
            {
                var asked = chunk.ToHashSet();
                var found = await query(DocumentSet, HopQuery(chunk), ct).ConfigureAwait(false);
                foreach (var parent in found.Select(r => FromWebApi(IdOf(r), r)))
                {
                    if (asked.Contains(parent.DocumentId))
                        known.TryAdd(parent.DocumentId, parent); // only what was asked: an answer for another id is ignored
                }
            }

            var current = pending;
            pending = new Dictionary<DocumentNarrowingRow, (Guid Next, HashSet<Guid> Visited)>();
            foreach (var (subject, walk) in current)
            {
                if (!known.TryGetValue(walk.Next, out var ancestor))
                {
                    decided[subject] = DocumentNarrowingReason.ParentNotFound;
                    continue;
                }

                Step(subject, ancestor, hop, walk.Visited);
            }
        }

        // Defensive: the loop above decides every walk by MaxHops (Step refuses a hop past it).
        foreach (var subject in pending.Keys)
            decided[subject] = DocumentNarrowingReason.ChainTooLong;

        if (communicationOf.Count > 0)
        {
            var visible = await CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync(
                communicationOf.Values, query, ct).ConfigureAwait(false);
            foreach (var (subject, communicationId) in communicationOf)
            {
                decided[subject] = visible.Contains(communicationId)
                    ? DocumentNarrowingReason.Visible
                    : DocumentNarrowingReason.CommunicationHidden;
            }
        }

        return new DocumentNarrowingResult(decided, faulted: false);

        // One node of a subject's chain: the subject itself (hop 0) or the ancestor reached at `hop`.
        void Step(DocumentNarrowingRow subject, DocumentNarrowingRow node, int hop, HashSet<Guid> visited)
        {
            if (hop > 0 && node.Own != DocumentPermission.NotNarrowed)
            {
                decided[subject] = node.Own == DocumentPermission.Restricted
                    ? DocumentNarrowingReason.AncestorRestricted
                    : DocumentNarrowingReason.AncestorUnreadable;
                return;
            }

            if (node.Communication == UnreadableLink)
            {
                decided[subject] = DocumentNarrowingReason.LinkUnreadable;
                return;
            }

            if (node.Communication != Guid.Empty)
            {
                communicationOf[subject] = node.Communication; // the first link found decides
                return;
            }

            if (node.Parent == Guid.Empty)
            {
                decided[subject] = DocumentNarrowingReason.Visible; // no link anywhere in the chain
                return;
            }

            if (node.Parent == UnreadableLink)
            {
                decided[subject] = DocumentNarrowingReason.ParentUnreadable;
                return;
            }

            if (visited.Contains(node.Parent))
            {
                decided[subject] = DocumentNarrowingReason.Cycle;
                return;
            }

            if (hop >= MaxHops)
            {
                decided[subject] = DocumentNarrowingReason.ChainTooLong;
                return;
            }

            visited.Add(node.Parent);
            pending[subject] = (node.Parent, visited);
        }
    }

    /// <summary>
    /// <see cref="DecideAsync"/> for the external plane: a failed read (other than the caller's own cancellation) hides
    /// every row and says so (<see cref="DocumentNarrowingResult.Faulted"/>), logged with counts only.
    /// </summary>
    internal static async Task<DocumentNarrowingResult> DecideOrFailClosedAsync(
        IReadOnlyCollection<DocumentNarrowingRow> rows, CommunicationOpenThreadGate.RowQuery query, ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            return await DecideAsync(rows, query, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-NARROWING] The document narrowing read for {Count} document(s) failed. Failing CLOSED: none of them is "
                + "shown.", rows.Count);
            return DocumentNarrowingResult.ReadFailed;
        }
    }

    /// <summary>
    /// For hits that carry no columns (the assistant's <c>dataverse.search_data</c>): reads the documents by id (batched,
    /// <see cref="SelectColumns"/>), then decides them. A document that does not come back is not visible. A failed read
    /// THROWS.
    /// </summary>
    internal static async Task<DocumentNarrowingResult> ReadAndDecideAsync(
        IEnumerable<Guid> documentIds, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(query);
        var distinct = documentIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var rows = new List<DocumentNarrowingRow>();
        foreach (var chunk in distinct.Chunk(ChunkSize))
        {
            var asked = chunk.ToHashSet();
            var found = await query(DocumentSet, HopQuery(chunk), ct).ConfigureAwait(false);
            rows.AddRange(found.Select(r => FromWebApi(IdOf(r), r)).Where(r => asked.Contains(r.DocumentId)));
        }

        return await DecideAsync(rows, query, ct).ConfigureAwait(false);
    }

    private static Guid IdOf(IReadOnlyDictionary<string, JsonElement> row) =>
        row.TryGetValue(DocumentPkField, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var id)
            ? id
            : Guid.Empty;
}

/// <summary>A document's own Access Permission as the narrowing reads it.</summary>
internal enum DocumentPermission
{
    /// <summary>Null, absent, Standard or Limited: narrows nothing.</summary>
    NotNarrowed = 0,

    /// <summary>Restricted: hides the document (and, on the walk, its descendants).</summary>
    Restricted = 1,

    /// <summary>A value that is not a known level: hides (fail closed).</summary>
    Unreadable = 2,
}

/// <summary>
/// What the narrowing needs of one document: its id, its own Access Permission, its communication link and its parent
/// document link (<see cref="Guid.Empty"/>: not set; <see cref="ExternalDocumentNarrowing.UnreadableLink"/>: unreadable).
/// Built only from a row as read (<see cref="ExternalDocumentNarrowing.FromWebApi(Guid, IReadOnlyDictionary{string, JsonElement})"/>,
/// <see cref="ExternalDocumentNarrowing.FromFetch"/>).
/// </summary>
internal readonly record struct DocumentNarrowingRow(Guid DocumentId, DocumentPermission Own, Guid Communication, Guid Parent);

/// <summary>Why a document is hidden (or <see cref="Visible"/>). Logged as counts per reason code only.</summary>
internal enum DocumentNarrowingReason
{
    Visible = 0,
    OwnRestricted,
    OwnUnreadable,
    AncestorRestricted,
    AncestorUnreadable,
    CommunicationHidden,
    LinkUnreadable,
    ParentUnreadable,
    ParentNotFound,
    Cycle,
    ChainTooLong,
    ReadFailed,
}

/// <summary>
/// The narrowing's answer for one request (<see cref="ExternalDocumentNarrowing.DecideAsync"/>). A document it was not
/// asked about is NOT visible.
/// </summary>
internal sealed class DocumentNarrowingResult
{
    private readonly IReadOnlyDictionary<DocumentNarrowingRow, DocumentNarrowingReason> _byRow;
    private readonly Dictionary<Guid, DocumentNarrowingReason> _byId = new();

    internal DocumentNarrowingResult(IReadOnlyDictionary<DocumentNarrowingRow, DocumentNarrowingReason> decided, bool faulted)
    {
        _byRow = decided;
        Faulted = faulted;
        // By id: an id is visible only when every row read for it is (two different reads of one document cannot open it).
        foreach (var (row, reason) in decided)
        {
            if (row.DocumentId == Guid.Empty)
                continue;
            if (!_byId.TryGetValue(row.DocumentId, out var earlier) || earlier == DocumentNarrowingReason.Visible)
                _byId[row.DocumentId] = reason;
        }
    }

    /// <summary>The read failed: nothing is visible, and a list that hid rows says <c>Truncated</c>.</summary>
    internal static DocumentNarrowingResult ReadFailed { get; } =
        new(new Dictionary<DocumentNarrowingRow, DocumentNarrowingReason>(), faulted: true);

    /// <summary>
    /// An internal systemuser (the 065 rule's own exemption, <c>CommunicationThreadReadService</c>): nothing is narrowed, as
    /// on the internal plane, where a child's value is display only and internal-only email is theirs to read.
    /// </summary>
    internal static DocumentNarrowingResult NotNarrowed(IEnumerable<DocumentNarrowingRow> rows) =>
        new(rows.Distinct().ToDictionary(row => row, _ => DocumentNarrowingReason.Visible), faulted: false);

    /// <summary><c>true</c> when the read failed (as opposed to a document the rule hides).</summary>
    internal bool Faulted { get; }

    /// <summary>Whether <paramref name="row"/> (one of the rows decided) is visible. A row not decided is not.</summary>
    internal bool IsVisible(DocumentNarrowingRow row) => ReasonFor(row) == DocumentNarrowingReason.Visible;

    /// <summary>Whether the document <paramref name="documentId"/> is visible: every row decided for it is. An id not
    /// decided is not.</summary>
    internal bool IsVisible(Guid documentId) => ReasonFor(documentId) == DocumentNarrowingReason.Visible;

    /// <summary>The decision for <paramref name="row"/>; <see cref="DocumentNarrowingReason.ReadFailed"/> when the read failed
    /// or the row was not decided.</summary>
    internal DocumentNarrowingReason ReasonFor(DocumentNarrowingRow row) =>
        _byRow.TryGetValue(row, out var reason) ? reason : DocumentNarrowingReason.ReadFailed;

    /// <summary>The decision for <paramref name="documentId"/>; <see cref="DocumentNarrowingReason.ReadFailed"/> when the read
    /// failed or the document was not decided.</summary>
    internal DocumentNarrowingReason ReasonFor(Guid documentId) =>
        documentId != Guid.Empty && _byId.TryGetValue(documentId, out var reason) ? reason : DocumentNarrowingReason.ReadFailed;

    /// <summary>The log reason code of <paramref name="reason"/> (ADR-015: codes and counts only).</summary>
    internal static string CodeOf(DocumentNarrowingReason reason) => reason switch
    {
        DocumentNarrowingReason.Visible => "visible",
        DocumentNarrowingReason.OwnRestricted => "own_restricted",
        DocumentNarrowingReason.OwnUnreadable => "own_unreadable",
        DocumentNarrowingReason.AncestorRestricted => "ancestor_restricted",
        DocumentNarrowingReason.AncestorUnreadable => "ancestor_unreadable",
        DocumentNarrowingReason.CommunicationHidden => "related_communication_hidden",
        DocumentNarrowingReason.LinkUnreadable => "link_unreadable",
        DocumentNarrowingReason.ParentUnreadable => "parent_unreadable",
        DocumentNarrowingReason.ParentNotFound => "parent_not_found",
        DocumentNarrowingReason.Cycle => "chain_cycle",
        DocumentNarrowingReason.ChainTooLong => "chain_too_long",
        _ => "read_failed",
    };

    /// <summary>The hidden count per reason code among <paramref name="reasons"/> (one log line; codes and counts only).</summary>
    internal static string Summary(IEnumerable<DocumentNarrowingReason> reasons) =>
        string.Join(",", reasons
            .Where(r => r != DocumentNarrowingReason.Visible)
            .GroupBy(r => r)
            .OrderBy(g => g.Key)
            .Select(g => $"{CodeOf(g.Key)}={g.Count()}"));

    /// <summary><see cref="Summary"/> over the decisions for <paramref name="rows"/>.</summary>
    internal string HiddenSummary(IEnumerable<DocumentNarrowingRow> rows) => Summary(rows.Select(ReasonFor));

    /// <summary><see cref="Summary"/> over the decisions for <paramref name="documentIds"/>.</summary>
    internal string HiddenSummary(IEnumerable<Guid> documentIds) => Summary(documentIds.Select(ReasonFor));
}

/// <summary>
/// Task 188: the narrowing a surface applies to the document rows that passed the root rule, for <paramref name="caller"/>
/// (<see cref="ExternalDataService.NarrowDocumentsAsync"/>: an internal systemuser is not narrowed; every other caller is;
/// a failed read hides every row).
/// </summary>
internal delegate Task<DocumentNarrowingResult> DocumentNarrower(
    CallerPrincipal caller, IReadOnlyCollection<DocumentNarrowingRow> rows, CancellationToken ct);
