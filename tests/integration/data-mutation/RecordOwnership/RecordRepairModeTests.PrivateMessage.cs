using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22, Q5): a private message is never filed, by any writer, and
/// Repair is one. <c>sprk_communication</c> is a repaired table with no mapping-protected regarding column, so a field-mapping rule
/// from the filing parent can fill a typed regarding (here <c>sprk_regardingorganization</c>, an organization: no ownership parent, so not stopped as a re-file) on an existing communication. Repair's one
/// write asks <see cref="PrivateMessageWriteGuard"/> on the columns it sends: a private message is skipped with
/// <c>sdap.communication.private.not_fileable</c>, a fault of the guard's read fails the row with
/// <see cref="PrivateMessageWriteGuard.CheckUnavailable"/>, and in both cases nothing is written.
/// </summary>
public sealed partial class RecordRepairModeTests
{
    private const string RegardingOrganization = "sprk_regardingorganization";
    private static readonly Guid ClientOfTheMatter = Guid.Parse("a0560000-0000-4000-8000-0000000000c1");

    /// <summary>
    /// The world's communication, filed under the matter, with a rule that fills its empty organization regarding from the
    /// matter's client. An organization is no ownership parent, so Repair's would-refile filter does not stop the rule.
    /// </summary>
    private void TheMatterMapsItsClientIntoTheCommunicationsRegarding(bool isPrivate)
    {
        _world.Directory.Row("sprk_matter", RepairWorld.Matter)["sprk_clientorganization"] =
            new EntityReference("sprk_organization", ClientOfTheMatter);
        _world.Map("sprk_matter", "sprk_communication",
            RepairWorld.CopyLookup("sprk_clientorganization", RegardingOrganization, 1));
        _world.ColumnOverrides[("sprk_communication", PrivateContextColumns.Matter)] =
            RepairWorld.Typed(PrivateContextColumns.Matter, "Lookup");
        if (isPrivate)
        {
            _world.Directory.Row("sprk_communication", RepairWorld.Communication)[PrivateContextColumns.Matter] =
                new EntityReference("sprk_matter", RepairWorld.Matter);
        }
    }

    [Fact]
    public async Task APrivateMessage_WhoseRepairWouldFillARegarding_IsSkipped_AndNothingIsWritten()
    {
        TheMatterMapsItsClientIntoTheCommunicationsRegarding(isPrivate: true);

        var outcome = await _world.RepairAsync("sprk_communication", RepairWorld.Communication);

        outcome.Status.Should().Be(RecordRepairStatus.Skipped);
        outcome.ReasonCode.Should().Be(RecordOwnerRefusal.PrivateMessageNotFileable);
        _world.Writes.Should().BeEmpty();
        _world.Directory.Row("sprk_communication", RepairWorld.Communication).Attributes.Should().NotContainKey(RegardingOrganization);
    }

    [Fact]
    public async Task AGuardReadFault_FailsTheRow_AndNothingIsWritten()
    {
        TheMatterMapsItsClientIntoTheCommunicationsRegarding(isPrivate: false);
        _world.PrivateMessageReadFault = new TimeoutException("Test: the private-message read timed out.");

        var outcome = await _world.RepairAsync("sprk_communication", RepairWorld.Communication);

        outcome.Status.Should().Be(RecordRepairStatus.Failed);
        outcome.ReasonCode.Should().Be(PrivateMessageWriteGuard.CheckUnavailable);
        _world.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnOrdinaryCommunication_HasItsRegardingFilled()
    {
        // Must-not-fire: the same repair of a message that is not private is written.
        TheMatterMapsItsClientIntoTheCommunicationsRegarding(isPrivate: false);

        var outcome = await _world.RepairAsync("sprk_communication", RepairWorld.Communication);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.Column == RegardingOrganization
            && f.State == RecordRepairFieldState.Repaired);
        _world.Writes.Should().ContainSingle(w => w.Applied && w.Fields.ContainsKey(RegardingOrganization));
    }
}

internal sealed partial class RepairWorld
{
    /// <summary>fm task 056: when set, the private-message guard's read of a communication (its private-context columns) throws it.</summary>
    public Exception? PrivateMessageReadFault { get; set; }

    private Task<EntityCollection>? PrivateMessageReadAnswer(QueryExpression query) =>
        PrivateMessageReadFault is { } fault && query.EntityName == "sprk_communication"
            && query.ColumnSet.Columns.Any(c => PrivateContextColumns.All.Contains(c))
            ? Task.FromException<EntityCollection>(fault)
            : null;
}
