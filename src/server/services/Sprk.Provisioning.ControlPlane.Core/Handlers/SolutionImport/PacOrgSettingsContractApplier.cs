// -----------------------------------------------------------------------------
// PacOrgSettingsContractApplier.cs
//
// HANDLER-08 (Wave 2 pre-dispatch remediation 2026-08-27) — F14 verbatim.
// Production <see cref="IOrgSettingsContractApplier"/> impl. LIVE
// (2026-08-27 Wave 2.5 replacement of the Wave 2 scaffold): shells out to
// `pac org list-settings` for a read-first idempotency check, then
// `pac org update-settings --name X --value Y` per setting that needs a bump.
// Wraps System.Diagnostics.Process behind the shared
// <see cref="IProcessRunner"/> seam so unit tests substitute a fake runner
// (parity with H4b's Configure-script shell-out).
//
// F14 (SKILL.md "Fresh-Sub Automation Gaps" §):
//   Fresh Production-tier Dataverse envs default
//   organization.maxuploadfilesize = 5,242,880 (5 MB). The
//   UniversalDocumentUpload PCF bundle exceeds this → solution import fails
//   5 min in with "Webresource content size is too big". Pre-import: read the
//   canonical Org Settings contract, compare against current values, and
//   auto-apply any drift via `pac org update-settings`. Idempotent + fast
//   (~2 s per setting). Initial contract: `maxuploadfilesize: 25_600_000`
//   (25 MB — matches spaarkedev1 reference env).
//
// IDEMPOTENCY:
//   Level-3 durable dedup lives on H6SolutionImportHandler (idempotency key
//   solimport-{customerId}-{catalogHash}). This applier adds a per-setting
//   read-first check so a re-run within the same H6 attempt (or a subsequent
//   H6 attempt whose Level-3 key didn't match) does NOT redundantly write
//   settings that are already at-or-above the target value. Numeric settings
//   (maxuploadfilesize + friends) use long-parse compare; unparseable values
//   fall back to case-insensitive string equality.
//
// AUTH PROFILE ASSUMPTION (parity with the pre-Wave-G-4 PacCliSolutionVerifier):
//   pac CLI requires an authenticated profile pointing at the target env.
//   This applier does NOT invoke `pac auth create` itself — it assumes the
//   upstream L2 environment (either the App Service's UAMI-bound pac auth
//   profile per ADR-028 MI-outbound, or an operator-provisioned profile) is
//   already targeting the customer's Dataverse env. Every pac invocation
//   passes `--environment <dvUrl>` explicitly so cross-tenant races on a
//   shared pac profile still target the correct env; but if pac is entirely
//   unauthenticated, list-settings returns a non-zero exit with a clear
//   diagnostic which we surface as a Failure.
//
//   Rationale: `pac auth create` mutates the ambient CLI profile
//   process-globally; invoking it from a background worker on every ApplyAsync
//   is racy against concurrent H6 attempts for different customers. A future
//   task can move to per-invocation `--profile <ephemeral-name>` isolation if
//   the ambient-profile assumption ever proves brittle. For the r1 delivery
//   the shared-profile / explicit-`--environment` pattern matches what the
//   retired H5/H6 pac shell-outs did.
//
// FAILURE CLASSIFICATION (returned as OrgSettingsContractOutcome.Failure):
//   - pac list-settings non-zero exit → Failure (H6 maps to Resumable +
//     OrgSettingsContractFailed).
//   - pac update-settings non-zero exit → Failure (H6 maps to Resumable +
//     OrgSettingsContractFailed).
//   - IProcessRunner TimeoutException / InvalidOperationException → Failure
//     with the wrapped error text; H6 maps to Resumable.
//   - The H6 handler catches unexpected exceptions from ApplyAsync (see
//     step 7.6 in H6SolutionImportHandler) so uncontrolled process-runner
//     faults never quarantine the run.
//
// DIAGNOSTICS PRIVACY:
//   Truncates pac stdout/stderr to bounded tails before including in the
//   Failure diagnostic — matches PacCliSolutionVerifier / H4b's redaction
//   discipline. pac org list-settings / update-settings do NOT print
//   secret material (no client-secret or KV values pass through pac org
//   commands), so no additional redaction is required beyond the tail cap.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Production <see cref="IOrgSettingsContractApplier"/> — shells out to
/// <c>pac org list-settings</c> + <c>pac org update-settings</c> with an
/// idempotent read-first compare per setting.
/// </summary>
public sealed class PacOrgSettingsContractApplier : IOrgSettingsContractApplier
{
    private const int StderrTailBudget = 800;
    private const int StdoutTailBudget = 400;

    private static readonly char[] s_lineSplit = { '\r', '\n' };
    private static readonly char[] s_tokenSplit = { ' ', '\t' };

    private readonly IProcessRunner _processRunner;
    private readonly SolutionImportOptions _options;
    private readonly ILogger<PacOrgSettingsContractApplier> _logger;

    /// <summary>Constructs the applier bound to the pac CLI executable + per-call timeout.</summary>
    public PacOrgSettingsContractApplier(
        IProcessRunner processRunner,
        IOptions<SolutionImportOptions> options,
        ILogger<PacOrgSettingsContractApplier> logger)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _processRunner = processRunner;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OrgSettingsContractOutcome> ApplyAsync(
        OrgSettingsContractApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDataverseUrl);

        if (request.OrgSettings.Count == 0)
        {
            _logger.LogDebug(
                "HANDLER-08: empty Org Settings contract for '{DataverseUrl}' — no work",
                request.TargetDataverseUrl);
            return new OrgSettingsContractOutcome.Success(request.OrgSettings);
        }

        // (1) Read the current settings ONCE — reused for every per-setting
        //     idempotency check below. Single shell-out amortizes the pac
        //     startup cost across the (small) contract set.
        ProcessResult listResult;
        try
        {
            listResult = await _processRunner.RunAsync(
                _options.PacCliExecutable,
                new[]
                {
                    "org", "list-settings",
                    "--environment", request.TargetDataverseUrl,
                },
                environment: null,
                timeout: _options.VerifierCallTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException tex)
        {
            var diag =
                $"pac org list-settings timed out after {_options.VerifierCallTimeout.TotalSeconds:F0}s for " +
                $"'{request.TargetDataverseUrl}': {tex.Message}";
            _logger.LogWarning(tex, "HANDLER-08: {Diagnostic}", diag);
            return new OrgSettingsContractOutcome.Failure(diag);
        }
        catch (InvalidOperationException iex)
        {
            var diag =
                $"pac org list-settings failed to start for '{request.TargetDataverseUrl}': {iex.Message}. " +
                "Verify pac CLI is on PATH (or SolutionImportOptions:PacCliExecutable is set).";
            _logger.LogWarning(iex, "HANDLER-08: {Diagnostic}", diag);
            return new OrgSettingsContractOutcome.Failure(diag);
        }

        if (listResult.ExitCode != 0)
        {
            var diag =
                $"pac org list-settings exited {listResult.ExitCode} for '{request.TargetDataverseUrl}'. " +
                $"Stderr: {Truncate(listResult.Stderr, StderrTailBudget)} " +
                $"Stdout tail: {Truncate(listResult.Stdout, StdoutTailBudget)}";
            return new OrgSettingsContractOutcome.Failure(diag);
        }

        var currentByName = ParseListOutput(listResult.Stdout);
        _logger.LogInformation(
            "HANDLER-08: pac org list-settings parsed {Count} setting(s) for '{DataverseUrl}'",
            currentByName.Count, request.TargetDataverseUrl);

        var appliedOrAlreadyCorrect = new Dictionary<string, string>(
            request.OrgSettings.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in request.OrgSettings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var settingName = kv.Key;
            var targetValue = kv.Value;

            // (2) Idempotency: skip if the current value is already at-or-above
            //     the target. maxuploadfilesize increases monotonically in this
            //     contract; the compare uses numeric semantics for parseable
            //     values so an env at 30 MB does not get downgraded to 25 MB.
            if (currentByName.TryGetValue(settingName, out var currentValue)
                && SettingAlreadyAtOrAboveTarget(currentValue, targetValue))
            {
                _logger.LogInformation(
                    "HANDLER-08: setting '{Setting}' already at '{Current}' (target '{Target}') on '{DataverseUrl}' — skip",
                    settingName, currentValue, targetValue, request.TargetDataverseUrl);
                appliedOrAlreadyCorrect[settingName] = currentValue;
                continue;
            }

            // (3) Apply. `pac org update-settings --name X --value Y
            //     --environment <url>` — one shell-out per setting so each
            //     update's exit code is independently classified.
            ProcessResult updateResult;
            try
            {
                updateResult = await _processRunner.RunAsync(
                    _options.PacCliExecutable,
                    new[]
                    {
                        "org", "update-settings",
                        "--name", settingName,
                        "--value", targetValue,
                        "--environment", request.TargetDataverseUrl,
                    },
                    environment: null,
                    timeout: _options.VerifierCallTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException tex)
            {
                var diag =
                    $"pac org update-settings timed out after {_options.VerifierCallTimeout.TotalSeconds:F0}s " +
                    $"applying '{settingName}={targetValue}' on '{request.TargetDataverseUrl}': {tex.Message}";
                _logger.LogWarning(tex, "HANDLER-08: {Diagnostic}", diag);
                return new OrgSettingsContractOutcome.Failure(diag);
            }
            catch (InvalidOperationException iex)
            {
                var diag =
                    $"pac org update-settings failed to start applying '{settingName}={targetValue}' on " +
                    $"'{request.TargetDataverseUrl}': {iex.Message}.";
                _logger.LogWarning(iex, "HANDLER-08: {Diagnostic}", diag);
                return new OrgSettingsContractOutcome.Failure(diag);
            }

            if (updateResult.ExitCode != 0)
            {
                var diag =
                    $"pac org update-settings exited {updateResult.ExitCode} applying '{settingName}={targetValue}' " +
                    $"on '{request.TargetDataverseUrl}'. Stderr: {Truncate(updateResult.Stderr, StderrTailBudget)} " +
                    $"Stdout tail: {Truncate(updateResult.Stdout, StdoutTailBudget)}";
                return new OrgSettingsContractOutcome.Failure(diag);
            }

            _logger.LogInformation(
                "HANDLER-08: setting '{Setting}' applied '{Target}' (was '{Current}') on '{DataverseUrl}'",
                settingName, targetValue,
                currentByName.TryGetValue(settingName, out var was) ? was : "(unset)",
                request.TargetDataverseUrl);
            appliedOrAlreadyCorrect[settingName] = targetValue;
        }

        return new OrgSettingsContractOutcome.Success(appliedOrAlreadyCorrect);
    }

    /// <summary>
    /// Parses pac org list-settings tabular stdout into a (settingName →
    /// currentValue) map. Format is name-then-value token per line; header
    /// / separator lines are rejected. Exposed <c>internal</c> for direct
    /// unit testing (parity with <see cref="PacCliSolutionVerifier.ParseListOutput"/>).
    ///
    /// Real pac CLI ≥ 1.30 shape (canonical, from a live session):
    /// <code>
    /// Setting Name              Value
    /// -------------------------- --------
    /// maxuploadfilesize          5242880
    /// maxdepthforhierarchicalsecuritymodel 100
    /// ...
    /// </code>
    /// Token-based (not positional) split — survives column-width drift + a
    /// header row that uses either "Setting Name" or "Name" or "NAME".
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ParseListOutput(string? stdout)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return map;
        }

        foreach (var rawLine in stdout.Split(s_lineSplit, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            // Reject pure-separator lines (dashes only).
            if (line.All(c => c == '-' || c == '─' || c == '=' || c == ' ')) continue;

            var tokens = line.Split(s_tokenSplit, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;

            var name = tokens[0];
            var value = tokens[^1];  // last token = value (survives multi-word "Setting Name" header)

            // Skip header rows (any case variation of "Setting Name" / "Name" /
            // "NAME"); tokens[0] alone catches "Name" / "NAME"; the "Setting"
            // prefix catches the "Setting Name" header where tokens[0] == "Setting".
            if (string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Setting", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            map[name] = value;
        }

        return map;
    }

    /// <summary>
    /// Numeric-first idempotency check: prefer <see cref="long"/> compare
    /// (matches maxuploadfilesize + peers); fall back to case-insensitive
    /// string equality. Exposed <c>internal</c> for unit testing.
    /// </summary>
    internal static bool SettingAlreadyAtOrAboveTarget(string current, string target)
    {
        if (long.TryParse(current, out var currentNum)
            && long.TryParse(target, out var targetNum))
        {
            return currentNum >= targetNum;
        }
        return string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s ?? string.Empty
            : s[..max] + "...[truncated]";
}
