using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>One eBilling item of the external notification feed: an invoice whose outcome the firm may open.</summary>
internal sealed record EbillingFeedItem(Guid InvoiceId, DateTimeOffset OccurredOn);

/// <summary>The eBilling items of a caller's feed. <paramref name="Truncated"/> is true when a cut or a read that could not be decided left the list possibly incomplete (never silent).</summary>
internal sealed record EbillingFeedResult(IReadOnlyList<EbillingFeedItem> Items, bool Truncated);

/// <summary>
/// The eBilling source of the external notification feed (ebilling-r1 task 132, FR-39, FR-38, NFR-10, EB-D-258): the invoices of the caller's firms whose
/// outcome the firm should look at, DERIVED from the invoice's own status (no new table, no per-user row): a Rejected invoice, a Returned to Firm invoice
/// and an Approved invoice that carries an adjustment (the projection's one definition, <see cref="FirmOutcomeKind.ApprovedWithAdjustments"/>), each
/// changed inside the feed window. A rejected SUBMISSION that created no invoice has no route to open, so it is not an item (a known limit).
/// </summary>
/// <remarks>
/// <para><b>Fail closed, per item.</b> The candidates are read app-only, narrowed to the caller's firms, then EACH is re-checked by
/// <see cref="FirmOutcomeAuthorizationFilter.DecideInvoiceForFeedAsync"/>: the SAME invoice rule the outcome route applies (firm, Read on the core record,
/// the secure-roots rule and the (a') hiding decision). Only a Visible invoice is an item; Denied is dropped; a read or decision that faulted drops the
/// item and flags the feed truncated. A caller with no firm, an unreadable contact, or a workforce systemuser (who reads an outcome through the internal
/// review) gets nothing. So another firm's contact, and a contact who cannot read the invoice's core record, see no item.</para>
/// <para><b>Bounded.</b> At most <see cref="MaxCandidates"/> invoices are looked at, newest first; the cut is flagged. The item's time is the invoice's
/// last change, which can also be a change that is not the outcome (a known limit: a later edit re-surfaces the item).</para>
/// <para><b>Seam.</b> The four reads are delegates (the DI constructor binds them to the real services; the internal one is the test seam, the pattern of
/// <see cref="FirmNoticeComposer"/>), so the per-item rule is driven with doubles while the rule itself is the outcome route's own.</para>
/// <para>§11: <i>Existing</i> — the feed's grant and message sources and 130b's outcome rule. <i>Extension</i> — the feed has no invoice source, and the
/// outcome rule lives in a route filter; this adds a source that asks that rule, with no new route and no change to the item's shape. <i>Cost of doing nothing</i> —
/// a firm learns of a rejected, returned or adjusted invoice only by opening the SPA's invoice list (FR-39's in-app half).</para>
/// </remarks>
internal sealed class EbillingFeedSource
{
    /// <summary>The most invoices looked at for one feed.</summary>
    public const int MaxCandidates = 25;

    /// <summary>The fixed sender label on an eBilling item: the SPA's feed shows a sender name for a "message" item (the existing item shape; no new kind).</summary>
    public const string SenderLabel = "Billing";

    /// <summary>
    /// The <c>sprk_invoice</c> columns whose values reach a firm through this source: the item's id (<c>sprk_invoiceid</c>, always returned), the status that
    /// decides whether it is news, and the change time shown as the item's time. EB-D-207: every column here must be in the external invoice module's
    /// ReadableColumns, except the timestamp-only <c>modifiedon</c>, which is named in the pinning test, so a column added here fails that test.
    /// The query's column set IS this list, so the pin cannot drift from what is read.
    /// </summary>
    internal static readonly string[] RenderedInvoiceColumns = ["sprk_invoiceid", "statuscode", "modifiedon"];

    private readonly Func<CallerPrincipal, CancellationToken, Task<(IReadOnlyList<Guid> Firms, bool Unreadable)>> _readFirms;
    private readonly Func<IReadOnlyList<Guid>, DateTimeOffset, CancellationToken, Task<IReadOnlyList<Entity>>> _readCandidates;
    private readonly Func<CallerPrincipal, IReadOnlyList<Guid>, Guid, CancellationToken, Task<InvoiceFeedDecision>> _decide;
    private readonly Func<Guid, CancellationToken, Task<FirmOutcome?>> _outcome;
    private readonly ILogger<EbillingFeedSource> _logger;

    /// <summary>The DI constructor: the real firm read, the app-only candidate read, the outcome route's own invoice rule and the projection.</summary>
    public EbillingFeedSource(
        IGenericEntityService entities,
        ExternalParticipationService participations,
        ExternalDataService data,
        InvoiceSubmissionService submissions,
        FirmFindingsProjection projection,
        ILogger<EbillingFeedSource> logger,
        ILogger<FirmOutcomeAuthorizationFilter> filterLogger,
        ExternalInvoiceHiding? hideInvoices = null,
        ExternalInvoiceSubmissionHiding? hideSubmissions = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(participations);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(submissions);
        ArgumentNullException.ThrowIfNull(projection);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _readFirms = (principal, ct) => ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync(principal, participations, logger, ct);
        _readCandidates = async (firms, cutoff, ct) =>
        {
            // This window's Rejected, Returned and Approved invoices of the caller's firms, newest change first (one more than the cap, to know it was cut).
            var query = new QueryExpression("sprk_invoice")
            {
                ColumnSet = new ColumnSet(RenderedInvoiceColumns),
                Criteria = new FilterExpression(LogicalOperator.And),
                TopCount = MaxCandidates + 1,
            };
            query.Criteria.AddCondition("sprk_vendororg", ConditionOperator.In, firms.Cast<object>().ToArray());
            query.Criteria.AddCondition(
                "statuscode", ConditionOperator.In,
                InvoiceStatusCodes.Rejected, InvoiceStatusCodes.ReturnedToFirm, InvoiceStatusCodes.Approved);
            query.Criteria.AddCondition("modifiedon", ConditionOperator.GreaterEqual, cutoff.UtcDateTime);
            query.AddOrder("modifiedon", OrderType.Descending);
            return (IReadOnlyList<Entity>)(await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.ToList();
        };

        // The outcome route's own invoice rule, per invoice (fail closed). The filter is cheap to build and holds nothing between calls.
        _decide = (principal, firms, invoiceId, ct) => new FirmOutcomeAuthorizationFilter(
                FirmOutcomeSubject.Invoice, participations, data, submissions, filterLogger, hideInvoices, hideSubmissions)
            .DecideInvoiceForFeedAsync(principal, firms, invoiceId, ct);
        _outcome = projection.GetForInvoiceAsync;
    }

    /// <summary>The test seam: the four reads as delegates.</summary>
    internal EbillingFeedSource(
        Func<CallerPrincipal, CancellationToken, Task<(IReadOnlyList<Guid> Firms, bool Unreadable)>> readFirms,
        Func<IReadOnlyList<Guid>, DateTimeOffset, CancellationToken, Task<IReadOnlyList<Entity>>> readCandidates,
        Func<CallerPrincipal, IReadOnlyList<Guid>, Guid, CancellationToken, Task<InvoiceFeedDecision>> decide,
        Func<Guid, CancellationToken, Task<FirmOutcome?>> outcome,
        ILogger<EbillingFeedSource> logger)
    {
        _readFirms = readFirms ?? throw new ArgumentNullException(nameof(readFirms));
        _readCandidates = readCandidates ?? throw new ArgumentNullException(nameof(readCandidates));
        _decide = decide ?? throw new ArgumentNullException(nameof(decide));
        _outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The caller's eBilling feed items changed since <paramref name="cutoff"/>.</summary>
    public async Task<EbillingFeedResult> ReadAsync(CallerPrincipal principal, DateTimeOffset cutoff, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // A workforce systemuser reads an outcome through the internal review; a contact that could not be read is not a firm caller.
        if (ExternalInvoiceSubmissionAuthorizationFilter.IsSystemUser(principal) || principal.ContactId == Guid.Empty || principal.ContactUnreadable)
        {
            return new EbillingFeedResult([], false);
        }

        (IReadOnlyList<Guid> Firms, bool Unreadable) read;
        try
        {
            read = await _readFirms(principal, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[EXT-NOTIFY] The eBilling feed could not read the caller's firms | ExceptionType {ExceptionType}", ex.GetType().Name);
            return new EbillingFeedResult([], true);
        }

        if (read.Unreadable)
        {
            return new EbillingFeedResult([], true);
        }

        if (read.Firms.Count == 0)
        {
            return new EbillingFeedResult([], false);
        }

        IReadOnlyList<Entity> candidates;
        try
        {
            candidates = await _readCandidates(read.Firms, cutoff, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[EXT-NOTIFY] The eBilling feed candidates could not be read | ExceptionType {ExceptionType}", ex.GetType().Name);
            return new EbillingFeedResult([], true);
        }

        var truncated = candidates.Count > MaxCandidates;
        var items = new List<EbillingFeedItem>();
        foreach (var row in candidates.Take(MaxCandidates))
        {
            var occurredOn = row.GetAttributeValue<DateTime?>("modifiedon");
            if (occurredOn is null)
            {
                continue;
            }

            // Denied is dropped; an undecidable invoice (a read or the decision faulted) is dropped and the feed says truncated.
            InvoiceFeedDecision decision;
            try
            {
                decision = await _decide(principal, read.Firms, row.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("[EXT-NOTIFY] An eBilling feed invoice could not be decided | ExceptionType {ExceptionType}", ex.GetType().Name);
                decision = InvoiceFeedDecision.Unavailable;
            }

            if (decision == InvoiceFeedDecision.Unavailable)
            {
                truncated = true;
                continue;
            }

            if (decision != InvoiceFeedDecision.Visible)
            {
                continue;
            }

            if (row.GetAttributeValue<OptionSetValue>("statuscode")?.Value == InvoiceStatusCodes.Approved)
            {
                // An approval is news only when it carries an adjustment: the projection's one definition (the same the notice composer applies).
                FirmOutcome? outcome;
                try
                {
                    outcome = await _outcome(row.Id, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("[EXT-NOTIFY] An eBilling feed approval could not be read | ExceptionType {ExceptionType}", ex.GetType().Name);
                    outcome = null;
                }

                if (outcome is null || outcome.Degraded || outcome.Kind == FirmOutcomeKind.Unavailable)
                {
                    truncated = true;
                    continue;
                }

                if (outcome.Kind != FirmOutcomeKind.ApprovedWithAdjustments)
                {
                    continue;
                }
            }

            items.Add(new EbillingFeedItem(row.Id, new DateTimeOffset(DateTime.SpecifyKind(occurredOn.Value, DateTimeKind.Utc))));
        }

        return new EbillingFeedResult(items, truncated);
    }
}
