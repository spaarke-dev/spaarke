using System.Security.Claims;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/grant
///
/// Grants an external Contact access to a Secure Project by:
///   1. Creating a sprk_externalrecordaccess record in Dataverse.
///   2. Invalidating the contact's participation cache in Redis.
///
/// Every grant is time-bounded (spec FR-33, task 097): a requested expiry before today is rejected (400,
/// <c>sdap.access.grant.expiry_in_past</c>); an ABSENT one keeps the grant's existing expiry, else becomes
/// today + <see cref="ExternalGrantLifecycle.DefaultExpiryDays"/>. No client has to send a date.
///
/// Broker-only (ADR-028 Amendment A1): external users never authenticate to SPE
/// directly — all external SPE access is app-only via the BFF — so no synthetic
/// SPE container permission is written on grant.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-009: Redis cache invalidation after grant — ExternalParticipationService.InvalidateGrantSetsAsync (task 137).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class GrantExternalAccessEndpoint
{
    private const string EntitySet = "sprk_externalrecordaccesses";
    // Cache invalidation (task 137): through ExternalParticipationService.InvalidateGrantSetsAsync — the ONE
    // routine every grant-write path calls. It owns the key (resource + version), removes under every tenant a
    // grant set can be cached under (the CIAM one included), and expands an organization grant to its members.
    // This file used to carry its own cache.RemoveAsync copy keyed on the caller's tid alone.

    /// <summary>
    /// Registers the grant endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapGrantExternalAccessEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/grant", GrantAccessAsync)
            .WithName("GrantExternalAccess")
            .WithSummary("Grant external access to a Contact for a Secure Project")
            .WithDescription(
                "Creates a sprk_externalrecordaccess record and invalidates the contact's Redis " +
                "participation cache after granting. External SPE access is app-only (broker-only).")
            .Produces<GrantAccessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            // 403: the delegation gate, or (task 139) the grantor's own rights allow granting nothing
            // (sdap.access.grant.caller_cannot_grant).
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // 409: the upsert matched an EXPIRED row and the request supplied no new expiry (task 023), or (task 139)
            // the request was capped at the grantor's level and the grantee already holds more (would_lower_existing).
            .ProducesProblem(StatusCodes.Status409Conflict)
            // 422: the record's access policy refuses this grantee (task 138 — record_restricted /
            // org_grant_direct_only_record), or the grantee is on the record's No Access list (task 139 —
            // grantee_denied). 503: the policy could not be read (policy_unreadable), or whether the grantee is on the
            // No Access list could not be checked (task 142 r4 — no_access_unverifiable).
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    /// <summary>Handles <c>POST /api/v1/external-access/grant</c>. Internal so the auth tests can drive it directly.</summary>
    internal static async Task<IResult> GrantAccessAsync(
        GrantAccessRequest request,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        CallerRecordAccessProbe callerAccessProbe,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        HttpContext httpContext,
        ILogger<Program> logger,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        // Grantee kind (task 073 #7): a normal grant names a Contact; an ORGANIZATION grant names an
        // Organization with NO ContactId — every ACTIVE member of that org then inherits access at
        // check time (AccessibleRecordSetService Term 3). Exactly one grantee kind is required; an
        // empty ContactId is only valid when an OrganizationId is supplied.
        var isOrgGrant = request.ContactId == Guid.Empty;
        if (isOrgGrant && !request.OrganizationId.HasValue)
            return ProblemDetailsHelper.ValidationError(
                "ContactId is required, unless granting to an Organization (supply OrganizationId with no ContactId).");

        // Resolve the polymorphic grant root (project|matter|workassignment) or the legacy ProjectId
        // shorthand. Fail-closed: a missing/unknown root is rejected 400 and NO row is written.
        var root = ResolveGrantRoot(request);
        if (!root.Ok)
            return ProblemDetailsHelper.ValidationError(root.Error!);

        if (!Enum.IsDefined(typeof(ExternalAccessLevel), request.AccessLevel))
            return ProblemDetailsHelper.ValidationError(
                $"AccessLevel must be one of: {string.Join(", ", Enum.GetNames<ExternalAccessLevel>())}.");

        // FR-33 (task 097): a past expiry is rejected before anything is written. An absent one is not an
        // error — CreateGrantAsync defaults it.
        var today = ExternalGrantLifecycle.TodayUtc(timeProvider);
        if (ValidateRequestedExpiry(request.ExpiryDate, today, httpContext) is { } expiryProblem)
            return expiryProblem;

        // ── Resolve caller identity for granted-by reference ─────────────────
        var callerSystemUserId = ResolveCallerSystemUserId(httpContext);

        // ── Task 139: the grantor's ceiling — their OWN rights on this record, re-probed as them ──
        // Re-probed rather than carried over from DelegationRuleFilter, mirroring /share-user: a handler must not trust
        // authorization state cached by a filter it cannot see. A probe that THROWS is a 500 with nothing written; a
        // probe that answers None (its answer for every OBO/transport/parse failure) becomes a ceiling of "none",
        // which the core refuses with 403 caller_cannot_grant.
        GrantCeiling ceiling;
        try
        {
            ceiling = await ProbeGrantorCeilingAsync(callerAccessProbe, httpContext, root.Type, root.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-GRANT] Could not establish the caller's own rights on {RootType} {RootId}. Nothing was granted.",
                root.Type, root.Id);
            return CallerRightsUnreadableProblem(httpContext);
        }

        logger.LogInformation(
            "[EXT-GRANT] Granting {AccessLevel} access to Contact {ContactId} for {RootType} {RootId} (ceiling {Ceiling})",
            request.AccessLevel, request.ContactId, root.Type, root.Id, ceiling);

        // ── Create the access record (Dataverse) + invalidate cache ──────────
        GrantUpsertOutcome outcome;
        try
        {
            outcome = await CreateGrantAsync(
                request, root.Type, root.Id, today, ceiling, callerSystemUserId,
                dataverseClient, participations, accessibleRecords, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-GRANT] Failed to create Dataverse access record for Contact {ContactId} / {RootType} {RootId}",
                request.ContactId, root.Type, root.Id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to create external access record in Dataverse.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        // Task 138: the record's access policy refused this grantee — and since task 139 also the grantor ceiling
        // (403), the never-lower rule (409) and the No Access list (422). The core returned the refusal as a VALUE,
        // before writing anything, so it reaches here instead of the catch-all 500 above.
        if (outcome.Refusal is { } refusal)
            return PolicyRefusalProblem(refusal, httpContext);

        // ADR-003 (task 023): the upsert may have matched an EXPIRED row that this request did not
        // resolve. The row exists, so this is not a server fault — but reporting a bare 200 would tell
        // the operator access was restored when the grantee still has none. 409 says "your request was
        // understood and did not take effect", and carries the row id so the caller can retry against it.
        if (outcome.Warning is { } warning)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Grant did not take effect",
                detail: warning,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = httpContext.TraceIdentifier,
                    ["reasonCode"] = "sdap.grant.expired_not_restored",
                    ["accessRecordId"] = outcome.AccessRecordId,
                });
        }

        // Task 142: a MANUAL grant onto a subject the Assigned-To ledger holds (an auto grant, a suggestion on a secure
        // record — "Grant" in Manage Access — or a declined entry) is now the operator's: ADOPTED, never revoked by the
        // rule afterwards. Keyed on the grant key the core wrote. Ledger-only; never thrown.
        var grantedKey = ResolveGrantKey(request, root.Type, root.Id);
        await assignedAccess.MarkGrantAdoptedAsync(
            root.Type, root.Id, grantedKey.ContactId, grantedKey.IsOrganizationGrant ? grantedKey.OrganizationId : null,
            outcome.AccessRecordId, CancellationToken.None);

        // Broker-only: no synthetic SPE container membership is granted on the external path. Task 139: the level
        // actually written, and whether the grantor's ceiling narrowed the request.
        return TypedResults.Ok(new GrantAccessResponse(
            outcome.AccessRecordId,
            SpeContainerMembershipGranted: false,
            GrantedAccessLevel: outcome.GrantedLevel,
            Narrowed: outcome.Narrowed));
    }

    /// <summary>
    /// Re-probes the caller's own rights on the grant root (as them, over OBO) and turns them into the grantor
    /// ceiling (task 139). Shared by <c>/grant</c> and <c>/invite-and-grant</c>. Exceptions propagate — the caller
    /// answers 500 and writes nothing.
    /// </summary>
    internal static async Task<GrantCeiling> ProbeGrantorCeilingAsync(
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        ExternalGrantRootType rootType,
        Guid rootId,
        CancellationToken ct)
    {
        var rights = await callerAccessProbe.GetCallerRightsAsync(
            Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext),
            ExternalGrantRoot.BindFor(rootType).EntitySet,
            rootId,
            ct);

        return GrantCeiling.FromGrantorRights(rights);
    }

    /// <summary>The 500 for a grantor-rights probe that threw (task 139): nothing was written or onboarded.</summary>
    internal static IResult CallerRightsUnreadableProblem(HttpContext httpContext)
        => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Access not granted",
            detail: "Your own access to this record could not be established, so nothing was granted. Try again.",
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
                ["reasonCode"] = ExternalGrantLifecycle.CallerRightsUnreadableReasonCode,
            });

    // =========================================================================
    // Reusable core (shared with the invite-and-grant orchestration, task 029)
    // =========================================================================

    /// <summary>
    /// UPSERTS a <c>sprk_externalrecordaccess</c> grant for one logical grant key (root × grantee), and
    /// invalidates the Contact's Redis participation cache. Throws on Dataverse failure; cache
    /// invalidation failure is non-fatal. Shared by <c>/grant</c> and <c>/invite-and-grant</c> (task 029)
    /// so both write an identical, audited grant.
    /// </summary>
    /// <returns>
    /// The id of the single surviving active row, and a <c>Warning</c> that is non-null when the key does NOT
    /// confer access after this request (task 023 / ADR-003 — see the expired-row branch). With no expiry in
    /// the request nothing was written (task 113); only a request that supplied a date the row will not confer
    /// on (the Assigned-To restore) was written as asked.
    /// </returns>
    /// <remarks>
    /// <para><b>Idempotent since task 010</b> (spec FR-09, finding A-11). This method previously CREATEd
    /// unconditionally, with no pre-existence check anywhere on the path — so granting the same contact
    /// the same access on the same root twice produced two active rows. Revoke then deactivated exactly
    /// one of them by id, leaving the other standing: access survived revocation, and the participation
    /// surface could not even show it (<c>QueryGrantSetAsync</c> collapses duplicates via
    /// <c>GroupBy(root).Max(level)</c> and never returns access-record ids).</para>
    ///
    /// <para>Now: query the logical key first — no match creates; an exact match is a no-op returning the
    /// existing id; a match at a different level updates that row IN PLACE. Any surplus active rows on
    /// the same key (pre-existing duplicates, or a lost create race) are collapsed onto the survivor.</para>
    ///
    /// <para><b>The record's access policy runs FIRST (task 138).</b> Before any query or write, the root's flags
    /// are read and <see cref="ExternalGrantLifecycle.DecideGrantPolicy"/> decides whether this grantee kind is
    /// admitted: contact and organization grants are refused on a Restricted record, organization-wide grants
    /// on a Secure or Limited one, and an unreadable policy refuses everything. A refusal is RETURNED in
    /// <see cref="GrantUpsertOutcome.Refusal"/>, never thrown — every caller wraps this method in a catch-all
    /// that would turn an exception into a bare 500. Because the check lives in this shared core, any future
    /// writer that calls it (task 140's contact-side route, task 142's auto-grants) cannot bypass it.</para>
    ///
    /// <para><b>Which row survives (task 106, ISS-008).</b> The survivor is elected by
    /// <see cref="ExternalGrantLifecycle.ElectSurvivor"/> — the row that will confer access longest after
    /// this request, ties broken by ascending id — not the lowest id outright. That single change makes two
    /// properties structural instead of checked: the expired-row branch below speaks for the WHOLE key
    /// (an expired survivor proves every row is expired), and the collapse can never deactivate the row
    /// access rests on (that row is the survivor). A request carrying an explicit expiry ties every row,
    /// so it elects exactly what it always did.</para>
    ///
    /// <para><b>The grantor ceiling is a REQUIRED input (task 139, WP-1).</b> Every grant is written at most at
    /// <paramref name="ceiling"/> — a request above it is NARROWED, never refused, and the outcome says so
    /// (<see cref="GrantUpsertOutcome.GrantedLevel"/>, <see cref="GrantUpsertOutcome.Narrowed"/>). A ceiling of
    /// none refuses (403). A narrowed request never LOWERS an existing higher grant on the key: the match path below
    /// updates the survivor's level in place, so without that check a "Full Access please" capped to Collaborate
    /// would overwrite somebody else's Full Access grant (409). A grantee on the record's No Access list is refused
    /// (422). All of it runs in <see cref="CheckGrantAsync"/>, BEFORE any write, and is shared with
    /// <c>/invite-and-grant</c>'s pre-onboarding check.</para>
    /// <para><b>The contact-issuer mode (task 140).</b> When <paramref name="contactIssuer"/> is supplied — only by the
    /// contact-side routes — the same core writes the grant with three differences, each owner-mandated: the row is
    /// stamped with the contact issuer (<c>sprk_grantedbycontact</c>) and never with <c>sprk_grantedby</c>; a grantee
    /// holding a row anybody else issued is refused and left untouched (409 managed_elsewhere — no proxy revocation or
    /// extension); and on the caller's OWN row the level is never lowered and the expiry never shortened, while the
    /// written expiry is the requested one (or today + 90) capped at the grantor's own (owner G2 (i)).</para>
    /// <para><b>Systemuser over a contact-issued row (task 140).</b> In the default mode, when the upsert CHANGES a row a
    /// contact issued, the row's issuer becomes the systemuser making the change (the contact stamp is cleared), so the
    /// contact can no longer revoke a decision an internal user made. A no-op re-grant changes nothing, so it leaves the
    /// issuer as it was.</para>
    /// </remarks>
    internal static async Task<GrantUpsertOutcome> CreateGrantAsync(
        GrantAccessRequest request,
        ExternalGrantRootType rootType,
        Guid rootId,
        DateOnly today,
        GrantCeiling ceiling,
        string? callerOid,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        ILogger logger,
        CancellationToken ct,
        ContactGrantIssuer? contactIssuer = null)
    {
        // A missing ceiling is a caller bug, never "uncapped" (WP-1). Thrown, so it surfaces as the caller's 500.
        ArgumentNullException.ThrowIfNull(ceiling);

        var key = ResolveGrantKey(request, rootType, rootId);

        // ── Task 140: a contact issuer's expiry — the requested date (or today + 90), capped at the grantor's own ──
        // Resolved BEFORE the checks so every later decision (create, raise, the ADR-003 conferral check) sees the date
        // that will actually be written. Narrowed, never refused — the time analogue of the level cap (owner G2 (i)).
        var expiryNarrowed = false;
        if (contactIssuer is { } issuerForExpiry)
        {
            var asked = request.ExpiryDate ?? ExternalGrantLifecycle.DefaultExpiry(today);
            var capped = issuerForExpiry.ExpiryCap is { } cap && cap < asked ? cap : asked;
            expiryNarrowed = capped != asked;
            request = request with { ExpiryDate = capped };
        }

        // ── Tasks 138 + 139: policy, ceiling, never-lower, No Access — BEFORE any side effect ──
        // The grantee comes from the SAME key the upsert writes, so the checks judge exactly the row that would be
        // written (an OrganizationId beside a ContactId is the contact's firm — a deny subject, not the grantee).
        var check = await CheckGrantAsync(
            GrantGrantee.ForKey(key, request.OrganizationId), request.AccessLevel, rootType, rootId, ceiling,
            dataverseClient, participations, accessibleRecords, logger, ct, contactIssuer);
        if (check.Refusal is { } refusal)
            return GrantUpsertOutcome.Refused(refusal);

        // From here on the request is written at the GRANTED level — the requested one capped at the ceiling.
        request = request with { AccessLevel = check.GrantedLevel };
        var requestedLevel = (int)request.AccessLevel;

        // ── UPSERT: does this logical grant already exist? ───────────────────
        // Read by CheckGrantAsync (the never-lower rule needs it first). Failures propagated from there
        // deliberately: falling back to a blind create on a failed pre-existence query would reintroduce exactly
        // the duplicate task 010 removed.
        var existing = check.ExistingRows;

        if (existing.Count > 0)
        {
            // Task 106 (ISS-008 / #973): the survivor is the row that confers access LONGEST — by
            // ConferralRank, which depends only on the row and today — ties broken by ascending id, NOT
            // simply the lowest id. Electing a CONFERRING row is what makes the ADR-003 check below a
            // statement about the whole key, and what keeps CollapseDuplicatesAsync from deactivating the
            // row access actually rests on. The rank takes NO request value, deliberately: a
            // request-dependent order is not shared between callers, and two racers applying different
            // orders deactivate each other's row (V1/F1). See ElectSurvivor and ConferralRank.
            var survivor = ExternalGrantLifecycle.ElectSurvivor(existing, today);

            // ── Task 023 (finding H1): the match path must write the EXPIRY too ──────────────
            //
            // It previously wrote only sprk_accesslevel, so re-granting to ADD or EXTEND an expiry
            // was silently a no-op while returning 200 + a record id. With task 007's read filter
            // enforcing expiry server-side, that is A-5's shape resurrected on the WRITE path by the
            // two tasks that closed it on the read path: the operator asks for bounded access, is told
            // it worked, and access stays unbounded.
            //
            // NOTE on clearing: a null ExpiryDate does NOT clear an existing expiry here. The request
            // contract cannot distinguish "omitted" from "explicitly null" (both deserialise to null on
            // `DateOnly? ExpiryDate`), and of the two readings only this one is safe — treating null as
            // "clear" would silently REMOVE an expiry whenever a caller re-granted without restating it,
            // which is the same unbounded-access defect in the other direction. Making clearing possible
            // is a CONTRACT change, escalated per this task's trigger; see
            // notes/task-023-grant-upsert-expiry.md. (FR-33 / task 097 superseded that escalation: with
            // every grant bounded, there is no "clear" left to design.)
            //
            // FR-33 (task 097): an absent expiry is DEFAULTED, never left unbounded. On a match, the default
            // is to KEEP the survivor's existing expiry — a re-grant from a surface with no date field must
            // never move a date someone set, in either direction. Only an UNBOUNDED survivor gets
            // today + DefaultExpiryDays. (An EXPIRED survivor keeps its date too, and is reported by the
            // ADR-003 check below rather than silently renewed.)
            // `expiryToWrite` (null = write NOTHING) is a different value from the `effectiveExpiry`
            // computed below (what the row will CARRY). Renamed from `requestedExpiry` after review
            // finding F4: the election helpers take a parameter of that name holding the caller's RAW
            // date, so two different values wore one name twenty lines apart — and substituting this one
            // into the election would have been circular (it is derived FROM the survivor) and would have
            // compiled.
            //
            // Task 140 — a CONTACT issuer never SHORTENS its own row: the (already capped) date is written only when it
            // is later than the row's current one. The never-lower rule for the level ran in CheckGrantAsync.
            DateOnly? expiryToWrite = contactIssuer is null
                ? request.ExpiryDate ?? (survivor.ExpiresDate is null ? ExternalGrantLifecycle.DefaultExpiry(today) : null)
                : survivor.ExpiresDate is { } current && current >= request.ExpiryDate!.Value ? null : request.ExpiryDate;
            var expiryChanged = expiryToWrite.HasValue && expiryToWrite != survivor.ExpiresDate;
            var levelChanged = survivor.AccessLevel != requestedLevel;

            // ── ADR-003: do not report success over a grant that confers nothing ─────────────
            //
            // The match filter selects on statecode only, so an EXPIRED row is still "active" to this
            // query. Re-granting over one without supplying a new expiry therefore returned 200 and a
            // record id while task 007's read filter kept excluding the row — the caller is told access
            // was restored, and the grantee still has none. If the request carries a new expiry, that date
            // is what the row will carry, and (being today or later — the routes 400 a past one) this does
            // not fire.
            //
            // ── Task 106 (ISS-008 / #973): this judges the WHOLE KEY, not one row ───────────
            //
            // The survivor is elected by effective expiry, so if IT does not confer access then NO active
            // row on the key does — which is the question the caller is really asking, because the read
            // path unions every active unexpired row on the key. Before task 106 the elected row was
            // simply the lowest id, so an expired lowest-id row produced this 409 — "still confers no
            // access" — while a live duplicate meant the grantee did have access. The 409 was false.
            //
            // NOTE `expiryToWrite` and `effectiveExpiry` are distinct on purpose: the former is nullable
            // because null means "write NOTHING" (task 097 — a re-grant from a surface with no date field
            // must not move a date someone set), the latter is what the row will CARRY. (Review finding F2
            // of task 106: merging them would be behaviour-preserving — the `!=` guard on `expiryChanged`
            // sees to that — so they are kept apart for what the nullable type EXPRESSES, not for safety.)
            //
            // ── Task 113 (ISS-028 / #1008): decided BEFORE the write ────────────────────────
            //
            // Every input (the request's expiry, the survivor's, today, and for a contact issuer the
            // already-resolved `expiryToWrite`) exists before the update below, and none of it is produced
            // by that update, so the answer is computed here. When the request supplied NO expiry, the
            // update could not change it (the survivor's date is kept), so the row would still confer
            // nothing whatever else it wrote: the request is refused here and writes NOTHING. Before task
            // 113 the check ran after the update, so a re-grant at a different level over an expired key
            // wrote the new level (and, over a contact-issued row, took it over) and THEN answered 409
            // "Grant did not take effect" — a false statement about a row it had just changed.
            //
            // What this ordering does NOT carry: a request that SUPPLIES a date the row will not confer
            // on. The routes reject a past date (400) and the contact-issuer mode always writes today or
            // later, so only the in-process Assigned-To rule sends one — deliberately, to put a raised
            // grant back to the operator's own lapsed level and date, which ENDS the access the rule had
            // extended (AssignedAccessMaterializer, PriorLevelRestoredLapsed). That write is the caller's
            // intent, so it still happens, and the same warning follows it below. Refusing it here instead
            // would leave the rule's Collaborate level live on a row the ledger records as ended.
            //
            // Conferral itself is ExternalParticipationService.ConfersAccessOn — the in-memory mirror of
            // the read filter's own predicate.
            // Task 140: a contact issuer's request may carry a date the row does NOT take (never shortened), so what the row
            // carries is the written date, else its own.
            var effectiveExpiry = contactIssuer is null
                ? ExternalGrantLifecycle.EffectiveExpiry(request.ExpiryDate, survivor.ExpiresDate, today)
                : expiryToWrite ?? survivor.ExpiresDate ?? ExternalGrantLifecycle.DefaultExpiry(today);
            var confersAccess = ExternalParticipationService.ConfersAccessOn(effectiveExpiry, today);

            GrantUpsertOutcome ConfersNoAccess()
            {
                logger.LogWarning(
                    "[EXT-GRANT] Grant {Key} elected record {AccessRecordId} whose expiry {Expiry} has " +
                    "PASSED — no active row on the key confers access — and the request supplied no new " +
                    "expiry. Refusing to report success over a grant that confers no access.",
                    key, survivor.Id, effectiveExpiry);

                return new GrantUpsertOutcome(
                    survivor.Id,
                    $"The existing grant expired on {effectiveExpiry:yyyy-MM-dd} and this request supplied no new "
                    + "expiry date, so it still confers no access. Re-send with an expiryDate to restore it.")
                {
                    GrantedLevel = check.GrantedLevel,
                    Narrowed = check.Narrowed,
                    GrantedExpiry = effectiveExpiry,
                    ExpiryNarrowed = expiryNarrowed,
                };
            }

            if (!confersAccess && request.ExpiryDate is null)
                return ConfersNoAccess();

            if (levelChanged || expiryChanged)
            {
                var update = new Dictionary<string, object?>();

                if (levelChanged)
                    update["sprk_accesslevel"] = requestedLevel;

                if (expiryChanged)
                    update["sprk_expiresdate"] = FormatDateOnly(expiryToWrite!.Value);

                // Task 140: a systemuser (or the Assigned-To rule) CHANGING a row a contact issued takes the row over —
                // the contact stamp is cleared, so the contact can no longer revoke a decision somebody else made (the
                // contact-side revoke is scoped to rows whose contact issuer is the caller). The systemuser is recorded
                // when it resolves; an audit field never blocks the write (the create path's rule). Session 27 round 50
                // item 2: the lookup's recorded provenance (sprk_grantedbycontactid) is cleared in the same write, and a row
                // whose issuing contact was DELETED (provenance only) is taken over the same way.
                if (contactIssuer is null && survivor.IsContactIssued)
                {
                    var takeOverBy = await ResolveGrantedBySystemUserIdAsync(dataverseClient, callerOid, logger, ct);
                    ExternalGrantLifecycle.AddInternalTakeOverBinds(
                        update, survivor, Guid.TryParse(takeOverBy, out var takeOverSystemUserId) ? takeOverSystemUserId : null);

                    logger.LogInformation(
                        "[EXT-GRANT] Row {AccessRecordId} was issued by contact {IssuerContactId}; this change makes it " +
                        "the internal user's ({SystemUserId}) — the contact can no longer revoke it.",
                        survivor.Id, survivor.GrantedByContactId?.ToString() ?? $"{survivor.GrantedByContactProvenance} (deleted)",
                        takeOverBy ?? "(unresolved)");
                }

                if (contactIssuer is null)
                {
                    await dataverseClient.UpdateAsync(EntitySet, survivor.Id, update, ct);
                }
                else
                {
                    // Session 27 round 42 item 1: the contact's write is CONDITIONAL on the version CheckGrantAsync read when it
                    // decided "every row on this key is mine" (step 3a). An internal user who took the row over after that read
                    // (round 34 item 3) makes this write fail instead of lengthening or raising a decision they just made.
                    try
                    {
                        await dataverseClient.UpdateIfMatchAsync(EntitySet, survivor.Id, update, survivor.ETag ?? string.Empty, ct);
                    }
                    catch (System.Data.DBConcurrencyException)
                    {
                        return await ConcurrentChangeRefusalAsync(dataverseClient, survivor, key, contactIssuer, logger, ct);
                    }
                }

                logger.LogInformation(
                    "[EXT-GRANT] Grant {Key} updated in place on record {AccessRecordId}: " +
                    "level {OldLevel} → {NewLevel}, expiry {OldExpiry} → {NewExpiry}.",
                    key, survivor.Id,
                    survivor.AccessLevel, levelChanged ? requestedLevel : survivor.AccessLevel,
                    survivor.ExpiresDate, expiryChanged ? expiryToWrite : survivor.ExpiresDate);
            }
            else
            {
                logger.LogInformation(
                    "[EXT-GRANT] Grant {Key} already active at level {Level} with expiry {Expiry} — " +
                    "no-op (idempotent).", key, requestedLevel, survivor.ExpiresDate);
            }

            // The explicit-date case the check above leaves to the write (see "What this ordering does NOT carry"):
            // the date the caller supplied is now on the row, and it confers nothing. Returned before the collapse
            // and the cache invalidation, as it always was — the Assigned-To caller invalidates on this outcome.
            if (!confersAccess)
                return ConfersNoAccess();

            await CollapseDuplicatesAsync(dataverseClient, existing, survivor.Id, key, conditional: contactIssuer is not null, logger, ct);
            await InvalidateGranteeCacheAsync(key, participations);

            return new GrantUpsertOutcome(survivor.Id, null)
            {
                GrantedLevel = check.GrantedLevel,
                Narrowed = check.Narrowed,
                GrantedExpiry = effectiveExpiry,
                ExpiryNarrowed = expiryNarrowed,
            };
        }

        // sprk_grantedby is a systemuser lookup — its target is a Dataverse systemuserid, which is
        // DISTINCT from the caller's Azure AD object id (oid). Resolve the systemuserid from the oid;
        // if the caller has no matching systemuser, omit grantedby (an audit field must never 400 the grant).
        //
        // Task 140: a CONTACT issuer is stamped on its own, contact-typed lookup instead — sprk_grantedby is a systemuser
        // lookup and stays EMPTY on a contact-issued grant (the caller's oid is a CIAM or contact-only identity, which no
        // systemuser carries; resolving it would only ever cost a query and find nothing).
        var grantedBySystemUserId = contactIssuer is null
            ? await ResolveGrantedBySystemUserIdAsync(dataverseClient, callerOid, logger, ct)
            : null;

        // FR-33 (task 097): a new grant is never written unbounded — an absent expiry becomes
        // today + DefaultExpiryDays.
        var createRequest = request.ExpiryDate is null
            ? request with { ExpiryDate = ExternalGrantLifecycle.DefaultExpiry(today) }
            : request;
        var payload = BuildGrantPayload(
            createRequest, rootType, rootId, grantedBySystemUserId, today, contactIssuer?.ContactId);
        var accessRecordId = await dataverseClient.CreateAsync(EntitySet, payload, ct);

        logger.LogInformation(
            "[EXT-GRANT] Created access record {AccessRecordId} for Contact {ContactId} / {RootType} {RootId}",
            accessRecordId, request.ContactId, rootType, rootId);

        // Race window: two concurrent grants on the same key both see zero rows and both create. Re-query
        // and collapse so the pair converges on one row. Both racers compute the SAME total order over the
        // rows they see (ConferralRank depends only on the row and today), so they elect the same survivor
        // and cannot deactivate each other.
        //
        // ⚠️ This comment previously said "the same survivor (lowest id)", which the task-106 election
        // superseded, and it was left five lines from its own replacement — review finding F6, the exact
        // docs-vs-reality drift this task's notes were congratulating themselves on repairing elsewhere.
        try
        {
            var afterCreate = await ExternalGrantLifecycle.QueryActiveRowsAsync(dataverseClient, key, ct);

            // Task 140: a contact issuer converges ONLY over its own rows. A row another issuer raced in beside it is
            // never deactivated by a contact's write (no proxy revocation); the mixed pair is left for the next grant
            // or revoke on the key, which sweep by key.
            if (contactIssuer is { } racingIssuer)
                afterCreate = afterCreate.Where(r => r.GrantedByContactId == racingIssuer.ContactId).ToList();

            if (afterCreate.Count > 1)
            {
                // Same election rule as the match path (task 106), and for the same reason: the rank is
                // request-independent, so both racers agree on the survivor.
                //
                // ⚠️ An earlier comment here claimed this path was "provably identical to the pre-106
                // `afterCreate[0]`" because "every row is a fresh create carrying the same
                // requested-or-default expiry". Review finding F1: that premise is FALSE on two counts —
                // racers differing in whether they send a date create rows with different expiries, and
                // even two date-less racers straddling a UTC midnight get DefaultExpiry values a day
                // apart. This path needed no pre-existing duplicates to hit the mutual-deactivation bug.
                var survivor = ExternalGrantLifecycle.ElectSurvivor(afterCreate, today);
                await CollapseDuplicatesAsync(dataverseClient, afterCreate, survivor.Id, key, conditional: contactIssuer is not null, logger, ct);
                accessRecordId = survivor.Id;
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: the grant itself succeeded. A surviving duplicate is swept at the next grant or
            // revoke on this key, both of which sweep by key rather than by id.
            logger.LogWarning(ex,
                "[EXT-GRANT] Post-create duplicate check failed for {Key}. The grant succeeded; any " +
                "duplicate will be collapsed by the next grant or revoke on this key.", key);
        }

        await InvalidateGranteeCacheAsync(key, participations);

        return new GrantUpsertOutcome(accessRecordId, null)
        {
            GrantedLevel = check.GrantedLevel,
            Narrowed = check.Narrowed,
            GrantedExpiry = createRequest.ExpiryDate,
            ExpiryNarrowed = expiryNarrowed,
        };
    }

    // =========================================================================
    // Write-time checks (tasks 138 + 139) — shared by the core and /invite-and-grant's pre-onboarding check
    // =========================================================================

    /// <summary>
    /// The outcome of <see cref="CheckGrantAsync"/>: a refusal, or the level to write and the key's existing rows.
    /// </summary>
    /// <param name="Refusal">Non-null when the grant must not be written.</param>
    /// <param name="GrantedLevel">The requested level capped at the ceiling — what will be written.</param>
    /// <param name="Narrowed">The ceiling lowered the request.</param>
    /// <param name="ExistingRows">The key's ACTIVE rows (empty for a contact that does not exist yet).</param>
    internal sealed record GrantCheck(
        GrantPolicyDecision? Refusal,
        ExternalAccessLevel GrantedLevel,
        bool Narrowed,
        IReadOnlyList<ExternalGrantRow> ExistingRows)
    {
        public static GrantCheck Refused(GrantPolicyDecision refusal, ExternalAccessLevel requested)
            => new(refusal, requested, false, Array.Empty<ExternalGrantRow>());
    }

    /// <summary>
    /// Every write-time check a grant must pass, in order, with NO side effect (task 138's policy; task 139's
    /// ceiling, never-lower and No Access). Called by <see cref="CreateGrantAsync"/> for every write, and by
    /// <c>/invite-and-grant</c> BEFORE onboarding so a refused request leaves no Contact or CIAM account behind.
    /// </summary>
    /// <remarks>
    /// <para><b>Order.</b> (1) The record's access policy (task 138) — first, so a caller learns nothing more about a
    /// record whose policy refuses this grantee kind. (2) The ceiling: none → 403; otherwise the request is capped
    /// (NARROW, not refuse — owner Q1). (3) Never-lower: when the cap narrowed the request and an active row on the
    /// key holds a HIGHER level, refuse 409 — an explicit request for a lower level (not narrowed) is a deliberate
    /// downgrade by a Write-holder and is allowed, as before. (4) The No Access list, through the read path's own
    /// veto code (<see cref="IAccessibleRecordSetService.CheckGranteeNoAccessAsync"/>): an entry refuses 422
    /// (<see cref="GrantPolicyDecision.GranteeDenied"/>); a check that could not be completed refuses 503
    /// (<see cref="GrantPolicyDecision.GranteeDenyListUnreadable"/>, task 142 r4) — a fault, never an entry.</para>
    /// <para>The existing-row read propagates its exception, exactly as the upsert's own read always did — a failed
    /// pre-existence read must never be mistaken for "no rows".</para>
    /// </remarks>
    internal static async Task<GrantCheck> CheckGrantAsync(
        GrantGrantee grantee,
        ExternalAccessLevel requestedLevel,
        ExternalGrantRootType rootType,
        Guid rootId,
        GrantCeiling ceiling,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        ILogger logger,
        CancellationToken ct,
        ContactGrantIssuer? contactIssuer = null)
    {
        ArgumentNullException.ThrowIfNull(ceiling);

        // (1) Task 138 — the record's access policy.
        var policy = await ExternalGrantLifecycle.EvaluateGrantPolicyAsync(
            participations, rootType, rootId, grantee.Kind, logger, ct);
        if (!policy.IsAllowed)
            return GrantCheck.Refused(policy, requestedLevel);

        // (2) Task 139 — the grantor ceiling.
        if (ceiling.Level is not { } ceilingLevel)
        {
            logger.LogWarning(
                "[EXT-GRANT] Ceiling {Ceiling} allows granting nothing on {RootType} {RootId}; nothing was granted.",
                ceiling, rootType, rootId);
            return GrantCheck.Refused(GrantPolicyDecision.CallerCannotGrant, requestedLevel);
        }

        var granted = ExternalGrantLifecycle.CapAt(requestedLevel, ceilingLevel);
        var narrowed = granted != requestedLevel;
        if (narrowed)
        {
            logger.LogInformation(
                "[EXT-GRANT] Request for {Requested} on {RootType} {RootId} narrowed to {Granted} by ceiling {Ceiling}.",
                requestedLevel, rootType, rootId, granted, ceiling);
        }

        // (3) Never silently lower an existing higher grant.
        IReadOnlyList<ExternalGrantRow> existing = grantee.Key is { } key
            ? await ExternalGrantLifecycle.QueryActiveRowsAsync(dataverseClient, key, ct)
            : Array.Empty<ExternalGrantRow>();

        // (3a) Task 140 — a CONTACT issuer never touches access somebody else gave (no proxy revocation or extension):
        // any active row on the key that the caller did not issue — a systemuser's, the Assigned-To rule's, another
        // contact's, or a legacy row with no issuer — refuses, and nothing is changed. On the caller's OWN row any LOWER
        // request refuses, narrowed or not (owner: "it never lowers the level").
        if (contactIssuer is { } issuer)
        {
            if (existing.Any(row => row.GrantedByContactId != issuer.ContactId))
            {
                logger.LogWarning(
                    "[EXT-GRANT] Refused: {Key} already holds access issued by someone other than contact {IssuerContactId}; " +
                    "a contact grant never changes a row somebody else issued.",
                    grantee.Key, issuer.ContactId);
                return GrantCheck.Refused(GrantPolicyDecision.ManagedElsewhere, requestedLevel);
            }

            if (existing.Any(row => (row.AccessLevel ?? 0) > (int)granted))
            {
                logger.LogWarning(
                    "[EXT-GRANT] Refused: contact {IssuerContactId}'s own grant on {Key} is above {Granted}; a contact " +
                    "grant never lowers it.", issuer.ContactId, grantee.Key, granted);
                return GrantCheck.Refused(GrantPolicyDecision.WouldLowerExisting(granted), requestedLevel);
            }
        }
        else if (narrowed && existing.Any(row => (row.AccessLevel ?? 0) > (int)granted))
        {
            logger.LogWarning(
                "[EXT-GRANT] Refused: {Key} already holds a level above {Granted}, and the request was narrowed to it " +
                "(asked {Requested}, ceiling {Ceiling}). Writing would lower someone else's grant.",
                grantee.Key, granted, requestedLevel, ceiling);
            return GrantCheck.Refused(GrantPolicyDecision.WouldLowerExisting(granted), requestedLevel);
        }

        // (4) FR-23 at write time — the grantee must not be on this record's No Access list. A TRI-STATE answer (task 142
        // r4 · owner round 13 item 4): an entry is the record's policy (422 grantee_denied); a check that could not be
        // completed is a FAULT (503 no_access_unverifiable), reported as one and never absorbed into "denied". Both
        // refuse — only Allowed grants.
        NoAccessCheckAnswer noAccess;
        try
        {
            noAccess = await accessibleRecords.CheckGranteeNoAccessAsync(
                ExternalGrantRoot.LogicalNameFor(rootType), rootId, grantee.ContactId, grantee.OrganizationIds, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The check's contract is "never throws but for the caller's cancellation"; a throw anyway is the same fault.
            logger.LogError(ex,
                "[EXT-GRANT] DENY-LIST-UNREADABLE: the No Access check for {RootType} {RootId} threw; refusing (fail " +
                "closed), reported as a fault.", rootType, rootId);
            return GrantCheck.Refused(GrantPolicyDecision.GranteeDenyListUnreadable, requestedLevel);
        }

        switch (noAccess)
        {
            case NoAccessCheckAnswer.Allowed:
                return new GrantCheck(null, granted, narrowed, existing);

            case NoAccessCheckAnswer.Denied:
                return GrantCheck.Refused(GrantPolicyDecision.GranteeDenied, requestedLevel);

            default:
                // Unverifiable — or an answer this code does not know, which is never "allowed" (fail closed).
                logger.LogError(
                    "[EXT-GRANT] DENY-LIST-UNREADABLE: the No Access check for {RootType} {RootId} could not be completed " +
                    "({Answer}); refusing (fail closed), reported as a fault, not as an entry on the list.",
                    rootType, rootId, noAccess);
                return GrantCheck.Refused(GrantPolicyDecision.GranteeDenyListUnreadable, requestedLevel);
        }
    }

    /// <summary>
    /// Result of the grant upsert: the surviving row id, plus a warning when the grant does not currently
    /// confer access (with no expiry in the request, nothing was written — task 113).
    /// </summary>
    /// <remarks>
    /// Added by task 023 (finding H1 / ADR-003). The method previously returned a bare <c>Guid</c>, so the
    /// one case where a caller most needs to be told something — "this matched an EXPIRED row and you
    /// supplied no new expiry, so the grantee still has nothing" — was indistinguishable from success.
    /// </remarks>
    internal sealed record GrantUpsertOutcome(Guid AccessRecordId, string? Warning, GrantPolicyDecision? Refusal = null)
    {
        /// <summary>The level written — the requested one capped at the grantor's ceiling (task 139). Null on a refusal.</summary>
        public ExternalAccessLevel? GrantedLevel { get; init; }

        /// <summary>The grantor's ceiling lowered the request (task 139).</summary>
        public bool Narrowed { get; init; }

        /// <summary>
        /// The expiry the surviving row carries after the write (task 140 reports it to a contact grantor). Null on a
        /// refusal.
        /// </summary>
        public DateOnly? GrantedExpiry { get; init; }

        /// <summary>
        /// A contact issuer's requested expiry (or the +90 default) was cut back to the grantor's own (task 140, owner
        /// G2 (i)). Always false outside the contact-issuer mode.
        /// </summary>
        public bool ExpiryNarrowed { get; init; }

        /// <summary>
        /// Task 140 (session 27 round 42 item 1): the contact-issuer write was refused because the row changed after the
        /// "every row on this key is mine" check — this is the row as RE-READ after the refused write (<c>null</c> when the
        /// re-read failed or the row is gone). Set only on that refusal.
        /// </summary>
        public ExternalGrantRow? ChangedRowNow { get; init; }

        /// <summary>The contact-issuer write was refused because the row changed after the issuer check (round 42 item 1).</summary>
        public bool ChangedConcurrently { get; init; }

        /// <summary>
        /// The record's access policy refused the grant (task 138): nothing was queried or written, and there is
        /// no row id. A typed value, never an exception — see <see cref="CreateGrantAsync"/>.
        /// </summary>
        public static GrantUpsertOutcome Refused(GrantPolicyDecision refusal) => new(Guid.Empty, null, refusal);
    }

    /// <summary>
    /// The contact-issuer mode's answer when its conditional write found the row CHANGED since CheckGrantAsync read it
    /// (HTTP 412; session 27 round 42 item 1): 409 managed_elsewhere with the existing copy. The row is re-read for the
    /// response — the record of what it now is — and is NOT written again (no blind retry). Re-applying the contact's
    /// request over a row an internal user just took over is exactly the proxy extension managed_elsewhere refuses.
    /// </summary>
    private static async Task<GrantUpsertOutcome> ConcurrentChangeRefusalAsync(
        DataverseWebApiClient dataverseClient,
        ExternalGrantRow survivor,
        ExternalGrantKey key,
        ContactGrantIssuer contactIssuer,
        ILogger logger,
        CancellationToken ct)
    {
        logger.LogWarning(
            "[EXT-GRANT] Refused: {Key} row {AccessRecordId} changed after contact {IssuerContactId} checked it was theirs " +
            "(If-Match {ETag} failed); nothing was written and the write is not retried.",
            key, survivor.Id, contactIssuer.ContactId, survivor.ETag);

        var now = await ExternalGrantLifecycle.ReReadChangedRowsAsync(
            dataverseClient, new[] { survivor.Id }, contactIssuer.ContactId, logger, ct);

        return GrantUpsertOutcome.Refused(GrantPolicyDecision.ManagedElsewhere) with
        {
            ChangedConcurrently = true,
            ChangedRowNow = now[0],
        };
    }

    /// <summary>
    /// The ProblemDetails for a write-time policy refusal (task 138), shared by <c>/grant</c>,
    /// <c>/invite-and-grant</c> and <c>/invite</c>: 422 (record_restricted / org_grant_direct_only_record /
    /// grantee_denied), 503 (policy_unreadable, or no_access_unverifiable — the No Access check could not be completed,
    /// task 142 r4), 403 (caller_cannot_grant) or 409 (would_lower_existing) — grantee_denied, 403 and 409 added by
    /// task 139 — each with its stable <c>reasonCode</c>, a human-readable <c>detail</c> the Manage Access dialog shows
    /// verbatim, and the <c>traceId</c>.
    /// </summary>
    internal static IResult PolicyRefusalProblem(
        GrantPolicyDecision refusal, HttpContext httpContext, IDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = httpContext.TraceIdentifier,
            ["reasonCode"] = refusal.ReasonCode,
        };
        if (extra is not null)
        {
            foreach (var (k, v) in extra)
                extensions[k] = v;
        }

        return Results.Problem(
            statusCode: refusal.StatusCode,
            title: refusal.ReasonCode == ExternalGrantLifecycle.ContactGrantManagedElsewhereReasonCode
                ? "Access is managed by someone else" // task 140: a 409 that is not about a higher level
                : refusal.StatusCode switch
            {
                StatusCodes.Status503ServiceUnavailable => "Access settings unavailable",
                StatusCodes.Status409Conflict => "Existing access is higher",
                StatusCodes.Status403Forbidden => "Access not granted",
                _ => "Grant not allowed on this record",
            },
            detail: refusal.Detail,
            extensions: extensions);
    }

    /// <summary>Stable reason code for a requested expiry before today (spec FR-33, task 097).</summary>
    internal const string ExpiryInPastReasonCode = "sdap.access.grant.expiry_in_past";

    /// <summary>
    /// Rejects a requested expiry BEFORE <paramref name="today"/>. Shared by <c>/grant</c> and
    /// <c>/invite-and-grant</c>, which both call it before writing — or, for the latter, before onboarding.
    /// </summary>
    /// <remarks>
    /// <para>Expiry = today is VALID — "access until 30 June means 30 June works" (task 007's Date Only
    /// rule; the read filter uses <c>ge</c>). There is no maximum: the owner removed the cap.</para>
    /// <para>An ABSENT expiry is not an error — <see cref="CreateGrantAsync"/> defaults it (owner decision
    /// 2026-09-10: no client sends one, so rejecting it would break every sharing surface).</para>
    /// </remarks>
    /// <returns>A 400 ProblemDetails carrying <see cref="ExpiryInPastReasonCode"/>, or <c>null</c> when the
    /// request's expiry is acceptable.</returns>
    internal static IResult? ValidateRequestedExpiry(DateOnly? requested, DateOnly today, HttpContext httpContext)
        => requested is { } expiry && expiry < today
            ? Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error",
                detail: $"ExpiryDate {expiry:yyyy-MM-dd} is in the past. Choose today ({today:yyyy-MM-dd}) or a " +
                        "later date, or omit it: an existing grant then keeps its current expiry, and a new or " +
                        $"unbounded grant gets today + {ExternalGrantLifecycle.DefaultExpiryDays} days.",
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = httpContext.TraceIdentifier,
                    ["reasonCode"] = ExpiryInPastReasonCode,
                })
            : null;

    /// <summary>
    /// Formats a <see cref="DateOnly"/> for a Dataverse <b>Date Only</b> column.
    /// </summary>
    /// <remarks>
    /// Both <c>sprk_granteddate</c> and <c>sprk_expiresdate</c> are Date Only in live metadata, and task
    /// 007 compares <c>sprk_expiresdate</c> against a bare <c>yyyy-MM-dd</c> literal. Writing a full
    /// round-trip timestamp into such a column was a recorded LOW finding.
    /// </remarks>
    private static string FormatDateOnly(DateOnly value) => value.ToString("yyyy-MM-dd");

    /// <summary>
    /// Derives the logical grant key this request targets: root × grantee, where the grantee is the
    /// Contact when one is named and the Organization otherwise.
    /// </summary>
    /// <remarks>
    /// An <c>OrganizationId</c> on a per-contact grant is the contact's firm — association metadata, not
    /// grantee identity. Treating it as identity would make a person grant and an org grant on the same
    /// root collide, so one could revoke the other.
    /// </remarks>
    internal static ExternalGrantKey ResolveGrantKey(
        GrantAccessRequest request, ExternalGrantRootType rootType, Guid rootId)
        => request.ContactId != Guid.Empty
            ? ExternalGrantKey.ForContact(rootType, rootId, request.ContactId)
            : ExternalGrantKey.ForOrganization(rootType, rootId, request.OrganizationId!.Value);

    /// <summary>
    /// Deactivates every active row for a logical grant except the elected survivor.
    /// </summary>
    /// <remarks>
    /// Non-fatal by design: the caller's grant has already been applied to the survivor, and a surviving
    /// duplicate is swept by the next grant or revoke on this key (both sweep by key, not by id). Failing
    /// the grant here would be worse — the caller's intent was satisfied.
    /// <para><b><paramref name="conditional"/> — the contact-issuer mode (task 140, session 27 round 42 item 1).</b> A
    /// contact collapses only rows it read as its own, so each deactivation is conditional on the version that read
    /// returned (<see cref="ExternalGrantLifecycle.DeactivateIfUnchangedAsync"/>): a duplicate an internal user took over in
    /// between is left exactly as it is — never ended by a contact's write.</para>
    /// </remarks>
    private static async Task CollapseDuplicatesAsync(
        DataverseWebApiClient dataverseClient,
        IReadOnlyList<ExternalGrantRow> rows,
        Guid survivorId,
        ExternalGrantKey key,
        bool conditional,
        ILogger logger,
        CancellationToken ct)
    {
        var duplicates = rows.Where(r => r.Id != survivorId).ToList();
        if (duplicates.Count == 0)
            return;

        logger.LogWarning(
            "[EXT-GRANT] {Count} duplicate active row(s) found for {Key}; collapsing onto {SurvivorId}. " +
            "Duplicates predate task 010's upsert or lost a create race.",
            duplicates.Count, key, survivorId);

        try
        {
            if (conditional)
                await ExternalGrantLifecycle.DeactivateIfUnchangedAsync(dataverseClient, duplicates, logger, ct);
            else
                await ExternalGrantLifecycle.DeactivateAsync(dataverseClient, duplicates.Select(r => r.Id), logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to collapse duplicates for {Key}. The grant itself succeeded; the " +
                "next grant or revoke on this key will sweep them.", key);
        }
    }

    /// <summary>
    /// Invalidates the grant cache of everyone the written grant reaches, through the ONE routine (task 137):
    /// the contact on a person grant, every ACTIVE member on an organization grant — under every tenant id a grant
    /// set can be cached under, so a CIAM grantee's entry is cleared too. Non-fatal by construction.
    /// </summary>
    /// <remarks>
    /// Keyed on the grant KEY the upsert wrote, not on the request: an OrganizationId beside a ContactId is the
    /// contact's firm (metadata), so only a key with no contact is an organization grant. The write has committed,
    /// so the clean-up runs to completion even if the caller disconnects.
    /// </remarks>
    private static Task InvalidateGranteeCacheAsync(ExternalGrantKey key, ExternalParticipationService participations)
        => participations.InvalidateGrantSetsAsync(
            key.ContactId is { } contactId ? new[] { contactId } : Array.Empty<Guid>(),
            key.IsOrganizationGrant && key.OrganizationId is { } organizationId ? new[] { organizationId } : Array.Empty<Guid>(),
            CancellationToken.None);

    /// <summary>
    /// Resolves the caller's Azure AD object id (<c>oid</c>) — the input to
    /// <see cref="ResolveGrantedBySystemUserIdAsync"/>, which maps it to the Dataverse systemuserid the
    /// audited <c>sprk_grantedby</c> lookup requires. NOTE: the oid is NOT itself a systemuserid.
    /// </summary>
    internal static string? ResolveCallerSystemUserId(HttpContext httpContext)
        => CallerResolution.ResolveObjectId(httpContext.User);

    /// <summary>
    /// Maps the caller's Azure AD object id (<paramref name="callerOid"/>) to their Dataverse
    /// <c>systemuserid</c> for the audited <c>sprk_grantedby</c> lookup. The systemuserid is DISTINCT
    /// from the AAD oid — binding the raw oid as a systemuserid fails Dataverse validation (400).
    /// Returns <c>null</c> when the oid is absent/unparseable or has no matching active systemuser, in
    /// which case <c>sprk_grantedby</c> is omitted (an audit field must never block the grant).
    /// </summary>
    internal static async Task<string?> ResolveGrantedBySystemUserIdAsync(
        DataverseWebApiClient dataverseClient, string? callerOid, ILogger logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callerOid) || !Guid.TryParse(callerOid, out _))
            return null;

        try
        {
            var rows = await dataverseClient.QueryAsync<SystemUserRow>(
                "systemusers",
                filter: $"azureactivedirectoryobjectid eq {callerOid}",
                select: "systemuserid",
                top: 1,
                cancellationToken: ct);

            return rows.Count > 0 ? rows[0].systemuserid?.ToString() : null;
        }
        catch (Exception ex)
        {
            // Non-fatal: grantedby is audit metadata. Log and omit rather than fail the grant.
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to resolve grantedby systemuser for oid {Oid} — omitting the audit field.",
                callerOid);
            return null;
        }
    }

    /// <summary>The <c>systemusers</c> projection of <see cref="ResolveGrantedBySystemUserIdAsync"/>. Internal so a test can
    /// answer that read at the <see cref="DataverseWebApiClient"/> seam (no HTTP double, ADR-038 B1).</summary>
    internal sealed class SystemUserRow
    {
        public Guid? systemuserid { get; set; }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Result of resolving the polymorphic grant root from a <see cref="GrantAccessRequest"/>.
    /// <c>Ok == false</c> carries a caller-safe validation <see cref="Error"/> (→ 400, no write).
    /// </summary>
    internal readonly record struct GrantRootResolution(bool Ok, ExternalGrantRootType Type, Guid Id, string? Error);

    /// <summary>
    /// Resolves the ONE grant root a request targets. Precedence: an explicit
    /// <c>RecordType</c> + <c>RecordId</c> wins; otherwise the legacy <c>ProjectId</c> shorthand maps to a
    /// project root. Fail-closed (NFR-08): an unknown <c>RecordType</c>, an explicit <c>RecordType</c> with
    /// an empty <c>RecordId</c>, or no root at all (incl. a bare <c>RecordId</c> without <c>RecordType</c>)
    /// returns <c>Ok == false</c> — the caller rejects 400 and writes NO row.
    /// </summary>
    internal static GrantRootResolution ResolveGrantRoot(GrantAccessRequest request)
    {
        // Explicit polymorphic root takes precedence over the legacy shorthand.
        if (!string.IsNullOrWhiteSpace(request.RecordType))
        {
            if (!ExternalGrantRoot.TryParse(request.RecordType, out var type))
                return new GrantRootResolution(false, default, Guid.Empty,
                    "RecordType must be one of: project, matter, workassignment.");

            var explicitId = request.RecordId ?? Guid.Empty;
            if (explicitId == Guid.Empty)
                return new GrantRootResolution(false, default, Guid.Empty,
                    "RecordId is required and must be a valid GUID when RecordType is specified.");

            return new GrantRootResolution(true, type, explicitId, null);
        }

        // Legacy shorthand: a bare ProjectId maps to the project root (back-compat until task 071).
        if (request.ProjectId != Guid.Empty)
            return new GrantRootResolution(true, ExternalGrantRootType.Project, request.ProjectId, null);

        // Fail-closed: no usable root (also covers RecordId supplied without RecordType).
        return new GrantRootResolution(false, default, Guid.Empty,
            "A grant root is required: provide recordType + recordId, or the legacy projectId.");
    }

    /// <summary>
    /// Resolves a root named ONLY by an explicit <c>recordType</c> + <c>recordId</c> — for the requests that carry no
    /// legacy <c>projectId</c> shorthand (task 098's record-wide expiry, task 063's system-user shares). Each such
    /// route's <see cref="DelegationRuleFilter"/> case and its handler both call this, so the record that is
    /// authorized is the record that is read or written.
    /// </summary>
    /// <remarks>
    /// Fail-closed: a missing or unknown type, or a missing id, returns <c>Ok == false</c>. There is no shorthand,
    /// deliberately — a request carrying two ways to name a record could authorize one and write another.
    /// </remarks>
    internal static GrantRootResolution ResolveExplicitRoot(string? recordType, Guid? recordId)
    {
        if (!ExternalGrantRoot.TryParse(recordType, out var type))
            return new GrantRootResolution(false, default, Guid.Empty,
                "RecordType is required and must be one of: project, matter, workassignment.");

        if (recordId is not { } id || id == Guid.Empty)
            return new GrantRootResolution(false, default, Guid.Empty,
                "RecordId is required and must be a valid GUID.");

        return new GrantRootResolution(true, type, id, null);
    }

    /// <summary>
    /// Builds the <c>sprk_externalrecordaccess</c> create payload. Internal (not private) so the test
    /// assembly (<c>InternalsVisibleTo("Sprk.Bff.Api.Tests")</c>) can assert the typed-lookup bind
    /// contract directly — a wrong <c>@odata.bind</c> key silently breaks the grant.
    /// </summary>
    /// <param name="today">
    /// The grant date for <c>sprk_granteddate</c> — the same "today" the expiry was computed from (task 097),
    /// so the two can never straddle a UTC midnight. Optional only for direct callers that build a payload
    /// outside a request; they get the wall-clock UTC date.
    /// </param>
    /// <param name="grantedByContactId">
    /// Task 140: the CONTACT who issued the grant through the contact-side routes, bound to <c>sprk_grantedbycontact</c>.
    /// Null for every other writer — a contact-issued grant never carries <c>sprk_grantedby</c> as well.
    /// </param>
    internal static object BuildGrantPayload(
        GrantAccessRequest request, ExternalGrantRootType rootType, Guid rootId, string? grantedBySystemUserId,
        DateOnly? today = null, Guid? grantedByContactId = null)
    {
        // Bind exactly ONE typed root lookup per record type (never two). Nav property is PascalCase
        // (sprk_Project / sprk_Matter / sprk_WorkAssignment), verified live — see ExternalGrantRoot.
        var (navigationProperty, entitySet) = ExternalGrantRoot.BindFor(rootType);

        // @odata.bind nav-property names are PascalCase (sprk_Contact / sprk_GrantedBy / sprk_Project…),
        // verified live against sprk_externalrecordaccess $metadata (task 070). The lowercase *id forms
        // teams-app-r1 used were wrong and 400'd every grant.
        var payload = new Dictionary<string, object?>
        {
            [$"{navigationProperty}@odata.bind"] = $"/{entitySet}({rootId})",
            ["sprk_accesslevel"] = (int)request.AccessLevel,
            // DATE ONLY column (live metadata) — a full timestamp was the recorded LOW finding; task 023.
            ["sprk_granteddate"] = FormatDateOnly(today ?? DateOnly.FromDateTime(DateTime.UtcNow))
        };

        // Grantee: bind the Contact for a per-contact grant; OMIT it for an ORGANIZATION grant (task 073
        // #7 — an empty ContactId + a bound sprk_Organization identifies the grantee, and every active
        // org member inherits at check time). A row with NO sprk_Contact is exactly how the read path
        // (AccessibleRecordSetService Term 3) distinguishes an org grant from a per-contact grant, so this
        // omission is load-bearing, not cosmetic.
        if (request.ContactId != Guid.Empty)
        {
            payload["sprk_Contact@odata.bind"] = $"/contacts({request.ContactId})";
        }

        // grantedBySystemUserId is already a resolved Dataverse systemuserid (see
        // ResolveGrantedBySystemUserIdAsync) — NOT the caller's raw AAD oid. Omitted when unresolved.
        if (!string.IsNullOrEmpty(grantedBySystemUserId) &&
            Guid.TryParse(grantedBySystemUserId, out var systemUserId))
        {
            payload["sprk_GrantedBy@odata.bind"] = $"/systemusers({systemUserId})";
        }

        // Task 140: the contact issuer, on its own contact-typed lookup (sprk_grantedby can only reference a systemuser) —
        // and, in the same write, its id as text (session 27 round 50 item 2): the lookup is emptied if the contact is ever
        // deleted (RemoveLink), the provenance is not, so the reconciliation job can still end the grant it outlived.
        if (grantedByContactId is { } issuerContactId && issuerContactId != Guid.Empty)
        {
            payload[$"{ExternalGrantLifecycle.GrantedByContactNavigationProperty}@odata.bind"] = $"/contacts({issuerContactId})";
            payload[ExternalGrantLifecycle.GrantedByContactIdAttribute] = ExternalGrantLifecycle.ContactIssuerProvenance(issuerContactId);
        }

        if (request.ExpiryDate.HasValue)
        {
            // Bug fix (task 070): the grant table's expiry field is sprk_expiresdate (verified live via
            // describe), NOT sprk_expirydate — the prior name would 400 any grant that carries an expiry.
            payload["sprk_expiresdate"] = FormatDateOnly(request.ExpiryDate.Value);
        }

        // Firm/org association (task 070, owner steer 2026-08-11): bind the grantee's sprk_organization —
        // NOT the OOB `account`. Nav property is PascalCase sprk_Organization (lookup added to
        // sprk_externalrecordaccess in this project); value uses the plural set /sprk_organizations({id}).
        if (request.OrganizationId.HasValue)
        {
            payload["sprk_Organization@odata.bind"] = $"/sprk_organizations({request.OrganizationId.Value})";
        }

        return payload;
    }
}
