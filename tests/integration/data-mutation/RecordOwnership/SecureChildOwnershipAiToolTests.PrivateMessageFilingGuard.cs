using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.DataMutation.Communication;
using Sprk.Bff.Api.Tests.Domain.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner, 2026-11-22, Q5): a private message is never filed. The ONE re-file core
/// (<see cref="OwnedChildWrite.RefileAsync"/>) asks <see cref="PrivateMessageWriteGuard.RefuseUpdateAsync"/> once, before both its
/// ordinary update and its re-file, so <c>PATCH /api/communications/{id}/filing</c> refuses a change the re-file's own rule does not
/// see as a filing: only a typed regarding that is no ownership parent (a person), or only the ADR-024 regarding pair. 409 with the
/// code and nothing written; an ordinary Direct message is filed as before; a guard read fault is 503 and nothing is written.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid PrivateFilingContact = Guid.Parse("d1d1d1d1-0056-4056-8056-0000000003c1");
    private static readonly Guid PrivateFilingRecordType = Guid.Parse("d1d1d1d1-0056-4056-8056-0000000003b1");

    /// <summary>The route's app-only reader: the guard's schema and row read answered by the directory.</summary>
    private IGenericEntityService GuardReaderOverTheWorld() => GuardOverTheWorld(new Mock<IGenericEntityService>()).Object;

    /// <summary>
    /// A composition's own app-only reader, also answering the guard from the directory: every writer of a communication's
    /// filing asks the guard first, and it fails closed on a reader that gives no answer.
    /// </summary>
    private Mock<IGenericEntityService> GuardOverTheWorld(Mock<IGenericEntityService> entities) =>
        entities.AnsweringTheGuardFrom(q => _world.Answer(q));

    /// <inheritdoc cref="GuardOverTheWorld(Mock{IGenericEntityService})"/>
    private IGenericEntityService GuardOverTheWorld(IGenericEntityService entities) => GuardOverTheWorld(Mock.Get(entities)).Object;

    private Task<IResult> FileTheMessage(Dictionary<string, object?> payload, IGenericEntityService? entities = null) =>
        CommunicationEndpoints.RefileCommunicationAsync(
            DirectUnfileMessage, Payload(payload), _user, _world.Resolver(), Restamper(), Children(),
            entities ?? GuardReaderOverTheWorld(), signalTrigger: null, HttpContextOfCaller(), NullLogger<Program>.Instance,
            CancellationToken.None);

    private static Dictionary<string, object?> OnlyAPerson() =>
        new() { ["sprk_RegardingPerson@odata.bind"] = $"/contacts({PrivateFilingContact:D})" };

    private static Dictionary<string, object?> OnlyTheRegardingPair() => new()
    {
        ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
        ["sprk_regardingrecordtype@odata.bind"] = $"/sprk_recordtype_refs({PrivateFilingRecordType:D})",
    };

    private void AssertTheMessageIsUntouched()
    {
        _user.Patches.Should().BeEmpty("the caller's PATCH is never sent");
        _world.Assignments.Should().BeEmpty("no owner is written");
        var row = _world.Row("sprk_communication", DirectUnfileMessage);
        row.GetAttributeValue<EntityReference>("sprk_regardingperson").Should().BeNull();
        row.GetAttributeValue<string>("sprk_regardingrecordid").Should().BeNull();
    }

    [Fact(DisplayName = "fm 056 Q5: PATCH /api/communications/{id}/filing setting only a person on a private message is 409 not_fileable and nothing is written")]
    public async Task FilingRoute_APrivateMessage_WithOnlyAPerson_Is409WithTheCode_AndNothingIsWritten()
    {
        APrivateMessageExists();

        var result = await FileTheMessage(OnlyAPerson());

        Status(result).Should().Be(StatusCodes.Status409Conflict, Detail(result));
        ReasonCode(result).Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
        AssertTheMessageIsUntouched();
    }

    [Fact(DisplayName = "fm 056 Q5: PATCH /api/communications/{id}/filing setting only the regarding pair on a private message is 409 not_fileable and nothing is written")]
    public async Task FilingRoute_APrivateMessage_WithOnlyTheRegardingPair_Is409WithTheCode_AndNothingIsWritten()
    {
        APrivateMessageExists();

        var result = await FileTheMessage(OnlyTheRegardingPair());

        Status(result).Should().Be(StatusCodes.Status409Conflict, Detail(result));
        ReasonCode(result).Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
        AssertTheMessageIsUntouched();
    }

    [Fact(DisplayName = "fm 056 Q5 must not fire: PATCH /api/communications/{id}/filing setting only a person on an ordinary Direct message is written (204)")]
    public async Task FilingRoute_AnOrdinaryDirectMessage_WithOnlyAPerson_IsWritten()
    {
        _world.WithUnfiledDirectMessage(Directory.ChildBu, Directory.ChildTeam);

        var result = await FileTheMessage(OnlyAPerson());

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle();
    }

    [Fact(DisplayName = "fm 056 Q5: PATCH /api/communications/{id}/filing whose private-message read faults is 503 check_unavailable and nothing is written")]
    public async Task FilingRoute_AGuardReadFault_Is503_AndNothingIsWritten()
    {
        _world.WithUnfiledDirectMessage(Directory.ChildBu, Directory.ChildTeam);
        var faulting = new Mock<IGenericEntityService>().AnsweringTheGuardFrom(
            _ => throw new InvalidOperationException("Test: Dataverse unavailable."));

        var result = await FileTheMessage(OnlyAPerson(), faulting.Object);

        Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable, Detail(result));
        ReasonCode(result).Should().Be(PrivateMessageWriteGuard.CheckUnavailable);
        AssertTheMessageIsUntouched();
    }
}
