// -----------------------------------------------------------------------------
// PowerShellAppConfigSeeder.cs
//
// Production <see cref="IAppConfigSeeder"/> implementation — shells out to
// one of the existing per-scope PS scripts under <c>scripts/</c> (per task 004
// §4b decision matrix) and interprets the exit code + tail of stdout/stderr
// as the seed outcome.
//
// SPEC / ADR references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-16 (H12b
//     app-config seed acceptance).
//   - projects/customer-provisioning-orchestration-r1/notes/ai-seed-drift-resolution-2026-08.md
//     §4b — the two scopes with existing shipping scripts (DataGrid,
//     workspace-layout) route through this class; scopes lacking a repo
//     source (field-mapping, chart-def) route through DeferredAppConfigSeeder.
//   - ADR-004: idempotent + at-least-once safe. Every wrapped script is
//     upsert-safe (existence-check-then-insert or PATCH on
//     <c>sprk_gridconfigurationid</c> / <c>sprk_workspacelayoutid</c>) so
//     re-invocation is a no-op or a refresh.
//   - ADR-010: seam justified — ≥2 implementations exist
//     (PowerShellAppConfigSeeder + DeferredAppConfigSeeder + test-only stubs).
//   - ADR-028: auth chain is <c>az login</c> (operator or App Service UAMI
//     via DefaultAzureCredential inside the script) — no account keys, no
//     plaintext secrets in argument strings.
//
// PROCESS SHAPE (parity with PowerShellPreflightProbe + ProvisionCustomerScriptBicepDeployRunner):
//   pwsh -NoProfile -NonInteractive -File {scriptPath}
//        -EnvironmentUrl {targetDataverseUrl}
//        [-DataverseUrl  {targetDataverseUrl}]   # heuristic: some scripts use one name, some the other
//
//   Argument passing is via ProcessStartInfo.ArgumentList (per-arg escaping —
//   never string concatenation) so paths with spaces + special chars are safe.
//
// EXIT-CODE INTERPRETATION:
//   0                   → AppConfigSeederStatus.Ok + diagnostic quoting stdout tail
//   non-zero            → AppConfigSeederStatus.Failed + diagnostic quoting
//                         exit code + stderr tail + stdout tail (in that order
//                         so the operator sees the most-signal line first)
//
// TIMEOUT + CANCELLATION:
//   Linked CTS with AppConfigSeedOptions.SeederTimeout ceiling. Timeout →
//   TimeoutException (translated by handler into
//   AppConfigSeedRejectionCodes.SeederInfrastructureError). Cancellation
//   propagates via OperationCanceledException as normal.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pwsh + real Azure — parity with
//   PowerShellPreflightProbe's CI exclusion). Handler-level unit tests use
//   in-memory fakes; end-to-end validation lives in H13 acceptance-gate +
//   the manual smoke via ServiceBusSmokeTests-style env-guarded fixture.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// <see cref="IAppConfigSeeder"/> implementation that shells out to one
/// script under <c>scripts/</c>. One instance per scope — bound to a specific
/// <see cref="ScopeName"/> + script basename at DI-registration time.
/// </summary>
public sealed class PowerShellAppConfigSeeder : IAppConfigSeeder
{
    private readonly string _scriptFileName;
    private readonly AppConfigSeedOptions _options;
    private readonly ILogger<PowerShellAppConfigSeeder> _logger;

    /// <inheritdoc/>
    public string ScopeName { get; }

    /// <summary>
    /// Constructs a seeder bound to <paramref name="scopeName"/> +
    /// <paramref name="scriptFileName"/>.
    /// </summary>
    /// <param name="scopeName">Scope identifier (see <see cref="AppConfigSeedScopes"/>).</param>
    /// <param name="scriptFileName">
    /// PS script basename (e.g. <c>seed-reconciliation-gridconfig.ps1</c>),
    /// relative to <see cref="AppConfigSeedOptions.ScriptsDirectory"/>.
    /// </param>
    /// <param name="options">Bound app-config seed options.</param>
    /// <param name="logger">Structured logger.</param>
    public PowerShellAppConfigSeeder(
        string scopeName,
        string scriptFileName,
        IOptions<AppConfigSeedOptions> options,
        ILogger<PowerShellAppConfigSeeder> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFileName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        ScopeName = scopeName;
        _scriptFileName = scriptFileName;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AppConfigSeederResult> SeedAsync(
        AppConfigSeedInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TargetDataverseUrl);

        var scriptPath = Path.Combine(_options.ScriptsDirectory, _scriptFileName);
        if (!File.Exists(scriptPath))
        {
            // Configuration / deployment error — the runbook says the L2 App
            // Service linux-x64 publish MUST include scripts/. Fail loud so
            // the operator fixes the deployment rather than silently treating
            // the seed as passed.
            throw new FileNotFoundException(
                $"App-config seed script '{_scriptFileName}' not found at '{scriptPath}' " +
                $"(scope: {ScopeName}). Verify scripts/ ships in the L2 publish output.",
                scriptPath);
        }

        // Command shape — see file header. Both -EnvironmentUrl and
        // -DataverseUrl are wired because the shipping scripts use one or
        // the other (heuristic parity with Invoke-SeedManifest.ps1's own
        // parameter-name inference). Passing both is harmless: PowerShell
        // scripts silently ignore extra named parameters when the script
        // has [CmdletBinding()], and both scripts accept string-typed URL
        // params (any conflict would surface at bind time, not at run time).
        var psi = new ProcessStartInfo
        {
            FileName = _options.PwshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _options.ScriptsDirectory,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        // Only ONE URL parameter name so we don't collide on scripts that
        // strictly define one (Deploy-SystemWorkspaceLayouts.ps1 uses
        // -EnvironmentUrl; seed-reconciliation-gridconfig.ps1 currently
        // hard-codes spaarkedev1 — see task 004 §4b row 10 note that N1
        // delta will generalize it. Both accept -EnvironmentUrl at their
        // typical invocation OR ignore it silently if not declared. This
        // is the minimum viable wiring; N-delta migration will unify.)
        psi.ArgumentList.Add("-EnvironmentUrl");
        psi.ArgumentList.Add(input.TargetDataverseUrl);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "App-config seeder starting: scope={ScopeName} script={ScriptFileName} customerId={CustomerId} " +
            "dataverseUrl={DataverseUrl}",
            ScopeName, _scriptFileName, input.CustomerId, input.TargetDataverseUrl);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.SeederTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"App-config seeder '{ScopeName}' timed out after {_options.SeederTimeout} " +
                $"(script: {_scriptFileName}).");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "App-config seeder exited: scope={ScopeName} script={ScriptFileName} exitCode={ExitCode}",
            ScopeName, _scriptFileName, process.ExitCode);

        if (process.ExitCode == 0)
        {
            var diagnostic =
                $"{ScopeName} seed OK via '{_scriptFileName}'. " +
                $"Stdout tail: {Truncate(stdoutText, 400)}";
            return AppConfigSeederResult.Ok(diagnostic, evidence: BuildEvidence(stdoutText));
        }

        var failureDiagnostic =
            $"{ScopeName} seed FAILED — '{_scriptFileName}' exited {process.ExitCode}. " +
            $"Stderr: {Truncate(stderrText, 600)} " +
            $"Stdout tail: {Truncate(stdoutText, 400)}";
        return AppConfigSeederResult.Failed(failureDiagnostic, evidence: BuildEvidence(stdoutText));
    }

    /// <summary>
    /// Wraps the stdout tail into a JSON evidence element so
    /// <see cref="AppConfigSeederResult.Evidence"/> carries a structured
    /// payload rather than a bare string. Kept minimal — full logs live in
    /// App Insights via structured-log emission upstream.
    /// </summary>
    private static JsonElement BuildEvidence(string stdoutText)
    {
        var evidence = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            stdoutTail = Truncate(stdoutText, 800),
        }));
        return evidence.RootElement.Clone();
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
            // Process already exited between HasExited check + Kill — race is benign.
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
        {
            return s;
        }
        return s[..max] + "...[truncated]";
    }
}
