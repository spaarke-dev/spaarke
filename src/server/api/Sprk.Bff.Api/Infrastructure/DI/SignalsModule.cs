using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Signal/Policy domain (ADR-010: feature module pattern; spec FR-09..FR-16,
/// spaarke-ontology-platform-r1 task 023). Registers the policy-scope resolution building block; the
/// predicate compiler (task 021), Signal writer (task 030) and the nightly/event-triggered evaluators
/// (tasks 031/032) add their own registrations here as they land.
/// </summary>
public static class SignalsModule
{
    public static IServiceCollection AddSignalsModule(this IServiceCollection services)
    {
        // PolicyScopeResolver (spec FR-09, FR-10): scope-match + ordering + fail-closed semantics copied
        // VERBATIM from CommunicationRuleGate. Concrete singleton (ADR-010) — its sole dependency
        // (IGenericEntityService) is already a singleton; no interface introduced (design.md §3.2 defers
        // IPolicyEvaluator until a second consumer exists).
        services.AddSingleton<PolicyScopeResolver>();

        return services;
    }
}
