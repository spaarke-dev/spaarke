// -----------------------------------------------------------------------------
// H10DataverseAppUserGraphParityHandler.cs
//
// L2 CONTROL-PLANE H10 Dataverse App User + Graph app-role parity handler
// (task 053, wave C4 Batch 3E). T2 + T3 silent-fail trap owner.
//
// PURPOSE:
//   Registers the BFF app-registration AND the UAMI as Dataverse System
//   Administrator Application Users on the target Dataverse environment, then
//   syncs the Microsoft Graph application (app-only) permission catalog
//   (Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles, mirrored locally via
//   IGraphAppRolesRegistry — see that file's header for the L2/BFF
//   assembly-isolation rationale) onto the UAMI service principal: the
//   Entra-granted roles (FileStorageContainer.Selected since task 261), and
//   REMOVES every other Microsoft Graph app role on it (task 261 / G31 — a stamp
//   in Spaarke's tenant must not hold tenant-wide directory, SharePoint or mail
//   rights; evidence: notes/t261-stamp-graph-least-privilege.md). The mailbox
//   roles are granted by H14a through Exchange, scoped to the customer's group
//   (task 251) — never here.
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
//   │ dataverseEnvUrl (H3/H2a/H5 not done yet)    │ handler hasn't run yet)   │
//   │ Run not found in Cosmos partition          │ Resumable                 │
//   │ H10 escalation gate (null AppRoleId)       │ Resumable (no write yet)  │
//   │ BFF/UAMI App User creation call failed     │ Resumable (idempotent op) │
//   │ T259 displayName missing/invalid           │ Resumable (no write yet)  │
//   │ T259 customer unit ambiguous / read fault  │ Resumable                 │
//   │ T259 customer unit not under the root      │ QuarantineRequired        │
//   │ T259 App User already in another unit      │ QuarantineRequired        │
//   │ T2 post-condition mismatch (count != 1)    │ QuarantineRequired        │
//   │ Graph role grant call(s) failed            │ RetryableWithCleanup      │
//   │ Removing an extra Graph role failed (T261) │ RetryableWithCleanup      │
//   │ T3 post-condition partial (still missing   │ QuarantineRequired        │
//   │ roles after "successful" grant loop)       │                           │
//   │ T3 extra role still present (T261)         │ QuarantineRequired        │
//   │ T3 extras re-read failed (T261)            │ Resumable                 │
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
//           IdempotencyKey == appuser-{customerId}-g{catalog fingerprint}). Match ⇒ Success no-op.
//           The fingerprint (task 261) is a hash of the Entra-granted role ids: a run that
//           completed H10 under an OLDER catalog (more roles) does not match, so a re-dispatch
//           of H10 re-runs the reconcile and removes what that catalog granted. Every step is
//           idempotent. (A run already past H10 is not re-dispatched by the reconciler: for a
//           stamp provisioned before task 261 use scripts/provisioning/Remove-StampGraphExtraRoles.ps1.)
//           Key format is customer-only (no version token) per POML
//           constraint — App User registration is per-customer + version-
//           independent (parity with H5's dvenv-{customerId}).
//
// DOWNSTREAM ENQUEUE (WAVE C4 NOTE):
//   Parity with H3/H5/H6: the Wave C5 reconciler owns fan-out from H10 to its
//   successors (H12c/H14 per the plan.md critical path). This handler mutates
//   Cosmos state (advancing CurrentPhase + CompletedPhases + InterStepState +
//   GateStates) and returns Success; it does not enqueue anything directly.
//
// CUSTOMER BUSINESS UNIT (T259 — ISS-010 / #1486, owner decision 2026-10-09; INCOMING-145 §6 T1/T3):
//   "For secure records, only users explicitly granted access should have access; no users are added to the secure
//   business unit." Before registering anyone H10 finds or creates the customer's OWN business unit — named by intake
//   displayName (CustomerBusinessUnitIntake), a DIRECT child of the Dataverse root and so a SIBLING of the Secure Record
//   unit — and creates both App Users IN it with System Administrator (the unit's copy of the role). Deep depth in the
//   customer unit never reaches its sibling. An App User found in another unit is QuarantineRequired, never moved (a
//   business-unit change strips every role, and H6/H7/H7b sign in as the BFF App User). The unit id is written to
//   InterStepState.CustomerBusinessUnitId for H7b (checks it) and H11 (puts guests in it).
//
// MODEL 1 / MODEL 2 CODE PATH (task 205d / punch row A41 — auth-v4 §10.1 Δ5):
//   H10 itself is DELIBERATELY tenancy-model-agnostic — it always registers
//   exactly the two systemuser rows named by whatever bffAppRegId/miClientId/
//   miObjectId InterStepState carries, without branching on run.TenancyModel.
//   The Model 1 vs Model 2 SHAPE is established upstream, not here:
//     - Model 1 (dedicated stamp in Spaarke's Azure tenant — D-12/D-13, 2026-09-28):
//       the same shape as Model 2 below — a per-customer BFF app-reg (H3) and a
//       per-stamp UAMI (H2a), so H10 writes this customer's own systemuser rows.
//       (H2a deploys Model 1 stamps since task 228.)
//       The former shared shape (one multitenant app-reg + `sprk-{env}-shared-bff-uami`
//       registered once per DV environment for every Model 1 customer) is retired:
//       H3's shared branch by task 222, the shared stack by task 225a.
//     - Model 2 (dedicated stamp): H3 provisions a per-customer BFF app-reg +
//       federated identity credential per customer, and H2a's uami.bicep
//       provisions a per-stamp UAMI (`mi-spaarke-{customerId}-{env}`, customer.bicep) — see
//       GraphRegistrationProvisioner.cs:547-557 (task 130) for the per-profile
//       issuer derivation that makes the Model 2 app-reg's identity genuinely
//       per-customer. H10's dispatch for a Model 2 customer therefore writes
//       systemuser rows unique to that customer's stamp.
//   No `if (run.TenancyModel == ...)` branch belongs in this handler — adding
//   one would duplicate a decision that upstream handlers already made and
//   InterStepState already encodes.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
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
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, _rolesRegistry.GetEntraGranted());

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
                "InterStepState.dataverseEnvUrl is not populated — H5 (Dataverse env adoption) must complete before H10.",
                cancellationToken).ConfigureAwait(false);
        }

        // T259: the customer unit's name — the intake rule POST /api/runs applied, re-checked (defence in depth).
        var displayName = CustomerBusinessUnitIntake.Validate(parameters, SecureRecordOwnerRoleSet.Embedded.BusinessUnitName);
        if (displayName is CustomerBusinessUnitIntakeOutcome.Invalid invalidName)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, invalidName.RejectionCode,
                $"Run parameter {invalidName.Diagnostic} Nothing was written.", cancellationToken).ConfigureAwait(false);
        }
        var customerUnitName = ((CustomerBusinessUnitIntakeOutcome.Valid)displayName).Name;

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
                "populate ALL AppRoleId GUIDs via live 'az ad sp show' enumeration BEFORE H10 runs for any " +
                "production customer. Refusing to proceed — NO Graph or Dataverse write has been attempted.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                H10Rejections.EscalationGateNullAppRoleId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (8b) T259 — the customer's own business unit, directly under the root (INCOMING-145 §6 T1).
        var unitOutcome = await _appUserCreator.EnsureCustomerBusinessUnitAsync(
            dataverseEnvUrl, tenantId, customerUnitName, cancellationToken).ConfigureAwait(false);
        Guid customerUnitId;
        switch (unitOutcome)
        {
            case CustomerBusinessUnitOutcome.Success found:
                customerUnitId = found.BusinessUnitId;
                break;
            case CustomerBusinessUnitOutcome.Ambiguous ambiguous:
                return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.CustomerBusinessUnitAmbiguous,
                    $"{ambiguous.Count} business units are named '{customerUnitName}' — H10 does not guess which one is the " +
                    "customer's. Rename or remove the extra one, then resume. Nothing was written.",
                    cancellationToken).ConfigureAwait(false);
            case CustomerBusinessUnitOutcome.WrongParent wrong:
                return await FailAsync(run, etag, FailureClass.QuarantineRequired, H10Rejections.CustomerBusinessUnitWrongParent,
                    $"Business unit '{customerUnitName}' ({wrong.BusinessUnitId}) has parent " +
                    $"{wrong.ParentId?.ToString() ?? "(none — it is the root)"}, not the root unit {wrong.RootBusinessUnitId}. " +
                    "The customer's unit must be a DIRECT child of the root, a sibling of the Secure Record unit (INCOMING-145 " +
                    "§6 T1); re-parenting a unit is an owner decision. Nothing was written.",
                    cancellationToken).ConfigureAwait(false);
            default:
                return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.CustomerBusinessUnitFailed,
                    $"The customer business unit '{customerUnitName}' could not be read or created: " +
                    $"{((CustomerBusinessUnitOutcome.Failure)unitOutcome).Diagnostic}",
                    cancellationToken).ConfigureAwait(false);
        }

        // (9) Register the BFF app-reg as a Dataverse System Administrator App User — IN the customer unit (T3).
        var bffOutcome = await _appUserCreator.EnsureAppUserAsync(
            new DataverseAppUserCreationRequest(dataverseEnvUrl, tenantId, bffAppRegId, _options.SecurityRoleName, customerUnitId),
            cancellationToken).ConfigureAwait(false);
        if (bffOutcome is DataverseAppUserCreationOutcome.InForeignBusinessUnit bffElsewhere)
        {
            return await ForeignUnitAsync(run, etag, "BFF app-reg", bffAppRegId, bffElsewhere, customerUnitId, cancellationToken)
                .ConfigureAwait(false);
        }
        if (bffOutcome is DataverseAppUserCreationOutcome.Failure bffFailure)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.BffAppUserCreationFailed,
                $"BFF app-reg App User registration failed: {bffFailure.Diagnostic}", cancellationToken)
                .ConfigureAwait(false);
        }
        var bffSystemUserId = ((DataverseAppUserCreationOutcome.Success)bffOutcome).SystemUserId;

        // (10) Register the UAMI as a Dataverse System Administrator App User.
        // auth-v4 §10.4 BINDING (punch row A41, the documented "single
        // most-missed item"): azureactivedirectoryobjectid MUST be set
        // EXPLICITLY to the UAMI's principalId (uamiObjectId — InterStepState
        // .miObjectId, H2a's Bicep output) — NEVER the UAMI's clientId
        // (uamiClientId, used for `applicationid` only). Both are valid-shaped
        // GUIDs; a row created with the wrong one still passes the T2
        // existence/count check below AND H13's independent re-verification
        // count check — only the app-only Dataverse call's oid-claim match
        // fails at first real use, 401ing every call for this customer. See
        // DataverseAppUserCreationRequest.AzureActiveDirectoryObjectId's
        // remarks + mi-proof-dataverse-side.md for the full trap shape.
        var uamiOutcome = await _appUserCreator.EnsureAppUserAsync(
            new DataverseAppUserCreationRequest(
                dataverseEnvUrl, tenantId, uamiClientId, _options.SecurityRoleName, customerUnitId,
                AzureActiveDirectoryObjectId: uamiObjectId),
            cancellationToken).ConfigureAwait(false);
        if (uamiOutcome is DataverseAppUserCreationOutcome.InForeignBusinessUnit uamiElsewhere)
        {
            return await ForeignUnitAsync(run, etag, "UAMI", uamiClientId, uamiElsewhere, customerUnitId, cancellationToken)
                .ConfigureAwait(false);
        }
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

        // (12) Grant the Entra-granted Graph app-roles onto the UAMI service principal. The mailbox
        //      roles (Mail.*, MailboxSettings.Read) are NOT granted here: H14a grants them through
        //      Exchange, scoped to the customer's group (task 251, owner D26) — an Entra grant would
        //      reach every mailbox in the tenant and void that scope.
        var entraRoles = _rolesRegistry.GetEntraGranted();
        var grantOutcome = await _roleGranter.GrantRolesAsync(
            uamiObjectId, tenantId, entraRoles, cancellationToken).ConfigureAwait(false);
        if (grantOutcome is GraphAppRoleGrantOutcome.Failure grantFailure)
        {
            var diagnostic =
                $"Graph app-role grant failed: {grantFailure.Diagnostic} Grants are individually idempotent " +
                "(re-POSTing an already-granted role is a safe no-op) — resume re-attempts only the still-missing " +
                "roles.";
            return await FailAsync(run, etag, FailureClass.RetryableWithCleanup,
                H10Rejections.GraphRoleGrantFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (12b) Task 261 (G31): REMOVE every Microsoft Graph app role on the stamp identity that is not in the
        //       Entra-granted set — roles an earlier catalog granted (Directory.ReadWrite.All, User.ReadWrite.All,
        //       Files.*, Sites.*, the mailbox roles before T251, …) or someone granted by hand. Every stamp lives in
        //       Spaarke's tenant, so a leftover tenant-wide role reaches Spaarke's own directory. Each removal is
        //       logged by the granter; assignments on other resources are untouched.
        var removal = await _roleGranter.RemoveUnexpectedRolesAsync(
            uamiObjectId, uamiClientId, tenantId, entraRoles, cancellationToken).ConfigureAwait(false);
        if (removal is GraphAppRoleRemovalOutcome.Failure removalFailure)
        {
            var diagnostic =
                $"Removing Graph app roles outside the stamp set failed: {removalFailure.Diagnostic}" +
                (removalFailure.FailedRoleValues.Count > 0
                    ? $" Still assigned: {string.Join(", ", removalFailure.FailedRoleValues)}."
                    : string.Empty) +
                " Each removal is idempotent (404 = already gone) — resume re-reads the assignments and removes only what " +
                "is still extra.";
            return await FailAsync(run, etag, FailureClass.RetryableWithCleanup,
                H10Rejections.GraphRoleRemovalFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }
        var removedRoles = ((GraphAppRoleRemovalOutcome.Success)removal).RemovedRoleValues;
        if (removedRoles.Count > 0)
        {
            _logger.LogWarning(
                "H10 removed {RemovedCount} Graph app role(s) outside the stamp set from stamp identity SP {UamiSpId}: {RemovedRoles} " +
                "(runId={RunId} customerId={CustomerId})",
                removedRoles.Count, uamiObjectId, string.Join(", ", removedRoles), envelope.RunId, envelope.CustomerId);
        }

        // (13) T3 SILENT-FAIL TRAP — independent post-grant re-query.
        var t3Result = await _roleParityVerifier.VerifyAsync(
            uamiObjectId, tenantId, entraRoles, cancellationToken).ConfigureAwait(false);
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

        // (13b) T3, task 261 — independent re-read: nothing outside the Entra-granted set remains. Microsoft Graph is
        //       eventually consistent, so a role this very call removed can still be listed for a few seconds: re-read
        //       with a growing delay before deciding. The final decision is exhaustive — only None passes.
        var attempts = Math.Max(1, _options.ExtrasRecheckAttempts);
        GraphAppRoleExtrasResult extras = new GraphAppRoleExtrasResult.Unknown("The extras re-read did not run.");
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            extras = await _roleParityVerifier.FindUnexpectedRolesAsync(
                uamiObjectId, tenantId, entraRoles, cancellationToken).ConfigureAwait(false);
            if (extras is GraphAppRoleExtrasResult.None || attempt == attempts)
            {
                break;
            }
            if (_options.ExtrasRecheckDelay > TimeSpan.Zero)
            {
                await Task.Delay(_options.ExtrasRecheckDelay * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        switch (extras)
        {
            case GraphAppRoleExtrasResult.None:
                break;
            case GraphAppRoleExtrasResult.Found found when removedRoles.Count > 0:
                // This call removed roles and the directory still lists extras after the bounded re-read: most likely
                // replication lag, not a re-granter. Resumable — a retry re-reads and removes what is still there.
                return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.GraphRoleExtrasUnverified,
                    $"After removing {string.Join(", ", removedRoles)}, the directory still lists Graph app role(s) outside the " +
                    $"stamp set after {attempts} read(s): {string.Join(", ", found.RoleValues)}. Likely replication lag — resume " +
                    "re-reads and removes what remains.", cancellationToken).ConfigureAwait(false);
            case GraphAppRoleExtrasResult.Found found:
                // Nothing was removed by this call yet extras are listed: the removal pass read an older view, or
                // something grants them concurrently. Quarantine — find the granter before resuming.
                return await FailAsync(run, etag, FailureClass.QuarantineRequired, H10Rejections.TrapT3UnexpectedRoles,
                    $"T3 verification FAILED: the stamp identity (SP {uamiObjectId}) holds Graph app role(s) outside the stamp " +
                    $"set that the removal pass did not see: {string.Join(", ", found.RoleValues)}. Something grants them " +
                    "concurrently (an operator script, another pipeline) — find the granter before resuming; the stamp set is " +
                    "projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md.",
                    cancellationToken).ConfigureAwait(false);
            case GraphAppRoleExtrasResult.Unknown unknown:
                return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.GraphRoleExtrasUnverified,
                    $"Could not confirm that no Graph app role outside the stamp set remains: {unknown.Diagnostic}",
                    cancellationToken).ConfigureAwait(false);
            default:
                return await FailAsync(run, etag, FailureClass.Resumable, H10Rejections.GraphRoleExtrasUnverified,
                    "The extras re-read returned no recognisable result.", cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "H10 succeeded: runId={RunId} customerId={CustomerId} uamiSystemUserId={UamiSystemUserId} " +
            "bffSystemUserId={BffSystemUserId} rolesGranted={RolesGranted} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, uamiSystemUserId, bffSystemUserId, entraRoles.Count,
            stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(
            run, etag, idempotencyKey, uamiSystemUserId, bffSystemUserId ?? uamiSystemUserIdFromCreate, customerUnitId,
            envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>T259: an App User already in another business unit — QuarantineRequired, nothing moved.</summary>
    private Task<HandlerResult> ForeignUnitAsync(
        ProvisioningRun run, string etag, string label, string applicationId,
        DataverseAppUserCreationOutcome.InForeignBusinessUnit elsewhere, Guid customerUnitId, CancellationToken cancellationToken)
        => FailAsync(run, etag, FailureClass.QuarantineRequired, H10Rejections.AppUserInForeignBusinessUnit,
            $"The {label} App User (applicationid {applicationId}, systemuser {elsewhere.SystemUserId}) already exists in business " +
            $"unit {elsewhere.BusinessUnitId}, not the customer's unit {customerUnitId}. H10 never moves it: a business-unit " +
            "change strips every role, and H6/H7/H7b sign in as the BFF App User. Moving it (and re-granting System " +
            "Administrator in the customer unit) is an owner decision; then resume.",
            cancellationToken);

    /// <summary>
    /// Computes the deterministic H10 idempotency key: <c>appuser-{customerId}-g{fingerprint}</c>, where the fingerprint
    /// is the first 8 hex digits of SHA-256 over the sorted, lower-cased Entra-granted app-role ids (task 261). App User
    /// registration is per-customer and otherwise version-independent (parity with H5's <c>dvenv-{customerId}</c>); the
    /// Graph role set is the one part that changes, and a change must re-run the reconcile. Exposed internal so unit
    /// tests can construct expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, IReadOnlyList<GraphAppRoleEntry> entraRoles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(entraRoles);
        var ids = string.Join("|", entraRoles.Select(r => (r.AppRoleId ?? string.Empty).ToLowerInvariant()).OrderBy(i => i, StringComparer.Ordinal));
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ids));
        return $"appuser-{customerId}-g{Convert.ToHexString(hash, 0, 4).ToLowerInvariant()}";
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
        Guid customerUnitId,
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
        run.InterStepState.CustomerBusinessUnitId = customerUnitId.ToString("D");   // T259 — read by H7b and H11

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
