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

    private readonly IGenericEntityService _entityService;
    private readonly IReadOnlyDictionary<CommunicationType, IThreadKeyStrategy> _strategies;
    private readonly ILogger<ThreadResolver> _logger;

    public ThreadResolver(
        IEnumerable<IThreadKeyStrategy> strategies,
        IGenericEntityService entityService,
        ILogger<ThreadResolver> logger)
    {
        _entityService = entityService;
        // Fail fast on a duplicate channel registration (mirrors CommunicationChannelDispatcher's guard).
        _strategies = strategies.ToDictionary(s => s.SupportedType);
        _logger = logger;
    }

    public async Task<Guid?> ResolveAndAssignThreadAsync(ThreadResolutionRequest request, CancellationToken ct = default)
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

            // JOIN — an existing thread was found; assign it and we're done.
            if (resolution.ExistingThreadId is { } existing)
            {
                await AssignThreadAsync(request.CommunicationId, existing, ct);
                _logger.LogInformation(
                    "Joined existing thread {ThreadId} | CommunicationId: {CommunicationId}, Channel: {ChannelType}",
                    existing, request.CommunicationId, request.ChannelType);
                return existing;
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
            var threadId = await CreateThreadAsync(request, ct);
            await strategy.OnThreadCreatedAsync(threadId, request, ct);
            await AssignThreadAsync(request.CommunicationId, threadId, ct);
            _logger.LogInformation(
                "Created thread {ThreadId} | CommunicationId: {CommunicationId}, Channel: {ChannelType}",
                threadId, request.CommunicationId, request.ChannelType);
            return threadId;
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

    private async Task<Guid> CreateThreadAsync(ThreadResolutionRequest request, CancellationToken ct)
    {
        var anchor = await ReadRegardingAnchorAsync(request.CommunicationId, ct);

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
            thread["sprk_regardingrecordtype"] = TruncateTo(anchor.RecordType, 100);
            if (!string.IsNullOrWhiteSpace(anchor.RecordName))
                thread["sprk_regardingrecordname"] = TruncateTo(anchor.RecordName, 400);
            if (!string.IsNullOrWhiteSpace(anchor.RecordUrl))
                thread["sprk_regardingrecordurl"] = TruncateTo(anchor.RecordUrl, 400);
        }

        return await _entityService.CreateAsync(thread, ct);
    }

    /// <summary>
    /// FR-09 auto-threading ladder — invoked only on the Tier-1 miss (the former SKIP→null path). Guarantees
    /// a non-null thread: Tier 2 (per-record default, ONE per regarding record, created lazily) when the
    /// message has a resolved ADR-024 regarding; otherwise Tier 3 (per-user master catch-all, ONE per owning
    /// user). Runs INSIDE the caller's NFR-02 try/catch, so any Dataverse failure here is caught upstream and
    /// returned as null (the message persists without a thread) rather than throwing to the send/capture path.
    /// </summary>
    private async Task<Guid?> ResolveViaFallbackLadderAsync(ThreadResolutionRequest request, CancellationToken ct)
    {
        // Tier 2 — per-record default. REUSE the SAME ADR-024 regarding the resolver already reads for the
        // Tier-1 CREATE anchor (ReadRegardingAnchorAsync) — no second regarding mechanism (ADR-024/ADR-046).
        var anchor = await ReadRegardingAnchorAsync(request.CommunicationId, ct);
        if (anchor is not null)
        {
            var recordThreadId = await FindOrCreateDefaultThreadAsync(anchor.RecordType, anchor.RecordId, anchor, ct);
            await AssignThreadAsync(request.CommunicationId, recordThreadId, ct);
            _logger.LogInformation(
                "Resolved per-record default thread {ThreadId} (Tier 2) | CommunicationId: {CommunicationId}, Regarding: {RecordType}:{RecordId}",
                recordThreadId, request.CommunicationId, anchor.RecordType, anchor.RecordId);
            return recordThreadId;
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
            MasterThreadOwnerKeyType, owner.ToString(), anchor: null, ct);
        await AssignThreadAsync(request.CommunicationId, masterThreadId, ct);
        _logger.LogInformation(
            "Resolved per-user master thread {ThreadId} (Tier 3) | CommunicationId: {CommunicationId}, Owner: {OwnerId}",
            masterThreadId, request.CommunicationId, owner);
        return masterThreadId;
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
            var columns = new[]
            {
                "sprk_name", NameIsAutoDerivedField,
                "sprk_regardingrecordtype", "sprk_regardingrecordid", "sprk_regardingrecordname", "sprk_regardingrecordurl",
            };
            var thread = await _entityService.RetrieveAsync("sprk_communicationthread", threadId, columns, ct);

            var recordType = thread.GetAttributeValue<string>("sprk_regardingrecordtype");

            // Master-thread guard — MUST NOT attempt to resolve "systemuser" as a regarding record (070 flag).
            if (string.Equals(recordType, MasterThreadOwnerKeyType, StringComparison.OrdinalIgnoreCase))
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

            var recordId = thread.GetAttributeValue<string>("sprk_regardingrecordid");
            var recordName = thread.GetAttributeValue<string>("sprk_regardingrecordname");
            var recordUrl = thread.GetAttributeValue<string>("sprk_regardingrecordurl");
            var anchor = !string.IsNullOrWhiteSpace(recordType) && !string.IsNullOrWhiteSpace(recordId)
                ? new RegardingAnchor(recordId, recordType, recordName, recordUrl)
                : null;

            var newName = TruncateTo(BuildTopicFromRegardingAnchor(anchor), 200);
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
                threadId, newName, recordType, recordId);
        }
        catch (Exception ex)
        {
            // NFR-02: best-effort / non-fatal — never fail the caller.
            _logger.LogWarning(ex, "Thread name re-derive failed (non-fatal) | ThreadId: {ThreadId}", threadId);
        }
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
        string keyType, string keyId, RegardingAnchor? anchor, CancellationToken ct)
    {
        if (await FindDefaultThreadAsync(keyType, keyId, ct) is { } existing)
            return existing;

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
            // Denormalized regarding pointer doubles as the idempotency key (queried above before create).
            ["sprk_regardingrecordtype"] = TruncateTo(keyType, 100),
            ["sprk_regardingrecordid"] = TruncateTo(keyId, 100),
        };

        // Copy the regarding display fields only for a real record anchor (Tier 2) — REUSE of the ADR-024
        // regarding family, identical to CreateThreadAsync. The Tier-3 master carries no display name/url.
        if (anchor is not null)
        {
            if (!string.IsNullOrWhiteSpace(anchor.RecordName))
                thread["sprk_regardingrecordname"] = TruncateTo(anchor.RecordName, 400);
            if (!string.IsNullOrWhiteSpace(anchor.RecordUrl))
                thread["sprk_regardingrecordurl"] = TruncateTo(anchor.RecordUrl, 400);
        }

        return await _entityService.CreateAsync(thread, ct);
    }

    /// <summary>
    /// Finds the single existing default/master thread for a key (marker + denormalized regarding pointer),
    /// or null when none exists yet (→ lazy create). The idempotency query that keeps Tier 2/Tier 3 to at
    /// most one thread per record / per user.
    /// </summary>
    private async Task<Guid?> FindDefaultThreadAsync(string keyType, string keyId, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_communicationthread")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression(DefaultThreadMarkerField, ConditionOperator.Equal, true),
                    new ConditionExpression("sprk_regardingrecordtype", ConditionOperator.Equal, keyType),
                    new ConditionExpression("sprk_regardingrecordid", ConditionOperator.Equal, keyId),
                },
            },
        };

        var result = await _entityService.RetrieveMultipleAsync(query, ct);
        return result.Entities.Count > 0 ? result.Entities[0].Id : null;
    }

    /// <summary>Reads the owning user of the persisted communication (keys the Tier-3 per-user master).</summary>
    private async Task<Guid?> ReadOwnerIdAsync(Guid communicationId, CancellationToken ct)
    {
        var comm = await _entityService.RetrieveAsync(
            "sprk_communication", communicationId, new[] { "ownerid" }, ct);
        return comm.GetAttributeValue<EntityReference>("ownerid")?.Id;
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
        var columns = new List<string>
        {
            "sprk_regardingrecordid", "sprk_regardingrecordtype", "sprk_regardingrecordname", "sprk_regardingrecordurl",
        };
        columns.AddRange(RegardingFieldMap.AllRegardingFields);

        var comm = await _entityService.RetrieveAsync("sprk_communication", communicationId, columns.ToArray(), ct);

        var recordType = comm.GetAttributeValue<string>("sprk_regardingrecordtype");
        var recordId = comm.GetAttributeValue<string>("sprk_regardingrecordid");
        if (!string.IsNullOrWhiteSpace(recordType) && !string.IsNullOrWhiteSpace(recordId))
        {
            return new RegardingAnchor(
                recordId,
                recordType,
                comm.GetAttributeValue<string>("sprk_regardingrecordname"),
                comm.GetAttributeValue<string>("sprk_regardingrecordurl"));
        }

        // Fall back to the first typed regarding lookup present (ADR-024 priority order).
        foreach (var (entityLogicalName, field) in RegardingFieldMap.All)
        {
            if (comm.GetAttributeValue<EntityReference>(field) is { } er)
            {
                return new RegardingAnchor(
                    er.Id.ToString(),
                    string.IsNullOrWhiteSpace(er.LogicalName) ? entityLogicalName : er.LogicalName,
                    er.Name,
                    RecordUrl: null);
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

    private sealed record RegardingAnchor(string RecordId, string RecordType, string? RecordName, string? RecordUrl);
}
