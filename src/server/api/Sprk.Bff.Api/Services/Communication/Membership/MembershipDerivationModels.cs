using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Membership;

// messaging-communication-app-r1 task 041 (FR-07) — membership derivation models.
// The "authorized participant set" is the SINGLE shared access contract that both the WRITE side
// (this task's ACS reconcile) and the READ side (task 042's BFF query-filter) compute from Dataverse.
// They MUST agree: authorized = MembershipResolver(anchor) ∪ activeOverlayGrants (design §5 / spike 002).

/// <summary>
/// A thread's topology + privacy posture, read from <c>sprk_communicationthread</c>. Drives which
/// derivation path applies (open → ADR-034 reverse membership; direct → explicit two-party list;
/// private → derived ∪ overlay grants).
/// </summary>
public sealed record ThreadMembershipContext
{
    public required Guid ThreadId { get; init; }

    /// <summary>Record-Anchored (open) vs Direct (1:1). Maps <c>sprk_threadtype</c>.</summary>
    public required ThreadTopology Topology { get; init; }

    /// <summary>Open vs Private. Maps <c>sprk_privacystate</c>.</summary>
    public required ThreadPrivacyState Privacy { get; init; }

    /// <summary>ADR-024 regarding anchor (denormalized pointer). Null for a Direct thread with no regarding.</summary>
    public string? AnchorRecordId { get; init; }

    /// <summary>ADR-024 regarding anchor entity logical name (e.g. <c>sprk_matter</c>).</summary>
    public string? AnchorRecordType { get; init; }

    public bool HasAnchor => !string.IsNullOrWhiteSpace(AnchorRecordId) && !string.IsNullOrWhiteSpace(AnchorRecordType);
}

/// <summary>Thread topology — mirrors <c>sprk_threadtype</c> (task 004 schema).</summary>
public enum ThreadTopology
{
    /// <summary>Record-Anchored (open) — membership derives from ADR-034 over the anchor record.</summary>
    RecordAnchored = 100000000,

    /// <summary>Direct 1:1 — membership is the explicit two-participant list (task 043).</summary>
    Direct = 100000001,
}

/// <summary>Thread privacy state — mirrors <c>sprk_privacystate</c> (task 004 schema).</summary>
public enum ThreadPrivacyState
{
    Open = 100000000,
    Private = 100000001,
}

/// <summary>Why a participant is in the authorized set — for audit provenance + debuggability.</summary>
public enum AuthorizationReason
{
    /// <summary>Derived from ADR-034 membership over the record-anchored thread's anchor.</summary>
    RecordMembership,

    /// <summary>The explicit two-participant list of a Direct 1:1 thread.</summary>
    DirectParticipant,

    /// <summary>An explicit per-record overlay grant on a Private thread (spike 002 option B).</summary>
    PrivateOverlayGrant,
}

/// <summary>One authorized participant + the reason it is authorized (provenance for audit).</summary>
public sealed record AuthorizedParticipant
{
    public required ParticipantReference Participant { get; init; }
    public required AuthorizationReason Reason { get; init; }
}

/// <summary>
/// The computed authorized participant set for one thread — the shared access contract (design §5).
/// De-duplicated by participant identity; the reconcile projects EXACTLY this set onto ACS and never
/// exceeds it (project-INVARIANT). Task 042's read-filter enforces the SAME set on reads.
/// </summary>
public sealed record ThreadAuthorizedSet
{
    public required Guid ThreadId { get; init; }

    /// <summary>Distinct authorized participants (systemuser / contact refs).</summary>
    public required IReadOnlyList<AuthorizedParticipant> Participants { get; init; }

    public static ThreadAuthorizedSet Empty(Guid threadId) => new()
    {
        ThreadId = threadId,
        Participants = Array.Empty<AuthorizedParticipant>(),
    };
}

/// <summary>
/// An explicit participant/grant row declared ON the thread — either a Direct-thread participant or a
/// Private-thread overlay grant (spike 002 option B). Task 043 (direct topology) + task 042 (private
/// grant schema) own the persisted shape; task 041 consumes it through
/// <see cref="IThreadExplicitParticipantReader"/> so the two sides share one contract.
/// </summary>
public sealed record ThreadExplicitParticipant
{
    public required ParticipantReference Participant { get; init; }

    /// <summary>Direct list membership vs a private overlay grant.</summary>
    public required ThreadExplicitParticipantKind Kind { get; init; }

    /// <summary>
    /// Point-forward effective moment for an overlay grant (design §5 / D-04: <c>createdon ≥ effectiveFrom</c>).
    /// Not used by the ACS reconcile (membership is present/absent, not per-message), but carried so the
    /// contract is identical to task 042's read-filter input. Null = effective immediately.
    /// </summary>
    public DateTimeOffset? EffectiveFrom { get; init; }
}

public enum ThreadExplicitParticipantKind
{
    DirectParticipant,
    PrivateOverlayGrant,
}
