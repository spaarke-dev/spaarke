// unified-access-control-r2 task 194b fix round R (R4) — parents a row holds no ordinary lookup for are keyed under the REAL
// column that files the row there, and ranked by the table's order: a message's thread filing under sprk_communicationthread
// (after the message's own lookups), a project's pair under sprk_regardingrecordid (first). The creator (RecordCreationService,
// Create and Repair) and the reconciler (SecureChildReconciler's question) ask the same ordered question.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

[Trait("status", "task-194b-uac-r2")]
public class OwnershipColumnKeyingParityTests
{
    private static readonly Guid MatterId = Guid.Parse("194b0004-0000-4000-8000-0000000000a1");
    private static readonly Guid ProjectId = Guid.Parse("194b0004-0000-4000-8000-0000000000b1");
    private static readonly Guid ThreadMatterId = Guid.Parse("194b0004-0000-4000-8000-0000000000a2");
    private static readonly Guid ThreadProjectId = Guid.Parse("194b0004-0000-4000-8000-0000000000b2");
    private static readonly Guid ThreadId = Guid.Parse("194b0004-0000-4000-8000-0000000000c1");

    private static Entity Message() => new("sprk_communication", Guid.NewGuid())
    {
        // Attribute order deliberately NOT the table's: the project before the matter, the thread first.
        [RecordThreadFiling.ThreadColumn] = new EntityReference(RecordThreadFiling.ThreadTable, ThreadId),
        ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId),
        ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
    };

    private static readonly RecordOwnershipParent Matter = new("sprk_matter", MatterId);
    private static readonly RecordOwnershipParent Project = new("sprk_project", ProjectId);
    private static readonly RecordOwnershipParent ThreadMatter = new("sprk_matter", ThreadMatterId);
    private static readonly RecordOwnershipParent ThreadProject = new("sprk_project", ThreadProjectId);

    [Fact]
    public void AMessagesThreadFiling_RanksAfterItsOwnLookups_AsTheReconcilerAsksIt()
    {
        var row = Message();

        var question = RecordThreadFiling.QuestionWithThreadFiling(row, [ThreadMatter, ThreadProject]);

        question.Parents.Should().Equal(Matter, Project, ThreadMatter, ThreadProject);
        question.Parents.Should().Equal(
            RecordOwnershipContext.OrderedParentsOf(row).Concat([ThreadMatter, ThreadProject]).Distinct(),
            "the reconciler's rule before R4: the message's own parents, then its thread's filing");
        question.TargetEntityLogicalName.Should().Be("sprk_matter", "the message's own primary, never its thread's");
    }

    [Fact]
    public void TwoParentsUnderTheThreadColumn_KeepTheirOrder()
    {
        var question = RecordThreadFiling.QuestionWithThreadFiling(Message(), [ThreadProject, ThreadMatter]);

        question.Parents.Should().Equal(Matter, Project, ThreadProject, ThreadMatter);
    }

    [Fact]
    public void ADirectThread_ContributesNoParent()
    {
        var direct = new Entity(RecordThreadFiling.ThreadTable, ThreadId)
        {
            [RecordThreadFiling.ThreadTypeColumn] = new OptionSetValue(RecordThreadFiling.DirectThreadType),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", ThreadMatterId),
        };
        var row = Message();

        RecordThreadFiling.QuestionWithThreadFiling(row, RecordThreadFiling.ParentsOf(direct)).Parents
            .Should().Equal(RecordOwnershipContext.ForChild(row).Parents);
    }

    [Fact]
    public void RepairOfAMessage_AsksTheReconcilersQuestion()
    {
        var row = Message();
        var threadParents = new[] { ThreadMatter, ThreadProject };

        var repair = RecordCreationService.OwnerQuestionOf(
            row.LogicalName,
            RecordOwnershipContext.LookupsOf(row).Concat(RecordThreadFiling.FilingLookupsOf(threadParents)),
            pairParent: null, RecordOwnershipContext.PairIdOf(row), callerObjectId: null, callerSystemUserId: null);

        repair.Parents.Should().Equal(RecordThreadFiling.QuestionWithThreadFiling(row, threadParents).Parents);
    }

    [Fact]
    public void AProjectsPair_IsItsPrimary_AheadOfAnyLookupItHolds()
    {
        // The pair names a matter; the project also holds a lookup to another ownership parent (named to sort BEFORE
        // sprk_regardingrecordid by column name, so only the project's order can put the pair first).
        var pairMatter = new RecordOwnershipParent("sprk_matter", MatterId);
        var other = new RecordOwnershipParent("sprk_workassignment", Guid.Parse("194b0004-0000-4000-8000-0000000000d1"));

        var create = RecordCreationService.OwnerQuestionOf(
            "sprk_project",
            [KeyValuePair.Create("sprk_parentworkassignment", (RecordOwnershipParent?)other)],
            pairMatter, MatterId.ToString("D"), callerObjectId: null, callerSystemUserId: null);

        create.Parents.Should().Equal(pairMatter, other);
        create.TargetEntityLogicalName.Should().Be("sprk_matter");

        // Repair passes the pair it read already as a keyed parent (no second read): the same question.
        var repair = RecordCreationService.OwnerQuestionOf(
            "sprk_project",
            [
                KeyValuePair.Create("sprk_parentworkassignment", (RecordOwnershipParent?)other),
                KeyValuePair.Create("sprk_regardingrecordid", (RecordOwnershipParent?)pairMatter),
            ],
            pairParent: null, MatterId.ToString("D"), callerObjectId: null, callerSystemUserId: null);

        repair.Parents.Should().Equal(create.Parents);
    }
}
