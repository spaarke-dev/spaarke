using System.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="InvoiceTotalsService.RecalculateAsync"/> (ebilling-r1 task 016, FR-07, FR-32): the persistence half of I-1.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): "a refused result writes nothing" and "the write is conditional on the version read" are the two
/// guarantees callers (049, 100-102) rely on. Dataverse is mocked at the narrow interfaces the service takes.
/// </remarks>
public sealed class InvoiceTotalsServiceTests
{
    private static readonly Guid InvoiceId = Guid.NewGuid();

    private readonly Mock<IFieldMappingDataverseService> _records = new();
    private readonly Mock<IGenericEntityService> _entities = new();

    private InvoiceTotalsService CreateService() =>
        new(_records.Object, _entities.Object, Mock.Of<ILogger<InvoiceTotalsService>>());

    private void GivenInvoice(object? billed, object? version = null) =>
        _records
            .Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object?> { ["sprk_totalamount"] = billed, ["versionnumber"] = version ?? 42L });

    private void GivenLines(params Entity[] lines) =>
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(lines.ToList()));

    private static Entity Line(InvoiceLineCostType type, decimal amount, InvoiceLineStatus? status = null, decimal? approved = null,
        InvoiceAdjustmentMethod? method = null, decimal? value = null)
    {
        var line = new Entity("sprk_billingevent", Guid.NewGuid()) { ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId) }; // as the query returns it (164a fix round K2)
        line["sprk_costtype"] = new OptionSetValue((int)type);
        line["sprk_amount"] = new Money(amount);
        if (status is not null) line["sprk_linestatus"] = new OptionSetValue((int)status.Value);
        if (approved is not null) line["sprk_approvedamount"] = new Money(approved.Value);
        if (method is not null) line["sprk_adjustmentmethod"] = new OptionSetValue((int)method.Value);
        if (value is not null) line["sprk_adjustmentvalue"] = value.Value;
        return line;
    }

    [Fact]
    public async Task Recalculate_WritesTheFiveTotals_ConditionallyOnTheVersionRead_AndTheAdjustmentLineAmount()
    {
        GivenInvoice(1500L, version: 77L);
        var adjustment = Line(InvoiceLineCostType.Adjustment, 0m, method: InvoiceAdjustmentMethod.Percent, value: 10m);
        GivenLines(
            Line(InvoiceLineCostType.Fee, 400m, InvoiceLineStatus.Reduced, 300m),
            Line(InvoiceLineCostType.Fee, 600m),
            Line(InvoiceLineCostType.Expense, 500m),
            adjustment);

        Dictionary<string, object?>? invoiceWrite = null;
        long? versionUsed = null;
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, f, v, _) => { invoiceWrite = f; versionUsed = v; })
            .Returns(Task.CompletedTask);

        var result = await CreateService().RecalculateAsync(InvoiceId);

        result.IsRefused.Should().BeFalse();
        versionUsed.Should().Be(77L);
        invoiceWrite.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_feestotal"] = 1000m,
            ["sprk_expensestotal"] = 500m,
            ["sprk_lineadjustmentstotal"] = 100m,
            ["sprk_invoiceadjustmentstotal"] = 90m,
            ["sprk_approvedtotal"] = 1310m,
        });
        _records.Verify(r => r.UpdateExistingRecordFieldsAsync(
            "sprk_billingevent", adjustment.Id,
            It.Is<Dictionary<string, object?>>(f => f.Count == 1 && (decimal)f["sprk_amount"]! == 90m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Task 164a-2 (EB-D-193, EB-D-199): the recalculation reads the marker from the extraction column and applies the added-line rule.
    [Theory]
    [InlineData("""{"provenance":{"source":"reviewer","decisionId":"0d0d0d0d-0000-4000-8000-000000000001"}}""", true)]
    [InlineData("""{"provenance":{"analyzerId":"sprk_invoice_v1","apiVersion":"2026-06-01-preview"}}""", false)]
    [InlineData("""{"provenance":{"source":"extraction"}}""", false)]
    [InlineData("""{"provenance":{"source":"reviewer"}}""", false)]
    [InlineData("""{"provenance":{"source":"reviewer","decisionId":"0d0d0d0d-0000-4000-8000-000000000002"}}""", false)] // F1: names another decision than the row's id
    [InlineData("""{"fields":{}}""", false)]
    [InlineData("not json", false)]
    [InlineData(null, false)]
    public async Task Recalculate_ReadsTheReviewerAddedMarkerFromTheExtractionColumn_AndStoresTheApprovedTotalPerEb199(string? extraction, bool reviewerAdded)
    {
        GivenInvoice(1000L, version: 5L);
        var added = Line(InvoiceLineCostType.Fee, 200m, InvoiceLineStatus.Accepted, approved: 200m);
        // F1: a row Add line created has the id derived from its decision (0d0d0d0d-...-001 in the markers above).
        added.Id = ExtractionProvenance.AddedLineIdFor(InvoiceId, Guid.Parse("0d0d0d0d-0000-4000-8000-000000000001"));
        if (extraction is not null) added["sprk_extractionconfidence"] = extraction;
        QueryExpression? query = null;
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => query = q)
            .ReturnsAsync(new EntityCollection(new List<Entity> { Line(InvoiceLineCostType.Fee, 1000m), added }));

        Dictionary<string, object?>? invoiceWrite = null;
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 5L, It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, f, _, _) => invoiceWrite = f)
            .Returns(Task.CompletedTask);

        var result = await CreateService().RecalculateAsync(InvoiceId);

        query!.ColumnSet.Columns.Should().Contain("sprk_extractionconfidence", "the marker lives there; without it an added line would be counted as billed");
        query.ColumnSet.Columns.Should().Contain("sprk_invoice", "164a fix round K2: the reviewer-added rule reads the line's invoice");
        result.IsRefused.Should().BeFalse();
        if (reviewerAdded)
        {
            // billed stays 1000; the Accepted added line of 200 is added to the approved total, and is in neither the fees nor the expenses.
            invoiceWrite!["sprk_approvedtotal"].Should().Be(1200m);
            invoiceWrite["sprk_feestotal"].Should().Be(1000m);
            invoiceWrite["sprk_expensestotal"].Should().Be(0m);
        }
        else
        {
            // An intake line: it is in the fees total and (Accepted) adjusts nothing, so approved stays the billed total.
            invoiceWrite!["sprk_approvedtotal"].Should().Be(1000m);
            invoiceWrite["sprk_feestotal"].Should().Be(1200m);
        }
    }

    [Fact(DisplayName = "164a-2 F1 must-fire: an INTAKE line carrying a valid-looking reviewer marker (any decision id) stays in billed, fees and the percent base; the totals are as without the marker")]
    public async Task Recalculate_AnIntakeLineWithACopiedMarker_IsStillBilled()
    {
        GivenInvoice(1200L, version: 5L);
        var intake = Line(InvoiceLineCostType.Fee, 200m, InvoiceLineStatus.Disallowed);
        intake["sprk_extractionconfidence"] = ExtractionProvenance.ForReviewerAddedLine(intake.Id);
        var other = Line(InvoiceLineCostType.Fee, 1000m, InvoiceLineStatus.Accepted, approved: 1000m);
        other["sprk_extractionconfidence"] = ExtractionProvenance.ForReviewerAddedLine(Guid.NewGuid());
        GivenLines(other, intake);

        Dictionary<string, object?>? invoiceWrite = null;
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 5L, It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, f, _, _) => invoiceWrite = f)
            .Returns(Task.CompletedTask);

        await CreateService().RecalculateAsync(InvoiceId);

        invoiceWrite!["sprk_feestotal"].Should().Be(1200m);
        invoiceWrite["sprk_lineadjustmentstotal"].Should().Be(200m, "the disallowed intake line is still a firm-line adjustment");
        invoiceWrite["sprk_approvedtotal"].Should().Be(1000m);
    }

    // D-121 (ebilling-r1 task 049 part A): a credit line is read with its negative sprk_amount and summed as a signed value.
    [Fact]
    public async Task Recalculate_WithACreditLine_SumsItSigned_AndWritesTheNetTotals()
    {
        GivenInvoice(1050L, version: 9L);
        GivenLines(
            Line(InvoiceLineCostType.Fee, 1000m),
            Line(InvoiceLineCostType.Fee, -100m),   // credit
            Line(InvoiceLineCostType.Expense, 200m),
            Line(InvoiceLineCostType.Expense, -50m)); // credit

        Dictionary<string, object?>? invoiceWrite = null;
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 9L, It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, f, _, _) => invoiceWrite = f)
            .Returns(Task.CompletedTask);

        var result = await CreateService().RecalculateAsync(InvoiceId);

        result.IsRefused.Should().BeFalse();
        invoiceWrite.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_feestotal"] = 900m,
            ["sprk_expensestotal"] = 150m,
            ["sprk_lineadjustmentstotal"] = 0m,
            ["sprk_invoiceadjustmentstotal"] = 0m,
            ["sprk_approvedtotal"] = 1050m,
        });
    }

    [Fact]
    // D-127: an all-credit invoice (a credit memo) has negative totals, which are stored as computed.
    public async Task Recalculate_OfAnAllCreditInvoice_StoresTheNegativeTotals_D127()
    {
        GivenInvoice(-300L, version: 9L);
        GivenLines(Line(InvoiceLineCostType.Fee, -200m), Line(InvoiceLineCostType.Fee, -100m));

        Dictionary<string, object?>? invoiceWrite = null;
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), 9L, It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, f, _, _) => invoiceWrite = f)
            .Returns(Task.CompletedTask);

        var result = await CreateService().RecalculateAsync(InvoiceId);

        result.IsRefused.Should().BeFalse();
        invoiceWrite.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_feestotal"] = -300m,
            ["sprk_expensestotal"] = 0m,
            ["sprk_lineadjustmentstotal"] = 0m,
            ["sprk_invoiceadjustmentstotal"] = 0m,
            ["sprk_approvedtotal"] = -300m,
        });
    }

    [Fact]
    public async Task Recalculate_LeavesAnAdjustmentLineAlone_WhenItsStoredAmountIsAlreadyRight()
    {
        GivenInvoice(1000L);
        GivenLines(
            Line(InvoiceLineCostType.Fee, 1000m),
            Line(InvoiceLineCostType.Adjustment, 100m, method: InvoiceAdjustmentMethod.Percent, value: 10m));

        await CreateService().RecalculateAsync(InvoiceId);

        _records.Verify(r => r.UpdateExistingRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Recalculate_WhenTheResultWouldBeNegative_ReturnsTheRefusal_AndWritesNothing()
    {
        GivenInvoice(100L);
        GivenLines(
            Line(InvoiceLineCostType.Fee, 100m),
            Line(InvoiceLineCostType.Adjustment, 0m, method: InvoiceAdjustmentMethod.Fixed, value: 150m));

        var result = await CreateService().RecalculateAsync(InvoiceId);

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.ApprovedTotalNegative);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Recalculate_WhenALineCannotBePlaced_ReturnsTheRefusal_AndWritesNothing()
    {
        GivenInvoice(100L);
        var noCostType = new Entity("sprk_billingevent", Guid.NewGuid()) { ["sprk_amount"] = new Money(100m) };
        GivenLines(noCostType);

        var result = await CreateService().RecalculateAsync(InvoiceId);

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.LineCostTypeUnknown);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Recalculate_WhenTheInvoiceChangedInBetween_ReReadsAndWritesAgainWithTheFreshVersion()
    {
        var versions = new Queue<object?>(new object?[] { 10L, 11L });
        _records
            .Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new Dictionary<string, object?> { ["sprk_totalamount"] = 100L, ["versionnumber"] = versions.Dequeue() });
        GivenLines(Line(InvoiceLineCostType.Fee, 100m));

        var attempts = new List<long>();
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, _, v, _) =>
            {
                attempts.Add(v);
                return attempts.Count == 1 ? Task.FromException(new DBConcurrencyException("changed")) : Task.CompletedTask;
            });

        var result = await CreateService().RecalculateAsync(InvoiceId);

        result.IsRefused.Should().BeFalse();
        attempts.Should().Equal(10L, 11L);
    }

    [Fact]
    public async Task Recalculate_WhenTheInvoiceKeepsChanging_ThrowsAfterTheMaxAttempts()
    {
        GivenInvoice(100L);
        GivenLines(Line(InvoiceLineCostType.Fee, 100m));
        _records
            .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DBConcurrencyException("changed"));

        var act = () => CreateService().RecalculateAsync(InvoiceId);

        await act.Should().ThrowAsync<DBConcurrencyException>();
        _records.Verify(r => r.UpdateRecordFieldsIfUnchangedAsync(
            "sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Exactly(InvoiceTotalsService.MaxWriteAttempts));
    }

    [Fact]
    public async Task Recalculate_ForAMissingInvoice_Throws_AndWritesNothing()
    {
        _records
            .Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object?>());

        var act = () => CreateService().RecalculateAsync(InvoiceId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Recalculate_WithoutAVersion_Throws_RatherThanWriteUnconditionally()
    {
        _records
            .Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object?> { ["sprk_totalamount"] = 100L, ["versionnumber"] = null });

        var act = () => CreateService().RecalculateAsync(InvoiceId);

        await act.Should().ThrowAsync<InvalidOperationException>();
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Recalculate_ReadsAFractionalBilledTotal_ThatArrivesAsADouble_WhateverTheHostCulture()
    {
        // RetrieveRecordFieldsAsync returns a double for any non-integral JSON number; under de-DE a double.ToString() is "1500,5".
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            GivenInvoice(1500.5d);
            GivenLines(Line(InvoiceLineCostType.Fee, 1500.5m));
            Dictionary<string, object?>? write = null;
            _records
                .Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Callback<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, f, _, _) => write = f)
                .Returns(Task.CompletedTask);

            await CreateService().RecalculateAsync(InvoiceId);

            write!["sprk_approvedtotal"].Should().Be(1500.5m);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public async Task Recalculate_WithoutABilledTotal_ReturnsTheRefusal_AndWritesNothing()
    {
        GivenInvoice(null);
        GivenLines(Line(InvoiceLineCostType.Fee, 100m));

        var result = await CreateService().RecalculateAsync(InvoiceId);

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.BilledTotalMissing);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Recalculate_WhenAnAdjustmentLineIsDeletedMidWay_ReReads_InsteadOfReportingInvoiceNotFound()
    {
        GivenInvoice(1000L);
        var gone = Line(InvoiceLineCostType.Adjustment, 0m, method: InvoiceAdjustmentMethod.Percent, value: 10m);
        GivenLines(Line(InvoiceLineCostType.Fee, 1000m), gone);
        _records
            .SetupSequence(r => r.UpdateExistingRecordFieldsAsync("sprk_billingevent", gone.Id, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("line gone"))
            .Returns(Task.CompletedTask);

        var result = await CreateService().RecalculateAsync(InvoiceId);

        result.IsRefused.Should().BeFalse();
        _records.Verify(r => r.UpdateRecordFieldsIfUnchangedAsync(
            "sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recalculate_ReadsEveryPageOfLines()
    {
        GivenInvoice(300L);
        var first = new EntityCollection(new List<Entity> { Line(InvoiceLineCostType.Fee, 100m), Line(InvoiceLineCostType.Fee, 100m) })
        {
            MoreRecords = true,
            PagingCookie = "cookie",
        };
        var second = new EntityCollection(new List<Entity> { Line(InvoiceLineCostType.Fee, 100m) });
        var pages = new Queue<EntityCollection>(new[] { first, second });
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => pages.Dequeue());

        var result = await CreateService().RecalculateAsync(InvoiceId);

        result.FeesTotal.Should().Be(300m);
    }

    private void VerifyNothingWritten()
    {
        _records.Verify(r => r.UpdateRecordFieldsIfUnchangedAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        _records.Verify(r => r.UpdateExistingRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()), Times.Never);
        _records.Verify(r => r.UpdateRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()), Times.Never);
    }
}
