using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// D-176 (#2118, ontology task 157): whether the CALLER may see what a billing Duplicate finding quotes about an earlier invoice. The
/// earlier invoice is readable only when the caller's own read of <c>sprk_invoices</c> returns it. A reference names that invoice by
/// id, or (a finding written without one, today as before: every same-invoice repeat, <c>duplicate.line_same_invoice</c>, and a prior
/// with no invoice) by the earlier submission's tracking number within the current invoice's
/// vendor org, resolved through <c>sprk_invoicesubmissions</c> (alternate key <c>sprk_invoicesubmission_firm_trackingnumber</c> =
/// vendor org + tracking number) read as the caller.
/// </summary>
/// <remarks>
/// <para><b>Fail closed.</b> No submission row, more than one, a row with no invoice, or a reference with neither an id nor a vendor
/// org and number: not readable. A 403 on a table is a verdict (none of its rows is readable). Any other failure (a 401, a 5xx, an
/// unparseable body, an exception) is a fault, and a fault makes NOTHING readable: the caller then withholds every Duplicate it judged.</para>
/// <para><b>Bounded.</b> One submission read per chunk of <see cref="ChunkSize"/> (200) distinct tracking numbers and group of
/// <see cref="OrgsPerRead"/> (20) vendor orgs, and one invoice read per chunk of 200 distinct invoice ids; never one read per
/// reference. Every read fits one page: a body that still carries <c>@odata.nextLink</c> is a fault, never a short answer.</para>
/// <para><b>Callers.</b> <see cref="SignalReadService"/> (the worklist) and ebilling task 119's invoice-review read apply this one rule.
/// Each resolves its own CURRENT invoice and vendor org; this class judges only the earlier invoice.</para>
/// <para>Component justification (CLAUDE.md section 11). Existing: <c>SignalReadService.WitnessesTheCallerReadsAsync</c> (D-142) reads
/// witness rows as the caller. Extension: that read is private and keyed on uac-r2's lineage tables, and an earlier invoice is named
/// by a tracking number that must first be resolved; a second caller (ebilling 119) needs the identical rule. Cost of doing nothing: a
/// reader who cannot open the earlier invoice sees its tracking number in the Duplicate finding (D-176), or 119 carries a second copy
/// of the rule that can drift.</para>
/// <para>Public only because <see cref="SignalReadService"/>'s public constructor takes it (dependency injection uses public
/// constructors); its helpers stay internal.</para>
/// </remarks>
public sealed class EarlierInvoiceReadability
{
    /// <summary>
    /// The sentence of a Duplicate finding whose earlier invoice is withheld. The same text as ebilling's write-time
    /// <c>BillingRuleCatalogue.WithheldDuplicateTemplate</c> (D-112; on <c>work/ebilling-r1</c>, not on this branch), so a row
    /// withheld at read time reads exactly like one withheld when it was written.
    /// </summary>
    public const string WithheldSentence = "This matches an earlier submission.";

    /// <summary>Values per batched read (In lists of at most 200, as the D-142 witness read).</summary>
    internal const int ChunkSize = 200;

    /// <summary>
    /// The vendor orgs one submission read names (keeps the URL short). A chunk naming more is read in groups of this size, so every
    /// read filters on its orgs: under the (org, number) alternate key a page then holds at most 200 x 20 = 4,000 rows, inside
    /// Dataverse's 5,000-row page. Without the org filter, numbers such as <c>1001.1</c> recur across firms and a page could truncate.
    /// </summary>
    internal const int OrgsPerRead = 20;

    internal const string InvoiceEntitySet = "sprk_invoices";
    internal const string InvoiceKey = "sprk_invoiceid";
    internal const string SubmissionEntitySet = "sprk_invoicesubmissions";
    internal const string SubmissionKey = "sprk_invoicesubmissionid";
    internal const string TrackingNumberColumn = "sprk_trackingnumber";

    /// <summary><c>sprk_vendororg</c> as a Web API name (on <c>sprk_invoice</c> and <c>sprk_invoicesubmission</c>).</summary>
    internal const string VendorOrgColumn = "_sprk_vendororg_value";

    /// <summary><c>sprk_vendororg</c> as the LOGICAL name a <c>Microsoft.Dynamics.CRM.In</c> filter needs (the <c>_value</c> name is a 400 there; proven live).</summary>
    internal const string VendorOrgLogicalName = "sprk_vendororg";

    /// <summary><c>sprk_invoice</c> as a Web API name (on <c>sprk_invoicesubmission</c> and <c>sprk_billingevent</c>).</summary>
    internal const string InvoiceColumn = "_sprk_invoice_value";

    /// <summary><c>sprk_invoicesubmission.sprk_invoice</c> as the LOGICAL name a <c>Microsoft.Dynamics.CRM.In</c> filter needs (as
    /// <see cref="VendorOrgLogicalName"/>): <see cref="SignalReadService"/> reads a current invoice's submissions by it (UAC review F4).</summary>
    internal const string SubmissionInvoiceFilterColumn = "sprk_invoice";

    private readonly ILogger<EarlierInvoiceReadability> _logger;

    public EarlierInvoiceReadability(ILogger<EarlierInvoiceReadability> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>One earlier-invoice reference. <paramref name="EarlierInvoiceId"/> wins when set (only the invoice read is needed);
    /// otherwise <paramref name="VendorOrgId"/> and <paramref name="TrackingNumber"/> are resolved through the submission.</summary>
    /// <param name="EarlierInvoiceId">The earlier <c>sprk_invoice</c> id (<c>duplicateOf.invoiceId</c>).</param>
    /// <param name="VendorOrgId">The CURRENT invoice's vendor org, taken from its submission (the firm its priors were read from), never
    /// from the invoice's own <c>sprk_vendororg</c> (UAC review F4): the earlier submission is always the same firm's.</param>
    /// <param name="TrackingNumber">The earlier submission's tracking number (<c>duplicateOf.trackingNumber</c>).</param>
    public readonly record struct Reference(Guid? EarlierInvoiceId, Guid? VendorOrgId, string? TrackingNumber);

    /// <summary>
    /// The items whose earlier-invoice detail may be SHOWN to the caller: the ONE rule both callers (the worklist and ebilling task
    /// 119) apply. An item is shown only when it has AT LEAST ONE reference and EVERY one of its references is readable. An item with
    /// no references is NOT shown. A reference with neither an id nor (a vendor org plus a tracking number) is not readable, so an
    /// item holding one is NOT shown. On any fault (a failed or unparseable read, a 401, an exception), nothing is shown.
    /// </summary>
    /// <remarks>
    /// One <see cref="Reference"/> = one prior: its id is used when present, else its tracking number within its vendor org. A caller
    /// that holds two forms of the same prior passes ONE reference for it. The reads are the batched ones of
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

    /// <summary>A reference names its prior by an id, or by a vendor org plus a tracking number.</summary>
    private static bool IsUsable(Reference reference) =>
        (reference.EarlierInvoiceId is { } id && id != Guid.Empty)
        || (reference.VendorOrgId is { } org && org != Guid.Empty && !string.IsNullOrWhiteSpace(reference.TrackingNumber));

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
        var byNumber = new List<Reference>();
        foreach (var reference in references.Distinct())
        {
            if (reference.EarlierInvoiceId is { } id && id != Guid.Empty)
            {
                invoiceOf[reference] = id;
            }
            else if (reference.VendorOrgId is { } org && org != Guid.Empty && !string.IsNullOrWhiteSpace(reference.TrackingNumber))
            {
                byNumber.Add(reference);
            }

            // Neither: nothing identifies the earlier invoice, so it is never readable.
        }

        if (byNumber.Count > 0)
        {
            var resolved = await ResolveByTrackingNumberAsync(caller, byNumber, ct).ConfigureAwait(false);
            if (resolved is null)
            {
                return new HashSet<Reference>();
            }

            foreach (var (reference, invoice) in resolved)
            {
                invoiceOf[reference] = invoice;
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

    /// <summary>
    /// Each number-only reference's earlier invoice: the ONE submission of that vendor org with that tracking number, and its invoice.
    /// One read per chunk of <see cref="ChunkSize"/> distinct tracking numbers and group of <see cref="OrgsPerRead"/> vendor orgs: an
    /// In list on the number and an In list on the vendor org. The exact (org, number) pair is matched after the read. Zero rows, more than one, or no invoice: absent (not readable). Null on a fault.
    /// </summary>
    private async Task<Dictionary<Reference, Guid>?> ResolveByTrackingNumberAsync(
        IDataverseUserClient caller, IReadOnlyList<Reference> references, CancellationToken ct)
    {
        var resolved = new Dictionary<Reference, Guid>();

        // Dataverse compares text without regard to case, so the numbers are matched back the same way.
        foreach (var chunk in references.Select(r => r.TrackingNumber!).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(ChunkSize))
        {
            var asked = chunk.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var inChunk = references.Where(r => asked.Contains(r.TrackingNumber!)).ToList();
            var rows = new List<JsonElement>();
            foreach (var orgs in inChunk.Select(r => r.VendorOrgId!.Value).Distinct().Chunk(OrgsPerRead))
            {
                var numbers = inChunk.Where(r => orgs.Contains(r.VendorOrgId!.Value)).Select(r => r.TrackingNumber!)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                var filter = $"Microsoft.Dynamics.CRM.In(PropertyName='{TrackingNumberColumn}',PropertyValues=[{string.Join(',', numbers.Select(Literal))}])"
                    + " and " + SignalReadService.InFilter(VendorOrgLogicalName, orgs);

                var page = await ReadAsCallerAsync(
                    caller,
                    $"{SubmissionEntitySet}?$select={TrackingNumberColumn},{VendorOrgColumn},{InvoiceColumn}&$filter={Uri.EscapeDataString(filter)}",
                    "sprk_invoicesubmission",
                    ct).ConfigureAwait(false);
                if (page is null)
                {
                    return null;
                }

                rows.AddRange(page);
            }

            var submissions = rows
                .Select(row => (Org: ReadGuid(row, VendorOrgColumn),
                    Number: row.TryGetProperty(TrackingNumberColumn, out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
                    Invoice: ReadGuid(row, InvoiceColumn)))
                .Where(s => s.Org is not null && s.Number is not null && asked.Contains(s.Number))
                .GroupBy(s => (s.Org!.Value, s.Number!), PairComparer.Instance)
                .ToDictionary(g => g.Key, g => g.ToList(), PairComparer.Instance);

            foreach (var reference in inChunk)
            {
                if (submissions.TryGetValue((reference.VendorOrgId!.Value, reference.TrackingNumber!), out var found)
                    && found.Count == 1 && found[0].Invoice is { } invoice)
                {
                    resolved[reference] = invoice;
                }
            }
        }

        return resolved;
    }

    /// <summary>The (vendor org, tracking number) pair, the number compared without regard to case.</summary>
    private sealed class PairComparer : IEqualityComparer<(Guid Org, string Number)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((Guid Org, string Number) x, (Guid Org, string Number) y) =>
            x.Org == y.Org && string.Equals(x.Number, y.Number, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid Org, string Number) pair) =>
            HashCode.Combine(pair.Org, StringComparer.OrdinalIgnoreCase.GetHashCode(pair.Number));
    }

    /// <summary>
    /// A value of a <c>Microsoft.Dynamics.CRM.In</c> list, as a JSON string. The list is parsed as a JSON array: an OData-style doubled
    /// quote (<c>O''Brien</c>) is a 400, a JSON string (<c>"O'Brien"</c>) matches the row (both proven live on spaarkedev1). The caller
    /// then escapes the whole filter for the URL.
    /// </summary>
    private static string Literal(string value) => JsonSerializer.Serialize(value, LiteralOptions);

    private static readonly JsonSerializerOptions LiteralOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
