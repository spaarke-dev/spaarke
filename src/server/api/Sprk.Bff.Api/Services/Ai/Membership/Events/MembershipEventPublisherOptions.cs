// R3 Part 1 Phase 2 — Task 081 (2026-06-22)
// Configuration for MembershipEventPublisher. Binds from the
// "Membership:EventPublisher" appsettings section. Default Enabled=false:
// the operator MUST flip the flag on AFTER task 071 deploys the Service
// Bus topic `sprk-membership-changes`. Until then, the Null-Object peer
// is registered per ADR-032 P2 (quiet no-op + log) and BFF endpoints
// fire-and-forget into the no-op publisher (no Azure dependency).
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.3,
//            FR-2P2.6, Q2, NFR-08; .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            sibling Services/Ai/Membership/MembershipOptions.cs (binding convention).

namespace Sprk.Bff.Api.Services.Ai.Membership.Events;

/// <summary>
/// Configuration for <see cref="MembershipEventPublisher"/>. Bound from the
/// <c>"Membership:EventPublisher"</c> appsettings section via the Options
/// pattern (ADR-010). Default <see cref="Enabled"/>=<c>false</c> — operator
/// flips the flag after the Service Bus topic deploy (task 071) completes.
/// </summary>
public sealed class MembershipEventPublisherOptions
{
    /// <summary>
    /// Configuration section name used by
    /// <c>IConfiguration.GetSection(...)</c>: <c>"Membership:EventPublisher"</c>.
    /// </summary>
    public const string SectionName = "Membership:EventPublisher";

    /// <summary>
    /// Master switch. When <c>false</c> (default), DI registers a
    /// <see cref="NullMembershipEventPublisher"/> peer (ADR-032 P2 Quiet
    /// no-op) and publish calls log + return immediately with NO Azure
    /// Service Bus interaction. When <c>true</c>, the real
    /// <see cref="MembershipEventPublisher"/> sends payloads to the topic
    /// named by <see cref="TopicName"/>. Operator flips to <c>true</c>
    /// AFTER task 071 provisions the topic in Azure.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Service Bus topic name for membership-change events. Per spec D3
    /// (Owner Clarification 2026-06-20), the canonical topic is
    /// <c>sprk-membership-changes</c> with subscription-per-consumer.
    /// </summary>
    public string TopicName { get; set; } = "sprk-membership-changes";
}
