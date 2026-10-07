using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Tests.Api.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression;

/// <summary>
/// GitHub #1079 (ISS-015): the Office invoice quick-create named the new row by writing <c>sprk_invoicename</c>, which
/// <c>sprk_invoice</c> does not have, so Dataverse refused every invoice quick-create from the pane.
/// </summary>
/// <remarks>
/// <para><b>Live metadata</b> (<c>spaarkedev1</c>, 2026-10-01): <c>sprk_invoice.PrimaryNameAttribute</c> is
/// <c>sprk_name</c>; no <c>sprk_invoicename</c> attribute exists. The wrong column came in with #934 and survived
/// because no test asserted the invoice's name attribute. This test is that assertion.</para>
///
/// <para>Not to be confused with <c>sprk_billingevent.sprk_invoicename</c>, which does exist.</para>
/// </remarks>
[Trait("status", "new")]
public class Issue1079_InvoiceQuickCreateNameTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1079");
    private static readonly Guid CreatedInvoiceId = Guid.Parse("00000000-0000-0000-0000-000000001079");

    [Fact]
    public async Task QuickCreateInvoice_NamesTheRowWithItsPrimaryNameAttribute_NotTheNonexistentInvoiceName()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));

        Entity? created = null;
        factory.Entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => created = entity)
            .ReturnsAsync(CreatedInvoiceId);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/quickcreate/invoice", new { name = "  INV-2026-0042  " });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Should().NotBeNull("the invoice row is written through the generic create");
        created!.LogicalName.Should().Be("sprk_invoice");
        created.GetAttributeValue<string>("sprk_name").Should().Be(
            "INV-2026-0042", "sprk_name is sprk_invoice's primary name attribute");
        created.Contains("sprk_invoicename").Should().BeFalse(
            "sprk_invoice has no such column; writing it makes Dataverse refuse the create (#1079)");
    }
}
