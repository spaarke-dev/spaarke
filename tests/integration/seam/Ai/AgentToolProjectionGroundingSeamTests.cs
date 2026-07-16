using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Seam.Ai;

/// <summary>
/// FR-H1 grounding-predicate vertical-slice seam (assistant-enhancements-r1 task 044). Exercises the
/// convergence the grounding guard threads end-to-end with PRODUCTION types:
/// <c>ChatHostContext.IsValid()</c> (the attached-record fact the factory derives) →
/// <c>AgentToolFilterContext.HasAttachedRecord</c> → the deterministic
/// <see cref="AgentToolProjection.Finalize"/> pre-filter → the capability is (or is not) in the projected
/// tool block the model sees. This is the ADR-043 seam DoD for the dispatch-spine projection change: a
/// pure predicate removes "Create matter" when the session is hosted on a record, and keeps it otherwise —
/// no model call, no tool-name list (ADR-039 §3.2 removes-the-impossible).
/// </summary>
/// <remarks>
/// Production types: <see cref="ChatHostContext"/>, <see cref="AgentToolProjection"/>,
/// <see cref="BindingCapabilityTool"/>, <see cref="AgentTurnContract"/>. The <c>HasAttachedRecord</c>
/// derivation is the SAME expression <c>SprkChatAgentFactory</c> uses (<c>hostContext?.IsValid() == true</c>),
/// so the slice proves the host-record → projection-exclusion chain, not a re-implementation of the rule.
/// </remarks>
public class AgentToolProjectionGroundingSeamTests
{
    private const string MatterId = "33333333-3333-3333-3333-333333333333";

    // A valid host record (the user is inside a matter) — IsValid() is true (known type + id present).
    private static ChatHostContext MatterHost() => new("matter", MatterId, "Acme Corp v. Beta LLC");

    private static BindingCapabilityTool Capability(string consumerType, bool requiresNoAttachedRecord) =>
        new(
            new Binding
            {
                BindingId = Guid.NewGuid(),
                ConsumerType = consumerType,
                ActionId = Guid.NewGuid(),
                ToolDescription = "Maker-authored intent description.",
                RequiresNoAttachedRecord = requiresNoAttachedRecord,
            },
            new ServiceCollection().BuildServiceProvider(),
            "tenant-seam",
            "session-seam",
            NullLogger.Instance);

    private static AgentToolFilterContext FilterFor(ChatHostContext? host) =>
        new(
            AgentToolFilterContext.AssistantSurface,
            HasSessionFiles: false,
            HasActiveDocument: false,
            HasAnalysisBinding: false,
            // The EXACT expression SprkChatAgentFactory threads (task 044).
            HasAttachedRecord: host?.IsValid() == true);

    [Fact]
    public void Projection_CreateMatterInsideAMatter_IsRemovedFromTheToolBlock()
    {
        var createMatter = Capability("create-matter", requiresNoAttachedRecord: true);
        var createTodo = Capability("create-todo", requiresNoAttachedRecord: false);
        var handler = AIFunctionFactory.Create(() => "x", "web_search");

        // Hosted ON a matter → HasAttachedRecord true → the grounding predicate fires.
        var finalized = AgentToolProjection.Finalize(
            new AIFunction[] { createMatter, createTodo, handler },
            FilterFor(MatterHost()),
            new AgentTurnContract(toolCallBudget: 8),
            citations: null,
            NullLogger.Instance);

        var names = finalized.Select(t => t.Name).ToList();
        names.Should().NotContain(createMatter.Name,
            "requires-no-attached-record + an attached matter removes the capability — 'Create matter' is hidden inside a matter");
        names.Should().Contain(createTodo.Name,
            "create-todo has no such precondition (it is regarding-based) — it survives inside a record");
        names.Should().Contain("web_search", "handler tools are never grounding-filtered by this predicate");
    }

    [Fact]
    public void Projection_CreateMatterWithNoHostRecord_IsOfferedInTheToolBlock()
    {
        var createMatter = Capability("create-matter", requiresNoAttachedRecord: true);

        // No host record (top-level assistant) → HasAttachedRecord false → precondition met → offered.
        var finalized = AgentToolProjection.Finalize(
            new AIFunction[] { createMatter },
            FilterFor(host: null),
            new AgentTurnContract(toolCallBudget: 8),
            citations: null,
            NullLogger.Instance);

        finalized.Select(t => t.Name).Should().Contain(createMatter.Name,
            "with no attached record the precondition is met — 'Create matter' is offered");
    }
}
