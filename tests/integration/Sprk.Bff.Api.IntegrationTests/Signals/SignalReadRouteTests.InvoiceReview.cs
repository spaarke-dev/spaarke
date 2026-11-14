// spaarke-ontology-platform-r1 task 174 (D-207; ebilling EB-D-194) — GET /api/v1/signals/worklist names the invoice each billing Work
// Item is decided on: `invoiceReview: { invoiceId, lineId }`. Same real pipeline and fake caller-Dataverse as SignalReadRouteTests.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

public sealed partial class SignalReadRouteTests
{
    // The one extra read task 174 adds: the parent invoices of every line item in the response (sprk_billingevent.sprk_invoice).
    private const string LineInvoiceRead = "sprk_billingevents?$select=sprk_billingeventid,_sprk_invoice_value";

    private static int LineInvoiceReads(World world) =>
        world.Requests.Count(r => r.StartsWith(LineInvoiceRead, StringComparison.Ordinal));

    private static JsonElement? InvoiceReviewOf(JsonElement body, Guid signalId) =>
        Item(body, signalId).TryGetProperty("invoiceReview", out var review) && review.ValueKind == JsonValueKind.Object ? review : null;

    private static bool HasNoInvoiceReview(JsonElement body, Guid signalId) => InvoiceReviewOf(body, signalId) is null;

    [Fact]
    public async Task Task174_ALineItemCarriesItsParentInvoiceAndLine_AnInvoiceItemItsInvoiceWithNoLine_OthersNone_InOneExtraRead()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var invoiceA = world.Invoice([Alice], VendorOrg, matter: m);
        var invoiceB = world.Invoice([Alice], VendorOrg, matter: m);
        var lineOnA = world.BillingEvent([Alice], invoiceA);
        var lineOnB = world.BillingEvent([Alice], invoiceB);
        var invoiceSubject = world.Invoice([Alice], VendorOrg, matter: m);
        var lineSignalA = BillingSignal(world, m, (BillingEventType, lineOnA), evidence: "[]");
        var lineSignalB = BillingSignal(world, m, (BillingEventType, lineOnB), evidence: "[]");
        var invoiceSignal = BillingSignal(world, m, (NotCoreType, invoiceSubject), evidence: "[]");
        var matterSignal = world.Signal(Decide, core: (MatterType, m));
        var eventSignal = world.Signal(Decide, core: (MatterType, m), subject: (EventType, world.Event(Alice, readers: [Alice])));
        var todoSignal = world.Signal(Do, core: null, subject: (TodoType, world.Todo(Alice, readers: [Alice])));

        var body = await OkAsync(world, Alice);

        var lineA = InvoiceReviewOf(body, lineSignalA)!.Value;
        lineA.GetProperty("invoiceId").GetGuid().Should().Be(invoiceA);
        lineA.GetProperty("lineId").GetGuid().Should().Be(lineOnA);
        var lineB = InvoiceReviewOf(body, lineSignalB)!.Value;
        lineB.GetProperty("invoiceId").GetGuid().Should().Be(invoiceB);
        lineB.GetProperty("lineId").GetGuid().Should().Be(lineOnB);

        var invoice = InvoiceReviewOf(body, invoiceSignal)!.Value;
        invoice.GetProperty("invoiceId").GetGuid().Should().Be(invoiceSubject);
        invoice.GetProperty("lineId").ValueKind.Should().Be(JsonValueKind.Null, "an invoice subject has no line");
        invoice.GetRawText().Should().Be($"{{\"invoiceId\":\"{invoiceSubject:D}\",\"lineId\":null}}");

        foreach (var other in new[] { matterSignal, eventSignal, todoSignal })
        {
            HasNoInvoiceReview(body, other).Should().BeTrue("only a billing item carries invoiceReview");
        }

        LineInvoiceReads(world).Should().Be(1, "two lines on two invoices and one invoice item cost exactly one extra read");
        world.Requests.Single(r => r.StartsWith(LineInvoiceRead, StringComparison.Ordinal))
            .Should().Contain(lineOnA.ToString("D")).And.Contain(lineOnB.ToString("D")).And.NotContain(invoiceSubject.ToString("D"));
    }

    [Fact]
    public async Task Task174_WithNoLineItems_NoExtraReadIsMade()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var invoice = world.Invoice([Alice], VendorOrg, matter: m);
        var invoiceSignal = BillingSignal(world, m, (NotCoreType, invoice), evidence: "[]");
        world.Signal(Decide, core: (MatterType, m));

        var body = await OkAsync(world, Alice);

        InvoiceReviewOf(body, invoiceSignal)!.Value.GetProperty("invoiceId").GetGuid().Should().Be(invoice);
        LineInvoiceReads(world).Should().Be(0);
    }

    [Fact]
    public async Task Task174_ALineWithNoParentInvoiceOrAnUnreadableOne_OmitsTheMember_AndIsNamedOnceByIdAtWarning()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var good = world.BillingEvent([Alice], world.Invoice([Alice], VendorOrg, matter: m));
        var noInvoice = world.BillingEvent([Alice], null);
        var unreadable = world.BillingEvent([Bob], world.Invoice([Alice], VendorOrg, matter: m));
        var goodSignal = BillingSignal(world, m, (BillingEventType, good), evidence: "[]");
        var noInvoiceSignal = BillingSignal(world, m, (BillingEventType, noInvoice), evidence: "[]");
        var unreadableSignal = BillingSignal(world, m, (BillingEventType, unreadable), evidence: "[]");
        _factory.Logs.Warnings.Clear();

        var body = await OkAsync(world, Alice);

        InvoiceReviewOf(body, goodSignal).Should().NotBeNull();
        HasNoInvoiceReview(body, noInvoiceSignal).Should().BeTrue("a line with no invoice never gets an invoiceReview with a null invoiceId");
        HasNoInvoiceReview(body, unreadableSignal).Should().BeTrue();
        Item(body, noInvoiceSignal).GetRawText().Should().NotContain("\"invoiceId\":null");
        LineInvoiceReads(world).Should().Be(1);
        var warnings = _factory.Logs.Warnings.Where(w => w.Contains("the caller cannot read the line, or the line has no invoice", StringComparison.Ordinal)).ToList();
        warnings.Should().ContainSingle();
        warnings[0].Should().Contain(noInvoice.ToString("D")).And.Contain(unreadable.ToString("D")).And.NotContain(good.ToString("D"));
    }

    [Theory]
    [InlineData("500")]
    [InlineData("401")]
    [InlineData("429")]
    [InlineData("unparseable")]
    [InlineData("throw")]
    public async Task Task174_AFaultOnTheBatchedRead_OmitsTheMemberForEveryLine_LogsOnce_AndTheRouteStill200s(string fault)
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var invoice = world.Invoice([Alice], VendorOrg, matter: m);
        var line1 = BillingSignal(world, m, (BillingEventType, world.BillingEvent([Alice], invoice)), evidence: "[]");
        var line2 = BillingSignal(world, m, (BillingEventType, world.BillingEvent([Alice], invoice)), evidence: "[]");
        var invoiceSignal = BillingSignal(world, m, (NotCoreType, invoice), evidence: "[]");
        var matterSignal = world.Signal(Decide, core: (MatterType, m));
        world.PathFaults[LineInvoiceRead] = fault;
        _factory.Logs.Warnings.Clear();

        var response = await GetAsync(world, Alice);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();

        AllItems(body).Should().HaveCount(4, "a fault never shortens the list");
        HasNoInvoiceReview(body, line1).Should().BeTrue();
        HasNoInvoiceReview(body, line2).Should().BeTrue();
        HasNoInvoiceReview(body, matterSignal).Should().BeTrue();
        InvoiceReviewOf(body, invoiceSignal)!.Value.GetProperty("invoiceId").GetGuid().Should().Be(invoice, "an invoice item needs no read");
        // ONE warning for the whole fault, from the read helper, labelled for this read (not the D-176 Duplicate sentence).
        var faults = _factory.Logs.Warnings.Where(w => w.Contains("Worklist invoice-line read: the caller's read of sprk_billingevent", StringComparison.Ordinal)).ToList();
        faults.Should().ContainSingle();
        faults[0].Should().Contain("invoiceReview is omitted");
        _factory.Logs.Warnings.Should().NotContain(w => w.Contains("Duplicate earlier-invoice check", StringComparison.Ordinal), "there is no Duplicate here");
        _factory.Logs.Warnings.Where(w => w.Contains("sprk_billingevent", StringComparison.Ordinal)).Should().ContainSingle("one warning, not two");
    }
}
