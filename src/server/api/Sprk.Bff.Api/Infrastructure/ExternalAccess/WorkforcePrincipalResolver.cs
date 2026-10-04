// teams-app-r1 Task 020 (2026-08-03) — Workforce-token → principal resolver.
// unified-access-control-r2 task 141 (2026-10-01) — identity binding by Entra oid.
//
// ADR-028 Amendment A2 sanctions the workforce collaboration plane: a Teams-host caller
// authenticates with their workforce Microsoft Entra identity and MUST resolve to a Dataverse
// PRINCIPAL — a systemuser (→ ADR-034 membership) or, for a non-systemuser, a contact
// (→ contact-anchored membership) — else be DENIED.
//
//   (a) AAD oid → systemuser  : MembershipEndpoints.ResolveSystemUserIdAsync (existing; ADR-028), then the
//                               systemuser's contact from IIdentityNormalizationService (its sprk_primarycontact
//                               link, else the contact bound to its oid). A licensed user with NO linked contact
//                               is linked INLINE here (ContactIdentityBinder, task 141) and the new link takes
//                               effect on this request — the cached identity is invalidated. The inline link is
//                               gated on the job's rollout switch (IdentityLink:Reconciliation:WritesEnabled).
//   (b) AAD oid → contact     : ContactIdentityBinder.ResolveWorkforceCallerAsync — the contact BOUND to the
//                               caller's oid (contact.sprk_externalobjectid). A first sign-in by a MEMBER of a
//                               configured customer workforce tenant (acct = 0) may bind by email (exactly one
//                               active, unbound match) or create a contact keyed by the oid. Everyone else
//                               resolves only through an existing oid binding. Collisions are refused AND flagged.
//   (c) neither                : explicit DENY with the binding decision's own deny code — never a silent fallback
//                               to an unscoped/anonymous principal.
//
// Broker-only (ADR-028 A2 NFR-02): this resolver reads CLAIMS from the already-validated workforce
// token and queries Dataverse APP-ONLY. It MUST NOT exchange the caller token downstream (no OBO) and MUST NOT
// call Microsoft Graph with the caller token.

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
/// needed), mirroring <see cref="IIdentityNormalizationService"/>.
/// </remarks>
public interface IWorkforcePrincipalResolver
{
    /// <summary>
    /// Resolves the supplied validated workforce principal.
    /// </summary>
    /// <param name="user">
    /// The <see cref="ClaimsPrincipal"/> from the already-validated workforce JWT
    /// (<c>HttpContext.User</c>). The <c>oid</c> / <c>tid</c> / <c>acct</c> / email claims are read from it.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<WorkforcePrincipalResolution> ResolveAsync(ClaimsPrincipal user, CancellationToken ct);
}

/// <inheritdoc />
public sealed class WorkforcePrincipalResolver : IWorkforcePrincipalResolver
{
    // Deny codes (auth.md format: {domain}.{area}.{action}.{reason}).
    internal const string DenyMissingIdentityClaims = "sdap.access.deny.missing_identity_claims";
    internal const string DenyPrincipalNotResolved = "sdap.access.deny.principal_not_resolved";

    /// <summary>
    /// Cache resource for the "a link was attempted and could not be made" marker. Cached DATA, not an
    /// authorization decision (ADR-003): it only stops a collision user from paying the link-attempt reads on
    /// every request. It never grants. 10 minutes — it no longer matches the identity cache, which task 132 cut to
    /// 2 minutes; this marker caches a link ATTEMPT, not access, so it does not bound any access change.
    /// </summary>
    internal const string LinkAttemptCacheResource = "identity-link-attempt";

    private static readonly TimeSpan LinkAttemptTtl = TimeSpan.FromMinutes(10);

    private readonly IIdentityNormalizationService _identity;
    private readonly IDataverseService _dataverse;
    private readonly ITenantCache _cache;
    private readonly ContactIdentityBinder _binder;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkforcePrincipalResolver> _logger;

    public WorkforcePrincipalResolver(
        IIdentityNormalizationService identity,
        IDataverseService dataverse,
        ITenantCache cache,
        ContactIdentityBinder binder,
        IConfiguration configuration,
        ILogger<WorkforcePrincipalResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _identity = identity;
        _dataverse = dataverse;
        _cache = cache;
        _binder = binder;
        _configuration = configuration;
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
            // ContactId MAY be null (a systemuser with no linked contact) — that is still a valid systemuser
            // principal; its accessible set comes from ADR-034 membership regardless (task 021/022).
            Guid? derivedContactId = null;

            // Task 132 (C12): whether the derived contact is UNKNOWN (its reads failed) rather than absent. The deny
            // veto's subject on this plane is this contact; an unknown subject must deny, never check nothing.
            var contactUnreadable = false;
            try
            {
                var identity = await _identity.ResolveAsync(suid, ct).ConfigureAwait(false);
                derivedContactId = identity.ContactId;
                contactUnreadable = identity.ContactUnreadable;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Non-fatal: a systemuser still authorizes via ADR-034 membership without a derived contact — but the
                // contact is UNKNOWN, so the evaluator's deny veto denies every candidate for this request (task 132).
                contactUnreadable = true;
                _logger.LogWarning(ex,
                    "[WF-AUTH] Failed to derive contactId for systemuser {SystemUserId}; " +
                    "proceeding as a systemuser principal with an UNREADABLE derived contact",
                    suid);
            }

            // Unchanged by task 132: the inline link runs whenever no contact was derived. If it links one, the contact
            // is known after all and ForSystemUser drops the unreadable flag.
            derivedContactId ??= await TryLinkSystemUserAsync(suid, tenantId, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "[WF-AUTH] Resolved workforce caller oid={CallerOid} to systemuser {SystemUserId} " +
                "(derivedContact: {HasContact}, contactUnreadable: {ContactUnreadable})",
                callerOid, suid, derivedContactId is not null, contactUnreadable);

            return WorkforcePrincipalResolution.ForSystemUser(
                suid, derivedContactId, callerOid.ToString("D"), tenantId, ExtractTokenEmail(user),
                contactUnreadable: contactUnreadable);
        }

        // ── (b) contact-only branch — the oid binding (task 141) ──
        ContactBindingResult binding;
        try
        {
            binding = await _binder.ResolveWorkforceCallerAsync(user, callerOid, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Guarded deliberately. HttpClient surfaces a TIMEOUT as TaskCanceledException, which is an
            // OperationCanceledException — so an unguarded arm here would rethrow a Dataverse timeout and
            // produce an un-auditable 500. Only a real cancellation (the caller went away) propagates.
            throw;
        }
        catch (Exception ex)
        {
            // The binder isolates its own failures into deny codes. If it ever stops doing so, the caller must
            // still be DENIED rather than have the exception escape (ADR-003 fail-closed, auditable).
            _logger.LogError(ex,
                "[WF-AUTH] Contact binding threw for workforce caller oid={CallerOid} — denying ({DenyCode})",
                callerOid, DenyPrincipalNotResolved);
            return WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, DenyPrincipalNotResolved);
        }

        if (binding.IsResolved)
        {
            _logger.LogInformation(
                "[WF-AUTH] Resolved workforce caller oid={CallerOid} to contact-only principal {ContactId} via {Action}",
                callerOid, binding.ContactId, binding.Action);
            return WorkforcePrincipalResolution.ForContact(binding.ContactId!.Value, callerOid.ToString("D"), tenantId);
        }

        // ── (c) explicit deny — the binding decision's own code reaches the ProblemDetails reasonCode ──
        _logger.LogWarning(
            "[WF-AUTH] Workforce caller oid={CallerOid} matched neither a systemuser nor a bound contact — denying ({DenyCode})",
            callerOid, binding.DenyCode);
        return WorkforcePrincipalResolution.Denied(
            WorkforceDenyReason.PrincipalNotResolved, binding.DenyCode ?? DenyPrincipalNotResolved);
    }

    /// <summary>
    /// The inline link (task 141): a licensed user who resolves with no contact gets the same decision every
    /// other path runs, keyed by the systemuser's own oid and directory-synced email. A link written here is
    /// returned directly AND the cached identity is invalidated, so it takes effect on THIS request. A user
    /// whose link cannot be made (a flagged collision) is not re-attempted for 10 minutes.
    /// </summary>
    /// <remarks>
    /// Gated on the SAME rollout switch as the reconciliation job
    /// (<see cref="ContactIdentityBinder.LinkWritesEnabledConfigKey"/>): until the owner turns writes on after
    /// reviewing the report-only run, a licensed user who signs in is not linked, flagged or given a created
    /// contact here, exactly as the job writes nothing (verifier finding 4). The user stays a systemuser
    /// principal with no derived contact, which is the behaviour before task 141; nothing is read.
    /// </remarks>
    private async Task<Guid?> TryLinkSystemUserAsync(Guid systemUserId, string tenantId, CancellationToken ct)
    {
        if (!ContactIdentityBinder.LinkWritesEnabled(_configuration))
        {
            _logger.LogDebug(
                "[WF-AUTH] Inline contact link for systemuser {SystemUserId} skipped: {Switch} is not true (report-only rollout)",
                systemUserId, ContactIdentityBinder.LinkWritesEnabledConfigKey);
            return null;
        }

        var cacheId = systemUserId.ToString("D");
        try
        {
            var recent = await _cache.GetAsync<string>(tenantId, LinkAttemptCacheResource, cacheId, 1, ct: ct)
                .ConfigureAwait(false);
            if (recent is not null)
            {
                return null;
            }

            var result = await _binder.EnsureSystemUserLinkAsync(systemUserId, applyWrites: true, ct).ConfigureAwait(false);
            if (result.IsLinked && result.ContactId is { } linked)
            {
                await _identity.InvalidateAsync(systemUserId, ct).ConfigureAwait(false);
                return linked;
            }

            await _cache.SetAsync(tenantId, LinkAttemptCacheResource, cacheId, 1,
                result.DenyCode ?? result.Outcome.ToString(), LinkAttemptTtl, ct: ct).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Non-fatal, exactly like the derived-contact read above: the systemuser principal stands.
            _logger.LogWarning(ex, "[WF-AUTH] Inline contact link failed for systemuser {SystemUserId}", systemUserId);
            return null;
        }
    }

    /// <summary>
    /// The caller's email from the token (<c>email</c> → <c>preferred_username</c> → <c>upn</c>). NOT verified
    /// by anything — Microsoft documents these claims as mutable and unsuitable for authorization. It is carried
    /// on the principal for display and audit, and used to bind ONLY behind the member test
    /// (<see cref="WorkforceMembershipTest"/>). Renamed by task 141 from <c>ExtractVerifiedEmail</c>, whose name
    /// claimed a verification nothing performed.
    /// </summary>
    internal static string? ExtractTokenEmail(ClaimsPrincipal user) => WorkforceCallerClaims.TokenEmail(user);
}
