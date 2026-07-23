using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Membership;

// messaging-communication-app-r1 task 041 (FR-07) — membership-change audit.
// Every ACS membership mutation the reconcile applies (add/remove) writes exactly one audit entry so the
// projection is fully traceable: WHO changed, on WHICH thread, WHY (trigger), and WHEN.

/// <summary>Whether the reconcile added or removed a participant from the ACS thread.</summary>
public enum MembershipChangeAction
{
    Added,
    Removed,
}

/// <summary>One membership-change audit record — emitted per ACS mutation the reconcile applies.</summary>
public sealed record MembershipReconcileAuditEntry
{
    public required Guid ThreadId { get; init; }

    /// <summary>The ACS <c>ChatThreadId</c> the mutation targeted.</summary>
    public required string ChatThreadId { get; init; }

    public required MembershipChangeAction Action { get; init; }

    /// <summary>The ACS <c>communicationUserId</c> (MRI) added/removed.</summary>
    public required string CommunicationUserId { get; init; }

    /// <summary>The Dataverse identity behind the MRI, when known (systemuser/contact). Null for an ACS-only extra participant being removed.</summary>
    public ParticipantReference? Participant { get; init; }

    /// <summary>Why the reconcile ran (record-access change / privacy switch / participant edit / periodic sweep).</summary>
    public required MembershipReconcileTrigger Trigger { get; init; }

    /// <summary>Provenance for an add (record membership / direct / overlay grant); null for a remove.</summary>
    public AuthorizationReason? Reason { get; init; }

    /// <summary>Actor that caused the change, when the trigger carried one (e.g. the user who flipped privacy). "system" for the sweep.</summary>
    public required string Actor { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Correlation id threading the reconcile job through logs.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Sink for membership-change audit entries. Kept behind a seam (ADR-010) so the audit obligation is a
/// first-class, independently testable requirement (FR-07: "audit entry per change") rather than an
/// incidental log line. The default <see cref="LoggingMembershipReconcileAuditSink"/> writes structured logs;
/// a future task may add a durable sink (e.g. Cosmos append-only, mirroring <c>IAuditLogService</c>) behind
/// the same seam with no reconcile change.
/// </summary>
public interface IMembershipReconcileAuditSink
{
    /// <summary>Records one membership-change audit entry. MUST NOT throw — auditing is best-effort (NFR-02).</summary>
    Task RecordAsync(MembershipReconcileAuditEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Default audit sink: writes one structured, queryable log entry per membership change. Never throws — an
/// audit-write failure must not fail the reconcile (NFR-02); it is logged and swallowed.
/// </summary>
public sealed class LoggingMembershipReconcileAuditSink : IMembershipReconcileAuditSink
{
    private readonly ILogger<LoggingMembershipReconcileAuditSink> _logger;

    public LoggingMembershipReconcileAuditSink(ILogger<LoggingMembershipReconcileAuditSink> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task RecordAsync(MembershipReconcileAuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation(
                "MEMBERSHIP-AUDIT {Action} thread={ThreadId} chatThread={ChatThreadId} participant={Participant} mri={Mri} " +
                "reason={Reason} trigger={Trigger} actor={Actor} at={Timestamp} correlation={CorrelationId}",
                entry.Action,
                entry.ThreadId,
                entry.ChatThreadId,
                entry.Participant is null ? "(acs-only)" : $"{entry.Participant.EntityLogicalName}:{entry.Participant.RecordId}",
                entry.CommunicationUserId,
                entry.Reason?.ToString() ?? "-",
                entry.Trigger,
                entry.Actor,
                entry.TimestampUtc,
                entry.CorrelationId ?? "-");
        }
        catch (Exception ex)
        {
            // Best-effort (NFR-02): never let an audit-write failure fail the reconcile.
            _logger.LogWarning(ex, "Failed to write membership audit entry for thread {ThreadId}.", entry.ThreadId);
        }

        return Task.CompletedTask;
    }
}
