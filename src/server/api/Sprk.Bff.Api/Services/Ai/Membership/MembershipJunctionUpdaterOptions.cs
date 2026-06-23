// R3 Part 1 Phase 2 — Subscription consumer configuration (task 084).
//
// Binds the "Membership:JunctionUpdater" appsettings section. The
// real consumer host (`MembershipJunctionUpdaterHost`) is feature-gated
// via the <see cref="Enabled"/> flag — when false, a Null-Object peer
// (`NullMembershipJunctionUpdaterHost`) is registered instead and
// performs zero Service Bus work. This is required because task 071's
// topic Bicep is authored but not yet operator-deployed; until deploy
// completes, the BFF cannot legally call
// <c>ServiceBusClient.CreateProcessor</c> against
// <c>sprk-membership-changes</c>/<c>recon-junction-updater</c> without
// failing at startup. ADR-032 (Null-Object Kill-Switch Pattern) codifies
// this defense.
//
// Field rationale:
//   Enabled                — master toggle (default false; operator flips
//                            to true post-topic-deploy)
//   TopicName              — full topic name (defaults to D3-resolved
//                            "sprk-membership-changes")
//   SubscriptionName       — full subscription name (defaults to
//                            "recon-junction-updater" — see task 071 Bicep
//                            module `membership-topic.bicep`)
//   ServiceBusNamespace    — FQDN of the namespace (for token-credential
//                            client construction; co-existing
//                            JobProcessingModule's connection-string
//                            client uses a sibling registration). Empty
//                            in the default template; operator sets the
//                            real value alongside flipping `Enabled` to
//                            true.
//   MaxConcurrentCalls     — `ServiceBusProcessorOptions.MaxConcurrentCalls`
//                            equivalent. Default 5 (matches the existing
//                            sdap-jobs processor; safe for the expected
//                            mutation-event volume).
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.4 +
//            NFR-07; .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            projects/spaarke-platform-foundations-r3/notes/
//            operator-followup-task071.md.

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Configuration for the membership-change Service Bus subscription
/// consumer (R3 Part 1 Phase 2, task 084). Bound from the
/// <c>Membership:JunctionUpdater</c> appsettings section.
/// </summary>
public sealed class MembershipJunctionUpdaterOptions
{
    /// <summary>
    /// Configuration section name used by
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration.GetSection(string)"/>.
    /// Distinct sub-key from the publisher-side configuration (task 081
    /// uses <c>Membership:EventPublisher</c>) so the two consumer / publisher
    /// kill-switch flips remain independent.
    /// </summary>
    public const string SectionName = "Membership:JunctionUpdater";

    /// <summary>
    /// Master kill switch. <c>false</c> by default until the operator
    /// completes the task 071 topic deploy. When <c>false</c>, the DI
    /// module registers <c>NullMembershipJunctionUpdaterHost</c> in place
    /// of the real host — no Service Bus client is started, no
    /// subscription is created, no upserts run.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Service Bus topic name on which membership-change events are
    /// published. Defaults to the D3-resolved name <c>sprk-membership-changes</c>
    /// (spec FR-2P2.3 + task 071 Bicep).
    /// </summary>
    public string TopicName { get; set; } = "sprk-membership-changes";

    /// <summary>
    /// Service Bus subscription name this consumer reads from. Defaults
    /// to <c>recon-junction-updater</c> (task 071 Bicep). Other
    /// subscriptions on the same topic (e.g., future cache warmers,
    /// Teams notifiers) get their own hosted-service consumers.
    /// </summary>
    public string SubscriptionName { get; set; } = "recon-junction-updater";

    /// <summary>
    /// Fully-qualified Service Bus namespace (e.g.,
    /// <c>spaarkesb-dev.servicebus.windows.net</c>). Empty by default;
    /// operator populates alongside flipping <see cref="Enabled"/> to
    /// <c>true</c>. The host uses <c>DefaultAzureCredential</c> against
    /// this FQDN (ADR-028 canonical outbound auth).
    /// </summary>
    public string ServiceBusNamespace { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Azure.Messaging.ServiceBus.ServiceBusProcessorOptions.MaxConcurrentCalls"/>.
    /// Caps in-flight message processing. Default 5 — matches the
    /// existing <c>ServiceBusJobProcessor</c> default and is safe for the
    /// expected mutation-event volume.
    /// </summary>
    public int MaxConcurrentCalls { get; set; } = 5;
}
