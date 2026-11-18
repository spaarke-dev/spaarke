using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>One eBilling item of the external notification feed: an invoice whose outcome the firm may open.</summary>
internal sealed record EbillingFeedItem(Guid InvoiceId, DateTimeOffset OccurredOn);

/// <summary>The eBilling items of a caller's feed. <paramref name="Truncated"/> is true when a cut or a read that could not be decided left the list possibly incomplete (never silent).</summary>
internal sealed record EbillingFeedResult(IReadOnlyList<EbillingFeedItem> Items, bool Truncated);

/// <summary>
/// The eBilling source of the external notification feed (ebilling-r1 task 132, FR-39, FR-38, NFR-10, EB-D-258): the invoices whose outcome the firm
/// should look at, DERIVED from the invoice's own status (no new table, no per-user row): a Rejected invoice, a Returned to Firm invoice and an Approved
/// invoice that carries an adjustment (the projection's one definition, <see cref="FirmOutcomeKind.ApprovedWithAdjustments"/>), each changed inside the
/// feed window. A rejected SUBMISSION that created no invoice has no route to open, so it is not an item (a known limit).
/// </summary>
/// <remarks>
/// <para><b>Audience (NFR-10, ADR-028).</b> An item reaches only the invoice's SUBMITTER (<c>sprk_submittedby</c>) and the BILLING CONTACT of the
/// invoice's firm (<c>sprk_organization.sprk_billingcontact</c>), the two people the firm email goes to. The candidate query is narrowed to exactly that
/// (<see cref="CandidateQuery"/>), and every row it returns is checked again in memory (<see cref="InAudience"/>), so a query that came back too wide
/// still shows nothing to anyone else. Another contact of the same firm, with access to the same matter, gets no item.</para>
/// <para><b>Fail closed, in one batch.</b> The audience candidates are read app-only, with the columns the access rule needs, then decided TOGETHER by
/// <see cref="FirmOutcomeAuthorizationFilter.DecideInvoicesForFeedAsync"/>: the SAME invoice rule the outcome route applies (firm, Read on the core
/// record, the secure-roots rule and the (a') hiding decision), whose secure-root reads and hiding decision are one call each for the whole batch, not
/// four to eight per invoice. Only a Visible invoice is an item; Denied is dropped; a decision that faulted drops the items and flags the feed truncated.
/// A caller with no firm, an unreadable contact, or a workforce systemuser (who reads an outcome through the internal review) gets nothing. The
/// kill switch (<see cref="EbillingOptions.Enabled"/>) is read per call: off, nothing is read and nothing is returned.</para>
/// <para><b>Bounded, cut AFTER the decision.</b> At most <see cref="MaxCandidatesRead"/> invoices are read (newest first) and at most
/// <see cref="MaxItems"/> items are returned. The cap counts only items the caller may see, so <c>Truncated</c> says "there is more you could open" and
/// is never raised by an invoice that was hidden from the caller. It is also raised when the read bound itself was reached (older invoices were not
/// looked at). The item's time is the invoice's last change, which can also be a change that is not the outcome (a known limit).</para>
/// <para><b>Seam.</b> The reads are delegates (the DI constructor binds them to the real services; the internal one is the test seam, the pattern of
/// <see cref="FirmNoticeComposer"/>), so the per-item rule is driven with doubles while the rule itself is the outcome route's own.</para>
/// <para>§11: <i>Existing</i> — the feed's grant and message sources and 130b's outcome rule. <i>Extension</i> — the feed has no invoice source, and the
/// outcome rule lives in a route filter; this adds a source that asks that rule, with no new route and no change to the item's shape. <i>Cost of doing nothing</i> —
/// a firm learns of a rejected, returned or adjusted invoice only by opening the SPA's invoice list (FR-39's in-app half).</para>
/// </remarks>
internal sealed class EbillingFeedSource
{
    /// <summary>The most items returned for one feed (counted after the access decision).</summary>
    public const int MaxItems = 25;

    /// <summary>The most audience candidates read for one feed (newest first); the access decision runs over all of them.</summary>
    public const int MaxCandidatesRead = 100;

    /// <summary>
    /// The most projection reads (one per VISIBLE Approved candidate, to learn whether the approval carries an adjustment) made for one feed. Reaching it
    /// stops the reading and flags the feed truncated, so a poll costs at most this many projection reads however many Approved invoices are visible.
    /// </summary>
    public const int MaxProjectionReads = MaxItems;

    /// <summary>The fixed sender label on an eBilling item: the SPA's feed shows a sender name for a "message" item (the existing item shape; no new kind).</summary>
    public const string SenderLabel = "Billing";

    /// <summary>
    /// The <c>sprk_invoice</c> columns whose values reach a firm through this source: the item's id (<c>sprk_invoiceid</c>, always returned), the status that
    /// decides whether it is news, the change time shown as the item's time, and the invoice's name (the item's title, read by the feed's title step,
    /// <see cref="ExternalDataService.GetRecordNamesAsync"/>). EB-D-207: every column here must be in the external invoice module's ReadableColumns, except
    /// the timestamp-only <c>modifiedon</c>, which is named in the pinning test, so a column added here fails that test.
    /// </summary>
    internal static readonly string[] RenderedInvoiceColumns = ["sprk_invoiceid", "statuscode", "modifiedon", ExternalDataService.InvoiceNameColumn];

    /// <summary>The columns that decide who an invoice is for. Read to narrow and re-check the audience; never rendered.</summary>
    internal static readonly string[] AudienceColumns = ["sprk_submittedby", "sprk_vendororg"];

    private const string SubmittedBy = "sprk_submittedby";
    private const string VendorOrg = "sprk_vendororg";

    private readonly Func<bool> _enabled;
    private readonly Func<CallerPrincipal, CancellationToken, Task<(IReadOnlyList<Guid> Firms, bool Unreadable)>> _readFirms;
    private readonly Func<Guid, IReadOnlyList<Guid>, CancellationToken, Task<IReadOnlyList<Guid>>> _readBillingFirms;
    private readonly Func<Guid, IReadOnlyList<Guid>, IReadOnlyList<Guid>, DateTimeOffset, CancellationToken, Task<IReadOnlyList<Entity>>> _readCandidates;
    private readonly Func<CallerPrincipal, IReadOnlyList<Guid>, IReadOnlyList<Entity>, CancellationToken, Task<IReadOnlyDictionary<Guid, InvoiceFeedDecision>>> _decide;
    private readonly Func<Guid, CancellationToken, Task<FirmOutcome?>> _outcome;
    private readonly ILogger<EbillingFeedSource> _logger;

    /// <summary>The DI constructor: the real firm read, the app-only candidate read, the outcome route's own invoice rule and the projection.</summary>
    public EbillingFeedSource(
        IGenericEntityService entities,
        ExternalParticipationService participations,
        ExternalDataService data,
        InvoiceSubmissionService submissions,
        FirmFindingsProjection projection,
        IOptionsMonitor<EbillingOptions> ebilling,
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
        ArgumentNullException.ThrowIfNull(ebilling);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // The kill switch is read per call (ADR-018): the feed never shows eBilling items while eBilling is off.
        _enabled = () => ebilling.CurrentValue.Enabled;
        _readFirms = (principal, ct) => ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync(principal, participations, logger, ct);
        _readBillingFirms = async (contactId, firms, ct) =>
        {
            // The caller's firms for which the caller is the billing contact (sprk_organization.sprk_billingcontact, the one the firm email uses).
            var query = new QueryExpression(EbillingNotificationRecipients.OrganizationEntity)
            {
                ColumnSet = new ColumnSet(EbillingNotificationRecipients.OrganizationEntity + "id"),
                Criteria = new FilterExpression(LogicalOperator.And),
            };
            query.Criteria.AddCondition(EbillingNotificationRecipients.OrganizationEntity + "id", ConditionOperator.In, firms.Cast<object>().ToArray());
            query.Criteria.AddCondition(EbillingNotificationRecipients.OrganizationBillingContact, ConditionOperator.Equal, contactId);
            return (IReadOnlyList<Guid>)(await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.Select(e => e.Id).ToList();
        };
        _readCandidates = async (contactId, firms, billingFirms, cutoff, ct) =>
            (IReadOnlyList<Entity>)(await entities.RetrieveMultipleAsync(CandidateQuery(contactId, firms, billingFirms, cutoff), ct).ConfigureAwait(false)).Entities.ToList();

        // The outcome route's own invoice rule, over the whole batch (fail closed). The filter is cheap to build and holds nothing between calls.
        _decide = (principal, firms, rows, ct) => new FirmOutcomeAuthorizationFilter(
                FirmOutcomeSubject.Invoice, participations, data, submissions, filterLogger, hideInvoices, hideSubmissions)
            .DecideInvoicesForFeedAsync(principal, firms, rows.Select(r => (r.Id, ScopeRowOf(r))).ToList(), ct);
        _outcome = projection.GetForInvoiceAsync;
    }

    /// <summary>The test seam: the reads as delegates.</summary>
    internal EbillingFeedSource(
        Func<bool> enabled,
        Func<CallerPrincipal, CancellationToken, Task<(IReadOnlyList<Guid> Firms, bool Unreadable)>> readFirms,
        Func<Guid, IReadOnlyList<Guid>, CancellationToken, Task<IReadOnlyList<Guid>>> readBillingFirms,
        Func<Guid, IReadOnlyList<Guid>, IReadOnlyList<Guid>, DateTimeOffset, CancellationToken, Task<IReadOnlyList<Entity>>> readCandidates,
        Func<CallerPrincipal, IReadOnlyList<Guid>, IReadOnlyList<Entity>, CancellationToken, Task<IReadOnlyDictionary<Guid, InvoiceFeedDecision>>> decide,
        Func<Guid, CancellationToken, Task<FirmOutcome?>> outcome,
        ILogger<EbillingFeedSource> logger)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _readFirms = readFirms ?? throw new ArgumentNullException(nameof(readFirms));
        _readBillingFirms = readBillingFirms ?? throw new ArgumentNullException(nameof(readBillingFirms));
        _readCandidates = readCandidates ?? throw new ArgumentNullException(nameof(readCandidates));
        _decide = decide ?? throw new ArgumentNullException(nameof(decide));
        _outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The candidate read: invoices of the caller's firms, Rejected, Returned or Approved, changed since <paramref name="cutoff"/>, that are the caller's
    /// OWN (submitted by the caller) or belong to a firm whose billing contact is the caller, newest change first, one more than
    /// <see cref="MaxCandidatesRead"/> (to know the read was cut). It carries the columns of the access rule (<see cref="FirmOutcomeAuthorizationFilter.InvoiceScopeColumnNames"/>)
    /// so the decision needs no read per invoice.
    /// </summary>
    internal static QueryExpression CandidateQuery(Guid contactId, IReadOnlyList<Guid> firms, IReadOnlyList<Guid> billingFirms, DateTimeOffset cutoff)
    {
        var query = new QueryExpression("sprk_invoice")
        {
            ColumnSet = new ColumnSet(CandidateColumns()),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = MaxCandidatesRead + 1,
        };
        query.Criteria.AddCondition(VendorOrg, ConditionOperator.In, firms.Cast<object>().ToArray());
        query.Criteria.AddCondition(
            "statuscode", ConditionOperator.In,
            InvoiceStatusCodes.Rejected, InvoiceStatusCodes.ReturnedToFirm, InvoiceStatusCodes.Approved);
        query.Criteria.AddCondition("modifiedon", ConditionOperator.GreaterEqual, cutoff.UtcDateTime);

        // The audience: submitted by the caller, OR a firm whose billing contact is the caller. Nobody else's invoice is a candidate.
        var audience = new FilterExpression(LogicalOperator.Or);
        audience.AddCondition(SubmittedBy, ConditionOperator.Equal, contactId);
        if (billingFirms.Count > 0)
        {
            audience.AddCondition(VendorOrg, ConditionOperator.In, billingFirms.Cast<object>().ToArray());
        }

        query.Criteria.AddFilter(audience);
        query.AddOrder("modifiedon", OrderType.Descending);
        return query;
    }

    /// <summary>The columns <see cref="CandidateQuery"/> reads: what decides the item (id, status, time), the audience, and the access rule's scope columns. Never the name.</summary>
    internal static string[] CandidateColumns() =>
        [.. RenderedInvoiceColumns.Where(c => !string.Equals(c, ExternalDataService.InvoiceNameColumn, StringComparison.OrdinalIgnoreCase))
            .Concat(AudienceColumns).Concat(FirmOutcomeAuthorizationFilter.InvoiceScopeColumnNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>True when the invoice is the caller's own (submitted by the caller) or its firm is one whose billing contact is the caller.</summary>
    internal static bool InAudience(Entity invoice, Guid contactId, IReadOnlyCollection<Guid> billingFirms) =>
        contactId != Guid.Empty
        && (invoice.GetAttributeValue<EntityReference>(SubmittedBy)?.Id == contactId
            || (invoice.GetAttributeValue<EntityReference>(VendorOrg)?.Id is { } firm && billingFirms.Contains(firm)));

    /// <summary>
    /// A candidate's scope columns in the shape the access rule reads an invoice's lookups (<c>ExternalDataService.GetInvoiceLookupsAsync</c>): a set
    /// lookup is its id, an unset one is absent, and a value that is not an id is passed through as it came (the rule reads it as malformed and fails closed).
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ScopeRowOf(Entity invoice)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in FirmOutcomeAuthorizationFilter.InvoiceScopeColumnNames)
        {
            if (!invoice.Attributes.TryGetValue(column, out var value) || value is null)
            {
                continue;
            }

            row[column] = value switch
            {
                EntityReference reference => reference.Id,
                Guid id => id,
                _ => value.ToString(),
            };
        }

        return row;
    }

    /// <summary>The caller's eBilling feed items changed since <paramref name="cutoff"/>.</summary>
    public async Task<EbillingFeedResult> ReadAsync(CallerPrincipal principal, DateTimeOffset cutoff, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // The kill switch (ADR-018): eBilling off means the feed has no eBilling item, and nothing is read.
        if (!_enabled())
        {
            return new EbillingFeedResult([], false);
        }

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

        var truncated = false;

        // The firms whose billing contact is the caller. A fault narrows the audience to the caller's own submissions and says so.
        IReadOnlyList<Guid> billingFirms;
        try
        {
            billingFirms = await _readBillingFirms(principal.ContactId, read.Firms, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[EXT-NOTIFY] The eBilling feed could not read the caller's billing-contact firms | ExceptionType {ExceptionType}", ex.GetType().Name);
            billingFirms = [];
            truncated = true;
        }

        IReadOnlyList<Entity> readRows;
        try
        {
            readRows = await _readCandidates(principal.ContactId, read.Firms, billingFirms, cutoff, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[EXT-NOTIFY] The eBilling feed candidates could not be read | ExceptionType {ExceptionType}", ex.GetType().Name);
            return new EbillingFeedResult([], true);
        }

        // The read bound was reached: older invoices were not looked at, so the list may be incomplete (said, never silent).
        truncated |= readRows.Count > MaxCandidatesRead;

        // Every row is the caller's own or its billing firm's; one that is neither (a query that came back too wide) is dropped here, unasked.
        var candidates = readRows
            .Take(MaxCandidatesRead)
            .Where(row => row.GetAttributeValue<DateTime?>("modifiedon") is not null && InAudience(row, principal.ContactId, billingFirms))
            .ToList();
        if (candidates.Count == 0)
        {
            return new EbillingFeedResult([], truncated);
        }

        // ONE decision for the whole batch. A fault is "could not decide": nothing is shown and the feed says truncated.
        IReadOnlyDictionary<Guid, InvoiceFeedDecision> decisions;
        try
        {
            decisions = await _decide(principal, read.Firms, candidates, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[EXT-NOTIFY] The eBilling feed invoices could not be decided | ExceptionType {ExceptionType}", ex.GetType().Name);
            return new EbillingFeedResult([], true);
        }

        var items = new List<EbillingFeedItem>();
        var projectionReads = 0;
        foreach (var row in candidates)
        {
            // Denied is dropped; an invoice with no answer, or an Unavailable one, is dropped and the feed says truncated.
            var decision = decisions.TryGetValue(row.Id, out var answered) ? answered : InvoiceFeedDecision.Unavailable;
            if (decision == InvoiceFeedDecision.Unavailable)
            {
                truncated = true;
                continue;
            }

            if (decision != InvoiceFeedDecision.Visible)
            {
                continue;
            }

            // An Approved invoice needs one projection read to know whether it is news. The reads are bounded per poll: at the bound the reading stops
            // and the feed says truncated (never silent), so the cost of a poll does not grow with the number of visible Approved invoices.
            if (row.GetAttributeValue<OptionSetValue>("statuscode")?.Value == InvoiceStatusCodes.Approved && ++projectionReads > MaxProjectionReads)
            {
                truncated = true;
                break;
            }

            var news = await IsNewsAsync(row, ct).ConfigureAwait(false);
            if (news is null)
            {
                truncated = true;
                continue;
            }

            if (news == false)
            {
                continue;
            }

            if (items.Count == MaxItems)
            {
                // A further item the caller may open exists: the cut is real (and only counts what the caller can see).
                truncated = true;
                break;
            }

            var occurredOn = row.GetAttributeValue<DateTime>("modifiedon");
            items.Add(new EbillingFeedItem(row.Id, new DateTimeOffset(DateTime.SpecifyKind(occurredOn, DateTimeKind.Utc))));
        }

        return new EbillingFeedResult(items, truncated);
    }

    /// <summary>True when a visible invoice is news, false when it is not, null when that could not be decided.</summary>
    private async Task<bool?> IsNewsAsync(Entity row, CancellationToken ct)
    {
        if (row.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.Approved)
        {
            return true;
        }

        // An approval is news only when it carries an adjustment: the projection's one definition (the same the notice composer applies).
        FirmOutcome? outcome;
        try
        {
            outcome = await _outcome(row.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("[EXT-NOTIFY] An eBilling feed approval could not be read | ExceptionType {ExceptionType}", ex.GetType().Name);
            return null;
        }

        if (outcome is null || outcome.Degraded || outcome.Kind == FirmOutcomeKind.Unavailable)
        {
            return null;
        }

        return outcome.Kind == FirmOutcomeKind.ApprovedWithAdjustments;
    }
}
