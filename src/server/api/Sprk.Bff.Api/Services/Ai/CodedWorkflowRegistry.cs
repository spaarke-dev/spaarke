// FR-P0-06 (spaarke-ai-architecture-redesign-r1 task 007) — coded-workflow registry
// implementation, mirroring ToolHandlerRegistry (E-1).

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Registry implementation that indexes discovered <see cref="ICodedWorkflow"/> instances
/// by <see cref="ICodedWorkflow.WorkflowClassRef"/> for resolution from Action rows.
/// </summary>
/// <remarks>
/// <para>
/// Discovery flow (mirrors <see cref="ToolHandlerRegistry"/>):
/// </para>
/// <list type="number">
/// <item>At startup, <see cref="CodedWorkflowExtensions.AddCodedWorkflowsFromAssembly"/>
/// scans for <see cref="ICodedWorkflow"/> implementations and binds each to the interface
/// via a factory that defers to the concrete type's own registration (so ADR-032
/// Null-Object kill-switch swaps flow through unchanged).</item>
/// <item>The registry receives the discovered set via constructor injection
/// (<c>IEnumerable&lt;ICodedWorkflow&gt;</c>) and indexes by class reference.</item>
/// <item>Executors resolve by the Action row's <c>sprk_workflowclass</c> value via
/// <see cref="GetWorkflow"/>.</item>
/// </list>
/// <para>Follows ADR-010 DI minimalism (constructor injection; no service locator).</para>
/// </remarks>
public sealed class CodedWorkflowRegistry : ICodedWorkflowRegistry
{
    private readonly Dictionary<string, ICodedWorkflow> _workflows;
    private readonly ILogger<CodedWorkflowRegistry> _logger;

    public CodedWorkflowRegistry(
        IEnumerable<ICodedWorkflow> workflows,
        ILogger<CodedWorkflowRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workflows = new Dictionary<string, ICodedWorkflow>(StringComparer.OrdinalIgnoreCase);

        foreach (var workflow in workflows)
        {
            var classRef = workflow.WorkflowClassRef;
            if (string.IsNullOrWhiteSpace(classRef))
            {
                _logger.LogWarning(
                    "Skipping coded workflow {Type} with empty WorkflowClassRef",
                    workflow.GetType().Name);
                continue;
            }

            if (!_workflows.TryAdd(classRef, workflow))
            {
                _logger.LogWarning(
                    "Duplicate WorkflowClassRef {WorkflowClassRef} — workflow {ExistingType} already registered, skipping {NewType}",
                    classRef,
                    _workflows[classRef].GetType().Name,
                    workflow.GetType().Name);
            }
        }

        _logger.LogInformation(
            "Coded workflow registration complete: {Count} workflow(s) — {ClassRefs}",
            _workflows.Count,
            string.Join(", ", _workflows.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)));
    }

    /// <inheritdoc />
    public ICodedWorkflow? GetWorkflow(string workflowClassRef)
    {
        if (string.IsNullOrWhiteSpace(workflowClassRef))
            return null;

        if (_workflows.TryGetValue(workflowClassRef, out var workflow))
            return workflow;

        _logger.LogWarning(
            "Coded workflow {WorkflowClassRef} not found in registry", workflowClassRef);
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredWorkflowClassRefs() => _workflows.Keys.ToList();

    /// <inheritdoc />
    public bool IsWorkflowAvailable(string workflowClassRef) =>
        !string.IsNullOrWhiteSpace(workflowClassRef) && _workflows.ContainsKey(workflowClassRef);

    /// <inheritdoc />
    public int WorkflowCount => _workflows.Count;
}
