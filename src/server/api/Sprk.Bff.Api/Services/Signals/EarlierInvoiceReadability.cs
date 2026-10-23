using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// D-176 (#2118, ontology task 157): whether the CALLER may see what a billing Duplicate finding quotes about an earlier invoice. The
/// earlier invoice is readable only when the caller's own read of <c>sprk_invoices</c> returns it. A finding names that invoice by id
/// (<c>duplicateOf.invoiceId</c>); a same-invoice repeat (<c>duplicate.line_same_invoice</c>) names none, and its earlier invoice is the
/// line's own current invoice, which each surface supplies. #2294 / DEF-043: a finding identified only by a tracking number names no
/// invoice this check will look up: it is withheld with the generic sentence, whatever the number would resolve to (<see cref="ReferencesOf"/>).
/// </summary>
/// <remarks>
/// <para><b>Fail closed.</b> A reference with no invoice id is not readable. A 403 on the table is a verdict (none of its rows is
/// readable). Any other failure (a 401, a 5xx, an unparseable body, an exception) is a fault, and a fault makes NOTHING readable: the
/// caller then withholds every Duplicate it judged.</para>
/// <para><b>Bounded.</b> One invoice read per chunk of <see cref="ChunkSize"/> (200) distinct invoice ids; never one read per reference.
/// Every read fits one page: a body that still carries <c>@odata.nextLink</c> is a fault, never a short answer.</para>
/// <para><b>Callers.</b> <see cref="SignalReadService"/> (the worklist) and ebilling task 119's invoice-review and blocked-submission
/// reads (<c>DuplicateDetailGate</c>) apply this one rule: <see cref="ReferencesOf"/> decides what a finding is judged by, and
/// <see cref="ShownAsync{TKey}"/> judges it. Each surface resolves its own CURRENT invoice (the worklist: the line's invoice, bounded by
/// the Signal's frozen core record; the finance routes: the invoice the route reads); this class judges only invoices.</para>
/// <para>Component justification (CLAUDE.md section 11). Existing: <c>SignalReadService.WitnessesTheCallerReadsAsync</c> (D-142) reads
/// witness rows as the caller. Extension: that read is private and keyed on uac-r2's lineage tables; a second caller (ebilling 119) needs
/// the identical rule. Cost of doing nothing: a reader who cannot open the earlier invoice sees its tracking number in the Duplicate
/// finding (D-176), or 119 carries a second copy of the rule that can drift.</para>
/// <para>Public only because <see cref="SignalReadService"/>'s public constructor takes it (dependency injection uses public
/// constructors); its helpers stay internal.</para>
/// </remarks>
public sealed class EarlierInvoiceReadability
{
    /// <summary>
    /// The sentence of a Duplicate finding whose earlier invoice is withheld. The same text as ebilling's write-time
    /// <c>BillingRuleCatalogue.WithheldDuplicateTemplate</c> (D-112), so a row withheld at read time reads exactly like one withheld when
    /// it was written.
    /// </summary>
    public const string WithheldSentence = "This matches an earlier submission.";

    /// <summary>Values per batched read (In lists of at most 200, as the D-142 witness read).</summary>
    internal const int ChunkSize = 200;

    internal const string InvoiceEntitySet = "sprk_invoices";
    internal const string InvoiceKey = "sprk_invoiceid";

    /// <summary><c>sprk_invoice</c> as a Web API name (on <c>sprk_billingevent</c>): <see cref="SignalReadService"/> reads a line's
    /// current invoice by it.</summary>
    internal const string InvoiceColumn = "_sprk_invoice_value";

    private readonly ILogger<EarlierInvoiceReadability> _logger;

    public EarlierInvoiceReadability(ILogger<EarlierInvoiceReadability> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>One earlier-invoice reference: the invoice a Duplicate's detail is judged by. Null when the finding names none that may
    /// be judged (<see cref="ReferencesOf"/>): such a reference is never readable.</summary>
    /// <param name="EarlierInvoiceId">The <c>sprk_invoice</c> id to read as the caller.</param>
    public readonly record struct Reference(Guid? EarlierInvoiceId);

    /// <summary>
    /// #2294 / DEF-043: what ONE stored Duplicate finding is judged by, the rule every surface applies.
    /// <list type="bullet">
    /// <item>It quotes <c>duplicateOf.invoiceId</c>: one reference per distinct id (D-176, unchanged).</item>
    /// <item>Otherwise, a same-invoice repeat (<see cref="IsSameInvoiceRepeat"/>): its earlier invoice is the line's own current invoice,
    /// <paramref name="currentInvoiceId"/>, which the surface has bounded (no tracking-number lookup). None: withheld.</item>
    /// <item>Otherwise (identified only by a tracking number, or by nothing): a reference that identifies nothing, so the finding is
    /// withheld with <see cref="WithheldSentence"/>, whatever the number would resolve to. A number is never looked up: a re-pointed
    /// current invoice would otherwise choose the firm it is resolved in.</item>
    /// </list>
    /// </summary>
    /// <param name="earlierInvoiceIds">The non-empty <c>duplicateOf.invoiceId</c> values the finding quotes.</param>
    /// <param name="sameInvoiceRepeat">The finding is a same-invoice repeat (<see cref="IsSameInvoiceRepeat"/>).</param>
    /// <param name="currentInvoiceId">The line's own current invoice, as the surface bounds it; null when it has none.</param>
    public static IReadOnlyList<Reference> ReferencesOf(IEnumerable<Guid> earlierInvoiceIds, bool sameInvoiceRepeat, Guid? currentInvoiceId)
    {
        ArgumentNullException.ThrowIfNull(earlierInvoiceIds);

        var ids = earlierInvoiceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count > 0)
        {
            return [.. ids.Select(id => new Reference(id))];
        }

        return sameInvoiceRepeat && currentInvoiceId is { } current && current != Guid.Empty
            ? [new Reference(current)]
            : [new Reference(null)];
    }

    /// <summary>A same-invoice repeat: the reason code ebilling's <c>DuplicateHandler</c> gives a line that repeats an earlier line of
    /// the SAME invoice (<see cref="Billing.BillingReasonCodes.DuplicateOfEarlierLine"/>, compared exactly).</summary>
    public static bool IsSameInvoiceRepeat(string? reasonCode) =>
        string.Equals(reasonCode, Billing.BillingReasonCodes.DuplicateOfEarlierLine, StringComparison.Ordinal);

    /// <summary>
    /// The items whose earlier-invoice detail may be SHOWN to the caller: the ONE rule both callers (the worklist and ebilling task
    /// 119) apply. An item is shown only when it has AT LEAST ONE reference and EVERY one of its references is readable. An item with
    /// no references is NOT shown. A reference with no invoice id is not readable, so an item holding one is NOT shown. On any fault
    /// (a failed or unparseable read, a 401, an exception), nothing is shown.
    /// </summary>
    /// <remarks>
    /// One <see cref="Reference"/> = one invoice, as <see cref="ReferencesOf"/> builds it for a finding. The reads are the batched ones of
    /// <see cref="ReadableAsync"/>, over the references of the items that could be shown at all.
    /// </remarks>
    /// <typeparam name="TKey">The caller's item key (a Signal id, an invoice id, ...).</typeparam>
    /// <param name="caller">The caller's Dataverse client: every read here is the caller's.</param>
    /// <param name="refsByItem">Each item and the references (the priors) its detail quotes. An item with an empty list is not shown.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The keys of the items whose detail may be shown.</returns>
    public async Task<IReadOnlySet<TKey>> ShownAsync<TKey>(
        IDataverseUserClient caller, IReadOnlyDictionary<TKey, IReadOnlyCollection<Reference>> refsByItem, CancellationToken ct)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(refsByItem);

        var candidates = refsByItem.Where(item => item.Value is { Count: > 0 } && item.Value.All(IsUsable)).ToList();
        if (candidates.Count == 0)
        {
            return new HashSet<TKey>();
        }

        var readable = await ReadableAsync(caller, candidates.SelectMany(item => item.Value), ct).ConfigureAwait(false);
        return candidates.Where(item => item.Value.All(readable.Contains)).Select(item => item.Key).ToHashSet();
    }

    /// <summary>A reference names an invoice by its id.</summary>
    private static bool IsUsable(Reference reference) => reference.EarlierInvoiceId is { } id && id != Guid.Empty;

    /// <summary>
    /// The <paramref name="references"/> whose earlier invoice the caller's own read returns, judged one by one. On a fault, none.
    /// A caller deciding whether to SHOW an item (which may quote several priors, or none) should use
    /// <see cref="ShownAsync{TKey}"/>, which applies the combining rule: at least one reference and every one readable.
    /// </summary>
    /// <param name="caller">The caller's Dataverse client: every read here is the caller's.</param>
    /// <param name="references">The references to judge (duplicates are judged once).</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<IReadOnlySet<Reference>> ReadableAsync(
        IDataverseUserClient caller, IEnumerable<Reference> references, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(references);

        var readable = new HashSet<Reference>();
        var invoiceOf = new Dictionary<Reference, Guid>();
        foreach (var reference in references.Distinct())
        {
            // No id: nothing identifies the earlier invoice, so it is never readable.
            if (reference.EarlierInvoiceId is { } id && id != Guid.Empty)
            {
                invoiceOf[reference] = id;
            }
        }

        if (invoiceOf.Count == 0)
        {
            return readable;
        }

        var invoices = new HashSet<Guid>();
        foreach (var chunk in invoiceOf.Values.Distinct().Chunk(ChunkSize))
        {
            var rows = await ReadAsCallerAsync(
                caller, $"{InvoiceEntitySet}?$select={InvoiceKey}&$filter={SignalReadService.InFilter(InvoiceKey, chunk)}", "sprk_invoice", ct)
                .ConfigureAwait(false);
            if (rows is null)
            {
                return new HashSet<Reference>();
            }

            foreach (var row in rows)
            {
                if (ReadGuid(row, InvoiceKey) is { } id && chunk.Contains(id))
                {
                    invoices.Add(id);
                }
            }
        }

        foreach (var (reference, invoice) in invoiceOf)
        {
            if (invoices.Contains(invoice))
            {
                readable.Add(reference);
            }
        }

        return readable;
    }

    /// <summary>
    /// A batched read AS THE CALLER, with this check's fail-closed rules: the rows on success; none on a 403 (no Read on the table);
    /// null on anything else (a 401 is the token failing mid-request, so a fault, not a verdict; a 5xx; an unparseable body; a page
    /// that carries <c>@odata.nextLink</c>, whose missing rows would otherwise read as "no row"; an exception), logged at Warning
    /// with the table and the error code only. <see cref="SignalReadService"/> reads the current
    /// invoice through this too, so the D-176 reads share one rule.
    /// </summary>
    internal async Task<IReadOnlyList<JsonElement>?> ReadAsCallerAsync(
        IDataverseUserClient caller, string relativePath, string table, CancellationToken ct)
    {
        DataverseUserResponse response;
        try
        {
            response = await caller.GetAsync(relativePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Duplicate earlier-invoice check: the caller's read of {Table} faulted ({ExceptionType}); every Duplicate's earlier-invoice detail is withheld (D-176, fail closed).",
                table, ex.GetType().Name);
            return null;
        }

        if (response.IsSuccess
            && response.Body is { } body && body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            if (body.TryGetProperty("@odata.nextLink", out _))
            {
                _logger.LogWarning(
                    "Duplicate earlier-invoice check: the caller's read of {Table} returned more than one page; every Duplicate's earlier-invoice detail is withheld (D-176, fail closed).",
                    table);
                return null;
            }

            return value.EnumerateArray().Select(e => e.Clone()).ToList();
        }

        if (!response.IsSuccess && response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied && response.StatusCode != 401)
        {
            return [];
        }

        _logger.LogWarning(
            "Duplicate earlier-invoice check: the caller's read of {Table} failed ({ErrorCode}); every Duplicate's earlier-invoice detail is withheld (D-176, fail closed).",
            table, response.IsSuccess ? "unparseable" : response.ErrorCode);
        return null;
    }

    /// <summary>A GUID column of a Web API row; null when absent, not a string, not a GUID, or empty.</summary>
    internal static Guid? ReadGuid(JsonElement row, string column) =>
        row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty(column, out var value) && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var id) && id != Guid.Empty
            ? id
            : null;
}
