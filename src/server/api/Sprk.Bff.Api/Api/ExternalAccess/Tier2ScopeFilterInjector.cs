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
    /// Adds <c>&lt;filter type='and'&gt;&lt;condition attribute='{idAttribute}' operator='in'&gt;…&lt;/condition&gt;&lt;/filter&gt;</c>
    /// to the primary <c>&lt;entity&gt;</c> of <paramref name="fetchXml"/>, one <c>&lt;value&gt;</c> per
    /// accessible id. The filter is inserted before the first <c>&lt;order&gt;</c> element to honor the
    /// FetchXML canonical child order (attributes → filter → order). The caller MUST guarantee
    /// <paramref name="accessibleIds"/> is non-empty (an empty set would emit an invalid <c>IN ()</c>);
    /// the endpoint short-circuits the empty case before calling this.
    /// </summary>
    /// <exception cref="FetchXmlParseException">If the FetchXML is not well-formed or has no entity.</exception>
    public static string Inject(string fetchXml, string idAttribute, IReadOnlySet<Guid> accessibleIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idAttribute);
        ArgumentNullException.ThrowIfNull(accessibleIds);
        if (accessibleIds.Count == 0)
        {
            throw new ArgumentException(
                "accessibleIds must be non-empty (the empty case is short-circuited before injection).",
                nameof(accessibleIds));
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

        var condition = new XElement("condition",
            new XAttribute("attribute", idAttribute),
            new XAttribute("operator", "in"));
        foreach (var id in accessibleIds)
        {
            condition.Add(new XElement("value", id.ToString("D")));
        }
        var filter = new XElement("filter", new XAttribute("type", "and"), condition);

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
