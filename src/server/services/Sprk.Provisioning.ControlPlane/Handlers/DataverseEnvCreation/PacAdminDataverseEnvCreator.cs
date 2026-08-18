// -----------------------------------------------------------------------------
// PacAdminDataverseEnvCreator.cs
//
// Production <see cref="IDataverseEnvCreator"/> implementation — shells out to
// `pac admin create-environment` (interim per design.md § 4.1 H5 row + M-10
// deferral of TF Power Platform provider adoption) with the H5 envelope's
// parameters and parses the pac CLI's JSON output to extract the created
// environment URL.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-08 acceptance:
//       H5 uses interim `pac admin create-environment` + gate verified until
//       TF Power Platform provider adoption lands.
//   - projects/customer-provisioning-orchestration-r1/design.md § 4.1 H5 row:
//       Interim pac admin CLI; TF migration is Phase D backlog.
//   - projects/customer-provisioning-orchestration-r1/design.md § 4.2 fire-
//       and-forget: 15–30 min operation off-thread; the pac CLI blocks
//       synchronously until Dataverse acknowledges completion.
//   - .claude/adr/ADR-028-spaarke-auth-architecture.md: auth chain is
//       `pac auth create` (operator or App Service UAMI via DefaultAzureCredential
//       inside a service-principal login) — no account keys.
//
// NON-GOALS:
//   - Does NOT re-implement env creation via Dataverse admin SDK — pac CLI
//     IS the source of truth for the create+wait behavior (parity with
//     H2a wrapping Provision-Customer.ps1 and H0 wrapping preflight scripts).
//   - Does NOT own post-create health verification — that's
//     <see cref="IDataverseHealthProbe"/> invoked by the handler AFTER
//     this creator reports success.
//
// FAILURE CLASSIFICATION:
//   Failures are classified by parsing pac CLI stderr / exit code + returning
//   a typed <see cref="DataverseEnvCreationOutcome.Failure"/> with a
//   <see cref="DataverseEnvCreationFailureKind"/> the handler maps to a §4C
//   class. The classification heuristics are intentionally conservative:
//     - Explicit "rate limit" text → RateLimited (Resumable)
//     - Explicit "quota" text → QuotaExhausted (Resumable)
//     - Explicit "not authorized" / "unauthorized" / "auth" text → AuthFailure (Resumable)
//     - Exit code 124 (POSIX timeout convention) OR our timeout → Timeout (Resumable)
//     - Anything else → UnknownInvocationFailure (Resumable). The operator
//       inspects stderr and can escalate to quarantine manually if partial
//       provisioning is confirmed.
//   The handler treats UnknownInvocationFailure as Resumable optimistically —
//   this favors keeping the run advancing over accidentally quarantining a
//   recoverable case. PartialProvisioning (QuarantineRequired) is emitted
//   only when a specific "already exists" / "conflicting" signal is detected.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pac CLI + real Dataverse — parity with
//   ProvisionCustomerScriptBicepDeployRunner + PowerShellPreflightProbe CI
//   exclusion). Env-guarded smoke tests can be added alongside
//   CosmosSmokeTests when a dedicated dev-tenant is available; for wave C4
//   the handler unit tests via stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>
/// Shells out to <c>pac admin create-environment</c> and translates the CLI's
/// JSON output into <see cref="DataverseEnvCreationOutcome"/>.
/// </summary>
public sealed class PacAdminDataverseEnvCreator : IDataverseEnvCreator
{
    private readonly DataverseEnvCreationOptions _options;
    private readonly ILogger<PacAdminDataverseEnvCreator> _logger;

    /// <summary>Constructs the creator bound to the pac CLI executable + creation timeout.</summary>
    public PacAdminDataverseEnvCreator(
        IOptions<DataverseEnvCreationOptions> options,
        ILogger<PacAdminDataverseEnvCreator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DataverseEnvCreationOutcome> CreateEnvironmentAsync(
        DataverseEnvCreationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Region);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Tier);

        // Command shape (`pac admin create-environment` reference):
        //   pac admin create-environment
        //     --name          <displayName>
        //     --region        <region>
        //     --type          <tier>
        //     --tenant        <tenantId>            # §4D I1 — always explicit
        //     --domain        <customerId>          # subdomain prefix
        //     --json                                # structured output
        //
        // The `--tenant` parameter satisfies POML §4D I1 constraint — no
        // default tenant fallback. The `--domain` parameter derives the env
        // URL (https://{domain}.crm.dynamics.com) so the JSON output's
        // environmentUrl is derivable from the input even if the CLI omits
        // the field on some versions.
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? $"{request.CustomerId} (Spaarke)"
            : request.DisplayName!;

        var psi = new ProcessStartInfo
        {
            FileName = _options.PacCliExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("admin");
        psi.ArgumentList.Add("create-environment");
        psi.ArgumentList.Add("--name");
        psi.ArgumentList.Add(displayName);
        psi.ArgumentList.Add("--region");
        psi.ArgumentList.Add(request.Region);
        psi.ArgumentList.Add("--type");
        psi.ArgumentList.Add(request.Tier);
        psi.ArgumentList.Add("--tenant");
        psi.ArgumentList.Add(request.TenantId);
        psi.ArgumentList.Add("--domain");
        psi.ArgumentList.Add(request.CustomerId);
        psi.ArgumentList.Add("--json");

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H5 pac admin create-environment starting: customerId={CustomerId} region={Region} tier={Tier}",
            request.CustomerId, request.Region, request.Tier);

        var timedOut = false;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.CreationTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                TryKill(process);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failure to even start pac CLI (missing binary, PATH issue).
            // Bubbles up as an exception — handler catches + treats as
            // Resumable via PacInvocationFailed.
            throw;
        }

        if (timedOut)
        {
            var diagnostic =
                $"pac admin create-environment for '{request.CustomerId}' timed out after {_options.CreationTimeout}. " +
                $"Stderr so far: {Truncate(stderr.ToString(), 800)}";
            return new DataverseEnvCreationOutcome.Failure(
                DataverseEnvCreationFailureKind.Timeout, diagnostic);
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "H5 pac admin create-environment exited: customerId={CustomerId} exitCode={ExitCode}",
            request.CustomerId, process.ExitCode);

        if (process.ExitCode != 0)
        {
            var kind = ClassifyStderr(stderrText, process.ExitCode);
            var diagnostic =
                $"pac admin create-environment exit {process.ExitCode} for customerId '{request.CustomerId}'. " +
                $"Stderr: {Truncate(stderrText, 800)} Stdout tail: {Truncate(stdoutText, 400)}";
            return new DataverseEnvCreationOutcome.Failure(kind, diagnostic);
        }

        // Success path — try to parse an environmentUrl from stdout JSON.
        // Fall back to the deterministic subdomain form when the CLI omits it.
        var parsedUrl = TryParseEnvironmentUrl(stdoutText);
        var envUrl = parsedUrl ?? BuildDerivedUrl(request.CustomerId);
        return new DataverseEnvCreationOutcome.Success(envUrl);
    }

    /// <summary>
    /// Heuristic mapping from pac stderr text + exit code to a classified
    /// <see cref="DataverseEnvCreationFailureKind"/>. Exposed internal for
    /// unit testing.
    /// </summary>
    internal static DataverseEnvCreationFailureKind ClassifyStderr(string stderrText, int exitCode)
    {
        if (exitCode == 124)
        {
            // Standard POSIX `timeout` command exit — pac may not use this
            // directly, but preserved for compatibility with process
            // wrappers that do.
            return DataverseEnvCreationFailureKind.Timeout;
        }

        var lower = (stderrText ?? string.Empty).ToLowerInvariant();

        if (lower.Contains("rate limit", StringComparison.Ordinal)
            || lower.Contains("throttle", StringComparison.Ordinal)
            || lower.Contains("too many requests", StringComparison.Ordinal))
        {
            return DataverseEnvCreationFailureKind.RateLimited;
        }

        if (lower.Contains("quota", StringComparison.Ordinal)
            || lower.Contains("capacity", StringComparison.Ordinal)
            || lower.Contains("no more environments", StringComparison.Ordinal))
        {
            return DataverseEnvCreationFailureKind.QuotaExhausted;
        }

        if (lower.Contains("unauthorized", StringComparison.Ordinal)
            || lower.Contains("not authorized", StringComparison.Ordinal)
            || lower.Contains("authentication", StringComparison.Ordinal)
            || lower.Contains("access denied", StringComparison.Ordinal)
            || lower.Contains("forbidden", StringComparison.Ordinal))
        {
            return DataverseEnvCreationFailureKind.AuthFailure;
        }

        if (lower.Contains("already exists", StringComparison.Ordinal)
            || lower.Contains("conflicting", StringComparison.Ordinal)
            || lower.Contains("partial", StringComparison.Ordinal))
        {
            return DataverseEnvCreationFailureKind.PartialProvisioning;
        }

        return DataverseEnvCreationFailureKind.UnknownInvocationFailure;
    }

    /// <summary>
    /// Attempts to extract <c>environmentUrl</c> (or <c>url</c>) from a
    /// JSON block in pac CLI stdout. Returns null if no parseable block
    /// contains the field — the caller falls back to the deterministic
    /// subdomain form.
    /// </summary>
    private static string? TryParseEnvironmentUrl(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        var firstBrace = stdout.IndexOf('{');
        var lastBrace = stdout.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            return null;
        }

        var slice = stdout[firstBrace..(lastBrace + 1)];
        try
        {
            using var doc = JsonDocument.Parse(slice);
            var root = doc.RootElement;

            string? Read(string key)
                => root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString()
                    : null;

            return Read("environmentUrl")
                ?? Read("url")
                ?? Read("EnvironmentUrl")
                ?? Read("Url");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildDerivedUrl(string customerId)
        => $"https://{customerId}.crm.dynamics.com/";

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
