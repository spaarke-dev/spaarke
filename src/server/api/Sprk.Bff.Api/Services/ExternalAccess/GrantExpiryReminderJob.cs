using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// Spec FR-33 (d), task 100 — reminds an internal user 30, 14, 7, 3 and 1 days before an external grant
/// (<c>sprk_externalrecordaccess.sprk_expiresdate</c>) lapses, so the person who can renew it hears first.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> Every external grant now expires (task 097). An expired grant fails silently —
/// the external user finds out when they try to work — so the reminder is the only warning anyone gets.</para>
///
/// <para><b>Who is reminded — never the grantee.</b> An external contact cannot renew their own access, so a
/// reminder to them is pressure with no action available (spec FR-33 (d)). The recipient is the first PERSON
/// in this chain (owner decision 2026-09-11): <c>sprk_grantedby</c> → the record's owning user → the record's
/// creator. A "person" is an enabled systemuser that is not an application user — every grant row's own
/// <c>createdby</c>/<c>ownerid</c> is the BFF's service identity, and most records are owned by a business-unit
/// team, which is why the chain exists at all (task 100 notes: 20 of 28 live grants had no
/// <c>sprk_grantedby</c>; 10 of 11 root records were team-owned). A grant with no person anywhere in the chain
/// is <b>unroutable</b>: it is counted in the heartbeat and logged at Error for each occurrence. It is never
/// silently dropped and never redirected to the grantee.</para>
///
/// <para><b>Which day.</b> A grant is reminded only on the exact days 30/14/7/3/1 before expiry, computed in
/// <see cref="DateOnly"/> on the UTC calendar that the read filter enforces expiry against. There is no
/// catch-up: a missed day stays missed, and a grant whose expiry is first set 20 days out gets its first
/// reminder at 14. The heartbeat is what makes a missed day visible.</para>
///
/// <para><b>At most once per (grant, expiry date, threshold)</b> — across re-runs, restarts and instances —
/// through <see cref="IIdempotencyService"/>. The expiry date is part of the key, so renewing a grant restarts
/// its reminders. Two limits of that service are accepted and documented in the task notes: it fails OPEN when
/// the cache is unreachable, and its lock is check-then-set rather than atomic, so two instances firing in the
/// same instant can each send once. Either way the result is a duplicate reminder, never a missing one.</para>
///
/// <para><b>Cost</b> (NFR-02): ONE FetchXML query per run returns every due grant together with everything
/// needed to route and word the reminder — no per-grant query. It pages only past <see cref="PageSize"/> grants
/// due on the same day.</para>
///
/// <para><b>Heartbeat</b> (spec FR-33 known debt): every run logs one structured line with its counts —
/// including a run with nothing due — and returns them in <see cref="JobRunResult.ResultJson"/>, which the
/// scheduler keeps as run history. "Nothing due" (status <c>ok</c>, due 0) and "the job died" (status
/// <c>error</c>, or no heartbeat that day) do not look alike.</para>
///
/// <para><b>Placement</b> (CLAUDE.md §10): in the BFF, on the existing in-process <c>Spaarke.Scheduling</c>
/// host — the same home as <c>MembershipReconciliationJob</c>. It reads the BFF-owned grant table and writes
/// through the existing <see cref="NotificationService"/> (Dataverse <c>appnotification</c>, the model-driven
/// app's bell). No new scheduler, channel, store or package. Registered unconditionally in
/// <c>ExternalAccessModule</c>; operators pause it with the scheduler's admin enable/disable, not a flag.</para>
/// </remarks>
public sealed class GrantExpiryReminderJob : IScheduledJob
{
    /// <summary>Stable job id — the scheduler's run history and admin endpoints key off it.</summary>
    public const string JobIdConstant = "external-grant-expiry-reminders";

    /// <summary>Daily at 06:00 UTC.</summary>
    internal const string DefaultCronSchedule = "0 6 * * *";

    /// <summary>The owner-confirmed reminder days (2026-09-10): exactly these, nothing in between.</summary>
    internal static readonly IReadOnlyList<int> ReminderDays = new[] { 30, 14, 7, 3, 1 };

    /// <summary>Rows per page of the single due-grants query.</summary>
    internal const int PageSize = 5000;

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    private const string NotificationCategory = "external-access";
    private const int PriorityInformational = 200000000;
    private const int PriorityWarning = 200000001;
    private const int PriorityCritical = 200000002;

    // Outlives the 30-day window, so a marker is still present on any later day its key could recur.
    private static readonly TimeSpan SentMarkerLifetime = TimeSpan.FromDays(35);
    private static readonly TimeSpan SendLockDuration = TimeSpan.FromMinutes(5);

    private const string GrantEntity = "sprk_externalrecordaccess";
    private const string GrantedByAlias = "gb";
    private const string ContactAlias = "ct";
    private const string OrganizationAlias = "og";

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The three grantable roots, in <see cref="ExternalGrantLifecycle.DeriveKey"/>'s order.</summary>
    private static readonly RootSpec[] Roots =
    {
        new("sprk_project", "sprk_projectid", "sprk_projectname", "pr", "project"),
        new("sprk_matter", "sprk_matterid", "sprk_mattername", "mt", "matter"),
        new("sprk_workassignment", "sprk_workassignmentid", "sprk_name", "wa", "work assignment"),
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GrantExpiryReminderJob> _logger;

    public GrantExpiryReminderJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<GrantExpiryReminderJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "External Grant Expiry Reminders";

    /// <inheritdoc />
    public string Description =>
        "Reminds the internal user who granted an external share (else the record's owner, else its creator) " +
        "30, 14, 7, 3 and 1 days before the share expires (spec FR-33). Never notifies the external grantee.";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var today = ExternalGrantLifecycle.TodayUtc(_timeProvider);
        var counts = new RunCounts();
        var status = StatusOk;
        string? error = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();

            var rows = await QueryDueGrantsAsync(entityService, today, cancellationToken).ConfigureAwait(false);
            counts.Due = rows.Count;

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RemindAsync(row, today, notifications, idempotency, counts, context.CorrelationId, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (counts.Failed > 0)
            {
                status = StatusPartial;
                error = $"{counts.Failed} reminder(s) could not be written; see the per-grant warnings.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            error = "Cancelled by host shutdown before every due grant was processed.";
        }
        catch (Exception ex)
        {
            status = StatusError;
            error = ex.Message;
            _logger.LogError(ex,
                "[GRANT-EXPIRY-REMINDER] Run failed before completing — due grants may not have been reminded. correlationId={CorrelationId}",
                context.CorrelationId);
        }

        var duration = _timeProvider.GetElapsedTime(started);

        // THE HEARTBEAT. Emitted on every run, whatever happened above — including a run with nothing due.
        _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "[GRANT-EXPIRY-REMINDER] heartbeat status={Status} today={Today} due={Due} sent={Sent} alreadySent={AlreadySent} " +
            "unroutable={Unroutable} failed={Failed} skipped={Skipped} durationMs={DurationMs} trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            status, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), counts.Due, counts.Sent, counts.AlreadySent,
            counts.Unroutable, counts.Failed, counts.Skipped, (long)duration.TotalMilliseconds, context.Trigger,
            context.RunId, context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: error,
            ProcessedItems: counts.Sent,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    counts.Due,
                    counts.Sent,
                    counts.AlreadySent,
                    counts.Unroutable,
                    counts.Failed,
                    counts.Skipped,
                    sentTo = new { granter = counts.SentToGranter, recordOwner = counts.SentToOwner, recordCreator = counts.SentToCreator },
                },
                ResultJsonOptions));
    }

    /// <summary>
    /// The run's ONE query: active grants whose expiry is exactly one reminder day away, joined to everything
    /// needed to route and word the reminder. Pages only past <see cref="PageSize"/> rows.
    /// </summary>
    private static async Task<List<Entity>> QueryDueGrantsAsync(
        IGenericEntityService entityService, DateOnly today, CancellationToken ct)
    {
        var rows = new List<Entity>();
        var page = 1;
        string? pagingCookie = null;

        while (true)
        {
            var result = await entityService
                .RetrieveMultipleAsync(new FetchExpression(BuildDueGrantsFetchXml(today, page, pagingCookie)), ct)
                .ConfigureAwait(false);

            rows.AddRange(result.Entities);

            if (!result.MoreRecords)
            {
                return rows;
            }

            page++;
            pagingCookie = result.PagingCookie;
        }
    }

    /// <summary>
    /// The due-grants FetchXML. Every join is OUTER: a grant with no granter, a team owner or no contact must
    /// still come back, or the fallback chain — and the unroutable count — would silently lose it.
    /// </summary>
    /// <remarks>
    /// Verified against live dev metadata and data on 2026-09-12 (task 100 notes): the column names, the
    /// <c>in</c> operator over bare <c>yyyy-MM-dd</c> values on this Date-Only column, and the aliased result
    /// names this class reads.
    /// </remarks>
    internal static string BuildDueGrantsFetchXml(DateOnly today, int page, string? pagingCookie)
    {
        var fetch = new XElement("fetch",
            new XAttribute("version", "1.0"),
            new XAttribute("mapping", "logical"),
            new XAttribute("no-lock", "true"),
            new XAttribute("count", PageSize),
            new XAttribute("page", page));

        if (pagingCookie is not null)
        {
            fetch.Add(new XAttribute("paging-cookie", pagingCookie));
        }

        var entity = new XElement("entity", new XAttribute("name", GrantEntity),
            Attributes("sprk_externalrecordaccessid", "sprk_expiresdate", "sprk_contact", "sprk_organization",
                "sprk_project", "sprk_matter", "sprk_workassignment", "sprk_grantedby"),
            new XElement("filter", new XAttribute("type", "and"),
                Condition("statecode", "eq", "0"),
                new XElement("condition",
                    new XAttribute("attribute", "sprk_expiresdate"),
                    new XAttribute("operator", "in"),
                    ReminderDays.Select(days => new XElement("value",
                        today.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))))),
            PersonLink("sprk_grantedby", GrantedByAlias),
            OuterLink("contact", "contactid", "sprk_contact", ContactAlias, Attributes("fullname")),
            OuterLink("sprk_organization", "sprk_organizationid", "sprk_organization", OrganizationAlias,
                Attributes("sprk_organizationname")));

        foreach (var root in Roots)
        {
            entity.Add(OuterLink(root.EntityName, root.IdAttribute, root.EntityName, root.Alias,
                Attributes(root.NameAttribute, "owninguser", "createdby"),
                PersonLink("owninguser", root.OwnerAlias),
                PersonLink("createdby", root.CreatorAlias)));
        }

        fetch.Add(entity);
        return fetch.ToString(SaveOptions.DisableFormatting);
    }

    private async Task RemindAsync(
        Entity row,
        DateOnly today,
        NotificationService notifications,
        IIdempotencyService idempotency,
        RunCounts counts,
        string correlationId,
        CancellationToken ct)
    {
        var grant = DueGrant.Read(row, today);

        if (grant is null)
        {
            counts.Skipped++;
            _logger.LogWarning(
                "[GRANT-EXPIRY-REMINDER] Skipped grant {GrantId}: no expiry on a reminder day or no root record, so it cannot be reminded. correlationId={CorrelationId}",
                row.Id, correlationId);
            return;
        }

        if (grant.Recipient is not { } recipient)
        {
            counts.Unroutable++;
            _logger.LogError(
                "[GRANT-EXPIRY-REMINDER] UNROUTABLE grant {GrantId} on {RootEntity} {RootId}: expires {ExpiresDate} ({DaysLeft} days) and nobody can be told — " +
                "sprk_grantedby, the record's owner and the record's creator are each empty or not an enabled person. correlationId={CorrelationId}",
                grant.Id, grant.Root.EntityName, grant.RootId, grant.ExpiresDateText, grant.DaysLeft, correlationId);
            return;
        }

        var key = $"grant-expiry-reminder:{grant.Id:N}:{grant.ExpiresDate:yyyyMMdd}:{grant.DaysLeft}";

        if (await idempotency.IsEventProcessedAsync(key, ct).ConfigureAwait(false)
            || !await idempotency.TryAcquireProcessingLockAsync(key, SendLockDuration, ct).ConfigureAwait(false))
        {
            counts.AlreadySent++;
            return;
        }

        try
        {
            await notifications.CreateNotificationAsync(
                userId: recipient.UserId,
                title: grant.DaysLeft == 1 ? "External access expires tomorrow" : $"External access expires in {grant.DaysLeft} days",
                body: $"{grant.GranteeText} will lose access to the {grant.Root.Label} \"{grant.RootName}\" on {grant.ExpiresDateText}. " +
                      $"To keep it, set a new expiration date in Manage Access on the {grant.Root.Label}.",
                category: NotificationCategory,
                priority: grant.DaysLeft <= 1 ? PriorityCritical : grant.DaysLeft <= 7 ? PriorityWarning : PriorityInformational,
                actionUrl: $"/main.aspx?etn={grant.Root.EntityName}&id={grant.RootId}&pagetype=entityrecord",
                regardingId: grant.RootId,
                cancellationToken: ct).ConfigureAwait(false);

            await idempotency.MarkEventAsProcessedAsync(key, SentMarkerLifetime, ct).ConfigureAwait(false);

            counts.Sent++;
            switch (recipient.Source)
            {
                case RecipientSource.Granter: counts.SentToGranter++; break;
                case RecipientSource.RecordOwner: counts.SentToOwner++; break;
                default: counts.SentToCreator++; break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            counts.Failed++;
            _logger.LogWarning(ex,
                "[GRANT-EXPIRY-REMINDER] Could not write the {DaysLeft}-day reminder for grant {GrantId} to user {UserId}; it will not be retried today. correlationId={CorrelationId}",
                grant.DaysLeft, grant.Id, recipient.UserId, correlationId);
        }
        finally
        {
            await idempotency.ReleaseProcessingLockAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static IEnumerable<XElement> Attributes(params string[] names)
        => names.Select(name => new XElement("attribute", new XAttribute("name", name)));

    private static XElement Condition(string attribute, string op, string value)
        => new("condition", new XAttribute("attribute", attribute), new XAttribute("operator", op), new XAttribute("value", value));

    private static XElement OuterLink(string entityName, string from, string to, string alias, params object[] content)
        => new("link-entity",
            new XAttribute("name", entityName),
            new XAttribute("from", from),
            new XAttribute("to", to),
            new XAttribute("link-type", "outer"),
            new XAttribute("alias", alias),
            content);

    /// <summary>Joins a systemuser lookup to the two columns that decide whether it is a person to remind.</summary>
    private static XElement PersonLink(string lookupAttribute, string alias)
        => OuterLink("systemuser", "systemuserid", lookupAttribute, alias, Attributes("isdisabled", "applicationid"));

    private sealed record RootSpec(string EntityName, string IdAttribute, string NameAttribute, string Alias, string Label)
    {
        public string OwnerAlias => Alias + "o";
        public string CreatorAlias => Alias + "c";
    }

    private enum RecipientSource
    {
        Granter,
        RecordOwner,
        RecordCreator,
    }

    private readonly record struct Recipient(Guid UserId, RecipientSource Source);

    /// <summary>One due grant, read from a row of the due-grants query.</summary>
    private sealed record DueGrant(
        Guid Id,
        DateOnly ExpiresDate,
        int DaysLeft,
        RootSpec Root,
        Guid RootId,
        string RootName,
        string GranteeText,
        Recipient? Recipient)
    {
        public string ExpiresDateText => ExpiresDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <returns><c>null</c> when the row has no expiry on a reminder day or no root record.</returns>
        public static DueGrant? Read(Entity row, DateOnly today)
        {
            if (row.GetAttributeValue<DateTime?>("sprk_expiresdate") is not { } expiresValue)
            {
                return null;
            }

            var expires = DateOnly.FromDateTime(expiresValue);
            var daysLeft = expires.DayNumber - today.DayNumber;
            if (!ReminderDays.Contains(daysLeft))
            {
                return null;
            }

            var root = Roots.FirstOrDefault(r => AsId(row.Contains(r.EntityName) ? row[r.EntityName] : null) is not null);
            if (root is null)
            {
                return null;
            }

            var rootId = AsId(row[root.EntityName])!.Value;
            var rootName = Aliased(row, root.Alias, root.NameAttribute) as string;

            var contactName = Aliased(row, ContactAlias, "fullname") as string;
            var organizationName = Aliased(row, OrganizationAlias, "sprk_organizationname") as string;
            var granteeText = !string.IsNullOrWhiteSpace(contactName) ? contactName!
                : !string.IsNullOrWhiteSpace(organizationName) ? $"Members of {organizationName}"
                : "An external user";

            // The owner decision's chain, in order. The grantee is not in it and cannot be: a contact is not a
            // systemuser, and only systemuser lookups are candidates.
            Recipient? recipient =
                Person(AsId(row.Contains("sprk_grantedby") ? row["sprk_grantedby"] : null), row, GrantedByAlias) is { } granter
                    ? new Recipient(granter, RecipientSource.Granter)
                : Person(AsId(Aliased(row, root.Alias, "owninguser")), row, root.OwnerAlias) is { } owner
                    ? new Recipient(owner, RecipientSource.RecordOwner)
                : Person(AsId(Aliased(row, root.Alias, "createdby")), row, root.CreatorAlias) is { } creator
                    ? new Recipient(creator, RecipientSource.RecordCreator)
                : null;

            return new DueGrant(
                row.Id,
                expires,
                daysLeft,
                root,
                rootId,
                string.IsNullOrWhiteSpace(rootName) ? "(unnamed)" : rootName!,
                granteeText,
                recipient);
        }

        /// <summary>
        /// A candidate counts only when its joined systemuser row positively says it is an enabled, non-application
        /// user. Absent join columns mean "not established", which falls through to the next candidate.
        /// </summary>
        private static Guid? Person(Guid? userId, Entity row, string alias)
            => userId is { } id
               && Aliased(row, alias, "isdisabled") is false
               && AsId(Aliased(row, alias, "applicationid")) is null
                ? id
                : null;

        private static object? Aliased(Entity row, string alias, string attribute)
            => row.Attributes.TryGetValue($"{alias}.{attribute}", out var value)
                ? value is AliasedValue aliased ? aliased.Value : value
                : null;

        private static Guid? AsId(object? value) => value switch
        {
            EntityReference reference when reference.Id != Guid.Empty => reference.Id,
            Guid id when id != Guid.Empty => id,
            _ => null,
        };
    }

    private sealed class RunCounts
    {
        public int Due;
        public int Sent;
        public int AlreadySent;
        public int Unroutable;
        public int Failed;
        public int Skipped;
        public int SentToGranter;
        public int SentToOwner;
        public int SentToCreator;
    }
}
