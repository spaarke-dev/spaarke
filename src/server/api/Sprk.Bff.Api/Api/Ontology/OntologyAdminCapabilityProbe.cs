using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Api.Ontology;

/// <summary>
/// The Ontology admin capability probe (ontology platform R1, D-22, FR-56): asks Dataverse, AS THE CALLER, whether the caller holds
/// <c>prvCreatesprk_PolicyVersion</c>, and keeps the answer in the tenant cache. Split from <c>OntologyAdminEndpoints</c> so the
/// probe and its answer live in exactly one file, which <c>OntologyAdminCallerBoundaryGuardTests</c> (rule A) treats as the only
/// file allowed to touch them.
/// </summary>
/// <remarks>
/// <para><b>What the answer is, and is not (D-22).</b> Display data: the Console registers the "Ontology admin" workspace tab only
/// when it is true. It decides what is SHOWN, never what is ALLOWED: every admin write route runs as the caller through
/// <see cref="IDataverseUserClient"/>, so Dataverse enforces the Spaarke Ontology Administrator role on each write and the audit
/// names the human. Nothing in the BFF may read this answer to authorize anything; a route that did would make the probe the
/// security boundary, which D-22 forbids. The route file reaches this class through ONE named entry point,
/// <see cref="ResolveAsync"/>, called once, by the capability handler. The guard fails on a second call, and on any other member of
/// this class (<c>ProbeAsync</c>, the cache helpers, the cache resource) named outside this file.</para>
/// <para><b>How it asks (verified live on spaarkedev1, 2026-10-16).</b> <c>WhoAmI()</c> as the caller names their
/// <c>systemuserid</c>; then <c>RetrieveUserPrivilegeByPrivilegeName</c> on that user, also as the caller. The function includes
/// privileges the user holds through a team's roles (checked live on a team-only privilege), and a Spaarke Console User may call it
/// about themselves: as Chelsea Friez (Core + Console + Office Add In User, plus the default team) it answered 200 with no rows, as
/// the owner 200 with the Global row. The id the function is asked about is the one <c>WhoAmI</c> returned, never a request value,
/// so the probe can only describe the caller.</para>
/// <para><b>Cache (D-22 constraint; ADR-009).</b> A clean answer (true or false) is kept in the tenant cache per caller (the token's
/// <c>oid</c>) for at most the token's remaining lifetime, and never longer than <see cref="MaxCacheLifetime"/> (ADR-009: short TTLs
/// for security data). The cached value is display data, not an authorization decision. A fault is never cached; a token without
/// <c>tid</c>, <c>oid</c> or <c>exp</c> is never cached.</para>
/// </remarks>
internal static class OntologyAdminCapabilityProbe
{
    /// <summary>The privilege the probe asks about: creating a policy version is what authoring a rule means (D-22).</summary>
    internal const string AuthorPoliciesPrivilege = "prvCreatesprk_PolicyVersion";

    /// <summary>Tenant-cache resource name; the id is the caller's Entra object id.</summary>
    internal const string CacheResource = "ontology-admin-capability";

    /// <summary>Tenant-cache payload version (ADR-009 key versioning).</summary>
    internal const int CacheVersion = 1;

    /// <summary>The longest a capability answer is cached, whatever the token's lifetime.</summary>
    internal static readonly TimeSpan MaxCacheLifetime = TimeSpan.FromMinutes(5);

    /// <summary>What <see cref="ResolveAsync"/> found: the answer, or why there is none.</summary>
    internal readonly record struct Resolution(OntologyAdminProbeOutcome Outcome, OntologyAdminCapabilities? Capabilities);

    /// <summary>
    /// The one entry point the route file uses: the caller's capability, from the tenant cache or, on a miss, from Dataverse as the
    /// caller. A clean answer is cached; a fault never is.
    /// </summary>
    internal static async Task<Resolution> ResolveAsync(
        ClaimsPrincipal principal,
        IDataverseUserClient user,
        ITenantCache cache,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct)
    {
        var tenantId = TenantResolution.ResolveTenantId(principal);
        var objectId = CallerResolution.ResolveObjectId(principal);
        var cacheLifetime = CacheLifetime(principal, clock.GetUtcNow());
        var cacheable = tenantId is not null && objectId is not null && cacheLifetime is not null;

        if (cacheable)
        {
            var cached = await TryGetCachedAsync(cache, tenantId!, objectId!, logger, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                return new Resolution(OntologyAdminProbeOutcome.Answered, cached);
            }
        }

        var probe = await ProbeAsync(user, logger, ct).ConfigureAwait(false);
        if (probe.Outcome != OntologyAdminProbeOutcome.Answered)
        {
            return new Resolution(probe.Outcome, null);
        }

        var body = new OntologyAdminCapabilities(probe.CanAuthorPolicies);
        if (cacheable)
        {
            await TrySetCachedAsync(cache, tenantId!, objectId!, body, cacheLifetime!.Value, logger, ct).ConfigureAwait(false);
        }

        return new Resolution(OntologyAdminProbeOutcome.Answered, body);
    }

    internal readonly record struct ProbeResult(OntologyAdminProbeOutcome Outcome, bool CanAuthorPolicies);

    /// <summary>
    /// The two reads, both AS THE CALLER: <c>WhoAmI()</c>, then the privilege function on the id it returned. Unresolved is the
    /// same set the worklist uses (no user context, OBO not configured or refused, Dataverse refusing <c>WhoAmI</c>, or no id).
    /// </summary>
    internal static async Task<ProbeResult> ProbeAsync(IDataverseUserClient user, ILogger logger, CancellationToken ct)
    {
        try
        {
            var who = await user.GetAsync("WhoAmI()", ct).ConfigureAwait(false);
            if (!who.IsSuccess || !TryReadUserId(who.Body, out var callerId))
            {
                if (who.IsSuccess || IsCallerUnresolved(who.ErrorCode))
                {
                    logger.LogWarning("Ontology admin capability: the caller could not be resolved ({ErrorCode}); single 403.",
                        who.ErrorCode ?? "no-user-id");
                    return new ProbeResult(OntologyAdminProbeOutcome.CallerUnresolved, false);
                }

                logger.LogWarning("Ontology admin capability: the caller's identity could not be checked ({ErrorCode}); unavailable.",
                    who.ErrorCode);
                return new ProbeResult(OntologyAdminProbeOutcome.Unavailable, false);
            }

            var privilege = await user.GetAsync(PrivilegeQuery(callerId), ct).ConfigureAwait(false);
            if (!privilege.IsSuccess || !TryReadHeld(privilege.Body, out var held))
            {
                logger.LogWarning("Ontology admin capability: {Privilege} could not be read for the caller ({ErrorCode}); unavailable.",
                    AuthorPoliciesPrivilege, privilege.ErrorCode ?? "unparseable-body");
                return new ProbeResult(OntologyAdminProbeOutcome.Unavailable, false);
            }

            return new ProbeResult(OntologyAdminProbeOutcome.Answered, held);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A transport timeout is a TaskCanceledException while the request's own token is not cancelled: a fault, never a 500.
            logger.LogWarning("Ontology admin capability: the privilege read threw ({ExceptionType}); unavailable.", ex.GetType().Name);
            return new ProbeResult(OntologyAdminProbeOutcome.Unavailable, false);
        }
    }

    /// <summary>The privilege function, bound to the caller's own <c>systemuser</c> row.</summary>
    internal static string PrivilegeQuery(Guid callerSystemUserId) =>
        $"systemusers({callerSystemUserId:D})/Microsoft.Dynamics.CRM.RetrieveUserPrivilegeByPrivilegeName(PrivilegeName='{AuthorPoliciesPrivilege}')";

    /// <summary>
    /// How long a capability answer may be cached for this principal: the token's remaining lifetime (<c>exp</c>), capped at
    /// <see cref="MaxCacheLifetime"/>; <see langword="null"/> (do not cache) when the token carries no readable <c>exp</c> or has
    /// already expired.
    /// </summary>
    internal static TimeSpan? CacheLifetime(ClaimsPrincipal principal, DateTimeOffset now)
    {
        var exp = principal.FindFirst("exp")?.Value;
        if (!long.TryParse(exp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        DateTimeOffset expiresAt;
        try
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        var remaining = expiresAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }

        return remaining < MaxCacheLifetime ? remaining : MaxCacheLifetime;
    }

    private static async Task<OntologyAdminCapabilities?> TryGetCachedAsync(
        ITenantCache cache, string tenantId, string objectId, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await cache.GetAsync<OntologyAdminCapabilities>(tenantId, CacheResource, objectId, CacheVersion, ct: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A cache fault falls through to Dataverse: the cache only saves two reads.
            logger.LogWarning("Ontology admin capability: cache read failed ({ExceptionType}); asking Dataverse.", ex.GetType().Name);
            return null;
        }
    }

    private static async Task TrySetCachedAsync(
        ITenantCache cache, string tenantId, string objectId, OntologyAdminCapabilities value, TimeSpan ttl, ILogger logger, CancellationToken ct)
    {
        try
        {
            await cache.SetAsync(tenantId, CacheResource, objectId, CacheVersion, value, ttl, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Ontology admin capability: cache write failed ({ExceptionType}); answer returned uncached.", ex.GetType().Name);
        }
    }

    private static bool IsCallerUnresolved(string? code) =>
        code is DataverseUserClientErrorCodes.UserContextRequired
            or DataverseUserClientErrorCodes.OboExchangeFailed
            or DataverseUserClientErrorCodes.OboNotConfigured
            or DataverseUserClientErrorCodes.AccessDenied;

    private static bool TryReadUserId(JsonElement? body, out Guid userId)
    {
        userId = Guid.Empty;
        return body is { ValueKind: JsonValueKind.Object } b
            && b.TryGetProperty("UserId", out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out userId)
            && userId != Guid.Empty;
    }

    /// <summary>
    /// Held when the function returns at least one row for the privilege at any depth; an empty <c>RolePrivileges</c> is a clean
    /// "not held". A body without the array is unparseable, never "not held".
    /// </summary>
    private static bool TryReadHeld(JsonElement? body, out bool held)
    {
        held = false;
        if (body is not { ValueKind: JsonValueKind.Object } b
            || !b.TryGetProperty("RolePrivileges", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object
                && row.TryGetProperty("PrivilegeName", out var name)
                && string.Equals(name.GetString(), AuthorPoliciesPrivilege, StringComparison.OrdinalIgnoreCase))
            {
                held = true;
                break;
            }
        }

        return true;
    }
}

/// <summary>How a capability lookup ended: answered, the caller could not be resolved (single 403), or the privilege could not be read (503).</summary>
internal enum OntologyAdminProbeOutcome
{
    Answered,
    CallerUnresolved,
    Unavailable,
}

/// <summary>The caller's Ontology admin capability set: booleans only (D-22).</summary>
/// <param name="CanAuthorPolicies">True when the caller holds <c>prvCreatesprk_PolicyVersion</c>; the Console then shows the tab.</param>
public sealed record OntologyAdminCapabilities(bool CanAuthorPolicies);
