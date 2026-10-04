using System.Reflection;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 165 — every SPE admin request BODY that names a config must implement
/// <see cref="ISpeAdminConfigScopedRequest"/>, so the tenant-scope filter sees its configId.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this prevents.</b> <c>SpeAdminTenantScopeFilter</c> confines a request to configs in the
/// caller's business units — but only for a configId it can SEE. <c>BulkDeleteRequest</c> and
/// <c>BulkPermissionsRequest</c> carried <c>ConfigId</c> in the JSON body, where the filter never looked, so
/// any SPE admin could bulk soft-delete, or grant owner on, another business unit's containers (sweep
/// findings #44, #72). The filter now reads a bound body that implements the marker; this guard makes the
/// NEXT such request record implement it too, instead of bypassing the boundary as those two did.
/// </para>
/// <para>
/// Scope: public types in <c>Sprk.Bff.Api.Models.SpeAdmin</c> whose name ends in <c>Request</c> and that
/// declare a public <c>ConfigId</c> property. Response DTOs are not request bodies and are out of scope.
/// </para>
/// </remarks>
public sealed class SpeAdminConfigScopedBodyGuardTests
{
    [Fact(DisplayName = "Every SPE admin request body carrying ConfigId implements ISpeAdminConfigScopedRequest")]
    public void EveryRequestBodyCarryingAConfigId_ImplementsTheScopedMarker()
    {
        var requestTypesWithConfigId = typeof(ISpeAdminConfigScopedRequest).Assembly
            .GetTypes()
            .Where(t => t.IsClass
                        && t.IsPublic
                        && t.Namespace == typeof(ISpeAdminConfigScopedRequest).Namespace
                        && t.Name.EndsWith("Request", StringComparison.Ordinal)
                        && t.GetProperty("ConfigId", BindingFlags.Public | BindingFlags.Instance) is not null)
            .ToList();

        Assert.True(
            requestTypesWithConfigId.Count > 0,
            "the scan must find the bulk request records, or this guard passes vacuously");

        var unmarked = requestTypesWithConfigId
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
}
