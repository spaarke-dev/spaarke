// teams-app-r1 Task 020 (2026-08-03) — Workforce-token → principal resolver.
// unified-access-control-r2 task 141 (2026-10-01) — identity binding by Entra oid.
//
// ADR-028 Amendment A2 sanctions the workforce collaboration plane: a Teams-host caller
// authenticates with their workforce Microsoft Entra identity and MUST resolve to a Dataverse
// PRINCIPAL — a systemuser (→ ADR-034 membership) or, for a non-systemuser, a contact
// (→ contact-anchored membership) — else be DENIED.
//
//   (0) NOT a user token      : an app-only (or unclassifiable) token is denied before ANY lookup — its oid can be a
//                               Dataverse application user's systemuser, which must never resolve as a person.
//   (a) AAD oid → systemuser  : ResolveSystemUserAsync (one read; same query and cache entry as ADR-028's
//                               MembershipEndpoints.ResolveSystemUserIdAsync, plus isdisabled), then the
//                               systemuser's contact from IIdentityNormalizationService (its sprk_primarycontact
//                               link, else the contact bound to its oid). A licensed user with NO linked contact
//                               is linked INLINE here (ContactIdentityBinder, task 141) and the new link takes
//                               effect on this request — the cached identity is invalidated. The inline link is
//                               gated on the job's rollout switch (IdentityLink:Reconciliation:WritesEnabled).
//                               A DISABLED systemuser (R3 task 012 review) is denied here and never reaches (b).
//   (b) AAD oid → contact     : ContactIdentityBinder.ResolveWorkforceCallerAsync — the contact BOUND to the
//                               caller's oid (contact.sprk_externalobjectid). A first sign-in by a MEMBER of a
//                               configured customer workforce tenant (acct = 0) — or a registered workforce-contact
//                               guest (R3 FR-15: acct = 1, tid = AzureAd:TenantId, idp home tenant on the customer
//                               list) — may bind by email (exactly one
//                               active, unbound match) or create a contact keyed by the oid. Everyone else
//                               resolves only through an existing oid binding. Collisions are refused AND flagged.
//   (c) neither                : explicit DENY with the binding decision's own deny code — never a silent fallback
//                               to an unscoped/anonymous principal.
//
// Broker-only (ADR-028 A2 NFR-02): this resolver reads CLAIMS from the already-validated workforce
// token and queries Dataverse APP-ONLY. It MUST NOT exchange the caller token downstream (no OBO) and MUST NOT
// call Microsoft Graph with the caller token.

using System.Security.Claims;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Core.Auth;
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
    /// The oid names a systemuser that is DISABLED. Such a user is "no longer a valid principal"
    /// (as in <c>MembershipEndpoints.ResolveSystemUserIdAsync</c>), so it must not fall through to the contact branch,
    /// where a registered-guest (FR-15) or member token would otherwise be admitted as a contact.
    /// </summary>
    internal const string DenySystemUserDisabled = "sdap.access.deny.systemuser_disabled";

    /// <summary>
    /// The stamp's own tenant (the workforce JwtBearer scheme's tenant): a registered workforce-contact guest's
    /// <c>tid</c> must equal it (R3 FR-15). Absent or unparseable = the guest branch never applies.
    /// </summary>
    internal const string StampTenantConfigKey = "AzureAd:TenantId";

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

        // ── (0) user tokens only. An app-only token's oid is its service principal's — for a Dataverse application
        //    user that is a systemuser row, which branch (a) would resolve as a person. The member test would refuse
        //    it later, but only on branch (b); refuse it here, before any lookup (R3 task 011 review K1).
        // The deny code (WorkforceMembershipTest.DenyNotUserToken, "...workforce_app_only_token") is returned for every
        // non-UserDelegated kind - Application AND Indeterminate (a user-shaped token with no positive idtyp/scp
        // evidence). It is a documented, shared member-test code (task-141 notes), not an assertion that the token
        // is app-only; the log line below carries the actual kind.
        var callerKind = CallerIdentity.FromPrincipal(user).Kind;
        if (callerKind != CallerKind.UserDelegated)
        {
            _logger.LogWarning(
                "[WF-AUTH] Workforce caller is not a user-delegated token (kind: {Kind}) — denying ({DenyCode})",
                callerKind, WorkforceMembershipTest.DenyNotUserToken);
            LogGuestCandidateDeny(user, WorkforceMembershipTest.DenyNotUserToken);
            return WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, WorkforceMembershipTest.DenyNotUserToken);
        }

        // ── (a) systemuser branch — AAD oid → systemuser. ONE uncached read answers all three cases (enabled /
        //    disabled-only / none); see ResolveSystemUserAsync. A throw fails closed, auditable (not an unhandled 500).
        SystemUserLookup lookup;
        try
        {
            lookup = await ResolveSystemUserAsync(callerOid, tenantId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cannot rule a disabled systemuser out → fail closed.
            _logger.LogError(ex,
                "[WF-AUTH] Systemuser lookup threw for workforce caller oid={CallerOid} — denying ({DenyCode})",
                callerOid, DenyPrincipalNotResolved);
            LogGuestCandidateDeny(user, DenyPrincipalNotResolved);
            return WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, DenyPrincipalNotResolved);
        }

        if (lookup.Kind == SystemUserLookupKind.Enabled)
        {
            var suid = lookup.Id;

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

        // ── (a-disabled) a DISABLED systemuser is not a valid principal — it must not fall through to (b) ──
        // A disabled licensed user (including a licensed B2B guest whose home tenant FR-15 would now admit as a
        // registered guest) would otherwise be admitted by the binder as a Member and handed a contact. This also
        // closes the pre-existing Model-2 analogue: a disabled systemuser carrying an acct = 0 member token.
        if (lookup.Kind == SystemUserLookupKind.Disabled)
        {
            _logger.LogWarning(
                "[WF-AUTH] Workforce caller oid={CallerOid} maps to a DISABLED systemuser — denying ({DenyCode})",
                callerOid, DenySystemUserDisabled);
            LogGuestCandidateDeny(user, DenySystemUserDisabled);
            return WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, DenySystemUserDisabled);
        }

        // ── (b) contact-only branch — the oid binding (task 141) ──
        ContactBindingResult binding;
        try
        {
            // The stamp tenant comes from this deployment's own configuration, never from the token (R3 FR-15).
            binding = await _binder.ResolveWorkforceCallerAsync(
                    user, callerOid, _configuration[StampTenantConfigKey], ct)
                .ConfigureAwait(false);
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
            LogGuestCandidateDeny(user, DenyPrincipalNotResolved);
            return WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, DenyPrincipalNotResolved);
        }

        if (binding.IsResolved)
        {
            // A registered guest (FR-15) is told from a home-tenant member by its parsed home tenant.
            _logger.LogInformation(
                "[WF-AUTH] Resolved workforce caller oid={CallerOid} to contact-only principal {ContactId} via {Action} " +
                "(homeTenant: {HomeTenant})",
                callerOid, binding.ContactId, binding.Action, GuestHomeTenantForLog(user));
            return WorkforcePrincipalResolution.ForContact(binding.ContactId!.Value, callerOid.ToString("D"), tenantId);
        }

        // ── (c) explicit deny — the binding decision's own code reaches the ProblemDetails reasonCode ──
        _logger.LogWarning(
            "[WF-AUTH] Workforce caller oid={CallerOid} matched neither a systemuser nor a bound contact — denying ({DenyCode})",
            callerOid, binding.DenyCode);
        LogGuestCandidateDeny(user, binding.DenyCode ?? DenyPrincipalNotResolved);
        return WorkforcePrincipalResolution.Denied(
            WorkforceDenyReason.PrincipalNotResolved, binding.DenyCode ?? DenyPrincipalNotResolved);
    }

    private enum SystemUserLookupKind { None, Enabled, Disabled }

    private readonly record struct SystemUserLookup(SystemUserLookupKind Kind, Guid Id);

    /// <summary>
    /// The single systemuser read for a workforce caller (R3 task 012 re-check K2): ONE query by
    /// <c>azureactivedirectoryobjectid</c> with no <c>isdisabled</c> filter, returning <c>isdisabled</c>, so the
    /// enabled / disabled-only / none decision costs one uncached read — previously a contact-only caller paid two
    /// (enabled-only lookup, then an existence check). Resolver-local on purpose: the enabled-only
    /// <c>MembershipEndpoints.ResolveSystemUserIdAsync</c> keeps its behaviour for its other callers.
    /// </summary>
    /// <remarks>
    /// Semantics: any ENABLED row wins over a disabled duplicate (rows are ordered enabled-first and
    /// <c>TopCount = 2</c> keeps a duplicate visible); only disabled rows → <see cref="SystemUserLookupKind.Disabled"/>;
    /// no rows → <see cref="SystemUserLookupKind.None"/>. A row whose <c>isdisabled</c> is absent is treated as
    /// disabled (fail closed). The positive result shares the existing <c>membership-currentuser</c> cache entry
    /// (same key, version and 10 minute TTL as <c>MembershipEndpoints</c>); a cache failure is non-fatal. Nothing
    /// negative is cached, so a stale "disabled" never outlives a re-enable.
    /// </remarks>
    private async Task<SystemUserLookup> ResolveSystemUserAsync(Guid aadObjectId, string tenantId, CancellationToken ct)
    {
        var cacheId = aadObjectId.ToString("D");

        try
        {
            var cached = await _cache.GetAsync<Guid>(
                    tenantId, MembershipEndpoints.CurrentUserCacheResource, cacheId,
                    MembershipEndpoints.CurrentUserCacheVersion, ct: ct)
                .ConfigureAwait(false);
            if (cached != Guid.Empty)
            {
                return new SystemUserLookup(SystemUserLookupKind.Enabled, cached);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[WF-AUTH] Current-user cache read failed for tenant={TenantId} id={CacheId}; falling through to live resolve",
                tenantId, cacheId);
        }

        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "isdisabled"),
            TopCount = 2,
            NoLock = true,
        };
        query.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, aadObjectId);
        query.AddOrder("isdisabled", OrderType.Ascending);

        var results = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        var sawRow = false;
        foreach (var row in results.Entities)
        {
            if (row.Id == Guid.Empty)
            {
                continue;
            }

            sawRow = true;
            if (row.GetAttributeValue<bool?>("isdisabled") != false)
            {
                continue;
            }

            try
            {
                await _cache.SetAsync(
                        tenantId, MembershipEndpoints.CurrentUserCacheResource, cacheId,
                        MembershipEndpoints.CurrentUserCacheVersion, row.Id,
                        MembershipEndpoints.CurrentUserCacheTtl, ct: ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[WF-AUTH] Current-user cache write failed for tenant={TenantId} id={CacheId}; next call will re-resolve",
                    tenantId, cacheId);
            }

            return new SystemUserLookup(SystemUserLookupKind.Enabled, row.Id);
        }

        return new SystemUserLookup(sawRow ? SystemUserLookupKind.Disabled : SystemUserLookupKind.None, Guid.Empty);
    }

    /// <summary>The home tenant parsed from <c>idp</c>, for a guest only; otherwise a fixed placeholder (never a raw claim).</summary>
    private static string GuestHomeTenantForLog(ClaimsPrincipal user)
    {
        var claims = WorkforceCallerClaims.From(user);
        if (claims.Acct?.Trim() != "1")
        {
            return "n/a";
        }

        return WorkforceMembershipTest.TryParseHomeTenant(claims.Idp, out var home) ? home.ToString("D") : "unparsed";
    }

    /// <summary>
    /// One warning on every <c>acct = 1</c> deny in this resolver (R3 task 012 review): the deny reason, whether the
    /// stamp tenant parsed, and — only when <c>idp</c> parsed — its issuer host and home-tenant GUID. No other claim
    /// values and no tokens are written. This is the line an operator reads when a guest "cannot get in".
    /// </summary>
    private void LogGuestCandidateDeny(ClaimsPrincipal user, string denyCode)
    {
        var claims = WorkforceCallerClaims.From(user);
        if (claims.Acct?.Trim() != "1")
        {
            return;
        }

        var stampParsed = Guid.TryParse(_configuration[StampTenantConfigKey]?.Trim(), out var stamp) && stamp != Guid.Empty;
        var idpParsed = WorkforceMembershipTest.TryParseHomeTenant(claims.Idp, out var home);
        var idpHost = !idpParsed
            ? "unparsed"
            : Uri.TryCreate(claims.Idp, UriKind.Absolute, out var uri) ? uri.Host : "bare-guid";

        _logger.LogWarning(
            "[WF-AUTH] Guest (acct=1) workforce caller denied ({DenyCode}): stampTenantParsed={StampTenantParsed}, " +
            "idpHost={IdpHost}, idpHomeTenant={IdpHomeTenant}",
            denyCode, stampParsed, idpHost, idpParsed ? home.ToString("D") : "unparsed");
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
