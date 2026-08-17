// -----------------------------------------------------------------------------
// PowerShellPreflightProbe.cs
//
// Production <see cref="IPreflightQuotaProbe"/> implementation — shells out to
// one of the four scripts under scripts/preflight/*.ps1 (task 016) and
// deserializes the PSCustomObject output into <see cref="PreflightCheckResult"/>.
//
// SPEC / ADR references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-01 + NFR-12:
//       H0 preflight blocks the run before H1 if any of the four quota /
//       readiness checks fails.
//   - scripts/preflight/README.md § Shared Return Contract: exact wire
//       shape that this class parses.
//   - ADR-004: idempotency + at-least-once safety — the probes are pure
//       read-only quota queries; a re-invocation is a no-op (no external
//       side effect).
//   - ADR-010: seam justified — ≥2 implementations exist (this class +
//       test-only stubs), so the interface earns its keep.
//   - ADR-028: auth chain is `az login` (operator or App Service UAMI via
//       DefaultAzureCredential inside the script) — no account keys, no
//       plaintext secrets in argument strings (the scripts read from env
//       vars per README examples).
//
// NOT under test in the CI unit suite. Integration coverage lives in a
// smoke test (env-guarded — set PS_PREFLIGHT_SMOKE=1 and provide the
// Azure creds the scripts expect) that can be added alongside the
// existing CosmosSmokeTests / ServiceBusSmokeTests when task 069's seed
// manifest wires the dev-env config.
//
// NON-GOALS:
//   - This class does NOT translate script inputs. Every script's
//     parameter surface is documented in scripts/preflight/README.md;
//     the operator supplies matching keys in ProvisioningRun.parameters.
//   - This class does NOT reimplement the Azure REST paths in C#. The
//     scripts ARE the quota-query surface (spec.md FR-01 + task 016
//     ownership). H0 orchestrates; scripts probe.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// <see cref="IPreflightQuotaProbe"/> implementation that shells out to one
/// script under scripts/preflight/*.ps1. One instance per script — bound
/// by <see cref="ScriptFileName"/>.
/// </summary>
public sealed class PowerShellPreflightProbe : IPreflightQuotaProbe
{
    private readonly PreflightModuleOptions _options;
    private readonly ILogger<PowerShellPreflightProbe> _logger;

    /// <summary>Basename of the PS script this probe wraps (e.g. <c>Test-AzureOpenAiTpmHeadroom.ps1</c>).</summary>
    public string ScriptFileName { get; }

    /// <inheritdoc/>
    public string CheckName { get; }

    /// <summary>
    /// Constructs a probe bound to <paramref name="scriptFileName"/> under
    /// <see cref="PreflightModuleOptions.ScriptsDirectory"/>.
    /// </summary>
    /// <param name="checkName">Stable check identifier — matches <see cref="PreflightCheckNames"/> constants.</param>
    /// <param name="scriptFileName">PS script basename (e.g. <c>Test-AzureOpenAiTpmHeadroom.ps1</c>).</param>
    /// <param name="options">Bound preflight options (pwsh path, scripts directory, timeout).</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    public PowerShellPreflightProbe(
        string checkName,
        string scriptFileName,
        IOptions<PreflightModuleOptions> options,
        ILogger<PowerShellPreflightProbe> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFileName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        CheckName = checkName;
        ScriptFileName = scriptFileName;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PreflightCheckResult> CheckAsync(
        PreflightProbeInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var scriptPath = Path.Combine(_options.ScriptsDirectory, ScriptFileName);
        if (!File.Exists(scriptPath))
        {
            // Configuration / deployment error — the runbook says the L2 App
            // Service linux-x64 publish MUST include scripts/preflight/. Fail
            // loud so the operator fixes the deployment rather than silently
            // treating the check as passed (spec.md NFR-05 fail-fast).
            throw new FileNotFoundException(
                $"Preflight script '{ScriptFileName}' not found at '{scriptPath}'. " +
                "Verify scripts/preflight/ ships in the L2 publish output (see spec.md NFR-05 fail-fast).",
                scriptPath);
        }

        // Command shape: pwsh -NoProfile -NonInteractive -File <script>
        //                -CustomerId <id> -TenantId <tid>
        //                -Parameters '<json>'
        // Individual scripts define their own parameter surface per
        // scripts/preflight/README.md; H0's contract with those scripts is
        // the -CustomerId / -TenantId / -Parameters trio + the shared
        // PSCustomObject return contract. Any per-script parameter (Region,
        // SubscriptionId, per-model TPM, per-family vCPU, KV name, cert
        // name) is passed inside the -Parameters JSON so this class stays
        // script-agnostic. The scripts as authored in task 016 accept
        // discrete named parameters — the trailing wave-C4 wiring task
        // that connects real Azure creds will thread the parameter shape;
        // for now the JSON payload is the future-proof envelope.
        var parametersJson = JsonSerializer.Serialize(input.NonSecretParameters);

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
        psi.ArgumentList.Add("-CustomerId");
        psi.ArgumentList.Add(input.CustomerId);
        psi.ArgumentList.Add("-TenantId");
        psi.ArgumentList.Add(input.TenantId);
        psi.ArgumentList.Add("-Parameters");
        psi.ArgumentList.Add(parametersJson);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Preflight probe '{CheckName}' timed out after {_options.Timeout} " +
                $"(script: {ScriptFileName}).");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "Preflight probe {CheckName} exited with code {ExitCode} (script: {Script}, customerId={CustomerId})",
            CheckName, process.ExitCode, ScriptFileName, input.CustomerId);

        // The scripts emit exit code 0 on Pass, non-zero on Fail — but the
        // authoritative signal is the JSON PSCustomObject on stdout. Parse
        // that first; fall back to exit code + stderr only if JSON parse
        // fails. That way a Fail with a well-formed result is still a
        // structured failure (not an infrastructure exception).
        var parsed = TryParseResult(stdoutText);
        if (parsed is not null)
        {
            return parsed;
        }

        // JSON parse failed — treat as infrastructure error. The operator
        // needs to see stdout + stderr in the diagnostic to root-cause.
        throw new InvalidOperationException(
            $"Preflight probe '{CheckName}' produced unparseable output " +
            $"(exit code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}, " +
            $"script: {ScriptFileName}). " +
            $"Stdout: {Truncate(stdoutText, 400)} " +
            $"Stderr: {Truncate(stderrText, 400)}");
    }

    private static PreflightCheckResult? TryParseResult(string stdoutText)
    {
        if (string.IsNullOrWhiteSpace(stdoutText))
        {
            return null;
        }

        // Scripts may print progress lines before the final JSON. Find the
        // first '{' + last '}' to isolate the JSON payload.
        var firstBrace = stdoutText.IndexOf('{');
        var lastBrace = stdoutText.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            return null;
        }

        var jsonSlice = stdoutText[firstBrace..(lastBrace + 1)];
        try
        {
            using var doc = JsonDocument.Parse(jsonSlice);
            var root = doc.RootElement;

            var result = root.GetProperty("Result").GetString() ?? "Fail";
            var checkName = root.GetProperty("CheckName").GetString() ?? string.Empty;
            var diagnostic = root.GetProperty("Diagnostic").GetString() ?? string.Empty;

            // Clone the Headroom element out of the disposable JsonDocument
            // so the caller (H0PreflightHandler) can retain it beyond this
            // method's using scope.
            var headroom = root.TryGetProperty("Headroom", out var hr)
                ? hr.Clone()
                : JsonDocument.Parse("{}").RootElement.Clone();

            return new PreflightCheckResult(
                CheckName: checkName,
                Passed: string.Equals(result, "Pass", StringComparison.Ordinal),
                Headroom: headroom,
                Diagnostic: diagnostic);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
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
            // Process already exited between HasExited check + Kill call — race is benign.
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

/// <summary>
/// Bound options for <see cref="PowerShellPreflightProbe"/>. Loaded from the
/// <c>Preflight</c> configuration section by
/// <see cref="Sprk.Provisioning.ControlPlane.Modules.HandlersModule"/>.
/// </summary>
public sealed class PreflightModuleOptions
{
    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via
    /// PATH). On Linux App Service the pwsh binary lives at
    /// <c>/usr/bin/pwsh</c> after installation; on Windows it's typically
    /// <c>C:\Program Files\PowerShell\7\pwsh.exe</c>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Absolute path to the scripts/preflight/ directory in the L2 publish
    /// output. Defaults to <c>scripts/preflight</c> relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments should
    /// override via app-setting so the linux-x64 publish layout is honored.
    /// </summary>
    public string ScriptsDirectory { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "preflight");

    /// <summary>
    /// Maximum time to wait for a single probe invocation. Defaults to
    /// 60 seconds — the underlying <c>az cognitiveservices usage list</c> +
    /// <c>pac admin quota</c> calls typically complete in single-digit
    /// seconds; the 60s ceiling catches hung processes without prematurely
    /// aborting slow-but-progressing checks.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}
