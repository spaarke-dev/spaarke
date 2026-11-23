using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Domain.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner, 2026-11-22, Q5): a private message is never filed and never moved to another
/// thread. The refusal is the re-file core's (<see cref="IRecordOwnershipResolver.ReparentAsync"/>,
/// <see cref="RecordOwnerRefusal.PrivateMessageNotFileable"/>); these drive each writer that reaches it with the REAL resolver over
/// <see cref="Directory"/>: the filing route answers 409 with the code, and no writer writes anything.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private void APrivateMessageExists() =>
        _world.WithPrivateMessage(PrivateContextColumns.Matter, OrdinaryMatter);

    private Task<IResult> FileTheMessageUnderTheOrdinaryMatter() =>
        CommunicationEndpoints.RefileCommunicationAsync(
            DirectUnfileMessage, Payload(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})" }),
            _user, _world.Resolver(), Restamper(), Children(), GuardReaderOverTheWorld(), signalTrigger: null,
            HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);

    [Fact(DisplayName = "fm 056 Q5: PATCH /api/communications/{id}/filing of a private message is 409 sdap.communication.private.not_fileable and nothing is written")]
    public async Task FilingRoute_APrivateMessage_Is409WithTheCode_AndNothingIsWritten()
    {
        APrivateMessageExists();

        var result = await FileTheMessageUnderTheOrdinaryMatter();

        Status(result).Should().Be(StatusCodes.Status409Conflict, Detail(result));
        ReasonCode(result).Should().Be("sdap.communication.private.not_fileable");
        _user.Patches.Should().BeEmpty("the caller's PATCH is never sent");
        _world.Assignments.Should().BeEmpty("no owner is written");
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("sprk_regardingmatter").Should().BeNull();
    }

    [Fact(DisplayName = "fm 056 Q5 must not fire: the filing route files an ordinary Direct message as before (204)")]
    public async Task FilingRoute_AnOrdinaryDirectMessage_IsFiledAsBefore()
    {
        _world.WithUnfiledDirectMessage(Directory.ChildBu, Directory.ChildTeam);

        var result = await FileTheMessageUnderTheOrdinaryMatter();

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle();
    }

    [Fact(DisplayName = "fm 056 Q5: the ONE re-file core (OwnedChildWrite.RefileAsync) refuses a private message's filing with the code and sends no PATCH")]
    public async Task RefileCore_APrivateMessage_IsRefusedWithTheCode_AndSendsNoPatch()
    {
        APrivateMessageExists();

        var outcome = await OwnedChildWrite.RefileAsync(
            _user, _world.Resolver(), "sprk_communication", DirectUnfileMessage, "sprk_communications", "sprk_communicationid",
            new DataverseWriteItemMapper.MappedItem(
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
                }),
                ["sprk_regardingmatter"])
            {
                Lookups = [new DataverseWriteItemMapper.MappedLookup("sprk_regardingmatter", "sprk_matter", "sprk_matters", OrdinaryMatter)],
            },
            callerObjectId: null, CancellationToken.None, appOnlyEntities: GuardReaderOverTheWorld());

        outcome.Written.Should().BeFalse();
        outcome.OwnerRefusal?.RefusalCode.Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
    }

    [Fact(DisplayName = "fm 056 Q5: the chat update tool filing a private message is refused and writes nothing")]
    public async Task ChatUpdate_FilingAPrivateMessage_IsRefused_AndWritesNothing()
    {
        APrivateMessageExists();
        var lookup = new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectUnfileTeam).Lookup();
        var calls = new List<string>();

        var result = await ChatUpdateWithStepAfterRefile(lookup, StepAfterRefileRecorded(calls).Object, calls,
            Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeFalse();
        _user.Patches.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
        calls.Should().BeEmpty("no grant, no owner write, no step after a re-file");
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("sprk_regardingmatter").Should().BeNull();
    }

    [Fact(DisplayName = "fm 056 Q5: a thread join that would put a private message into a record's thread is refused and stamps nothing")]
    public async Task ThreadJoin_OfAPrivateMessage_IntoARecordThread_IsRefused_AndStampsNothing()
    {
        AMessageFiledUnderTheOrdinaryMatter_AndAThreadOf(OrdinaryMatter);
        _world.Write("sprk_communication", JoiningMessage, new Dictionary<string, object?>
        {
            [PrivateContextColumns.Project] = new EntityReference("sprk_project", FlaggedProject),
        });

        var assignment = await JoiningResolver(StepScopes()).ResolveAndAssignAsync(JoinRequest(), CancellationToken.None);

        assignment.Should().BeNull("a refusal leaves the message where it is");
        _threadStamps.Should().BeEmpty();
        _world.Assignments.Should().BeEmpty();
        _stepThreads.Invocations.Should().BeEmpty();
    }
}
