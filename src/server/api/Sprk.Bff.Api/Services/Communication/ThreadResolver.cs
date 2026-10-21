using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Direction-symmetric, channel-agnostic implementation of <see cref="IThreadResolver"/> (FR-06). Dispatches
/// to a per-channel <see cref="IThreadKeyStrategy"/> (keyed by <see cref="CommunicationType"/>, mirroring
/// <see cref="Channels.CommunicationChannelDispatcher"/>) to JOIN an existing <c>sprk_communicationthread</c>
/// or CREATE a new one, then stamps the <c>sprk_communicationthread</c> lookup on the message. Wholly
/// best-effort / non-fatal (NFR-02): any failure is logged and swallowed — the caller's send/capture path
/// never fails because of thread resolution.
/// </summary>
public sealed class ThreadResolver : IThreadResolver
{
    // sprk_communicationthread choice integers (task 004 schema spec).
    private const int ThreadTypeRecordAnchored = 100000000;
    private const int ThreadTypeDirect = 100000001;
    private const int PrivacyStateOpen = 100000000;

    // FR-09 auto-threading ladder (task 070). The task-002 boolean marker distinguishes an auto-created
    // catch-all (per-record default OR per-user master) from a normal conversation thread, and — together
    // with the denormalized regarding pointer — is the idempotent find-or-create key so neither the
    // per-record default nor the per-user master is ever duplicated.
    private const string DefaultThreadMarkerField = "sprk_isdefaultthread";

    // Tier-3 (per-user master) reuses the SAME denormalized regarding-pointer fields as the idempotency
    // key, stamped with the owning user: (sprk_regardingrecordtype = "systemuser", sprk_regardingrecordid =
    // ownerId). "systemuser" is NOT an ADR-024 regarding entity (not in RegardingFieldMap), so a master
    // thread never collides with a record-anchored default and never surfaces in any by-regarding read
    // (task 010/011 query the 11 typed regarding lookups, none of which a master carries). This is NOT a
    // second regarding mechanism (ADR-046) — it is the existing denormalized pointer doubling as the
    // per-user catch-all key.
    private const string MasterThreadOwnerKeyType = "systemuser";

    // FR-07 naming re-derive (task 071). Boolean marker (task-002 schema): true/missing = Auto (re-derive
    // on regarding change), false = Edited (a user renamed the thread — preserve it). Stamped explicitly on
    // every CREATE path below rather than relying solely on the Dataverse column default.
    private const string NameIsAutoDerivedField = "sprk_nameisautoderived";

    // FR-24 pin/favorite (task 040 schema / task 041 write). Boolean column added live to spaarkedev1 by task 040;
    // pre-existing rows read back null (DefaultValue does not backfill), normalized to false by the read side
    // (CommunicationThreadReadService). This write path only ever sets an explicit true/false.
    private const string PinnedField = "sprk_ispinned";

    // FR-17 participant roll-up (task 004). Names a RECORD-LESS thread (no ADR-024 regarding anchor) from the
    // people/addresses in its messages, via the MESSAGE-grain sprk_communicationparticipant junction (R2 tasks
    // 003/050) — REUSE of the existing ADR-024/ADR-048 participant index, NOT a second participant store. Two
    // bounded queries (the thread's messages, then those messages' participants) keep the read O(1) in message
    // count (NFR-07 — no per-row fan-out).
    private const string ParticipantEntity = "sprk_communicationparticipant";
    private const string ParticipantMessageLookup = "sprk_communication"; // junction → sprk_communication (message)
    private const string MessageThreadLookup = "sprk_communicationthread"; // sprk_communication → thread
    private const int MaxRollupMessageScan = 200;      // bound the thread's message scan (NFR-07)
    private const int MaxRollupParticipantScan = 500;  // bound the participant scan across those messages
    private const int MaxNamesInRollup = 3;            // render "Alice, Bob, Carol, +N"

    private readonly IGenericEntityService _entityService;
    private readonly IReadOnlyDictionary<CommunicationType, IThreadKeyStrategy> _strategies;
    private readonly ILogger<ThreadResolver> _logger;

    /// <summary>
    /// unified-access-control-r2 task 146, owner decision S6 (round 3, accepted): a RECORD thread — one anchored to a
    /// regarding record — is a child of that record, owned by its team (the named Secure team for a secure record, with
    /// sharee access mirrored by task 149), and a message that JOINS a record thread is filed under the thread's record.
    /// Direct threads and the per-user MASTER thread carry no regarding and stay as they were (per-participant /
    /// per-user — the constraint's per-user waiver and escalation E2).
    /// </summary>
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownership;

    /// <summary>
    /// Task 129: the per-message read grant a filing reconcile issues for the thread the message now sits in (#1629's
    /// join→grant sequence). Optional, like the other call sites' (<see cref="CommunicationService"/>,
    /// <see cref="Channels.MessagingIngestor"/>): without it the reconcile still moves the thread, and the message stays
    /// readable by its owner team.
    /// </summary>
    private readonly Access.IDirectThreadAccessService? _directThreadAccess;

    /// <summary>
    /// Task 129 F1-b: the Secure Record business-unit and owner-team names, to tell whether a thread is owned by the Secure
    /// team (the same detection <see cref="Access.DirectThreadAccessService"/> uses). Without it a team-owned thread is
    /// treated as possibly secure (fail closed).
    /// </summary>
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// #2091: where a JOIN that files a message under a record thread gets the scoped
    /// <see cref="Sprk.Bff.Api.Services.Access.SecureChildReconciler"/> for the ONE step after a re-file (<see cref="JoinThreadAsync"/>).
    /// A scope factory, not the reconciler: this resolver is a singleton the reconciler itself depends on. Optional so the test
    /// compositions keep compiling; the host passes the root's.
    /// </summary>
    private readonly IServiceScopeFactory? _scopeFactory;

    public ThreadResolver(
        IEnumerable<IThreadKeyStrategy> strategies,
        IGenericEntityService entityService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        ILogger<ThreadResolver> logger,
        Access.IDirectThreadAccessService? directThreadAccess = null,
        IConfiguration? configuration = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _entityService = entityService;
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        // Fail fast on a duplicate channel registration (mirrors CommunicationChannelDispatcher's guard).
        _strategies = strategies.ToDictionary(s => s.SupportedType);
        _logger = logger;
        _directThreadAccess = directThreadAccess;
    }

    public async Task<Guid?> ResolveAndAssignThreadAsync(ThreadResolutionRequest request, CancellationToken ct = default) =>
        (await ResolveAndAssignAsync(request, ct))?.ThreadId;

    /// <inheritdoc />
    public Task<ThreadAssignment?> ResolveAndAssignAsync(ThreadResolutionRequest request, CancellationToken ct = default) =>
        ResolveAndAssignCoreAsync(request, EveryJoinRunsTheStep, ct);

    /// <summary>A capture or send path's join: every join that files the message under a record thread runs the step after a re-file.</summary>
    private static readonly Func<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent, bool> EveryJoinRunsTheStep = static _ => true;

    /// <summary>A join inside the step after a re-file (<see cref="FilingReconcileHost.AfterRefileStep"/>): the step runs after it.</summary>
    private static readonly Func<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent, bool> NoJoinRunsTheStep = static _ => false;

    /// <param name="joinRunsAfterRefileFor">#2091: whether a JOIN that files the message under the record thread's record (the
    /// argument) runs the step after a re-file (<see cref="JoinThreadAsync"/>). Every join on the capture and send paths does;
    /// inside <see cref="ReconcileThreadForFilingAsync(Guid, FilingReconcileHost, CancellationToken)"/> only a standalone
    /// reconcile's join under a record the email is not filed to does (#2110).</param>
    private async Task<ThreadAssignment?> ResolveAndAssignCoreAsync(
        ThreadResolutionRequest request,
        Func<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent, bool> joinRunsAfterRefileFor,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (!_strategies.TryGetValue(request.ChannelType, out var strategy))
            {
                _logger.LogDebug(
                    "No thread-key strategy for channel {ChannelType}; skipping thread resolution | CommunicationId: {CommunicationId}",
                    request.ChannelType, request.CommunicationId);
                return null;
            }

            var resolution = await strategy.ResolveAsync(request, ct);

            // JOIN — an existing thread was found; assign it and we're done. Joining a RECORD thread files the message
            // under the thread's record (S6, task 146): its owner is re-derived before the assignment is written, and the
            // step after a re-file follows (#2091).
            if (resolution.ExistingThreadId is { } existing)
            {
                var joined = await JoinThreadAsync(
                    _entityService, _ownership, _scopeFactory, _logger, request.CommunicationId, existing,
                    token => AssignThreadAsync(request.CommunicationId, existing, token), joinRunsAfterRefileFor, ct);
                _logger.LogInformation(
                    "Joined existing thread {ThreadId} | CommunicationId: {CommunicationId}, Channel: {ChannelType}",
                    existing, request.CommunicationId, request.ChannelType);
                return joined;
            }

            // FALL-THROUGH (Tier 1 miss) — the channel key produced no groupable conversation (e.g. an
            // outbound chat with no ACS thread id, or a message with neither subject nor ancestry). Instead
            // of orphaning the message (R1 returned null here), descend the FR-09 auto-threading ladder so it
            // NEVER lands without a thread: Tier 2 = the per-record default (when the message has a resolved
            // ADR-024 regarding), else Tier 3 = the per-user master catch-all.
            if (!resolution.CreateWhenAbsent)
            {
                return await ResolveViaFallbackLadderAsync(request, ct);
            }

            // CREATE — a thin thread anchored to the message's resolved regarding (ADR-024 reuse).
            var (threadId, anchor) = await CreateThreadAsync(request, ct);
            await strategy.OnThreadCreatedAsync(threadId, request, ct);
            await AssignThreadAsync(request.CommunicationId, threadId, ct);
            _logger.LogInformation(
                "Created thread {ThreadId} | CommunicationId: {CommunicationId}, Channel: {ChannelType}",
                threadId, request.CommunicationId, request.ChannelType);
            // #1629: the grant audience is what this create wrote — the message's own regarding, or Direct.
            return AssignmentFor(threadId, anchor) ?? ThreadAssignment.Direct(threadId);
        }
        catch (Exception ex)
        {
            // NFR-02: best-effort / non-fatal — never fail the send or inbound-capture path.
            _logger.LogWarning(
                ex,
                "Thread resolution failed (non-fatal) | CommunicationId: {CommunicationId}, Channel: {ChannelType}",
                request.CommunicationId, request.ChannelType);
            return null;
        }
    }

    private async Task<(Guid ThreadId, RegardingAnchor? Anchor)> CreateThreadAsync(ThreadResolutionRequest request, CancellationToken ct)
    {
        var anchor = await ReadRegardingAnchorAsync(request.CommunicationId, ct);

        // Task 146 / S6: a record thread is owned by its record's team. A Direct thread (no anchor) keeps its creator.
        var recordOwner = await ResolveRecordThreadOwnerAsync(anchor, ct);

        var thread = new DataverseEntity("sprk_communicationthread")
        {
            ["sprk_name"] = TruncateTo(BuildTopic(request, anchor), 200),
            // Record-Anchored when the message resolved a regarding record; else a Direct 1:1 conversation.
            ["sprk_threadtype"] = new OptionSetValue(anchor is not null ? ThreadTypeRecordAnchored : ThreadTypeDirect),
            // Threads start Open; the point-forward privacy flip (Private) is task 042, not this seam.
            ["sprk_privacystate"] = new OptionSetValue(PrivacyStateOpen),
            // FR-07 (task 071) — stamp Auto explicitly so ReDeriveThreadNameAsync re-derives on a later
            // regarding change, until a user renames the thread (marker flips to Edited).
            [NameIsAutoDerivedField] = true,
        };

        // Anchor = REUSE the ADR-024 regarding family (NOT a second mechanism / NOT new sprk_anchor* fields).
        if (anchor is not null)
        {
            thread["sprk_regardingrecordid"] = TruncateTo(anchor.RecordId, 100);
            // TYPED ADR-024 regarding lookup — the field the by-regarding read filters on. The thread has NO
            // 'sprk_regardingrecordtype' text attribute (RB R3 UAT 2026-07-24); the typed lookup carries the anchor.
            SetTypedRegardingLookup(thread, anchor.RecordType, anchor.RecordId);
            if (!string.IsNullOrWhiteSpace(anchor.RecordName))
                thread["sprk_regardingrecordname"] = TruncateTo(anchor.RecordName, 400);
            if (!string.IsNullOrWhiteSpace(anchor.RecordUrl))
                thread["sprk_regardingrecordurl"] = TruncateTo(anchor.RecordUrl, 400);
        }

        if (recordOwner is { } ownerTeamId)
            thread["ownerid"] = new EntityReference("team", ownerTeamId);

        return (await _entityService.CreateAsync(thread, ct), anchor);
    }

    /// <summary>
    /// The grant audience of a record thread anchored to <paramref name="anchor"/> (#1629): its members; <c>null</c> when
    /// the anchor names no record (the caller decides the fallback).
    /// </summary>
    private static ThreadAssignment? AssignmentFor(Guid threadId, RegardingAnchor? anchor) =>
        anchor is not null && Guid.TryParse(anchor.RecordId, out var recordId) && recordId != Guid.Empty
            ? ThreadAssignment.Anchored(threadId, anchor.RecordType, recordId)
            : null;

    // ── Ownership (unified-access-control-r2 task 146, owner decision S6) ────────────────────────────────────────

    /// <summary>
    /// The typed regarding lookups a THREAD actually carries: <see cref="RegardingFieldMap"/>'s set minus
    /// <c>sprk_regardingreportcard</c>, which does not exist on <c>sprk_communicationthread</c> (verified against its
    /// metadata 2026-08-03 — the same exclusion <c>CommunicationThreadReadService.ThreadRegardingFields</c> makes).
    /// Selecting a column the table lacks faults the whole read.
    /// </summary>
    internal static readonly string[] ThreadRegardingColumns = RegardingFieldMap.All
        .Where(x => x.RegardingField != "sprk_regardingreportcard")
        .Select(x => x.RegardingField)
        .ToArray();

    /// <summary>The message's thread lookup — the column a JOIN writes (and a failed owner assignment puts back).</summary>
    internal const string ThreadLookupOnCommunication = "sprk_communicationthread";

    /// <summary>
    /// The owner of a RECORD thread anchored to <paramref name="anchor"/>: its record's team via the ONE resolver (the
    /// named Secure team for a secure record). <c>null</c> for a thread with no anchor (Direct / per-user master), which
    /// keeps its creator. A REFUSAL throws — inside the resolution paths this class's best-effort catch leaves the
    /// message unthreaded, so no record thread is ever created with an ordinary owner for a secure record.
    /// </summary>
    private async Task<Guid?> ResolveRecordThreadOwnerAsync(RegardingAnchor? anchor, CancellationToken ct) =>
        (await ResolveRecordThreadOwnershipAsync(anchor, ct))?.OwningTeamId;

    /// <summary>
    /// <see cref="ResolveRecordThreadOwnerAsync"/>'s full answer — also whether the team is the Secure Record owner team
    /// (<see cref="Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution.IsSecureOwner"/>, task 129 F1-b). <c>null</c> for no
    /// anchor or a party anchor; a refusal throws.
    /// </summary>
    private async Task<Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution?> ResolveRecordThreadOwnershipAsync(
        RegardingAnchor? anchor, CancellationToken ct)
    {
        if (anchor is null || !Guid.TryParse(anchor.RecordId, out var recordId) || recordId == Guid.Empty
            || !Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsOwnershipParent(anchor.RecordType))
            return null; // no anchor, or a party anchor (account / contact / organization): keeps its creator

        var owner = await _ownership.ResolveOwnerAsync(
            new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext
            {
                TargetEntityLogicalName = anchor.RecordType,
                TargetRecordId = recordId,
            },
            ct);

        if (!owner.IsOwned)
            throw new Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException("sprk_communicationthread", owner);

        return owner;
    }

    /// <summary>
    /// Assigns a message to a thread it JOINED. When the thread is a record thread, the message is filed under the
    /// thread's record (S6): a reparent — its owner is re-derived (secure-if-any over its own parents plus the thread's
    /// record) BEFORE the assignment is written, and reassigned when it moves. A REFUSAL leaves the message unthreaded
    /// (logged) rather than joining a secure record's thread while owned elsewhere. Direct / master threads (no
    /// regarding) are assigned as before.
    /// </summary>
    private Task<ThreadAssignment> AssignThreadReconcilingOwnerAsync(
        Guid communicationId, Guid threadId, Sprk.Bff.Api.Services.Access.SecureRemovalCaller? secureExitCaller,
        CancellationToken ct) =>
        AssignToThreadReconcilingOwnerAsync(
            _entityService, _ownership, communicationId, threadId,
            token => AssignThreadAsync(communicationId, threadId, token), secureExitCaller, ct);

    /// <summary>
    /// The ONE "a message joins an existing thread" ownership step, shared by this resolver's JOIN path and
    /// <see cref="CommunicationService"/>'s explicit respond-into-thread stamp (task 146, S6). Reads the thread's
    /// typed regarding; for a record thread anchored to an ownership parent, re-derives the message's owner over its
    /// own parents plus the thread's record BEFORE <paramref name="assign"/> runs, and reassigns it when it moves. A
    /// REFUSAL throws <see cref="Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException"/> without assigning.
    /// A thread with no ownership-parent anchor (Direct / master / party-regarding), or an unreadable thread row, is
    /// assigned as before.
    /// <para>#1629 F-C / F1: returns the message's read-grant audience from the SAME read of the thread that filed it —
    /// its typed anchor's members whenever the thread carries a typed regarding lookup (whatever its
    /// <c>sprk_threadtype</c> says: the message is filed under that anchor), Direct only for a Direct thread with NO typed
    /// lookup, nobody otherwise (or when the thread could not be read) — so the grant never re-reads a type or lookup the
    /// thread's owner can rewrite in between.</para>
    /// </summary>
    internal static Task<ThreadAssignment> AssignToThreadReconcilingOwnerAsync(
        IGenericEntityService entityService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Guid communicationId,
        Guid threadId,
        Func<CancellationToken, Task> assign,
        CancellationToken ct) =>
        AssignToThreadReconcilingOwnerAsync(entityService, ownership, communicationId, threadId, assign, secureExitCaller: null, ct);

    /// <summary>
    /// <see cref="AssignToThreadReconcilingOwnerAsync(IGenericEntityService, Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver, Guid, Guid, Func{CancellationToken, Task}, CancellationToken)"/>
    /// acting for <paramref name="secureExitCaller"/> (#2143 round 7, F1): the person whose re-file this join follows — its
    /// reparent asks that person's F3 rights for a move off a secure record's thread. <c>null</c>: nobody (refused).
    /// </summary>
    internal static async Task<ThreadAssignment> AssignToThreadReconcilingOwnerAsync(
        IGenericEntityService entityService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Guid communicationId,
        Guid threadId,
        Func<CancellationToken, Task> assign,
        Sprk.Bff.Api.Services.Access.SecureRemovalCaller? secureExitCaller,
        CancellationToken ct) =>
        (await JoinCoreAsync(entityService, ownership, communicationId, threadId, assign, beforeRefileWrite: null, secureExitCaller, ct)
            .ConfigureAwait(false)).Assignment;

    /// <summary>
    /// #2091: a message JOINS an existing thread (<see cref="AssignToThreadReconcilingOwnerAsync"/>), then — when the join
    /// filed it under the thread's record and wrote — the ONE step after a re-file,
    /// <see cref="Sprk.Bff.Api.Services.Access.SecureChildReconciler.AfterRefileAsync(string, Guid, Func{Task{bool}}, Sprk.Bff.Api.Services.Access.RefileThreadStep, CancellationToken)"/>
    /// with <see cref="Sprk.Bff.Api.Services.Access.RefileThreadStep.PlacedByWriter"/>: the message's own mirror, the pass over
    /// what is filed under it and its gate Decision Records (ontology 042b) — every step but the thread reconcile, which this
    /// join is. Shared by this resolver's JOIN and <see cref="CommunicationService"/>'s respond-into-thread.
    /// </summary>
    /// <remarks>
    /// <para>Whether the message was isolated is read immediately before the thread lookup is written (once more after a
    /// fault, <see cref="Sprk.Bff.Api.Services.Access.RefileIsolationRead"/>), and only on a reparent: a Direct, master or
    /// party-regarding thread files the message under nothing, so that join reads nothing and runs no step. A refusal writes
    /// nothing, reads nothing and runs nothing (it throws, as before). A faulted read still writes, and the step releases
    /// nothing — which a join never could: the message's owner is secure-if-any over its own parents plus the thread's record,
    /// and the resolver refuses a person-less move out of the Secure business unit (F3 with no caller), so a join never takes
    /// a message out of isolation. Refusing before the write would only leave a message already sent unthreaded.</para>
    /// <para>The reconciler is scoped and depends on this singleton resolver, so it comes from a scope of
    /// <paramref name="scopeFactory"/>, opened only for a reparent. A host without one leaves the message's sharing as it was
    /// and logs an error, as the playbook update node does.</para>
    /// </remarks>
    internal static Task<ThreadAssignment> JoinThreadAsync(
        IGenericEntityService entityService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        IServiceScopeFactory? scopeFactory,
        ILogger logger,
        Guid communicationId,
        Guid threadId,
        Func<CancellationToken, Task> assign,
        CancellationToken ct) =>
        JoinThreadAsync(entityService, ownership, scopeFactory, logger, communicationId, threadId, assign, EveryJoinRunsTheStep, ct);

    /// <summary>
    /// <see cref="JoinThreadAsync(IGenericEntityService, Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver, IServiceScopeFactory?, ILogger, Guid, Guid, Func{CancellationToken, Task}, CancellationToken)"/>,
    /// running the step only when <paramref name="runsStepFor"/> answers <c>true</c> for the record the join files the message
    /// under (#2110). A join it answers <c>false</c> for opens no scope, reads nothing and runs no step — as
    /// <see cref="AssignToThreadReconcilingOwnerAsync"/>.
    /// </summary>
    private static async Task<ThreadAssignment> JoinThreadAsync(
        IGenericEntityService entityService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        IServiceScopeFactory? scopeFactory,
        ILogger logger,
        Guid communicationId,
        Guid threadId,
        Func<CancellationToken, Task> assign,
        Func<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent, bool> runsStepFor,
        CancellationToken ct)
    {
        IServiceScope? scope = null;
        try
        {
            var runsStep = false;
            Sprk.Bff.Api.Services.Access.SecureChildReconciler? children = null;
            Func<Task<bool>>? wasIsolated = null;
            var (assignment, refiled) = await JoinCoreAsync(
                entityService, ownership, communicationId, threadId, assign,
                beforeRefileWrite: async (filedUnder, token) =>
                {
                    if (!runsStepFor(filedUnder))
                        return;

                    runsStep = true;
                    scope = scopeFactory?.CreateScope();
                    children = scope?.ServiceProvider.GetService<Sprk.Bff.Api.Services.Access.SecureChildReconciler>();
                    if (children is not null)
                    {
                        wasIsolated = await Sprk.Bff.Api.Services.Access.RefileIsolationRead
                            .BeforeWriteAsync(children, CommunicationTable, communicationId, token).ConfigureAwait(false);
                    }
                },
                secureExitCaller: null,
                ct).ConfigureAwait(false);

            if (!refiled || !runsStep)
                return assignment;

            if (children is null || wasIsolated is null)
            {
                logger.LogError(
                    "Communication {CommunicationId} joined thread {ThreadId} and was filed under its record, but no " +
                    "SecureChildReconciler is registered: its sharing and the rows filed under it were NOT reconciled after the " +
                    "move (#2091)", communicationId, threadId);
                return assignment;
            }

            // The write has landed: never cancelled, never thrown.
            await children.AfterRefileAsync(
                CommunicationTable, communicationId, wasIsolated,
                Sprk.Bff.Api.Services.Access.RefileThreadStep.PlacedByWriter, CancellationToken.None).ConfigureAwait(false);
            return assignment;
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private const string CommunicationTable = "sprk_communication";

    /// <summary>
    /// <see cref="AssignToThreadReconcilingOwnerAsync"/>'s body, also answering whether the join RE-FILED the message: the
    /// reparent ran (a record thread anchored to an ownership parent), wrote (<paramref name="assign"/> ran), and moved what
    /// it is filed under (<see cref="Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution.ParentsUnchanged"/> is false).
    /// <paramref name="beforeRefileWrite"/> runs only on that reparent, inside its write and immediately before
    /// <paramref name="assign"/>, told the record the join files the message under; a join that files the message under
    /// nothing never calls it.
    /// </summary>
    private static async Task<(ThreadAssignment Assignment, bool Refiled)> JoinCoreAsync(
        IGenericEntityService entityService,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Guid communicationId,
        Guid threadId,
        Func<CancellationToken, Task> assign,
        Func<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent, CancellationToken, Task>? beforeRefileWrite,
        Sprk.Bff.Api.Services.Access.SecureRemovalCaller? secureExitCaller,
        CancellationToken ct)
    {
        var threadRow = await entityService.RetrieveAsync(
            "sprk_communicationthread", threadId, ThreadRegardingColumns.Append("sprk_threadtype").ToArray(), ct);
        var anchor = threadRow is null ? null : ReadTypedRegardingAnchorFromThread(threadRow);
        // #1629 F1: the typed anchor wins over sprk_threadtype. A thread carrying a typed lookup files the message under
        // that anchor (the reparent below), so its audience is the anchor's members — never the thread owner's sharees,
        // which a user with Write + Share on the thread could otherwise point at a record's message. Direct applies only
        // to a thread with NO typed regarding lookup.
        var assignment = threadRow is null
            ? ThreadAssignment.NoGrant(threadId)
            : AssignmentFor(threadId, anchor)
              ?? (threadRow.GetAttributeValue<OptionSetValue>("sprk_threadtype")?.Value == ThreadTypeDirect
                  ? ThreadAssignment.Direct(threadId)
                  : ThreadAssignment.NoGrant(threadId));

        if (anchor is null || !Guid.TryParse(anchor.RecordId, out var recordId) || recordId == Guid.Empty
            || !Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsOwnershipParent(anchor.RecordType))
        {
            await assign(ct);
            return (assignment, false);
        }

        var wrote = false;
        var filedUnder = new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent(anchor.RecordType, recordId);
        var reparent = await ownership.ReparentAsync(
            new Sprk.Bff.Api.Services.Dataverse.RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = communicationId,
                // #2143 round 5 (F1): the thread lookup this join writes, WITH the row it writes, so the resolver reads it as
                // this call's own change and not another writer's (its lineage overlay). A lineage-only lookup decides no owner
                // and names nothing to put back; the put-back still comes from AttachColumns. Enforced by:
                // RecordOwnershipResolverTests.Reparent_WhenAnAttachColumnIsALineageOnlyLookupNotNamedInParentChanges_ThrowsArgumentException_BeforeAnyReadOrWrite.
                ParentChanges = new Dictionary<string, EntityReference?>
                {
                    [ThreadLookupOnCommunication] = new EntityReference("sprk_communicationthread", threadId),
                },
                // #2143 round 6: no InheritedParents — the resolver reads the thread this join names in ParentChanges and files
                // the message under the thread's own filing (RecordThreadFiling), the ONE source of a message's thread parents.
                // The anchor read above only gates this reparent, picks the audience and is told to beforeRefileWrite.
                // Task 146 b2: the column the JOIN writes — put back if the owner assignment that must follow it fails, so
                // the message is never left in a secure record's thread while owned elsewhere.
                AttachColumns = new[] { ThreadLookupOnCommunication },
                WhenUnfiled = Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.KeepCreator,
                // #2143 round 7 (F1): the person whose re-file this join follows, when it does (the filing reconcile's move
                // to the record's thread); a capture or send join acts for nobody — it never leaves a secure record.
                SecureExitCaller = secureExitCaller,
            },
            async token =>
            {
                if (beforeRefileWrite is not null)
                    await beforeRefileWrite(filedUnder, token);
                await assign(token);
                wrote = true;
            },
            ct);

        if (reparent.IsRefused)
        {
            throw new Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException("sprk_communication", reparent);
        }

        return (assignment, wrote && !reparent.ParentsUnchanged);
    }

    /// <summary>
    /// FR-09 auto-threading ladder — invoked only on the Tier-1 miss (the former SKIP→null path). Guarantees
    /// a non-null thread: Tier 2 (per-record default, ONE per regarding record, created lazily) when the
    /// message has a resolved ADR-024 regarding; otherwise Tier 3 (per-user master catch-all, ONE per owning
    /// user). Runs INSIDE the caller's NFR-02 try/catch, so any Dataverse failure here is caught upstream and
    /// returned as null (the message persists without a thread) rather than throwing to the send/capture path.
    /// </summary>
    private async Task<ThreadAssignment?> ResolveViaFallbackLadderAsync(ThreadResolutionRequest request, CancellationToken ct)
    {
        // Tier 2 — per-record default. REUSE the SAME ADR-024 regarding the resolver already reads for the
        // Tier-1 CREATE anchor (ReadRegardingAnchorAsync) — no second regarding mechanism (ADR-024/ADR-046).
        var anchor = await ReadRegardingAnchorAsync(request.CommunicationId, ct);
        if (anchor is not null)
        {
            var recordThreadId = await FindOrCreateDefaultThreadAsync(
                anchor.RecordType, anchor.RecordId, anchor, request.RequireOpenDefaultThread, ct);
            await AssignThreadAsync(request.CommunicationId, recordThreadId, ct);
            _logger.LogInformation(
                "Resolved per-record default thread {ThreadId} (Tier 2) | CommunicationId: {CommunicationId}, Regarding: {RecordType}:{RecordId}",
                recordThreadId, request.CommunicationId, anchor.RecordType, anchor.RecordId);
            // #1629 F-C: the grant goes to the members of the message's OWN regarding (which the find matched the thread
            // on), never to whatever the thread says by the time the grant runs.
            return AssignmentFor(recordThreadId, anchor) ?? ThreadAssignment.NoGrant(recordThreadId);
        }

        // Tier 3 — per-user master catch-all. No resolvable regarding, so key on the message's owning user.
        var ownerId = await ReadOwnerIdAsync(request.CommunicationId, ct);
        if (ownerId is not { } owner)
        {
            // Dataverse always stamps an owner, so this is an extreme edge (e.g. a mocked/partial record).
            // Best-effort (NFR-02): log and leave the message unthreaded rather than inventing a global bucket.
            _logger.LogWarning(
                "No regarding and no resolvable owner | CommunicationId: {CommunicationId}, Channel: {ChannelType}; message remains unthreaded",
                request.CommunicationId, request.ChannelType);
            return null;
        }

        var masterThreadId = await FindOrCreateDefaultThreadAsync(
            MasterThreadOwnerKeyType, owner.ToString(), anchor: null, request.RequireOpenDefaultThread, ct);
        await AssignThreadAsync(request.CommunicationId, masterThreadId, ct);
        _logger.LogInformation(
            "Resolved per-user master thread {ThreadId} (Tier 3) | CommunicationId: {CommunicationId}, Owner: {OwnerId}",
            masterThreadId, request.CommunicationId, owner);
        // #1629: the per-user master is a catch-all with no audience — its legitimate owner is the BFF's application
        // user, and its key is user-writable (#1637) — so it grants nobody.
        return ThreadAssignment.NoGrant(masterThreadId);
    }

    /// <summary>
    /// FR-07 marker-gated naming re-derive (task 071). See <see cref="IThreadResolver.ReDeriveThreadNameAsync"/>
    /// for the full contract. Reads the thread's CURRENT denormalized regarding
    /// (<c>sprk_regardingrecordtype/id/name/url</c> — the same fields <see cref="CreateThreadAsync"/> and
    /// <see cref="FindOrCreateDefaultThreadAsync"/> populate, and the same fields the RegardingResolver
    /// PCF's standard write payload updates on a regarding change), applies the master-thread guard, checks
    /// the naming-edited marker, and — only when Auto — recomputes <c>sprk_name</c> and writes it if changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Master-thread guard (070 flag, honored here):</b> the Tier-3 per-user master catch-all is keyed
    /// <c>(sprk_regardingrecordtype = "systemuser", sprk_regardingrecordid = ownerId)</c> and named
    /// <c>"Messages"</c> at create (<see cref="BuildDefaultThreadName"/>). <c>"systemuser"</c> is not an
    /// ADR-024 regarding entity, so this method short-circuits before attempting to resolve it as one — the
    /// master thread's name is never re-derived. The RegardingResolver PCF cannot write to the Text
    /// <c>sprk_regardingrecordtype</c> field either (it is not a lookup relationship — see task-002 note),
    /// so this guard stays reliable even after a PCF placement/edit attempt on the master thread.
    /// </para>
    /// <para>
    /// <b>Legacy-null-as-Auto:</b> Dataverse does not backfill a new column's default onto existing rows, so
    /// a thread created before the task-002 schema landed has no <c>sprk_nameisautoderived</c> value. A
    /// missing/null marker is treated the SAME as Auto (matching the schema's declared default) so
    /// pre-existing threads keep re-deriving until a user explicitly renames one.
    /// </para>
    /// <para>
    /// <b>⚠️ Trigger + marker-flip wiring is explicitly OUT OF SCOPE for this task</b> (root CLAUDE.md §6 —
    /// surfaced rather than guessed). Two events are meant to reach this capability and neither currently
    /// does, because both are client-side Dataverse writes that bypass the BFF: (1) a regarding change via
    /// the RegardingResolver PCF (<c>ResolverWriteHandler.applyRegardingSelection</c> /
    /// <c>clearRegarding</c> call <c>Xrm.WebApi.updateRecord</c> directly — no BFF call), which should
    /// invoke this method; and (2) a user editing <c>sprk_name</c> directly on the form, which should flip
    /// <c>sprk_nameisautoderived</c> to Edited. This task's tags (bff-api, pcf, communication — no
    /// dataverse-plugin tag) and outputs (no new endpoint, no new plugin file) scope it to building the
    /// tested BFF capability only. Recommended follow-on: a Dataverse plugin (Post-Operation, Update of
    /// <c>sprk_communicationthread</c>) registered on (a) the typed regarding lookups +
    /// <c>sprk_regardingrecordtype_ref</c> to invoke this method, and (b) <c>sprk_name</c> to detect a
    /// user-authored value diverging from what this method would derive and flip the marker to Edited.
    /// </para>
    /// </remarks>
    public async Task ReDeriveThreadNameAsync(Guid threadId, CancellationToken ct = default)
    {
        try
        {
            var columns = new List<string>
            {
                "sprk_name", NameIsAutoDerivedField, "sprk_threadtype", DefaultThreadMarkerField,
                "sprk_regardingrecordname", "sprk_regardingrecordurl",
            };
            // Task 129: the THREAD's typed lookups only — sprk_regardingreportcard does not exist on the thread, and selecting
            // it faulted the whole read (the same fix #1629 made to the membership derivation's thread read).
            columns.AddRange(ThreadRegardingColumns);
            var thread = await _entityService.RetrieveAsync(
                "sprk_communicationthread", threadId, columns.ToArray(), ct);

            // Master-thread guard — the Tier-3 per-user master is a DEFAULT thread of type Direct (keyed on the
            // owning user, not a resolvable record); its "Messages" name must never re-derive. Detected via the
            // default marker + threadtype now that there is no 'sprk_regardingrecordtype' text attribute to key on
            // (RB R3 UAT 2026-07-24).
            var isDefault = thread.GetAttributeValue<bool?>(DefaultThreadMarkerField) ?? false;
            var threadTypeValue = thread.GetAttributeValue<OptionSetValue>("sprk_threadtype")?.Value;
            if (isDefault && threadTypeValue == ThreadTypeDirect)
            {
                _logger.LogDebug(
                    "Skipping name re-derive for per-user master thread {ThreadId} — regarding key is the owning user, not a resolvable record",
                    threadId);
                return;
            }

            // Marker gate: Edited (explicit false) preserves the user's name; Auto (true) or missing/null
            // (legacy — see remarks) re-derives.
            var marker = thread.GetAttributeValue<bool?>(NameIsAutoDerivedField);
            if (marker == false)
            {
                _logger.LogDebug(
                    "Skipping name re-derive for thread {ThreadId} — naming-edited marker is Edited", threadId);
                return;
            }

            // Anchor from the TYPED ADR-024 lookups (authoritative) + the denormalized display name/url.
            var anchor = ReadTypedRegardingAnchorFromThread(thread);

            string newName;
            if (anchor is not null)
            {
                // Record-anchored: name from the current regarding's display name (unchanged).
                newName = TruncateTo(BuildTopicFromRegardingAnchor(anchor), 200);
            }
            else
            {
                // Record-LESS (no ADR-024 regarding anchor): FR-17 — roll up the thread's participants instead of
                // the generic "Conversation" fallback. The master-thread guard + Edited-marker gate above are
                // untouched, so this branch only runs for a non-master, Auto/legacy-null thread with no anchor.
                var participantName = await BuildParticipantRollupNameAsync(threadId, ct);
                newName = TruncateTo(participantName ?? "Conversation", 200);
            }
            var currentName = thread.GetAttributeValue<string>("sprk_name");
            if (string.Equals(newName, currentName, StringComparison.Ordinal))
                return; // No-op — avoid a needless write when the derived name hasn't actually changed.

            await _entityService.UpdateAsync(
                "sprk_communicationthread",
                threadId,
                new Dictionary<string, object> { ["sprk_name"] = newName },
                ct);
            _logger.LogInformation(
                "Re-derived thread name {ThreadId}: {NewName} (regarding: {RecordType}:{RecordId})",
                threadId, newName, anchor?.RecordType, anchor?.RecordId);
        }
        catch (Exception ex)
        {
            // NFR-02: best-effort / non-fatal — never fail the caller.
            _logger.LogWarning(ex, "Thread name re-derive failed (non-fatal) | ThreadId: {ThreadId}", threadId);
        }
    }

    // ── The thread follows the filing (spaarkeai-word-add-in-r1 task 129) ───────────────────────────────────────

    private const string CommunicationTypeField = "sprk_communicationtype";
    private const string SubjectField = "sprk_subject";
    private const string DirectionField = "sprk_direction";
    private const string GraphMessageIdField = "sprk_graphmessageid";
    private const string InReplyToField = "sprk_inreplyto";
    private const string OwningTeamField = "owningteam";
    private const string OwnerIdField = "ownerid";
    private const string PrivacyStateField = "sprk_privacystate";
    private const string StateCodeFilterField = "statecode";
    private const int StateActive = 0;

    /// <summary>
    /// Bounds the scan of a thread's messages when deciding whether it may be anchored (NFR-07). A thread with more active
    /// messages than this is not proven unmixed, so its email moves to the record's default thread instead.
    /// </summary>
    internal const int MaxFilingReconcileMessageScan = 200;

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Email only.</b> The defect and the design (task 129 note §3.1) are about filed emails. A chat thread is a
    /// person-to-person or ACS conversation: anchoring it would re-own a 1:1 away from its participants and file every later
    /// message that joins it (S6) under the record, granted to the record's members. Other channels are left as they are.</para>
    /// <para><b>A private thread is left alone</b> (note §2: an email in a private thread without a grant is the invariant's
    /// exception) — neither anchored nor emptied of the email.</para>
    /// </remarks>
    public Task ReconcileThreadForFilingAsync(Guid communicationId, CancellationToken ct = default) =>
        ReconcileThreadForFilingAsync(communicationId, FilingReconcileHost.AfterRefileStep, ct);

    /// <inheritdoc />
    public Task ReconcileThreadForFilingAsync(Guid communicationId, FilingReconcileHost host, CancellationToken ct = default) =>
        ReconcileThreadForFilingAsync(communicationId, host, secureExitCaller: null, beforeMessageGrant: null, ct);

    /// <inheritdoc />
    public async Task ReconcileThreadForFilingAsync(
        Guid communicationId,
        FilingReconcileHost host,
        Sprk.Bff.Api.Services.Access.SecureRemovalCaller? secureExitCaller,
        Func<CancellationToken, Task>? beforeMessageGrant,
        CancellationToken ct = default)
    {
        try
        {
            var columns = MessageRegardingColumns();
            columns.Add(ThreadLookupOnCommunication);
            columns.Add(CommunicationTypeField);
            columns.Add(SubjectField);
            columns.Add(DirectionField);
            columns.Add(GraphMessageIdField);
            columns.Add(InReplyToField);
            var email = await _entityService.RetrieveAsync("sprk_communication", communicationId, columns.ToArray(), ct);

            if (email.GetAttributeValue<OptionSetValue>(CommunicationTypeField)?.Value != (int)CommunicationType.Email)
            {
                _logger.LogDebug("Filing reconcile skipped: {CommunicationId} is not an email", communicationId);
                return;
            }

            // R — the email's primary typed regarding, the value the filing write just validated (AppendTo as the caller).
            // Null when the email is unfiled (an unlink clears every regarding lookup).
            RegardingAnchor? regarding = null;
            var recordId = Guid.Empty;
            if (MessageRegardingAnchorOf(email) is { } filedTo && Guid.TryParse(filedTo.RecordId, out var filedToId)
                && filedToId != Guid.Empty)
            {
                regarding = filedTo;
                recordId = filedToId;
            }

            if (email.GetAttributeValue<EntityReference>(ThreadLookupOnCommunication)?.Id is not { } threadId || threadId == Guid.Empty)
            {
                // Behaviour review F2: a filed email with NO thread (an Office add-in save captured before task 129, or one
                // whose thread resolution failed) never shows on a record's panel. Resolve its thread now, through the same
                // join→grant sequence as the capture paths; an unfiled email without a thread is left as it is.
                if (regarding is not null)
                    await ResolveMissingThreadAsync(email, communicationId, host, beforeMessageGrant, ct);
                else
                    _logger.LogDebug("Filing reconcile skipped: {CommunicationId} is unfiled and has no thread", communicationId);
                return;
            }

            var thread = await _entityService.RetrieveAsync(
                "sprk_communicationthread",
                threadId,
                ThreadRegardingColumns
                    .Append("sprk_threadtype").Append(DefaultThreadMarkerField).Append(PrivacyStateField)
                    .Append("sprk_regardingrecordid").Append("sprk_regardingrecordname").Append("sprk_regardingrecordurl")
                    .Append(OwningTeamField).Append(OwnerIdField).Append(StateCodeFilterField)
                    .ToArray(),
                ct);
            if (thread is null)
            {
                _logger.LogWarning(
                    "Filing reconcile skipped: thread {ThreadId} of {CommunicationId} could not be read", threadId, communicationId);
                return;
            }

            var current = ThreadAnchorOf(thread);
            var isOpen = thread.GetAttributeValue<OptionSetValue>(PrivacyStateField)?.Value == PrivacyStateOpen;

            // An inactive (deactivated) thread does not show on a record's panel (behaviour review F3): it is never left as
            // the email's thread by rule 1, and never moved whole — only the email moves, to R's active default thread.
            var isActive = (thread.GetAttributeValue<OptionSetValue>(StateCodeFilterField)?.Value ?? StateActive) == StateActive;

            // A DEFAULT thread — the per-user master (Tier 3) or a record's per-record default (Tier 2) — is a catch-all and is
            // never moved whole (owner decision 2026-10-11): moving R_old's default would strip R_old of it and give R two.
            var isDefaultThread = thread.GetAttributeValue<bool?>(DefaultThreadMarkerField) == true;

            // Final re-check F3: a non-default, active thread the email leaves is retired when nothing is left in it.
            var retireIfEmpty = isActive && !isDefaultThread;

            if (regarding is null)
            {
                // Behaviour review F3 (unlink): the email was un-filed, but its thread still shows it on the record it was
                // filed to. It leaves that thread for the one the unfiled ingest would pick (the fallback ladder: no regarding
                // → the per-user master of the email's owner), with no grant to any record. A private thread keeps it.
                if (current.HasAnchor && isOpen)
                {
                    await MoveUnfiledEmailOutAsync(threadId, communicationId, current, ct);
                    if (retireIfEmpty)
                        await RetireIfEmptyAsync(threadId, ct);
                }
                else
                {
                    _logger.LogDebug("Filing reconcile skipped: {CommunicationId} is unfiled", communicationId);
                }
                return;
            }

            // Rule 1 (F1-a): T already shows on a record the email is filed to — ANY of its typed regarding lookups, not only
            // the primary. A sharee adding matter M to an email filed to secure project P makes M primary; T, anchored to P,
            // stays where it is rather than leaving P (and P's Secure team).
            if (isActive && current.HasAnchor && IsFiledTo(email, current))
            {
                _logger.LogDebug(
                    "Filing reconcile left thread {ThreadId} as it is: it is anchored to {RecordType}:{RecordId}, a record " +
                    "{CommunicationId} is filed to", threadId, current.RecordType, current.RecordId, communicationId);
                return;
            }

            if (!isOpen)
            {
                _logger.LogInformation(
                    "Filing reconcile left thread {ThreadId} as it is: it is not Open, so {CommunicationId} keeps the thread's " +
                    "isolation (filed to {RecordType}:{RecordId})", threadId, communicationId, regarding.RecordType, recordId);
                return;
            }

            // Rule 2 (owner clarification 2026-10-11): any other ACTIVE thread — unanchored, or anchored to a previous record —
            // follows the filing as a whole when every active message in it is an email (F1-c) and none other is filed to a
            // different record — and neither its owner nor R's is the Secure team (F1-b; AnchorThreadForFilingAsync answers
            // false, and only the email moves).
            if (!isDefaultThread && isActive
                && await FiledToOnlyAsync(threadId, communicationId, regarding, recordId, ct) is { } filedToRecord
                && await AnchorThreadForFilingAsync(
                    threadId, thread, communicationId, regarding, recordId, filedToRecord, beforeMessageGrant, ct))
            {
                return;
            }

            if (await MoveToRecordDefaultThreadAsync(
                    threadId, communicationId, regarding, secureExitCaller, beforeMessageGrant, ct) && retireIfEmpty)
                await RetireIfEmptyAsync(threadId, ct);
        }
        catch (Exception ex)
        {
            // NFR-02: best-effort / non-fatal — the filing already stands.
            _logger.LogWarning(ex, "Filing thread reconcile failed (non-fatal) | CommunicationId: {CommunicationId}", communicationId);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The owner S6 gives an anchored thread is resolved by <see cref="ResolveRecordThreadOwnershipAsync"/> — the step
    /// <see cref="AnchorThreadForFilingAsync"/> and the create paths use — and the Secure-team guard is
    /// <see cref="AnchorThreadForFilingAsync"/>'s (<c>ownership.IsSecureOwner || ThreadOwnerMayBeSecureAsync</c>). The one
    /// write is the owner; no other column of the thread, no message, and no grant is touched (a message's own owner is its
    /// own, and its read grants do not depend on the thread's owner). A thread with more than one typed regarding lookup is
    /// skipped (<see cref="ThreadOwnerReconcileOutcome.SkippedAmbiguousAnchor"/>): <see cref="ThreadAnchorOf"/> takes the first
    /// in priority order, which need not be the record the thread is for, and the owner is derived from that anchor. The
    /// write is the only place a thread's previous owner is lost, so the Apply log line and the result carry it (before-state
    /// for reversing a wrong re-own).
    /// </remarks>
    public async Task<ThreadOwnerReconcileResult> ReconcileAnchoredThreadOwnerAsync(
        Guid threadId, bool dryRun = false, CancellationToken ct = default)
    {
        try
        {
            var thread = await _entityService.RetrieveAsync(
                "sprk_communicationthread",
                threadId,
                ThreadRegardingColumns
                    .Append(PrivacyStateField).Append(OwningTeamField).Append(OwnerIdField).Append(StateCodeFilterField)
                    .ToArray(),
                ct);
            if (thread is null)
            {
                _logger.LogWarning("Thread owner reconcile skipped: thread {ThreadId} could not be read", threadId);
                return ThreadOwnerReconcileOutcome.NotApplicable;
            }

            var isActive = (thread.GetAttributeValue<OptionSetValue>(StateCodeFilterField)?.Value ?? StateActive) == StateActive;
            var isOpen = thread.GetAttributeValue<OptionSetValue>(PrivacyStateField)?.Value == PrivacyStateOpen;
            var anchor = ThreadAnchorOf(thread);
            if (!isActive || !isOpen || !anchor.HasAnchor)
            {
                _logger.LogDebug(
                    "Thread owner reconcile skipped: thread {ThreadId} is {Shape}", threadId,
                    !isActive ? "inactive" : !isOpen ? "not Open" : "not anchored to a record");
                return ThreadOwnerReconcileOutcome.NotApplicable;
            }

            // More than one typed lookup: the anchor is only the first in priority order, so the owner derived from it may be
            // the wrong record's team. Never re-own such a thread (owner decision: a wrong owner is worse than an old one).
            var populatedLookups = ThreadRegardingColumns.Count(
                c => thread.GetAttributeValue<EntityReference>(c) is { } er && er.Id != Guid.Empty);
            if (populatedLookups > 1)
            {
                _logger.LogInformation(
                    "Thread owner reconcile left thread {ThreadId}: {LookupCount} typed regarding lookups are set, so its anchor " +
                    "is ambiguous", threadId, populatedLookups);
                return ThreadOwnerReconcileOutcome.SkippedAmbiguousAnchor;
            }

            Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution? ownership;
            try
            {
                ownership = await ResolveRecordThreadOwnershipAsync(
                    new RegardingAnchor(anchor.RecordId!.Value.ToString(), anchor.RecordType!, null, null), ct);
            }
            catch (Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Thread owner reconcile left thread {ThreadId}: the owner of {RecordType}:{RecordId} could not be resolved",
                    threadId, anchor.RecordType, anchor.RecordId);
                return ThreadOwnerReconcileOutcome.Refused;
            }

            if (ownership?.OwningTeamId is not { } teamId)
            {
                _logger.LogDebug(
                    "Thread owner reconcile skipped: {RecordType}:{RecordId} owns no thread (thread {ThreadId})",
                    anchor.RecordType, anchor.RecordId, threadId);
                return ThreadOwnerReconcileOutcome.NotApplicable;
            }

            // The same guard as the whole-thread move (F1-b): never take a thread out of the Secure team, nor into it — the
            // sharee mirror of a secure record's children is not written here. An undeterminable owner counts as Secure.
            if (ownership.IsSecureOwner || await ThreadOwnerMayBeSecureAsync(thread, ct))
            {
                _logger.LogInformation(
                    "Thread owner reconcile left thread {ThreadId}: it or {RecordType}:{RecordId} belongs to a secure record " +
                    "(or that cannot be told)", threadId, anchor.RecordType, anchor.RecordId);
                return ThreadOwnerReconcileOutcome.SkippedSecure;
            }

            var currentTeam = thread.GetAttributeValue<EntityReference>(OwningTeamField)?.Id
                ?? (thread.GetAttributeValue<EntityReference>(OwnerIdField) is { } owner
                    && string.Equals(owner.LogicalName, "team", StringComparison.OrdinalIgnoreCase) ? owner.Id : (Guid?)null);
            if (currentTeam == teamId)
                return ThreadOwnerReconcileOutcome.AlreadyCorrect;

            if (!dryRun)
            {
                await _entityService.UpdateAsync(
                    "sprk_communicationthread",
                    threadId,
                    new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", teamId) },
                    ct);
            }

            // The before-state: ownerid is overwritten, so the previous owner is logged here and returned for the run report.
            var previousOwner = thread.GetAttributeValue<EntityReference>(OwnerIdField);
            _logger.LogInformation(
                "Thread owner reconcile {Action} thread {ThreadId} (anchored to {RecordType}:{RecordId}) to team {TeamId}; " +
                "previous owner {PreviousOwnerType}:{PreviousOwnerId}",
                dryRun ? "would assign" : "assigned", threadId, anchor.RecordType, anchor.RecordId, teamId,
                previousOwner?.LogicalName, previousOwner?.Id);
            return new ThreadOwnerReconcileResult(
                ThreadOwnerReconcileOutcome.Assigned, previousOwner?.LogicalName, previousOwner?.Id, teamId);
        }
        catch (Exception ex)
        {
            // NFR-02: best-effort / non-fatal — the thread is left as it was.
            _logger.LogWarning(ex, "Thread owner reconcile failed (non-fatal) | ThreadId: {ThreadId}", threadId);
            return ThreadOwnerReconcileOutcome.Failed;
        }
    }

    /// <summary>
    /// The active messages of <paramref name="threadId"/> filed to R (always including the filed email), or <c>null</c> when
    /// any active message in it is filed to another record, or is not an email (F1-c: a chat or other channel's message is a
    /// conversation between people — anchoring would re-own it and file what later joins it under R) — or the bounded scan
    /// could not see them all.
    /// </summary>
    private async Task<IReadOnlyList<Guid>?> FiledToOnlyAsync(
        Guid threadId, Guid communicationId, RegardingAnchor regarding, Guid recordId, CancellationToken ct)
    {
        var columns = MessageRegardingColumns();
        columns.Add(CommunicationTypeField);
        var query = new QueryExpression("sprk_communication")
        {
            ColumnSet = new ColumnSet(columns.ToArray()),
            TopCount = MaxFilingReconcileMessageScan + 1,
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression(MessageThreadLookup, ConditionOperator.Equal, threadId),
                    new ConditionExpression(StateCodeFilterField, ConditionOperator.Equal, StateActive),
                },
            },
        };
        var messages = (await _entityService.RetrieveMultipleAsync(query, ct)).Entities;
        if (messages.Count > MaxFilingReconcileMessageScan)
            return null;

        var filedToRecord = new List<Guid> { communicationId };
        foreach (var message in messages)
        {
            if (message.GetAttributeValue<OptionSetValue>(CommunicationTypeField)?.Value != (int)CommunicationType.Email)
                return null; // not an email (or its type is unknown): the thread is not only a filing's email thread

            if (MessageRegardingAnchorOf(message) is not { } other)
                continue; // unfiled: keeps its visibility (design K-b), never granted to R's members

            var sameRecord = Guid.TryParse(other.RecordId, out var otherId) && otherId == recordId
                && string.Equals(other.RecordType, regarding.RecordType, StringComparison.OrdinalIgnoreCase);
            if (!sameRecord)
                return null; // mixed: the thread cannot be anchored to R

            if (message.Id != Guid.Empty && !filedToRecord.Contains(message.Id))
                filedToRecord.Add(message.Id);
        }

        return filedToRecord;
    }

    /// <summary>
    /// Rule 2: anchors the thread to R — the fields <see cref="CreateThreadAsync"/> writes for an anchored thread, owned by
    /// R's team (S6) — re-derives its name, and grants R's members every message in it filed to R. A thread anchored to a
    /// previous record loses that record's typed lookup (and its display fields when R has none), so the thread's one anchor
    /// (<see cref="ThreadAnchorOf"/>) is R. #2143 round 10 (F2-a): <paramref name="beforeMessageGrant"/> runs once, after the
    /// anchor and before the grants — as on rule 3's move — so the step after a re-file takes the email's mirror off before
    /// this grant, never after it (the removal revokes every direct share).
    /// </summary>
    /// <returns>
    /// <c>false</c> when the thread may not follow whole because its owner or R's is the Secure team (F1-b) — nothing is
    /// written and the caller moves only the email (rule 3). <c>true</c> otherwise: anchored (to a party — account / contact /
    /// organization — keeping the thread's owner, as <see cref="CreateThreadAsync"/> does), or left as it is when R's owner
    /// is refused.
    /// </returns>
    private async Task<bool> AnchorThreadForFilingAsync(
        Guid threadId, DataverseEntity thread, Guid communicationId, RegardingAnchor regarding, Guid recordId,
        IReadOnlyList<Guid> filedToRecord, Func<CancellationToken, Task>? beforeMessageGrant, CancellationToken ct)
    {
        Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution? ownership;
        try
        {
            ownership = await ResolveRecordThreadOwnershipAsync(regarding, ct);
        }
        catch (Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException ex)
        {
            _logger.LogWarning(
                ex,
                "Filing reconcile left thread {ThreadId} unanchored: the owner of {RecordType}:{RecordId} could not be resolved " +
                "(CommunicationId: {CommunicationId})", threadId, regarding.RecordType, recordId, communicationId);
            return true;
        }

        // A party (account / contact / organization — not an ownership parent) has no team to own its thread; like
        // CreateThreadAsync, the thread is anchored to it and keeps its current owner (owner 2026-10-12: an email related to a
        // contact shows on the contact's Messages panel too). Any other record with no owner team is left as it is.
        var isParty = ownership is null && RegardingFieldMap.FieldFor(regarding.RecordType) is not null
            && !Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsOwnershipParent(regarding.RecordType);
        if (ownership?.OwningTeamId is null && !isParty)
        {
            _logger.LogInformation(
                "Filing reconcile left thread {ThreadId} unanchored: {RecordType}:{RecordId} owns no thread (CommunicationId: " +
                "{CommunicationId})", threadId, regarding.RecordType, recordId, communicationId);
            return true;
        }
        var teamId = ownership?.OwningTeamId; // null for a party: the thread keeps its owner

        // F1-b: a whole-thread move never takes a thread out of the Secure team, nor into it. Out of it, the thread's other
        // messages would leave a secure record's isolation; into it, the thread's sharee mirror is not written here (K1).
        if (ownership?.IsSecureOwner == true || await ThreadOwnerMayBeSecureAsync(thread, ct))
        {
            _logger.LogInformation(
                "Filing reconcile moves only {CommunicationId}: thread {ThreadId} or {RecordType}:{RecordId} belongs to a secure " +
                "record (or that cannot be told), so the thread does not follow whole",
                communicationId, threadId, regarding.RecordType, recordId);
            return false;
        }

        // K2: the thread's display name is R's own primary name, read server-side — not the name the caller filed with.
        var recordName = await ReadRecordPrimaryNameAsync(regarding.RecordType, recordId, ct);

        // The anchored-thread field set of CreateThreadAsync, written as one update.
        var fields = new Dictionary<string, object>
        {
            ["sprk_threadtype"] = new OptionSetValue(ThreadTypeRecordAnchored),
            ["sprk_regardingrecordid"] = TruncateTo(regarding.RecordId, 100),
        };
        if (teamId is { } team)
            fields["ownerid"] = new EntityReference("team", team);
        if (RegardingFieldMap.FieldFor(regarding.RecordType) is { } typedField)
            fields[typedField] = new EntityReference(regarding.RecordType, recordId); // what SetTypedRegardingLookup writes
        if (!string.IsNullOrWhiteSpace(recordName))
            fields["sprk_regardingrecordname"] = TruncateTo(recordName, 400);
        if (!string.IsNullOrWhiteSpace(regarding.RecordUrl))
            fields["sprk_regardingrecordurl"] = TruncateTo(regarding.RecordUrl, 400);

        // A re-file: clear what still names the previous record (DBNull.Value = clear, through the generic update) — every
        // other typed lookup, and a display name/url R does not replace — so the thread's anchor, owner and name are R's.
        foreach (var column in ThreadRegardingColumns)
        {
            if (!fields.ContainsKey(column) && thread.GetAttributeValue<EntityReference>(column) is not null)
                fields[column] = DBNull.Value;
        }
        foreach (var column in new[] { "sprk_regardingrecordname", "sprk_regardingrecordurl" })
        {
            if (!fields.ContainsKey(column) && thread.Contains(column))
                fields[column] = DBNull.Value;
        }

        await _entityService.UpdateAsync("sprk_communicationthread", threadId, fields, ct);
        _logger.LogInformation(
            "Filing reconcile anchored thread {ThreadId} to {RecordType}:{RecordId}, owned by team {TeamId} (none: a party, " +
            "the owner is kept) | CommunicationId: " +
            "{CommunicationId}", threadId, regarding.RecordType, recordId, teamId, communicationId);

        await ReDeriveThreadNameAsync(threadId, ct); // marker-gated; a user-renamed thread keeps its name

        if (beforeMessageGrant is not null)
            await beforeMessageGrant(ct);

        var assignment = ThreadAssignment.Anchored(threadId, regarding.RecordType, recordId);
        foreach (var messageId in filedToRecord)
            await GrantAsync(messageId, assignment, ct);
        return true;
    }

    /// <summary>
    /// <c>true</c> unless the thread's owner is proven NOT to be the Secure Record owner team: a user owner, or a team whose
    /// own row rules it out — the detection <see cref="Access.DirectThreadAccessService"/> uses
    /// (<see cref="Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync"/>, then
    /// <see cref="Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync"/>). A team owner
    /// with no configuration, or an ambiguous Secure team, answers <c>true</c> (fail closed). A Dataverse fault propagates.
    /// </summary>
    private async Task<bool> ThreadOwnerMayBeSecureAsync(DataverseEntity thread, CancellationToken ct)
    {
        var owningTeam = thread.GetAttributeValue<EntityReference>(OwningTeamField)?.Id
            ?? (thread.GetAttributeValue<EntityReference>(OwnerIdField) is { } owner
                && string.Equals(owner.LogicalName, "team", StringComparison.OrdinalIgnoreCase) ? owner.Id : null);
        if (owningTeam is not { } team || team == Guid.Empty)
            return false; // user-owned: never a secure child

        if (_configuration is null)
            return true;

        if (await Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(
                _entityService, _configuration, team, ct))
            return false;

        var secure = await Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(
            _entityService, _configuration, ct);
        return secure.Refusal is not null || secure.TeamId == team;
    }

    /// <summary>
    /// <paramref name="entityLogicalName"/>'s primary name for <paramref name="recordId"/>, read server-side through the one
    /// name map (<see cref="RegardingNameFields.PrimaryNameField"/>); <c>null</c> for a type it does not list, a row that
    /// cannot be read, or a blank name.
    /// </summary>
    private async Task<string?> ReadRecordPrimaryNameAsync(string entityLogicalName, Guid recordId, CancellationToken ct)
    {
        if (RegardingNameFields.PrimaryNameField(entityLogicalName.ToLowerInvariant()) is not { } nameField)
            return null;

        try
        {
            var record = await _entityService.RetrieveAsync(entityLogicalName.ToLowerInvariant(), recordId, new[] { nameField }, ct);
            return record?.GetAttributeValue<string>(nameField) is { } name && !string.IsNullOrWhiteSpace(name) ? name : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Filing reconcile could not read the name of {RecordType}:{RecordId}", entityLogicalName, recordId);
            return null;
        }
    }

    /// <summary>
    /// Rule 1 (F1-a): <c>true</c> when <paramref name="anchor"/> is one of the records <paramref name="email"/> is filed to —
    /// any of its typed ADR-024 regarding lookups, not only the primary.
    /// </summary>
    internal static bool IsFiledTo(DataverseEntity email, ThreadAnchor anchor)
    {
        foreach (var (entityLogicalName, field) in RegardingFieldMap.All)
        {
            if (email.GetAttributeValue<EntityReference>(field) is not { } er || er.Id == Guid.Empty || er.Id != anchor.RecordId)
                continue;

            var type = string.IsNullOrWhiteSpace(er.LogicalName) ? entityLogicalName : er.LogicalName;
            if (string.Equals(type, anchor.RecordType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Rule 3: moves the email to R's per-record default thread (Tier 2) through the S6 join, then grants it to R's
    /// members — the Tier-2 audience, from the email's own regarding (#1629 F-C), never a re-read of the thread. Answers
    /// whether the email left <paramref name="fromThreadId"/>. #2143 round 7 (F1): the join acts for
    /// <paramref name="secureExitCaller"/>, the person whose re-file this move follows (<c>null</c>: nobody). Round 8 (F1-A):
    /// <paramref name="beforeMessageGrant"/> runs between the join and the grant (the step after a re-file takes the email's
    /// mirror off there when the join took it out of isolation, so the removal never revokes this grant).
    /// </summary>
    private async Task<bool> MoveToRecordDefaultThreadAsync(
        Guid fromThreadId, Guid communicationId, RegardingAnchor regarding,
        Sprk.Bff.Api.Services.Access.SecureRemovalCaller? secureExitCaller,
        Func<CancellationToken, Task>? beforeMessageGrant, CancellationToken ct)
    {
        var recordThreadId = await FindOrCreateDefaultThreadAsync(
            regarding.RecordType, regarding.RecordId, regarding, requireOpen: false, ct);
        if (recordThreadId == fromThreadId)
            return false;

        // #2091: the S6 join without the step after a re-file — inside that step this IS its thread reconcile, and from the sweep
        // (#2110) it adds no parent either: the default thread is R's, the record the email is already filed to.
        try
        {
            await AssignThreadReconcilingOwnerAsync(communicationId, recordThreadId, secureExitCaller, ct);
        }
        catch (Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException refused)
        {
            // #2143 round 7b: the re-file this move follows asked F3 about every secure record the move leaves (the resolver,
            // before the re-file wrote), so a refusal here is only a race — the thread was re-filed between that check and this
            // move — or a reconcile no person's re-file started. Fail-safe: the email keeps the thread and its owner (an
            // isolated email stays isolated), named here for an operator; the filing sweep retries it. Round 8: an Error only
            // for the race (a person's pre-checked re-file); a person-less reconcile (the sweep, on every run, for each legacy
            // row it cannot move) is a Warning.
            _logger.Log(
                secureExitCaller is null ? LogLevel.Warning : LogLevel.Error,
                "Filing reconcile could not move {CommunicationId} from thread {FromThreadId} to the default thread {ThreadId} of " +
                "{RecordType}:{RecordId}: the move was refused ({Code}). The email keeps its thread and its owner (#2143).",
                communicationId, fromThreadId, recordThreadId, regarding.RecordType, regarding.RecordId, refused.RefusalCode);
            throw;
        }

        _logger.LogInformation(
            "Filing reconcile moved {CommunicationId} from thread {FromThreadId} to the default thread {ThreadId} of " +
            "{RecordType}:{RecordId}", communicationId, fromThreadId, recordThreadId, regarding.RecordType, regarding.RecordId);

        if (beforeMessageGrant is not null)
            await beforeMessageGrant(ct);

        if (AssignmentFor(recordThreadId, regarding) is { } assignment)
            await GrantAsync(communicationId, assignment, ct);
        return true;
    }

    /// <summary>
    /// Final re-check F3: the Messages panel lists a record's threads by their anchor, so a thread the email has just left
    /// stays on its record's panel with nothing in it. A non-default thread left with no active message is deactivated (a
    /// default thread is a catch-all and is never passed here). Best-effort and fail-safe: when the check cannot tell the
    /// thread is empty, it is left as it is; the move already stands.
    /// </summary>
    private async Task RetireIfEmptyAsync(Guid threadId, CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression("sprk_communication")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(MessageThreadLookup, ConditionOperator.Equal, threadId),
                        new ConditionExpression(StateCodeFilterField, ConditionOperator.Equal, StateActive),
                    },
                },
            };
            var remaining = await _entityService.RetrieveMultipleAsync(query, ct);
            if (remaining?.Entities is not { Count: 0 })
                return;

            await DeactivateThreadAsync(threadId, ct);
            _logger.LogInformation("Filing reconcile retired thread {ThreadId}: no active message is left in it", threadId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Retiring the emptied thread {ThreadId} failed (non-fatal)", threadId);
        }
    }

    /// <summary>
    /// Behaviour review F2: a filed email that has no thread joins (or starts) one through <see cref="ResolveAndAssignAsync"/> —
    /// the capture paths' own resolution, with the email's subject (an existing row carries no ancestry headers, so the email
    /// key strategy creates a thread anchored to the email's regarding, owned by its record's team, S6) — and is granted to
    /// the audience that join fixed (#1629).
    /// </summary>
    /// <remarks>
    /// #2110 (ontology K2): the thread the email's conversation key finds may be anchored to a record R2 the email is not filed
    /// to, and the join files it under R2 too (S6). Inside the step after a re-file (<see cref="FilingReconcileHost.AfterRefileStep"/>)
    /// the step's mirror, its pass and its gate step run after this reconcile and see that join, so the join runs none of its
    /// own. On its own (<see cref="FilingReconcileHost.Standalone"/>, the backfill sweep) nothing else follows it: a join under a
    /// record the email is NOT filed to runs the step once for that join; a join under a record it is filed to adds no parent
    /// and runs none. #2143 round 10 (F2-a): <paramref name="beforeMessageGrant"/> runs between that join and the grant, as on
    /// rule 3's move.
    /// </remarks>
    private async Task ResolveMissingThreadAsync(
        DataverseEntity email, Guid communicationId, FilingReconcileHost host,
        Func<CancellationToken, Task>? beforeMessageGrant, CancellationToken ct)
    {
        var joinRunsAfterRefileFor = host == FilingReconcileHost.Standalone
            ? (Func<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent, bool>)(filedUnder =>
                !IsFiledTo(email, new ThreadAnchor(filedUnder.EntityLogicalName, filedUnder.RecordId, ThreadPairAgreement.NoPair, null)))
            : NoJoinRunsTheStep;
        var assignment = await ResolveAndAssignCoreAsync(
            new ThreadResolutionRequest
            {
                CommunicationId = communicationId,
                ChannelType = CommunicationType.Email,
                Direction = CommunicationDirection.Incoming,
                Message = new NormalizedMessage
                {
                    Direction = CommunicationDirection.Incoming,
                    Subject = email.GetAttributeValue<string>(SubjectField),
                },
            },
            joinRunsAfterRefileFor,
            ct);
        if (assignment is null)
            return; // logged by ResolveAndAssignAsync (NFR-02)

        _logger.LogInformation(
            "Filing reconcile gave the threadless email {CommunicationId} thread {ThreadId}", communicationId, assignment.ThreadId);
        if (beforeMessageGrant is not null)
            await beforeMessageGrant(ct);
        await GrantAsync(communicationId, assignment, ct);

        // Task 065 fix round 4 (K1): a RECEIVED email is written internal-only until its thread is known; this sweep is the
        // last chance to lower it, or it stays hidden from partners for good. Same shared rule as the capture paths: lower
        // only when the thread reads Open AND the parent is not a blocker. The sweep cannot evaluate a parent, so: a row with a
        // Graph message id is a mailbox email whose headers were lost, so it stays internal-only (cannot evaluate = fail
        // closed); a row with a non-blank sprk_inreplyto (fix round 6, K1: the add-in capture stores it when the upload carried
        // reply headers) replies to a parent the sweep does not read, so it also stays internal-only; a row with neither is a
        // header-less add-in save and follows the AddInSaveWithoutReplyHeadersIsShareable decision (owner D6, 2026-10-14: stays shareable).
        // Only an explicit Incoming direction is lowered; fail closed otherwise. Never throws except on cancellation.
        var isIncoming = email.GetAttributeValue<OptionSetValue>(DirectionField)?.Value == (int)CommunicationDirection.Incoming;
        if (isIncoming)
        {
            var isMailboxRow = !string.IsNullOrWhiteSpace(email.GetAttributeValue<string>(GraphMessageIdField));
            var repliesToAParent = !string.IsNullOrWhiteSpace(email.GetAttributeValue<string>(InReplyToField));
            var parentNotShareable = isMailboxRow || repliesToAParent || !CommunicationInternalOnlyFloor.AddInSaveWithoutReplyHeadersIsShareable;
            await CommunicationInternalOnlyFloor.LowerWhenSafeAsync(
                _entityService, communicationId, assignment.ThreadId, parentNotShareable, _logger, ct);
        }
    }

    /// <summary>
    /// Behaviour review F3 (unlink): an UNFILED email leaves the record thread it still sits in, for the thread the unfiled
    /// ingest would give it — the fallback ladder (<see cref="ResolveViaFallbackLadderAsync"/>), which with no regarding is
    /// the per-user master keyed on the email's OWNER. No grant: the master grants nobody (#1629), and the email is filed to
    /// no record whose members it could be granted to.
    /// </summary>
    private async Task MoveUnfiledEmailOutAsync(Guid fromThreadId, Guid communicationId, ThreadAnchor from, CancellationToken ct)
    {
        var assignment = await ResolveViaFallbackLadderAsync(
            new ThreadResolutionRequest
            {
                CommunicationId = communicationId,
                ChannelType = CommunicationType.Email,
                Direction = CommunicationDirection.Incoming,
                Message = new NormalizedMessage { Direction = CommunicationDirection.Incoming },
            },
            ct);
        _logger.LogInformation(
            "Filing reconcile moved the un-filed email {CommunicationId} out of thread {FromThreadId} (anchored to " +
            "{RecordType}:{RecordId}) to thread {ThreadId}", communicationId, fromThreadId, from.RecordType, from.RecordId,
            assignment?.ThreadId);
    }

    /// <summary>The #1629 grant for one message, when the grant service is wired (it never grants a secure message).</summary>
    private Task GrantAsync(Guid communicationId, ThreadAssignment assignment, CancellationToken ct) =>
        _directThreadAccess?.GrantMessageAccessAsync(communicationId, assignment, ct) ?? Task.CompletedTask;

    /// <inheritdoc />
    public async Task<string> RenameThreadAsync(Guid threadId, string name, CancellationToken ct = default)
    {
        // Defense-in-depth (the endpoint also validates → 400). Truncate to the sprk_name field max (200).
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Thread name must not be blank.", nameof(name));

        var persisted = TruncateTo(name.Trim(), 200);

        // Edit-preserve (correctness-critical): set the user's name AND flip the marker to Edited ATOMICALLY in
        // ONE UpdateAsync, so ReDeriveThreadNameAsync's marker gate (marker == false → return) short-circuits on
        // the NEXT re-derive and never clobbers it. NOT wrapped in a non-fatal swallow — a user-initiated rename
        // failure must surface to the endpoint (unlike the best-effort re-derive).
        await _entityService.UpdateAsync(
            "sprk_communicationthread",
            threadId,
            new Dictionary<string, object>
            {
                ["sprk_name"] = persisted,
                [NameIsAutoDerivedField] = false,
            },
            ct);

        _logger.LogInformation("Renamed thread {ThreadId} (naming-edited marker → Edited): {Name}", threadId, persisted);
        return persisted;
    }

    /// <inheritdoc />
    public async Task<bool> SetPinnedAsync(Guid threadId, bool pinned, CancellationToken ct = default)
    {
        // User-initiated write, NOT best-effort (unlike ReDeriveThreadNameAsync) — a failure must surface to the
        // endpoint, mirroring RenameThreadAsync's contract.
        await _entityService.UpdateAsync(
            "sprk_communicationthread",
            threadId,
            new Dictionary<string, object> { [PinnedField] = pinned },
            ct);

        _logger.LogInformation("Set pinned={Pinned} on thread {ThreadId}", pinned, threadId);
        return pinned;
    }

    // Dataverse state fields for soft-delete (round 7 items 7/8). statecode=1 (Inactive) + statuscode=2 deactivate a
    // row without physically deleting it; the read filter keys off `statecode eq 0`, so this hides the row.
    //
    // Two hard-won constraints (round-8.3 fix — empirically verified against dev on 2026-07-31):
    //  1. statecode/statuscode are STATE/STATUS option-set attributes — they MUST be written to the ServiceClient SDK
    //     Entity as OptionSetValue, NOT raw int. The generic IGenericEntityService.UpdateAsync (DataverseServiceClientImpl)
    //     copies dictionary values straight onto the Entity, and the SDK rejects a raw int for a State/Status attribute
    //     (the original round-8 code passed raw ints → every update threw a 500 → no row was EVER deactivated). See
    //     CommunicationService/MessagingIngestor, which likewise wrap statecode/statuscode in OptionSetValue.
    //  2. statuscode 2 is the valid Inactive-state reason on BOTH entities (confirmed by deactivating a live record of
    //     each: sprk_communicationthread → "Inactive" (2); sprk_communication → "Deleted" (2)). We set BOTH state + its
    //     reason explicitly so the write is unambiguous — setting statuscode alone can be rejected as "not valid for the
    //     record's current state", and setting statecode alone leaves the reason to the platform default.
    private const string StateCodeField = "statecode";
    private const string StatusCodeField = "statuscode";
    private const int StateInactive = 1;
    private const int StatusInactive = 2;

    /// <inheritdoc />
    public async Task DeactivateThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        // User-initiated write, NOT best-effort — a failure must surface to the endpoint (mirrors SetPinnedAsync).
        await _entityService.UpdateAsync(
            "sprk_communicationthread",
            threadId,
            new Dictionary<string, object> { [StateCodeField] = new OptionSetValue(StateInactive), [StatusCodeField] = new OptionSetValue(StatusInactive) },
            ct);

        _logger.LogInformation("Deactivated (soft-deleted) thread {ThreadId}", threadId);
    }

    /// <inheritdoc />
    public async Task DeactivateMessageAsync(Guid communicationId, CancellationToken ct = default)
    {
        // User-initiated write, NOT best-effort — a failure must surface to the endpoint.
        await _entityService.UpdateAsync(
            "sprk_communication",
            communicationId,
            new Dictionary<string, object> { [StateCodeField] = new OptionSetValue(StateInactive), [StatusCodeField] = new OptionSetValue(StatusInactive) },
            ct);

        _logger.LogInformation("Deactivated (soft-deleted) communication {CommunicationId}", communicationId);
    }

    /// <inheritdoc />
    public async Task<Guid> CreateRecordThreadAsync(
        Guid ownerSystemUserId, string? name, RecordThreadAnchor regarding, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(regarding);
        if (string.IsNullOrWhiteSpace(regarding.EntityType) || string.IsNullOrWhiteSpace(regarding.RecordId))
            throw new ArgumentException(
                "A regarding entity type and record id are required to create a record-anchored thread.",
                nameof(regarding));

        // User-provided name → Edited (preserve). Blank → derive from the record name (Auto → re-derives).
        var hasName = !string.IsNullOrWhiteSpace(name);
        var resolvedName = hasName
            ? name!.Trim()
            : (!string.IsNullOrWhiteSpace(regarding.RecordName) ? regarding.RecordName!.Trim() : "Conversation");

        var entityType = regarding.EntityType.Trim();

        // The new thread must be findable by the by-regarding read, which filters on the TYPED ADR-024
        // regarding LOOKUP (RegardingFieldMap: e.g. sprk_matter → sprk_regardingmatter), NOT a text field.
        // NOTE: sprk_communicationthread has NO 'sprk_regardingrecordtype' text attribute — it carries the
        // per-family typed lookups (+ a sprk_regardingrecordtype_ref lookup) alongside the denormalized
        // sprk_regardingrecordid / sprk_regardingrecordname / sprk_regardingrecordurl text/url fields. Writing
        // the non-existent 'sprk_regardingrecordtype' is what raised the create-time InvalidOperationException
        // (RB: R3 UAT 2026-07-24). REUSE of RegardingFieldMap — not a second regarding mechanism.
        var regardingField = RegardingFieldMap.FieldFor(entityType);
        if (regardingField is null)
            throw new ArgumentException(
                $"'{entityType}' is not a supported regarding entity type (ADR-024 family).", nameof(regarding));
        if (!Guid.TryParse(regarding.RecordId.Trim(), out var regardingId) || regardingId == Guid.Empty)
            throw new ArgumentException(
                "A valid regarding record id (GUID) is required to create a record-anchored thread.", nameof(regarding));

        // Task 146 / owner decision S6: a RECORD thread anchored to an ownership parent (a matter, project, invoice, …)
        // is a child of that record — owned by the record's team (the named Secure team for a secure record; task 149
        // mirrors the record's sharees, so the caller keeps sight of it as a sharee of the record). A refusal is a 409
        // with a stable reason code and creates nothing; a Dataverse fault propagates as the request's 5xx. A thread
        // regarding a party (account / contact / organization — not an ownership parent) keeps the caller as owner,
        // as before, "so the new (empty) thread is visible in the caller's all-mode list".
        EntityReference threadOwner = new("systemuser", ownerSystemUserId);
        Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution? recordOwner = null;
        if (Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsOwnershipParent(entityType))
        {
            var owner = await _ownership.ResolveOwnerAsync(
                new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext
                {
                    TargetEntityLogicalName = entityType,
                    TargetRecordId = regardingId,
                    CallerSystemUserId = ownerSystemUserId,
                    // Task 146 c1-r1 (owner round 13 item 9): the caller asked for this app-created record thread.
                    RequestedBy = Sprk.Bff.Api.Services.Dataverse.RecordRequester.Of(ownerSystemUserId),
                },
                ct);
            if (!owner.IsOwned)
            {
                throw new Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException(
                    code: owner.RefusalCode ?? Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.NoOwnerSource,
                    title: "Record owner unresolved",
                    detail: $"The conversation was not created: {owner.Reason}.",
                    statusCode: 409);
            }

            threadOwner = new EntityReference("team", owner.OwningTeamId!.Value);
            recordOwner = owner;
        }

        var thread = new DataverseEntity("sprk_communicationthread")
        {
            ["sprk_name"] = TruncateTo(resolvedName, 200),
            // Anchored to an ADR-024 record → Record-Anchored (mirrors CreateThreadAsync's anchor branch).
            ["sprk_threadtype"] = new OptionSetValue(ThreadTypeRecordAnchored),
            ["sprk_privacystate"] = new OptionSetValue(PrivacyStateOpen),
            [NameIsAutoDerivedField] = !hasName,
            // Owner — the record's team for an ownership parent, else the caller (task 146 above).
            ["ownerid"] = threadOwner,
            // TYPED regarding lookup — the exact field the by-regarding read filters on, so the new thread
            // shows in the record-scoped conversation list.
            [regardingField] = new EntityReference(entityType, regardingId),
            // Denormalized display pointer (this text attribute DOES exist on the thread).
            ["sprk_regardingrecordid"] = TruncateTo(regarding.RecordId.Trim(), 100),
        };
        if (!string.IsNullOrWhiteSpace(regarding.RecordName))
            thread["sprk_regardingrecordname"] = TruncateTo(regarding.RecordName!.Trim(), 400);

        recordOwner?.StampCreatorOn(thread); // task 146 c1-r1 — the caller, on a team-owned record thread

        var threadId = await _entityService.CreateAsync(thread, ct);
        _logger.LogInformation(
            "Created record-anchored thread {ThreadId} for caller {Caller}, owned by {OwnerType}:{Owner}, regarding {RecordType}:{RecordId}",
            threadId, ownerSystemUserId, threadOwner.LogicalName, threadOwner.Id, regarding.EntityType, regarding.RecordId);
        return threadId;
    }

    /// <summary>
    /// FR-17 participant roll-up (task 004): names a RECORD-LESS thread from the DISTINCT people/addresses in its
    /// messages. Two bounded queries — the thread's messages, then those messages' participants from the
    /// <c>sprk_communicationparticipant</c> junction (ADR-024/ADR-048 REUSE — no second participant store, no
    /// per-row fan-out, NFR-07). A resolved person contributes its typed lookup's display name
    /// (<c>sprk_systemuser</c> then <c>sprk_contact</c> — the SDK populates the <see cref="EntityReference.Name"/>);
    /// an unresolved external party contributes its <c>sprk_addresstext</c>. Distinct names (case-insensitive) are
    /// ordered DETERMINISTICALLY (ordinal, case-insensitive — independent of query order) and rendered
    /// "Alice, Bob, +N". Returns <c>null</c> when the thread has no messages or no usable participant name, so the
    /// caller keeps the generic "Conversation" fallback.
    /// </summary>
    private async Task<string?> BuildParticipantRollupNameAsync(Guid threadId, CancellationToken ct)
    {
        // 1) The thread's messages (bounded scan).
        var messageQuery = new QueryExpression("sprk_communication")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = MaxRollupMessageScan,
            Criteria =
            {
                Conditions = { new ConditionExpression(MessageThreadLookup, ConditionOperator.Equal, threadId) },
            },
        };
        var messages = await _entityService.RetrieveMultipleAsync(messageQuery, ct);
        var messageIds = messages.Entities
            .Select(m => m.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (messageIds.Length == 0)
            return null;

        // 2) Those messages' participants (bounded scan). The junction carries the typed person lookups (whose
        //    display name the SDK populates on the EntityReference) plus the raw address as provenance.
        var participantQuery = new QueryExpression(ParticipantEntity)
        {
            ColumnSet = new ColumnSet("sprk_systemuser", "sprk_contact", "sprk_addresstext"),
            TopCount = MaxRollupParticipantScan,
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression(
                        ParticipantMessageLookup, ConditionOperator.In, messageIds.Cast<object>().ToArray()),
                },
            },
        };
        var participants = await _entityService.RetrieveMultipleAsync(participantQuery, ct);

        // 3) Distinct display names in a deterministic order.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var p in participants.Entities)
        {
            var display =
                p.GetAttributeValue<EntityReference>("sprk_systemuser")?.Name
                ?? p.GetAttributeValue<EntityReference>("sprk_contact")?.Name
                ?? p.GetAttributeValue<string>("sprk_addresstext");
            if (string.IsNullOrWhiteSpace(display))
                continue;
            var trimmed = display.Trim();
            if (seen.Add(trimmed))
                names.Add(trimmed);
        }
        if (names.Count == 0)
            return null;

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return BuildParticipantDisplayName(names);
    }

    /// <summary>Renders up to <see cref="MaxNamesInRollup"/> distinct names as "Alice, Bob, +N" (deterministic).</summary>
    private static string BuildParticipantDisplayName(IReadOnlyList<string> distinctOrderedNames)
    {
        var shown = distinctOrderedNames.Take(MaxNamesInRollup).ToList();
        var remainder = distinctOrderedNames.Count - shown.Count;
        var joined = string.Join(", ", shown);
        return remainder > 0 ? $"{joined}, +{remainder}" : joined;
    }

    /// <summary>
    /// Idempotent find-or-create for an auto-created default/master thread, keyed on the task-002
    /// <c>sprk_isdefaultthread</c> marker + the denormalized regarding pointer (Tier 2: the regarding record;
    /// Tier 3: <c>("systemuser", ownerId)</c>). Queries for the single existing default first and JOINs it;
    /// only the first orphan for a given key CREATEs the thread ("lazy"). A pre-existing <paramref name="anchor"/>
    /// makes the thread Record-Anchored and copies the regarding display fields; a null anchor (Tier 3) makes it
    /// a Direct catch-all.
    /// </summary>
    /// <remarks>
    /// Concurrency: the query-then-create is not transactional, so two orphans racing on the SAME key could
    /// each create a default thread. In practice thread resolution runs per-message on the serialized
    /// inbound-job / outbound-send paths, making the race rare; if it occurs the duplicate is benign (both are
    /// valid default threads for the record/user) and NFR-02 keeps it non-fatal. An optional dedup/merge sweep
    /// is out of scope for FR-09 (point-forward only).
    /// </remarks>
    private async Task<Guid> FindOrCreateDefaultThreadAsync(
        string keyType, string keyId, RegardingAnchor? anchor, bool requireOpen, CancellationToken ct)
    {
        if (await FindDefaultThreadAsync(keyType, keyId, requireOpen, ct) is { } existing)
            return existing;

        // Task 146 / S6: a Tier-2 per-record default thread is owned by its record's team; the Tier-3 per-user master
        // (no anchor) keeps its creator — per-user by design.
        var recordOwner = await ResolveRecordThreadOwnerAsync(anchor, ct);

        var thread = new DataverseEntity("sprk_communicationthread")
        {
            ["sprk_name"] = TruncateTo(BuildDefaultThreadName(anchor), 200),
            // Record-Anchored when keyed on a regarding record; Direct for the per-user master catch-all.
            ["sprk_threadtype"] = new OptionSetValue(anchor is not null ? ThreadTypeRecordAnchored : ThreadTypeDirect),
            ["sprk_privacystate"] = new OptionSetValue(PrivacyStateOpen),
            // Task-002 marker — additive Boolean (NFR-04); flags this as an auto-created catch-all.
            [DefaultThreadMarkerField] = true,
            // FR-07 (task 071) — same Auto stamp as a normal CREATE; a default/master thread's name still
            // re-derives (subject to the ReDeriveThreadNameAsync master-thread guard for Tier 3) until edited.
            [NameIsAutoDerivedField] = true,
            // Denormalized GUID pointer doubles as the idempotency key (queried by FindDefaultThreadAsync before
            // create). keyId is a record GUID (Tier 2) or the owning-user GUID (Tier 3 master) — globally unique,
            // so it is a sufficient key on its own (no 'sprk_regardingrecordtype' — that text attr does not exist
            // on the thread; RB R3 UAT 2026-07-24).
            ["sprk_regardingrecordid"] = TruncateTo(keyId, 100),
        };

        // Tier 2 (per-record default) — set the TYPED ADR-024 lookup so this record's default thread appears in
        // the by-regarding conversation list. No-op for the Tier-3 master (keyType "systemuser" is not a family).
        SetTypedRegardingLookup(thread, keyType, keyId);

        // Copy the regarding display fields only for a real record anchor (Tier 2) — REUSE of the ADR-024
        // regarding family, identical to CreateThreadAsync. The Tier-3 master carries no display name/url.
        if (anchor is not null)
        {
            if (!string.IsNullOrWhiteSpace(anchor.RecordName))
                thread["sprk_regardingrecordname"] = TruncateTo(anchor.RecordName, 400);
            if (!string.IsNullOrWhiteSpace(anchor.RecordUrl))
                thread["sprk_regardingrecordurl"] = TruncateTo(anchor.RecordUrl, 400);
        }

        if (recordOwner is { } ownerTeamId)
            thread["ownerid"] = new EntityReference("team", ownerTeamId);

        return await _entityService.CreateAsync(thread, ct);
    }

    /// <summary>
    /// Finds the single existing default/master thread for a key (marker + denormalized regarding pointer),
    /// or null when none exists yet (→ lazy create). The idempotency query that keeps Tier 2/Tier 3 to at
    /// most one thread per record / per user.
    /// </summary>
    /// <remarks>
    /// #1629: <c>sprk_regardingrecordid</c> and <c>sprk_isdefaultthread</c> are plain columns any user with Write on a
    /// thread can set through the Web API, so on their own they say nothing about which record a thread is for. A Tier-2
    /// per-record default is therefore found only when it is Record-Anchored and its TYPED regarding lookup for
    /// <paramref name="keyType"/> (the one Dataverse checks AppendTo on) repeats the key: the rule #1628 applies to a
    /// work assignment's pair, and the anchor rule of <see cref="ThreadAnchorOf"/>. A thread whose pair names the record
    /// without that lookup is never joined, so a message filed under a record is not drawn into a thread its owner pointed
    /// at that record by text alone, and it must carry NO other typed lookup, so its anchor is the key's record by
    /// construction (F-B). A user can still mark a thread of their own this way for a record they can AppendTo and so
    /// misroute its default-thread messages, with no access gained (#1645). The Tier-3 master (key "systemuser", not an
    /// ADR-024 family) has no typed lookup to repeat its key and keeps the marker + pointer match (#1637); it grants nobody.
    /// <para><b>D8 (task 065 fix round 6):</b> with <paramref name="requireOpen"/> (a partner's portal post only) a default
    /// thread is found only when <c>sprk_privacystate</c> reads EXPLICITLY Open; a Private default is skipped and the caller
    /// creates a new Open one. The lookup takes the OLDEST match (<c>createdon</c> ascending), so once an Open default sits
    /// beside a Private one the internal lookup (which does not filter on privacy) still answers with the original
    /// deterministically. Without <paramref name="requireOpen"/> the conditions are those they always were.</para>
    /// </remarks>
    private async Task<Guid?> FindDefaultThreadAsync(string keyType, string keyId, bool requireOpen, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_communicationthread")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
            Criteria =
            {
                // keyId (a record GUID for Tier 2, the owning-user GUID for Tier 3) is globally unique, so the
                // marker + GUID pointer are a sufficient idempotency key. The former
                // 'sprk_regardingrecordtype == keyType' condition filtered on a non-existent thread attribute
                // (the query threw → always "not found" → duplicate/failed creates). RB R3 UAT 2026-07-24.
                Conditions =
                {
                    new ConditionExpression(DefaultThreadMarkerField, ConditionOperator.Equal, true),
                    new ConditionExpression("sprk_regardingrecordid", ConditionOperator.Equal, keyId),
                    // Task 129 (behaviour review F3): an inactive (deactivated) default is never joined — a message put
                    // there would be hidden from the record's panel. None active → a new one is created.
                    new ConditionExpression(StateCodeFilterField, ConditionOperator.Equal, StateActive),
                },
            },
        };

        if (requireOpen)
            query.Criteria.Conditions.Add(new ConditionExpression("sprk_privacystate", ConditionOperator.Equal, PrivacyStateOpen));
        query.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));

        if (!string.Equals(keyType, MasterThreadOwnerKeyType, StringComparison.OrdinalIgnoreCase))
        {
            // Tier 2 (#1629): the typed lookup must repeat the key. A record type with no typed lookup on the thread, or a
            // key that is not a record id, names no default thread: none is joined (one is created, as before).
            var typedField = RegardingFieldMap.FieldFor(keyType);
            if (typedField is null
                || !ThreadRegardingColumns.Contains(typedField, StringComparer.OrdinalIgnoreCase)
                || !Guid.TryParse(keyId, out var recordId)
                || recordId == Guid.Empty)
            {
                return null;
            }

            query.Criteria.Conditions.Add(new ConditionExpression("sprk_threadtype", ConditionOperator.Equal, ThreadTypeRecordAnchored));
            query.Criteria.Conditions.Add(new ConditionExpression(typedField, ConditionOperator.Equal, recordId));

            // #1629 F-B: and NO other typed lookup. ThreadAnchorOf anchors on the FIRST populated lookup in priority order,
            // so a thread carrying this record's lookup beside a higher-priority one (a matter the owner belongs to) would
            // be anchored, owned and named by that other record. With every other lookup empty, the joined thread's anchor
            // is (keyType, key) by construction.
            foreach (var other in ThreadRegardingColumns)
            {
                if (!string.Equals(other, typedField, StringComparison.OrdinalIgnoreCase))
                    query.Criteria.Conditions.Add(new ConditionExpression(other, ConditionOperator.Null));
            }
        }

        var result = await _entityService.RetrieveMultipleAsync(query, ct);
        return result.Entities.Count > 0 ? result.Entities[0].Id : null;
    }

    /// <summary>
    /// The USER the per-user master (Tier 3) is keyed on: the message's owner when a user owns it, else (a team owns it — e.g.
    /// an email un-filed from a record keeps that record's team as owner, task 129) the user who created it. The master's key
    /// type is <c>systemuser</c>, so a team id is never used as one.
    /// </summary>
    private async Task<Guid?> ReadOwnerIdAsync(Guid communicationId, CancellationToken ct)
    {
        var comm = await _entityService.RetrieveAsync(
            "sprk_communication", communicationId, new[] { "ownerid", "createdby" }, ct);
        var owner = comm.GetAttributeValue<EntityReference>("ownerid");
        if (owner is not null && !string.Equals(owner.LogicalName, "team", StringComparison.OrdinalIgnoreCase))
            return owner.Id;
        return comm.GetAttributeValue<EntityReference>("createdby")?.Id ?? owner?.Id;
    }

    private static string BuildDefaultThreadName(RegardingAnchor? anchor)
        => !string.IsNullOrWhiteSpace(anchor?.RecordName) ? anchor!.RecordName! : "Messages";

    /// <summary>
    /// Reads the message's resolved regarding and derives the thread anchor. Prefers the denormalized
    /// polymorphic pointer (<c>sprk_regardingrecordid/type/name/url</c>); falls back to the first typed
    /// <c>sprk_regarding*</c> lookup in ADR-024 priority order (<see cref="RegardingFieldMap"/>). Returns
    /// null when the message has no regarding (e.g. an unassociated chat message → a Direct thread).
    /// </summary>
    private async Task<RegardingAnchor?> ReadRegardingAnchorAsync(Guid communicationId, CancellationToken ct)
    {
        var comm = await _entityService.RetrieveAsync(
            "sprk_communication", communicationId, MessageRegardingColumns().ToArray(), ct);
        return MessageRegardingAnchorOf(comm);
    }

    /// <summary>The message columns <see cref="MessageRegardingAnchorOf"/> reads.</summary>
    private static List<string> MessageRegardingColumns()
    {
        var columns = new List<string>
        {
            "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl",
        };
        columns.AddRange(RegardingFieldMap.AllRegardingFields);
        return columns;
    }

    /// <summary>A message's primary regarding: its first typed ADR-024 lookup in priority order (see
    /// <see cref="ReadRegardingAnchorAsync"/>); <c>null</c> when it carries none.</summary>
    private static RegardingAnchor? MessageRegardingAnchorOf(DataverseEntity comm)
    {
        // The record TYPE + specific record come from the TYPED ADR-024 lookup (RegardingFieldMap) — the
        // authoritative relational anchor, in ADR-024 priority order — plus the message's denormalized display
        // name/url. NOTE: sprk_regardingrecordtype on the MESSAGE is a LOOKUP to sprk_recordtype_ref (a
        // record-type reference), NOT a text type-name; reading it as a string was invalid (null, or an
        // InvalidCastException when populated). The typed lookups are the source of truth. RB R3 UAT 2026-07-24.
        var recordName = comm.GetAttributeValue<string>("sprk_regardingrecordname");
        var recordUrl = comm.GetAttributeValue<string>("sprk_regardingrecordurl");
        foreach (var (entityLogicalName, field) in RegardingFieldMap.All)
        {
            if (comm.GetAttributeValue<EntityReference>(field) is { } er && er.Id != Guid.Empty)
            {
                return new RegardingAnchor(
                    er.Id.ToString(),
                    string.IsNullOrWhiteSpace(er.LogicalName) ? entityLogicalName : er.LogicalName,
                    string.IsNullOrWhiteSpace(recordName) ? er.Name : recordName,
                    recordUrl);
            }
        }

        return null;
    }

    private Task AssignThreadAsync(Guid communicationId, Guid threadId, CancellationToken ct) =>
        _entityService.UpdateAsync(
            "sprk_communication",
            communicationId,
            new Dictionary<string, object>
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", threadId),
            },
            ct);

    private static string BuildTopic(ThreadResolutionRequest request, RegardingAnchor? anchor)
    {
        if (!string.IsNullOrWhiteSpace(request.Message.Subject))
            return request.Message.Subject!;
        if (!string.IsNullOrWhiteSpace(anchor?.RecordName))
            return anchor!.RecordName!;
        return request.ChannelType == CommunicationType.Message ? "Conversation" : "(No Subject)";
    }

    /// <summary>
    /// Naming fallback for <see cref="ReDeriveThreadNameAsync"/> — there is no message/subject context on a
    /// regarding-change re-derive (unlike <see cref="BuildTopic"/>, message-subject-first, used only at
    /// thread CREATE). Prefers the current anchor's display name; falls back to a generic "Conversation"
    /// when the regarding was cleared. Distinct from <see cref="BuildDefaultThreadName"/> (default/master
    /// CREATE fallback "Messages") — deliberately a separate, documented fallback string for this path
    /// (task 071 decision, per root §6 — stated explicitly rather than reusing either existing fallback
    /// without checking whether it fit).
    /// </summary>
    private static string BuildTopicFromRegardingAnchor(RegardingAnchor? anchor)
        => !string.IsNullOrWhiteSpace(anchor?.RecordName) ? anchor!.RecordName! : "Conversation";

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>
    /// Sets the TYPED ADR-024 regarding lookup on a thread entity (RegardingFieldMap: sprk_matter →
    /// sprk_regardingmatter) — the field the by-regarding read filters on (<c>_sprk_regardingmatter_value</c>).
    /// Best-effort / non-throwing: a no-op when the type is not an ADR-024 family (e.g. the Tier-3 master's
    /// "systemuser" key) or the id is not a GUID. There is NO <c>sprk_regardingrecordtype</c> text attribute on
    /// <c>sprk_communicationthread</c> — the thread carries the typed lookups (+ a <c>sprk_regardingrecordtype_ref</c>
    /// lookup) + denormalized <c>sprk_regardingrecordid/name/url</c>. RB R3 UAT 2026-07-24.
    /// </summary>
    private static void SetTypedRegardingLookup(DataverseEntity thread, string? recordType, string? recordId)
    {
        if (string.IsNullOrWhiteSpace(recordType) || string.IsNullOrWhiteSpace(recordId)) return;
        if (RegardingFieldMap.FieldFor(recordType) is not { } field) return;
        if (!Guid.TryParse(recordId, out var id) || id == Guid.Empty) return;
        thread[field] = new EntityReference(recordType, id);
    }

    /// <summary>
    /// Reads a thread's regarding anchor from its TYPED ADR-024 lookups (the authoritative anchor, ADR-024
    /// priority order per RegardingFieldMap) + its denormalized display name/url. Mirrors the message-side
    /// <see cref="ReadRegardingAnchorAsync"/> fallback. Returns null for a record-less/master thread.
    /// </summary>
    private static RegardingAnchor? ReadTypedRegardingAnchorFromThread(DataverseEntity thread)
    {
        var recordName = thread.GetAttributeValue<string>("sprk_regardingrecordname");
        var recordUrl = thread.GetAttributeValue<string>("sprk_regardingrecordurl");
        foreach (var (entityLogicalName, field) in RegardingFieldMap.All)
        {
            if (thread.GetAttributeValue<EntityReference>(field) is { } er && er.Id != Guid.Empty)
            {
                return new RegardingAnchor(
                    er.Id.ToString(),
                    string.IsNullOrWhiteSpace(er.LogicalName) ? entityLogicalName : er.LogicalName,
                    string.IsNullOrWhiteSpace(recordName) ? er.Name : recordName,
                    recordUrl);
            }
        }
        return null;
    }

    private sealed record RegardingAnchor(string RecordId, string RecordType, string? RecordName, string? RecordUrl);

    /// <summary>How a thread's denormalized <c>sprk_regardingrecordid</c> stands against its typed regarding lookups.</summary>
    internal enum ThreadPairAgreement
    {
        /// <summary>The pair is empty.</summary>
        NoPair,

        /// <summary>A typed regarding lookup on the thread names the same record.</summary>
        RepeatedByTypedLookup,

        /// <summary>The pair names a record no typed regarding lookup on the thread names, or is not a record id.</summary>
        NotRepeated,
    }

    /// <summary>A thread's anchor: the record its typed regarding lookup names (both null when none), and its pair's standing.</summary>
    internal readonly record struct ThreadAnchor(string? RecordType, Guid? RecordId, ThreadPairAgreement Pair, string? PairText)
    {
        /// <summary>A typed regarding lookup anchors the thread.</summary>
        public bool HasAnchor => RecordType is not null && RecordId is not null;
    }

    /// <summary>
    /// The ONE thread-anchor rule (#1629): a thread is anchored to the record its TYPED ADR-024 regarding lookup names, the
    /// first populated in <see cref="RegardingFieldMap"/> priority order: the anchor its owner is derived from (task 146)
    /// and its name re-derived from. Dataverse checks AppendTo on a lookup; the denormalized <c>sprk_regardingrecordid</c>
    /// is text any user with Write on the thread can set through the Web API, so it never anchors a thread on its own. It
    /// counts only when a typed lookup on the thread repeats it (#1628's rule), and then it adds nothing that lookup does
    /// not already say. A thread with a pair and no typed lookup has NO anchor (fail closed). <see cref="ThreadAnchor.Pair"/>
    /// reports the pair's standing so a reader can log a pair that is not repeated.
    /// </summary>
    internal static ThreadAnchor ThreadAnchorOf(DataverseEntity thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        var typed = ReadTypedRegardingAnchorFromThread(thread);
        Guid? anchorId = typed is not null && Guid.TryParse(typed.RecordId, out var parsed) && parsed != Guid.Empty ? parsed : null;
        var pairText = thread.GetAttributeValue<string>("sprk_regardingrecordid");

        ThreadPairAgreement pair;
        if (string.IsNullOrWhiteSpace(pairText))
        {
            pair = ThreadPairAgreement.NoPair;
        }
        else if (Guid.TryParse(pairText.Trim(), out var pairId)
                 && pairId != Guid.Empty
                 && RegardingFieldMap.All.Any(x =>
                     thread.GetAttributeValue<EntityReference>(x.RegardingField) is { } er && er.Id == pairId))
        {
            pair = ThreadPairAgreement.RepeatedByTypedLookup;
        }
        else
        {
            pair = ThreadPairAgreement.NotRepeated;
        }

        return new ThreadAnchor(anchorId is null ? null : typed!.RecordType, anchorId, pair, pairText);
    }
}
