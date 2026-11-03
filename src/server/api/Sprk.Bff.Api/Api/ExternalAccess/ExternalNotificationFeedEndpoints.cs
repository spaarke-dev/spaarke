// spaarke-SPA-external-access-platform-r3 task 037 (2026-10-12) — FR-08 in-portal notification feed + last-seen marker.
//
// GET  /api/v1/external/notifications       — the caller's feed, newest first, with unreadCount against the marker.
// POST /api/v1/external/notifications/seen  — sets the caller's last-seen marker to now.
//
// Both are mounted UNDER the shared /api/v1/external group (ExternalAccessEndpoints), so they inherit the
// ExternalCollaboration dual-scheme policy AND the group-level CallerPrincipalAuthorizationFilter. The identity is the
// resolved CallerPrincipal on HttpContext.Items and nothing else; the handlers never branch on plane (NFR-01).
//
// The feed is DERIVED (owner 2026-10-08: no new table; appnotification targets systemusers only) from two sources:
//   (a) grants — ACTIVE, unexpired sprk_externalrecordaccess rows naming the caller's contact as grantee, granted OR
//       renewed in place in the last 30 days (ExternalDataService.GetRecentGrantsAsync; a renewal is the row's modifiedon,
//       the only timestamp that moves — owner 2026-10-12, with the known false positives noted there). Each row's root is re-checked against the caller
//       principal's accessible set (CallerPrincipal.RightsOn must carry Read): a grant row for a record no longer
//       accessible is DROPPED (fail-closed);
//   (b) messages — the newest messages of the last 30 days filed regarding a record IN the caller's accessible set (the
//       Read-bearing projects / matters / work assignments, plus the caller's own service requests through the Service
//       Requests module's predicate, partner switch included), read app-only by
//       CommunicationThreadReadService.ReadRecentForRecordsAppOnlyAsync — the SAME external rules as the task 033 thread
//       read: sent/received only, Privileged and Potentially Privileged NEVER (ExternallyHiddenPrivilege, owner
//       2026-10-11), internal-only only to an internal systemuser. The caller's OWN messages are excluded by the creator
//       stamp (ExternalMessageEndpoints.IsAuthoredBy — the 034 isMine rule), never by direction or sender name.
// Every item carries its record's route segment, id and title; an item whose record no longer exists (a complete title
// read did not return it — e.g. a deleted service request) is dropped, so a delete never breaks the feed. Bounded: 30
// days, at most 100 items, newest first; a cut is flagged `truncated`, never silent.
//
// Last-seen marker: Redis through ITenantCache (ADR-009; notes/decisions.md 2026-10-10 — no Dataverse column), keyed by
// the principal id (systemuser:{id} or contact:{id}) in the token's tenant, 30-day TTL refreshed on every write. Losing it
// only re-marks the last 30 days as unread: a missing or unreadable marker means "every item is unread".
//
// No email, no OBO, no new secret: every read is app-only through the BFF's existing Dataverse identity.

using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Membership;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// The external-plane notification feed (FR-08): <c>GET /api/v1/external/notifications</c> and
/// <c>POST /api/v1/external/notifications/seen</c>. See the file header for the sources, the gates and the marker.
/// </summary>
public static class ExternalNotificationFeedEndpoints
{
    /// <summary>The feed window: items older than this are never returned.</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>The most items one feed returns.</summary>
    internal const int MaxItems = 100;

    /// <summary>ITenantCache resource of the last-seen marker; the id is <see cref="MarkerId"/>.</summary>
    internal const string MarkerResource = "external-notifications-seen";

    /// <summary>Payload version of the marker (ADR-009 key versioning).</summary>
    internal const int MarkerVersion = 1;

    /// <summary>The marker's TTL, refreshed on every write. Equal to the window: an older marker changes nothing.</summary>
    internal static readonly TimeSpan MarkerTtl = TimeSpan.FromDays(30);

    /// <summary>Deny code for a /seen by a principal that is neither a systemuser nor a contact (no marker key).</summary>
    public const string DenyCallerUnattributable = "sdap.external.notifications.caller_unattributable";

    /// <summary>Error code for a /seen whose marker could not be stored (retryable).</summary>
    public const string ErrorSeenUnavailable = "EXT_NOTIFICATIONS_SEEN_UNAVAILABLE";

    /// <summary>Error code for a feed read that failed.</summary>
    public const string ErrorInternal = "EXT_NOTIFICATIONS_INTERNAL_ERROR";

    /// <summary>
    /// Error code for a caller whose derived contact could not be READ (a Dataverse fault on the systemuser row or the
    /// oid binding). Both routes answer a retryable 503 (task 037 review F3-a; same fail-closed rule as task 035).
    /// </summary>
    public const string ErrorContactUnreadable = "EXT_NOTIFICATIONS_CONTACT_UNREADABLE";

    /// <summary>The stored marker.</summary>
    internal sealed record SeenMarker(DateTimeOffset SeenAt);

    /// <summary>Entity logical name → external route segment (the task 033 message roots, reversed).</summary>
    private static readonly IReadOnlyDictionary<string, string> SegmentByEntity =
        ExternalMessageEndpoints.Roots.Values.ToDictionary(r => r.EntityLogicalName, r => r.Segment, StringComparer.OrdinalIgnoreCase);

    private const string ServiceRequestEntity = "sprk_servicerequest";

    /// <summary>
    /// Registers the feed routes on the shared external collaboration group. The group's policy and
    /// CallerPrincipalAuthorizationFilter are inherited; this adds the routes only.
    /// </summary>
    public static RouteGroupBuilder MapExternalNotificationFeedEndpoints(this RouteGroupBuilder externalGroup)
    {
        externalGroup.MapGet("/notifications", GetFeedAsync)
            .WithName("GetExternalNotificationFeed")
            .WorkforceCeilingInHandler("The feed mixes tables: items are filtered to the tables the role can Read, before each read.")
            .WithSummary("Read the caller's in-portal notification feed")
            .WithDescription(
                "Returns up to 100 items of the last 30 days, newest first: new grants to the caller and new messages by " +
                "others on records the caller can access (never internal-only to a contact, never privileged), with " +
                "unreadCount against the caller's last-seen marker.")
            // Several app-only Dataverse reads per call: per-user throttle on the caller key, as the external writes have.
            .RequireRateLimiting("dataverse-query")
            .Produces<ExternalNotificationFeedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        externalGroup.MapPost("/notifications/seen", MarkSeenAsync)
            .WithName("MarkExternalNotificationsSeen")
            .ExemptFromWorkforceCeiling("Writes only the caller's own last-seen marker in Redis; no Dataverse record.")
            .WithSummary("Set the caller's notification last-seen marker to now")
            .WithDescription(
                "Stores the caller's last-seen marker (server time). Feed items at or before it are read; unreadCount " +
                "is 0 until a newer item arrives. Takes no body.")
            .RequireRateLimiting("dataverse-query")
            .Produces<ExternalNotificationSeenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return externalGroup;
    }

    private static async Task<IResult> GetFeedAsync(
        HttpContext httpContext,
        ExternalModuleRegistry registry,
        ExternalDataService dataService,
        CommunicationThreadReadService threadReadService,
        ITenantCache cache,
        IDataversePrivilegeChecker privileges,
        [Microsoft.AspNetCore.Mvc.FromServices] ExternalInvoiceHiding? hideInvoices,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (httpContext.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal principal)
        {
            return MissingPrincipal();
        }

        // The workforce ceiling (unified-access-control-r2 task 186, review round 1 F1): the feed mixes tables, so a
        // workforce-contact caller gets only the items whose tables the Workforce role can Read, and nothing is read from a
        // table it cannot. An unavailable role reads nothing (an empty feed, not an error, so the SPA renders).
        var ceiling = await WorkforceRoleCeilingGate.CeilingForAsync(principal, privileges, ct).ConfigureAwait(false);

        var now = (httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        return await GetFeedCoreAsync(
            principal, MembershipEndpoints.ExtractTenantId(httpContext.User), registry, dataService, threadReadService,
            cache, now, logger, ct, table => WorkforceRoleCeilingGate.CanRead(ceiling, table), hideInvoices).ConfigureAwait(false);
    }

    private static Task<IResult> MarkSeenAsync(
        HttpContext httpContext,
        ITenantCache cache,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (httpContext.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal principal)
        {
            return Task.FromResult(MissingPrincipal());
        }

        var now = (httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        return MarkSeenCoreAsync(principal, MembershipEndpoints.ExtractTenantId(httpContext.User), cache, now, logger, ct);
    }

    /// <summary>
    /// A contact that could not be READ is unknown, not absent (<see cref="CallerPrincipal.ContactId"/> is Guid.Empty in
    /// both cases, so the flag decides). The grant source and the service-request messages would be missing, so the feed
    /// would look complete when it is not, and a following /seen would bury them for good. Both routes therefore answer a
    /// retryable 503 with a fixed detail (no exception text) and touch nothing (task 037 review F3-a; task 035
    /// <c>ExternalIntakeEndpoints</c> does the same). Null when the contact is readable.
    /// </summary>
    private static IResult? RefuseWhenContactUnreadable(CallerPrincipal principal, ILogger logger, string operation)
    {
        if (!(principal.ContactUnreadable && principal.ContactId == Guid.Empty))
        {
            return null;
        }

        logger.LogWarning(
            "[EXT-NOTIFY] {Operation} refused for systemuser {SystemUserId}: the derived contact could not be read.",
            operation, principal.SystemUserId);
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable",
            detail: "Your account could not be checked right now. Your notifications were not read or changed; try again shortly.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorContactUnreadable });
    }

    private static IResult MissingPrincipal() => Results.Problem(
        statusCode: StatusCodes.Status500InternalServerError,
        title: "Internal Server Error",
        detail: "Authentication context not available — ensure AddCallerPrincipalAuthorizationFilter is applied");

    /// <summary>
    /// The marker key for a principal: <c>systemuser:{id}</c> for a systemuser (the KIND decides, as the creator stamp
    /// does), else <c>contact:{id}</c>; null for a principal that is neither.
    /// </summary>
    internal static string? MarkerId(CallerPrincipal principal) =>
        principal.SystemUserId is { } systemUserId
            ? systemUserId == Guid.Empty ? null : $"systemuser:{systemUserId:D}"
            : principal.ContactId == Guid.Empty ? null : $"contact:{principal.ContactId:D}";

    /// <summary>
    /// <c>POST /notifications/seen</c>: stores now as the caller's marker (30-day TTL, refreshed by every write). A
    /// principal with no marker key is 403; a cache failure is a retryable 503 (the marker was not stored).
    /// </summary>
    internal static async Task<IResult> MarkSeenCoreAsync(
        CallerPrincipal principal, string tenantId, ITenantCache cache, DateTimeOffset now, ILogger logger, CancellationToken ct)
    {
        // Before anything else: no marker may be set while a source was unreadable (F3-a).
        if (RefuseWhenContactUnreadable(principal, logger, "Seen") is { } refused)
        {
            return refused;
        }

        if (MarkerId(principal) is not { } markerId)
        {
            return ProblemDetailsHelper.Forbidden(
                DenyCallerUnattributable, "Your identity could not be attributed, so no last-seen marker can be stored.");
        }

        try
        {
            await cache.SetAsync(tenantId, MarkerResource, markerId, MarkerVersion, new SeenMarker(now), MarkerTtl, ct: ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[EXT-NOTIFY] The last-seen marker for {MarkerId} could not be stored.", markerId);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable",
                detail: "The notifications could not be marked as seen. Try again.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorSeenUnavailable });
        }

        return Results.Ok(new ExternalNotificationSeenResponse { LastSeenAt = now });
    }

    /// <summary>
    /// The feed pipeline (see the file header): grants gated on the accessible set → the accessible records (own service
    /// requests through the module predicate) → the app-only recent-message read under the external rules, minus the
    /// caller's own → titles (an item whose record is gone is dropped) → newest first, at most <see cref="MaxItems"/> →
    /// unread against the marker. Separated from the route handler so it can be driven with doubles at the I/O seams.
    /// <para><paramref name="canReadTable"/> is the workforce ceiling (task 186): a table it refuses contributes no item and
    /// is not read — grants on its records are dropped, its records are not asked for messages, a refused
    /// <c>sprk_servicerequest</c> is not queried, and a refused <c>sprk_communication</c> skips the message read. Null
    /// (every caller the ceiling does not apply to) reads every table.</para>
    /// </summary>
    internal static async Task<IResult> GetFeedCoreAsync(
        CallerPrincipal principal,
        string tenantId,
        ExternalModuleRegistry registry,
        ExternalDataService dataService,
        CommunicationThreadReadService threadReadService,
        ITenantCache cache,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken ct,
        Func<string, bool>? canReadTable = null,
        ExternalInvoiceHiding? hideInvoices = null)
    {
        canReadTable ??= static _ => true;

        if (RefuseWhenContactUnreadable(principal, logger, "Feed") is { } refused)
        {
            return refused;
        }

        try
        {
            var cutoff = now - Window;
            var truncated = false;
            var items = new List<FeedItem>();

            // (a) Grants to the caller's contact — each root re-checked against the accessible set (fail-closed). Under a
            //     ceiling that can read none of the grant roots, the grant rows are not read at all.
            if (principal.ContactId != Guid.Empty
                && (canReadTable("sprk_project") || canReadTable("sprk_matter") || canReadTable("sprk_workassignment")))
            {
                // EventOn is the grant time, or the renewal time of a grant restored in place (owner 2026-10-12).
                var grants = await dataService.GetRecentGrantsAsync(
                        principal.ContactId, DateOnly.FromDateTime(cutoff.UtcDateTime), DateOnly.FromDateTime(now.UtcDateTime), ct,
                        modifiedSince: cutoff)
                    .ConfigureAwait(false);
                truncated |= grants.Truncated;

                var kept = grants.Grants
                    .Where(g => g.EventOn >= cutoff
                                && canReadTable(EntityFor(g.RootType))
                                && principal.RightsOn(g.RootType, g.RootId).HasFlag(Spaarke.Dataverse.AccessRights.Read))
                    .OrderByDescending(g => g.EventOn)
                    .ThenByDescending(g => g.GrantId)
                    .ToList();
                truncated |= kept.Count > MaxItems;
                items.AddRange(kept.Take(MaxItems).Select(g => new FeedItem(
                    ExternalNotificationDto.KindGrant, g.GrantId, EntityFor(g.RootType), g.RootId, g.EventOn, null)));
            }

            // (b) The accessible records whose messages the feed reports.
            var records = new List<(string EntityType, Guid RecordId)>();
            if (canReadTable("sprk_project"))
                records.AddRange(principal.GetAccessibleProjectIds().Select(id => ("sprk_project", id)));
            if (canReadTable("sprk_matter"))
                records.AddRange(principal.GetAccessibleMatterIds().Select(id => ("sprk_matter", id)));
            if (canReadTable("sprk_workassignment"))
                records.AddRange(principal.GetAccessibleWorkAssignmentIds().Select(id => ("sprk_workassignment", id)));

            var serviceRequestNames = new Dictionary<Guid, string?>();
            var serviceRequests = canReadTable(ServiceRequestEntity) ? registry.FindByEntity(ServiceRequestEntity) : null;
            if (serviceRequests is not null)
            {
                var requesterIds = serviceRequests.EffectiveDimensions
                    .SelectMany(d => d.AccessibleIds(principal))
                    .Where(id => id != Guid.Empty)
                    .ToHashSet();
                if (requesterIds.Count > 0)
                {
                    var own = await dataService.GetServiceRequestsRequestedByAsync(requesterIds, ct).ConfigureAwait(false);
                    truncated |= own.Truncated;

                    // The module's own predicate decides, as on the grid and the thread read — not the query.
                    var rows = own.Requests.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["@logicalName"] = ServiceRequestEntity,
                        ["sprk_requestedby"] = r.RequestedBy,
                        ["sprk_servicerequestid"] = r.Id,
                    }).ToList();
                    var admitted = serviceRequests.ScopeRows(principal, rows)
                        .Select(r => r.TryGetValue("sprk_servicerequestid", out var v) && v is Guid g ? g : Guid.Empty)
                        .Where(g => g != Guid.Empty)
                        .ToHashSet();
                    foreach (var request in own.Requests.Where(r => admitted.Contains(r.Id)))
                    {
                        records.Add((ServiceRequestEntity, request.Id));
                        serviceRequestNames[request.Id] = request.Name;
                    }
                }
            }

            // Messages: the external read rules (status, privilege, internal-only) are the read service's; the caller's
            // own messages are dropped by the creator stamp.
            // A ceiling without Read on sprk_communication reads no message at all.
            if (canReadTable(WorkforceRoleCeilingGate.CommunicationTable))
            {
                var recent = await threadReadService
                    .ReadRecentForRecordsAppOnlyAsync(
                        records, cutoff, principal.SystemUserId, MaxItems,
                        // A message also filed to a SECURE root the caller cannot read is not reported (ExternalMultiRootRule,
                        // owner decision 2026-10-12, option B); the secure flags are read once for the whole feed.
                        ExternalMessageEndpoints.SecureRootsFilter(
                            principal, dataService.GetNonSecureRootsAsync, logger, dataService.GetLiveParentRootsAsync,
                            authorizedParent: null,
                            // ebilling-r1 task 098: a message regarding an invoice (a') hides is not reported (fail closed).
                            hideInvoices: hideInvoices),
                        ct)
                    .ConfigureAwait(false);
                truncated |= recent.Truncated;
                foreach (var m in recent.Messages)
                {
                    if (ExternalMessageEndpoints.IsAuthoredBy(m.Message, principal) || m.Message.CreatedOn is not { } createdOn)
                    {
                        continue;
                    }

                    items.Add(new FeedItem(
                        ExternalNotificationDto.KindMessage, m.Message.MessageId, m.EntityType, m.RecordId, createdOn,
                        m.Message.SentByName));
                }
            }

            // Titles. A complete read that does not return a record means the record is gone: its item is dropped.
            var titles = new Dictionary<(string Entity, Guid Id), string?>();
            var gone = new HashSet<(string Entity, Guid Id)>();
            foreach (var group in items.Where(i => i.EntityType != ServiceRequestEntity).GroupBy(i => i.EntityType))
            {
                var ids = group.Select(i => i.RecordId).Distinct().ToList();
                var names = await dataService.GetRecordNamesAsync(group.Key, ids, ct).ConfigureAwait(false);
                // A title read that did not finish is reported, never silent (review K2): the items stay, untitled.
                truncated |= !names.Complete;
                foreach (var id in ids)
                {
                    if (names.Names.TryGetValue(id, out var name))
                        titles[(group.Key, id)] = name;
                    else if (names.Complete)
                        gone.Add((group.Key, id));
                }
            }

            foreach (var (id, name) in serviceRequestNames)
            {
                titles[(ServiceRequestEntity, id)] = name;
            }

            var ordered = items
                .Where(i => !gone.Contains((i.EntityType, i.RecordId)) && SegmentByEntity.ContainsKey(i.EntityType))
                .OrderByDescending(i => i.OccurredOn)
                .ThenBy(i => i.Kind, StringComparer.Ordinal)
                .ThenByDescending(i => i.Id)
                .ToList();
            truncated |= ordered.Count > MaxItems;

            var lastSeen = await ReadMarkerAsync(principal, tenantId, cache, logger, ct).ConfigureAwait(false);
            var value = ordered.Take(MaxItems).Select(i => new ExternalNotificationDto
            {
                Kind = i.Kind,
                Id = i.Id.ToString("D"),
                RecordType = SegmentByEntity[i.EntityType],
                RecordId = i.RecordId.ToString("D"),
                RecordTitle = titles.TryGetValue((i.EntityType, i.RecordId), out var title) ? title : null,
                OccurredOn = i.OccurredOn,
                SenderName = i.SenderName,
                IsUnread = lastSeen is null || i.OccurredOn > lastSeen.Value,
            }).ToList();

            return Results.Ok(new ExternalNotificationFeedResponse
            {
                Value = value,
                UnreadCount = value.Count(i => i.IsUnread),
                LastSeenAt = lastSeen,
                Truncated = truncated,
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-NOTIFY] The notification feed read failed for contact {ContactId} / systemuser {SystemUserId}.",
                principal.ContactId, principal.SystemUserId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "An unexpected error occurred reading the notifications.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorInternal });
        }
    }

    /// <summary>The caller's marker, or null when none is stored, the principal has no key, or the cache cannot be read.</summary>
    private static async Task<DateTimeOffset?> ReadMarkerAsync(
        CallerPrincipal principal, string tenantId, ITenantCache cache, ILogger logger, CancellationToken ct)
    {
        if (MarkerId(principal) is not { } markerId)
        {
            return null;
        }

        try
        {
            var marker = await cache.GetAsync<SeenMarker>(tenantId, MarkerResource, markerId, MarkerVersion, ct: ct)
                .ConfigureAwait(false);
            return marker?.SeenAt;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Losing the marker only re-marks the window as unread (notes/decisions.md 2026-10-10); the feed still answers.
            logger.LogWarning(ex, "[EXT-NOTIFY] The last-seen marker for {MarkerId} could not be read; every item is unread.",
                markerId);
            return null;
        }
    }

    private static string EntityFor(ExternalGrantRootType rootType) => rootType switch
    {
        ExternalGrantRootType.Project => "sprk_project",
        ExternalGrantRootType.Matter => "sprk_matter",
        ExternalGrantRootType.WorkAssignment => "sprk_workassignment",
        _ => throw new ArgumentOutOfRangeException(nameof(rootType), rootType, "Not a grant root."),
    };

    private sealed record FeedItem(
        string Kind, Guid Id, string EntityType, Guid RecordId, DateTimeOffset OccurredOn, string? SenderName);
}
