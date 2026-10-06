using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Delivery;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Pins <see cref="DataverseEmailTemplate"/> to the row the Dataverse Web API actually returns for
/// <c>templates({id})?$select=title,subject,body,ispersonal,templatetypecode</c>. The fixture is the live dev response
/// (2026-10-06, built-in template "Thank you for registering with us"), trimmed. <c>templatetypecode</c> arrives as a
/// logical-name string; typed as a number it made every template read throw.
/// </summary>
public class DataverseEmailTemplateWireShapeTests
{
    private const string LiveTemplateRow = """
        {
          "@odata.context": "https://example.crm.dynamics.com/api/data/v9.2/$metadata#templates(title,subject,body,ispersonal,templatetypecode)/$entity",
          "@odata.etag": "W/\"96611\"",
          "templateid": "7816b01c-efa8-4396-8ba5-0b6b72da5c08",
          "templatetypecode": "contact",
          "ispersonal": false,
          "body": "<p>Thank you</p>",
          "title": "Thank you for registering with us",
          "subject": "Welcome"
        }
        """;

    [Fact]
    public void TheLiveTemplateRow_Deserializes_WithTheTemplateTypeAsItsLogicalName()
    {
        var template = JsonSerializer.Deserialize<DataverseEmailTemplate>(LiveTemplateRow);

        template.Should().NotBeNull();
        template!.TemplateTypeCode.Should().Be("contact");
        template.Title.Should().Be("Thank you for registering with us");
        template.Body.Should().Be("<p>Thank you</p>");
        template.IsPersonal.Should().BeFalse();
    }
}
