using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 071 (spec FR-24, D-27; POML constraints "closed dispatch: no reflection, no registry, no expression evaluation"
/// and "handlers are pure: no Dataverse, HTTP, clock or randomness"): the billing rule handlers, their dispatch and the fact-set
/// and finding contracts they use reference nothing that does I/O, reads the clock, draws randomness, logs, reflects, builds
/// expressions or holds shared mutable state. The evaluation date and the reliability threshold come in the fact set.
/// </summary>
/// <remarks>
/// <para><b>How.</b> Every method reference in the compiled IL of the scanned types (<see cref="IlCallScan.MethodReferences"/>:
/// <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c>, <c>ldvirtftn</c>, <c>ldtoken</c> of a method), whatever lambda, local
/// function or state machine wraps it. A static field scan covers the registry shape (a static dictionary of handlers): the only
/// static state allowed is a readonly <c>string[]</c> lookup table.</para>
/// <para><b>Enforced by this test:</b> the rule "the billing handlers are pure" is a type-level fact of the compiled assembly, so
/// a prose rule would drift; a must-fire control fixture per forbidden shape (below) proves the scan detects it.</para>
/// <para><b>Not covered:</b> reflection reached through a type this list does not name, and a handler that gains an input from a
/// static the scan does not read (a new <c>static</c> field is caught by the static-state test). Both are code-review concerns.</para>
/// </remarks>
public sealed class BillingHandlerPurityGuardTests
{
    private const string HandlersNamespace = "Sprk.Bff.Api.Services.Signals.Billing.Handlers";

    /// <summary>Whole namespaces a pure handler never calls into.</summary>
    private static readonly string[] ForbiddenNamespaces =
    [
        "System.IO", "System.Net", "System.Data", "System.Reflection", "System.Diagnostics", "System.Threading",
        "System.Linq.Expressions", "System.Collections.Concurrent", "System.Security.Cryptography",
        "Microsoft.Xrm", "Microsoft.Extensions.Logging", "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Http",
        "Microsoft.Graph", "Azure", "Spaarke.Dataverse", "Sprk.Bff.Api.Services.Dataverse", "Sprk.Bff.Api.Infrastructure",
    ];

    /// <summary>Whole types a pure handler never calls (the clock, randomness, the environment, late binding, the console).</summary>
    private static readonly string[] ForbiddenTypes =
    [
        "System.TimeProvider", "System.Random", "System.Environment", "System.Activator", "System.Console", "System.AppContext",
    ];

    /// <summary>
    /// Single members: the ambient clock, a fresh identity (<c>NewGuid</c> and the time-ordered <c>CreateVersion7</c>), the
    /// machine's current culture and time zone are impure (the same input would give different findings on another machine);
    /// <c>Type.GetTypeFromHandle</c> (a <c>typeof</c>), <c>CultureInfo.InvariantCulture</c> and <c>TimeZoneInfo.Utc</c> are not.
    /// </summary>
    private static readonly (string Type, string Member)[] ForbiddenMembers =
    [
        ("System.Guid", "NewGuid"), ("System.Guid", "CreateVersion7"),
        ("System.DateTime", "get_Now"), ("System.DateTime", "get_UtcNow"), ("System.DateTime", "get_Today"),
        ("System.DateTimeOffset", "get_Now"), ("System.DateTimeOffset", "get_UtcNow"),
        ("System.Globalization.CultureInfo", "get_CurrentCulture"), ("System.Globalization.CultureInfo", "get_CurrentUICulture"),
        ("System.TimeZoneInfo", "get_Local"),
    ];

    private static readonly Type[] ContractTypes =
    [
        typeof(BillingFieldNames), typeof(BillingFactSet), typeof(BillingInvoiceFacts), typeof(BillingLineFacts),
        typeof(BillingCoreRecordFacts), typeof(BillingPriorSubmission), typeof(BillingPriorLine), typeof(BillingFinding),
        typeof(BillingSubject), typeof(BillingSuggestedAdjustment), typeof(BillingValueRead), typeof(BillingRuleOrigin),
        typeof(BillingReasonCodes), typeof(EmptyConfidence),
    ];

    private static readonly string[] HandlerTypeNames =
    [
        "ApprovedTimekeeperHandler", "ApprovedRateHandler", "TimeIncrementHandler", "DailyHoursCapHandler", "NonBillableActivityHandler",
        "ExpenseRuleHandler", "InvoiceAgeHandler", "CoreRecordWindowHandler", "DuplicateHandler", "RequiredDataHandler",
        "AiObservationHandler", "BillingRuleHandlers", "HandlerContext", "BillingMath", "BillingText", "FactValues",
    ];

    private static Type[] HandlerTypes() =>
        typeof(HandlerContext).Assembly.GetTypes().Where(t => t.Namespace == HandlersNamespace).ToArray();

    private static Type[] ScannedTypes() =>
        HandlerTypes().Concat(ContractTypes.SelectMany(IlCallScan.WithNested)).Distinct().ToArray();

    [Fact(DisplayName = "Task 071: the billing handlers and their contracts are pure (no I/O, clock, randomness, logging, reflection)")]
    public void HandlersReferenceNothingImpure()
    {
        var violations = Violations(ScannedTypes()).ToList();

        Assert.True(violations.Count == 0,
            "A billing handler or its contract references something impure. The handlers judge one rule against one fact set; "
            + "anything they would read from the world belongs in BillingFactSet (assembled by task 072). "
            + string.Join("; ", violations));
    }

    [Fact(DisplayName = "Task 071: the purity scan is not vacuous (it reads all eleven handlers, the dispatch and the contracts)")]
    public void TheScanSeesTheHandlers()
    {
        var scanned = ScannedTypes();
        var names = scanned.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var expected in HandlerTypeNames)
        {
            Assert.Contains(expected, names);
        }

        var references = IlCallScan.MethodReferences(scanned).ToList();
        Assert.True(references.Count > 200, $"the scan found only {references.Count} method references; it is not reading the handler bodies");
        Assert.Contains(references, r => r.Target.DeclaringType?.FullName == "System.Decimal" && r.Target.Name == "Round");
    }

    // Scoped to the handler namespace, where a registry would live; the contracts hold one benign immutable singleton (EmptyConfidence).
    [Fact(DisplayName = "Task 071: the dispatch holds no registry or shared mutable state (the only static state is a readonly string[] table)")]
    public void NoMutableStaticStateOrRegistry()
    {
        var violations = StaticStateViolations(HandlerTypes()).ToList();

        Assert.True(violations.Count == 0,
            "The dispatch is a closed switch (FR-24, D-27). Static state in the handlers is limited to readonly string[] lookup tables: "
            + string.Join("; ", violations));
    }

    [Fact(DisplayName = "Task 071: the dispatch is a switch over RuleType, not a lookup (no Dictionary or Func registry in BillingRuleHandlers)")]
    public void TheDispatchBuildsNoCollectionOfHandlers()
    {
        var dispatch = typeof(BillingRuleHandlers);
        var collectionConstruction = IlCallScan.MethodReferences(IlCallScan.WithNested(dispatch))
            .Where(r => r.Target.DeclaringType is { IsGenericType: true } d
                        && (d.GetGenericTypeDefinition() == typeof(Dictionary<,>) || d.GetGenericTypeDefinition() == typeof(List<>)
                            || d.GetGenericTypeDefinition() == typeof(HashSet<>)))
            .ToList();

        Assert.Empty(collectionConstruction);
    }

    [Theory(DisplayName = "Task 071: the purity guard fires on each forbidden shape (must-fire controls)")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.ReadsTheClock), "DateTime")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.ReadsTheClockWithOffset), "DateTimeOffset")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.ReadsAProvider), "TimeProvider")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.DrawsRandomness), "Random")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.MintsAnIdentity), "Guid")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.MintsATimeOrderedIdentity), "System.Guid.CreateVersion7")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.ReadsTheCurrentCulture), "System.Globalization.CultureInfo.get_CurrentCulture")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.ReadsTheCurrentUiCulture), "System.Globalization.CultureInfo.get_CurrentUICulture")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.ReadsTheLocalTimeZone), "System.TimeZoneInfo.get_Local")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.ReadsAFile), "System.IO")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.CallsTheNetwork), "System.Net")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.ReadsTheEnvironment), "Environment")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.Reflects), "System.Reflection")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.GetsATypeByName), "late binding")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.BindsLate), "Activator")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.Times), "System.Diagnostics")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.Logs), "Microsoft.Extensions.Logging")]
    [InlineData(nameof(BillingHandlerPurityControlFixtures.BuildsAnExpression), "System.Linq.Expressions")]
    public void EveryDetectorFiresOnItsControlFixture(string fixtureMethod, string expectedFragment)
    {
        var violations = Violations(IlCallScan.WithNested(typeof(BillingHandlerPurityControlFixtures)))
            .Where(v => v.StartsWith($"{nameof(BillingHandlerPurityControlFixtures)}::{fixtureMethod} calls ", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(violations, v => v.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 071: the static-state guard fires on a handler registry (must-fire control) and not on a readonly string[] (must-not-fire control)")]
    public void TheStaticStateDetectorFires_AndAllowsTheLookupTable()
    {
        var fires = StaticStateViolations([typeof(BillingHandlerPurityControlFixtures.RegistryControl)]).ToList();
        Assert.Contains(fires, v => v.Contains("Registry", StringComparison.Ordinal));
        Assert.Contains(fires, v => v.Contains("Mutable", StringComparison.Ordinal));

        var quiet = StaticStateViolations([typeof(BillingHandlerPurityControlFixtures.LookupTableControl)]).ToList();
        Assert.Empty(quiet);
    }

    [Fact(DisplayName = "Task 071: a pure method is not flagged (must-not-fire control)")]
    public void APureMethodIsNotFlagged()
    {
        var violations = Violations([typeof(BillingHandlerPurityControlFixtures.PureControl)]).ToList();

        Assert.Empty(violations);
    }

    // ── The scan ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<string> Violations(IEnumerable<Type> types)
    {
        foreach (var (caller, callerMethod, target) in IlCallScan.MethodReferences(types))
        {
            var declaring = target.DeclaringType;
            if (declaring is null)
            {
                continue;
            }

            var typeName = declaring.IsGenericType ? declaring.GetGenericTypeDefinition().FullName! : declaring.FullName!;
            var reason =
                ForbiddenNamespaces.FirstOrDefault(ns => typeName.StartsWith(ns + ".", StringComparison.Ordinal))
                ?? ForbiddenTypes.FirstOrDefault(t => typeName == t)
                ?? ForbiddenMembers.Where(m => m.Type == typeName && m.Member == target.Name).Select(m => $"{m.Type}.{m.Member}").FirstOrDefault()
                ?? (typeName == "System.Type" && target.Name != "GetTypeFromHandle" && target.Name != "op_Equality" && target.Name != "op_Inequality"
                    ? "System.Type late binding"
                    : null);

            if (reason is not null)
            {
                yield return $"{IlCallScan.Outermost(caller).Name}::{callerMethod.Name} calls {typeName}::{target.Name} ({reason})";
            }
        }
    }

    private static IEnumerable<string> StaticStateViolations(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            if (type.IsDefined(typeof(CompilerGeneratedAttribute), false))
            {
                continue; // closure and method-group caches the compiler adds are not authored state
            }

            const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in type.GetFields(all))
            {
                if (field.IsLiteral)
                {
                    continue; // a const
                }

                if (field.IsInitOnly && field.FieldType == typeof(string[]))
                {
                    continue; // a readonly lookup table
                }

                var kind = field.IsInitOnly ? "Registry-shaped" : "Mutable";
                yield return $"{type.Name}.{field.Name} is static {field.FieldType.Name} ({kind})";
            }
        }
    }
}

/// <summary>NEGATIVE CONTROLS for <see cref="BillingHandlerPurityGuardTests"/> — never called. Each commits one shape the guard forbids;
/// they live in the test assembly, so they are not themselves violations (the rule scans <c>Sprk.Bff.Api</c>).</summary>
internal static class BillingHandlerPurityControlFixtures
{
    internal static DateTime ReadsTheClock() => DateTime.UtcNow;

    internal static DateTimeOffset ReadsTheClockWithOffset() => DateTimeOffset.UtcNow;

    internal static DateTimeOffset ReadsAProvider() => TimeProvider.System.GetUtcNow();

    internal static int DrawsRandomness() => new Random().Next();

    internal static Guid MintsAnIdentity() => Guid.NewGuid();

    internal static Guid MintsATimeOrderedIdentity() => Guid.CreateVersion7();

    internal static CultureInfo ReadsTheCurrentCulture() => CultureInfo.CurrentCulture;

    internal static CultureInfo ReadsTheCurrentUiCulture() => CultureInfo.CurrentUICulture;

    internal static TimeZoneInfo ReadsTheLocalTimeZone() => TimeZoneInfo.Local;

    internal static string ReadsAFile() => File.ReadAllText("rules.json");

    internal static HttpClient CallsTheNetwork() => new();

    internal static string? ReadsTheEnvironment() => Environment.GetEnvironmentVariable("X");

    internal static object? Reflects() => typeof(BillingHandlerPurityControlFixtures).GetMethod("ReadsTheClock")?.Invoke(null, null);

    internal static Type? GetsATypeByName() => Type.GetType("System.String");

    internal static object? BindsLate() => Activator.CreateInstance(typeof(object));

    internal static long Times() => System.Diagnostics.Stopwatch.GetTimestamp();

    internal static void Logs(Microsoft.Extensions.Logging.ILogger logger) =>
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "x");

    internal static System.Linq.Expressions.Expression<Func<int>> BuildsAnExpression() => () => 1;

    /// <summary>A handler-shaped pure method: arithmetic and text only. Must not be flagged.</summary>
    internal static class PureControl
    {
        internal static string Judge(decimal hours, decimal increment) =>
            (hours % increment) == 0m ? "holds" : decimal.Round(hours, 2, MidpointRounding.AwayFromZero).ToString("0.##");

        /// <summary>The ambient-state siblings a handler MAY use: the invariant culture, UTC, a parsed or empty identity. The
        /// forbidden members are member-specific, so none of these is flagged.</summary>
        internal static string InvariantAndDeterministic(decimal value, string id) =>
            value.ToString("F2", CultureInfo.InvariantCulture)
            + TimeZoneInfo.Utc.Id
            + (Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty);
    }

    /// <summary>A registry of handlers: a static dictionary keyed by rule type. The guard must fire on it.</summary>
    internal static class RegistryControl
    {
        internal static readonly Dictionary<string, Func<int>> Registry = new();

        internal static int Mutable = 1;
    }

    /// <summary>A readonly lookup table of strings: the one static state the handlers may hold.</summary>
    internal static class LookupTableControl
    {
        internal static readonly string[] Table = ["a", "b"];

        internal const string Constant = "c";
    }
}
