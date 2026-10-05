// -----------------------------------------------------------------------------
// PacRequiredApplicationsInstaller.cs
//
// HANDLER-07 (Wave 2 pre-dispatch remediation 2026-08-27) — F13 verbatim.
// Production <see cref="IRequiredApplicationsInstaller"/> impl. LIVE
// (2026-08-27 Wave 2.5 replacement of the Wave 2 log-and-return-Success
// scaffold): shells out to `pac application install --application-name X
// --environment Y` per required app, then polls `pac application list
// --environment Y` every <see cref="DefaultPollInterval"/> up to
// <see cref="DefaultPerAppDeadline"/> per app to confirm the app landed.
// Wraps System.Diagnostics.Process behind the shared
// <see cref="IProcessRunner"/> seam so unit tests substitute a fake runner
// (parity with H4b's Configure-script shell-out + HANDLER-08's
// PacOrgSettingsContractApplier).
//
// F13 (SKILL.md "Fresh-Sub Automation Gaps" §):
//   Fresh Production-tier Dataverse envs do NOT include Power BI Extensions
//   (msft_PowerBI_Anchor, publisher solution msft_PowerBI_Entities,
//   isFirstParty="False"). One SpaarkeMaster env-var-def carries a spurious
//   dependency on `powerbimashupparameter` → SpaarkeMaster import fails 5
//   min in with 1 unresolved MissingDependency even after F12 fix.
//   Session-2 remediation was manual: `pac application install
//   --environment {url} --application-name msft_PowerBI_Anchor` (~6 min).
//   This handler makes that pre-install a gate H6 enforces automatically.
//
// EXECUTION MODEL:
//   Per app in the canonical manifest:
//     (1) Invoke `pac application install --application-name X --environment Y`
//         with a bounded per-app deadline (default 10 min). pac's real-world
//         behavior blocks synchronously while Dataverse provisions the app
//         (internally polling every 30s per F13 field notes), returning 0
//         on install completion. Idempotent — no-op if already installed.
//     (2) On non-zero exit → Failure with rejection-code diagnostic naming
//         the app + exit code + redacted stderr tail. H6 maps to Resumable +
//         MissingRequiredApplication.
//     (3) On IProcessRunner TimeoutException (per-app deadline exceeded) →
//         Failure. H6 maps to Resumable + MissingRequiredApplication.
//     (4) On exit 0 → poll `pac application list --environment Y` every
//         <see cref="DefaultPollInterval"/> (default 30s) up to the same
//         per-app deadline to confirm the app is now present. This is a
//         defense-in-depth check for the (rare) case where pac exits before
//         the async Dataverse provisioning fully settles; the loop typically
//         confirms on the FIRST poll after pac's own internal wait. If the
//         app never appears within the deadline → Failure. Individual list-
//         call timeouts are treated as inconclusive polls and the loop
//         continues until the overall deadline.
//
// AUTH PROFILE ASSUMPTION (parity with PacOrgSettingsContractApplier +
// pre-Wave-G-4 PacCliSolutionVerifier):
//   pac CLI requires an authenticated profile pointing at the target env.
//   This installer does NOT invoke `pac auth create` itself — it assumes
//   the upstream L2 environment (either the App Service's UAMI-bound pac
//   auth profile per ADR-028 MI-outbound, or an operator-provisioned
//   profile) is already targeting the customer's Dataverse env. Every
//   pac invocation passes `--environment <dvUrl>` explicitly so cross-
//   tenant races on a shared pac profile still target the correct env;
//   if pac is entirely unauthenticated, install returns a non-zero exit
//   with a clear diagnostic which we surface as a Failure.
//
// FAILURE CLASSIFICATION (returned as RequiredApplicationsInstallOutcome.Failure):
//   - pac application install non-zero exit → Failure (H6 maps to
//     Resumable + MissingRequiredApplication).
//   - IProcessRunner TimeoutException on install → Failure (H6 maps to
//     Resumable + MissingRequiredApplication).
//   - IProcessRunner InvalidOperationException on install (pac binary not
//     on PATH) → Failure with PATH-remediation hint.
//   - Post-install list poll never confirms → Failure with poll-count in
//     the diagnostic.
//
// DIAGNOSTICS PRIVACY:
//   Truncates pac stdout/stderr to bounded tails before including in the
//   Failure diagnostic — matches PacOrgSettingsContractApplier's discipline.
//   pac application install / list do NOT print secret material (no
//   client-secret or KV values pass through), so no additional redaction is
//   required beyond the tail cap.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls: PacRequiredApplicationsInstaller (this LIVE impl) + test-only
//   stubs (StubRequiredApplicationsInstaller in H6SolutionImportHandlerTests
//   for H6-gate testing; PacRequiredApplicationsInstallerTests uses a fake
//   IProcessRunner for handler-scope live-behavior tests). No NIH.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Production <see cref="IRequiredApplicationsInstaller"/> — shells out to
/// <c>pac application install</c> per required app + polls
/// <c>pac application list</c> to confirm the app landed. Idempotent.
/// </summary>
public sealed class PacRequiredApplicationsInstaller : IRequiredApplicationsInstaller
{
    private const int StderrTailBudget = 800;
    private const int StdoutTailBudget = 400;

    private static readonly char[] s_lineSplit = { '\r', '\n' };

    /// <summary>
    /// Default per-app deadline (also used as the pac install-call timeout).
    /// F13 session-2 measured wall-clock: ~6 min per Power BI Anchor install;
    /// 10 min gives headroom for slower Dataverse regions without exceeding
    /// H6's own 60 min <see cref="SolutionImportOptions.ImportTimeout"/>.
    /// </summary>
    internal static readonly TimeSpan DefaultPerAppDeadline = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Default interval between post-install <c>pac application list</c>
    /// polls. F13 field notes: pac's internal poll cadence during install is
    /// 30s; we match that to keep our defense-in-depth loop from spamming
    /// the list endpoint faster than pac's own progress cadence.
    /// </summary>
    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default per-list-call timeout. <c>pac application list</c> against a
    /// healthy env returns in under 5s; 90s is generous headroom for slow
    /// tenants and matches the applier's <see cref="SolutionImportOptions.VerifierCallTimeout"/>.
    /// </summary>
    internal static readonly TimeSpan DefaultListTimeout = TimeSpan.FromSeconds(90);

    private readonly IProcessRunner _processRunner;
    private readonly SolutionImportOptions _options;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _perAppDeadline;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _listTimeout;
    private readonly ILogger<PacRequiredApplicationsInstaller> _logger;

    /// <summary>Constructs the installer bound to the pac CLI executable + default 10min/30s/90s cadence.</summary>
    public PacRequiredApplicationsInstaller(
        IProcessRunner processRunner,
        IOptions<SolutionImportOptions> options,
        TimeProvider clock,
        ILogger<PacRequiredApplicationsInstaller> logger)
        : this(processRunner, options, clock, DefaultPerAppDeadline, DefaultPollInterval, DefaultListTimeout, logger)
    { }

    /// <summary>
    /// Test-only overload: supplies custom per-app deadline / poll interval /
    /// list timeout so unit tests can exercise the poll loop + timeout paths
    /// in milliseconds instead of minutes. Internal so it is not part of the
    /// public surface (parity with <c>HttpHealthzProbe</c>'s internal backoff-
    /// schedule overload).
    /// </summary>
    internal PacRequiredApplicationsInstaller(
        IProcessRunner processRunner,
        IOptions<SolutionImportOptions> options,
        TimeProvider clock,
        TimeSpan perAppDeadline,
        TimeSpan pollInterval,
        TimeSpan listTimeout,
        ILogger<PacRequiredApplicationsInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        if (perAppDeadline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(perAppDeadline));
        if (pollInterval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (listTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(listTimeout));

        _processRunner = processRunner;
        _options = options.Value;
        _clock = clock;
        _perAppDeadline = perAppDeadline;
        _pollInterval = pollInterval;
        _listTimeout = listTimeout;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RequiredApplicationsInstallOutcome> EnsureInstalledAsync(
        RequiredApplicationsInstallRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDataverseUrl);

        if (request.RequiredApplicationNames.Count == 0)
        {
            _logger.LogDebug(
                "HANDLER-07: empty required-applications manifest for '{DataverseUrl}' — no work",
                request.TargetDataverseUrl);
            return new RequiredApplicationsInstallOutcome.Success(request.RequiredApplicationNames);
        }

        var appliedOrAlreadyPresent = new List<string>(request.RequiredApplicationNames.Count);

        foreach (var app in request.RequiredApplicationNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var perAppFailure = await EnsureSingleAppInstalledAsync(app, request.TargetDataverseUrl, cancellationToken)
                .ConfigureAwait(false);
            if (perAppFailure is not null)
            {
                return perAppFailure;
            }
            appliedOrAlreadyPresent.Add(app);
        }

        return new RequiredApplicationsInstallOutcome.Success(appliedOrAlreadyPresent);
    }

    /// <summary>
    /// Ensures ONE app is installed. Returns <c>null</c> on success (caller
    /// continues to the next app) or a <see cref="RequiredApplicationsInstallOutcome.Failure"/>
    /// on any failure (caller returns immediately — first failure short-
    /// circuits the manifest walk so the H6 gate diagnostic names the FIRST
    /// missing app rather than a compound list).
    /// </summary>
    private async Task<RequiredApplicationsInstallOutcome.Failure?> EnsureSingleAppInstalledAsync(
        string appName,
        string targetDataverseUrl,
        CancellationToken cancellationToken)
    {
        var startedAt = _clock.GetUtcNow();
        var deadline = startedAt + _perAppDeadline;

        // (1) Fire `pac application install`. Idempotent — pac no-ops if the
        //     app is already installed. Blocks synchronously (~6 min real-world
        //     per F13) while Dataverse provisions the app.
        _logger.LogInformation(
            "HANDLER-07: invoking pac application install for '{App}' on '{DataverseUrl}' (per-app deadline {Deadline})",
            appName, targetDataverseUrl, _perAppDeadline);

        ProcessResult installResult;
        try
        {
            installResult = await _processRunner.RunAsync(
                _options.PacCliExecutable,
                new[]
                {
                    "application", "install",
                    "--application-name", appName,
                    "--environment", targetDataverseUrl,
                },
                environment: null,
                timeout: _perAppDeadline,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException tex)
        {
            var diag =
                $"pac application install '{appName}' on '{targetDataverseUrl}' exceeded the " +
                $"{_perAppDeadline.TotalMinutes:F0}min per-app deadline: {tex.Message}. Operator can " +
                $"pre-install the app via `pac application install --application-name {appName} " +
                $"--environment {targetDataverseUrl}` (typical wall-clock ~6 min) and re-run H6.";
            _logger.LogWarning(tex, "HANDLER-07: {Diagnostic}", diag);
            return new RequiredApplicationsInstallOutcome.Failure(diag);
        }
        catch (InvalidOperationException iex)
        {
            var diag =
                $"pac application install failed to start ({_options.PacCliExecutable}) for '{appName}' " +
                $"on '{targetDataverseUrl}': {iex.Message}. Verify pac CLI is on PATH " +
                $"(or SolutionImportOptions:PacCliExecutable is set to the absolute path).";
            _logger.LogWarning(iex, "HANDLER-07: {Diagnostic}", diag);
            return new RequiredApplicationsInstallOutcome.Failure(diag);
        }

        if (installResult.ExitCode != 0)
        {
            var diag =
                $"pac application install '{appName}' on '{targetDataverseUrl}' exited " +
                $"{installResult.ExitCode}. Stderr: {Truncate(installResult.Stderr, StderrTailBudget)} " +
                $"Stdout tail: {Truncate(installResult.Stdout, StdoutTailBudget)}";
            _logger.LogWarning("HANDLER-07: {Diagnostic}", diag);
            return new RequiredApplicationsInstallOutcome.Failure(diag);
        }

        _logger.LogInformation(
            "HANDLER-07: pac application install '{App}' on '{DataverseUrl}' exited 0 — polling list to confirm",
            appName, targetDataverseUrl);

        // (2) Defense-in-depth: poll `pac application list` up to the remaining
        //     deadline. Typical case = install internally waited for Dataverse
        //     provisioning to settle, so the FIRST poll confirms. Rare case =
        //     pac exits before the async provisioning is visible via list; the
        //     loop tolerates that up to the per-app deadline.
        var pollNumber = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pollNumber++;

            var (isPresent, pollFailure) = await IsAppPresentAsync(appName, targetDataverseUrl, cancellationToken)
                .ConfigureAwait(false);
            if (pollFailure is not null)
            {
                // Deterministic infrastructure fault (pac PATH gone mid-poll) —
                // surface as a Failure with the poll number so operator triage
                // separates "install succeeded, then env glitched" from "install
                // never worked".
                var diag =
                    $"pac application list infrastructure fault at poll {pollNumber} for '{appName}' on " +
                    $"'{targetDataverseUrl}' after successful install: {pollFailure}";
                _logger.LogWarning("HANDLER-07: {Diagnostic}", diag);
                return new RequiredApplicationsInstallOutcome.Failure(diag);
            }

            if (isPresent)
            {
                _logger.LogInformation(
                    "HANDLER-07: '{App}' confirmed installed on '{DataverseUrl}' after {Polls} poll(s)",
                    appName, targetDataverseUrl, pollNumber);
                return null;
            }

            var now = _clock.GetUtcNow();
            if (now >= deadline)
            {
                var elapsed = now - startedAt;
                var diag =
                    $"pac application install '{appName}' on '{targetDataverseUrl}' exited 0 but the app " +
                    $"did not appear in pac application list within {_perAppDeadline.TotalMinutes:F0}min " +
                    $"(polled {pollNumber} time(s), elapsed {elapsed.TotalSeconds:F0}s). Operator can " +
                    $"verify manually via `pac application list --environment {targetDataverseUrl}` and " +
                    $"re-run H6 once the app is present.";
                _logger.LogWarning("HANDLER-07: {Diagnostic}", diag);
                return new RequiredApplicationsInstallOutcome.Failure(diag);
            }

            var remaining = deadline - now;
            var sleep = _pollInterval < remaining ? _pollInterval : remaining;
            if (sleep > TimeSpan.Zero)
            {
                await Task.Delay(sleep, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// One post-install poll. Returns <c>(isPresent, pollFailure)</c> — a
    /// deterministic pac infrastructure fault (PATH gone, unreadable stderr,
    /// exec-not-found) surfaces via <c>pollFailure</c> so the caller aborts;
    /// a normal "app not yet visible" returns <c>(false, null)</c> so the
    /// caller keeps polling until the per-app deadline. Per-call
    /// TimeoutException is treated as an inconclusive poll (not fatal).
    /// </summary>
    private async Task<(bool IsPresent, string? PollFailure)> IsAppPresentAsync(
        string appName,
        string targetDataverseUrl,
        CancellationToken cancellationToken)
    {
        ProcessResult listResult;
        try
        {
            listResult = await _processRunner.RunAsync(
                _options.PacCliExecutable,
                new[]
                {
                    "application", "list",
                    "--environment", targetDataverseUrl,
                },
                environment: null,
                timeout: _listTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Single-poll timeout = inconclusive; keep polling within overall deadline.
            return (false, null);
        }
        catch (InvalidOperationException iex)
        {
            // pac binary missing mid-run — deterministic infra fault worth
            // failing on (a benign re-poll won't fix it).
            return (false, $"pac binary invocation failed: {iex.Message}");
        }

        if (listResult.ExitCode != 0)
        {
            // Non-zero list exit = probably ambient auth issue that pac install
            // wouldn't have seen (auth revoked between calls, env torn down,
            // etc.). Treat as inconclusive rather than infra fault so a
            // transient list failure doesn't fail the whole install; the
            // per-app deadline still bounds the tolerance.
            return (false, null);
        }

        return (ContainsApp(listResult.Stdout, appName), null);
    }

    /// <summary>
    /// Case-insensitive substring scan of the pac application list stdout for
    /// the app's unique name. Real pac output shape (canonical, from a live
    /// session):
    /// <code>
    /// Application Name        Application Id                        Publisher            Version    State
    /// ---------------------  ------------------------------------- -------------------- --------- ---------
    /// msft_PowerBI_Anchor    aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee  Microsoft Corporation 1.0.0.0   Installed
    /// </code>
    /// Substring-in-line is intentionally permissive — after our own
    /// `pac application install` returned 0, the app IS installed; the poll
    /// only exists to bridge the tiny window where pac exits before Dataverse's
    /// async provisioning is visible to a follow-up list. Exposed <c>internal</c>
    /// for direct unit testing (parity with <see cref="PacOrgSettingsContractApplier.ParseListOutput"/>).
    /// </summary>
    internal static bool ContainsApp(string? listOutput, string appUniqueName)
    {
        if (string.IsNullOrWhiteSpace(listOutput) || string.IsNullOrWhiteSpace(appUniqueName))
        {
            return false;
        }

        foreach (var rawLine in listOutput.Split(s_lineSplit, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            // Reject pure-separator lines (dashes only).
            if (line.All(c => c == '-' || c == '─' || c == '=' || c == ' ')) continue;
            if (line.Contains(appUniqueName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s ?? string.Empty
            : s[..max] + "...[truncated]";
}
