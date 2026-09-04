using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Derives the ultimate CORE-record ancestor of a regarding target so server-created child records carry
/// the same FR-26 stamp the client write path produces. The C# mirror of
/// <c>PolymorphicResolverService.deriveCoreAncestorStamps</c> in <c>@spaarke/ui-components</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The evaluator's child-inheritance term is a set-membership test —
/// <c>child.sprk_regarding{core} ∈ {accessible core ids}</c> — and it can only read a lookup the child ROW
/// already carries; the <c>ScopeDimension</c> shape is a synchronous
/// <c>Func&lt;CallerPrincipal, IReadOnlySet&lt;Guid&gt;&gt;</c> with no Dataverse round-trip by design. So a
/// <c>todo → communication → matter</c> chain is inexpressible unless the ultimate core ancestor is
/// denormalized onto the child at write time. That denormalized stamp is what keeps every chain ONE hop,
/// which is why ADR-034's 1-hop cap holds here unamended.
/// </para>
/// <para>
/// <b>Client parity is the point.</b> FR-26 acceptance covers ALL chains, not just PCF-authored ones. If a
/// server writer skips the stamp, FR-27 inheritance silently under-grants for exactly those records — a
/// contact with Project access does not see server-filed emails on the project — and the one-time backfill
/// (task 053) would permanently miss them going forward. The taxonomy literals below are pinned by a test on
/// BOTH sides so the two implementations cannot drift.
/// </para>
/// <para>
/// <b>Two rules that are easy to get backwards.</b>
/// (1) Matter does NOT inherit from Project — both are CORE, and selecting a core target stamps only that
/// target. Inverting it hands every Project holder every Matter beneath it.
/// (2) Derivation is exactly one hop: it reads the target's own core-ancestor lookups and stops. Those
/// columns are themselves FR-26 stamps written when the target was saved, which is why one read suffices.
/// </para>
/// <para>
/// <b>Placement (CLAUDE.md §10).</b> Lives in <c>Services/Dataverse/</c> rather than
/// <c>Services/Communication/</c> because it must serve every child-entity writer — todo, event, analysis,
/// document, invoice — not just the communication pipeline. Folding it into <c>ThreadResolver</c> would
/// couple those writers to the communication module. It adds no package, no endpoint, and no new interface
/// (ADR-010): reads go through the already-registered <see cref="IGenericEntityService"/>, and column
/// presence through a delegate seam rather than a fresh abstraction.
/// </para>
/// <para>
/// See <c>projects/unified-access-control-r2/notes/phase3-derivation-rules.md</c> for the rules table and
/// <c>notes/phase3-server-writers.md</c> for the writer inventory.
/// </para>
/// </remarks>
public sealed class CoreAncestorResolver
{
    /// <summary>
    /// CORE record entities — direct grants required; these never inherit.
    /// <b>Pinned literally by test, and MUST equal the TypeScript <c>CORE_RECORD_ENTITIES</c>.</b>
    /// Changing this set changes who can see what.
    /// </summary>
    public static readonly IReadOnlyList<string> CoreRecordEntities =
    [
        "sprk_project",
        "sprk_matter",
        "sprk_workassignment",
        "sprk_servicerequest",
    ];

    /// <summary>
    /// CHILD record entities — inherit their core ancestor's rights in one hop, via the stamp this resolver
    /// derives. <b>Pinned literally by test; MUST equal the TypeScript <c>CHILD_RECORD_ENTITIES</c>.</b>
    /// </summary>
    /// <remarks>
    /// Entities in NEITHER set (<c>sprk_budget</c>, <c>sprk_organization</c>, <c>contact</c>,
    /// <c>account</c>, <c>sprk_reportcard</c>) are intentionally unclassified for FR-26 — they confer access
    /// through other evaluator terms, never through core-ancestor inheritance. That is a distinct,
    /// non-error state; see <see cref="CoreAncestorStatus.Unclassified"/>.
    /// </remarks>
    public static readonly IReadOnlyList<string> ChildRecordEntities =
    [
        "sprk_invoice",
        "sprk_communication",
        "sprk_document",
        "sprk_event",
        "sprk_todo",
        "sprk_analysis",
    ];

    /// <summary>
    /// The lookup column that carries each CORE entity's stamp on a child row. These four are the ONLY
    /// access-conferring ancestor lookups — any other <c>sprk_regarding*</c> column is a relationship, not an
    /// access edge.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not every child entity carries all four: <c>sprk_todo</c> has no <c>sprk_regardingservicerequest</c>
    /// while <c>sprk_communication</c> does. Presence is therefore always resolved against live metadata, never
    /// assumed — reading a non-existent column would fault and turn a schema gap into a blocked write.
    /// </remarks>
    public static readonly IReadOnlyList<(string EntityType, string LookupAttribute)> CoreAncestorLookups =
    [
        ("sprk_project", "sprk_regardingproject"),
        ("sprk_matter", "sprk_regardingmatter"),
        ("sprk_workassignment", "sprk_regardingworkassignment"),
        ("sprk_servicerequest", "sprk_regardingservicerequest"),
    ];

    private static readonly HashSet<string> CoreSet = new(CoreRecordEntities, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ChildSet = new(ChildRecordEntities, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the entity is a CORE record (direct grants required).</summary>
    public static bool IsCoreRecordEntity(string entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && CoreSet.Contains(entityLogicalName);

    /// <summary>True when the entity is a CHILD record (inherits via its core ancestor).</summary>
    public static bool IsChildRecordEntity(string entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && ChildSet.Contains(entityLogicalName);

    /// <summary>
    /// Reports which of an entity's columns exist. The test seam for column presence — a delegate rather than
    /// a new interface (ADR-010). Production supplies <see cref="FromMetadata"/>, which is backed by the
    /// already-registered, 6h-cached <see cref="MetadataService"/>.
    /// </summary>
    public delegate Task<IReadOnlySet<string>> EntityColumnProbe(string entityLogicalName, CancellationToken ct);

    private readonly IGenericEntityService _entityService;
    private readonly EntityColumnProbe _columnProbe;
    private readonly ILogger<CoreAncestorResolver> _logger;

    public CoreAncestorResolver(
        IGenericEntityService entityService,
        EntityColumnProbe columnProbe,
        ILogger<CoreAncestorResolver> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _columnProbe = columnProbe ?? throw new ArgumentNullException(nameof(columnProbe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Build an <see cref="EntityColumnProbe"/> backed by <see cref="MetadataService"/>. A metadata failure
    /// surfaces as an exception so the caller fails closed rather than reading an empty column set and
    /// concluding "no ancestor".
    /// </summary>
    public static EntityColumnProbe FromMetadata(MetadataService metadataService)
    {
        ArgumentNullException.ThrowIfNull(metadataService);
        return async (entityLogicalName, ct) =>
        {
            EntityMetadataDto meta = await metadataService.GetMetadataAsync(entityLogicalName, ct).ConfigureAwait(false);
            return new HashSet<string>(
                meta.Attributes.Select(a => a.LogicalName),
                StringComparer.OrdinalIgnoreCase);
        };
    }

    /// <summary>
    /// Resolve the CORE-record ancestor stamp(s) for a regarding target.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CORE target → the target itself, with no read at all. CHILD target → ONE read of the target's own
    /// core-ancestor lookups, then stop (ADR-034: no recursion, no grandparent walk). Anything else →
    /// <see cref="CoreAncestorStatus.Unclassified"/>.
    /// </para>
    /// <para>
    /// <b>Fail closed (NFR-01).</b> A read or metadata failure returns <see cref="CoreAncestorStatus.Error"/>;
    /// callers MUST fail the operation (or queue a retry per their existing error contract) rather than create
    /// a child that silently carries no inherited access. This method does not throw — the status is the
    /// contract, so a caller cannot accidentally swallow the failure in a broad catch.
    /// </para>
    /// <para>
    /// <see cref="CoreAncestorStatus.NoAncestor"/> and <see cref="CoreAncestorStatus.Error"/> are kept
    /// DISTINCT on purpose: "this record inherits nothing" (a legitimate orphan) and "we could not find out"
    /// must never share a branch.
    /// </para>
    /// </remarks>
    public async Task<CoreAncestorResult> ResolveStampsAsync(
        string targetEntityLogicalName,
        Guid targetRecordId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetEntityLogicalName))
        {
            return CoreAncestorResult.Failed("Target entity logical name is required.");
        }

        if (targetRecordId == Guid.Empty)
        {
            // Guid.Empty would silently match nothing and read as "no ancestor". Refuse it explicitly —
            // the same fail-closed-by-construction posture as DataverseImpersonation.
            return CoreAncestorResult.Failed(
                $"Target record id for '{targetEntityLogicalName}' is Guid.Empty; refusing to derive an ancestor.");
        }

        // --- CORE target: the target IS the ancestor. Its own parent associations are NOT ancestors —
        //     a Matter associated to a Project does not inherit from it (design.md §4.3). No read.
        if (IsCoreRecordEntity(targetEntityLogicalName))
        {
            var lookup = CoreAncestorLookups
                .First(c => string.Equals(c.EntityType, targetEntityLogicalName, StringComparison.OrdinalIgnoreCase));
            return new CoreAncestorResult(
                CoreAncestorStatus.CoreTarget,
                [new CoreAncestorStamp(lookup.EntityType, lookup.LookupAttribute, targetRecordId)],
                null);
        }

        // --- Neither core nor child: no ancestor concept applies. Not an error.
        if (!IsChildRecordEntity(targetEntityLogicalName))
        {
            return new CoreAncestorResult(CoreAncestorStatus.Unclassified, [], null);
        }

        // --- CHILD target: read ITS core-ancestor stamps. This is the single hop.
        IReadOnlySet<string> columns;
        try
        {
            columns = await _columnProbe(targetEntityLogicalName, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // An empty column set is indistinguishable from "metadata unavailable", and guessing the
            // optimistic branch would write an unstamped child. Fail closed.
            _logger.LogError(ex,
                "Core-ancestor column probe failed for child target {Entity}; failing closed per NFR-01.",
                targetEntityLogicalName);
            return CoreAncestorResult.Failed(
                $"Could not read metadata for child target '{targetEntityLogicalName}': {ex.Message}");
        }

        var applicable = CoreAncestorLookups
            .Where(c => columns.Contains(c.LookupAttribute))
            .ToList();

        if (applicable.Count == 0)
        {
            // Child-class but carries no core-ancestor lookup at all — its chain is already broken upstream.
            _logger.LogWarning(
                "Child target {Entity} has none of the core-ancestor lookups ({Lookups}); no stamp can be derived.",
                targetEntityLogicalName,
                string.Join(", ", CoreAncestorLookups.Select(c => c.LookupAttribute)));
            return new CoreAncestorResult(CoreAncestorStatus.NoAncestor, [], null);
        }

        Entity row;
        try
        {
            row = await _entityService
                .RetrieveAsync(targetEntityLogicalName, targetRecordId, applicable.Select(c => c.LookupAttribute).ToArray(), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Core-ancestor read failed for {Entity}({Id}); failing closed per NFR-01.",
                targetEntityLogicalName, targetRecordId);
            return CoreAncestorResult.Failed(
                $"Failed to read core-ancestor lookups from {targetEntityLogicalName}({targetRecordId}): {ex.Message}");
        }

        if (row is null)
        {
            return CoreAncestorResult.Failed(
                $"Core-ancestor read returned no row for {targetEntityLogicalName}({targetRecordId}).");
        }

        var stamps = new List<CoreAncestorStamp>();
        foreach (var (entityType, lookupAttribute) in applicable)
        {
            var reference = row.GetAttributeValue<EntityReference>(lookupAttribute);
            if (reference is not null && reference.Id != Guid.Empty)
            {
                stamps.Add(new CoreAncestorStamp(entityType, lookupAttribute, reference.Id));
            }
        }

        if (stamps.Count == 0)
        {
            _logger.LogWarning(
                "Child target {Entity}({Id}) carries no core-ancestor stamp; a record written against it will inherit no access.",
                targetEntityLogicalName, targetRecordId);
            return new CoreAncestorResult(CoreAncestorStatus.NoAncestor, [], null);
        }

        return new CoreAncestorResult(CoreAncestorStatus.Derived, stamps, null);
    }

    /// <summary>
    /// Apply derived ancestor stamps to a child entity being written, skipping any the host cannot store.
    /// </summary>
    /// <remarks>
    /// A derived ancestor the host has no column for (a <c>sprk_todo</c> whose ancestor is a Service Request —
    /// <c>sprk_todo</c> has no <c>sprk_regardingservicerequest</c>) is a genuine hole in child inheritance. It is
    /// RETURNED as <c>unstampable</c> and logged, never silently dropped: it is a schema finding for the owner,
    /// not a runtime condition to paper over.
    /// </remarks>
    /// <param name="child">The child entity being built (mutated in place).</param>
    /// <param name="result">Output of <see cref="ResolveStampsAsync"/>.</param>
    /// <param name="hostColumns">Columns that exist on the host entity (from the same probe).</param>
    /// <param name="skipEntityType">The directly-bound target, whose lookup the caller already wrote.</param>
    /// <returns>Lookup attributes that could not be stamped because the host lacks the column.</returns>
    public IReadOnlyList<string> ApplyStamps(
        Entity child,
        CoreAncestorResult result,
        IReadOnlySet<string> hostColumns,
        string? skipEntityType = null)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(hostColumns);

        var unstampable = new List<string>();
        foreach (var stamp in result.Stamps)
        {
            if (skipEntityType is not null &&
                string.Equals(stamp.EntityType, skipEntityType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!hostColumns.Contains(stamp.LookupAttribute))
            {
                unstampable.Add(stamp.LookupAttribute);
                _logger.LogWarning(
                    "Derived core ancestor {Entity}({Id}) cannot be stamped on {Host}: no '{Lookup}' column. " +
                    "This record will NOT inherit that ancestor's access (FR-26 gap).",
                    stamp.EntityType, stamp.RecordId, child.LogicalName, stamp.LookupAttribute);
                continue;
            }

            child[stamp.LookupAttribute] = new EntityReference(stamp.EntityType, stamp.RecordId);
        }

        return unstampable;
    }
}

/// <summary>
/// Outcome of a core-ancestor derivation. A closed set of DISTINCT states — collapsing any two of them
/// produces either blocked valid writes or silently unstamped children.
/// </summary>
public enum CoreAncestorStatus
{
    /// <summary>Target is itself CORE. The stamp is the target; its own parents are NOT ancestors.</summary>
    CoreTarget,

    /// <summary>Target is CHILD and carried at least one core-ancestor stamp.</summary>
    Derived,

    /// <summary>Target is CHILD and every core-ancestor lookup is null. Legitimate; confers nothing.</summary>
    NoAncestor,

    /// <summary>Target is neither CORE nor CHILD (budget, organization, contact, account, report card).</summary>
    Unclassified,

    /// <summary>Derivation failed. The caller MUST NOT create the child unstamped (NFR-01).</summary>
    Error,
}

/// <summary>One resolved core-ancestor stamp to write onto the child being saved.</summary>
public sealed record CoreAncestorStamp(string EntityType, string LookupAttribute, Guid RecordId);

/// <summary>Result of <see cref="CoreAncestorResolver.ResolveStampsAsync"/>.</summary>
public sealed record CoreAncestorResult(
    CoreAncestorStatus Status,
    IReadOnlyList<CoreAncestorStamp> Stamps,
    string? Error)
{
    /// <summary>True when the caller may proceed with the write.</summary>
    public bool Succeeded => Status != CoreAncestorStatus.Error;

    internal static CoreAncestorResult Failed(string error) =>
        new(CoreAncestorStatus.Error, [], error);
}
