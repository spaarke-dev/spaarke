using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// Ontology task 232 (D-230, D-61): <see cref="FinanceRollupService"/> runs after every finance Threshold Signal the evaluator raises, so a
/// recalculation that changes nothing must write nothing. Each PATCH (the counted rollup, then Pending Spend, D-104) is compared with the
/// parent's stored values first; a stored read that fails leaves the write as it was.
/// </summary>
public class FinanceRollupUnchangedWriteTests
{
    private static readonly Guid MatterId = Guid.NewGuid();

    private static readonly HashSet<string> MoneyColumns = new(StringComparer.Ordinal)
    {
        "sprk_totalspendtodate", "sprk_monthlyspendcurrent", "sprk_totalbudget", "sprk_remainingbudget", "sprk_averageinvoiceamount",
        "sprk_pendingspend",
    };

    private readonly Mock<IDataverseService> _reads = new();
    private readonly Mock<IFieldMappingDataverseService> _writer = new();
    private readonly List<Dictionary<string, object?>> _written = new();
    private Entity _stored = new("sprk_matter", MatterId);
    private Exception? _storedReadFault;

    public FinanceRollupUnchangedWriteTests()
    {
        // No counted or pending invoice (so no writer id is asked), one budget of 1000.
        _reads.Setup(r => r.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName == FinanceBaseValues.BudgetEntity
                ? new EntityCollection(new List<Entity> { new(FinanceBaseValues.BudgetEntity, Guid.NewGuid()) { [FinanceBaseValues.BudgetTotalBudget] = new Money(1000m) } })
                : new EntityCollection());
        _reads.Setup(r => r.RetrieveAsync("sprk_matter", MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid _, string[] columns, CancellationToken _) =>
            {
                if (_storedReadFault is not null) throw _storedReadFault;
                var row = new Entity("sprk_matter", MatterId);
                foreach (var column in columns.Where(_stored.Attributes.Contains)) row[column] = _stored[column];
                return row;
            });
        _writer
            .Setup(w => w.UpdateExistingRecordFieldsAsync("sprk_matter", MatterId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken>((_, _, fields, _) => _written.Add(fields))
            .Returns(Task.CompletedTask);
    }

    private FinanceRollupService Service() => new(
        _reads.Object, _writer.Object,
        new BillingDecisionRecorder(new Sprk.Bff.Api.Tests.Services.Signals.DecisionRecordWriterTests.Harness().Writer,
            Mock.Of<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>(), NullLogger<BillingDecisionRecorder>.Instance),
        NullLogger<FinanceRollupService>.Instance);

    /// <summary>What Dataverse holds after <paramref name="writes"/>: money as <see cref="Money"/>, every amount rounded to the columns'
    /// Accuracy 2.</summary>
    private static Entity StoredAfter(IEnumerable<Dictionary<string, object?>> writes)
    {
        var row = new Entity("sprk_matter", MatterId);
        foreach (var (column, value) in writes.SelectMany(w => w))
        {
            row[column] = value switch
            {
                decimal d when MoneyColumns.Contains(column) => new Money(Math.Round(d, 2)),
                decimal d => Math.Round(d, 2),
                _ => value,
            };
        }

        return row;
    }

    [Fact]
    public async Task Recalculate_TheStoredRollupAlreadyHoldsTheValues_WritesNothing()
    {
        await Service().RecalculateMatterAsync(MatterId);
        _written.Should().HaveCount(2, "the first recalculation finds nothing stored: the counted rollup, then Pending Spend");
        _written[0]["sprk_totalbudget"].Should().Be(1000m);

        _stored = StoredAfter(_written);
        _written.Clear();
        var response = await Service().RecalculateMatterAsync(MatterId);

        _written.Should().BeEmpty("nothing changed since the last recalculation (D-61)");
        response.TotalBudget.Should().Be(1000m, "the response still reports the recalculated figures");
    }

    [Fact]
    public async Task Recalculate_OneStoredValueDiffers_WritesTheCountedRollup_AndSkipsTheUnchangedPendingSpend()
    {
        await Service().RecalculateMatterAsync(MatterId);
        _stored = StoredAfter(_written);
        _stored["sprk_totalbudget"] = new Money(900m); // a budget edited since, on a form that did not recalculate
        _written.Clear();

        await Service().RecalculateMatterAsync(MatterId);

        _written.Should().ContainSingle().Which.Should().ContainKey("sprk_totalbudget").WhoseValue.Should().Be(1000m);
        _written.Single().Should().NotContainKey("sprk_pendingspend");
    }

    [Fact]
    public async Task Recalculate_StoredPendingSpendEmpty_WritesZero()
    {
        // ontology-r1 re-check F3 (049): 0 is a real value; an empty stored column against a computed 0 is a change.
        await Service().RecalculateMatterAsync(MatterId);
        _stored = StoredAfter(_written.Take(1));
        _written.Clear();

        await Service().RecalculateMatterAsync(MatterId);

        _written.Should().ContainSingle().Which.Should().Equal(new Dictionary<string, object?> { ["sprk_pendingspend"] = 0m });
    }

    [Fact]
    public async Task Recalculate_TheStoredReadFails_WritesAsBefore()
    {
        // Control: a parent gone, a column missing on the environment or a fault leaves the recalculation exactly as it was.
        _storedReadFault = new InvalidOperationException("stored read failed");

        await Service().RecalculateMatterAsync(MatterId);

        _written.Should().HaveCount(2);
    }

    [Fact]
    public async Task Recalculate_TheParentIsGone_StillSurfacesTheUpdateOnlyNotFound()
    {
        // Task 130 contract: the update-only write raises KeyNotFoundException for a deleted parent (the routes' 404); the failed stored
        // read before it must not hide that.
        _storedReadFault = new KeyNotFoundException("gone");
        _writer
            .Setup(w => w.UpdateExistingRecordFieldsAsync("sprk_matter", MatterId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var act = () => Service().RecalculateMatterAsync(MatterId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(95.238095238, 95.24, true)]   // an unrounded utilization against its stored (Accuracy 2) copy
    [InlineData(95.235, 95.24, true)]         // the midpoint either way Dataverse rounds it
    [InlineData(95.25, 95.24, false)]         // a cent is a change
    [InlineData(0, 0, true)]
    public void IsUnchanged_ComparesAmountsAtTheStoredPrecision(double computed, double stored, bool expected)
    {
        var fields = new Dictionary<string, object?> { ["sprk_budgetutilizationpercent"] = (decimal)computed };
        var row = new Entity("sprk_matter", MatterId) { ["sprk_budgetutilizationpercent"] = (decimal)stored };

        FinanceRollupService.IsUnchanged(fields, row).Should().Be(expected);
    }

    [Fact]
    public void IsUnchanged_MoneyCountAndText_AndAMissingColumnOrUnknownRowIsAChange()
    {
        var fields = new Dictionary<string, object?>
        {
            ["sprk_totalbudget"] = 1000m,
            ["sprk_invoicecount"] = 3,
            ["sprk_monthlyspendtimeline"] = """[{"month":"2026-11","spend":0}]""",
        };
        var row = new Entity("sprk_matter", MatterId)
        {
            ["sprk_totalbudget"] = new Money(1000.00m),
            ["sprk_invoicecount"] = 3,
            ["sprk_monthlyspendtimeline"] = """[{"month":"2026-11","spend":0}]""",
        };

        FinanceRollupService.IsUnchanged(fields, row).Should().BeTrue();
        FinanceRollupService.IsUnchanged(fields, null).Should().BeFalse("an unknown stored row is written");

        row["sprk_invoicecount"] = 4;
        FinanceRollupService.IsUnchanged(fields, row).Should().BeFalse();
        row["sprk_invoicecount"] = 3;
        row["sprk_monthlyspendtimeline"] = "[]";
        FinanceRollupService.IsUnchanged(fields, row).Should().BeFalse();
        row.Attributes.Remove("sprk_monthlyspendtimeline");
        FinanceRollupService.IsUnchanged(fields, row).Should().BeFalse("an empty column is not the computed value");
    }
}
