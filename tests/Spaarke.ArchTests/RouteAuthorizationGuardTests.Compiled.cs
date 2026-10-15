using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// NO SERVER CODE WRITES THE HOST ENVIRONMENT'S NAME (unified-access-control-r2 task 167 f2-v2, main-session round 52 item 2:
/// "an IL-scan ban on any write to <c>IHostEnvironment.EnvironmentName</c> / <c>IWebHostEnvironment.EnvironmentName</c> in
/// <c>src/server/**</c>").
///
/// <para>An <c>IsDevelopment()</c>-only route (the anonymous <c>POST /api/office/save-debug</c>) is development-only only while
/// nothing rewrites the name <c>IsDevelopment()</c> reads. The source rule could not see a write it was not looking for (the
/// f2-v1 verifier's <c>env.EnvironmentName = Environments.Development;</c> kept it green); the compiled assembly shows every
/// write as a call to a <c>set_EnvironmentName</c> — through either interface (<c>IWebHostEnvironment</c> inherits the
/// property from <c>IHostEnvironment</c>), through a lambda, helper or alias, and through the builder options that set the
/// name at start-up (<c>WebApplicationOptions</c>, <c>HostApplicationBuilderSettings</c>). The authority is the runtime proof
/// <see cref="NoDevelopmentOnlyRouteIsMappedInProduction"/> (the app booted as Production); this rule keeps the write out of
/// the code that ships.</para>
///
/// <para><b>Limits, stated exactly</b> (owner round 56: recorded, not built — class d). The rule reads calls to a FRAMEWORK
/// type's <c>EnvironmentName</c> setter in every production assembly built from <c>src/server</c>. It does not look for other
/// ways to choose an environment: <c>UseEnvironment</c>, <c>Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", …)</c>,
/// an <c>--environment</c> argument, a hand-written <c>IHostEnvironment</c>, reflection or an <c>[UnsafeAccessor]</c>. Test
/// assemblies are not scanned (a test fake environment does not ship).</para>
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    /// <summary>Each production project under <c>src/server</c> (every <c>.csproj</c> except test projects) and its built
    /// assembly: in this test's output when referenced, otherwise the newest under the project's own <c>bin/</c> (the L2
    /// control-plane projects, built by this project's <c>BuildL2ForCosmosGuard</c> target). Fails closed when one is not built.</summary>
    internal static IReadOnlyList<(string Assembly, string Path)> ServerProductionAssemblies()
    {
        var root = Path.Combine(SourceScan.RepoRoot, "src", "server");
        var result = new List<(string, string)>();
        var missing = new List<string>();
        foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var xml = File.ReadAllText(project);
            var name = Regex.Match(xml, @"<AssemblyName>\s*([^<\s]+)\s*</AssemblyName>") is { Success: true } m
                ? m.Groups[1].Value
                : Path.GetFileNameWithoutExtension(project);
            if (IsBuildOutput(project) || name.EndsWith(".Tests", StringComparison.Ordinal)
                || Regex.IsMatch(xml, @"<IsTestProject>\s*true\s*</IsTestProject>", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var beside = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            var bin = Path.Combine(Path.GetDirectoryName(project)!, "bin");
            var built = File.Exists(beside)
                ? beside
                : Directory.Exists(bin)
                    ? Directory.EnumerateFiles(bin, name + ".dll", SearchOption.AllDirectories)
                        .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault()
                    : null;
            if (built is null)
            {
                missing.Add(SourceScan.Relative(project).Replace('\\', '/'));
                continue;
            }

            result.Add((name, built));
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "These production projects under src/server are not built, so their compiled code cannot be scanned (fail closed): "
                + string.Join(", ", missing) + ". Build them, or add them to the BuildL2ForCosmosGuard target in Spaarke.ArchTests.csproj.");
        }

        return result;
    }

    /// <summary>"file method: writes Type.EnvironmentName" for every call to an <c>EnvironmentName</c> setter of a type the
    /// scanned assemblies do not define themselves (a DTO's own <c>EnvironmentName</c> property is not the host's).</summary>
    private static List<string> EnvironmentNameWriteViolations(IEnumerable<IlCallScan.FileUse> uses, IReadOnlySet<string> scannedAssemblies)
        => uses.Where(u => u.Target is { Member: "set_EnvironmentName" } t && !scannedAssemblies.Contains(t.Assembly))
            .Select(u => $"{u.File} {u.Method}: writes {u.Target!.Type}.EnvironmentName — IsDevelopment() then answers whatever was written")
            .ToList();

    [Fact(DisplayName = "Task 167 f2-v2: no compiled server code writes the host environment's EnvironmentName (round 52 item 2)")]
    public void NoServerCodeWritesTheHostEnvironmentName()
    {
        var assemblies = ServerProductionAssemblies();
        var scanned = assemblies.Select(a => a.Assembly).ToHashSet(StringComparer.Ordinal);
        var violations = assemblies.SelectMany(a => EnvironmentNameWriteViolations(IlCallScan.FileUses(a.Path), scanned)).ToList();
        Assert.True(
            violations.Count == 0,
            "A development-only route is development-only only while nothing rewrites the environment name IsDevelopment() reads. "
            + "These compiled instructions write it.\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: every production project under src/server is scanned — the BFF, its shared libraries and the L2 control
        // plane — and the IL reader sees the environment READS that do exist.
        Assert.Contains(assemblies, a => a.Assembly == "Sprk.Bff.Api");
        Assert.Contains(assemblies, a => a.Assembly == "Sprk.Provisioning.ControlPlane.Worker");
        Assert.True(assemblies.Count >= 7, $"only {assemblies.Count} production assemblies were scanned: {string.Join(", ", assemblies.Select(a => a.Assembly))}");
        Assert.Contains(IlCallScan.FileUses(assemblies.Single(a => a.Assembly == "Sprk.Bff.Api").Path),
            u => u.Target is { Member: "get_EnvironmentName", Type: "Microsoft.Extensions.Hosting.IHostEnvironment" });
    }

    // Compiled fixtures for the control — real writes, compiled into THIS assembly (never a scanned one), read by the same function.
    internal static class EnvironmentWriteFixtures
    {
        internal static void SetOnTheWebHostEnvironment(IWebHostEnvironment env) => env.EnvironmentName = Environments.Development;

        internal static void SetOnTheHostEnvironment(IHostEnvironment env) => env.EnvironmentName = "Development";

        internal static WebApplicationOptions BuilderOptions() => new() { EnvironmentName = "Development" };

        internal static bool ReadsOnly(IHostEnvironment env) => env.IsDevelopment() && env.EnvironmentName.Length > 0;
    }

    [Fact(DisplayName = "Task 167 f2-v2 controls: each compiled EnvironmentName write fails; reading the environment does not")]
    public void EnvironmentNameWrite_NegativeControl_EachCompiledWriteFails()
    {
        var self = typeof(RouteAuthorizationGuardTests).Assembly;
        var uses = IlCallScan.FileUses(self.Location);
        var fixture = typeof(EnvironmentWriteFixtures).FullName!;
        var scanned = new HashSet<string>(StringComparer.Ordinal) { self.GetName().Name! };
        List<string> Of(string member) => EnvironmentNameWriteViolations(uses.Where(u => u.Method == $"{fixture}::{member}"), scanned);

        // The f2-v1 verifier's seed (through IWebHostEnvironment the setter is IHostEnvironment's), the other interface, and
        // the start-up option.
        Assert.Contains(Of("SetOnTheWebHostEnvironment"), v => v.Contains("writes Microsoft.Extensions.Hosting.IHostEnvironment.EnvironmentName", StringComparison.Ordinal));
        Assert.Contains(Of("SetOnTheHostEnvironment"), v => v.Contains("writes Microsoft.Extensions.Hosting.IHostEnvironment.EnvironmentName", StringComparison.Ordinal));
        Assert.Contains(Of("BuilderOptions"), v => v.Contains("writes Microsoft.AspNetCore.Builder.WebApplicationOptions.EnvironmentName", StringComparison.Ordinal));

        // POSITIVE: reading the environment (IsDevelopment(), EnvironmentName) — what the BFF does.
        Assert.Empty(Of("ReadsOnly"));
    }
}
