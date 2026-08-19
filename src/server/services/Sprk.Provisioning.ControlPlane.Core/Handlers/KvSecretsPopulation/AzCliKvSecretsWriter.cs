// -----------------------------------------------------------------------------
// AzCliKvSecretsWriter.cs
//
// RETIRED (task 125, Wave G-2, 2026-08-19): superseded by
// <see cref="SecretClientKvWriter"/> (Azure.Security.KeyVault.Secrets SDK
// port). No longer registered in Worker/Program.cs's IKvSecretsWriter DI
// slot. Retained on disk (NOT deleted); see
// ProvisionCustomerScriptBicepDeployRunner.cs's retirement banner for the
// full rationale.
// -----------------------------------------------------------------------------

// Production <see cref="IKvSecretsWriter"/> — shells out to
// `az keyvault secret set/show/delete` under the operator's `az` auth chain
// (DefaultAzureCredential inside az CLI via `az login`). Parity with H2a's
// AzCliArmKeyVaultRefProbe + AzCliUpgradeDriftDetector shell-out posture.
//
// SCOPE NOTE (wave C4):
//   The value-resolution channel (Bicep output / existing KV secret / generated /
//   run parameter) is a Phase H concern — the wave-C4 impl below focuses on the
//   `az keyvault secret show` (existence probe) + rotation-safe upsert flow.
//   Actual value resolution for interim manifest entries is stubbed via
//   <c>ResolveValueForEntry</c>: it returns a deterministic placeholder value
//   `<canonical>-interim-placeholder` per entry, sufficient to exercise T1/T5/
//   BINDING logic in production dry-runs. When Phase H task 084 lands the real
//   generator, ResolveValueForEntry gains real branches per
//   <see cref="KvSecretValueSource"/> — the writer's public contract does not
//   change (parity with the interim StaticKvSecretManifest).
//
//   Because the interim placeholder is a well-formed non-secret string, it does
//   NOT satisfy the H4 leak-guard's "secret-shaped" heuristic and would surface
//   in a real deploy immediately — but tests never invoke this impl (they use
//   FakeWriter stubs). Live-deploy safety is provided by the fact that this
//   writer is behind the H4 handler which is behind the Phase H manifest guard:
//   until Phase H manifest lands with real value sources, H4 should not be
//   invoked in a production customer stamp (the L3 skill's H0 preflight
//   surfaces this as a lead-time item).
//
// CI COVERAGE:
//   Not exercised by CI unit suite (no live az CLI + no live KV — parity with
//   PowerShellPreflightProbe / RegisterEntraAppRegScriptProvisioner). Env-guarded
//   smoke tests can be added alongside CosmosSmokeTests when a dedicated
//   dev-provisioning tenant is available; for wave C4 the handler unit tests
//   via stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Shells out to `az keyvault secret set/show/delete` for each manifest entry.
/// </summary>
public sealed class AzCliKvSecretsWriter : IKvSecretsWriter
{
    private readonly KvSecretsPopulationOptions _options;
    private readonly ILogger<AzCliKvSecretsWriter> _logger;

    /// <summary>Constructs the writer bound to the az CLI executable path.</summary>
    public AzCliKvSecretsWriter(
        IOptions<KvSecretsPopulationOptions> options,
        ILogger<AzCliKvSecretsWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<KvSecretsWriteOutcome> WriteAsync(
        KvSecretWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetKeyVaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);

        // Whole-writer prerequisite probe — verify `az` is on PATH BEFORE
        // attempting any per-entry work. A missing az CLI is a Failure
        // (Resumable) not a per-entry partial-fail.
        try
        {
            await ProbeAzCliAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new KvSecretsWriteOutcome.Failure(
                $"az CLI probe failed BEFORE any KV write attempted: {ex.GetType().Name}: {ex.Message}. " +
                "Verify az is on PATH + `az login` succeeded on the L2 App Service host.");
        }

        var results = new List<KvSecretWriteResult>(capacity: request.Entries.Count);

        foreach (var entry in request.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteEntryAsync(entry, request, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return new KvSecretsWriteOutcome.Success(results);
    }

    private async Task<KvSecretWriteResult> ExecuteEntryAsync(
        KvSecretEntry entry,
        KvSecretWriteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (entry.Operation == KvSecretOperation.Delete)
            {
                // BINDING belt-and-braces guard (code-review W1, 2026-08-17).
                // The HANDLER's pre-check is the primary defense — if it fires
                // the writer is never invoked. But IKvSecretsWriter is a public
                // interface with ≥2 impls; a future refactor OR a direct caller
                // OR a test-double gone wrong could bypass the handler and
                // invoke the writer directly. Two independent gates raise the
                // bar (parity with ADR-039's dispatcher-vs-orchestrator double-
                // gate).
                if (H4KvSecretsPopulationHandler.BindingNeverDeleteSecrets.Contains(entry.CanonicalName))
                {
                    return new KvSecretWriteResult(
                        entry.CanonicalName, KvSecretWriteAction.Failed,
                        $"BINDING never-delete guard: writer refuses Delete op for '{entry.CanonicalName}' — " +
                        "spec.md MUST rule + r3 handoff. Fleet-wide OBO + shared-lib Dataverse depend on this secret.");
                }

                _logger.LogInformation(
                    "H4 KV writer: DELETE {SecretName} on vault {Vault} (customer={CustomerId})",
                    entry.CanonicalName, request.TargetKeyVaultName, request.CustomerId);
                await InvokeAzAsync(
                    _options.KvOperationTimeout,
                    cancellationToken,
                    "keyvault", "secret", "delete",
                    "--vault-name", request.TargetKeyVaultName,
                    "--name", entry.CanonicalName,
                    "--subscription", request.SubscriptionId)
                    .ConfigureAwait(false);
                return new KvSecretWriteResult(entry.CanonicalName, KvSecretWriteAction.Deleted, null);
            }

            // Upsert branch — rotation-safe by default.
            var exists = await SecretExistsAsync(
                request.TargetKeyVaultName, request.SubscriptionId, entry.CanonicalName, cancellationToken)
                .ConfigureAwait(false);

            if (exists && request.UpgradeMode && !request.RotateExisting)
            {
                _logger.LogInformation(
                    "H4 KV writer: SKIP (rotation-safe) {SecretName} on vault {Vault} " +
                    "(upgrade mode + rotate=false + already exists) — customer={CustomerId}",
                    entry.CanonicalName, request.TargetKeyVaultName, request.CustomerId);
                return new KvSecretWriteResult(
                    entry.CanonicalName, KvSecretWriteAction.SkippedRotationSafe, null);
            }

            var value = ResolveValueForEntry(entry, request);

            // The cleartext value ONLY passes through the ArgumentList here to
            // the az CLI process. It is not logged, not returned in the result
            // record, and not persisted to Cosmos anywhere. `--value` is the
            // only surface — kept as `--value` rather than `--file` to
            // preserve az's own throttle + retry semantics.
            _logger.LogInformation(
                "H4 KV writer: WRITE {SecretName} on vault {Vault} (customer={CustomerId} rotate={Rotate})",
                entry.CanonicalName, request.TargetKeyVaultName, request.CustomerId, request.RotateExisting);
            await InvokeAzAsync(
                _options.KvOperationTimeout,
                cancellationToken,
                "keyvault", "secret", "set",
                "--vault-name", request.TargetKeyVaultName,
                "--name", entry.CanonicalName,
                "--value", value,
                "--subscription", request.SubscriptionId)
                .ConfigureAwait(false);
            return new KvSecretWriteResult(entry.CanonicalName, KvSecretWriteAction.Wrote, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Per-entry failure — return a Failed result WITHOUT the value.
            // ErrorMessage carries the az CLI exit signal only.
            return new KvSecretWriteResult(entry.CanonicalName, KvSecretWriteAction.Failed,
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<bool> SecretExistsAsync(
        string vaultName, string subscriptionId, string secretName, CancellationToken cancellationToken)
    {
        try
        {
            await InvokeAzAsync(
                _options.KvOperationTimeout,
                cancellationToken,
                "keyvault", "secret", "show",
                "--vault-name", vaultName,
                "--name", secretName,
                "--subscription", subscriptionId,
                "--query", "id",
                "-o", "tsv")
                .ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("SecretNotFound", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    /// <summary>
    /// Interim value resolver — Phase H task 084 will replace this with real
    /// branches per <see cref="KvSecretValueSource"/>. Deterministic placeholder
    /// suffices for wave-C4 tests + dev provisioning. NOT invoked by unit
    /// tests (they use fakes); dev-provisioning runs surface this as a
    /// non-secret non-functional placeholder that the operator/H8/H14 can
    /// overwrite with the real value.
    /// </summary>
    private static string ResolveValueForEntry(KvSecretEntry entry, KvSecretWriteRequest request)
    {
        // Deterministic non-secret placeholder. Format is intentionally NOT
        // secret-shaped (short, hyphenated, includes "placeholder") so if it
        // ever reaches production it fails loudly at the first consumer
        // rather than silently persisting.
        return $"{entry.CanonicalName}-interim-placeholder-{request.CustomerId}";
    }

    private async Task ProbeAzCliAsync(CancellationToken cancellationToken)
    {
        await InvokeAzAsync(
            TimeSpan.FromSeconds(15),
            cancellationToken,
            "account", "show", "--query", "id", "-o", "tsv")
            .ConfigureAwait(false);
    }

    private async Task InvokeAzAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _options.AzCliExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

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

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"az CLI invocation timed out after {timeout}. Args: {SafeArgsForLog(args)}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"az CLI exit {process.ExitCode}. Stderr: {Truncate(stderr.ToString(), 800)}");
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
            // Benign race between HasExited check + Kill.
        }
    }

    /// <summary>
    /// Redacts any <c>--value</c> argument to <c>***</c> so cleartext never
    /// hits log output. Preserves subcommand shape for triage.
    /// </summary>
    private static string SafeArgsForLog(string[] args)
    {
        var redacted = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            redacted.Add(args[i]);
            if (string.Equals(args[i], "--value", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                redacted.Add("***");
                i++;
            }
        }
        return string.Join(' ', redacted);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
