using Microsoft.Extensions.DependencyInjection.Extensions;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse.Extensions;

/// <summary>
/// DI registration for the Spaarke DataGrid Framework R1 metadata-projection services (FR-BFF-03).
/// </summary>
/// <remarks>
/// <para>
/// Registers <see cref="MetadataService"/> as scoped. Scoped (vs. singleton) is chosen because the
/// service depends on <c>IDataverseService</c> which is registered scoped in <c>DataverseModule</c>
/// to align with per-request <c>ServiceClient</c> lifetime semantics. The service has no per-request
/// mutable state beyond the injected dependencies.
/// </para>
/// <para>
/// Main session wires this from <c>Program.cs</c> via
/// <c>builder.Services.AddDataverseMetadataServices();</c> after the BFF wave completes.
/// </para>
/// </remarks>
public static class MetadataServiceExtensions
{
    /// <summary>
    /// Registers the <see cref="MetadataService"/> required by the Spaarke DataGrid Framework R1
    /// metadata endpoint (FR-BFF-03 — <c>GET /api/dataverse/metadata/{entityLogicalName}</c>).
    /// </summary>
    public static IServiceCollection AddDataverseMetadataServices(this IServiceCollection services)
    {
        services.AddScoped<MetadataService>();
        services.AddCoreAncestorResolver();
        // Task 080: every BFF record create assigns ownerid to the acting user's BU default owner team.
        // Registered here because Program.cs calls this method unconditionally (:81).
        services.AddRecordOwnershipResolver();

        // Task 156 (owner round 4 item 5, option b; round 3 R3/R4 — minutes, not hours): the 5-minute safety net that
        // re-stamps a child whose core-ancestor copy differs from its intermediate's current root (writes outside the
        // BFF, failed cascades, regarding lookups cleared on a form). ADR-036 A1 rule 6: AddScheduledJob, once — here,
        // not in AddCoreAncestorResolver, which AddToolFramework also calls and AddScheduledJob is not idempotent.
        // UNCONDITIONAL (ADR-032): every dependency is unconditional, so there is no flag and no Null-Object; its dry-run
        // mode is configuration (CoreAncestorStampReconciliationJob.WritesEnabledConfigKey), not an `if` here.
        services.AddScheduledJob<CoreAncestorStampReconciliationJob>(CoreAncestorStampReconciliationJob.DefaultCronSchedule);
        return services;
    }

    /// <summary>
    /// Registers <see cref="CoreAncestorResolver"/> — the FR-26 core-ancestor derivation every server-side
    /// writer of a <c>sprk_regarding*</c> lookup on a child record routes through (task 052).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Singleton, with a scope-bridged metadata probe.</b> The resolver's consumers span every lifetime:
    /// <c>CommunicationService</c> and <c>IncomingAssociationResolver</c> are singletons, <c>OfficeService</c>
    /// and the tool handlers are scoped, and <c>TaskActionCore</c> / <c>TodoRegardingBuilder</c> are
    /// constructed inline. Only a singleton can serve all of them. But <see cref="MetadataService"/> is
    /// SCOPED, so capturing one here would be a captive dependency — instead the
    /// <see cref="CoreAncestorResolver.EntityColumnProbe"/> opens a scope per call, the same bridge
    /// <c>UpdateRecordActionCore</c> already uses for the same service. The probe is cheap: metadata is
    /// Redis-cached for 6 hours, so the steady-state cost is a cache read, not a Dataverse round-trip.
    /// </para>
    /// <para>
    /// <b>Idempotent + registered from every entry point.</b> <c>TryAddSingleton</c> and a call from both
    /// <see cref="AddDataverseMetadataServices"/> and <c>AddToolFramework</c> — because
    /// <c>EmailDraftToolHandler</c> is registered by the tool-framework assembly scan and would otherwise
    /// fail to resolve wherever the tool framework is composed without this module. That asymmetry is the
    /// CLAUDE.md §10 F.1 anti-pattern; the same fix is applied here as for <c>TimeProvider</c>.
    /// </para>
    /// <para>
    /// ADR-010: a concrete registered once, no interface — the only test seam
    /// (<see cref="CoreAncestorResolver.EntityColumnProbe"/>) is a delegate.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCoreAncestorResolver(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new CoreAncestorResolver(
            sp.GetRequiredService<IGenericEntityService>(),
            async (entityLogicalName, ct) =>
            {
                using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
                var metadata = scope.ServiceProvider.GetRequiredService<MetadataService>();
                return await CoreAncestorResolver.FromMetadata(metadata)(entityLogicalName, ct)
                    .ConfigureAwait(false);
            },
            sp.GetRequiredService<ILogger<CoreAncestorResolver>>()));

        // Task 156: the stamp's refresh side — the cascade every BFF re-file path calls, and the queue the storage
        // resolver enqueues a stale child on (consumed by CoreAncestorRestampJobHandler, JobProcessingModule). Singletons
        // over singletons, registered beside the resolver so every composition that has the resolver has these too
        // (the same §10 F.1 reasoning as above). The queue resolves Service Bus LAZILY, so composing it never needs
        // Service Bus configuration.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<CoreAncestorRestamper>();
        services.TryAddSingleton<CoreAncestorRestampQueue>();

        // Task 156, owner round 8 item 1 (§6.5 path B): the ONE app-only step the user-OBO AI update tool may take — the
        // after-write re-stamp, inline (DataverseUpdateRecordHandler). Here, beside the restamper, because AddToolFramework
        // (which registers that handler by assembly scan) calls this method too: no composition has the handler without it
        // (§10 F.1). Unconditional (ADR-032).
        services.TryAddSingleton<CoreAncestorAfterWriteRestamp>();

        // Task 158 (owner round 6): the two calls a BFF writer of a work assignment or project makes around a create or a
        // re-file, so one filed under a secure matter or project is secured in the same operation. Here for the same §10
        // F.1 reason: every composition with a writer (the tool framework's scan, the playbook nodes, the Office and
        // finance modules) calls this method. A singleton over IServiceScopeFactory only — it resolves the scoped
        // SecureRootInheritance (ExternalAccessModule) per call and REFUSES a filing write where it cannot. Unconditional.
        services.TryAddSingleton<Sprk.Bff.Api.Services.Access.SecureRootFilingGate>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IRecordOwnershipResolver"/> — resolves the business-unit default owner team that
    /// owns a new record: the filed record's unit, else the acting user's (spaarkeai-word-add-in-r1 task 080,
    /// owner decisions 2026-09-22 / 2026-09-25). The Office writers use it; other BFF writers are GitHub #1034.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered <b>UNCONDITIONALLY</b> (ADR-010 / ADR-032). This is deliberate and load-bearing: the create
    /// paths that consume it REFUSE when a team cannot be resolved, so a conditional or missing registration
    /// would not degrade gracefully — it would either break every create or, worse, tempt a fallback to
    /// app-only ownership, which is exactly the defect this resolver exists to remove.
    /// </para>
    /// <para>
    /// Stateless over the singleton <see cref="IGenericEntityService"/>, so singleton. It performs two small
    /// reads per create (target or systemuser → business unit, business unit → default owner team). Deliberately
    /// <b>uncached</b> for now: a record create already makes several Dataverse round trips and is not a
    /// hot path like a typeahead, and ADR-009 rules out an in-memory cache for cross-request data. If
    /// measurement later shows it matters, the cache belongs behind <c>ITenantCache</c>, not in a static.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRecordOwnershipResolver(this IServiceCollection services)
    {
        services.TryAddSingleton<IRecordOwnershipResolver, RecordOwnershipResolver>();
        return services;
    }
}
