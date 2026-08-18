// -----------------------------------------------------------------------------
// ExchangePolicyScriptApplier.cs
//
// Production IExchangePolicyApplier — shells out to the new
// scripts/Set-ExchangeApplicationAccessPolicy.ps1, which owns the
// ExchangeOnlineManagement PS module connect + Get/New-ApplicationAccessPolicy
// action-and-verify sequence (spec.md T4). Exchange Online has no REST
// equivalent for ApplicationAccessPolicy management — the PS module is the
// only supported surface (parity with H2a's IBicepDeployRunner + H3's
// IEntraAppRegProvisioner script-wrapping posture for surfaces without a
// clean REST API).
//
// STDOUT PARSING:
//   The script emits exactly one line prefixed "SPAARKE-H14A-RESULT-JSON:"
//   followed by a compact JSON object:
//     { "outcome": "Applied"|"Drift"|"Failure",
//       "createdCount": 0,
//       "expectedAppIds": ["...","..."],
//       "observedAppIds": ["...","..."],
//       "diagnostic": "..." }
//   This applier parses that single line rather than relying on free-text
//   summary output (contrast with RegisterEntraAppRegScriptProvisioner's
//   regex-over-summary-block approach) — the script is new (this task), so
//   a structured contract was chosen from day 1.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pwsh + real Exchange Online — parity with
//   RegisterEntraAppRegScriptProvisioner's CI exclusion). Handler unit tests
//   substitute a fake IExchangePolicyApplier.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Shells out to <c>scripts/Set-ExchangeApplicationAccessPolicy.ps1</c> and
/// translates its structured JSON result line into
/// <see cref="ExchangePolicyApplyOutcome"/>.
/// </summary>
public sealed class ExchangePolicyScriptApplier : IExchangePolicyApplier
{
    private const string ResultLinePrefix = "SPAARKE-H14A-RESULT-JSON:";

    private readonly IntegrationWiringOptions _options;
    private readonly ILogger<ExchangePolicyScriptApplier> _logger;

    /// <summary>Constructs the applier bound to the on-disk PS script + pwsh executable.</summary>
    public ExchangePolicyScriptApplier(
        IOptions<IntegrationWiringOptions> options,
        ILogger<ExchangePolicyScriptApplier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ExchangePolicyApplyOutcome> ApplyAsync(
        ExchangePolicyApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PolicyScopeGroupId);
        if (request.ExpectedAppIds is not { Count: 2 })
        {
            return new ExchangePolicyApplyOutcome.Failure(
                $"ExpectedAppIds must contain exactly 2 entries (BFF app-reg + UAMI); got {request.ExpectedAppIds?.Count ?? 0}.");
        }

        var scriptPath = _options.ExchangePolicyScriptPath;
        if (!File.Exists(scriptPath))
        {
            return new ExchangePolicyApplyOutcome.Failure(
                $"Set-ExchangeApplicationAccessPolicy.ps1 not found at '{scriptPath}'. " +
                "Verify scripts/ ships in the L2 publish output.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = _options.PwshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("-TenantId");
        psi.ArgumentList.Add(request.TenantId);
        psi.ArgumentList.Add("-ExpectedAppIds");
        psi.ArgumentList.Add(string.Join(",", request.ExpectedAppIds));
        psi.ArgumentList.Add("-PolicyScopeGroupId");
        psi.ArgumentList.Add(request.PolicyScopeGroupId);
        psi.ArgumentList.Add("-DescriptionPrefix");
        psi.ArgumentList.Add(request.DescriptionPrefix);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H14a Exchange policy apply starting: tenantId={TenantId} expectedAppIds={ExpectedAppIds}",
            request.TenantId, string.Join(",", request.ExpectedAppIds));

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ExchangeScriptTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ExchangePolicyApplyOutcome.Failure(
                $"Set-ExchangeApplicationAccessPolicy.ps1 timed out after {_options.ExchangeScriptTimeout} " +
                $"for tenantId '{request.TenantId}'.");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "H14a Exchange policy apply exited: tenantId={TenantId} exitCode={ExitCode}",
            request.TenantId, process.ExitCode);

        if (process.ExitCode != 0)
        {
            return new ExchangePolicyApplyOutcome.Failure(
                $"Set-ExchangeApplicationAccessPolicy.ps1 exited {process.ExitCode} for tenantId '{request.TenantId}'. " +
                $"Stderr: {Truncate(stderrText, 800)} Stdout tail: {Truncate(stdoutText, 400)}");
        }

        return ParseResultLine(stdoutText)
            ?? new ExchangePolicyApplyOutcome.Failure(
                "Set-ExchangeApplicationAccessPolicy.ps1 completed with exit 0 but no parseable " +
                $"'{ResultLinePrefix}' line was found in stdout. Stdout tail: {Truncate(stdoutText, 800)}");
    }

    /// <summary>
    /// Parses the script's single structured JSON result line. Exposed
    /// internal so unit tests can validate parsing without a live process.
    /// </summary>
    internal static ExchangePolicyApplyOutcome? ParseResultLine(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(ResultLinePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var jsonText = trimmed[ResultLinePrefix.Length..].Trim();
            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                var root = doc.RootElement;
                var outcome = root.GetProperty("outcome").GetString();
                var diagnostic = root.TryGetProperty("diagnostic", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                var observed = root.TryGetProperty("observedAppIds", out var obs)
                    ? obs.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
                    : new List<string>();

                switch (outcome)
                {
                    case "Applied":
                        var createdCount = root.TryGetProperty("createdCount", out var cc) ? cc.GetInt32() : 0;
                        return new ExchangePolicyApplyOutcome.Applied(createdCount, observed);
                    case "Drift":
                        var expected = root.TryGetProperty("expectedAppIds", out var exp)
                            ? exp.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
                            : new List<string>();
                        return new ExchangePolicyApplyOutcome.Drift(expected, observed);
                    case "Failure":
                        return new ExchangePolicyApplyOutcome.Failure(diagnostic);
                    default:
                        return new ExchangePolicyApplyOutcome.Failure(
                            $"Unrecognized outcome value '{outcome}' in script result JSON.");
                }
            }
            catch (JsonException ex)
            {
                return new ExchangePolicyApplyOutcome.Failure(
                    $"Failed to parse script result JSON: {ex.Message}. Line: {Truncate(jsonText, 400)}");
            }
        }

        return null;
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
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
