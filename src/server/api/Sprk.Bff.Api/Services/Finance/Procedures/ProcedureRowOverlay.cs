using System.Xml;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Procedures;

/// <summary>
/// ebilling-r1 task 084d-1 (design <c>notes/084d-dry-run-design.md</c> §1, ontology-r1 review F1 and F4): the per-request row source of a
/// dry run's "After" reading. It answers the four override tables as they would read once a proposed override is written: the rows the write
/// would delete are gone, and the rows it would create or change are there as after-images. Everything else passes through unchanged, and
/// every write throws (<see cref="WriteRefusingEntityService"/>).
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> Only <c>sprk_procedureassignment</c>, <c>sprk_ruleset</c>, <c>sprk_policy</c> and <c>sprk_policyversion</c>. The
/// constructor refuses an after-image or a deletion of any other table, so there is never a synthetic Procedure or Procedure Version.</para>
/// <para><b>Queries on the four tables</b> (<see cref="RetrieveMultipleAsync(QueryExpression, CancellationToken)"/>):</para>
/// <list type="bullet">
/// <item>A returned row whose id is deleted, or has an after-image ("replaced": ANY id with an after-image, so a real row and its image are
/// never both returned), is dropped from every page.</item>
/// <item>The after-images of the table are appended once, on the last page (<see cref="EntityCollection.MoreRecords"/> false), cut to the
/// query's columns, as fresh copies.</item>
/// <item>Which after-images a query gets is decided by one exact condition of the query's top-level AND criteria, never by evaluating the
/// criteria: a primary-key <c>Equal</c>/<c>In</c> that is the SOLE top-level condition gives the images with those ids (with other conditions
/// beside it, a query that those ids name an image for throws, since the overlay cannot judge the others; a key inside an Or or a nested filter
/// throws); a query with any linked entity throws (084d-2a fix round 1, K-3 and K-4); otherwise on <c>sprk_policy</c> the
/// <c>sprk_ruleset</c> <c>In</c>/<c>Equal</c> condition (the resolver's chunk of rule sets) and on <c>sprk_policyversion</c> the
/// <c>sprk_policy</c> <c>In</c>/<c>Equal</c> condition (its chunk of policies) give the images whose parent is in that list, so an image is
/// appended to exactly one chunk. A policy or version query with neither condition, or with more than one, throws (fails closed). On the
/// assignment and rule-set tables, a query without a primary-key condition gets every image of the table; the resolver's own per-row
/// re-checks decide membership.</item>
/// </list>
/// <para><b>Other reads of the four tables throw</b>: <see cref="RetrieveAsync"/>, a FetchXML read that names one of them (or that cannot be
/// parsed), and an alternate-key read. The dry run reads them only through <see cref="QueryExpression"/>.</para>
/// <para><b>Server-filled columns.</b> A CREATED row's image gets <c>statecode</c> 0 and the one per-request <c>createdon</c> when it lacks
/// them, so every version image and the Exclude row it pairs with carry the same stamp. A real row's image (one the write would update) is the
/// row as read, whole, with the update applied; nothing is added to it.</para>
/// <para><b>Per request.</b> Built inside the dry run from one plan and dropped with it: never a field, a DI registration or a static.
/// Images are copied in and copied out, so nothing a caller does to a returned row reaches another query.</para>
/// <para>§11: <i>Existing</i> — <see cref="Signals.Billing.ProcedureResolver"/> reads through <see cref="IGenericEntityService"/> and has no
/// other row source. <i>Extension</i> — a typed overlay on the resolver's rows would need rows its first read never loads (another firm's or an
/// assigned-only Procedure, an Off record rule switched back on), and their readers are private to the resolver, so the row-source seam is the
/// one place that needs no change under <c>Services/Signals</c>. <i>Cost of doing nothing</i> — a billing admin confirms a pin, exclusion or
/// rule change without seeing which rules it changes, or a second resolver drifts from the one that judges invoices.</para>
/// </remarks>
internal sealed class ProcedureRowOverlay : WriteRefusingEntityService
{
    /// <summary>The four tables the overlay shapes (names compared case-insensitively). A plan naming any other table is refused.</summary>
    private static readonly HashSet<string> Tables = new(StringComparer.OrdinalIgnoreCase)
    {
        ProcedureAssignmentEntity.Entity, BillingFactLoader.RuleSetEntity, BillingFactLoader.PolicyEntity, BillingFactLoader.VersionEntity,
    };

    /// <summary>The parent column a chunked query of a table names (the chunk rule), for the two tables the resolver chunks by parent.</summary>
    private static readonly IReadOnlyDictionary<string, string> ChunkParentColumn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BillingFactLoader.PolicyEntity] = "sprk_ruleset",
        [BillingFactLoader.VersionEntity] = "sprk_policy",
    };

    private readonly Dictionary<(string Table, Guid Id), Entity> _images = new();
    private readonly HashSet<(string Table, Guid Id)> _deleted = new();

    /// <param name="inner">The application service the real rows come from.</param>
    /// <param name="plan">The proposed override's rows: what it deletes, the after-images of what it creates or updates, and which images are created.</param>
    /// <param name="createdOnUtc">The one <c>createdon</c> of every created row in this request (UTC).</param>
    public ProcedureRowOverlay(IGenericEntityService inner, ProcedureOverridePlan plan, DateTime createdOnUtc)
        : base(inner)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The created-on stamp of a dry run is UTC.", nameof(createdOnUtc));
        }

        foreach (var gone in plan.Deleted)
        {
            _deleted.Add((Scoped(gone.LogicalName), gone.Id));
        }

        var created = plan.Created.Select(c => (Scoped(c.LogicalName), c.Id)).ToHashSet();
        foreach (var image in plan.AfterImages)
        {
            var key = (Scoped(image.LogicalName), image.Id);
            if (image.Id == Guid.Empty || !_images.TryAdd(key, Copy(image)))
            {
                throw new ArgumentException($"A plan holds one after-image per row; {image.LogicalName} {image.Id} is empty or repeated.", nameof(plan));
            }

            if (created.Remove(key))
            {
                var row = _images[key];
                if (!row.Contains("statecode"))
                {
                    row["statecode"] = new OptionSetValue(0);
                }

                if (!row.Contains("createdon"))
                {
                    row["createdon"] = createdOnUtc;
                }
            }
        }

        if (created.Count > 0)
        {
            throw new ArgumentException("Every created row of a plan has an after-image.", nameof(plan));
        }
    }

    /// <summary>The ids that read as replaced: every id with an after-image (tests and the dry run's own bookkeeping).</summary>
    internal IReadOnlyCollection<(string Table, Guid Id)> Replaced => _images.Keys;

    public override async Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // K-4 (fail closed): a join brings rows of a table into the answer without going through the overlay. Any link naming one of the four
        // tables, from any query, and any link at all on a query of the four, is refused before it reads (names compared case-insensitively).
        if (Tables.Contains(query.EntityName) ? query.LinkEntities.Count > 0 : LinksNameAnOverlayTable(query.LinkEntities))
        {
            throw Unsupported($"a {query.EntityName} query with linked entities that name or start from an override table");
        }

        if (!Tables.TryGetValue(query.EntityName, out var table))
        {
            return await Inner.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        }

        // Decided before the read, so a query the overlay cannot place fails without reading.
        var wanted = ImagesFor(query, table);
        var page = await Inner.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var rows = page.Entities
            .Where(r => !_deleted.Contains((table, r.Id)) && !_images.ContainsKey((table, r.Id)))
            .ToList();
        if (!page.MoreRecords)
        {
            rows.AddRange(wanted.Select(image => Cut(image, query.ColumnSet)));
        }

        return new EntityCollection(rows)
        {
            EntityName = page.EntityName ?? query.EntityName,
            MoreRecords = page.MoreRecords,
            PagingCookie = page.PagingCookie,
            TotalRecordCount = page.TotalRecordCount,
            TotalRecordCountLimitExceeded = page.TotalRecordCountLimitExceeded,
        };
    }

    public override Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default) =>
        Tables.Contains(entityLogicalName)
            ? throw Unsupported($"a by-id read of {entityLogicalName}")
            : Inner.RetrieveAsync(entityLogicalName, id, columns, ct);

    public override Task<Entity> RetrieveByAlternateKeyAsync(
        string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) =>
        Tables.Contains(entityLogicalName)
            ? throw Unsupported($"an alternate-key read of {entityLogicalName}")
            : Inner.RetrieveByAlternateKeyAsync(entityLogicalName, alternateKeyValues, columns, ct);

    public override Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        return NamesAnOverlayTable(fetch.Query)
            ? throw Unsupported("a FetchXML read of an override table")
            : Inner.RetrieveMultipleAsync(fetch, ct);
    }

    // ── The match ───────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The after-images <paramref name="query"/> gets (see the remarks). Throws when a policy or version query names no chunk condition.</summary>
    private IReadOnlyList<Entity> ImagesFor(QueryExpression query, string table)
    {
        var images = _images.Where(i => i.Key.Table == table).Select(i => i.Value).ToList();
        if (images.Count == 0)
        {
            return images;
        }

        var key = table + "id";
        var top = query.Criteria.FilterOperator == LogicalOperator.And ? query.Criteria.Conditions : (IEnumerable<ConditionExpression>)[];
        if (OneListCondition(top, key, table) is { } ids)
        {
            // K-3: the primary-key path answers a key read and nothing else. When the key is the SOLE top-level condition, the images it names are
            // the answer. When other conditions ride with it, the overlay cannot judge them (it never evaluates criteria), so an image the key
            // names would be returned unjudged: that is refused. An image the key does not name cannot match, so a query that names none is safe.
            var named = images.Where(i => ids.Contains(i.Id)).ToList();
            if (IsSoleCondition(query.Criteria) || named.Count == 0)
            {
                return named;
            }

            throw Unsupported($"a {table} read by key with other conditions that name a row the proposal changes");
        }

        if (Mentions(query.Criteria, key))
        {
            throw Unsupported($"a {table} query that names the key inside an Or, a nested filter or an operator the overlay does not place");
        }

        if (!ChunkParentColumn.TryGetValue(table, out var parentColumn))
        {
            return images;
        }

        var parents = OneListCondition(top, parentColumn, table)
            ?? throw Unsupported($"a {table} query without a top-level {parentColumn} In condition (the chunk rule cannot place an after-image)");
        return images.Where(i => i.GetAttributeValue<EntityReference>(parentColumn)?.Id is { } parent && parents.Contains(parent)).ToList();
    }

    /// <summary>The values of the one top-level <c>Equal</c>/<c>In</c> condition on <paramref name="column"/>; null when there is none. More than one throws.</summary>
    private static HashSet<Guid>? OneListCondition(IEnumerable<ConditionExpression> conditions, string column, string table)
    {
        var matching = conditions
            .Where(c => string.Equals(c.AttributeName, column, StringComparison.Ordinal) && c.Operator is ConditionOperator.Equal or ConditionOperator.In)
            .ToList();
        if (matching.Count > 1)
        {
            throw Unsupported($"a {table} query with {matching.Count} top-level {column} conditions");
        }

        return matching.Count == 0
            ? null
            : matching[0].Values.Select(v => v switch
            {
                Guid g => g,
                EntityReference r => r.Id,
                _ => throw Unsupported($"a {table} query whose {column} condition holds a {v?.GetType().Name ?? "null"}"),
            }).ToHashSet();
    }

    /// <summary>True when the one condition of an And filter with no nested filter is the whole criteria.</summary>
    private static bool IsSoleCondition(FilterExpression criteria) =>
        criteria.FilterOperator == LogicalOperator.And && criteria.Filters.Count == 0 && criteria.Conditions.Count == 1;

    /// <summary>True when <paramref name="column"/> is named by any condition of the filter or of its nested filters.</summary>
    private static bool Mentions(FilterExpression filter, string column) =>
        filter.Conditions.Any(c => string.Equals(c.AttributeName, column, StringComparison.Ordinal))
        || filter.Filters.Any(f => Mentions(f, column));

    /// <summary>True when any link, at any depth, joins one of the four tables (case-insensitive).</summary>
    private static bool LinksNameAnOverlayTable(IEnumerable<LinkEntity> links) =>
        links.Any(l => Tables.Contains(l.LinkToEntityName) || Tables.Contains(l.LinkFromEntityName) || LinksNameAnOverlayTable(l.LinkEntities));

    private static bool NamesAnOverlayTable(string fetchXml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(fetchXml);
        }
        catch (XmlException)
        {
            return true; // a read the overlay cannot place is refused, never passed through
        }

        return document.Descendants()
            .Where(e => e.Name.LocalName is "entity" or "link-entity")
            .Any(e => e.Attribute("name")?.Value is { } name && Tables.Contains(name));
    }

    private static string Scoped(string table) =>
        Tables.TryGetValue(table, out var canonical)
            ? canonical
            : throw new ArgumentException($"A dry run overlays the four override tables only; {table} is not one of them.", nameof(table));

    private static Entity Cut(Entity image, ColumnSet columns)
    {
        var row = new Entity(image.LogicalName, image.Id);
        foreach (var attribute in image.Attributes.Where(a => columns.AllColumns || columns.Columns.Contains(a.Key)))
        {
            row[attribute.Key] = attribute.Value;
        }

        return row;
    }

    private static Entity Copy(Entity image) => Cut(image, new ColumnSet(true));

    private static InvalidOperationException Unsupported(string what) =>
        new($"The dry-run overlay does not answer {what}; it refuses rather than pass the real rows through.");
}
