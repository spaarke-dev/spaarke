using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// The side-effect classification of <see cref="ExecutorType"/> is CLOSED (unified-access-control-r2 task 162): POST
/// /api/ai/analysis/execute requires Write on the documents for a playbook with a side-effecting node, so a new
/// executor type that nobody classified must fail here rather than silently count as read-only.
/// </summary>
public class ExecutorSideEffectClassificationTests
{
    [Fact(DisplayName = "162: every ExecutorType is classified exactly once, as side-effecting or read-only, with a reason")]
    public void EveryExecutorType_IsClassifiedExactlyOnce()
    {
        var unclassified = Enum.GetValues<ExecutorType>()
            .Where(t => !ExecutorSideEffects.SideEffecting.ContainsKey(t) && !ExecutorSideEffects.ReadOnly.ContainsKey(t))
            .ToList();
        var both = ExecutorSideEffects.SideEffecting.Keys.Intersect(ExecutorSideEffects.ReadOnly.Keys).ToList();

        unclassified.Should().BeEmpty(
            "classify a new ExecutorType in ExecutorSideEffects (Services/Ai/Nodes/INodeExecutor.cs) in the same change "
            + "that adds it: does its executor create or update a row, write SPE or an index, enqueue a job, send a "
            + "message, or hand off to an external agent?");
        both.Should().BeEmpty();
        ExecutorSideEffects.SideEffecting.Values.Concat(ExecutorSideEffects.ReadOnly.Values)
            .Should().OnlyContain(reason => !string.IsNullOrWhiteSpace(reason));
    }

    [Theory(DisplayName = "162: the executors that create, update, send, enqueue or delegate are side-effecting")]
    [InlineData(ExecutorType.CreateTask)]
    [InlineData(ExecutorType.SendEmail)]
    [InlineData(ExecutorType.UpdateRecord)]
    [InlineData(ExecutorType.CallWebhook)]
    [InlineData(ExecutorType.SendTeamsMessage)]
    [InlineData(ExecutorType.DeliverToIndex)]
    [InlineData(ExecutorType.CreateNotification)]
    [InlineData(ExecutorType.AgentService)]
    [InlineData(ExecutorType.ObservationEmit)]
    public void WritingExecutors_AreSideEffecting(ExecutorType executorType)
    {
        ExecutorSideEffects.IsSideEffecting(executorType).Should().BeTrue();
    }

    [Fact(DisplayName = "162: a value outside the classification is treated as side-effecting (fail closed)")]
    public void UnknownExecutorType_IsSideEffecting()
    {
        ExecutorSideEffects.IsSideEffecting((ExecutorType)9999).Should().BeTrue();
        ExecutorSideEffects.IsSideEffecting(ExecutorType.AiAnalysis).Should().BeFalse();
    }
}
