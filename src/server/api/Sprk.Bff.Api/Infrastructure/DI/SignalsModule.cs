using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Signal/Policy domain (ADR-010: feature module pattern; spec FR-09..FR-16,
/// spaarke-ontology-platform-r1 task 023). Registers the policy-scope resolution building block, the
/// predicate compiler (task 021), the fail-closed policy-version validator (task 022) and the Signal writer
/// (task 030). The nightly/event-triggered evaluators (tasks 031/032) add their own registrations here as they
/// land.
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

        // PolicyVersionValidator (task 022, spec FR-08): the fail-closed save/evaluation-time gate on top of
        // the two registrations above -- the single seam any future sprk_policyversion save path AND the
        // evaluator (task 031) both call so an invalid rule body is refused with one shared EventId/reason
        // vocabulary rather than two independently hand-rolled checks.
        services.AddSingleton<PolicyVersionValidator>();

        // OntologyWriterDataverseClient (task 030; rework F10 — ADR-010 Path C): registered as the CONCRETE
        // type (public sealed, no interface of this project's own) — see its own XML doc for why that is safe
        // for testability (the typed seam is the SDK's own IOrganizationServiceAsync2, not a throwaway
        // interface). Its credential resolution (and therefore OntologyWriterCredentialFactory's fail-closed
        // throw) is deferred behind its own Lazy<IOrganizationServiceAsync2> — NOT performed in this factory
        // delegate. ValidateOnBuild resolves every singleton once at app startup; doing the credential lookup
        // here would crash Build() in every environment that has not yet set
        // Ontology:Writer:ManagedIdentityClientId (today, every environment except a deployed spaarke-bff-dev
        // with the app setting added).
        services.AddSingleton(sp => new OntologyWriterDataverseClient(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<OntologyWriterDataverseClient>>()));

        // SignalWriter (task 030; spec FR-03/FR-14/NFR-08): writes sprk_signal via the dedicated client above,
        // plus the SHARED sysadmin IGenericEntityService (GraphModule.AddGraphModule) for the one read that is
        // metadata, not a Signal write — the grouping matter's owningbusinessunit (F25). Two DIFFERENT
        // Dataverse connections, injected as two DIFFERENT types, so neither can be swapped for the other by
        // accident. Task 039 (D-33): also uac-r2's IRecordOwnershipResolver (singleton, registered unconditionally by
        // AddRecordOwnershipResolver) — the one owner of invariant I-6, reused, never re-derived here.
        services.AddSingleton<SignalWriter>();

        // Decision plan read (task 036, FR-49/FR-50): the Signal-level access decision (as the caller, through the
        // caller-identity client, so scoped) and the plan resolver (the catalog itself is a static table). Both
        // unconditional -- the route that uses them is mapped unconditionally (bff-extensions.md F.1).
        services.AddScoped<SignalCoreRecordAccess>();
        services.AddScoped<DecisionPlanService>();

        // Decision action executors (task 044, FR-52): one per catalog action the commit route (task 043) calls, except the
        // inquiry (070) and Assign Work (046). Internal services, not routes; scoped because every write is the request
        // caller's. DecisionRouteCores adapts the shipped event, child-record and communications cores. Unconditional:
        // nothing here is feature-gated (bff-extensions.md F.1).
        services.AddScoped<DecisionRouteCores>();
        services.AddScoped<IDecisionActionExecutor, ReviseBudgetExecutor>();
        services.AddScoped<IDecisionActionExecutor, ApproveVarianceExecutor>();
        services.AddScoped<IDecisionActionExecutor, MarkCompleteExecutor>();
        services.AddScoped<IDecisionActionExecutor, RescheduleExecutor>();
        services.AddScoped<IDecisionActionExecutor, ReassignExecutor>();
        services.AddScoped<IDecisionActionExecutor, SendReminderExecutor>();
        services.AddScoped<IDecisionActionExecutor, ExtendResponseDateExecutor>();
        services.AddScoped<IDecisionActionExecutor, RecordTheResponseExecutor>();
        services.AddScoped<IDecisionActionExecutor, AddTodoExecutor>();
        services.AddScoped<IDecisionActionExecutor, CreateEventExecutor>();
        services.AddScoped<IDecisionActionExecutor, SendEmailExecutor>();
        services.AddScoped<DecisionActionExecutors>();

        // RuleBodyDescriber (task 026, FR-48): the read-side plain-language description of a rule body. Stateless over the
        // compiler and the shared sysadmin entity service, both singletons.
        services.AddSingleton<RuleBodyDescriber>();

        return services;
    }
}
