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
/// methods cast IDataverseService to ServiceClient (Microsoft.Xrm.Sdk) internally and call
/// RetrieveMultiple — a Dataverse SDK operation that is not mockable via IDataverseService.
/// Tests covering those paths require an integration test harness (WireMock + real SDK client).
///
/// These unit tests cover the handler's public contract layer: parameter validation,
/// unsupported entity/operation detection, and basic constructor validation.
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

    [Fact]
    public async Task ExecuteAsync_MatterIdProvided_ServiceClientCastFails_ReturnsError()
    {
        // Arrange — IDataverseService mock is NOT a ServiceClient, so the internal cast will fail.
        // This is an expected failure mode when the handler is invoked in a test context
        // without a real Dataverse ServiceClient wired up.
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["matterId"] = Guid.NewGuid()
        });

        // Act — Should gracefully catch the InvalidOperationException from the cast
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Financial calculation failed");
    }

    [Fact]
    public async Task ExecuteAsync_ProjectIdProvided_ServiceClientCastFails_ReturnsError()
    {
        // Arrange — same as above but for project path
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["projectId"] = Guid.NewGuid()
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Financial calculation failed");
    }
}
