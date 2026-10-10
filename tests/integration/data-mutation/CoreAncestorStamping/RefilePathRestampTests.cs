using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// unified-access-control-r2 task 156 — EVERY BFF path in the inventory (<c>notes/task-156-stamp-freshness.md</c>) that
/// can change a record's root, or what a record is filed under, re-stamps the dependent copies in the SAME operation:
/// asserted on the child PATCHes, through each path's own entry point, over the real <see cref="CoreAncestorRestamper"/>.
/// (The document PUT is driven through its real mapped route in <see cref="DocumentRefileRestampRouteTests"/>.)
/// The POST associate-record path left the inventory with its route (task 164, owner round 10 item 1); the document PUT is
/// now the only HTTP document re-file.
/// </summary>
public class RefilePathRestampTests
{
    private static readonly Guid MatterA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid MatterB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid Document = Guid.Parse("15600000-0000-0000-0000-000000000d01");
    private static readonly Guid Communication = Guid.Parse("15600000-0000-0000-0000-000000000c01");
    private static readonly Guid Event = Guid.Parse("15600000-0000-0000-0000-000000000e01");
    private static readonly Guid TodoUnderDocument = Guid.Parse("15600000-0000-0000-0000-000000000107");
    private static readonly Guid TodoUnderCommunication = Guid.Parse("15600000-0000-0000-0000-000000000101");
    private static readonly Guid TodoUnderEvent = Guid.Parse("15600000-0000-0000-0000-000000000104");
    private static readonly Guid CarrierTodo = Guid.Parse("15600000-0000-0000-0000-000000000105");

    [Fact(DisplayName = "Task 156 path: an UpdateRecord playbook node / ActionSeam writing a communication's matter re-stamps the to-do under it")]
    public async Task UpdateRecordActionCore_RestampsTheCommunicationsChildren()
    {
        var world = CommunicationWorld();
        // Batch 4 integration (task 146): filing a communication is a reparent — its owner is resolved first (a double
        // that applies the write), then this re-stamp runs.
        var services = new ServiceCollection()
            .AddSingleton(world.Restamper)
            .AddSingleton<IRecordOwnershipResolver>(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble())
            .BuildServiceProvider();
        var core = new UpdateRecordActionCore(
            new Mock<IFieldMappingDataverseService>().Object,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance);

        await core.UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup("sprk_regardingmatter", "sprk_matter", MatterB.ToString())]),
            CancellationToken.None);

        world.Lookup("sprk_todo", TodoUnderCommunication, "sprk_regardingmatter").Should().Be(MatterB,
            "the payload key is the Web API bind sprk_regardingmatter@odata.bind — normalized to the column");
    }

    [Fact(DisplayName = "Task 156 path: an UpdateRecord write that cannot move a stamp resolves nothing (no restamper, no reads)")]
    public async Task UpdateRecordActionCore_WriteThatCannotMoveAStamp_ResolvesNothing()
    {
        var world = CommunicationWorld();
        var core = new UpdateRecordActionCore(
            new Mock<IFieldMappingDataverseService>().Object,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), // no restamper at all
            NullLogger.Instance);

        var act = async () => await core.UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_triagesummary"] = "x" }, Lookups: null),
            CancellationToken.None);

        await act.Should().NotThrowAsync("a write that cannot move a stamp never even asks for the restamper");
        world.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "Task 156 path: the playbook output orchestrator's DataverseUpdateHandler re-stamps the children of the record it re-filed")]
    public async Task DataverseUpdateHandler_RestampsTheCommunicationsChildren()
    {
        var world = CommunicationWorld();
        var handler = new DataverseUpdateHandler(
            new Mock<IFieldMappingDataverseService>().Object, world.Service, world.Restamper,
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance);

        await handler.UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = MatterB }, ConcurrencyMode.None, maxRetries: 1,
            CancellationToken.None);

        world.Lookup("sprk_todo", TodoUnderCommunication, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156 path: a field-mapping PUSH that writes an event's matter re-stamps the to-do under the event")]
    public async Task FieldMappingPush_RestampsTheChildsChildren()
    {
        var world = new StampWorld()
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", TodoUnderEvent,
                [("sprk_regardingevent", "sprk_event", Event), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Event.ToString());
        var fieldMapping = new Mock<IFieldMappingDataverseService>().Object;

        var (updated, failed, _, _, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            fieldMapping,
            world.Restamper,
            [
                new FieldMappingRuleDto
                {
                    SourceField = "sprk_regardingmatter", TargetField = "sprk_regardingmatter",
                    SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
                },
            ],
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = MatterB },
            "sprk_event",
            [Event],
            // Task 166 (S-67): the push writes each child AS the caller (MSCRMCallerID).
            Guid.Parse("0000c166-0000-0000-0000-00000000ca11"),
            NullLogger.Instance,
            CancellationToken.None);

        updated.Should().Be(1);
        failed.Should().Be(0);
        world.Lookup("sprk_todo", TodoUnderEvent, "sprk_regardingmatter").Should().Be(MatterB);
    }

    [Fact(DisplayName = "Task 156 path: a cascade failure never fails the field-mapping push — the event's own update is counted, the to-do is left for the job")]
    public async Task FieldMappingPush_CascadeFailure_DoesNotFailThePush()
    {
        var world = new StampWorld()
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", MatterB)])
            .Row("sprk_todo", TodoUnderEvent,
                [("sprk_regardingevent", "sprk_event", Event), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Event.ToString())
            .FailWrite("sprk_todo", TodoUnderEvent);

        var (updated, failed, _, _, _) = await FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync(
            new Mock<IFieldMappingDataverseService>().Object,
            world.Restamper,
            [
                new FieldMappingRuleDto
                {
                    SourceField = "sprk_regardingmatter", TargetField = "sprk_regardingmatter",
                    SourceFieldType = "Lookup", TargetFieldType = "Lookup", Priority = 1,
                },
            ],
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = MatterB },
            "sprk_event",
            [Event],
            // Task 166 (S-67): the push writes each child AS the caller (MSCRMCallerID).
            Guid.Parse("0000c166-0000-0000-0000-00000000ca11"),
            NullLogger.Instance,
            CancellationToken.None);

        updated.Should().Be(1, "the event's own update stands");
        failed.Should().Be(0);
        world.Lookup("sprk_todo", TodoUnderEvent, "sprk_regardingmatter").Should().Be(MatterA);
    }

    /// <summary>A document now filed under matter B, a to-do under it still copying A, and an Office carrier to-do.</summary>
    internal static StampWorld DocumentWorld() => new StampWorld()
        .Row("sprk_document", Document, [("sprk_matter", "sprk_matter", MatterB)])
        .Row("sprk_todo", TodoUnderDocument,
            [("sprk_regardingdocument", "sprk_document", Document), ("sprk_regardingmatter", "sprk_matter", MatterA)],
            pairId: Document.ToString())
        .Row("sprk_todo", CarrierTodo,
            [("sprk_regardingdocument", "sprk_document", Document), ("sprk_regardingmatter", "sprk_matter", MatterA)],
            pairId: MatterA.ToString());

    /// <summary>A communication now filed under matter B and a to-do under it still copying A.</summary>
    private static StampWorld CommunicationWorld() => new StampWorld()
        .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterB)])
        .Row("sprk_todo", TodoUnderCommunication,
            [("sprk_regardingcommunication", "sprk_communication", Communication), ("sprk_regardingmatter", "sprk_matter", MatterA)],
            pairId: Communication.ToString());
}
