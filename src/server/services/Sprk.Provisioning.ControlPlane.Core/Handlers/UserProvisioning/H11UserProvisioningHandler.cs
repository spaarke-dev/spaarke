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
//   │ Missing/malformed/empty usersJson           │ Resumable                 │
//   │ Run not found in Cosmos partition          │ Resumable                 │
//   │ NativeAccount: user creation failed        │ Resumable (POST /users is │
//   │                                             │ idempotent via UPN check) │
//   │ NativeAccount: license assignment failed   │ RetryableWithCleanup      │
//   │                                             │ (user exists; assign-    │
//   │                                             │ License itself idempotent│
//   │ B2BGuest: invitation failed                │ Resumable (POST           │
//   │                                             │ /invitations idempotent)  │
//   │ B2B consent Pending (WaitingOnGate         │ (NOT a failure — Success  │
//   │ transition)                                 │ with WaitingOnGate state) │
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
// DOWNSTREAM ENQUEUE (Wave C4 note):
//   H11 does not enqueue a specific successor. Parity with H3/H5/H6/H10: the
//   Wave C5 reconciler owns fan-out from H11 to H12a/b/c per the plan.md
//   critical path. This handler mutates Cosmos state (advancing CurrentPhase
//   + CompletedPhases + InterStepState.ProvisionedUsers + GateStates) and
//   returns Success (or WaitingOnGate + Success on B2B consent-pending).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H11UserProvisioningHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = "H11";

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the D6 identity preset (<c>B2BGuest</c> | <c>NativeAccount</c>).</summary>
    public const string IdentityPresetParameterKey = "identityPreset";

    /// <summary>Non-secret parameter key carrying the JSON-array-encoded user list (see UserProvisioningEntry.cs header for the encoding rationale).</summary>
    public const string UsersJsonParameterKey = "usersJson";

    /// <summary>D6 identity preset value — cross-tenant B2B guest access.</summary>
    public const string IdentityPresetB2BGuest = "B2BGuest";

    /// <summary>D6 identity preset value — low-IT-friction native tenant account.</summary>
    public const string IdentityPresetNativeAccount = "NativeAccount";

    private static readonly JsonSerializerOptions UsersJsonDeserializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IProvisioningRunRepository _repository;
    private readonly IGraphUserProvisioner _userProvisioner;
    private readonly IB2BInvitationClient _b2bInvitationClient;
    private readonly IB2BConsentVerifier _consentVerifier;
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
        ILogger<H11UserProvisioningHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(userProvisioner);
        ArgumentNullException.ThrowIfNull(b2bInvitationClient);
        ArgumentNullException.ThrowIfNull(consentVerifier);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _userProvisioner = userProvisioner;
        _b2bInvitationClient = b2bInvitationClient;
        _consentVerifier = consentVerifier;
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

        // (4) identityPreset guard (design.md D6).
        if (!TryGetNonEmpty(parameters, IdentityPresetParameterKey, out var identityPreset))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.MissingIdentityPreset,
                "Run parameter 'identityPreset' is required by H11 (design.md D6) — MUST be " +
                $"'{IdentityPresetB2BGuest}' or '{IdentityPresetNativeAccount}'. Operator/upstream must " +
                "populate before H11 dispatches.",
                cancellationToken).ConfigureAwait(false);
        }

        var isNativeAccount = string.Equals(identityPreset, IdentityPresetNativeAccount, StringComparison.Ordinal);
        var isB2BGuest = string.Equals(identityPreset, IdentityPresetB2BGuest, StringComparison.Ordinal);
        if (!isNativeAccount && !isB2BGuest)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.InvalidIdentityPreset,
                $"Run parameter 'identityPreset' value '{identityPreset}' is not one of the two D6 presets " +
                $"('{IdentityPresetB2BGuest}', '{IdentityPresetNativeAccount}').",
                cancellationToken).ConfigureAwait(false);
        }

        // (5) usersJson guard.
        if (!TryGetNonEmpty(parameters, UsersJsonParameterKey, out var usersJson))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.MissingUsers,
                "Run parameter 'usersJson' is required by H11 — a JSON array of {firstName,lastName,email," +
                "companyName} entries. Operator/upstream must populate before H11 dispatches.",
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<UserProvisioningEntry> users;
        try
        {
            users = JsonSerializer.Deserialize<IReadOnlyList<UserProvisioningEntry>>(
                usersJson, UsersJsonDeserializerOptions) ?? Array.Empty<UserProvisioningEntry>();
        }
        catch (JsonException ex)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.MalformedUsersPayload,
                $"Run parameter 'usersJson' could not be deserialized as a user array: {ex.Message}",
                cancellationToken).ConfigureAwait(false);
        }

        if (users.Count == 0)
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.MissingUsers,
                "Run parameter 'usersJson' deserialized to an empty array — H11 requires at least one user entry.",
                cancellationToken).ConfigureAwait(false);
        }

        return isNativeAccount
            ? await HandleNativeAccountAsync(run, etag, envelope, idempotencyKey, tenantId, users, stopwatch, cancellationToken)
                .ConfigureAwait(false)
            : await HandleB2BGuestAsync(run, etag, envelope, idempotencyKey, tenantId, users, stopwatch, cancellationToken)
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
        var provisioned = new List<ProvisionedUserRecord>();
        foreach (var entry in users)
        {
            UserCreationOutcome creationOutcome;
            try
            {
                creationOutcome = await _userProvisioner.CreateUserAsync(entry, tenantId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.UserCreationFailed,
                    $"User creation infrastructure error for '{entry.FirstName} {entry.LastName}': " +
                    $"{ex.GetType().Name}: {ex.Message}",
                    cancellationToken).ConfigureAwait(false);
            }

            if (creationOutcome is UserCreationOutcome.Failure creationFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.UserCreationFailed,
                    $"User creation failed for '{entry.FirstName} {entry.LastName}': {creationFailure.Diagnostic}",
                    cancellationToken).ConfigureAwait(false);
            }

            var created = (UserCreationOutcome.Success)creationOutcome;

            LicenseAssignmentOutcome licenseOutcome;
            try
            {
                licenseOutcome = await _userProvisioner.AssignLicenseAsync(created.UserId, tenantId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FailAsync(run, etag, FailureClass.RetryableWithCleanup, H11Rejections.LicenseAssignmentFailed,
                    $"License assignment infrastructure error for user '{created.Upn}' " +
                    $"({entry.FirstName} {entry.LastName}): {ex.GetType().Name}: {ex.Message}. User account already " +
                    "exists — retry re-attempts only the license assignment.",
                    cancellationToken).ConfigureAwait(false);
            }

            if (licenseOutcome is LicenseAssignmentOutcome.Failure licenseFailure)
            {
                return await FailAsync(run, etag, FailureClass.RetryableWithCleanup, H11Rejections.LicenseAssignmentFailed,
                    $"License assignment failed for user '{created.Upn}' ({entry.FirstName} {entry.LastName}): " +
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
    /// B2BGuest branch (design.md D6): B2B invitation per user + a separate
    /// consent-verification query. Pending consent transitions the run to
    /// WaitingOnGate (NOT a failure) — parity with H3's admin-consent gate.
    /// </summary>
    private async Task<HandlerResult> HandleB2BGuestAsync(
        ProvisioningRun run,
        string etag,
        HandlerEnvelope envelope,
        string idempotencyKey,
        string tenantId,
        IReadOnlyList<UserProvisioningEntry> users,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var invited = new List<ProvisionedUserRecord>();
        var invitedUserIds = new List<string>();
        foreach (var entry in users)
        {
            if (string.IsNullOrWhiteSpace(entry.Email))
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.B2BInvitationFailed,
                    $"B2BGuest entry for '{entry.FirstName} {entry.LastName}' is missing 'email' — B2B invitations " +
                    "require invitedUserEmailAddress.",
                    cancellationToken).ConfigureAwait(false);
            }

            B2BInvitationOutcome invitationOutcome;
            try
            {
                invitationOutcome = await _b2bInvitationClient.InviteAsync(entry, tenantId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.B2BInvitationFailed,
                    $"B2B invitation infrastructure error for '{entry.Email}': {ex.GetType().Name}: {ex.Message}",
                    cancellationToken).ConfigureAwait(false);
            }

            if (invitationOutcome is B2BInvitationOutcome.Failure invitationFailure)
            {
                return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.B2BInvitationFailed,
                    $"B2B invitation failed for '{entry.Email}': {invitationFailure.Diagnostic}",
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H11 B2B consent verifier threw unexpected exception: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return await FailAsync(run, etag, FailureClass.Resumable, H11Rejections.B2BInvitationFailed,
                $"B2B consent-verification infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
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

        stopwatch.Stop();
        var verified = (B2BConsentVerificationResult.Verified)consentResult;
        _logger.LogInformation(
            "H11 B2BGuest provisioning succeeded: runId={RunId} customerId={CustomerId} userCount={UserCount} " +
            "accepted={AcceptedCount}/{ExpectedCount} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, invited.Count, verified.AcceptedCount, verified.ExpectedCount,
            stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(
            run, etag, idempotencyKey, invited, envelope, setB2BConsentGate: true, verified.Evidence,
            cancellationToken).ConfigureAwait(false);
    }

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
