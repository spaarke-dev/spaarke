using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Server-side helper that mirrors the semantics of
/// <c>PolymorphicResolverService.applyResolverFields</c> for the <c>sprk_todo</c>
/// entity (smart-todo-decoupling-r3, ADR-024), and, through the same code, for every host table in
/// <see cref="RegardingLookupsByHostTable"/> (spaarke-ontology-platform-r1 task 141, D-133).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Host tables (task 141, D-133).</strong> The one regarding builder serves <c>sprk_todo</c>,
/// <c>sprk_signal</c> and <c>sprk_decisionrecord</c>. Each host table has its own map of supported regarding
/// parents to its own specific lookups (<see cref="RegardingLookupsByHostTable"/>); everything else (the
/// ADR-024 mutual-exclusion guard, the resolver pair, the record URL, the untyped-pair tolerance and the
/// core-ancestor stamp) is the same code for every host. <c>sprk_todo</c> behaviour is unchanged.
/// <c>sprk_event</c> is not a host here: <c>TaskActionCore</c> owns its typed lookup and reuses only
/// <see cref="ApplyResolverPairAsync"/>.
/// </para>
///
/// <para>
/// <strong>Untyped pair.</strong> A parent whose table has no active <c>sprk_recordtype_ref</c> row (on
/// spaarkedev1, <c>sprk_communication</c>) gets the pair without a type: id, name and url only. That is a
/// valid pair, never an error (D-133), on every host. A catalog read that FAULTS is not "no row": it throws, is never
/// cached, and the caller does not create the row (task 141 review F1).
/// </para>
///
/// <para>
/// <c>sprk_todo</c> has 11 entity-specific regarding lookups
/// (<c>sprk_regardingmatter</c>, <c>sprk_regardingproject</c>, <c>sprk_regardingevent</c>,
/// <c>sprk_regardingcommunication</c>, <c>sprk_regardingworkassignment</c>,
/// <c>sprk_regardinginvoice</c>, <c>sprk_regardingbudget</c>, <c>sprk_regardinganalysis</c>,
/// <c>sprk_regardingorganization</c>, <c>sprk_regardingcontact</c>,
/// <c>sprk_regardingdocument</c>) and 4 denormalized resolver fields
/// (<c>sprk_regardingrecordtype</c>, <c>sprk_regardingrecordid</c>,
/// <c>sprk_regardingrecordname</c>, <c>sprk_regardingrecordurl</c>).
/// </para>
///
/// <para>
/// <strong>ADR-024 invariants enforced here:</strong>
/// </para>
/// <list type="bullet">
///   <item>At MOST ONE specific regarding lookup is populated at a time.</item>
///   <item>The four resolver fields are populated atomically alongside the
///         specific lookup — never independently.</item>
/// </list>
///
/// <para>
/// <strong>Portability:</strong> No hard-coded org URLs or tenant ids. Record-URL is
/// built relative to <c>/main.aspx</c>; the model-driven app resolves the host
/// origin at click time. <c>sprk_recordtype_ref</c> resolution goes through the
/// injected <see cref="IDataverseService"/> (which is backed by
/// <c>ServiceClient</c> / <c>IOrganizationService</c>).
/// </para>
/// </remarks>
internal sealed class TodoRegardingBuilder
{
    // ─────────────────────────────────────────────────────────────────────────
    // Field name + entity-set-name table
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Map of supported regarding parent entities to the matching
    /// <c>sprk_todo</c> specific lookup attribute.
    /// </summary>
    /// <remarks>
    /// Mirrors the 11-entity contract from
    /// <c>src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md</c>.
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, string> RegardingLookupByEntity =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = "sprk_regardingmatter",
            ["sprk_project"] = "sprk_regardingproject",
            ["sprk_event"] = "sprk_regardingevent",
            ["sprk_communication"] = "sprk_regardingcommunication",
            ["sprk_workassignment"] = "sprk_regardingworkassignment",
            ["sprk_invoice"] = "sprk_regardinginvoice",
            ["sprk_budget"] = "sprk_regardingbudget",
            ["sprk_analysis"] = "sprk_regardinganalysis",
            ["sprk_organization"] = "sprk_regardingorganization",
            ["contact"] = "sprk_regardingcontact",
            ["sprk_document"] = "sprk_regardingdocument",
        };

    /// <summary>
    /// All 11 specific regarding lookup attribute names on <c>sprk_todo</c>.
    /// Used for the multi-lookup guard.
    /// </summary>
    internal static readonly IReadOnlySet<string> AllRegardingLookups =
        new HashSet<string>(RegardingLookupByEntity.Values, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The 10 specific regarding lookups that <c>sprk_signal</c> and <c>sprk_decisionrecord</c> both carry, keyed by
    /// parent entity (live schema on spaarkedev1 after task 140, <c>notes/140-schema-delta.md</c> section 3; solution
    /// source <c>src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Signal/Entity.xml</c> and <c>sprk_DecisionRecord/Entity.xml</c>).
    /// ebilling-r1 task 026 (FR-15, D-31): <c>sprk_regardinginvoiceline</c> → <c>sprk_billingevent</c> (the Invoice Line,
    /// D-18), live on spaarkedev1 2026-10-16 (<c>scripts/ebilling/Deploy-EbillingSignalLineSubjectSchema.ps1</c>).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> SignalAndDecisionRecordRegardingLookupByEntity =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = "sprk_regardingmatter",
            ["sprk_communication"] = "sprk_regardingcommunication",
            ["sprk_project"] = "sprk_regardingproject",
            ["sprk_servicerequest"] = "sprk_regardingservicerequest",
            ["sprk_workassignment"] = "sprk_regardingworkassignment",
            ["sprk_todo"] = "sprk_regardingtodo",
            ["sprk_event"] = "sprk_regardingevent",
            ["sprk_invoice"] = "sprk_regardinginvoice",
            ["sprk_document"] = "sprk_regardingdocument",
            ["sprk_billingevent"] = "sprk_regardinginvoiceline",
        };

    /// <summary>
    /// The host tables this builder writes the regarding association on, each with its own parent → specific lookup
    /// map (task 141, D-133). The ADR-024 mutual-exclusion guard on a host checks that host's own lookups.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RegardingLookupsByHostTable =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_todo"] = RegardingLookupByEntity,
            ["sprk_signal"] = SignalAndDecisionRecordRegardingLookupByEntity,
            ["sprk_decisionrecord"] = SignalAndDecisionRecordRegardingLookupByEntity,
        };

    /// <summary>
    /// The 4 denormalized resolver field names on <c>sprk_todo</c>.
    /// </summary>
    internal const string FieldRegardingRecordType = "sprk_regardingrecordtype";
    internal const string FieldRegardingRecordId = "sprk_regardingrecordid";
    internal const string FieldRegardingRecordName = "sprk_regardingrecordname";
    internal const string FieldRegardingRecordUrl = "sprk_regardingrecordurl";

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ICommunicationDataverseService _communicationService;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly ILogger _logger;

    /// <summary>
    /// Lazy cache for <c>sprk_recordtype_ref</c> lookups (entity logical name → GUID + display name). Concurrent since ontology
    /// task 042 (K2): the Decision Record writer, a singleton called from many enrichment runs, keeps ONE builder so its catalog reads
    /// are paid once per type, not once per record.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Guid Id, string DisplayName)?> _recordTypeRefCache = new();

    /// <param name="communicationService">The <c>sprk_recordtype_ref</c> lookup (<c>QueryRecordTypeRefAsync</c>).</param>
    /// <param name="coreAncestors">The FR-26 core-ancestor stamp (step 3 of <see cref="ApplyResolverFieldsAsync"/>).</param>
    /// <param name="logger">
    /// Any logger: <c>TodoGenerationService</c> passes its <c>ILogger&lt;TodoRegardingBuilder&gt;</c>; <c>TaskActionCore</c>,
    /// which reuses <see cref="ApplyResolverPairAsync"/> for <c>sprk_event</c>, passes its own (owner round 8 item 2).
    /// </param>
    public TodoRegardingBuilder(
        ICommunicationDataverseService communicationService,
        CoreAncestorResolver coreAncestors,
        ILogger logger)
    {
        _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Atomically apply the regarding-parent association to a <c>sprk_todo</c> (or another host table in
    /// <see cref="RegardingLookupsByHostTable"/>) <see cref="Entity"/> in flight: the specific lookup, the resolver
    /// pair and the FR-26 core-ancestor stamp.
    /// </summary>
    /// <param name="todoEntity">The host entity being built (mutated in place): <c>sprk_todo</c>, <c>sprk_signal</c> or
    /// <c>sprk_decisionrecord</c>.</param>
    /// <param name="regardingEntityName">Parent entity logical name (e.g. <c>sprk_matter</c>).</param>
    /// <param name="regardingId">Parent record GUID.</param>
    /// <param name="regardingDisplayName">Parent display name (for <c>sprk_regardingrecordname</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Any required argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Empty <paramref name="regardingId"/>, a <paramref name="regardingEntityName"/> the host table has no lookup for,
    /// or a host table that is not in <see cref="RegardingLookupsByHostTable"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A specific regarding lookup is already populated on <paramref name="todoEntity"/>
    /// (mutual-exclusion guard per ADR-024), or the core-ancestor stamp could not be derived.
    /// </exception>
    public async Task ApplyResolverFieldsAsync(
        Entity todoEntity,
        string regardingEntityName,
        Guid regardingId,
        string regardingDisplayName,
        CancellationToken ct = default)
    {
        // 1) Specific lookup + 2) resolver fields (4), populated atomically.
        await ApplyLookupAndPairAsync(
            todoEntity, nameof(todoEntity), regardingEntityName, regardingId, regardingDisplayName, ct);

        // 3) FR-26 core-ancestor stamp — LAST, so it can never be overwritten by steps 1–2, and so the
        //    ADR-024 mutual-exclusion guard above (which fires on an ALREADY-SET lookup) does not mistake
        //    an ancestor stamp for a competing user choice. A CORE target stamps only itself and is
        //    skipped here as already bound by step 1; a CHILD target (event / communication / invoice /
        //    analysis / document — 5 of the 11 supported parents) additionally carries its own core
        //    ancestor onto this to-do. Without it, a to-do filed under a communication is invisible to
        //    every principal whose access comes from that communication's matter.
        var stamp = await _coreAncestors
            .StampAsync(todoEntity, regardingEntityName, regardingId, ct)
            .ConfigureAwait(false);

        if (!stamp.Succeeded)
        {
            // NFR-01 fail-closed. This method's existing error contract is to throw, and its callers
            // already treat a throw as "do not create the to-do" — so an unstamped row cannot be written
            // by this path.
            throw new InvalidOperationException(
                $"Core-ancestor derivation failed for regarding target '{regardingEntityName}' " +
                $"({regardingId:D}); refusing to write an unstamped {todoEntity.LogicalName} (FR-26 / NFR-01). " +
                stamp.Error);
        }

        _logger.LogDebug(
            "Applied resolver fields to {HostEntity}: Entity={Entity}, Id={Id}, Name={Name}, AncestorStatus={Status}",
            todoEntity.LogicalName, regardingEntityName, regardingId.ToString("D").ToLowerInvariant(), regardingDisplayName, stamp.Status);
    }

    /// <summary>
    /// Steps 1 and 2 of <see cref="ApplyResolverFieldsAsync"/> only: the host table's specific lookup for the parent
    /// plus the resolver pair (<see cref="ApplyResolverPairAsync"/>), under the same validation and the same ADR-024
    /// mutual-exclusion guard. Writes NO core-ancestor stamp.
    /// </summary>
    /// <remarks>
    /// For a writer that derives its row's grouping core itself and must not have this builder stamp the row:
    /// <c>SignalWriter</c> takes the core from <c>CoreAncestorResolver.ResolveStampsAsync</c> and writes it as the immutable
    /// <c>sprk_corerecordtype</c>/<c>sprk_corerecordid</c> context (D-34, D-36); the Signal's live stamp belongs to the
    /// restamper registration (task 144). Same exceptions as <see cref="ApplyResolverFieldsAsync"/>, except that there is
    /// no stamp to fail.
    /// <para>Only <see cref="SubjectLookupHostTables"/> (<c>sprk_signal</c>, <c>sprk_decisionrecord</c>): any other host
    /// throws <see cref="ArgumentException"/>. A <c>sprk_todo</c> writer that used this would silently skip the FR-26 stamp
    /// and the task-173 inherited Access Permission, which fails open; To Dos go through
    /// <see cref="ApplyResolverFieldsAsync"/> (task 141 review K1, uac-r2).</para>
    /// </remarks>
    internal async Task ApplySubjectLookupAndPairAsync(
        Entity host,
        string regardingEntityName,
        Guid regardingId,
        string? regardingDisplayName,
        CancellationToken ct = default)
    {
        if (host is not null && !SubjectLookupHostTables.Contains(host.LogicalName ?? string.Empty))
        {
            throw new ArgumentException(
                $"ApplySubjectLookupAndPairAsync writes no core-ancestor stamp, so it only serves " +
                $"{string.Join(", ", SubjectLookupHostTables)}. Got: '{host.LogicalName}'; use ApplyResolverFieldsAsync.",
                nameof(host));
        }

        await ApplyLookupAndPairAsync(host!, nameof(host), regardingEntityName, regardingId, regardingDisplayName, ct)
            .ConfigureAwait(false);
    }

    /// <summary>The host tables <see cref="ApplySubjectLookupAndPairAsync"/> serves: the ones whose writer owns its own stamp.</summary>
    internal static readonly IReadOnlySet<string> SubjectLookupHostTables =
        new HashSet<string>(["sprk_signal", "sprk_decisionrecord"], StringComparer.OrdinalIgnoreCase);

    private async Task ApplyLookupAndPairAsync(
        Entity host,
        string hostParameterName,
        string regardingEntityName,
        Guid regardingId,
        string? regardingDisplayName,
        CancellationToken ct)
    {
        if (host is null)
            throw new ArgumentNullException(hostParameterName);
        if (string.IsNullOrWhiteSpace(regardingEntityName))
            throw new ArgumentException("Regarding entity name is required.", nameof(regardingEntityName));
        if (regardingId == Guid.Empty)
            throw new ArgumentException("Regarding id must be a non-empty GUID.", nameof(regardingId));

        if (!RegardingLookupsByHostTable.TryGetValue(host.LogicalName ?? string.Empty, out var lookupByEntity))
        {
            throw new ArgumentException(
                $"TodoRegardingBuilder only operates on {string.Join(", ", RegardingLookupsByHostTable.Keys)} entities. " +
                $"Got: '{host.LogicalName}'.",
                hostParameterName);
        }

        if (!lookupByEntity.TryGetValue(regardingEntityName, out var specificLookup))
        {
            throw new ArgumentException(
                $"Entity '{regardingEntityName}' is not a supported {host.LogicalName} regarding parent. " +
                $"Supported: {string.Join(", ", lookupByEntity.Keys)}.",
                nameof(regardingEntityName));
        }

        // ADR-024: at most ONE specific regarding lookup at a time, among THIS host table's lookups.
        var alreadySet = host.Attributes.Keys
            .Where(k => lookupByEntity.Values.Contains(k, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (alreadySet.Count > 0)
        {
            throw new InvalidOperationException(
                $"{host.LogicalName} already has a regarding lookup set ({string.Join(", ", alreadySet)}). " +
                "ADR-024 requires at most one specific regarding lookup. " +
                "Clear the existing one before setting a new parent.");
        }

        // 1) Specific lookup
        host[specificLookup] = new EntityReference(regardingEntityName, regardingId)
        {
            Name = regardingDisplayName
        };

        // 2) Resolver fields (4) — populated atomically
        await ApplyResolverPairAsync(host, regardingEntityName, regardingId, regardingDisplayName, ct);
    }

    /// <summary>
    /// The four ADR-024 resolver fields — the polymorphic regarding PAIR — on <paramref name="host"/>, for the parent
    /// <paramref name="regardingEntityName"/> <paramref name="regardingId"/>: <c>sprk_regardingrecordid</c> (lowercase
    /// "D" GUID), <c>sprk_regardingrecordname</c> (empty when unknown), <c>sprk_regardingrecordurl</c> (the relative
    /// model-driven record URL) and, when a <c>sprk_recordtype_ref</c> row exists for the parent's type,
    /// <c>sprk_regardingrecordtype</c>. Step 2 of <see cref="ApplyResolverFieldsAsync"/>.
    /// </summary>
    /// <remarks>
    /// Host-agnostic on purpose (unified-access-control-r2 task 156, owner decisions round 8 item 2): <c>TaskActionCore</c>
    /// writes the same pair on the <c>sprk_event</c> tasks it creates by calling this method rather than a copy of it, so
    /// the pair a task carries is exactly the one a to-do carries. A missing record-type row is non-fatal (logged; the id,
    /// name and url are still written — the id is what the core-ancestor reconciliation job finds a regarding cleared on a
    /// form by, F-051-6). This method writes nothing else: the caller owns the typed lookup and the core-ancestor stamp.
    /// </remarks>
    internal async Task ApplyResolverPairAsync(
        Entity host,
        string regardingEntityName,
        Guid regardingId,
        string? regardingDisplayName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        var cleanId = regardingId.ToString("D").ToLowerInvariant();
        host[FieldRegardingRecordId] = cleanId;
        host[FieldRegardingRecordName] = regardingDisplayName ?? string.Empty;
        host[FieldRegardingRecordUrl] = BuildRecordUrl(regardingEntityName, cleanId);

        var recordTypeRef = await ResolveRecordTypeRefAsync(regardingEntityName, ct);
        if (recordTypeRef.HasValue)
        {
            host[FieldRegardingRecordType] = new EntityReference(
                "sprk_recordtype_ref", recordTypeRef.Value.Id)
            {
                Name = recordTypeRef.Value.DisplayName
            };
        }
        else
        {
            // Untyped-pair tolerance (D-133), on every host table: non-fatal, log + continue. Specific lookup +
            // id/name/url still populated. sprk_recordtype_ref is used by cross-entity views; missing it loses
            // the entity type icon but does not break correctness. A parent table with no catalog row
            // (sprk_communication on spaarkedev1) always takes this branch.
            _logger.LogWarning(
                "sprk_recordtype_ref not found for entity '{Entity}'. Resolver type field left unset.",
                regardingEntityName);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve the <c>sprk_recordtype_ref</c> GUID + display name for an entity
    /// logical name. Cached for the lifetime of the builder instance.
    /// </summary>
    /// <remarks>
    /// Only a FOUND row is cached (task 042: a read that succeeded and found no row is answered untyped, D-133, but not remembered, so a catalog row added later is picked up). A read that
    /// FAULTS (a 429, a timeout) is not an answer: it propagates and is never cached, so one transient fault cannot type
    /// every later pair of that parent table as untyped for the builder's lifetime (task 141 review F1; the
    /// <c>TodoGenerationService</c> builder lives as long as its singleton job). Caller cancellation propagates as itself.
    /// Every caller treats a throw from the builder as "do not create".
    /// </remarks>
    internal async Task<(Guid Id, string DisplayName)?> ResolveRecordTypeRefAsync(
        string entityLogicalName, CancellationToken ct)
    {
        if (_recordTypeRefCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        var record = await _communicationService.QueryRecordTypeRefAsync(entityLogicalName, ct);
        (Guid Id, string DisplayName)? entry = record is null
            ? null
            : (record.Id, record.GetAttributeValue<string>("sprk_recorddisplayname") ?? entityLogicalName);
        // Only a FOUND row is cached (ontology task 042, uac-r2 re-check K2): a catalog row added after a miss is picked up on the next
        // call instead of leaving that type untyped until the process restarts (the Decision Record writer keeps one builder for its life).
        if (entry is not null)
        {
            _recordTypeRefCache[entityLogicalName] = entry;
        }

        return entry;
    }

    /// <summary>
    /// Build a Dataverse model-driven-app record URL for the resolver.
    /// </summary>
    /// <remarks>
    /// Returns a RELATIVE URL — the host origin is resolved by the model-driven
    /// app at click time. No org URL or tenant id is hard-coded here.
    /// </remarks>
    internal static string BuildRecordUrl(string entityLogicalName, string recordId) =>
        // One server-side owner of the ADR-024 record-URL format (task 097 review F3b): sprk_event uses it too.
        RegardingRecordType.BuildRecordUrl(entityLogicalName, recordId);
}
