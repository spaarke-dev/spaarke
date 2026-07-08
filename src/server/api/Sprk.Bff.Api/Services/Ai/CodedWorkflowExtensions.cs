// FR-P0-06 (spaarke-ai-architecture-redesign-r1 task 007) — assembly-scan discovery for
// ICodedWorkflow implementations, mirroring ToolFrameworkExtensions (E-1).

using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// DI extension methods for registering coded workflows (ADR-010 feature module pattern).
/// Mirrors <see cref="ToolFrameworkExtensions"/> — the tool-handler discovery pattern —
/// per the E-1 overlay exception ruling.
/// </summary>
public static class CodedWorkflowExtensions
{
    /// <summary>
    /// Discovers and registers all <see cref="ICodedWorkflow"/> implementations from the
    /// executing assembly, plus the <see cref="ICodedWorkflowRegistry"/> that resolves
    /// them by class reference (an Action row's <c>sprk_workflowclass</c> value).
    /// </summary>
    public static IServiceCollection AddCodedWorkflows(this IServiceCollection services) =>
        services.AddCodedWorkflowsFromAssembly(Assembly.GetExecutingAssembly());

    /// <summary>
    /// Discovers and registers all <see cref="ICodedWorkflow"/> implementations from an
    /// assembly (mirrors <see cref="ToolFrameworkExtensions.AddToolHandlersFromAssembly"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Null-Object exclusion (ADR-032)</b>: kill-switch subclasses (e.g.,
    /// <c>NullDailyBriefingNarrator : DailyBriefingNarrator</c>) also satisfy
    /// <c>IsAssignableFrom</c> but carry the SAME workflow identity as their base — they are
    /// DI swap-ins for the compound-AI-OFF path, not distinct workflows. The scan therefore
    /// registers only base-most implementers (types whose base class does NOT itself
    /// implement <see cref="ICodedWorkflow"/>).
    /// </para>
    /// <para>
    /// <b>Gate-aware resolution</b>: each workflow is bound to <see cref="ICodedWorkflow"/>
    /// via a factory that defers to the concrete type's own registration. The owning DI
    /// module (e.g., <c>AnalysisServicesModule</c>) keeps full authority over lifetime and
    /// kill-switch behavior: when the compound AI gate is OFF, the module registers the
    /// Null-Object peer under the concrete type, and THAT is what class-ref resolution
    /// returns — the ADR-032 <c>FeatureDisabledException</c> contract flows through the
    /// convention unchanged. <see cref="ServiceCollectionDescriptorExtensions.TryAddTransient(IServiceCollection, Type)"/>
    /// is only a backstop for future workflows whose module adds no explicit registration.
    /// </para>
    /// <para>
    /// This method must be called AFTER the modules that register the concrete workflow
    /// types (so the TryAdd backstop never shadows a gate-aware registration).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCodedWorkflowsFromAssembly(
        this IServiceCollection services,
        Assembly assembly)
    {
        var workflowInterface = typeof(ICodedWorkflow);

        var workflowTypes = assembly.GetTypes()
            .Where(t => t.IsClass
                && !t.IsAbstract
                && workflowInterface.IsAssignableFrom(t)
                // ADR-032: skip Null-Object subclasses — they inherit their base's
                // workflow identity and are swapped in by the owning module's DI, not
                // registered as separate workflows.
                && !(t.BaseType is not null && workflowInterface.IsAssignableFrom(t.BaseType)))
            .ToList();

        foreach (var workflowType in workflowTypes)
        {
            // Backstop only: modules that gate-register the concrete type (real class ON /
            // Null peer OFF) win — TryAdd never overrides an existing registration.
            // Transient is the convention default (matches DailyBriefingNarrator's
            // typed-HttpClient-safe lifetime); modules override by registering first.
            services.TryAddTransient(workflowType);

            // Enumeration binding for the registry (mirrors AddToolHandlersFromAssembly's
            // AddScoped(handlerInterface, handlerType)); the factory defers to the concrete
            // type's registration so gate-aware swaps flow through.
            services.AddTransient(
                workflowInterface,
                sp => (ICodedWorkflow)sp.GetRequiredService(workflowType));
        }

        // Scoped to match the widest workflow lifetime (collector is Scoped); mirrors
        // IToolHandlerRegistry's scoped registration.
        services.TryAddScoped<ICodedWorkflowRegistry, CodedWorkflowRegistry>();

        return services;
    }
}
