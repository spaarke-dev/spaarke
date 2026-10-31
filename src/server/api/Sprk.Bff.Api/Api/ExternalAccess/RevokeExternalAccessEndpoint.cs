// Graph, Azure.Identity and HTTP-header usings were dropped by task 017: this endpoint no longer talks to
// Graph at all. Its forked SPE matcher (finding A-13) was deleted in favour of
// SpeContainerMembershipService, which owns that conversation.
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/revoke
///
/// Revokes one external grant (a contact's or an organization's, on one record) through the shared revoke
/// (<see cref="ExternalGrantRevocationService"/>):
///   1. Deactivating every active sprk_externalrecordaccess row of the logical grant (statecode=1, statuscode=2).
///   2. Removing the revoked grantee's SPE container permission(s) on the record's own container.
///   3. Invalidating the grantee's grant cache.
///   4. Marking an Assigned-To auto grant as removed, so the rule does not re-create it.
/// It assigns and removes no portal web role.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-009: Redis cache invalidation after revoke — ExternalParticipationService.InvalidateGrantSetsAsync (task 137).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class RevokeExternalAccessEndpoint
{
    private const string AccessEntitySet = "sprk_externalrecordaccesses";
    // Cache invalidation (task 137): ExternalParticipationService.InvalidateGrantSetsAsync owns the key, every
    // tenant it can be cached under, and the organization → members expansion. No private RemoveAsync copy here.

    /// <summary>
    /// M2 (task 024 → task 065). The reason code for the one incomplete-revoke shape this endpoint can
    /// report: the Dataverse grant WAS deactivated, but the SPE container permission could not be
    /// confirmed removed (<see cref="SpeContainerRevokeOutcome.Failed"/>). Reuses the
    /// <c>sdap.*.incomplete.*</c> FAMILY <see cref="ProjectClosureEndpoint"/> established for the
    /// identical failure shape, with its own leaf — a client switching on the code must be able to tell a
    /// single-grant revoke from a project closure, not just "something is incomplete".
    /// </summary>
    internal const string RevokeSpeCleanupIncompleteReason = "sdap.revoke.incomplete.container_not_cleared";

    /// <summary>
    /// #2103 follow-up. The NON-retryable leaf of the same <c>sdap.*.incomplete.*</c> family: an ORGANIZATION grant was
    /// revoked in Dataverse, but the organization has more active members than one revoke may sweep
    /// (<see cref="ExternalOrganizationMembership.MaxMembersPerSweep"/>), so no member's container permission was
    /// touched. A retry meets the same bound and changes nothing — the detail tells an administrator what to do instead.
    /// Still fail-closed (a 500, never a 200): members may retain file access.
    /// </summary>
    internal const string RevokeOrganizationTooLargeReason = "sdap.revoke.incomplete.organization_too_large";

    /// <summary>
    /// #2104 round 3. The NON-retryable leaf for a match the revoke refused to act on: more than one eligible permission
    /// on the container matches a revoked grantee, or the one match names no holder object id and so cannot be told
    /// apart from an internal user's (<see cref="SpeContainerMembershipService.IsAmbiguousPermissionMatch"/>). Nothing
    /// was removed for that grantee, and a retry reads the same permissions and refuses again — the detail tells an
    /// administrator to resolve the container's permissions in the SPE admin app. Still fail-closed (a 500).
    /// </summary>
    /// <remarks>
    /// <b>Precedence</b> (the rule <see cref="ProjectClosureEndpoint"/> applies to <c>organization_too_large</c> versus
    /// <c>container_not_cleared</c>): a non-retryable cause wins over a transient one, because a retry can clear only the
    /// transient part and the revoke still cannot complete. The detail then also counts the other members not confirmed
    /// removed. <see cref="RevokeOrganizationTooLargeReason"/> never meets this one: an oversized organization is not
    /// swept member by member, so no member's permission is matched.
    /// </remarks>
    internal const string RevokePermissionAmbiguousReason = "sdap.revoke.incomplete.permission_ambiguous";

    /// <summary>
    /// Slice C2b: the route's OpenAPI description, stating the rule the shared revoke
    /// (<see cref="ExternalGrantRevocationService"/>) applies. No portal web role is involved.
    /// </summary>
    internal const string RouteDescription =
        "Revokes one external grant (a contact's or an organization's, on one record): every active " +
        "sprk_externalrecordaccess row of the grant is deactivated, the revoked grantee's SPE container permissions on " +
        "the record's container are removed, the grantee's grant cache is invalidated, and an Assigned-To automatic " +
        "grant is marked removed so it is not created again. No portal web role is assigned or removed. A container " +
        "permission that could not be confirmed removed answers 500 (incomplete); the grant itself stays revoked.";

    /// <summary>
    /// Registers the revoke endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapRevokeExternalAccessEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/revoke", RevokeAccessAsync)
            .WithName("RevokeExternalAccess")
            .WithSummary("Revoke external access from a Contact for a Secure Project")
            .WithDescription(RouteDescription)
            .Produces<RevokeAccessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    /// <summary>
    /// Internal (not private) so the test assembly can exercise the PRODUCTION handler directly per
    /// <c>InternalsVisibleTo("Sprk.Bff.Api.Tests")</c> — the same convention used across this codebase,
    /// and no reflection into a private member (ADR-038 §7 ban B8). The revoke sweep's correctness is a
    /// privilege-retention question; it must be tested against the real handler, not a re-implementation.
    /// </summary>
    internal static async Task<IResult> RevokeAccessAsync(
        RevokeAccessRequest request,
        DataverseWebApiClient dataverseClient,
        SpeContainerMembershipService speContainerMembership,
        ExternalParticipationService participations,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        // Task 166 (amendment b): the container is derived from the grant's ROOT — never from the request.
        RecordContainerResolver containerResolver,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct,
        // Task 166 r1: "today" for the remaining-grant overlap check (which grants still confer access). Optional so
        // the many direct callers in tests keep compiling; resolved from DI on the route (registered unconditionally
        // by ExternalAccessModule).
        TimeProvider? timeProvider = null,
        // The identity binding store for THIS environment (registered by ExternalAccessModule): a contact whose binding
        // read EMPTY and who has no email has "nothing to clear" only while the binding columns' read is proven. Optional
        // so direct callers keep compiling; without it that case fails closed.
        IContactIdentityStore? identityStore = null)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (request.AccessRecordId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("AccessRecordId is required and must be a valid GUID.");

        // ContactId is OPTIONAL (task 073 #7): revoke is authoritative by AccessRecordId (root- AND
        // grantee-agnostic). Since task 137 the cache invalidation (Step 3) is keyed on the revoked ROW's grantee,
        // so it no longer needs ContactId either: a contact grant clears that contact, an ORGANIZATION grant clears
        // every active member. Since #2104 (pass-1 K3) the per-contact SPE cleanup (Step 2) is keyed on the row's
        // grantee too, so ContactId is not read by any step; it is logged only.
        //
        // Note (task 070): ProjectId is NOT required — revoke deactivates by AccessRecordId and is
        // root-agnostic (works for a project/matter/work-assignment grant alike). The field is retained
        // on the DTO for back-compat but no longer gates the request.

        logger.LogInformation(
            "[EXT-REVOKE] Revoking access record {AccessRecordId} for Contact {ContactId}",
            request.AccessRecordId, request.ContactId);

        // ── Steps 1-3 and the Assigned-To marking: the shared revoke (slice C1, ExternalGrantRevocationService) ──
        // The deactivation sweep by grant key, the SPE container cleanup, the grant-cache invalidation and the ledger
        // marking are one service, so every surface that revokes a grant runs the same steps. This handler turns its
        // outcome into the response.
        var revocation = await new ExternalGrantRevocationService(
                dataverseClient, speContainerMembership, participations, assignedAccess, containerResolver,
                timeProvider ?? TimeProvider.System, identityStore, logger)
            .RevokeAsync(request.AccessRecordId, ct);

        switch (revocation.Status)
        {
            case GrantRevocationStatus.NotFound:
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"Access record '{request.AccessRecordId}' was not found.");

            case GrantRevocationStatus.NoGrantKey:
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: $"Access record '{request.AccessRecordId}' is missing the root or grantee lookup " +
                            "needed to identify every row of this grant. No rows were deactivated.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });

            case GrantRevocationStatus.DeactivationFailed:
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "Failed to deactivate external access record in Dataverse.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        var deactivatedCount = revocation.DeactivatedCount;
        var speOutcome = revocation.SpeOutcome;
        var orgCleanup = revocation.OrgCleanup;
        var residualAccessTerms = revocation.ResidualAccessTerms ?? Array.Empty<string>();
        var organizationTooLarge = revocation.OrganizationTooLarge;
        var permissionsAmbiguous = revocation.PermissionsAmbiguous;

        // ── M2 (task 024 → task 065): align with /close-project for the identical failure shape ──
        //
        // ONLY Failed warrants a 500 — NotAttempted, PermissionRemoved and NoPermissionFound are all
        // healthy, expected outcomes under the broker-only model (see SpeContainerRevokeOutcome's own doc
        // comments) and stay a 200. Before this, /revoke reported Failed — "the grantee may RETAIN file
        // access" — as a 200 with the failure buried in the body, while ProjectClosureEndpoint reported
        // the identical shape as a 500. Per this task's ADR-003 constraint and the owner's 2026-09-10
        // directive ("if revoked, a user message should be provided not just a 500 error"), the status
        // code now matches AND deactivatedCount/speContainerOutcome/speOrgMemberCleanup ride in the
        // ProblemDetails extensions so the caller can still render the specific, three-outcome message —
        // never a bare 500.
        if (speOutcome == SpeContainerRevokeOutcome.Failed)
        {
            logger.LogError(
                "[EXT-REVOKE] Revoke of {AccessRecordId} INCOMPLETE: {Deactivated} Dataverse row(s) " +
                "deactivated, but the SPE container permission could not be confirmed removed. The " +
                "grantee may RETAIN file access. Reporting 500 (M2) rather than 200.",
                request.AccessRecordId, deactivatedCount);

            return RevokeIncomplete(
                httpContext, deactivatedCount, speOutcome, orgCleanup, residualAccessTerms, organizationTooLarge,
                permissionsAmbiguous);
        }

        // DeactivatedCount makes the outcome explicit rather than inferable: 0 means the grant was
        // already fully inactive (a safe no-op), >1 means duplicates existed and were swept — the exact
        // condition that used to leave access standing after a "successful" revoke.
        return TypedResults.Ok(new RevokeAccessResponse(
            SpeContainerMembershipRevoked: speOutcome == SpeContainerRevokeOutcome.PermissionRemoved,
            SpeContainerOutcome: speOutcome,
            DeactivatedCount: deactivatedCount,
            SpeOrgMemberCleanup: orgCleanup,
            ResidualAccessTerms: residualAccessTerms.Count > 0 ? residualAccessTerms : null));
    }

    /// <summary>
    /// The "SPE container permission could not be confirmed removed" shape: 500 + ProblemDetails carrying
    /// a machine-readable reason code (ADR-003) and the correlation id (ADR-019) — the M2 alignment with
    /// <see cref="ProjectClosureEndpoint"/>'s <c>ClosureIncomplete</c> for the identical failure shape.
    /// </summary>
    /// <remarks>
    /// <para><b>M2 (task 024 → task 065).</b> Only <see cref="SpeContainerRevokeOutcome.Failed"/> reaches
    /// here. Reuses the <c>sdap.*.incomplete.*</c> shape closure established, with its OWN leaf
    /// (<see cref="RevokeSpeCleanupIncompleteReason"/>) rather than closure's literal code — a client
    /// switching on the code must be told a single-grant revoke from a project closure.</para>
    ///
    /// <para><b>Owner directive, 2026-09-10.</b> "if revoked, a user message should be provided not just a
    /// 500 error" — <paramref name="deactivatedCount"/> rides in extensions for exactly the reason
    /// <c>ClosureIncomplete</c>'s own remarks give: N grants WERE deactivated even when the SPE half
    /// failed, and a bare 500 cannot distinguish that from nothing having happened.
    /// <paramref name="speOutcome"/> and <paramref name="orgCleanup"/> are carried too — for an
    /// organization revoke, <c>orgCleanup.MembersEnumerated == null</c> is the only signal distinguishing
    /// "we swept everyone" from "we do not know who to sweep", the same load-bearing <c>null</c> the 200
    /// path already preserves through serialization.</para>
    /// </remarks>
    private static IResult RevokeIncomplete(
        HttpContext httpContext,
        int deactivatedCount,
        SpeContainerRevokeOutcome speOutcome,
        SpeOrgMemberCleanupSummary? orgCleanup,
        IReadOnlyList<string>? residualAccessTerms = null,
        bool organizationTooLarge = false,
        int permissionsAmbiguous = 0)
    {
        var deactivated = deactivatedCount > 0
            ? $"Deactivated {deactivatedCount} Dataverse access grant(s). "
            : "No Dataverse access grants were active to deactivate. ";

        // Precedence (RevokePermissionAmbiguousReason): too-large, then ambiguous, then the retryable not-cleared.
        var reasonCode = organizationTooLarge
            ? RevokeOrganizationTooLargeReason
            : permissionsAmbiguous > 0
                ? RevokePermissionAmbiguousReason
                : RevokeSpeCleanupIncompleteReason;

        var detail = organizationTooLarge
            ? deactivated +
              $"The organization has more than {ExternalOrganizationMembership.MaxMembersPerSweep} active members, " +
              "more than one revoke may sweep, so no member's SPE container permission was removed and members may " +
              "RETAIN file access. Retrying will not change this. An administrator must remove those members' " +
              "permissions from the record's container in the SPE admin app (the container's Permissions)."
            : permissionsAmbiguous > 0
            ? deactivated + PermissionAmbiguousSentence(orgCleanup, permissionsAmbiguous)
            : deactivatedCount > 0
            ? $"Deactivated {deactivatedCount} Dataverse access grant(s), but the SPE container " +
              "permission could not be confirmed removed. The grantee may RETAIN file access. Retry the " +
              "revoke."
            : "The SPE container permission could not be confirmed removed, and no Dataverse access " +
              "grants were deactivated. Retry the revoke.";

        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "External access revoke incomplete",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = reasonCode,
                ["deactivatedCount"] = deactivatedCount,
                ["speContainerOutcome"] = speOutcome,
                ["speOrgMemberCleanup"] = orgCleanup,
                ["residualAccessTerms"] = residualAccessTerms is { Count: > 0 } ? residualAccessTerms : null,
                ["traceId"] = httpContext.TraceIdentifier
            });
    }

    /// <summary>
    /// #2104 round 3: the <see cref="RevokePermissionAmbiguousReason"/> sentence and its NON-retryable administrator
    /// instruction. Names no person and no address (the detail reaches the caller's UI): a count, for an organization.
    /// </summary>
    private static string PermissionAmbiguousSentence(SpeOrgMemberCleanupSummary? orgCleanup, int permissionsAmbiguous)
    {
        const string instruction =
            "Retrying will not change this. An administrator must resolve the container's permissions in the SPE admin " +
            "app (the record's container, its Permissions).";

        if (orgCleanup is null)
        {
            return "More than one permission on the record's SPE container matches the revoked grantee, or the matching " +
                   "permission's holder cannot be verified, so none was removed and the grantee may RETAIN file access. " +
                   instruction;
        }

        var others = orgCleanup.Failed - permissionsAmbiguous;
        return $"{permissionsAmbiguous} member(s) of the organization match more than one permission on the record's SPE " +
               "container, or a permission whose holder cannot be verified, so none of those permissions was removed and " +
               "those members may RETAIN file access. " + instruction +
               (others > 0 ? $" {others} other member(s) could also not be confirmed removed." : string.Empty);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Collapses the per-member arithmetic into the single outcome the caller reads. A total function
    /// over the summary — every shape maps somewhere, and only one shape maps to success.
    /// </summary>
    /// <remarks>
    /// The ordering is the ADR-003 fail-closed rule at member granularity, and each branch exists because
    /// the alternative is a lie: an unknown member list is not "clean" (we never looked), and one member
    /// retaining access is not "removed" merely because eleven others lost theirs.
    /// </remarks>
    internal static SpeContainerRevokeOutcome AggregateOrgOutcome(SpeOrgMemberCleanupSummary summary)
    {
        // "We could not tell who the members are" — never reportable as cleaned.
        if (summary.MembersEnumerated is null)
            return SpeContainerRevokeOutcome.Failed;

        // "Some members retain access" — never reportable as success, however many others were cleaned.
        if (summary.Failed > 0)
            return SpeContainerRevokeOutcome.Failed;

        if (summary.PermissionsRemoved > 0)
            return SpeContainerRevokeOutcome.PermissionRemoved;

        // Task 166 r1: every enumerated member is still entitled through another live grant on this root — nobody's
        // permission was even looked up, so "nobody held one" would be a claim this revoke never checked. #2103: a member
        // with no email key is the same — nothing was looked up for them either.
        if (summary.MembersEnumerated > 0
            && summary.RetainedByOtherGrant + summary.NoMembershipKey == summary.MembersEnumerated)
            return SpeContainerRevokeOutcome.NotAttempted;

        // The member list was established (possibly empty) and nobody held a permission. Under the
        // broker-only model this is the ordinary, healthy answer — not a problem.
        return SpeContainerRevokeOutcome.NoPermissionFound;
    }

    /// <summary>The contact columns <see cref="ResolveContactMembershipKeyAsync"/> reads.</summary>
    internal static readonly string ContactMembershipKeySelect = string.Join(",",
        "emailaddress1", ContactBindingDecision.ExternalObjectIdColumn, ContactBindingDecision.IdentityPlaneColumn);

    /// <summary>
    /// Reads a contact's identity key (#2105): its bound Entra object id (<c>sprk_externalobjectid</c>, read through
    /// <see cref="ContactBindingDecision.ReadBinding"/>, the one binding parse) and its <c>emailaddress1</c>.
    /// </summary>
    /// <remarks>
    /// <para>Uses <see cref="DataverseWebApiClient.RetrieveAsync{T}"/> directly rather than introducing a
    /// contact-key service: three columns on one row, and the client is already injected (CLAUDE.md §11).
    /// <c>internal</c> since task 166: project closure removes the revoked grantees' container permissions by the
    /// SAME identity key, so it reuses this rather than a second copy.</para>
    /// <para><b>An UNREADABLE binding is not "no object id"</b> (<see cref="BindingKind.Unreadable"/>: a plane marker
    /// with no readable oid — the field-security masking signature — is never treated as unbound). With an email the
    /// email still matches, as it always has. With no email the contact may hold a permission under an oid this read
    /// cannot see, so "nothing to clear" would be a guess: it THROWS, which every caller already reports as a key that
    /// could not be read (Failed / not cleared) — fail closed.</para>
    /// <para><b>An EMPTY binding is "no object id" only on a proven read.</b> The binding and its plane are both
    /// field-secured, so a hidden binding reads empty, like no binding. With no email either, "nothing to clear" is
    /// answered only while <paramref name="identityStore"/>'s masking probe proves the identity columns' read
    /// (<see cref="DataverseContactIdentityStore.ProbeBindingReadabilityAsync"/>: Readable, or no marked rows); otherwise —
    /// no store, Masked, or a probe that failed — it THROWS, as above.</para>
    /// </remarks>
    internal static async Task<SpeMembershipKey> ResolveContactMembershipKeyAsync(
        DataverseWebApiClient dataverseClient, IContactIdentityStore? identityStore, Guid contactId, CancellationToken ct)
    {
        var row = await dataverseClient.RetrieveAsync<ContactEmailRow>(
            "contacts", contactId, ContactMembershipKeySelect, ct);

        var binding = ContactBindingDecision.ReadBinding(row?.sprk_externalobjectid, row?.sprk_identityplane);
        var key = new SpeMembershipKey(binding.Kind == BindingKind.Bound ? binding.Oid : null, row?.emailaddress1);

        if (binding.Kind == BindingKind.Unreadable && key.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Contact {contactId} has no emailaddress1 and an unreadable identity binding (a plane marker without a "
                + "readable object id), so its container permission cannot be matched or ruled out.");
        }

        if (binding.Kind == BindingKind.Unbound && key.IsEmpty)
        {
            var readability = identityStore is null
                ? (BindingReadability?)null
                : await identityStore.ProbeBindingReadabilityAsync(ct).ConfigureAwait(false);
            if (readability is not (BindingReadability.Readable or BindingReadability.NoMarkedRows))
            {
                throw new InvalidOperationException(
                    $"Contact {contactId} has no emailaddress1 and its identity binding read empty, but the read of the "
                    + $"identity columns is not proven ({readability?.ToString() ?? "no identity store"}), so its container "
                    + "permission cannot be ruled out.");
            }
        }

        return key;
    }

    /// <summary>
    /// #2103 / #2105 — THE decision for a grantee contact with no identity key (no bound Entra object id AND no
    /// <c>emailaddress1</c>), shared by every SPE cleanup that revokes contacts: the single-grant revoke (a contact
    /// grant, and each member of an organization grant) and project closure. <see langword="true"/> means no container
    /// permission can be attributed to this contact, so there is nothing to clear — NOT a failure, and never a "retry".
    /// </summary>
    /// <remarks>
    /// <para><b>Why "nothing to clear" is the truthful answer, not a guess.</b></para>
    /// <list type="number">
    /// <item><b>No grant-time write records a key.</b> <see cref="SpeContainerMembershipService.GrantMembershipAsync"/>
    /// has no caller and never had one; <c>/grant</c> answers <c>SpeContainerMembershipGranted: false</c>; the invite
    /// endpoints do not touch SPE. A contact's container permission, when one exists, was added by hand — in the SPE
    /// admin app (a Spaarke surface: Add Permission names the user by UPN, resolved from their object id) or outside
    /// Spaarke.</item>
    /// <item><b>Such a permission names its holder by object id, email and UPN.</b> The revoke matches it by the
    /// contact's bound object id first, then by its email against the permission's <c>email</c> and
    /// <c>userPrincipalName</c> (<see cref="SpeContainerMembershipService.FindContactPermissions"/>). With either key,
    /// "no permission under it" is the healthy <see cref="SpeContainerRevokeOutcome.NoPermissionFound"/>; with neither,
    /// no permission on the container can be tied to this contact at all.</item>
    /// <item><b>The roles Spaarke itself creates are never a contact's.</b> The marked grants
    /// (<see cref="SpeContainerMembershipService.StandingWriterMarkerPrefix"/> /
    /// <see cref="SpeContainerMembershipService.JitWriterMarkerPrefix"/> keyed by a systemuserid,
    /// <see cref="SpeContainerMembershipService.DemoWriterMarkerPrefix"/> by a demo user's Entra object id) belong to
    /// <c>SpeContainerMembershipSync</c> and demo expiry. The matcher excludes every permission a marker records
    /// (#2105), so a contact revoke never removes one — whatever its keys match.</item>
    /// </list>
    /// <para>It used to report <see cref="SpeContainerRevokeOutcome.Failed"/> (a 500 that says "retry"). A missing
    /// key is permanent, so the retry could never succeed (#2103, found by the UAC 099b live gate). Every other
    /// Failed — the key read faulting (an unreadable binding with no email included), Graph refusing, an incomplete
    /// permission or marker read — is transient and stays Failed.</para>
    /// </remarks>
    internal static bool HasNoContainerMembershipKey(SpeMembershipKey contactKey) => contactKey.IsEmpty;

    /// <summary>
    /// The per-contact outcome of <see cref="HasNoContainerMembershipKey"/>: <see cref="SpeContainerRevokeOutcome.NotAttempted"/>,
    /// because the container was not read for this contact. It is not <see cref="SpeContainerRevokeOutcome.NoPermissionFound"/>, which
    /// claims a read. It is a 200.
    /// </summary>
    internal const SpeContainerRevokeOutcome NoContainerMembershipKeyOutcome = SpeContainerRevokeOutcome.NotAttempted;

    /// <summary>
    /// Minimal projection of <c>contacts</c> for SPE identity matching (<see cref="ContactMembershipKeySelect"/>). The
    /// name predates #2105, which added the binding columns.
    /// </summary>
    internal sealed class ContactEmailRow
    {
        public string? emailaddress1 { get; set; }

        /// <summary><c>sprk_externalobjectid</c> — the bound Entra object id (field-secured; null when masked).</summary>
        public string? sprk_externalobjectid { get; set; }

        /// <summary><c>sprk_identityplane</c> — which plane wrote the binding (field-secured; null when masked).</summary>
        public int? sprk_identityplane { get; set; }
    }
}
