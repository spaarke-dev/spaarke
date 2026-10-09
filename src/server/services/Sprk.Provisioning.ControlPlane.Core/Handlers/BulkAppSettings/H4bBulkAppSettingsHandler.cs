// -----------------------------------------------------------------------------
// H4bBulkAppSettingsHandler.cs
//
// Task 201 — H4b BulkAppSettings handler. Applies ALL required BFF app
// settings from the canonical secret-catalog manifest — every secret's
// app_settings as a Key Vault reference plus the per_env_settings values —
// in ONE write per slot → ONE App Service restart cycle, then polls /healthz
// to prove the BFF booted; on failure, fetches container docker logs from
// Kudu SCM + parses for the failing IOptions module name. Kills the F20 /
// F20a progressive fail-fast chain.
//
// Task 253 (G38): H4b used to run `pwsh -File` on the generated
// Configure-AppServiceSettings.generated.ps1. The Worker host (App Service
// DOTNETCORE|10.0) has no pwsh and no scripts/ folder, so H4b now computes the
// same settings in C# (BuildDesiredSettings — parity-tested against the
// generated script) and merges them through IAppServiceSettingsWriter (ARM
// SDK). The generated script stays an operator artifact; nothing in L2 runs it.
//
// FLOW:
//   (1) Load ProvisioningRun.
//   (2) Guards: intake tenantId / subscriptionId; H2a outputs
//       InterStepState.KeyVaultName / ResourceGroupName / AppServiceName
//       (task 245a — these were read from run parameters nobody wrote).
//       environmentName = IntakeParameterCatalog.ResolveEnvironmentName.
//   (3) Read the manifest; its content version is secretsVer (task 245b —
//       formerly a run parameter nothing wrote).
//   (4) Idempotency Level-3: appsettings-{environmentName}-{secretsVer}.
//   (5) Resolve each non-literal entry through PerEnvSourceCatalog (typed
//       InterStepState output or intake value); required-and-missing =
//       Resumable Failure BEFORE any write. An optional (`required: false`)
//       entry without a value is not written, and a value already on the site
//       is left alone (T254).
//   (6) Merge the settings into the production site and the staging slot
//       (IAppServiceSettingsWriter — merge, never replace; no write when a slot
//       already matches). An ARM refusal = Resumable.
//   (7) Poll /healthz with 8-min backoff. Success = advance state.
//   (8) On healthz timeout, fetch docker logs from Kudu + parse for
//       `Unhandled exception. System.InvalidOperationException:` line +
//       extract IOptions module name. Return QuarantineRequired w/
//       actionable diagnostic.
//   (9) MarkComplete — write CompletedPhase(H4b, idempotencyKey).
//
// ADR-028 discipline: per-env values (endpoints, public GUIDs) pass through
// this handler as LOCAL VARIABLES ONLY — handed to the writer, never
// serialized back into Cosmos.Parameters, InterStepState, or Log* calls (the
// writer logs setting NAMES only).
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
    private readonly IAppServiceSettingsWriter _settingsWriter;
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
        IAppServiceSettingsWriter settingsWriter,
        IHealthzProbe healthzProbe,
        IContainerLogFetcher logFetcher,
        IOptions<BulkAppSettingsOptions> options,
        ILogger<H4bBulkAppSettingsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(settingsWriter);
        ArgumentNullException.ThrowIfNull(healthzProbe);
        ArgumentNullException.ThrowIfNull(logFetcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _manifest = manifest;
        _settingsWriter = settingsWriter;
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
        if (!TryGetNonEmpty(parameters, SubscriptionIdParameterKey, out var subscriptionId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingSubscriptionId,
                "Run parameter 'subscriptionId' is required by H4b.",
                cancellationToken).ConfigureAwait(false);
        }
        // Customer-stamp identifiers are H2a outputs (task 245a): H2a persists them from the
        // ARM deployment; nothing ever wrote them as run parameters.
        var keyVaultName = run.InterStepState.KeyVaultName;
        if (string.IsNullOrWhiteSpace(keyVaultName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingKeyVaultName,
                "InterStepState.KeyVaultName (H2a output) is required by H4b (the vault its Key Vault references name). H2a must complete first.",
                cancellationToken).ConfigureAwait(false);
        }
        var resourceGroupName = run.InterStepState.ResourceGroupName;
        if (string.IsNullOrWhiteSpace(resourceGroupName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingResourceGroupName,
                "InterStepState.ResourceGroupName (H2a output) is required by H4b (the BFF App Service's resource group). H2a must complete first.",
                cancellationToken).ConfigureAwait(false);
        }
        var appServiceName = run.InterStepState.AppServiceName;
        if (string.IsNullOrWhiteSpace(appServiceName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.MissingAppServiceName,
                "InterStepState.AppServiceName (H2a output) is required by H4b (the App Service it writes + /healthz + Kudu URLs). H2a must complete first.",
                cancellationToken).ConfigureAwait(false);
        }
        // One stamp environment for every handler (CreateRun stores it; a pre-245a run resolves
        // to the same default H2a/H2b use).
        var environmentName = IntakeParameterCatalog.ResolveEnvironmentName(parameters);
        // (3) Read the manifest. Its content version is this handler's secretsVer (task 245b) — the
        //     same manifest H4 reads, so the same value.
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
        var manifest = (PerEnvSettingsManifestReadResult.Success)manifestResult;

        var idempotencyKey = BuildIdempotencyKey(environmentName, manifest.ContentVersion);

        // (4) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H4b idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        var entries = manifest.Entries;

        // (5) Resolve per-env values through PerEnvSourceCatalog (task 245a): each
        //     source names the typed InterStepState output or intake value it
        //     reads. Required-and-missing fails early BEFORE any write.
        //     Deduplicate by source key (several manifest entries may share one
        //     source, e.g. Graph__ManagedIdentity__ClientId + ManagedIdentity__ClientId
        //     both use uami_client_id).
        var resolvedPerEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.PerEnvSource == PerEnvSettingSource.Literal)
            {
                // Literals carry their value in the manifest — nothing to resolve.
                continue;
            }

            var sourceKey = entry.ParameterKey!;
            if (resolvedPerEnv.ContainsKey(sourceKey)) continue;  // dedup

            if (!PerEnvSourceCatalog.BySourceKey.TryGetValue(sourceKey, out var source))
            {
                // FilePerEnvSettingsManifest rejects unknown sources at load; this guards a
                // hand-built manifest (tests) or a future reader that skips the check.
                return await FailAsync(run, etag, FailureClass.Resumable,
                    BulkAppSettingsRejectionCodes.ManifestReadFailed,
                    $"per_env_settings entry '{entry.Key}' uses source '{sourceKey}', which is not in PerEnvSourceCatalog.",
                    cancellationToken).ConfigureAwait(false);
            }

            var value = source.Resolve(run);
            if (string.IsNullOrWhiteSpace(value))
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
                    $"'{sourceKey}' from {source.Location}, which is absent or empty" +
                    (source.ProducerHandlerId is null
                        ? $" — supply it at intake ({source.Location})."
                        : $" — {source.ProducerHandlerId} must complete before H4b.");
                return await FailAsync(run, etag, FailureClass.Resumable,
                    BulkAppSettingsRejectionCodes.PerEnvInputMissing, diagnostic, cancellationToken)
                    .ConfigureAwait(false);
            }
            resolvedPerEnv[sourceKey] = value;
        }

        // (6) Build the full settings set — exactly what the generated Configure script wrote — and merge it
        //     into the production site and the staging slot (task 253: ARM SDK, no process).
        var settings = BuildDesiredSettings(manifest, keyVaultName, resolvedPerEnv);

        AppServiceSettingsWriteResult writeResult;
        try
        {
            writeResult = await _settingsWriter.MergeAsync(
                new AppServiceSettingsWriteRequest(subscriptionId, resourceGroupName, appServiceName, settings),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Transport faults, an ARM call timing out (OperationCanceledException without caller cancellation) and
            // credential failures land here: nothing is known to be written in full — Resumable, a re-run converges.
            _logger.LogError(ex,
                "H4b app-settings write infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.AppSettingsWriteFailed,
                $"App-settings write to App Service '{appServiceName}' failed: {ex.GetType().Name}: {ex.Message}",
                cancellationToken).ConfigureAwait(false);
        }

        if (writeResult is AppServiceSettingsWriteResult.Failure writeFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                BulkAppSettingsRejectionCodes.AppSettingsWriteFailed, writeFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "H4b app settings in place: runId={RunId} customerId={CustomerId} settings={SettingCount} " +
            "keyVaultReferences={ReferenceCount} perEnvSourcesResolved={SourceCount} slotsWritten={SlotsWritten}",
            envelope.RunId, envelope.CustomerId, settings.Count, manifest.KeyVaultReferences.Count, resolvedPerEnv.Count,
            string.Join(",", ((AppServiceSettingsWriteResult.Success)writeResult).SlotsWritten));

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
    /// Task 253 — the settings H4b writes, built exactly as the generated
    /// <c>Configure-AppServiceSettings.generated.ps1</c> builds its <c>$settings</c> array:
    /// <list type="number">
    /// <item>every <c>secrets[].app_settings</c> key as <c>@Microsoft.KeyVault(VaultName={vault};SecretName={secret})</c>;</item>
    /// <item>then every <c>per_env_settings</c> entry — a literal's <c>literal_value</c>, or its source's resolved run
    ///   value. A per-env entry overrides a Key Vault reference with the same key (the script emitted per-env lines
    ///   last and <c>az</c> keeps the last value), e.g. <c>AzureAd__TenantId</c>;</item>
    /// <item>an optional (<c>required: false</c>) entry whose source has no value is left out — and so is never
    ///   written over a value already on the site (T254).</item>
    /// </list>
    /// <paramref name="resolvedBySource"/> holds the resolved value per source key; a non-literal entry whose source
    /// is absent from it is an optional one H4b skipped. Internal so the parity test can compare it with the script.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildDesiredSettings(
        PerEnvSettingsManifestReadResult.Success manifest,
        string keyVaultName,
        IReadOnlyDictionary<string, string> resolvedBySource)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyVaultName);
        ArgumentNullException.ThrowIfNull(resolvedBySource);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in manifest.KeyVaultReferences)
        {
            settings[reference.AppSettingKey] = FormatKeyVaultReference(keyVaultName, reference.SecretName);
        }

        foreach (var entry in manifest.Entries)
        {
            if (entry.PerEnvSource == PerEnvSettingSource.Literal)
            {
                settings[entry.Key] = entry.LiteralValue ?? string.Empty;
            }
            else if (entry.ParameterKey is not null && resolvedBySource.TryGetValue(entry.ParameterKey, out var value))
            {
                settings[entry.Key] = value;
            }
        }

        return settings;
    }

    /// <summary>
    /// The Key Vault reference form the generated script's <c>Format-KvRef</c> emits — the canonical single form
    /// (App Service resolves it with the slot's <c>keyVaultReferenceIdentity</c>, H4 / trap T1).
    /// </summary>
    internal static string FormatKeyVaultReference(string keyVaultName, string secretName)
        => $"@Microsoft.KeyVault(VaultName={keyVaultName};SecretName={secretName})";

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
