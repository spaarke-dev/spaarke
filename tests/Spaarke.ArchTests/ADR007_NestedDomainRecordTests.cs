using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-007: closes a gap in <see cref="ADR007_GraphIsolationTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>ADR007_GraphIsolationTests.GraphTypesMustBeIsolatedToInfrastructure</c> skips any type whose
/// namespace <em>contains</em> "Infrastructure.Graph" or "SpeFileStore". That exemption is correct for
/// the facade itself — <c>SpeAdminGraphService</c> legitimately handles Graph SDK types. But it is
/// applied by namespace, so it also exempts every <b>nested domain record</b> declared inside those
/// services (<c>SpeAdminGraphService.SpeContainerTypeSummary</c>, <c>.SpeConsumingTenant</c>,
/// <c>.ContainerTypeSettingsResult</c>, <c>.SpeContainerColumn</c>, …). Those records ARE the facade's
/// public output — the layer above consumes them directly — so a Graph SDK type leaking onto one of
/// their properties is exactly the ADR-007 violation the suite exists to catch, and it was invisible.
/// </para>
/// <para>
/// Found 2026-08-27 by task 042 while classifying scaffolding tests: five ad-hoc reflection tests
/// scattered across <c>Phase2IntegrationTests</c>, <c>Phase3IntegrationTests</c>,
/// <c>ContainerTypeEndpointsTests</c> and <c>UpdateContainerTypeSettingsTests</c> were each hand-checking
/// ONE record. They matched ADR-038 §7 B8 (reflection tests) and sat off the KEEP path, but deleting
/// them would have removed the only protection. This rule generalises all of them into one invariant on
/// the ratified 8th KEEP path (Amendment A1), so the per-record copies can retire.
/// </para>
/// </remarks>
public class ADR007_NestedDomainRecordTests
{
    /// <summary>Namespaces whose CONTAINING types may handle Graph SDK types (the facade itself).</summary>
    private static readonly string[] FacadeNamespaces = ["Infrastructure.Graph", "SpeFileStore"];

    /// <summary>
    /// A nested record returned by the facade is public API for the layer above. It must be expressible
    /// in BCL types alone — if it needs a Graph SDK type, the facade has not finished mapping.
    /// </summary>
    [Fact(DisplayName = "ADR-007: nested domain records inside the Graph facade must not expose Graph SDK types")]
    public void NestedDomainRecordsMustNotExposeGraphSdkTypes()
    {
        var assembly = typeof(Program).Assembly;
        var violations = new List<string>();

        foreach (var containingType in Types.InAssembly(assembly).GetTypes())
        {
            // Only the types the sibling rule exempts — that exemption is the gap being closed.
            if (!FacadeNamespaces.Any(ns => containingType.Namespace?.Contains(ns) == true))
                continue;

            foreach (var nested in containingType.GetNestedTypes(BindingFlags.Public))
            {
                foreach (var prop in nested.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    foreach (var leaked in GraphTypesIn(prop.PropertyType))
                    {
                        violations.Add(
                            $"{containingType.Name}.{nested.Name}.{prop.Name} exposes {leaked}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-007: these nested domain records leak Graph SDK types to callers above the facade. " +
            "Map them to BCL types inside the service instead.\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>
    /// Yields any Microsoft.Graph type reachable from <paramref name="type"/>, unwrapping the shapes a
    /// record property actually uses: nullables and generic collections (<c>IReadOnlyList&lt;T&gt;</c>,
    /// <c>List&lt;T&gt;</c>, <c>Dictionary&lt;K,V&gt;</c>). A bare namespace check on the outer type
    /// would miss <c>IReadOnlyList&lt;Microsoft.Graph.Models.User&gt;</c> — which is a leak.
    /// </summary>
    private static IEnumerable<string> GraphTypesIn(Type type)
    {
        // `Microsoft.SharePoint` is here for the same reason as `Microsoft.Graph`: the SPE Admin
        // register path talks to the SharePoint REST API, and an SDK type from it crossing the facade
        // is the identical ADR-007 leak. Added 2026-08-27 — the first pass checked only Graph, which
        // is why `RegisterContainerTypeResult_HasNoSharePointSdkTypeReferences` could not retire into
        // this rule. It can now.
        if (type.Namespace?.StartsWith("Microsoft.Graph", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("Microsoft.SharePoint", StringComparison.Ordinal) == true)
        {
            yield return type.FullName ?? type.Name;
            yield break;
        }

        if (!type.IsGenericType)
            yield break;

        foreach (var arg in type.GetGenericArguments())
        {
            foreach (var leaked in GraphTypesIn(arg))
                yield return leaked;
        }
    }

    /// <summary>
    /// The sibling rule to the one above: what the facade RETURNS must also be Graph-free.
    /// </summary>
    /// <remarks>
    /// A leak here is caught indirectly today — the caller lives outside the exempt namespace, so
    /// <c>ADR007_GraphIsolationTests</c> would flag the consuming endpoint. But it flags the wrong place:
    /// the endpoint is the victim, the facade signature is the cause. This names the cause, and it also
    /// covers a service method with no caller yet. Replaces
    /// <c>SpeContainerTypeSummary_UsedAsReturnType_FromCreateContainerTypeAsync</c>, which hand-checked
    /// exactly one method by name (task 042).
    /// </remarks>
    [Fact(DisplayName = "ADR-007: public facade methods must not return Graph SDK types")]
    public void FacadeMethodsMustNotReturnGraphSdkTypes()
    {
        var assembly = typeof(Program).Assembly;
        var violations = new List<string>();

        foreach (var facadeType in Types.InAssembly(assembly).GetTypes())
        {
            if (!FacadeNamespaces.Any(ns => facadeType.Namespace?.Contains(ns) == true))
                continue;

            foreach (var method in facadeType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                // Property accessors are covered by the nested-record rule above; skip the duplicates.
                if (method.IsSpecialName)
                    continue;

                foreach (var leaked in GraphTypesIn(method.ReturnType))
                {
                    // `GraphServiceClient` is the client itself, not a data model. Returning it IS the
                    // contract of a client factory (`IGraphClientFactory.ForUserAsync`,
                    // `SpeAdminGraphService.GetClientForConfigAsync`) — ADR-007 governs Graph *models*
                    // crossing the facade, not the plumbing that builds the client. Verified 2026-08-27:
                    // narrowing to this one exemption leaves 9 legitimate factory methods passing and
                    // still fails on any `Microsoft.Graph.Models.*` return.
                    if (leaked == "Microsoft.Graph.GraphServiceClient")
                        continue;

                    violations.Add($"{facadeType.Name}.{method.Name} returns {leaked}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ADR-007: these facade methods hand Graph SDK types to the layer above. " +
            "Return a domain record instead.\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>
    /// Positive control. A fitness function that can only ever pass is worth nothing — this proves the
    /// detector actually fires, including through the collection-unwrapping path above.
    /// </summary>
    [Fact(DisplayName = "ADR-007: the nested-record detector actually detects a leak")]
    public void TheDetectorFires_OnATypeThatDoesLeak()
    {
        Assert.NotEmpty(GraphTypesIn(typeof(Microsoft.Graph.Models.FileStorageContainer)));
        Assert.NotEmpty(GraphTypesIn(typeof(List<Microsoft.Graph.Models.FileStorageContainer>)));
        Assert.NotEmpty(GraphTypesIn(typeof(IReadOnlyList<Microsoft.Graph.Models.FileStorageContainer>)));

        // Negative control — BCL types and collections of them must NOT be reported.
        Assert.Empty(GraphTypesIn(typeof(string)));
        Assert.Empty(GraphTypesIn(typeof(DateTimeOffset?)));
        Assert.Empty(GraphTypesIn(typeof(IReadOnlyList<string>)));
    }
}
