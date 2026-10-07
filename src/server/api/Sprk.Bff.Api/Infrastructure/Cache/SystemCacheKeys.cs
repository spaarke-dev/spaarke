namespace Sprk.Bff.Api.Infrastructure.Cache;

/// <summary>
/// Allow-list of system-level (non-tenant-scoped) cache key resources per
/// <see href="../../../../../projects/spaarke-redis-cache-remediation-r1/spec.md">spaarke-redis-cache-remediation-r1 NFR-08</see>.
/// </summary>
/// <remarks>
/// <para>
/// The default cache contract is <see cref="ITenantCache"/> which mandates a non-empty
/// tenantId on every key. A small, audited set of cache sites legitimately operates at
/// system (cross-tenant) scope because the cached resource has no tenant context in scope
/// at the call site, the key is intrinsically system-level (e.g., a content hash, a
/// Service Bus message ID, a Dataverse-org-wide schema entry), or the data is an
/// org-wide aggregate by design.
/// </para>
/// <para>
/// <b>Adding to this list requires architecture review.</b> The spec caps the total at
/// 20 distinct logical resources (Assumption §3 / NFR-08); the current allow-list contains
/// 15 entries (the two scheduler keys added 2026-09-14 by unified-access-control-r2 task 103; <see cref="JobStatusSequence"/>
/// added 2026-10-01 by spaarkeai-word-add-in-r1 task 068 and approved by the owner on review 2026-10-02, its
/// justification in its own remarks; see
/// <c>projects/spaarke-redis-cache-remediation-r1/notes/system-cache-exceptions.md</c> for the earlier per-exception
/// three-question justification).
/// </para>
/// <para>
/// AI wrappers that use the <c>"system"</c> tenant sentinel against <see cref="ITenantCache"/>
/// must use one of the <c>SystemTenantSentinel</c>-paired resource constants below
/// (<see cref="Embedding"/>, <see cref="PlaybookByName"/>, <see cref="DocText"/>) so the
/// on-wire key shape (<c>spaarke:tenant:system:{resource}:{id}:v{version}</c>) is
/// auditable from a single source.
/// </para>
/// </remarks>
public static class SystemCacheKeys
{
    /// <summary>
    /// Sentinel tenant ID used by AI wrappers when calling <see cref="ITenantCache"/> for
    /// a resource that is intrinsically system-level (no tenantId in scope at the call site).
    /// On-wire key shape: <c>spaarke:tenant:system:{resource}:{id}:v{version}</c>.
    /// </summary>
    public const string SystemTenantSentinel = "system";

    // ---- Job / idempotency infrastructure ---------------------------------

    /// <summary>
    /// Per-unit job idempotency marker ("this unit of work was already done").
    /// Site: <c>Services/Jobs/IdempotencyService.cs</c>. Raw key: <c>idempotency:processed:{eventId}</c>.
    /// Justification: the ids are system-level — Service Bus message IDs for the queue handlers, and per-unit keys
    /// built by scheduled jobs from Dataverse record ids (e.g. <c>GrantExpiryReminderJob</c>'s
    /// <c>grant-expiry-reminder:{grantId}:{expiry}:{threshold}</c>, task 100). The unit, not a tenant, is what
    /// must happen exactly once, so tenant-scoping would break the invariant.
    /// </summary>
    public const string IdempotencyProcessed = "idempotency-processed";

    /// <summary>
    /// Per-unit job claim (cross-instance mutual exclusion) — Service Bus handlers and scheduled jobs alike.
    /// Site: <c>Services/Jobs/IdempotencyService.cs</c>. Raw key: <c>idempotency:lock:{eventId}</c>.
    /// Justification: lock semantics must be tenant-agnostic so any worker can acquire/release.
    /// </summary>
    public const string IdempotencyLock = "idempotency-lock";

    /// <summary>
    /// Email-batch job status record (Pending / Running / Completed / Failed).
    /// Site: <c>Services/Jobs/BatchJobStatusStore.cs</c>. Raw key: <c>batch:job:{jobId}</c>.
    /// Justification: <c>JobContract</c> carries no tenantId; batch-job IDs are system-level GUIDs.
    /// </summary>
    public const string BatchJob = "batch-job";

    /// <summary>
    /// Per-entity-type watermark for Dataverse RecordSyncJob.
    /// Site: <c>Services/Jobs/RecordSyncJob.cs</c>. Raw key: <c>recordsync:watermark:{entityType}</c>.
    /// Justification: watermark is a durable system-wide bookmark; tenant-scoping would
    /// fragment the bookmark and re-process records across tenants.
    /// </summary>
    public const string RecordSyncWatermark = "recordsync-watermark";

    /// <summary>
    /// Scheduled-job dispatch lease (cross-instance mutual exclusion — ADR-036 A1 rule 1, unified-access-control-r2
    /// task 103). Site: <c>Infrastructure/Scheduling/RedisScheduledJobLease.cs</c>. Raw key:
    /// <c>{InstanceName}scheduler:lease:{jobId}</c>.
    /// Justification: a scheduled job runs for the whole BFF, not for a tenant; like <see cref="IdempotencyLock"/>
    /// the lock must be tenant-agnostic so any instance can take and release it.
    /// <para>Second site, same key family (unified-access-control-r2 task 143, catalogued in r2): the No Access
    /// enforcer's per-record removal lock, <c>Infrastructure/ExternalAccess/NoAccessShareEnforcer.cs</c>. Raw key:
    /// <c>{InstanceName}scheduler:lease:no-access-enforce:{table}:{recordId}</c>. A keyed mutex over the same store, never
    /// a job's dispatch key (no job id starts with <c>no-access-enforce:</c>); it serializes owner S5's "someone else keeps
    /// access" check across instances, so it must be tenant-agnostic for the same reason. The reuse is a recorded §6.5
    /// path-A exception to ADR-036 A1-7 (<c>projects/unified-access-control-r2/design.md</c> §9).</para>
    /// </summary>
    public const string SchedulerLease = "scheduler-lease";

    /// <summary>
    /// The last dispatched cron occurrence per scheduled job (task 103). Site:
    /// <c>Infrastructure/Scheduling/RedisScheduledJobLease.cs</c>. Raw key: <c>{InstanceName}scheduler:last-fire:{jobId}</c>.
    /// Justification: it is what makes a tick run once across instances when a short run releases its lease before a
    /// slower instance wakes; like <see cref="RecordSyncWatermark"/> it is a system-wide bookmark.
    /// </summary>
    public const string SchedulerLastFire = "scheduler-last-fire";

    /// <summary>
    /// The SSE event number line of one Office job (task 068, #1086). Site: <c>Services/Office/JobStatusService.cs</c>.
    /// Raw key: <c>sdap:job:{jobId}:seq</c> (Redis <c>INCR</c>, 24 h sliding expiry), beside that job's own pub/sub
    /// channel <c>sdap:job:{jobId}:status</c>.
    /// Justification: (1) existing — it replaced a per-instance in-memory dictionary, which numbered one job's events
    /// independently on each instance and after every restart; (2) a tenant key would not help — the job's GUID is the
    /// unit, and every instance publishing for that job must share one counter; (3) without it a <c>Last-Event-ID</c>
    /// reconnect skips or repeats events.
    /// Architecture review: approved by the owner, 2026-10-02.
    /// </summary>
    public const string JobStatusSequence = "job-status-sequence";

    // ---- Authentication & token caches ------------------------------------

    /// <summary>
    /// On-Behalf-Of Graph access-token cache (user-scoped via SHA256 hash of user token).
    /// Site: <c>Services/GraphTokenCache.cs</c>. Raw key: <c>sdap:graph:token:{sha256(userToken)}</c>.
    /// Justification: the user-token implicitly identifies its issuing tenant and the
    /// caller (<c>GraphClientFactory</c>) does not have tenantId in scope.
    /// </summary>
    public const string GraphToken = "graph-token";

    // ---- Org-wide Dataverse / SPE configuration ---------------------------

    /// <summary>
    /// Dataverse entity-metadata schema cache (org-wide; one BFF per org per ADR-029).
    /// Site: <c>Services/Dataverse/MetadataService.cs</c>. Raw key: <c>sdap:dv:entitymetadata:{logicalName}</c>.
    /// Justification: entity metadata is org-wide configuration; tenant-scoping would
    /// defeat the purpose of the schema cache.
    /// </summary>
    public const string DataverseEntityMetadata = "dv-entity-metadata";

    /// <summary>
    /// The org's entity catalog as the securable-entity registry derives it from ONE live metadata query:
    /// every entity LOGICAL NAME in the org, and the subset carrying <c>sprk_issecure</c>
    /// (unified-access-control-r2 task 075; the known-entity half added by task 151 / #1038).
    /// Site: <c>Infrastructure/Dataverse/SecurableEntityRegistry.cs</c>. Raw key:
    /// <c>sdap:dv:dv-securable-entities:v2</c> (<c>SecurableEntityRegistry.CacheKey</c>).
    /// Justification: like <see cref="DataverseEntityMetadata"/> this is org-wide SCHEMA, not per-tenant
    /// data — which entities exist and which can be marked secure is a property of the solution, identical
    /// for every caller, so tenant-scoping would defeat the cache without changing any answer. The cached
    /// value is entity LOGICAL NAMES only; no record data, no container ids, nothing caller-specific.
    /// Versioning: <c>:v2</c> because the unversioned key holds the previous build's bare securable-names
    /// array, which must never be read as the known-entity set; the value is also shape-checked on read.
    /// Fail-closed note: an EMPTY set is deliberately never written to this key — an empty set is
    /// indistinguishable from a failed metadata query, and caching it would make every record read as
    /// non-secure (or every entity read as unknown) for the 6h TTL.
    /// </summary>
    public const string DataverseSecurableEntities = "dv-securable-entities";

    /// <summary>
    /// SPE dashboard cross-tenant metrics aggregate.
    /// Site: <c>Services/SpeAdmin/SpeDashboardSyncService.cs</c>. Raw key: <c>sdap:spe:dashboard:metrics</c>.
    /// Justification: dashboard metrics aggregate across all tenants/containers in the
    /// BFF org; cross-tenant aggregation is the intentional shape of the metric.
    /// </summary>
    public const string SpeDashboardMetrics = "spe-dashboard-metrics";

    /// <summary>
    /// Communication-account merged list (CommunicationOptions + sprk_communicationaccount).
    /// Site: <c>Services/Communication/ApprovedSenderValidator.cs</c>. Raw key: <c>communication:accounts:merged</c>.
    /// Justification: approved-senders is org-wide configuration; one BFF per org per ADR-029.
    /// </summary>
    public const string CommApprovedSenders = "comm-approved-senders";

    /// <summary>
    /// Communication-account send/receive-enabled flag caches.
    /// Site: <c>Services/Communication/CommunicationAccountService.cs</c>. Raw keys:
    /// <c>comm:accounts:send-enabled</c>, <c>comm:accounts:receive-enabled</c>.
    /// Justification: send/receive-enabled flags are org-wide; invalidation must match
    /// set-path tenant scoping (none).
    /// </summary>
    public const string CommAccountFlags = "comm-account-flags";

    // ---- AI wrappers using "system" sentinel against ITenantCache --------

    /// <summary>
    /// Content-addressed embedding cache. Used by <c>EmbeddingCache</c>.
    /// On-wire key: <c>spaarke:tenant:system:embedding:{contentHash}:v1</c>.
    /// Justification: embeddings are deterministic for same (content, model); the public API
    /// takes only a contentHash and has no tenantId in scope at the call site.
    /// </summary>
    public const string Embedding = "embedding";

    /// <summary>
    /// Playbook-by-name lookup cache. Used by <c>PlaybookService</c>.
    /// On-wire key: <c>spaarke:tenant:system:playbook-by-name:{name}:v1</c>.
    /// Justification: playbook-by-name lookup is org-wide per ADR-029 (single Redis per
    /// BFF org); the public API takes only a name and has no tenantId in scope.
    /// </summary>
    public const string PlaybookByName = "playbook-by-name";

    /// <summary>
    /// Document-text extraction cache (Document Intelligence results).
    /// Used by <c>TextExtractorService</c>. On-wire key:
    /// <c>spaarke:tenant:system:doc-text:{driveId}:{itemId}:{etag}:v1</c>.
    /// Justification: the SPE drive+item+etag tuple is already a content-versioned
    /// identifier (ETag changes auto-invalidate); the public API has no tenantId in scope.
    /// </summary>
    public const string DocText = "doc-text";
}
