// Task 141 fix round 2 (K1): TaskActionCore keeps its "degraded success" contract (a fault building the regarding pair
// returns Guid.Empty, never a rethrow: a rethrow would 500 CommunicationCreateTaskApplyService and abort
// CommunicationRiActionService's outbox write), but a denied BFF identity and a programming fault must not look like a
// transient catalog hiccup: they log at Error on their own EventIds and bump their own counter.

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

[Trait("status", "task-141")]
public class TaskActionCoreFaultVisibilityTests
{
    private static readonly Guid MatterId = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<ICommunicationDataverseService> _recordTypes = new();
    private readonly CapturingLogger<TaskActionCoreFaultVisibilityTests> _logger = new();

    public TaskActionCoreFaultVisibilityTests()
    {
        _entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
    }

    private async Task<(Guid Id, List<KeyValuePair<string, object?>[]> Measurements)> CreateWithCatalogFaultAsync(Exception fault)
    {
        _recordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>())).ThrowsAsync(fault);

        var measurements = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.task.create_faults")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => measurements.Add(tags.ToArray()));
        listener.Start();

        var core = new TaskActionCore(
            _entities.Object,
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            IdentityNormalizationFixtures.NoLinkedContact(),
            _recordTypes.Object,
            _logger);

        var id = await core.CreateAsync(
            new TaskActionInput("Follow up", null, null, MatterId, "sprk_matter", null), CancellationToken.None);
        return (id, measurements);
    }

    private static bool HasReason(List<KeyValuePair<string, object?>[]> measurements, string reason) =>
        measurements.Any(m => m.Any(t => t.Key == "reason" && Equals(t.Value, reason)));

    [Fact]
    public async Task AccessDenied_ReturnsEmpty_LogsErrorOnItsOwnEventId_AndCountsIt()
    {
        var (id, measurements) = await CreateWithCatalogFaultAsync(
            new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220), Message = "SecLib::AccessCheckEx failed" }));

        id.Should().Be(Guid.Empty);
        _entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.EventId == OntologyWriterEvents.TaskRegardingAccessDenied);
        _logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning && e.Message.Contains("could not be built"));
        HasReason(measurements, TaskCreateFaultReason.AccessDenied).Should().BeTrue();
    }

    [Fact]
    public async Task ArgumentException_ReturnsEmpty_LogsErrorOnItsOwnEventId_AndCountsIt()
    {
        var (id, measurements) = await CreateWithCatalogFaultAsync(new ArgumentException("bad entity name"));

        id.Should().Be(Guid.Empty);
        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.EventId == OntologyWriterEvents.TaskRegardingArgumentFault);
        HasReason(measurements, TaskCreateFaultReason.ArgumentFault).Should().BeTrue();
    }

    [Fact]
    public async Task OtherCatalogFault_StaysAWarning_AndIsNotCounted()
    {
        var (id, measurements) = await CreateWithCatalogFaultAsync(new TimeoutException("catalog read timed out"));

        id.Should().Be(Guid.Empty);
        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("could not be built"));
        _logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
        measurements.Should().BeEmpty();
    }
}
