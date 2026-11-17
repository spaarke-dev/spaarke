using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Delivery;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Pins <see cref="DataverseEmailTemplate"/> to the row the Dataverse Web API actually returns for
/// <c>templates({id})?$select=templateid,title,ispersonal,templatetypecode,subjectsafehtml,safehtml</c> (ontology task 195: the XSL
/// <c>subject</c> / <c>body</c> columns are no longer read). The fixture is the shape of the dev response (built-in template
/// "Thank you for registering with us"), trimmed. <c>templatetypecode</c> arrives as a
/// logical-name string; typed as a number it made every template read throw.
/// </summary>
public class DataverseEmailTemplateWireShapeTests
{
    private const string LiveTemplateRow = """
        {
          "@odata.context": "https://example.crm.dynamics.com/api/data/v9.2/$metadata#templates(templateid,title,ispersonal,templatetypecode,subjectsafehtml,safehtml)/$entity",
          "@odata.etag": "W/\"96611\"",
          "templateid": "7816b01c-efa8-4396-8ba5-0b6b72da5c08",
          "templatetypecode": "contact",
          "ispersonal": false,
          "safehtml": "<div><p>Thank you</p></div>",
          "title": "Thank you for registering with us",
          "subjectsafehtml": "Welcome"
        }
        """;

    [Fact]
    public void TheLiveTemplateRow_Deserializes_WithTheTemplateTypeAsItsLogicalName()
    {
        var template = JsonSerializer.Deserialize<DataverseEmailTemplate>(LiveTemplateRow);

        template.Should().NotBeNull();
        template!.TemplateTypeCode.Should().Be("contact");
        template.Title.Should().Be("Thank you for registering with us");
        template.SafeHtml.Should().Be("<div><p>Thank you</p></div>");
        template.SubjectSafeHtml.Should().Be("Welcome");
        template.IsPersonal.Should().BeFalse();
    }
}
