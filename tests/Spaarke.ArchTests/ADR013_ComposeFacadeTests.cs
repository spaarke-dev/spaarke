using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-013 / NFR-05 (spaarkeai-compose-r2 task 025): Tier-1 facade guard scoped to
/// <c>Sprk.Bff.Api.Services.Compose</c>. The Phase 2 LLM Editing Pattern services
/// (<c>IComposeEditValidator</c>, <c>ComposeEditBatch</c>, <c>ComposeEditTransaction</c>,
/// <c>SemanticAppendixGenerator</c>, <c>CriticMarkupRenderer</c>, <c>ComposeService</c>, ...)
/// are pure deterministic text-transform logic per design.md §6.1 — the LLM proposes edits
/// out-of-band (via the shipped session-dispatch seam), and Compose only resolves/applies spans
/// against text it already has. None of these types may reach the model or the
/// capability-invocation router directly.
/// </summary>
/// <remarks>
/// This is narrower than <see cref="ADR013_AiBoundaryTests"/> (which guards the whole assembly
/// against <c>IOpenAiClient</c>/<c>IPlaybookService</c> with a grandfathered-exception list for
/// the AI API surface itself). <c>Services/Compose/</c> has ZERO legitimate exceptions — it is
/// the reference example of a facade-compliant consumer per the Compose project's own doc
/// comments (see <c>IComposeEditValidator.cs</c>, <c>ComposeEditTransaction.cs</c>,
/// <c>ComposeEditBatch.cs</c> remarks, all of which cite this test by task number). Without this
/// guard, a future edit could inject <c>IOpenAiClient</c>, a <c>Services/Ai/Nodes</c> executor, or
/// <c>IConsumerRoutingService</c> into a Compose service and pass CI, silently reintroducing an
/// AI dependency into code the design requires to stay deterministic and model-free.
/// See:
///   - .claude/adr/ADR-013-ai-architecture.md (Tier-1 facade discipline)
///   - projects/spaarkeai-compose-r2/spec.md NFR-05
///   - projects/spaarkeai-compose-r2/tasks/025-llm-services-tests.poml
/// </remarks>
public class ADR013_ComposeFacadeTests
{
    /// <summary>
    /// AI-internal types/namespaces the Compose facade must never reference. Namespace prefixes
    /// use NetArchTest's dependency-substring matching (same mechanism as
    /// <see cref="ADR013_AiBoundaryTests.ForbiddenAiInternalTypes"/>): a prefix here forbids
    /// dependency on that type OR any type nested/declared under that namespace.
    /// </summary>
    private static readonly string[] ForbiddenAiInternalTypesAndNamespaces =
    {
        // The direct model-call facade — Compose never talks to the model.
        "Sprk.Bff.Api.Services.Ai.IOpenAiClient",

        // The capability-invocation router (2026-07-05 ADR-013 amendment canonical verb) —
        // Compose dispatches through the session-dispatch seam, never the router directly.
        "Sprk.Bff.Api.Services.Ai.PublicContracts.IConsumerRoutingService",

        // All playbook-node executor types (AiCompletionNodeExecutor, StartNodeExecutor, ...)
        // live under this namespace; Compose has no business invoking a node executor.
        "Sprk.Bff.Api.Services.Ai.Nodes.",

        // The chat-turn resume executor is declared outside Services/Ai/Nodes/ — call out
        // explicitly since it also satisfies "executor types" per the task/ADR wording.
        "Sprk.Bff.Api.Services.Ai.Chat.TypedHandlerResumeExecutor",
    };

    [Fact(DisplayName = "ADR-013/NFR-05: Services/Compose/ must not depend on IOpenAiClient, a Nodes executor, or IConsumerRoutingService")]
    public void ComposeServicesMustNotDependOnAiInternals()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act — pure assembly inspection (deterministic; no source scans, no ceilings)
        var result = Types.InAssembly(assembly)
            .That().ResideInNamespace("Sprk.Bff.Api.Services.Compose")
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenAiInternalTypesAndNamespaces)
            .GetResult();

        var violations = (result.FailingTypeNames ?? new List<string>())
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToList();

        // Assert
        Assert.True(
            violations.Count == 0,
            "ADR-013 / NFR-05 violation: a Sprk.Bff.Api.Services.Compose type depends on " +
            "IOpenAiClient, a Services/Ai/Nodes executor, or IConsumerRoutingService. Compose " +
            "services are pure deterministic text-transform logic (design.md §6.1) and MUST NOT " +
            "reach the model or the capability-invocation router directly — the LLM proposes " +
            "edits out-of-band via the shipped session-dispatch seam. If this is a legitimate new " +
            "requirement, follow CLAUDE.md §6.5 (path A: documented exception, or path B: ADR " +
            "amendment) before adding an exception here. " +
            $"Violating types: {string.Join(", ", violations)}");
    }
}
