using Microsoft.AspNetCore.Http;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ONE DETACHED-RUN HELPER (spaarkeai-mcp-bff-api-r2 task 030; ADR-052 amendment 2026-11-26, condition 2; owner decision
/// D-030-run-model).
///
/// <para>A <see cref="DefaultHttpContext"/> built by production code is a request context that no request owns: work that runs on it
/// acts as whoever its principal and bearer header say, outside the request's lifetime. The amendment allows exactly one such helper,
/// <c>Sprk.Bff.Api.Services.Ai.PlaybookRuns.DetachedPlaybookRunner</c>, which carries the bounds the amendment requires (memory-only token,
/// no ExecutionContext flow into the request's accessor, a cap, shutdown, fail-closed auth). The three existing builders are a ratchet
/// baseline (Compose's best-effort profile, the Decision route cores' scratch context, the assign-task notifier): they may stay, and they
/// leave the list when they move onto the helper (#3224). A new builder anywhere fails; so does a baseline entry that no longer builds
/// one (the list only shrinks).</para>
///
/// <para>The rule reads the COMPILED code of every production assembly under <c>src/server</c>
/// (<see cref="RouteAuthorizationGuardTests.ServerProductionAssemblies"/>) and finds every <c>newobj</c> of
/// <c>Microsoft.AspNetCore.Http.DefaultHttpContext::.ctor</c>, however it is written (object initializer, target-typed <c>new()</c>,
/// lambda, local function, async state machine): each is an ordinary method body in IL. The owner of a use is the outermost type of the
/// method that holds it.</para>
///
/// <para><b>Limits, stated exactly</b>: a context built through reflection, <c>Activator</c>, a factory such as
/// <c>IHttpContextFactory.Create</c>, or a subclass of <see cref="HttpContext"/> is not seen; the rule reads only
/// <see cref="DefaultHttpContext"/>'s constructor. Test assemblies are not scanned.</para>
/// </summary>
public class DetachedHttpContextGuardTests
{
    private const string DefaultHttpContextType = "Microsoft.AspNetCore.Http.DefaultHttpContext";

    /// <summary>The one helper the amendment allows.</summary>
    private const string Helper = "Sprk.Bff.Api.Services.Ai.PlaybookRuns.DetachedPlaybookRunner";

    /// <summary>The ratchet baseline at task 030: builders that existed before the amendment. Remove an entry when it moves onto the helper.</summary>
    private static readonly IReadOnlySet<string> Baseline = new HashSet<string>(StringComparer.Ordinal)
    {
        "Sprk.Bff.Api.Services.Compose.ComposeProfileDispatcher",       // best-effort profile after a Compose save (#3224 moves it)
        "Sprk.Bff.Api.Services.Signals.Actions.DecisionRouteCores",      // a scratch context inside the caller's own request
        "Sprk.Bff.Api.Services.Signals.AssignTaskNotifier",              // the assign-task notice (r1 ISS-010 file; not edited here)
    };

    /// <summary>The outermost type of each method that builds a <see cref="DefaultHttpContext"/>, with "file method" for the message.</summary>
    internal static List<(string Owner, string Where)> Builders(IEnumerable<IlCallScan.FileUse> uses) =>
        uses.Where(u => u.OpCode == "newobj" && u.Target is { Type: DefaultHttpContextType, Member: ".ctor" })
            .Select(u => (Owner: OutermostType(u.Method), Where: $"{u.File} {u.Method}"))
            .Distinct()
            .ToList();

    /// <summary>"Ns.Outer+<Inner>d__3::MoveNext" → "Ns.Outer".</summary>
    private static string OutermostType(string method)
    {
        var type = method[..method.IndexOf("::", StringComparison.Ordinal)];
        var nested = type.IndexOf('+');
        return nested < 0 ? type : type[..nested];
    }

    [Fact(DisplayName = "Task 030: only DetachedPlaybookRunner (plus the shrinking baseline) builds a DefaultHttpContext in production code")]
    public void OnlyTheHelperBuildsADetachedHttpContext()
    {
        var assemblies = RouteAuthorizationGuardTests.ServerProductionAssemblies();
        var builders = assemblies.SelectMany(a => Builders(IlCallScan.FileUses(a.Path))).ToList();

        var violations = builders.Where(b => b.Owner != Helper && !Baseline.Contains(b.Owner)).Select(b => b.Where).ToList();
        Assert.True(
            violations.Count == 0,
            "A production DefaultHttpContext is a request context no request owns (ADR-052 amendment 2026-11-26, condition 2). Run detached "
            + "caller work through IPlaybookRunAi / DetachedPlaybookRunner, or bring the change to the owner as an ADR-052 amendment.\n\n  "
            + string.Join("\n  ", violations));

        var stale = Baseline.Where(owner => builders.All(b => b.Owner != owner)).ToList();
        Assert.True(
            stale.Count == 0,
            "These baseline entries no longer build a DefaultHttpContext; remove them from DetachedHttpContextGuardTests.Baseline so the "
            + "list only shrinks:\n  " + string.Join("\n  ", stale));

        // Non-vacuous: the BFF was scanned and the helper itself was found.
        Assert.Contains(assemblies, a => a.Assembly == "Sprk.Bff.Api");
        Assert.Contains(builders, b => b.Owner == Helper);
    }

    // Compiled fixtures for the controls: real DefaultHttpContext builds, compiled into THIS assembly (never a scanned one).
    internal static class Fixtures
    {
        internal static HttpContext Direct() => new DefaultHttpContext();

        internal static HttpContext WithInitializer(IServiceProvider services) => new DefaultHttpContext { RequestServices = services };

        internal static HttpContext TargetTyped()
        {
            DefaultHttpContext context = new();
            return context;
        }

        internal static Func<HttpContext> InALambda() => () => new DefaultHttpContext();

        internal static async Task<HttpContext> InAnAsyncMethod()
        {
            await Task.Yield();
            return new DefaultHttpContext();
        }

        internal static string? ReadsAnExistingContext(HttpContext context) => context.TraceIdentifier;

        internal static HttpContextAccessor BuildsAnAccessorOnly() => new();
    }

    [Fact(DisplayName = "Task 030 controls: each compiled DefaultHttpContext build is found; reading a context or building an accessor is not")]
    public void Controls_EachBuildIsFound_AndNothingElse()
    {
        var uses = IlCallScan.FileUses(typeof(DetachedHttpContextGuardTests).Assembly.Location);
        var fixture = typeof(Fixtures).FullName!;
        List<(string Owner, string Where)> Of(string member) =>
            Builders(uses.Where(u => u.Method.StartsWith(fixture, StringComparison.Ordinal) && u.Method.Contains(member, StringComparison.Ordinal)));

        // Must fire.
        Assert.NotEmpty(Of("Direct"));
        Assert.NotEmpty(Of("WithInitializer"));
        Assert.NotEmpty(Of("TargetTyped"));
        Assert.NotEmpty(Of("InALambda"));             // the lambda body is "<InALambda>b__…" in the display class
        Assert.NotEmpty(Of("InAnAsyncMethod"));       // the state machine "<InAnAsyncMethod>d__…::MoveNext"
        Assert.All(Of("InAnAsyncMethod"), b => Assert.Equal(typeof(DetachedHttpContextGuardTests).FullName, b.Owner));

        // Must not fire.
        Assert.Empty(Of("ReadsAnExistingContext"));
        Assert.Empty(Of("BuildsAnAccessorOnly"));
    }
}
