namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>What caused a reconcile to be enqueued — recorded on every audit entry (FR-07).</summary>
public enum MembershipReconcileTrigger
{
    /// <summary>A Dataverse record-access change on the thread's anchor (owner/assignment edited).</summary>
    RecordAccessChanged,

    /// <summary>The thread's privacy state was flipped Open↔Private (task 042).</summary>
    PrivacySwitched,

    /// <summary>A thread's explicit participant list / overlay grant was edited (tasks 043/042).</summary>
    ParticipantEdited,

    /// <summary>The periodic eventual-consistency sweep (design §8.4 safety net).</summary>
    PeriodicSweep,
}

/// <summary>
/// Payload of a <c>MembershipReconcile</c> job (messaging-communication-app-r1 task 041 / FR-07). Carried on
/// the EXISTING ADR-004 Service Bus job contract — reconcile is background work, never inline in send/capture
/// (NFR-02). Idempotent: reconciling an already-consistent thread is a no-op.
/// </summary>
public sealed record MembershipReconcileJobPayload
{
    /// <summary>The <c>sprk_communicationthread</c> to reconcile.</summary>
    public Guid ThreadId { get; init; }

    /// <summary>Why the reconcile was enqueued (drives the audit entry + observability).</summary>
    public MembershipReconcileTrigger Trigger { get; init; }

    /// <summary>Actor that caused the change (Dataverse systemuserid / upn), or "system" for the sweep.</summary>
    public string Actor { get; init; } = "system";

    /// <summary>Job type constant — routed by <c>ServiceBusJobProcessor</c> on the shared <c>sdap-jobs</c> queue.</summary>
    public const string JobType = "MembershipReconcile";
}
