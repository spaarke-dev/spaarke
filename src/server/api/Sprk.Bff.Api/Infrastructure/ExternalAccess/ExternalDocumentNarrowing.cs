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
/// <item><b>A Restricted ancestor</b> (main-session scope addition and decision, 2026-10-20). On the SAME walk, ANY
/// ancestor document (parent, grandparent, great-grandparent) whose own value reads Restricted (or unreadable) hides the
/// descendant: an attachment under a Restricted .eml is hidden, even when the attachment carries its own visible link. The
/// walk therefore continues past the first communication link to the top of the chain (at most <see cref="MaxHops"/>
/// hops); only the communication leg stops at the first link.</item>
/// <item><b>An unknowable chain.</b> A link that is present but not a GUID, a parent that does not come back, a cycle, and
/// a chain that would need a fourth hop, even for a document whose own link is visible (the ancestor leg cannot be
/// decided past the bound; accepted by the main session, 2026-10-20).</item>
/// </list>
/// <para><b>Whose reads.</b> The predicate is a security check, so every read it makes (the documents' own columns, the
/// parent chain, the communications and the threads) runs under the BFF's app-only identity, the external plane's reader
/// (<see cref="ExternalDataService"/>), on EVERY surface. The assistant's tools reach it only through
/// <see cref="AppOnlyDocumentNarrowing"/>, which re-reads the documents by id app-only: a column field security masks for
/// the user (it reads as null) can never read as "no link" here.</para>
/// <para><b>Fail closed.</b> <see cref="DecideAsync"/> THROWS on a failed read (the assistant's tools fail the call with no
/// data); <see cref="DecideOrFailClosedAsync"/> turns a failed read into "nothing visible" and says so
/// (<see cref="DocumentNarrowingResult.Faulted"/>: a list then reports <c>Truncated</c>). A document id the decision was
/// not asked about is not visible.</para>
/// <para><b>Cost</b> (task 188 notes, "Query cost"). No read for a row that decides on its own columns (own Restricted, or
/// no link and no parent). Otherwise one batched read per hop level over the DISTINCT parents still undecided (OR-filter
/// chunks of <see cref="ChunkSize"/> ids, the URL bound, up to <see cref="CommunicationOpenThreadGate.MaxChunksInFlight"/>
/// in flight at once; a parent already on the page or read at an earlier hop is not read again), then the 065 rule's two
/// batched reads (communications, threads) over the DISTINCT communications, chunked the same way. Never one read per row.
/// The counts are returned (<see cref="DocumentNarrowingResult.Cost"/>) and logged once per request by the seam. No cache:
/// an own Access Permission change or a flag change applies on the next request.</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <see cref="ExternalMultiRootRule"/> decides by
/// ROOTS; the 065 rule decides a communication; r3 task 078's <c>ExternalCallerCommunicationGuard</c> applied one leg in the
/// assistant only. All are reused; 078's document leg is REMOVED in favour of this class. (2) Extension — the multi-root
/// rule's input is a row's root links and the caller's grants, read through the secure-flag and live-parent seams; this
/// predicate's input is the document's own columns and its communication chain, read through a different seam (one
/// app-only OData reader, the external plane's, on every surface), so it sits beside the rule rather than inside it. (3) Cost of doing
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

    /// <summary>
    /// ebilling-r1 task 098: <see cref="FromWebApi(Guid, IReadOnlyDictionary{string, JsonElement})"/> that also reads the
    /// submission marker (its <c>$select</c> must also carry <see cref="SubmissionMarkerColumns"/>) when
    /// <paramref name="readSubmissionMarker"/> is set. The row is marked only when BOTH <see cref="EmailArchiveField"/> and
    /// <see cref="SubmissionMarkerField"/> read exactly JSON <c>true</c>: absent, null, false or any other value is not marked
    /// (fail closed: an unmarked Restricted source hides the invoice). Only a decision that honours the marker
    /// (<see cref="DecideAsync(IEnumerable{DocumentNarrowingRow}, CommunicationOpenThreadGate.RowQuery, CancellationToken, bool)"/>)
    /// looks at it.
    /// </summary>
    internal static DocumentNarrowingRow FromWebApi(
        Guid documentId, IReadOnlyDictionary<string, JsonElement>? row, bool readSubmissionMarker)
    {
        var parsed = FromWebApi(documentId, row);
        return readSubmissionMarker && row is not null && IsJsonTrue(row, EmailArchiveField) && IsJsonTrue(row, SubmissionMarkerField)
            ? parsed with { SubmissionMarked = true }
            : parsed;
    }

    private static bool IsJsonTrue(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>ebilling-r1 task 098: the document is an archived email (.eml).</summary>
    internal const string EmailArchiveField = "sprk_isemailarchive";

    /// <summary>
    /// ebilling-r1 tasks 088 / 098: the submission marker on an .eml
    /// (<see cref="Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.SubmissionMarkerColumn"/>): the Restricted narrowing on it was
    /// written by the invoice submission, in the same create (procedure-and-file-model 9.1).
    /// </summary>
    internal const string SubmissionMarkerField = Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.SubmissionMarkerColumn;

    /// <summary>ebilling-r1 task 098: the extra <c>$select</c> columns a marker-reading row needs.</summary>
    internal static IReadOnlyList<string> SubmissionMarkerColumns { get; } = new[] { EmailArchiveField, SubmissionMarkerField };

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

    /// <summary>
    /// Owner round 122: one SDK row (a <c>QueryExpression</c> answer, as the reconciler reads documents): <see cref="FromFetch"/>
    /// over its attributes, keyed by logical name, with its id. A column the read did not select reads as "not set".
    /// </summary>
    internal static DocumentNarrowingRow FromEntity(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in row.Attributes)
            attributes[attribute.Key] = attribute.Value;
        attributes[DocumentPkField] = row.Id;
        return FromFetch(attributes);
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
    /// <param name="query">The BFF's app-only reader (the external plane's), on every surface.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static Task<DocumentNarrowingResult> DecideAsync(
        IEnumerable<DocumentNarrowingRow> rows, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct) =>
        DecideAsync(rows, query, ct, honourSubmissionMarker: false);

    /// <summary>
    /// <see cref="DecideAsync(IEnumerable{DocumentNarrowingRow}, CommunicationOpenThreadGate.RowQuery, CancellationToken)"/>
    /// with ebilling-r1 task 098's opt-in marker-aware rule 2 (procedure-and-file-model 9.1, the invoice's (a′) predicate):
    /// when <paramref name="honourSubmissionMarker"/> is set, a SUBJECT row (hop 0) whose own value is Restricted and that
    /// carries the submission marker (<see cref="DocumentNarrowingRow.SubmissionMarked"/>, read by
    /// <see cref="FromWebApi(Guid, IReadOnlyDictionary{string, JsonElement}, bool)"/>) skips the OWN leg only. An unreadable
    /// own value, every ancestor and the communication leg are decided as always. <c>false</c> is the 188 decision unchanged.
    /// </summary>
    internal static Task<DocumentNarrowingResult> DecideAsync(
        IEnumerable<DocumentNarrowingRow> rows, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct,
        bool honourSubmissionMarker)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(query);
        var counter = new ReadCounter();
        return DecideCoreAsync(rows, counter.Wrap(query), counter, ct, honourSubmissionMarker);
    }

    private static async Task<DocumentNarrowingResult> DecideCoreAsync(
        IEnumerable<DocumentNarrowingRow> rows, CommunicationOpenThreadGate.RowQuery query, ReadCounter counter,
        CancellationToken ct, bool honourSubmissionMarker = false)
    {
        // Keyed by the row AS READ (a value): a row is decided on its own columns and chain, so one that came back without its
        // id is still decided, and two copies of one row decide alike.
        var decided = new Dictionary<DocumentNarrowingRow, DocumentNarrowingReason>();
        var communicationOf = new Dictionary<DocumentNarrowingRow, Guid>();

        var input = rows.Distinct().ToList();
        var hopLevels = await WalkChainsAsync(
            input, (ids, c) => ReadDocumentsByIdAsync(ids, query, c), Visit, End, ct).ConfigureAwait(false);

        // The communication leg, for the subjects whose whole walk passed (a hidden walk needs no communication read).
        var linked = communicationOf.Where(kv => !decided.ContainsKey(kv.Key)).ToList();
        if (linked.Count > 0)
        {
            var visible = await CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync(
                linked.Select(kv => kv.Value), query, ct).ConfigureAwait(false);
            foreach (var (subject, communicationId) in linked)
            {
                decided[subject] = visible.Contains(communicationId)
                    ? DocumentNarrowingReason.Visible
                    : DocumentNarrowingReason.CommunicationHidden;
            }
        }

        return new DocumentNarrowingResult(decided, faulted: false, counter.Cost(input.Count, hopLevels));

        // One node of a subject's chain: the subject itself (hop 0) or the ancestor reached at `hop`. The own leg (hop 0) and
        // the ancestor leg check EVERY node to the top of the chain; the communication leg takes the FIRST link found.
        bool Visit(DocumentNarrowingRow subject, DocumentNarrowingRow node, int hop)
        {
            if (node.Own != DocumentPermission.NotNarrowed && !SkipsOwnLeg(node, hop))
            {
                decided[subject] = (hop, node.Own) switch
                {
                    (0, DocumentPermission.Restricted) => DocumentNarrowingReason.OwnRestricted,
                    (0, _) => DocumentNarrowingReason.OwnUnreadable,
                    (_, DocumentPermission.Restricted) => DocumentNarrowingReason.AncestorRestricted,
                    _ => DocumentNarrowingReason.AncestorUnreadable,
                };
                return false;
            }

            if (!communicationOf.ContainsKey(subject))
            {
                if (node.Communication == UnreadableLink)
                {
                    decided[subject] = DocumentNarrowingReason.LinkUnreadable;
                    return false;
                }

                if (node.Communication != Guid.Empty)
                    communicationOf[subject] = node.Communication; // the first link found decides the communication leg
            }

            return true;
        }

        // Task 098: only the subject's own Restricted value, only when it carries the submission marker, and only when the caller
        // asked for the marker-aware rule. Never an Unreadable value, never an ancestor.
        bool SkipsOwnLeg(DocumentNarrowingRow node, int hop) =>
            honourSubmissionMarker && hop == 0 && node.Own == DocumentPermission.Restricted && node.SubmissionMarked;

        // The walk ended without a node deciding the subject.
        void End(DocumentNarrowingRow subject, DocumentNarrowingReason? unknowable)
        {
            if (unknowable is { } why)
                decided[subject] = why;
            else if (!communicationOf.ContainsKey(subject))
                decided[subject] = DocumentNarrowingReason.Visible; // the top, no Restricted ancestor and no link anywhere
            // otherwise: the top with a link — the communication leg decides it above
        }
    }

    /// <summary>
    /// THE walk up a document's parent chain (task 188), shared by the narrowing (<see cref="DecideAsync"/>) and the
    /// communication-link walk (owner round 122, <see cref="FindCommunicationLinksAsync"/>): each subject itself (hop 0),
    /// then its <c>sprk_parentdocument</c>, grandparent and great-grandparent, at most <see cref="MaxHops"/> hops.
    /// </summary>
    /// <remarks>
    /// Each hop level is ONE batched read (<paramref name="read"/>) over the DISTINCT parents still pending that are not in
    /// hand: a subject, or a parent read at an earlier hop, is not read again. A chain cannot be known when a parent link is
    /// unreadable, a parent does not come back, the chain loops back to a node already on it, or it needs a hop past
    /// <see cref="MaxHops"/>.
    /// </remarks>
    /// <param name="subjects">The documents to walk (distinct).</param>
    /// <param name="read">Reads the parents of one hop level. A failed read THROWS, and so does this walk.</param>
    /// <param name="visit">Called for each node of a subject's chain in order (the subject itself at hop 0): false when it has
    /// decided the subject (its walk stops there), true to go on up.</param>
    /// <param name="end">Called once for each subject the visitor did not stop: <see langword="null"/> when its chain reached
    /// a node with no parent; otherwise why the chain cannot be known (<see cref="DocumentNarrowingReason.ParentUnreadable"/>,
    /// <see cref="DocumentNarrowingReason.ParentNotFound"/>, <see cref="DocumentNarrowingReason.Cycle"/>,
    /// <see cref="DocumentNarrowingReason.ChainTooLong"/>).</param>
    /// <returns>The hop levels that needed a read.</returns>
    private static async Task<int> WalkChainsAsync(
        IReadOnlyList<DocumentNarrowingRow> subjects, DocumentRowReader read,
        Func<DocumentNarrowingRow, DocumentNarrowingRow, int, bool> visit,
        Action<DocumentNarrowingRow, DocumentNarrowingReason?> end, CancellationToken ct)
    {
        var pending = new Dictionary<DocumentNarrowingRow, (Guid Next, HashSet<Guid> Visited)>();
        // Every row already in hand: a parent that is on the same page, or was read at an earlier hop, is not read again.
        var known = new Dictionary<Guid, DocumentNarrowingRow>();
        foreach (var row in subjects.Where(r => r.DocumentId != Guid.Empty && r.DocumentId != UnreadableLink))
            known.TryAdd(row.DocumentId, row);

        foreach (var row in subjects)
        {
            var visited = new HashSet<Guid>();
            if (row.DocumentId != Guid.Empty)
                visited.Add(row.DocumentId);
            Step(row, row, hop: 0, visited);
        }

        var hopLevels = 0;
        for (var hop = 1; hop <= MaxHops && pending.Count > 0; hop++)
        {
            var unread = pending.Values.Select(p => p.Next).Where(id => !known.ContainsKey(id)).Distinct().ToList();
            if (unread.Count > 0)
                hopLevels++;
            foreach (var parent in await read(unread, ct).ConfigureAwait(false))
                known.TryAdd(parent.DocumentId, parent);

            var current = pending;
            pending = new Dictionary<DocumentNarrowingRow, (Guid Next, HashSet<Guid> Visited)>();
            foreach (var (subject, walk) in current)
            {
                if (!known.TryGetValue(walk.Next, out var ancestor))
                {
                    end(subject, DocumentNarrowingReason.ParentNotFound);
                    continue;
                }

                Step(subject, ancestor, hop, walk.Visited);
            }
        }

        // Defensive: the loop above ends every walk by MaxHops (Step refuses a hop past it).
        foreach (var subject in pending.Keys)
            end(subject, DocumentNarrowingReason.ChainTooLong);

        return hopLevels;

        void Step(DocumentNarrowingRow subject, DocumentNarrowingRow node, int hop, HashSet<Guid> visited)
        {
            if (!visit(subject, node, hop))
                return;

            if (node.Parent == Guid.Empty)
            {
                end(subject, null); // the top of the chain
                return;
            }

            if (node.Parent == UnreadableLink)
            {
                end(subject, DocumentNarrowingReason.ParentUnreadable);
                return;
            }

            if (visited.Contains(node.Parent))
            {
                end(subject, DocumentNarrowingReason.Cycle);
                return;
            }

            if (hop >= MaxHops)
            {
                end(subject, DocumentNarrowingReason.ChainTooLong);
                return;
            }

            visited.Add(node.Parent);
            pending[subject] = (node.Parent, visited);
        }
    }

    /// <summary>
    /// Owner round 122: the communication walk of leg 2 ALONE, for the reconciler that releases a provisional own narrowing
    /// (<c>AccessInheritance.OwnPermissionOf</c>). For each row: the FIRST communication link on it, or up its
    /// <c>sprk_parentdocument</c> chain within <see cref="MaxHops"/> hops, on <see cref="WalkChainsAsync"/>, the same walk
    /// the narrowing takes.
    /// </summary>
    /// <remarks>
    /// It decides nothing about visibility: no communication is read, and the own and ancestor legs are not applied (a
    /// provisional <c>.eml</c> is itself Restricted until it is released). Fail closed for its caller: a link that is not a
    /// GUID, an unreadable parent link, a parent that does not come back, a cycle and a chain that needs a fourth hop all
    /// answer <see cref="DocumentCommunicationLink.Unknowable"/>, never a link. A failed read THROWS. Round 122 fix round 1
    /// (F1): each answer carries the rows the walk passed through (<see cref="DocumentCommunicationLink.Path"/>), so the
    /// caller can refuse a release past a document that a person narrowed. Collecting them changes nothing in the walk.
    /// </remarks>
    internal static async Task<IReadOnlyDictionary<DocumentNarrowingRow, DocumentCommunicationLink>> FindCommunicationLinksAsync(
        IEnumerable<DocumentNarrowingRow> rows, DocumentRowReader read, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(read);
        var found = new Dictionary<DocumentNarrowingRow, DocumentCommunicationLink>();
        var paths = new Dictionary<DocumentNarrowingRow, List<DocumentNarrowingRow>>();
        await WalkChainsAsync(
            rows.Distinct().ToList(), read,
            (subject, node, _) =>
            {
                var path = PathOf(subject);
                path.Add(node); // the subject itself at hop 0, then each ancestor in order
                if (node.Communication == UnreadableLink)
                {
                    found[subject] = new DocumentCommunicationLink(Guid.Empty, DocumentNarrowingReason.LinkUnreadable) { Path = path };
                    return false;
                }

                if (node.Communication == Guid.Empty)
                    return true; // no link here: go on up

                // The first link found; the path ends at the document that carries it.
                found[subject] = new DocumentCommunicationLink(node.Communication, null) { Path = path };
                return false;
            },
            (subject, unknowable) => found[subject] = new DocumentCommunicationLink(Guid.Empty, unknowable) { Path = PathOf(subject) },
            ct).ConfigureAwait(false);
        return found;

        List<DocumentNarrowingRow> PathOf(DocumentNarrowingRow subject)
        {
            if (!paths.TryGetValue(subject, out var path))
                paths[subject] = path = new List<DocumentNarrowingRow>();
            return path;
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
    /// For the assistant's <c>dataverse.*</c> tools (through <see cref="AppOnlyDocumentNarrowing"/>): reads the documents by
    /// id (batched, <see cref="SelectColumns"/>), then decides them, never on the columns the user's own read returned. A
    /// document that does not come back is not visible. A failed read THROWS.
    /// </summary>
    internal static async Task<DocumentNarrowingResult> ReadAndDecideAsync(
        IEnumerable<Guid> documentIds, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(query);
        var counter = new ReadCounter();
        var counted = counter.Wrap(query);
        var distinct = documentIds.Where(id => id != Guid.Empty && id != UnreadableLink).Distinct().ToList();
        var rows = await ReadDocumentsByIdAsync(distinct, counted, ct).ConfigureAwait(false);
        return await DecideCoreAsync(rows, counted, counter, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The documents of <paramref name="ids"/> (distinct, none empty), read by primary key in OR-filter chunks of
    /// <see cref="ChunkSize"/>, up to <see cref="CommunicationOpenThreadGate.MaxChunksInFlight"/> chunks at once. Only rows
    /// whose id was asked come back (an answer for another id is ignored). A failed chunk THROWS.
    /// </summary>
    private static async Task<IReadOnlyList<DocumentNarrowingRow>> ReadDocumentsByIdAsync(
        IReadOnlyList<Guid> ids, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        var chunks = await CommunicationOpenThreadGate.ReadChunksAsync(
            ids, (chunk, c) => query(DocumentSet, HopQuery(chunk), c), ct).ConfigureAwait(false);
        var rows = new List<DocumentNarrowingRow>();
        foreach (var (chunk, found) in chunks)
        {
            var asked = chunk.ToHashSet();
            rows.AddRange(found.Select(r => FromWebApi(IdOf(r), r)).Where(r => asked.Contains(r.DocumentId)));
        }

        return rows;
    }

    private static Guid IdOf(IReadOnlyDictionary<string, JsonElement> row) =>
        row.TryGetValue(DocumentPkField, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var id)
            ? id
            : Guid.Empty;

    /// <summary>Counts the reads one decision makes, by entity set (chunks may run concurrently).</summary>
    private sealed class ReadCounter
    {
        private int _documents;
        private int _communications;
        private int _threads;

        internal CommunicationOpenThreadGate.RowQuery Wrap(CommunicationOpenThreadGate.RowQuery query) =>
            (entitySet, odata, ct) =>
            {
                switch (entitySet)
                {
                    case DocumentSet: Interlocked.Increment(ref _documents); break;
                    case CommunicationOpenThreadGate.CommunicationSet: Interlocked.Increment(ref _communications); break;
                    case CommunicationOpenThreadGate.ThreadSet: Interlocked.Increment(ref _threads); break;
                }

                return query(entitySet, odata, ct);
            };

        internal DocumentNarrowingCost Cost(int rows, int hopLevels) =>
            new(rows, hopLevels, Volatile.Read(ref _documents), Volatile.Read(ref _communications), Volatile.Read(ref _threads));
    }
}

/// <summary>
/// What one narrowing decision read (task 188 cost line): the rows decided, the hop levels that needed a read, and the
/// requests per entity set. Counts only (ADR-015).
/// </summary>
internal readonly record struct DocumentNarrowingCost(int Rows, int HopLevels, int DocumentReads, int CommunicationReads, int ThreadReads)
{
    /// <summary>Every request the decision made.</summary>
    internal int Reads => DocumentReads + CommunicationReads + ThreadReads;
}

/// <summary>
/// Task 188 (main-session decision 2026-10-20): THE document narrowing for the assistant's <c>dataverse.*</c> tools — the
/// documents <paramref name="documentIds"/> re-read by id and decided under the BFF's APP-ONLY identity (the external
/// plane's reader, <see cref="ExternalDataService.ReadAndNarrowDocumentsAsync"/>), so a column field security masks for
/// the user can never read as "no link". It returns only the decision (no row data reaches the caller), and a failed read
/// THROWS (the tool call fails with no data). Registered by <c>AddExternalAccess</c>.
/// </summary>
/// <remarks>
/// The one app-only dependency of the read tools, narrow by construction (the shape of owner rounds 7 and 8 for the write
/// tools): a delegate, so nothing else of the app-only reader can be reached through it.
/// </remarks>
public delegate Task<DocumentNarrowingResult> AppOnlyDocumentNarrowing(IReadOnlyCollection<Guid> documentIds, CancellationToken ct);

/// <summary>
/// Task 188 round 3 slice C: the hide-only overload of <see cref="AppOnlyDocumentNarrowing"/> for rows that DERIVE from a
/// document (an <c>sprk_analysis</c> row's source document, and through it an <c>sprk_analysisoutput</c>'s).
/// </summary>
/// <remarks>
/// Owner round 111 (CLAUDE.md §6.5 path B, approved): the AI tools' one app-only exception is this seam, and it returns a hide
/// decision only. This overload narrows that further: it returns the SET of hidden document ids, no rows and no reasons.
/// </remarks>
internal static class AppOnlyDocumentNarrowingExtensions
{
    /// <summary>
    /// The ids among <paramref name="documentIds"/> that an external caller may NOT see: the documents are re-read by id
    /// APP-ONLY and decided by THE narrowing in ONE call (the seam's own batched reads). <see cref="Guid.Empty"/> (no document)
    /// is never in the set. <see cref="ExternalDocumentNarrowing.UnreadableLink"/> (a document id that cannot be decided) is
    /// always in it, without a read; so is a document that does not come back. A failed read THROWS (fail closed: the caller
    /// returns no data). No id at all: no call.
    /// </summary>
    internal static async Task<IReadOnlySet<Guid>> HiddenDocumentIdsAsync(
        this AppOnlyDocumentNarrowing narrowing, IEnumerable<Guid> documentIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(narrowing);
        ArgumentNullException.ThrowIfNull(documentIds);
        var asked = documentIds.Where(id => id != Guid.Empty).ToHashSet();
        var decidable = asked.Where(id => id != ExternalDocumentNarrowing.UnreadableLink).ToList();
        if (decidable.Count == 0)
            return asked; // nothing to read: only the undecidable sentinel (hidden), or nothing at all

        var decision = await narrowing(decidable, ct).ConfigureAwait(false);
        return asked.Where(id => !decision.IsVisible(id)).ToHashSet();
    }
}

/// <summary>
/// Task 188 round 3 slice B: THE document narrowing for the internal plane's document routes
/// (<c>Api/Filters/InternalPlaneDocumentNarrowing</c>: the document filter, the bulk download and the container lists). The
/// same app-only read and decision as <see cref="AppOnlyDocumentNarrowing"/> (<see cref="ExternalDataService.ReadAndNarrowDocumentsAsync"/>);
/// a separate delegate only so its cost line says <c>plane=internal-routes</c> and so a test host can state these routes'
/// narrowing without touching the assistant's. A failed read THROWS. Registered by <c>AddExternalAccess</c>.
/// </summary>
internal delegate Task<DocumentNarrowingResult> InternalRouteDocumentNarrowing(IReadOnlyCollection<Guid> documentIds, CancellationToken ct);

/// <summary>
/// Reads the documents of <paramref name="ids"/> (distinct, none empty) for one hop level of the parent-chain walk
/// (<see cref="ExternalDocumentNarrowing.FindCommunicationLinksAsync"/>): the rows that come back, each with its own Access
/// Permission and both links. A document that does not come back is left out. A failed read THROWS.
/// </summary>
internal delegate Task<IReadOnlyList<DocumentNarrowingRow>> DocumentRowReader(IReadOnlyList<Guid> ids, CancellationToken ct);

/// <summary>
/// Owner round 122: what the communication walk found for one document
/// (<see cref="ExternalDocumentNarrowing.FindCommunicationLinksAsync"/>): the first communication link
/// (<see cref="Communication"/>), none (both empty), or why the chain cannot be known (<see cref="Unknowable"/>).
/// </summary>
internal readonly record struct DocumentCommunicationLink(Guid Communication, DocumentNarrowingReason? Unknowable)
{
    /// <summary>
    /// Round 122 fix round 1 (F1): the rows the walk passed through, in order: the document itself (hop 0), then each
    /// ancestor up to and INCLUDING the one that carries the link (or, with no link, the last one it reached).
    /// </summary>
    internal IReadOnlyList<DocumentNarrowingRow> Path { get; init; } = [];

    /// <summary>A link was found within the walk's bound. Only this releases a provisional own narrowing.</summary>
    internal bool IsLinked => Communication != Guid.Empty && Unknowable is null;
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
internal readonly record struct DocumentNarrowingRow(Guid DocumentId, DocumentPermission Own, Guid Communication, Guid Parent)
{
    /// <summary>
    /// ebilling-r1 task 098: the row is an .eml whose Restricted narrowing the invoice submission wrote (both
    /// <see cref="ExternalDocumentNarrowing.EmailArchiveField"/> and <see cref="ExternalDocumentNarrowing.SubmissionMarkerField"/>
    /// read true). Set only by the marker-reading <c>FromWebApi</c> overload; <c>false</c> on every other row, so it changes
    /// nothing for a decision that does not honour it.
    /// </summary>
    internal bool SubmissionMarked { get; init; }
}

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
/// <remarks>Public only so the <see cref="AppOnlyDocumentNarrowing"/> delegate the public tool handlers take can name it;
/// every member is internal.</remarks>
public sealed class DocumentNarrowingResult
{
    private readonly IReadOnlyDictionary<DocumentNarrowingRow, DocumentNarrowingReason> _byRow;
    private readonly Dictionary<Guid, DocumentNarrowingReason> _byId = new();

    internal DocumentNarrowingResult(
        IReadOnlyDictionary<DocumentNarrowingRow, DocumentNarrowingReason> decided, bool faulted, DocumentNarrowingCost cost = default)
    {
        _byRow = decided;
        Faulted = faulted;
        Cost = cost;
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

    /// <summary>What the decision read (zero for a decision that made no read, or one that failed).</summary>
    internal DocumentNarrowingCost Cost { get; }

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
