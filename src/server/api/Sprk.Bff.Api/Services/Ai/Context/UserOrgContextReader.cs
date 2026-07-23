// spaarkeai-assistant-enhancements-r1 FR-E5 BU/team enrichment (un-defer D-032-01): the BU/team half of
// FR-E5. Resolves the caller's business-unit + team NAMES and returns them as preference/context so a
// profiled turn can carry "this user is in BU X, on teams Y/Z". Read (this) ≠ render
// (UserOrgContextRenderer); ContextBinder composes the rendered block into the User slice's
// ContextEnvelope.userFragment as its OWN deterministic block (after the stated-profile block, before the
// memory-recall block). SIBLING of IStatedProfileReader / the user-memory RECALL fragment.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — nothing reads the caller's BU/team NAMES into a prompt. IIdentityNormalizationService
//       resolves BusinessUnitId + TeamIds[] (Redis-cached IDs, not names); IStatedProfileReader reads the
//       typed sprk_userprofile row (a different source — ADR-042 stated facts, not org membership).
//   (2) Extension — this REUSES IIdentityNormalizationService for the identity resolution (the SAME
//       server-resolved systemuserid; no second identity mechanism per the FR-E5 constraint) and only adds
//       the cheap name-resolution + its own hot-path cache. It is NOT jammed into the StatedProfile record
//       (different source), NOT folded into PersonIdentity (that would widen a shared hot path — forcing
//       name reads on every membership-resolution consumer + a cache-schema bump), and NOT the deferred
//       IOrganizationalContextProvider (that is inbound org-scope context for grounding — this is
//       preference-only prompt bias that NEVER reaches AgentToolFilterContext; ADR-039).
//   (3) Cost-of-doing-nothing — a profiled turn cannot carry the caller's BU/team context to bias the one
//       agent turn's wording; the BU/team half of FR-E5 stays unshipped.
//
// Placement Justification (bff-extensions.md): lives in Services/Ai/Context/ (ADR-013 in-zone AI code)
// alongside the Context Binder — its ONE consumer — exactly like IStatedProfileReader /
// ICallerSystemUserResolver. Latency-coupled to the per-turn bind (runs inside ContextBinder.BindAsync),
// so it belongs in the BFF, not a separate service. Additive C# only (no new package → no new CVE;
// ~0 publish-size delta). NFR-03: the name resolution is a per-turn hot-path read, so it is Redis-cached
// via ITenantCache with the SAME per-systemuserid, 10-minute-TTL pattern IIdentityNormalizationService
// uses (ADR-009). ADR-039: preference-only — MUST NOT touch AgentToolFilterContext / grounding / dispatch.

using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Services.Ai.Context;

/// <summary>
/// Resolves the caller's business-unit + team NAMES (keyed by the resolved Dataverse
/// <c>systemuserid</c>). Reuses <see cref="IIdentityNormalizationService"/> for the (Redis-cached)
/// <c>BusinessUnitId</c> + <c>TeamIds[]</c> and adds the cheap name reads on top, caching the resolved
/// names per-systemuserid for 10 minutes (ADR-009). Server-side only: the systemuserid is always the
/// output of <see cref="ContextBinder"/>'s deterministic claims→systemuser resolution (ADR-028), never a
/// client Arg or an LLM completion. Consumed by <see cref="ContextBinder"/> to produce the org block folded
/// into the User slice's <c>userFragment</c> — preference/context ONLY (ADR-039), never grounding.
/// </summary>
public interface IUserOrgContextReader
{
    /// <summary>
    /// Reads the caller's business-unit + team names for <paramref name="systemUserId"/> (a Dataverse
    /// <c>systemuserid</c>, "D"-format GUID string). Returns <c>null</c> when the user has no BU/team,
    /// the id is not a valid GUID, or any read fails — the caller degrades the org block to absent
    /// (soft-fail; the bind is never taken down by this read).
    /// </summary>
    Task<UserOrgContext?> ReadAsync(string systemUserId, CancellationToken ct);
}

/// <summary>
/// The caller's resolved organizational context (FR-E5 BU/team half). Team names are already sorted
/// Ordinal by the reader so the rendered block is byte-stable turn-to-turn. Rendering into a prompt block
/// is <see cref="UserOrgContextRenderer"/>'s concern (read ≠ render).
/// </summary>
public sealed record UserOrgContext
{
    /// <summary>The caller's business-unit primary name (<c>businessunit.name</c>). Null/blank when unresolved.</summary>
    public string? BusinessUnitName { get; init; }

    /// <summary>The caller's team names (<c>team.name</c>), sorted Ordinal. Empty when the user is on no teams.</summary>
    public IReadOnlyList<string> TeamNames { get; init; } = Array.Empty<string>();

    /// <summary>True when at least one org fact is present (otherwise the block renders to nothing).</summary>
    public bool HasAny => !string.IsNullOrWhiteSpace(BusinessUnitName) || TeamNames.Count > 0;
}

/// <summary>Default <see cref="IUserOrgContextReader"/> — see file header for Component/Placement Justification.</summary>
public sealed class UserOrgContextReader : IUserOrgContextReader
{
    /// <summary>Cache resource label (per ITenantCache contract) — on-wire key <c>tenant:{t}:user-org-context:{userId:D}:v1</c>.</summary>
    internal const string CacheResource = "user-org-context";

    /// <summary>Cache schema version per ADR-009.</summary>
    private const int CacheVersion = 1;

    /// <summary>Redis TTL — mirrors <see cref="IIdentityNormalizationService"/>'s 10-minute per-user TTL (ADR-009, NFR-03).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private const string BusinessUnitEntity = "businessunit";
    private const string BusinessUnitNameColumn = "name";
    private const string TeamEntity = "team";
    private const string TeamIdColumn = "teamid";
    private const string TeamNameColumn = "name";

    private readonly IIdentityNormalizationService _identity;
    private readonly IDataverseService _dataverse;
    private readonly ITenantCache _cache;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<UserOrgContextReader> _logger;

    public UserOrgContextReader(
        IIdentityNormalizationService identity,
        IDataverseService dataverse,
        ITenantCache cache,
        ILogger<UserOrgContextReader> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public async Task<UserOrgContext?> ReadAsync(string systemUserId, CancellationToken ct)
    {
        if (!Guid.TryParse(systemUserId, out var systemUserGuid) || systemUserGuid == Guid.Empty)
        {
            return null;
        }

        try
        {
            // NFR-03: cache the resolved names per-systemuserid (10-min TTL) so the name reads run at most
            // once per user per 10 minutes on the hot bind path. An empty result is cached too (it is a
            // non-null UserOrgContext) so an org-less user does not re-read every turn.
            var context = await _cache.GetOrCreateAsync(
                GetTenantId(),
                CacheResource,
                systemUserGuid.ToString("D"),
                CacheVersion,
                token => ResolveFreshAsync(systemUserGuid, token),
                CacheTtl,
                ct: ct).ConfigureAwait(false);

            return context.HasAny ? context : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Soft-fail-to-null: an identity/name read (or cache) failure degrades the org block to absent —
            // a bind is NEVER taken down by this read (mirrors the stated-profile soft-fail). NFR-07.
            _logger.LogWarning(ex,
                "UserOrgContextReader: BU/team name resolution failed for the resolved caller — the org " +
                "block degrades to absent (soft-fail; a bind is never taken down by this read). NFR-03/NFR-07.");
            return null;
        }
    }

    /// <summary>
    /// Resolves a fresh (uncached) <see cref="UserOrgContext"/>. Reuses the cached identity resolution for
    /// the BU/team IDs, then resolves the names. Always returns a non-null value (possibly empty) so the
    /// cache can memoize the org-less case as well.
    /// </summary>
    private async Task<UserOrgContext> ResolveFreshAsync(Guid systemUserGuid, CancellationToken ct)
    {
        var identity = await _identity.ResolveAsync(systemUserGuid, ct).ConfigureAwait(false);

        var businessUnitName = identity.BusinessUnitId is { } businessUnitId
            ? await ResolveBusinessUnitNameAsync(businessUnitId, ct).ConfigureAwait(false)
            : null;

        var teamNames = await ResolveTeamNamesAsync(identity.TeamIds, ct).ConfigureAwait(false);

        return new UserOrgContext
        {
            BusinessUnitName = businessUnitName,
            TeamNames = teamNames,
        };
    }

    private async Task<string?> ResolveBusinessUnitNameAsync(Guid businessUnitId, CancellationToken ct)
    {
        var entity = await _dataverse
            .RetrieveAsync(BusinessUnitEntity, businessUnitId, new[] { BusinessUnitNameColumn }, ct)
            .ConfigureAwait(false);

        var name = entity?.GetAttributeValue<string>(BusinessUnitNameColumn);
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    /// <summary>
    /// Resolves the team names for <paramref name="teamIds"/> in a single keyed read, sorted Ordinal so the
    /// rendered block is byte-stable turn-to-turn (the reader owns the sort; the renderer preserves order).
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveTeamNamesAsync(
        IReadOnlyList<Guid> teamIds, CancellationToken ct)
    {
        if (teamIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var query = new QueryExpression(TeamEntity)
        {
            ColumnSet = new ColumnSet(TeamNameColumn),
            NoLock = true,
        };
        query.Criteria.AddCondition(
            TeamIdColumn, ConditionOperator.In, teamIds.Select(id => (object)id).ToArray());

        var result = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (result?.Entities is null || result.Entities.Count == 0)
        {
            return Array.Empty<string>();
        }

        return result.Entities
            .Select(e => e.GetAttributeValue<string>(TeamNameColumn))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Tenant id for the tenant-scoped cache key (FR-05) — reads the AAD <c>tid</c> claim from the current
    /// HttpContext per ADR-028; falls back to <c>"anonymous"</c> when no HttpContext is available. Mirrors
    /// <see cref="IIdentityNormalizationService"/>'s tenant derivation so both caches key consistently.
    /// </summary>
    private string GetTenantId()
        => _httpContextAccessor?.HttpContext?.User?.FindFirst("tid")?.Value
            ?? _httpContextAccessor?.HttpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? "anonymous";
}

/// <summary>
/// ADR-032 Null-Object default for <see cref="IUserOrgContextReader"/> — used by <see cref="ContextBinder"/>
/// when no real reader is DI-registered (or a caller/test constructs <see cref="ContextBinder"/> directly
/// without supplying one). Always returns <c>null</c> (P2 quiet no-op) so the org block degrades to absent
/// rather than the Binder throwing.
/// </summary>
public sealed class NullUserOrgContextReader : IUserOrgContextReader
{
    /// <summary>Shared stateless instance (the reader holds no per-call state).</summary>
    public static readonly NullUserOrgContextReader Instance = new();

    public Task<UserOrgContext?> ReadAsync(string systemUserId, CancellationToken ct) =>
        Task.FromResult<UserOrgContext?>(null);
}
