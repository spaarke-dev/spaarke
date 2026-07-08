// FR-P0-06 (spaarke-ai-architecture-redesign-r1 task 007) — ICodedWorkflow registration
// convention. Canonical design: SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md §4.2 +
// overlay exception E-1 ("one interface + assembly scan, mirroring the tool-handler
// discovery pattern").

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Contract for a coded workflow — a C# class that fulfils a <c>coded</c>-kind Action row
/// (ADR-039 grounded execution: composites are coded workflows named by the closed Action
/// catalog, never maker-authored graphs on the frozen engine).
/// </summary>
/// <remarks>
/// <para>
/// <b>Discovery (E-1)</b>: implementations are discovered by assembly scan
/// (<see cref="CodedWorkflowExtensions.AddCodedWorkflowsFromAssembly"/>) mirroring the
/// tool-handler pattern (<see cref="ToolFrameworkExtensions.AddToolHandlersFromAssembly"/>),
/// and resolved by class reference via <see cref="ICodedWorkflowRegistry.GetWorkflow"/>.
/// The class reference is the value a <c>coded</c>-kind Action row's
/// <c>sprk_workflowclass</c> column carries (FR-P0-03).
/// </para>
/// <para>
/// <b>Class-ref convention</b>: <see cref="WorkflowClassRef"/> returns the simple class
/// name of the implementing type (e.g., <c>nameof(Narrators.DailyBriefingNarrator)</c>).
/// Null-Object kill-switch subclasses (ADR-032) inherit their base's class ref — they are
/// the SAME workflow identity, swapped in by DI when the compound AI gate is OFF. The
/// assembly scan therefore registers only base-most implementers; whether the real class
/// or its Null peer answers a resolution is decided by the owning DI module's registration
/// of the concrete type.
/// </para>
/// <para>
/// <b>ADR-013 binding</b>: this contract is AI-internal — it is NOT surfaced through
/// <c>Services/Ai/PublicContracts/</c>. CRUD-side callers never resolve workflows directly.
/// </para>
/// <para>
/// First instances: <see cref="Narrators.DailyBriefingNarrator"/> and
/// <see cref="Narrators.DailyBriefingCollector"/> (the Wave-11 coded-workflow shape).
/// FR-P3-04 promotes Daily Briefing to the first full coded composite Action on top of
/// this convention.
/// </para>
/// </remarks>
public interface ICodedWorkflow
{
    /// <summary>
    /// Stable class reference an Action row's <c>sprk_workflowclass</c> column names this
    /// workflow by. Convention: the simple class name of the implementing type.
    /// </summary>
    string WorkflowClassRef { get; }

    /// <summary>
    /// Execute the workflow. <paramref name="context"/> carries the typed arguments
    /// (JSON per the Action row's <c>sprk_inputschema</c>) plus the acting-user identity
    /// for workflows that query Dataverse on the user's behalf.
    /// </summary>
    /// <param name="context">Invocation context (arguments + acting user).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The workflow's result object (serializable response DTO).</returns>
    Task<object?> ExecuteAsync(CodedWorkflowContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Per-invocation context for an <see cref="ICodedWorkflow"/> execution.
/// </summary>
/// <remarks>
/// Deliberately minimal (CLAUDE.md §11 — every field must earn its place): the two fields
/// cover the first instances' needs — JSON arguments (narrator) and the acting user
/// (collector; user-OBO is mandatory for all Dataverse access per the redesign spec).
/// The executor task (FR-P3-04) extends this record if and when a concrete workflow
/// demonstrates the need.
/// </remarks>
public sealed record CodedWorkflowContext
{
    /// <summary>
    /// JSON arguments for the workflow, shaped per the Action row's <c>sprk_inputschema</c>.
    /// <c>"{}"</c> when the workflow takes no arguments.
    /// </summary>
    public string ArgumentsJson { get; init; } = "{}";

    /// <summary>
    /// Acting user (Dataverse <c>systemuserid</c>) for workflows that query on the user's
    /// behalf. Null when the workflow is not user-scoped.
    /// </summary>
    public Guid? UserId { get; init; }
}
