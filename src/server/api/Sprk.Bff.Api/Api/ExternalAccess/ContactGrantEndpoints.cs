using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 140 (#1063) — contact-side Grant Access on the <c>/api/v1/external</c> group.
/// <list type="bullet">
/// <item><c>POST /contact-grants</c> — grant ONE colleague of the caller's own organization access to a record, at or
/// below the caller's own level.</item>
/// <item><c>GET /contact-grants?recordType=…&amp;recordId=…</c> — the grants the caller issued on that record.</item>
/// <item><c>POST /contact-grants/revoke</c> — revoke one grant the caller issued.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>Owner rules (session 27, binding).</b> C4: a contact holding Collaborate or Full Access may grant; View Only
/// may not. Q1 (settled round 3b): every grant is capped at the grantor's level. Q2: only contacts of the grantor's OWN
/// organization, never organization-wide. Restricted records admit no contact access; Secure and Limited admit direct
/// grants only. Round 3 decisions G1 (a) — a person who is not yet a contact of the grantor's organization is refused,
/// and no contact ever creates or alters an organization membership; G2 — an issued grant's expiry is capped at the
/// grantor's own when their level rests on a dated grant, issued rows do not cascade when the grantor loses access, and
/// a colleague's grant somebody else issued is never changed; G3 (a) — the grantor's EFFECTIVE post-veto level counts.</para>
/// <para><b>Placement (CLAUDE.md §10).</b> In the BFF, on the existing principal-agnostic <c>/api/v1/external</c> group
/// (ADR-028 A3: one ExternalCollaboration group, no second workforce entry point). The management group cannot host
/// these routes: its scheme is workforce-only and its <see cref="DelegationRuleFilter"/> asks Dataverse, OBO, a question a
/// contact can never answer. Every write goes through the ONE grant core
/// (<see cref="GrantExternalAccessEndpoint.CreateGrantAsync"/>, WP-1) with the contact's level as the REQUIRED ceiling.
/// The handlers inject only services the group's existing routes already resolve — no new registration.</para>
/// <para><b>Nothing here writes <c>sprk_contactorganization</c></b> (owner G1 (a) / escalation trigger 6), creates a
/// contact, or onboards a CIAM identity: the grantee must already be an active colleague.</para>
/// </remarks>
public static class ContactGrantEndpoints
{
    internal const string GranteeNotInOrganizationReasonCode = "sdap.access.contact_grant.grantee_not_in_organization";
    internal const string GranteeAmbiguousReasonCode = "sdap.access.contact_grant.grantee_ambiguous";
    internal const string SelfGrantReasonCode = "sdap.access.contact_grant.self_grant";
    internal const string GrantFailedReasonCode = "sdap.access.contact_grant.grant_failed";
    internal const string GrantorAccessUnreadableReasonCode = "sdap.access.contact_grant.grantor_access_unreadable";
    internal const string ListFailedReasonCode = "sdap.access.contact_grant.list_failed";

    /// <summary>Maps the three routes on the collaboration group, each behind <see cref="ContactGrantorAuthorizationFilter"/>.</summary>
    public static RouteGroupBuilder MapContactGrantEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/contact-grants", GrantAsync)
            .AddContactGrantorAuthorizationFilter()
            .WithName("GrantAccessAsContact")
            .WithSummary("A contact grants a colleague of their own organization access to a record")
            .WithDescription(
                "Grants ONE active colleague of the caller's own organization access to a project, matter or work " +
                "assignment, at or below the caller's own level (Collaborate or Full Access required). Never " +
                "organization-wide. The expiry defaults to today + 90 days and never exceeds the caller's own grant.")
            .Produces<ContactGrantResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/contact-grants", ListAsync)
            .AddContactGrantorAuthorizationFilter()
            .WithName("ListGrantsIssuedByContact")
            .WithSummary("The grants the calling contact issued on a record")
            .Produces<ContactIssuedGrantsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/contact-grants/revoke", RevokeAsync)
            .AddContactGrantorAuthorizationFilter()
            .WithName("RevokeGrantIssuedByContact")
            .WithSummary("The calling contact revokes a grant they issued")
            .Produces<ContactGrantRevokeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    // =========================================================================================
    // POST /contact-grants
    // =========================================================================================

    /// <summary>Handles <c>POST /api/v1/external/contact-grants</c>. Internal so the auth tests can drive it directly.</summary>
    internal static async Task<IResult> GrantAsync(
        ContactGrantRequest request,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        IContactIdentityStore identities,
        HttpContext httpContext,
        ILogger<ContactGrantorAuthorizationFilter> logger,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        // ── Re-validate the grantor (the filter ran these too; a handler does not trust a pipeline it cannot see) ──
        var (grantor, denial) = await ContactGrantorAuthorizationFilter.EvaluateGrantorAsync(
            httpContext, request.RecordType, request.RecordId, requireOrganization: true, participations, logger, ct);
        if (denial is not null)
            return denial;
        var g = grantor!;

        // ── Request validation ──
        if (!Enum.IsDefined(typeof(ExternalAccessLevel), request.AccessLevel))
            return Validation(httpContext,
                $"AccessLevel must be one of: {string.Join(", ", Enum.GetNames<ExternalAccessLevel>())}.");

        var today = ExternalGrantLifecycle.TodayUtc(timeProvider);
        if (GrantExternalAccessEndpoint.ValidateRequestedExpiry(request.ExpiryDate, today, httpContext) is { } expiryProblem)
            return expiryProblem;

        var byId = request.GranteeContactId is { } id && id != Guid.Empty;
        var byEmail = !string.IsNullOrWhiteSpace(request.GranteeEmail);
        if (byId == byEmail)
            return Validation(httpContext, "Name the colleague by granteeContactId or by granteeEmail — exactly one of them.");

        // ── The grantee: ONE active contact, resolved strictly within the grantor's organizations ──
        var (grantee, granteeLabel, granteeProblem) = await ResolveGranteeAsync(
            request, g, identities, participations, httpContext, logger, ct);
        if (granteeProblem is not null)
            return granteeProblem;

        // ── The written expiry is capped at the grantor's own (owner G2 (i)) ──
        var grantedLevel = ExternalGrantLifecycle.CapAt(request.AccessLevel, g.Level);
        var cap = await ResolveGrantorExpiryCapAsync(g, grantedLevel, today, dataverseClient, participations, logger, ct);
        switch (cap.Kind)
        {
            case GrantorExpiryCapKind.Fault:
                return ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status503ServiceUnavailable,
                    "Access not granted", GrantorAccessUnreadableReasonCode,
                    "Your own access to this record could not be read just now, so nothing was granted. Try again in a moment.");
            case GrantorExpiryCapKind.Refuse:
                return ContactGrantorAuthorizationFilter.LevelInsufficientDenial(httpContext);
        }

        // ── The ONE grant core: policy (138) → ceiling (139) → issuer + never-lower (140) → No Access (139/142) → upsert ──
        var coreRequest = new GrantAccessRequest(
            ContactId: grantee,
            ProjectId: Guid.Empty,
            AccessLevel: request.AccessLevel,
            ExpiryDate: request.ExpiryDate,
            OrganizationId: null,
            RecordType: request.RecordType,
            RecordId: g.RootId);
        var ceiling = GrantCeiling.FromContactGrantorRights(g.Rights);
        var issuer = new ContactGrantIssuer(g.ContactId, cap.Cap);

        GrantExternalAccessEndpoint.GrantUpsertOutcome outcome;
        try
        {
            outcome = await GrantExternalAccessEndpoint.CreateGrantAsync(
                coreRequest, g.RootType, g.RootId, today, ceiling, callerOid: null,
                dataverseClient, participations, accessibleRecords, logger, ct, issuer);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-CONTACT-GRANT] Grant by contact {GrantorContactId} to {GranteeContactId} on {RootType} {RootId} failed.",
                g.ContactId, grantee, g.RootType, g.RootId);
            return ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status500InternalServerError,
                "Access not granted", GrantFailedReasonCode,
                "The access could not be saved. Nothing may have been granted; refresh the list and try again.");
        }

        if (outcome.Refusal is { } refusal)
        {
            if (refusal.ReasonCode == ExternalGrantLifecycle.ContactGrantManagedElsewhereReasonCode)
            {
                refusal = refusal with
                {
                    Detail = $"{granteeLabel} already has access to this record that was granted by someone else; ask " +
                             "them or the record's team to change it. Nothing was changed.",
                };
            }

            // Round 42 item 1: a row that changed after the issuer check gets the same managed_elsewhere answer; what the
            // re-read found is recorded with it.
            logger.LogWarning(
                "[EXT-CONTACT-GRANT] Refused: contact {GrantorContactId} → {GranteeContactId} on {RootType} {RootId}: {ReasonCode} " +
                "(changed after the check: {ChangedConcurrently}; now issued by contact {NowIssuerContactId} / systemuser " +
                "{NowIssuerSystemUserId}).",
                g.ContactId, grantee, g.RootType, g.RootId, refusal.ReasonCode, outcome.ChangedConcurrently,
                outcome.ChangedRowNow?.GrantedByContactId, outcome.ChangedRowNow?.GrantedBySystemUserId);
            return GrantExternalAccessEndpoint.PolicyRefusalProblem(refusal, httpContext);
        }

        if (outcome.Warning is { } warning)
        {
            // Unreachable by construction (the contact-issuer mode always writes a date on or after today), kept so the
            // ADR-003 "never report success over a grant that confers nothing" rule holds on this route too.
            return ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status409Conflict,
                "Grant did not take effect", "sdap.grant.expired_not_restored", warning);
        }

        logger.LogInformation(
            "[EXT-CONTACT-GRANT] Grantor contact {GrantorContactId} granted contact {GranteeContactId} on {RootType} {RootId}: " +
            "requested {RequestedLevel}, granted {GrantedLevel}, narrowed {Narrowed}, expiry {Expiry} (narrowed {ExpiryNarrowed}, " +
            "grantor cap {Cap}). Record {AccessRecordId}.",
            g.ContactId, grantee, g.RootType, g.RootId, request.AccessLevel, outcome.GrantedLevel, outcome.Narrowed,
            outcome.GrantedExpiry, outcome.ExpiryNarrowed, cap.Cap?.ToString("yyyy-MM-dd") ?? "none", outcome.AccessRecordId);

        return TypedResults.Ok(new ContactGrantResponse(
            outcome.AccessRecordId, grantee, outcome.GrantedLevel, outcome.Narrowed, outcome.GrantedExpiry,
            outcome.ExpiryNarrowed));
    }

    /// <summary>
    /// Resolves the colleague strictly WITHIN the grantor's organizations (task 140 POML step 4): by contact id, or by
    /// email among the active members of those organizations — never by a system-wide lookup.
    /// </summary>
    /// <remarks>
    /// <para><b>Nothing about a non-colleague is ever disclosed</b> (owner Q2 scoping; the route is not a contact oracle —
    /// task 140 verifier r1, items 2, 3, 11 and 12):</para>
    /// <list type="bullet">
    /// <item><b>By contact id.</b> An id that names no contact, an inactive contact, and an active contact outside the
    /// grantor's organizations are ONE refusal (422 <c>grantee_not_in_organization</c>) worded with "This person" — the
    /// contact's stored email is never read into a message, so a known GUID reveals neither whether the contact exists
    /// nor its address. Only a VERIFIED colleague's email may label a later message (managed_elsewhere).</item>
    /// <item><b>By email.</b> The ONE scoped read (<see cref="ExternalParticipationService.FindConferringMembersByEmailAsync"/>)
    /// returns only conferring members of the grantor's organizations who use that email. Zero is 422 worded with the
    /// address the caller typed; MORE than one colleague is 409 ambiguous ("more than one person in your organization");
    /// a person outside those organizations is never counted, so a colleague is granted even when an outsider shares the
    /// address, and the answer never says such an outsider exists.</item>
    /// </list>
    /// <para>A person who is not yet a colleague is refused (owner G1 (a)) — never created, never added to an
    /// organization, never onboarded. A read that could not be completed is 503, never "not a member".</para>
    /// </remarks>
    private static async Task<(Guid Grantee, string Label, IResult? Problem)> ResolveGranteeAsync(
        ContactGrantRequest request,
        ContactGrantor grantor,
        IContactIdentityStore identities,
        ExternalParticipationService participations,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        return request.GranteeContactId is { } contactId && contactId != Guid.Empty
            ? await ResolveGranteeByIdAsync(contactId, grantor, identities, participations, httpContext, logger, ct)
            : await ResolveGranteeByEmailAsync(request.GranteeEmail!.Trim(), grantor, participations, httpContext, logger, ct);
    }

    /// <summary>The label every by-id refusal uses: the caller named a GUID, so nothing the store holds is echoed.</summary>
    internal const string UnnamedGranteeLabel = "This person";

    private static async Task<(Guid Grantee, string Label, IResult? Problem)> ResolveGranteeByIdAsync(
        Guid contactId,
        ContactGrantor grantor,
        IContactIdentityStore identities,
        ExternalParticipationService participations,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        if (contactId == grantor.ContactId)
            return (Guid.Empty, UnnamedGranteeLabel, SelfGrant(httpContext));

        ContactLookup lookup;
        try
        {
            lookup = await identities.GetContactAsync(contactId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "[EXT-CONTACT-GRANT] The grantee lookup threw; refusing (fail closed).");
            lookup = ContactLookup.Failed;
        }

        // The membership read runs for EVERY id — one naming no contact, an inactive contact, another organization's
        // contact or a colleague — so the work done, like the answer, does not tell the caller whether the id names a
        // contact (no existence oracle; task 140 verifier r1 item 4).
        var memberships = await ContactGrantorAuthorizationFilter.ReadMembershipsAsync(participations, contactId, logger, ct);

        if (lookup.Status != LookupStatus.Read || memberships.Unreadable)
            return (Guid.Empty, UnnamedGranteeLabel, ColleagueUnverifiable(httpContext, "this person"));

        var contact = lookup.Rows.FirstOrDefault(r => r.ContactId == contactId && r.IsActive);
        if (contact is null || !memberships.ConferringOrganizationIds.Intersect(grantor.OrganizationIds).Any())
        {
            logger.LogWarning(
                "[EXT-CONTACT-GRANT] Refused: grantee {GranteeContactId} named by grantor {GrantorContactId} is {Why}.",
                contactId, grantor.ContactId,
                contact is null ? "not an active contact" : "in no active organization the grantor confers through");
            return (Guid.Empty, UnnamedGranteeLabel, NotInOrganization(httpContext, UnnamedGranteeLabel));
        }

        // A VERIFIED colleague: their address may name them in a later message (managed_elsewhere).
        return (contactId, string.IsNullOrWhiteSpace(contact.Email) ? UnnamedGranteeLabel : contact.Email!, null);
    }

    private static async Task<(Guid Grantee, string Label, IResult? Problem)> ResolveGranteeByEmailAsync(
        string email,
        ContactGrantor grantor,
        ExternalParticipationService participations,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        ExternalParticipationService.ColleagueEmailMatch match;
        try
        {
            match = await participations.FindConferringMembersByEmailAsync(grantor.OrganizationIds, email, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "[EXT-CONTACT-GRANT] The colleague-by-email read threw; refusing (fail closed).");
            match = ExternalParticipationService.ColleagueEmailMatch.Failed;
        }

        if (match.Unreadable)
            return (Guid.Empty, email, ColleagueUnverifiable(httpContext, email));

        if (match.ContactIds.Count > 1)
        {
            return (Guid.Empty, email, ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status409Conflict,
                "More than one person matches", GranteeAmbiguousReasonCode,
                $"More than one person in your organization uses {email}, so it does not identify one colleague. Nothing " +
                "was granted; ask the record's team to grant access."));
        }

        if (match.ContactIds.Count == 0)
        {
            logger.LogWarning(
                "[EXT-CONTACT-GRANT] Refused: no active colleague of grantor {GrantorContactId} uses the email given.",
                grantor.ContactId);
            return (Guid.Empty, email, NotInOrganization(httpContext, email));
        }

        var grantee = match.ContactIds[0];
        if (grantee == grantor.ContactId)
            return (Guid.Empty, email, SelfGrant(httpContext));

        return (grantee, email, null);
    }

    private static IResult ColleagueUnverifiable(HttpContext httpContext, string who)
        => ContactGrantorAuthorizationFilter.MembershipUnreadableDenial(httpContext,
            $"Whether {who} is a colleague in your organization could not be checked, so nothing was granted. Try again " +
            "in a moment.");

    private static IResult SelfGrant(HttpContext httpContext)
        => ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status400BadRequest,
            "Validation Error", SelfGrantReasonCode, "You cannot grant access to yourself.");

    private static IResult NotInOrganization(HttpContext httpContext, string label)
        => ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status422UnprocessableEntity,
            "Not a colleague in your organization", GranteeNotInOrganizationReasonCode,
            $"{label} is not yet a member of your organization in this system; ask the record's team to add them. Nothing " +
            "was granted.");

    // =========================================================================================
    // The grantor's own expiry (owner G2 (i))
    // =========================================================================================

    internal enum GrantorExpiryCapKind
    {
        /// <summary>The grantor's qualifying access rests on dated grants: <see cref="GrantorExpiryCap.Cap"/> is the latest.</summary>
        Cap,

        /// <summary>The grantor's level rests on an undated term (standing grant, organization expansion): no cap.</summary>
        NoCap,

        /// <summary>No source of the grantor's level could be found live — they do not hold it (403).</summary>
        Refuse,

        /// <summary>A read failed (503).</summary>
        Fault,
    }

    internal readonly record struct GrantorExpiryCap(GrantorExpiryCapKind Kind, DateOnly? Cap);

    /// <summary>
    /// The latest date until which the grantor holds at least <paramref name="grantedLevel"/> through a DATED grant on
    /// the record — the cap on the expiry they may issue (owner decision G2 (i), session 27 round 3: a grantor cannot
    /// hand out time they do not hold).
    /// </summary>
    /// <remarks>
    /// <para><b>Which rows count</b> — <see cref="ExternalGrantLifecycle.ReadContactHeldGrantsAsync"/>, the ONE definition
    /// of a contact's own grant on a record (shared with the reconciliation job's contact-issued R1 rule, session 27 round
    /// 42 item 2): the grantor's own active rows on the record and, unless it is Secure or Limited, the organization-wide
    /// rows of their CONFERRING organizations, at <paramref name="grantedLevel"/> or above, with a direct row naming an
    /// inactive firm excluded (ISS-026; a firm that cannot be read is a fault). Here only rows that confer TODAY count —
    /// a dated, unexpired row.</para>
    /// <para><b>No qualifying dated row</b> means the grantor's level rests on an UNDATED term — standing-grant
    /// membership or organization expansion — which only a workforce contact's composition carries, and never on a
    /// direct-only record. Then there is nothing to cap at (NoCap). Anywhere else, the live rows contradict the
    /// principal's level (the grant set is cached up to 60 s), so the grantor is treated as not holding it (Refuse).</para>
    /// <para>Over-approximation, in the safe direction: when a dated row AND an undated term both confer, the dated
    /// row's date still caps — the grant may end earlier than strictly necessary, never later.</para>
    /// </remarks>
    internal static async Task<GrantorExpiryCap> ResolveGrantorExpiryCapAsync(
        ContactGrantor grantor,
        ExternalAccessLevel grantedLevel,
        DateOnly today,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        ILogger logger,
        CancellationToken ct)
    {
        var logicalName = ExternalGrantRoot.LogicalNameFor(grantor.RootType);
        ContactHeldGrants held;
        try
        {
            held = await ExternalGrantLifecycle.ReadContactHeldGrantsAsync(
                grantor.ContactId, grantor.OrganizationIds, grantor.RootType, grantor.RootId, grantedLevel,
                row => ExternalParticipationService.ConfersAccessOn(row.ExpiresDate, today),
                dataverseClient, participations, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-CONTACT-GRANT] The grantor's own grants on {RootType} {RootId} could not be read; refusing (fail closed).",
                grantor.RootType, grantor.RootId);
            return new GrantorExpiryCap(GrantorExpiryCapKind.Fault, null);
        }

        if (held.Rows.Count > 0)
            return new GrantorExpiryCap(GrantorExpiryCapKind.Cap, held.Rows.Max(r => r.ExpiresDate!.Value));

        if (!held.DirectOnly && grantor.Principal.UndatedAccessTermEntityTypes.Contains(logicalName))
            return new GrantorExpiryCap(GrantorExpiryCapKind.NoCap, null);

        logger.LogWarning(
            "[EXT-CONTACT-GRANT] Contact {ContactId} holds {Level} on {RootType} {RootId} per the principal, but no dated grant " +
            "at {Granted} or above confers it now and no undated term applies; treating the level as not held.",
            grantor.ContactId, grantor.Level, grantor.RootType, grantor.RootId, grantedLevel);
        return new GrantorExpiryCap(GrantorExpiryCapKind.Refuse, null);
    }

    // =========================================================================================
    // GET /contact-grants
    // =========================================================================================

    /// <summary>Handles <c>GET /api/v1/external/contact-grants</c>: the caller's own issued, active grants on one record.</summary>
    internal static async Task<IResult> ListAsync(
        [AsParameters] ContactGrantListQuery query,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        HttpContext httpContext,
        ILogger<ContactGrantorAuthorizationFilter> logger,
        CancellationToken ct)
    {
        var (grantor, denial) = await ContactGrantorAuthorizationFilter.EvaluateGrantorAsync(
            httpContext, query.RecordType, query.RecordId, requireOrganization: false, participations, logger, ct);
        if (denial is not null)
            return denial;
        var g = grantor!;

        List<ExternalGrantRow> rows;
        try
        {
            rows = await ExternalGrantLifecycle.QueryActiveRowsIssuedByContactAsync(
                dataverseClient, g.RootType, g.RootId, g.ContactId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "[EXT-CONTACT-GRANT] Listing grants issued by contact {ContactId} failed.", g.ContactId);
            return ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status503ServiceUnavailable,
                "Grants unavailable", ListFailedReasonCode,
                "The access you granted could not be listed just now. Try again in a moment.");
        }

        // Person grants only — a contact can never issue an organization-wide one, so an issued row naming no contact
        // is not one of theirs to show. The issuer is re-checked in memory: the filter decides what is returned.
        var mine = rows
            .Where(r => r.GrantedByContactId == g.ContactId && r.ContactId is { } c && c != Guid.Empty)
            .ToList();

        var names = await ReadContactNamesAsync(dataverseClient, mine.Select(r => r.ContactId!.Value).Distinct().ToList(), logger, ct);

        var grants = mine
            .Select(r => new ContactIssuedGrant(
                r.Id,
                r.ContactId!.Value,
                names.TryGetValue(r.ContactId!.Value, out var n) ? n.FullName : null,
                names.TryGetValue(r.ContactId!.Value, out var e) ? e.Email : null,
                r.AccessLevel is { } level && Enum.IsDefined(typeof(ExternalAccessLevel), level)
                    ? (ExternalAccessLevel)level
                    : null,
                r.ExpiresDate))
            .OrderBy(x => x.FullName ?? x.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return TypedResults.Ok(new ContactIssuedGrantsResponse(grants));
    }

    /// <summary>
    /// Display names for the listed grantees. Degrades to no names on a read failure (logged): the list is still the
    /// caller's own grants, and Revoke works by id.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, (string? FullName, string? Email)>> ReadContactNamesAsync(
        DataverseWebApiClient dataverseClient, IReadOnlyList<Guid> contactIds, ILogger logger, CancellationToken ct)
    {
        if (contactIds.Count == 0)
            return new Dictionary<Guid, (string?, string?)>();

        try
        {
            var filter = string.Join(" or ", contactIds.Select(id => $"contactid eq {id}"));
            var rows = await dataverseClient.QueryAsync<ContactNameRow>(
                "contacts", filter: filter, select: "contactid,fullname,emailaddress1", cancellationToken: ct);
            return rows
                .Where(r => r.ContactId is not null)
                .GroupBy(r => r.ContactId!.Value)
                .ToDictionary(grp => grp.Key, grp => (grp.First().FullName, grp.First().Email));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[EXT-CONTACT-GRANT] Grantee names could not be read; listing without them.");
            return new Dictionary<Guid, (string?, string?)>();
        }
    }

    private sealed class ContactNameRow
    {
        [JsonPropertyName("contactid")]
        public Guid? ContactId { get; set; }

        [JsonPropertyName("fullname")]
        public string? FullName { get; set; }

        [JsonPropertyName("emailaddress1")]
        public string? Email { get; set; }
    }

    // =========================================================================================
    // POST /contact-grants/revoke
    // =========================================================================================

    /// <summary>
    /// Handles <c>POST /api/v1/external/contact-grants/revoke</c>: deactivates the caller's OWN active rows on the named
    /// grant (root × colleague). A row somebody else issued on the same grant is never touched — the response says the
    /// colleague keeps that access.
    /// </summary>
    internal static async Task<IResult> RevokeAsync(
        ContactGrantRevokeRequest request,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        HttpContext httpContext,
        ILogger<ContactGrantorAuthorizationFilter> logger,
        CancellationToken ct)
    {
        var (grantor, row, denial) = await ContactGrantorAuthorizationFilter.EvaluateRevokerAsync(
            httpContext, request.AccessRecordId, dataverseClient, logger, ct);
        if (denial is not null)
            return denial;
        var g = grantor!;
        var key = ExternalGrantLifecycle.DeriveKey(row!)!.Value;

        List<ExternalGrantRow> activeRows;
        try
        {
            activeRows = await ExternalGrantLifecycle.QueryActiveRowsAsync(dataverseClient, key, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "[EXT-CONTACT-GRANT] Revoke {AccessRecordId}: the grant's rows could not be read.", request.AccessRecordId);
            return ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status503ServiceUnavailable,
                "Access not revoked", ContactGrantorAuthorizationFilter.RevokeFailedReasonCode,
                "The access could not be revoked just now. Nothing was changed; try again in a moment.");
        }

        // "Issued by me" is decided from THIS read, and each deactivation is conditional on the version this read returned
        // (session 27 round 42 item 1): an internal user who takes a row over after this read (round 34 item 3) makes the
        // write fail rather than be overwritten.
        var mine = activeRows.Where(r => r.GrantedByContactId == g.ContactId).ToList();
        var othersRemain = activeRows.Any(r => r.GrantedByContactId != g.ContactId);

        ConditionalDeactivation outcome;
        try
        {
            outcome = await ExternalGrantLifecycle.DeactivateIfUnchangedAsync(dataverseClient, mine, logger, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-CONTACT-GRANT] Revoke {AccessRecordId} by contact {ContactId} failed part-way ({Count} row(s) to deactivate).",
                request.AccessRecordId, g.ContactId, mine.Count);
            return ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status500InternalServerError,
                "Access not fully revoked", ContactGrantorAuthorizationFilter.RevokeFailedReasonCode,
                "The access could not be fully revoked; your colleague may still have it. Refresh the list and revoke " +
                "it again.");
        }

        // The grant cache of the colleague — the ONE invalidation routine (task 137): every tenant a grant set is cached
        // under. Non-fatal by construction; the write has committed.
        if (outcome.Deactivated.Count > 0 && key.ContactId is { } colleague)
            await participations.InvalidateGrantSetsAsync(new[] { colleague }, Array.Empty<Guid>(), CancellationToken.None);

        if (outcome.ChangedSinceRead.Count > 0)
        {
            // Round 42 item 1: a row changed between the "issued by me" read and the write — 409 managed_elsewhere, with the
            // existing copy; the row is re-read for the record of what it now is, and never written again (no blind retry).
            await ExternalGrantLifecycle.ReReadChangedRowsAsync(dataverseClient, outcome.ChangedSinceRead, g.ContactId, logger, ct);

            var refusal = outcome.Deactivated.Count == 0
                ? GrantPolicyDecision.ManagedElsewhere
                : GrantPolicyDecision.ManagedElsewhere with { Detail = PartlyManagedElsewhereDetail };
            return GrantExternalAccessEndpoint.PolicyRefusalProblem(refusal, httpContext);
        }

        logger.LogInformation(
            "[EXT-CONTACT-GRANT] Contact {GrantorContactId} revoked grant {Key} ({AccessRecordId}): {Count} of their row(s) " +
            "deactivated; access issued by others remains: {OthersRemain}.",
            g.ContactId, key, request.AccessRecordId, outcome.Deactivated.Count, othersRemain);

        return TypedResults.Ok(new ContactGrantRevokeResponse(request.AccessRecordId, outcome.Deactivated.Count, othersRemain));
    }

    /// <summary>
    /// The 409 managed_elsewhere detail when a revoke DID end some of the caller's rows on the grant before another of them
    /// turned out to have changed since the read (duplicate rows — rare, but "Nothing was changed" would then be untrue).
    /// </summary>
    internal const string PartlyManagedElsewhereDetail =
        "This person also has access to this record that someone else changed while you were revoking it; ask them or " +
        "the record's team to change it. The access you granted was ended.";

    private static IResult Validation(HttpContext httpContext, string detail)
        => ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
            "sdap.access.contact_grant.invalid_request", detail);
}
