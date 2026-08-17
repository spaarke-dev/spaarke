// -----------------------------------------------------------------------------
// RegisterEntraAppRegScriptProvisioner.cs
//
// Production <see cref="IEntraAppRegProvisioner"/> implementation — shells out
// to the hardened <c>scripts/Register-EntraAppRegistrations.ps1</c> with the
// H3 envelope's parameters and parses the script's summary output to extract
// the BFF app registration's <c>appId</c>.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-06 acceptance:
//       H3 invokes the hardened Register-EntraAppRegistrations.ps1 (post-r1
//       task 010 commit fea66c023 — full Get-then-Add reconciler idempotency);
//       -TenantId mandatory; 14 grants sourced from GraphAppRoles.cs.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H3 row:
//       Handler wraps script; admin-consent verification is a separate
//       concern owned by IAdminConsentVerifier.
//
// NON-GOALS (this wave):
//   - Does NOT re-implement the reconciler in Microsoft.Graph SDK v6. The PS
//     script IS the source-of-truth for the 5-step reconciler (create /
//     signInAudience / identifierUri / exposed scope / requiredResourceAccess).
//   - Does NOT own admin-consent verification — that's <see cref="IAdminConsentVerifier"/>
//     invoked BY the handler AFTER this provisioner returns Success.
//   - Does NOT provision the Dataverse S2S app-reg — r3 task 060 removed it
//     from the script itself; this provisioner has no code path to create one.
//
// STDOUT PARSING:
//   The PS script's summary block emits (script line ~929):
//       BFF API App Registration:
//         Display Name:    spaarke-bff-api-prod
//         Application ID:  {guid}
//         App ID URI:      api://{guid}
//         ...
//   The provisioner parses the first "Application ID:  {guid}" line via a
//   fixed regex. Format was hardened in r1 task 010; a regression would be
//   caught at H3 unit-test-level via the script-test provisioner fake, and
//   at H3 acceptance-level (Phase F E2E) via the real script. If Microsoft
//   ever changes the script surface, the runner rejects with
//   ProvisioningOutputsIncomplete.
//
// KV SECRET URI CONVENTION:
//   The script writes the client secret to KV as <c>BFF-API-ClientSecret</c>
//   with the naming convention <c>@Microsoft.KeyVault(SecretUri=https://
//   {vaultName}.vault.azure.net/secrets/BFF-API-ClientSecret/)</c>. The
//   provisioner ASSEMBLES this URI reference deterministically from the
//   input <c>KeyVaultName</c> — the cleartext secret NEVER traverses the
//   runner boundary (parity with ADR-028 MUST rule; verified by the handler's
//   downstream cleartext-secret-leak guard).
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pwsh + real Graph — parity with
//   <see cref="Preflight.PowerShellPreflightProbe"/>'s CI exclusion). Env-guarded
//   smoke tests can be added alongside CosmosSmokeTests when a dedicated
//   dev-provisioning tenant is available; for wave C4 the handler unit tests
//   via stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Shells out to <c>scripts/Register-EntraAppRegistrations.ps1</c> and
/// translates its summary output into <see cref="EntraAppRegOutcome"/>.
/// </summary>
public sealed class RegisterEntraAppRegScriptProvisioner : IEntraAppRegProvisioner
{
    private static readonly Regex ApplicationIdRegex = new(
        @"Application ID:\s+(?<appId>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        matchTimeout: TimeSpan.FromSeconds(1));

    private readonly EntraAppRegOptions _options;
    private readonly ILogger<RegisterEntraAppRegScriptProvisioner> _logger;

    /// <summary>Constructs the provisioner bound to the on-disk PS script + pwsh executable.</summary>
    public RegisterEntraAppRegScriptProvisioner(
        IOptions<EntraAppRegOptions> options,
        ILogger<RegisterEntraAppRegScriptProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<EntraAppRegOutcome> ProvisionAsync(
        EntraAppRegRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.KeyVaultName);

        var scriptPath = _options.RegisterEntraAppRegistrationsScriptPath;
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                $"Register-EntraAppRegistrations.ps1 not found at '{scriptPath}'. " +
                "Verify scripts/ ships in the L2 publish output.",
                scriptPath);
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
        // §4D I1 — MANDATORY -TenantId (script parameter is [Mandatory = $true]).
        psi.ArgumentList.Add("-TenantId");
        psi.ArgumentList.Add(request.TenantId);
        psi.ArgumentList.Add("-KeyVaultName");
        psi.ArgumentList.Add(request.KeyVaultName);
        // CustomerId is not a script parameter — it's a partition/correlation
        // scalar the handler uses. The single BFF app-reg is per-tenant, not
        // per-customer, so passing customerId here would be structurally wrong.

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H3 Entra app-reg provisioning starting: customerId={CustomerId} tenantId={TenantId} keyVault={KeyVault}",
            request.CustomerId, request.TenantId, request.KeyVaultName);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ProvisionTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"H3 Register-EntraAppRegistrations.ps1 for tenantId '{request.TenantId}' " +
                $"timed out after {_options.ProvisionTimeout}.");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "H3 Entra app-reg provisioning exited: customerId={CustomerId} exitCode={ExitCode}",
            request.CustomerId, process.ExitCode);

        if (process.ExitCode != 0)
        {
            var diagnostic =
                $"Register-EntraAppRegistrations.ps1 exited {process.ExitCode} for tenantId '{request.TenantId}'. " +
                $"Stderr: {Truncate(stderrText, 800)} Stdout tail: {Truncate(stdoutText, 400)}";
            return new EntraAppRegOutcome.Failure(diagnostic);
        }

        var appId = TryExtractAppId(stdoutText);
        if (appId is null)
        {
            var diagnostic =
                $"Register-EntraAppRegistrations.ps1 completed with exit 0 but no parseable " +
                $"'Application ID: {{guid}}' line was found in stdout. " +
                $"Stdout tail: {Truncate(stdoutText, 800)}";
            return new EntraAppRegOutcome.Failure(diagnostic);
        }

        // KV URI reference is assembled deterministically — cleartext secret
        // NEVER traverses this method's boundary (ADR-028 MUST rule).
        var kvUriRef = BuildKvUriReference(request.KeyVaultName);

        return new EntraAppRegOutcome.Success(new EntraAppRegOutputs
        {
            BffAppRegId = appId,
            BffClientSecretKvUri = kvUriRef,
        });
    }

    /// <summary>
    /// Builds the canonical <c>@Microsoft.KeyVault(SecretUri=https://{vault}.
    /// vault.azure.net/secrets/BFF-API-ClientSecret/)</c> URI reference
    /// literal. Exposed internal so unit tests can construct expected refs
    /// without duplicating the format.
    /// </summary>
    internal static string BuildKvUriReference(string keyVaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVaultName);
        return $"@Microsoft.KeyVault(SecretUri=https://{keyVaultName}.vault.azure.net/secrets/BFF-API-ClientSecret/)";
    }

    /// <summary>
    /// Attempts to extract the BFF Application ID GUID from the PS script's
    /// summary output. Returns null if no match; the caller maps null to
    /// <see cref="EntraAppRegOutcome.Failure"/>.
    /// </summary>
    private static string? TryExtractAppId(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        try
        {
            var match = ApplicationIdRegex.Match(stdout);
            return match.Success ? match.Groups["appId"].Value : null;
        }
        catch (RegexMatchTimeoutException)
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
            // Process already exited between HasExited check + Kill — race is benign.
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
