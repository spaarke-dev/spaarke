using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance.Tools;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Tools;

public class FinancialCalculationToolHandlerTests
{
    private readonly IDataverseService _dataverseService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<FinancialCalculationToolHandler> _logger;
    private readonly FinancialCalculationToolHandler _handler;

    public FinancialCalculationToolHandlerTests()
    {
        _dataverseService = Substitute.For<IDataverseService>();
        _telemetry = Substitute.For<FinanceTelemetry>();
        _logger = Substitute.For<ILogger<FinancialCalculationToolHandler>>();
        _handler = new FinancialCalculationToolHandler(_dataverseService, _telemetry, _logger);
    }

    [Fact]
    public void ToolName_ShouldReturnFinancialCalculation()
    {
        // Act
        var toolName = _handler.ToolName;

        // Assert
        toolName.Should().Be("FinancialCalculation");
    }

    [Fact]
    public async Task ExecuteAsync_RecalculateOperation_CalculatesFromAllInvoices()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entityType"] = "sprk_matter",
            ["recordId"] = matterId,
            ["operation"] = "recalculate"
        });

        var matterRecord = new Dictionary<string, object>
        {
            ["sprk_totalbudget"] = new Money { Value = 100000m },
            ["@odata.etag"] = "W/\"12345\""
        };

        var invoices = new List<Dictionary<string, object>>
        {
            new() { ["sprk_totalamount"] = new Money { Value = 15000m } },
            new() { ["sprk_totalamount"] = new Money { Value = 22000m } },
            new() { ["sprk_totalamount"] = new Money { Value = 18000m } }
        };

        _dataverseService.GetRecordAsync(
            "sprk_matter",
            matterId,
            Arg.Any<string[]>(),
            Arg.Any<CancellationToken>())
            .Returns(matterRecord);

        _dataverseService.QueryRecordsAsync(
            "sprk_invoice",
            $"_sprk_matter_value eq {matterId}",
            Arg.Any<string[]>(),
            Arg.Any<CancellationToken>())
            .Returns(invoices);

        _dataverseService.UpdateRecordAsync(
            "sprk_matter",
            matterId,
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();

        var totals = result.Data as MatterFinancialTotals;
        totals.Should().NotBeNull();
        totals!.TotalBudget.Should().Be(100000m);
        totals.TotalSpendToDate.Should().Be(55000m); // 15000 + 22000 + 18000
        totals.RemainingBudget.Should().Be(45000m); // 100000 - 55000
        totals.BudgetUtilizationPercent.Should().BeApproximately(55m, 0.01m); // 55000 / 100000 * 100
        totals.InvoiceCount.Should().Be(3);
        totals.AverageInvoiceAmount.Should().BeApproximately(18333.33m, 0.01m); // 55000 / 3

        await _dataverseService.Received(1).UpdateRecordAsync(
            "sprk_matter",
            matterId,
            Arg.Is<Dictionary<string, object>>(d =>
                ((Money)d["sprk_totalspendtodate"]).Value == 55000m &&
                ((Money)d["sprk_remainingbudget"]).Value == 45000m &&
                (decimal)d["sprk_budgetutilizationpercent"] == 55m &&
                (int)d["sprk_invoicecount"] == 3),
            "W/\"12345\"",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_IncrementOperation_AddsInvoiceAmount()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entityType"] = "sprk_matter",
            ["recordId"] = matterId,
            ["operation"] = "increment",
            ["invoiceAmount"] = 15000m
        });

        var matterRecord = new Dictionary<string, object>
        {
            ["sprk_totalbudget"] = new Money { Value = 100000m },
            ["sprk_totalspendtodate"] = new Money { Value = 40000m },
            ["sprk_invoicecount"] = 2,
            ["@odata.etag"] = "W/\"12345\""
        };

        _dataverseService.GetRecordAsync(
            "sprk_matter",
            matterId,
            Arg.Any<string[]>(),
            Arg.Any<CancellationToken>())
            .Returns(matterRecord);

        _dataverseService.UpdateRecordAsync(
            "sprk_matter",
            matterId,
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();

        var totals = result.Data as MatterFinancialTotals;
        totals.Should().NotBeNull();
        totals!.TotalSpendToDate.Should().Be(55000m); // 40000 + 15000
        totals.RemainingBudget.Should().Be(45000m); // 100000 - 55000
        totals.BudgetUtilizationPercent.Should().BeApproximately(55m, 0.01m);
        totals.InvoiceCount.Should().Be(3); // 2 + 1
        totals.AverageInvoiceAmount.Should().BeApproximately(18333.33m, 0.01m); // 55000 / 3
    }

    [Fact]
    public async Task ExecuteAsync_Project_UsesProjectEntity()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entityType"] = "sprk_project",
            ["recordId"] = projectId,
            ["operation"] = "recalculate"
        });

        var projectRecord = new Dictionary<string, object>
        {
            ["sprk_totalbudget"] = new Money { Value = 50000m },
            ["@odata.etag"] = "W/\"12345\""
        };

        _dataverseService.GetRecordAsync(
            "sprk_project",
            projectId,
            Arg.Any<string[]>(),
            Arg.Any<CancellationToken>())
            .Returns(projectRecord);

        _dataverseService.QueryRecordsAsync(
            "sprk_invoice",
            $"_sprk_project_value eq {projectId}",
            Arg.Any<string[]>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object>>());

        _dataverseService.UpdateRecordAsync(
            "sprk_project",
            projectId,
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        await _dataverseService.Received(1).GetRecordAsync("sprk_project", Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>());
        await _dataverseService.Received(1).UpdateRecordAsync("sprk_project", Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidEntityType_ReturnsError()
    {
        // Arrange
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entityType"] = "sprk_invoice",
            ["recordId"] = Guid.NewGuid(),
            ["operation"] = "recalculate"
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unsupported entity type");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidOperation_ReturnsError()
    {
        // Arrange
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entityType"] = "sprk_matter",
            ["recordId"] = Guid.NewGuid(),
            ["operation"] = "delete"
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unsupported operation");
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrencyConflict_RetriesWithExponentialBackoff()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entityType"] = "sprk_matter",
            ["recordId"] = matterId,
            ["operation"] = "recalculate"
        });

        var matterRecord = new Dictionary<string, object>
        {
            ["sprk_totalbudget"] = new Money { Value = 100000m },
            ["@odata.etag"] = "W/\"12345\""
        };

        _dataverseService.GetRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(matterRecord);

        _dataverseService.QueryRecordsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(new List<Dictionary<string, object>>());

        // Fail first 2 attempts with 412, succeed on 3rd
        _dataverseService.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                x => throw new DataverseException("Precondition Failed", 412),
                x => throw new DataverseException("Precondition Failed", 412),
                x => Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        await _dataverseService.Received(3).UpdateRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ZeroBudget_CalculatesUtilizationAsZero()
    {
        // Arrange
        var matterId = Guid.NewGuid();
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["entityType"] = "sprk_matter",
            ["recordId"] = matterId,
            ["operation"] = "recalculate"
        });

        var matterRecord = new Dictionary<string, object>
        {
            ["sprk_totalbudget"] = new Money { Value = 0m }, // Zero budget
            ["@odata.etag"] = "W/\"12345\""
        };

        var invoices = new List<Dictionary<string, object>>
        {
            new() { ["sprk_totalamount"] = new Money { Value = 15000m } }
        };

        _dataverseService.GetRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(matterRecord);

        _dataverseService.QueryRecordsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(invoices);

        _dataverseService.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var totals = result.Data as MatterFinancialTotals;
        totals!.BudgetUtilizationPercent.Should().Be(0m); // Avoid division by zero
    }
}
