using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Ontology;
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
        // plus the SHARED sysadmin IGenericEntityService (GraphModule.AddGraphModule) for the reads that are not a
        // Signal write (the subject, its date, the catalog). Two DIFFERENT Dataverse connections, injected as two
        // DIFFERENT types, so neither can be swapped for the other by accident. Task 039 / 143 (D-33, D-130): every
        // Signal's owner is uac-r2's IRecordOwnershipResolver answer (singleton, registered unconditionally by
        // AddRecordOwnershipResolver), applied as returned. Task 143 also hands it the IServiceScopeFactory, through which
        // it resolves two SCOPED services already registered unconditionally by AddExternalAccess, per call:
        // AssignedAccessStore (the D-134 assignee read) and SecureChildReconciler (the no-core re-own). No new registration.
        services.AddSingleton<SignalWriter>();

        // DecisionRecordWriter (task 040, FR-18..FR-22): the one append-only Decision Record per review, created through the same
        // dedicated writer client and uac-r2's IRecordOwnershipResolver. Called last by the commit route (task 043). Task 142: also
        // the CoreAncestorResolver and ICommunicationDataverseService SignalWriter already takes (filing under the subject through the
        // shared regarding builder, then the core-ancestor stamp) -- no new registration.
        services.AddSingleton<DecisionRecordWriter>();

        // GateDecisionRecordSharing (task 042b, D-169): the gate Decision Record of an UNFILED communication is shared Read with the
        // communication's internal sharees (at write, and on each later grant of the communication), and re-owned to the resolver's team
        // when any re-file of the communication runs SecureChildReconciler.AfterRefileAsync. Registered unconditionally, singleton over
        // singletons (the shared sysadmin reader, the POA seam, the writer client above, the resolver, the cache invalidator), so the
        // scoped SecureChildReconciler and the singleton DirectThreadAccessService take it as a REQUIRED dependency with no captive
        // scope (GateDecisionRecordSharingRegistrationTests). DecisionRecordWriter takes it as a REQUIRED last parameter too. CLAUDE.md section 11: Existing: the writer (create-only, guarded), uac-r2's
        // DirectThreadAccessService and SecureChildReconciler.ReownOrdinaryRowAsync (refuses a user-owned row). Extension: the writer cannot
        // assign without breaking its create-only guard; the other two are uac-r2's. Cost of doing nothing: D-169 fails (owner-only record).
        services.AddSingleton<GateDecisionRecordSharing>();

        // Decision plan read (task 036, FR-49/FR-50): the Signal-level access decision (as the caller, through the
        // caller-identity client, so scoped) and the plan resolver (the catalog itself is a static table). Both
        // unconditional -- the route that uses them is mapped unconditionally (bff-extensions.md F.1).
        services.AddScoped<SignalCoreRecordAccess>();
        services.AddScoped<DecisionPlanService>();

        // Signal read route (task 038, FR-54): the worklist's only Signal reader. Scoped (every read is the request caller's
        // through the caller-identity client); unconditional, like the route that uses it (bff-extensions.md F.1).
        services.AddScoped<SignalReadService>();
        // D-176 (task 157): the earlier-invoice readability check of a billing Duplicate, as the caller (the caller's client is passed
        // in). Shared with ebilling task 119's invoice-review read; scoped like SignalReadService, which takes it.
        services.AddScoped<EarlierInvoiceReadability>();

        // Decision action executors (task 044, FR-52): one per catalog action the commit route (task 043) calls, except the
        // inquiry (070) and Assign Work (046). Internal services, not routes; scoped because every write is the request
        // caller's. DecisionRouteCores adapts the shipped event, child-record and communications cores. Unconditional:
        // nothing here is feature-gated (bff-extensions.md F.1).
        services.AddScoped<DecisionRouteCores>();
        services.AddScoped<IDecisionActionExecutor, ReviseBudgetExecutor>();
        services.AddScoped<IDecisionActionExecutor, CreateBudgetExecutor>();
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
        // Task 043: the last two catalog actions (Assign Work through the 046 create; the inquiry adapting 070).
        services.AddScoped<IDecisionActionExecutor, AssignWorkExecutor>();
        // Task 159 (D-178): Assign Task, the no-budget decision's action (a sprk_event of type Task) through the same child-records create.
        services.AddScoped<IDecisionActionExecutor, AssignTaskExecutor>();
        services.AddScoped<IDecisionActionExecutor, BudgetInquiryActionExecutor>();
        services.AddScoped<DecisionActionExecutors>();
        // Task 043: the decision commit route's service and its after-record step (032 trigger, 034 suppression).
        services.AddScoped<DecisionCommitFollowUps>();
        services.AddScoped<DecisionCommitService>();
        // Task 043 (K4): the server-side plan-read time the commit route uses as the review start (ITenantCache, a singleton).
        services.AddSingleton<DecisionPlanReadStamp>();

        // RuleBodyDescriber (task 026, FR-48): the read-side plain-language description of a rule body. Stateless over the
        // compiler and the shared sysadmin entity service, both singletons.
        services.AddSingleton<RuleBodyDescriber>();

        // Task 100 (FR-57, D-22): the Ontology admin Rules routes' service. Scoped: it holds the CALLER's IDataverseUserClient, so every read
        // and write runs as the caller. The delegate is task 033's immediate PolicyRetired closure (a seam so Retire's ordering and re-run
        // rule are testable without the Signal writer's constructor). Unconditional: the routes map unconditionally.
        services.AddScoped<Sprk.Bff.Api.Services.Ontology.PolicyRetiredClosure>(
            sp => sp.GetRequiredService<SignalClosureService>().ClosePolicyRetiredNowAsync);
        services.AddScoped<Sprk.Bff.Api.Services.Ontology.OntologyAdminRulesService>();

        // Task 031 (FR-11, FR-15, D-13): the side-effect-free evaluation core (reused by the event triggers, 032, and the admin
        // dry run, 101) and the ONE nightly re-evaluating job wrapping it with the writes. An ADR-036 IScheduledJob on the
        // shared Spaarke.Scheduling host (no timer BackgroundService, ADR-052: BFF placement), registered unconditionally
        // (bff-extensions.md F.1). The diagnostic staleness check is a /healthz/catalog check (catalog tag), never liveness.
        services.AddSingleton<SignalEvaluator>();

        // Task 034 (FR-17, D-11): the dismissal counter and the sprk_suppresseduntil stamp the job above reads, shared with the
        // suppression read route (DecisionPlanEndpoints). Singleton: its dependencies are the shared sysadmin reader and the writer client.
        services.AddSingleton<SignalSuppressionService>();

        // Task 033 (FR-16): Superseded / PolicyRetired closures; the job runs them nightly, tasks 100/101 call them directly.
        services.AddSingleton<SignalClosureService>();
        services.AddScheduledJob<SignalReevaluationJob>(SignalReevaluationJob.DefaultCronSchedule);
        services.AddSingleton<SignalEvaluatorHealthCheck>();

        // Task 101 (D-22 slice b): the Ontology admin rule-authoring service (scoped: every read and write is the request caller's
        // through the caller-identity client) and its one seam onto the evaluator and the job above (singleton, as they are).
        // Unconditional, like the routes that use them (bff-extensions.md F.1).
        services.AddSingleton<IOntologyAdminAuthoringSignalEngine, OntologyAdminAuthoringSignalEngine>();
        services.AddScoped<OntologyAdminAuthoringService>();

        // Task 148 (D-135 item 2, follow the assignee): every 5 minutes (recently changed To Dos) and nightly (all), a personal
        // To Do's Signals and Decision Records are re-owned to its current Assigned To's team, only through uac-r2's
        // SecureChildReconciler.ReownOrdinaryRowAsync. REPORT-ONLY unless Ontology:TodoAssigneeOwner:WritesEnabled=true (owner).
        // ADR-036 IScheduledJob on the shared host (ADR-052: BFF placement). Unconditional: CoreAncestorResolver,
        // IRecordOwnershipResolver, TimeProvider and IConfiguration are singletons; the scoped services it resolves per run
        // (IGenericEntityService, AssignedAccessStore, SecureChildReconciler, IIdempotencyService) are registered unconditionally.
        services.AddScheduledJob<TodoAssigneeOwnerReconciliationJob>(TodoAssigneeOwnerReconciliationJob.DefaultCronSchedule);

        // Task 032 (D-12, D-48): the event triggers. ONE ADR-004 IJobHandler on the main queue that calls the nightly job's own
        // scoped evaluate-plan-write path (no second evaluator), and the enqueue the classification path, the budget-revision
        // create and the association-confirmed filing path call. Unconditional (bff-extensions.md F.1).
        services.AddSingleton<SignalEvaluationTrigger>();
        services.AddSingleton<IJobHandler, SignalEvaluationTriggerHandler>();

        // Task 149 (D-139): the communications gate queues its decision and acts at once; ONE ADR-004 IJobHandler writes the Decision Record
        // (exactly one per communication + outcome) through the writer above. The enqueuer is a singleton (the gate consumer, a singleton,
        // takes it); the handler is SCOPED because IIdempotencyService is (WorkersModule), and the job processor resolves its handlers from a
        // scope. Unconditional (bff-extensions.md F.1): JobSubmissionService, IGenericEntityService and IIdempotencyService are too.
        services.AddSingleton<GateDecisionRecordEnqueuer>();
        services.AddScoped<IJobHandler, GateDecisionRecordJobHandler>();
        services.AddHealthChecks()
            .AddCatalogCheck<SignalEvaluatorHealthCheck>(
                "signal-reevaluation",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: new[] { "ontology", "signals" });

        return services;
    }
}
