// spaarke-SPA-external-access-platform-r2 Task 015 (2026-08-06) — FR-22 module-framework generalization.
//
// Generalizes the teams-app-r1 CallerPrincipalResolver seam (ADR-028 Amendment A3) into a per-module
// (widget) REGISTRATION framework: each module registers its Tier-2 record predicate over the SHARED,
// principal-agnostic CallerPrincipal. The identity PLANE strategies (CIAM / workforce) remain shared
// across every module via ICallerPrincipalResolver — a module does NOT own a plane; it owns a
// per-module RECORD SCOPE (R2 NFR-08). This is the "add a module = register { plane strategy (shared),
// Tier-2 predicate }" seam FR-22 asked for.
//
// A3 canonical rule realized here: "per-module record-scope is a Tier-2 predicate composed into
// CallerPrincipal.ProjectAccess." The descriptor's AccessibleRecordIds delegate IS that pluggable
// predicate; it reads ONLY the already-resolved CallerPrincipal, so it is plane-agnostic (the CIAM and
// workforce strategies already composed their record scope onto CallerPrincipal) and never branches on
// plane / scheme / iss / tid.
//
// Broker-only (ADR-028 A1/A2/A3 · NFR-02): the predicate is a pure in-memory read of the resolved
// principal — no OBO, no Graph SDK / AI-internal types, no downstream token exchange.
//
// spaarke-SPA-external-access-platform-r2 Task 016 (2026-08-06) — TryGetRecordId EntityReference
// support. A module's RecordIdAttribute need not be the module's own primary-id attribute: it is
// "the attribute on each row checked for membership in AccessibleRecordIds(principal)". Task 016
// registers document/invoice/work-assignment modules whose Tier-2 scope is derived from the
// caller's ACCESSIBLE PROJECT ids (already precomputed on CallerPrincipal — no extra I/O), by
// pointing RecordIdAttribute at the child entity's typed lookup BACK to sprk_project
// (sprk_document.sprk_project, sprk_invoice.sprk_project, sprk_workassignment.sprk_regardingproject)
// instead of the child's own primary id. A FetchXML lookup attribute is projected by the Dataverse
// SDK as an <see cref="Microsoft.Xrm.Sdk.EntityReference"/> (not a raw Guid/string), so
// TryGetRecordId gained an EntityReference case below — purely additive, the existing Guid/string
// cases (used by the collaboration/sprk_project module's own-primary-id scoping) are unchanged.

using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// One Tier-2 scope dimension of a module (spaarke-SPA-external-access-platform-r2 task 028 — polymorphic
/// scoping): a row is accessible if, for ANY of the module's dimensions, the row's <see cref="Attribute"/>
/// value is in that dimension's accessible-id set. A ROOT module has one dimension whose attribute is the
/// entity's own primary id (e.g. <c>sprk_projectid</c>); a CHILD module (documents/invoices) has one
/// dimension per typed parent lookup (<c>sprk_project</c> / <c>sprk_matter</c> / <c>sprk_workassignment</c>),
/// OR'd, so a child rolls up to ANY accessible root.
/// </summary>
public sealed class ScopeDimension
{
    /// <summary>The row attribute checked for membership — a root's own primary id, or a child's typed
    /// lookup back to a root.</summary>
    public required string Attribute { get; init; }

    /// <summary>The accessible root ids for this dimension, read from the resolved (plane-agnostic)
    /// principal. A principal with no accessible roots of this type yields an empty set.</summary>
    public required Func<CallerPrincipal, IReadOnlySet<Guid>> AccessibleIds { get; init; }
}

/// <summary>
/// A registered module (widget) on the dual-plane external module-host platform (ADR-028 A3 / FR-22).
/// Declares the module's Tier-2 record predicate — the per-module accessible-record scope (R2 NFR-08) —
/// over the shared, plane-agnostic <see cref="CallerPrincipal"/>. Registering a module is purely
/// additive: it does NOT touch the resolver, the strategies, the group filter, or any handler.
/// </summary>
/// <remarks>
/// Task 028 generalized the single-attribute predicate to a LIST of OR'd <see cref="ScopeDimension"/>s
/// (a child can roll up to a project OR matter OR work-assignment root). The single-attribute shorthand
/// (<see cref="RecordIdAttribute"/> + <see cref="AccessibleRecordIds"/>) is retained for root/config
/// modules whose scope is one dimension — it materializes as a one-element <see cref="EffectiveDimensions"/>.
/// </remarks>
public sealed class ExternalModuleDescriptor
{
    /// <summary>Stable module/widget key (e.g. <c>collaboration</c>, <c>assigned-work</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The Dataverse entity logical name whose records this module reads (Tier-2 entity).</summary>
    public required string RecordEntity { get; init; }

    /// <summary>Single-dimension shorthand: the attribute whose value is checked for membership in
    /// <see cref="AccessibleRecordIds"/> (a root's own primary id, or one child lookup). Set this +
    /// <see cref="AccessibleRecordIds"/> for a one-dimension module, OR set <see cref="ScopeDimensions"/>
    /// for a multi-parent (OR) module — not both.</summary>
    public string? RecordIdAttribute { get; init; }

    /// <summary>
    /// Single-dimension shorthand: the set of accessible ids for <see cref="RecordIdAttribute"/>, read
    /// from the plane-agnostic <see cref="CallerPrincipal"/>. Being entitled to the module does NOT imply
    /// access to all records; a principal with no accessible records yields an empty set.
    /// </summary>
    public Func<CallerPrincipal, IReadOnlySet<Guid>>? AccessibleRecordIds { get; init; }

    /// <summary>Multi-dimension form (task 028): the OR'd scope dimensions. A row is accessible if ANY
    /// dimension matches. Use for child modules that roll up to more than one kind of root.</summary>
    public IReadOnlyList<ScopeDimension>? ScopeDimensions { get; init; }

    /// <summary>
    /// The module's resolved scope dimensions — either the explicit <see cref="ScopeDimensions"/> list,
    /// or a one-element list built from the <see cref="RecordIdAttribute"/> + <see cref="AccessibleRecordIds"/>
    /// shorthand. Throws if neither is configured (a wiring bug — fail fast).
    /// </summary>
    public IReadOnlyList<ScopeDimension> EffectiveDimensions
    {
        get
        {
            if (ScopeDimensions is { Count: > 0 })
            {
                return ScopeDimensions;
            }
            if (!string.IsNullOrWhiteSpace(RecordIdAttribute) && AccessibleRecordIds is not null)
            {
                return new[]
                {
                    new ScopeDimension { Attribute = RecordIdAttribute, AccessibleIds = AccessibleRecordIds }
                };
            }
            throw new InvalidOperationException(
                $"External module '{Name}' must declare either ScopeDimensions or RecordIdAttribute + AccessibleRecordIds.");
        }
    }

    /// <summary>
    /// The Tier-2 enforcement check for a record-scoped read: is <paramref name="recordId"/> in ANY of
    /// the principal's accessible dimension sets? A <c>false</c> result MUST be enforced as a DENY by the
    /// caller. (For a root/config module the single dimension's attribute is the entity's own id, so this
    /// is the direct record∈set check; child single-record reads — not used by the read-only grids — fail
    /// closed because a child's own id is never in a parent-id set.)
    /// </summary>
    public bool IsRecordAccessible(CallerPrincipal principal, Guid recordId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (recordId == Guid.Empty)
        {
            return false;
        }
        foreach (var dim in EffectiveDimensions)
        {
            if (dim.AccessibleIds(principal).Contains(recordId))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Tier-2-scopes a fetch result: keeps ONLY the rows whose entity matches <see cref="RecordEntity"/>
    /// (defense-in-depth against a FetchXML that queries a different primary entity than the module claims)
    /// AND for which SOME dimension's attribute value is in that dimension's accessible set. A principal
    /// with an empty accessible set across ALL dimensions receives an empty result — never the unscoped
    /// rows (NFR-08 negative case).
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ScopeRows(
        CallerPrincipal principal, IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(rows);

        // Materialize each dimension's accessible set once. Fail-closed: if EVERY dimension is empty the
        // caller can see nothing in this module — return nothing.
        var resolved = EffectiveDimensions
            .Select(d => (d.Attribute, Ids: d.AccessibleIds(principal)))
            .ToList();
        var scoped = new List<IReadOnlyDictionary<string, object?>>();
        if (resolved.All(d => d.Ids.Count == 0))
        {
            return scoped;
        }

        foreach (var row in rows)
        {
            if (!RowMatchesEntity(row))
            {
                continue;
            }
            foreach (var (attribute, ids) in resolved)
            {
                if (ids.Count > 0 && TryGetAttributeId(row, attribute, out var id) && ids.Contains(id))
                {
                    scoped.Add(row);
                    break; // OR across dimensions — one match is enough.
                }
            }
        }

        return scoped;
    }

    /// <summary>
    /// Confirms a projected fetch row's primary entity matches <see cref="RecordEntity"/> using the
    /// synthetic <c>@logicalName</c> key the fetch projection always emits. Fail-closed: a row that does
    /// not positively confirm its entity (missing/blank <c>@logicalName</c>) is dropped — a scoping
    /// function must never trust a row it cannot verify.
    /// </summary>
    private bool RowMatchesEntity(IReadOnlyDictionary<string, object?> row)
    {
        if (row is null)
        {
            return false;
        }

        return row.TryGetValue("@logicalName", out var raw) && raw is string logicalName &&
               string.Equals(logicalName, RecordEntity, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a Guid id from the row's <paramref name="attribute"/>, tolerating the Guid / EntityReference
    /// / string shapes the fetch projection can carry. Returns <c>false</c> for a missing/empty/unparseable
    /// value. (A root module reads its own primary id; a child module reads a typed parent lookup — which
    /// the SDK projects as an <see cref="EntityReference"/>.)
    /// </summary>
    internal static bool TryGetAttributeId(IReadOnlyDictionary<string, object?> row, string attribute, out Guid id)
    {
        id = Guid.Empty;
        if (row is null || string.IsNullOrWhiteSpace(attribute) ||
            !row.TryGetValue(attribute, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case Guid g:
                id = g;
                return g != Guid.Empty;
            case EntityReference er:
                // Lookup-typed attributes (e.g. a child entity's FK back to sprk_project) are
                // projected by the SDK as EntityReference, not a raw Guid — see file-header note.
                id = er.Id;
                return er.Id != Guid.Empty;
            case string s when Guid.TryParse(s, out var parsed):
                id = parsed;
                return parsed != Guid.Empty;
            default:
                return Guid.TryParse(raw.ToString(), out id) && id != Guid.Empty;
        }
    }
}

/// <summary>
/// The per-module registry for the external module-host platform (FR-22). The read-data endpoint group
/// resolves the module by its record entity to apply that module's Tier-2 predicate; each module is a
/// registered <see cref="ExternalModuleDescriptor"/>. Registering an additional module (or plane) is
/// purely additive — no route, filter, or handler changes (ADR-028 A3).
/// </summary>
/// <remarks>
/// ADR-010: registered as a concrete singleton (no interface) — a single implementation whose pluggable
/// seam is the per-module descriptor delegates, mirroring the concrete-registration precedent of the
/// other external-access services. Populated once at startup via <c>AddExternalModule</c>; read-only
/// (thread-safe) thereafter.
/// </remarks>
public sealed class ExternalModuleRegistry
{
    private readonly Dictionary<string, ExternalModuleDescriptor> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExternalModuleDescriptor> _byEntity =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All registered modules.</summary>
    public IReadOnlyCollection<ExternalModuleDescriptor> Modules => _byName.Values;

    /// <summary>
    /// Registers a module. Throws on a duplicate module name OR a duplicate record entity — each module
    /// owns a distinct name and a distinct Tier-2 entity, so a collision is a wiring bug (fail-fast).
    /// </summary>
    public void Register(ExternalModuleDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Name))
        {
            throw new ArgumentException("Module Name is required.", nameof(descriptor));
        }
        if (string.IsNullOrWhiteSpace(descriptor.RecordEntity))
        {
            throw new ArgumentException("Module RecordEntity is required.", nameof(descriptor));
        }
        // Validate the scope model resolves (either ScopeDimensions or the single-attribute shorthand) —
        // EffectiveDimensions throws a descriptive InvalidOperationException if neither is configured.
        _ = descriptor.EffectiveDimensions;
        if (!_byName.TryAdd(descriptor.Name, descriptor))
        {
            throw new InvalidOperationException(
                $"An external module named '{descriptor.Name}' is already registered.");
        }
        if (!_byEntity.TryAdd(descriptor.RecordEntity, descriptor))
        {
            _byName.Remove(descriptor.Name);
            throw new InvalidOperationException(
                $"An external module for entity '{descriptor.RecordEntity}' is already registered " +
                $"(by module '{_byEntity[descriptor.RecordEntity].Name}').");
        }
    }

    /// <summary>Finds a registered module by its stable name, or null.</summary>
    public ExternalModuleDescriptor? FindByName(string name) =>
        !string.IsNullOrWhiteSpace(name) && _byName.TryGetValue(name, out var d) ? d : null;

    /// <summary>Finds the module that owns the given record entity, or null.</summary>
    public ExternalModuleDescriptor? FindByEntity(string entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && _byEntity.TryGetValue(entityLogicalName, out var d)
            ? d
            : null;
}
