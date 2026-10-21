using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// D-176 (#2118, ontology task 157): whether the CALLER may see what a billing Duplicate finding quotes about an earlier invoice. The
/// earlier invoice is readable only when the caller's own read of <c>sprk_invoices</c> returns it. A reference names that invoice by
/// id, or (older Signals, and every same-invoice repeat) by the earlier submission's tracking number within the current invoice's
/// vendor org, resolved through <c>sprk_invoicesubmissions</c> (alternate key <c>sprk_invoicesubmission_firm_trackingnumber</c> =
/// vendor org + tracking number) read as the caller.
/// </summary>
/// <remarks>
/// <para><b>Fail closed.</b> No submission row, more than one, a row with no invoice, or a reference with neither an id nor a vendor
/// org and number: not readable. A 403 on a table is a verdict (none of its rows is readable). Any other failure (a 401, a 5xx, an
/// unparseable body, an exception) is a fault, and a fault makes NOTHING readable: the caller then withholds every Duplicate it judged.</para>
/// <para><b>Bounded.</b> One submission read per vendor org and one invoice read per call, each in In lists of at most
/// <see cref="ChunkSize"/> values; never one read per reference.</para>
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

    internal const string InvoiceEntitySet = "sprk_invoices";
    internal const string InvoiceKey = "sprk_invoiceid";
    internal const string SubmissionEntitySet = "sprk_invoicesubmissions";
    internal const string SubmissionKey = "sprk_invoicesubmissionid";
    internal const string TrackingNumberColumn = "sprk_trackingnumber";

    /// <summary><c>sprk_vendororg</c> as a Web API name (on <c>sprk_invoice</c> and <c>sprk_invoicesubmission</c>).</summary>
    internal const string VendorOrgColumn = "_sprk_vendororg_value";

    /// <summary><c>sprk_invoice</c> as a Web API name (on <c>sprk_invoicesubmission</c> and <c>sprk_billingevent</c>).</summary>
    internal const string InvoiceColumn = "_sprk_invoice_value";

    private readonly ILogger<EarlierInvoiceReadability> _logger;

    public EarlierInvoiceReadability(ILogger<EarlierInvoiceReadability> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>One earlier-invoice reference. <paramref name="EarlierInvoiceId"/> wins when set (only the invoice read is needed);
    /// otherwise <paramref name="VendorOrgId"/> and <paramref name="TrackingNumber"/> are resolved through the submission.</summary>
    /// <param name="EarlierInvoiceId">The earlier <c>sprk_invoice</c> id (<c>duplicateOf.invoiceId</c>).</param>
    /// <param name="VendorOrgId">The CURRENT invoice's vendor org: the earlier submission is always the same firm's.</param>
    /// <param name="TrackingNumber">The earlier submission's tracking number (<c>duplicateOf.trackingNumber</c>).</param>
    public readonly record struct Reference(Guid? EarlierInvoiceId, Guid? VendorOrgId, string? TrackingNumber);

    /// <summary>The <paramref name="references"/> whose earlier invoice the caller's own read returns. On a fault, none.</summary>
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
    /// null on anything else (a 401 is the token failing mid-request, so a fault, not a verdict; a 5xx; an unparseable body; an
    /// exception), logged at Warning with the table and the error code only. <see cref="SignalReadService"/> reads the current
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
    /// One read per vendor org (In lists of at most <see cref="ChunkSize"/> numbers). Zero rows, more than one, or no invoice: absent
    /// (not readable). Null on a fault.
    /// </summary>
    private async Task<Dictionary<Reference, Guid>?> ResolveByTrackingNumberAsync(
        IDataverseUserClient caller, IReadOnlyList<Reference> references, CancellationToken ct)
    {
        var resolved = new Dictionary<Reference, Guid>();
        foreach (var byOrg in references.GroupBy(r => r.VendorOrgId!.Value))
        {
            // Dataverse compares text without regard to case, so the numbers are matched back the same way.
            foreach (var chunk in byOrg.Select(r => r.TrackingNumber!).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(ChunkSize))
            {
                var filter = $"{VendorOrgColumn} eq {byOrg.Key:D} and Microsoft.Dynamics.CRM.In(PropertyName='{TrackingNumberColumn}',"
                    + $"PropertyValues=[{string.Join(',', chunk.Select(Quote))}])";
                var rows = await ReadAsCallerAsync(
                    caller,
                    $"{SubmissionEntitySet}?$select={SubmissionKey},{TrackingNumberColumn},{VendorOrgColumn},{InvoiceColumn}&$filter={Uri.EscapeDataString(filter)}",
                    "sprk_invoicesubmission",
                    ct).ConfigureAwait(false);
                if (rows is null)
                {
                    return null;
                }

                var asked = chunk.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var submissions = rows
                    .Where(row => ReadGuid(row, VendorOrgColumn) == byOrg.Key)
                    .Select(row => (Number: row.TryGetProperty(TrackingNumberColumn, out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
                        Invoice: ReadGuid(row, InvoiceColumn)))
                    .Where(s => s.Number is not null && asked.Contains(s.Number))
                    .GroupBy(s => s.Number!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                foreach (var reference in byOrg.Where(r => asked.Contains(r.TrackingNumber!)))
                {
                    if (submissions.TryGetValue(reference.TrackingNumber!, out var found) && found.Count == 1 && found[0].Invoice is { } invoice)
                    {
                        resolved[reference] = invoice;
                    }
                }
            }
        }

        return resolved;
    }

    /// <summary>An OData string literal.</summary>
    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
