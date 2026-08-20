// -----------------------------------------------------------------------------
// CreateNewContainerTypeScriptProvisioner.cs
//
// RETIRED (task 131, Wave G-3) — kept on disk, UNREGISTERED (Worker/Program.cs
// now wires GraphContainerTypeProvisioner.cs instead), per this project's
// established retirement pattern (RegisterEntraAppRegScriptProvisioner.cs /
// AzCliKvSecretsWriter.cs / NullAdminConsentVerifier.cs). Superseded because
// Option D's target state forbids ProcessStartInfo/shell-out collaborators in
// the L2 runtime (design.md §4.1b Class A). Do NOT re-register this class.
//
// Production ISpeContainerTypeProvisioner implementation — shells out to the
// T6-hardened scripts/Create-NewContainerType.ps1 (task 011 confidential-client
// cert-based refactor) with -CreateTestContainer, and parses the script's
// summary output for the container-type id + root container id + the T6
// "cleared" evidence markers the script emits on the confidential-client path.
//
// T6 VERIFICATION (spec.md FR-33 + design.md §4B):
//   Create-NewContainerType.ps1 has NO delegated-token code path left after
//   task 011 — auth is exclusively confidential-client cert-based via
//   Get-SpeConfidentialClientToken.ps1. This provisioner's job is to catch a
//   REGRESSION of that guarantee (someone re-adds a delegated fallback, or the
//   cert-based flow silently degrades), not to re-implement the check. It does
//   so by requiring BOTH of the script's own "T6 cleared: ..." evidence lines
//   (one for the container-type creation, one for the -CreateTestContainer
//   root container) to be present when exit code is 0, and by scanning
//   stdout/stderr for the literal Graph 403 message ("public client not
//   allowed") that the T6 trap produces. Absence of the T6-cleared markers OR
//   presence of the trap phrase is classified IsDelegatedTokenTrap=true
//   regardless of exit code.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>
/// Shells out to <c>scripts/Create-NewContainerType.ps1 -CreateTestContainer</c>
/// and translates its summary output into <see cref="SpeContainerTypeProvisionOutcome"/>.
/// </summary>
public sealed class CreateNewContainerTypeScriptProvisioner : ISpeContainerTypeProvisioner
{
    private static readonly Regex ContainerTypeIdRegex = new(
        @"Container Type ID:\s+(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        matchTimeout: TimeSpan.FromSeconds(1));

    // Anchored at line-start (Multiline) so it does NOT also match the
    // "Container Type ID:" line above — the two labels differ immediately
    // after "Container ", so a line-start anchor cleanly disambiguates them.
    private static readonly Regex RootContainerIdRegex = new(
        @"^Container ID:\s+(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline,
        matchTimeout: TimeSpan.FromSeconds(1));

    private const string T6ClearedMarker = "T6 cleared:";
    private const string DelegatedTokenTrapPhrase = "public client not allowed";

    private readonly SpeContainerTypeOptions _options;
    private readonly ILogger<CreateNewContainerTypeScriptProvisioner> _logger;

    /// <summary>Constructs the provisioner bound to the on-disk PS script + pwsh executable.</summary>
    public CreateNewContainerTypeScriptProvisioner(
        IOptions<SpeContainerTypeOptions> options,
        ILogger<CreateNewContainerTypeScriptProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SpeContainerTypeProvisionOutcome> ProvisionAsync(
        SpeContainerTypeProvisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SharePointDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CertSecretName);

        var scriptPath = _options.CreateNewContainerTypeScriptPath;
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                $"Create-NewContainerType.ps1 not found at '{scriptPath}'. " +
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
        psi.ArgumentList.Add("-OwningAppId");
        psi.ArgumentList.Add(request.OwningAppId);
        // §4D I1/I5 — MANDATORY -TenantId (target tenant explicit; script param is mandatory).
        psi.ArgumentList.Add("-TenantId");
        psi.ArgumentList.Add(request.TenantId);
        psi.ArgumentList.Add("-SharePointDomain");
        psi.ArgumentList.Add(request.SharePointDomain);
        // T6 cert bootstrap — production path (KV). No CertThumbprint dev-fallback
        // branch is ever passed by the handler (that path is operator/local-only).
        psi.ArgumentList.Add("-KeyVaultName");
        psi.ArgumentList.Add(request.VaultName);
        psi.ArgumentList.Add("-CertSecretName");
        psi.ArgumentList.Add(request.CertSecretName);
        psi.ArgumentList.Add("-DisplayName");
        psi.ArgumentList.Add(request.DisplayName);
        psi.ArgumentList.Add("-CreateTestContainer");

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H8 SPE container-type provisioning starting: customerId={CustomerId} tenantId={TenantId} " +
            "owningAppId={OwningAppId} spDomain={SpDomain}",
            request.CustomerId, request.TenantId, request.OwningAppId, request.SharePointDomain);

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
                $"H8 Create-NewContainerType.ps1 for customerId '{request.CustomerId}' " +
                $"timed out after {_options.ProvisionTimeout}.");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();
        var combined = stdoutText + "\n" + stderrText;

        _logger.LogInformation(
            "H8 SPE container-type provisioning exited: customerId={CustomerId} exitCode={ExitCode}",
            request.CustomerId, process.ExitCode);

        // T6 trap detection — checked REGARDLESS of exit code. A delegated-token
        // 403 may surface as a non-zero exit (caught exception -> exit 1) OR
        // (in a hypothetical script regression) as an exit-0 path missing the
        // cert-based evidence markers. Both are QuarantineRequired via
        // IsDelegatedTokenTrap=true so the operator sees the T6-specific
        // diagnostic rather than a generic provisioning failure.
        if (combined.Contains(DelegatedTokenTrapPhrase, StringComparison.OrdinalIgnoreCase))
        {
            var diagnostic =
                $"T6 silent-fail trap detected: Create-NewContainerType.ps1 output contains " +
                $"'{DelegatedTokenTrapPhrase}' (delegated-token 403 signature) for customerId " +
                $"'{request.CustomerId}'. Confidential-client cert-based auth MUST be used for SPE " +
                $"container-type creation (spec.md FR-33 / T6). Stdout tail: {Truncate(stdoutText, 800)}";
            return new SpeContainerTypeProvisionOutcome.Failure(diagnostic, IsDelegatedTokenTrap: true);
        }

        if (process.ExitCode != 0)
        {
            var diagnostic =
                $"Create-NewContainerType.ps1 exited {process.ExitCode} for customerId '{request.CustomerId}'. " +
                $"Stderr: {Truncate(stderrText, 800)} Stdout tail: {Truncate(stdoutText, 400)}";
            return new SpeContainerTypeProvisionOutcome.Failure(diagnostic, IsDelegatedTokenTrap: false);
        }

        var containerTypeId = TryExtractGuid(ContainerTypeIdRegex, stdoutText);
        var rootContainerId = TryExtractGuid(RootContainerIdRegex, stdoutText);

        if (containerTypeId is null || rootContainerId is null)
        {
            var diagnostic =
                "Create-NewContainerType.ps1 completed with exit 0 but the summary output was missing " +
                $"a parseable {(containerTypeId is null ? "'Container Type ID:'" : "'Container ID:'")} line. " +
                $"Verify -CreateTestContainer ran + the script's output shape matches task-011 hardening. " +
                $"Stdout tail: {Truncate(stdoutText, 800)}";
            return new SpeContainerTypeProvisionOutcome.Failure(diagnostic, IsDelegatedTokenTrap: false);
        }

        // T6-cleared evidence count — the script emits ONE marker for the
        // container-type creation and ONE for the -CreateTestContainer root
        // container. Fewer than 2 with an otherwise-successful exit is treated
        // as a T6 regression signal (script no longer proves cert-based auth
        // was used on both Graph calls) rather than a silent pass.
        var t6ClearedCount = CountOccurrences(stdoutText, T6ClearedMarker);
        if (t6ClearedCount < 2)
        {
            var diagnostic =
                $"Create-NewContainerType.ps1 completed with exit 0 + parseable IDs, but only " +
                $"{t6ClearedCount} of 2 expected 'T6 cleared:' evidence markers were found in stdout. " +
                "This indicates the confidential-client cert-based auth proof is incomplete — treating " +
                "as a T6 regression rather than a silent pass. Stdout tail: " + Truncate(stdoutText, 800);
            return new SpeContainerTypeProvisionOutcome.Failure(diagnostic, IsDelegatedTokenTrap: true);
        }

        return new SpeContainerTypeProvisionOutcome.Success(new SpeContainerTypeProvisionOutputs(
            ContainerTypeId: containerTypeId,
            RootContainerId: rootContainerId));
    }

    private static string? TryExtractGuid(Regex pattern, string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        try
        {
            var match = pattern.Match(stdout);
            return match.Success ? match.Groups["id"].Value : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
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
