// R3 Part 1 — User-Record Membership Resolution (DI module)
// Task 012 (2026-06-21): Registers MembershipOptions binding.
// Task 031 (2026-06-21): Adds IIdentityNormalizationService singleton.
// Task 032 (2026-06-21): Adds OrganizationMembershipResolver (registered as
// both IOrganizationMembershipResolver canonical + IIdentityOrganizationResolver
// task-031 seam).
// Task 030 (2026-06-21): Adds IMembershipFieldDiscoveryService singleton.
// Task 033 (2026-06-21): Adds IMembershipResolverService singleton (orchestration).
// Remaining registrations (endpoint mappings) arrive in later P4 tasks (035-036).
//
// ADR-010 (DI Minimalism): Feature-module pattern — one Add{Module}() per
// feature area, called from Program.cs.
// bff-extensions.md §A: BFF-touching addition. Placement = BFF (membership
// resolution is request-scoped, has TTFB budget against BFF state, and is
// consumed by AI playbook nodes + endpoints in the same request lifecycle).

using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration for the user-record membership resolution feature
/// (R3 Part 1). Currently binds <see cref="MembershipOptions"/> from
/// configuration; service registrations follow in later P4 tasks.
/// </summary>
public static class MembershipModule
{
    /// <summary>
    /// Registers <see cref="MembershipOptions"/> bound to the
    /// <c>"Membership"</c> configuration section. Defaults are conservative
    /// (empty lists) so apps that never opt into the membership feature still
    /// resolve <c>IOptions&lt;MembershipOptions&gt;</c> cleanly.
    /// </summary>
    public static IServiceCollection AddMembership(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options binding only — no validation gate here. The discovery
        // service (task 030) will validate the contents at first-use.
        services.Configure<MembershipOptions>(
            configuration.GetSection(MembershipOptions.SectionName));

        // Task 032: organization-membership resolver. One concrete satisfies
        // both consumer-facing interfaces:
        //   - IOrganizationMembershipResolver: canonical (PersonIdentity-aware) contract
        //   - IIdentityOrganizationResolver: task 031's IEnumerable seam consumed by
        //     IdentityNormalizationService
        // Registered as singleton (ADR-010) — the resolver holds no per-request
        // state; the once-per-process "no mapping configured" log latch is
        // intentionally singleton-scoped.
        services.AddSingleton<OrganizationMembershipResolver>();
        services.AddSingleton<IOrganizationMembershipResolver>(
            sp => sp.GetRequiredService<OrganizationMembershipResolver>());
        services.AddSingleton<IIdentityOrganizationResolver>(
            sp => sp.GetRequiredService<OrganizationMembershipResolver>());

        // Task 031: identity normalization. Singleton (per ADR-010, holds no
        // per-request state — Redis cache is the only mutable surface, and
        // IDistributedCache itself is thread-safe). Consumes IDataverseService
        // (registered elsewhere), IDistributedCache (CacheModule),
        // IEnumerable<IIdentityOrganizationResolver> (registered above —
        // empty enumerable is acceptable; service returns empty OrganizationIds),
        // IOptions<MembershipOptions>, ILogger.
        services.AddSingleton<IIdentityNormalizationService, IdentityNormalizationService>();

        // Task 030: metadata-driven Lookup-field discovery. Singleton (per
        // ADR-010, holds no per-request state — IDistributedCache is the only
        // mutable surface and is thread-safe). Consumes IDataverseService
        // (unwrapped to ServiceClient for RetrieveEntityRequest, matches the
        // existing Services.Dataverse.MetadataService pattern), IDistributedCache
        // (CacheModule), IOptions<MembershipOptions>, ILogger.
        services.AddSingleton<IMembershipFieldDiscoveryService, MembershipFieldDiscoveryService>();

        // Task 033: top-level orchestration. Singleton (per ADR-010, holds no
        // per-request state — IDistributedCache is the only mutable surface and is
        // thread-safe). Consumes IMembershipFieldDiscoveryService (above),
        // IIdentityNormalizationService (above), IDataverseService (registered
        // elsewhere — used for FetchExpression queries against the target entity),
        // IDistributedCache (CacheModule), IOptions<MembershipOptions>, ILogger.
        services.AddSingleton<IMembershipResolverService, MembershipResolverService>();

        return services;
    }
}
