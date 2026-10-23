using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// Ontology D-48 / issue #1659 (owner decision D-125): <c>PATCH /api/communications/{id}/filing</c> queues the association-
/// confirmed Signal trigger after a SUCCESSFUL re-file of a communication that is classified and on a matter, and only then.
/// The shared re-file core (<see cref="ChildRecordEndpoints.UpdateAsync"/>, also the events and generic child-record routes) is
/// untouched; the trigger is in the communication route's own handler, driven here over the same harness as the re-file tests.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid FiledCommunication = Guid.Parse("a1659000-0000-4000-8000-0000000000c2");

    /// <summary>The communication route's REAL handler, with the trigger and the app-only read it adds.</summary>
    private Task<IResult> RefileCommunicationWithTrigger(
        Dictionary<string, object?> payload, IGenericEntityService entities, SignalEvaluationTrigger? trigger) =>
        CommunicationEndpoints.RefileCommunicationAsync(
            FiledCommunication, Payload(payload), _user, _world.Resolver(), Restamper(), Children(), entities, trigger,
            HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);

    private static Dictionary<string, object?> FileUnderOrdinaryMatter() =>
        new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})" };

    private void UnfiledCommunicationExists() =>
        _world.WithRecord("sprk_communication", FiledCommunication, Directory.ChildBu, owningTeam: Directory.ChildTeam);

    private static readonly Guid PreviousMatter = Guid.Parse("a1659000-0000-4000-8000-0000000000a3");

    /// <summary>The app-only reads of the filing route: the matter BEFORE the write, then the row AFTER it.</summary>
    private static Mock<IGenericEntityService> ReadsBeforeAndAfter(Guid? matterBefore, Guid? matterAfter, bool classified = true) =>
        FilingSignalDoubles.EntitiesBeforeAndAfter(
            FilingSignalDoubles.Communication(FiledCommunication, matterBefore, classified),
            FilingSignalDoubles.Communication(FiledCommunication, matterAfter, classified));

    private static void VerifyOnlyTheBeforeRead(Mock<IGenericEntityService> entities) =>
        entities.Verify(e => e.RetrieveAsync("sprk_communication", FiledCommunication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once, "a refused or failed write reads the matter only before it, and never after");

    [Fact]
    public async Task CommunicationFiling_ClassifiedAndFiledToAMatter_QueuesTheAssociationConfirmedTrigger_Once_AndStillAnswers204()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: null, matterAfter: OrdinaryMatter);

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, OrdinaryMatter, FiledCommunication);
        trigger.Verify(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once, "exactly one enqueue per re-file");
    }

    [Fact]
    public async Task CommunicationFiling_NotYetClassified_QueuesNothing_ClassificationFiresItsOwnTrigger()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: null, matterAfter: OrdinaryMatter, classified: false);

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task CommunicationFiling_ClassifiedAndUnfiledFromAMatter_ReEvaluatesThatMatter_AndNoOther()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: OrdinaryMatter, matterAfter: null);

        var result = await RefileCommunicationWithTrigger(
            new() { ["sprk_RegardingMatter@odata.bind"] = null }, entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, OrdinaryMatter, FiledCommunication);
        trigger.Verify(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommunicationFiling_AMoveFromAToB_ReEvaluatesBothMatters()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: PreviousMatter, matterAfter: OrdinaryMatter);

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, OrdinaryMatter, FiledCommunication);
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, PreviousMatter, FiledCommunication);
    }

    [Fact]
    public async Task CommunicationFiling_APatchThatChangesNoFiling_QueuesNothing()
    {
        // The same matter re-sent: the main queue has no duplicate detection, so each enqueue would be a full evaluation.
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: OrdinaryMatter, matterAfter: OrdinaryMatter);

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task CommunicationFiling_ARefusedReFile_400_QueuesNothing_AndReadsOnlyBeforeTheWrite()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: PreviousMatter, matterAfter: OrdinaryMatter);

        var result = await RefileCommunicationWithTrigger(
            new() { ["sprk_RegardingMatter@odata.bind"] = null, ["sprk_associationstatus"] = null }, entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        VerifyOnlyTheBeforeRead(entities);
    }

    [Fact]
    public async Task CommunicationFiling_ARefusedReFile_UnderARecordTheCallerCannotAppendTo_QueuesNothing_AndReadsOnlyBeforeTheWrite()
    {
        UnfiledCommunicationExists();
        _user.NoAppendTo.Add(OrdinaryMatter);
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: PreviousMatter, matterAfter: OrdinaryMatter);

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status404NotFound);
        _user.Patches.Should().BeEmpty();
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        VerifyOnlyTheBeforeRead(entities);
    }

    [Fact]
    public async Task CommunicationFiling_ADeniedWrite_403_QueuesNothing()
    {
        UnfiledCommunicationExists();
        _user.PatchStatus = 403;
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: PreviousMatter, matterAfter: OrdinaryMatter);

        // A name-only update is the caller's own PATCH (not a re-file), which the scripted Dataverse refuses with 403.
        var result = await RefileCommunicationWithTrigger(
            new() { ["sprk_regardingrecordid"] = Guid.NewGuid().ToString("D") }, entities.Object, trigger.Object);

        Status(result).Should().NotBe(StatusCodes.Status204NoContent);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        VerifyOnlyTheBeforeRead(entities);
    }

    [Fact]
    public async Task CommunicationFiling_WhenTheEnqueueFails_StillAnswers204()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger(enqueueResult: false);
        var entities = ReadsBeforeAndAfter(matterBefore: null, matterAfter: OrdinaryMatter);

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, OrdinaryMatter, FiledCommunication);
    }

    [Fact]
    public async Task CommunicationFiling_WhenTheClassificationReadThrows_StillAnswers204_AndQueuesNothing()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_communication", FiledCommunication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse down"));

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task CommunicationFiling_WithNoTriggerRegistered_StillAnswers204()
    {
        UnfiledCommunicationExists();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: true));

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger: null);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        entities.VerifyNoOtherCalls();
    }

    // ── The chat update tool (`dataverse.update_record`) is the third server-side filing path (D-125) ───────────────

    /// <summary>The chat update tool over a scope that resolves the real reconciler plus the app-only entity read and the
    /// Signal trigger the handler resolves for a re-filed communication (the trigger is left unregistered when null).</summary>
    private Task<Sprk.Bff.Api.Services.Ai.ToolResult> UpdateRecordWithTrigger(
        string table, Guid id, IGenericEntityService entities, SignalEvaluationTrigger? trigger,
        params (string Column, JsonElement Value)[] item)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Children());
        services.AddSingleton(entities);
        if (trigger is not null)
            services.AddSingleton(trigger);
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler(
                _user,
                new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp,
                NullLogger<Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler>.Instance,
                _world.Resolver(),
                Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
                scopes)
            .ExecuteChatAsync(
                BuildChatInvocationContext(
                    toolArgumentsJson: JsonSerializer.Serialize(new
                    {
                        tablename = table,
                        recordId = id,
                        item = item.ToDictionary(i => i.Column, i => i.Value),
                    })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler)), CancellationToken.None);
    }

    private static (string Column, JsonElement Value) MatterLookup(Guid matter) => Lookup("sprk_regardingmatter", "sprk_matter", matter);

    private void FiledCommunicationInTheWorld()
    {
        UnfiledCommunicationExists();
        ShareWorld.OrdinaryChild("sprk_communication", FiledCommunication, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
    }

    [Fact]
    public async Task UpdateTool_ReFilingAClassifiedCommunicationToAMatter_QueuesTheAssociationConfirmedTrigger_Once_AndTheResultIsUnchanged()
    {
        FiledCommunicationInTheWorld();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: null, matterAfter: OrdinaryMatter);

        var result = await UpdateRecordWithTrigger(
            "sprk_communication", FiledCommunication, entities.Object, trigger.Object, MatterLookup(OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, OrdinaryMatter, FiledCommunication);
        trigger.Verify(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once, "exactly one enqueue per re-file");
    }

    [Fact]
    public async Task UpdateTool_AMoveOfAClassifiedCommunicationFromAToB_QueuesBothMatters()
    {
        FiledCommunicationInTheWorld();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: PreviousMatter, matterAfter: OrdinaryMatter);

        var result = await UpdateRecordWithTrigger(
            "sprk_communication", FiledCommunication, entities.Object, trigger.Object, MatterLookup(OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, OrdinaryMatter, FiledCommunication);
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, PreviousMatter, FiledCommunication);
    }

    [Fact]
    public async Task UpdateTool_AReFileThatLeavesTheMatterAsItWas_QueuesNothing()
    {
        FiledCommunicationInTheWorld();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: OrdinaryMatter, matterAfter: OrdinaryMatter);

        var result = await UpdateRecordWithTrigger(
            "sprk_communication", FiledCommunication, entities.Object, trigger.Object, MatterLookup(OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task UpdateTool_ReFilingAnUnclassifiedCommunication_QueuesNothing()
    {
        FiledCommunicationInTheWorld();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: null, matterAfter: OrdinaryMatter, classified: false);

        var result = await UpdateRecordWithTrigger(
            "sprk_communication", FiledCommunication, entities.Object, trigger.Object, MatterLookup(OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task UpdateTool_ReFilingAnotherTable_QueuesNothing_AndReadsNothing()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        var trigger = FilingSignalDoubles.Trigger();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: true));

        var result = await UpdateRecordWithTrigger("sprk_todo", Todo, entities.Object, trigger.Object, MatterLookup(OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateTool_ARefusedCommunicationReFile_QueuesNothing_AndReadsOnlyBeforeTheWrite()
    {
        UnfiledCommunicationExists();
        _user.NoAppendTo.Add(OrdinaryMatter);
        var trigger = FilingSignalDoubles.Trigger();
        var entities = ReadsBeforeAndAfter(matterBefore: PreviousMatter, matterAfter: OrdinaryMatter);

        var result = await UpdateRecordWithTrigger(
            "sprk_communication", FiledCommunication, entities.Object, trigger.Object, MatterLookup(OrdinaryMatter));

        result.Success.Should().BeFalse("the re-file was refused");
        _user.Patches.Should().BeEmpty();
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        VerifyOnlyTheBeforeRead(entities);
    }

    [Fact]
    public async Task UpdateTool_WhenTheClassificationReadThrows_TheReFileResultIsUnchanged_AndNothingIsQueued()
    {
        FiledCommunicationInTheWorld();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_communication", FiledCommunication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse down"));

        var result = await UpdateRecordWithTrigger(
            "sprk_communication", FiledCommunication, entities.Object, trigger.Object, MatterLookup(OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task UpdateTool_WithNoTriggerRegistered_TheReFileResultIsUnchanged()
    {
        FiledCommunicationInTheWorld();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: true));

        var result = await UpdateRecordWithTrigger(
            "sprk_communication", FiledCommunication, entities.Object, trigger: null, MatterLookup(OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    // ── Events and generic child records keep no Signal trigger ─────────────────────────────────────────────────

    /// <summary>A caller context whose request services carry a live trigger and a classified communication, so any
    /// shared-core code that resolved them from the request (it must not) would enqueue.</summary>
    private static HttpContext HttpContextWithSignalServices(IGenericEntityService entities, SignalEvaluationTrigger trigger)
    {
        var services = new ServiceCollection();
        services.AddSingleton(entities);
        services.AddSingleton(trigger);
        var context = HttpContextOfCaller();
        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    [Fact]
    public async Task EventAndGenericChildReFiles_QueueNothing_EvenWithAClassifiedCommunicationOnTheMatter()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_todo", Todo, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        var trigger = FilingSignalDoubles.Trigger();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: true));

        var eventResult = await Sprk.Bff.Api.Api.Events.EventEndpoints.RefileEventAsync(
            Event, Payload(FileUnderOrdinaryMatter()), _user, _world.Resolver(), Restamper(), Children(),
            HttpContextWithSignalServices(entities.Object, trigger.Object), NullLogger<Program>.Instance, CancellationToken.None);
        var childResult = await ChildRecordEndpoints.RefileAsync(
            "sprk_todo", Todo, Payload(FileUnderOrdinaryMatter()), _user, _world.Resolver(), Restamper(), Children(), AppEntities(),
            HttpContextWithSignalServices(entities.Object, trigger.Object), NullLogger<Program>.Instance, CancellationToken.None);

        Status(eventResult).Should().Be(StatusCodes.Status204NoContent, Detail(eventResult));
        Status(childResult).Should().Be(StatusCodes.Status204NoContent, Detail(childResult));
        _user.Patches.Should().HaveCount(2, "both re-files were written");
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        entities.VerifyNoOtherCalls();
    }
}
