using System.Reflection;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 165 — every SPE admin request BODY that names a config must implement
/// <see cref="ISpeAdminConfigScopedRequest"/>, and every body that names ONE container must implement
/// <see cref="ISpeAdminContainerScopedRequest"/>, so the tenant-scope filter sees the id it must judge.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this prevents.</b> <c>SpeAdminTenantScopeFilter</c> confines a request to configs in the caller's
/// business units — and, since owner round 20, to containers bound inside them — but only for an id it can SEE.
/// <c>BulkDeleteRequest</c> and <c>BulkPermissionsRequest</c> carried <c>ConfigId</c> in the JSON body, where the filter
/// never looked, so any SPE admin could bulk soft-delete, or grant owner on, another business unit's containers (sweep
/// findings #44, #72). The filter now reads a bound body that implements the marker; this guard makes the NEXT such
/// request record implement it too.
/// </para>
/// <para>
/// <b>Scope</b> (widened by the task 165 follow-up round, verifier finding 7): EVERY type an <c>/api/spe</c> handler binds
/// — any class or record that is a parameter of a method declared in <c>Sprk.Bff.Api.Api.SpeAdmin</c> (nested records
/// in an endpoint class included, at any accessibility) — plus every <c>*Request</c> in <c>Sprk.Bff.Api.Models.SpeAdmin</c>.
/// The first version scanned only the latter, so a body record declared beside its handler bypassed both the filter and
/// the guard. A body carrying <c>ContainerIds</c> (many) is not in scope: the bulk job decides those per item.
/// </para>
/// </remarks>
public sealed class SpeAdminConfigScopedBodyGuardTests
{
    private const string HandlerNamespace = "Sprk.Bff.Api.Api.SpeAdmin";
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact(DisplayName = "Every SPE admin request body carrying ConfigId implements ISpeAdminConfigScopedRequest")]
    public void EveryRequestBodyCarryingAConfigId_ImplementsTheScopedMarker()
    {
        var bodies = RequestBodies().Where(t => t.GetProperty("ConfigId", AnyInstance) is not null).ToList();

        Assert.True(
            bodies.Count >= 2,
            "the scan must find at least the two bulk request records, or this guard passes vacuously");

        var unmarked = bodies
            .Where(t => !typeof(ISpeAdminConfigScopedRequest).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(
            unmarked.Count == 0,
            "These SPE admin request bodies carry a ConfigId but do not implement ISpeAdminConfigScopedRequest, "
            + "so SpeAdminTenantScopeFilter cannot see it and the business-unit boundary does not run on their "
            + "routes (the task 165 bulk-route defect). Implement the marker:\n  "
            + string.Join("\n  ", unmarked));
    }

    [Fact(DisplayName = "Every SPE admin request body naming one ContainerId implements ISpeAdminContainerScopedRequest")]
    public void EveryRequestBodyNamingOneContainer_ImplementsTheContainerMarker()
    {
        var bodies = RequestBodies().Where(t => t.GetProperty("ContainerId", AnyInstance) is not null).ToList();

        Assert.True(
            bodies.Count >= 1,
            "the scan must find at least the search-items request, which names a container in its body");

        var unmarked = bodies
            .Where(t => !typeof(ISpeAdminContainerScopedRequest).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(
            unmarked.Count == 0,
            "These SPE admin request bodies name a container but do not implement ISpeAdminContainerScopedRequest, "
            + "so SpeAdminTenantScopeFilter cannot judge that container against its business-unit binding (owner round "
            + "20 item 2). Implement the marker:\n  " + string.Join("\n  ", unmarked));
    }

    [Fact(DisplayName = "The body scan reaches records nested in an /api/spe endpoint class")]
    public void TheScan_ReachesRecordsNestedInAnEndpointClass()
    {
        // Non-vacuity for the widening: SearchItemsRequest is declared INSIDE SearchItemsEndpoints.
        Assert.Contains(RequestBodies(), t => t.DeclaringType is not null && t.Name == "SearchItemsRequest");
    }

    /// <summary>
    /// Every class/record an <c>/api/spe</c> handler can bind: parameter types of every method declared in a type of
    /// <see cref="HandlerNamespace"/> (nested types included), plus every <c>*Request</c> in <c>Models.SpeAdmin</c>.
    /// </summary>
    private static IReadOnlyList<Type> RequestBodies()
    {
        var assembly = typeof(ISpeAdminConfigScopedRequest).Assembly;
        var types = ADR001_MinimalApiTests.LoadableTypes(assembly).ToList();

        var handlerParameters = types
            .Where(t => t.Namespace == HandlerNamespace)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                                          | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(IsEndpointHandler)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Where(t => t.IsClass && t.Assembly == assembly);

        var modelRequests = types
            .Where(t => t.IsClass
                        && t.Namespace == typeof(ISpeAdminConfigScopedRequest).Namespace
                        && t.Name.EndsWith("Request", StringComparison.Ordinal));

        return handlerParameters.Concat(modelRequests).Distinct().ToList();
    }

    /// <summary>A Minimal API handler: it answers an <c>IResult</c> (directly, or through a Task/ValueTask).</summary>
    private static bool IsEndpointHandler(MethodInfo method)
    {
        var returns = method.ReturnType;
        if (returns.IsGenericType && (returns.GetGenericTypeDefinition() == typeof(Task<>)
                                      || returns.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            returns = returns.GetGenericArguments()[0];
        }

        return typeof(Microsoft.AspNetCore.Http.IResult).IsAssignableFrom(returns);
    }
}
