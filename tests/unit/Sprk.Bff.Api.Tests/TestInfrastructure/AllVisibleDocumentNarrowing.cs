using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// unified-access-control-r2 task 188 round 3 slice B: a test double of <see cref="InternalRouteDocumentNarrowing"/> that
/// shows every document it is asked about.
/// </summary>
/// <remarks>
/// <para>For test hosts whose tests are about something else on the internal plane's document routes (identity, profile,
/// Office save, compose, version history, ...). Their principal is not a resolved internal systemuser (a non-GUID
/// <c>oid</c>, or no systemuser modelled), so the routes apply the external document narrowing, whose app-only read cannot
/// succeed offline; this double states "nothing is hidden" instead.</para>
/// <para>The narrowing itself is covered on every one of these surfaces by <c>DocumentRouteExternalNarrowingTests</c> (the
/// REAL predicate behind the real seam) and by the ArchTest <c>ExternalDocumentNarrowingGuardTests</c>.
/// <c>ExternalDocumentNarrowingRouteTests.TheInternalRoutesNarrowing_IsRegistered_OverTheAppOnlyReader</c> pins that the
/// production composition registers the real seam. The assistant's <see cref="AppOnlyDocumentNarrowing"/> is NOT replaced.</para>
/// </remarks>
internal static class AllVisibleDocumentNarrowing
{
    /// <summary>The double: every asked id is visible; nothing is read.</summary>
    public static InternalRouteDocumentNarrowing Instance { get; } = (documentIds, _) =>
        Task.FromResult(DocumentNarrowingResult.NotNarrowed(documentIds
            .Select(id => new DocumentNarrowingRow(id, DocumentPermission.NotNarrowed, Guid.Empty, Guid.Empty))));

    /// <summary>Replaces the host's <see cref="InternalRouteDocumentNarrowing"/> with <see cref="Instance"/>.</summary>
    public static IServiceCollection UseAllVisibleDocumentNarrowing(this IServiceCollection services)
    {
        services.RemoveAll<InternalRouteDocumentNarrowing>();
        services.AddSingleton(Instance);
        return services;
    }
}
