using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// spaarkeai-mcp-bff-api-r2 task 033 review F4 (NFR-05, ADR-015): the finance summary service, shared by the REST route and the MCP tool,
/// logs ids, counts and outcomes only. A spend amount is never in a log line (a template placeholder, a rendered message or a structured
/// value). Drives <see cref="FinanceSummaryService.GetSummaryAsync"/> on a cache miss, with the Dataverse reads replaced by one seam.
/// </summary>
public class FinanceSummaryLogTests
{
    private const decimal Spend = 12345.67m;
    private const decimal Budget = 98765.43m;
    private const decimal Variance = 86419.76m;

    [Fact]
    public async Task OnACacheMiss_NoLogLine_CarriesASpendBudgetOrVarianceAmount()
    {
        var logger = new CapturingLogger();
        var service = NewService(logger);
        var matterId = Guid.NewGuid();

        var summary = await service.GetSummaryAsync(matterId);

        summary.Should().NotBeNull();
        summary!.CurrentSpend.Should().Be(Spend, "the response is unchanged: the amount is in the body, only the log drops it");
        logger.Lines.Should().NotBeEmpty("the miss path logs its outcome");
        logger.Lines.Should().Contain(l => l.Contains(matterId.ToString()) && l.Contains("ActiveSignals=1") && l.Contains("RecentInvoices=0"),
            "the line keeps the id and the counts");
        foreach (var amount in new[] { Spend, Budget, Variance })
        {
            // The amount as a decimal, as a number with and without separators, and as currency in the culture-neutral and en-US forms.
            var forms = new[] { amount.ToString(System.Globalization.CultureInfo.InvariantCulture), amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture),
                amount.ToString("C", System.Globalization.CultureInfo.InvariantCulture), amount.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("en-US")) };
            foreach (var form in forms)
            {
                logger.Lines.Should().NotContain(l => l.Contains(form), $"NFR-05: no log line carries the amount ('{form}')");
            }
        }

        logger.Lines.Should().NotContain(l => l.Contains("CurrentSpend", StringComparison.OrdinalIgnoreCase), "nor a CurrentSpend field at all");
    }

    private static FinanceSummaryService NewService(ILogger<FinanceSummaryService> logger)
    {
        var cache = new Mock<ITenantCache>();
        cache.Setup(c => c.GetAsync<FinanceSummaryDto>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinanceSummaryDto?)null);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tid", "11111111-2222-4333-8444-555555555555") }, "Test")) };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(http);
        return new SeededService(new Mock<IDataverseService>().Object, cache.Object, accessor.Object, Options.Create(new FinanceOptions()), logger);
    }

    /// <summary>The service with its one Dataverse seam answering: a snapshot with amounts, one signal and no invoices.</summary>
    private sealed class SeededService(
        IDataverseService dataverse, ITenantCache cache, IHttpContextAccessor accessor, IOptions<FinanceOptions> options, ILogger<FinanceSummaryService> logger)
        : FinanceSummaryService(dataverse, cache, accessor, options, logger)
    {
        internal override Task<(SpendSnapshotData? Snapshot, List<SpendSignalDto> Signals, List<RecentInvoiceDto> RecentInvoices)> ReadFinanceDataAsync(
            Guid matterId, CancellationToken ct) =>
            Task.FromResult<(SpendSnapshotData?, List<SpendSignalDto>, List<RecentInvoiceDto>)>((
                new SpendSnapshotData { InvoicedAmount = Spend, BudgetAmount = Budget, BudgetVariance = Variance, VelocityPct = 3m },
                [new SpendSignalDto { SignalType = "BudgetWarning", Severity = "Warning", Message = "m" }],
                []));
    }

    /// <summary>Records every line as its rendered message plus each structured value, so an amount is caught however it is logged.</summary>
    private sealed class CapturingLogger : ILogger<FinanceSummaryService>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(" ", pairs.Select(p => $"{p.Key}={Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture)}"))
                : string.Empty;
            Lines.Add(formatter(state, exception) + " | " + values);
        }
    }
}
