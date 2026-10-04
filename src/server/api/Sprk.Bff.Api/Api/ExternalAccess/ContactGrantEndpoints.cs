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

            logger.LogWarning(
                "[EXT-CONTACT-GRANT] Refused: contact {GrantorContactId} → {GranteeContactId} on {RootType} {RootId}: {ReasonCode}.",
                g.ContactId, grantee, g.RootType, g.RootId, refusal.ReasonCode);
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
    /// Resolves the colleague: by contact id, or by email among ACTIVE contacts (the identity store's one email lookup,
    /// task 141) — then requires a CONFERRING membership in one of the grantor's organizations.
    /// </summary>
    /// <remarks>
    /// <para>An email that names no active contact, or a contact outside the grantor's organizations, is the same 422:
    /// a person who is not yet a colleague in this system is refused here (owner G1 (a)) — never created, never added to
    /// the organization, never onboarded.</para>
    /// <para>An email shared by MORE than one active contact names nobody (409 ambiguous) — the identity store reads two
    /// rows, so "more than one" is all it can know, and guessing would grant the wrong person.</para>
    /// <para>A read that could not be completed is 503, never "not a member".</para>
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
        var email = request.GranteeEmail?.Trim();
        ContactLookup lookup;
        try
        {
            lookup = request.GranteeContactId is { } contactId && contactId != Guid.Empty
                ? await identities.GetContactAsync(contactId, ct)
                : await identities.FindActiveContactsByEmailAsync(email!, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "[EXT-CONTACT-GRANT] The grantee lookup threw; refusing (fail closed).");
            lookup = ContactLookup.Failed;
        }

        if (lookup.Status != LookupStatus.Read)
        {
            return (Guid.Empty, string.Empty, ContactGrantorAuthorizationFilter.MembershipUnreadableDenial(httpContext,
                "Whether this person is a colleague in your organization could not be checked, so nothing was granted. " +
                "Try again in a moment."));
        }

        var active = lookup.Rows.Where(r => r.IsActive).ToList();
        var label = email ?? active.FirstOrDefault()?.Email ?? "This person";

        if (active.Count > 1)
        {
            return (Guid.Empty, label, ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status409Conflict,
                "More than one person matches", GranteeAmbiguousReasonCode,
                $"More than one person in this system uses {label}, so it does not identify one colleague. Nothing was " +
                "granted; ask the record's team to grant access."));
        }

        if (active.Count == 0)
            return (Guid.Empty, label, NotInOrganization(httpContext, label));

        var grantee = active[0].ContactId;
        label = active[0].Email ?? label;

        if (grantee == grantor.ContactId)
        {
            return (Guid.Empty, label, ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status400BadRequest,
                "Validation Error", SelfGrantReasonCode, "You cannot grant access to yourself."));
        }

        var memberships = await ContactGrantorAuthorizationFilter.ReadMembershipsAsync(participations, grantee, logger, ct);
        if (memberships.Unreadable)
        {
            return (Guid.Empty, label, ContactGrantorAuthorizationFilter.MembershipUnreadableDenial(httpContext,
                $"Whether {label} is a colleague in your organization could not be checked, so nothing was granted. Try " +
                "again in a moment."));
        }

        if (!memberships.ConferringOrganizationIds.Intersect(grantor.OrganizationIds).Any())
        {
            logger.LogWarning(
                "[EXT-CONTACT-GRANT] Refused: contact {GranteeContactId} shares no active organization with grantor {GrantorContactId}.",
                grantee, grantor.ContactId);
            return (Guid.Empty, label, NotInOrganization(httpContext, label));
        }

        return (grantee, label, null);
    }

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
    /// <para><b>Which rows count</b> — exactly the rows the read path lets confer (<c>ExternalParticipationService</c>):
    /// the grantor's own active, unexpired rows on the record; and, unless the record is Secure or Limited (direct grants
    /// only, FR-22), the active, unexpired organization-wide rows of the grantor's CONFERRING organizations. A row
    /// counts only at <paramref name="grantedLevel"/> or above. A direct row carrying a firm organization confers only
    /// while that organization is active (ISS-026), so such a row is checked; a firm that cannot be read is a fault.</para>
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
        bool directOnly;
        var rows = new List<ExternalGrantRow>();
        try
        {
            var flags = await participations.GetRootRecordFlagsAsync(logicalName, new[] { grantor.RootId }, ct);
            // Unknown or unreadable flags: direct grants only (the fail-closed reading — fewer sources, so an earlier cap).
            directOnly = !flags.TryGetValue(grantor.RootId, out var f) || f.IsUnreadable || f.IsDirectOnly;

            rows.AddRange(await ExternalGrantLifecycle.QueryActiveRowsAsync(
                dataverseClient, ExternalGrantKey.ForContact(grantor.RootType, grantor.RootId, grantor.ContactId), ct));

            if (!directOnly)
            {
                foreach (var organizationId in grantor.OrganizationIds)
                {
                    rows.AddRange(await ExternalGrantLifecycle.QueryActiveRowsAsync(
                        dataverseClient, ExternalGrantKey.ForOrganization(grantor.RootType, grantor.RootId, organizationId), ct));
                }
            }

            // ISS-026: a direct row naming a firm confers only while the firm is active. A firm that IS one of the grantor's
            // conferring organizations is active by construction; any other is read.
            var qualifying = new List<ExternalGrantRow>();
            foreach (var row in rows.Where(r => (r.AccessLevel ?? 0) >= (int)grantedLevel
                                                && ExternalParticipationService.ConfersAccessOn(r.ExpiresDate, today)))
            {
                if (row.ContactId is not null && row.OrganizationId is { } firm && firm != Guid.Empty
                    && !grantor.OrganizationIds.Contains(firm))
                {
                    OrganizationStateRow? organization;
                    try
                    {
                        organization = await dataverseClient.RetrieveAsync<OrganizationStateRow>(
                            "sprk_organizations", firm, "statecode", ct);
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        organization = null; // a deleted firm confers nothing — the row does not count
                    }

                    if (organization is null || organization.StateCode is not (null or 0))
                        continue;
                }

                qualifying.Add(row);
            }

            if (qualifying.Count > 0)
                return new GrantorExpiryCap(GrantorExpiryCapKind.Cap, qualifying.Max(r => r.ExpiresDate!.Value));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-CONTACT-GRANT] The grantor's own grants on {RootType} {RootId} could not be read; refusing (fail closed).",
                grantor.RootType, grantor.RootId);
            return new GrantorExpiryCap(GrantorExpiryCapKind.Fault, null);
        }

        if (!directOnly && grantor.Principal.UndatedAccessTermEntityTypes.Contains(logicalName))
            return new GrantorExpiryCap(GrantorExpiryCapKind.NoCap, null);

        logger.LogWarning(
            "[EXT-CONTACT-GRANT] Contact {ContactId} holds {Level} on {RootType} {RootId} per the principal, but no dated grant " +
            "at {Granted} or above confers it now and no undated term applies; treating the level as not held.",
            grantor.ContactId, grantor.Level, grantor.RootType, grantor.RootId, grantedLevel);
        return new GrantorExpiryCap(GrantorExpiryCapKind.Refuse, null);
    }

    private sealed class OrganizationStateRow
    {
        [JsonPropertyName("statecode")]
        public int? StateCode { get; set; }
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

        var mine = activeRows.Where(r => r.GrantedByContactId == g.ContactId).Select(r => r.Id).ToList();
        var othersRemain = activeRows.Any(r => r.GrantedByContactId != g.ContactId);

        int deactivated;
        try
        {
            deactivated = await ExternalGrantLifecycle.DeactivateAsync(dataverseClient, mine, logger, ct);
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
        if (key.ContactId is { } colleague)
            await participations.InvalidateGrantSetsAsync(new[] { colleague }, Array.Empty<Guid>(), CancellationToken.None);

        logger.LogInformation(
            "[EXT-CONTACT-GRANT] Contact {GrantorContactId} revoked grant {Key} ({AccessRecordId}): {Count} of their row(s) " +
            "deactivated; access issued by others remains: {OthersRemain}.",
            g.ContactId, key, request.AccessRecordId, deactivated, othersRemain);

        return TypedResults.Ok(new ContactGrantRevokeResponse(request.AccessRecordId, deactivated, othersRemain));
    }

    private static IResult Validation(HttpContext httpContext, string detail)
        => ContactGrantorAuthorizationFilter.Problem(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
            "sdap.access.contact_grant.invalid_request", detail);
}
