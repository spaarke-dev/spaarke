// -----------------------------------------------------------------------------
// IB2BConsentVerifier.cs
//
// L2 abstraction over the B2B consent-verification GATE H11 owns for the
// B2BGuest (D6) branch. Shape mirrors H3's IAdminConsentVerifier
// (Handlers/EntraAppReg/IAdminConsentVerifier.cs) — Verified vs Pending, with
// Pending driving a WaitingOnGate transition rather than a Failure.
//
// SPEC / DESIGN references:
//   - spec.md FR-14 (H11): "B2B needs consent-verification gate."
//   - design.md §4.1 H11 row: B2B consent gate; WaitingOnGate on pending.
//
// GATE SEMANTICS:
//   - Verified(acceptedCount, expectedCount) — every invited guest has
//     redeemed their invitation (externalUserState == "Accepted"); the
//     `b2b-consent` gate flips to Verified; handler returns Success.
//   - Pending(acceptedCount, expectedCount, diagnostic) — one or more
//     invited guests have not yet accepted; handler transitions run to
//     WaitingOnGate with `b2b-consent` = Pending; Reconciler re-invokes H11
//     after the customer admin(s) accept.
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>
/// Verifies that every invited B2B guest has accepted their invitation.
/// Domain outcomes (accepted, pending) return typed results; only unexpected
/// infrastructure errors (transient Graph fault, network fault) should throw.
/// </summary>
public interface IB2BConsentVerifier
{
    /// <summary>
    /// Queries Microsoft Graph for the current <c>externalUserState</c> of
    /// each invited guest.
    /// </summary>
    /// <param name="tenantId">Target Entra tenant id (§4D I1 — mandatory).</param>
    /// <param name="invitedUserIds">Entra ID object ids of the invited guests (from <see cref="B2BInvitationOutcome.Success.InvitedUserId"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<B2BConsentVerificationResult> VerifyAsync(
        string tenantId,
        IReadOnlyList<string> invitedUserIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of one <see cref="IB2BConsentVerifier.VerifyAsync"/> invocation.
/// Exhaustive: <see cref="Verified"/> | <see cref="Pending"/>.
/// </summary>
public abstract record B2BConsentVerificationResult
{
    private B2BConsentVerificationResult() { }

    /// <summary>Every invited guest has accepted their B2B invitation.</summary>
    /// <param name="AcceptedCount">Number of guests observed with <c>externalUserState == "Accepted"</c>.</param>
    /// <param name="ExpectedCount">Total number of guests invited this run.</param>
    /// <param name="Evidence">Optional Graph response payload for the gate's <c>Evidence</c> field.</param>
    public sealed record Verified(
        int AcceptedCount,
        int ExpectedCount,
        JsonElement? Evidence) : B2BConsentVerificationResult;

    /// <summary>
    /// One or more invited guests have NOT yet accepted. Handler transitions
    /// run to <see cref="Sprk.Provisioning.ControlPlane.Models.RunStatus.WaitingOnGate"/>
    /// with the <c>b2b-consent</c> gate marked
    /// <see cref="Sprk.Provisioning.ControlPlane.Models.GateState.Pending"/>.
    /// </summary>
    /// <param name="AcceptedCount">Number of guests currently observed as accepted (may be zero).</param>
    /// <param name="ExpectedCount">Total number of guests invited this run.</param>
    /// <param name="Diagnostic">Human-readable diagnostic naming the still-pending guests.</param>
    /// <param name="Evidence">Optional Graph response payload for the gate's <c>Evidence</c> field.</param>
    public sealed record Pending(
        int AcceptedCount,
        int ExpectedCount,
        string Diagnostic,
        JsonElement? Evidence) : B2BConsentVerificationResult;
}
