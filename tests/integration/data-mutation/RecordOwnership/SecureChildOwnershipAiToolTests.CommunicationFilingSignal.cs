using FluentAssertions;
using Microsoft.AspNetCore.Http;
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

    [Fact]
    public async Task CommunicationFiling_ClassifiedAndFiledToAMatter_QueuesTheAssociationConfirmedTrigger_Once_AndStillAnswers204()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: true));

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
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: false));

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task CommunicationFiling_ClassifiedButFiledToNoMatter_QueuesNothing()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, matter: null, classified: true));

        var result = await RefileCommunicationWithTrigger(
            new() { ["sprk_RegardingMatter@odata.bind"] = null }, entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task CommunicationFiling_ARefusedReFile_400_QueuesNothing_AndReadsNothing()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: true));

        var result = await RefileCommunicationWithTrigger(
            new() { ["sprk_RegardingMatter@odata.bind"] = null, ["sprk_associationstatus"] = null }, entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CommunicationFiling_ARefusedReFile_UnderARecordTheCallerCannotAppendTo_QueuesNothing_AndReadsNothing()
    {
        UnfiledCommunicationExists();
        _user.NoAppendTo.Add(OrdinaryMatter);
        var trigger = FilingSignalDoubles.Trigger();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: true));

        var result = await RefileCommunicationWithTrigger(FileUnderOrdinaryMatter(), entities.Object, trigger.Object);

        Status(result).Should().Be(StatusCodes.Status404NotFound);
        _user.Patches.Should().BeEmpty();
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CommunicationFiling_ADeniedWrite_403_QueuesNothing()
    {
        UnfiledCommunicationExists();
        _user.PatchStatus = 403;
        var trigger = FilingSignalDoubles.Trigger();
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: true));

        // A name-only update is the caller's own PATCH (not a re-file), which the scripted Dataverse refuses with 403.
        var result = await RefileCommunicationWithTrigger(
            new() { ["sprk_regardingrecordid"] = Guid.NewGuid().ToString("D") }, entities.Object, trigger.Object);

        Status(result).Should().NotBe(StatusCodes.Status204NoContent);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
        entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CommunicationFiling_WhenTheEnqueueFails_StillAnswers204()
    {
        UnfiledCommunicationExists();
        var trigger = FilingSignalDoubles.Trigger(enqueueResult: false);
        var entities = FilingSignalDoubles.Entities(
            FilingSignalDoubles.Communication(FiledCommunication, OrdinaryMatter, classified: true));

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

    [Fact]
    public async Task EventsAndGenericChildFiling_AreUnaffected_TheyCarryNoSignalTrigger()
    {
        // The shared core takes no trigger and reads no communication: the events and generic child-record routes are
        // exactly as they were. (Their handlers cannot reach the trigger; this pins that the core still answers them.)
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_name"] = "renamed" });

        Status(result).Should().Be(StatusCodes.Status204NoContent);
        typeof(ChildRecordEndpoints).GetMethod(nameof(ChildRecordEndpoints.UpdateAsync),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetParameters().Select(p => p.ParameterType).Should().NotContain(typeof(SignalEvaluationTrigger),
                "the trigger belongs to the communication route, not to the core shared with events and generic child records");
    }
}
