using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>
/// Reconciles one thread's ACS membership to its Dataverse-derived authorized set (FR-07). Testing seam
/// (ADR-010) so the job's outcome/DLQ mapping can be unit-tested against a mock.
/// </summary>
public interface IMembershipReconciler
{
    /// <summary>See <see cref="MembershipReconciler.ReconcileAsync"/>.</summary>
    Task<MembershipReconcileResult> ReconcileAsync(
        Guid threadId,
        MembershipReconcileTrigger trigger,
        string actor,
        string? correlationId,
        CancellationToken ct = default);
}

/// <summary>
/// Reconciles ACS thread membership to the Dataverse-derived authorized set (FR-07). ACS membership is a
/// PROJECTION of Dataverse-derived access — never a second ACL. The reconcile:
/// <c>desired = derive(thread)</c> mapped to ACS <c>communicationUserId</c> MRIs;
/// <c>current = ACS participants</c>; <c>Add = desired \ current</c>; <c>Remove = current \ desired</c>.
/// </summary>
/// <remarks>
/// <para><b>Projection-never-exceeds invariant (the load-bearing security property).</b> The plan only ever
/// ADDs MRIs drawn from <c>desired</c> and REMOVEs MRIs NOT in <c>desired</c>. The resulting ACS set is
/// therefore exactly <c>(current ∩ desired) ∪ (desired \ current) = desired</c> — it can never exceed the
/// Dataverse-derived set. A defensive guard asserts <c>toAdd ⊆ desired</c> before any mutation and refuses to
/// proceed otherwise (fail-closed against a future coding regression).</para>
/// <para><b>Idempotent.</b> Reconciling an already-consistent thread computes empty add/remove sets and issues
/// zero ACS calls. The ACS add/remove ops are themselves idempotent (task 011), so a retry is always safe.</para>
/// <para><b>Best-effort / non-fatal (NFR-02).</b> The reconciler is invoked ONLY from the background job /
/// sweep — never inline in the send or inbound-capture path. It may throw (ACS 429, Dataverse transient); the
/// job maps that to a retry/DLQ outcome. A reconcile failure never fails a send or a capture; the periodic
/// sweep repairs any residual drift.</para>
/// </remarks>
public sealed class MembershipReconciler : IMembershipReconciler
{
    private const string ChannelRefEntity = "sprk_communicationchannelref";

    private readonly IThreadMembershipDerivationService _derivation;
    private readonly IAcsIdentityService _identity;
    private readonly IAcsThreadService _acsThread;
    private readonly IGenericEntityService _entityService;
    private readonly IMembershipReconcileAuditSink _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MembershipReconciler> _logger;

    public MembershipReconciler(
        IThreadMembershipDerivationService derivation,
        IAcsIdentityService identity,
        IAcsThreadService acsThread,
        IGenericEntityService entityService,
        IMembershipReconcileAuditSink audit,
        TimeProvider timeProvider,
        ILogger<MembershipReconciler> logger)
    {
        _derivation = derivation ?? throw new ArgumentNullException(nameof(derivation));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _acsThread = acsThread ?? throw new ArgumentNullException(nameof(acsThread));
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reconciles one thread's ACS membership to its Dataverse-derived authorized set. Throws on a transient
    /// ACS/Dataverse failure (the caller — the job — maps that to retry/DLQ). A thread with no ACS chat thread
    /// yet is a successful no-op.
    /// </summary>
    public async Task<MembershipReconcileResult> ReconcileAsync(
        Guid threadId,
        MembershipReconcileTrigger trigger,
        string actor,
        string? correlationId,
        CancellationToken ct = default)
    {
        var chatThreadId = await ReadAcsChatThreadIdAsync(threadId, ct);
        if (string.IsNullOrWhiteSpace(chatThreadId))
        {
            _logger.LogInformation(
                "Thread {ThreadId} has no ACS chat thread yet; nothing to reconcile (no-op).", threadId);
            return MembershipReconcileResult.NoAcsThread(threadId);
        }

        // ── desired = Dataverse-derived authorized set, mapped to ACS MRIs ──
        var derived = await _derivation.DeriveAuthorizedSetAsync(threadId, ct);

        var desiredByMri = new Dictionary<string, AuthorizedParticipant>(StringComparer.Ordinal);
        foreach (var authorized in derived.Participants)
        {
            var mapping = await _identity.EnsureIdentityAsync(authorized.Participant, ct);
            if (!string.IsNullOrWhiteSpace(mapping.CommunicationUserId))
                desiredByMri[mapping.CommunicationUserId] = authorized;
        }

        var desired = new HashSet<string>(desiredByMri.Keys, StringComparer.Ordinal);

        // ── current = ACS participants ──
        var current = new HashSet<string>(await _acsThread.ListParticipantsAsync(chatThreadId, ct), StringComparer.Ordinal);

        // ── plan: Add (desired \ current), Remove (current \ desired) ──
        var toAdd = desired.Except(current).ToList();
        var toRemove = current.Except(desired).ToList();

        // ── INVARIANT GUARD: ACS membership must NEVER exceed the Dataverse-derived set. ──
        // Every planned add MUST be a member of `desired`; if not, refuse to mutate (fail-closed). This can
        // only fire on a coding regression — the plan is derived from `desired` — but the assertion is the
        // security backstop the whole projection model rests on.
        var overreach = toAdd.Where(mri => !desired.Contains(mri)).ToList();
        if (overreach.Count > 0)
        {
            throw new InvalidOperationException(
                $"Membership reconcile for thread {threadId} planned to add {overreach.Count} participant(s) " +
                "NOT in the Dataverse-derived authorized set — refusing to over-expose the ACS thread " +
                "(projection invariant: ACS membership ⊆ Dataverse-derived access).");
        }

        // Security-first ordering: REVOKE over-exposure before granting new access.
        foreach (var mri in toRemove)
        {
            await _acsThread.RemoveParticipantAsync(chatThreadId, mri, ct);
            await _audit.RecordAsync(new MembershipReconcileAuditEntry
            {
                ThreadId = threadId,
                ChatThreadId = chatThreadId,
                Action = MembershipChangeAction.Removed,
                CommunicationUserId = mri,
                Participant = null, // an ACS-only extra — no longer in the Dataverse-derived set
                Trigger = trigger,
                Reason = null,
                Actor = actor,
                TimestampUtc = _timeProvider.GetUtcNow(),
                CorrelationId = correlationId,
            }, ct);
        }

        if (toAdd.Count > 0)
        {
            await _acsThread.AddParticipantsAsync(chatThreadId, toAdd, ct);
            foreach (var mri in toAdd)
            {
                var provenance = desiredByMri.TryGetValue(mri, out var ap) ? ap : null;
                await _audit.RecordAsync(new MembershipReconcileAuditEntry
                {
                    ThreadId = threadId,
                    ChatThreadId = chatThreadId,
                    Action = MembershipChangeAction.Added,
                    CommunicationUserId = mri,
                    Participant = provenance?.Participant,
                    Trigger = trigger,
                    Reason = provenance?.Reason,
                    Actor = actor,
                    TimestampUtc = _timeProvider.GetUtcNow(),
                    CorrelationId = correlationId,
                }, ct);
            }
        }

        _logger.LogInformation(
            "Reconciled thread {ThreadId} (chat {ChatThreadId}) trigger={Trigger}: desired={Desired}, current={Current}, added={Added}, removed={Removed}.",
            threadId, chatThreadId, trigger, desired.Count, current.Count, toAdd.Count, toRemove.Count);

        return new MembershipReconcileResult
        {
            ThreadId = threadId,
            ChatThreadId = chatThreadId,
            DesiredCount = desired.Count,
            CurrentCount = current.Count,
            Added = toAdd.Count,
            Removed = toRemove.Count,
        };
    }

    /// <summary>
    /// Reads the thread's ACS <c>ChatThreadId</c> from the <c>sprk_communicationchannelref</c> child row for the
    /// Message channel (the canonical home of the external ref per task 004 schema). Returns null when the
    /// thread has no messaging channel-ref yet.
    /// </summary>
    private async Task<string?> ReadAcsChatThreadIdAsync(Guid threadId, CancellationToken ct)
    {
        // A transient read failure PROPAGATES so the job retries; a thread with no Message channel-ref simply
        // returns an empty result (→ null → a safe no-op, not an error).
        var query = new QueryExpression(ChannelRefEntity)
        {
            ColumnSet = new ColumnSet("sprk_externalref", "sprk_channeltype"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sprk_thread", ConditionOperator.Equal, threadId),
                    new ConditionExpression("sprk_channeltype", ConditionOperator.Equal, (int)CommunicationType.Message),
                },
            },
            TopCount = 1,
        };

        var result = await _entityService.RetrieveMultipleAsync(query, ct);
        return result.Entities
            .Select(e => e.GetAttributeValue<string>("sprk_externalref"))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }
}

/// <summary>Outcome of a single-thread reconcile (observability).</summary>
public sealed record MembershipReconcileResult
{
    public required Guid ThreadId { get; init; }
    public string? ChatThreadId { get; init; }
    public int DesiredCount { get; init; }
    public int CurrentCount { get; init; }
    public int Added { get; init; }
    public int Removed { get; init; }

    /// <summary>True when the thread has no ACS chat thread yet — a successful no-op.</summary>
    public bool SkippedNoAcsThread { get; init; }

    public static MembershipReconcileResult NoAcsThread(Guid threadId) => new()
    {
        ThreadId = threadId,
        SkippedNoAcsThread = true,
    };
}
