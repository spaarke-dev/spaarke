using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// unified-access-control-r2 task 188 round 4a: states WHO the caller is for THE external document narrowing
/// (<c>InternalPlaneDocumentNarrowing</c>) and what the BFF's app-only identity reads, for a test host or a bare
/// <c>DefaultHttpContext</c>.
/// </summary>
/// <remarks>
/// The same three registrations <see cref="DocumentRouteNarrowingTestFixture"/> makes inline: the REAL
/// <see cref="CallerSystemUserResolver"/> (over a GUID oid, <see cref="GuidOidCallerResolver"/>) answering from the stated
/// <see cref="NarrowingCallerMode"/>, and <see cref="InternalRouteDocumentNarrowing"/> bound to the REAL predicate behind the
/// real app-only seam (<see cref="AppOnlyDocumentWorld"/>). A host whose tests are about something else states
/// <see cref="NarrowingCallerMode.Internal"/>: the narrowing then makes no call and no read, as for an internal user in
/// production.
/// </remarks>
internal static class StatedNarrowingCaller
{
    /// <summary>Registers the stated caller and the world (both read per request, so a test may change them).</summary>
    public static IServiceCollection AddStatedNarrowingCaller(
        this IServiceCollection services, Func<NarrowingCallerMode> mode, Func<AppOnlyDocumentWorld> world)
    {
        services.RemoveAll<ISystemUserIdentityResolver>();
        services.AddSingleton<ISystemUserIdentityResolver>(new NarrowingStatedIdentityResolver(mode));
        services.RemoveAll<ICallerSystemUserResolver>();
        services.AddScoped<ICallerSystemUserResolver>(sp => new GuidOidCallerResolver(
            new CallerSystemUserResolver(
                sp.GetRequiredService<ISystemUserIdentityResolver>(), NullLogger<CallerSystemUserResolver>.Instance)));
        services.RemoveAll<InternalRouteDocumentNarrowing>();
        services.AddScoped<InternalRouteDocumentNarrowing>(_ => (ids, ct) => world().Narrowing(ids, ct));
        return services;
    }

    /// <summary>
    /// Request services for a bare <c>DefaultHttpContext</c> whose caller is a resolved INTERNAL systemuser: the narrowing
    /// makes no call and no read.
    /// </summary>
    public static IServiceProvider InternalCallerServices()
    {
        var world = new AppOnlyDocumentWorld();
        return new ServiceCollection().AddLogging()
            .AddStatedNarrowingCaller(() => NarrowingCallerMode.Internal, () => world)
            .BuildServiceProvider();
    }
}
