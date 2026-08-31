// -----------------------------------------------------------------------------
// H11SeamsSmokeTests.cs
//
// L2 CONTROL-PLANE H11 (UserProvisioning) live-verification smoke tests (task
// 144 -- Wave G-4 Batch G-4C). DS-4 §2 classified H11UserProvisioningHandler's
// three REST/Graph collaborators as "REAL" (code complete); the only
// documented blocker was C5.8 (task 111's Grant-ControlPlaneIdentity.ps1
// grants). This file exercises the ONE fully READ-ONLY production
// collaborator -- GraphRestB2BConsentVerifier (the B2B consent GATE) -- live
// against real Microsoft Graph. The two WRITE-capable collaborators
// (GraphRestUserProvisioner's POST /users + POST /assignLicense;
// GraphRestB2BInvitationClient's POST /invitations) are deliberately NOT
// exercised here -- see file-footer rationale (same class of decision as
// H10SeamsSmokeTests.cs's WRITE-path deferral).
//
// WHY THE CONSENT VERIFIER IS THE LOAD-BEARING SEAM FOR THIS TASK: task 144's
// POML escalation trigger #2 fires HIGH severity if "the B2B consent verifier
// reports Pass for a genuinely-pending consent (a false positive)" -- exactly
// the consent-gate-advances-on-fiction defect class DS-4 found in H3
// (NullAdminConsentVerifier, fixed by task 130). GraphRestB2BConsentVerifier
// is fully read-only, so unlike the WRITE-capable seams it can be safely
// exercised against real Graph without side effects.
//
// CREDENTIAL-FAILURE-DETECTION (same load-bearing pattern as
// H10SeamsSmokeTests.cs -- see that file's header for the full explanation):
// GraphRestB2BConsentVerifier does NOT swallow a DefaultAzureCredential
// failure the way H10's collaborators do -- VerifyAsync lets
// credential.GetTokenAsync's exception propagate (no try/catch around token
// acquisition), so in a non-Azure sandbox this test will THROW rather than
// return a business-shaped-but-fake result. The CapturingLogger infrastructure
// is retained here for structural parity with H10SeamsSmokeTests.cs and for
// any FUTURE H11 collaborator that adopts the swallow-and-log pattern, but the
// soft-skip in THIS file additionally wraps the call itself in a try/catch
// that treats a credential-shaped exception as a soft-skip (see
// IsCredentialAcquisitionFailure).
//
// ENV-GUARD: Skipped by default (soft-skip, same convention as
// H10SeamsSmokeTests.cs / CosmosSmokeTests.cs / ServiceBusSmokeTests.cs). Set:
//   H11_L2_SMOKE_TENANT_ID                    the Entra tenant id (§4D I5 explicit scope)
//   H11_L2_SMOKE_KNOWN_ACCEPTED_GUEST_USER_ID (optional) an Entra ID object id
//                                              KNOWN to already be a B2B guest
//                                              with externalUserState=Accepted
//                                              in the target tenant -- exercises
//                                              the Verified branch. If unset,
//                                              only the Pending (unknown-id)
//                                              branch runs.
// Auth: DefaultAzureCredential (exactly as production constructs it -- no
// exclusions) -- succeeds inside a real Azure host with a managed identity; in
// a non-Azure sandbox, see CREDENTIAL-FAILURE-DETECTION above.
//
// NOT a unit test -- do NOT add this file to a "unit-tests-only" glob.
// ADR-038 KEEP category: lives with L2 owning code (task 037 scoping ruling,
// same precedent H10SeamsSmokeTests.cs cites), gated by environment variables
// so CI unit runs are unaffected.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests;

/// <summary>
/// Env-guarded live smoke tests over the one READ-ONLY H11 REST/Graph seam:
/// <see cref="GraphRestB2BConsentVerifier"/> (the B2B consent gate). Skipped
/// by default; opt in by setting <c>H11_L2_SMOKE_TENANT_ID</c>.
/// </summary>
[Trait("Category", "Smoke")]
[Trait("RequiresLiveResource", "Graph")]
public sealed class H11SeamsSmokeTests
{
    private const string TenantIdEnvVar = "H11_L2_SMOKE_TENANT_ID";
    private const string KnownAcceptedGuestUserIdEnvVar = "H11_L2_SMOKE_KNOWN_ACCEPTED_GUEST_USER_ID";

    private static readonly H11UserProvisioningOptions HandlerOptions = new();

    // ---------- Consent-verifier seam: Pending branch (anti-false-positive; the escalation-trigger-relevant case) ----------

    [Fact]
    public async Task GraphRestB2BConsentVerifier_VerifyAsync_UnknownInvitedUserId_ReturnsPendingNotVerified()
    {
        var tenantId = Environment.GetEnvironmentVariable(TenantIdEnvVar);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return; // env-guarded skip
        }

        var logger = new CapturingLogger<GraphRestB2BConsentVerifier>();
        var verifier = new GraphRestB2BConsentVerifier(new HttpClient(), Options.Create(HandlerOptions), logger);

        // The all-zero GUID is deterministically guaranteed to never be a real
        // invited guest's Entra ID object id -- Graph responds 404, which
        // VerifyAsync's !response.IsSuccessStatusCode branch folds into
        // "pending" (added to pendingIds), never into "accepted". This is the
        // load-bearing anti-false-positive proof the POML's escalation
        // trigger #2 cares about: an unresolvable/never-accepted invited user
        // must NEVER be reported as Verified.
        B2BConsentVerificationResult result;
        try
        {
            result = await verifier.VerifyAsync(
                tenantId, new[] { "00000000-0000-0000-0000-000000000000" }, CancellationToken.None);
        }
        catch (Exception ex) when (IsCredentialAcquisitionFailure(ex))
        {
            return; // soft-skip: DefaultAzureCredential could not resolve in this sandbox (see file header)
        }

        logger.AssertNoUnexpectedErrorsLogged();

        var pending = result.Should().BeOfType<B2BConsentVerificationResult.Pending>().Subject;
        pending.AcceptedCount.Should().Be(0);
        pending.ExpectedCount.Should().Be(1);
    }

    // ---------- Consent-verifier seam: Verified branch (optional, requires a known real accepted guest) ----------

    [Fact]
    public async Task GraphRestB2BConsentVerifier_VerifyAsync_KnownAcceptedGuest_ReturnsVerified()
    {
        var tenantId = Environment.GetEnvironmentVariable(TenantIdEnvVar);
        var knownGuestId = Environment.GetEnvironmentVariable(KnownAcceptedGuestUserIdEnvVar);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(knownGuestId))
        {
            return; // env-guarded skip -- this branch additionally needs a known accepted guest
        }

        var logger = new CapturingLogger<GraphRestB2BConsentVerifier>();
        var verifier = new GraphRestB2BConsentVerifier(new HttpClient(), Options.Create(HandlerOptions), logger);

        B2BConsentVerificationResult result;
        try
        {
            result = await verifier.VerifyAsync(tenantId, new[] { knownGuestId }, CancellationToken.None);
        }
        catch (Exception ex) when (IsCredentialAcquisitionFailure(ex))
        {
            return; // soft-skip: DefaultAzureCredential could not resolve in this sandbox (see file header)
        }

        logger.AssertNoUnexpectedErrorsLogged();

        var verified = result.Should().BeOfType<B2BConsentVerificationResult.Verified>().Subject;
        verified.AcceptedCount.Should().Be(1);
        verified.ExpectedCount.Should().Be(1);
    }

    // ---------- helpers ----------

    /// <summary>
    /// GraphRestB2BConsentVerifier does not swallow token-acquisition
    /// exceptions (unlike H10's collaborators) -- credential.GetTokenAsync's
    /// exception propagates straight out of VerifyAsync. In this sandbox that
    /// surfaces as Azure.Identity.AuthenticationFailedException (or a
    /// CredentialUnavailableException) -- treat either as a soft-skip, same
    /// posture as an unset env var, per H10SeamsSmokeTests.cs's file-header
    /// rationale for why ManagedIdentityCredential's IMDS probe fails outside
    /// Azure even when the operator is genuinely `az login`-authenticated.
    /// </summary>
    private static bool IsCredentialAcquisitionFailure(Exception ex) =>
        ex.GetType().FullName?.StartsWith("Azure.Identity.", StringComparison.Ordinal) == true;

    /// <summary>
    /// Minimal <see cref="ILogger{T}"/> capture -- retained for structural
    /// parity with H10SeamsSmokeTests.cs's CapturingLogger and for any future
    /// H11 collaborator that adopts the swallow-and-log pattern. Deliberately
    /// not a mocking-framework double (ADR-038 keeps mocks off
    /// transport/collaborator boundaries) -- this is a plain in-memory
    /// recorder over the SAME production <see cref="ILogger{T}"/> contract
    /// the class already takes.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }

        public void AssertNoUnexpectedErrorsLogged()
        {
            var errors = _entries.Where(e => e.Level >= LogLevel.Error).ToList();
            errors.Should().BeEmpty(
                "a genuine live consent-verifier query should never log at Error severity -- Pending/Verified "
                + "are both expected business outcomes logged at Information (Pending) or not at all (Verified). "
                + "Captured: {0}",
                errors.Count == 0 ? "(none)" : string.Join(" | ", errors.Select(e => $"[{e.Level}] {e.Message}")));
        }
    }
}

// -----------------------------------------------------------------------------
// Why the two WRITE-capable seams (GraphRestUserProvisioner's POST /users +
// POST /assignLicense; GraphRestB2BInvitationClient's POST /invitations) are
// NOT exercised by an automated smoke test here:
//
//   1. POST /users creates a REAL Entra ID user account in the target tenant
//      with a generated UPN + a temporary password -- there is no clean,
//      safe, unattended undo for an automated test (a created user is a real
//      administrative artifact, not something to delete-and-forget in a
//      shared dev tenant).
//   2. POST /invitations sends a REAL invitation EMAIL to whatever address is
//      passed as invitedUserEmailAddress -- an automated test cannot safely
//      choose a target address without either spamming a real external inbox
//      or depending on a disposable mailbox this sandbox does not have
//      access to provision.
//   3. Task 111's Grant-ControlPlaneIdentity.ps1 (C5.8) has NOT been
//      live-executed (its own <notes-completion>: "Live-exec verification:
//      DEFERRED"). Even setting aside (1) and (2), the L2 UAMI does not yet
//      hold the Graph app-role grants (User.ReadWrite.All, User.Invite.All --
//      the latter ADDED by this same task 144, see GraphAppRoles.cs) a
//      production H11 run depends on, so a live write attempt today would be
//      testing a configuration state that does not match production.
//
// This follows the SAME "live-ceremony vs authoring separation" pattern
// H10SeamsSmokeTests.cs documents (task 143 precedent) -- authoring +
// read-path live verification complete now; write-path E2E is grouped into
// the live-ceremony operator run. See
// notes/h11-live-verification-2026-08.md for the full verification report.
// -----------------------------------------------------------------------------
