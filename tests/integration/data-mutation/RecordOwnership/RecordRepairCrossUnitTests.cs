using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using RestrictedContentOwnerTeam = Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeam;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 052g (spec FR-25 as amended; design 052e §3.3, §3.4; UAC-r2's guard shape): Repair
/// re-owns a person-owned row to a team in another ordinary business unit and reports the move; Repair leaves isolated rows to
/// the secure pass (the answer or the row in the Secure Record unit, a restricted-content owner team as the answer, a row owned
/// by the Secure Record owner team), and keeps the owner when that cannot be told; a 412 on an owner write is read once and
/// never retried. The guard's reads are the shared ones: the configured Secure Record unit and the reconciler's isolation read.
/// </summary>
public sealed partial class RecordRepairModeTests
{
    private static readonly Guid PersonInGeneral = Directory.CallerUserId; // the standard directory's user in the general unit

    // =====================================================================================
    // AC1 / AC4b must-not-fire: an ordinary cross-unit re-own is written and reported
    // =====================================================================================

    [Fact]
    public async Task AC1_APersonOwnedEventUnderAMatterInAnotherUnit_IsReOwnedToThatUnitsTeam_AndTheMoveIsReported()
    {
        // Replaces task 016's AnOwnerInAnotherBusinessUnit_IsNotWritten_EscalationTrigger2 (052g, design 052e §3.3.2): the person
        // who owns the event sits in the general unit, the matter it is filed under in the child unit.
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", PersonInGeneral), owningUnit: Directory.GeneralBu, indexed: true);

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        var write = _world.Writes.Should().ContainSingle("one conditional update, as any owner write").Subject;
        write.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        write.Applied.Should().BeTrue();
        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Repaired);
        outcome.MovedBusinessUnit.Should().Be(new RecordRepairBusinessUnitMove(
            Directory.GeneralBu, Directory.ChildBu, new EntityReference("sprk_matter", RepairWorld.Matter)));
        _world.Evictions.Should().ContainSingle().Which.Should().Be(("sprk_event", "sprk_events", id));
        _world.Log.Entries.Should().ContainSingle(e => e.Message.Contains("re-owned across business units")
            && e.Message.Contains(id.ToString()) && e.Message.Contains(Directory.GeneralBu.ToString())
            && e.Message.Contains(Directory.ChildBu.ToString()) && e.Message.Contains(RepairWorld.Matter.ToString("D")));
        _world.IsolationReads.Should().Equal([("sprk_event", id)], "AC4b: the guard asked the shared isolation read once");
        _world.Directory.Row("sprk_event", id).GetAttributeValue<EntityReference>("owningbusinessunit").Id
            .Should().Be(Directory.ChildBu);
    }

    [Fact]
    public async Task AC1_APersonOwnedDocumentUnderAMatterInAnotherUnit_IsReOwnedToThatUnitsTeam_AndTheMoveIsReported()
    {
        // Replaces task 041's ADocumentOwnedInAnotherBusinessUnit_IsNotWritten_EscalationTrigger2 (052g, design 052e §3.3.2).
        var id = _world.RowOf("sprk_document", Directory.GeneralBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", PersonInGeneral),
            ["sprk_matter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
        });

        var outcome = await _world.RepairAsync("sprk_document", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        _world.Writes.Should().ContainSingle().Which.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        outcome.MovedBusinessUnit.Should().NotBeNull();
        outcome.MovedBusinessUnit!.FromUnit.Should().Be(Directory.GeneralBu);
        outcome.MovedBusinessUnit.ToUnit.Should().Be(Directory.ChildBu);
        outcome.MovedBusinessUnit.FamilyRoot.Should().BeNull(
            "the document holds no I-1 stamp column and the plan adds none, so no family root is named");
        _world.Log.Entries.Should().ContainSingle(e => e.Message.Contains("re-owned across business units") && e.Message.Contains(id.ToString()));
    }

    [Fact]
    public async Task AC1_AToDoMissingItsStamp_ReportsTheFamilyRootTheStampPlanAdds()
    {
        var id = _world.ToDoMissingItsStampUnderAnInvoiceOfTheMatter(Directory.GeneralBu);

        var outcome = await _world.RepairAsync("sprk_todo", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        write.Fields["sprk_regardingmatter"].Should().Be(new EntityReference("sprk_matter", RepairWorld.Matter));
        outcome.MovedBusinessUnit.Should().Be(new RecordRepairBusinessUnitMove(
            Directory.GeneralBu, Directory.ChildBu, new EntityReference("sprk_matter", RepairWorld.Matter)));
    }

    [Fact]
    public async Task AC1_ASameUnitReOwn_IsWritten_WithNoMoveReported()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent", indexed: true);

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        outcome.MovedBusinessUnit.Should().BeNull("the person and the matter's team sit in the same unit");
        _world.Log.Entries.Should().NotContain(e => e.Message.Contains("re-owned across business units"));
    }

    [Fact]
    public async Task AC1_AMoveWhoseOwnerTheCascadeCheckTookOut_IsNeitherWrittenNorReported()
    {
        // 052f's check runs after the owner step: a child along an unaccepted relationship takes the owner out of the patch.
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", PersonInGeneral), owningUnit: Directory.GeneralBu, indexed: true);
        _world.CascadesTo("sprk_event", "sprk_event_Tasks", "activitypointer", "regardingobjectid");
        _world.AddCascadeChild("activitypointer", "regardingobjectid", id, activityType: "task");

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid");
        outcome.MovedBusinessUnit.Should().BeNull();
        _world.Log.Entries.Should().NotContain(e => e.Message.Contains("re-owned across business units"));
    }

    [Fact]
    public async Task AC1_AMoveWhoseWriteFails_IsNotReported()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", PersonInGeneral), owningUnit: Directory.GeneralBu, indexed: true);
        _world.WriteFault = new TimeoutException("Test: the update timed out.");

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.ReasonCode.Should().Be(RecordRepairReason.WriteFailed);
        outcome.MovedBusinessUnit.Should().BeNull("a move is reported only after a confirmed write");
        _world.Log.Entries.Should().NotContain(e => e.Message.Contains("re-owned across business units"));
    }

    // =====================================================================================
    // AC2: a person-owned ordinary root re-owned across units: one update, nothing else
    // =====================================================================================

    [Fact]
    public async Task AC2_APersonOwnedOrdinaryWorkAssignmentUnderAMatterInAnotherUnit_GetsExactlyOneUpdate_AndNoOtherCall()
    {
        var id = _world.RowOf("sprk_workassignment", Directory.GeneralBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", PersonInGeneral),
            ["sprk_matter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
            ["sprk_issecure"] = false,
        });
        var versionBefore = _world.Directory.Version("sprk_workassignment", id);

        var outcome = await _world.RepairAsync("sprk_workassignment", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        write.Applied.Should().BeTrue("the row changed, so its modifiedon moves and the ordinary-moves pass reaches its family");
        _world.Directory.Version("sprk_workassignment", id).Should().BeGreaterThan(versionBefore);
        outcome.MovedBusinessUnit.Should().NotBeNull();
        outcome.MovedBusinessUnit!.ToUnit.Should().Be(Directory.ChildBu);

        // Repair's only write is the one conditional update; nothing else wrote anywhere (no family mover, no hand-off).
        var serviceCalls = _world.ServiceEntityMocks.SelectMany(m => m.Invocations).Select(i => i.Method.Name).ToList();
        serviceCalls.Count(n => n == nameof(IGenericEntityService.UpdateIfRowVersionMatchesAsync)).Should().Be(1);
        serviceCalls.Should().OnlyContain(n => n == nameof(IGenericEntityService.UpdateIfRowVersionMatchesAsync)
            || n == nameof(IGenericEntityService.RetrieveMultipleAsync) || n == nameof(IGenericEntityService.RetrieveAsync)
            || n == nameof(IGenericEntityService.GetColumnMetadataAsync) || n == nameof(IGenericEntityService.GetAssignCascadesAsync)
            || n == nameof(IGenericEntityService.GetCurrentIdentityIdAsync));
        _world.ReconcilerEntityMocks.Should().ContainSingle("the guard resolved the reconciler once");
        _world.ReconcilerEntityMocks.SelectMany(m => m.Invocations)
            .Where(i => i.Method.Name != nameof(IGenericEntityService.RetrieveMultipleAsync))
            .Should().BeEmpty("the reconciler is only asked the isolation read (a root is no lineage child: it reads nothing); "
                + "none of its movers runs");
        _world.Directory.Assignments.Should().BeEmpty("no resolver re-own");
        _world.Directory.FieldUpdates.Should().BeEmpty();
    }

    // =====================================================================================
    // AC3 (must fire): the answer is isolated — a document, an event and a to-do under the secure matter
    // =====================================================================================

    public enum IsolatedAnswer
    {
        SecureNamedTeam,
        OtherTeamInTheSecureUnit,
        RestrictedContentTeamSubstituted,
        RestrictedContentTeamByName,
    }

    [Theory]
    [InlineData("sprk_document", IsolatedAnswer.SecureNamedTeam)]
    [InlineData("sprk_event", IsolatedAnswer.SecureNamedTeam)]
    [InlineData("sprk_todo", IsolatedAnswer.SecureNamedTeam)]
    [InlineData("sprk_document", IsolatedAnswer.OtherTeamInTheSecureUnit)]
    [InlineData("sprk_event", IsolatedAnswer.OtherTeamInTheSecureUnit)]
    [InlineData("sprk_todo", IsolatedAnswer.OtherTeamInTheSecureUnit)]
    [InlineData("sprk_document", IsolatedAnswer.RestrictedContentTeamSubstituted)]
    [InlineData("sprk_event", IsolatedAnswer.RestrictedContentTeamSubstituted)]
    [InlineData("sprk_todo", IsolatedAnswer.RestrictedContentTeamSubstituted)]
    [InlineData("sprk_document", IsolatedAnswer.RestrictedContentTeamByName)]
    [InlineData("sprk_event", IsolatedAnswer.RestrictedContentTeamByName)]
    [InlineData("sprk_todo", IsolatedAnswer.RestrictedContentTeamByName)]
    public async Task AC3_ARowUnderTheSecureMatterWhoseAnswerIsIsolated_IsLeftToTheSecurePass_NeverWritten(string table, IsolatedAnswer answer)
    {
        var id = _world.UnderTheSecureMatter(table, Directory.ChildBu);
        switch (answer)
        {
            case IsolatedAnswer.SecureNamedTeam:
                break; // the real resolver answers the Secure Record owner team for a child of a secure record
            case IsolatedAnswer.OtherTeamInTheSecureUnit:
                _world.ResolverAnswer = RecordOwnerResolution.Owned(Directory.SecureDefaultTeam); // not the named team
                break;
            case IsolatedAnswer.RestrictedContentTeamSubstituted:
                // The resolver put the restricted-content owner team in place of the unit's default team; its name is not the
                // configured one here, so only the substitution can tell it.
                _world.GuardSettings[RestrictedContentOwnerTeam.OwnerTeamNameConfigKey] = "Some Other Team Name";
                _world.ResolverAnswer = RecordOwnerResolution.Owned(Directory.RestrictedContentTeam) with
                {
                    InPlaceOfDefaultTeamId = Directory.ChildTeam,
                };
                break;
            case IsolatedAnswer.RestrictedContentTeamByName:
                _world.ResolverAnswer = RecordOwnerResolution.Owned(Directory.RestrictedContentTeam); // its configured (default) name
                break;
        }

        var outcome = await _world.RepairAsync(table, id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Skipped);
        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerLeftToSecurePass);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid");
        _world.Directory.Row(table, id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
        outcome.MovedBusinessUnit.Should().BeNull();
        outcome.NeedsRetry.Should().BeFalse("left to the secure pass is an answer, not a fault");
        _world.Evictions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_document")]
    [InlineData("sprk_event")]
    [InlineData("sprk_todo")]
    public async Task AC3_ARowUnderTheSecureMatterAlreadyInTheSecureUnit_IsLeftToTheSecurePass(string table)
    {
        // Before 052g a reparentable child in the Secure Record unit was re-owned to the Secure team here (same unit). Now the
        // secure pass owns every move into isolation (design 052e §3.3.1).
        var id = _world.UnderTheSecureMatter(table, Directory.SecureBu);

        var outcome = await _world.RepairAsync(table, id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerLeftToSecurePass);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid");
    }

    [Fact]
    public async Task AC4b_ASecureFiledRow_KeepsItsF1bMappingBlock_WhenLeftToTheSecurePass()
    {
        _world.SecureMatterHolding("Secret Client", "Secret Practice");
        var id = _world.EventUnderTheSecureMatter(new EntityReference("systemuser", RepairWorld.PersonInChild), Directory.ChildBu);

        var outcome = await _world.RepairAsync("sprk_event", id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerLeftToSecurePass);
        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Mapping)
            .Which.ReasonCode.Should().Be(RecordRepairReason.MappingSourceSecure);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(["ownerid", "sprk_clientname", "sprk_practicearea"]);
    }

    // =====================================================================================
    // AC4 (must fire): the row itself is isolated
    // =====================================================================================

    [Fact]
    public async Task AC4_AUserOwnedRowInTheSecureUnit_WithAnOrdinaryAnswer_IsLeftToTheSecurePass()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owningUnit: Directory.SecureBu, indexed: true);

        var outcome = await _world.RepairAsync("sprk_event", id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerLeftToSecurePass);
        _world.Writes.Should().BeEmpty();
        _world.TeamReads.Should().BeEmpty("decided on the row's unit, before the answer team is read");
    }

    [Fact]
    public async Task AC4_ARowTheSharedIsolationReadCallsIsolated_IsLeftToTheSecurePass()
    {
        // The fixture's row reads systemuser-owned (so Repair reaches the guard) while its owning team is the Secure Record owner
        // team: only the shared isolation read (it reads owningteam) can tell. Dataverse keeps the two consistent; the guard does
        // not rely on it.
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent", indexed: true);
        _world.Directory.Row("sprk_event", id)["owningteam"] = new EntityReference("team", Directory.SecureNamedTeam);

        var outcome = await _world.RepairAsync("sprk_event", id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerLeftToSecurePass);
        _world.Writes.Should().BeEmpty();
        _world.IsolationReads.Should().Equal([("sprk_event", id)]);
        _world.Directory.RecordQueries.Should().Contain(q => q.EntityName == "sprk_event" && q.TopCount == 1
            && q.ColumnSet.Columns.SequenceEqual(new[] { "owningteam" }), "AC4b: the reconciler's own read, not a local substitute");
    }

    // =====================================================================================
    // AC4a (must fire): whatever cannot be read leaves the owner, counted and asked again
    // =====================================================================================

    public enum IsolationFault
    {
        SecureUnitReadFaults,
        SecureUnitAmbiguous,
        AnswerTeamReadFaults,
        AnswerTeamHasNoUnit,
        RowUnitUnreadable,
        IsolationReadFaultsTwice,
        ReconcilerNotResolvable,
        ConfigurationNotResolvable,
    }

    [Theory]
    [InlineData(IsolationFault.SecureUnitReadFaults)]
    [InlineData(IsolationFault.SecureUnitAmbiguous)]
    [InlineData(IsolationFault.AnswerTeamReadFaults)]
    [InlineData(IsolationFault.AnswerTeamHasNoUnit)]
    [InlineData(IsolationFault.RowUnitUnreadable)]
    [InlineData(IsolationFault.IsolationReadFaultsTwice)]
    [InlineData(IsolationFault.ReconcilerNotResolvable)]
    [InlineData(IsolationFault.ConfigurationNotResolvable)]
    public async Task AC4a_WhenIsolationCannotBeTold_TheOwnerIsKept_NeverWritten_AndAskedAgain(IsolationFault fault)
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", PersonInGeneral), owningUnit: Directory.GeneralBu, indexed: true);
        switch (fault)
        {
            case IsolationFault.SecureUnitReadFaults:
                _world.SecureUnitReadFault = new TimeoutException("Test: the business unit read timed out.");
                break;
            case IsolationFault.SecureUnitAmbiguous:
                _world.Directory.WithBusinessUnit(Guid.NewGuid(), "Twice Named").WithBusinessUnit(Guid.NewGuid(), "Twice Named");
                _world.GuardSettings["SecureRecord:BusinessUnitName"] = "Twice Named";
                break;
            case IsolationFault.AnswerTeamReadFaults:
                _world.AnswerTeamReadFault = new TimeoutException("Test: the team read timed out.");
                break;
            case IsolationFault.AnswerTeamHasNoUnit:
                _world.TeamsWithoutUnit.Add(Directory.ChildTeam);
                break;
            case IsolationFault.RowUnitUnreadable:
                _world.Directory.Row("sprk_event", id).Attributes.Remove("owningbusinessunit");
                break;
            case IsolationFault.IsolationReadFaultsTwice:
                _world.IsolationReadFaults = 2;
                break;
            case IsolationFault.ReconcilerNotResolvable:
                _world.ReconcilerRegistered = false;
                break;
            case IsolationFault.ConfigurationNotResolvable:
                _world.ConfigurationRegistered = false;
                break;
        }

        var outcome = await _world.RepairAsync("sprk_event", id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Skipped);
        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerIsolationUnknown);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid");
        _world.Directory.Row("sprk_event", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", PersonInGeneral));
        outcome.NeedsRetry.Should().BeTrue("a fault, not an answer: the row is asked again");
        outcome.OwnerRefusal.Should().BeNull();
        _world.Log.Entries.Should().Contain(e => e.Message.Contains(RecordRepairReason.OwnerIsolationUnknown) && e.Message.Contains(id.ToString()));
    }

    [Fact]
    public async Task AC4a_AnIsolationReadThatFaultsOnce_IsReadAgainByTheSharedRead_AndTheMoveIsWritten()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", PersonInGeneral), owningUnit: Directory.GeneralBu, indexed: true);
        _world.IsolationReadFaults = 1;

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.IsolationReads.Should().HaveCount(2, "RefileIsolationRead reads once more after a fault");
        _world.Writes.Should().ContainSingle().Which.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        outcome.MovedBusinessUnit.Should().NotBeNull();
    }

    [Fact]
    public async Task AC4a_AnEnvironmentWithNoSecureRecordUnit_DecidesOnTheRestOfTheGuard()
    {
        // (null, not ambiguous): no unit is isolated by the unit tests; the restricted-content and isolation reads still run.
        _world.GuardSettings["SecureRecord:BusinessUnitName"] = "No Such Unit";
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", PersonInGeneral), owningUnit: Directory.GeneralBu, indexed: true);

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.ChildTeam));
        outcome.MovedBusinessUnit.Should().NotBeNull();
        _world.IsolationReads.Should().ContainSingle();
    }

    // =====================================================================================
    // AC4b: the guard follows the configuration (no team id in code)
    // =====================================================================================

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AC4b_TheSecureRecordUnit_IsTheConfiguredOne(bool configuredAsTheSecureUnit)
    {
        // The answer is the default team of the customer's "Litigation" unit. Configured as the Secure Record unit, it is left
        // to the secure pass; otherwise it is an ordinary cross-unit move (must-not-fire twin).
        if (configuredAsTheSecureUnit)
            _world.GuardSettings["SecureRecord:BusinessUnitName"] = "Litigation";
        _world.ResolverAnswer = RecordOwnerResolution.Owned(Directory.CustomerChildTeam);
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent", indexed: true);

        var outcome = await _world.RepairAsync("sprk_event", id);

        if (configuredAsTheSecureUnit)
        {
            Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerLeftToSecurePass);
            _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid");
        }
        else
        {
            _world.Writes.Should().ContainSingle().Which.Fields["ownerid"].Should().Be(new EntityReference("team", Directory.CustomerChildTeam));
            outcome.MovedBusinessUnit!.ToUnit.Should().Be(Directory.CustomerChildBu);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AC4b_TheRestrictedContentOwnerTeam_IsTheConfiguredName(bool renamed)
    {
        // With the name configured elsewhere, a team that merely carries the default name is an ordinary answer, and the team
        // with the configured name is the restricted-content owner team.
        var renamedTeam = Guid.NewGuid();
        _world.Directory.WithTeam(renamedTeam, Directory.ChildBu, isDefault: false, teamType: 0, "Restricted Owners Renamed");
        if (renamed)
            _world.GuardSettings[RestrictedContentOwnerTeam.OwnerTeamNameConfigKey] = " restricted owners renamed ";

        _world.ResolverAnswer = RecordOwnerResolution.Owned(Directory.RestrictedContentTeam);
        var defaultNamed = await _world.RepairAsync("sprk_event", _world.EventUnderTheMatter(
            clientName: "Acme", practiceArea: "Tax", priority: "Urgent", indexed: true));
        _world.ResolverAnswer = RecordOwnerResolution.Owned(renamedTeam);
        var renamedNamed = await _world.RepairAsync("sprk_event", _world.EventUnderTheMatter(
            clientName: "Acme", practiceArea: "Tax", priority: "Urgent", indexed: true));

        Field(defaultNamed, RecordRepairPart.Owner, "ownerid").ReasonCode
            .Should().Be(renamed ? null : RecordRepairReason.OwnerLeftToSecurePass);
        Field(renamedNamed, RecordRepairPart.Owner, "ownerid").ReasonCode
            .Should().Be(renamed ? RecordRepairReason.OwnerLeftToSecurePass : null);
    }

    // =====================================================================================
    // AC4d (must fire): an isolated event with a note is skipped whole; its note is never read or moved
    // =====================================================================================

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AC4d_AnEventWithANoteUnderTheSecureMatter_IsSkippedWhole_ItsNoteUntouched(bool isolationReadFaults)
    {
        _world.CascadesTo("sprk_event", "sprk_event_Annotations", "annotation", "objectid");
        var id = isolationReadFaults
            ? _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent", indexed: true)
            : _world.UnderTheSecureMatter("sprk_event", Directory.ChildBu);
        if (isolationReadFaults)
            _world.IsolationReadFaults = 2;
        _world.AddNote(id, new EntityReference("systemuser", RepairWorld.PersonInChild));

        var outcome = await _world.RepairAsync("sprk_event", id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(isolationReadFaults
            ? RecordRepairReason.OwnerIsolationUnknown
            : RecordRepairReason.OwnerLeftToSecurePass);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid", "no Assign, so no note moves with it");
        _world.AssignCascadeReads.Should().BeEmpty("the cascade check runs only when an owner is in the patch");
        _world.CascadeChildQueries.Should().BeEmpty("the note is never read");
        _world.RoleQueries.Should().BeEmpty();
    }

    // =====================================================================================
    // AC4c: a 412 on an owner write is read once, never retried
    // =====================================================================================

    [Fact]
    public async Task AC4c_A412OnAnOwnerWrite_ReadsTheOwnerOnce_WritesOnce_AndIsRowChanged()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", PersonInGeneral), owningUnit: Directory.GeneralBu, indexed: true);
        _world.BeforeWrite = () => _world.Directory.Write("sprk_event", id,
            new Dictionary<string, object?> { ["ownerid"] = new EntityReference("team", Directory.ChildTeam) });

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.RowChanged);
        outcome.ReasonCode.Should().Be(RecordRepairReason.RowChanged);
        outcome.NeedsRetry.Should().BeTrue("as before: the next pass reads it afresh");
        outcome.MovedBusinessUnit.Should().BeNull();
        _world.UpdateCalls.Should().Be(1, "no second write, no loop");
        _world.OwnerReReads.Should().Equal([("sprk_event", id)]);
        _world.Log.Entries.Should().ContainSingle(e => e.Message.Contains("after the conflict")
            && e.Message.Contains("another writer settled the owner") && e.Message.Contains(Directory.ChildTeam.ToString("D"))
            && e.Message.Contains(Directory.ChildBu.ToString("D")));
        _world.Evictions.Should().BeEmpty("nothing was written");
    }

    [Fact]
    public async Task AC4c_A412WhoseReReadFaults_IsLoggedAndIgnored()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", PersonInGeneral), owningUnit: Directory.GeneralBu, indexed: true);
        _world.BeforeWrite = () => _world.Directory.Write("sprk_event", id,
            new Dictionary<string, object?> { ["sprk_clientname"] = "Changed" });
        _world.OwnerReReadFault = new TimeoutException("Test: the re-read timed out.");

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.RowChanged);
        _world.UpdateCalls.Should().Be(1);
        _world.OwnerReReads.Should().ContainSingle();
        _world.Log.Entries.Should().ContainSingle(e => e.Message.Contains("its owner could not be read again"));
    }

    [Fact]
    public async Task AC4c_A412OnAPatchWithoutAnOwner_IsNotReRead()
    {
        // A team-owned event missing its mapped columns: the patch holds no owner.
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null,
            owner: new EntityReference("team", Directory.ChildTeam), indexed: true);
        _world.BeforeWrite = () => _world.Directory.Write("sprk_event", id,
            new Dictionary<string, object?> { ["sprk_description"] = "Changed" });

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.RowChanged);
        _world.Writes.Should().ContainSingle().Which.Fields.Should().NotContainKey("ownerid");
        _world.OwnerReReads.Should().BeEmpty();
    }

    // =====================================================================================
    // AC5 (negative): a secure root is never re-owned, whatever unit its filing is in
    // =====================================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task AC5_ASecureFlaggedRootUnderAMatterInAnotherUnit_IsNeverReOwned(bool? isSecure)
    {
        var columns = new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", PersonInGeneral),
            ["sprk_matter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
        };
        if (isSecure is { } flag)
            columns["sprk_issecure"] = flag;
        var id = _world.RowOf("sprk_workassignment", Directory.GeneralBu, columns);
        if (isSecure is null)
            _world.Directory.Row("sprk_workassignment", id).Attributes.Remove("sprk_issecure");

        var outcome = await _world.RepairAsync("sprk_workassignment", id);

        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerRootSecure);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid");
        _world.IsolationReads.Should().BeEmpty("kept before the guard");
    }

    // =====================================================================================
    // AC7: the cross-unit skip is retired
    // =====================================================================================

    [Fact]
    public void AC7_NoReasonCodeNamesTheRetiredCrossUnitSkip()
    {
        typeof(RecordRepairReason)
            .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(f => f.IsLiteral)
            .Select(f => (string?)f.GetRawConstantValue())
            .Should().NotContain("record_repair.owner_moves_business_unit")
            .And.Contain([RecordRepairReason.OwnerLeftToSecurePass, RecordRepairReason.OwnerIsolationUnknown]);
        RecordRepairOutcome.TransientReasons.Should().Contain(RecordRepairReason.OwnerIsolationUnknown)
            .And.NotContain(RecordRepairReason.OwnerLeftToSecurePass);
    }
}

/// <summary>Task 052g: the pass counts (the run result and the heartbeat).</summary>
public sealed partial class RecordRepairPassJobTests
{
    [Fact]
    public async Task AC1_TheRunCountsACrossUnitReOwn_AndTheHeartbeatNamesIt()
    {
        _pass.NativeCrossUnitEvent();

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        PassWorld.Count(result, "repaired").Should().Be(1);
        PassWorld.Count(result, "ownerMovedBusinessUnit").Should().Be(1);
        PassWorld.Count(result, "ownerLeftToSecurePass").Should().Be(0);
        PassWorld.Count(result, "ownerIsolationUnknown").Should().Be(0);
        _pass.Log.Entries.Last(e => e.Message.Contains("heartbeat")).Message
            .Should().Contain("ownerMovedBusinessUnit=1 ").And.Contain("ownerLeftToSecurePass=0 ").And.Contain("ownerIsolationUnknown=0 ");
        World.Log.Entries.Should().ContainSingle(e => e.Message.Contains("re-owned across business units")
            && e.Message.Contains(Directory.GeneralBu.ToString()) && e.Message.Contains(Directory.ChildBu.ToString()));
    }

    [Fact]
    public async Task AC3_TheRunCountsRowsLeftToTheSecurePass_AndTheUndecidedOnesAreAlsoCountedAndAskedAgain()
    {
        _pass.Native("sprk_event", World.UnderTheSecureMatter("sprk_event", Directory.ChildBu));
        _pass.NativeCrossUnitEvent();
        World.IsolationReadFaults = 2; // the secure-filed row is left before the isolation read; the other faults

        var result = await _pass.RunAsync();

        result.ErrorMessage.Should().Contain("1 row(s) to ask again", "the undecided row is a fault the run reports");
        World.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("ownerid");
        PassWorld.Count(result, "ownerLeftToSecurePass").Should().Be(2);
        PassWorld.Count(result, "ownerIsolationUnknown").Should().Be(1);
        PassWorld.Count(result, "ownerMovedBusinessUnit").Should().Be(0);
        PassWorld.Count(result, "pendingRetry").Should().Be(1, "only the undecided row is asked again");
        _pass.Log.Entries.Last(e => e.Message.Contains("heartbeat")).Message
            .Should().Contain("ownerLeftToSecurePass=2 ").And.Contain("ownerIsolationUnknown=1 ");
    }

    [Fact]
    public async Task AC6_APreGoLiveRowInACrossUnitPosition_IsNeverWritten()
    {
        var id = _pass.NativeCrossUnitEvent(createdOn: PassWorld.GoLive.AddDays(-1));

        var outcome = await World.RepairAsync("sprk_event", id, _pass.Context());

        outcome.Status.Should().Be(RecordRepairStatus.Skipped);
        outcome.ReasonCode.Should().Be(RecordRepairReason.RowCreatedBeforeGoLive);
        World.Writes.Should().BeEmpty();
        World.IsolationReads.Should().BeEmpty("nothing is decided for a row outside the pass");
        outcome.MovedBusinessUnit.Should().BeNull();
    }

    private sealed partial class PassWorld
    {
        /// <summary>Task 052g: a native event under the matter (child unit), owned by a person in the general unit.</summary>
        public Guid NativeCrossUnitEvent(DateTimeOffset? createdOn = null) =>
            Seed("sprk_event", World.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
                owner: new EntityReference("systemuser", Directory.CallerUserId), owningUnit: Directory.GeneralBu, indexed: true),
                createdOn, createdBy: null, withoutCreatedOn: false);
    }
}

/// <summary>
/// Task 052g: the repair world's isolation read — the configuration and the REAL <see cref="SecureChildReconciler"/> the guard
/// resolves per row (over this world's directory, its isolation read logged and faulted on demand), the guard's Secure Record unit
/// and answer-team reads (faulted on demand), and the one owner re-read after a 412.
/// </summary>
internal sealed partial class RepairWorld
{
    /// <summary>The configuration the guard resolves: the Secure Record unit's and the restricted-content team's names.</summary>
    public Dictionary<string, string?> GuardSettings { get; } = new()
    {
        ["SecureRecord:BusinessUnitName"] = Directory.SecureBuName,
        ["SecureRecord:OwnerTeamName"] = Directory.SecureOwnerTeamName,
    };

    /// <summary>Whether the host registers <see cref="IConfiguration"/> (default: yes).</summary>
    public bool ConfigurationRegistered { get; set; } = true;

    /// <summary>Whether the host registers <see cref="SecureChildReconciler"/> (default: yes).</summary>
    public bool ReconcilerRegistered { get; set; } = true;

    /// <summary>When set, the guard's read of the Secure Record unit (by name) throws it.</summary>
    public Exception? SecureUnitReadFault { get; set; }

    /// <summary>When set, the guard's read of the answer team throws it.</summary>
    public Exception? AnswerTeamReadFault { get; set; }

    /// <summary>Teams whose row reads back with no business unit.</summary>
    public HashSet<Guid> TeamsWithoutUnit { get; } = [];

    /// <summary>Every single-row team read the service made: the team and the columns.</summary>
    public List<(Guid Team, string[] Columns)> TeamReads { get; } = [];

    /// <summary>How many of the next isolation reads fault.</summary>
    public int IsolationReadFaults { get; set; }

    /// <summary>Every isolation read the reconciler made (the row's owning team).</summary>
    public List<(string Table, Guid Id)> IsolationReads { get; } = [];

    /// <summary>Every owner re-read after a 412.</summary>
    public List<(string Table, Guid Id)> OwnerReReads { get; } = [];

    /// <summary>When set, the owner re-read after a 412 throws it.</summary>
    public Exception? OwnerReReadFault { get; set; }

    /// <summary>The service's Dataverse doubles, one per service built.</summary>
    public List<Mock<IGenericEntityService>> ServiceEntityMocks { get; } = [];

    /// <summary>The reconcilers' Dataverse doubles, one per reconciler resolved.</summary>
    public List<Mock<IGenericEntityService>> ReconcilerEntityMocks { get; } = [];

    /// <summary>A row of <paramref name="table"/> (document, event or to-do) in <paramref name="owningUnit"/>, owned by the person
    /// in the child unit, filed under the secure matter (isolated: the Secure Record unit, its named team, flagged).</summary>
    public Guid UnderTheSecureMatter(string table, Guid owningUnit)
    {
        Directory.WithSecureRoot("sprk_matter", SecureMatter);
        _stamps.Row("sprk_matter", SecureMatter);
        var person = new EntityReference("systemuser", PersonInChild);
        var matter = new EntityReference("sprk_matter", SecureMatter);
        return table switch
        {
            "sprk_event" => EventUnderTheSecureMatter(person, owningUnit),
            "sprk_document" => RowOf(table, owningUnit, new Dictionary<string, object> { ["ownerid"] = person, ["sprk_matter"] = matter }),
            "sprk_todo" => RowOf(table, owningUnit, new Dictionary<string, object> { ["ownerid"] = person, ["sprk_regardingmatter"] = matter }),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Test: a document, an event or a to-do."),
        };
    }

    /// <summary>An ordinary invoice under the matter, owned by the child unit's team.</summary>
    public static readonly Guid OrdinaryInvoice = Guid.Parse("a0160000-0000-4000-8000-0000000005c1");

    /// <summary>A to-do in <paramref name="owningUnit"/>, owned by the person in the general unit, filed under
    /// <see cref="OrdinaryInvoice"/> of the ordinary matter, and missing its I-1 stamp (the plan adds it).</summary>
    public Guid ToDoMissingItsStampUnderAnInvoiceOfTheMatter(Guid owningUnit)
    {
        Directory.WithRecord("sprk_invoice", OrdinaryInvoice, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new Dictionary<string, object> { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) });
        _stamps.Row("sprk_matter", Matter)
            .Row("sprk_invoice", OrdinaryInvoice, [("sprk_matter", "sprk_matter", Matter)]);
        return RowOf("sprk_todo", owningUnit, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", Directory.CallerUserId),
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", OrdinaryInvoice),
        });
    }

    private IServiceCollection WithTheIsolationRead(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(GuardSettings).Build();
        if (ConfigurationRegistered)
            services.AddSingleton<IConfiguration>(configuration);
        if (ReconcilerRegistered)
            services.AddScoped(_ => Reconciler(configuration));
        return services;
    }

    /// <summary>The REAL reconciler over this world's directory (its webApi is never reached by the isolation read).</summary>
    private SecureChildReconciler Reconciler(IConfiguration configuration)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        ReconcilerEntityMocks.Add(entities);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (SecureChildLineage.Children.ContainsKey(query.EntityName) && query.ColumnSet.Columns is ["owningteam"])
                {
                    IsolationReads.Add((query.EntityName, (Guid)query.Criteria.Conditions.Single().Values.Single()));
                    if (IsolationReadFaults > 0)
                    {
                        IsolationReadFaults--;
                        return Task.FromException<EntityCollection>(new TimeoutException("Test: the isolation read timed out."));
                    }
                }

                return Task.FromResult(Directory.Answer(query));
            });
        var synchronizer = new SecureChildShareSynchronizer(entities.Object, new FakeRecordShareTable(),
            SecureChildShareWorld.NobodyWalled(), configuration, NullLogger<SecureChildShareSynchronizer>.Instance);
        return new SecureChildReconciler(entities.Object, Directory.Resolver(), synchronizer, null!, configuration,
            NullLogger<SecureChildReconciler>.Instance, new GateSharingWorld().Sharing);
    }

    /// <summary>The guard's read of the Secure Record unit, faulted when <see cref="SecureUnitReadFault"/> is set.</summary>
    private Task<EntityCollection>? SecureUnitAnswer(QueryExpression query) =>
        SecureUnitReadFault is { } fault && query.EntityName == "businessunit"
            && query.Criteria.Conditions.Any(c => c.AttributeName == "name")
            ? Task.FromException<EntityCollection>(fault)
            : null;

    private Entity OwnerReRead(string table, Guid id)
    {
        OwnerReReads.Add((table, id));
        if (OwnerReReadFault is { } fault)
            throw fault;
        var row = Directory.Row(table, id);
        var copy = new Entity(table, id);
        foreach (var column in new[] { "ownerid", "owningbusinessunit" })
        {
            if (row.Attributes.TryGetValue(column, out var value))
                copy[column] = value;
        }

        return copy;
    }
}
