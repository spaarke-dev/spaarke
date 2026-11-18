using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>The answer to "may this contact be emailed about the subject?": a settled yes, a settled no, or "could not decide" (the job retries).</summary>
internal enum RecipientAccessDecision
{
    /// <summary>The route's row rule is Visible for the contact: tell it.</summary>
    Told,

    /// <summary>The row rule refused (or the contact has no firm or subject): drop that recipient.</summary>
    Denied,

    /// <summary>The answer could not be had (a fault, or an Unavailable decision): never a drop; the job is retried.</summary>
    Unavailable,
}

/// <summary>
/// May a firm contact be EMAILED about an invoice (or a rejected submission)? (ebilling-r1 task 132, owner ruling D-70 of 2026-11-08: an external
/// contact sees only its own firms' invoices on records it can access.) The answer is the outcome route's and the feed's own row rule, asked for the
/// contact: the principal the portal composes for a contact (<see cref="CiamContactPrincipalStrategy.ComposeAsync"/>, grants only, with Secure
/// suppression, No Access and Restricted applied) is put through <see cref="FirmOutcomeAuthorizationFilter.DecideSubjectForNoticeAsync"/>, which runs the
/// route's own decision for one id. Nothing of the rule is copied, so an email can never reach a contact the route would refuse.
/// </summary>
/// <remarks>
/// <para><b>Three answers, fail closed.</b> <see cref="RecipientAccessDecision.Told"/> only for a Visible decision. <see cref="RecipientAccessDecision.Denied"/>
/// is a settled no (the row rule refused, or the contact has no firm): the caller drops that recipient. <see cref="RecipientAccessDecision.Unavailable"/> is
/// "could not decide" (an Unavailable decision, or a composition or read that faulted): it is NOT a no, because dropping on a transient fault would lose the
/// email for good. The caller retries the job instead, before anything is marked or sent. Nothing but ids is logged (ADR-015).</para>
/// <para><b>Known limit.</b> Every contact is composed as a CIAM sign-in (grants only). A contact whose home sign-in confers more (standing-grant
/// membership) is judged on its grants alone, so such a contact can lose an email, never gain one.</para>
/// <para>§11: <i>Existing</i> — <see cref="FirmOutcomeAuthorizationFilter"/> and the portal's principal composition. <i>Extension</i> — the filter only
/// answers for a caller on a request; this asks it for a contact in a job, with one new entry point on the filter and no copy of the rule. <i>Cost of doing
/// nothing</i> — the firm email names an invoice outcome to a contact who cannot open that invoice in the portal.</para>
/// <para>Virtual members are the test seam (the pattern of <c>AssignTaskRecipientGate</c>).</para>
/// </remarks>
internal class EbillingRecipientAccessRule
{
    private readonly IAccessibleRecordSetService? _accessibleSet;
    private readonly ExternalParticipationService? _participations;
    private readonly ExternalDataService? _data;
    private readonly InvoiceSubmissionService? _submissions;
    private readonly ILogger<EbillingRecipientAccessRule> _logger;
    private readonly ILogger<FirmOutcomeAuthorizationFilter>? _filterLogger;
    private readonly ExternalInvoiceHiding? _hideInvoices;
    private readonly ExternalInvoiceSubmissionHiding? _hideSubmissions;

    public EbillingRecipientAccessRule(
        IAccessibleRecordSetService accessibleSet,
        ExternalParticipationService participations,
        ExternalDataService data,
        InvoiceSubmissionService submissions,
        ILogger<EbillingRecipientAccessRule> logger,
        ILogger<FirmOutcomeAuthorizationFilter> filterLogger,
        ExternalInvoiceHiding? hideInvoices = null,
        ExternalInvoiceSubmissionHiding? hideSubmissions = null)
    {
        _accessibleSet = accessibleSet ?? throw new ArgumentNullException(nameof(accessibleSet));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _filterLogger = filterLogger ?? throw new ArgumentNullException(nameof(filterLogger));
        _hideInvoices = hideInvoices;
        _hideSubmissions = hideSubmissions;
    }

    /// <summary>The test seam's constructor: every Dataverse-facing step is overridden.</summary>
    protected EbillingRecipientAccessRule(ILogger<EbillingRecipientAccessRule> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Whether the route's row rule lets <paramref name="contactId"/> (whose active firms are <paramref name="firms"/>) be told about the notice's
    /// subject: the invoice (<paramref name="subject"/> = Invoice) or the rejected submission. Told on Visible; Denied on a settled no; Unavailable when
    /// the answer could not be had (a fault, or an Unavailable decision).
    /// </summary>
    public virtual async Task<RecipientAccessDecision> MayBeToldAsync(
        Guid contactId, IReadOnlyList<Guid> firms, EbillingNotificationSubject subject, Guid subjectId, CancellationToken ct)
    {
        if (contactId == Guid.Empty || subjectId == Guid.Empty || firms.Count == 0)
        {
            return RecipientAccessDecision.Denied;
        }

        try
        {
            var principal = await ComposePrincipalAsync(contactId, ct).ConfigureAwait(false);
            var decision = await DecideAsync(principal, firms, subject, subjectId, ct).ConfigureAwait(false);
            if (decision != InvoiceFeedDecision.Visible)
            {
                _logger.LogInformation(
                    "eBilling notice: the outcome route's row rule is not Visible for a recipient | Contact {ContactId}, Subject {SubjectId}, Decision {Decision}",
                    contactId, subjectId, decision);
            }

            return decision switch
            {
                InvoiceFeedDecision.Visible => RecipientAccessDecision.Told,
                InvoiceFeedDecision.Unavailable => RecipientAccessDecision.Unavailable,
                _ => RecipientAccessDecision.Denied,
            };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "eBilling notice: the outcome route's row rule could not be decided for a recipient; the job is retried | Contact {ContactId}, Subject {SubjectId}, ExceptionType {ExceptionType}",
                contactId, subjectId, ex.GetType().Name);
            return RecipientAccessDecision.Unavailable;
        }
    }

    /// <summary>The principal the portal composes for the contact (grants only; Secure suppression, No Access and Restricted applied). A fault throws.</summary>
    internal virtual async Task<CallerPrincipal> ComposePrincipalAsync(Guid contactId, CancellationToken ct) =>
        (await CiamContactPrincipalStrategy.ComposeAsync(_accessibleSet!, contactId, email: null, oid: null, ct).ConfigureAwait(false)).Principal;

    /// <summary>The route's own decision for the subject, through the filter (one entry point; the rule is not copied).</summary>
    internal virtual Task<InvoiceFeedDecision> DecideAsync(
        CallerPrincipal principal, IReadOnlyList<Guid> firms, EbillingNotificationSubject subject, Guid subjectId, CancellationToken ct) =>
        new FirmOutcomeAuthorizationFilter(
                subject == EbillingNotificationSubject.Invoice ? FirmOutcomeSubject.Invoice : FirmOutcomeSubject.Submission,
                _participations!, _data!, _submissions!, _filterLogger!, _hideInvoices, _hideSubmissions)
            .DecideSubjectForNoticeAsync(principal, firms, subjectId, ct);
}
