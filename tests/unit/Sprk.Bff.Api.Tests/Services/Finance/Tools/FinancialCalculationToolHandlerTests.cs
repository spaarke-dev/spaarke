using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
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
    public void Constructor_NullDataverseService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new FinancialCalculationToolHandler(null!, _telemetry, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("dataverseService");
    }

    [Fact]
    public void Constructor_NullTelemetry_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new FinancialCalculationToolHandler(_dataverseService, null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new FinancialCalculationToolHandler(_dataverseService, _telemetry, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
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
}
