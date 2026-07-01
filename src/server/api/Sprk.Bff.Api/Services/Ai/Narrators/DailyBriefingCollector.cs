// R7 Wave 12 T131 (2026-06-30) — DailyBriefingCollector — 6-entity expansion.
//
// PURPOSE: Build the DailyBriefingNarrateRequest payload directly from live Dataverse
// queries — no appNotification dependency, no scheduled playbooks, no notification
// creation pipeline.
//   [widget call] → [collector queries Dataverse live, 6 entity types] → [narrator] → [response].
//
// MVP SCOPE (this file) — 6 operator-specified channels per wave12-mvp-completion-plan §2.1:
//   1. Upcoming Tasks — sprk_event, type=Task, sprk_duedate OR sprk_finalduedate in next 5 days, status=Open
//   2. Overdue Tasks  — sprk_event, type=Task, sprk_duedate OR sprk_finalduedate > 5 days past, status=Open
//   3. Documents      — sprk_document, modifiedon last 5 days, member of regarding matter/project
//   4. Matters        — sprk_matter, modifiedon last 5 days, statecode=Active, member
//   5. Projects       — sprk_project, modifiedon last 5 days, statecode=Active, member
//   6. To Dos         — sprk_todo, sprk_duedate today/tomorrow, owner/assignee
//
// MEMBERSHIP MODEL (R7 T130 fix landed): all ownership filters delegate to
// `IMembershipResolverService` (post T130 commit `451603bac`). The resolver returns the
// set of entity-instance IDs the caller is a member of (across all roles: owner,
// owningTeam, assignedAttorney, assignedParalegal, assignedLawFirm — for each entity's
// configured membership-bearing fields, discovered via metadata scan).
//
// Membership-resolved candidate set strategy per entity:
//   Tasks (Upcoming/Overdue): UNION of (a) sprk_event memberships (event owner/assignee
//                              roles on the event itself) and (b) sprk_matter memberships
//                              (events whose sprk_regardingmatter is a matter the user is on)
//                              and (c) sprk_project memberships (regarding-project membership)
//   Documents:                UNION of sprk_matter + sprk_project memberships (regarding edges)
//   Matters:                  sprk_matter memberships
//   Projects:                 sprk_project memberships
//   To Dos:                   no membership filter — sprk_todo is per-user
//                              (owner OR sprk_assignedto), filtered inline.
//
// Per-channel narrowing query uses ConditionOperator.In on the resolved candidate set +
// the operator-specified date/status filters (next-5-days, last-5-days, etc.).
//
// PRESERVES: BriefingItem projection shape (downstream narrator depends on it). Each
// channel's items[] populates RegardingMatterName/RegardingMatterId for entity-link
// click-through; the BriefingItem.EntityType field carries the source entity for
// EnrichBulletWithEntityRefs (per-channel `primaryEntityType` in narrator output).
//
// Reference:
//   projects/spaarke-ai-platform-unification-r7/notes/wave12-mvp-completion-plan.md §2.1
//   projects/spaarke-ai-platform-unification-r7/tasks/131-extend-collector-six-entities.poml
//   src/server/api/Sprk.Bff.Api/Services/Workspace/BriefingService.cs (reference pattern for
//     IMembershipResolverService consumption — top-priority-matter resolver)

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Membership;

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
/// Live-query collector for the Daily Briefing widget. Returns a populated
/// <see cref="DailyBriefingNarrateRequest"/> directly from Dataverse — bypasses
/// appNotification entirely. 6-channel coverage per operator spec (wave12 §2.1).
/// </summary>
/// <remarks>
/// Unsealed (R7 Wave 12 post-T135 CI fix 2026-06-30 — PR #520) so
/// <see cref="NullDailyBriefingCollector"/> can subclass it for the compound-OFF kill-switch
/// path. The /api/ai/daily-briefing/render endpoint is mapped unconditionally; without a
/// Null peer registered when Analysis:Enabled=false || DocumentIntelligence:Enabled=false,
/// minimal-API parameter inference fails at host startup. Mirrors
/// <see cref="Chat.NullSessionSummarizeOrchestrator"/> + ADR-032 §F.1.
/// </remarks>
public class DailyBriefingCollector
{
    // sprk_event type GUIDs (consistent with deployed notification playbooks).
    // Source of truth: sprk_eventtype_ref records in spaarkedev1.
    private const string EventTypeTask = "124f5fc9-98ff-f011-8406-7c1e525abd8b";

    // sprk_event statuscode values (consistent with deployed notification playbooks).
    private const int EventStatusOpen = 659490001;

    // sprk_todo statuscode values per docs/data-model schema (Open=1, In Progress=659490001).
    // Treat both as "active" for the today/tomorrow surface.
    private const int TodoStatusOpen = 1;
    private const int TodoStatusInProgress = 659490001;

    // Date-window constants (operator-stated; wave12 §2.1)
    private const int TaskUpcomingDaysAhead = 5;
    private const int TaskOverdueDaysPast = 5;
    private const int DocumentModifiedDaysBack = 5;
    private const int MatterModifiedDaysBack = 5;
    private const int ProjectModifiedDaysBack = 5;

    // Entity logical names (kept as constants so a typo fails at compile time).
    private const string EntityEvent = "sprk_event";
    private const string EntityDocument = "sprk_document";
    private const string EntityMatter = "sprk_matter";
    private const string EntityProject = "sprk_project";
    private const string EntityTodo = "sprk_todo";

    // Channel-code naming convention (T133 coordination) — kebab-case slugs.
    // Keep these stable; they are the keys downstream consumers (channel registry,
    // EnrichBulletWithEntityRefs primaryEntityType resolution) join on.
    private const string ChannelUpcomingTasks = "upcoming-tasks";
    private const string ChannelOverdueTasks = "overdue-tasks";
    private const string ChannelDocuments = "documents";
    private const string ChannelMatters = "matters";
    private const string ChannelProjects = "projects";
    private const string ChannelTodos = "to-dos";

    // Per-channel row caps (defensive — large result sets degrade narrator quality
    // before they cost LLM tokens). Operator may tune later via config table (deferred
    // per wave12 §4 — config-table-with-rules is interpreter).
    private const int PerChannelMaxRows = 50;

    private readonly IGenericEntityService _entityService;
    private readonly IMembershipResolverService _membershipResolver;
    private readonly ILogger<DailyBriefingCollector> _logger;

    public DailyBriefingCollector(
        IGenericEntityService entityService,
        IMembershipResolverService membershipResolver,
        ILogger<DailyBriefingCollector> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _membershipResolver = membershipResolver ?? throw new ArgumentNullException(nameof(membershipResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        _entityService = null!;
        _membershipResolver = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Run all 6 channel queries in parallel against Dataverse and build the request
    /// payload the narrator consumes. Empty channels are filtered out of the final
    /// payload (the narrator skips empty channels naturally).
    /// </summary>
    public virtual async Task<DailyBriefingNarrateRequest> CollectAsync(
        Guid systemUserId,
        CancellationToken ct)
    {
        if (systemUserId == Guid.Empty)
        {
            throw new ArgumentException("systemUserId is required", nameof(systemUserId));
        }
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "DailyBriefingCollector starting for systemUserId={SystemUserId}", systemUserId);

        // ── Phase 1: resolve candidate-set IDs for the 3 entities in parallel.
        //
        // R7 W12 widget cutover (2026-06-30) — BYPASS of IMembershipResolverService.
        //   The resolver returns 0 rows for users who own records via `ownerid` on
        //   sprk_matter / sprk_project / sprk_event, despite the FetchXml running against
        //   the correct systemUserId. Root cause is in the resolver's descriptor/condition
        //   translation for the polymorphic Owner attribute; needs deeper investigation and
        //   affects all consumers of IMembershipResolverService (chat scope resolution,
        //   knowledge base filtering, etc.). Filed as DEF-NNN for follow-up.
        //
        //   For the Daily Briefing MVP tonight, this collector uses direct `owninguser`
        //   queries — matches the Todos pattern below. Trade-off: collaborators (members
        //   via sprk_assignedattorney*, sprk_assignedparalegal*, sprk_assignedtointernal,
        //   etc.) are NOT included in the candidate set. Owner-only scope is the operator-
        //   accepted MVP fallback until the resolver bug is fixed. Once the resolver is
        //   fixed, revert to `ResolveMembershipsSafelyAsync` calls to restore collaborator
        //   scope.
        //
        //   Reference: projects/spaarke-ai-platform-unification-r7/notes/handoffs/
        //              daily-briefing-widget-cutover-restart.md §10 secondary risk.
        var membershipsTask = Task.WhenAll(
            ResolveOwnedIdsAsync(systemUserId, EntityEvent, "sprk_eventid", filterActive: false, ct),
            ResolveOwnedIdsAsync(systemUserId, EntityMatter, "sprk_matterid", filterActive: true, ct),
            ResolveOwnedIdsAsync(systemUserId, EntityProject, "sprk_projectid", filterActive: true, ct));

        var memberships = await membershipsTask.ConfigureAwait(false);
        var eventIds = memberships[0];
        var matterIds = memberships[1];
        var projectIds = memberships[2];

        _logger.LogInformation(
            "DailyBriefingCollector owned IDs resolved (resolver bypass — R7 W12): events={EventCount}, matters={MatterCount}, projects={ProjectCount}",
            eventIds.Count, matterIds.Count, projectIds.Count);

        // ── Phase 2: query per-channel candidate rows in parallel.
        //    Each query returns empty array on Dataverse failure (failure-soft per channel —
        //    a single broken channel does not abort the whole briefing).
        var queries = await Task.WhenAll(
            QueryUpcomingTasksAsync(systemUserId, eventIds, matterIds, projectIds, ct),
            QueryOverdueTasksAsync(systemUserId, eventIds, matterIds, projectIds, ct),
            QueryDocumentsAsync(matterIds, projectIds, ct),
            QueryMattersAsync(matterIds, ct),
            QueryProjectsAsync(projectIds, ct),
            QueryTodosAsync(systemUserId, ct)
        ).ConfigureAwait(false);

        var upcomingTasks = queries[0];
        var overdueTasks = queries[1];
        var documents = queries[2];
        var matters = queries[3];
        var projects = queries[4];
        var todos = queries[5];

        var request = BuildNarrateRequest(
            upcomingTasks, overdueTasks, documents, matters, projects, todos);

        _logger.LogInformation(
            "DailyBriefingCollector completed in {DurationMs}ms: upcoming={A}, overdue={B}, docs={C}, matters={D}, projects={E}, todos={F}, totalNotifs={Total}",
            (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            upcomingTasks.Length, overdueTasks.Length, documents.Length,
            matters.Length, projects.Length, todos.Length,
            request.TotalNotificationCount);

        return request;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // High Priority section (R7 W12 feedback item 9)
    //
    // Cross-entity flag scan: returns every record across the 7 flagged entities
    // (matter, project, invoice, document, workassignment, event, todo) where
    // sprk_highpriority = true OR sprk_monitor = true — regardless of ownership
    // in this MVP (operator flags what THEY care about; scoping by owninguser is
    // a follow-up refinement if the list becomes too broad).
    //
    // No LLM call — widget renders as a compact list of clickable record refs.
    // Ordered by due date ascending (undated items last).
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// R7 W12 feedback item 9 (2026-07-01) — collect all high-priority items across the 7
    /// flagged entities. Bypasses membership resolution + narrator. Returns items whose
    /// <c>sprk_highpriority</c> OR <c>sprk_monitor</c> = true, sorted by due date ascending.
    /// Empty array on error (per-entity queries are failure-soft).
    /// </summary>
    public virtual async Task<HighPriorityItemDto[]> CollectHighPriorityAsync(
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

        // 7 parallel queries — one per flagged entity. Each returns HighPriorityItemDto[].
        // Every query is failure-soft: on Dataverse exception, the entity contributes
        // an empty array (logged as warning) so the digest still renders.
        var queries = await Task.WhenAll(
            QueryHighPriorityMatterAsync(ct),
            QueryHighPriorityProjectAsync(ct),
            QueryHighPriorityInvoiceAsync(ct),
            QueryHighPriorityDocumentAsync(ct),
            QueryHighPriorityWorkassignmentAsync(ct),
            QueryHighPriorityEventAsync(ct),
            QueryHighPriorityTodoAsync(systemUserId, ct)
        ).ConfigureAwait(false);

        var all = queries.SelectMany(x => x)
            .OrderBy(x => x.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogInformation(
            "DailyBriefingCollector.CollectHighPriorityAsync completed in {DurationMs}ms: total={Total} " +
            "(matters={M}, projects={P}, invoices={I}, docs={D}, workassignments={W}, events={E}, todos={T})",
            (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            all.Length,
            queries[0].Length, queries[1].Length, queries[2].Length, queries[3].Length,
            queries[4].Length, queries[5].Length, queries[6].Length);

        return all;
    }

    private async Task<HighPriorityItemDto[]> QueryHighPriorityMatterAsync(CancellationToken ct)
    {
        return await QueryHighPriorityGenericAsync(
            entityType: EntityMatter,
            idColumn: "sprk_matterid",
            nameColumn: "sprk_mattername",
            descriptionColumn: "sprk_matterdescription",
            dueDateColumn: null,
            kindLabel: "Matter",
            includeStateFilter: true,
            ct: ct).ConfigureAwait(false);
    }

    private async Task<HighPriorityItemDto[]> QueryHighPriorityProjectAsync(CancellationToken ct)
    {
        return await QueryHighPriorityGenericAsync(
            entityType: EntityProject,
            idColumn: "sprk_projectid",
            nameColumn: "sprk_projectname",
            descriptionColumn: "sprk_description",
            dueDateColumn: null,
            kindLabel: "Project",
            includeStateFilter: true,
            ct: ct).ConfigureAwait(false);
    }

    private async Task<HighPriorityItemDto[]> QueryHighPriorityInvoiceAsync(CancellationToken ct)
    {
        // Invoice has sprk_invoicedate (invoice date, NOT payment due date) — don't map to
        // DueDate. Include all flagged invoices regardless of date. Operator can refine later.
        return await QueryHighPriorityGenericAsync(
            entityType: "sprk_invoice",
            idColumn: "sprk_invoiceid",
            nameColumn: "sprk_name",
            descriptionColumn: "sprk_description",
            dueDateColumn: null,
            kindLabel: "Invoice",
            includeStateFilter: true,
            ct: ct).ConfigureAwait(false);
    }

    private async Task<HighPriorityItemDto[]> QueryHighPriorityDocumentAsync(CancellationToken ct)
    {
        return await QueryHighPriorityGenericAsync(
            entityType: EntityDocument,
            idColumn: "sprk_documentid",
            nameColumn: "sprk_documentname",
            descriptionColumn: "sprk_documentdescription",
            dueDateColumn: null,
            kindLabel: "Document",
            includeStateFilter: true,
            ct: ct).ConfigureAwait(false);
    }

    private async Task<HighPriorityItemDto[]> QueryHighPriorityWorkassignmentAsync(CancellationToken ct)
    {
        return await QueryHighPriorityGenericAsync(
            entityType: "sprk_workassignment",
            idColumn: "sprk_workassignmentid",
            nameColumn: "sprk_name",
            descriptionColumn: "sprk_description",
            dueDateColumn: "sprk_responseduedate",
            kindLabel: "Work Assignment",
            includeStateFilter: true,
            ct: ct).ConfigureAwait(false);
    }

    private async Task<HighPriorityItemDto[]> QueryHighPriorityEventAsync(CancellationToken ct)
    {
        // Event has both sprk_duedate and sprk_finalduedate; use sprk_finalduedate first,
        // fall back to sprk_duedate. This mirrors QueryUpcomingTasksAsync's precedence.
        return await QueryHighPriorityGenericAsync(
            entityType: EntityEvent,
            idColumn: "sprk_eventid",
            nameColumn: "sprk_eventname",
            descriptionColumn: "sprk_eventdescription",
            dueDateColumn: "sprk_finalduedate",
            fallbackDueDateColumn: "sprk_duedate",
            kindLabel: "Task",
            includeStateFilter: false,
            ct: ct).ConfigureAwait(false);
    }

    private async Task<HighPriorityItemDto[]> QueryHighPriorityTodoAsync(Guid systemUserId, CancellationToken ct)
    {
        // R7 W12 fix (2026-07-01): todos scoped to `owninguser = systemUserId` to match
        // the primary Todos channel — operators shouldn't see other users' flagged todos
        // in their own briefing.
        return await QueryHighPriorityGenericAsync(
            entityType: EntityTodo,
            idColumn: "sprk_todoid",
            nameColumn: "sprk_name",
            descriptionColumn: "sprk_description",
            dueDateColumn: "sprk_duedate",
            kindLabel: "To Do",
            includeStateFilter: true,
            ownerUserId: systemUserId,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared query pattern for high-priority items on any of the 7 flagged entities.
    /// Applies the flag filter (sprk_highpriority=true OR sprk_monitor=true), optional
    /// state filter (statecode=0), and optional owner filter (owninguser=systemuserid).
    /// Projects into HighPriorityItemDto. Failure-soft: returns empty array on error.
    /// </summary>
    private async Task<HighPriorityItemDto[]> QueryHighPriorityGenericAsync(
        string entityType,
        string idColumn,
        string nameColumn,
        string? dueDateColumn,
        string kindLabel,
        bool includeStateFilter,
        CancellationToken ct,
        string? descriptionColumn = null,
        string? fallbackDueDateColumn = null,
        Guid? ownerUserId = null)
    {
        try
        {
            var columns = new List<string> { idColumn, nameColumn, "sprk_highpriority", "sprk_monitor", "modifiedon" };
            if (!string.IsNullOrEmpty(descriptionColumn)) columns.Add(descriptionColumn);
            if (!string.IsNullOrEmpty(dueDateColumn)) columns.Add(dueDateColumn);
            if (!string.IsNullOrEmpty(fallbackDueDateColumn)) columns.Add(fallbackDueDateColumn);

            var query = new QueryExpression(entityType)
            {
                NoLock = true,
                TopCount = PerChannelMaxRows,
                ColumnSet = new ColumnSet(columns.ToArray())
            };

            // Flag filter: HighPriority OR Monitor
            var flagGroup = new FilterExpression(LogicalOperator.Or);
            flagGroup.AddCondition("sprk_highpriority", ConditionOperator.Equal, true);
            flagGroup.AddCondition("sprk_monitor", ConditionOperator.Equal, true);
            query.Criteria.AddFilter(flagGroup);

            if (includeStateFilter)
            {
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            }

            if (ownerUserId.HasValue && ownerUserId.Value != Guid.Empty)
            {
                query.Criteria.AddCondition("owninguser", ConditionOperator.Equal, ownerUserId.Value);
            }

            var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var items = new List<HighPriorityItemDto>(result.Entities.Count);
            foreach (var e in result.Entities)
            {
                var id = e.GetAttributeValue<Guid>(idColumn);
                if (id == Guid.Empty) continue;

                DateTimeOffset? dueDate = null;
                if (!string.IsNullOrEmpty(dueDateColumn))
                {
                    var raw = e.GetAttributeValue<DateTime?>(dueDateColumn);
                    if (!raw.HasValue && !string.IsNullOrEmpty(fallbackDueDateColumn))
                    {
                        raw = e.GetAttributeValue<DateTime?>(fallbackDueDateColumn);
                    }
                    if (raw.HasValue)
                    {
                        dueDate = new DateTimeOffset(DateTime.SpecifyKind(raw.Value, DateTimeKind.Utc));
                    }
                }

                var highPriority = e.GetAttributeValue<bool?>("sprk_highpriority") ?? false;
                var monitor = e.GetAttributeValue<bool?>("sprk_monitor") ?? false;

                var description = !string.IsNullOrEmpty(descriptionColumn)
                    ? (e.GetAttributeValue<string>(descriptionColumn) ?? string.Empty)
                    : string.Empty;

                DateTimeOffset? modifiedOn = null;
                var rawModified = e.GetAttributeValue<DateTime?>("modifiedon");
                if (rawModified.HasValue)
                {
                    modifiedOn = new DateTimeOffset(DateTime.SpecifyKind(rawModified.Value, DateTimeKind.Utc));
                }

                var reason = highPriority && monitor ? "Both"
                    : highPriority ? "HighPriority"
                    : monitor ? "Monitor"
                    : string.Empty;

                var action = ClassifyAction(dueDate, modifiedOn);

                items.Add(new HighPriorityItemDto
                {
                    EntityType = entityType,
                    EntityId = id.ToString(),
                    Name = e.GetAttributeValue<string>(nameColumn) ?? "(untitled)",
                    DueDate = dueDate,
                    HighPriority = highPriority,
                    Monitor = monitor,
                    KindLabel = kindLabel,
                    Description = description,
                    Action = action,
                    Reason = reason,
                    ModifiedOn = modifiedOn,
                });
            }
            return items.ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DailyBriefingCollector.QueryHighPriority failed for entity={EntityType} — returning empty",
                entityType);
            return Array.Empty<HighPriorityItemDto>();
        }
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
    private static string ClassifyAction(DateTimeOffset? dueDate, DateTimeOffset? modifiedOn)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero);
        var tomorrowStart = todayStart.AddDays(1);
        var sevenDaysFromNow = todayStart.AddDays(7);
        var sevenDaysAgo = todayStart.AddDays(-7);

        if (dueDate.HasValue)
        {
            if (dueDate.Value < todayStart) return "Overdue";
            if (dueDate.Value < tomorrowStart) return "DueToday";
            if (dueDate.Value < sevenDaysFromNow) return "DueSoon";
        }

        if (modifiedOn.HasValue && modifiedOn.Value >= sevenDaysAgo)
        {
            return "Recent";
        }

        return "None";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Membership resolution (delegates to IMembershipResolverService)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// R7 W12 widget cutover (2026-06-30) — direct-owner ID query. Returns record IDs of
    /// <paramref name="entityType"/> where <c>owninguser</c> equals <paramref name="systemUserId"/>.
    /// Used in place of <see cref="ResolveMembershipsSafelyAsync"/> while the membership
    /// resolver's polymorphic-Owner classification bug is being investigated (DEF-NNN).
    /// </summary>
    /// <param name="systemUserId">The calling user's systemuserid.</param>
    /// <param name="entityType">Logical name of the target entity (sprk_matter / sprk_project / sprk_event).</param>
    /// <param name="idColumn">Primary-key column name for the entity (e.g. sprk_matterid).</param>
    /// <param name="filterActive">If true, adds statecode = 0 (Active). sprk_event doesn't have a statecode filter here — event status is checked in QueryEventsAsync.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of owned entity IDs. Empty on error (logged as warning) so downstream channels degrade gracefully.</returns>
    private async Task<IReadOnlyList<Guid>> ResolveOwnedIdsAsync(
        Guid systemUserId,
        string entityType,
        string idColumn,
        bool filterActive,
        CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression(entityType)
            {
                NoLock = true,
                TopCount = 500,
                ColumnSet = new ColumnSet(idColumn)
            };
            query.Criteria.AddCondition("owninguser", ConditionOperator.Equal, systemUserId);
            if (filterActive)
            {
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            }

            var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var ids = new List<Guid>(result.Entities.Count);
            foreach (var e in result.Entities)
            {
                var id = e.GetAttributeValue<Guid>(idColumn);
                if (id != Guid.Empty)
                {
                    ids.Add(id);
                }
            }
            return ids;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DailyBriefingCollector direct-owner lookup failed for entity={EntityType}; downstream channels will degrade",
                entityType);
            return Array.Empty<Guid>();
        }
    }

    /// <summary>
    /// Resolves membership for a single entity type. Returns empty list on any failure
    /// (logged as warning) so a partial-failure in the membership pipeline doesn't
    /// abort the whole briefing — the dependent per-channel query simply returns
    /// zero rows and the channel is skipped.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveMembershipsSafelyAsync(
        Guid systemUserId, string entityType, CancellationToken ct)
    {
        try
        {
            var response = await _membershipResolver
                .ResolveAsync(systemUserId, entityType, options: null, ct)
                .ConfigureAwait(false);
            return response.Ids;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DailyBriefingCollector membership resolution failed for entity={EntityType}; channel will be empty",
                entityType);
            return Array.Empty<Guid>();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Channel query methods
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Upcoming Tasks — sprk_event of type Task, due in next 5 days, status Open.
    /// Membership filter: event-side OR regarding-matter OR regarding-project member.
    /// </summary>
    private Task<BriefingItem[]> QueryUpcomingTasksAsync(
        Guid systemUserId,
        IReadOnlyList<Guid> eventIds,
        IReadOnlyList<Guid> matterIds,
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct)
    {
        return QueryEventsAsync(
            label: ChannelUpcomingTasks,
            eventIds: eventIds,
            matterIds: matterIds,
            projectIds: projectIds,
            applyDateFilter: query =>
            {
                // sprk_duedate OR sprk_finalduedate within next N days.
                var dateGroup = new FilterExpression(LogicalOperator.Or);
                dateGroup.AddCondition("sprk_duedate", ConditionOperator.NextXDays, TaskUpcomingDaysAhead);
                dateGroup.AddCondition("sprk_finalduedate", ConditionOperator.NextXDays, TaskUpcomingDaysAhead);
                query.Criteria.AddFilter(dateGroup);
            },
            ct: ct);
    }

    /// <summary>
    /// Overdue Tasks — sprk_event of type Task, due > 5 days past, status Open.
    /// Membership filter: event-side OR regarding-matter OR regarding-project member.
    /// </summary>
    private Task<BriefingItem[]> QueryOverdueTasksAsync(
        Guid systemUserId,
        IReadOnlyList<Guid> eventIds,
        IReadOnlyList<Guid> matterIds,
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct)
    {
        return QueryEventsAsync(
            label: ChannelOverdueTasks,
            eventIds: eventIds,
            matterIds: matterIds,
            projectIds: projectIds,
            applyDateFilter: query =>
            {
                // sprk_duedate OR sprk_finalduedate older than (today - TaskOverdueDaysPast).
                // Convention: "overdue" = past the threshold; use ConditionOperator.OnOrBefore
                // against a date computed once at call time (deterministic across the multi-query
                // fan-out within the same request).
                var cutoff = DateTime.UtcNow.Date.AddDays(-TaskOverdueDaysPast);
                var dateGroup = new FilterExpression(LogicalOperator.Or);
                dateGroup.AddCondition("sprk_duedate", ConditionOperator.OnOrBefore, cutoff);
                dateGroup.AddCondition("sprk_finalduedate", ConditionOperator.OnOrBefore, cutoff);
                query.Criteria.AddFilter(dateGroup);
            },
            ct: ct);
    }

    /// <summary>
    /// Common sprk_event query path used by Upcoming Tasks + Overdue Tasks.
    /// Both channels share the same shape (type=Task, status=Open, member-scope) and
    /// differ only in the date filter — apply it via the <paramref name="applyDateFilter"/>
    /// callback.
    /// </summary>
    private async Task<BriefingItem[]> QueryEventsAsync(
        string label,
        IReadOnlyList<Guid> eventIds,
        IReadOnlyList<Guid> matterIds,
        IReadOnlyList<Guid> projectIds,
        Action<QueryExpression> applyDateFilter,
        CancellationToken ct)
    {
        try
        {
            // Membership scope: event-side ids OR regarding-matter ids OR regarding-project ids.
            // If all 3 are empty, the user has no candidate rows at all — return early.
            if (eventIds.Count == 0 && matterIds.Count == 0 && projectIds.Count == 0)
            {
                return Array.Empty<BriefingItem>();
            }

            var query = new QueryExpression(EntityEvent)
            {
                NoLock = true,
                TopCount = PerChannelMaxRows,
                ColumnSet = new ColumnSet(
                    "sprk_eventid",
                    "sprk_eventname",
                    "sprk_duedate",
                    "sprk_finalduedate",
                    "modifiedon",
                    "sprk_regardingmatter",
                    "sprk_regardingproject",
                    "ownerid",
                    "sprk_priority")
            };

            // type=Task AND status=Open
            query.Criteria.AddCondition("sprk_eventtype_ref", ConditionOperator.Equal, new Guid(EventTypeTask));
            query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, EventStatusOpen);

            // Date filter — caller-provided (differs between upcoming/overdue).
            applyDateFilter(query);

            // Membership scope: OR group across the three candidate-set sources.
            var memberGroup = new FilterExpression(LogicalOperator.Or);
            if (eventIds.Count > 0)
            {
                memberGroup.AddCondition("sprk_eventid", ConditionOperator.In, eventIds.Cast<object>().ToArray());
            }
            if (matterIds.Count > 0)
            {
                memberGroup.AddCondition("sprk_regardingmatter", ConditionOperator.In, matterIds.Cast<object>().ToArray());
            }
            if (projectIds.Count > 0)
            {
                memberGroup.AddCondition("sprk_regardingproject", ConditionOperator.In, projectIds.Cast<object>().ToArray());
            }
            query.Criteria.AddFilter(memberGroup);

            query.AddOrder("sprk_finalduedate", OrderType.Ascending);
            query.AddOrder("sprk_duedate", OrderType.Ascending);

            var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var items = result.Entities.Select(MapEventToBriefingItem).ToArray();

            _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", label, items.Length);
            return items;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DailyBriefingCollector channel={Label} query failed — returning empty array", label);
            return Array.Empty<BriefingItem>();
        }
    }

    /// <summary>
    /// Documents — sprk_document modified in last 5 days where the user is a member of
    /// the regarding matter or project.
    /// </summary>
    private async Task<BriefingItem[]> QueryDocumentsAsync(
        IReadOnlyList<Guid> matterIds,
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct)
    {
        try
        {
            // If user has no matter/project memberships, no candidate documents.
            if (matterIds.Count == 0 && projectIds.Count == 0)
            {
                return Array.Empty<BriefingItem>();
            }

            var cutoff = DateTime.UtcNow.AddDays(-DocumentModifiedDaysBack);

            var query = new QueryExpression(EntityDocument)
            {
                NoLock = true,
                TopCount = PerChannelMaxRows,
                ColumnSet = new ColumnSet(
                    "sprk_documentid",
                    "sprk_documentname",
                    "sprk_filename",
                    "modifiedon",
                    "sprk_matter",
                    "sprk_project",
                    "sprk_documenttype",
                    "sprk_documentstatus")
            };

            query.Criteria.AddCondition("modifiedon", ConditionOperator.GreaterEqual, cutoff);

            // Membership scope: matter OR project regarding lookup.
            var memberGroup = new FilterExpression(LogicalOperator.Or);
            if (matterIds.Count > 0)
            {
                memberGroup.AddCondition("sprk_matter", ConditionOperator.In, matterIds.Cast<object>().ToArray());
            }
            if (projectIds.Count > 0)
            {
                memberGroup.AddCondition("sprk_project", ConditionOperator.In, projectIds.Cast<object>().ToArray());
            }
            query.Criteria.AddFilter(memberGroup);

            query.AddOrder("modifiedon", OrderType.Descending);

            var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var items = result.Entities.Select(MapDocumentToBriefingItem).ToArray();

            _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", ChannelDocuments, items.Length);
            return items;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DailyBriefingCollector channel={Label} query failed — returning empty array", ChannelDocuments);
            return Array.Empty<BriefingItem>();
        }
    }

    /// <summary>
    /// Matters — sprk_matter modified in last 5 days where the user is a member. Active only.
    /// </summary>
    private async Task<BriefingItem[]> QueryMattersAsync(
        IReadOnlyList<Guid> matterIds,
        CancellationToken ct)
    {
        try
        {
            if (matterIds.Count == 0)
            {
                return Array.Empty<BriefingItem>();
            }

            var cutoff = DateTime.UtcNow.AddDays(-MatterModifiedDaysBack);

            var query = new QueryExpression(EntityMatter)
            {
                NoLock = true,
                TopCount = PerChannelMaxRows,
                ColumnSet = new ColumnSet(
                    "sprk_matterid",
                    "sprk_mattername",
                    "sprk_matternumber",
                    "modifiedon",
                    "statecode",
                    "statuscode")
            };

            // statecode=Active (0)
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("modifiedon", ConditionOperator.GreaterEqual, cutoff);
            query.Criteria.AddCondition("sprk_matterid", ConditionOperator.In, matterIds.Cast<object>().ToArray());

            query.AddOrder("modifiedon", OrderType.Descending);

            var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var items = result.Entities.Select(MapMatterToBriefingItem).ToArray();

            _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", ChannelMatters, items.Length);
            return items;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DailyBriefingCollector channel={Label} query failed — returning empty array", ChannelMatters);
            return Array.Empty<BriefingItem>();
        }
    }

    /// <summary>
    /// Projects — sprk_project modified in last 5 days where the user is a member. Active only.
    /// </summary>
    private async Task<BriefingItem[]> QueryProjectsAsync(
        IReadOnlyList<Guid> projectIds,
        CancellationToken ct)
    {
        try
        {
            if (projectIds.Count == 0)
            {
                return Array.Empty<BriefingItem>();
            }

            var cutoff = DateTime.UtcNow.AddDays(-ProjectModifiedDaysBack);

            var query = new QueryExpression(EntityProject)
            {
                NoLock = true,
                TopCount = PerChannelMaxRows,
                ColumnSet = new ColumnSet(
                    "sprk_projectid",
                    "sprk_projectname",
                    "sprk_projectnumber",
                    "modifiedon",
                    "statecode",
                    "statuscode")
            };

            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("modifiedon", ConditionOperator.GreaterEqual, cutoff);
            query.Criteria.AddCondition("sprk_projectid", ConditionOperator.In, projectIds.Cast<object>().ToArray());

            query.AddOrder("modifiedon", OrderType.Descending);

            var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var items = result.Entities.Select(MapProjectToBriefingItem).ToArray();

            _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", ChannelProjects, items.Length);
            return items;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DailyBriefingCollector channel={Label} query failed — returning empty array", ChannelProjects);
            return Array.Empty<BriefingItem>();
        }
    }

    /// <summary>
    /// To Dos — sprk_todo due today or later, owner=user, status Open/In Progress.
    ///
    /// R7 W12 widget cutover fix (2026-07-01 UTC):
    ///   1. Ownership filter changed from `ownerid = systemUserId` to
    ///      `owninguser = systemUserId`. The polymorphic Owner attribute doesn't
    ///      match plain Guid values reliably in QueryExpression (same class of
    ///      bug as the membership resolver — see ResolveOwnedIdsAsync above).
    ///   2. Date filter widened from `= today OR = tomorrow` (exact timestamp
    ///      match, misses records with any time-of-day) to `>= today start UTC`
    ///      (matches operator intent: "today, tomorrow, later"). Operator can
    ///      set a due date in their local time; as long as the stored UTC value
    ///      is at-or-after UTC midnight today, it shows.
    ///
    /// sprk_assignedto targets contact; only ownerid/owninguser is systemuser-
    /// typed — contact-side filtering would require an identity-normalization
    /// round-trip and is out of MVP scope.
    /// </summary>
    private async Task<BriefingItem[]> QueryTodosAsync(Guid systemUserId, CancellationToken ct)
    {
        try
        {
            var todayStartUtc = DateTime.UtcNow.Date;

            var query = new QueryExpression(EntityTodo)
            {
                NoLock = true,
                TopCount = PerChannelMaxRows,
                ColumnSet = new ColumnSet(
                    "sprk_todoid",
                    "sprk_name",
                    "sprk_description",
                    "sprk_duedate",
                    "sprk_priority",
                    "sprk_regardingmatter",
                    "modifiedon",
                    "ownerid",
                    "sprk_assignedto",
                    "statuscode")
            };

            // Status: Open OR In Progress.
            var statusGroup = new FilterExpression(LogicalOperator.Or);
            statusGroup.AddCondition("statuscode", ConditionOperator.Equal, TodoStatusOpen);
            statusGroup.AddCondition("statuscode", ConditionOperator.Equal, TodoStatusInProgress);
            query.Criteria.AddFilter(statusGroup);

            // Due date: today or later (matches operator intent "today, tomorrow, later").
            // NotNull first so records without a due date are excluded (intentional —
            // undated todos would clutter the digest).
            query.Criteria.AddCondition("sprk_duedate", ConditionOperator.NotNull);
            query.Criteria.AddCondition("sprk_duedate", ConditionOperator.OnOrAfter, todayStartUtc);

            // Ownership: owninguser=user (NOT ownerid — see method docstring).
            query.Criteria.AddCondition("owninguser", ConditionOperator.Equal, systemUserId);

            query.AddOrder("sprk_duedate", OrderType.Ascending);
            query.AddOrder("sprk_priority", OrderType.Ascending);

            var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            var items = result.Entities.Select(MapTodoToBriefingItem).ToArray();

            _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", ChannelTodos, items.Length);
            return items;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DailyBriefingCollector channel={Label} query failed — returning empty array", ChannelTodos);
            return Array.Empty<BriefingItem>();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Per-entity → BriefingItem projection
    // ──────────────────────────────────────────────────────────────────────────

    private static BriefingItem MapEventToBriefingItem(Entity entity)
    {
        var matterRef = entity.GetAttributeValue<EntityReference>("sprk_regardingmatter");
        return new BriefingItem
        {
            Id = entity.Id.ToString(),
            EntityType = EntityEvent,
            EntityId = entity.GetAttributeValue<Guid>("sprk_eventid").ToString(),
            Title = entity.GetAttributeValue<string>("sprk_eventname") ?? "(untitled event)",
            Priority = MapPriority(entity.GetAttributeValue<OptionSetValue>("sprk_priority")),
            DueDate = ParseDate(entity.GetAttributeValue<DateTime?>("sprk_finalduedate")
                ?? entity.GetAttributeValue<DateTime?>("sprk_duedate")),
            RegardingMatterName = matterRef?.Name,
            RegardingMatterId = matterRef?.Id.ToString(),
            ModifiedOn = ParseDate(entity.GetAttributeValue<DateTime?>("modifiedon")),
        };
    }

    private static BriefingItem MapDocumentToBriefingItem(Entity entity)
    {
        var matterRef = entity.GetAttributeValue<EntityReference>("sprk_matter");
        var name = entity.GetAttributeValue<string>("sprk_documentname")
                   ?? entity.GetAttributeValue<string>("sprk_filename")
                   ?? "(untitled document)";
        return new BriefingItem
        {
            Id = entity.Id.ToString(),
            EntityType = EntityDocument,
            EntityId = entity.GetAttributeValue<Guid>("sprk_documentid").ToString(),
            Title = name,
            Priority = "normal",
            DueDate = null,
            RegardingMatterName = matterRef?.Name,
            RegardingMatterId = matterRef?.Id.ToString(),
            ModifiedOn = ParseDate(entity.GetAttributeValue<DateTime?>("modifiedon")),
        };
    }

    private static BriefingItem MapMatterToBriefingItem(Entity entity)
    {
        var matterId = entity.GetAttributeValue<Guid>("sprk_matterid");
        var matterName = entity.GetAttributeValue<string>("sprk_mattername") ?? "(untitled matter)";
        return new BriefingItem
        {
            Id = entity.Id.ToString(),
            EntityType = EntityMatter,
            EntityId = matterId.ToString(),
            Title = matterName,
            Priority = "normal",
            DueDate = null,
            // Self-regarding — the matter IS the regarding entity, surface as click-through.
            RegardingMatterName = matterName,
            RegardingMatterId = matterId.ToString(),
            ModifiedOn = ParseDate(entity.GetAttributeValue<DateTime?>("modifiedon")),
        };
    }

    private static BriefingItem MapProjectToBriefingItem(Entity entity)
    {
        var projectId = entity.GetAttributeValue<Guid>("sprk_projectid");
        var projectName = entity.GetAttributeValue<string>("sprk_projectname") ?? "(untitled project)";
        return new BriefingItem
        {
            Id = entity.Id.ToString(),
            EntityType = EntityProject,
            EntityId = projectId.ToString(),
            Title = projectName,
            Priority = "normal",
            DueDate = null,
            // Self-regarding — entity-link points to the project itself.
            RegardingMatterName = projectName,
            RegardingMatterId = projectId.ToString(),
            ModifiedOn = ParseDate(entity.GetAttributeValue<DateTime?>("modifiedon")),
        };
    }

    private static BriefingItem MapTodoToBriefingItem(Entity entity)
    {
        var todoId = entity.GetAttributeValue<Guid>("sprk_todoid");
        var matterRef = entity.GetAttributeValue<EntityReference>("sprk_regardingmatter");
        return new BriefingItem
        {
            Id = entity.Id.ToString(),
            EntityType = EntityTodo,
            EntityId = todoId.ToString(),
            Title = entity.GetAttributeValue<string>("sprk_name") ?? "(untitled to do)",
            Body = entity.GetAttributeValue<string>("sprk_description"),
            Priority = MapPriority(entity.GetAttributeValue<OptionSetValue>("sprk_priority")),
            DueDate = ParseDate(entity.GetAttributeValue<DateTime?>("sprk_duedate")),
            RegardingMatterName = matterRef?.Name,
            RegardingMatterId = matterRef?.Id.ToString(),
            ModifiedOn = ParseDate(entity.GetAttributeValue<DateTime?>("modifiedon")),
        };
    }

    /// <summary>
    /// Map an OptionSetValue priority to the narrator's expected "normal"|"high"|"urgent" strings.
    /// Source schemas:
    ///   sprk_event.sprk_priority = Low(100000000)|Normal(100000001)|High(100000002)|Urgent(100000003)
    ///   sprk_todo.sprk_priority  = Urgent(100000000)|High(100000001)|Medium(100000002)|Low(100000003)
    /// The narrator's downstream prompt convention is "urgent" > "high" > "normal" (lowercase strings).
    /// Per-entity schemas have different "urgent" code points; honor only explicit high/urgent on
    /// sprk_event (100000002/100000003) and explicit urgent/high on sprk_todo (100000000/100000001).
    /// Default to "normal" for unknown/empty.
    /// </summary>
    private static string MapPriority(OptionSetValue? priority)
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

    private static DateTimeOffset? ParseDate(DateTime? dt) =>
        dt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc), TimeSpan.Zero) : null;

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

        return new DailyBriefingNarrateRequest
        {
            Categories = categories,
            PriorityItems = priorityItems,
            TotalNotificationCount = total,
            Channels = channels,
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
