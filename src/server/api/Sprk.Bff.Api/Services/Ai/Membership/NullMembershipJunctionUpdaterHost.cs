// R3 Part 1 Phase 2 — Null-Object peer for the membership-change consumer host (task 084).
//
// ADR-032 (Null-Object Kill-Switch Pattern) compliance: when the feature
// flag `Membership:JunctionUpdater:Enabled=false`, this peer is registered
// as the `IHostedService` in place of the real
// `MembershipJunctionUpdaterHost`. The peer performs ZERO Service Bus
// work — no client construction, no subscription processor, no message
// pump. It logs an informational message on start so operators can
// confirm the kill-switch state from logs, then returns immediately.
//
// Why a Null host (not a feature-gate skip):
//
//   1. Symmetric registration per `bff-extensions.md` §F.1 — both
//      branches of the kill switch register the same surface (an
//      `IHostedService`). The host's downstream consumers (the .NET
//      hosting model itself) see a registered hosted service in BOTH
//      states; the difference is in the host's behavior.
//
//   2. Defensive design — task 071's topic Bicep is authored but the
//      operator deploy is gated (see notes/operator-followup-task071.md).
//      A bare feature-gate skip would let the real host construct a
//      `ServiceBusClient` against an absent topic on startup, producing
//      a `MessagingEntityNotFoundException` that aborts host startup
//      (taking down the entire BFF, not just the membership feature).
//      The Null peer fully avoids the construction.
//
//   3. Operator semantics — operators flipping the flag from false → true
//      do NOT need to redeploy the BFF binary; the next BFF restart
//      replaces the Null peer with the real host. The pattern is
//      symmetric to the conditional service registration documented in
//      ADR-032 §"P2 Quiet no-op Null-Object" — except adapted for
//      hosted-services which do NOT participate in minimal-API
//      metadata-gen (so a P3 fail-fast is NOT required here).
//
// Reference: .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            projects/spaarke-platform-foundations-r3/notes/
//            operator-followup-task071.md.

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Null-Object hosted-service peer for the membership-change consumer.
/// Registered when <see cref="MembershipJunctionUpdaterOptions.Enabled"/>
/// is <c>false</c>. Performs no Service Bus work; logs an informational
/// message and returns immediately.
/// </summary>
public sealed class NullMembershipJunctionUpdaterHost : BackgroundService
{
    private readonly ILogger<NullMembershipJunctionUpdaterHost> _logger;

    public NullMembershipJunctionUpdaterHost(
        ILogger<NullMembershipJunctionUpdaterHost> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MembershipJunctionUpdater disabled (Membership:JunctionUpdater:Enabled=false). " +
            "No Service Bus subscription will be created. " +
            "Flip the flag to true after the topic 'sprk-membership-changes' is operator-deployed (see task 071 runbook).");
        return Task.CompletedTask;
    }
}
