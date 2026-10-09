// -----------------------------------------------------------------------------
// H11UserProvisioningHandler.cs
//
// L2 CONTROL-PLANE H11 user-provisioning handler (task 054, wave C4 Batch 3F).
//
// PURPOSE:
//   Provisions the customer's initial user set per the D6 identity preset
//   (B2BGuest | NativeAccount) — the L2 port of the r1 registration flow's
//   DemoProvisioningService (9-step) + GraphUserService (user creation + UPN
//   + license assignment) pattern. L2 cannot reference the BFF assembly
//   (ADR-010 / project MUST rule) so this handler + its three collaborator
//   seams are an independent L2-owned re-implementation using raw
//   HttpClient + DefaultAzureCredential against the Graph REST surface
//   (parity with H10's NFR-09 Path-C rationale — see
//   H10DataverseAppUserGraphParityHandler.cs file header).
//
// SPEC / DESIGN references:
//   - spec.md FR-14 (H11): user provisioning per identity preset (D6:
//     B2BGuest or NativeAccount) via r1 registration flow; B2B needs
//     consent-verification gate; users created with correct UPN pattern;
//     license assignment succeeds (per r1 FR-11).
//   - spec.md §4D I1: tenantId flows explicitly; no default-tenant fallback.
//   - spec.md §4D I5: Graph token acquisition is per-tenant scoped.
//   - spec.md NFR-09: Graph SDK calls catch ODataError — this handler's raw-
//     HTTP collaborators surface the functional equivalent (status code +
//     body) per H10's established Path-C precedent (no new SDK dependency).
//   - design.md §4.1 H11 row: r1 registration flow (D6); B2B consent gate;
//     idempotency key users-{customerId}.
//   - design.md D6: two identity presets — B2BGuest (cross-tenant access) or
//     NativeAccount (low-IT-friction); branch at user-creation only; gates
//     differ (B2B needs consent verification).
//   - .claude/adr/ADR-004: single IJobHandler-shape impl registered in L2 DI.
//   - .claude/adr/ADR-010: register in L2, NOT BFF.
//   - .claude/adr/ADR-028: 21 MUSTs — Graph token acquisition per identity
//     preset semantics; DefaultAzureCredential, never account-key.
//   - .claude/adr/ADR-036: reuse background-job infrastructure; fire-and-forget.
//   - .claude/adr/ADR-044: userIds written to Cosmos follow GUID canonicalization.
//
// BRANCH SEMANTICS (resolved from the POML's <goal> prose vs its <steps>/
// <acceptance-criteria> — the latter are the more specific, testable
// contract; root CLAUDE.md §8.5 directional-mode guidance is to adapt when a
// step reading is ambiguous and record the resolution):
//   NativeAccount → GraphUserService-pattern CreateUser + AssignLicense per
//                   user (AC-1 / step order="3"). No B2B invitation, no
//                   consent gate.
//   B2BGuest      → B2B POST /invitations per user + a SEPARATE consent-
//                   verification query (AC-2/AC-3 / step order="4"/"5"). No
//                   CreateUser/AssignLicense call — B2B guests are
//                   provisioned by invitation in the real Graph B2B model,
//                   not by a direct POST /users. The <goal> prose's
//                   "additionally" is read as "in addition to NativeAccount
//                   existing as the alternative branch", not "on top of the
//                   NativeAccount steps within the same run" — the <steps>
//                   scope each collaborator call to its own branch
//                   explicitly (order="3" says "NativeAccount branch", "4"
//                   says "B2BGuest branch"), and every acceptance criterion
//                   that mentions license assignment (AC-1, AC-4) is scoped
//                   to identityPreset == "NativeAccount" only. Recorded in
//                   projects/customer-provisioning-orchestration-r1/notes/task-054-deviations.md.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌────────────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                               │ §4C class                 │
//   ├────────────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId (§4D I1)                  │ Resumable                 │
//   │ Missing/invalid identityPreset              │ Resumable                 │
//   │ Missing/malformed/empty usersJson, or an    │ Resumable                 │
//   │ unusable entry (UserProvisioningIntake)     │ (POST /api/runs rejects   │
//   │                                             │ these at intake, T245c)   │
//   │ Run not found in Cosmos partition          │ Resumable                 │
//   │ NativeAccount: user creation failed        │ Resumable (POST /users is │
//   │                                             │ idempotent via UPN check) │
//   │ NativeAccount: license assignment failed   │ RetryableWithCleanup      │
//   │                                             │ (user exists; assign-    │
//   │                                             │ License itself idempotent│
//   │ B2BGuest: invitation failed                │ Resumable (T232: an       │
//   │                                             │ existing guest is reused, │
//   │                                             │ never re-invited — a      │
//   │                                             │ re-POST re-sends the mail)│
//   │ B2B consent Pending (WaitingOnGate         │ (NOT a failure — Success  │
//   │ transition)                                 │ with WaitingOnGate state) │
//   │ T232: NativeAccount with no licence SKU    │ Resumable (before any     │
//   │                                             │ user is created)          │
//   │ T232: security group not this customer's / │ Resumable (before any     │
//   │ unreadable / not a security group           │ invitation)               │
//   │ T259: CustomerBusinessUnitId absent        │ Resumable (before any     │
//   │                                             │ invitation)               │
//   │ T259: guest already in a foreign unit      │ QuarantineRequired        │
//   │ T232: group membership / Dataverse user /  │ Resumable (each step      │
//   │ role write failed; role not in environment │ idempotent on re-run)     │
//   │ Concurrent Cosmos writer conflict          │ Resumable                 │
//   │ Run row deleted mid-flight                 │ Resumable                 │
//   └────────────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): future reconciler.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED (parity with
//           every other Wave-C4 handler).
//   Level 3 (handler body durable dedup): scans ProvisioningRun.CompletedPhases
//           for (Phase=="H11", IdempotencyKey=="users-{customerId}"). Match ⇒
//           Success no-op. Per POML constraint the key is customerId-ONLY
//           (no version/content-hash suffix) — user provisioning is a per-
//           customer steady state; per-user idempotency is delegated to the
//           Graph UPN alt-key check inside GraphRestUserProvisioner.CreateUserAsync.
//
// TASK 232 (D2, G10 — Model 1 guests usable; owner 2026-10-07: pay-as-you-go):
//   A Model 1 run takes only B2BGuest (UserProvisioningIntake). Guests get NO
//   licence — Spaarke pays for their access pay-as-you-go on the stamp
//   subscription (operator prerequisite PRQ-C-11). Before inviting anyone H11
//   reads the environment's security group (intake environmentSecurityGroupId,
//   PRQ-C-10) and refuses one not named sprk-{customerId}-users or not a security
//   group — the group is what keeps another customer's guests out of this
//   environment. Once every guest has redeemed, each guest is added to that
//   group and made a Dataverse user of H5's environment holding
//   GuestSecurityRoleNames (IDataverseGuestUserWriter; the roles are resolved
//   before anyone is invited). An existing guest is reused without a second
//   invitation email (GraphRestB2BInvitationClient). H11 checks the group's NAME;
//   that it is the group SET ON the environment is an operator check (skill Step
//   1e-bis, PRQ-C-10 — the binding is visible only to a Power Platform admin).
//
// TASK 259 (ISS-010 / #1486, owner decision 2026-10-09 — INCOMING-145 §6 T5):
//   "Guest users are added to the root customer business unit, NOT the secure business unit." Each guest becomes a
//   Dataverse user IN the customer's own business unit (H10's InterStepState.CustomerBusinessUnitId — a direct child of
//   the root, sibling of the Secure Record unit), holding the unit's copies of GuestSecurityRoleNames. Never the root:
//   Spaarke Basic User holds Deep read on project/matter/work assignment, and Deep at the root reaches the Secure Record
//   unit. A guest found in any other unit is QuarantineRequired (userprov-guest-in-foreign-business-unit), never moved.
//
// DOWNSTREAM ENQUEUE (Wave C4 note):
//   H11 does not enqueue a specific successor. Parity with H3/H5/H6/H10: the
//   Wave C5 reconciler owns fan-out from H11 to H12a/b/c per the plan.md
//   critical path. This handler mutates Cosmos state (advancing CurrentPhase
//   + CompletedPhases + InterStepState.ProvisionedUsers + GateStates) and
//   returns Success (or WaitingOnGate + Success on B2B consent-pending).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H11UserProvisioningHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H11;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the D6 identity preset (<c>B2BGuest</c> | <c>NativeAccount</c>).</summary>
    public const string IdentityPresetParameterKey = "identityPreset";

    /// <summary>Non-secret parameter key carrying the JSON-array-encoded user list (see UserProvisioningEntry.cs header for the encoding rationale).</summary>
    public const string UsersJsonParameterKey = "usersJson";

    /// <summary>Task 232: non-secret parameter key carrying the environment security group's object id (B2BGuest).</summary>
    public const string EnvironmentSecurityGroupIdParameterKey = "environmentSecurityGroupId";

    /// <summary>D6 identity preset value — cross-tenant B2B guest access.</summary>
    public const string IdentityPresetB2BGuest = UserProvisioningIntake.B2BGuest;

    /// <summary>D6 identity preset value — low-IT-friction native tenant account.</summary>
    public const string IdentityPresetNativeAccount = UserProvisioningIntake.NativeAccount;

    private readonly IProvisioningRunRepository _repository;
    private readonly IGraphUserProvisioner _userProvisioner;
    private readonly IB2BInvitationClient _b2bInvitationClient;
    private readonly IB2BConsentVerifier _consentVerifier;
    private readonly IEnvironmentSecurityGroupClient _securityGroupClient;
    private readonly IDataverseGuestUserWriter _guestUserWriter;
    private readonly H11UserProvisioningOptions _options;
    private readonly ILogger<H11UserProvisioningHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H11 handler. All collaborators are interface-abstracted
    /// so unit tests can substitute fakes for each seam.
    /// </summary>
    public H11UserProvisioningHandler(
        IProvisioningRunRepository repository,
        IGraphUserProvisioner userProvisioner,
        IB2BInvitationClient b2bInvitationClient,
        IB2BConsentVerifier consentVerifier,
        IEnvironmentSecurityGroupClient securityGroupClient,
        IDataverseGuestUserWriter guestUserWriter,
        IOptions<H11UserProvisioningOptions> options,
        ILogger<H11UserProvisioningHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(userProvisioner);
        ArgumentNullException.ThrowIfNull(b2bInvitationClient);
        ArgumentNullException.ThrowIfNull(consentVerifier);
        ArgumentNullException.ThrowIfNull(securityGroupClient);
        ArgumentNullException.ThrowIfNull(guestUserWriter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _userProvisioner = userProvisioner;
        _b2bInvitationClient = b2bInvitationClient;
        _consentVerifier = consentVerifier;
        _securityGroupClient = securityGroupClient;
        _guestUserWriter = guestUserWriter;
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
            // silently mis-executing (parity with H3/H10).
            throw new InvalidOperationException(
                $"H11UserProvisioningHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H11 user provisioning starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H11 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: H11Rejections.RunNotFound,
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
                "H11 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        var parameters = run.Parameters.NonSecret;

        // (3) §4D I1 tenant guard — H11 MUST NOT fall back to a default tenant.
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.MissingTenantId,
                "Run parameter 'tenantId' is required by H11 (§4D I1 no-hardcoded-tenant). Upstream handler " +
                "(H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this before H11 dispatches.",
                cancellationToken).ConfigureAwait(false);
        }

        // (4) identityPreset + usersJson (design.md D6) — the rules POST /api/runs already applied at intake
        //     (task 245c), checked again here for the WHOLE list before the first Graph call.
        parameters.TryGetValue(IdentityPresetParameterKey, out var identityPreset);
        parameters.TryGetValue(UsersJsonParameterKey, out var usersJson);
        parameters.TryGetValue(EnvironmentSecurityGroupIdParameterKey, out var securityGroupId);
        var intake = UserProvisioningIntake.Validate(run.TenancyModel, identityPreset, usersJson, securityGroupId);
        if (intake is UserProvisioningIntakeOutcome.Invalid invalid)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, invalid.RejectionCode,
                $"Run parameter {invalid.Diagnostic}", cancellationToken).ConfigureAwait(false);
        }
        var valid = (UserProvisioningIntakeOutcome.Valid)intake;

        return valid.IsNativeAccount
            ? await HandleNativeAccountAsync(run, etag, envelope, idempotencyKey, tenantId, valid.Users, stopwatch, cancellationToken)
                .ConfigureAwait(false)
            : await HandleB2BGuestAsync(run, etag, envelope, idempotencyKey, tenantId, valid.Users,
                    valid.EnvironmentSecurityGroupId!, stopwatch, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// NativeAccount branch (design.md D6): GraphUserService-pattern
    /// CreateUser + AssignLicense per user. Fail-fast on the first user
    /// failure (parity with H10's BFF/UAMI creator sequencing).
    /// </summary>
    private async Task<HandlerResult> HandleNativeAccountAsync(
        ProvisioningRun run,
        string etag,
        HandlerEnvelope envelope,
        string idempotencyKey,
        string tenantId,
        IReadOnlyList<UserProvisioningEntry> users,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        // Task 232 (R7): without a licence SKU every user would be created unlicensed and the run would report success.
        if (_options.LicenseSkuIds.Count == 0)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.LicenseSkuNotConfigured,
                "No licence SKU is configured for NativeAccount users (H11UserProvisioningOptions: " +
                "PowerAppsPlan2TrialSkuId / FabricFreeSkuId / PowerAutomateFreeSkuId) — nothing was created.",
                cancellationToken).ConfigureAwait(false);
        }

        var provisioned = new List<ProvisionedUserRecord>();
        // D15 (task 245c): diagnostics name a user by position in usersJson / Entra object id, never by name or UPN.
        for (var i = 0; i < users.Count; i++)
        {
            var entry = users[i];
            var position = i + 1;
            UserCreationOutcome creationOutcome;
            try
            {
                creationOutcome = await _userProvisioner.CreateUserAsync(entry, tenantId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsFailureNotCallerCancellation(ex, cancellationToken))
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.UserCreationFailed,
                    $"User creation infrastructure error for usersJson entry {position}: " +
                    $"{ex.GetType().Name}: {ex.Message}",
                    cancellationToken).ConfigureAwait(false);
            }

            if (creationOutcome is UserCreationOutcome.Failure creationFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.UserCreationFailed,
                    $"User creation failed for usersJson entry {position}: {creationFailure.Diagnostic}",
                    cancellationToken).ConfigureAwait(false);
            }

            var created = (UserCreationOutcome.Success)creationOutcome;

            LicenseAssignmentOutcome licenseOutcome;
            try
            {
                licenseOutcome = await _userProvisioner.AssignLicenseAsync(created.UserId, tenantId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsFailureNotCallerCancellation(ex, cancellationToken))
            {
                return await FailAsync(run, etag, FailureClass.RetryableWithCleanup, H11Rejections.LicenseAssignmentFailed,
                    $"License assignment infrastructure error for usersJson entry {position} (Entra user " +
                    $"{created.UserId}): {ex.GetType().Name}: {ex.Message}. User account already " +
                    "exists — retry re-attempts only the license assignment.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (licenseOutcome is LicenseAssignmentOutcome.Failure licenseFailure)
            {
                return await FailAsync(run, etag, FailureClass.RetryableWithCleanup, H11Rejections.LicenseAssignmentFailed,
                    $"License assignment failed for usersJson entry {position} (Entra user {created.UserId}): " +
                    $"{licenseFailure.Diagnostic}. User account already exists — retry re-attempts only the " +
                    "license assignment.",
                    cancellationToken).ConfigureAwait(false);
            }

            provisioned.Add(new ProvisionedUserRecord(created.UserId, created.Upn, IdentityPresetNativeAccount));
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "H11 NativeAccount provisioning succeeded: runId={RunId} customerId={CustomerId} userCount={UserCount} " +
            "durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, provisioned.Count, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(
            run, etag, idempotencyKey, provisioned, envelope, setB2BConsentGate: false, b2bConsentEvidence: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// B2BGuest branch (design.md D6; task 232): check the environment security group, make each user a guest
    /// (invitation or reuse), wait for redemption (WaitingOnGate — NOT a failure, parity with H3's admin-consent gate),
    /// then add each guest to the group and make it a Dataverse user with the configured role(s).
    /// </summary>
    private async Task<HandlerResult> HandleB2BGuestAsync(
        ProvisioningRun run,
        string etag,
        HandlerEnvelope envelope,
        string idempotencyKey,
        string tenantId,
        IReadOnlyList<UserProvisioningEntry> users,
        string securityGroupId,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        // H5 adopted the environment (H5 → H10 → H11); its absence is a state defect, refused before any write.
        var dataverseEnvUrl = run.InterStepState.DataverseEnvUrl;
        if (string.IsNullOrWhiteSpace(dataverseEnvUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.MissingDataverseEnvUrl,
                "InterStepState.DataverseEnvUrl (H5) is not set — H11 makes each guest a user of that environment. " +
                "Resume the run from H5.",
                cancellationToken).ConfigureAwait(false);
        }

        // T259: the customer's business unit (H10 → H11) — every guest goes there; its absence is a state defect.
        if (!Guid.TryParse(run.InterStepState.CustomerBusinessUnitId, out var customerUnitId) || customerUnitId == Guid.Empty)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.MissingCustomerBusinessUnit,
                "InterStepState.CustomerBusinessUnitId (H10) is not a GUID — H11 places every guest in the customer's business " +
                "unit, never the root. Resume the run from H10. Nothing was written.",
                cancellationToken).ConfigureAwait(false);
        }

        // The group keeps other customers' guests out of this environment: refuse one that is not this customer's
        // before anyone is invited (nothing written).
        if (await CheckSecurityGroupAsync(securityGroupId, tenantId, envelope.CustomerId, cancellationToken)
                .ConfigureAwait(false) is { } groupRejection)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.SecurityGroupRejected,
                groupRejection, cancellationToken).ConfigureAwait(false);
        }

        // Guests are blocked from Dataverse while restrictguestuseraccess is on (the default for a new environment).
        var guestAccess = await _guestUserWriter.ReadGuestAccessAsync(dataverseEnvUrl, tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (guestAccess is not GuestAccessOutcome.Allowed)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.GuestAccessRestricted,
                guestAccess is GuestAccessOutcome.Failure readFailure
                    ? $"The environment's guest-access setting could not be read: {readFailure.Diagnostic}. Nothing was written."
                    : "The environment restricts guest access (organization.restrictguestuseraccess = true, the default) — " +
                      "guests could not use it. Turn it off (prerequisite PRQ-C-12), then resume. Nothing was written.",
                cancellationToken).ConfigureAwait(false);
        }

        // Every role resolved once, before anyone is invited: a missing role (the package is T218's) must not send
        // invitations or add anyone to the group first.
        var roleNames = _options.EffectiveGuestSecurityRoleNames;
        var roles = await _guestUserWriter.ResolveRolesAsync(dataverseEnvUrl, tenantId, customerUnitId, roleNames, cancellationToken)
            .ConfigureAwait(false);
        switch (roles)
        {
            case GuestRoleResolution.RoleNotFound missingRole:
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.SecurityRoleNotFound,
                    $"Security role '{missingRole.RoleName}' (H11UserProvisioningOptions:GuestSecurityRoleNames) is not in " +
                    $"the customer's business unit {customerUnitId:D} — it ships in the Spaarke solution (H6) and Dataverse " +
                    "copies it into every unit. Nothing was written (no invitation sent).",
                    cancellationToken).ConfigureAwait(false);
            case GuestRoleResolution.Failure roleFailure:
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.DataverseUserFailed,
                    $"The guests' security role(s) could not be resolved: {roleFailure.Diagnostic}. Nothing was written.",
                    cancellationToken).ConfigureAwait(false);
        }
        var roleIds = ((GuestRoleResolution.Resolved)roles).RoleIds;

        var invited = new List<ProvisionedUserRecord>();
        var invitedUserIds = new List<string>();
        // Every entry carries an email: UserProvisioningIntake checked the whole list before this branch (T245c).
        // D15: diagnostics name a user by position in usersJson, never by email.
        for (var i = 0; i < users.Count; i++)
        {
            var entry = users[i];
            var position = i + 1;
            B2BInvitationOutcome invitationOutcome;
            try
            {
                invitationOutcome = await _b2bInvitationClient.InviteAsync(entry, tenantId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsFailureNotCallerCancellation(ex, cancellationToken))
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.B2BInvitationFailed,
                    $"B2B invitation infrastructure error for usersJson entry {position}: {ex.GetType().Name}: {ex.Message}",
                    cancellationToken).ConfigureAwait(false);
            }

            if (invitationOutcome is B2BInvitationOutcome.Failure invitationFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.B2BInvitationFailed,
                    $"B2B invitation failed for usersJson entry {position}: {invitationFailure.Diagnostic}",
                    cancellationToken).ConfigureAwait(false);
            }

            var success = (B2BInvitationOutcome.Success)invitationOutcome;
            invitedUserIds.Add(success.InvitedUserId);
            invited.Add(new ProvisionedUserRecord(success.InvitedUserId, entry.Email!, IdentityPresetB2BGuest));
        }

        B2BConsentVerificationResult consentResult;
        try
        {
            consentResult = await _consentVerifier.VerifyAsync(tenantId, invitedUserIds, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFailureNotCallerCancellation(ex, cancellationToken))
        {
            _logger.LogError(ex,
                "H11 B2B consent verifier threw unexpected exception: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.B2BInvitationFailed,
                (ex is OperationCanceledException
                    ? "B2B consent verification timed out."
                    : $"B2B consent-verification infrastructure error: {ex.GetType().Name}: {ex.Message}.") + " " +
                $"{invitedUserIds.Count} invitation(s) WERE sent — only consent verification failed. Resumable.",
                cancellationToken).ConfigureAwait(false);
        }

        if (consentResult is B2BConsentVerificationResult.Pending pending)
        {
            // WaitingOnGate — envelope processed correctly (invitations WERE
            // sent), but downstream fan-out MUST wait for the invited
            // guest(s) to accept. Do NOT add a CompletedPhase entry (H11
            // hasn't finished its job yet). Persist the invited-but-not-yet-
            // verified users so an operator can see who was invited.
            run.Status = RunStatus.WaitingOnGate;
            run.CurrentPhase = HandlerIdentifier;
            run.ErrorDetail = null;
            run.InterStepState.ProvisionedUsers = invited;
            run.GateStates[H11Gates.B2BConsent] = new GateEntry
            {
                Status = GateState.Pending,
                VerifierHandler = HandlerIdentifier,
                Evidence = pending.Evidence,
            };

            _logger.LogInformation(
                "H11 B2B consent Pending — run transitioned to WaitingOnGate: runId={RunId} customerId={CustomerId} " +
                "accepted={AcceptedCount}/{ExpectedCount}",
                envelope.RunId, envelope.CustomerId, pending.AcceptedCount, pending.ExpectedCount);

            var pendingReplace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
            return HandlePendingReplace(pendingReplace, run, idempotencyKey);
        }

        var verified = (B2BConsentVerificationResult.Verified)consentResult;

        // Task 232: every guest has redeemed — add each to the environment security group, then make it a Dataverse
        // user holding the configured role(s). Each step is idempotent, so a failed run resumes from the start.
        var guestUsers = new List<ProvisionedUserRecord>(invited.Count);
        for (var i = 0; i < invited.Count; i++)
        {
            var guest = invited[i];
            var position = i + 1;

            var membership = await _securityGroupClient
                .AddMemberAsync(securityGroupId, guest.UserId, tenantId, cancellationToken).ConfigureAwait(false);
            if (membership is SecurityGroupMembershipOutcome.Failure membershipFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.SecurityGroupMembershipFailed,
                    $"Adding the guest of usersJson entry {position} (Entra user {guest.UserId}) to the environment " +
                    $"security group failed: {membershipFailure.Diagnostic}",
                    cancellationToken).ConfigureAwait(false);
            }

            var dataverseUser = await _guestUserWriter.EnsureGuestUserAsync(
                new DataverseGuestUserRequest(dataverseEnvUrl, tenantId, guest.UserId, customerUnitId, roleIds),
                cancellationToken).ConfigureAwait(false);
            if (dataverseUser is DataverseGuestUserOutcome.InForeignBusinessUnit elsewhere)
            {
                return await FailAsync(run, etag, FailureClass.QuarantineRequired, H11Rejections.GuestInForeignBusinessUnit,
                    $"The guest of usersJson entry {position} (Entra user {guest.UserId}, systemuser {elsewhere.SystemUserId}) is " +
                    $"already a user in business unit {elsewhere.BusinessUnitId}, neither the customer's unit {customerUnitId:D} " +
                    "nor the root. H11 never moves it (it may be the Secure Record unit, where no user may be): moving it is an " +
                    "owner decision; then resume.",
                    cancellationToken).ConfigureAwait(false);
            }
            if (dataverseUser is DataverseGuestUserOutcome.Failure userFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.DataverseUserFailed,
                    $"Making the guest of usersJson entry {position} (Entra user {guest.UserId}) a Dataverse user " +
                    $"failed: {userFailure.Diagnostic}",
                    cancellationToken).ConfigureAwait(false);
            }

            guestUsers.Add(guest with { DataverseSystemUserId = ((DataverseGuestUserOutcome.Success)dataverseUser).SystemUserId });
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "H11 B2BGuest provisioning succeeded: runId={RunId} customerId={CustomerId} userCount={UserCount} " +
            "accepted={AcceptedCount}/{ExpectedCount} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, guestUsers.Count, verified.AcceptedCount, verified.ExpectedCount,
            stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(
            run, etag, idempotencyKey, guestUsers, envelope, setB2BConsentGate: true, verified.Evidence,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// T232: true for a collaborator failure H11 turns into a Resumable result — including an HttpClient timeout, which
    /// is an <see cref="OperationCanceledException"/> while the caller's token is NOT cancelled. Only the caller's own
    /// cancellation propagates.
    /// </summary>
    private static bool IsFailureNotCallerCancellation(Exception ex, CancellationToken cancellationToken)
        => ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Task 232: <c>null</c> when <paramref name="groupId"/> is this customer's environment security group
    /// (<c>sprk-{customerId}-users</c>, security-enabled); otherwise why it is refused.
    /// </summary>
    private async Task<string?> CheckSecurityGroupAsync(
        string groupId, string tenantId, string customerId, CancellationToken cancellationToken)
    {
        var expectedName = ExpectedSecurityGroupName(customerId);
        var read = await _securityGroupClient.ReadAsync(groupId, tenantId, cancellationToken).ConfigureAwait(false);
        return read switch
        {
            SecurityGroupReadOutcome.Failure failure =>
                $"The environment security group {groupId} could not be read: {failure.Diagnostic}. Nothing was written.",
            SecurityGroupReadOutcome.Found found when !string.Equals(found.DisplayName, expectedName, StringComparison.OrdinalIgnoreCase) =>
                $"The environment security group {groupId} is named '{found.DisplayName}', not '{expectedName}' — it is " +
                "not this customer's group (PRQ-C-10). Nothing was written; correct the group, then resume.",
            SecurityGroupReadOutcome.Found { SecurityEnabled: false } =>
                $"Group {groupId} ('{expectedName}') is not a security group — a Dataverse environment's security group " +
                "must be security-enabled (PRQ-C-10). Nothing was written.",
            _ => null,
        };
    }

    /// <summary>Task 232: the display name of a customer environment's security group (PRQ-C-10).</summary>
    internal static string ExpectedSecurityGroupName(string customerId) => $"sprk-{customerId}-users";

    /// <summary>
    /// Computes the deterministic H11 idempotency key: <c>users-{customerId}</c>.
    /// Per POML constraint the key is customerId-ONLY (version-independent) —
    /// per-user idempotency is delegated to the Graph UPN alt-key check.
    /// Exposed internal so unit tests can construct expected keys without
    /// duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return $"users-{customerId}";
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

        run.GateStates[$"h11-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H11 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H11 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        IReadOnlyList<ProvisionedUserRecord> provisioned,
        HandlerEnvelope envelope,
        bool setB2BConsentGate,
        JsonElement? b2bConsentEvidence,
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
        run.InterStepState.ProvisionedUsers = provisioned.ToList();

        if (setB2BConsentGate)
        {
            run.GateStates[H11Gates.B2BConsent] = new GateEntry
            {
                Status = GateState.Verified,
                VerifiedAt = completedAt,
                VerifierHandler = HandlerIdentifier,
                Evidence = b2bConsentEvidence,
            };
        }

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H11 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: H11Rejections.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H11 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H11 " +
                             "which will short-circuit on idempotency once the winning write lands.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H11 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: H11Rejections.RunDeletedDuringProvisioning,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H11 was in flight.");
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
                "H11 WaitingOnGate write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: H11Rejections.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H11 read + WaitingOnGate write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H11.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: H11Rejections.RunDeletedDuringProvisioning,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H11 was writing WaitingOnGate.");
        }

        // Success on the WaitingOnGate write path — H11 processed the
        // envelope successfully; the reconciler observes WaitingOnGate +
        // waits for operator resume (spec.md FR-14 B2B consent-gate branch).
        return new HandlerResult.Success(idempotencyKey);
    }
}
