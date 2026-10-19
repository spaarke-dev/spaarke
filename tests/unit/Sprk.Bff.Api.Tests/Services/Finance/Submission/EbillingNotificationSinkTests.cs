using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// <see cref="IEbillingNotificationSink"/> and its default <see cref="NoOpEbillingNotificationSink"/> (ebilling-r1 task 049 part A,
/// Processing Pipeline step 9; task 132 replaces the no-op).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the processor calls the seam unconditionally after every decision, so the registration must hold with every
/// flag off (a missing registration would fail the whole pipeline at resolve time), and the default must never throw or log extracted data.
/// </remarks>
public sealed class EbillingNotificationSinkTests
{
    private static SubmissionDecidedNotification Accepted() => new(
        Guid.NewGuid(), SubmissionDecisionOutcome.Accepted, InvoiceSubmissionChannel.Spa, InvoiceId: Guid.NewGuid(), ReasonCode: null);

    private static SubmissionDecidedNotification RejectedWithoutInvoice() => new(
        Guid.NewGuid(), SubmissionDecisionOutcome.Rejected, InvoiceSubmissionChannel.Internal, InvoiceId: null, ReasonCode: "ebilling.submission.unreadable");

    // ADR-038 B3 note: a gating-contract test, not a bare "resolves" test: it fails if the registration is wrapped in a feature flag
    // (the 014 / flag-rule precedent in InvoiceLifecycleServiceTests), because the processor calls the seam on every decision.
    [Fact]
    public void Sink_IsNotBehindAnyFeatureGate_AndDefaultsToTheNoOp()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinanceModule(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEbillingNotificationSink>().Should().BeOfType<NoOpEbillingNotificationSink>();
    }

    [Fact]
    public async Task NoOp_CompletesForAcceptedAndRejected_WithoutThrowing()
    {
        var sink = new NoOpEbillingNotificationSink(Mock.Of<ILogger<NoOpEbillingNotificationSink>>());

        await sink.SubmissionDecidedAsync(Accepted());
        await sink.SubmissionDecidedAsync(RejectedWithoutInvoice());
    }

    [Fact]
    public async Task NoOp_RefusesANullDecision_RatherThanSwallowingAProgrammingError()
    {
        var sink = new NoOpEbillingNotificationSink(Mock.Of<ILogger<NoOpEbillingNotificationSink>>());

        var act = async () => await sink.SubmissionDecidedAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task NoOp_LogsTheSubmissionIdAndOutcome_AndNeverTheReasonCodeOrInvoice()
    {
        var logger = new Mock<ILogger<NoOpEbillingNotificationSink>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var sink = new NoOpEbillingNotificationSink(logger.Object);
        var decision = RejectedWithoutInvoice();

        await sink.SubmissionDecidedAsync(decision);

        var logged = logger.Invocations
            .Where(i => i.Method.Name == nameof(ILogger.Log))
            .Select(i => i.Arguments[2]?.ToString() ?? string.Empty)
            .ToList();
        logged.Should().ContainSingle();
        logged[0].Should().Contain(decision.SubmissionId.ToString()).And.Contain("Rejected");
        logged[0].Should().NotContain(decision.ReasonCode!, "ADR-015: identifiers and the outcome only");
    }
}
