using Sprk.Bff.Api.Services.Dataverse.FetchXml;

namespace Sprk.Bff.Api.Services.Dataverse.Extensions;

/// <summary>
/// DI registration for the Spaarke DataGrid Framework R1 fetch-query services (FR-BFF-04).
/// </summary>
/// <remarks>
/// <para>
/// Registers:
/// </para>
/// <list type="bullet">
///   <item><see cref="IFetchXmlEntityExtractor"/> → <see cref="FetchXmlEntityExtractor"/> as
///         <b>singleton</b> — the extractor is stateless (pure parser) and the same instance
///         can safely serve every request. Singleton avoids per-request allocation for what is
///         a security-critical path called on every <c>/api/dataverse/fetch</c> request.</item>
///   <item><see cref="FetchService"/> as <b>scoped</b> — depends on <c>IDataverseService</c>
///         which is registered scoped per the Spaarke.Dataverse module's lifetime contract.
///         <see cref="FetchService"/> itself holds no per-request mutable state beyond the
///         injected dependencies.</item>
/// </list>
/// <para>
/// Main session wires this from <c>Program.cs</c> via
/// <c>builder.Services.AddDataverseFetchServices();</c> after the BFF wave completes.
/// </para>
/// </remarks>
public static class FetchServiceExtensions
{
    /// <summary>
    /// Registers the services required by the Spaarke DataGrid Framework R1 fetch endpoint
    /// (FR-BFF-04 — <c>POST /api/dataverse/fetch</c>).
    /// </summary>
    public static IServiceCollection AddDataverseFetchServices(this IServiceCollection services)
    {
        // Stateless singleton — same instance for every privilege-check pass + every endpoint call.
        services.AddSingleton<IFetchXmlEntityExtractor, FetchXmlEntityExtractor>();

        // Scoped — IDataverseService is scoped; FetchService follows the same lifetime.
        services.AddScoped<FetchService>();

        return services;
    }
}
