// spaarke-SPA-external-access-platform-r2 — server-side Tier-2 scope injection for the module-host
// widget-data read seam (FR-22 · ADR-028 A3). Extracted from ExternalModuleDataEndpoints so the
// security-relevant FetchXML transform is unit-testable as pure domain logic (see
// notes/grid-widget-empty-diagnosis.md — the "fetch-then-filter-in-memory" correctness bug this fixes).

using System.Xml;
using System.Xml.Linq;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Pushes a module's Tier-2 record scope into a caller-supplied FetchXML as a server-side filter, so
/// Dataverse returns ONLY the caller's accessible rows — replacing the prior "execute unfiltered, then
/// drop non-matching rows in memory" approach that silently returned 0 rows whenever the accessible
/// records fell outside the first page of a large/sparse table.
/// </summary>
internal static class Tier2ScopeFilterInjector
{
    /// <summary>
    /// One dimension of the OR-scope filter: an attribute (a root's own id, or a child's typed parent
    /// lookup) IN a non-empty set of accessible ids.
    /// </summary>
    public readonly record struct ScopeFilterDimension(string Attribute, IReadOnlySet<Guid> AccessibleIds);

    /// <summary>
    /// Single-dimension shorthand — adds a <c>&lt;filter&gt;</c> with one <c>&lt;condition attribute='{idAttribute}'
    /// operator='in'&gt;</c> to the primary <c>&lt;entity&gt;</c>. Delegates to the multi-dimension overload.
    /// </summary>
    /// <exception cref="FetchXmlParseException">If the FetchXML is not well-formed or has no entity.</exception>
    public static string Inject(string fetchXml, string idAttribute, IReadOnlySet<Guid> accessibleIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idAttribute);
        ArgumentNullException.ThrowIfNull(accessibleIds);
        return Inject(fetchXml, new[] { new ScopeFilterDimension(idAttribute, accessibleIds) });
    }

    /// <summary>
    /// Pushes a module's polymorphic Tier-2 scope into a caller-supplied FetchXML as a server-side
    /// <c>&lt;filter type='or'&gt;</c> — one <c>&lt;condition attribute='{dim.Attribute}' operator='in'&gt;</c>
    /// per dimension (one <c>&lt;value&gt;</c> per accessible id) — so Dataverse returns ONLY rows that roll
    /// up to ANY accessible root (task 028). Dimensions with an empty id set are omitted (an empty
    /// <c>IN ()</c> is invalid); the caller MUST pass at least one non-empty dimension (the endpoint
    /// short-circuits the all-empty case before calling this). The filter is inserted before the first
    /// <c>&lt;order&gt;</c> to honor the FetchXML canonical child order (attributes → filter → order).
    /// </summary>
    /// <exception cref="FetchXmlParseException">If the FetchXML is not well-formed or has no entity.</exception>
    public static string Inject(string fetchXml, IReadOnlyList<ScopeFilterDimension> dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        var nonEmpty = dimensions.Where(d => d.AccessibleIds is { Count: > 0 }).ToList();
        if (nonEmpty.Count == 0)
        {
            throw new ArgumentException(
                "At least one dimension with a non-empty id set is required (the all-empty case is " +
                "short-circuited before injection).",
                nameof(dimensions));
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(fetchXml);
        }
        catch (XmlException ex)
        {
            throw new FetchXmlParseException("FetchXML payload is not well-formed XML.", ex);
        }

        var entity = document.Root?.Element("entity")
            ?? throw new FetchXmlParseException("FetchXML must contain an <entity> element.");

        // type='or' across dimensions: a child row is accessible if it rolls up to ANY accessible root.
        // (A single dimension under an OR filter is logically identical to under an AND filter.)
        var filter = new XElement("filter", new XAttribute("type", "or"));
        foreach (var dim in nonEmpty)
        {
            var condition = new XElement("condition",
                new XAttribute("attribute", dim.Attribute),
                new XAttribute("operator", "in"));
            foreach (var id in dim.AccessibleIds)
            {
                condition.Add(new XElement("value", id.ToString("D")));
            }
            filter.Add(condition);
        }

        var firstOrder = entity.Elements("order").FirstOrDefault();
        if (firstOrder is not null)
        {
            firstOrder.AddBeforeSelf(filter);
        }
        else
        {
            entity.Add(filter);
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }
}
