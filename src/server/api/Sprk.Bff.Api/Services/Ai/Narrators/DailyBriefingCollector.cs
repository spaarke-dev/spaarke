// R7 Wave 12 T131 (2026-06-30) — DailyBriefingCollector — 6-entity expansion.
// unified-access-control-r2 task 152 (2026-10-02) — PEOPLE targeting + caller-context reads.
//
// PURPOSE: Build the DailyBriefingNarrateRequest payload directly from live Dataverse
// queries — no appNotification dependency, no scheduled playbooks, no notification
// creation pipeline.
//   [widget call] → [collector queries Dataverse live, 6 entity types] → [narrator] → [response].
//
// MVP SCOPE (this file) — 6 operator-specified channels per wave12-mvp-completion-plan §2.1:
//   1. Upcoming Tasks — sprk_event, type=Task, sprk_duedate OR sprk_finalduedate in next N days, status=Open
//   2. Overdue Tasks  — sprk_event, type=Task, sprk_duedate OR sprk_finalduedate > 5 days past, status=Open
//   3. Documents      — sprk_document, modifiedon in the recency window
//   4. Matters        — sprk_matter, modifiedon in the recency window, statecode=Active
//   5. Projects       — sprk_project, modifiedon in the recency window, statecode=Active
//   6. To Dos         — sprk_todo, sprk_duedate today or later, Open/In Progress
//
// WHO A RECORD IS FOR (task 152 — owner decisions round 2 item 9 + Q8, round 3 D1; ADR-034 Amendment A3):
//   Every candidate set comes from IMembershipResolverService with MembershipResolveOptions.People — the
//   PEOPLE-TARGETING surface. A record is for the caller when the caller CREATED it (human Created By), is NAMED in
//   one of its "Assigned *" contact columns (through the caller's linked contact, task 141), or personally OWNS it.
//   Team, business-unit and organization ownership select NOTHING: a BU's default team contains the whole BU, so the
//   pre-task default surface put every team- or BU-owned matter in every same-BU user's briefing. No query in this
//   file carries its own owner/createdby condition — the resolver is the one mechanism (ADR-034 MUST).
//
//   Tasks (Upcoming/Overdue): events FOR the caller, OR events regarding a matter/project FOR the caller.
//   Documents:                documents FOR the caller (own Created By / owner), OR documents on a matter/project
//                             FOR the caller.
//   Matters / Projects / To Dos: the records FOR the caller.
//
// WHAT THE CALLER MAY SEE (task 152 — owner D1 "the briefing only lists records the user can access in Dataverse";
// ADR-003 fail closed; ADR-047 privacy invariant): selecting a record FOR someone does not entitle them to read it.
// Every row this collector RETURNS is read through IImpersonatedCommunicationQuery as the caller (MSCRMCallerID =
// the caller's systemuserid), so Dataverse trims anything the caller cannot read. There is no app-only read of a
// returned row anywhere in this class, and no app-only fallback:
//   - a channel whose read (or any id chunk of it) fails is reported in DailyBriefingNarrateRequest.FailedChannels
//     — distinguishable from "nothing to report" — and never answered app-only or as a silent empty list;
//   - if EVERY channel fails, CollectAsync throws (mirrors word-add-in-r1 task 062's impersonated search fan-out:
//     an impersonation privilege that is not configured must not look like an empty briefing).
//   Candidate id lists are chunked (MaxIdsPerImpersonatedRequest) so each impersonated GET stays far below the URL
//   limit; a failed chunk fails its channel, never shrinks it. Each candidate set is read to completion
//   (PeopleTargetedSet: up to the resolver's 5,000-row ceiling); a set larger than that fails its channels too.
//
// PRESERVES: BriefingItem projection shape (downstream narrator depends on it). Each
// channel's items[] populates RegardingMatterName/RegardingMatterId for entity-link
// click-through; the BriefingItem.EntityType field carries the source entity for
// EnrichBulletWithEntityRefs (per-channel `primaryEntityType` in narrator output).
//
// Reference:
//   projects/spaarke-ai-platform-unification-r7/notes/wave12-mvp-completion-plan.md §2.1
//   projects/unified-access-control-r2/tasks/152-notifications-briefing-people-targeting.poml
//   projects/unified-access-control-r2/notes/task-152-people-targeting.md

using System.Globalization;
using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Communication;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// Internal projection of a Dataverse record into the lowest-common-denominator shape needed
/// by the narrator + widget. Carries enough to (a) include in narrative, (b) navigate to the
/// underlying record, (c) add to a To-Do list.
/// </summary>
internal sealed record BriefingItem
{
    public required string Id { get; init; }
    public required string EntityType { get; init; }       // sprk_event, sprk_document, sprk_matter, sprk_project, sprk_todo
    public required string EntityId { get; init; }         // for navigation + to-do creation
    public required string Title { get; init; }            // human-readable summary line
    public string? Body { get; init; }
    public string Priority { get; init; } = "normal";
    public DateTimeOffset? DueDate { get; init; }
    public string? RegardingMatterName { get; init; }
    public string? RegardingMatterId { get; init; }        // Matter GUID for click-through navigation
    public DateTimeOffset? ModifiedOn { get; init; }
}

/// <summary>
/// The High Priority section (task 152): the flagged records FOR the caller that the caller may read, plus the entity
/// types whose caller-context read failed — so a failed read is visibly "could not be loaded", never an empty list.
/// </summary>
/// <param name="Items">Flagged records, de-duplicated and ordered by due date then name.</param>
/// <param name="FailedEntityTypes">Entity logical names whose read failed (empty when every read succeeded).</param>
public sealed record HighPriorityCollection(HighPriorityItemDto[] Items, string[] FailedEntityTypes)
{
    /// <summary>No flagged items and no failures.</summary>
    public static HighPriorityCollection Empty { get; } = new([], []);
}

/// <summary>
/// Live-query collector for the Daily Briefing widget. Returns a populated
/// <see cref="DailyBriefingNarrateRequest"/> directly from Dataverse — bypasses
/// appNotification entirely. 6-channel coverage per operator spec (wave12 §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Unsealed (R7 Wave 12 post-T135 CI fix 2026-06-30 — PR #520) so
/// <see cref="NullDailyBriefingCollector"/> can subclass it for the compound-OFF kill-switch
/// path. The /api/ai/daily-briefing/render endpoint is mapped unconditionally; without a
/// Null peer registered when Analysis:Enabled=false || DocumentIntelligence:Enabled=false,
/// minimal-API parameter inference fails at host startup. Mirrors
/// <see cref="Chat.NullSessionDispatchOrchestrator"/> + ADR-032 §F.1.
/// </para>
/// <para>
/// FR-P0-06 (spaarke-ai-architecture-redesign-r1 task 007): first <see cref="ICodedWorkflow"/>
/// instance (alongside <see cref="DailyBriefingNarrator"/>) — resolvable by class reference
/// (<c>"DailyBriefingCollector"</c>) as a <c>coded</c>-kind Action row's
/// <c>sprk_workflowclass</c> names it. The retrofit is interface adoption only:
/// <see cref="ExecuteAsync"/> delegates to the pre-existing <see cref="CollectAsync"/>;
/// existing callers and behavior are unchanged.
/// </para>
/// <para>
/// Task 152: the collector holds NO app-only Dataverse client. Its only Dataverse access is the people-targeting
/// resolver (which returns ids, never row content) and the caller-context read seam. That is what makes "no app-only
/// read of a returned row" structural rather than a convention.
/// </para>
/// </remarks>
public class DailyBriefingCollector : ICodedWorkflow
{
    // sprk_event type GUIDs (consistent with deployed notification playbooks).
    // Source of truth: sprk_eventtype_ref records in spaarkedev1.
    private const string EventTypeTask = "124f5fc9-98ff-f011-8406-7c1e525abd8b";

    // sprk_event statuscode values (consistent with deployed notification playbooks).
    private const int EventStatusOpen = Spaarke.Dataverse.EventStatusCode.Open; // task 097 review F8: the one source of truth

    // sprk_todo statuscode values per docs/data-model schema (Open=1, In Progress=659490001).
    // Treat both as "active" for the today/tomorrow surface.
    private const int TodoStatusOpen = 1;
    private const int TodoStatusInProgress = 659490001;

    // Date-window DEFAULTS (operator-stated; wave12 §2.1). r5 settings-wiring (2026-07-09):
    // the upcoming-task + recency windows are now overridable per-user via the briefing
    // Display Parameters (see BriefingWindowOptions); these constants are the fallback when
    // no options are supplied (scheduled email leg, tests, and cold-load before prefs resolve).
    // TaskOverdueDaysPast stays fixed — "overdue" has no user-facing control.
    private const int TaskOverdueDaysPast = 5;
    // Kept for BriefingWindowOptions.Default only (see below).
    internal const int DefaultDueWithinDays = 5;
    internal const int DefaultRecencyHours = 120; // 5 days

    /// <summary>
    /// Per-user briefing date windows, sourced from the caller's Display Parameters
    /// (sprk_userpreference: Due-soon window + Recency window) and applied to the
    /// deterministic collector queries. <see cref="DueWithinDays"/> bounds the upcoming
    /// tasks/events look-ahead (NextXDays); <see cref="RecencyHours"/> bounds the
    /// documents/matters/projects modified-on look-back. <see cref="Default"/> reproduces
    /// the pre-wiring fixed 5-day behavior for callers that pass no options.
    /// </summary>
    public sealed record BriefingWindowOptions(int DueWithinDays, int RecencyHours)
    {
        public static readonly BriefingWindowOptions Default =
            new(DefaultDueWithinDays, DefaultRecencyHours);
    }

    // Entity logical names (kept as constants so a typo fails at compile time).
    private const string EntityEvent = "sprk_event";
    private const string EntityDocument = "sprk_document";
    private const string EntityMatter = "sprk_matter";
    private const string EntityProject = "sprk_project";
    private const string EntityTodo = "sprk_todo";
    private const string EntityInvoice = "sprk_invoice";
    private const string EntityWorkAssignment = "sprk_workassignment";

    // Channel-code naming convention (T133 coordination) — kebab-case slugs.
    // Keep these stable; they are the keys downstream consumers (channel registry,
    // EnrichBulletWithEntityRefs primaryEntityType resolution) join on.
    internal const string ChannelUpcomingTasks = "upcoming-tasks";
    internal const string ChannelOverdueTasks = "overdue-tasks";
    internal const string ChannelDocuments = "documents";
    internal const string ChannelMatters = "matters";
    internal const string ChannelProjects = "projects";
    internal const string ChannelTodos = "to-dos";

    // Per-channel row caps (defensive — large result sets degrade narrator quality
    // before they cost LLM tokens). Operator may tune later via config table (deferred
    // per wave12 §4 — config-table-with-rules is interpreter).
    private const int PerChannelMaxRows = 50;

    /// <summary>
    /// Task 152: the most candidate ids bound into ONE impersonated GET. A lookup clause is ~75 characters
    /// (<c>_sprk_regardingmatter_value eq {guid} or </c>), so 50 ids keep the query near 4 KB — an order of magnitude
    /// under the Dataverse Web API URL limit — whatever the other clauses add. Larger sets are read in several chunks
    /// and unioned; a failed chunk fails the whole channel.
    /// </summary>
    internal const int MaxIdsPerImpersonatedRequest = 50;

    /// <summary>The Web API annotation that carries a lookup's display name on the same impersonated row.</summary>
    private const string FormattedValueSuffix = "@OData.Community.Display.V1.FormattedValue";

    // R5 task 013 (FR-A4) — TL;DR scaffolding aggregation caps. The TL;DR call's ground-truth
    // payload MUST aggregate, not dump every source record (ADR-015 data minimization) — these
    // caps bound RecordNames/KeyDates regardless of how many rows a channel returns.
    internal const int TldrFactsMaxRecordNames = 20;
    internal const int TldrFactsMaxKeyDates = 6;

    private readonly IImpersonatedCommunicationQuery _callerQuery;
    private readonly IMembershipResolverService _membershipResolver;
    private readonly ILogger<DailyBriefingCollector> _logger;
    private readonly TimeProvider _clock;

    /// <param name="callerQuery">
    /// The existing caller-context read seam (MSCRMCallerID = the caller). Generic over entity set + OData query
    /// despite its Communication-specific name; reused, not duplicated (ExternalAccessModule records the decision
    /// not to declare a second impersonated-query interface).
    /// </param>
    /// <param name="membershipResolver">The canonical ADR-034 resolver, called with the people-targeting surface.</param>
    /// <param name="logger">Logger.</param>
    public DailyBriefingCollector(
        IImpersonatedCommunicationQuery callerQuery,
        IMembershipResolverService membershipResolver,
        ILogger<DailyBriefingCollector> logger,
        TimeProvider? clock = null)
    {
        _callerQuery = callerQuery ?? throw new ArgumentNullException(nameof(callerQuery));
        _membershipResolver = membershipResolver ?? throw new ArgumentNullException(nameof(membershipResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? TimeProvider.System; // optional so direct constructions need not supply one
    }

    /// <summary>
    /// Protected ctor used only by <see cref="NullDailyBriefingCollector"/> so the kill-switch
    /// subclass can be constructed when the compound AI gate is OFF. The Null override never
    /// reads the nulled fields — it throws
    /// <see cref="Sprk.Bff.Api.Configuration.FeatureDisabledException"/> before they are
    /// dereferenced.
    /// </summary>
    protected DailyBriefingCollector(ILogger<DailyBriefingCollector> logger)
    {
        _callerQuery = null!;
        _membershipResolver = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = TimeProvider.System;
    }

    /// <inheritdoc />
    /// <remarks>
    /// FR-P0-06: stable class reference an Action row's <c>sprk_workflowclass</c> carries.
    /// Inherited by <see cref="NullDailyBriefingCollector"/> — the Null peer is the same
    /// workflow identity on the compound-OFF path (ADR-032).
    /// </remarks>
    public string WorkflowClassRef => nameof(DailyBriefingCollector);

    /// <inheritdoc />
    /// <remarks>
    /// FR-P0-06 convention entry point: requires <see cref="CodedWorkflowContext.UserId"/>
    /// (the collector queries Dataverse scoped to the acting user) and delegates to
    /// <see cref="CollectAsync"/> (the virtual method — so the
    /// <see cref="NullDailyBriefingCollector"/> kill-switch override flows through class-ref
    /// invocation unchanged). No behavior change to the existing path.
    /// </remarks>
    public async Task<object?> ExecuteAsync(CodedWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.UserId is not { } userId || userId == Guid.Empty)
        {
            throw new ArgumentException(
                "DailyBriefingCollector: CodedWorkflowContext.UserId (acting systemuserid) is required — the collector queries Dataverse on the user's behalf.",
                nameof(context));
        }

        // Coded-workflow entry (Binding execution) — no per-user window options in the
        // workflow context, so use the fixed defaults. The /render endpoint's window override
        // is applied by the composite's direct CollectAsync(systemUserId, windows, ct) call,
        // whose collected payload is what gets rendered.
        return await CollectAsync(userId, BriefingWindowOptions.Default, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Run all 6 channel queries in parallel against Dataverse and build the request
    /// payload the narrator consumes. Empty channels are filtered out of the final
    /// payload (the narrator skips empty channels naturally); FAILED channels are named in
    /// <see cref="DailyBriefingNarrateRequest.FailedChannels"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Every channel's caller-context read failed (task 152).</exception>
    public virtual async Task<DailyBriefingNarrateRequest> CollectAsync(
        Guid systemUserId,
        BriefingWindowOptions windows,
        CancellationToken ct)
    {
        if (systemUserId == Guid.Empty)
        {
            throw new ArgumentException("systemUserId is required", nameof(systemUserId));
        }
        // r5 settings-wiring (2026-07-09): the user's Display Parameters (Due-soon window +
        // Recency window) drive the per-channel windows. Null-safe fallback to the fixed
        // defaults preserves the pre-wiring behavior for callers that don't pass options.
        var w = windows ?? BriefingWindowOptions.Default;
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "DailyBriefingCollector starting for systemUserId={SystemUserId}", systemUserId);

        // ── Phase 1: the records FOR the caller, per entity (ADR-034 A3 people-targeting surface). Ids only — the
        //    resolver never hands back row content, so nothing here is shown to anyone.
        var sets = await Task.WhenAll(
            ResolvePeopleSetAsync(systemUserId, EntityEvent, ct),
            ResolvePeopleSetAsync(systemUserId, EntityMatter, ct),
            ResolvePeopleSetAsync(systemUserId, EntityProject, ct),
            ResolvePeopleSetAsync(systemUserId, EntityDocument, ct),
            ResolvePeopleSetAsync(systemUserId, EntityTodo, ct)).ConfigureAwait(false);
        var events = sets[0];
        var matters = sets[1];
        var projects = sets[2];
        var documents = sets[3];
        var todos = sets[4];

        _logger.LogInformation(
            "DailyBriefingCollector people-targeted ids: events={EventCount}, matters={MatterCount}, projects={ProjectCount}, documents={DocumentCount}, todos={TodoCount}",
            events.Ids.Count, matters.Ids.Count, projects.Ids.Count, documents.Ids.Count, todos.Ids.Count);

        // Task 098: "today" is the caller's LOCAL day (their Dataverse time zone, read AS the caller), not the UTC day —
        // at 21:00 Eastern the UTC day is already tomorrow, so a task due today read as overdue. Resolved lazily: only
        // a channel that has something to read asks for it.
        var userDay = new Lazy<Task<UserDay>>(() => ResolveUserDayAsync(systemUserId, ct));

        // ── Phase 2: read each channel AS THE CALLER, in parallel.
        var results = await Task.WhenAll(
            QueryUpcomingTasksAsync(systemUserId, events, matters, projects, w.DueWithinDays, ct),
            QueryOverdueTasksAsync(systemUserId, events, matters, projects, userDay, ct),
            QueryDocumentsAsync(systemUserId, documents, matters, projects, w.RecencyHours, ct),
            QueryMattersAsync(systemUserId, matters, w.RecencyHours, ct),
            QueryProjectsAsync(systemUserId, projects, w.RecencyHours, ct),
            QueryTodosAsync(systemUserId, todos, userDay, ct)).ConfigureAwait(false);

        var channelCodes = new[]
        {
            ChannelUpcomingTasks, ChannelOverdueTasks, ChannelDocuments, ChannelMatters, ChannelProjects, ChannelTodos,
        };
        var failedChannels = channelCodes.Where((_, i) => results[i].Failed).ToArray();

        if (failedChannels.Length == channelCodes.Length)
        {
            // Fail closed, never "nothing to report": the most likely cause is the go-live prerequisite — the BFF
            // application user lacks prvActOnBehalfOfAnotherUser, so every impersonated read is rejected.
            throw new InvalidOperationException(
                "Daily briefing: every channel's caller-context read failed. Refusing to return an empty briefing — "
                + "the impersonated read may be rejected because the BFF application user lacks the Dataverse "
                + "Delegate privilege prvActOnBehalfOfAnotherUser.");
        }

        var request = BuildNarrateRequest(
            results[0].Items, results[1].Items, results[2].Items,
            results[3].Items, results[4].Items, results[5].Items) with
        {
            FailedChannels = failedChannels,
        };

        _logger.LogInformation(
            "DailyBriefingCollector completed in {DurationMs}ms: upcoming={A}, overdue={B}, docs={C}, matters={D}, projects={E}, todos={F}, totalNotifs={Total}, failedChannels={Failed}",
            (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            results[0].Items.Length, results[1].Items.Length, results[2].Items.Length,
            results[3].Items.Length, results[4].Items.Length, results[5].Items.Length,
            request.TotalNotificationCount,
            failedChannels.Length == 0 ? "none" : string.Join(",", failedChannels));

        return request;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // High Priority section (R7 W12 feedback item 9; narrowed by task 152)
    //
    // Flagged records (sprk_highpriority = true OR sprk_monitor = true) across the 7 flagged entities — but only
    // the ones FOR the caller (people-targeting surface) and readable BY the caller (impersonated read). Before task
    // 152 this was an org-wide, app-only scan returning every flagged record in the tenant, with names and
    // descriptions, to every caller (owner escalation (b): narrowed to the caller's people-targeted set).
    //
    // No LLM call — widget renders as a compact list of clickable record refs.
    // Ordered by due date ascending (undated items last).
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Collect the high-priority items FOR the caller across the 7 flagged entities, each read as the caller.
    /// <see cref="HighPriorityCollection.FailedEntityTypes"/> names any entity whose read failed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Every entity's read failed (task 152 — never an empty list).</exception>
    public virtual async Task<HighPriorityCollection> CollectHighPriorityAsync(
        Guid systemUserId,
        CancellationToken ct)
    {
        if (systemUserId == Guid.Empty)
        {
            throw new ArgumentException("systemUserId is required", nameof(systemUserId));
        }
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "DailyBriefingCollector.CollectHighPriorityAsync starting for systemUserId={SystemUserId}",
            systemUserId);

        // The people-targeted sets the 7 specs need (document additionally needs matter + project for its
        // parent term, which the matter/project specs already resolve).
        var setTasks = HighPriorityEntitySpecs
            .Select(s => s.EntityType)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(e => e, e => ResolvePeopleSetAsync(systemUserId, e, ct), StringComparer.Ordinal);
        await Task.WhenAll(setTasks.Values).ConfigureAwait(false);
        var sets = setTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result, StringComparer.Ordinal);

        // Task 098: Overdue / DueToday / DueSoon are judged against the caller's LOCAL today (lazy, as above).
        var userDay = new Lazy<Task<UserDay>>(() => ResolveUserDayAsync(systemUserId, ct));
        var queries = await Task.WhenAll(
            HighPriorityEntitySpecs.Select(spec => QueryHighPriorityAsync(spec, systemUserId, sets, userDay, ct))
        ).ConfigureAwait(false);

        var failed = HighPriorityEntitySpecs
            .Where((_, i) => queries[i].Failed)
            .Select(s => s.EntityType)
            .ToArray();

        if (failed.Length == HighPriorityEntitySpecs.Length)
        {
            throw new InvalidOperationException(
                "Daily briefing High Priority: every entity's caller-context read failed. Refusing to return an "
                + "empty list — the impersonated read may be rejected because the BFF application user lacks the "
                + "Dataverse Delegate privilege prvActOnBehalfOfAnotherUser.");
        }

        // R5 task 034 (FR-C5) — de-dup before ordering. The 7 queries are one per
        // flagged entity type so cross-entity collision is not possible today, but this
        // keys on stable record identity (entity + GUID, NEVER display text) as a
        // defensive boundary against any future entity/route addition that could surface
        // the same record twice. DistinctBy preserves first-occurrence order, so applying
        // it BEFORE the OrderBy/ThenBy below means the de-dup never reshuffles anything —
        // it only removes duplicates ahead of the existing DueDate-then-Name ordering.
        var all = queries.SelectMany(x => x.Items)
            .DistinctBy(x => (x.EntityType, x.EntityId))
            .OrderBy(x => x.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogInformation(
            "DailyBriefingCollector.CollectHighPriorityAsync completed in {DurationMs}ms: total={Total} " +
            "(matters={M}, projects={P}, invoices={I}, docs={D}, workassignments={W}, events={E}, todos={T}), failed={Failed}",
            (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            all.Length,
            queries[0].Items.Length, queries[1].Items.Length, queries[2].Items.Length, queries[3].Items.Length,
            queries[4].Items.Length, queries[5].Items.Length, queries[6].Items.Length,
            failed.Length == 0 ? "none" : string.Join(",", failed));

        return new HighPriorityCollection(all, failed);
    }

    // R5 task 036 (FR-C7) — the 7 flagged entities as data. Task 152 replaced the per-spec owner switch
    // (ScopeToOwner / owninguser) with the people-targeting surface for EVERY entity, and added the entity set the
    // impersonated read needs.
    private sealed record HighPriorityEntitySpec(
        string EntityType,
        string EntitySet,
        string IdColumn,
        string NameColumn,
        string? DescriptionColumn,
        string? DueDateColumn,
        string? FallbackDueDateColumn,
        string KindLabel,
        bool IncludeStateFilter);

    private static readonly HighPriorityEntitySpec[] HighPriorityEntitySpecs =
    {
        new(EntityMatter, "sprk_matters", "sprk_matterid", "sprk_mattername", "sprk_matterdescription",
            DueDateColumn: null, FallbackDueDateColumn: null, KindLabel: "Matter", IncludeStateFilter: true),
        new(EntityProject, "sprk_projects", "sprk_projectid", "sprk_projectname", "sprk_description",
            DueDateColumn: null, FallbackDueDateColumn: null, KindLabel: "Project", IncludeStateFilter: true),
        // Invoice has sprk_invoicedate (invoice date, NOT payment due date) — don't map to
        // DueDate. Include all flagged invoices regardless of date. Operator can refine later.
        new(EntityInvoice, "sprk_invoices", "sprk_invoiceid", "sprk_name", "sprk_description",
            DueDateColumn: null, FallbackDueDateColumn: null, KindLabel: "Invoice", IncludeStateFilter: true),
        new(EntityDocument, "sprk_documents", "sprk_documentid", "sprk_documentname", "sprk_documentdescription",
            DueDateColumn: null, FallbackDueDateColumn: null, KindLabel: "Document", IncludeStateFilter: true),
        new(EntityWorkAssignment, "sprk_workassignments", "sprk_workassignmentid", "sprk_name", "sprk_description",
            DueDateColumn: "sprk_responseduedate", FallbackDueDateColumn: null, KindLabel: "Work Assignment",
            IncludeStateFilter: true),
        // Event has both sprk_duedate and sprk_finalduedate; use sprk_finalduedate first,
        // fall back to sprk_duedate. This mirrors QueryUpcomingTasksAsync's precedence.
        // 🔴 Fixed 2026-09-29 (spaarke-ontology-platform-r1, master #1032): the description column was
        // "sprk_eventdescription", which DOES NOT EXIST on sprk_event — the real column is "sprk_description", exactly
        // as every sibling entry in this list already uses. The bad column made Dataverse reject the whole retrieve, so
        // this channel threw on every briefing run and the briefing could not see tasks at all (AP-14). Merged with task
        // 152's people-targeting spec shape (entity set, no per-spec owner switch).
        new(EntityEvent, "sprk_events", "sprk_eventid", "sprk_eventname", "sprk_description",
            DueDateColumn: "sprk_finalduedate", FallbackDueDateColumn: "sprk_duedate", KindLabel: "Task",
            IncludeStateFilter: false),
        new(EntityTodo, "sprk_todos", "sprk_todoid", "sprk_name", "sprk_description",
            DueDateColumn: "sprk_duedate", FallbackDueDateColumn: null, KindLabel: "To Do", IncludeStateFilter: true),
    };

    /// <summary>
    /// One flagged entity: its people-targeted ids (for <c>sprk_document</c>, also documents on a matter/project FOR
    /// the caller — the same rule as the Documents channel), read as the caller.
    /// </summary>
    private async Task<(HighPriorityItemDto[] Items, bool Failed)> QueryHighPriorityAsync(
        HighPriorityEntitySpec spec,
        Guid systemUserId,
        IReadOnlyDictionary<string, PeopleSet> sets,
        Lazy<Task<UserDay>> userDay,
        CancellationToken ct)
    {
        var terms = new List<IdTerm> { new(spec.IdColumn, sets[spec.EntityType]) };
        if (spec.EntityType == EntityDocument)
        {
            terms.Add(new IdTerm("_sprk_matter_value", sets[EntityMatter]));
            terms.Add(new IdTerm("_sprk_project_value", sets[EntityProject]));
        }

        var columns = new List<string> { spec.IdColumn, spec.NameColumn, "sprk_highpriority", "sprk_monitor", "modifiedon" };
        if (!string.IsNullOrEmpty(spec.DescriptionColumn)) columns.Add(spec.DescriptionColumn);
        if (!string.IsNullOrEmpty(spec.DueDateColumn)) columns.Add(spec.DueDateColumn);
        if (!string.IsNullOrEmpty(spec.FallbackDueDateColumn)) columns.Add(spec.FallbackDueDateColumn);

        var filters = new List<string> { "(sprk_highpriority eq true or sprk_monitor eq true)" };
        if (spec.IncludeStateFilter)
        {
            filters.Add("statecode eq 0");
        }

        var read = await ReadAsCallerAsync(
            systemUserId,
            spec.EntitySet,
            spec.IdColumn,
            filters,
            terms,
            columns,
            orderBy: null,
            label: $"high-priority:{spec.EntityType}",
            ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return (Array.Empty<HighPriorityItemDto>(), true);
        }

        var items = new List<HighPriorityItemDto>(read.Rows.Count);
        foreach (var row in read.Rows)
        {
            var id = GetGuid(row, spec.IdColumn);
            if (id is null) continue;

            DateTimeOffset? dueDate = null;
            string? dueColumn = null;
            if (!string.IsNullOrEmpty(spec.DueDateColumn))
            {
                dueDate = GetDate(row, spec.DueDateColumn);
                dueColumn = spec.DueDateColumn;
                if (dueDate is null && !string.IsNullOrEmpty(spec.FallbackDueDateColumn))
                {
                    dueDate = GetDate(row, spec.FallbackDueDateColumn);
                    dueColumn = spec.FallbackDueDateColumn;
                }
            }

            var highPriority = GetBool(row, "sprk_highpriority") ?? false;
            var monitor = GetBool(row, "sprk_monitor") ?? false;
            var modifiedOn = GetDate(row, "modifiedon");

            var reason = highPriority && monitor ? "Both"
                : highPriority ? "HighPriority"
                : monitor ? "Monitor"
                : string.Empty;

            items.Add(new HighPriorityItemDto
            {
                EntityType = spec.EntityType,
                EntityId = id.Value.ToString(),
                Name = GetString(row, spec.NameColumn) ?? "(untitled)",
                DueDate = dueDate,
                HighPriority = highPriority,
                Monitor = monitor,
                KindLabel = spec.KindLabel,
                Description = !string.IsNullOrEmpty(spec.DescriptionColumn)
                    ? (GetString(row, spec.DescriptionColumn) ?? string.Empty)
                    : string.Empty,
                Action = await ClassifyActionAsync(dueColumn is null ? null : GetString(row, dueColumn), modifiedOn, userDay)
                    .ConfigureAwait(false),
                Reason = reason,
                ModifiedOn = modifiedOn,
            });
        }

        return (items.ToArray(), false);
    }

    /// <summary>
    /// R7 W12 feedback (2026-07-01) — server-side classification of the "action" column
    /// for a high-priority item. Result strings are widget-facing enums:
    ///   - "Overdue"  — dueDate is before today UTC start
    ///   - "DueToday" — dueDate is today UTC
    ///   - "DueSoon"  — dueDate is within next 7 days
    ///   - "Recent"   — no dueDate but modifiedon within last 7 days (fresh activity)
    ///   - "None"     — no dueDate + no recent modifiedon
    /// Widget renders as a badge with distinct intent color per action class.
    /// </summary>
    /// <summary>
    /// Overdue / DueToday / DueSoon from the due date's CALENDAR DAY against the caller's LOCAL today (task 098 — the
    /// former UTC "today" called a task due today overdue from 20:00 Eastern), else Recent / None from modifiedon.
    /// </summary>
    private async Task<string> ClassifyActionAsync(string? rawDueDate, DateTimeOffset? modifiedOn, Lazy<Task<UserDay>> userDay)
    {
        if (!string.IsNullOrEmpty(rawDueDate))
        {
            var day = await userDay.Value.ConfigureAwait(false);
            if (DueDayOf(rawDueDate, day.Zone) is { } dueDay)
                return ClassifyAction(dueDay, modifiedOn, day.Today, _clock.GetUtcNow());
        }

        return ClassifyAction(null, modifiedOn, DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime), _clock.GetUtcNow());
    }

    /// <summary>The classification rule itself — pure; <paramref name="today"/> is the caller's local today.</summary>
    internal static string ClassifyAction(DateOnly? dueDay, DateTimeOffset? modifiedOn, DateOnly today, DateTimeOffset nowUtc)
    {
        if (dueDay is { } due)
        {
            if (due < today) return "Overdue";
            if (due == today) return "DueToday";
            if (due < today.AddDays(7)) return "DueSoon";
        }

        // Recency is elapsed time, not a calendar day: unchanged (7 days back from the UTC start of today).
        var sevenDaysAgo = new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero).AddDays(-7);
        if (modifiedOn.HasValue && modifiedOn.Value >= sevenDaysAgo)
        {
            return "Recent";
        }

        return "None";
    }

    /// <summary>
    /// A due value's calendar day: a bare <c>yyyy-MM-dd</c> (a Date Only column) is that day as written; a timestamp
    /// (a UserLocal column) is the day of that instant in the caller's zone (UTC when unknown).
    /// </summary>
    internal static DateOnly? DueDayOf(string raw, TimeZoneInfo? zone)
    {
        // The Web API returns only ISO shapes: "yyyy-MM-dd" for a Date Only column, "yyyy-MM-ddTHH:mm:ssZ" otherwise.
        // A non-ISO string would parse as UTC midnight and could land a day early — it does not occur on this path.
        if (raw.Length == 10 && DataverseDateOnly.TryParse(raw, out var date))
            return date;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)
            ? DataverseUserTimeZone.LocalDate(instant, zone)
            : null;
    }

    /// <summary>
    /// The caller's "today" in their own Dataverse time zone, read AS THE CALLER through the impersonated seam (this
    /// collector holds no app-only client): <c>usersettings.timezonecode</c>, then that code's
    /// <c>timezonedefinition.standardname</c>. Falls back to the UTC date with a warning.
    /// </summary>
    private async Task<UserDay> ResolveUserDayAsync(Guid systemUserId, CancellationToken ct)
    {
        var day = await DataverseUserTimeZone.UserDayAsync(
            async c =>
            {
                var rows = await _callerQuery.QueryAsync(
                    "usersettingscollection", $"$select=timezonecode&$filter=systemuserid eq {systemUserId:D}", systemUserId, c)
                    .ConfigureAwait(false);
                return rows.Count > 0 ? GetInt(rows[0], "timezonecode") : null;
            },
            async (code, c) =>
            {
                var rows = await _callerQuery.QueryAsync(
                    "timezonedefinitions",
                    $"$select=standardname&$filter=timezonecode eq {code.ToString(CultureInfo.InvariantCulture)}",
                    systemUserId, c).ConfigureAwait(false);
                return rows.Count > 0 ? GetString(rows[0], "standardname") : null;
            },
            _clock.GetUtcNow(),
            ct).ConfigureAwait(false);

        if (day.FallbackReason is not null)
        {
            _logger.LogWarning(
                "DailyBriefingCollector: the caller's time zone could not be read; \"today\" is the UTC date ({Reason}).",
                day.FallbackReason);
        }

        return day;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Who a record is FOR — the people-targeting surface (ADR-034 A3)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>The people-targeted ids for one entity, or a failure marker. Failure is never an empty set.</summary>
    private sealed record PeopleSet(IReadOnlyList<Guid> Ids, bool Failed)
    {
        public static PeopleSet FailedSet { get; } = new(Array.Empty<Guid>(), true);
    }

    /// <summary>One id-bearing clause of a channel query: <c>{FilterProperty} eq id</c> over the set's ids.</summary>
    private sealed record IdTerm(string FilterProperty, PeopleSet Set);

    /// <summary>
    /// Resolves the records of <paramref name="entityType"/> FOR the caller through the canonical resolver's
    /// people-targeting surface, read to completion (<see cref="PeopleTargetedSet"/>). A resolver failure, or a set
    /// larger than the resolver's ceiling, becomes a FAILED set — the channels that depend on it report failure
    /// instead of silently shrinking to an arbitrary subset.
    /// </summary>
    private async Task<PeopleSet> ResolvePeopleSetAsync(Guid systemUserId, string entityType, CancellationToken ct)
    {
        try
        {
            var set = await PeopleTargetedSet
                .ResolveAsync(_membershipResolver, systemUserId, entityType, _logger, ct)
                .ConfigureAwait(false);
            if (!set.Complete)
            {
                _logger.LogWarning(
                    "DailyBriefingCollector people-targeted set for entity={EntityType} exceeds the resolver ceiling; "
                    + "dependent channels are reported FAILED (never a truncated list)",
                    entityType);
                return PeopleSet.FailedSet;
            }
            return new PeopleSet(set.Ids, Failed: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DailyBriefingCollector people-targeting resolution failed for entity={EntityType}; dependent channels are reported FAILED",
                entityType);
            return PeopleSet.FailedSet;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // What the caller may see — every returned row is read AS the caller
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>The rows of one channel's caller-context read, or a failure marker.</summary>
    private sealed record CallerRead(IReadOnlyList<Dictionary<string, JsonElement>> Rows, bool Failed)
    {
        public static CallerRead FailedRead { get; } = new(Array.Empty<Dictionary<string, JsonElement>>(), true);
        public static CallerRead None { get; } = new(Array.Empty<Dictionary<string, JsonElement>>(), false);
    }

    /// <summary>
    /// Reads the rows matching <paramref name="filters"/> AND (any <paramref name="terms"/> id) under the CALLER's
    /// Dataverse security, chunking each id list at <see cref="MaxIdsPerImpersonatedRequest"/> and unioning by
    /// <paramref name="idColumn"/>. A failed dependency set or a failed chunk fails the whole read; there is no
    /// app-only path.
    /// </summary>
    private async Task<CallerRead> ReadAsCallerAsync(
        Guid callerSystemUserId,
        string entitySet,
        string idColumn,
        IReadOnlyList<string> filters,
        IReadOnlyList<IdTerm> terms,
        IReadOnlyList<string> columns,
        string? orderBy,
        string label,
        CancellationToken ct)
    {
        if (terms.Any(t => t.Set.Failed))
        {
            _logger.LogWarning(
                "DailyBriefingCollector channel={Label} FAILED: a people-targeted candidate set could not be resolved",
                label);
            return CallerRead.FailedRead;
        }

        if (terms.All(t => t.Set.Ids.Count == 0))
        {
            return CallerRead.None;
        }

        var baseFilter = string.Join(" and ", filters);
        var select = string.Join(",", columns);
        var rows = new Dictionary<Guid, Dictionary<string, JsonElement>>();

        foreach (var term in terms)
        {
            foreach (var chunk in term.Set.Ids.Distinct().Chunk(MaxIdsPerImpersonatedRequest))
            {
                var idClause = string.Join(
                    " or ",
                    chunk.Select(id => $"{term.FilterProperty} eq {id.ToString("D", CultureInfo.InvariantCulture)}"));
                var filter = baseFilter.Length == 0 ? $"({idClause})" : $"{baseFilter} and ({idClause})";

                var odata = new StringBuilder()
                    .Append("$select=").Append(select)
                    .Append("&$filter=").Append(filter);
                if (!string.IsNullOrEmpty(orderBy))
                {
                    odata.Append("&$orderby=").Append(orderBy);
                }
                odata.Append("&$top=").Append(PerChannelMaxRows.ToString(CultureInfo.InvariantCulture));

                IReadOnlyList<Dictionary<string, JsonElement>> page;
                try
                {
                    page = await _callerQuery
                        .QueryAsync(entitySet, odata.ToString(), callerSystemUserId, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "DailyBriefingCollector channel={Label} FAILED: the caller-context read of {EntitySet} was refused or "
                        + "faulted — reported as failed, never answered app-only",
                        label, entitySet);
                    return CallerRead.FailedRead;
                }

                foreach (var row in page)
                {
                    if (GetGuid(row, idColumn) is { } id)
                    {
                        rows.TryAdd(id, row);
                    }
                }
            }
        }

        return new CallerRead(rows.Values.ToList(), Failed: false);
    }

    /// <summary>A channel's items, or the failure marker the collected payload carries.</summary>
    private sealed record ChannelResult(BriefingItem[] Items, bool Failed)
    {
        public static ChannelResult FailedChannel { get; } = new(Array.Empty<BriefingItem>(), true);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Channel query methods
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly string[] EventColumns =
    {
        "sprk_eventid", "sprk_eventname", "sprk_duedate", "sprk_finalduedate", "modifiedon",
        "_sprk_regardingmatter_value", "_sprk_regardingproject_value", "sprk_priority",
    };

    /// <summary>
    /// Upcoming Tasks — sprk_event of type Task, due in the next N days, status Open.
    /// For the caller: the event itself, or its regarding matter / project.
    /// </summary>
    private Task<ChannelResult> QueryUpcomingTasksAsync(
        Guid systemUserId,
        PeopleSet events,
        PeopleSet matters,
        PeopleSet projects,
        int dueWithinDays,
        CancellationToken ct)
    {
        var days = dueWithinDays.ToString(CultureInfo.InvariantCulture);
        return QueryEventsAsync(
            ChannelUpcomingTasks,
            systemUserId,
            events,
            matters,
            projects,
            // sprk_duedate OR sprk_finalduedate within the user's Due-soon window (days).
            $"(Microsoft.Dynamics.CRM.NextXDays(PropertyName='sprk_duedate',PropertyValue={days}) or "
            + $"Microsoft.Dynamics.CRM.NextXDays(PropertyName='sprk_finalduedate',PropertyValue={days}))",
            ct);
    }

    /// <summary>
    /// Overdue Tasks — sprk_event of type Task, due more than 5 days ago, status Open.
    /// For the caller: the event itself, or its regarding matter / project.
    /// </summary>
    private async Task<ChannelResult> QueryOverdueTasksAsync(
        Guid systemUserId,
        PeopleSet events,
        PeopleSet matters,
        PeopleSet projects,
        Lazy<Task<UserDay>> userDay,
        CancellationToken ct)
    {
        // "Overdue" = on or before (today - TaskOverdueDaysPast), today being the caller's LOCAL day (task 098).
        // Nothing FOR the caller → nothing is read, so the time zone is not read either.
        var today = events.Ids.Count + matters.Ids.Count + projects.Ids.Count > 0
            ? (await userDay.Value.ConfigureAwait(false)).Today
            : DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var cutoff = DataverseDateOnly.Format(today.AddDays(-TaskOverdueDaysPast));
        return await QueryEventsAsync(
            ChannelOverdueTasks,
            systemUserId,
            events,
            matters,
            projects,
            $"(Microsoft.Dynamics.CRM.OnOrBefore(PropertyName='sprk_duedate',PropertyValue='{cutoff}') or "
            + $"Microsoft.Dynamics.CRM.OnOrBefore(PropertyName='sprk_finalduedate',PropertyValue='{cutoff}'))",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The sprk_event read shared by Upcoming + Overdue: type=Task, status=Open, the caller-provided date clause, and
    /// the event-side OR regarding-matter OR regarding-project people-targeted terms.
    /// </summary>
    private async Task<ChannelResult> QueryEventsAsync(
        string label,
        Guid systemUserId,
        PeopleSet events,
        PeopleSet matters,
        PeopleSet projects,
        string dateFilter,
        CancellationToken ct)
    {
        var read = await ReadAsCallerAsync(
            systemUserId,
            "sprk_events",
            "sprk_eventid",
            new[]
            {
                $"_sprk_eventtype_ref_value eq {EventTypeTask}",
                $"statuscode eq {EventStatusOpen.ToString(CultureInfo.InvariantCulture)}",
                dateFilter,
            },
            new[]
            {
                new IdTerm("sprk_eventid", events),
                new IdTerm("_sprk_regardingmatter_value", matters),
                new IdTerm("_sprk_regardingproject_value", projects),
            },
            EventColumns,
            "sprk_finalduedate asc,sprk_duedate asc",
            label,
            ct).ConfigureAwait(false);

        if (read.Failed) return ChannelResult.FailedChannel;

        var items = read.Rows
            .OrderBy(r => GetDate(r, "sprk_finalduedate") ?? DateTimeOffset.MaxValue)
            .ThenBy(r => GetDate(r, "sprk_duedate") ?? DateTimeOffset.MaxValue)
            .Take(PerChannelMaxRows)
            .Select(MapEventToBriefingItem)
            .ToArray();

        _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", label, items.Length);
        return new ChannelResult(items, false);
    }

    /// <summary>
    /// Documents — sprk_document modified within the user's Recency window, FOR the caller (its own Created By /
    /// owner) or filed on a matter / project FOR the caller.
    /// </summary>
    private async Task<ChannelResult> QueryDocumentsAsync(
        Guid systemUserId,
        PeopleSet documents,
        PeopleSet matters,
        PeopleSet projects,
        int recencyHours,
        CancellationToken ct)
    {
        var read = await ReadAsCallerAsync(
            systemUserId,
            "sprk_documents",
            "sprk_documentid",
            new[] { $"modifiedon ge {Cutoff(recencyHours)}" },
            new[]
            {
                new IdTerm("sprk_documentid", documents),
                new IdTerm("_sprk_matter_value", matters),
                new IdTerm("_sprk_project_value", projects),
            },
            new[]
            {
                "sprk_documentid", "sprk_documentname", "sprk_filename", "sprk_documentdescription", "modifiedon",
                "_sprk_matter_value", "_sprk_project_value",
            },
            "modifiedon desc",
            ChannelDocuments,
            ct).ConfigureAwait(false);

        if (read.Failed) return ChannelResult.FailedChannel;

        var items = read.Rows
            .OrderByDescending(r => GetDate(r, "modifiedon") ?? DateTimeOffset.MinValue)
            .Take(PerChannelMaxRows)
            .Select(MapDocumentToBriefingItem)
            .ToArray();

        _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", ChannelDocuments, items.Length);
        return new ChannelResult(items, false);
    }

    /// <summary>Matters — sprk_matter FOR the caller, modified within the Recency window, Active.</summary>
    private async Task<ChannelResult> QueryMattersAsync(
        Guid systemUserId,
        PeopleSet matters,
        int recencyHours,
        CancellationToken ct)
    {
        var read = await ReadAsCallerAsync(
            systemUserId,
            "sprk_matters",
            "sprk_matterid",
            new[] { "statecode eq 0", $"modifiedon ge {Cutoff(recencyHours)}" },
            new[] { new IdTerm("sprk_matterid", matters) },
            new[] { "sprk_matterid", "sprk_mattername", "sprk_matternumber", "sprk_matterdescription", "modifiedon" },
            "modifiedon desc",
            ChannelMatters,
            ct).ConfigureAwait(false);

        if (read.Failed) return ChannelResult.FailedChannel;

        var items = read.Rows
            .OrderByDescending(r => GetDate(r, "modifiedon") ?? DateTimeOffset.MinValue)
            .Take(PerChannelMaxRows)
            .Select(MapMatterToBriefingItem)
            .ToArray();

        _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", ChannelMatters, items.Length);
        return new ChannelResult(items, false);
    }

    /// <summary>Projects — sprk_project FOR the caller, modified within the Recency window, Active.</summary>
    private async Task<ChannelResult> QueryProjectsAsync(
        Guid systemUserId,
        PeopleSet projects,
        int recencyHours,
        CancellationToken ct)
    {
        var read = await ReadAsCallerAsync(
            systemUserId,
            "sprk_projects",
            "sprk_projectid",
            new[] { "statecode eq 0", $"modifiedon ge {Cutoff(recencyHours)}" },
            new[] { new IdTerm("sprk_projectid", projects) },
            new[] { "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "sprk_projectdescription", "modifiedon" },
            "modifiedon desc",
            ChannelProjects,
            ct).ConfigureAwait(false);

        if (read.Failed) return ChannelResult.FailedChannel;

        var items = read.Rows
            .OrderByDescending(r => GetDate(r, "modifiedon") ?? DateTimeOffset.MinValue)
            .Take(PerChannelMaxRows)
            .Select(MapProjectToBriefingItem)
            .ToArray();

        _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", ChannelProjects, items.Length);
        return new ChannelResult(items, false);
    }

    /// <summary>
    /// To Dos — sprk_todo FOR the caller, due today or later, Open / In Progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Task 152 (#1044, owner Q8): a to-do is FOR the person who created it (a HUMAN Created By) and the person named
    /// in <c>sprk_assignedto</c> (a CONTACT lookup, matched through the caller's linked contact — task 141), plus the
    /// user who personally owns it. All three come from the people-targeting resolver; this method carries no
    /// owner condition of its own. Before this task the channel filtered <c>owninguser = caller</c>, so a TEAM-owned
    /// to-do (every server-created to-do once RecordOwnershipResolver owns it by team) matched nobody, and the
    /// assignee was never consulted. Server-created to-dos have the BFF application user as Created By, which is why
    /// the generators now fill <c>sprk_assignedto</c> (TodoGenerationService, TaskActionCore, the external portal).
    /// </para>
    /// <para>
    /// Date filter (R7 W12 widget cutover, 2026-07-01): due today or later — undated to-dos are excluded so they do
    /// not clutter the digest.
    /// </para>
    /// </remarks>
    private async Task<ChannelResult> QueryTodosAsync(
        Guid systemUserId, PeopleSet todos, Lazy<Task<UserDay>> userDay, CancellationToken ct)
    {
        // Task 098: "today" is the caller's LOCAL day (the UTC day dropped a to-do due today from 20:00 Eastern).
        var today = DataverseDateOnly.Format(todos.Ids.Count > 0
            ? (await userDay.Value.ConfigureAwait(false)).Today
            : DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));

        var read = await ReadAsCallerAsync(
            systemUserId,
            "sprk_todos",
            "sprk_todoid",
            new[]
            {
                $"(statuscode eq {TodoStatusOpen.ToString(CultureInfo.InvariantCulture)} or statuscode eq {TodoStatusInProgress.ToString(CultureInfo.InvariantCulture)})",
                "sprk_duedate ne null",
                $"Microsoft.Dynamics.CRM.OnOrAfter(PropertyName='sprk_duedate',PropertyValue='{today}')",
            },
            new[] { new IdTerm("sprk_todoid", todos) },
            new[]
            {
                "sprk_todoid", "sprk_name", "sprk_description", "sprk_duedate", "sprk_priority",
                "_sprk_regardingmatter_value", "modifiedon",
            },
            "sprk_duedate asc,sprk_priority asc",
            ChannelTodos,
            ct).ConfigureAwait(false);

        if (read.Failed) return ChannelResult.FailedChannel;

        var items = read.Rows
            .OrderBy(r => GetDate(r, "sprk_duedate") ?? DateTimeOffset.MaxValue)
            .ThenBy(r => GetInt(r, "sprk_priority") ?? int.MaxValue)
            .Take(PerChannelMaxRows)
            .Select(MapTodoToBriefingItem)
            .ToArray();

        _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", ChannelTodos, items.Length);
        return new ChannelResult(items, false);
    }

    private static string Cutoff(int recencyHours) =>
        DateTime.UtcNow.AddHours(-recencyHours).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    // ──────────────────────────────────────────────────────────────────────────
    // Per-entity → BriefingItem projection (Web API rows read as the caller)
    // ──────────────────────────────────────────────────────────────────────────

    private static BriefingItem MapEventToBriefingItem(Dictionary<string, JsonElement> row)
    {
        var id = GetGuid(row, "sprk_eventid") ?? Guid.Empty;
        var matterId = GetGuid(row, "_sprk_regardingmatter_value");
        return new BriefingItem
        {
            Id = id.ToString(),
            EntityType = EntityEvent,
            EntityId = id.ToString(),
            Title = GetString(row, "sprk_eventname") ?? "(untitled event)",
            Priority = MapPriority(GetInt(row, "sprk_priority")),
            DueDate = GetDate(row, "sprk_finalduedate") ?? GetDate(row, "sprk_duedate"),
            RegardingMatterName = matterId is null ? null : GetFormatted(row, "_sprk_regardingmatter_value"),
            RegardingMatterId = matterId?.ToString(),
            ModifiedOn = GetDate(row, "modifiedon"),
        };
    }

    private static BriefingItem MapDocumentToBriefingItem(Dictionary<string, JsonElement> row)
    {
        var id = GetGuid(row, "sprk_documentid") ?? Guid.Empty;
        var matterId = GetGuid(row, "_sprk_matter_value");
        var name = GetString(row, "sprk_documentname")
                   ?? GetString(row, "sprk_filename")
                   ?? "(untitled document)";
        return new BriefingItem
        {
            Id = id.ToString(),
            EntityType = EntityDocument,
            EntityId = id.ToString(),
            Title = name,
            Body = GetString(row, "sprk_documentdescription"),
            Priority = "normal",
            DueDate = null,
            RegardingMatterName = matterId is null ? null : GetFormatted(row, "_sprk_matter_value"),
            RegardingMatterId = matterId?.ToString(),
            ModifiedOn = GetDate(row, "modifiedon"),
        };
    }

    private static BriefingItem MapMatterToBriefingItem(Dictionary<string, JsonElement> row)
    {
        var matterId = GetGuid(row, "sprk_matterid") ?? Guid.Empty;
        var matterName = GetString(row, "sprk_mattername") ?? "(untitled matter)";
        // Operator UAT (2026-07-09): title line = "{number}   {name}". Number omitted
        // gracefully when the matter has none (falls back to name only).
        var matterNumber = GetString(row, "sprk_matternumber");
        var matterTitle = string.IsNullOrWhiteSpace(matterNumber) ? matterName : $"{matterNumber}   {matterName}";
        return new BriefingItem
        {
            Id = matterId.ToString(),
            EntityType = EntityMatter,
            EntityId = matterId.ToString(),
            Title = matterTitle,
            Body = GetString(row, "sprk_matterdescription"),
            Priority = "normal",
            DueDate = null,
            // Self-regarding — the matter IS the regarding entity, surface as click-through.
            RegardingMatterName = matterName,
            RegardingMatterId = matterId.ToString(),
            ModifiedOn = GetDate(row, "modifiedon"),
        };
    }

    private static BriefingItem MapProjectToBriefingItem(Dictionary<string, JsonElement> row)
    {
        var projectId = GetGuid(row, "sprk_projectid") ?? Guid.Empty;
        var projectName = GetString(row, "sprk_projectname") ?? "(untitled project)";
        // Operator UAT (2026-07-09): title line = "{number}   {name}" (same as matters).
        var projectNumber = GetString(row, "sprk_projectnumber");
        var projectTitle = string.IsNullOrWhiteSpace(projectNumber) ? projectName : $"{projectNumber}   {projectName}";
        return new BriefingItem
        {
            Id = projectId.ToString(),
            EntityType = EntityProject,
            EntityId = projectId.ToString(),
            Title = projectTitle,
            Body = GetString(row, "sprk_projectdescription"),
            Priority = "normal",
            DueDate = null,
            // Self-regarding — entity-link points to the project itself.
            RegardingMatterName = projectName,
            RegardingMatterId = projectId.ToString(),
            ModifiedOn = GetDate(row, "modifiedon"),
        };
    }

    private static BriefingItem MapTodoToBriefingItem(Dictionary<string, JsonElement> row)
    {
        var todoId = GetGuid(row, "sprk_todoid") ?? Guid.Empty;
        var matterId = GetGuid(row, "_sprk_regardingmatter_value");
        return new BriefingItem
        {
            Id = todoId.ToString(),
            EntityType = EntityTodo,
            EntityId = todoId.ToString(),
            Title = GetString(row, "sprk_name") ?? "(untitled to do)",
            Body = GetString(row, "sprk_description"),
            Priority = MapPriority(GetInt(row, "sprk_priority")),
            DueDate = GetDate(row, "sprk_duedate"),
            RegardingMatterName = matterId is null ? null : GetFormatted(row, "_sprk_regardingmatter_value"),
            RegardingMatterId = matterId?.ToString(),
            ModifiedOn = GetDate(row, "modifiedon"),
        };
    }

    /// <summary>
    /// Map a priority option value to the narrator's expected "normal"|"high"|"urgent" strings.
    /// Source schemas:
    ///   sprk_event.sprk_priority = Low(100000000)|Normal(100000001)|High(100000002)|Urgent(100000003)
    ///   sprk_todo.sprk_priority  = Urgent(100000000)|High(100000001)|Medium(100000002)|Low(100000003)
    /// The narrator's downstream prompt convention is "urgent" > "high" > "normal" (lowercase strings).
    /// Per-entity schemas have different "urgent" code points; honor only explicit high/urgent on
    /// sprk_event (100000002/100000003) and explicit urgent/high on sprk_todo (100000000/100000001).
    /// Default to "normal" for unknown/empty.
    /// </summary>
    private static string MapPriority(int? priority)
    {
        if (priority is null) return "normal";
        return priority.Value switch
        {
            // sprk_event values
            100000003 => "urgent",
            100000002 => "high",
            // sprk_todo values
            100000000 => "urgent",
            100000001 => "high",
            _ => "normal"
        };
    }

    // ── Web API row readers ────────────────────────────────────────────────

    private static string? GetString(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static string? GetFormatted(Dictionary<string, JsonElement> row, string key) =>
        GetString(row, key + FormattedValueSuffix);

    private static Guid? GetGuid(Dictionary<string, JsonElement> row, string key) =>
        Guid.TryParse(GetString(row, key), out var g) && g != Guid.Empty ? g : null;

    private static int? GetInt(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : null;

    private static bool? GetBool(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var el)
            ? el.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;

    private static DateTimeOffset? GetDate(Dictionary<string, JsonElement> row, string key) =>
        DateTimeOffset.TryParse(
            GetString(row, key),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var d)
            ? d
            : null;

    // ──────────────────────────────────────────────────────────────────────────
    // Narrate request assembly
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Project the 6 channel item arrays into the request shape the narrator consumes.
    /// Empty channels are filtered (no point narrating a 0-item bucket). Priority items
    /// = top overdue tasks + top upcoming tasks (most urgent surface first).
    /// </summary>
    private static DailyBriefingNarrateRequest BuildNarrateRequest(
        BriefingItem[] upcomingTasks,
        BriefingItem[] overdueTasks,
        BriefingItem[] documents,
        BriefingItem[] matters,
        BriefingItem[] projects,
        BriefingItem[] todos)
    {
        // R5 task 034 (FR-C5) — de-dup across the 6 channels before assembling
        // categories/priorityItems/channels/total below. An item reachable via more than
        // one channel — e.g. an sprk_event whose sprk_duedate falls in the Overdue window
        // while its sprk_finalduedate falls in the Upcoming window (QueryEventsAsync's OR
        // date-filter allows both) — must appear exactly once in the assembled output.
        // Keys on stable record identity (EntityType + EntityId), NEVER display text, so
        // two genuinely-distinct records sharing a title both survive. First-occurrence
        // wins in this fixed channel-priority order (upcoming, overdue, documents,
        // matters, projects, todos); each channel's OWN internal ordering (e.g. Documents'
        // modifiedon desc) is untouched — de-dup only removes items, it never reorders.
        var seenKeys = new HashSet<(string EntityType, string EntityId)>();
        upcomingTasks = DeduplicateAcrossChannels(upcomingTasks, seenKeys);
        overdueTasks = DeduplicateAcrossChannels(overdueTasks, seenKeys);
        documents = DeduplicateAcrossChannels(documents, seenKeys);
        matters = DeduplicateAcrossChannels(matters, seenKeys);
        projects = DeduplicateAcrossChannels(projects, seenKeys);
        todos = DeduplicateAcrossChannels(todos, seenKeys);

        var categories = new[]
        {
            new NotificationCategoryDto { Name = "Upcoming Tasks", Count = upcomingTasks.Length, UnreadCount = upcomingTasks.Length },
            new NotificationCategoryDto { Name = "Overdue Tasks",  Count = overdueTasks.Length,  UnreadCount = overdueTasks.Length },
            new NotificationCategoryDto { Name = "Documents",      Count = documents.Length,     UnreadCount = documents.Length },
            new NotificationCategoryDto { Name = "Matters",        Count = matters.Length,       UnreadCount = matters.Length },
            new NotificationCategoryDto { Name = "Projects",       Count = projects.Length,      UnreadCount = projects.Length },
            new NotificationCategoryDto { Name = "To Dos",         Count = todos.Length,         UnreadCount = todos.Length },
        }.Where(c => c.Count > 0).ToArray();

        // Priority items = top overdue (most urgent) + top upcoming.  Surface a small set
        // so the narrator can construct a focused "top action" sentence.
        var priorityItems = overdueTasks.Take(3)
            .Concat(upcomingTasks.Take(3))
            .Select(i => new PriorityItemDto
            {
                Category = "Tasks",
                Title = i.Title,
                DueDate = i.DueDate
            })
            .ToArray();

        var channels = new[]
        {
            ToChannel(ChannelUpcomingTasks, "Upcoming Tasks", upcomingTasks),
            ToChannel(ChannelOverdueTasks,  "Overdue Tasks",  overdueTasks),
            ToChannel(ChannelDocuments,     "Documents",      documents),
            ToChannel(ChannelMatters,       "Matters",        matters),
            ToChannel(ChannelProjects,      "Projects",       projects),
            ToChannel(ChannelTodos,         "To Dos",         todos),
        }.Where(c => c.Items.Length > 0).ToArray();

        var total = upcomingTasks.Length + overdueTasks.Length + documents.Length
                  + matters.Length + projects.Length + todos.Length;

        var request = new DailyBriefingNarrateRequest
        {
            Categories = categories,
            PriorityItems = priorityItems,
            TotalNotificationCount = total,
            Channels = channels,
        };

        // R5 task 013 (FR-A4): stamp the deterministic TL;DR scaffolding onto the request here,
        // while the view model is freshly assembled — this is the collector's "Layer 1" fact
        // computation the narrator's TL;DR call consumes as ground truth (never computed by the
        // LLM). See BuildTldrFacts for the aggregation rules.
        return request with { TldrFacts = BuildTldrFacts(request) };
    }

    /// <summary>
    /// R5 task 034 (FR-C5) — filter <paramref name="items"/> down to the entries whose
    /// (EntityType, EntityId) identity has not already been claimed by an earlier-processed
    /// channel, recording each surviving key into <paramref name="seenKeys"/> as it goes.
    /// Keys on stable record identity, never display text (Title), so two distinct records
    /// that happen to share a title both survive — only a true re-appearance of the SAME
    /// record (same entity + same GUID) is dropped. Preserves the input array's relative
    /// order; only removes items, never reshuffles.
    /// </summary>
    private static BriefingItem[] DeduplicateAcrossChannels(
        BriefingItem[] items,
        HashSet<(string EntityType, string EntityId)> seenKeys)
    {
        if (items.Length == 0)
        {
            return items;
        }

        var kept = new List<BriefingItem>(items.Length);
        foreach (var item in items)
        {
            if (seenKeys.Add((item.EntityType, item.EntityId)))
            {
                kept.Add(item);
            }
        }

        return kept.Count == items.Length ? items : kept.ToArray();
    }

    /// <summary>
    /// R5 task 013 (FR-A4) — compute the TL;DR's factual scaffolding DETERMINISTICALLY from the
    /// already-assembled request view model (categories/priorityItems/channels/total, themselves
    /// sourced deterministically from Dataverse records above). This is the ONLY payload the
    /// TL;DR LLM call receives as ground truth (see <see cref="DailyBriefingNarrator"/>'s
    /// tldrPayload construction) — every count/date/name the TL;DR asserts traces back to this
    /// method, never to LLM invention.
    /// </summary>
    /// <remarks>
    /// Pure, static, and side-effect-free — callable from the collector (the primary path, via
    /// <see cref="BuildNarrateRequest"/> above) AND from <see cref="DailyBriefingNarrator"/> as a
    /// deterministic fallback for callers that construct a <see cref="DailyBriefingNarrateRequest"/>
    /// directly without going through the collector (e.g. the legacy <c>/narrate</c> leg, or a
    /// test driving the narrator in isolation) — both call sites reach the exact same computation,
    /// so the ground-truth facts are identical regardless of entry path (ADR-039 Binding decides
    /// dispatch; this method is what decides FACTS, and it decides them the same way everywhere).
    /// Aggregates rather than dumps (ADR-015): RecordNames/KeyDates are capped
    /// (<see cref="TldrFactsMaxRecordNames"/> / <see cref="TldrFactsMaxKeyDates"/>) so a
    /// large channel cannot blow the TL;DR call's token budget.
    /// </remarks>
    internal static TldrFactsDto BuildTldrFacts(DailyBriefingNarrateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Key dates: sourced from PriorityItems only — those are already the curated top-N
        // most-urgent items (top overdue + top upcoming, see BuildNarrateRequest above), so
        // their due dates ARE the "key dates" the TL;DR should be able to cite. Channel items
        // (ChannelItemDto) don't carry a due-date field at all (only CreatedOn), so there is no
        // additional due-date signal to mine there.
        var keyDates = request.PriorityItems
            .Where(p => p.DueDate.HasValue)
            .Select(p => new TldrKeyDateDto { RecordName = p.Title, Date = p.DueDate!.Value })
            .Take(TldrFactsMaxKeyDates)
            .ToArray();

        // Record names: the bounded set of names the TL;DR is permitted to reference. Priority-
        // item titles first (already curated + most likely to be worth naming), then the
        // regarding/record names surfaced across channels — deduplicated and capped so a
        // 50-row channel does not dump every record name into the LLM call.
        var recordNames = request.PriorityItems.Select(p => p.Title)
            .Concat(request.Channels.SelectMany(ch => ch.Items.Select(i => i.RegardingName)))
            .Concat(request.Channels.SelectMany(ch => ch.Items.Select(i => i.Title)))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(TldrFactsMaxRecordNames)
            .ToArray();

        return new TldrFactsDto
        {
            TotalNotificationCount = request.TotalNotificationCount,
            CategoryCounts = request.Categories,
            PriorityItemCount = request.PriorityItems.Length,
            KeyDates = keyDates,
            RecordNames = recordNames,
        };
    }

    /// <summary>
    /// Convert a BriefingItem array to a ChannelNarrationInput. Carries the per-item
    /// entity-link metadata (RegardingEntityType + RegardingId) so downstream
    /// EnrichBulletWithEntityRefs can build click-through links across all 6 entity types.
    /// </summary>
    private static ChannelNarrationInput ToChannel(string category, string label, BriefingItem[] items) =>
        new()
        {
            Category = category,
            Label = label,
            Items = items.Select(i => new ChannelItemDto
            {
                Id = i.Id,
                Title = i.Title,
                Body = i.Body ?? string.Empty,
                Priority = i.Priority,
                // The per-bullet entity-link projection. For self-regarding rows (Matter,
                // Project) RegardingId == EntityId. For Tasks/Documents/ToDos the regarding
                // is the parent matter (when one exists).
                RegardingName = i.RegardingMatterName ?? string.Empty,
                RegardingEntityType = string.IsNullOrEmpty(i.RegardingMatterId)
                    ? string.Empty
                    : (i.EntityType == EntityProject
                        // sprk_project rows are self-regarding via RegardingMatterId trick —
                        // surface the entity type as project (not matter) so downstream URLs
                        // route correctly.
                        ? EntityProject
                        : EntityMatter),
                RegardingId = i.RegardingMatterId ?? string.Empty,
                // R7 Wave 12 task 135 — carry the source entity type so
                // EnrichBulletWithEntityRefs can fall back to the source record
                // when an item has no regarding matter (orphan tasks, todos
                // without regarding, etc.). Without this, orphan bullets render
                // with no click-through link in the widget (link node hides
                // when primaryEntityType/Id are empty).
                SourceEntityType = i.EntityType,
                CreatedOn = (i.ModifiedOn ?? DateTimeOffset.UtcNow).ToString("o")
            }).ToArray()
        };
}
