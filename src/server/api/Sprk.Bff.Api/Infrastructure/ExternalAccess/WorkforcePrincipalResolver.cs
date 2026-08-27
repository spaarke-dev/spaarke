// teams-app-r1 Task 020 (2026-08-03) — Workforce-token → principal resolver.
//
// ADR-028 Amendment A2 sanctions the workforce collaboration plane: a Teams-host caller
// authenticates with their workforce Microsoft Entra identity and MUST resolve to a Dataverse
// PRINCIPAL — a systemuser (→ ADR-034 membership) or, for a non-systemuser, a contact
// (→ contact-anchored membership) — else be DENIED. This resolver COMPOSES two conversions that
// already exist (spec FR-04 / design §5, verified by the task-001 foundation spike §1.2):
//
//   (a) AAD oid → systemuser  : MembershipEndpoints.ResolveSystemUserIdAsync (existing; ADR-028)
//                               then DERIVE the systemuser's contactId via the existing
//                               IIdentityNormalizationService (sprk_primarycontact / AAD cross-ref).
//   (b) AAD oid → contact     : IIdentityNormalizationService.TryResolveContactByWorkforceIdentityAsync
//                               (oid cross-ref + verified-email fallback) — the contact-only branch.
//                               The email fallback applies the NO-HIJACK rule (spec FR-12, finding
//                               A-18, 2026-08-26): an email matching a contact already bound to a
//                               DIFFERENT oid resolves to nothing, so it lands on (c) DENY. That
//                               mirrors the CIAM guard in ExternalParticipationService — the two
//                               planes now enforce the same rule against their own binding column.
//   (c) neither                : explicit DENY — no silent fallback to an unscoped/anonymous principal.
//
// It adds NO new identity-resolution mechanism — only orchestration + a deny path (CLAUDE.md §11
// justification: extend both existing conversions).
//
// Broker-only (ADR-028 A2 NFR-02): this resolver reads CLAIMS from the already-validated workforce
// token and queries Dataverse APP-ONLY (via IDataverseService / the existing ResolveSystemUserIdAsync
// path). It MUST NOT exchange the caller token downstream (no OBO) and MUST NOT call Microsoft Graph
// with the caller token. No Graph SDK types and no AI-internal types are injected here.

using System.Security.Claims;
using Sprk.Bff.Api.Api.Membership;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Resolves a validated workforce (Teams-host) JwtBearer principal to exactly one of:
/// a systemuser principal (systemuserId + derived contactId), a contact-only principal
/// (contactId), or an explicit deny — with no silent fallback (ADR-028 A2 / FR-04).
/// </summary>
/// <remarks>
/// The interface exists as a testing seam (ADR-010 — interface allowed when a testing seam is
/// needed), mirroring <see cref="IIdentityNormalizationService"/>. The endpoint filter that consumes
/// it (<c>WorkforceCallerAuthorizationFilter</c>) can then be exercised against a substitute resolver.
/// </remarks>
public interface IWorkforcePrincipalResolver
{
    /// <summary>
    /// Resolves the supplied validated workforce principal.
    /// </summary>
    /// <param name="user">
    /// The <see cref="ClaimsPrincipal"/> from the already-validated workforce JWT
    /// (<c>HttpContext.User</c>). The <c>oid</c> / <c>tid</c> / email claims are read from it.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="WorkforcePrincipalResolution"/>: resolved (systemuser or contact-only) or denied.
    /// </returns>
    Task<WorkforcePrincipalResolution> ResolveAsync(ClaimsPrincipal user, CancellationToken ct);
}

/// <inheritdoc />
public sealed class WorkforcePrincipalResolver : IWorkforcePrincipalResolver
{
    // Deny codes (auth.md format: {domain}.{area}.{action}.{reason}).
    internal const string DenyMissingIdentityClaims = "sdap.access.deny.missing_identity_claims";
    internal const string DenyPrincipalNotResolved = "sdap.access.deny.principal_not_resolved";

    private readonly IIdentityNormalizationService _identity;
    private readonly IDataverseService _dataverse;
    private readonly ITenantCache _cache;
    private readonly ILogger<WorkforcePrincipalResolver> _logger;

    public WorkforcePrincipalResolver(
        IIdentityNormalizationService identity,
        IDataverseService dataverse,
        ITenantCache cache,
        ILogger<WorkforcePrincipalResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _identity = identity;
        _dataverse = dataverse;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkforcePrincipalResolution> ResolveAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ct.ThrowIfCancellationRequested();

        // ── Identity from claims (reuse MembershipEndpoints extraction, per ADR-028) ──
        var oid = MembershipEndpoints.ExtractAadObjectId(user);
        if (oid is null)
        {
            // We cannot identify the caller at all → 401-class deny. RequireAuthorization()
            // should already 401 an unauthenticated request; this is the defensive re-check.
            _logger.LogWarning(
                "[WF-AUTH] Workforce token missing usable AAD object id (oid) claim — denying");
            return WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.MissingIdentityClaims, DenyMissingIdentityClaims);
        }

        var tenantId = MembershipEndpoints.ExtractTenantId(user);
        var callerOid = oid.Value;

        // ── (a) systemuser branch — AAD oid → systemuser (existing conversion) ──
        var systemUserId = await MembershipEndpoints.ResolveSystemUserIdAsync(
                callerOid, tenantId, _dataverse, _cache, _logger, ct)
            .ConfigureAwait(false);

        if (systemUserId is { } suid && suid != Guid.Empty)
        {
            // Derive the systemuser's contactId via the existing 6-path normalization
            // (sprk_primarycontact primary + AAD cross-ref fallback). ContactId MAY be null
            // (systemuser with no linked contact) — that is still a valid systemuser principal;
            // its accessible set comes from ADR-034 membership regardless (task 021/022).
            Guid? derivedContactId = null;
            try
            {
                var identity = await _identity.ResolveAsync(suid, ct).ConfigureAwait(false);
                derivedContactId = identity.ContactId;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Non-fatal: a systemuser still authorizes via ADR-034 membership without a
                // derived contact. Log and continue with a null contactId.
                _logger.LogWarning(ex,
                    "[WF-AUTH] Failed to derive contactId for systemuser {SystemUserId}; " +
                    "proceeding as a systemuser principal with no derived contact",
                    suid);
            }

            _logger.LogInformation(
                "[WF-AUTH] Resolved workforce caller oid={CallerOid} to systemuser {SystemUserId} " +
                "(derivedContact: {HasContact})",
                callerOid, suid, derivedContactId is not null);

            return WorkforcePrincipalResolution.ForSystemUser(
                suid, derivedContactId, callerOid.ToString("D"), tenantId, ExtractVerifiedEmail(user));
        }

        // ── (b) contact-only branch — AAD oid / verified email → contact ──
        //
        // The email half of this conversion is a no-hijack decision, not a lookup: an email matching a
        // contact already bound to a DIFFERENT oid resolves to nothing (spec FR-12 / finding A-18,
        // mirroring the CIAM guard in ExternalParticipationService). This branch therefore has to treat
        // "no contact" and "denied contact" identically — both are the absence of a principal, and the
        // handling below already does exactly that.
        var verifiedEmail = ExtractVerifiedEmail(user);

        Guid? contactId;
        try
        {
            contactId = await _identity
                .TryResolveContactByWorkforceIdentityAsync(callerOid, verifiedEmail, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The contract says this conversion isolates its own failures and returns null. If it ever
            // stops doing so, the caller must still be DENIED rather than have the exception escape:
            // an unhandled throw here is a 500, which is fail-closed today only by accident of the
            // pipeline. A deny is fail-closed by construction (ADR-003) and is auditable.
            //
            // Deliberately NOT symmetrical with the systemuser branch above, which swallows and
            // continues: there, the derived contactId is supplementary to an already-established
            // systemuser principal. Here it IS the principal, so continuing would mean continuing
            // without one.
            _logger.LogError(ex,
                "[WF-AUTH] Contact resolution threw for workforce caller oid={CallerOid} — denying " +
                "({DenyCode}); the caller's contact binding state is unknown",
                callerOid, DenyPrincipalNotResolved);
            return WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, DenyPrincipalNotResolved);
        }

        if (contactId is { } cid && cid != Guid.Empty)
        {
            _logger.LogInformation(
                "[WF-AUTH] Resolved workforce caller oid={CallerOid} to contact-only principal {ContactId}",
                callerOid, cid);
            return WorkforcePrincipalResolution.ForContact(cid, callerOid.ToString("D"), tenantId);
        }

        // ── (c) explicit deny — matched neither a systemuser nor a contact ──
        // The caller is a valid Entra principal but is not provisioned as a systemuser and has no
        // matching contact. 403-class (a KNOWN identity that is not authorized here), never a silent
        // pass-through with an unscoped principal.
        _logger.LogWarning(
            "[WF-AUTH] Workforce caller oid={CallerOid} matched neither a systemuser nor a contact — denying",
            callerOid);
        return WorkforcePrincipalResolution.Denied(
            WorkforceDenyReason.PrincipalNotResolved, DenyPrincipalNotResolved);
    }

    /// <summary>
    /// Extracts the caller's tenant-verified email/UPN from the workforce token. Prefers the
    /// <c>email</c> claim (Entra populates it from the verified primary email), then
    /// <c>preferred_username</c> / <c>upn</c>. Returns <c>null</c> when none is present.
    /// </summary>
    internal static string? ExtractVerifiedEmail(ClaimsPrincipal user)
    {
        var email = user.FindFirst("email")?.Value
            ?? user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst("upn")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value;

        return string.IsNullOrWhiteSpace(email) ? null : email;
    }
}
