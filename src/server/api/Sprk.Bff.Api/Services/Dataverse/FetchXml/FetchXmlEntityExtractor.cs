using System.Xml;
using System.Xml.Linq;

namespace Sprk.Bff.Api.Services.Dataverse.FetchXml;

/// <summary>
/// Default <see cref="IFetchXmlEntityExtractor"/> backed by <see cref="XDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// SECURITY-CRITICAL. This extractor is the load-bearing input for the cross-entity
/// privilege check performed by <c>DataverseAuthorizationFilter</c> on
/// <c>POST /api/dataverse/fetch</c> (FR-BFF-04). It MUST return EVERY entity referenced
/// by the FetchXML — the primary <c>&lt;entity name="…"&gt;</c> plus every nested
/// <c>&lt;link-entity name="…"&gt;</c> at any depth — so the filter can reject any
/// request that touches an entity the caller lacks Read privilege on.
/// </para>
/// <para>
/// Per task 010 §5, Dataverse server-side RBAC does NOT cascade Read enforcement
/// through <c>&lt;link-entity&gt;</c> joins; an under-tested filter (e.g., one that
/// only checks the primary entity) creates a trivial information-disclosure path.
/// </para>
/// <para>
/// Implementation notes:
/// </para>
/// <list type="bullet">
///   <item>Uses <see cref="XDocument.Parse(string)"/> — in-box .NET; no new dependency
///         per task 010 Q2 resolution (2026-06-01).</item>
///   <item><see cref="XContainer.Descendants(XName)"/> walks the entire subtree, so
///         nested <c>&lt;link-entity&gt;</c> (link-entity inside link-entity, depth-N)
///         is naturally covered — no recursion to maintain.</item>
///   <item>Many-to-many bridge joins (<c>intersect="true"</c>) and LEFT OUTER joins
///         (<c>link-type="outer"</c>) still surface as <c>&lt;link-entity&gt;</c>
///         elements, so they are detected without special-casing.</item>
///   <item>Entity names are trimmed and lower-cased so the returned
///         <see cref="HashSet{T}"/> uses <see cref="StringComparer.OrdinalIgnoreCase"/>
///         and matches the privilege checker's case-insensitive contract.</item>
///   <item>Malformed XML is wrapped in <see cref="FetchXmlParseException"/> per the
///         interface contract — the authorization filter maps this to
///         <c>400 ProblemDetails (errorCode: DV_FETCHXML_MALFORMED)</c> per task 010 §7.</item>
/// </list>
/// <para>
/// Stateless and safe to register as a singleton.
/// </para>
/// </remarks>
internal sealed class FetchXmlEntityExtractor : IFetchXmlEntityExtractor
{
    /// <inheritdoc />
    public IReadOnlySet<string> ExtractEntities(string fetchXml)
    {
        if (string.IsNullOrWhiteSpace(fetchXml))
        {
            // The interface contract surfaces malformed input via FetchXmlParseException so
            // the filter can map uniformly to 400 DV_FETCHXML_MALFORMED.
            throw new FetchXmlParseException(
                "FetchXML payload cannot be null, empty, or whitespace.");
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(fetchXml);
        }
        catch (XmlException ex)
        {
            // Wrap so the filter's single catch path handles every malformation case.
            // We deliberately do NOT include the original XmlException message in the
            // outward-facing detail (the filter does that sanitisation); the inner
            // exception is preserved here for server-side logging.
            throw new FetchXmlParseException(
                "FetchXML payload is not well-formed XML.", ex);
        }

        var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Primary entity: <fetch><entity name="…">…</entity></fetch>
        // Per the Dataverse FetchXML schema a fetch must contain exactly one root entity.
        var primaryName = document.Root?
            .Element("entity")?
            .Attribute("name")?
            .Value;

        if (string.IsNullOrWhiteSpace(primaryName))
        {
            // Treat structural violation as malformed; the filter returns 400.
            throw new FetchXmlParseException(
                "FetchXML root must contain an <entity name=\"…\"> element.");
        }

        entities.Add(NormalizeEntityName(primaryName));

        // All link-entities at any depth — Descendants() walks the entire subtree, so
        // nested <link-entity> inside <link-entity> is covered without recursion.
        foreach (var linkEntity in document.Descendants("link-entity"))
        {
            var linkedName = linkEntity.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(linkedName))
            {
                // Per FetchXML schema, name is required on link-entity; treat missing
                // as malformed rather than silently allowing the join.
                throw new FetchXmlParseException(
                    "<link-entity> element is missing required 'name' attribute.");
            }

            // Many-to-many bridge entities (intersect="true") and LEFT OUTER joins
            // (link-type="outer") both appear as <link-entity name="…"> elements
            // and are added to the set the same way; the privilege check treats
            // them identically. No special-casing required here.
            entities.Add(NormalizeEntityName(linkedName));
        }

        return entities;
    }

    /// <summary>
    /// Normalizes a Dataverse logical name to the canonical form used as a set member:
    /// trimmed and lower-cased. Dataverse logical names are case-insensitive but are
    /// conventionally stored lower-case; we normalize to guarantee deterministic
    /// <see cref="HashSet{T}"/> membership and to match the privilege checker's contract
    /// (which also lowercases entity names).
    /// </summary>
    private static string NormalizeEntityName(string name) =>
        name.Trim().ToLowerInvariant();
}
