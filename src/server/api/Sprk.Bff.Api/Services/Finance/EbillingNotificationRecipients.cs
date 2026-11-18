using System.Net.Mail;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// What the notification job needs to know about its subject, read once (ebilling-r1 task 132). Ids and a status only: no firm text.
/// </summary>
/// <param name="SubmissionId">The submission: the subject itself, or the one that created the invoice; null when an invoice has none on record.</param>
/// <param name="InvoiceId">The invoice: the subject itself, or the one a submission created; null when none exists.</param>
/// <param name="FirmId">The invoice's or the submission's firm (<c>sprk_vendororg</c>); null when none is set.</param>
/// <param name="SubmitterContactId">The contact who submitted: the submission's <c>sprk_submitter</c>, or the invoice's <c>sprk_submittedby</c>.</param>
/// <param name="SubmissionStatus"><see cref="InvoiceSubmissionStatus"/> of <paramref name="SubmissionId"/>, when read.</param>
/// <param name="InvoiceStatus"><see cref="InvoiceStatusCodes"/> of <paramref name="InvoiceId"/>, when read (an invoice subject only).</param>
/// <param name="TrackingNumber">The invoice's tracking number (an invoice subject only), raw: clean it before it reaches text.</param>
/// <param name="CoreEntity">The invoice's one core record's table (an invoice subject only); null when it has none or several (fail closed).</param>
/// <param name="CoreId">The invoice's one core record's id.</param>
public sealed record EbillingNotificationFacts(
    Guid? SubmissionId,
    Guid? InvoiceId,
    Guid? FirmId,
    Guid? SubmitterContactId,
    int? SubmissionStatus,
    int? InvoiceStatus,
    string? TrackingNumber,
    string? CoreEntity,
    Guid? CoreId);

/// <summary>
/// The addresses a firm notice goes to. <paramref name="BillingContactMissing"/>: the firm has no billing contact with a usable address, so the submitter alone is told.
/// <paramref name="BillingContactDenied"/>: the firm has a billing contact but the outcome route's row rule refused it, so it is not told (a different cause; never also "missing").
/// </summary>
public sealed record FirmRecipients(IReadOnlyList<string> Addresses, bool BillingContactMissing, bool BillingContactDenied = false);

/// <summary>
/// Reads the facts and resolves the recipients of an eBilling notification (task 132; D-45, FR-39): a firm notice goes to the SUBMITTER and the
/// firm's BILLING CONTACT (<c>sprk_organization.sprk_billingcontact</c>, task 024). Reads are app-only through <see cref="IGenericEntityService"/>
/// (a job has no caller); the firm boundary is the stored link itself: the recipients are the contacts the submission and the firm record name,
/// never an address the request carried.
/// </summary>
/// <remarks>
/// Only ACTIVE contacts with a well-formed single address count, and only those who hold an ACTIVE membership in the notice's own firm RIGHT NOW
/// (<see cref="ExternalInvoiceSubmissionAuthorizationFilter.ReadContactFirmsAsync"/>, the one definition of "a contact's firms" the SPA and the routes use): a
/// submitter who has since left the firm, and a billing contact who belongs to another firm, are not told. A contact whose <c>statecode</c> was not read is
/// not active. The list is de-duplicated by address (case-insensitive), so a submitter who is also the billing contact is written to once. A missing
/// billing contact is not an error: the submitter alone is told, and the caller logs the ids. Logs carry ids and codes only (ADR-015): never an address.
/// <para><b>Row rule (D-70).</b> A recipient is also dropped unless the outcome route's own row rule is Visible for that contact on the notice's subject
/// (<see cref="EbillingRecipientAccessRule"/>): a contact without Read on the invoice's core record or its secure roots, or for whom the invoice is hidden,
/// is not told (only a settled Denied drops a recipient). A decision that cannot be made THROWS, so the job retries before anything is marked or sent: a
/// transient fault never loses the email. The email follows the same rule as the route and the feed.</para>
/// <para><b>Own submission.</b> The invoice's submission is always its OWN (<see cref="InvoiceBlockedHold.ReadOwnSubmissionAsync"/>); never the oldest one linked.</para>
/// </remarks>
internal sealed class EbillingNotificationRecipients
{
    internal const string OrganizationEntity = "sprk_organization";
    internal const string OrganizationBillingContact = "sprk_billingcontact";
    internal const string OrganizationName = "sprk_organizationname";
    internal const string ContactEntity = "contact";
    internal const string ContactEmail = "emailaddress1";
    private const int ContactStateActive = 0;

    private readonly IGenericEntityService _entities;
    private readonly ExternalParticipationService _participations;
    private readonly EbillingRecipientAccessRule _access;
    private readonly ILogger<EbillingNotificationRecipients> _logger;

    public EbillingNotificationRecipients(
        IGenericEntityService entities, ExternalParticipationService participations, EbillingRecipientAccessRule access,
        ILogger<EbillingNotificationRecipients> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The facts of the job's subject, or null when the record does not exist. A read fault throws (the job retries).</summary>
    public async Task<EbillingNotificationFacts?> ReadFactsAsync(EbillingNotificationSubject subject, Guid subjectId, CancellationToken ct)
    {
        if (subject == EbillingNotificationSubject.Submission)
        {
            var submission = await FirstAsync(
                InvoiceReviewReadService.SubmissionEntity, InvoiceReviewReadService.SubmissionEntity + "id", subjectId,
                [InvoiceSubmissionPersister.SubmissionSubmitter, InvoiceSubmissionPersister.InvoiceVendorOrg,
                    InvoiceSubmissionPersister.SubmissionInvoice, InvoiceSubmissionPersister.SubmissionStatus], ct).ConfigureAwait(false);
            if (submission is null)
            {
                return null;
            }

            return new EbillingNotificationFacts(
                subjectId,
                submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id,
                submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id,
                submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionSubmitter)?.Id,
                submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value,
                null, null, null, null);
        }

        var invoice = await FirstAsync(
            InvoiceReviewReadService.InvoiceEntity, InvoiceReviewReadService.InvoiceEntity + "id", subjectId,
            ["statuscode", InvoiceSubmissionPersister.InvoiceVendorOrg, InvoiceSubmissionPersister.InvoiceSubmittedBy,
                InvoiceReviewService.InvTrackingNumber, .. EligibleApproverResolver.CoreLookupColumns(InvoiceReviewReadService.InvoiceEntity)], ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return null;
        }

        // The invoice's OWN submission, by the one own-submission rule (InvoiceBlockedHold.ReadOwnSubmissionAsync, task 165a): the submitter when the
        // invoice names none, and the one place the sent text is stored. Another submission linked to the same invoice is never read here. When the own
        // submission cannot be determined there is no submission row to take a submitter from and no store target (fail closed).
        var origin = await InvoiceBlockedHold.ReadOwnSubmissionAsync(
            _entities, subjectId, [InvoiceSubmissionPersister.SubmissionSubmitter, InvoiceSubmissionPersister.SubmissionStatus], ct).ConfigureAwait(false);

        var core = EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice);
        return new EbillingNotificationFacts(
            origin?.Id,
            subjectId,
            invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id,
            invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceSubmittedBy)?.Id
                ?? origin?.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionSubmitter)?.Id,
            origin?.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value,
            invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value,
            invoice.GetAttributeValue<string>(InvoiceReviewService.InvTrackingNumber),
            core?.Entity,
            core?.Id);
    }

    /// <summary>The firm's display name, or null when there is none or it cannot be read (the caller falls back to wording without it).</summary>
    public async Task<string?> ReadFirmNameAsync(Guid? firmId, CancellationToken ct)
    {
        if (firmId is not { } firm || firm == Guid.Empty)
        {
            return null;
        }

        var row = await FirstAsync(OrganizationEntity, OrganizationEntity + "id", firm, [OrganizationName], ct).ConfigureAwait(false);
        return row?.GetAttributeValue<string>(OrganizationName);
    }

    /// <summary>The submitter's and the billing contact's usable addresses, de-duplicated. A read fault throws (the job retries).</summary>
    public async Task<FirmRecipients> ResolveFirmRecipientsAsync(EbillingNotificationFacts facts, EbillingNotificationSubject subject, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(facts);

        Guid? billing = null;
        if (facts.FirmId is { } firm && firm != Guid.Empty)
        {
            var org = await FirstAsync(OrganizationEntity, OrganizationEntity + "id", firm, [OrganizationBillingContact], ct).ConfigureAwait(false);
            billing = org?.GetAttributeValue<EntityReference>(OrganizationBillingContact)?.Id;
        }

        var contactIds = new List<Guid>();
        if (facts.SubmitterContactId is { } submitter && submitter != Guid.Empty)
        {
            contactIds.Add(submitter);
        }

        if (billing is { } billingId && billingId != Guid.Empty && !contactIds.Contains(billingId))
        {
            contactIds.Add(billingId);
        }

        // The firm is the notice's own: a contact who is not an ACTIVE member of it now (left the firm, belongs to another firm, or whose membership
        // is unreadable: the job retries) is not told. No firm on the record means nobody can be shown to belong to it.
        // Then the route's own row rule for that contact on the notice's subject (D-70). No subject on record means nothing to decide: nobody is told.
        var subjectId = subject == EbillingNotificationSubject.Invoice ? facts.InvoiceId : facts.SubmissionId;
        var members = new List<Guid>();
        var billingDenied = false;
        foreach (var id in contactIds)
        {
            var firms = await ActiveFirmsOfMemberAsync(id, facts.FirmId, ct).ConfigureAwait(false);
            if (firms is null)
            {
                _logger.LogInformation(
                    "eBilling notification recipient is not an active member of the firm and is not told | Contact {ContactId}, Firm {FirmId}", id, facts.FirmId);
            }
            else
            {
                var access = subjectId is { } subjectRecord && subjectRecord != Guid.Empty
                    ? await _access.MayBeToldAsync(id, firms, subject, subjectRecord, ct).ConfigureAwait(false)
                    : RecipientAccessDecision.Denied;
                switch (access)
                {
                    case RecipientAccessDecision.Told:
                        members.Add(id);
                        break;

                    case RecipientAccessDecision.Unavailable:
                        // Could not decide: not a no. Nothing is marked or sent yet, so the job retries (a dropped recipient here would be lost for good).
                        throw new InvalidOperationException("The row rule for a recipient could not be decided.");

                    default:
                        billingDenied |= billing == id;
                        _logger.LogInformation(
                            "eBilling notification recipient may not open the subject under the outcome route's row rule and is not told | Contact {ContactId}, Subject {SubjectId}",
                            id, subjectId);
                        break;
                }
            }
        }

        contactIds = members;
        if (contactIds.Count == 0)
        {
            return new FirmRecipients([], BillingContactMissing: !billingDenied, BillingContactDenied: billingDenied);
        }

        var query = new QueryExpression(ContactEntity)
        {
            ColumnSet = new ColumnSet(ContactEmail, "statecode"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(ContactEntity + "id", ConditionOperator.In, contactIds.Cast<object>().ToArray());
        var contacts = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;

        var usable = new Dictionary<Guid, string>();
        foreach (var contact in contacts)
        {
            // A statecode that was not read is NOT active (fail closed).
            var state = contact.GetAttributeValue<OptionSetValue>("statecode")?.Value;
            if (state == ContactStateActive && WellFormed(contact.GetAttributeValue<string>(ContactEmail)) is { } address)
            {
                usable[contact.Id] = address;
            }
        }

        var addresses = new List<string>();
        foreach (var id in contactIds)
        {
            if (usable.TryGetValue(id, out var address) && !addresses.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                addresses.Add(address);
            }
        }

        var billingMissing = !billingDenied && (billing is not { } b || !usable.ContainsKey(b));
        _logger.LogInformation(
            "eBilling notification recipients resolved | Invoice {InvoiceId}, Submission {SubmissionId}, Count {Count}, BillingContactMissing {BillingContactMissing}, BillingContactDenied {BillingContactDenied}",
            facts.InvoiceId, facts.SubmissionId, addresses.Count, billingMissing, billingDenied);
        return new FirmRecipients(addresses, billingMissing, billingDenied);
    }

    /// <summary>
    /// The contact's active firms when <paramref name="contactId"/> is an Active contact holding an active (conferring) membership in <paramref name="firmId"/>
    /// now; null when it is not a member. A read that could not be made throws (the job retries); it is never "not a member" and never a pass.
    /// </summary>
    private async Task<IReadOnlyList<Guid>?> ActiveFirmsOfMemberAsync(Guid contactId, Guid? firmId, CancellationToken ct)
    {
        if (firmId is not { } firm || firm == Guid.Empty)
        {
            return null;
        }

        var (firms, unreadable) = await ExternalInvoiceSubmissionAuthorizationFilter
            .ReadContactFirmsAsync(contactId, contactUnreadable: false, _participations, _logger, ct).ConfigureAwait(false);
        if (unreadable)
        {
            throw new InvalidOperationException("The firm membership of a recipient could not be read.");
        }

        return firms.Contains(firm) ? firms : null;
    }

    /// <summary>One plain address with no whitespace, comma, semicolon or control character (a header-injection or a list is refused); null otherwise.</summary>
    internal static string? WellFormed(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is ',' or ';' or '<' or '>' or '"'))
        {
            return null;
        }

        return MailAddress.TryCreate(trimmed, out var parsed) && string.Equals(parsed.Address, trimmed, StringComparison.Ordinal)
            ? trimmed
            : null;
    }

    private async Task<Entity?> FirstAsync(string entity, string column, Guid value, string[] columns, CancellationToken ct)
    {
        var query = new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(columns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition(column, ConditionOperator.Equal, value);
        return (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
    }
}
