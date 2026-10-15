using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;
using static Sprk.Bff.Api.Tests.Seam.ContentUnderstanding.ContentUnderstandingTestHost;

namespace Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;

/// <summary>
/// ADR-043 vertical-slice seam for <see cref="IInvoiceAi.ReadInvoiceAsync"/> (ebilling-r1 task 043; FR-21, FR-04, FR-47,
/// D-81): the production <see cref="InvoiceAi"/> → <c>ContentUnderstandingClient</c> → result mapper → completion
/// path, with only the HTTP wire doubled (<see cref="FakeContentUnderstandingService"/>, answering with recorded result
/// bodies and the error codes measured on dev on 2026-10-15).
/// </summary>
/// <remarks>
/// Anti-stub: the recorded result's values, confidences and sources are asserted on the returned DTO (so the tests fail
/// if mapping is skipped), and the request URL, bearer token and body are asserted on the wire (so they fail if the
/// configured analyzer or api-version does not reach the service). The playbook and model clients are STRICT mocks with
/// no setups: a read that touched either fails.
/// </remarks>
public sealed class InvoiceReadSeamTests
{
    private static readonly byte[] Pdf = "%PDF-1.7 synthetic"u8.ToArray();

    private static InvoiceAi Sut(ContentUnderstandingOptions options, FakeContentUnderstandingService service, TimeProvider? time = null) =>
        new(
            new Mock<IPlaybookService>(MockBehavior.Strict).Object,
            new Mock<IOpenAiClient>(MockBehavior.Strict).Object,
            new NullActionResolver(NullLogger<NullActionResolver>.Instance),
            new NullActionRunner(NullLogger<NullActionRunner>.Instance),
            Client(options, service, time),
            NullLogger<InvoiceAi>.Instance);

    private static InvoiceReadRequest Request(byte[]? content = null) =>
        new() { Content = content ?? Pdf, DocumentId = Guid.Parse("00000000-0000-0000-0000-0000000000b2") };

    private static FakeContentUnderstandingService Serving(string resultJson) =>
        new() { OnResult = _ => JsonResponse(HttpStatusCode.OK, resultJson) };

    // ── configuration: preview primary, GA fallback (D-81) ─────────────────────────────────────────────

    [Fact]
    public async Task ReadInvoiceAsync_PreviewConfiguration_CallsThePreviewAnalyzerWithAManagedIdentityToken()
    {
        var service = Serving(ContentUnderstandingRecordedResults.PreviewB0002);

        var invoice = await Sut(PreviewOptions(), service).ReadInvoiceAsync(Request());

        var post = service.Analyzes.Should().ContainSingle().Subject;
        post.Uri.AbsolutePath.Should().Be("/contentunderstanding/analyzers/sprk_invoice_v2p:analyzeBinary");
        post.Uri.Query.Should().Contain("api-version=2026-06-01-preview").And.Contain("range=1-20");
        post.Authorization.Should().Be($"Bearer {Token}");
        post.BodyLength.Should().Be(Pdf.Length);
        post.ContentType.Should().Be("application/pdf");
        service.Requests.Where(r => r.Method == HttpMethod.Get).Should().OnlyContain(r => r.Authorization == $"Bearer {Token}")
            .And.OnlyContain(r => r.Uri.Host == new Uri(Endpoint).Host);
        invoice.AnalyzerId.Should().Be("sprk_invoice_v2p");
        invoice.ApiVersion.Should().Be("2026-06-01-preview");
        invoice.PageCount.Should().Be(2, "the preview reports metadata.pageCount");
    }

    [Fact]
    public async Task ReadInvoiceAsync_GaConfiguration_CallsTheGaAnalyzerAndMapsTheSameSchema()
    {
        var service = Serving(ContentUnderstandingRecordedResults.GaB0002);

        var invoice = await Sut(GaOptions(), service).ReadInvoiceAsync(Request());

        var post = service.Analyzes.Should().ContainSingle().Subject;
        post.Uri.AbsolutePath.Should().Be("/contentunderstanding/analyzers/sprk_invoice_v2:analyzeBinary");
        post.Uri.Query.Should().Contain("api-version=2025-11-01");
        invoice.AnalyzerId.Should().Be("sprk_invoice_v2");
        invoice.ApiVersion.Should().Be("2025-11-01");
        invoice.PageCount.Should().Be(2, "GA has no page count; the last range ended before its end");
        invoice.Header.FirmInvoiceNumber.Value.Should().Be("2025023029");
        invoice.Lines.Should().HaveCount(5);
    }

    // ── mapping (FR-21, FR-04) ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadInvoiceAsync_RecordedResult_MapsHeaderWithConfidencePageAndNormalisedBox()
    {
        var invoice = await Sut(PreviewOptions(), Serving(ContentUnderstandingRecordedResults.PreviewB0002)).ReadInvoiceAsync(Request());

        var number = invoice.Header.FirmInvoiceNumber;
        number.Value.Should().Be("2025023029");
        number.Confidence.Should().Be(0.993m);
        number.Page.Should().Be(1);
        number.Repair.Should().Be(ExtractedValueRepair.None);
        // D(1,6.5410,1.7108,7.2507,1.7098,7.2507,1.8405,6.5410,1.8401) on an 8.5 x 11 in page.
        number.BoundingBox.Should().Be(new ExtractedBoundingBox(1, 0.7695m, 0.1554m, 0.0835m, 0.0119m));

        invoice.Header.InvoiceDate.Value.Should().Be(new DateOnly(2025, 8, 1));
        invoice.Header.BillingPeriodStart.Value.Should().Be(new DateOnly(2025, 7, 1));
        invoice.Header.BillingPeriodEnd.Value.Should().Be(new DateOnly(2025, 7, 31));
        invoice.Header.TotalDue.Value.Should().Be(12754.52m);
        invoice.Header.TotalDue.Page.Should().Be(2, "totals are on the second page");
        invoice.Header.AdjustmentsTotal.Value.Should().Be(0m);
        invoice.Header.FirmName.Value.Should().Be("Ravenscroft and Vance LLP");
        invoice.Header.FirmMatterReference.Value.Should().Be("13080-0029");
        invoice.Header.PurchaseOrderAsQuoted.Value.Should().Be("PO-4550267");
    }

    [Fact]
    public async Task ReadInvoiceAsync_NonUsdInvoice_ReturnsTheCurrencyAsRead()
    {
        var invoice = await Sut(PreviewOptions(), Serving(ContentUnderstandingRecordedResults.PreviewB0002)).ReadInvoiceAsync(Request());

        invoice.Header.Currency.Value.Should().Be("GBP");
    }

    [Fact]
    public async Task ReadInvoiceAsync_RecordedResult_MapsFeeThenExpenseRowsAndTheTimekeeperSummary()
    {
        var invoice = await Sut(PreviewOptions(), Serving(ContentUnderstandingRecordedResults.PreviewB0002)).ReadInvoiceAsync(Request());

        invoice.Lines.Select(l => (l.LineType, l.RowIndex)).Should().Equal(
            (InvoiceLineType.Fee, 0), (InvoiceLineType.Fee, 1), (InvoiceLineType.Fee, 2),
            (InvoiceLineType.Expense, 0), (InvoiceLineType.Expense, 1));

        var fee = invoice.Lines[0];
        fee.Date.Value.Should().Be(new DateOnly(2025, 7, 2));
        fee.TimekeeperAsBilled.Value.Should().Be("Naomi Lockhart");
        fee.Hours.Value.Should().Be(2.1m);
        fee.Rate.Value.Should().Be(260m);
        fee.Amount.Value.Should().Be(546m);
        fee.TaskCodeAsBilled.Value.Should().Be("L430");
        fee.ActivityCodeAsBilled.Value.Should().Be("A104");
        fee.Narrative.Value.Should().StartWith("[L430/A104] Revise complaint");
        fee.Quantity.Value.Should().BeNull();
        fee.ExpenseCodeAsBilled.Value.Should().BeNull();
        fee.RowBoundingBox.Should().NotBeNull();
        fee.RowBoundingBox!.Page.Should().Be(1);
        fee.RowBoundingBox.X.Should().Be(fee.Date.BoundingBox!.X, "the row starts at its date column");

        var expense = invoice.Lines[3];
        expense.Narrative.Value.Should().Be("[E110] Airfare to Atlanta for deposition");
        expense.Quantity.Value.Should().Be(1m);
        expense.UnitCost.Value.Should().Be(350.71m);
        expense.Amount.Value.Should().Be(350.71m);
        expense.ExpenseCodeAsBilled.Value.Should().Be("E110");
        expense.Hours.Value.Should().BeNull();
        expense.TimekeeperAsBilled.Value.Should().BeNull();

        invoice.TimekeeperSummary.Select(s => s.Name.Value).Should().Equal("Dalia Abernathy", "Imani Wickham", "Katya Rosenthal");
        invoice.TimekeeperSummary[1].Rate.Value.Should().Be(450m);
    }

    [Fact]
    public async Task ReadInvoiceAsync_MissingOptionalField_IsANullValueThatKeepsItsConfidence()
    {
        var invoice = await Sut(PreviewOptions(), Serving(ContentUnderstandingRecordedResults.PreviewB0002)).ReadInvoiceAsync(Request());

        // Recorded: "TitleAsBilled": {"type": "string", "confidence": 0.971} — the document prints no title.
        var title = invoice.Lines[0].TitleAsBilled;
        title.Value.Should().BeNull();
        title.Confidence.Should().Be(0.971m);
        title.Page.Should().BeNull();
        title.BoundingBox.Should().BeNull();
    }

    [Fact]
    public async Task ReadInvoiceAsync_ValueSpanningTwoRegions_HasTheUnionBox()
    {
        var invoice = await Sut(PreviewOptions(), Serving(ContentUnderstandingRecordedResults.PreviewB0002)).ReadInvoiceAsync(Request());

        // D(1,1.3416,3.2185,1.7411,3.2176,…,3.3531);D(1,1.3468,3.3918,1.8729,3.3918,…,3.5294,…): name wrapped on two lines.
        invoice.Lines[0].TimekeeperAsBilled.BoundingBox.Should().Be(new ExtractedBoundingBox(1, 0.1578m, 0.2925m, 0.0625m, 0.0284m));
    }

    // ── page ranges and the page limit ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadInvoiceAsync_LongDocument_IsReadInRangesAndMerged()
    {
        var service = new FakeContentUnderstandingService
        {
            OnResult = range => JsonResponse(HttpStatusCode.OK, range switch
            {
                "1-2" => Result(1, 2, pageCount: 3, Header("FirmInvoiceNumber", "A-1", 0.5m, page: 1) + "," + FeeRows(1, "2025-07-02")),
                "3-4" => Result(3, 3, pageCount: 3, Header("FirmInvoiceNumber", "B-2", 0.9m, page: 3) + "," + FeeRows(3, "2025-07-03")),
                _ => throw new InvalidOperationException(range),
            }),
        };

        var invoice = await Sut(PreviewOptions(o => o.PageRangeSize = 2), service).ReadInvoiceAsync(Request());

        service.AnalyzedRanges.Should().Equal("1-2", "3-4");
        invoice.PageCount.Should().Be(3);
        invoice.Header.FirmInvoiceNumber.Value.Should().Be("B-2", "the header value with the highest confidence wins");
        invoice.Header.FirmInvoiceNumber.Page.Should().Be(3, "page numbers are the original file's");
        invoice.Lines.Select(l => (l.RowIndex, l.Date.Page)).Should().Equal((0, 1), (1, 3));
    }

    [Fact]
    public async Task ReadInvoiceAsync_GaDocumentEndingOnARangeBoundary_StopsWhenTheNextRangeIsOutOfRange()
    {
        var service = new FakeContentUnderstandingService
        {
            OnAnalyze = range => range == "3-4" ? Error(HttpStatusCode.BadRequest, "InvalidRequest", "InvalidPagesOutOfRange") : null,
            OnResult = _ => JsonResponse(HttpStatusCode.OK, Result(1, 2, pageCount: null, Header("FirmInvoiceNumber", "A-1", 0.9m, page: 1))),
        };

        var invoice = await Sut(GaOptions(o => o.PageRangeSize = 2), service).ReadInvoiceAsync(Request());

        service.AnalyzedRanges.Should().Equal("1-2", "3-4");
        invoice.PageCount.Should().Be(2);
    }

    [Fact]
    public async Task ReadInvoiceAsync_ServiceRefusesThePageCount_IsPageLimit()
    {
        var service = new FakeContentUnderstandingService
        {
            OnAnalyze = _ => Error(HttpStatusCode.BadRequest, "InvalidRequest", "InputPageCountExceeded"),
        };

        await ReadShouldFail(Sut(PreviewOptions(), service), InvoiceAiException.ReadPageLimit);
    }

    [Fact]
    public async Task ReadInvoiceAsync_PreviewReportsMorePagesThanMaxPages_IsPageLimitAfterTheFirstRange()
    {
        var service = new FakeContentUnderstandingService
        {
            OnResult = _ => JsonResponse(HttpStatusCode.OK, Result(1, 2, pageCount: 26, Header("FirmInvoiceNumber", "A-1", 0.9m, page: 1))),
        };

        await ReadShouldFail(Sut(PreviewOptions(o => { o.MaxPages = 5; o.PageRangeSize = 2; }), service), InvoiceAiException.ReadPageLimit);
        service.AnalyzedRanges.Should().Equal("1-2");
    }

    [Fact]
    public async Task ReadInvoiceAsync_GaDocumentLongerThanMaxPages_IsPageLimitOnceThePageAfterTheLimitExists()
    {
        var service = new FakeContentUnderstandingService
        {
            OnResult = range => JsonResponse(HttpStatusCode.OK, range switch
            {
                "1-2" => Result(1, 2, pageCount: null, Header("FirmInvoiceNumber", "A-1", 0.9m, page: 1)),
                "3-4" => Result(3, 4, pageCount: null, "\"FirmName\":{\"type\":\"string\",\"confidence\":0.5}"),
                _ => throw new InvalidOperationException("page 5 is probed, never read"),
            }),
        };

        await ReadShouldFail(Sut(GaOptions(o => { o.MaxPages = 4; o.PageRangeSize = 2; }), service), InvoiceAiException.ReadPageLimit);
        service.AnalyzedRanges.Should().Equal("1-2", "3-4", "5");
    }

    [Fact]
    public async Task ReadInvoiceAsync_GaDocumentOfExactlyMaxPages_IsRead()
    {
        var service = new FakeContentUnderstandingService
        {
            OnAnalyze = range => range == "5" ? Error(HttpStatusCode.BadRequest, "InvalidRequest", "InvalidPagesOutOfRange") : null,
            OnResult = range => JsonResponse(HttpStatusCode.OK, range == "1-2"
                ? Result(1, 2, pageCount: null, Header("FirmInvoiceNumber", "A-1", 0.9m, page: 1))
                : Result(3, 4, pageCount: null, "\"FirmName\":{\"type\":\"string\",\"confidence\":0.5}")),
        };

        var invoice = await Sut(GaOptions(o => { o.MaxPages = 4; o.PageRangeSize = 2; }), service).ReadInvoiceAsync(Request());

        service.AnalyzedRanges.Should().Equal("1-2", "3-4", "5");
        invoice.PageCount.Should().Be(4);
    }

    // ── reason codes ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadInvoiceAsync_EncryptedPdf_IsUnreadable()
    {
        // Measured on dev: an encrypted (and a corrupt) PDF is refused synchronously with FailedToExtractPageCount.
        var service = new FakeContentUnderstandingService
        {
            OnAnalyze = _ => Error(HttpStatusCode.BadRequest, "InvalidRequest", "FailedToExtractPageCount"),
        };

        await ReadShouldFail(Sut(PreviewOptions(), service), InvoiceAiException.ReadUnreadable);
    }

    [Fact]
    public async Task ReadInvoiceAsync_FileOverTheServiceRequestSize_IsUnreadable()
    {
        var service = new FakeContentUnderstandingService
        {
            OnAnalyze = _ => Error(HttpStatusCode.RequestEntityTooLarge, "RequestEntityTooLarge"),
        };

        await ReadShouldFail(Sut(PreviewOptions(), service), InvoiceAiException.ReadUnreadable);
        service.Analyzes.Should().ContainSingle("a file that is too large is not retried");
    }

    [Fact]
    public async Task ReadInvoiceAsync_EmptyContent_IsUnreadableWithoutCallingTheService()
    {
        var service = new FakeContentUnderstandingService();

        await ReadShouldFail(Sut(PreviewOptions(), service), InvoiceAiException.ReadUnreadable, Array.Empty<byte>());
        service.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadInvoiceAsync_BlankDocument_IsNoInvoice()
    {
        // Measured on dev: a blank PDF succeeds with every field carrying a confidence and no value.
        var service = Serving(Result(1, 1, pageCount: 1,
            "\"FirmInvoiceNumber\":{\"type\":\"string\",\"confidence\":0.9},\"TotalDue\":{\"type\":\"number\",\"confidence\":0.9}," +
            "\"TimeEntries\":{\"type\":\"array\"},\"Expenses\":{\"type\":\"array\",\"valueArray\":[]}"));

        await ReadShouldFail(Sut(PreviewOptions(), service), InvoiceAiException.ReadNoInvoice);
    }

    public static TheoryData<string, string> MalformedSucceededResults() => new()
    {
        { "no contents", """{"id":"op","status":"Succeeded","result":{"analyzerId":"a","apiVersion":"v","contents":[]}}""" },
        { "content without fields", """{"id":"op","status":"Succeeded","result":{"contents":[{"kind":"document","startPageNumber":1,"endPageNumber":1}]}}""" },
        { "fields not an object", """{"id":"op","status":"Succeeded","result":{"contents":[{"kind":"document","fields":[]}]}}""" },
    };

    [Theory]
    [MemberData(nameof(MalformedSucceededResults))]
    public async Task ReadInvoiceAsync_SucceededResultWithoutFields_IsServiceFailedNotNoInvoice(string why, string resultJson)
    {
        // Our analyzers always return a fields object, even for a blank page; without one the read says nothing about
        // the document, so it must not tell the user the file is not an invoice.
        var sut = Sut(PreviewOptions(), Serving(resultJson));

        var ex = await FluentActions.Awaiting(() => sut.ReadInvoiceAsync(Request())).Should().ThrowAsync<InvoiceAiException>(why);
        ex.Which.ReasonCode.Should().Be(InvoiceAiException.ReadServiceFailed, why);
    }

    [Fact]
    public async Task ReadInvoiceAsync_PreviewPageCountOnALaterRange_IsTheWholeFileCount()
    {
        // Measured on dev 2026-10-15 (2026-06-01-preview): range 1-2 of a 302-page PDF reports pageCount "302", and
        // range 21-26 of a 26-page invoice reports "26". The count is the whole file's, not the range's, so the first
        // range of a long document does not end the read.
        var service = new FakeContentUnderstandingService
        {
            OnResult = range => JsonResponse(HttpStatusCode.OK, range switch
            {
                "1-20" => Result(1, 20, pageCount: 26, FeeRows(1, "2025-07-02")),
                "21-40" => Result(21, 26, pageCount: 26, FeeRows(26, "2025-07-03")),
                _ => throw new InvalidOperationException(range),
            }),
        };

        var invoice = await Sut(PreviewOptions(), service).ReadInvoiceAsync(Request());

        service.AnalyzedRanges.Should().Equal("1-20", "21-40");
        invoice.PageCount.Should().Be(26);
        invoice.Lines.Select(l => l.Date.Page).Should().Equal(1, 26);
    }

    [Fact]
    public async Task ReadInvoiceAsync_PersistentServerError_IsServiceFailedAfterThreeAttempts()
    {
        var service = new FakeContentUnderstandingService { OnAnalyze = _ => RetryNow(HttpStatusCode.InternalServerError) };

        await ReadShouldFail(Sut(PreviewOptions(), service), InvoiceAiException.ReadServiceFailed);
        service.Analyzes.Should().HaveCount(3);
    }

    [Fact]
    public async Task ReadInvoiceAsync_ThrottledOnce_RetriesAndReads()
    {
        var attempts = 0;
        var service = new FakeContentUnderstandingService
        {
            OnAnalyze = _ => ++attempts == 1 ? RetryNow(HttpStatusCode.TooManyRequests) : null,
            OnResult = _ => JsonResponse(HttpStatusCode.OK, ContentUnderstandingRecordedResults.PreviewB0002),
        };

        var invoice = await Sut(PreviewOptions(), service).ReadInvoiceAsync(Request());

        service.Analyzes.Should().HaveCount(2);
        invoice.Lines.Should().HaveCount(5);
    }

    [Fact]
    public async Task ReadInvoiceAsync_AnalyzerMissing_IsServiceFailed()
    {
        // Measured on dev: a preview analyzer id under the GA api-version (or a missing analyzer) is 404 ModelNotFound.
        var service = new FakeContentUnderstandingService
        {
            OnAnalyze = _ => Error(HttpStatusCode.NotFound, "NotFound", "ModelNotFound"),
        };

        await ReadShouldFail(Sut(GaOptions(o => o.AnalyzerId = "sprk_invoice_v2p"), service), InvoiceAiException.ReadServiceFailed);
    }

    [Fact]
    public async Task ReadInvoiceAsync_OperationFails_IsServiceFailed()
    {
        var service = new FakeContentUnderstandingService
        {
            OnResult = _ => JsonResponse(HttpStatusCode.OK,
                """{"id":"x","status":"Failed","error":{"code":"InternalServerError","message":"m"}}"""),
        };

        await ReadShouldFail(Sut(PreviewOptions(), service), InvoiceAiException.ReadServiceFailed);
    }

    [Fact]
    public async Task ReadInvoiceAsync_OperationFailsOnTheDocument_IsUnreadable()
    {
        var service = new FakeContentUnderstandingService
        {
            OnResult = _ => JsonResponse(HttpStatusCode.OK,
                """{"id":"x","status":"Failed","error":{"code":"InvalidRequest","message":"m","innererror":{"code":"UnsupportedContent","message":"m"}}}"""),
        };

        await ReadShouldFail(Sut(PreviewOptions(), service), InvoiceAiException.ReadUnreadable);
    }

    [Fact]
    public async Task ReadInvoiceAsync_OperationNeverFinishes_IsServiceFailedAtTheTimeout()
    {
        var time = new FakeTimeProvider();
        var service = new FakeContentUnderstandingService
        {
            OnPoll = (_, poll) =>
            {
                time.Advance(TimeSpan.FromSeconds(200));
                return JsonResponse(HttpStatusCode.OK, """{"id":"x","status":"Running"}""");
            },
        };

        await ReadShouldFail(Sut(PreviewOptions(), service, time), InvoiceAiException.ReadServiceFailed);
        service.Requests.Count(r => r.Method == HttpMethod.Get).Should().Be(4, "polls at 0, 200, 400 and 600 s; the deadline passes at 800 s");
    }

    [Fact]
    public async Task ReadInvoiceAsync_OperationLocationOnAnotherHost_IsServiceFailedAndTheTokenIsNotSentThere()
    {
        var service = Serving(ContentUnderstandingRecordedResults.PreviewB0002);
        service.OperationHost = "attacker.example.com";

        await ReadShouldFail(Sut(PreviewOptions(), service), InvoiceAiException.ReadServiceFailed);
        service.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task ReadInvoiceAsync_NotConfigured_ThrowsFeatureDisabledWithoutCallingTheService()
    {
        var service = new FakeContentUnderstandingService();

        var ex = await FluentActions.Awaiting(() => Sut(new ContentUnderstandingOptions(), service).ReadInvoiceAsync(Request()))
            .Should().ThrowAsync<FeatureDisabledException>();

        ex.Which.ErrorCode.Should().Be("ai.invoice.read.not_configured");
        service.Requests.Should().BeEmpty();
    }

    // ── deterministic completion on the read path (task 005 condition 1) ───────────────────────────────

    [Fact]
    public async Task ReadInvoiceAsync_ClockNotationHours_AreConvertedAndMarked()
    {
        var service = Serving(Result(1, 1, pageCount: 1,
            Header("FirmInvoiceNumber", "A-1", 0.9m, page: 1) + "," +
            "\"TimeEntries\":{\"type\":\"array\",\"valueArray\":[{\"type\":\"object\",\"valueObject\":{" +
            "\"Hours\":{\"type\":\"number\",\"valueNumber\":1.30,\"confidence\":0.6,\"source\":\"D(1,6,3,6.3,3,6.3,3.1,6,3.1)\"}," +
            "\"Rate\":{\"type\":\"number\",\"valueNumber\":300,\"confidence\":0.8}," +
            "\"Amount\":{\"type\":\"number\",\"valueNumber\":450,\"confidence\":0.8}}}]}"));

        var invoice = await Sut(PreviewOptions(), service).ReadInvoiceAsync(Request());

        var hours = invoice.Lines.Should().ContainSingle().Subject.Hours;
        hours.Value.Should().Be(1.5m);
        hours.ReturnedValue.Should().Be(1.30m);
        hours.Repair.Should().Be(ExtractedValueRepair.ClockHoursConverted);
        hours.Page.Should().Be(1, "a converted value keeps the source it was read from");
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────────────

    private static async Task ReadShouldFail(InvoiceAi sut, string reasonCode, byte[]? content = null)
    {
        var ex = await FluentActions.Awaiting(() => sut.ReadInvoiceAsync(Request(content))).Should().ThrowAsync<InvoiceAiException>();
        ex.Which.ReasonCode.Should().Be(reasonCode);
        ex.Which.ConsumerType.Should().Be(InvoiceAiException.ReadCapability);
    }

    private static HttpResponseMessage RetryNow(HttpStatusCode status)
    {
        var response = Error(status, status.ToString());
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }

    /// <summary>A Succeeded result for one range, shaped like the service's (pages 8.5 x 11 in).</summary>
    private static string Result(int start, int end, int? pageCount, string fields)
    {
        var pages = string.Join(",", Enumerable.Range(start, end - start + 1)
            .Select(p => $$"""{"pageNumber":{{p}},"angle":0,"width":8.5,"height":11}"""));
        var metadata = pageCount is null ? string.Empty : $$""","metadata":{"contentType":"application/pdf","pageCount":"{{pageCount}}"}""";
        return $$$"""
            {"id":"op","status":"Succeeded","result":{"analyzerId":"a","apiVersion":"v","warnings":[],"contents":[
            {"kind":"document","startPageNumber":{{{start}}},"endPageNumber":{{{end}}},"unit":"inch","pages":[{{{pages}}}],
            "fields":{{{{fields}}}}{{{metadata}}}}]}}
            """;
    }

    private static string Header(string name, string value, decimal confidence, int page) =>
        $$"""
        "{{name}}":{"type":"string","valueString":"{{value}}","confidence":{{confidence.ToString(CultureInfo.InvariantCulture)}},"source":"D({{page}},1,1,2,1,2,1.2,1,1.2)"}
        """;

    private static string FeeRows(int page, string date) =>
        $$$$"""
        "TimeEntries":{"type":"array","valueArray":[{"type":"object","valueObject":{
        "Date":{"type":"date","valueDate":"{{{{date}}}}","confidence":0.9,"source":"D({{{{page}}}},0.5,3,1.2,3,1.2,3.1,0.5,3.1)"},
        "Hours":{"type":"number","valueNumber":1,"confidence":0.9},"Rate":{"type":"number","valueNumber":100,"confidence":0.9},
        "Amount":{"type":"number","valueNumber":100,"confidence":0.9}}}]}
        """;
}
