using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-001 (as amended by A1, 2026-09-12): the BFF is a single Minimal API runtime, so the BFF ASSEMBLY carries no
/// Azure Functions or Durable Task packages and no Function-attributed members. Where background work runs is a
/// separate question, answered per workload by ADR-052 — a Functions project lives under
/// <c>src/server/functions/</c> and is covered by <see cref="WorkloadPlacementGuardTests"/>.
///
/// <para><b>Two defects fixed 2026-09-13 (unified-access-control-r2 task 102).</b> (1) The attribute test read
/// CLASS-level attributes only. Function attributes sit on METHODS (<c>[Function]</c> / <c>[FunctionName]</c>)
/// and trigger bindings on PARAMETERS (<c>[TimerTrigger]</c>, <c>[ServiceBusTrigger]</c> …), so it could never
/// fire. It now reads type, method and parameter attributes, and
/// <see cref="Detector_NegativeControl_FiresOnMethodAndParameterAttributes"/> proves it. (2) Its failure message
/// claimed Functions were not permitted anywhere — wrong since ADR-001's 2026-05-19 narrowing and contradicted by
/// ADR-052. It now states the rule's actual scope: the BFF assembly.</para>
/// </summary>
public class ADR001_MinimalApiTests
{
    private static readonly string[] ForbiddenNamespaces =
    {
        "Microsoft.Azure.WebJobs",
        "Microsoft.Azure.Functions",
        "Microsoft.DurableTask",
        "DurableTask.Core",
        "DurableTask.AzureStorage"
    };

    /// <summary>Attribute type names that mark an Azure Functions entry point or trigger binding.</summary>
    private static readonly HashSet<string> FunctionAttributeNames = new(StringComparer.Ordinal)
    {
        "FunctionAttribute",
        "FunctionNameAttribute",
        "TimerTriggerAttribute",
        "QueueTriggerAttribute",
        "ServiceBusTriggerAttribute",
        "HttpTriggerAttribute",
        "BlobTriggerAttribute",
        "EventGridTriggerAttribute",
        "EventHubTriggerAttribute",
        "CosmosDBTriggerAttribute",
        "OrchestrationTriggerAttribute",
        "ActivityTriggerAttribute",
        "EntityTriggerAttribute",
        "DurableClientAttribute",
    };

    private const string Guidance =
        "Azure Functions are not permitted inside the BFF assembly (ADR-001 A1): BFF endpoints are Minimal API, and " +
        "work that belongs in a Function goes in its own project under src/server/functions/<Name>/ (ADR-052 §5-§6), " +
        "never inside Sprk.Bff.Api. Durable Task likewise runs in its own host (ADR-052 §7).";

    [Fact(DisplayName = "ADR-001: the BFF assembly has no dependency on Azure Functions or Durable Task")]
    public void NoAzureFunctionsPackages()
    {
        var assembly = typeof(Program).Assembly;

        foreach (var forbiddenNamespace in ForbiddenNamespaces)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn(forbiddenNamespace)
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"ADR-001 violation: the BFF assembly depends on '{forbiddenNamespace}'. {Guidance} " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
        }
    }

    [Fact(DisplayName = "ADR-001: no type, method or parameter in the BFF assembly carries an Azure Functions attribute")]
    public void NoAzureFunctionsAttributes()
    {
        var hits = FindFunctionAttributes(LoadableTypes(typeof(Program).Assembly));

        Assert.True(hits.Count == 0, $"ADR-001 violation. {Guidance} Found: {string.Join(" | ", hits)}");
    }

    [Fact(DisplayName = "ADR-001: negative control — the detector fires on method- and parameter-level Function attributes")]
    public void Detector_NegativeControl_FiresOnMethodAndParameterAttributes()
    {
        var hits = FindFunctionAttributes(new[] { typeof(SeededFunctionHost) });

        Assert.Contains(hits, h => h.Contains(".Run:", StringComparison.Ordinal) && h.EndsWith("[FunctionAttribute]", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("(timer)", StringComparison.Ordinal) && h.EndsWith("[TimerTriggerAttribute]", StringComparison.Ordinal));

        // The pre-2026-09-13 detector read class-level attributes only — on this type it would have seen nothing.
        Assert.DoesNotContain(
            typeof(SeededFunctionHost).CustomAttributes,
            a => FunctionAttributeNames.Contains(a.AttributeType.Name));
    }

    [Fact(DisplayName = "ADR-001: positive control — the detector ignores ordinary attributes")]
    public void Detector_PositiveControl_IgnoresOrdinaryAttributes()
    {
        Assert.Empty(FindFunctionAttributes(new[] { typeof(SanctionedHost) }));
    }

    /// <summary>
    /// Every type-, method- and parameter-level attribute that marks an Azure Functions entry point or trigger:
    /// matched by name (so a stand-in with the same name is caught) or by a Functions / Durable Task namespace.
    /// </summary>
    internal static IReadOnlyList<string> FindFunctionAttributes(IEnumerable<Type> types)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                      BindingFlags.Static | BindingFlags.DeclaredOnly;
        var hits = new List<string>();

        foreach (var type in types)
        {
            var typeName = type.FullName ?? type.Name;
            AddHits(type.CustomAttributes, typeName, hits);

            foreach (var method in type.GetMethods(Declared))
            {
                var where = $"{typeName}.{method.Name}";
                AddHits(method.CustomAttributes, where, hits);

                foreach (var parameter in method.GetParameters())
                {
                    AddHits(parameter.CustomAttributes, $"{where}({parameter.Name})", hits);
                }
            }
        }

        return hits;
    }

    /// <summary>The assembly's types, tolerating members whose dependencies cannot be loaded.</summary>
    internal static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>();
        }
    }

    private static void AddHits(IEnumerable<CustomAttributeData> attributes, string where, List<string> hits)
    {
        foreach (var attribute in attributes)
        {
            var attributeType = attribute.AttributeType;
            var attributeNamespace = attributeType.Namespace ?? string.Empty;
            if (FunctionAttributeNames.Contains(attributeType.Name)
                || ForbiddenNamespaces.Any(ns => attributeNamespace.StartsWith(ns, StringComparison.Ordinal)))
            {
                hits.Add($"{where}: [{attributeType.Name}]");
            }
        }
    }

    // Stand-ins shaped like the isolated-worker attributes, so the controls need no Functions package.
    [AttributeUsage(AttributeTargets.Method)]
    private sealed class FunctionAttribute : Attribute
    {
        public FunctionAttribute(string name) => Name = name;

        public string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    private sealed class TimerTriggerAttribute : Attribute
    {
        public TimerTriggerAttribute(string schedule) => Schedule = schedule;

        public string Schedule { get; }
    }

    private static class SeededFunctionHost
    {
        [Function("Seeded")]
        public static void Run([TimerTrigger("0 */5 * * * *")] object timer) => _ = timer;
    }

    private static class SanctionedHost
    {
        [System.ComponentModel.Description("an endpoint handler")]
        public static void Handle([System.ComponentModel.Description("request")] object request) => _ = request;
    }
}
