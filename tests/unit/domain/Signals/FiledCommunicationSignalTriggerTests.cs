using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology D-48 / issue #1659 (owner decision D-125): a communication classified first and filed to a matter later reaches the
/// association-confirmed Signal trigger. This is the shared check both filing paths call (the re-file route and the Office
/// reconcile): enqueue exactly once when the communication is classified AND on a matter, never otherwise, and never throw.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). The route and Office-reconcile call sites have their own tests.</remarks>
public class FiledCommunicationSignalTriggerTests
{
    private static readonly Guid CommunicationId = Guid.Parse("a1659000-0000-4000-8000-0000000000c1");
    private static readonly Guid MatterId = Guid.Parse("a1659000-0000-4000-8000-0000000000a1");

    [Fact]
    public async Task ClassifiedAndOnAMatter_EnqueuesTheAssociationConfirmedTrigger_Once()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var entities = FilingSignalDoubles.Entities(FilingSignalDoubles.Communication(CommunicationId, MatterId, classified: true));

        var queued = await FiledCommunicationSignalTrigger.EnqueueIfClassifiedOnMatterAsync(
            entities.Object, trigger.Object, CommunicationId, NullLogger.Instance);

        queued.Should().BeTrue();
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, MatterId, CommunicationId);
    }

    [Theory]
    [InlineData(false, true)]   // classified but on no matter: the classification trigger already ran, nothing to evaluate
    [InlineData(true, false)]   // on a matter but unclassified: classifying it fires its own trigger, which then finds the matter
    [InlineData(false, false)]
    public async Task WithoutAMatterOrWithoutACategory_EnqueuesNothing(bool onAMatter, bool classified)
    {
        var trigger = FilingSignalDoubles.Trigger();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(CommunicationId, onAMatter ? MatterId : null, classified));

        var queued = await FiledCommunicationSignalTrigger.EnqueueIfClassifiedOnMatterAsync(
            entities.Object, trigger.Object, CommunicationId, NullLogger.Instance);

        queued.Should().BeFalse();
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task AnEmptyMatterOrCategoryReference_CountsAsNone()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var row = new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.Empty),
            ["sprk_triagecategory"] = new EntityReference("sprk_triagecategory", Guid.NewGuid()),
        };

        await FiledCommunicationSignalTrigger.EnqueueIfClassifiedOnMatterAsync(
            FilingSignalDoubles.Entities(row).Object, trigger.Object, CommunicationId, NullLogger.Instance);

        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task WhenTheRowCannotBeRead_LogsAWarning_EnqueuesNothing_AndNeverThrows()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse down"));
        var log = new List<(LogLevel Level, string Message)>();

        var queued = await FiledCommunicationSignalTrigger.EnqueueIfClassifiedOnMatterAsync(
            entities.Object, trigger.Object, CommunicationId, new ListLogger(log));

        queued.Should().BeFalse();
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        log.Should().ContainSingle(e => e.Level == LogLevel.Warning).Which.Message.Should().Contain("nightly pass");
    }

    [Fact]
    public async Task WhenTheEnqueueReportsFailure_ReturnsFalse_AndNeverThrows()
    {
        var trigger = FilingSignalDoubles.Trigger(enqueueResult: false);
        var entities = FilingSignalDoubles.Entities(FilingSignalDoubles.Communication(CommunicationId, MatterId, classified: true));

        var queued = await FiledCommunicationSignalTrigger.EnqueueIfClassifiedOnMatterAsync(
            entities.Object, trigger.Object, CommunicationId, NullLogger.Instance);

        queued.Should().BeFalse();
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, MatterId, CommunicationId);
    }

    [Fact]
    public async Task WithNoTriggerRegistered_DoesNothing_AndReadsNothing()
    {
        var entities = FilingSignalDoubles.Entities(FilingSignalDoubles.Communication(CommunicationId, MatterId, classified: true));

        var queued = await FiledCommunicationSignalTrigger.EnqueueIfClassifiedOnMatterAsync(
            entities.Object, signalTrigger: null, CommunicationId, NullLogger.Instance);

        queued.Should().BeFalse();
        entities.VerifyNoOtherCalls();
    }

    private sealed class ListLogger(List<(LogLevel Level, string Message)> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add((logLevel, formatter(state, exception)));
    }
}

/// <summary>Doubles shared by the three call-site tests of the filing trigger (helper, re-file route, Office reconcile).</summary>
internal static class FilingSignalDoubles
{
    /// <summary>A <see cref="SignalEvaluationTrigger"/> whose (virtual) enqueue is recorded; no Service Bus.</summary>
    public static Mock<SignalEvaluationTrigger> Trigger(bool enqueueResult = true)
    {
        var jobs = new Mock<JobSubmissionService>(
            MockBehavior.Loose, Options.Create(new ServiceBusOptions { QueueName = "test-jobs" }),
            NullLogger<JobSubmissionService>.Instance, new Mock<ServiceBusClient>().Object);
        var trigger = new Mock<SignalEvaluationTrigger>(jobs.Object, NullLogger<SignalEvaluationTrigger>.Instance);
        trigger.Setup(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(enqueueResult);
        return trigger;
    }

    public static Entity Communication(Guid id, Guid? matter, bool classified)
    {
        var row = new Entity("sprk_communication", id);
        if (matter is { } m) row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
        if (classified) row["sprk_triagecategory"] = new EntityReference("sprk_triagecategory", Guid.NewGuid());
        return row;
    }

    public static Mock<IGenericEntityService> Entities(Entity row)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_communication", row.Id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        return entities;
    }

    public static void VerifyEnqueuedOnce(Mock<SignalEvaluationTrigger> trigger, Guid matterId, Guid communicationId) =>
        trigger.Verify(t => t.EnqueueAsync(SignalTriggerKind.AssociationConfirmed, "sprk_matter", matterId, communicationId,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

    public static void VerifyNothingEnqueued(Mock<SignalEvaluationTrigger> trigger) =>
        trigger.Verify(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
}
