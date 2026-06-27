using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.Services.Dataverse.Extensions;

/// <summary>
/// DI registration extensions for the Dataverse projection services owned by task 011 (SavedQuery
/// + the shared authorization/privilege infrastructure consumed by tasks 012, 013, 014).
/// </summary>
/// <remarks>
/// <para>
/// Tasks 012 (entity metadata), 013 (FetchXML execution), and 014 (single-record read) will add
/// sibling <c>Add{Feature}Services</c> extensions. The main session aggregates the registrations
/// in <c>Program.cs</c> after the wave completes (per the parallel-safety boundary defined in the
/// task 011 prompt — this sub-agent does not modify <c>Program.cs</c>).
/// </para>
/// <para>
/// Lifetimes follow ADR-010 (DI minimalism) and the existing module precedents in
/// <c>Infrastructure/DI/</c>:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="IDataversePrivilegeChecker"/> → <see cref="UserPrivilegeChecker"/> as a singleton (caches at the application level via <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>; <see cref="UserPrivilegeChecker"/> is stateless beyond the injected dependencies).</description></item>
///   <item><description><see cref="SavedQueryService"/> as scoped (per-request lifetime matches its dependencies; consistent with the existing <c>DataverseUpdateHandler</c> precedent).</description></item>
/// </list>
/// <para>
/// <see cref="DataverseAuthorizationFilter"/> is NOT registered directly — it is constructed per
/// request by the <c>AddDataverseAuthorizationFilter</c> extension wired onto endpoints.
/// </para>
/// </remarks>
public static class DataverseServiceExtensions
{
    /// <summary>
    /// Registers the SavedQuery service and the shared privilege-checker infrastructure.
    /// </summary>
    /// <remarks>
    /// Must be called after the shared Dataverse module (which registers
    /// <c>IDataverseService → DataverseServiceClientImpl</c>) and after Redis-backed
    /// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> is registered.
    /// </remarks>
    public static IServiceCollection AddDataverseSavedQueryServices(this IServiceCollection services)
    {
        // Shared privilege checker — consumed by DataverseAuthorizationFilter (this task) AND by the
        // savedquery by-id handler (deferred privilege check) AND by sibling endpoints in tasks 012/013/014.
        services.AddSingleton<IDataversePrivilegeChecker, UserPrivilegeChecker>();

        // SavedQuery service.
        services.AddScoped<SavedQueryService>();

        return services;
    }
}
