using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// The ADR-018 category→team reconciliation-routing gate (FR-E7, task 057). Resolves the owning TEAM name a
/// triage category maps to, re-reading <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> on every call so
/// an operator's config change (add/remove a mapping, flip routing off) takes effect WITHOUT a redeploy.
/// Mirrors <see cref="AutoFileGate"/>.
/// </summary>
/// <remarks>
/// Pure config resolution with no external dependency — registered unconditionally (ADR-010); no Null-Object
/// peer required. It resolves ONLY the team NAME; the caller (the triage-persist path) resolves the name to a
/// team id and performs the ownership set, so this gate stays side-effect-free + trivially testable.
/// </remarks>
public sealed class CategoryRoutingGate
{
    private readonly IOptionsMonitor<CategoryRoutingOptions> _options;

    public CategoryRoutingGate(IOptionsMonitor<CategoryRoutingOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Resolve the owning TEAM name for a triage <paramref name="category"/> name, or <c>null</c> when routing
    /// is disabled, the category is blank, or no mapping exists. A per-tenant override (when
    /// <paramref name="tenantKey"/> is present) replaces the global on-off + map; a null/unknown tenant key
    /// uses the global default. Read fresh from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> each call.
    /// </summary>
    public string? ResolveTeamName(string? category, string? tenantKey = null)
    {
        if (string.IsNullOrWhiteSpace(category))
            return null;

        var o = _options.CurrentValue;
        var enabled = o.Enabled;
        var map = o.CategoryToTeam;

        if (!string.IsNullOrWhiteSpace(tenantKey) &&
            o.Tenants.TryGetValue(tenantKey, out var overrideEntry) &&
            overrideEntry is not null)
        {
            if (overrideEntry.Enabled.HasValue) enabled = overrideEntry.Enabled.Value;
            if (overrideEntry.CategoryToTeam is not null) map = overrideEntry.CategoryToTeam;
        }

        if (!enabled || map is null || map.Count == 0)
            return null;

        return map.TryGetValue(category.Trim(), out var team) && !string.IsNullOrWhiteSpace(team)
            ? team.Trim()
            : null;
    }
}
