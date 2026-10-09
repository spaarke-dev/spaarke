using System.Diagnostics;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// THE L2 CONTROL PLANE STARTS NO PROCESS (customer-provisioning-orchestration-r1 task 253, gap G38).
///
/// <para>The L2 Worker runs on App Service <c>linuxFxVersion: 'DOTNETCORE|10.0'</c>: no PowerShell, no pac CLI, no az CLI,
/// and its publish has no <c>scripts/</c> folder. A handler collaborator that starts a process therefore fails on the
/// first live run — and nothing showed it before T230a looked: H4b ran <c>pwsh -File …Configure-AppServiceSettings.generated.ps1</c>
/// and H6 ran <c>pac org update-settings</c> / <c>pac application install</c> behind a registered <c>IProcessRunner</c>. The
/// project rule (project CLAUDE.md §2): the L2 main site never shells out; the only PowerShell path in L2 is the H14a
/// Exchange sidecar, a separate container that is not a .NET assembly here.</para>
///
/// <para>The rule reads the COMPILED code of every <c>Sprk.Provisioning.ControlPlane.*</c> production assembly (Core, Api,
/// Worker — built by this project's <c>BuildL2ForCosmosGuard</c> target, located by
/// <see cref="RouteAuthorizationGuardTests.ServerProductionAssemblies"/>) and fails on any reference to
/// <see cref="ProcessStartInfo"/> or to <see cref="Process"/>'s constructor or <c>Start</c> — however the call is wrapped
/// (lambda, async state machine, helper). It covers every type in those assemblies, registered or not, so a retired
/// shell-out kept "on disk" fails too (task 253 deleted the last one, DotnetR3GateVerifier).</para>
///
/// <para><b>Limits, stated exactly</b>: a process started through reflection, <c>dynamic</c>, or a third-party library the
/// L2 code calls is not seen; the rule reads only the L2 assemblies' own IL.</para>
/// </summary>
public class ControlPlaneNoProcessStartGuardTests
{
    private const string ProcessType = "System.Diagnostics.Process";
    private const string ProcessStartInfoType = "System.Diagnostics.ProcessStartInfo";

    /// <summary>"file method: what it calls" for every call that builds a ProcessStartInfo or creates/starts a Process.</summary>
    private static List<string> ProcessStartViolations(IEnumerable<IlCallScan.FileUse> uses)
        => uses.Where(u => u.Target is { } t
                           && (t.Type == ProcessStartInfoType
                               || (t.Type == ProcessType && (t.Member == "Start" || t.Member == ".ctor"))))
            .Select(u => $"{u.File} {u.Method}: {u.OpCode} {u.Target!.Type}::{u.Target.Member}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

    [Fact(DisplayName = "Task 253: no compiled L2 control-plane code starts a process (G38 — the Worker host has no shell tools)")]
    public void NoControlPlaneCodeStartsAProcess()
    {
        var assemblies = RouteAuthorizationGuardTests.ServerProductionAssemblies()
            .Where(a => a.Assembly.StartsWith("Sprk.Provisioning.ControlPlane", StringComparison.Ordinal))
            .ToList();

        var violations = assemblies.SelectMany(a => ProcessStartViolations(IlCallScan.FileUses(a.Path))).ToList();

        Assert.True(
            violations.Count == 0,
            "The L2 Worker host (App Service DOTNETCORE|10.0) has no pwsh, pac or az and no scripts/ folder, so a process " +
            "start fails on the first live run (G38). Port the call to an SDK / REST client (ARM SDK, Dataverse Web API, " +
            "Graph); a PowerShell-only Exchange operation belongs in the H14a sidecar.\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: the three L2 production assemblies were all scanned (the Api project keeps the pre-split
        // assembly name Sprk.Provisioning.ControlPlane — its App Service entry point).
        Assert.Contains(assemblies, a => a.Assembly == "Sprk.Provisioning.ControlPlane.Core");
        Assert.Contains(assemblies, a => a.Assembly == "Sprk.Provisioning.ControlPlane.Worker");
        Assert.Contains(assemblies, a => a.Assembly == "Sprk.Provisioning.ControlPlane");
    }

    // Compiled fixtures for the control — real process starts, compiled into THIS assembly (never a scanned one).
    internal static class ProcessStartFixtures
    {
        internal static void StartsByName() => Process.Start("pwsh", "-File scripts/x.ps1")?.Dispose();

        internal static void StartsWithStartInfo()
        {
            var startInfo = new ProcessStartInfo { FileName = "pac", UseShellExecute = false };
            startInfo.ArgumentList.Add("org");
            using var process = new Process { StartInfo = startInfo };
            process.Start();
        }

        internal static async Task StartsInsideAnAsyncLambda()
        {
            Func<Task> run = async () =>
            {
                await Task.Yield();
                Process.Start(new ProcessStartInfo("az"))?.Dispose();
            };
            await run();
        }

        internal static int ReadsOwnProcessIdOnly() => Environment.ProcessId;
    }

    [Fact(DisplayName = "Task 253 controls: each compiled process start fails the rule; reading the process id does not")]
    public void ProcessStart_NegativeControl_EachCompiledStartFails()
    {
        var uses = IlCallScan.FileUses(typeof(ControlPlaneNoProcessStartGuardTests).Assembly.Location);
        var fixture = typeof(ProcessStartFixtures).FullName!;
        List<string> Of(string member) => ProcessStartViolations(uses.Where(u =>
            u.Method.StartsWith(fixture, StringComparison.Ordinal) && u.Method.Contains(member, StringComparison.Ordinal)));

        Assert.Contains(Of("StartsByName"), v => v.Contains($"{ProcessType}::Start", StringComparison.Ordinal));
        Assert.Contains(Of("StartsWithStartInfo"), v => v.Contains($"{ProcessStartInfoType}::.ctor", StringComparison.Ordinal));
        Assert.Contains(Of("StartsWithStartInfo"), v => v.Contains($"{ProcessType}::Start", StringComparison.Ordinal));
        Assert.Contains(Of("StartsInsideAnAsyncLambda"), v => v.Contains($"{ProcessType}::Start", StringComparison.Ordinal));

        Assert.Empty(Of("ReadsOwnProcessIdOnly"));
    }
}
