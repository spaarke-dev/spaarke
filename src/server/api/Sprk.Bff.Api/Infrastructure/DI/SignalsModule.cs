using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // PredicateCompiler (spec FR-06, FR-07; task 021): Existence rule body -> ONE FetchXML query. Pure and
        // stateless; validates through RuleBodySchemaValidator (task 020's seam, registered here so the compiler
        // and the task 022 save-time refusal share one instance) and resolves relative dates via TimeProvider.
        // Both registrations are unconditional — no feature flag gates this module (bff-extensions.md §F.1).
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RuleBodySchemaValidator>();
        services.AddSingleton<PredicateCompiler>();

        return services;
    }
}
