using System.Security.Claims;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/set-record-share-expiry
///
/// Sets ONE expiry date on every active external share of a record — contact shares AND organization
/// shares — in a single all-or-nothing write (spec FR-33, task 098). This is the Manage Access toolbar's
/// "Expiration" (owner design 2026-09-10): one date per record that "applies to all sharing", stored on
/// each share's <c>sprk_expiresdate</c> rather than on the record itself (design note §11).
///
/// <para><b>Why one endpoint rather than N calls to /grant.</b> Calling <c>/grant</c> once per share from
/// the client is the non-atomic path: a failure part-way through while SHORTENING leaves some shares at the
/// later date — access that outlives the date the user just set. Here every row is written by ONE
/// <see cref="IGenericEntityService.BulkUpdateAsync"/>, which task 096 made a genuine
/// <c>ExecuteTransactionRequest</c>: every share read at the start gets the date, or none does. A share
/// created by a concurrent <c>/grant</c> between that read and the commit keeps <c>/grant</c>'s own date —
/// still bounded (task 097), and task 099 sends the toolbar date with every add, so the two converge.</para>
///
/// <para><b>Which rows.</b> Every <c>statecode = 0</c> row whose root lookup is this record, selected by ONE
/// server-side filter built from the request's own recordType + recordId. The client never supplies row ids,
/// so a caller with Write on record A cannot reach record B's shares — the task's escalation trigger was
/// evaluated against this and does not fire. A row already past its date is renewed too (owner decision
/// 2026-09-11, "Renew them too"). Revoked (inactive) rows are untouched, and standing-grant access has no
/// row at all, so it is out of scope by design (owner 2026-09-10, design note §12.1).</para>
///
/// <para><b>Contact-issued shares are taken over</b> (task 140; session 27 round 34 item 3): a share a contact issued
/// from the external SPA gets the date AND, in the same transaction, its contact issuer cleared and
/// <c>sprk_grantedby</c> set to the caller — so the contact can neither re-lengthen (own re-grant) nor revoke the
/// operator's deliberate time bound. Every other share gets the expiry and nothing else.</para>
///
/// <para><b>Authorization</b> is the group-level <see cref="DelegationRuleFilter"/>: Write on THIS record,
/// evaluated as the caller (OBO), before the handler runs. The filter and the handler resolve the target
/// through the same <see cref="ResolveRoot"/>, and this request has no legacy <c>projectId</c>, so the
/// record that was authorized and the rows that are written cannot diverge. The write itself runs app-only
/// (as <c>/grant</c>'s does), so the caller is recorded in the log lines.</para>
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: authorization by the route group's endpoint filter.
/// ADR-009: the participation DATA cache is invalidated after the write; no decision is cached.
/// </summary>
public static class SetRecordShareExpiryEndpoint
{
    /// <summary>The request named no record the handler can resolve (unreachable through the route — the filter denies first).</summary>
    internal const string RecordUnresolvedReasonCode = "sdap.access.share_expiry.record_unresolved";

    /// <summary>The request named no expiry. Unlike <c>/grant</c> there is nothing to default to: the date IS the request.</summary>
    internal const string ExpiryRequiredReasonCode = "sdap.access.share_expiry.expiry_required";

    /// <summary>The record's shares could not be read, so nothing was changed.</summary>
    internal const string EnumerationFailedReasonCode = "sdap.access.share_expiry.enumeration_failed";

    /// <summary>More shares than one transaction may carry, so nothing was changed.</summary>
    internal const string TooManySharesReasonCode = "sdap.access.share_expiry.too_many_shares";

    /// <summary>A share was read but carries no usable id, so it cannot be addressed; nothing was changed.</summary>
    internal const string ShareUnidentifiableReasonCode = "sdap.access.share_expiry.share_unidentifiable";

    /// <summary>A share is also linked to ANOTHER record, so changing it here would change access there; nothing was changed.</summary>
    internal const string ShareSpansRecordsReasonCode = "sdap.access.share_expiry.share_spans_records";

    /// <summary>
    /// The transaction was attempted and its outcome could not be confirmed. All-or-nothing: the record is
    /// either fully at the new date or untouched — never half.
    /// </summary>
    internal const string WriteFailedReasonCode = "sdap.access.share_expiry.write_failed";

    /// <summary>
    /// The most shares one request will update.
    /// </summary>
    /// <remarks>
    /// <para>1,000 is the ceiling this project treats as <c>ExecuteTransactionRequest</c>'s limit (design
    /// note §12.2 — the documented <c>ExecuteMultiple</c> batch size; the transaction page states none of its
    /// own). The bound is also what keeps the read honest: <see cref="DataverseWebApiClient.QueryAsync{T}"/>
    /// reads ONE page and discards <c>@odata.nextLink</c>, so asking for <c>Max + 1</c> turns a silent
    /// truncation — which would leave the unread shares at their OLD, possibly later, date — into a refusal.</para>
    /// <para>Per-record share counts are far below this; it is a guard rail, not a live limit.</para>
    /// </remarks>
    internal const int MaxSharesPerRecord = 1000;

    /// <summary>Title for the refusals where it is certain that no share was changed.</summary>
    private const string NotAppliedTitle = "Expiry not applied";

    /// <summary>Registers the route on the external-access management group.</summary>
    public static RouteGroupBuilder MapSetRecordShareExpiryEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/set-record-share-expiry", Handle)
            .WithName("SetRecordShareExpiry")
            .WithSummary("Set one expiry on every active external share of a record")
            .WithDescription(
                "Writes the expiry to every active sprk_externalrecordaccess row of the record (contact and " +
                "organization shares) in one all-or-nothing transaction, then invalidates the affected " +
                "contacts' participation cache. Requires Write on the record.")
            .Produces<SetRecordShareExpiryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    /// <summary>
    /// Handles POST /api/v1/external-access/set-record-share-expiry.
    /// </summary>
    /// <returns>
    /// 200 with the number of shares now carrying the date (0 when the record has none — the client then keeps
    /// the date as the default for the next share it adds, task 099). 400 when the expiry is missing or before
    /// today. 409 when a share is also linked to another record. 422 when the record has more shares than one
    /// transaction may carry. 500, with a reason code and a message, when the shares could not be read or
    /// addressed, or the transaction could not be confirmed.
    /// </returns>
    internal static async Task<IResult> Handle(
        SetRecordShareExpiryRequest request,
        DataverseWebApiClient dataverseClient,
        IDataverseService dataverseService,
        ExternalParticipationService participations,
        TimeProvider timeProvider,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        // The delegation filter already resolved this root and would have denied an unresolvable one, so a
        // failure here is not reachable through the route. Checked anyway: the handler must not trust a
        // pipeline it cannot see.
        var root = ResolveRoot(request);
        if (!root.Ok)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                RecordUnresolvedReasonCode, root.Error!);

        if (request.ExpiryDate is not { } expiry)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                ExpiryRequiredReasonCode, "ExpiryDate is required: it is the date every share on this record will end.");

        // Same rule as /grant (task 097): Date Only, UTC "today", today itself valid.
        var today = ExternalGrantLifecycle.TodayUtc(timeProvider);
        if (GrantExternalAccessEndpoint.ValidateRequestedExpiry(expiry, today, httpContext) is { } pastExpiry)
            return pastExpiry;

        var callerOid = CallerResolution.ResolveObjectId(httpContext.User);

        // ── Enumerate: every active share of THIS record, one server-side filter ──
        List<ExternalGrantRow> shares;
        try
        {
            shares = await ExternalGrantLifecycle.QueryActiveRowsForRootAsync(
                dataverseClient, root.Type, root.Id, top: MaxSharesPerRecord + 1, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[SHARE-EXPIRY] Could not read the active shares of {RootType} {RootId} (caller {CallerOid}). " +
                "Nothing was changed.", root.Type, root.Id, callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotAppliedTitle,
                EnumerationFailedReasonCode,
                "The shares on this record could not be read, so no expiry was changed. Try again.");
        }

        if (shares.Count > MaxSharesPerRecord)
        {
            logger.LogError(
                "[SHARE-EXPIRY] {RootType} {RootId} has more than {Max} active shares — more than one " +
                "transaction may carry. Refusing rather than updating a subset.",
                root.Type, root.Id, MaxSharesPerRecord);
            return Refused(httpContext, StatusCodes.Status422UnprocessableEntity, NotAppliedTitle,
                TooManySharesReasonCode,
                $"This record has more than {MaxSharesPerRecord} active shares, which is more than one change " +
                "can update together. No expiry was changed.");
        }

        // A row without an id cannot be addressed by an update. Skipping it would leave that share at its old
        // — possibly LATER — date while reporting success, so the whole request is refused instead.
        if (shares.Any(s => s.Id == Guid.Empty))
        {
            logger.LogError(
                "[SHARE-EXPIRY] An active share of {RootType} {RootId} came back without an id. Nothing was changed.",
                root.Type, root.Id);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotAppliedTitle,
                ShareUnidentifiableReasonCode,
                "One of the shares on this record could not be identified, so no expiry was changed.");
        }

        // A share row carrying a SECOND root lookup confers access on that other record too — the read path
        // grants every populated root. Re-dating it, or reviving it under "Renew them too", would change access
        // to a record the caller's Write was never checked on. The BFF never writes such a row, but an import or
        // a form edit can. Refused rather than skipped: skipping would leave it at its old date on THIS record.
        var spanning = shares.Count(s => RootLookupCount(s) > 1);
        if (spanning > 0)
        {
            logger.LogError(
                "[SHARE-EXPIRY] {Count} active share(s) of {RootType} {RootId} are also linked to another record. " +
                "Refusing: changing them would change access to a record the caller's Write was not checked on.",
                spanning, root.Type, root.Id);
            return Refused(httpContext, StatusCodes.Status409Conflict, NotAppliedTitle,
                ShareSpansRecordsReasonCode,
                $"{spanning} share(s) on this record are also linked to another record, so changing their expiry " +
                "here would change access to that record too. No expiry was changed; correct those shares first.");
        }

        if (shares.Count == 0)
        {
            logger.LogInformation(
                "[SHARE-EXPIRY] {RootType} {RootId} has no active shares; nothing to update (caller {CallerOid}).",
                root.Type, root.Id, callerOid);
            return TypedResults.Ok(new SetRecordShareExpiryResponse(UpdatedCount: 0, ExpiresDate: expiry));
        }

        // ── Take-over of contact-issued shares (task 140; session 27 round 34 item 3) ──
        // A Write holder setting the record's date is an INTERNAL decision on every share, contact-issued ones included.
        // Left stamped with its contact issuer, such a share could be re-lengthened by that contact's own re-grant (up to
        // its cap) or revoked by it — overriding the operator's deliberate time bound, the harm managed_elsewhere exists to
        // prevent. So each one is taken over in the SAME transaction: the contact issuer is cleared and sprk_grantedby is
        // the caller (when the caller's systemuser resolves; an audit field never blocks the write — the core's rule).
        // Session 27 round 50 item 2: "contact-issued" includes a share whose issuing contact was DELETED (its recorded
        // provenance survives, the lookup does not), and the take-over clears that provenance in the same write.
        var contactIssued = shares.Count(s => s.IsContactIssued);
        Guid? takeOverBy = null;
        if (contactIssued > 0
            && Guid.TryParse(
                await GrantExternalAccessEndpoint.ResolveGrantedBySystemUserIdAsync(dataverseClient, callerOid, logger, ct),
                out var callerSystemUserId))
        {
            takeOverBy = callerSystemUserId;
        }

        // ── Write: ONE all-or-nothing transaction (task 096) ──────────────────
        var storedValue = ExternalGrantLifecycle.ToSdkDateOnly(expiry);
        var updates = shares
            .Select(s =>
            {
                var fields = new Dictionary<string, object> { ["sprk_expiresdate"] = storedValue };
                if (s.IsContactIssued)
                    ExternalGrantLifecycle.AddInternalTakeOverFields(fields, takeOverBy);
                return (s.Id, fields);
            })
            .ToList();

        try
        {
            await dataverseService.BulkUpdateAsync(ExternalGrantLifecycle.EntityLogicalName, updates, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[SHARE-EXPIRY] The transaction setting {Count} shares of {RootType} {RootId} to {Expiry} failed " +
                "(caller {CallerOid}).", shares.Count, root.Type, root.Id, expiry, callerOid);

            // Truthful by construction: a transaction whose OUTCOME is unknown (e.g. a timeout after commit)
            // may have applied — so neither the title nor the detail claims "nothing changed", only "never half".
            return Refused(httpContext, StatusCodes.Status500InternalServerError, "Expiry change not confirmed",
                WriteFailedReasonCode,
                $"The expiry could not be confirmed as applied. The change is all-or-nothing: either every share on " +
                $"this record now ends on {expiry:yyyy-MM-dd} or none does. Reload to see which, then retry if needed.");
        }

        if (contactIssued > 0)
        {
            logger.LogInformation(
                "[SHARE-EXPIRY] {Count} contact-issued share(s) of {RootType} {RootId} were taken over by the caller " +
                "(systemuser {SystemUserId}): their contact issuer is cleared, so the contact can no longer change or " +
                "revoke them.", contactIssued, root.Type, root.Id, takeOverBy?.ToString() ?? "(unresolved)");
        }

        logger.LogInformation(
            "[SHARE-EXPIRY] Caller {CallerOid} set {Count} active shares of {RootType} {RootId} to expire {Expiry} " +
            "({Renewed} of them had already lapsed and are renewed).",
            callerOid, shares.Count, root.Type, root.Id, expiry,
            // Task 106 review finding F3: this was a second in-memory definition of "expired" sitting in
            // the same folder as the read filter's. Log-only, and the exact negation of the predicate, so
            // there was no divergence yet -- which is precisely when to consolidate rather than after one
            // appears. Routed through the shared mirror.
            shares.Count(s => !ExternalParticipationService.ConfersAccessOn(s.ExpiresDate, today)));

        await InvalidateAffectedCachesAsync(shares, participations);

        return TypedResults.Ok(new SetRecordShareExpiryResponse(UpdatedCount: shares.Count, ExpiresDate: expiry));
    }

    /// <summary>
    /// The ONE record this request targets, from its explicit <c>recordType</c> + <c>recordId</c>. Shared with
    /// <see cref="DelegationRuleFilter"/>, so the record that is authorized is the record that is written.
    /// </summary>
    /// <remarks>
    /// Fail-closed: a missing or unknown type, or a missing id, returns <c>Ok == false</c>. There is no legacy
    /// <c>projectId</c> shorthand here, deliberately — a request carrying both could authorize one record and
    /// write another. The rule lives in <see cref="GrantExternalAccessEndpoint.ResolveExplicitRoot"/>, shared with
    /// the system-user share routes (task 063).
    /// </remarks>
    internal static GrantExternalAccessEndpoint.GrantRootResolution ResolveRoot(SetRecordShareExpiryRequest request)
        => GrantExternalAccessEndpoint.ResolveExplicitRoot(request.RecordType, request.RecordId);

    private static int RootLookupCount(ExternalGrantRow row)
        => (row.ProjectId.HasValue ? 1 : 0) + (row.MatterId.HasValue ? 1 : 0) + (row.WorkAssignmentId.HasValue ? 1 : 0);

    /// <summary>
    /// Clears the grant cache of every contact whose access came through one of the updated shares — the contact on
    /// a contact share, every ACTIVE member of the organization on an organization share — through the ONE
    /// invalidation routine (task 137), under every tenant a grant set can be cached under. Non-fatal, and
    /// deliberately NOT bound to the request's cancellation token: the write has already committed, so a client
    /// that disconnects now must not stop the clean-up half-way.
    /// </summary>
    /// <remarks>
    /// <para><b>Freshness, not a security boundary.</b> The cache holds WHICH grants a contact has, not their
    /// dates — expiry is applied by the read query's <c>$filter</c> when an entry is built (task 007). The new
    /// date is today or later, so the grantee is admitted today either way; the only window is the
    /// ≤60-second tail after an expiry midnight, which every grant already has on the read path and which
    /// invalidating at write time cannot shorten. What invalidation does buy is prompt RENEWAL: a lapsed share
    /// this request revives is normally visible on the next evaluation — at worst after the 60-second TTL,
    /// because a read already in flight can re-populate the entry after the removal (the participation service
    /// caches fire-and-forget).</para>
    /// <para><b>Task 137.</b> This method used to expand organizations itself, through the revoke path's
    /// 200-member reader, and SKIP an organization over that bound. The routine pages every organization to
    /// completion instead, and the other grant-write paths now share it rather than each keeping a copy.</para>
    /// </remarks>
    private static Task InvalidateAffectedCachesAsync(
        IReadOnlyList<ExternalGrantRow> shares,
        ExternalParticipationService participations)
    {
        var contactIds = shares.Where(s => s.ContactId.HasValue).Select(s => s.ContactId!.Value).Distinct().ToList();

        // A row with BOTH a contact and an organization is a person share whose firm is metadata — only a row
        // with NO contact is the organization's own share (ExternalGrantKey's rule).
        var organizationIds = shares
            .Where(s => s.ContactId is null && s.OrganizationId.HasValue)
            .Select(s => s.OrganizationId!.Value)
            .Distinct()
            .ToList();

        return participations.InvalidateGrantSetsAsync(contactIds, organizationIds, CancellationToken.None);
    }
    /// <summary>The single refusal shape: ProblemDetails with a reason code (ADR-003) and the trace id (ADR-019).</summary>
    private static IResult Refused(HttpContext httpContext, int statusCode, string title, string reasonCode, string detail)
        => Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
                ["reasonCode"] = reasonCode,
            });
}
