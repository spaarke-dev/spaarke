// -----------------------------------------------------------------------------
// H4bBulkAppSettingsHandler.cs
//
// Task 201 — H4b BulkAppSettings handler. Thin wrapper around task 084's
// shipped Configure-AppServiceSettings.generated.ps1 script (extended by
// this task with per-env-literal lines emitted from the new
// per_env_settings: manifest section). Kills the F20 / F20a progressive
// fail-fast chain by applying ALL required BFF app settings in ONE batched
// call → ONE App Service restart cycle, then polls /healthz to prove the BFF
// booted; on failure, fetches container docker logs from Kudu SCM + parses
// for the failing IOptions module name to make operator triage 30-second
// instead of 15-30 min.
//
// FLOW:
//   (1) Load ProvisioningRun.
//   (2) Parameter guards (subscriptionId, keyVaultName, resourceGroupName,
//       appServiceName, environmentName, secretsVer).
//   (3) Idempotency Level-3: appsettings-{environmentName}-{secretsVer}.
//   (4) Read per_env_settings manifest.
//   (5) Resolve each entry from envelope.Parameters.NonSecret (literal /
//       from-handler-output / from-handler-parameter); required-and-missing
//       = Resumable Failure BEFORE any script call.
//   (6) Shell pwsh Configure-AppServiceSettings.generated.ps1 with fixed
//       args (-VaultName / -AppServiceName / -ResourceGroupName) + one
//       -<PsVarName> per unique per-env source. Non-zero exit = Resumable.
//   (7) Poll /healthz with 8-min backoff. Success = advance state.
//   (8) On healthz timeout, fetch docker logs from Kudu + parse for
//       `Unhandled exception. System.InvalidOperationException:` line +
//       extract IOptions module name. Return QuarantineRequired w/
//       actionable diagnostic.
//   (9) MarkComplete — write CompletedPhase(H4b, idempotencyKey).
//
// ADR-028 discipline: per-env cleartext values pass through this handler as
// LOCAL VARIABLES ONLY — they are forwarded to IProcessRunner as argv, never
// serialized back into Cosmos.Parameters, InterStepState, or Log* calls. The
// generated script's stdout/stderr is REDACTED (bounded tail only) in log
// lines to avoid accidentally leaking a value echoed by the child process.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H4bBulkAppSettingsHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches HandlerIds.H4b.</summary>
    public const string HandlerIdentifier = HandlerIds.H4b;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the target subscription id.</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>Non-secret parameter key carrying the target Key Vault name (script's -VaultName arg).</summary>
    public const string KeyVaultNameParameterKey = "keyVaultName";

    /// <summary>Non-secret parameter key carrying the App Service resource group.</summary>
    public const string ResourceGroupNameParameterKey = "resourceGroupName";

    /// <summary>Non-secret parameter key carrying the App Service name (script's -AppServiceName arg + /healthz + Kudu URLs).</summary>
    public const string AppServiceNameParameterKey = "appServiceName";

    /// <summary>Non-secret parameter key carrying the environment name — feeds idempotency key.</summary>
    public const string EnvironmentNameParameterKey = "environmentName";

    /// <summary>Non-secret parameter key carrying the manifest content hash / semantic version — feeds idempotency key.</summary>
    public const string SecretsVersionParameterKey = "secretsVer";

    /// <summary>
    /// Parses the first fail-fast IOptions module name from a container docker
    /// log. Matches the SESSION 2 verbatim pattern
    /// <c>Unhandled exception. System.InvalidOperationException: {SectionKey}:{FieldKey}
    /// (or {FallbackKey}) configuration is required for {Module}.</c> and
    /// the shorter <c>configuration is required for {Module}.</c> variant.
    /// </summary>
    private static readonly Regex FailFastPattern = new(
        @"Unhandled exception\. System\.InvalidOperationException:\s*(?<detail>[^\r\n]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        matchTimeout: TimeSpan.FromSeconds(1));

    /// <summary>
    /// Secondary regex — pulls the "for {Module}" phrase from the detail line
    /// when the fail-fast text follows the SESSION 2 pattern (SpeAdminModule /
    /// AiPersistenceModule / ...). Best-effort; some IOptions validators emit
    /// different shapes.
    /// </summary>
    private static readonly Regex ModuleNamePattern = new(
        @"(?:required\s+for|for)\s+(?<module>[A-Za-z][A-Za-z0-9_]*Module)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        matchTimeout: TimeSpan.FromSeconds(1));

    private readonly IProvisioningRunRepository _repository;
    private readonly IPerEnvSettingsManifest _manifest;
    private readonly IProcessRunner _processRunner;
    private readonly IHealthzProbe _healthzProbe;
    private readonly IContainerLogFetcher _logFetcher;
    private readonly BulkAppSettingsOptions _options;
    private readonly ILogger<H4bBulkAppSettingsHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>Constructs the H4b handler.</summary>
    public H4bBulkAppSettingsHandler(
        IProvisioningRunRepository repository,
        IPerEnvSettingsManifest manifest,
        IProcessRunner processRunner,
        IHealthzProbe healthzProbe,
        IContainerLogFetcher logFetcher,
        IOptions<BulkAppSettingsOptions> options,
        ILogger<H4bBulkAppSettingsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(healthzProbe);
        ArgumentNullException.ThrowIfNull(logFetcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _manifest = manifest;
        _processRunner = processRunner;
        _healthzProbe = healthzProbe;
        _logFetcher = logFetcher;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);

        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"H4bBulkAppSettingsHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H4b BulkAppSettings starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load ProvisioningRun.
        var read = await _repository.ReadRunAsync(envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H4b aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: BulkAppSettingsRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) Parameter guards.
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var _))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingTenantId,
                "Run parameter 'tenantId' is required by H4b (§4D I1 no-hardcoded-tenant).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SubscriptionIdParameterKey, out var _))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingSubscriptionId,
                "Run parameter 'subscriptionId' is required by H4b.",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, KeyVaultNameParameterKey, out var keyVaultName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingKeyVaultName,
                "Run parameter 'keyVaultName' is required by H4b (Configure script -VaultName arg).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, ResourceGroupNameParameterKey, out var resourceGroupName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingResourceGroupName,
                "Run parameter 'resourceGroupName' is required by H4b (Configure script -ResourceGroupName arg).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, AppServiceNameParameterKey, out var appServiceName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingAppServiceName,
                "Run parameter 'appServiceName' is required by H4b (Configure script -AppServiceName arg + /healthz + Kudu URLs).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, EnvironmentNameParameterKey, out var environmentName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingEnvironmentName,
                "Run parameter 'environmentName' is required by H4b (feeds idempotency key appsettings-{env}-{secretsVer}).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SecretsVersionParameterKey, out var secretsVer))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingSecretsVersion,
                "Run parameter 'secretsVer' is required by H4b (manifest content hash — feeds idempotency key).",
                cancellationToken).ConfigureAwait(false);
        }

        var idempotencyKey = BuildIdempotencyKey(environmentName, secretsVer);

        // (3) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H4b idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (4) Read manifest.
        PerEnvSettingsManifestReadResult manifestResult;
        try
        {
            manifestResult = await _manifest.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H4b manifest read infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.ManifestReadFailed,
                $"per_env_settings manifest read failed: {ex.GetType().Name}: {ex.Message}.",
                cancellationToken).ConfigureAwait(false);
        }
        if (manifestResult is PerEnvSettingsManifestReadResult.Failure manifestFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.ManifestReadFailed,
                $"Manifest reader reported failure: {manifestFailure.Diagnostic}",
                cancellationToken).ConfigureAwait(false);
        }
        var entries = ((PerEnvSettingsManifestReadResult.Success)manifestResult).Entries;

        // (5) Resolve per-env values. Non-literal entries look up their
        //     ParameterKey in envelope.Parameters.NonSecret; required-and-missing
        //     fails early BEFORE any script call. Deduplicate by source
        //     (multiple manifest entries may share one source, e.g.
        //     Graph__ManagedIdentity__ClientId + ManagedIdentity__ClientId
        //     both reference uami_client_id).
        var resolvedPerEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.PerEnvSource == PerEnvSettingSource.Literal)
            {
                // Literals do not contribute a script parameter — they're emitted
                // verbatim by the generator. Nothing for H4b to resolve here.
                continue;
            }

            var sourceKey = entry.ParameterKey!;
            if (resolvedPerEnv.ContainsKey(sourceKey)) continue;  // dedup

            if (!TryGetNonEmpty(parameters, sourceKey, out var value))
            {
                if (!entry.Required)
                {
                    _logger.LogWarning(
                        "H4b skipping optional per_env entry: key={Key} source={SourceKey} required=false",
                        entry.Key, sourceKey);
                    continue;
                }
                var diagnostic =
                    $"per_env_settings entry '{entry.Key}' (BFF module '{entry.IOptionsModuleName}') requires " +
                    $"envelope.Parameters.NonSecret['{sourceKey}'] but the source key is absent or empty. " +
                    "Upstream handler MUST populate this parameter before H4b dispatches.";
                return await FailAsync(run, etag, FailureClass.Resumable,
                    BulkAppSettingsRejectionCodes.PerEnvInputMissing, diagnostic, cancellationToken)
                    .ConfigureAwait(false);
            }
            resolvedPerEnv[sourceKey] = value;
        }

        // (6) Build the pwsh argv + invoke IProcessRunner. Fixed args first,
        //     then one -<PsVarName> per unique resolved source. Script param
        //     names are the PascalCase of the source key (matches the
        //     generator's ConvertTo-PascalCase output).
        var args = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-File", _options.ConfigureScriptPath,
            "-ResourceGroupName", resourceGroupName,
            "-AppServiceName", appServiceName,
            "-VaultName", keyVaultName,
        };
        // Deterministic order for arg dumping / test reproducibility (alphabetical by source key).
        foreach (var kv in resolvedPerEnv.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            args.Add("-" + ConvertToPascalCase(kv.Key));
            args.Add(kv.Value);
        }

        ProcessResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(
                _options.PwshExecutable,
                args,
                environment: null,
                timeout: _options.ScriptTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.AppSettingsWriteFailed,
                $"Configure script timed out after {_options.ScriptTimeout.TotalSeconds:F0}s: {ex.Message}",
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.AppSettingsWriteFailed,
                $"Configure script failed to start: {ex.Message}. Verify PwshExecutable + ConfigureScriptPath options.",
                cancellationToken).ConfigureAwait(false);
        }

        if (processResult.ExitCode != 0)
        {
            var redacted = RedactProcessDiagnostic(processResult);
            var diagnostic =
                $"Configure-AppServiceSettings.generated.ps1 returned exit code {processResult.ExitCode}. " +
                $"Redacted tail: {redacted}";
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.AppSettingsWriteFailed, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "H4b Configure script succeeded: runId={RunId} customerId={CustomerId} settingsWritten>=1 entriesResolved={EntryCount} literalsInlined={LiteralCount}",
            envelope.RunId, envelope.CustomerId, resolvedPerEnv.Count,
            entries.Count(e => e.PerEnvSource == PerEnvSettingSource.Literal));

        // (7) Poll /healthz.
        //
        // FIC-PROPAGATION-FLAP TOLERANCE (task 205c / punch row A39, §5 item 4
        // verifier addition): this backoff-poll (5 probes / ~8-min budget, see
        // HttpHealthzProbe.DefaultBackoffSchedule) is the CHOSEN mechanism for
        // tolerating Entra's measured ~130s AADSTS70025 federated-credential
        // propagation flap after the auth-v4 §10.2 entries (Graph__Credentials__
        // Order__0 / RequireSecretFreeIdentity, applied via per_env_settings
        // above) are written. Full rationale + the rejected alternative (a
        // pre-apply verified-exchange gate, infeasible because H13's post-
        // App-Service verification runs AFTER H4b in the DAG) is documented in
        // scripts/canonical-secret-catalog/manifest.yaml directly above the
        // Graph__Credentials__Order__0 entry — read that comment before
        // changing this probe's budget or the credential-selection entries.
        var healthzUrl = new Uri(_options.HealthzUrlTemplate.Replace("{appServiceName}", appServiceName, StringComparison.Ordinal));
        var healthResult = await _healthzProbe.ProbeWithBackoffAsync(healthzUrl, cancellationToken).ConfigureAwait(false);

        if (healthResult is HealthzResult.Timeout timeout)
        {
            // (8) Fetch docker logs + parse failing IOptions module.
            string? failingModule = null;
            string? failingDetail = null;
            try
            {
                var logs = await _logFetcher.FetchDockerLogsAsync(appServiceName, cancellationToken).ConfigureAwait(false);
                if (TryParseFailFastModule(logs, out var mod, out var detail))
                {
                    failingModule = mod;
                    failingDetail = detail;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "H4b docker-log fetch failed; falling back to generic timeout diagnostic.");
            }

            var kuduHint =
                _options.KuduHostTemplate.Replace("{appServiceName}", appServiceName, StringComparison.Ordinal) +
                "/api/logs/docker";
            var diag = failingModule is not null
                ? $"BFF fail-fast on {failingModule}: {failingDetail}. /healthz never returned 200 within " +
                  $"backoff budget ({timeout.LastErrorSummary}). Add the missing config to " +
                  "scripts/canonical-secret-catalog/manifest.yaml per_env_settings + regenerate."
                : $"/healthz never returned 200 within backoff budget ({timeout.LastErrorSummary}). " +
                  $"Container docker logs did not carry a parseable fail-fast IOptions exception; " +
                  $"inspect manually at https://{kuduHint}.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                BulkAppSettingsRejectionCodes.HealthzTimeout, diag, cancellationToken)
                .ConfigureAwait(false);
        }

        var healthSuccess = (HealthzResult.Success)healthResult;
        stopwatch.Stop();
        _logger.LogInformation(
            "H4b BulkAppSettings succeeded: runId={RunId} customerId={CustomerId} healthzMs={HealthzMs} totalMs={TotalMs}",
            envelope.RunId, envelope.CustomerId, (long)healthSuccess.Elapsed.TotalMilliseconds, stopwatch.ElapsedMilliseconds);

        // (9) MarkComplete.
        return await MarkCompleteAsync(run, etag, idempotencyKey, envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deterministic idempotency-key format: <c>appsettings-{env}-{secretsVer}</c>.
    /// Env-scoped (not customer-scoped) because per_env_settings drives an
    /// env-scoped App Service. Exposed internal for test reproducibility.
    /// </summary>
    internal static string BuildIdempotencyKey(string environmentName, string secretsVer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretsVer);
        return $"appsettings-{environmentName}-{secretsVer}";
    }

    /// <summary>
    /// Snake / kebab-case → PascalCase. MUST mirror the generator's
    /// ConvertTo-PascalCase (Invoke-CatalogGenerator.ps1) — the generator's
    /// script param names are computed the same way. Exposed internal for
    /// test reproducibility. Empty / invalid input → "P" (matches generator).
    /// </summary>
    internal static string ConvertToPascalCase(string snakeOrKebab)
    {
        if (string.IsNullOrWhiteSpace(snakeOrKebab)) return "P";

        var parts = snakeOrKebab.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "P";

        var sb = new System.Text.StringBuilder();
        foreach (var p in parts)
        {
            if (p.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(p[0]));
            if (p.Length > 1) sb.Append(p[1..]);
        }
        var result = sb.ToString();
        if (result.Length == 0) return "P";
        if (!char.IsLetter(result[0])) return "P" + result;
        return result;
    }

    /// <summary>
    /// Parses a container docker log for the first fail-fast IOptions module
    /// name. Returns true + module name + full detail line when the
    /// SESSION 2 pattern
    /// (<c>Unhandled exception. System.InvalidOperationException: ... for XyzModule.</c>)
    /// is found. Exposed internal so H4b test theory can pin log samples.
    /// </summary>
    internal static bool TryParseFailFastModule(string? logs, out string? moduleName, out string? detail)
    {
        moduleName = null;
        detail = null;
        if (string.IsNullOrWhiteSpace(logs)) return false;

        Match ffMatch;
        try { ffMatch = FailFastPattern.Match(logs); }
        catch (RegexMatchTimeoutException) { return false; }
        if (!ffMatch.Success) return false;

        detail = ffMatch.Groups["detail"].Value.Trim();

        Match modMatch;
        try { modMatch = ModuleNamePattern.Match(detail); }
        catch (RegexMatchTimeoutException) { return true; }  // Still return the detail line even if module extraction fails.
        if (modMatch.Success)
        {
            moduleName = modMatch.Groups["module"].Value;
        }
        return true;
    }

    private static bool TryGetNonEmpty(
        IDictionary<string, string> parameters,
        string key,
        out string value)
    {
        if (parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }
        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Bounded / structural summary of a failed Configure-script invocation.
    /// Redacts the stdout/stderr streams to the FIRST + LAST 200 chars each
    /// to prevent inadvertent per-env cleartext value leak into log stores /
    /// Cosmos error fields (ADR-028 discipline). Callers use this in place of
    /// the raw stdout/stderr in every operator-facing message.
    /// </summary>
    internal static string RedactProcessDiagnostic(ProcessResult processResult)
    {
        static string Tail(string s, int n = 200)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            if (s.Length <= n * 2) return s;
            return s[..n] + " ...[truncated]... " + s[^n..];
        }
        return $"stdout={Tail(processResult.Stdout)} | stderr={Tail(processResult.Stderr)}";
    }

    private async Task<HandlerResult> FailAsync(
        ProvisioningRun run,
        string etag,
        FailureClass failureClass,
        string rejectionCode,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        run.Status = failureClass == FailureClass.QuarantineRequired
            ? RunStatus.Quarantined
            : RunStatus.Failed;
        run.CurrentPhase = HandlerIdentifier;
        run.ErrorDetail = $"[{rejectionCode}] {diagnostic}";
        if (failureClass == FailureClass.QuarantineRequired)
        {
            run.Quarantine = new QuarantineInfo
            {
                State = QuarantineState.Quarantined,
                Reason = diagnostic,
                QuarantinedByHandler = HandlerIdentifier,
                QuarantinedAt = DateTimeOffset.UtcNow,
            };
        }
        run.GateStates[$"h4b-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H4b failure state write LOST optimistic-concurrency race: runId={RunId} winningStatus={WinningStatus}",
                run.RunId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H4b failure state write raced with row delete: runId={RunId}", run.RunId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier;
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H4b success state write LOST optimistic-concurrency race: runId={RunId} winningStatus={WinningStatus}",
                run.RunId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: BulkAppSettingsRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H4b read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H4b.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H4b success state write raced with row delete: runId={RunId}", run.RunId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: BulkAppSettingsRejectionCodes.RunDeletedDuringPopulation,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H4b was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
