// -----------------------------------------------------------------------------
// SpeContainerAppOnlyVerifier.cs
//
// Production ISpeContainerVerifier implementation — shells out to the new
// T6-compliant scripts/Get-SpeContainerMetadata-AppOnly.ps1 (see
// ISpeContainerVerifier.cs header for why the existing delegated
// Get-ContainerMetadata.ps1 is NOT used here).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>
/// Shells out to <c>scripts/Get-SpeContainerMetadata-AppOnly.ps1</c> and
/// translates its output into <see cref="SpeContainerVerificationResult"/>.
/// </summary>
public sealed class SpeContainerAppOnlyVerifier : ISpeContainerVerifier
{
    private static readonly Regex StatusRegex = new(
        @"^Status:\s+(?<status>\S+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline,
        matchTimeout: TimeSpan.FromSeconds(1));

    private const string T6ClearedMarker = "T6 cleared:";
    private const string DelegatedTokenTrapPhrase = "public client not allowed";

    private readonly SpeContainerTypeOptions _options;
    private readonly ILogger<SpeContainerAppOnlyVerifier> _logger;

    /// <summary>Constructs the verifier bound to the on-disk PS script + pwsh executable.</summary>
    public SpeContainerAppOnlyVerifier(
        IOptions<SpeContainerTypeOptions> options,
        ILogger<SpeContainerAppOnlyVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SpeContainerVerificationResult> VerifyAsync(
        SpeContainerVerificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.KeyVaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CertSecretName);

        var scriptPath = _options.GetContainerMetadataAppOnlyScriptPath;
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                $"Get-SpeContainerMetadata-AppOnly.ps1 not found at '{scriptPath}'. " +
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
        psi.ArgumentList.Add("-ContainerId");
        psi.ArgumentList.Add(request.ContainerId);
        psi.ArgumentList.Add("-OwningAppId");
        psi.ArgumentList.Add(request.OwningAppId);
        psi.ArgumentList.Add("-TenantId");
        psi.ArgumentList.Add(request.TenantId);
        psi.ArgumentList.Add("-KeyVaultName");
        psi.ArgumentList.Add(request.KeyVaultName);
        psi.ArgumentList.Add("-CertSecretName");
        psi.ArgumentList.Add(request.CertSecretName);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H8 SPE container app-only verification starting: containerId={ContainerId}",
            request.ContainerId);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.VerifyTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"H8 Get-SpeContainerMetadata-AppOnly.ps1 for containerId '{request.ContainerId}' " +
                $"timed out after {_options.VerifyTimeout}.");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();
        var combined = stdoutText + "\n" + stderrText;

        _logger.LogInformation(
            "H8 SPE container app-only verification exited: containerId={ContainerId} exitCode={ExitCode}",
            request.ContainerId, process.ExitCode);

        if (combined.Contains(DelegatedTokenTrapPhrase, StringComparison.OrdinalIgnoreCase))
        {
            var diagnostic =
                $"T6 silent-fail trap detected during verification: output contains " +
                $"'{DelegatedTokenTrapPhrase}' for containerId '{request.ContainerId}'. " +
                $"Stdout tail: {Truncate(stdoutText, 800)}";
            return new SpeContainerVerificationResult.NotVerified(diagnostic, IsDelegatedTokenTrap: true);
        }

        if (process.ExitCode != 0)
        {
            var diagnostic =
                $"Get-SpeContainerMetadata-AppOnly.ps1 exited {process.ExitCode} for containerId " +
                $"'{request.ContainerId}'. Stderr: {Truncate(stderrText, 800)} Stdout tail: {Truncate(stdoutText, 400)}";
            return new SpeContainerVerificationResult.NotVerified(diagnostic, IsDelegatedTokenTrap: false);
        }

        if (!combined.Contains(T6ClearedMarker, StringComparison.Ordinal))
        {
            var diagnostic =
                "Get-SpeContainerMetadata-AppOnly.ps1 completed with exit 0 but the 'T6 cleared:' " +
                "evidence marker was not found — treating as a T6 regression rather than a silent pass. " +
                $"Stdout tail: {Truncate(stdoutText, 800)}";
            return new SpeContainerVerificationResult.NotVerified(diagnostic, IsDelegatedTokenTrap: true);
        }

        var status = TryExtractStatus(stdoutText) ?? "unknown";
        return new SpeContainerVerificationResult.Verified(status);
    }

    private static string? TryExtractStatus(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        try
        {
            var match = StatusRegex.Match(stdout);
            return match.Success ? match.Groups["status"].Value : null;
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
