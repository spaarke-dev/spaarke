using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>What a firm outcome route names: an invoice or a submission.</summary>
public enum FirmOutcomeSubject
{
    /// <summary>GET /ebilling/invoices/{invoiceId}/outcome.</summary>
    Invoice,

    /// <summary>GET /ebilling/submissions/{submissionId}/outcome.</summary>
    Submission,
}

/// <summary>Attaches <see cref="FirmOutcomeAuthorizationFilter"/> to a firm outcome route.</summary>
public static class FirmOutcomeAuthorizationFilterExtensions
{
    /// <summary>
    /// Decides the caller, the firm, the core record and the visibility of the invoice or submission a firm outcome route names
    /// (ebilling-r1 task 130b, FR-38, NFR-10, D-70) before the handler. Chain it BEFORE <c>RequireEbillingEnabled</c>: the flag never
    /// replaces authorization (ADR-018).
    /// </summary>
    public static TBuilder AddFirmOutcomeAuthorizationFilter<TBuilder>(this TBuilder builder, FirmOutcomeSubject subject)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new FirmOutcomeAuthorizationFilter(
                subject,
                services.GetRequiredService<ExternalParticipationService>(),
                services.GetRequiredService<ExternalDataService>(),
                services.GetRequiredService<InvoiceSubmissionService>(),
                services.GetRequiredService<ILogger<FirmOutcomeAuthorizationFilter>>(),
                services.GetService<ExternalInvoiceHiding>(),
                services.GetService<ExternalInvoiceSubmissionHiding>());
            return await filter.InvokeAsync(context, next);
        });
}

/// <summary>What the filter decided, for the handler that runs after it: the one id the caller may be told about.</summary>
/// <param name="Subject">Whether <paramref name="SubjectId"/> is an invoice or a submission.</param>
/// <param name="SubjectId">The invoice or submission, as READ server-side (never a request field beyond the route's own id, which was checked).</param>
/// <param name="InvoiceDecided">
/// True when this filter decided, by the invoice rule, the invoice the outcome would show (the invoice route's own invoice, or a submission's
/// derived invoice that existed when the filter looked). False only for a submission whose invoice did not exist yet: the handler then shows
/// nothing read from that invoice (130b fix round 2, F1), even if it has been created since, because no invoice decision covers it.
/// </param>
internal sealed record FirmOutcomeScope(FirmOutcomeSubject Subject, Guid SubjectId, bool InvoiceDecided)
{
    /// <summary>HttpContext.Items key under which the decided scope is stored.</summary>
    internal static readonly object HttpContextItemsKey = new();
}

/// <summary>
/// ebilling-r1 task 130b — the route-level gate of the firm outcome routes (FR-38, NFR-10, D-70, ADR-008), on the
/// <c>/api/v1/external</c> collaboration group (whose caller-principal filter has already resolved the caller).
/// <see cref="Sprk.Bff.Api.Services.Finance.FirmFindingsProjection"/> has NO caller check; this filter is the check, and the handler runs only behind it.
/// </summary>
/// <remarks>
/// <para><b>The rule (closed set).</b> The caller sees an invoice's (or submission's) outcome only when ALL hold: it is not a workforce
/// systemuser; it has at least one active firm (<see cref="ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync"/>, the one
/// definition 044 and <c>/me</c> use); the invoice's (submission's) firm (<c>sprk_vendororg</c>) is one of them; the caller holds Read on the
/// core record (the principal's post-veto rights, <see cref="CallerPrincipal.RightsOn"/>); and the invoice (submission) is not hidden from it
/// by the (a') predicate (<see cref="ExternalInvoiceHiding"/> / <see cref="ExternalInvoiceSubmissionEndpoints.VisibleSubmissionsAsync"/>,
/// the same decisions every other external invoice read applies). A submission's invoice, once it exists, must ALSO pass the invoice rule. A
/// caller of several firms sees each of its firms' items; another firm's item is refused even on a shared matter.</para>
/// <para><b>One 403 for every refusal.</b> No firm (a decided empty set), another firm's item, no core access, a hidden item, a systemuser and an
/// id that does not exist all answer the SAME 403 <see cref="FirmOutcomeErrors.Forbidden"/> with the same body, so the status says nothing about
/// which. <b>Not-found is 403 too, not 404:</b> an id that does not exist has no firm, so the firm check can never pass for it, and a 404 for it
/// beside a 403 for another firm's item would let a caller learn which ids exist. A read that cannot decide (the caller's firms, the invoice or
/// submission, or the visibility decision, faulted or came back unreadable) is a retryable 503 <see cref="FirmOutcomeErrors.Unavailable"/>
/// (EB-D-169), never a 200 and never a denial that blames the caller. The firm read is decided before any item is read.</para>
/// <para><b>Order.</b> The group's workforce ceiling (id-independent), then principal, systemuser, firms, record read, firm, core record, visibility
/// (a submission: then its invoice's); the kill switch after this filter. App-only reads (ADR-028 A1/A3), no OBO; no principal denies.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: 044's <see cref="ExternalInvoiceSubmissionAuthorizationFilter"/> (its
/// <c>ReadOne</c> answers a uniform 404 and keys on a submission only). Extension: it cannot serve this route without changing what 044's
/// callers observe (404 versus the 403 the task requires) and it has no invoice arm; this filter reuses its firm read, its systemuser test, the
/// (a') decisions and the principal's rights unchanged and adds only the decision order. Cost of doing nothing: the projection has no caller
/// check, so a route without this gate would show another firm's reductions.</para>
/// </remarks>
internal sealed class FirmOutcomeAuthorizationFilter : IEndpointFilter
{
    /// <summary>The columns of an invoice that decide its firm and its core record.</summary>
    private static readonly string[] InvoiceScopeColumns =
        ["sprk_vendororg", "sprk_matter", "sprk_project", "sprk_regardingworkassignment"];

    private readonly FirmOutcomeSubject _subject;
    private readonly ExternalParticipationService _participations;
    private readonly ExternalDataService _data;
    private readonly InvoiceSubmissionService _submissions;
    private readonly ILogger<FirmOutcomeAuthorizationFilter> _logger;
    private readonly ExternalInvoiceHiding? _hideInvoices;
    private readonly ExternalInvoiceSubmissionHiding? _hideSubmissions;

    public FirmOutcomeAuthorizationFilter(
        FirmOutcomeSubject subject,
        ExternalParticipationService participations,
        ExternalDataService data,
        InvoiceSubmissionService submissions,
        ILogger<FirmOutcomeAuthorizationFilter> logger,
        ExternalInvoiceHiding? hideInvoices = null,
        ExternalInvoiceSubmissionHiding? hideSubmissions = null)
    {
        _subject = subject;
        _participations = participations;
        _data = data;
        _submissions = submissions;
        _logger = logger;
        _hideInvoices = hideInvoices;
        _hideSubmissions = hideSubmissions;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;

        if (http.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal principal)
        {
            _logger.LogWarning("[EBILLING-FIRM-OUTCOME] No caller principal on a firm outcome route; denying (fail closed).");
            return FirmOutcomeErrors.Forbidden();
        }

        // A workforce systemuser is not an external caller here: the firm membership a linked contact carries is a CONTACT's, and an internal
        // user reads the outcome through the internal review. Refused before anything is read (the shape of 044's submit refusal).
        if (ExternalInvoiceSubmissionAuthorizationFilter.IsSystemUser(principal))
        {
            _logger.LogWarning("[EBILLING-FIRM-OUTCOME] DENIED: a systemuser called an external firm outcome route.");
            return FirmOutcomeErrors.Forbidden();
        }

        var routeKey = _subject == FirmOutcomeSubject.Invoice ? "invoiceId" : "submissionId";
        if (!Guid.TryParse(http.Request.RouteValues[routeKey]?.ToString(), out var subjectId) || subjectId == Guid.Empty)
        {
            return FirmOutcomeErrors.Forbidden();
        }

        // Unknown is not "no firm": a firm read that faulted or is unreadable is a retryable 503 (nothing blames the caller); only a DECIDED
        // empty set is the uniform 403. Both are decided here, before any invoice or submission is read.
        var firms = await ReadFirmsAsync(principal, ct);
        if (firms is null)
        {
            return FirmOutcomeErrors.Unavailable();
        }

        if (firms.Count == 0)
        {
            return FirmOutcomeErrors.Forbidden();
        }

        var (denial, invoiceDecided) = _subject == FirmOutcomeSubject.Invoice
            ? await DecideInvoiceAsync(principal, firms, subjectId, ct)
            : await DecideSubmissionAsync(principal, firms, subjectId, ct);
        if (denial is not null)
        {
            return denial;
        }

        http.Items[FirmOutcomeScope.HttpContextItemsKey] = new FirmOutcomeScope(_subject, subjectId, invoiceDecided);
        return await next(context);
    }

    // ── The firm ──

    /// <summary>
    /// The caller's active firms; null when they could not be read. An unreadable firm set is a SYSTEM failure (logged at Error) and the
    /// caller gets the retryable 503: it is never "no firm", never a pass.
    /// </summary>
    private async Task<IReadOnlyList<Guid>?> ReadFirmsAsync(CallerPrincipal principal, CancellationToken ct)
    {
        (IReadOnlyList<Guid> Firms, bool Unreadable) read;
        try
        {
            read = await ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync(principal, _participations, _logger, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[EBILLING-FIRM-OUTCOME] SYSTEM FAILURE: the caller's firms could not be read; answering 503.");
            return null;
        }

        if (read.Unreadable || read.Firms is null)
        {
            _logger.LogError("[EBILLING-FIRM-OUTCOME] SYSTEM FAILURE: the caller's firms are unreadable; answering 503.");
            return null;
        }

        return read.Firms;
    }

    // ── Invoice ──

    /// <summary>
    /// The invoice rule. Returns a refusal, or no refusal with <c>InvoiceDecided</c> true when the invoice passed it. A submission's invoice that
    /// does not exist yet (<paramref name="missingPasses"/>) passes UNDECIDED (<c>InvoiceDecided</c> false), so nothing of it is shown.
    /// </summary>
    private async Task<(IResult? Denial, bool InvoiceDecided)> DecideInvoiceAsync(
        CallerPrincipal principal, IReadOnlyList<Guid> firms, Guid invoiceId, CancellationToken ct, bool missingPasses = false)
    {
        IReadOnlyDictionary<string, object?> invoice;
        try
        {
            invoice = await _data.GetInvoiceLookupsAsync(invoiceId, InvoiceScopeColumns, ct);
        }
        catch (RecordNotFoundException)
        {
            // No such invoice: the SAME 403 as another firm's (a 404 would be an existence oracle). A submission's not-yet-created invoice
            // passes, but undecided: the handler shows nothing read from it, even if it is created before the projection runs.
            return missingPasses ? (null, false) : (FirmOutcomeErrors.Forbidden(), false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[EBILLING-FIRM-OUTCOME] The invoice's firm and core record could not be read; answering 503.");
            return (FirmOutcomeErrors.Unavailable(), false);
        }

        // The firm: a missing or malformed value names no firm, so it matches none of the caller's.
        if (!invoice.TryGetValue("sprk_vendororg", out var firm) || firm is not Guid firmId || firmId == Guid.Empty || !firms.Contains(firmId))
        {
            _logger.LogInformation("[EBILLING-FIRM-OUTCOME] Denied: the invoice's firm is not one of the caller's firms.");
            return (FirmOutcomeErrors.Forbidden(), false);
        }

        // The core record: Read on the invoice's matter, project or work assignment (the union the invoices module and the invoice
        // documents already apply). A value that is not a GUID grants nothing.
        var readable = CoreRoots.Any(root =>
            invoice.TryGetValue(root.Column, out var value) && value is Guid id && id != Guid.Empty
            && principal.RightsOn(root.Type, id).HasFlag(AccessRights.Read));
        if (!readable)
        {
            _logger.LogInformation("[EBILLING-FIRM-OUTCOME] Denied: the caller cannot read the invoice's core record.");
            return (FirmOutcomeErrors.Forbidden(), false);
        }

        // THE (a') decision every external invoice read applies: a hidden invoice is another firm's, refused and never projected. No
        // predicate registered denies (fail closed); a decision that could not be made is a retryable 503 (EB-D-169).
        if (_hideInvoices is null)
        {
            _logger.LogError("[EBILLING-FIRM-OUTCOME] The invoice decision (ExternalInvoiceHiding) is not registered; denying (fail closed).");
            return (FirmOutcomeErrors.Forbidden(), false);
        }

        InvoiceVisibilityResult decision;
        try
        {
            decision = await _hideInvoices(principal, [invoiceId], ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[EBILLING-FIRM-OUTCOME] The invoice visibility decision faulted; answering 503.");
            return (FirmOutcomeErrors.Unavailable(), false);
        }

        if (decision.Faulted)
        {
            return (FirmOutcomeErrors.Unavailable(), false);
        }

        if (!decision.IsVisible(invoiceId))
        {
            _logger.LogInformation("[EBILLING-FIRM-OUTCOME] Denied: the invoice is hidden from the caller ({Reasons}).", decision.HiddenSummary());
            return (FirmOutcomeErrors.Forbidden(), false);
        }

        return (null, true);
    }

    /// <summary>The core record lookups of an invoice and the grant root each is checked against.</summary>
    private static readonly (string Column, ExternalGrantRootType Type)[] CoreRoots =
    [
        ("sprk_matter", ExternalGrantRootType.Matter),
        ("sprk_project", ExternalGrantRootType.Project),
        ("sprk_regardingworkassignment", ExternalGrantRootType.WorkAssignment),
    ];

    // ── Submission ──

    private async Task<(IResult? Denial, bool InvoiceDecided)> DecideSubmissionAsync(CallerPrincipal principal, IReadOnlyList<Guid> firms, Guid submissionId, CancellationToken ct)
    {
        InvoiceSubmissionView? view;
        try
        {
            view = await _submissions.GetAsync(submissionId, ct);
        }
        catch (SdapProblemException ex)
        {
            _logger.LogWarning("[EBILLING-FIRM-OUTCOME] The submission could not be read ({Code}); answering 503.", ex.Code);
            return (FirmOutcomeErrors.Unavailable(), false);
        }

        // Another firm's, a missing one and one with no firm or core record are all the same 403.
        if (view is not { FirmId: { } firmId, CoreRecord: { } core } || !firms.Contains(firmId))
        {
            _logger.LogInformation("[EBILLING-FIRM-OUTCOME] Denied: the submission is missing or its firm is not one of the caller's firms.");
            return (FirmOutcomeErrors.Forbidden(), false);
        }

        if (!principal.RightsOn(core.GrantRootType, core.RecordId).HasFlag(AccessRights.Read))
        {
            _logger.LogInformation("[EBILLING-FIRM-OUTCOME] Denied: the caller cannot read the submission's core record.");
            return (FirmOutcomeErrors.Forbidden(), false);
        }

        // THE (a') decision the submission list and the single read apply (one seam): with an invoice, the invoice predicate; without one,
        // the submission's own source. A withheld submission is refused and never projected; no seam withholds it too (fail closed).
        IReadOnlyList<InvoiceSubmissionView> visible;
        try
        {
            visible = await ExternalInvoiceSubmissionEndpoints.VisibleSubmissionsAsync(principal, [view], _hideSubmissions, _logger, ct);
        }
        catch (SdapProblemException)
        {
            return (FirmOutcomeErrors.Unavailable(), false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[EBILLING-FIRM-OUTCOME] The submission visibility decision faulted; answering 503.");
            return (FirmOutcomeErrors.Unavailable(), false);
        }

        if (visible.Count == 0)
        {
            _logger.LogInformation("[EBILLING-FIRM-OUTCOME] Denied: the submission is hidden from the caller.");
            return (FirmOutcomeErrors.Forbidden(), false);
        }

        // The invoice the projection would show for this submission (the derived id GetForSubmissionAsync reads: its billed total while
        // Received or Processing, its whole outcome once Accepted) is decided exactly as the invoice route decides it: its OWN firm among the
        // caller's, Read on its OWN core record, and the (a') predicate. The submission's firm and core record are the submission's own
        // columns and say nothing about the invoice's. A submission is shown only when it AND its invoice are the caller's to see. No invoice
        // yet passes UNDECIDED (InvoiceDecided false): the projection then reads nothing of the invoice, so one created after this check is
        // never shown without it (fix round 2, F1). A refusal is the same 403, a fault the same 503.
        return await DecideInvoiceAsync(principal, firms, InvoiceSubmissionPersister.InvoiceIdFor(submissionId), ct, missingPasses: true);
    }
}
