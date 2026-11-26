using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Tools;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Tools;

/// <summary>
/// Unit tests for FinancialCalculationToolHandler: the public contract layer (parameter validation). Task 165b-4: the totals come from the
/// shared counted reader (<see cref="FinanceBaseValues.ReadCountedInvoicesAsync"/>) through the app-only <see cref="IGenericEntityService"/>
/// (no <c>ServiceClient</c> unwrap any more), so the "totals compute" path is tested offline with the other spend readers in
/// <see cref="SpendServerWrittenSourcesTests"/> (S1-S5, key, D-211, faults, O6 parity). The earlier task 021 fail-loud unwrap tests are retired
/// with the unwrap.
/// </summary>
public class FinancialCalculationToolHandlerTests
{
    private readonly FinancialCalculationToolHandler _handler;

    public FinancialCalculationToolHandlerTests()
    {
        var recorder = new BillingDecisionRecorder(new DecisionRecordWriterTests.Harness().Writer,
            new Mock<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>().Object, NullLogger<BillingDecisionRecorder>.Instance);
        _handler = new FinancialCalculationToolHandler(new Mock<IDataverseService>().Object, recorder, new Mock<FinanceTelemetry>().Object,
            new Mock<ILogger<FinancialCalculationToolHandler>>().Object);
    }

    [Fact]
    public void ToolName_ShouldReturnFinancialCalculation()
    {
        _handler.ToolName.Should().Be("FinancialCalculation");
    }

    [Fact]
    public async Task ExecuteAsync_MissingBothMatterIdAndProjectId_ReturnsError()
    {
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["operation"] = "recalculate"
        });

        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("matterId");
        result.Error.Should().Contain("projectId");
    }

    [Fact]
    public async Task ExecuteAsync_BothMatterIdAndProjectIdProvided_ReturnsError()
    {
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["matterId"] = Guid.NewGuid(),
            ["projectId"] = Guid.NewGuid()
        });

        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Cannot specify both");
    }
}
