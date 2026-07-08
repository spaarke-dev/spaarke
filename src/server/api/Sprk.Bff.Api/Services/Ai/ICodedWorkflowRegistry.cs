// FR-P0-06 (spaarke-ai-architecture-redesign-r1 task 007) — coded-workflow resolution
// surface, mirroring IToolHandlerRegistry (E-1).

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Registry for resolving <see cref="ICodedWorkflow"/> implementations by class reference —
/// the value a <c>coded</c>-kind Action row's <c>sprk_workflowclass</c> column carries.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IToolHandlerRegistry"/> (the tool-handler discovery pattern) per the
/// E-1 overlay exception: workflows are registered at startup via assembly scan
/// (<see cref="CodedWorkflowExtensions.AddCodedWorkflowsFromAssembly"/>) and indexed by
/// <see cref="ICodedWorkflow.WorkflowClassRef"/>. See ADR-039 (grounded execution / closed
/// catalogs) and ADR-010 (DI minimalism — constructor injection of the discovered set).
/// </remarks>
public interface ICodedWorkflowRegistry
{
    /// <summary>
    /// Resolves a coded workflow by its class reference (case-insensitive).
    /// </summary>
    /// <param name="workflowClassRef">
    /// The class reference (e.g., <c>"DailyBriefingNarrator"</c>) as an Action row's
    /// <c>sprk_workflowclass</c> column would name it.
    /// </param>
    /// <returns>The registered workflow, or null if not found.</returns>
    ICodedWorkflow? GetWorkflow(string workflowClassRef);

    /// <summary>
    /// Gets all registered workflow class references (catalog reconciliation surface —
    /// FR-P0-04's health check compares these against <c>coded</c>-kind Action rows).
    /// </summary>
    IReadOnlyList<string> GetRegisteredWorkflowClassRefs();

    /// <summary>
    /// Checks whether a workflow is registered for the given class reference.
    /// </summary>
    bool IsWorkflowAvailable(string workflowClassRef);

    /// <summary>
    /// Gets the count of registered workflows.
    /// </summary>
    int WorkflowCount { get; }
}
