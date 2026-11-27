using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology R1 task 227 (#1881, DI-82): the evaluator's "who is this item for" column (which decides whose "today" a Date Only bound
/// is judged in, D-25 as amended by D-129) and the worklist's Do-lane "mine" test (<see cref="OwnedChildWrite.ForPersonColumns"/>)
/// must name the same column. They are two maps, so the test pins them together for every subject both define.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): pure data, no fakes.</remarks>
[Trait("status", "new")]
public class SignalEvaluatorAssigneeColumnTests
{
    [Fact]
    public void WorkAssignment_ReadsTheAssignedToInternalContact_NotAssignedTo()
    {
        SignalEvaluator.AssigneeColumnBySubject["sprk_workassignment"]
            .Should().Be(OwnedChildWrite.ForPersonColumns["sprk_workassignment"].Column)
            .And.Be("sprk_assignedtointernal");
    }

    [Fact]
    public void EverySubjectBothMapsDefine_NamesTheSameColumn()
    {
        var shared = SignalEvaluator.AssigneeColumnBySubject
            .Where(entry => OwnedChildWrite.ForPersonColumns.ContainsKey(entry.Key))
            .ToList();

        // Must-fire control: the comparison below must actually run on the three Do-lane subjects.
        shared.Select(entry => entry.Key).Should().BeEquivalentTo("sprk_todo", "sprk_event", "sprk_workassignment");

        foreach (var (subject, column) in shared)
        {
            column.Should().Be(OwnedChildWrite.ForPersonColumns[subject].Column, $"the evaluator and the worklist agree on {subject}'s individual");
        }
    }

    [Fact]
    public void OtherSubjects_KeepTheirColumns()
    {
        SignalEvaluator.AssigneeColumnBySubject.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["sprk_todo"] = "sprk_assignedto",
            ["sprk_event"] = "sprk_assignedto",
            ["sprk_workassignment"] = "sprk_assignedtointernal",
        });
    }
}
