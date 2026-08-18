// -----------------------------------------------------------------------------
// DotnetR3GateVerifier.cs
//
// Production <see cref="IR3GateVerifier"/> implementation. Runs each of the
// five r3-era gates in a deterministic order via <c>dotnet build</c> +
// <c>dotnet test</c> + <c>pwsh</c> shell-outs, treating gates whose artifacts
// have not yet landed in the tree as <see cref="R3GateStatus.Skipped"/>.
//
// SPEC / DESIGN references:
//   - spec.md §5.5 r3-era gates: analyzers-as-errors (r3 task 041), god-class
//     ratchet (r3 task 040), 4 new ArchTests (r1 task 064), naming-conformance
//     (r3 task 063), Graph app-role parity (r3 task 062 / r1 task 067).
//   - spec.md FR-12 acceptance criterion 2: distinct rejection code per
//     failing gate (BffDeployRejectionCodes.R3GateFailed*).
//
// GATE-NOT-YET-AVAILABLE POSTURE (per POML mandatory pre-work #8 + IR3GateVerifier
// file header): tasks 064 (I1-I5 ArchTests) + 067 (Graph parity) + 063
// (naming-conformance) are not landed at H9 authoring time. Missing gate
// artifacts are reported as <see cref="R3GateStatus.Skipped"/> — logged
// prominently but do NOT block the deploy. Once the pending artifacts land,
// the verifier auto-picks them up (filesystem-based discovery, not compile-
// time-bound).
//
// CI COVERAGE:
//   Not exercised by CI unit suite (dotnet CLI + pwsh + Azure — parity with
//   BicepInfraDeploy's ProvisionCustomerScriptBicepDeployRunner CI exclusion).
//   Handler unit tests via IR3GateVerifier stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Shells out to dotnet CLI + pwsh to run the five r3-era gates.
/// </summary>
public sealed class DotnetR3GateVerifier : IR3GateVerifier
{
    private readonly BffDeployOptions _options;
    private readonly ILogger<DotnetR3GateVerifier> _logger;

    public DotnetR3GateVerifier(
        IOptions<BffDeployOptions> options,
        ILogger<DotnetR3GateVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<R3GateVerificationResult> VerifyAsync(
        R3GateVerificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcomes = new List<R3GateOutcome>(5);

        outcomes.Add(await RunAnalyzersGateAsync(request, cancellationToken).ConfigureAwait(false));
        outcomes.Add(await RunGodClassRatchetGateAsync(request, cancellationToken).ConfigureAwait(false));
        outcomes.Add(await RunArchTestsGateAsync(request, cancellationToken).ConfigureAwait(false));
        outcomes.Add(await RunNamingConformanceGateAsync(request, cancellationToken).ConfigureAwait(false));
        outcomes.Add(await RunGraphAppRoleParityGateAsync(request, cancellationToken).ConfigureAwait(false));

        return new R3GateVerificationResult(outcomes);
    }

    private async Task<R3GateOutcome> RunAnalyzersGateAsync(
        R3GateVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var bffProject = Path.Combine(_options.RepositoryRoot, "src", "server", "api", "Sprk.Bff.Api");
        if (!Directory.Exists(bffProject))
        {
            return new R3GateOutcome(
                R3GateKind.Analyzers,
                R3GateStatus.Skipped,
                $"BFF project directory not found at '{bffProject}' — analyzers gate skipped (interim posture per IR3GateVerifier file header).");
        }

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            _options.DotnetExecutable,
            new[] { "build", bffProject, "-c", "Release", "--verbosity", "minimal", "/warnaserror" },
            request.RunId,
            R3GateKind.Analyzers,
            cancellationToken).ConfigureAwait(false);

        if (exitCode == 0)
        {
            return new R3GateOutcome(R3GateKind.Analyzers, R3GateStatus.Passed, string.Empty);
        }
        return new R3GateOutcome(
            R3GateKind.Analyzers,
            R3GateStatus.Failed,
            $"dotnet build exit {exitCode}. Stderr: {Truncate(stderr, 600)} Stdout tail: {Truncate(stdout, 400)}");
    }

    private async Task<R3GateOutcome> RunGodClassRatchetGateAsync(
        R3GateVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var archTestsProject = Path.Combine(_options.RepositoryRoot, "tests", "Spaarke.ArchTests");
        if (!Directory.Exists(archTestsProject))
        {
            return new R3GateOutcome(
                R3GateKind.GodClassRatchet,
                R3GateStatus.Skipped,
                $"ArchTests project directory not found at '{archTestsProject}' — god-class ratchet gate skipped.");
        }

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            _options.DotnetExecutable,
            new[] { "test", archTestsProject, "-c", "Release", "--filter", "FullyQualifiedName~GodClassGuardTests", "--no-build", "--verbosity", "minimal" },
            request.RunId,
            R3GateKind.GodClassRatchet,
            cancellationToken).ConfigureAwait(false);

        if (exitCode == 0)
        {
            return new R3GateOutcome(R3GateKind.GodClassRatchet, R3GateStatus.Passed, string.Empty);
        }
        return new R3GateOutcome(
            R3GateKind.GodClassRatchet,
            R3GateStatus.Failed,
            $"dotnet test (GodClassGuardTests) exit {exitCode}. Stderr: {Truncate(stderr, 600)} Stdout tail: {Truncate(stdout, 400)}");
    }

    private async Task<R3GateOutcome> RunArchTestsGateAsync(
        R3GateVerificationRequest request,
        CancellationToken cancellationToken)
    {
        // r1 task 064 lands 4 new tenant-isolation ArchTests (I1–I5) as
        // separate test classes in tests/Spaarke.ArchTests/. Until that task
        // ships, the filter matches no test cases and dotnet test reports
        // "no tests found" (exit code 1 by default, but treated as SKIPPED
        // per the pending-artifact posture — we don't want to block deploys
        // on a gate that has not been authored).
        var archTestsProject = Path.Combine(_options.RepositoryRoot, "tests", "Spaarke.ArchTests");
        if (!Directory.Exists(archTestsProject))
        {
            return new R3GateOutcome(
                R3GateKind.ArchTests,
                R3GateStatus.Skipped,
                $"ArchTests project directory not found at '{archTestsProject}' — tenant-isolation ArchTests gate skipped.");
        }

        // The 4 new ArchTests share a common naming prefix (TenantIsolation)
        // agreed with r1 task 064. Absence-detection via a filesystem scan
        // avoids fragile dotnet-test-exit-code-1-means-no-tests interpretation.
        var candidateFiles = Directory.GetFiles(archTestsProject, "TenantIsolation*Tests.cs", SearchOption.TopDirectoryOnly);
        if (candidateFiles.Length == 0)
        {
            return new R3GateOutcome(
                R3GateKind.ArchTests,
                R3GateStatus.Skipped,
                "No TenantIsolation*Tests.cs found in tests/Spaarke.ArchTests/ — r1 task 064 not yet landed (interim posture per IR3GateVerifier file header).");
        }

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            _options.DotnetExecutable,
            new[] { "test", archTestsProject, "-c", "Release", "--filter", "FullyQualifiedName~TenantIsolation", "--no-build", "--verbosity", "minimal" },
            request.RunId,
            R3GateKind.ArchTests,
            cancellationToken).ConfigureAwait(false);

        if (exitCode == 0)
        {
            return new R3GateOutcome(R3GateKind.ArchTests, R3GateStatus.Passed, string.Empty);
        }
        return new R3GateOutcome(
            R3GateKind.ArchTests,
            R3GateStatus.Failed,
            $"dotnet test (TenantIsolation ArchTests) exit {exitCode}. Stderr: {Truncate(stderr, 600)} Stdout tail: {Truncate(stdout, 400)}");
    }

    private async Task<R3GateOutcome> RunNamingConformanceGateAsync(
        R3GateVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var scriptPath = _options.NamingConformanceScriptPath;
        if (!File.Exists(scriptPath))
        {
            return new R3GateOutcome(
                R3GateKind.NamingConformance,
                R3GateStatus.Skipped,
                $"naming-conformance-check.ps1 not found at '{scriptPath}' — r3 task 063 script not yet at this publish location.");
        }

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            _options.PwshExecutable,
            new[] { "-NoProfile", "-NonInteractive", "-File", scriptPath, "-CustomerId", request.CustomerId },
            request.RunId,
            R3GateKind.NamingConformance,
            cancellationToken).ConfigureAwait(false);

        if (exitCode == 0)
        {
            return new R3GateOutcome(R3GateKind.NamingConformance, R3GateStatus.Passed, string.Empty);
        }
        return new R3GateOutcome(
            R3GateKind.NamingConformance,
            R3GateStatus.Failed,
            $"naming-conformance-check.ps1 exit {exitCode}. Stderr: {Truncate(stderr, 600)} Stdout tail: {Truncate(stdout, 400)}");
    }

    private async Task<R3GateOutcome> RunGraphAppRoleParityGateAsync(
        R3GateVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var archTestsProject = Path.Combine(_options.RepositoryRoot, "tests", "Spaarke.ArchTests");
        if (!Directory.Exists(archTestsProject))
        {
            return new R3GateOutcome(
                R3GateKind.GraphAppRoleParity,
                R3GateStatus.Skipped,
                $"ArchTests project directory not found at '{archTestsProject}' — Graph app-role parity gate skipped.");
        }

        var candidateFiles = Directory.GetFiles(archTestsProject, "GraphAppRoleParity*Tests.cs", SearchOption.TopDirectoryOnly);
        if (candidateFiles.Length == 0)
        {
            return new R3GateOutcome(
                R3GateKind.GraphAppRoleParity,
                R3GateStatus.Skipped,
                "No GraphAppRoleParity*Tests.cs found in tests/Spaarke.ArchTests/ — r3 task 062 / r1 task 067 not yet landed (queued behind CI-wiring per task 088).");
        }

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            _options.DotnetExecutable,
            new[] { "test", archTestsProject, "-c", "Release", "--filter", "FullyQualifiedName~GraphAppRoleParity", "--no-build", "--verbosity", "minimal" },
            request.RunId,
            R3GateKind.GraphAppRoleParity,
            cancellationToken).ConfigureAwait(false);

        if (exitCode == 0)
        {
            return new R3GateOutcome(R3GateKind.GraphAppRoleParity, R3GateStatus.Passed, string.Empty);
        }
        return new R3GateOutcome(
            R3GateKind.GraphAppRoleParity,
            R3GateStatus.Failed,
            $"dotnet test (GraphAppRoleParity) exit {exitCode}. Stderr: {Truncate(stderr, 600)} Stdout tail: {Truncate(stdout, 400)}");
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string executable,
        string[] arguments,
        string runId,
        R3GateKind kind,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _options.RepositoryRoot,
        };
        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H9 r3-gate {Kind} starting: runId={RunId} executable={Executable}",
            kind, runId, executable);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.R3GateStepTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"H9 r3-gate {kind} timed out after {_options.R3GateStepTimeout}.");
        }

        _logger.LogInformation(
            "H9 r3-gate {Kind} exited: runId={RunId} exitCode={ExitCode}",
            kind, runId, process.ExitCode);

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Race between HasExited + Kill — benign.
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
