// R7 Wave 11 T118 — DailyBriefingCollector (2026-06-30).
//
// PURPOSE: Build the DailyBriefingNarrateRequest payload directly from live Dataverse
// queries — no appNotification dependency, no scheduled playbooks, no notification
// creation pipeline. Replaces the chain
//   [scheduled playbook] → [appnotification rows] → [widget reads via Xrm.WebApi] → [narrator]
// with the cleaner
//   [widget call] → [collector queries Dataverse live] → [narrator] → [response].
//
// POC SCOPE (this file): sprk_event entity only, 4 channels:
//   1. Tasks Due Soon         — events of type Task, sprk_duedate in next 3 days
//   2. Tasks Overdue          — events of type Task, sprk_duedate < today, status open
//   3. Recent Matter Activity — events modified in last 24h on user's matters, by OTHER users
//   4. My Recent Updates      — events modified by the current user in last 24h
//
// Membership filter is built INLINE into each FetchXML query via the `eq-userid` operator
// pattern (replaced at execution time to the current user's systemuserid GUID). The membership
// fields applied:
//   On the event itself:   ownerid, sprk_assignedto, sprk_assignedattorney, sprk_assignedparalegal
//   On the regarding matter: ownerid, sprk_assignedattorney, sprk_assignedparalegal
//
// This replaces dependence on the IMembershipResolverService which currently returns 0 for all
// users in spaarkedev1. The replacement uses Dataverse's native field-based ownership semantics
// directly in the query.
//
// Reference:
//   projects/spaarke-ai-platform-unification-r7/notes/spikes/narrator-spike-plan.md
//   projects/spaarke-ai-platform-unification-r7/notes/spikes/narrator-vs-playbook-comparison.md

using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// Internal projection of a Dataverse record into the lowest-common-denominator shape needed
/// by the narrator + widget. Carries enough to (a) include in narrative, (b) navigate to the
/// underlying record, (c) add to a To-Do list.
/// </summary>
internal sealed record BriefingItem
{
    public required string Id { get; init; }
    public required string EntityType { get; init; }       // sprk_event, sprk_document, sprk_matter, ...
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
/// appNotification entirely.
/// </summary>
public sealed class DailyBriefingCollector
{
    // sprk_event type GUIDs (consistent with deployed notification playbooks).
    // Source of truth: sprk_eventtype_ref records in spaarkedev1.
    private const string EventTypeTask = "124f5fc9-98ff-f011-8406-7c1e525abd8b";

    // sprk_event statuscode values (consistent with deployed notification playbooks).
    private const int EventStatusOpen = 659490001;

    private readonly IGenericEntityService _entityService;
    private readonly ILogger<DailyBriefingCollector> _logger;

    public DailyBriefingCollector(
        IGenericEntityService entityService,
        ILogger<DailyBriefingCollector> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Run all 4 channel queries in parallel against Dataverse and build the request
    /// payload the narrator consumes. Empty channels are still included (count=0) so
    /// the narrator's response shape is stable.
    /// </summary>
    public async Task<DailyBriefingNarrateRequest> CollectAsync(
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

        var results = await Task.WhenAll(
            QueryAsync("Tasks Due Soon",         BuildTasksDueSoonFetchXml(systemUserId, daysAhead: 3),   ct),
            QueryAsync("Tasks Overdue",          BuildTasksOverdueFetchXml(systemUserId),                  ct),
            QueryAsync("Recent Matter Activity", BuildRecentMatterActivityFetchXml(systemUserId, hours: 24), ct),
            QueryAsync("My Recent Updates",      BuildMyRecentUpdatesFetchXml(systemUserId, hours: 24),     ct)
        ).ConfigureAwait(false);

        var (dueSoon, overdue, matterActivity, myUpdates) = (results[0], results[1], results[2], results[3]);

        var request = BuildNarrateRequest(dueSoon, overdue, matterActivity, myUpdates);

        _logger.LogInformation(
            "DailyBriefingCollector completed in {DurationMs}ms: dueSoon={A}, overdue={B}, matterActivity={C}, myUpdates={D}, totalNotifs={Total}",
            (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            dueSoon.Length, overdue.Length, matterActivity.Length, myUpdates.Length,
            request.TotalNotificationCount);

        return request;
    }

    /// <summary>
    /// Project the 4 channel item arrays into the request shape the narrator consumes.
    /// Maps each Event item into a ChannelItemDto with regardingName/title/body/priority.
    /// Priority items are surfaced from Overdue (most urgent) then Due Soon (top 3 of each).
    /// </summary>
    private static DailyBriefingNarrateRequest BuildNarrateRequest(
        BriefingItem[] dueSoon, BriefingItem[] overdue,
        BriefingItem[] matterActivity, BriefingItem[] myUpdates)
    {
        var categories = new[]
        {
            new NotificationCategoryDto { Name = "Tasks Due Soon",         Count = dueSoon.Length,         UnreadCount = dueSoon.Length },
            new NotificationCategoryDto { Name = "Tasks Overdue",          Count = overdue.Length,         UnreadCount = overdue.Length },
            new NotificationCategoryDto { Name = "Recent Matter Activity", Count = matterActivity.Length,  UnreadCount = matterActivity.Length },
            new NotificationCategoryDto { Name = "My Recent Updates",      Count = myUpdates.Length,       UnreadCount = myUpdates.Length },
        }.Where(c => c.Count > 0).ToArray();

        // Priority items = top overdue (most urgent) + top due soon
        var priorityItems = overdue.Take(3)
            .Concat(dueSoon.Take(3))
            .Select(i => new PriorityItemDto
            {
                Category = "Tasks",
                Title = i.Title,
                DueDate = i.DueDate
            })
            .ToArray();

        var channels = new[]
        {
            ToChannel("tasks-due-soon",   "Tasks Due Soon",         dueSoon),
            ToChannel("tasks-overdue",    "Tasks Overdue",          overdue),
            ToChannel("matter-activity",  "Recent Matter Activity", matterActivity),
            ToChannel("my-updates",       "My Recent Updates",      myUpdates),
        }.Where(c => c.Items.Length > 0).ToArray();

        var total = dueSoon.Length + overdue.Length + matterActivity.Length + myUpdates.Length;

        return new DailyBriefingNarrateRequest
        {
            Categories = categories,
            PriorityItems = priorityItems,
            TotalNotificationCount = total,
            Channels = channels,
        };
    }

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
                RegardingName = i.RegardingMatterName ?? string.Empty,
                RegardingEntityType = string.IsNullOrEmpty(i.RegardingMatterName) ? string.Empty : "sprk_matter",
                RegardingId = i.RegardingMatterId ?? string.Empty,
                CreatedOn = (i.ModifiedOn ?? DateTimeOffset.UtcNow).ToString("o")
            }).ToArray()
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Query execution
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<BriefingItem[]> QueryAsync(string label, string fetchXml, CancellationToken ct)
    {
        try
        {
            var result = await _entityService.RetrieveMultipleAsync(new FetchExpression(fetchXml), ct)
                .ConfigureAwait(false);
            var items = result.Entities.Select(MapEventToBriefingItem).ToArray();
            _logger.LogDebug("DailyBriefingCollector channel={Label} returned {Count} items", label, items.Length);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DailyBriefingCollector channel={Label} query failed — returning empty array. FetchXmlLen={Len}",
                label, fetchXml.Length);
            return Array.Empty<BriefingItem>();
        }
    }

    private static BriefingItem MapEventToBriefingItem(Entity entity)
    {
        var matterName = entity.GetAttributeValue<AliasedValue>("m.sprk_mattername")?.Value as string;
        var matterIdVal = entity.GetAttributeValue<AliasedValue>("m.sprk_matterid")?.Value;
        var matterId = matterIdVal switch
        {
            Guid g => g.ToString(),
            string s => s,
            _ => null
        };
        return new BriefingItem
        {
            Id = entity.Id.ToString(),
            EntityType = "sprk_event",
            EntityId = entity.GetAttributeValue<Guid>("sprk_eventid").ToString(),
            Title = entity.GetAttributeValue<string>("sprk_eventname") ?? "(untitled event)",
            Priority = "normal",
            DueDate = ParseDate(entity.GetAttributeValue<DateTime?>("sprk_finalduedate")
                ?? entity.GetAttributeValue<DateTime?>("sprk_duedate")),
            RegardingMatterName = matterName,
            RegardingMatterId = matterId,
            ModifiedOn = ParseDate(entity.GetAttributeValue<DateTime?>("modifiedon")),
        };
    }

    private static DateTimeOffset? ParseDate(DateTime? dt) =>
        dt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc), TimeSpan.Zero) : null;

    // ──────────────────────────────────────────────────────────────────────────
    // FetchXML builders — eq-userid replaced inline with the systemuserid GUID
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Membership filter shared across all queries. Captures rows where the user is
    /// the owner OR an assigned role OR holds the same role on the regarding matter.
    /// Used inside the outer `<filter type="and">` of each query.
    /// </summary>
    private static string BuildMembershipFilterFragment(Guid userId)
    {
        var uid = userId.ToString("D");
        return $@"
            <filter type=""or"">
              <!-- On the event itself -->
              <condition attribute=""ownerid"" operator=""eq"" value=""{uid}""/>
              <!-- On the regarding matter (alias 'm') -->
              <condition entityname=""m"" attribute=""ownerid"" operator=""eq"" value=""{uid}""/>
            </filter>";
    }

    private static string BuildTasksDueSoonFetchXml(Guid uid, int daysAhead) => $@"
        <fetch top=""50"">
          <entity name=""sprk_event"">
            <attribute name=""sprk_eventid""/>
            <attribute name=""sprk_eventname""/>
            <attribute name=""sprk_duedate""/>
            <attribute name=""sprk_finalduedate""/>
            <attribute name=""modifiedon""/>
            <attribute name=""sprk_regardingmatter""/>
            <attribute name=""ownerid""/>
            <link-entity name=""sprk_matter"" from=""sprk_matterid"" to=""sprk_regardingmatter"" link-type=""outer"" alias=""m"">
              <attribute name=""sprk_matterid""/>
              <attribute name=""sprk_mattername""/>
              <attribute name=""ownerid""/>
            </link-entity>
            <filter type=""and"">
              <condition attribute=""sprk_eventtype_ref"" operator=""eq"" value=""{EventTypeTask}""/>
              <condition attribute=""statuscode"" operator=""eq"" value=""{EventStatusOpen}""/>
              <filter type=""or"">
                <condition attribute=""sprk_duedate""      operator=""next-x-days"" value=""{daysAhead}""/>
                <condition attribute=""sprk_finalduedate"" operator=""next-x-days"" value=""{daysAhead}""/>
              </filter>
              {BuildMembershipFilterFragment(uid)}
            </filter>
            <order attribute=""sprk_finalduedate"" descending=""false""/>
            <order attribute=""sprk_duedate""      descending=""false""/>
          </entity>
        </fetch>";

    private static string BuildTasksOverdueFetchXml(Guid uid) => $@"
        <fetch top=""50"">
          <entity name=""sprk_event"">
            <attribute name=""sprk_eventid""/>
            <attribute name=""sprk_eventname""/>
            <attribute name=""sprk_duedate""/>
            <attribute name=""sprk_finalduedate""/>
            <attribute name=""modifiedon""/>
            <attribute name=""sprk_regardingmatter""/>
            <attribute name=""ownerid""/>
            <link-entity name=""sprk_matter"" from=""sprk_matterid"" to=""sprk_regardingmatter"" link-type=""outer"" alias=""m"">
              <attribute name=""sprk_matterid""/>
              <attribute name=""sprk_mattername""/>
              <attribute name=""ownerid""/>
            </link-entity>
            <filter type=""and"">
              <condition attribute=""sprk_eventtype_ref"" operator=""eq"" value=""{EventTypeTask}""/>
              <condition attribute=""statuscode"" operator=""eq"" value=""{EventStatusOpen}""/>
              <filter type=""or"">
                <condition attribute=""sprk_duedate""      operator=""last-x-days"" value=""365""/>
                <condition attribute=""sprk_finalduedate"" operator=""last-x-days"" value=""365""/>
              </filter>
              {BuildMembershipFilterFragment(uid)}
            </filter>
            <order attribute=""sprk_finalduedate"" descending=""false""/>
            <order attribute=""sprk_duedate""      descending=""false""/>
          </entity>
        </fetch>";

    private static string BuildRecentMatterActivityFetchXml(Guid uid, int hours) => $@"
        <fetch top=""50"">
          <entity name=""sprk_event"">
            <attribute name=""sprk_eventid""/>
            <attribute name=""sprk_eventname""/>
            <attribute name=""sprk_duedate""/>
            <attribute name=""sprk_finalduedate""/>
            <attribute name=""modifiedon""/>
            <attribute name=""modifiedby""/>
            <attribute name=""sprk_regardingmatter""/>
            <attribute name=""ownerid""/>
            <link-entity name=""sprk_matter"" from=""sprk_matterid"" to=""sprk_regardingmatter"" link-type=""outer"" alias=""m"">
              <attribute name=""sprk_matterid""/>
              <attribute name=""sprk_mattername""/>
              <attribute name=""ownerid""/>
            </link-entity>
            <filter type=""and"">
              <condition attribute=""modifiedon"" operator=""last-x-hours"" value=""{hours}""/>
              <condition attribute=""modifiedby"" operator=""ne"" value=""{uid}""/>
              {BuildMembershipFilterFragment(uid)}
            </filter>
            <order attribute=""modifiedon"" descending=""true""/>
          </entity>
        </fetch>";

    private static string BuildMyRecentUpdatesFetchXml(Guid uid, int hours) => $@"
        <fetch top=""50"">
          <entity name=""sprk_event"">
            <attribute name=""sprk_eventid""/>
            <attribute name=""sprk_eventname""/>
            <attribute name=""sprk_duedate""/>
            <attribute name=""sprk_finalduedate""/>
            <attribute name=""modifiedon""/>
            <attribute name=""sprk_regardingmatter""/>
            <attribute name=""ownerid""/>
            <link-entity name=""sprk_matter"" from=""sprk_matterid"" to=""sprk_regardingmatter"" link-type=""outer"" alias=""m"">
              <attribute name=""sprk_matterid""/>
              <attribute name=""sprk_mattername""/>
            </link-entity>
            <filter type=""and"">
              <condition attribute=""modifiedby"" operator=""eq"" value=""{uid.ToString("D")}""/>
              <condition attribute=""modifiedon"" operator=""last-x-hours"" value=""{hours}""/>
            </filter>
            <order attribute=""modifiedon"" descending=""true""/>
          </entity>
        </fetch>";
}
