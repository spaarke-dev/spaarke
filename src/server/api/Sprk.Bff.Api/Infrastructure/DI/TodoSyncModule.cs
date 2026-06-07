using Sprk.Bff.Api.Services.Todo;
using Sprk.Bff.Api.Services.Todo.NullObject;
using Sprk.Bff.Api.Services.Todo.Placeholder;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for Microsoft Graph <c>/me/todo</c> sync scaffolding (smart-todo-decoupling-r3
/// Phase 6, task 018). Registers four interfaces under the Null-Object Kill-Switch Pattern
/// (ADR-032 P2 quiet) with UNCONDITIONAL bindings — every interface always resolves to a
/// concrete impl, the choice between Null-Object and the (Phase-7) real impl is made by
/// the <c>Spaarke:Graph:TodoSync:Enabled</c> flag at registration time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asymmetric-registration anti-pattern guard</b> (bff-extensions.md §F.1):
/// The <c>AddSingleton&lt;I…&gt;(…)</c> binding calls appear UNCONDITIONALLY — there is no
/// <c>if (enabled) { services.AddSingleton&lt;IFoo, RealFoo&gt;(); }</c> branch. Both the
/// Null-Object impl and the placeholder real impl are pre-registered as concrete singletons,
/// and the interface binding is a factory that resolves to one based on a captured boolean.
/// This pattern guarantees that the DI graph is uniform whether the flag is on or off.
/// </para>
/// <para>
/// <b>Pattern choice per ADR-032 §"Three Patterns"</b>: P2 Quiet Null-Object for all four
/// services. Rationale: each is a side-effecting fire-and-forget service (sync, ensure-list,
/// ensure-subscription, backfill); the steady-state pre-feature behavior IS "no Graph mirror,
/// Dataverse is system-of-record." P3 Fail-fast would falsely signal recovery-required to
/// upstream job handlers. The backfiller logs once on flag-off invocation to preserve operator
/// observability (see <see cref="NullTodoSyncBackfiller"/>).
/// </para>
/// <para>
/// <b>Placeholder real impls</b>: until Phase 7 (tasks 061/062/063/065) lands the real
/// Graph-backed impls, the flag-on branch resolves to placeholders that throw
/// <see cref="NotImplementedException"/>. This makes a misconfigured "flag on without real
/// impl" environment fail loudly at the call site rather than silently no-op.
/// </para>
/// </remarks>
public static class TodoSyncModule
{
    /// <summary>
    /// Registers <see cref="ITodoGraphSyncHandler"/>, <see cref="ISpaarkeListProvisioner"/>,
    /// <see cref="ITodoSubscriptionManager"/>, and <see cref="ITodoSyncBackfiller"/> with
    /// Null-Object fallbacks per the <c>Spaarke:Graph:TodoSync:Enabled</c> feature flag.
    /// </summary>
    public static IServiceCollection AddTodoSync(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("Spaarke:Graph:TodoSync:Enabled");

        // Pre-register BOTH concrete impls as singletons so the interface factory below can
        // resolve either. The Null-Object backfiller takes an ILogger ctor dep; the rest are
        // pure no-ops with no deps.
        services.AddSingleton<NullTodoGraphSyncHandler>();
        services.AddSingleton<NullSpaarkeListProvisioner>();
        services.AddSingleton<NullTodoSubscriptionManager>();
        services.AddSingleton<NullTodoSyncBackfiller>();

        services.AddSingleton<NotImplementedTodoGraphSyncHandler>();
        services.AddSingleton<NotImplementedSpaarkeListProvisioner>();
        services.AddSingleton<NotImplementedTodoSubscriptionManager>();
        services.AddSingleton<NotImplementedTodoSyncBackfiller>();

        // UNCONDITIONAL interface bindings — the factory captures `enabled` and dispatches
        // to the appropriate pre-registered concrete type. This is the canonical ADR-032 P2
        // shape: the binding call ALWAYS executes, only the resolved instance differs.
        //
        // Per bff-extensions.md §F.1, the static-scan recipe greps for the anti-pattern
        // `if (…enabled…) … services.Add…` inside Infrastructure/DI/ and expects zero hits.
        services.AddSingleton<ITodoGraphSyncHandler>(sp => enabled
            ? sp.GetRequiredService<NotImplementedTodoGraphSyncHandler>()
            : sp.GetRequiredService<NullTodoGraphSyncHandler>());

        services.AddSingleton<ISpaarkeListProvisioner>(sp => enabled
            ? sp.GetRequiredService<NotImplementedSpaarkeListProvisioner>()
            : sp.GetRequiredService<NullSpaarkeListProvisioner>());

        services.AddSingleton<ITodoSubscriptionManager>(sp => enabled
            ? sp.GetRequiredService<NotImplementedTodoSubscriptionManager>()
            : sp.GetRequiredService<NullTodoSubscriptionManager>());

        services.AddSingleton<ITodoSyncBackfiller>(sp => enabled
            ? sp.GetRequiredService<NotImplementedTodoSyncBackfiller>()
            : sp.GetRequiredService<NullTodoSyncBackfiller>());

        Console.WriteLine(enabled
            ? "⚠ Todo Graph sync ENABLED (Spaarke:Graph:TodoSync:Enabled=true) — placeholder impls throw NotImplementedException until Phase 7 (tasks 061/062/063/065)"
            : "✓ Todo Graph sync disabled (Spaarke:Graph:TodoSync:Enabled=false) — Null-Object impls registered (ADR-032 P2)");

        return services;
    }
}
