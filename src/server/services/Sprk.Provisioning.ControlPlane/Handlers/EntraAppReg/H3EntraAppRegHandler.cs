// -----------------------------------------------------------------------------
// H3EntraAppRegHandler.cs
//
// L2 CONTROL-PLANE H3 Entra app-registration handler (task 046, wave C4).
//
// PURPOSE:
//   Provisions the single BFF app-registration per customer + verifies its
//   14 Graph application-role grants are admin-consented, by wrapping the
//   hardened scripts/Register-EntraAppRegistrations.ps1 (post-r1 task 010,
//   commit fea66c023 — full Get-then-Add reconciler idempotency). The
//   Dataverse S2S app-registration is EXPLICITLY NOT provisioned per r3
//   task 060 (zero code consumers; BFF app-reg IS the Dataverse Application
//   User).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-06 (H3):
//       1 Entra app registration (BFF API) with ~14 Graph + Dynamics
//       permission grants per GraphAppRoles.cs. Sign-in audience
//       AzureADMultipleOrgs (v3 change enables Model 2 consent). Client
//       secret stored as KV URI reference. Admin consent gate verified via
//       Graph query. S2S Dataverse app-reg explicitly NOT provisioned.
//       Acceptance: script idempotent; -TenantId mandatory; 14 permissions
//       returned by Graph oauth2PermissionGrants query.
//   - projects/customer-provisioning-orchestration-r1/spec.md § MUST rules:
//       Dataverse S2S app-reg MUST NOT be re-introduced (r3 task 060).
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1:
//       -TenantId is MANDATORY on the invoked PS script; no default tenant
//       fallback. Handler validates parameter before invoking.
//   - projects/customer-provisioning-orchestration-r1/spec.md NFR-09:
//       Graph v6 / Kiota 2.0 error type — catch ODataError (not
//       ServiceException); ResponseStatusCode is int. Wave C5's real
//       IAdminConsentVerifier impl honors this rule.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H3 row:
//       Wraps hardened script; owns admin-consent WaitingOnGate transition;
//       writes bffAppRegId to interStepState.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       H3 provisioning failures are Resumable (operator resolves consent /
//       permissions / connectivity + POST /api/runs/{id}/resume);
//       admin-consent PENDING is WaitingOnGate (NOT a failure);
//       cleartext-secret-leak + S2S-forbidden are Quarantine-required
//       (write-side data-leak / orphaned state).
//   - .claude/adr/ADR-004: single IJobHandler impl registered in L2 DI.
//   - .claude/adr/ADR-010: register in L2, NOT BFF.
//   - .claude/adr/ADR-028: auth ceremony follows the 21 MUSTs. Client secret
//       stored as KV URI reference; NEVER in cleartext in Cosmos parameters.
//   - .claude/adr/ADR-036: reuse background-job infrastructure; return 202;
//       fire-and-forget.
//   - .claude/adr/ADR-044: bffAppRegId written to Cosmos interStepState is a
//       Graph appId GUID (lowercase canonical).
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌──────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                         │ §4C class                 │
//   ├──────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId (§4D I1)            │ Resumable                 │
//   │ Missing keyVaultName                 │ Resumable                 │
//   │ Run not found in Cosmos partition    │ Resumable                 │
//   │ Provisioner PS script hard failure   │ Resumable                 │
//   │ Provisioner outputs incomplete       │ Resumable                 │
//   │ Admin consent Pending (WaitingOnGate │ (NOT a failure — Success  │
//   │ transition)                          │ with WaitingOnGate state) │
//   │ Cleartext-secret-leak detected in    │ QuarantineRequired        │
//   │ provisioner output                   │ (data-leak — must not     │
//   │                                      │ silently proceed)         │
//   │ S2S app-reg accidentally provisioned │ QuarantineRequired        │
//   │ (spec MUST rule violation)           │ (orphaned Entra state)    │
//   │ Concurrent Cosmos writer conflict    │ Resumable                 │
//   │ Run row deleted mid-flight           │ Resumable                 │
//   └──────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): future reconciler computes
//           deterministic MessageId per (HandlerId, RunId, CustomerId,
//           paramHash); SB duplicate-detection collapses re-enqueues.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (design.md
//           §4.1 preamble; parity with H0 / H0.5 / H1 / H2a).
//   Level 3 (handler body durable dedup): this handler scans
//           ProvisioningRun.CompletedPhases for (Phase=="H3",
//           IdempotencyKey==appreg-{customerId}-{tenantId}). Match ⇒ Success
//           no-op. Key includes tenantId per POML constraint (Model 2
//           customer tenants are per-customer distinct; Model 1 shares
//           Spaarke's tenant — same customerId different tenants would be a
//           bug the handler cannot detect, but same (customerId, tenantId)
//           tuple always short-circuits).
//
// DOWNSTREAM ENQUEUE (Wave C4 note):
//   H3 does NOT enqueue a specific successor. Parity with H2a (task 044): the
//   downstream DAG per design.md §4.1 fans out from H2a to H2b/H3/H4/H5 in
//   parallel — the wave-C5 reconciler owns fan-out. Downstream H4 (task 047
//   in Batch 3D) reads bffAppRegId from interStepState — the reconciler
//   observes this handler's completion + dispatches H4 based on Cosmos state.
//   For wave C4 this handler mutates Cosmos state (CurrentPhase +
//   CompletedPhases + InterStepState + optionally WaitingOnGate on admin-
//   consent Pending) and returns Success.
//
// ADMIN-CONSENT SEMANTICS:
//   Verifier returns Verified → CompletedPhase(H3) + admin-consent gate
//                                = Verified; Success.
//   Verifier returns Pending  → NO CompletedPhase entry (H3 has not
//                                completed its job yet); run.Status =
//                                WaitingOnGate; admin-consent gate =
//                                Pending. Handler returns SUCCESS
//                                (envelope was processed correctly; the
//                                gate is external, not a handler failure).
//                                The reconciler's future WaitingOnGate scan
//                                (or an operator-invoked resume after
//                                granting consent) re-dispatches H3, which
//                                will find the app-reg already exists
//                                (idempotent script no-op) then re-verify
//                                consent (which should now Verify).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H3EntraAppRegHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = "H3";

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the target Key Vault name (feeds script <c>-KeyVaultName</c>).</summary>
    public const string KeyVaultNameParameterKey = "keyVaultName";

    /// <summary>
    /// Simple heuristics to detect a cleartext secret pattern accidentally
    /// smuggled into the provisioner output. NOT a cryptographic scan — a
    /// forcing-function to catch obvious regressions (e.g. someone replaces
    /// the KV URI ref with the raw <c>xxxxx~xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx</c>
    /// secret literal). The KV URI reference literal MUST start with
    /// <c>@Microsoft.KeyVault(</c>; anything else that looks secret-shaped
    /// trips this guard.
    /// </summary>
    private static readonly Regex[] CleartextSecretPatterns =
    {
        // Client-secret literals typically look like "Nxx8Q~..." (43+ chars, mixed alnum + ~._-).
        new(@"[A-Za-z0-9~._\-]{40,}", RegexOptions.CultureInvariant | RegexOptions.Compiled,
            matchTimeout: TimeSpan.FromSeconds(1)),
    };

    private readonly IProvisioningRunRepository _repository;
    private readonly IEntraAppRegProvisioner _provisioner;
    private readonly IAdminConsentVerifier _consentVerifier;
    private readonly EntraAppRegOptions _options;
    private readonly ILogger<H3EntraAppRegHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H3 Entra app-reg handler. All collaborators are
    /// interface-abstracted so unit tests can substitute stubs.
    /// </summary>
    public H3EntraAppRegHandler(
        IProvisioningRunRepository repository,
        IEntraAppRegProvisioner provisioner,
        IAdminConsentVerifier consentVerifier,
        IOptions<EntraAppRegOptions> options,
        ILogger<H3EntraAppRegHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(provisioner);
        ArgumentNullException.ThrowIfNull(consentVerifier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _provisioner = provisioner;
        _consentVerifier = consentVerifier;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);

        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            // Defensive: the reconciler routes by HandlerId string match. A
            // mismatch here means a dispatch bug — fail loud rather than
            // silently mis-executing (parity with H2aBicepInfraDeployHandler).
            throw new InvalidOperationException(
                $"H3EntraAppRegHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H3 Entra app-reg starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H3 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) §4D I1 tenant guard — H3 MUST NOT fall back to a default tenant.
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H3 (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this " +
                "before H3 dispatches. Register-EntraAppRegistrations.ps1's -TenantId parameter " +
                "is [Mandatory = $true] per script commit fea66c023.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.MissingTenantId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (3) Key Vault name guard — the script requires -KeyVaultName for
        //     BFF-API-ClientSecret storage. Missing → refuse to invoke.
        if (!TryGetNonEmpty(parameters, KeyVaultNameParameterKey, out var keyVaultName))
        {
            var diagnostic =
                "Run parameter 'keyVaultName' is required by H3 (feeds script's -KeyVaultName). " +
                "Upstream handler (H2a Bicep) MUST populate this from the deployed platform KV " +
                "name (e.g. 'sprk-{customerId}-prod-kv') before H3 dispatches.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.MissingKeyVaultName, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (4) Expected-role-count guard — ExpectedAppRoleCount MUST be >= 1
        //     (defaults to 14 per r1 task 005 populating GraphAppRoles.cs).
        //     Zero / negative would silently pass the admin-consent Verified
        //     branch even when NO grants exist — refuse.
        if (_options.ExpectedAppRoleCount < 1)
        {
            var diagnostic =
                $"EntraAppReg:ExpectedAppRoleCount is {_options.ExpectedAppRoleCount} — MUST be >= 1. " +
                "Configuration drift; defaults to 14 per Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles. " +
                "The nightly Graph-app-role-parity ArchTest (task 067) enforces this count stays in sync.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.NullAppRoleIdInCatalog, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, tenantId);

        // (5) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H3 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (6) Invoke the provisioner. Domain failures do NOT throw (Failure);
        //     only unexpected infra faults throw. §4C Resumable on hard fail
        //     — Entra app-reg is idempotent, so re-running after operator
        //     fixes the precondition is safe.
        EntraAppRegOutcome outcome;
        try
        {
            var request = new EntraAppRegRequest(
                CustomerId: envelope.CustomerId,
                TenantId: tenantId,
                KeyVaultName: keyVaultName);
            outcome = await _provisioner.ProvisionAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H3 Entra app-reg provisioning infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Entra app-reg provisioning infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Verify pwsh + az CLI are on PATH and the operator has Graph API + KV access on the target tenant. " +
                "This is Resumable — operator resolves the precondition and POSTs /api/runs/{id}/resume.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.ProvisioningFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (outcome is EntraAppRegOutcome.Failure runnerFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.ProvisioningFailed, runnerFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        var outputs = ((EntraAppRegOutcome.Success)outcome).Outputs;

        // (7) Structural outputs guard — reject any incomplete output payload.
        if (string.IsNullOrWhiteSpace(outputs.BffAppRegId)
            || string.IsNullOrWhiteSpace(outputs.BffClientSecretKvUri))
        {
            var diagnostic =
                "Entra app-reg provisioner returned incomplete outputs — one of BffAppRegId / " +
                "BffClientSecretKvUri is blank. Verify Register-EntraAppRegistrations.ps1 summary " +
                "output contains the 'Application ID: {guid}' line + the runner assembled the KV URI ref.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.ProvisioningOutputsIncomplete, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (8) ADR-028 cleartext-secret-leak guard — the KV URI ref MUST start
        //     with '@Microsoft.KeyVault('; anything else that looks
        //     secret-shaped is a data-leak (Quarantine-required, write-side
        //     surface — do NOT persist to Cosmos).
        if (IsCleartextSecretPattern(outputs.BffClientSecretKvUri))
        {
            var diagnostic =
                "Entra app-reg provisioner returned a client-secret-shaped value where a KV URI " +
                "reference was expected — ADR-028 MUST rule violation. Refusing to persist to " +
                "Cosmos interStepState. Verify RegisterEntraAppRegScriptProvisioner.BuildKvUriReference " +
                "is used to construct the ref (starts with '@Microsoft.KeyVault(SecretUri=...').";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                EntraAppRegRejectionCodes.CleartextSecretLeak, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (9) S2S-forbidden structural guard — MUST NOT re-introduce Dataverse
        //     S2S app-reg per r3 task 060. The provisioner output shape lacks a
        //     S2sAppRegId field (compile-time guard). Belt-and-braces runtime
        //     guard: if interStepState already carries an S2sAppRegId from a
        //     prior (mistaken) write, refuse to advance.
        if (!string.IsNullOrWhiteSpace(run.InterStepState.S2SAppRegId))
        {
            var diagnostic =
                $"ProvisioningRun.interStepState.s2sAppRegId is populated ('{run.InterStepState.S2SAppRegId}') — " +
                "spec MUST rule violation (r3 task 060 dropped the Dataverse S2S app-reg; BFF app-reg IS " +
                "the single Dataverse Application User). Handler refuses to advance while orphaned S2S " +
                "state remains recorded — operator must clear + explain provenance.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                EntraAppRegRejectionCodes.S2SAppRegForbidden, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (10) Admin-consent verification. Verified → CompletedPhase + Success.
        //      Pending → WaitingOnGate transition + Success (envelope-processing
        //      succeeded; the gate is external, not a handler failure).
        AdminConsentVerificationResult consentResult;
        try
        {
            consentResult = await _consentVerifier.VerifyAsync(
                outputs.BffAppRegId,
                tenantId,
                _options.ExpectedAppRoleCount,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Wave-C5 real Graph impl may throw on transient SDK faults;
            // Wave C4 Null verifier never throws. Classify Resumable so
            // operator can resume after Graph clears — the app-reg was
            // successfully provisioned, only the consent verification is
            // in doubt.
            _logger.LogError(ex,
                "H3 admin-consent verifier threw unexpected exception: runId={RunId} customerId={CustomerId} bffAppRegId={BffAppRegId}",
                envelope.RunId, envelope.CustomerId, outputs.BffAppRegId);
            var diagnostic =
                $"Admin-consent verifier infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                $"App-registration WAS provisioned (bffAppRegId={outputs.BffAppRegId}); only consent " +
                "verification failed. This is Resumable — operator resolves Graph connectivity + resumes.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                EntraAppRegRejectionCodes.ProvisioningFailed, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "H3 Entra app-reg provisioning succeeded: runId={RunId} customerId={CustomerId} " +
            "bffAppRegId={BffAppRegId} adminConsent={ConsentState} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, outputs.BffAppRegId,
            consentResult.GetType().Name, stopwatch.ElapsedMilliseconds);

        return await MarkAdvanceAsync(run, etag, idempotencyKey, outputs, envelope,
            consentResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H3 idempotency key:
    /// <c>appreg-{customerId}-{tenantId}</c>. Exposed internal so unit tests
    /// can construct expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return $"appreg-{customerId}-{tenantId}";
    }

    /// <summary>
    /// Detects candidate cleartext-secret patterns in the given value. Exposed
    /// internal so unit tests can validate the guard's coverage. Any value
    /// that BEGINS with <c>@Microsoft.KeyVault(</c> is treated as a KV URI
    /// reference literal (safe) and short-circuits to <c>false</c>.
    /// </summary>
    internal static bool IsCleartextSecretPattern(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.StartsWith("@Microsoft.KeyVault(", StringComparison.Ordinal)) return false;

        foreach (var pattern in CleartextSecretPatterns)
        {
            try
            {
                if (pattern.IsMatch(value)) return true;
            }
            catch (RegexMatchTimeoutException)
            {
                // Timeout — treat as suspicious (safer than assuming clean).
                return true;
            }
        }
        return false;
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

        run.GateStates[$"h3-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H3 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H3 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkAdvanceAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        EntraAppRegOutputs outputs,
        HandlerEnvelope envelope,
        AdminConsentVerificationResult consentResult,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        // Write bffAppRegId regardless of consent state — H4 needs it whether
        // consent is Verified now or pending; when the reconciler resumes the
        // run, H4 will consume this value.
        run.InterStepState.BffAppRegId = outputs.BffAppRegId;
        run.ErrorDetail = null;

        if (consentResult is AdminConsentVerificationResult.Pending pending)
        {
            // WaitingOnGate — envelope processed correctly, but downstream
            // fan-out MUST wait for tenant admin to grant consent. Do NOT add
            // a CompletedPhase entry (H3 hasn't finished its job yet).
            run.Status = RunStatus.WaitingOnGate;
            run.CurrentPhase = HandlerIdentifier;
            run.GateStates[EntraAppRegGates.AdminConsent] = new GateEntry
            {
                Status = GateState.Pending,
                VerifierHandler = HandlerIdentifier,
                Evidence = pending.Evidence,
            };

            _logger.LogInformation(
                "H3 admin-consent Pending — run transitioned to WaitingOnGate: " +
                "runId={RunId} customerId={CustomerId} bffAppRegId={BffAppRegId} " +
                "granted={GrantedCount}/{ExpectedCount}",
                run.RunId, run.CustomerId, outputs.BffAppRegId,
                pending.GrantedCount, pending.ExpectedCount);

            var pendingReplace = await _repository.ReplaceRunAsync(run, etag, cancellationToken)
                .ConfigureAwait(false);
            return HandlePendingReplace(pendingReplace, run, idempotencyKey);
        }

        // Verified — H3 completed its full job.
        var verified = (AdminConsentVerificationResult.Verified)consentResult;

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out (H4).
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.GateStates[EntraAppRegGates.AdminConsent] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
            Evidence = verified.Evidence,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H3 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H3 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H3.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H3 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.RunDeletedDuringProvisioning,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H3 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }

    private HandlerResult HandlePendingReplace(
        ReplaceRunResult replace,
        ProvisioningRun run,
        string idempotencyKey)
    {
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H3 WaitingOnGate write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H3 read + WaitingOnGate write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H3.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: EntraAppRegRejectionCodes.RunDeletedDuringProvisioning,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H3 was writing WaitingOnGate.");
        }

        // Success on the WaitingOnGate write path — H3 processed the envelope
        // successfully; the reconciler observes WaitingOnGate + waits for
        // operator resume (spec.md FR-06 admin-consent-gate branch).
        return new HandlerResult.Success(idempotencyKey);
    }
}
