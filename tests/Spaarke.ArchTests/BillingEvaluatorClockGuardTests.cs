using System.Reflection;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 072 (POML constraint "no clock reads inside the evaluator (dates come from the invoice)"; 072 review A3): the
/// in-memory evaluation (<c>InvoiceComplianceEvaluator.Evaluate</c>) and the Signal mapping (<see cref="BillingFindingSignalMapper"/>)
/// never read the clock, directly or through any <c>Sprk.Bff.Api</c> method they reach. The judging date of a subject with no line
/// date and no invoice date is <c>BillingEvaluationRequest.ReceivedOn</c>, data that task 049 supplies.
/// </summary>
/// <remarks>
/// <para><b>Why a separate guard.</b> <see cref="BillingHandlerPurityGuardTests"/> covers the handlers and their contracts, not the
/// evaluator (which logs, meters and calls the gate, so the handlers' full purity list does not apply to it). This guard checks the
/// one property the evaluator owes: determinism in time.</para>
/// <para><b>How.</b> The compiled IL of the roots and, transitively, of every method declared in <c>Sprk.Bff.Api</c> they reach
/// (<see cref="IlCallScan"/>), once per method. The fact loader (<c>EvaluateAsync</c>'s I/O half) is not a root.</para>
/// <para><b>Enforced by this test.</b> A must-fire control (a clock read two calls deep) and a must-not-fire control (a pure chain).</para>
/// <para><b>Not covered:</b> a call through a delegate, an interface or a virtual method chosen at run time (the handler dispatch is
/// a delegate; the handlers are covered by the 071 purity guard), a static field load (<c>ldsfld</c>) and a type initialiser
/// (<c>.cctor</c>): the scan follows method references only, and a type initialiser is never referenced in IL.</para>
/// </remarks>
public sealed class BillingEvaluatorClockGuardTests
{
    private static readonly Assembly BffAssembly = typeof(InvoiceComplianceEvaluator).Assembly;

    /// <summary>The clock: the ambient now and today, a time provider, and a stopwatch.</summary>
    private static readonly (string Type, string? Member)[] ClockReads =
    [
        ("System.DateTime", "get_Now"), ("System.DateTime", "get_UtcNow"), ("System.DateTime", "get_Today"),
        ("System.DateTimeOffset", "get_Now"), ("System.DateTimeOffset", "get_UtcNow"),
        ("System.TimeProvider", null), ("System.Diagnostics.Stopwatch", null), ("System.Environment", "get_TickCount"),
        ("System.Environment", "get_TickCount64"),
    ];

    private static IEnumerable<MethodBase> Roots()
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        yield return typeof(InvoiceComplianceEvaluator).GetMethod("Evaluate", all)
                     ?? throw new InvalidOperationException("InvoiceComplianceEvaluator.Evaluate was not found; the guard would be vacuous.");
        foreach (var body in IlCallScan.DeclaredBodies(typeof(BillingFindingSignalMapper)))
        {
            yield return body;
        }
    }

    [Fact(DisplayName = "Task 072 (A3): the in-memory evaluation and the Signal mapping never read the clock")]
    public void TheEvaluatorAndTheMapperNeverReadTheClock()
    {
        var reached = Reach(Roots(), BffAssembly).ToList();
        var violations = Violations(reached).ToList();

        Assert.True(reached.Count > 100, $"the scan reached only {reached.Count} references; it is not reading the evaluator");
        Assert.Contains(reached, r => IlCallScan.Outermost(r.Caller.DeclaringType!) == typeof(InvoiceComplianceEvaluator) && r.Target.Name == "SelectVersion");
        Assert.True(violations.Count == 0,
            "The billing evaluator or mapper reads the clock. Dates come from the request (line date, invoice date, ReceivedOn): "
            + string.Join("; ", violations));
    }

    [Fact(DisplayName = "Task 072 (A3): the clock guard fires on a clock read two calls deep (must-fire control)")]
    public void TheGuardFiresOnAClockReadTwoCallsDeep()
    {
        var roots = IlCallScan.DeclaredBodies(typeof(ClockGuardControlEntry));
        var follow = typeof(ClockGuardControlEntry).Assembly;

        var violations = Violations(Reach(roots, follow)).ToList();

        Assert.Contains(violations, v => v.Contains("System.DateTime::get_UtcNow", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("System.TimeProvider", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 072 (A3): the clock guard does not fire on a pure chain that uses dates as data (must-not-fire control)")]
    public void TheGuardDoesNotFireOnDatesAsData()
    {
        var roots = IlCallScan.DeclaredBodies(typeof(ClockGuardControlPure));

        Assert.Empty(Violations(Reach(roots, typeof(ClockGuardControlPure).Assembly)));
    }

    private static IEnumerable<(MethodBase Caller, MethodBase Target)> Reach(IEnumerable<MethodBase> roots, Assembly follow)
    {
        var visited = new HashSet<(Module, int)>();
        var pending = new Queue<MethodBase>();

        void Enqueue(MethodBase method)
        {
            var definition = method.Module.ResolveMethod(method.MetadataToken)!;
            if (visited.Add((definition.Module, definition.MetadataToken)))
            {
                pending.Enqueue(definition);
                if (definition is MethodInfo info && info.GetCustomAttribute<System.Runtime.CompilerServices.StateMachineAttribute>() is not null)
                {
                    Enqueue(IlCallScan.CompiledBody(info));
                }
            }
        }

        foreach (var root in roots)
        {
            Enqueue(root);
        }

        while (pending.TryDequeue(out var current))
        {
            foreach (var target in IlCallScan.ReferencedMethods(current))
            {
                yield return (current, target);
                if (target.DeclaringType?.Assembly == follow)
                {
                    Enqueue(target);
                }
            }
        }
    }

    private static IEnumerable<string> Violations(IEnumerable<(MethodBase Caller, MethodBase Target)> references)
    {
        foreach (var (caller, target) in references)
        {
            var declaring = target.DeclaringType;
            if (declaring is null)
            {
                continue;
            }

            var typeName = declaring.IsGenericType ? declaring.GetGenericTypeDefinition().FullName! : declaring.FullName!;
            if (ClockReads.Any(c => c.Type == typeName && (c.Member is null || c.Member == target.Name)))
            {
                yield return $"{IlCallScan.Outermost(caller.DeclaringType!).Name}::{caller.Name} calls {typeName}::{target.Name}";
            }
        }
    }
}

/// <summary>MUST-FIRE control for <see cref="BillingEvaluatorClockGuardTests"/>: never called. The clock is two calls away.</summary>
internal static class ClockGuardControlEntry
{
    internal static DateOnly JudgingDate(DateOnly? lineDate) => lineDate ?? ClockGuardControlHelper.Today();

    internal static DateTimeOffset ViaAProvider() => ClockGuardControlHelper.Provider();
}

internal static class ClockGuardControlHelper
{
    internal static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    internal static DateTimeOffset Provider() => TimeProvider.System.GetUtcNow();
}

/// <summary>MUST-NOT-FIRE control: dates handled as data only.</summary>
internal static class ClockGuardControlPure
{
    internal static DateOnly JudgingDate(DateOnly? lineDate, DateOnly? invoiceDate, DateOnly receivedOn) =>
        Earlier(lineDate ?? invoiceDate ?? receivedOn, receivedOn);

    private static DateOnly Earlier(DateOnly a, DateOnly b) => a < b ? a : b.AddDays(0);
}
