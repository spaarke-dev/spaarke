// spaarke-SPA-external-access-platform-r2 — regression coverage for the server-side Tier-2 scope
// injection that fixed the "all child grids empty" bug (notes/grid-widget-empty-diagnosis.md):
// documents/invoices returned 0 rows because the seam executed the caller's FetchXML UNFILTERED
// (one page) then dropped non-matching rows in memory — accessible rows on later pages were lost.
// The fix pushes an `IN (accessible ids)` filter on the module's RecordIdAttribute into the FetchXML
// BEFORE execution. These are pure-domain-logic unit tests of that security-relevant transform
// (ADR-038: the fetch happy-path itself needs a live ServiceClient and cannot run in-process).

using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public sealed class Tier2ScopeFilterInjectorTests
{
    private static readonly Guid IdA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid IdB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string ProjectFetchXml =
        "<fetch><entity name=\"sprk_project\">" +
        "<attribute name=\"sprk_projectid\"/><attribute name=\"sprk_projectname\"/>" +
        "<order attribute=\"sprk_projectname\" descending=\"false\"/>" +
        "</entity></fetch>";

    [Fact]
    public void Inject_GivenAccessibleIds_AddsInConditionOnIdAttributeWithOneValuePerId()
    {
        var result = Tier2ScopeFilterInjector.Inject(
            ProjectFetchXml, "sprk_projectid", new HashSet<Guid> { IdA, IdB });

        var entity = XDocument.Parse(result).Root!.Element("entity")!;
        var condition = entity.Element("filter")!.Element("condition")!;

        condition.Attribute("attribute")!.Value.Should().Be("sprk_projectid");
        condition.Attribute("operator")!.Value.Should().Be("in");
        condition.Elements("value").Select(v => v.Value)
            .Should().BeEquivalentTo(new[] { IdA.ToString("D"), IdB.ToString("D") });
    }

    [Fact]
    public void Inject_GivenChildLookupAttribute_ScopesOnThatLookupNotThePrimaryId()
    {
        // Documents scope by the sprk_project LOOKUP, not the document's own id (the bug's core case).
        const string docFetchXml =
            "<fetch><entity name=\"sprk_document\"><attribute name=\"sprk_documentid\"/>" +
            "<attribute name=\"sprk_project\"/></entity></fetch>";

        var result = Tier2ScopeFilterInjector.Inject(docFetchXml, "sprk_project", new HashSet<Guid> { IdA });

        var condition = XDocument.Parse(result).Root!.Element("entity")!.Element("filter")!.Element("condition")!;
        condition.Attribute("attribute")!.Value.Should().Be("sprk_project");
    }

    [Fact]
    public void Inject_WhenEntityHasOrder_PlacesFilterBeforeOrderForCanonicalFetchXml()
    {
        var result = Tier2ScopeFilterInjector.Inject(ProjectFetchXml, "sprk_projectid", new HashSet<Guid> { IdA });

        var children = XDocument.Parse(result).Root!.Element("entity")!.Elements().ToList();
        var filterIndex = children.FindIndex(e => e.Name == "filter");
        var orderIndex = children.FindIndex(e => e.Name == "order");

        filterIndex.Should().BeGreaterThanOrEqualTo(0);
        orderIndex.Should().BeGreaterThan(filterIndex, "FetchXML requires filter before order");
    }

    [Fact]
    public void Inject_WhenNoOrderElement_AppendsFilterToEntity()
    {
        const string noOrderFetchXml =
            "<fetch><entity name=\"sprk_project\"><attribute name=\"sprk_projectid\"/></entity></fetch>";

        var result = Tier2ScopeFilterInjector.Inject(noOrderFetchXml, "sprk_projectid", new HashSet<Guid> { IdA });

        XDocument.Parse(result).Root!.Element("entity")!.Element("filter").Should().NotBeNull();
    }

    [Fact]
    public void Inject_PreservesOriginalProjectedAttributes()
    {
        // ScopeRows (defense-in-depth) reads the RecordIdAttribute from each row, so injection must
        // never drop the caller's projected attributes.
        var result = Tier2ScopeFilterInjector.Inject(ProjectFetchXml, "sprk_projectid", new HashSet<Guid> { IdA });

        var attrs = XDocument.Parse(result).Root!.Element("entity")!
            .Elements("attribute").Select(a => a.Attribute("name")!.Value).ToList();
        attrs.Should().Contain(new[] { "sprk_projectid", "sprk_projectname" });
    }

    [Fact]
    public void Inject_WhenAccessibleIdsEmpty_Throws()
    {
        var act = () => Tier2ScopeFilterInjector.Inject(ProjectFetchXml, "sprk_projectid", new HashSet<Guid>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Inject_WhenFetchXmlMalformed_ThrowsFetchXmlParseException()
    {
        var act = () => Tier2ScopeFilterInjector.Inject("<fetch><entity", "sprk_projectid", new HashSet<Guid> { IdA });

        act.Should().Throw<FetchXmlParseException>();
    }
}
