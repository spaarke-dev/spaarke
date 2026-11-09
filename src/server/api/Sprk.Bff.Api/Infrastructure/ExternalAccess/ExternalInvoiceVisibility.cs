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
/// <para><b>Own firms (task 130-0, design.md:414: an external user sees only their firm's submissions and invoices).</b> On the
/// caller-aware invoice seam (<see cref="ExternalDataService.HideInvoicesForAsync"/>) a caller sees only invoices whose
/// <c>sprk_vendororg</c> is one of the caller's firms; any other invoice, and one with no firm, is hidden
/// (<see cref="InvoiceHiddenReason.FirmNotCallers"/>). The firm is read in the same app-only batch as the firm hold
/// (<see cref="ReadFirmHeldInvoicesAsync"/>). The caller's firms come from <see cref="CallerFirmReader"/> as a <see cref="CallerFirms"/>,
/// which keeps two states apart: a caller KNOWN to have no firm sees no invoice (owner decision 2026-11-08; reason
/// <see cref="InvoiceHiddenReason.FirmNotCallers"/>, not a fault), and firms that are UNKNOWN hide every asked invoice as a failed read
/// (<see cref="InvoiceVisibilityResult.ReadFailed"/>). Task 130-0b: the assistant tools' seam
/// (<see cref="ExternalDataService.HideInvoiceFamilyRowsAsync"/>) applies it too, and there a submission row is also hidden when its own
/// firm is not one of the caller's (<see cref="HiddenFamilyRowsAsync"/>). The submission routes do not apply it here
/// (<see cref="InvoiceVisibilityReaders.CallerFirms"/> is <c>null</c> there; their filter scopes by the submission's firm).</para>
/// <para><b>Own submission (task 165e).</b> The invoice is decided only from its own submission
/// (<see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceBlockedHold.IsOwnSubmission"/>, read with the other submissions linked to
/// it in the same batch): an invoice whose own submission is not found among them is hidden
/// (<see cref="InvoiceHiddenReason.OwnSubmissionMissing"/>), whatever its other columns and files say.</para>
/// <para><b>Own document (task 165f).</b> A submission's <c>sprk_document</c> is a source only when it is the submission's own document
/// (<see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf"/>, the persister's rule;
/// no copy). Any other link contributes nothing: the submission, or its invoice, is decided as if the link were empty.</para>
/// <para><b>Composition, not a copy (ADR-013, root §11).</b> Rules 2–4 are task 188's <see cref="ExternalDocumentNarrowing"/>
/// with its opt-in marker-aware rule 2 (<see cref="InvoiceVisibilityReaders.DecideSources"/>, bound by
/// <see cref="ExternalDataService"/>, the only app-only seam allowed to call the core decision); the roots term is
/// <see cref="ExternalMultiRootRule.ReadLiveParentsAsync"/> + <see cref="ChildRowRoots"/> over the document row and
/// <c>ExternalParticipationService.GetEffectiveRootRecordFlagsAsync</c>.</para>
/// <para><b>Fail closed.</b> A Stored File or submission page that may be cut short, a source link that is not a GUID, a
/// Stored File whose purpose reads null (a masked row), a Stored File with a source communication or attachment identity but
/// no source document, an Internal submission whose document link is empty when the invoice has no InvoicePdf that is
/// sourceless or has a source document, a submission whose channel is unknown, a source that does not come back, an unreadable own value, marker, chain or communication, a root that cannot be resolved and a flag that
/// cannot be read each hide the invoice. A SUBMISSION with no firm is withheld from every external caller whatever its source
/// (<see cref="InvoiceHiddenReason.FirmMissing"/>; it hides the submission only, never its invoice). A failed read THROWS; the caller-aware seam
/// (<see cref="ExternalDataService.HideInvoicesForAsync"/>) turns that into "every asked invoice hidden".</para>
/// <para><b>Taint closure.</b> <see cref="HiddenFamilyRowsAsync"/> maps a row of the invoice family (lines, submissions,
/// approvals, external identifiers, corrections, Decision Records, Signals, Stored Files) to its invoice, directly or through
/// its invoice line, read app-only, and hides it with its invoice. A submission row is decided by
/// <see cref="DecideSubmissionsAsync"/> (review round 5), so one with no invoice is decided by its own source, as on the routes.</para>
/// <para><b>Cost.</b> One Stored File read and one submission read per 50 invoices; one invoice existence read per 50 submissions with
/// no invoice link (the submission decision only); one source read per 50 sources; the 188 decision (one read per hop
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

    /// <summary>
    /// Task 130-0b step 2: a CHILD table with an invoice link (<see cref="InvoiceLinks.Tables"/>, derived from the registries): not a
    /// root (<see cref="InvoiceLinks.IsRootWithInvoiceLink"/>; a root is never hidden by its invoice link) and not in the invoice family
    /// (<see cref="FamilyTables"/>, which keeps its own path). A row of such a table is hidden when any invoice it links to is.
    /// </summary>
    internal static bool IsInvoiceLinkedChildTable(string? logicalName) =>
        InvoiceLinks.HasInvoiceLink(logicalName)
        && !InvoiceLinks.IsRootWithInvoiceLink(logicalName)
        && !IsInvoiceFamilyTable(logicalName);

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
    internal static Task<InvoiceVisibilityResult> DecideAsync(
        IReadOnlyCollection<Guid> invoiceIds, InvoiceVisibilityReaders readers, CancellationToken ct) =>
        DecideAsync(invoiceIds, readers, NoneLogged, NoDocumentLogged, NoneReported, ct);

    /// <summary>
    /// <see cref="DecideAsync(IReadOnlyCollection{Guid}, InvoiceVisibilityReaders, CancellationToken)"/> within one submission decision
    /// (<see cref="DecideSubmissionsAsync"/>): <paramref name="loggedNotOwn"/> holds the (submission, invoice) pairs that call already logged
    /// as <see cref="InvoiceLinkNotOwnEvent"/>, so the same pair is logged once at Warning per call (task 165c);
    /// <paramref name="loggedDocumentNotOwn"/> holds the (submission, document) pairs that call already logged as
    /// <see cref="DocumentLinkNotOwnEvent"/> (task 165f, the same split); <paramref name="ownMissingReported"/> holds the invoices whose
    /// <see cref="OwnSubmissionMissingEvent"/> that call logs at Warning with its submission, so this read logs them at Debug (task 165e).
    /// </summary>
    internal static async Task<InvoiceVisibilityResult> DecideAsync(
        IReadOnlyCollection<Guid> invoiceIds, InvoiceVisibilityReaders readers, IReadOnlySet<(Guid Submission, Guid Invoice)> loggedNotOwn,
        IReadOnlySet<(Guid Submission, Guid Document)> loggedDocumentNotOwn, IReadOnlySet<Guid> ownMissingReported, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoiceIds);
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(loggedNotOwn);
        ArgumentNullException.ThrowIfNull(loggedDocumentNotOwn);
        ArgumentNullException.ThrowIfNull(ownMissingReported);
        var asked = invoiceIds.Where(id => id != Guid.Empty && id != ExternalDocumentNarrowing.UnreadableLink).Distinct().ToList();
        var hidden = new Dictionary<Guid, InvoiceHiddenReason>();
        var cost = new InvoiceVisibilityCost();
        if (asked.Count == 0)
            return new InvoiceVisibilityResult(asked, hidden, faulted: false, cost);

        // (0) ebilling-r1 095d-2b-1 (uac-r2 2026-11-06 C2): an invoice whose own field-secured firm hold (sprk_invoice.sprk_firmhold) is set,
        //     read APP-ONLY (InvoiceVisibilityReaders.FirmHeld), is hidden whatever its source: firm_missing (no firm yet) or the rerun-pending
        //     marker (Set firm wrote sprk_vendororg, but the billing-rules re-run has not committed, so the lines still carry their pre-firm
        //     statuses). Only the completed re-run clears the marker, so the invoice shows exactly when the re-run commits. No source read is
        //     spent on it.
        var firmHeld = await readers.FirmHeld(asked, ct).ConfigureAwait(false);
        foreach (var invoiceId in asked.Where(firmHeld.Held.Contains))
            hidden[invoiceId] = InvoiceHiddenReason.FirmMissing;

        // (0b) Task 130-0 (design.md:414): a partner sees only invoices of its own firms. The invoice's sprk_vendororg comes from the
        //      same app-only read as the hold above; an invoice with no firm, or one that did not come back, is not one of the caller's.
        //      Applied only on the caller-aware invoice seam (CallerFirms is null on the other seams).
        if (readers.CallerFirms is { } callerFirms)
        {
            foreach (var invoiceId in asked.Where(id => !hidden.ContainsKey(id)))
            {
                if (firmHeld.FirmOf(invoiceId) is not { } firm || !callerFirms.Contains(firm))
                    hidden[invoiceId] = InvoiceHiddenReason.FirmNotCallers;
            }
        }

        var open = asked.Where(id => !hidden.ContainsKey(id)).ToList();
        if (open.Count == 0)
            return new InvoiceVisibilityResult(asked, hidden, faulted: false, cost);

        // (1) The sources, one read per chunk of each table; both reads fail closed the same way.
        //     Stored Files (review round 4a): a purpose that reads null hides the invoice; a source document is a source,
        //     whatever else the row carries; a source communication or attachment with no document hides the invoice. An
        //     InvoicePdf that is sourceless (an upload's own PDF) or has a source document is one rule 3 can rely on.
        //     Submissions (by channel, the shared InvoiceSubmissionChannel enum; review round 3):
        //       Spa                     never a document source; the Stored Files decide;
        //       Internal, document set  that document is a source (the confirm route records its source only here);
        //       Internal, link empty    decided by the invoice's InvoicePdf: sourceless, visible; with a source document,
        //                               decided by that document; with no InvoicePdf, hidden;
        //       unknown or unreadable   hidden.
        //     Task 165e: only the invoice's own submission decides it, and an invoice whose own submission is not found is hidden.
        var sourcesOf = new Dictionary<Guid, List<Guid>>();
        var invoicePdfs = await ReadStoredFileSourcesAsync(open, readers.Query, sourcesOf, hidden, cost, ct).ConfigureAwait(false);
        var internalWithoutDocument = await ReadSubmissionSourcesAsync(
            open, readers.Query, sourcesOf, hidden, cost, readers.Logger, loggedNotOwn, loggedDocumentNotOwn, ownMissingReported, ct)
            .ConfigureAwait(false);
        foreach (var invoiceId in internalWithoutDocument.Where(id => !invoicePdfs.Contains(id)))
            hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceLinkUnreadable);

        var pending = sourcesOf.Where(kv => !hidden.ContainsKey(kv.Key)).ToList();
        var sourceIds = pending.SelectMany(kv => kv.Value).Distinct().ToList();
        if (sourceIds.Count == 0)
            return new InvoiceVisibilityResult(asked, hidden, faulted: false, cost);

        // (2) The sources themselves: THE source decision (rules 2-4 and the roots term).
        var decided = await DecideSourceDocumentsAsync(sourceIds, readers, cost, ct).ConfigureAwait(false);

        // The decision per invoice: hidden when ANY of its sources is.
        foreach (var (invoiceId, sources) in pending)
        {
            foreach (var source in sources.Distinct())
            {
                if (decided.HiddenReason(source) is { } reason)
                {
                    hidden.TryAdd(invoiceId, reason);
                    break;
                }
            }
        }

        return new InvoiceVisibilityResult(asked, hidden, faulted: false, cost);
    }

    /// <summary>
    /// THE decision over a set of source documents for a caller who is NOT an internal systemuser (the caller has decided
    /// that): each source read by id app-only (narrowing columns, marker, root links, live parents), task 188's narrowing with
    /// the marker-aware rule 2 (<see cref="InvoiceVisibilityReaders.DecideSources"/>), and the roots term (the six root links and
    /// the live roots of the source's other parents, then the EFFECTIVE flags). Every invoice decision and every
    /// submission-route decision over a source document goes through here. <see cref="Guid.Empty"/> and an unreadable link
    /// are never read and are hidden; a source that does not come back is hidden. Adds its reads to <paramref name="cost"/>
    /// when one is given. A failed read THROWS.
    /// </summary>
    internal static async Task<SourceDocumentDecision> DecideSourceDocumentsAsync(
        IReadOnlyCollection<Guid> sourceIds, InvoiceVisibilityReaders readers, InvoiceVisibilityCost? cost, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        ArgumentNullException.ThrowIfNull(readers);
        cost ??= new InvoiceVisibilityCost();
        var reasons = new Dictionary<Guid, InvoiceHiddenReason?>();
        var readable = sourceIds.Where(id => id != Guid.Empty && id != ExternalDocumentNarrowing.UnreadableLink).Distinct().ToList();
        if (readable.Count == 0)
            return new SourceDocumentDecision(reasons);

        // The sources themselves, by id, app-only: narrowing columns, marker, root links and live parents.
        var sourceRows = new Dictionary<Guid, Dictionary<string, JsonElement>>();
        var sourceChunks = await CommunicationOpenThreadGate.ReadChunksAsync(readable, (chunk, c) => readers.Query(
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
        cost.NarrowingReads += narrowing.Cost.Reads;

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

        foreach (var source in readable)
            reasons[source] = SourceHiddenReason(source);

        cost.Sources += readable.Count;
        return new SourceDocumentDecision(reasons);

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

    /// <summary>
    /// Review round 4b: THE decision over a page of the external submission routes (the list and the single read), keyed by
    /// SUBMISSION id, for a caller who is NOT an internal systemuser (the caller has decided that):
    /// <list type="bullet">
    /// <item>a submission with NO firm (<c>sprk_vendororg</c> blank) is withheld from every external caller, whatever its source or
    /// invoice (<see cref="InvoiceHiddenReason.FirmMissing"/>); this is decided first;</item>
    /// <item>ebilling-r1 task 095d-2b-1 (uac-r2 2026-11-05 conditions 1 and 2; 2026-11-06 C1): so is a submission whose invoice's own
    /// field-secured firm hold (<c>sprk_invoice.sprk_firmhold</c>) is set, read app-only (<see cref="InvoiceVisibilityReaders.FirmHeld"/>),
    /// the invoice asked by the submission's link and by its deterministic id (same reason). Set firm replaces firm_missing there with the
    /// rerun-pending marker, and only the re-run's one commit clears it, so the withhold lifts exactly when the re-run commits, and a failed,
    /// refused or dead-lettered re-run keeps it. A stored reason of firm_missing also withholds (an extra withhold only; the column is
    /// user-writable, so it never lifts one);</item>
    /// <item>ebilling-r1 task 165c: a submission whose invoice link is not its own invoice
    /// (<see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceBlockedHold.IsOwnSubmission"/>) is withheld
    /// (<see cref="InvoiceHiddenReason.InvoiceLinkNotOwn"/>) after the firm withhold above, and that invoice is not decided for it;</item>
    /// <item>a submission WITH an invoice is decided by the invoice predicate (<see cref="DecideAsync"/>), as before; since task 165e that
    /// predicate hides an invoice whose own submission it does not find (<see cref="InvoiceHiddenReason.OwnSubmissionMissing"/>, logged
    /// here at Warning with the submission id);</item>
    /// <item>task 165f (165e K1): one with no invoice link whose own invoice
    /// (<see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.InvoiceIdFor"/>) exists, read app-only, is decided by
    /// that invoice's verdict, as a linked one is;</item>
    /// <item>task 165f: an Internal submission's document link counts only when it is the submission's own document
    /// (<see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf"/>); any other link is
    /// logged at Warning (<see cref="DocumentLinkNotOwnEvent"/>) and the submission is decided as if it had none;</item>
    /// <item>one WITHOUT an invoice (Received, Processing, or a terminal outcome that produced none) is decided by its own
    /// source, by channel (the shared <see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel"/> enum):
    /// Spa is visible (never a document source, rule 1); Internal with a document is decided by that document through
    /// <see cref="DecideSourceDocumentsAsync"/> (the marker is honoured); Internal with an empty link is withheld (its source,
    /// if any, is recorded only on the invoice's Stored Files, which do not exist yet); an unknown, unreadable or unrecognised
    /// channel is withheld (<see cref="InvoiceHiddenReason.SourceKindUnknown"/>).</item>
    /// </list>
    /// One invoice decision and one source decision for the whole page. A submission it was not asked about is not visible. A
    /// failed read THROWS; the caller-aware seam (<see cref="ExternalDataService.HideSubmissionsForAsync"/>) turns that into
    /// "every asked submission withheld".
    /// </summary>
    internal static async Task<InvoiceVisibilityResult> DecideSubmissionsAsync(
        IReadOnlyCollection<Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView> submissions,
        InvoiceVisibilityReaders readers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        ArgumentNullException.ThrowIfNull(readers);
        var page = submissions.Where(s => s is not null && s.SubmissionId != Guid.Empty)
            .GroupBy(s => s.SubmissionId).Select(g => g.First()).ToList();
        var asked = page.Select(s => s.SubmissionId).ToList();
        var withheld = new Dictionary<Guid, InvoiceHiddenReason>();
        if (page.Count == 0)
            return new InvoiceVisibilityResult(asked, withheld, faulted: false);

        // (0) With NO firm (sprk_vendororg blank, as an internal from-email row may be): withheld from every external caller, whatever
        //     its source, decided before the invoice, channel and document below (a firmless row belongs to none of the caller's firms).
        //     No source or invoice read is spent on it.
        //     095d-2b-1 (uac-r2 2026-11-06 C1): a submission whose invoice's own field-secured firm hold (sprk_invoice.sprk_firmhold) is set,
        //     read APP-ONLY, is withheld the same way: firm_missing, or the rerun-pending marker Set firm writes and only the completed re-run
        //     clears. That marker decides; no user-writable submission column can lift it (Spaarke Core User holds Write on the submission, and
        //     its firm, reason and invoice link are not field-secured). The invoice is asked by the submission's link AND by its deterministic
        //     id (InvoiceSubmissionPersister.InvoiceIdFor), so unlinking or repointing the submission does not lift it either. The stored
        //     reason firm_missing (IsUndecidedForFirm) is kept as an EXTRA withhold only: it can add a withhold, never lift one.
        var firmHeld = await readers.FirmHeld(page.SelectMany(InvoiceKeysOf).Distinct().ToList(), ct).ConfigureAwait(false);
        foreach (var firmless in page.Where(s => HasNoFirm(s) || IsUndecidedForFirm(s) || InvoiceKeysOf(s).Any(firmHeld.Held.Contains)).ToList())
        {
            withheld[firmless.SubmissionId] = InvoiceHiddenReason.FirmMissing;
            page.Remove(firmless);
        }

        // Task 165c: visibility counts only a submission's own invoice (the one own-submission rule,
        // InvoiceBlockedHold.IsOwnSubmission). A submission whose invoice link is not its own invoice is withheld and decides nothing.
        // Each pair is logged once at Warning here; the invoice read below logs a pair already logged at Debug, and one it meets alone at Information.
        var loggedNotOwn = new HashSet<(Guid Submission, Guid Invoice)>();
        foreach (var notOwn in page.Where(s => InvoiceOf(s) is { } linked && linked != ExternalDocumentNarrowing.UnreadableLink
                     && !Sprk.Bff.Api.Services.Finance.Submission.InvoiceBlockedHold.IsOwnSubmission(linked, s.SubmissionId)).ToList())
        {
            withheld[notOwn.SubmissionId] = InvoiceHiddenReason.InvoiceLinkNotOwn;
            if (loggedNotOwn.Add((notOwn.SubmissionId, notOwn.InvoiceId!.Value)))
                LogInvoiceLinkNotOwn(readers.Logger, LogLevel.Warning, notOwn.SubmissionId, notOwn.InvoiceId!.Value);
            page.Remove(notOwn);
        }

        // Task 165f: an Internal submission's document link counts only when it is the submission's own document (the one rule,
        // InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf; no copy). Any other link contributes nothing: the submission is decided
        // as if it had no document. Each pair is logged once at Warning here; the invoice read below logs a pair already logged at Debug.
        var loggedDocumentNotOwn = new HashSet<(Guid Submission, Guid Document)>();
        var documentNotOwn = new HashSet<Guid>();
        foreach (var submission in page.Where(s => s.ChannelValue == (int)Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel.Internal
                     && DocumentOf(s) is { } document
                     && !Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(document, s.SubmissionId)))
        {
            documentNotOwn.Add(submission.SubmissionId);
            if (loggedDocumentNotOwn.Add((submission.SubmissionId, submission.DocumentId!.Value)))
                LogDocumentLinkNotOwn(readers.Logger, LogLevel.Warning, submission.SubmissionId, submission.DocumentId!.Value);
        }

        // Task 165f (165e K1): a submission with no invoice link whose own invoice (InvoiceIdFor, as InvoiceKeysOf names it) exists takes
        // that invoice's verdict, as a linked one does. One existence read per chunk, app-only; a read fault THROWS (every asked submission
        // withheld). One whose invoice does not exist yet (Received, Processing) is decided by channel below.
        var derivedOf = page.Where(s => InvoiceOf(s) is null).ToDictionary(
            s => s.SubmissionId, s => Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.InvoiceIdFor(s.SubmissionId));
        var existing = derivedOf.Count == 0
            ? new HashSet<Guid>()
            : await ReadExistingInvoicesAsync(derivedOf.Values.Distinct().ToList(), readers.Query, ct).ConfigureAwait(false);
        var decidingInvoice = page.ToDictionary(
            s => s.SubmissionId,
            s => InvoiceOf(s) ?? (derivedOf.TryGetValue(s.SubmissionId, out var derived) && existing.Contains(derived) ? derived : (Guid?)null));

        // (1) With an invoice: THE invoice predicate, one call for the page. Every invoice left is its submission's own, so task 165e's
        //     "own submission not found" is logged here at Warning with the submission, and the invoice read logs it at Debug.
        var invoiceIds = decidingInvoice.Values.OfType<Guid>().Distinct().ToList();
        var invoices = invoiceIds.Count == 0
            ? null
            : await DecideAsync(invoiceIds, readers, loggedNotOwn, loggedDocumentNotOwn, invoiceIds.ToHashSet(), ct).ConfigureAwait(false);
        var cost = invoices?.Cost ?? new InvoiceVisibilityCost();

        // (2) Without an invoice: by channel; an Internal submission's own document is decided with the rest of the page.
        var decidedBy = new Dictionary<Guid, Guid>();
        foreach (var submission in page)
        {
            if (decidingInvoice[submission.SubmissionId] is { } invoiceId)
            {
                if (invoices!.HiddenReason(invoiceId) is { } reason)
                {
                    withheld[submission.SubmissionId] = reason;
                    if (reason == InvoiceHiddenReason.OwnSubmissionMissing)
                        LogOwnSubmissionMissing(readers.Logger, LogLevel.Warning, submission.SubmissionId, invoiceId);
                }

                continue;
            }

            if (submission.ChannelValue is not { } channel
                || !Enum.IsDefined(typeof(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel), channel))
            {
                withheld[submission.SubmissionId] = InvoiceHiddenReason.SourceKindUnknown;
                continue;
            }

            switch ((Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel)channel)
            {
                case Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel.Spa:
                    break; // never a document source: visible
                case Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel.Internal:
                    if (submission.DocumentId is { } document && document != Guid.Empty && !documentNotOwn.Contains(submission.SubmissionId))
                        decidedBy[submission.SubmissionId] = document;
                    else
                        withheld[submission.SubmissionId] = InvoiceHiddenReason.SourceLinkUnreadable; // no own source to decide by yet
                    break;
                default:
                    // Defined but not handled above: a channel added to the enum later is unknown here until decided.
                    withheld[submission.SubmissionId] = InvoiceHiddenReason.SourceKindUnknown;
                    break;
            }
        }

        if (decidedBy.Count > 0)
        {
            var sources = await DecideSourceDocumentsAsync(decidedBy.Values.Distinct().ToList(), readers, cost, ct)
                .ConfigureAwait(false);
            foreach (var (submissionId, document) in decidedBy)
            {
                if (sources.HiddenReason(document) is { } reason)
                    withheld[submissionId] = reason;
            }
        }

        return new InvoiceVisibilityResult(asked, withheld, faulted: false, cost);

        static Guid? InvoiceOf(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView s) =>
            s.InvoiceId is { } id && id != Guid.Empty ? id : null;

        // A document link that is a GUID (not blank, Guid.Empty or ExternalDocumentNarrowing.UnreadableLink).
        static Guid? DocumentOf(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView s) =>
            s.DocumentId is { } id && id != Guid.Empty && id != ExternalDocumentNarrowing.UnreadableLink ? id : null;

        // The invoices whose firm hold withholds the submission: its linked invoice (when a GUID) and the one its pipeline creates for it.
        static IEnumerable<Guid> InvoiceKeysOf(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView s)
        {
            if (InvoiceOf(s) is { } linked && linked != ExternalDocumentNarrowing.UnreadableLink)
                yield return linked;
            yield return Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.InvoiceIdFor(s.SubmissionId);
        }

        // Blank, Guid.Empty or a link that is not a GUID (ExternalDocumentNarrowing.UnreadableLink): no firm can be named.
        static bool HasNoFirm(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView s) =>
            s.FirmId is not { } firm || firm == Guid.Empty || firm == ExternalDocumentNarrowing.UnreadableLink;

        // 095d-2b-1: the stored reason is still the firm hold's. sprk_outcomereasoncode is user-writable (not field-secured), so this only ever
        // ADDS a withhold; the invoice's firm hold above is what holds it (uac-r2 2026-11-06 C1).
        static bool IsUndecidedForFirm(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView s) =>
            string.Equals(s.OutcomeReasonCode, Sprk.Bff.Api.Services.Finance.Submission.SubmissionReasonCodes.FirmMissing, StringComparison.Ordinal);
    }

    private const string StoredFileCommunicationValue =
        "_" + Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.SourceCommunication + "_value";
    private const string StoredFileAttachmentIdentity = Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.SourceAttachmentIdentity;
    private const string StoredFilePurposeColumn = Sprk.Bff.Api.Services.Finance.Files.StoredFileSchema.Purpose;

    private const string SubmissionTable = "sprk_invoicesubmission";
    private const string SubmissionSet = "sprk_invoicesubmissions";
    private const string SubmissionIdField = "sprk_invoicesubmissionid";
    private const string SubmissionInvoiceValue = "_sprk_invoice_value";
    private const string SubmissionDocumentValue = "_sprk_document_value";
    private const string SubmissionChannel = "sprk_channel";
    private const string SubmissionFirmValue = "_sprk_vendororg_value";

    /// <summary>095d-2b-1: the stored outcome reason (a submission still held for its firm is withheld, <see cref="DecideSubmissionsAsync"/>).</summary>
    private const string SubmissionReasonValue = "sprk_outcomereasoncode";

    /// <summary>
    /// The Stored Files of <paramref name="asked"/> (094, 095), every row (review round 4a):
    /// <list type="bullet">
    /// <item>a purpose that reads null hides the invoice (a row masked by field security reads its secured columns as null);</item>
    /// <item>a source document is collected into <paramref name="sourcesOf"/> and decides the row, even when the row also
    /// carries a source communication or attachment identity (095's from-email rows: the .eml, its communication and part);</item>
    /// <item>a source communication or attachment identity with no document hides the invoice (a source this rule cannot
    /// check; 090's file route refuses the shape too);</item>
    /// <item>a sourceless file of a purpose other than InvoicePdf is not a source.</item>
    /// </list>
    /// Returns the invoices with an <see cref="Sprk.Bff.Api.Services.Finance.Files.StoredFilePurpose.InvoicePdf"/> rule 3 can
    /// rely on: sourceless (an upload's own invoice PDF) or with a source document (decided by that document). A supporting
    /// file never counts. A failed read THROWS.
    /// </summary>
    private static async Task<HashSet<Guid>> ReadStoredFileSourcesAsync(
        IReadOnlyList<Guid> asked, CommunicationOpenThreadGate.RowQuery query, Dictionary<Guid, List<Guid>> sourcesOf,
        Dictionary<Guid, InvoiceHiddenReason> hidden, InvoiceVisibilityCost cost, CancellationToken ct)
    {
        var invoicePdfs = new HashSet<Guid>();
        var (rows, _) = await ReadRowsOfInvoicesAsync(
            StoredFileSet,
            new[] { StoredFileIdField, StoredFileInvoiceValue, StoredFileSourceValue, StoredFileCommunicationValue,
                StoredFileAttachmentIdentity, StoredFilePurposeColumn },
            StoredFileInvoiceValue, asked, query, hidden, () => cost.StoredFileReads++, ct).ConfigureAwait(false);
        foreach (var (invoiceId, row) in rows)
        {
            if (IntOf(row, StoredFilePurposeColumn) is not { } purpose)
            {
                // Unreadable (masked, or never written): what the row is cannot be told.
                hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceLinkUnreadable);
                continue;
            }

            var isInvoicePdf = purpose == (int)Sprk.Bff.Api.Services.Finance.Files.StoredFilePurpose.InvoicePdf;
            switch (LinkOf(row, StoredFileSourceValue))
            {
                case { } source when source == ExternalDocumentNarrowing.UnreadableLink:
                    hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceLinkUnreadable);
                    break;
                case { } source:
                    AddSource(sourcesOf, invoiceId, source);
                    if (isInvoicePdf)
                        invoicePdfs.Add(invoiceId); // decided by its source document
                    break;
                case null when IsSet(row, StoredFileCommunicationValue) || IsSet(row, StoredFileAttachmentIdentity):
                    // A source this rule cannot check.
                    hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceLinkUnreadable);
                    break;
                case null when isInvoicePdf:
                    invoicePdfs.Add(invoiceId); // an upload's own invoice PDF
                    break;
                case null:
                    break; // a sourceless file of another purpose: not a source
            }
        }

        return invoicePdfs;
    }

    /// <summary>
    /// The submissions of <paramref name="asked"/>, every row, by channel (the shared
    /// <see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionChannel"/> enum): Spa is never a document source;
    /// Internal with a document collects it into <paramref name="sourcesOf"/> (the confirm route records its source only
    /// there); an unknown, unreadable or unrecognised channel hides the invoice. Returns the invoices with an Internal
    /// submission whose document link is empty (the caller hides them unless they have an InvoicePdf rule 3 can rely on). A
    /// failed read THROWS.
    /// Task 165c: only the invoice's own submission counts
    /// (<see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceBlockedHold.IsOwnSubmission"/>); a row whose link is not its own
    /// invoice adds nothing to that invoice (no source, no hide) and is logged: at Information (pass 2: the submission reader owns the Warning), or at Debug when the submission decision of
    /// the same call already logged that pair (<paramref name="loggedNotOwn"/>). A row whose own id cannot be read hides the invoice.
    /// Task 165e: an invoice whose submissions were read whole and none of them is its own is hidden
    /// (<see cref="InvoiceHiddenReason.OwnSubmissionMissing"/>) and logged with its id: at Information, or at Debug when the submission
    /// decision of the same call logs it at Warning (<paramref name="ownMissingReported"/>).
    /// Task 165f: an own Internal submission's document link that is not its own document
    /// (<see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf"/>) is no source; the
    /// invoice is decided as if the link were empty, and the pair is logged at Information, or at Debug when the submission decision of the
    /// same call already logged it (<paramref name="loggedDocumentNotOwn"/>).
    /// </summary>
    private static async Task<HashSet<Guid>> ReadSubmissionSourcesAsync(
        IReadOnlyList<Guid> asked, CommunicationOpenThreadGate.RowQuery query, Dictionary<Guid, List<Guid>> sourcesOf,
        Dictionary<Guid, InvoiceHiddenReason> hidden, InvoiceVisibilityCost cost, ILogger logger,
        IReadOnlySet<(Guid Submission, Guid Invoice)> loggedNotOwn, IReadOnlySet<(Guid Submission, Guid Document)> loggedDocumentNotOwn,
        IReadOnlySet<Guid> ownMissingReported, CancellationToken ct)
    {
        var internalWithoutDocument = new HashSet<Guid>();
        var withOwn = new HashSet<Guid>();
        var (rows, readWhole) = await ReadRowsOfInvoicesAsync(
            SubmissionSet,
            new[] { SubmissionIdField, SubmissionInvoiceValue, SubmissionDocumentValue, SubmissionChannel },
            SubmissionInvoiceValue, asked, query, hidden, () => cost.SubmissionReads++, ct).ConfigureAwait(false);
        foreach (var (invoiceId, row) in rows)
        {
            if (LinkOf(row, SubmissionIdField) is not { } submissionId || submissionId == ExternalDocumentNarrowing.UnreadableLink)
            {
                hidden.TryAdd(invoiceId, InvoiceHiddenReason.SourceLinkUnreadable); // cannot be shown to be the invoice's own
                withOwn.Add(invoiceId); // it may be the own row: hidden as unreadable, not as missing
                continue;
            }

            if (!Sprk.Bff.Api.Services.Finance.Submission.InvoiceBlockedHold.IsOwnSubmission(invoiceId, submissionId))
            {
                LogInvoiceLinkNotOwn(
                    logger, loggedNotOwn.Contains((submissionId, invoiceId)) ? LogLevel.Debug : LogLevel.Information, submissionId, invoiceId);
                continue;
            }

            withOwn.Add(invoiceId);

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
                        case { } source when !Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService
                            .IsExistingDocumentSubmissionIdOf(source, submissionId):
                            // Task 165f: not the submission's own document. It contributes nothing; the invoice is decided as if the link
                            // were empty (its InvoicePdf decides, and with none it is hidden).
                            LogDocumentLinkNotOwn(
                                logger, loggedDocumentNotOwn.Contains((submissionId, source)) ? LogLevel.Debug : LogLevel.Information,
                                submissionId, source);
                            internalWithoutDocument.Add(invoiceId);
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

        // Task 165e: an invoice whose submissions were read whole and none of them is its own is hidden (fail closed): only its own
        // submission decides it. A chunk the read could not use has already hidden its invoices. The submission decision logs the Warning
        // only when this is the invoice's recorded reason, so an invoice already hidden by its Stored Files is logged here at Information.
        foreach (var invoiceId in readWhole.Where(id => !withOwn.Contains(id)))
        {
            var recorded = hidden.TryAdd(invoiceId, InvoiceHiddenReason.OwnSubmissionMissing);
            LogOwnSubmissionMissing(
                logger, recorded && ownMissingReported.Contains(invoiceId) ? LogLevel.Debug : LogLevel.Information, invoiceId);
        }

        return internalWithoutDocument;
    }

    /// <summary>
    /// Every row of <paramref name="entitySet"/> whose <paramref name="invoiceValue"/> is one of <paramref name="asked"/>, one
    /// read per chunk, paired with its invoice. A chunk whose read may be cut short hides every invoice of the chunk
    /// (<see cref="InvoiceHiddenReason.SourcesTruncated"/>); a row the filter cannot have produced hides every invoice of the
    /// chunk and none of its rows is used. Also returns the invoices whose chunk was read whole (every row used). A failed read THROWS.
    /// </summary>
    private static async Task<(List<(Guid Invoice, Dictionary<string, JsonElement> Row)> Rows, HashSet<Guid> ReadWhole)> ReadRowsOfInvoicesAsync(
        string entitySet, IReadOnlyList<string> select, string invoiceValue, IReadOnlyList<Guid> asked,
        CommunicationOpenThreadGate.RowQuery query, Dictionary<Guid, InvoiceHiddenReason> hidden, Action counted,
        CancellationToken ct)
    {
        var result = new List<(Guid, Dictionary<string, JsonElement>)>();
        var readWhole = new HashSet<Guid>();
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
            readWhole.UnionWith(chunk);
        }

        return (result, readWhole);
    }

    /// <summary>Task 165c: the event a submission whose invoice link is not its own invoice logs (the two ids and this code only).</summary>
    internal const string InvoiceLinkNotOwnEvent = "ebilling.visibility.invoice_link_not_own";

    private static void LogInvoiceLinkNotOwn(ILogger logger, LogLevel level, Guid submissionId, Guid invoiceId) =>
        logger.Log(level, "{EventCode}: submission {SubmissionId}, invoice {InvoiceId}", InvoiceLinkNotOwnEvent, submissionId, invoiceId);

    /// <summary>Task 165e: the event an invoice whose own submission is not found logs (this code and the ids only).</summary>
    internal const string OwnSubmissionMissingEvent = "ebilling.visibility.own_submission_missing";

    private static void LogOwnSubmissionMissing(ILogger logger, LogLevel level, Guid invoiceId) =>
        logger.Log(level, "{EventCode}: invoice {InvoiceId}", OwnSubmissionMissingEvent, invoiceId);

    private static void LogOwnSubmissionMissing(ILogger logger, LogLevel level, Guid submissionId, Guid invoiceId) =>
        logger.Log(level, "{EventCode}: submission {SubmissionId}, invoice {InvoiceId}", OwnSubmissionMissingEvent, submissionId, invoiceId);

    /// <summary>Task 165f: the event a submission whose document link is not its own document logs (this code and the two ids only).</summary>
    internal const string DocumentLinkNotOwnEvent = "ebilling.visibility.document_link_not_own";

    private static void LogDocumentLinkNotOwn(ILogger logger, LogLevel level, Guid submissionId, Guid documentId) =>
        logger.Log(level, "{EventCode}: submission {SubmissionId}, document {DocumentId}", DocumentLinkNotOwnEvent, submissionId, documentId);

    /// <summary>No pair logged yet: an invoice decision outside a submission decision.</summary>
    private static readonly IReadOnlySet<(Guid Submission, Guid Invoice)> NoneLogged = new HashSet<(Guid Submission, Guid Invoice)>();

    /// <summary>No (submission, document) pair logged yet: an invoice decision outside a submission decision.</summary>
    private static readonly IReadOnlySet<(Guid Submission, Guid Document)> NoDocumentLogged = new HashSet<(Guid Submission, Guid Document)>();

    /// <summary>
    /// Task 165f (165e K1): which of <paramref name="invoiceIds"/> exist, read BY ID app-only (one read per chunk, the invoice key only). A
    /// row the filter cannot have produced counts every invoice of its chunk as existing, so those are decided by the invoice predicate
    /// (fail closed). A failed read THROWS.
    /// </summary>
    private static async Task<HashSet<Guid>> ReadExistingInvoicesAsync(
        IReadOnlyList<Guid> invoiceIds, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        var found = new HashSet<Guid>();
        var chunks = await CommunicationOpenThreadGate.ReadChunksAsync(invoiceIds, (chunk, c) => query(InvoiceSet,
            $"$select={InvoiceIdField}" +
            $"&$filter={string.Join(" or ", chunk.Select(id => $"{InvoiceIdField} eq {id:D}"))}&$top={chunk.Length}", c), ct)
            .ConfigureAwait(false);
        foreach (var (chunk, rows) in chunks)
        {
            var inChunk = chunk.ToHashSet();
            var ids = rows.Select(row => LinkOf(row, InvoiceIdField)).ToList();
            if (ids.Any(id => id is not { } i || !inChunk.Contains(i)))
            {
                found.UnionWith(chunk);
                continue;
            }

            found.UnionWith(ids.OfType<Guid>());
        }

        return found;
    }

    /// <summary>No invoice reported by a submission decision: an invoice decision outside one.</summary>
    private static readonly IReadOnlySet<Guid> NoneReported = new HashSet<Guid>();

    private static void AddSource(Dictionary<Guid, List<Guid>> sourcesOf, Guid invoiceId, Guid source)
    {
        if (!sourcesOf.TryGetValue(invoiceId, out var list))
            sourcesOf[invoiceId] = list = new List<Guid>();
        list.Add(source);
    }

    private static bool IsSet(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    /// <summary>A text column: <c>null</c> when absent, null or not a string.</summary>
    private static string? TextOf(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>An integer column (an option value): <c>null</c> when absent, null or not a 32-bit integer.</summary>
    private static int? IntOf(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)
            ? i
            : null;

    /// <summary>
    /// The taint closure (model 9.1): which of <paramref name="rowIds"/> (rows of <paramref name="table"/>, a table of
    /// <see cref="FamilyTables"/>) are hidden because their invoice, directly or through their invoice line, is hidden. The
    /// rows' links are read BY ID app-only (a row that does not come back, or a link that is not a GUID, hides the row); a row
    /// with no invoice and no line is not hidden, EXCEPT a submission (review round 5): <c>sprk_invoicesubmission</c> rows are
    /// decided by <see cref="DecideSubmissionsAsync"/>, the function the submission routes use, so a submission with no invoice
    /// is decided by its own source exactly as on the routes. A failed read THROWS.
    /// </summary>
    internal static async Task<IReadOnlySet<Guid>> HiddenFamilyRowsAsync(
        string table, IReadOnlyCollection<Guid> rowIds, InvoiceVisibilityReaders readers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentNullException.ThrowIfNull(readers);
        if (string.Equals(table, SubmissionTable, StringComparison.OrdinalIgnoreCase))
            return await HiddenSubmissionRowsAsync(rowIds, readers, ct).ConfigureAwait(false);

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
    /// Review round 5 (F1): the assistant tools' decision over submission rows. Each row's invoice link, <c>sprk_channel</c>,
    /// <c>_sprk_document_value</c> and firm (<c>_sprk_vendororg_value</c>) are read BY ID app-only in one read per chunk, and the
    /// page is decided by <see cref="DecideSubmissionsAsync"/>: no firm, withheld; with an invoice, the invoice predicate; without
    /// one, the submission's own source.
    /// A row that does not come back, or whose invoice link is not a GUID, is hidden. A failed read THROWS.
    /// </summary>
    private static async Task<IReadOnlySet<Guid>> HiddenSubmissionRowsAsync(
        IReadOnlyCollection<Guid> rowIds, InvoiceVisibilityReaders readers, CancellationToken ct)
    {
        var distinct = rowIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var hidden = new HashSet<Guid>();
        if (distinct.Count == 0)
            return hidden;

        var views = await ReadSubmissionViewsAsync(distinct, readers.Query, ct).ConfigureAwait(false);
        var decidable = new List<Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView>();
        foreach (var id in distinct)
        {
            if (views.TryGetValue(id, out var view) && view.InvoiceId != ExternalDocumentNarrowing.UnreadableLink)
                decidable.Add(view);
            else
                hidden.Add(id); // not back, or an invoice that cannot be decided
        }

        if (decidable.Count == 0)
            return hidden;

        var decision = await DecideSubmissionsAsync(decidable, readers, ct).ConfigureAwait(false);
        foreach (var view in decidable.Where(v => !decision.IsVisible(v.SubmissionId)))
            hidden.Add(view.SubmissionId);

        // Task 130-0b (design.md:414): on the assistant tools' seam (the only caller of this function that passes the caller's firms), a
        // submission is ALSO hidden when its own firm (sprk_vendororg, read above) is not one of the caller's firms, even when its invoice
        // is visible. The most restrictive reading: the submission routes scope by the submission's own firm only.
        if (readers.CallerFirms is { } callerFirms)
        {
            foreach (var view in decidable.Where(v => v.FirmId is not { } firm || !callerFirms.Contains(firm)))
                hidden.Add(view.SubmissionId);
        }

        return hidden;
    }

    /// <summary>
    /// The submissions <paramref name="ids"/>, read BY ID app-only (one read per chunk), as the view
    /// <see cref="DecideSubmissionsAsync"/> decides: the invoice link, the document link, the firm link (a value that is not a GUID
    /// reads as <see cref="ExternalDocumentNarrowing.UnreadableLink"/>; a blank firm withholds the submission) and the raw channel value. A row that does not come back is
    /// absent. A failed read THROWS.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView>> ReadSubmissionViewsAsync(
        IReadOnlyList<Guid> ids, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        var columns = new[] { SubmissionIdField, SubmissionInvoiceValue, SubmissionDocumentValue, SubmissionChannel, SubmissionFirmValue, SubmissionReasonValue };
        var chunks = await CommunicationOpenThreadGate.ReadChunksAsync(ids, (chunk, c) => query(SubmissionSet,
            $"$select={string.Join(",", columns)}" +
            $"&$filter={string.Join(" or ", chunk.Select(id => $"{SubmissionIdField} eq {id:D}"))}&$top={chunk.Length}", c), ct)
            .ConfigureAwait(false);
        var found = new Dictionary<Guid, Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView>();
        foreach (var (chunk, rows) in chunks)
        {
            var inChunk = chunk.ToHashSet();
            foreach (var row in rows)
            {
                if (LinkOf(row, SubmissionIdField) is not { } id || !inChunk.Contains(id))
                    continue;
                found[id] = new Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView
                {
                    SubmissionId = id,
                    InvoiceId = LinkOf(row, SubmissionInvoiceValue),
                    DocumentId = LinkOf(row, SubmissionDocumentValue),
                    ChannelValue = IntOf(row, SubmissionChannel),
                    FirmId = LinkOf(row, SubmissionFirmValue),
                    OutcomeReasonCode = TextOf(row, SubmissionReasonValue),
                };
            }
        }

        return found;
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

    /// <summary>
    /// ebilling-r1 095d-2b-1 (uac-r2 2026-11-06 C1, C2): of <paramref name="invoiceIds"/>, the invoices whose own firm hold
    /// (<see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceFirmHold.InvoiceHoldColumn"/>) is set
    /// (<see cref="Sprk.Bff.Api.Services.Finance.Submission.InvoiceFirmHold.IsInvoiceHeld"/>: any non-blank value, fail closed), read APP-ONLY
    /// through <paramref name="entities"/>, one read per <see cref="ChunkSize"/> invoices. The column is field-secured, so a read on a caller's
    /// token would return null ("no hold") for a user outside the Readers profile; the application identity is in "Spaarke BFF-Managed Field
    /// Writers". An invoice that does not come back holds nothing (as <c>InvoiceFirmHold.InvoiceHoldStateAsync</c>). A failed read THROWS.
    /// Bound to <see cref="InvoiceVisibilityReaders.FirmHeld"/> by <see cref="ExternalDataService"/>; the ArchTest
    /// <c>InvoiceFirmHoldAppOnlyGuardTests</c> lists this type as a reviewed reader.
    /// Task 130-0: the same read also returns each invoice's firm (<c>sprk_vendororg</c>), for the own-firms rule; no extra query.
    /// </summary>
    internal static async Task<InvoiceFirmRead> ReadFirmHeldInvoicesAsync(
        Spaarke.Dataverse.IGenericEntityService entities, IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(invoiceIds);
        var held = new HashSet<Guid>();
        var firms = new Dictionary<Guid, Guid>();
        foreach (var chunk in invoiceIds.Where(id => id != Guid.Empty && id != ExternalDocumentNarrowing.UnreadableLink).Distinct().Chunk(ChunkSize))
        {
            var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(InvoiceTable)
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(
                    Sprk.Bff.Api.Services.Finance.Submission.InvoiceFirmHold.InvoiceHoldColumn, InvoiceFirmColumn),
                Criteria = new Microsoft.Xrm.Sdk.Query.FilterExpression(Microsoft.Xrm.Sdk.Query.LogicalOperator.And),
            };
            query.Criteria.AddCondition(InvoiceIdField, Microsoft.Xrm.Sdk.Query.ConditionOperator.In, chunk.Cast<object>().ToArray());
            var inChunk = chunk.ToHashSet();
            foreach (var invoice in (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities)
            {
                if (!inChunk.Contains(invoice.Id))
                    continue;
                if (Sprk.Bff.Api.Services.Finance.Submission.InvoiceFirmHold.IsInvoiceHeld(invoice))
                    held.Add(invoice.Id);
                // A value that is not a firm reference reads as "no firm" (fail closed under the own-firms rule).
                if (invoice.Attributes.TryGetValue(InvoiceFirmColumn, out var value)
                    && value is Microsoft.Xrm.Sdk.EntityReference { Id: var firm } && firm != Guid.Empty)
                    firms[invoice.Id] = firm;
            }
        }

        return new InvoiceFirmRead(held, firms);
    }

    /// <summary>Task 130-0: the invoice's firm, read with the firm hold (<see cref="ReadFirmHeldInvoicesAsync"/>).</summary>
    internal const string InvoiceFirmColumn = Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.InvoiceVendorOrg;

    /// <summary>Absent or null: <c>null</c> (not set); a non-empty GUID string: that id; anything else
    /// <see cref="ExternalDocumentNarrowing.UnreadableLink"/>. Also read by <see cref="InvoiceLinks"/> (task 130-0b).</summary>
    internal static Guid? LinkOf(IReadOnlyDictionary<string, JsonElement> row, string column)
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
/// <param name="FirmHeld">095d-2b-1: the invoices whose own field-secured firm hold is set, read app-only
/// (<see cref="ExternalInvoiceVisibility.ReadFirmHeldInvoicesAsync"/>); a failed read THROWS. Since task 130-0 the same read also
/// returns each invoice's firm.</param>
/// <param name="CallerFirms">Task 130-0: the caller's firms, at least one, on the caller-aware invoice seam
/// (<see cref="ExternalDataService.HideInvoicesForAsync"/>): an invoice whose firm is not one of them is hidden
/// (<see cref="InvoiceHiddenReason.FirmNotCallers"/>). Task 130-0b: also on the assistant tools' seam
/// (<see cref="ExternalDataService.HideInvoiceFamilyRowsAsync"/>), where a submission row whose own firm is not one of them is hidden
/// too. <c>null</c> on the seam that does not apply the rule here (the submission routes, scoped by firm in their filter).</param>
internal sealed record InvoiceVisibilityReaders(
    CommunicationOpenThreadGate.RowQuery Query,
    InvoiceSourceDecider DecideSources,
    LiveParentRootReader LiveParents,
    EffectiveRootFlagReader RootFlags,
    ILogger Logger,
    InvoiceFirmHoldReader FirmHeld,
    IReadOnlySet<Guid>? CallerFirms = null);

/// <summary>095d-2b-1: of a batch of invoices, those whose own field-secured firm hold (<c>sprk_invoice.sprk_firmhold</c>) is set, and
/// (task 130-0) each invoice's firm, read app-only in one read per chunk. A failed read THROWS.</summary>
internal delegate Task<InvoiceFirmRead> InvoiceFirmHoldReader(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct);

/// <summary>
/// 095d-2b-1 / task 130-0: what <see cref="ExternalInvoiceVisibility.ReadFirmHeldInvoicesAsync"/> read: the invoices whose firm hold is set,
/// and the firm (<c>sprk_vendororg</c>) of each invoice that came back with one.
/// </summary>
internal sealed class InvoiceFirmRead
{
    private readonly IReadOnlyDictionary<Guid, Guid> _firms;

    internal InvoiceFirmRead(IReadOnlySet<Guid> held, IReadOnlyDictionary<Guid, Guid> firms)
    {
        Held = held ?? throw new ArgumentNullException(nameof(held));
        _firms = firms ?? throw new ArgumentNullException(nameof(firms));
    }

    /// <summary>The invoices whose own firm hold is set.</summary>
    internal IReadOnlySet<Guid> Held { get; }

    /// <summary>The firm of <paramref name="invoiceId"/>; <c>null</c> when it has none, or did not come back.</summary>
    internal Guid? FirmOf(Guid invoiceId) => _firms.TryGetValue(invoiceId, out var firm) ? firm : null;
}

/// <summary>
/// Task 130-0: the caller's firms as the caller-aware invoice seam reads them, with "no firm" and "unknown" kept apart: a caller
/// <see cref="CallerFirmsState.Known"/> to be a member of firms, a caller known to have <see cref="CallerFirmsState.NoFirm"/>, and
/// firms that could not be read (<see cref="CallerFirmsState.Unknown"/>).
/// </summary>
internal sealed class CallerFirms
{
    private CallerFirms(CallerFirmsState state, IReadOnlySet<Guid> firms)
    {
        State = state;
        Firms = firms;
    }

    internal CallerFirmsState State { get; }

    /// <summary>The caller's firms; empty unless <see cref="State"/> is <see cref="CallerFirmsState.Known"/>.</summary>
    internal IReadOnlySet<Guid> Firms { get; }

    /// <summary>The read could not be completed: never "no firm".</summary>
    internal static CallerFirms Unknown { get; } = new(CallerFirmsState.Unknown, new HashSet<Guid>());

    /// <summary>The read completed and the caller is a member of no firm.</summary>
    internal static CallerFirms NoFirm { get; } = new(CallerFirmsState.NoFirm, new HashSet<Guid>());

    /// <summary>
    /// From <c>ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync</c>'s answer (the one definition of "the caller's
    /// firms", shared with the submission routes and /me): <paramref name="unreadable"/> or a null list is
    /// <see cref="Unknown"/>; no usable id is <see cref="NoFirm"/>; otherwise <see cref="CallerFirmsState.Known"/>.
    /// </summary>
    internal static CallerFirms From(IReadOnlyCollection<Guid>? firms, bool unreadable)
    {
        if (unreadable || firms is null)
            return Unknown;
        var known = firms.Where(id => id != Guid.Empty).ToHashSet();
        return known.Count == 0 ? NoFirm : new CallerFirms(CallerFirmsState.Known, known);
    }
}

/// <summary>Task 130-0: the three states of <see cref="CallerFirms"/>.</summary>
internal enum CallerFirmsState
{
    Unknown = 0,
    NoFirm,
    Known,
}

/// <summary>
/// Task 130-0: the caller's firms, read once per call of the caller-aware invoice seam. Bound by <c>AddExternalAccess</c> to
/// <c>ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync</c> through <see cref="CallerFirms.From"/>. A fault may THROW;
/// the seam treats a throw, a null answer and <see cref="CallerFirmsState.Unknown"/> alike (every asked invoice hidden).
/// </summary>
internal delegate Task<CallerFirms> CallerFirmReader(CallerPrincipal caller, CancellationToken ct);

/// <summary>
/// The caller-aware seam every external surface applies (<see cref="ExternalDataService.HideInvoicesForAsync"/>): an internal
/// systemuser hides nothing; every other caller (a CIAM contact, a flagged or unresolvable systemuser, a caller whose lookup
/// failed) gets the predicate; a failed read hides every asked invoice (<see cref="InvoiceVisibilityResult.Faulted"/>).
/// Registered by <c>AddExternalAccess</c>.
/// </summary>
internal delegate Task<InvoiceVisibilityResult> ExternalInvoiceHiding(
    CallerPrincipal caller, IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct);

/// <summary>
/// Review round 4b: the caller-aware seam of the external submission routes (<see cref="ExternalDataService.HideSubmissionsForAsync"/>,
/// <see cref="ExternalInvoiceVisibility.DecideSubmissionsAsync"/>): which of a page of submissions the caller may see, keyed by
/// submission id. An internal systemuser sees every one; every other caller gets the decision (with an invoice, the invoice
/// predicate; without one, its own source); a failed read withholds every asked submission
/// (<see cref="InvoiceVisibilityResult.Faulted"/>). Registered by <c>AddExternalAccess</c>.
/// </summary>
internal delegate Task<InvoiceVisibilityResult> ExternalInvoiceSubmissionHiding(
    CallerPrincipal caller, IReadOnlyCollection<Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionView> submissions,
    CancellationToken ct);

/// <summary>
/// The assistant's <c>dataverse.*</c> tools' seam (task 098 surface (d)): of <paramref name="rowIds"/> (rows of an invoice-family
/// table the USER's own read returned), the ids an external caller may NOT see. The rows' invoice links and the decision are
/// read app-only (a submission row is decided by <see cref="ExternalInvoiceVisibility.DecideSubmissionsAsync"/>, as on the
/// routes); only the hidden id set comes back (a hide decision; no row data). The tool has already decided the caller is
/// external. A failed read THROWS (the tool call fails with no data). Registered by <c>AddExternalAccess</c>.
/// Task 130-0b (design.md:414): a partner sees only invoices of its own firms. <paramref name="caller"/> is the tool call's caller
/// (<see cref="InvoiceFamilyCaller"/>, one per tool call), whose firms are read at most once for every call of the seam in that
/// tool call. No firm, or firms that cannot be read, hide every asked row.
/// Task 130-0b step 2: <paramref name="table"/> may also be a CHILD table with an invoice link
/// (<see cref="ExternalInvoiceVisibility.IsInvoiceLinkedChildTable"/>: a to-do, event, document, communication, analysis, memo...).
/// Such a row is hidden when ANY invoice it links to is not visible (the same invoice decision, with the caller's firms) or cannot be
/// decided; a row with no invoice link is not hidden. On a child table a fault does not throw: a failed link read hides every asked
/// row, and a failed decision (or firms that cannot be read) hides every asked row that links to an invoice.
/// </summary>
public delegate Task<IReadOnlySet<Guid>> AppOnlyInvoiceFamilyHiding(
    InvoiceFamilyCaller caller, string table, IReadOnlyCollection<Guid> rowIds, CancellationToken ct);

/// <summary>
/// Task 130-0b: the caller of ONE assistant tool call, as <see cref="AppOnlyInvoiceFamilyHiding"/> takes it. The tool builds it once,
/// from the external-caller classification it already made (<c>CallerExternality.ClassifyCallerAsync</c>), and passes the same
/// object to every call of the seam in that tool call. It remembers the caller's firms, so they are read at most ONCE per tool call.
/// A caller with no resolved systemuser has no firm (nothing is read).
/// </summary>
public sealed class InvoiceFamilyCaller
{
    private readonly object _gate = new();
    private Task<CallerFirms>? _firms;

    public InvoiceFamilyCaller(Guid? systemUserId)
    {
        SystemUserId = systemUserId is { } id && id != Guid.Empty ? id : null;
    }

    /// <summary>The caller's resolved <c>systemuserid</c>; <c>null</c> when none resolved.</summary>
    public Guid? SystemUserId { get; }

    /// <summary>
    /// The caller's firms: the first call reads them through <paramref name="read"/> (only when a systemuser resolved); every later
    /// call in the same tool call gets that answer, a fault included.
    /// </summary>
    internal Task<CallerFirms> FirmsAsync(Func<Guid, CancellationToken, Task<CallerFirms>> read, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(read);
        lock (_gate)
            return _firms ??= SystemUserId is { } systemUserId ? read(systemUserId, ct) : Task.FromResult(CallerFirms.NoFirm);
    }
}

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

    /// <summary>A SUBMISSION with no firm (<c>sprk_vendororg</c> blank, null or not a GUID): withheld from every external caller,
    /// whatever its source (095c P2 precondition); a firmless submission hides itself only, never its invoice. Since 095d-2b-1 (uac-r2
    /// 2026-11-06 C1, C2) also an INVOICE whose own field-secured firm hold (<c>sprk_invoice.sprk_firmhold</c>, read app-only) is set
    /// (firm_missing, or the rerun-pending marker until the re-run commits), and every submission of such an invoice.</summary>
    FirmMissing,

    /// <summary>ebilling-r1 task 165c: a SUBMISSION whose invoice link is not its own invoice (visibility counts only a submission's own
    /// invoice); it hides the submission only, never the linked invoice.</summary>
    InvoiceLinkNotOwn,

    /// <summary>ebilling-r1 task 165e: an INVOICE whose own submission is not found among the submissions linked to it (visibility decides
    /// only from the invoice's own submission, failing closed), and every submission that takes that invoice's verdict.</summary>
    OwnSubmissionMissing,

    /// <summary>ebilling-r1 task 130-0 (design.md:414): an INVOICE whose firm (<c>sprk_vendororg</c>) is not one of the caller's firms
    /// (an invoice with no firm included), and every invoice for a caller known to be a member of no firm (owner decision 2026-11-08).
    /// Applied on the caller-aware invoice seam only.</summary>
    FirmNotCallers,
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

/// <summary>
/// <see cref="ExternalInvoiceVisibility.DecideSourceDocumentsAsync"/>'s answer: per source document, why it is hidden, or
/// <c>null</c> when it is visible. A source it was not asked about (or could not read) is hidden.
/// </summary>
internal sealed class SourceDocumentDecision
{
    private readonly IReadOnlyDictionary<Guid, InvoiceHiddenReason?> _reasons;

    internal SourceDocumentDecision(IReadOnlyDictionary<Guid, InvoiceHiddenReason?> reasons) => _reasons = reasons;

    /// <summary>Why <paramref name="source"/> is hidden; <c>null</c> only when it was decided and is visible.</summary>
    internal InvoiceHiddenReason? HiddenReason(Guid source) =>
        _reasons.TryGetValue(source, out var reason) ? reason : InvoiceHiddenReason.SourceNotFound;

    /// <summary>Whether <paramref name="source"/> was decided and is visible.</summary>
    internal bool IsVisible(Guid source) => HiddenReason(source) is null;
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

    /// <summary>Why <paramref name="invoiceId"/> is not visible; <c>null</c> only when it was asked and is visible.</summary>
    internal InvoiceHiddenReason? HiddenReason(Guid invoiceId) =>
        IsVisible(invoiceId) ? null : _hidden.TryGetValue(invoiceId, out var reason) ? reason : InvoiceHiddenReason.SourceNotFound;

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
        InvoiceHiddenReason.FirmMissing => "firm_missing",
        InvoiceHiddenReason.InvoiceLinkNotOwn => "invoice_link_not_own",
        InvoiceHiddenReason.OwnSubmissionMissing => "own_submission_missing",
        InvoiceHiddenReason.FirmNotCallers => "firm_not_callers",
        _ => "read_failed",
    };
}
