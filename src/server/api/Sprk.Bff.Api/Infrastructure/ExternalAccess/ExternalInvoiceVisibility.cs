using System.Text.Json;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 098 (procedure-and-file-model 9.1 "Derived rows: (a′)", 5.2, 9.3): THE read-time predicate that hides an
/// invoice, and every row derived from it, from a caller who is not an internal systemuser while the invoice's Submit Invoice
/// source is isolated. Nothing is stored or cached: every call reads the current state.
/// </summary>
/// <remarks>
/// <para><b>The rule (O-32).</b> An invoice is hidden when, for ANY of its Submit Invoice sources S (every non-null
/// <c>sprk_storedfile.sprk_sourcedocument</c> on its Stored Files, and the <c>sprk_invoicesubmission.sprk_document</c> of
/// each Internal-channel submission, where the confirm route records its source; review rounds 1-3):
/// D7 rule 2, S's own Access Permission is Restricted, EXCEPT when S is an .eml carrying the submission marker
/// (<see cref="ExternalDocumentNarrowing.SubmissionMarkerField"/>); rule 3, a Restricted ancestor on S's parent chain; rule 4,
/// the 065 communication rule on S or its chain; or ANY root S is filed under (its six root links and the live roots of its
/// other parents) is effectively Restricted. Rule 1 (root grants) is never applied, so a refile never hides the invoice from
/// its firm. An invoice with no source is not hidden by this rule.</para>
/// <para><b>Composition, not a copy (ADR-013, root §11).</b> Rules 2–4 are task 188's <see cref="ExternalDocumentNarrowing"/>
/// with its opt-in marker-aware rule 2 (<see cref="InvoiceVisibilityReaders.DecideSources"/>, bound by
/// <see cref="ExternalDataService"/>, the only app-only seam allowed to call the core decision); the roots term is
/// <see cref="ExternalMultiRootRule.ReadLiveParentsAsync"/> + <see cref="ChildRowRoots"/> over the document row and
/// <c>ExternalParticipationService.GetEffectiveRootRecordFlagsAsync</c>.</para>
/// <para><b>Fail closed.</b> A Stored File or submission page that may be cut short, a source link that is not a GUID, a
/// Stored File with a source communication or attachment identity but no source document, an Internal submission whose
/// document link is empty when the invoice has no sourceless invoice PDF, a submission whose channel is unknown, a source that does
/// not come back, an unreadable own value, marker, chain or communication, a root that cannot be resolved and a flag that
/// cannot be read each hide the invoice. A failed read THROWS; the caller-aware seam
/// (<see cref="ExternalDataService.HideInvoicesForAsync"/>) turns that into "every asked invoice hidden".</para>
/// <para><b>Taint closure.</b> <see cref="HiddenFamilyRowsAsync"/> maps a row of the invoice family (lines, submissions,
/// approvals, external identifiers, corrections, Decision Records, Signals, Stored Files) to its invoice, directly or through
/// its invoice line, read app-only, and hides it with its invoice.</para>
/// <para><b>Cost.</b> One Stored File read and one submission read per 50 invoices; one source read per 50 sources; the 188 decision (one read per hop
/// level, at most three, plus the communication and thread reads); one live-parent walk; one effective-flag read per root type
/// (at most three). Logs carry counts only (ADR-015).</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <see cref="ExternalDocumentNarrowing"/> decides
/// documents and <see cref="ExternalMultiRootRule"/> decides roots; nothing decides an invoice by its source. (2) Extension —
/// 188's predicate is keyed on document rows and cannot answer for an invoice without the source lookup, the marker exception
/// and the roots' Restricted term, so this thin composition calls it (one opt-in option added to it). (3) Cost of doing
/// nothing — an invoice extracted from an internal-only or Restricted email reaches the firm and its partners with its lines,
/// approval, submission, Signals and findings.</para>
/// </remarks>
internal static class ExternalInvoiceVisibility
{
    internal const string InvoiceTable = "sprk_invoice";
    internal const string InvoiceSet = "sprk_invoices";
    internal const string InvoiceIdField = "sprk_invoiceid";
    internal const string InvoiceLineTable = "sprk_billingevent";

    private const string StoredFileSet = Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.EntitySet;
    private const string StoredFileIdField = Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.IdColumn;
    private const string StoredFileInvoiceValue = "_" + Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.InvoiceTable + "_value";
    private const string StoredFileSourceValue = "_" + Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.SourceDocument + "_value";

    /// <summary>Invoices (and sources) per batched read: the 065 rule's chunk, so one OR filter stays inside the URL limit.</summary>
    internal const int ChunkSize = CommunicationOpenThreadGate.ChunkSize;

    /// <summary>
    /// The most source-bearing Stored File rows one read of <see cref="ChunkSize"/> invoices may return. A read that returns
    /// more may be cut short, so every invoice of that chunk is hidden (fail closed).
    /// </summary>
    internal const int MaxSourceFilesPerChunk = 2000;

    /// <summary>
    /// How a table of the invoice family reaches its invoice (model 9.1 taint closure; lookups from SecureChildLineage, read
    /// live): its entity set and primary id, the invoice lookup, and the invoice-line lookup (through
    /// <c>sprk_billingevent.sprk_invoice</c>). <c>null</c> InvoiceLink on <see cref="InvoiceTable"/>: the row IS the invoice.
    /// </summary>
    internal sealed record FamilyTable(string EntitySet, string IdColumn, string? InvoiceLink, string? LineLink);

    /// <summary>The closed taint list of model 9.1 (task 098 constraint "taint closure").</summary>
    internal static readonly IReadOnlyDictionary<string, FamilyTable> FamilyTables =
        new Dictionary<string, FamilyTable>(StringComparer.OrdinalIgnoreCase)
        {
            [InvoiceTable] = new(InvoiceSet, InvoiceIdField, null, null),
            [InvoiceLineTable] = new("sprk_billingevents", "sprk_billingeventid", "_sprk_invoice_value", null),
            ["sprk_invoicesubmission"] = new("sprk_invoicesubmissions", "sprk_invoicesubmissionid", "_sprk_invoice_value", null),
            ["sprk_invoiceapproval"] = new("sprk_invoiceapprovals", "sprk_invoiceapprovalid", "_sprk_invoice_value", null),
            ["sprk_externalidentifier"] = new("sprk_externalidentifiers", "sprk_externalidentifierid", "_sprk_regardinginvoice_value", null),
            ["sprk_invoicecorrection"] = new("sprk_invoicecorrections", "sprk_invoicecorrectionid", "_sprk_invoice_value", "_sprk_invoiceline_value"),
            ["sprk_decisionrecord"] = new("sprk_decisionrecords", "sprk_decisionrecordid", "_sprk_regardinginvoice_value", "_sprk_regardinginvoiceline_value"),
            ["sprk_signal"] = new("sprk_signals", "sprk_signalid", "_sprk_regardinginvoice_value", "_sprk_regardinginvoiceline_value"),
            [Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.Entity] =
                new(StoredFileSet, StoredFileIdField, StoredFileInvoiceValue, null),
        };

    /// <summary>True for <c>sprk_invoice</c> and every table of the taint closure (<see cref="FamilyTables"/>).</summary>
    internal static bool IsInvoiceFamilyTable(string? logicalName) =>
        logicalName is not null && FamilyTables.ContainsKey(logicalName);

    /// <summary>The columns a source document is read with: 188's narrowing columns, the marker, the root links and the
    /// live-parent lookups (whose live roots join the roots term).</summary>
    internal static IReadOnlyList<string> SourceSelectColumns { get; } =
        new[] { ExternalDocumentNarrowing.DocumentPkField }
            .Concat(ExternalDocumentNarrowing.SelectColumns)
            .Concat(ExternalDocumentNarrowing.SubmissionMarkerColumns)
            .Concat(ExternalDataService.DocumentRootLinks.SelectColumns)
            .Concat(ExternalMultiRootRule.LiveParentSelect(ExternalDocumentNarrowing.DocumentTable))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    // ── The decision ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which of <paramref name="invoiceIds"/> the predicate hides, read app-only through <paramref name="readers"/>. The caller
    /// has already decided that the caller is NOT an internal systemuser. <see cref="Guid.Empty"/> is ignored. A failed read
    /// THROWS.
    /// </summary>
    internal static async Task<InvoiceVisibilityResult> DecideAsync(
        IReadOnlyCollection<Guid> invoiceIds, InvoiceVisibilityReaders readers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoiceIds);
        ArgumentNullException.ThrowIfNull(readers);
        var asked = invoiceIds.Where(id => id != Guid.Empty && id != ExternalDocumentNarrowing.UnreadableLink).Distinct().ToList();
        var hidden = new Dictionary<Guid, InvoiceHiddenReason>();
        var cost = new InvoiceVisibilityCost();
        if (asked.Count == 0)
            return new InvoiceVisibilityResult(asked, hidden, faulted: false, cost);

        // (1) The sources, one read per chunk of each table; both reads fail closed the same way.
        //     Stored Files: every non-null source document. A source communication or attachment with no document hides the
        //     invoice. A sourceless InvoicePdf is an upload's own invoice PDF.
        //     Submissions (by channel, the shared InvoiceSubmissionChannel enum; review round 3):
        //       Spa                     never a document source; the Stored Files decide;
        //       Internal, document set  that document is a source (the confirm route records its source only here);
        //       Internal, link empty    hidden unless the invoice has a sourceless InvoicePdf (the internal upload's own PDF);
        //       unknown or unreadable   hidden.
        var sourcesOf = new Dictionary<Guid, List<Guid>>();
        var uploadPdfs = await ReadStoredFileSourcesAsync(asked, readers.Query, sourcesOf, hidden, cost, ct).ConfigureAwait(false);
        var internalWithoutDocument = await ReadSubmissionSourcesAsync(asked, readers.Query, sourcesOf, hidden, cost, ct)
            .ConfigureAwait(false);
        foreach (var invoiceId in internalWithoutDocument.Where(id => !uploadPdfs.Contains(id)))
            hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceLinkUnreadable);

        var pending = sourcesOf.Where(kv => !hidden.ContainsKey(kv.Key)).ToList();
        var sourceIds = pending.SelectMany(kv => kv.Value).Distinct().ToList();
        if (sourceIds.Count == 0)
            return new InvoiceVisibilityResult(asked, hidden, faulted: false, cost);

        // (2) The sources themselves, by id, app-only: narrowing columns, marker, root links and live parents.
        var sourceRows = new Dictionary<Guid, Dictionary<string, JsonElement>>();
        var sourceChunks = await CommunicationOpenThreadGate.ReadChunksAsync(sourceIds, (chunk, c) => readers.Query(
            ExternalDocumentNarrowing.DocumentSet,
            $"$select={string.Join(",", SourceSelectColumns)}" +
            $"&$filter={string.Join(" or ", chunk.Select(id => $"{ExternalDocumentNarrowing.DocumentPkField} eq {id:D}"))}" +
            $"&$top={chunk.Length}", c), ct).ConfigureAwait(false);
        foreach (var (chunk, rows) in sourceChunks)
        {
            cost.SourceReads++;
            var inChunk = chunk.ToHashSet();
            foreach (var row in rows)
            {
                if (LinkOf(row, ExternalDocumentNarrowing.DocumentPkField) is { } id && inChunk.Contains(id))
                    sourceRows.TryAdd(id, row);
            }
        }

        // Rules 2-4: THE document narrowing with the marker-aware rule 2 (task 188's decision; never copied).
        var narrowingRows = sourceRows.ToDictionary(
            kv => kv.Key, kv => ExternalDocumentNarrowing.FromWebApi(kv.Key, kv.Value, readSubmissionMarker: true));
        var narrowing = narrowingRows.Count == 0
            ? DocumentNarrowingResult.NotNarrowed(Array.Empty<DocumentNarrowingRow>())
            : await readers.DecideSources(narrowingRows.Values.ToList(), ct).ConfigureAwait(false);
        cost.NarrowingReads = narrowing.Cost.Reads;

        // The roots term: the six root links ∪ the live roots of the source's other parents, then the EFFECTIVE flags.
        var live = await ExternalMultiRootRule.ReadLiveParentsAsync(
            readers.LiveParents,
            sourceRows.Values.Select(row => ExternalMultiRootRule.LiveParentsOf(ExternalDocumentNarrowing.DocumentTable, row)),
            readers.Logger, ct).ConfigureAwait(false);
        var rootsOf = new Dictionary<Guid, (List<(ExternalGrantRootType Root, Guid Id)> Roots, bool Unresolved)>();
        foreach (var (sourceId, row) in sourceRows)
        {
            var roots = new List<(ExternalGrantRootType, Guid)>();
            var unresolved = false;
            var links = ChildRowRoots.Of(
                ExternalDocumentNarrowing.DocumentTable, row, ExternalDataService.DocumentRootLinks.From(row).Links, live);
            foreach (var (root, value) in links.Links)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue; // not filed there
                if (Guid.TryParse(value, out var rootId) && rootId != Guid.Empty)
                    roots.Add((root, rootId));
                else
                    unresolved = true; // malformed, or a parent whose live roots could not be resolved
            }

            rootsOf[sourceId] = (roots, unresolved);
        }

        var flags = new Dictionary<(ExternalGrantRootType Root, Guid Id), RootRecordFlags>();
        foreach (var group in rootsOf.Values.SelectMany(r => r.Roots).Distinct().GroupBy(r => r.Root))
        {
            var ids = group.Select(r => r.Id).ToList();
            cost.RootFlagReads++;
            var read = await readers.RootFlags(ExternalGrantRoot.LogicalNameFor(group.Key), ids, ct).ConfigureAwait(false);
            foreach (var id in ids)
                flags[(group.Key, id)] = read.TryGetValue(id, out var f) ? f : RootRecordFlags.Unreadable;
        }

        // The decision per invoice: hidden when ANY of its sources is.
        foreach (var (invoiceId, sources) in pending)
        {
            foreach (var source in sources.Distinct())
            {
                if (SourceHiddenReason(source) is { } reason)
                {
                    hidden.TryAdd(invoiceId, reason);
                    break;
                }
            }
        }

        cost.Sources = sourceIds.Count;
        return new InvoiceVisibilityResult(asked, hidden, faulted: false, cost);

        InvoiceHiddenReason? SourceHiddenReason(Guid source)
        {
            if (!narrowingRows.TryGetValue(source, out var row))
                return InvoiceHiddenReason.SourceNotFound;
            if (!narrowing.IsVisible(row))
                return InvoiceHiddenReason.SourceHidden;
            var (roots, unresolved) = rootsOf[source];
            if (unresolved)
                return InvoiceHiddenReason.RootUnresolved;
            foreach (var root in roots)
            {
                if (!flags.TryGetValue(root, out var f) || f.IsUnreadable)
                    return InvoiceHiddenReason.RootUnresolved;
                if (f.IsRestricted)
                    return InvoiceHiddenReason.RootRestricted;
            }

            return null;
        }
    }

    private const string StoredFileCommunicationValue =
        "_" + Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.SourceCommunication + "_value";
    private const string StoredFileAttachmentIdentity = Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.SourceAttachmentIdentity;
    private const string StoredFilePurposeColumn = Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.Purpose;

    private const string SubmissionSet = "sprk_invoicesubmissions";
    private const string SubmissionIdField = "sprk_invoicesubmissionid";
    private const string SubmissionInvoiceValue = "_sprk_invoice_value";
    private const string SubmissionDocumentValue = "_sprk_document_value";
    private const string SubmissionChannel = "sprk_channel";

    /// <summary>
    /// The Stored Files of <paramref name="asked"/> (094, 095), every row: a source document is collected into
    /// <paramref name="sourcesOf"/>; a source communication or attachment identity with no document hides the invoice (a shape
    /// the schema allows and no writer creates today; 090's file route refuses it too). Returns the invoices that have an
    /// <see cref="Sprk.Bff.Api.Services.Finance.Files.StoredFilePurpose.InvoicePdf"/> with no source at all (an upload's own
    /// invoice PDF). A sourceless file of any other purpose is not a source and is not an upload's PDF. A failed read THROWS.
    /// </summary>
    private static async Task<HashSet<Guid>> ReadStoredFileSourcesAsync(
        IReadOnlyList<Guid> asked, CommunicationOpenThreadGate.RowQuery query, Dictionary<Guid, List<Guid>> sourcesOf,
        Dictionary<Guid, InvoiceHiddenReason> hidden, InvoiceVisibilityCost cost, CancellationToken ct)
    {
        var uploadPdfs = new HashSet<Guid>();
        var rows = await ReadRowsOfInvoicesAsync(
            StoredFileSet,
            new[] { StoredFileIdField, StoredFileInvoiceValue, StoredFileSourceValue, StoredFileCommunicationValue,
                StoredFileAttachmentIdentity, StoredFilePurposeColumn },
            StoredFileInvoiceValue, asked, query, hidden, () => cost.StoredFileReads++, ct).ConfigureAwait(false);
        foreach (var (invoiceId, row) in rows)
        {
            switch (LinkOf(row, StoredFileSourceValue))
            {
                case { } source when source == ExternalDocumentNarrowing.UnreadableLink:
                    hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceLinkUnreadable);
                    break;
                case { } source:
                    AddSource(sourcesOf, invoiceId, source);
                    break;
                case null when IsSet(row, StoredFileCommunicationValue) || IsSet(row, StoredFileAttachmentIdentity):
                    // A source this rule cannot check.
                    hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceLinkUnreadable);
                    break;
                case null when IntOf(row, StoredFilePurposeColumn)
                    == (int)Sprk.Bff.Api.Services.Finance.Files.StoredFilePurpose.InvoicePdf:
                    uploadPdfs.Add(invoiceId);
                    break;
                case null:
                    break; // a sourceless file of another purpose: not a source
            }
        }

        return uploadPdfs;
    }

    /// <summary>
    /// The submissions of <paramref name="asked"/>, every row, by channel (the shared
    /// <see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel"/> enum): Spa is never a document source;
    /// Internal with a document collects it into <paramref name="sourcesOf"/> (the confirm route records its source only
    /// there); an unknown, unreadable or unrecognised channel hides the invoice. Returns the invoices with an Internal
    /// submission whose document link is empty (the caller hides them unless they have an upload's own invoice PDF). A
    /// failed read THROWS.
    /// </summary>
    private static async Task<HashSet<Guid>> ReadSubmissionSourcesAsync(
        IReadOnlyList<Guid> asked, CommunicationOpenThreadGate.RowQuery query, Dictionary<Guid, List<Guid>> sourcesOf,
        Dictionary<Guid, InvoiceHiddenReason> hidden, InvoiceVisibilityCost cost, CancellationToken ct)
    {
        var internalWithoutDocument = new HashSet<Guid>();
        var rows = await ReadRowsOfInvoicesAsync(
            SubmissionSet,
            new[] { SubmissionIdField, SubmissionInvoiceValue, SubmissionDocumentValue, SubmissionChannel },
            SubmissionInvoiceValue, asked, query, hidden, () => cost.SubmissionReads++, ct).ConfigureAwait(false);
        foreach (var (invoiceId, row) in rows)
        {
            if (IntOf(row, SubmissionChannel) is not { } channel
                || !Enum.IsDefined(typeof(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel), channel))
            {
                hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceKindUnknown);
                continue;
            }

            switch ((Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel)channel)
            {
                case Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel.Spa:
                    break; // never a document source: the invoice's Stored Files decide
                case Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel.Internal:
                    switch (LinkOf(row, SubmissionDocumentValue))
                    {
                        case null:
                            internalWithoutDocument.Add(invoiceId);
                            break;
                        case { } source when source == ExternalDocumentNarrowing.UnreadableLink:
                            hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceLinkUnreadable);
                            break;
                        case { } source:
                            AddSource(sourcesOf, invoiceId, source);
                            break;
                    }

                    break;
                default:
                    // Defined but not handled above: a channel added to the enum later is unknown here until decided.
                    hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceKindUnknown);
                    break;
            }
        }

        return internalWithoutDocument;
    }

    /// <summary>
    /// Every row of <paramref name="entitySet"/> whose <paramref name="invoiceValue"/> is one of <paramref name="asked"/>, one
    /// read per chunk, paired with its invoice. A chunk whose read may be cut short hides every invoice of the chunk
    /// (<see cref="InvoiceHiddenReason.SourcesTruncated"/>); a row the filter cannot have produced hides every invoice of the
    /// chunk and none of its rows is used. A failed read THROWS.
    /// </summary>
    private static async Task<List<(Guid Invoice, Dictionary<string, JsonElement> Row)>> ReadRowsOfInvoicesAsync(
        string entitySet, IReadOnlyList<string> select, string invoiceValue, IReadOnlyList<Guid> asked,
        CommunicationOpenThreadGate.RowQuery query, Dictionary<Guid, InvoiceHiddenReason> hidden, Action counted,
        CancellationToken ct)
    {
        var result = new List<(Guid, Dictionary<string, JsonElement>)>();
        var chunks = await CommunicationOpenThreadGate.ReadChunksAsync(asked, (chunk, c) => query(entitySet,
            $"$select={string.Join(",", select)}" +
            $"&$filter={string.Join(" or ", chunk.Select(id => $"{invoiceValue} eq {id:D}"))}" +
            $"&$top={MaxSourceFilesPerChunk + 1}", c), ct).ConfigureAwait(false);
        foreach (var (chunk, rows) in chunks)
        {
            counted();
            if (rows.Count > MaxSourceFilesPerChunk)
            {
                foreach (var id in chunk)
                    hidden.TryAdd(id, InvoiceHiddenReason.SourcesTruncated);
                continue;
            }

            var inChunk = chunk.ToHashSet();
            var paired = new List<(Guid, Dictionary<string, JsonElement>)>();
            var stray = false;
            foreach (var row in rows)
            {
                if (LinkOf(row, invoiceValue) is not { } invoiceId || !inChunk.Contains(invoiceId))
                {
                    stray = true;
                    break;
                }

                paired.Add((invoiceId, row));
            }

            if (stray)
            {
                // An answer the filter cannot have produced: which invoice it belongs to is unknown, so none is decided.
                foreach (var id in chunk)
                    hidden.TryAdd(id, InvoiceHiddenReason.SourceLinkUnreadable);
                continue;
            }

            result.AddRange(paired);
        }

        return result;
    }

    private static void AddSource(Dictionary<Guid, List<Guid>> sourcesOf, Guid invoiceId, Guid source)
    {
        if (!sourcesOf.TryGetValue(invoiceId, out var list))
            sourcesOf[invoiceId] = list = new List<Guid>();
        list.Add(source);
    }

    private static bool IsSet(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    /// <summary>An integer column (an option value): <c>null</c> when absent, null or not a 32-bit integer.</summary>
    private static int? IntOf(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)
            ? i
            : null;

    /// <summary>
    /// The taint closure (model 9.1): which of <paramref name="rowIds"/> (rows of <paramref name="table"/>, a table of
    /// <see cref="FamilyTables"/>) are hidden because their invoice, directly or through their invoice line, is hidden. The
    /// rows' links are read BY ID app-only (a row that does not come back, or a link that is not a GUID, hides the row); a row
    /// with no invoice and no line is not hidden. A failed read THROWS.
    /// </summary>
    internal static async Task<IReadOnlySet<Guid>> HiddenFamilyRowsAsync(
        string table, IReadOnlyCollection<Guid> rowIds, InvoiceVisibilityReaders readers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentNullException.ThrowIfNull(readers);
        var invoicesOf = await ReadInvoicesOfAsync(table, rowIds, readers.Query, ct).ConfigureAwait(false);
        var decidable = invoicesOf.Values.SelectMany(v => v).Where(id => id != ExternalDocumentNarrowing.UnreadableLink)
            .Distinct().ToList();
        var decision = decidable.Count == 0
            ? null
            : await DecideAsync(decidable, readers, ct).ConfigureAwait(false);
        return invoicesOf
            .Where(kv => kv.Value.Any(invoice => invoice == ExternalDocumentNarrowing.UnreadableLink
                || decision is null || !decision.IsVisible(invoice)))
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    /// <summary>
    /// The invoices each row of <paramref name="rowIds"/> belongs to (<see cref="ExternalDocumentNarrowing.UnreadableLink"/>:
    /// one that cannot be decided). An <c>sprk_invoice</c> row is its own invoice. A failed read THROWS.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ReadInvoicesOfAsync(
        string table, IReadOnlyCollection<Guid> rowIds, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        if (!FamilyTables.TryGetValue(table ?? string.Empty, out var family))
            throw new ArgumentOutOfRangeException(nameof(table), table, "Not a table of the invoice family.");

        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentNullException.ThrowIfNull(query);
        var distinct = rowIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (family.InvoiceLink is null)
            return distinct.ToDictionary(id => id, id => (IReadOnlyList<Guid>)new[] { id });

        IReadOnlyList<Guid> undecidable = new[] { ExternalDocumentNarrowing.UnreadableLink };
        var links = await ReadLinksAsync(family, distinct, query, ct).ConfigureAwait(false);
        // A row that did not come back has an invoice that cannot be decided.
        var result = distinct.ToDictionary(
            id => id,
            id => links.TryGetValue(id, out var found)
                ? found
                : (Invoices: undecidable, Lines: (IReadOnlyList<Guid>)Array.Empty<Guid>()));

        // An invoice line reaches its invoice through sprk_billingevent.sprk_invoice.
        var lineIds = result.Values.SelectMany(v => v.Lines).Where(IsUsable).Distinct().ToList();
        IReadOnlyDictionary<Guid, (IReadOnlyList<Guid> Invoices, IReadOnlyList<Guid> Lines)> lines =
            lineIds.Count == 0
                ? new Dictionary<Guid, (IReadOnlyList<Guid>, IReadOnlyList<Guid>)>()
                : await ReadLinksAsync(FamilyTables[InvoiceLineTable], lineIds, query, ct).ConfigureAwait(false);

        return result.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<Guid>)kv.Value.Invoices
                .Concat(kv.Value.Lines.SelectMany(line => !IsUsable(line)
                    ? new[] { ExternalDocumentNarrowing.UnreadableLink }
                    : lines.TryGetValue(line, out var found) ? found.Invoices : undecidable))
                .Distinct()
                .ToList());

        static bool IsUsable(Guid id) => id != Guid.Empty && id != ExternalDocumentNarrowing.UnreadableLink;
    }

    private static async Task<IReadOnlyDictionary<Guid, (IReadOnlyList<Guid> Invoices, IReadOnlyList<Guid> Lines)>> ReadLinksAsync(
        FamilyTable family, IReadOnlyList<Guid> ids, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        var columns = new[] { family.IdColumn, family.InvoiceLink!, family.LineLink }.OfType<string>().ToArray();
        var chunks = await CommunicationOpenThreadGate.ReadChunksAsync(ids, (chunk, c) => query(family.EntitySet,
            $"$select={string.Join(",", columns)}" +
            $"&$filter={string.Join(" or ", chunk.Select(id => $"{family.IdColumn} eq {id:D}"))}&$top={chunk.Length}", c), ct)
            .ConfigureAwait(false);
        var found = new Dictionary<Guid, (IReadOnlyList<Guid>, IReadOnlyList<Guid>)>();
        foreach (var (chunk, rows) in chunks)
        {
            var inChunk = chunk.ToHashSet();
            foreach (var row in rows)
            {
                if (LinkOf(row, family.IdColumn) is not { } id || !inChunk.Contains(id))
                    continue;
                IReadOnlyList<Guid> invoices = LinkOf(row, family.InvoiceLink!) is { } invoice ? new[] { invoice } : Array.Empty<Guid>();
                IReadOnlyList<Guid> lines = family.LineLink is not null && LinkOf(row, family.LineLink) is { } line
                    ? new[] { line }
                    : Array.Empty<Guid>();
                found[id] = (invoices, lines);
            }
        }

        return found;
    }

    /// <summary>Absent or null: <c>null</c> (not set); a non-empty GUID string: that id; anything else
    /// <see cref="ExternalDocumentNarrowing.UnreadableLink"/>.</summary>
    private static Guid? LinkOf(IReadOnlyDictionary<string, JsonElement> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) && id != Guid.Empty
            ? id
            : ExternalDocumentNarrowing.UnreadableLink;
    }
}

/// <summary>The marker-aware 188 decision over source rows (bound by <see cref="ExternalDataService"/>; app-only; throws).</summary>
internal delegate Task<DocumentNarrowingResult> InvoiceSourceDecider(
    IReadOnlyCollection<DocumentNarrowingRow> rows, CancellationToken ct);

/// <summary>The EFFECTIVE flags of a batch of roots (<c>ExternalParticipationService.GetEffectiveRootRecordFlagsAsync</c>):
/// every asked id present, an unreadable one <see cref="RootRecordFlags.Unreadable"/>.</summary>
public delegate Task<IReadOnlyDictionary<Guid, RootRecordFlags>> EffectiveRootFlagReader(
    string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct);

/// <summary>What <see cref="ExternalInvoiceVisibility"/> reads through, every reader app-only.</summary>
/// <param name="Query">The external plane's app-only OData reader; a failed read THROWS.</param>
/// <param name="DecideSources">188's decision with the marker-aware rule 2.</param>
/// <param name="LiveParents">The live-parent walk (task 187).</param>
/// <param name="RootFlags">The effective root flags.</param>
/// <param name="Logger">For the live-parent walk's own fail-closed lines (counts only).</param>
internal sealed record InvoiceVisibilityReaders(
    CommunicationOpenThreadGate.RowQuery Query,
    InvoiceSourceDecider DecideSources,
    LiveParentRootReader LiveParents,
    EffectiveRootFlagReader RootFlags,
    ILogger Logger);

/// <summary>
/// The caller-aware seam every external surface applies (<see cref="ExternalDataService.HideInvoicesForAsync"/>): an internal
/// systemuser hides nothing; every other caller (a CIAM contact, a flagged or unresolvable systemuser, a caller whose lookup
/// failed) gets the predicate; a failed read hides every asked invoice (<see cref="InvoiceVisibilityResult.Faulted"/>).
/// Registered by <c>AddExternalAccess</c>.
/// </summary>
internal delegate Task<InvoiceVisibilityResult> ExternalInvoiceHiding(
    CallerPrincipal caller, IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct);

/// <summary>
/// The assistant's <c>dataverse.*</c> tools' seam (task 098 surface (d)): of <paramref name="rowIds"/> (rows of an invoice-family
/// table the USER's own read returned), the ids an external caller may NOT see. The rows' invoice links and the decision are
/// read app-only; only the hidden id set comes back (a hide decision; no row data). The tool has already decided the caller is
/// external. A failed read THROWS (the tool call fails with no data). Registered by <c>AddExternalAccess</c>.
/// </summary>
public delegate Task<IReadOnlySet<Guid>> AppOnlyInvoiceFamilyHiding(
    string table, IReadOnlyCollection<Guid> rowIds, CancellationToken ct);

/// <summary>Why an invoice is hidden (logged as counts per code only).</summary>
internal enum InvoiceHiddenReason
{
    SourceHidden = 1,
    SourceNotFound,
    SourceLinkUnreadable,
    SourcesTruncated,
    RootRestricted,
    RootUnresolved,
    ReadFailed,
    SourceKindUnknown,
}

/// <summary>What one decision read (counts only).</summary>
internal sealed class InvoiceVisibilityCost
{
    internal int StoredFileReads { get; set; }
    internal int SubmissionReads { get; set; }
    internal int SourceReads { get; set; }
    internal int Sources { get; set; }
    internal int NarrowingReads { get; set; }
    internal int RootFlagReads { get; set; }
    internal int Reads => StoredFileReads + SubmissionReads + SourceReads + NarrowingReads + RootFlagReads;
}

/// <summary>The predicate's answer for one request. An invoice it was not asked about is NOT visible.</summary>
internal sealed class InvoiceVisibilityResult
{
    private readonly HashSet<Guid> _asked;
    private readonly IReadOnlyDictionary<Guid, InvoiceHiddenReason> _hidden;

    internal InvoiceVisibilityResult(
        IEnumerable<Guid> asked, IReadOnlyDictionary<Guid, InvoiceHiddenReason> hidden, bool faulted, InvoiceVisibilityCost? cost = null)
    {
        _asked = asked.Where(id => id != Guid.Empty).ToHashSet();
        _hidden = hidden;
        Faulted = faulted;
        Cost = cost ?? new InvoiceVisibilityCost();
    }

    /// <summary>An internal systemuser, or nothing asked: nothing hidden.</summary>
    internal static InvoiceVisibilityResult NotHidden(IEnumerable<Guid> asked) =>
        new(asked, new Dictionary<Guid, InvoiceHiddenReason>(), faulted: false);

    /// <summary>The read failed: every asked invoice is hidden, and a list that dropped rows says so.</summary>
    internal static InvoiceVisibilityResult ReadFailed(IEnumerable<Guid> asked)
    {
        var ids = asked.Where(id => id != Guid.Empty).Distinct().ToList();
        return new InvoiceVisibilityResult(ids, ids.ToDictionary(id => id, _ => InvoiceHiddenReason.ReadFailed), faulted: true);
    }

    /// <summary><c>true</c> when the read failed (as opposed to an invoice the rule hides).</summary>
    internal bool Faulted { get; }

    internal InvoiceVisibilityCost Cost { get; }

    /// <summary>Whether <paramref name="invoiceId"/> was asked and is not hidden.</summary>
    internal bool IsVisible(Guid invoiceId) =>
        invoiceId != Guid.Empty && _asked.Contains(invoiceId) && !_hidden.ContainsKey(invoiceId);

    /// <summary>The asked invoices that are hidden.</summary>
    internal IReadOnlyCollection<Guid> Hidden => _hidden.Keys.ToList();

    /// <summary>The hidden count per reason code (one log line; codes and counts only).</summary>
    internal string HiddenSummary() =>
        string.Join(",", _hidden.Values.GroupBy(r => r).OrderBy(g => g.Key).Select(g => $"{CodeOf(g.Key)}={g.Count()}"));

    internal static string CodeOf(InvoiceHiddenReason reason) => reason switch
    {
        InvoiceHiddenReason.SourceHidden => "source_hidden",
        InvoiceHiddenReason.SourceNotFound => "source_not_found",
        InvoiceHiddenReason.SourceLinkUnreadable => "source_link_unreadable",
        InvoiceHiddenReason.SourcesTruncated => "sources_truncated",
        InvoiceHiddenReason.RootRestricted => "root_restricted",
        InvoiceHiddenReason.RootUnresolved => "root_unresolved",
        InvoiceHiddenReason.SourceKindUnknown => "source_kind_unknown",
        _ => "read_failed",
    };
}
