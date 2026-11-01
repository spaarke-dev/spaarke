using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Insights.LiveFacts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Insights.LiveFacts;

/// <summary>
/// Unit tests for <see cref="InvoiceLiveFactResolver"/> (r2 Wave D5 task 034). Verifies the
/// per-entity resolver pattern for the <c>invoice:</c> subject scheme per design-a6 §6.3,
/// including the inter-entity reference behavior (A6-D7 — <c>relatedMatter</c> returns
/// <c>{id, name}</c> only, no recursion).
/// </summary>
public class InvoiceLiveFactResolverTests
{
    private const string TenantId = "tenant-acme";
    private static readonly Guid InvoiceId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly string InvoiceSubject = $"invoice:{InvoiceId}";

    private static readonly Guid RelatedMatterId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private readonly Mock<IGenericEntityService> _entityServiceMock = new(MockBehavior.Strict);

    private InvoiceLiveFactResolver CreateSut()
        => new(_entityServiceMock.Object, NullLogger<InvoiceLiveFactResolver>.Instance);

    private static Entity BuildInvoice(bool withStatusFormatted = true, decimal totalAsDecimal = 12500.00m, bool useMoney = false)
    {
        var invoice = new Entity("sprk_invoice", InvoiceId);
        invoice["sprk_invoiceid"] = InvoiceId;
        invoice["sprk_invoicenumber"] = "INV-2026-007";
        // The live columns (ebilling-r1 task 015, ISS-001 / #1904): sprk_totalamount and statuscode.
        invoice["sprk_totalamount"] = useMoney ? (object)new Money(totalAsDecimal) : totalAsDecimal;
        invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Paid);
        if (withStatusFormatted)
        {
            invoice.FormattedValues.Add("statuscode", "Paid");
        }
        invoice["sprk_matter"] = new EntityReference("sprk_matter", RelatedMatterId) { Name = "Atlas Acquisition" };
        return invoice;
    }

    // Only a read of the real columns gets the row: a read of a column the table does not have fails in Dataverse, so the
    // strict mock refuses it too.
    private static readonly string[] LiveInvoiceColumns =
        ["sprk_invoiceid", "sprk_invoicenumber", "sprk_totalamount", "statuscode", "sprk_matter"];

    private void SetupReturnsInvoice(Entity invoice)
    {
        _entityServiceMock
            .Setup(s => s.RetrieveAsync(
                "sprk_invoice",
                InvoiceId,
                It.Is<string[]>(columns => columns.OrderBy(c => c).SequenceEqual(LiveInvoiceColumns.OrderBy(c => c))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
    }

    [Fact]
    public async Task ResolveAsync_InvoiceNumber_ReturnsPlainStringFact()
    {
        SetupReturnsInvoice(BuildInvoice());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(InvoiceSubject, "invoiceNumber", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Value.DisplayHint.Should().Be("text");
        fact.Value.Raw.GetString().Should().Be("INV-2026-007");
        fact.ProducedBy.Id.Should().Be("dataverse://sprk_invoice");
    }

    [Fact]
    public async Task ResolveAsync_InvoiceTotal_ReturnsNumericFact_FromDecimal()
    {
        SetupReturnsInvoice(BuildInvoice(useMoney: false));
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(InvoiceSubject, "invoiceTotal", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Value.DisplayHint.Should().Be("currency-usd");
        fact.Value.Raw.GetDecimal().Should().Be(12500.00m);
    }

    [Fact]
    public async Task ResolveAsync_InvoiceTotal_ReturnsNumericFact_FromMoney()
    {
        // Dataverse Money fields surface as Microsoft.Xrm.Sdk.Money; the resolver must unwrap.
        SetupReturnsInvoice(BuildInvoice(useMoney: true, totalAsDecimal: 9999.99m));
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(InvoiceSubject, "invoiceTotal", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Value.Raw.GetDecimal().Should().Be(9999.99m);
    }

    [Fact]
    public async Task ResolveAsync_InvoiceStatus_ReturnsFormattedValue()
    {
        SetupReturnsInvoice(BuildInvoice(withStatusFormatted: true));
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(InvoiceSubject, "invoiceStatus", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Value.Raw.GetString().Should().Be("Paid");
    }

    [Fact]
    public async Task ResolveAsync_InvoiceStatus_WithoutAFormattedValue_ReturnsTheStatuscodeNumber()
    {
        SetupReturnsInvoice(BuildInvoice(withStatusFormatted: false));
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(InvoiceSubject, "invoiceStatus", TenantId, CancellationToken.None);

        fact!.Value.Raw.GetString().Should().Be(InvoiceStatusCodes.Paid.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ResolveAsync_RelatedMatter_ReturnsEntityReferenceShape_NoRecursion()
    {
        // A6-D7: invoice.relatedMatter returns {id, name} ONLY. Does NOT recurse into the
        // matter resolver. The IGenericEntityService is invoked ONCE (for sprk_invoice). No
        // call to sprk_matter is expected — verified by the strict mock.
        SetupReturnsInvoice(BuildInvoice());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(InvoiceSubject, "relatedMatter", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Value.DisplayHint.Should().Be("entity-reference");
        fact.Value.Raw.GetProperty("id").GetString().Should().Be(RelatedMatterId.ToString());
        fact.Value.Raw.GetProperty("name").GetString().Should().Be("Atlas Acquisition");

        // Strict mock guarantees: only ONE Dataverse read happened (the invoice itself).
        _entityServiceMock.Verify(
            s => s.RetrieveAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _entityServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveAsync_CurrentInvoiceFactsComposite_ReturnsAllSubvalues()
    {
        SetupReturnsInvoice(BuildInvoice());
        var sut = CreateSut();

        var fact = await sut.ResolveAsync(InvoiceSubject, "currentInvoiceFacts", TenantId, CancellationToken.None);

        fact.Should().NotBeNull();
        fact!.Value.DisplayHint.Should().Be("invoice-facts");
        var raw = fact.Value.Raw;
        raw.GetProperty("invoiceNumber").GetString().Should().Be("INV-2026-007");
        raw.GetProperty("invoiceTotal").GetDecimal().Should().Be(12500.00m);
        raw.GetProperty("invoiceStatus").GetString().Should().Be("Paid");
        raw.GetProperty("relatedMatter").GetProperty("id").GetString().Should().Be(RelatedMatterId.ToString());
    }

    [Fact]
    public async Task ResolveAsync_UnsupportedPredicate_Throws()
    {
        SetupReturnsInvoice(BuildInvoice());
        var sut = CreateSut();

        Func<Task> act = () => sut.ResolveAsync(InvoiceSubject, "unknownPredicate", TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<LiveFactNotSupportedException>();
    }

    [Fact]
    public async Task ResolveAsync_InvalidScheme_Throws()
    {
        var sut = CreateSut();
        Func<Task> act = () => sut.ResolveAsync($"matter:{Guid.NewGuid()}", "invoiceNumber", TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<LiveFactNotSupportedException>();
    }

    [Fact]
    public async Task ResolveAsync_InvoiceNotFound_ReturnsNull()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveAsync(
                "sprk_invoice",
                InvoiceId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity sprk_invoice with id ... was not found."));

        var sut = CreateSut();

        var fact = await sut.ResolveAsync(InvoiceSubject, "invoiceNumber", TenantId, CancellationToken.None);

        fact.Should().BeNull();
    }

}
