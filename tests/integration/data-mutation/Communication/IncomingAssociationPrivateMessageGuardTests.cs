using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056: a private message is never filed. The inbound association write
/// (<see cref="IncomingAssociationResolver"/>) takes the plain write when the decision sets no ownership parent (a contact regarding,
/// <c>sprk_regardingperson</c>) or the row's owner is already resolved, so the reparent rule never sees it; the plain write asks
/// <see cref="PrivateMessageWriteGuard.RefuseUpdateAsync"/> first. A private row is refused (the resolver's own fail-closed contract: a
/// <see cref="RecordOwnerUnresolvedException"/>, nothing written, a Warning logged); an ordinary row is written as before; a guard
/// read that faults writes nothing.
/// </summary>
public sealed class IncomingAssociationPrivateMessageGuardTests
{
    private const string Communication = "sprk_communication";
    private static readonly Guid Row = Guid.Parse("05600000-0000-4000-8000-0000000000d1");
    private static readonly Guid Contact = Guid.Parse("05600000-0000-4000-8000-0000000000d2");
    private static readonly Guid Matter = Guid.Parse("05600000-0000-4000-8000-0000000000d3");

    private readonly Mock<IDataverseService> _dataverse = new();
    private readonly List<Dictionary<string, object>> _updates = new();
    private readonly ListLogger _log = new();
    private Entity _row = new(Communication, Row);
    private Exception? _guardReadFault;

    public IncomingAssociationPrivateMessageGuardTests()
    {
        _dataverse.Setup(d => d.GetColumnMetadataAsync(Communication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivateContextColumns.All.Append("sprk_communicationid")
                .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase));
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (_guardReadFault is not null)
                    throw _guardReadFault;
                return Task.FromResult(new EntityCollection(new List<Entity> { _row }));
            });
        _dataverse.Setup(d => d.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid __, Dictionary<string, object> fields, CancellationToken ___) => _updates.Add(new Dictionary<string, object>(fields)))
            .Returns(Task.CompletedTask);
        _dataverse.Setup(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, Guid id, string[] _, CancellationToken __) => new Entity(name, id));
    }

    private IncomingAssociationResolver Resolver()
    {
        var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
        var monitor = Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options);
        return new IncomingAssociationResolver(
            Array.Empty<IAssociationRung>(), _dataverse.Object, _dataverse.Object,
            new AssociationStatusMapper(new AutoFileGate(monitor), NullLogger<AssociationStatusMapper>.Instance),
            CoreAncestorResolverFixtures.Inert(), new RecordOwnershipResolverDouble(), _log);
    }

    /// <summary>A decision whose only regarding is a contact: no ownership parent, so the write is the plain one.</summary>
    private static AssociationDecision ContactOnly() => new()
    {
        Status = AssociationStatusCodes.Suggested,
        RegardingWrites = new Dictionary<string, EntityReference> { ["sprk_regardingperson"] = new("contact", Contact) },
        AutoFiled = false,
        Provenance = new AssociationProvenance
        {
            Direction = "Incoming",
            Decision = new AssociationDecisionTrace
            {
                Status = "Suggested",
                AutoFiled = false,
                KillSwitchEnabled = true,
                AutoFileThreshold = 0.85,
                TopDeterministicConfidence = 1,
                TopConfidence = 1,
                AiInvolved = false,
                Reason = "test",
            },
            RungsFired = Array.Empty<string>(),
            Candidates = Array.Empty<CandidateTrace>(),
            Signals = Array.Empty<SignalTrace>(),
        },
    };

    [Fact]
    public async Task ApplyToNewRecord_APrivateRowWithOnlyAContactRegarding_IsRefused_AndNothingIsWritten()
    {
        _row = new Entity(Communication, Row) { [PrivateContextColumns.Matter] = new EntityReference("sprk_matter", Matter) };

        var act = () => Resolver().ApplyToNewRecordAsync(Row, ContactOnly(), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<RecordOwnerUnresolvedException>();
        thrown.Which.Message.Should().Contain(RecordOwnerRefusal.PrivateMessageNotFileable);
        _updates.Should().BeEmpty("a private message is never filed");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains(Row.ToString()));
    }

    [Fact]
    public async Task ApplyToNewRecord_AnOrdinaryRow_IsWritten_AsBefore()
    {
        await Resolver().ApplyToNewRecordAsync(Row, ContactOnly(), CancellationToken.None);

        _updates.Should().ContainSingle().Which.Should().ContainKey("sprk_regardingperson");
    }

    [Fact]
    public async Task ApplyToNewRecord_AGuardReadThatFaults_WritesNothing()
    {
        _guardReadFault = new InvalidOperationException("Test: Dataverse unavailable.");

        var act = () => Resolver().ApplyToNewRecordAsync(Row, ContactOnly(), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<RecordOwnerUnresolvedException>();
        thrown.Which.Message.Should().Contain(PrivateMessageWriteGuard.CheckUnavailable);
        _updates.Should().BeEmpty();
    }

    private sealed class ListLogger : ILogger<IncomingAssociationResolver>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
