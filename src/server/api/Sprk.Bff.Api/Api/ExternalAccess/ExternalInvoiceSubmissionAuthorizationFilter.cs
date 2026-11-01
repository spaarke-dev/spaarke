using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>Which external invoice submission route a <see cref="ExternalInvoiceSubmissionAuthorizationFilter"/> guards.</summary>
public enum ExternalInvoiceSubmissionAccess
{
    /// <summary>POST /{core}/{id}/invoice-submissions: Read and Create on the core record, and the firm.</summary>
    Submit,

    /// <summary>GET /invoice-submissions/{id}: the caller's own firm's submission, on a core record the caller can read.</summary>
    ReadOne,

    /// <summary>GET /invoice-submissions: the caller's firms (the list is then scoped by the caller's readable records).</summary>
    List,

    /// <summary>
    /// GET /invoice-submissions/firm-records?firmId=: which of the caller's readable core records are linked to one of the caller's firms
    /// (ebilling-r1 task 051, D-106: the SPA picker lists only those). The firm must be one of the caller's.
    /// </summary>
    FirmRecords,
}

/// <summary>Attaches <see cref="ExternalInvoiceSubmissionAuthorizationFilter"/> to an external invoice submission route.</summary>
public static class ExternalInvoiceSubmissionAuthorizationFilterExtensions
{
    /// <summary>
    /// Decides the caller, the firm and the core record of an external invoice submission route (ebilling-r1 task 044, NFR-10) before the
    /// handler. <paramref name="coreKind"/> is required for <see cref="ExternalInvoiceSubmissionAccess.Submit"/>.
    /// </summary>
    public static TBuilder AddExternalInvoiceSubmissionAuthorizationFilter<TBuilder>(
        this TBuilder builder, ExternalInvoiceSubmissionAccess access, InvoiceCoreRecordKind? coreKind = null)
        where TBuilder : IEndpointConventionBuilder
    {
        if (access == ExternalInvoiceSubmissionAccess.Submit && coreKind is null)
        {
            throw new ArgumentException("A submit route names its core record kind.", nameof(coreKind));
        }

        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new ExternalInvoiceSubmissionAuthorizationFilter(
                access,
                coreKind,
                services.GetRequiredService<ExternalParticipationService>(),
                services.GetRequiredService<InvoiceSubmissionService>(),
                services.GetRequiredService<ILogger<ExternalInvoiceSubmissionAuthorizationFilter>>(),
                services.GetService<ExternalInvoiceSubmissionHiding>());
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>What the filter decided, for the handler that runs after it.</summary>
/// <param name="Principal">The resolved caller principal.</param>
/// <param name="FirmIds">The caller's conferring organizations (active, current memberships of active organizations).</param>
/// <param name="FirmId">Submit: the validated firm the submission is filed for.</param>
/// <param name="CoreRecord">Submit: the core record the route names.</param>
/// <param name="Submission">ReadOne: the submission the caller may see.</param>
internal sealed record ExternalInvoiceSubmissionScope(
    CallerPrincipal Principal,
    IReadOnlyList<Guid> FirmIds,
    Guid? FirmId,
    InvoiceCoreRecord? CoreRecord,
    InvoiceSubmissionView? Submission)
{
    /// <summary>HttpContext.Items key under which the decided scope is stored.</summary>
    internal static readonly object HttpContextItemsKey = new();
}

/// <summary>
/// ebilling-r1 task 044 — the route-level gate of the external invoice submission routes (ADR-008), on the
/// <c>/api/v1/external</c> collaboration group (whose caller-principal filter has already resolved the caller).
/// </summary>
/// <remarks>
/// <para><b>The core record.</b> The caller's rights on it are the evaluator's post-veto answer already on
/// <see cref="CallerPrincipal"/> (<c>RightsOn</c>, the same expression the external document upload uses through
/// <c>ExternalProjectDataEndpoints.RightsForRoot</c>). A submit needs <see cref="AccessRights.Read"/> and
/// <see cref="AccessRights.Create"/> — Collaborate or Full Access — because it creates a document and a submission under the record;
/// View Only and "no access" are the same 403 <see cref="InvoiceSubmissionErrors.RecordForbidden"/>. (The POML names
/// <c>IAccessibleRecordSetService.IsOperationPermittedAsync</c>; it takes a WORKFORCE principal and cannot answer for a CIAM contact.
/// The principal's rights ARE that service's composed answer, for both planes.)</para>
/// <para><b>The firm (NFR-10).</b> Derived server-side from the caller contact's CONFERRING organization memberships
/// (<see cref="ReadCallerFirmsAsync"/>: an Active contact, then <c>ExternalParticipationService.ReadOrganizationMembershipsAsync</c>, the one
/// membership read). A <c>firmId</c> query value is
/// accepted only when it is one of them (else 403 <see cref="InvoiceSubmissionErrors.FirmForbidden"/>). Without one: a single firm is
/// used; several are 400 <see cref="InvoiceSubmissionErrors.FirmRequired"/> (the server never picks one); none is 403. A membership read
/// that could not be completed — or a contact that could not be read — is 503 <see cref="InvoiceSubmissionErrors.FirmUnverifiable"/>,
/// never "no firm".</para>
/// <para><b>A systemuser (task 051).</b> A workforce systemuser is refused on a submit route before anything is read or written
/// (403 <see cref="InvoiceSubmissionErrors.UseInternalRoute"/>); internal users submit through the internal route.</para>
/// <para><b>The firm is linked to the record (D-106, D-110, task 051).</b> After the checks above, a submit is accepted only when the chosen firm is
/// one of the record's assigned law firms; else 403 <see cref="InvoiceSubmissionErrors.FirmNotLinked"/> (code only), and a link read that
/// faults is 503 <see cref="InvoiceSubmissionErrors.FirmUnverifiable"/>. <b>The owner of this rule is <see cref="InvoiceSubmissionService.SubmitAsync"/></b>,
/// which every channel (this one and the internal route) calls and which enforces it with the same codes; the check here is an EARLY copy of
/// it, through the same <see cref="InvoiceSubmissionService.CheckFirmLinkAsync"/>, kept so a refused request is answered before its upload is read.
/// The copy refuses only a definite 403 NotLinked: an unreadable answer (a fault, or a record that is not returned, which the reader cannot tell
/// apart) passes through to the service, which answers a missing record 422 and a real fault 503.</para>
/// <para><b>Reads.</b> One submission is visible only when its firm is one of the caller's and the caller can read its core record;
/// anything else, including a missing row, is the same 404 (no existence oracle). The list is scoped by the caller's firms here and by
/// the caller's readable records in the query.</para>
/// <para>Broker-only (ADR-028 A1/A3): app-only reads, no OBO, no plane branching. Fails closed: a missing principal denies.</para>
/// </remarks>
internal sealed class ExternalInvoiceSubmissionAuthorizationFilter : IEndpointFilter
{
    /// <summary>What a submit needs on the core record. Also what the firm-records picker offers: a record the caller could not submit on is not listed.</summary>
    internal const AccessRights SubmitRights = AccessRights.Read | AccessRights.Create;

    private readonly ExternalInvoiceSubmissionAccess _access;
    private readonly InvoiceCoreRecordKind? _coreKind;
    private readonly ExternalParticipationService _participations;
    private readonly InvoiceSubmissionService _submissions;
    private readonly ILogger<ExternalInvoiceSubmissionAuthorizationFilter> _logger;
    private readonly ExternalInvoiceSubmissionHiding? _hideSubmissions;

    /// <param name="hideSubmissions">ebilling-r1 task 098 (review round 4b): the submission decision the list applies too. Null
    /// withholds every submission on the read-one route (fail closed).</param>
    public ExternalInvoiceSubmissionAuthorizationFilter(
        ExternalInvoiceSubmissionAccess access,
        InvoiceCoreRecordKind? coreKind,
        ExternalParticipationService participations,
        InvoiceSubmissionService submissions,
        ILogger<ExternalInvoiceSubmissionAuthorizationFilter> logger,
        ExternalInvoiceSubmissionHiding? hideSubmissions = null)
    {
        _access = access;
        _coreKind = coreKind;
        _participations = participations;
        _submissions = submissions;
        _logger = logger;
        _hideSubmissions = hideSubmissions;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal principal)
        {
            _logger.LogWarning("[EBILLING-SUBMIT] No caller principal on an external invoice submission route; denying (fail closed).");
            return Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.RecordForbidden, "Your access could not be determined.");
        }

        var (scope, denial) = _access switch
        {
            ExternalInvoiceSubmissionAccess.Submit => await DecideSubmitAsync(http, principal),
            ExternalInvoiceSubmissionAccess.ReadOne => await DecideReadOneAsync(http, principal),
            ExternalInvoiceSubmissionAccess.List => await DecideListAsync(http, principal),
            ExternalInvoiceSubmissionAccess.FirmRecords => await DecideFirmRecordsAsync(http, principal),
            _ => (null, Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.RecordForbidden, "Unknown route.")),
        };

        if (denial is not null)
        {
            return denial;
        }

        http.Items[ExternalInvoiceSubmissionScope.HttpContextItemsKey] = scope;
        return await next(context);
    }

    // ── Submit ──

    private async Task<(ExternalInvoiceSubmissionScope?, IResult?)> DecideSubmitAsync(HttpContext http, CallerPrincipal principal)
    {
        // A workforce systemuser is refused before anything is read or written, whatever contact it is linked to: the firm membership a
        // linked contact carries is a CONTACT's, and an internal user submits through the internal route (the shape of
        // ContactGrantorAuthorizationFilter's UseManageAccessDenial). /me lists no firms for such a caller, so the SPA never offers it.
        if (IsSystemUser(principal))
        {
            _logger.LogWarning(
                "[EBILLING-SUBMIT] DENIED: systemuser {SystemUserId} called an external invoice submission route; internal users submit " +
                "through the internal route.", principal.SystemUserId);
            return (null, Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.UseInternalRoute,
                "This is the external invoice submission. As an internal user, submit invoices through the internal route instead."));
        }

        if (!Guid.TryParse(http.Request.RouteValues["id"]?.ToString(), out var recordId) || recordId == Guid.Empty)
        {
            return (null, Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.RecordForbidden, "You cannot submit invoices on this record."));
        }

        // The record first: a caller without Collaborate on it learns nothing about firms.
        var core = new InvoiceCoreRecord(_coreKind!.Value, recordId);
        if (!principal.RightsOn(core.GrantRootType, recordId).HasFlag(SubmitRights))
        {
            _logger.LogInformation("[EBILLING-SUBMIT] Denied: caller lacks Read+Create on {Kind} {RecordId}", core.Kind, recordId);
            return (null, Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.RecordForbidden, "You cannot submit invoices on this record."));
        }

        var (firms, firmDenial) = await ReadFirmsAsync(principal, http.RequestAborted);
        if (firmDenial is not null)
        {
            return (null, firmDenial);
        }

        if (firms.Count == 0)
        {
            return (null, Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.FirmForbidden,
                "You do not belong to an active firm, so you cannot submit invoices."));
        }

        Guid firmId;
        var requested = http.Request.Query["firmId"].ToString();
        if (requested.Length > 0)
        {
            if (!Guid.TryParse(requested, out firmId) || firmId == Guid.Empty)
            {
                return (null, Deny(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid, "firmId must be a GUID."));
            }

            if (!firms.Contains(firmId))
            {
                _logger.LogInformation("[EBILLING-SUBMIT] Denied: the requested firm is not one of the caller's firms");
                return (null, Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.FirmForbidden,
                    "You can submit invoices only for your own firm."));
            }
        }
        else if (firms.Count == 1)
        {
            firmId = firms[0];
        }
        else
        {
            return (null, Deny(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmRequired,
                "You belong to more than one firm; name the firm (firmId) the invoice is from."));
        }

        // D-106: the chosen firm must be linked to the chosen record. Last, so the answer says nothing to a caller who failed any check above.
        if (await DenyUnlinkedFirmAsync(core, firmId, http.RequestAborted) is { } unlinked)
        {
            return (null, unlinked);
        }

        return (new ExternalInvoiceSubmissionScope(principal, firms, firmId, core, null), null);
    }

    /// <summary>True for a workforce systemuser principal (any non-null systemuser id, fail closed: no emptiness test).</summary>
    internal static bool IsSystemUser(CallerPrincipal principal) => principal.SystemUserId is not null;

    /// <summary>
    /// D-106 / D-110, the EARLY copy of the submission service's own check (<see cref="InvoiceSubmissionService.CheckFirmLinkAsync"/>, the one
    /// definition of "linked": the record's assigned law firms <c>sprk_assignedlawfirm1/2</c>, an org-level grant does not link). Null when linked
    /// OR when the links could not be read; otherwise the 403 refusal, which carries the code only. <b>Only a definite NotLinked refuses here.</b>
    /// The reader cannot tell a record that is not returned (deleted, or not found) from a fault, so both come back unreadable; refusing 503 on that
    /// would answer a deleted record with "try again shortly". An unreadable answer passes through instead: the service reads the record's fields
    /// first (a missing record is its 422), then re-reads the links and answers 503 only for a real fault, so nothing is ever treated as linked.
    /// </summary>
    private async Task<IResult?> DenyUnlinkedFirmAsync(InvoiceCoreRecord core, Guid firmId, CancellationToken ct)
    {
        switch (await _submissions.CheckFirmLinkAsync(core, firmId, ct))
        {
            case InvoiceFirmLink.Linked:
                return null;
            case InvoiceFirmLink.NotLinked:
                _logger.LogInformation("[EBILLING-SUBMIT] Denied: the chosen firm is not linked to {Kind} {RecordId}", core.Kind, core.RecordId);
                return Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.FirmNotLinked,
                    "The firm you chose is not linked to this record, so an invoice cannot be submitted for it.");
            default:
                return null; // unreadable: the service decides (see the summary); never a pass for the write, only for this early copy
        }
    }

    // ── Read one ──

    private async Task<(ExternalInvoiceSubmissionScope?, IResult?)> DecideReadOneAsync(HttpContext http, CallerPrincipal principal)
    {
        if (!Guid.TryParse(http.Request.RouteValues["id"]?.ToString(), out var submissionId) || submissionId == Guid.Empty)
        {
            return (null, ProblemDetailsHelper.UniformRecordNotFound(http));
        }

        var (firms, firmDenial) = await ReadFirmsAsync(principal, http.RequestAborted);
        if (firmDenial is not null)
        {
            return (null, firmDenial);
        }

        if (firms.Count == 0)
        {
            return (null, ProblemDetailsHelper.UniformRecordNotFound(http));
        }

        InvoiceSubmissionView? view;
        try
        {
            view = await _submissions.GetAsync(submissionId, http.RequestAborted);
        }
        catch (SdapProblemException ex)
        {
            // A read fault is 503 (retryable), never a 500 and never the not-found 404.
            return (null, InvoiceSubmissionHttp.ReadFault(ex));
        }

        if (view is not { FirmId: { } firmId, CoreRecord: { } core }
            || !firms.Contains(firmId)
            || !principal.RightsOn(core.GrantRootType, core.RecordId).HasFlag(AccessRights.Read))
        {
            return (null, ProblemDetailsHelper.UniformRecordNotFound(http));
        }

        // ebilling-r1 task 098 (model 9.1, (a')), review round 4b: THE decision the list applies
        // (ExternalInvoiceSubmissionEndpoints.VisibleSubmissionsAsync): with an invoice, the invoice predicate; without one, the
        // submission's own source. A withheld submission is the SAME 404 as a missing one; no seam withholds it too (fail closed).
        // A failed read is a retryable 503 (EB-D-169), never the not-found 404, and never shows the submission.
        IReadOnlyList<InvoiceSubmissionView> visible;
        try
        {
            visible = await ExternalInvoiceSubmissionEndpoints.VisibleSubmissionsAsync(
                principal, new[] { view }, _hideSubmissions, _logger, http.RequestAborted);
        }
        catch (SdapProblemException ex)
        {
            return (null, InvoiceSubmissionHttp.ReadFault(ex));
        }

        if (visible.Count == 0)
        {
            return (null, ProblemDetailsHelper.UniformRecordNotFound(http));
        }

        return (new ExternalInvoiceSubmissionScope(principal, firms, firmId, core, view), null);
    }

    // ── List ──

    private async Task<(ExternalInvoiceSubmissionScope?, IResult?)> DecideListAsync(HttpContext http, CallerPrincipal principal)
    {
        var (firms, firmDenial) = await ReadFirmsAsync(principal, http.RequestAborted);
        return firmDenial is not null
            ? (null, firmDenial)
            : (new ExternalInvoiceSubmissionScope(principal, firms, null, null, null), null);
    }

    // ── Firm records (the picker's link read, D-106) ──

    private async Task<(ExternalInvoiceSubmissionScope?, IResult?)> DecideFirmRecordsAsync(HttpContext http, CallerPrincipal principal)
    {
        if (IsSystemUser(principal))
        {
            return (null, Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.UseInternalRoute,
                "This is the external invoice submission. As an internal user, submit invoices through the internal route instead."));
        }

        if (!Guid.TryParse(http.Request.Query["firmId"].ToString(), out var firmId) || firmId == Guid.Empty)
        {
            return (null, Deny(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid, "firmId must be a GUID."));
        }

        var (firms, firmDenial) = await ReadFirmsAsync(principal, http.RequestAborted);
        if (firmDenial is not null)
        {
            return (null, firmDenial);
        }

        if (!firms.Contains(firmId))
        {
            return (null, Deny(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.FirmForbidden,
                "You can list records only for your own firm."));
        }

        return (new ExternalInvoiceSubmissionScope(principal, firms, firmId, null, null), null);
    }

    // ── The firm ──

    /// <summary>The caller's conferring firms; empty for a caller with no contact; a 503 when they cannot be read.</summary>
    private async Task<(IReadOnlyList<Guid> Firms, IResult? Denial)> ReadFirmsAsync(CallerPrincipal principal, CancellationToken ct)
    {
        var (firms, unreadable) = await ReadCallerFirmsAsync(principal, _participations, _logger, ct);
        return unreadable ? (firms, Unverifiable()) : (firms, null);
    }

    /// <summary>
    /// The ONE definition of "the caller's firms": the contact's CONFERRING organization memberships. Shared by this filter and by
    /// <c>GET /api/v1/external/me</c> (ebilling-r1 task 051, which lists them for the firm step), so the list the SPA offers is exactly the set
    /// <c>firmId</c> is validated against. <c>Unreadable</c> is true when the contact or the memberships could not be read (never "no firm").
    /// A contact that is not Active confers no firms (the UAC task 137 rule, <c>AccessibleRecordSetService</c>: a deactivated contact's grants
    /// confer nothing), read LIVE and remembered for the request, so it is the read the principal's own composition already made.
    /// </summary>
    internal static async Task<(IReadOnlyList<Guid> Firms, bool Unreadable)> ReadCallerFirmsAsync(
        CallerPrincipal principal, ExternalParticipationService participations, ILogger logger, CancellationToken ct)
    {
        if (principal.ContactUnreadable)
        {
            return (Array.Empty<Guid>(), true);
        }

        if (principal.ContactId == Guid.Empty)
        {
            return (Array.Empty<Guid>(), false);
        }

        switch (await participations.ReadContactStateAsync(principal.ContactId, ct))
        {
            case ContactRecordState.Active:
                break;
            case ContactRecordState.Inactive:
                logger.LogWarning(
                    "[EBILLING-FIRMS] Contact {ContactId} is not Active: it confers no firms (UAC task 137).", principal.ContactId);
                return (Array.Empty<Guid>(), false);
            default:
                return (Array.Empty<Guid>(), true);
        }

        // The same junction read as ContactGrantorAuthorizationFilter.ReadMembershipsAsync (ExternalParticipationService.ReadOrganizationMembershipsAsync,
        // task 109), called here so a fault is logged as what it is on THESE paths (the submission routes and /me), not as a refused grant.
        ActiveOrgMemberships memberships;
        try
        {
            memberships = await participations.ReadOrganizationMembershipsAsync(principal.ContactId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "[EBILLING-FIRMS] The organization memberships of contact {ContactId} could not be read; the caller's firms are unknown.",
                principal.ContactId);
            return (Array.Empty<Guid>(), true);
        }

        return memberships.Unreadable
            ? (Array.Empty<Guid>(), true)
            : (memberships.ConferringOrganizationIds.Distinct().ToList(), false);
    }

    private static IResult Unverifiable() =>
        Deny(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.FirmUnverifiable,
            "Your firm could not be checked right now. Try again shortly.");

    private static IResult Deny(int status, string code, string detail) => InvoiceSubmissionHttp.Refused(status, code, detail);
}
