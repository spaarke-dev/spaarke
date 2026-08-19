// -----------------------------------------------------------------------------
// H10DataverseAppUserGraphParityHandler.cs
//
// L2 CONTROL-PLANE H10 Dataverse App User + Graph app-role parity handler
// (task 053, wave C4 Batch 3E). T2 + T3 silent-fail trap owner.
//
// PURPOSE:
//   Registers the BFF app-registration AND the UAMI as Dataverse System
//   Administrator Application Users on the target Dataverse environment, then
//   syncs the 14-role Microsoft Graph application (app-only) permission
//   catalog (Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles, mirrored locally
//   via IGraphAppRolesRegistry — see that file's header for the L2/BFF
//   assembly-isolation rationale) onto the UAMI service principal.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-13 (H10
//     acceptance) + FR-33 (T2 + T3 silent-fail traps) + § MUST rules (H10
//     escalation gate: 11-of-14-then-14-of-14 null AppRoleId completion
//     BEFORE first production customer).
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1: tenantId
//     flows explicitly; no default-tenant fallback.
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I5: Graph
//     token acquisition is per-tenant scoped (.default + explicit tenant),
//     never an ambient default-tenant credential.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H10 row
//     + §4B T2/T3 trap catalog + §9.3 R2 note (M-10 interim) + §4C rollback.
//   - .claude/adr/ADR-004: single IJobHandler-shape impl registered in L2 DI.
//   - .claude/adr/ADR-010: register in L2, NOT BFF.
//   - .claude/adr/ADR-028: auth ceremony 21 MUSTs — Dataverse App User (BFF +
//     UAMI) + Graph app-role parity are specific MUSTs this handler owns.
//   - .claude/adr/ADR-036: reuse background-job infrastructure; fire-and-forget.
//   - .claude/adr/ADR-044: systemuserid + applicationid + role IDs are lowercase
//     canonical GUIDs (no braces) when written to Cosmos.
//
// NFR-09 IMPLEMENTATION NOTE (Path C — pivot to comply in spirit, per root
// CLAUDE.md §6.5): spec.md NFR-09 describes the BFF's Microsoft.Graph SDK v6 /
// Kiota 2.0 error-handling contract (catch ODataError, not ServiceException;
// ResponseStatusCode is int; ResponseHeaders is dict). L2 does NOT reference
// the Microsoft.Graph SDK package — every other Wave-C4 L2 collaborator that
// calls Graph or Dataverse (H3's future consent verifier, H5's
// DataverseWebApiHealthProbe, H2b's RestApiAiSearchIndexVerifier) uses raw
// HttpClient + DefaultAzureCredential against the REST surface directly, and
// design.md §9.2's tool-selection table explicitly recommends "`az rest`
// against Graph endpoints" / "Direct Graph SDK invocation via a script" as
// the L2-tooling options — not a first-class Microsoft.Graph SDK dependency.
// H10's two Graph collaborators (GraphRestAppRoleGranter,
// GraphRestAppRoleParityVerifier) instead catch non-success HTTP status codes
// + surface status code + response body in the failure diagnostic — the same
// protection NFR-09 asks for (distinguish HTTP-shaped errors from generic
// faults; carry the status code + payload for diagnosis), achieved without
// adding a new SDK dependency to a project none of its 8 prior handlers
// needed. Alternative considered + rejected: add Microsoft.Graph 6.5.0 to L2
// purely for this handler — rejected as inconsistent with the established L2
// pattern and unnecessary complexity for a REST surface this simple (2 GET +
// 1 POST shapes, already validated in scripts/Grant-GraphAppRoles.ps1 task 015).
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌────────────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                               │ §4C class                 │
//   ├────────────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId (§4D I1)                  │ Resumable                 │
//   │ Missing bffAppRegId/miClientId/miObjectId/  │ Resumable (upstream       │
//   │ dataverseEnvUrl (H3/H2a/H5-H6 not done yet) │ handler hasn't run yet)   │
//   │ Run not found in Cosmos partition          │ Resumable                 │
//   │ H10 escalation gate (null AppRoleId)       │ Resumable (no write yet)  │
//   │ BFF/UAMI App User creation call failed     │ Resumable (idempotent op) │
//   │ T2 post-condition mismatch (count != 1)    │ QuarantineRequired        │
//   │ Graph role grant call(s) failed            │ RetryableWithCleanup      │
//   │ T3 post-condition partial (still missing   │ QuarantineRequired        │
//   │ roles after "successful" grant loop)       │                           │
//   │ Concurrent Cosmos writer conflict          │ Resumable                 │
//   │ Run row deleted mid-flight                 │ Resumable                 │
//   └────────────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): future reconciler computes
//           deterministic MessageId per (HandlerId, RunId, CustomerId,
//           paramHash); SB duplicate-detection collapses re-enqueues.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (parity
//           with every other Wave-C4 handler).
//   Level 3 (handler body durable dedup): this handler scans
//           ProvisioningRun.CompletedPhases for (Phase == "H10",
//           IdempotencyKey == appuser-{customerId}). Match ⇒ Success no-op.
//           Key format is customer-only (no version token) per POML
//           constraint — App User registration is per-customer + version-
//           independent (parity with H5's dvenv-{customerId}).
//
// DOWNSTREAM ENQUEUE (WAVE C4 NOTE):
//   Parity with H3/H5/H6: the Wave C5 reconciler owns fan-out from H10 to its
//   successors (H12c/H14 per the plan.md critical path). This handler mutates
//   Cosmos state (advancing CurrentPhase + CompletedPhases + InterStepState +
//   GateStates) and returns Success; it does not enqueue anything directly.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H10DataverseAppUserGraphParityHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H10;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    private readonly IProvisioningRunRepository _repository;
    private readonly IDataverseAppUserCreator _appUserCreator;
    private readonly IDataverseAppUserVerifier _appUserVerifier;
    private readonly IGraphAppRoleGranter _roleGranter;
    private readonly IGraphAppRoleParityVerifier _roleParityVerifier;
    private readonly IGraphAppRolesRegistry _rolesRegistry;
    private readonly H10DataverseAppUserGraphParityOptions _options;
    private readonly ILogger<H10DataverseAppUserGraphParityHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H10 handler. All collaborators are interface-abstracted
    /// so unit tests can substitute fakes for each seam.
    /// </summary>
    public H10DataverseAppUserGraphParityHandler(
        IProvisioningRunRepository repository,
        IDataverseAppUserCreator appUserCreator,
        IDataverseAppUserVerifier appUserVerifier,
        IGraphAppRoleGranter roleGranter,
        IGraphAppRoleParityVerifier roleParityVerifier,
        IGraphAppRolesRegistry rolesRegistry,
        IOptions<H10DataverseAppUserGraphParityOptions> options,
        ILogger<H10DataverseAppUserGraphParityHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(appUserCreator);
        ArgumentNullException.ThrowIfNull(appUserVerifier);
        ArgumentNullException.ThrowIfNull(roleGranter);
        ArgumentNullException.ThrowIfNull(roleParityVerifier);
        ArgumentNullException.ThrowIfNull(rolesRegistry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _appUserCreator = appUserCreator;
        _appUserVerifier = appUserVerifier;
        _roleGranter = roleGranter;
        _roleParityVerifier = roleParityVerifier;
        _rolesRegistry = rolesRegistry;
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
            // silently mis-executing (parity with H0/H1/H2a/H3/H5).
            throw new InvalidOperationException(
                $"H10DataverseAppUserGraphParityHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H10 Dataverse App User + Graph parity starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H10 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: H10Rejections.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId);

        // (2) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H10 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (3) §4D I1 tenant guard — H10 MUST NOT fall back to a default tenant.
        var parameters = run.Parameters.NonSecret;
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H10 (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this " +
                "before H10 dispatches.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                H10Rejections.MissingTenantId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (4)-(7) Upstream InterStepState guards — H10 depends on H3
        // (bffAppRegId), H2a/uami.bicep (miClientId + miObjectId), H5/H6
        // (dataverseEnvUrl) all having completed. Missing any is "upstream
        // handler hasn't run yet", not an H10 defect — Resumable.
        var interStep = run.InterStepState;
        if (string.IsNullOrWhiteSpace(interStep.BffAppRegId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.MissingBffAppRegId,
                "InterStepState.bffAppRegId is not populated — H3 (Entra app-reg) must complete before H10.",
                cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(interStep.MiClientId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.MissingUamiClientId,
                "InterStepState.miClientId is not populated — the UAMI (H2a/uami.bicep) must complete before H10.",
                cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(interStep.MiObjectId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.MissingUamiObjectId,
                "InterStepState.miObjectId is not populated — the UAMI (H2a/uami.bicep) must complete before H10.",
                cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(interStep.DataverseEnvUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.MissingDataverseEnvUrl,
                "InterStepState.dataverseEnvUrl is not populated — H5/H6 (Dataverse env) must complete before H10.",
                cancellationToken).ConfigureAwait(false);
        }

        var bffAppRegId = interStep.BffAppRegId!;
        var uamiClientId = interStep.MiClientId!;
        var uamiObjectId = interStep.MiObjectId!;
        var dataverseEnvUrl = interStep.DataverseEnvUrl!;

        // (8) H10 ESCALATION GATE (spec.md MUST rule, BINDING) — fires BEFORE
        //     any Graph or Dataverse write. Any null/empty AppRoleId in the
        //     mirrored catalog means Phase A GUID completion (task 005) is
        //     incomplete for at least one role.
        var expectedRoles = _rolesRegistry.GetAll();
        var nullGuidRoles = expectedRoles.Where(r => string.IsNullOrWhiteSpace(r.AppRoleId)).Select(r => r.Value).ToList();
        if (nullGuidRoles.Count > 0)
        {
            var diagnostic =
                $"H10 escalation gate: {nullGuidRoles.Count} of {expectedRoles.Count} GraphAppRoles entries have a " +
                $"null AppRoleId ({string.Join(", ", nullGuidRoles)}). Phase A GUID completion (task 005) must " +
                "populate ALL 14 AppRoleId GUIDs via live 'az ad sp show' enumeration BEFORE H10 runs for any " +
                "production customer. Refusing to proceed — NO Graph or Dataverse write has been attempted.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                H10Rejections.EscalationGateNullAppRoleId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (9) Register the BFF app-reg as a Dataverse System Administrator App User.
        var bffOutcome = await _appUserCreator.EnsureAppUserAsync(
            new DataverseAppUserCreationRequest(dataverseEnvUrl, tenantId, bffAppRegId, _options.SecurityRoleName),
            cancellationToken).ConfigureAwait(false);
        if (bffOutcome is DataverseAppUserCreationOutcome.Failure bffFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.BffAppUserCreationFailed,
                $"BFF app-reg App User registration failed: {bffFailure.Diagnostic}", cancellationToken)
                .ConfigureAwait(false);
        }
        var bffSystemUserId = ((DataverseAppUserCreationOutcome.Success)bffOutcome).SystemUserId;

        // (10) Register the UAMI as a Dataverse System Administrator App User.
        var uamiOutcome = await _appUserCreator.EnsureAppUserAsync(
            new DataverseAppUserCreationRequest(dataverseEnvUrl, tenantId, uamiClientId, _options.SecurityRoleName),
            cancellationToken).ConfigureAwait(false);
        if (uamiOutcome is DataverseAppUserCreationOutcome.Failure uamiFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.UamiAppUserCreationFailed,
                $"UAMI App User registration failed: {uamiFailure.Diagnostic}", cancellationToken)
                .ConfigureAwait(false);
        }
        var uamiSystemUserIdFromCreate = ((DataverseAppUserCreationOutcome.Success)uamiOutcome).SystemUserId;

        // (11) T2 SILENT-FAIL TRAP — independent post-registration re-query.
        var t2Result = await _appUserVerifier.VerifyAsync(
            dataverseEnvUrl, tenantId, uamiClientId, cancellationToken).ConfigureAwait(false);
        if (t2Result is DataverseAppUserVerificationResult.CountMismatch mismatch)
        {
            var diagnostic =
                $"T2 verification FAILED (spec.md FR-33): systemusers?$filter=applicationid eq {uamiClientId} " +
                $"returned count={mismatch.ObservedCount} (expected 1). Registration reported success but the " +
                "independent post-condition query disagrees — every BFF->Dataverse call for this customer will " +
                "403 until resolved. Operator must inspect the target Dataverse environment directly.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                H10Rejections.TrapT2VerificationFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }
        var uamiSystemUserId = ((DataverseAppUserVerificationResult.Verified)t2Result).SystemUserId;

        // (12) Grant all 14 Graph app-roles onto the UAMI service principal.
        var grantOutcome = await _roleGranter.GrantRolesAsync(
            uamiObjectId, tenantId, expectedRoles, cancellationToken).ConfigureAwait(false);
        if (grantOutcome is GraphAppRoleGrantOutcome.Failure grantFailure)
        {
            var diagnostic =
                $"Graph app-role grant failed: {grantFailure.Diagnostic} Grants are individually idempotent " +
                "(re-POSTing an already-granted role is a safe no-op) — resume re-attempts only the still-missing " +
                "roles.";
            return await FailAsync(run, etag, FailureClass.RetryableWithCleanup,
                H10Rejections.GraphRoleGrantFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (13) T3 SILENT-FAIL TRAP — independent post-grant re-query.
        var t3Result = await _roleParityVerifier.VerifyAsync(
            uamiObjectId, tenantId, expectedRoles, cancellationToken).ConfigureAwait(false);
        if (t3Result is GraphAppRoleParityResult.Partial partial)
        {
            var diagnostic =
                $"T3 verification FAILED (spec.md FR-33): {partial.MissingRoleValues.Count} of " +
                $"{partial.ExpectedCount} role(s) still missing after the grant loop reported success: " +
                $"{string.Join(", ", partial.MissingRoleValues)}. Per the POML escalation trigger, a still-partial " +
                "result despite a 'successful' grant loop most often means a GraphAppRoles.cs GUID value is WRONG " +
                "(not just null) — operator must diagnose via live 'az ad sp show' re-enumeration of the " +
                "Microsoft Graph resource SP. Do NOT blindly retry.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                H10Rejections.TrapT3VerificationFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "H10 succeeded: runId={RunId} customerId={CustomerId} uamiSystemUserId={UamiSystemUserId} " +
            "bffSystemUserId={BffSystemUserId} rolesGranted={RolesGranted} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, uamiSystemUserId, bffSystemUserId, expectedRoles.Count,
            stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(
            run, etag, idempotencyKey, uamiSystemUserId, bffSystemUserId ?? uamiSystemUserIdFromCreate,
            envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H10 idempotency key: <c>appuser-{customerId}</c>.
    /// No version token per POML constraint — App User registration is
    /// per-customer + version-independent (parity with H5's
    /// <c>dvenv-{customerId}</c>). Exposed internal so unit tests can construct
    /// expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return $"appuser-{customerId}";
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

        run.GateStates[$"h10-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H10 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H10 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        string uamiSystemUserId,
        string bffSystemUserId,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        // (ADR-044) systemuserid values are canonical lowercase GUIDs as
        // returned by Dataverse Web API — no brace-wrapping to strip.
        run.InterStepState.SystemUserId = uamiSystemUserId;
        run.InterStepState.BffAppRegSystemUserId = bffSystemUserId;

        run.GateStates[H10Gates.AppUserCreated] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
        };
        run.GateStates[H10Gates.GraphRoleParity] = new GateEntry
        {
            Status = GateState.Verified,
            VerifiedAt = completedAt,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H10 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: H10Rejections.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H10 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H10 " +
                             "which will short-circuit on idempotency once the winning write lands.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H10 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: H10Rejections.RunDeletedDuringProvisioning,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H10 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
