using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Tools;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Tools;

/// <summary>
/// Unit tests for FinancialCalculationToolHandler.
///
/// NOTE: The handler's CalculateMatterFinancialTotalsAsync / CalculateProjectFinancialTotalsAsync
/// methods unwrap the underlying ServiceClient from IDataverseService (via the concrete
/// DataverseServiceClientImpl.OrganizationService) and call RetrieveMultiple — a Dataverse SDK
/// operation that is not mockable through IDataverseService, and DataverseServiceClientImpl itself
/// requires a live Dataverse connection to construct. Tests covering the positive "totals compute"
/// path therefore require the live-tenant integration harness.
///
/// These unit tests cover the handler's public contract layer: parameter validation, basic
/// constructor validation, and the Bug-1 fail-loud resolution contract (task 021) — see the
/// regression tests below.
/// </summary>
public class FinancialCalculationToolHandlerTests
{
    private readonly IDataverseService _dataverseService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<FinancialCalculationToolHandler> _logger;
    private readonly FinancialCalculationToolHandler _handler;

    public FinancialCalculationToolHandlerTests()
    {
        _dataverseService = new Mock<IDataverseService>().Object;
        _telemetry = new Mock<FinanceTelemetry>().Object;
        _logger = new Mock<ILogger<FinancialCalculationToolHandler>>().Object;
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
    public async Task ExecuteAsync_MissingBothMatterIdAndProjectId_ReturnsError()
    {
        // Arrange — neither matterId nor projectId provided
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["operation"] = "recalculate"
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("matterId");
        result.Error.Should().Contain("projectId");
    }

    [Fact]
    public async Task ExecuteAsync_BothMatterIdAndProjectIdProvided_ReturnsError()
    {
        // Arrange — both matterId and projectId provided (mutually exclusive)
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["matterId"] = Guid.NewGuid(),
            ["projectId"] = Guid.NewGuid()
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Cannot specify both");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Bug-1 regression (code-quality-and-assurance-r3 task 021, spec FR-07).
    //
    // The invoice-totals resolver previously did `_dataverseService as ServiceClient`, which ALWAYS
    // returned null: the sole registered impl (DataverseServiceClientImpl) WRAPS a ServiceClient
    // (exposed via OrganizationService) rather than deriving from it, so the live invoice-totals path
    // threw InvalidOperationException 100% of the time. The fix unwraps via
    // `is DataverseServiceClientImpl impl => impl.OrganizationService` and FAILS LOUD with the actual
    // runtime type when the backing service is not the concrete impl.
    //
    // These tests exercise the matter and project totals paths
    // (ExecuteAsync → Calculate*FinancialTotalsAsync → GetServiceClient) with a service that is NOT the
    // concrete impl, and assert the corrected fail-loud contract. The "(actual: …)" runtime-type
    // context is emitted ONLY by the fixed resolver — the pre-fix message ("…resolved as
    // ServiceClient…") did not carry it — so each assertion below FAILS against the old always-throwing
    // cast, and passes against the fix.
    //
    // The positive "totals compute" path cannot be unit-tested offline (sealed ServiceClient +
    // connection-required DataverseServiceClientImpl); it is covered by the live-tenant integration
    // harness. Task 028's shared UnwrapServiceClient extension makes it unit-testable.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MatterId_WhenServiceIsNotConcreteImpl_FailsLoudWithRuntimeContext()
    {
        // Arrange — the injected IDataverseService is not DataverseServiceClientImpl.
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["matterId"] = Guid.NewGuid()
        });

        // Act — the resolver throws InvalidOperationException; ExecuteAsync surfaces it as an error result.
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert — corrected fail-loud contract (never a silent wrong result).
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Financial calculation failed");
        // The fixed resolver names the required backing type AND the actual runtime type. The
        // "(actual: …)" clause is absent from the pre-fix "resolved as ServiceClient" message, so these
        // three assertions collectively fail against the old always-throwing cast.
        result.Error.Should().Contain("DataverseServiceClientImpl");
        result.Error.Should().Contain("actual:");
        result.Error.Should().NotContain("resolved as ServiceClient");
    }

    [Fact]
    public async Task ExecuteAsync_ProjectId_WhenServiceIsNotConcreteImpl_FailsLoudWithRuntimeContext()
    {
        // Arrange — project path; same non-impl service.
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["projectId"] = Guid.NewGuid()
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Financial calculation failed");
        result.Error.Should().Contain("DataverseServiceClientImpl");
        result.Error.Should().Contain("actual:");
        result.Error.Should().NotContain("resolved as ServiceClient");
    }

    // ebilling-r1 task 015 (D-41, D-103; #1771): the invoice query filters statuscode on the counted set, reads the real amount
    // column, and is the rollup's own query. It once filtered the retired status column on three values, two of which never
    // existed, so Approved / Sent to AP / Paid invoices were never totalled.
    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    public void BuildInvoiceQuery_FiltersStatuscodeOnTheCountedSet_AndReadsTheTotalAmount(string lookup)
    {
        var parentId = Guid.NewGuid();

        var query = FinancialCalculationToolHandler.BuildInvoiceQuery(lookup, parentId);

        query.EntityName.Should().Be("sprk_invoice");
        // D-130: the tool's totals follow the rollup's amount rule (approved total, else billed), so both columns are read.
        query.ColumnSet.Columns.Should().Contain("sprk_totalamount").And.Contain("sprk_approvedtotal").And.Contain("statuscode");
        query.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == lookup && c.Operator == ConditionOperator.Equal)
            .Which.Values.Should().Equal(parentId);
        var status = query.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "statuscode").Subject;
        status.Operator.Should().Be(ConditionOperator.In);
        status.Values.Cast<int>().Should().Equal(FinanceBaseValues.CountedInvoiceStatuses);
        query.Criteria.Conditions.Should().HaveCount(2, "no other filter (in particular no statecode: Paid is Inactive)");
    }

    [Theory]
    [InlineData(InvoiceStatusCodes.Paid, true)]
    [InlineData(InvoiceStatusCodes.Approved, true)]
    [InlineData(InvoiceStatusCodes.Rejected, false)]
    [InlineData(InvoiceStatusCodes.Cancelled, false)]
    public void BuildInvoiceQuery_AdmitsARealStatuscode_OnlyWhenItIsCounted(int statusCode, bool admitted) =>
        FinancialCalculationToolHandler.BuildInvoiceQuery("sprk_matter", Guid.NewGuid()).Criteria.Conditions
            .Single(c => c.AttributeName == "statuscode").Values.Cast<int>().Contains(statusCode).Should().Be(admitted);
}
