namespace Sprk.Bff.Api.Services.Dataverse.Extensions;

/// <summary>
/// DI registrations for the Dataverse record service (FR-BFF-05).
/// </summary>
/// <remarks>
/// Composed into <c>AddDataverseProjectionModule()</c> by <c>Program.cs</c> after the Phase B Wave 1
/// services are merged. Kept as a standalone extension to make task 014's scope explicit and to
/// preserve parallel-execution boundaries across tasks 011-014.
/// </remarks>
internal static class RecordServiceExtensions
{
    /// <summary>
    /// Registers the <see cref="RecordService"/> used by <c>GET /api/dataverse/record/{entityLogicalName}/{id}</c>.
    /// Scoped per <c>IDataverseService</c> lifetime to avoid holding the privileged ServiceClient open across requests.
    /// </summary>
    public static IServiceCollection AddDataverseRecordServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<RecordService>();

        return services;
    }
}
