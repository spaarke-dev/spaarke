using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for ExecutionGraph - node dependency management and execution ordering.
/// Tests topological sort, cycle detection, batch generation, and disabled node handling.
/// </summary>
public class ExecutionGraphTests
{
    // Test node IDs for readability
    private static readonly Guid NodeA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid NodeB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid NodeC = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid NodeD = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid NodeE = Guid.Parse("00000000-0000-0000-0000-000000000005");

    #region Constructor Tests

    [Fact]
    public void Constructor_NullNodes_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ExecutionGraph(null!));
    }

    [Fact]
    public void Constructor_EmptyNodes_CreatesEmptyGraph()
    {
        // Act
        var graph = new ExecutionGraph(Array.Empty<PlaybookNodeDto>());

        // Assert
        graph.NodeCount.Should().Be(0);
        graph.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_SingleNode_CreatesGraphWithOneNode()
    {
        // Arrange
        var nodes = new[] { CreateNode(NodeA, "Node A") };

        // Act
        var graph = new ExecutionGraph(nodes);

        // Assert
        graph.NodeCount.Should().Be(1);
        graph.GetNode(NodeA).Should().NotBeNull();
    }

    [Fact]
    public void Constructor_DisabledNodes_ExcludedFromGraph()
    {
        // Arrange
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", isActive: true),
            CreateNode(NodeB, "Node B", isActive: false),
            CreateNode(NodeC, "Node C", isActive: true)
        };

        // Act
        var graph = new ExecutionGraph(nodes);

        // Assert
        graph.NodeCount.Should().Be(2);
        graph.GetNode(NodeA).Should().NotBeNull();
        graph.GetNode(NodeB).Should().BeNull();
        graph.GetNode(NodeC).Should().NotBeNull();
    }

    #endregion

    #region Topological Sort Tests

    [Fact]
    public void GetTopologicalOrder_EmptyGraph_ReturnsEmptyList()
    {
        // Arrange
        var graph = new ExecutionGraph(Array.Empty<PlaybookNodeDto>());

        // Act
        var order = graph.GetTopologicalOrder();

        // Assert
        order.Should().BeEmpty();
    }

    [Fact]
    public void GetTopologicalOrder_SingleNode_ReturnsThatNode()
    {
        // Arrange
        var nodes = new[] { CreateNode(NodeA, "Node A") };
        var graph = new ExecutionGraph(nodes);

        // Act
        var order = graph.GetTopologicalOrder();

        // Assert
        order.Should().HaveCount(1);
        order[0].Id.Should().Be(NodeA);
    }

    [Fact]
    public void GetTopologicalOrder_LinearChain_ReturnsCorrectOrder()
    {
        // Arrange: A → B → C
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A"),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA]),
            CreateNode(NodeC, "Node C", dependsOn: [NodeB])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var order = graph.GetTopologicalOrder();

        // Assert
        order.Should().HaveCount(3);
        order.Select(n => n.Id).Should().ContainInOrder(NodeA, NodeB, NodeC);
    }

    [Fact]
    public void GetTopologicalOrder_ParallelNodes_ReturnsAllNodes()
    {
        // Arrange: A, B, C with no dependencies
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", executionOrder: 1),
            CreateNode(NodeB, "Node B", executionOrder: 2),
            CreateNode(NodeC, "Node C", executionOrder: 3)
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var order = graph.GetTopologicalOrder();

        // Assert
        order.Should().HaveCount(3);
        // All nodes should be returned (order may vary as they're independent)
        order.Select(n => n.Id).Should().BeEquivalentTo([NodeA, NodeB, NodeC]);
    }

    [Fact]
    public void GetTopologicalOrder_DiamondPattern_ReturnsValidOrder()
    {
        // Arrange: A → B, A → C, B → D, C → D (diamond)
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A"),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA]),
            CreateNode(NodeC, "Node C", dependsOn: [NodeA]),
            CreateNode(NodeD, "Node D", dependsOn: [NodeB, NodeC])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var order = graph.GetTopologicalOrder();

        // Assert
        order.Should().HaveCount(4);
        var orderIds = order.Select(n => n.Id).ToList();

        // A must come before B and C
        orderIds.IndexOf(NodeA).Should().BeLessThan(orderIds.IndexOf(NodeB));
        orderIds.IndexOf(NodeA).Should().BeLessThan(orderIds.IndexOf(NodeC));

        // B and C must come before D
        orderIds.IndexOf(NodeB).Should().BeLessThan(orderIds.IndexOf(NodeD));
        orderIds.IndexOf(NodeC).Should().BeLessThan(orderIds.IndexOf(NodeD));
    }

    #endregion

    #region Cycle Detection Tests

    [Fact]
    public void GetTopologicalOrder_SimpleCycle_ThrowsInvalidOperationException()
    {
        // Arrange: A → B → A (cycle)
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", dependsOn: [NodeB]),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA])
        };
        var graph = new ExecutionGraph(nodes);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => graph.GetTopologicalOrder());
        ex.Message.Should().Contain("circular dependency");
    }

    [Fact]
    public void GetTopologicalOrder_SelfLoop_ThrowsInvalidOperationException()
    {
        // Arrange: A → A (self loop)
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", dependsOn: [NodeA])
        };
        var graph = new ExecutionGraph(nodes);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => graph.GetTopologicalOrder());
        ex.Message.Should().Contain("circular dependency");
    }

    [Fact]
    public void GetTopologicalOrder_ComplexCycle_ThrowsInvalidOperationException()
    {
        // Arrange: A → B → C → A (3-node cycle)
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", dependsOn: [NodeC]),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA]),
            CreateNode(NodeC, "Node C", dependsOn: [NodeB])
        };
        var graph = new ExecutionGraph(nodes);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => graph.GetTopologicalOrder());
    }

    [Fact]
    public void IsValid_NoCycle_ReturnsTrue()
    {
        // Arrange
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A"),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var isValid = graph.IsValid();

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithCycle_ReturnsFalse()
    {
        // Arrange
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", dependsOn: [NodeB]),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var isValid = graph.IsValid();

        // Assert
        isValid.Should().BeFalse();
    }

    #endregion

    #region Execution Batches Tests

    [Fact]
    public void GetExecutionBatches_EmptyGraph_ReturnsEmptyList()
    {
        // Arrange
        var graph = new ExecutionGraph(Array.Empty<PlaybookNodeDto>());

        // Act
        var batches = graph.GetExecutionBatches();

        // Assert
        batches.Should().BeEmpty();
    }

    [Fact]
    public void GetExecutionBatches_SingleNode_ReturnsSingleBatch()
    {
        // Arrange
        var nodes = new[] { CreateNode(NodeA, "Node A") };
        var graph = new ExecutionGraph(nodes);

        // Act
        var batches = graph.GetExecutionBatches();

        // Assert
        batches.Should().HaveCount(1);
        batches[0].Should().HaveCount(1);
        batches[0][0].Id.Should().Be(NodeA);
    }

    [Fact]
    public void GetExecutionBatches_AllParallel_ReturnsSingleBatch()
    {
        // Arrange: A, B, C with no dependencies (all can run in parallel)
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", executionOrder: 1),
            CreateNode(NodeB, "Node B", executionOrder: 2),
            CreateNode(NodeC, "Node C", executionOrder: 3)
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var batches = graph.GetExecutionBatches();

        // Assert
        batches.Should().HaveCount(1);
        batches[0].Should().HaveCount(3);
        // Should be ordered by ExecutionOrder within batch
        batches[0][0].Name.Should().Be("Node A");
        batches[0][1].Name.Should().Be("Node B");
        batches[0][2].Name.Should().Be("Node C");
    }

    [Fact]
    public void GetExecutionBatches_LinearChain_ReturnsSequentialBatches()
    {
        // Arrange: A → B → C (must be sequential)
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A"),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA]),
            CreateNode(NodeC, "Node C", dependsOn: [NodeB])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var batches = graph.GetExecutionBatches();

        // Assert
        batches.Should().HaveCount(3);
        batches[0].Should().HaveCount(1);
        batches[0][0].Id.Should().Be(NodeA);
        batches[1].Should().HaveCount(1);
        batches[1][0].Id.Should().Be(NodeB);
        batches[2].Should().HaveCount(1);
        batches[2][0].Id.Should().Be(NodeC);
    }

    [Fact]
    public void GetExecutionBatches_DiamondPattern_ReturnsCorrectBatches()
    {
        // Arrange: A → B, A → C, B → D, C → D
        // Batch 1: A, Batch 2: B,C (parallel), Batch 3: D
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", executionOrder: 1),
            CreateNode(NodeB, "Node B", executionOrder: 2, dependsOn: [NodeA]),
            CreateNode(NodeC, "Node C", executionOrder: 3, dependsOn: [NodeA]),
            CreateNode(NodeD, "Node D", executionOrder: 4, dependsOn: [NodeB, NodeC])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var batches = graph.GetExecutionBatches();

        // Assert
        batches.Should().HaveCount(3);

        // Batch 1: A
        batches[0].Should().HaveCount(1);
        batches[0][0].Id.Should().Be(NodeA);

        // Batch 2: B and C (can run in parallel)
        batches[1].Should().HaveCount(2);
        batches[1].Select(n => n.Id).Should().BeEquivalentTo([NodeB, NodeC]);

        // Batch 3: D
        batches[2].Should().HaveCount(1);
        batches[2][0].Id.Should().Be(NodeD);
    }

    [Fact]
    public void GetExecutionBatches_Cycle_ThrowsInvalidOperationException()
    {
        // Arrange
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", dependsOn: [NodeB]),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA])
        };
        var graph = new ExecutionGraph(nodes);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => graph.GetExecutionBatches());
    }

    #endregion

    #region Disabled Node Tests

    [Fact]
    public void GetExecutionBatches_DisabledNodeInDependency_ExcludesDependency()
    {
        // Arrange: A (disabled) → B → C
        // B should have no dependencies since A is disabled
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", isActive: false),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA]),
            CreateNode(NodeC, "Node C", dependsOn: [NodeB])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var batches = graph.GetExecutionBatches();

        // Assert
        batches.Should().HaveCount(2);
        // B has no dependencies (A is disabled), so B is in first batch
        batches[0][0].Id.Should().Be(NodeB);
        batches[1][0].Id.Should().Be(NodeC);
    }

    [Fact]
    public void GetExecutionBatches_DisabledMiddleNode_SkipsNode()
    {
        // Arrange: A → B (disabled) → C
        // C should be able to run if B is disabled (dependency ignored)
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", executionOrder: 1),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA], isActive: false, executionOrder: 2),
            CreateNode(NodeC, "Node C", dependsOn: [NodeB], executionOrder: 3)
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var batches = graph.GetExecutionBatches();

        // Assert
        graph.NodeCount.Should().Be(2);
        batches.Should().HaveCount(1);
        // A and C are both in first batch (C's dependency on B is ignored as B is disabled)
        batches[0].Select(n => n.Id).Should().BeEquivalentTo([NodeA, NodeC]);
    }

    #endregion

    #region Dependency Query Tests

    [Fact]
    public void GetDependencies_NodeWithDependencies_ReturnsDependencies()
    {
        // Arrange
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A"),
            CreateNode(NodeB, "Node B"),
            CreateNode(NodeC, "Node C", dependsOn: [NodeA, NodeB])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var deps = graph.GetDependencies(NodeC);

        // Assert
        deps.Should().BeEquivalentTo([NodeA, NodeB]);
    }

    [Fact]
    public void GetDependencies_NodeWithNoDependencies_ReturnsEmpty()
    {
        // Arrange
        var nodes = new[] { CreateNode(NodeA, "Node A") };
        var graph = new ExecutionGraph(nodes);

        // Act
        var deps = graph.GetDependencies(NodeA);

        // Assert
        deps.Should().BeEmpty();
    }

    [Fact]
    public void GetDependencies_NonExistentNode_ReturnsEmpty()
    {
        // Arrange
        var graph = new ExecutionGraph(Array.Empty<PlaybookNodeDto>());

        // Act
        var deps = graph.GetDependencies(NodeA);

        // Assert
        deps.Should().BeEmpty();
    }

    [Fact]
    public void GetDependents_NodeWithDependents_ReturnsDependents()
    {
        // Arrange
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A"),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA]),
            CreateNode(NodeC, "Node C", dependsOn: [NodeA])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var deps = graph.GetDependents(NodeA);

        // Assert
        deps.Should().BeEquivalentTo([NodeB, NodeC]);
    }

    [Fact]
    public void GetDependents_NodeWithNoDependents_ReturnsEmpty()
    {
        // Arrange
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A"),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA])
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var deps = graph.GetDependents(NodeB);

        // Assert
        deps.Should().BeEmpty();
    }

    #endregion

    #region Complex Scenario Tests

    [Fact]
    public void GetExecutionBatches_ComplexGraph_ReturnsCorrectBatches()
    {
        // Arrange: Complex graph
        //     A
        //    / \
        //   B   C
        //    \ / \
        //     D   E
        var nodes = new[]
        {
            CreateNode(NodeA, "Node A", executionOrder: 1),
            CreateNode(NodeB, "Node B", dependsOn: [NodeA], executionOrder: 2),
            CreateNode(NodeC, "Node C", dependsOn: [NodeA], executionOrder: 3),
            CreateNode(NodeD, "Node D", dependsOn: [NodeB, NodeC], executionOrder: 4),
            CreateNode(NodeE, "Node E", dependsOn: [NodeC], executionOrder: 5)
        };
        var graph = new ExecutionGraph(nodes);

        // Act
        var batches = graph.GetExecutionBatches();

        // Assert
        batches.Should().HaveCount(3);

        // Batch 1: A
        batches[0].Should().HaveCount(1);
        batches[0][0].Id.Should().Be(NodeA);

        // Batch 2: B, C (parallel)
        batches[1].Should().HaveCount(2);
        batches[1].Select(n => n.Id).Should().BeEquivalentTo([NodeB, NodeC]);

        // Batch 3: D, E (both ready after B,C complete)
        batches[2].Should().HaveCount(2);
        batches[2].Select(n => n.Id).Should().BeEquivalentTo([NodeD, NodeE]);
    }

    #endregion

    #region Helper Methods

    private static PlaybookNodeDto CreateNode(
        Guid id,
        string name,
        Guid[]? dependsOn = null,
        bool isActive = true,
        int executionOrder = 0)
    {
        return new PlaybookNodeDto
        {
            Id = id,
            PlaybookId = Guid.NewGuid(),
            ActionId = Guid.NewGuid(),
            Name = name,
            ExecutionOrder = executionOrder,
            DependsOn = dependsOn ?? [],
            OutputVariable = name.Replace(" ", "").ToLower(),
            IsActive = isActive,
            CreatedOn = DateTime.UtcNow,
            ModifiedOn = DateTime.UtcNow
        };
    }

    #endregion
}
