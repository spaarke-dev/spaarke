using System.Globalization;
using System.Runtime.ExceptionServices;
using System.ServiceModel;
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
/// creator. A "person" is an enabled, interactive systemuser: not disabled, no <c>applicationid</c>, and an
/// <c>accessmode</c> of Read-Write, Administrative or Read — never a Support or Delegated Admin user. Every grant
/// row's own <c>createdby</c>/<c>ownerid</c> is the BFF's service identity, and most records are owned by a
/// business-unit team, which is why the chain exists at all (task 100 notes: 20 of 28 live grants had no
/// <c>sprk_grantedby</c>; 10 of 11 root records were team-owned). A grant with no person anywhere in the chain
/// is <b>unroutable</b>: counted in every heartbeat and logged at Error once per threshold. It is never silently
/// dropped and never redirected to the grantee.</para>
///
/// <para><b>Which reminder (owner decision 2026-09-14: catch up).</b> Each run looks at every active grant that
/// expires within the next 30 days — access holds <i>through</i> the expiry date, so a grant expiring today is
/// still in. A grant's due threshold is the most urgent one whose day has come: the smallest of 30/14/7/3/1 that is
/// at least the days left. It is sent once per (grant, expiry date, threshold). So a missed day is caught up the
/// next day, several missed thresholds collapse into the most urgent one, and a missed 1-day reminder still goes
/// out on the expiry day itself. The expiry date is part of the key, so renewing a grant restarts its reminders.
/// Days are <see cref="DateOnly"/> on the UTC calendar the read filter enforces expiry against.</para>
///
/// <para><b>At most once</b> (ADR-036 A1 rule 3): "already sent?" → take a claim → check again under the claim →
/// send → write the completion marker → confirm it stuck → release. <see cref="IIdempotencyService"/>'s claim is
/// check-then-set and it fails open (#984), but this job runs on one instance at a time — the scheduler's lease
/// (ADR-036 A1 rule 1, task 103), apart from the documented overlap window after a Redis failover (A1 §5) — and the
/// second check closes the gap between the first check and the claim. The completion marker is written without the
/// run's cancellation token, so a reminder that went out is recorded even when the host is stopping; a marker that
/// did not stick is counted (<c>markFailed</c>), because the service swallows its own write failures.</para>
///
/// <para><b>Retry</b> (ADR-036 A1 rule 4): the attempt throws — after its heartbeat — only when a retry in this run
/// could send something no later run can: the query failed, or a reminder failed <i>transiently</i> on the grant's
/// last day (or its claim was held by a leftover from a failed release). A permanent rejection on the last day is
/// counted, not retried — another attempt would be refused the same way. The scheduler's retry policy re-runs the
/// attempt; reminders already sent are skipped by their markers. Any other failed reminder is counted and left to
/// tomorrow's run, which catches it up. A cancelled run never throws.</para>
///
/// <para><b>Cost</b> (NFR-02): ONE FetchXML query per run returns every grant in the window with everything
/// needed to route and word its reminder — no per-grant query. It is ordered by the grant's id for stable paging,
/// pages past <see cref="PageSize"/> rows and stops at <see cref="MaxPages"/> pages, reporting the run as
/// truncated. One unexpected failure on a row is counted and the run carries on with the rest.</para>
///
/// <para><b>Heartbeat</b> (spec FR-33 known debt; ADR-036 A1 rule 5): every attempt logs one structured line with
/// its counts and attempt number — including an attempt with nothing to send. An attempt that completes also
/// returns the counts in <see cref="JobRunResult.ResultJson"/> (an attempt that throws leaves only its heartbeat).
/// "Nothing to send" (status <c>ok</c>) and "the job died" (status <c>error</c>, or no heartbeat that day) do not
/// look alike.</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF, on the in-process <c>Spaarke.Scheduling</c> host,
/// registered with <c>AddScheduledJob</c> in <c>ExternalAccessModule</c>. Low volume, BFF identity and release
/// cadence, BFF domain code (ADR-052 B2/B3); the one Functions signal, one dispatch per schedule (F3), is met in
/// place by the scheduler's lease. Moving it would cost a deployable per stamp and extracting that domain code, for
/// one query a day. No new scheduler, channel, store or package.</para>
/// </remarks>
public sealed class GrantExpiryReminderJob : IScheduledJob
{
    /// <summary>Stable job id — the scheduler's run history and admin endpoints key off it.</summary>
    public const string JobIdConstant = "external-grant-expiry-reminders";

    /// <summary>Daily at 06:00 UTC.</summary>
    internal const string DefaultCronSchedule = "0 6 * * *";

    /// <summary>The owner-confirmed reminder thresholds (2026-09-10), most distant first.</summary>
    internal static readonly IReadOnlyList<int> ReminderDays = new[] { 30, 14, 7, 3, 1 };

    /// <summary>Rows per page of the single window query.</summary>
    internal const int PageSize = 5000;

    /// <summary>The paging loop's ceiling — 100,000 grants in one 30-day window. Past it the run reports truncated.</summary>
    internal const int MaxPages = 20;

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    private const string NotificationCategory = "external-access";
    private const int PriorityInformational = 200000000;
    private const int PriorityWarning = 200000001;
    private const int PriorityCritical = 200000002;

    // The interactive systemuser access modes: 0 Read-Write, 1 Administrative, 2 Read. Excludes 3 Support User,
    // 4 Non-interactive and 5 Delegated Admin — live dev has Support and Delegated Admin users (task 100 review).
    private const int LastPersonAccessMode = 2;

    // Dataverse service-protection (throttling) fault codes — a retry after the backoff can succeed.
    // https://learn.microsoft.com/power-apps/developer/data-platform/api-limits
    private static readonly HashSet<int> ThrottlingErrorCodes = new() { -2147015902, -2147015903, -2147015898 };

    // Outlives the 30-day window, so a marker is still present on any later day its key could recur.
    private static readonly TimeSpan SentMarkerLifetime = TimeSpan.FromDays(35);
    private static readonly TimeSpan SendLockDuration = TimeSpan.FromMinutes(5);

    private static readonly int WindowDays = ReminderDays.Max();

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
        "30, 14, 7, 3 and 1 days before the share expires, catching up a missed reminder (spec FR-33). " +
        "Never notifies the external grantee.";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var today = ExternalGrantLifecycle.TodayUtc(_timeProvider);
        var counts = new RunCounts();
        var status = StatusOk;
        var problems = new List<string>();
        ExceptionDispatchInfo? noProgress = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();

            var (rows, truncated) = await QueryWindowAsync(entityService, today, cancellationToken).ConfigureAwait(false);
            counts.InWindow = rows.Count;
            counts.Truncated = truncated;

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await RemindAsync(row, today, notifications, idempotency, counts, context.CorrelationId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // One bad row must not stop every other grant's reminder, day after day.
                    counts.Failed++;
                    _logger.LogWarning(ex,
                        "[GRANT-EXPIRY-REMINDER] Grant {GrantId} could not be processed; continuing with the rest. correlationId={CorrelationId}",
                        row.Id, context.CorrelationId);
                }
            }

            if (truncated)
            {
                problems.Add($"Stopped after {MaxPages} pages of {PageSize} grants; later grants in the window were not reminded.");
                _logger.LogError(
                    "[GRANT-EXPIRY-REMINDER] The window query still had more rows after {MaxPages} pages — stopped. correlationId={CorrelationId}",
                    MaxPages, context.CorrelationId);
            }

            if (counts.Failed > 0)
            {
                problems.Add($"{counts.Failed} reminder(s) could not be written; see the per-grant warnings.");
            }

            if (counts.ClaimHeld > 0)
            {
                problems.Add($"{counts.ClaimHeld} reminder(s) were skipped because a claim was still held.");
            }

            if (problems.Count > 0)
            {
                status = StatusPartial;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled before every grant in the window was processed.");
        }
        catch (Exception ex)
        {
            status = StatusError;
            problems.Add(ex.Message);
            noProgress = ExceptionDispatchInfo.Capture(ex);
            _logger.LogError(ex,
                "[GRANT-EXPIRY-REMINDER] Run failed before completing — grants in the window may not have been reminded. attempt={Attempt} correlationId={CorrelationId}",
                context.Attempt, context.CorrelationId);
        }

        var duration = _timeProvider.GetElapsedTime(started);

        // THE HEARTBEAT. Emitted on every attempt, whatever happened above — including one with nothing to send.
        _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "[GRANT-EXPIRY-REMINDER] heartbeat status={Status} today={Today} attempt={Attempt} inWindow={InWindow} sent={Sent} " +
            "alreadySent={AlreadySent} unroutable={Unroutable} failed={Failed} lastDayFailed={LastDayFailed} claimHeld={ClaimHeld} " +
            "markFailed={MarkFailed} skipped={Skipped} truncated={Truncated} durationMs={DurationMs} trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            status, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), context.Attempt, counts.InWindow, counts.Sent,
            counts.AlreadySent, counts.Unroutable, counts.Failed, counts.LastDayFailed, counts.ClaimHeld, counts.MarkFailed,
            counts.Skipped, counts.Truncated, (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);

        // ADR-036 A1 rule 4: throw only when a retry in this run could send something no later run can.
        noProgress?.Throw();
        if (counts.LastDayFailed > 0 && status != StatusCancelled)
        {
            throw new InvalidOperationException(
                $"{counts.LastDayFailed} reminder(s) for grants expiring today failed transiently, and no later run can send them — " +
                "failing the attempt so the scheduler retries it (ADR-036 A1 rule 4).");
        }

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: counts.Sent,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    attempt = context.Attempt,
                    counts.InWindow,
                    counts.Sent,
                    counts.AlreadySent,
                    counts.Unroutable,
                    counts.Failed,
                    counts.LastDayFailed,
                    counts.ClaimHeld,
                    counts.MarkFailed,
                    counts.Skipped,
                    counts.Truncated,
                    sentTo = new { granter = counts.SentToGranter, recordOwner = counts.SentToOwner, recordCreator = counts.SentToCreator },
                },
                ResultJsonOptions));
    }

    /// <summary>
    /// The run's ONE query: active grants expiring from today through today + 30, joined to everything needed to
    /// route and word the reminder. Pages past <see cref="PageSize"/> rows, up to <see cref="MaxPages"/>.
    /// </summary>
    private static async Task<(List<Entity> Rows, bool Truncated)> QueryWindowAsync(
        IGenericEntityService entityService, DateOnly today, CancellationToken ct)
    {
        var rows = new List<Entity>();
        string? pagingCookie = null;

        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await entityService
                .RetrieveMultipleAsync(new FetchExpression(BuildWindowFetchXml(today, page, pagingCookie)), ct)
                .ConfigureAwait(false);

            rows.AddRange(result.Entities);

            if (!result.MoreRecords)
            {
                return (rows, false);
            }

            pagingCookie = result.PagingCookie;
        }

        return (rows, true);
    }

    /// <summary>
    /// The window FetchXML. Every join is OUTER: a grant with no granter, a team owner or no contact must still come
    /// back, or the fallback chain — and the unroutable count — would silently lose it. Ordered by the grant's id so
    /// paging is stable.
    /// </summary>
    /// <remarks>
    /// Verified against live dev data: column names and aliased result names on 2026-09-12, and this shape — the
    /// <c>ge</c>/<c>le</c> date range and <c>accessmode</c> on every systemuser join — on 2026-09-15 (task 100 notes
    /// §6). The range uses the same comparison as the read filter (<c>ExternalParticipationService.ExpiryPredicate</c>).
    /// </remarks>
    internal static string BuildWindowFetchXml(DateOnly today, int page, string? pagingCookie)
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
            new XElement("order", new XAttribute("attribute", "sprk_externalrecordaccessid")),
            new XElement("filter", new XAttribute("type", "and"),
                Condition("statecode", "eq", "0"),
                Condition("sprk_expiresdate", "ge", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Condition("sprk_expiresdate", "le", today.AddDays(WindowDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
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
                "[GRANT-EXPIRY-REMINDER] Skipped grant {GrantId}: no expiry inside the window or no root record, so it cannot be reminded. correlationId={CorrelationId}",
                row.Id, correlationId);
            return;
        }

        // Invariant formatting: the key must not change with the server's culture, or every marker would be missed.
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"grant-expiry-reminder:{grant.Id:N}:{grant.ExpiresDate:yyyyMMdd}:{grant.Threshold}");

        if (grant.Recipient is not { } recipient)
        {
            counts.Unroutable++;
            // Counted on every run while the grant is in the window; logged at Error once per threshold, so an alert
            // on it fires when a reminder would have gone out, not every morning for 30 days.
            var reportKey = key + ":unroutable";
            if (!await idempotency.IsEventProcessedAsync(reportKey, ct).ConfigureAwait(false))
            {
                _logger.LogError(
                    "[GRANT-EXPIRY-REMINDER] UNROUTABLE grant {GrantId} on {RootEntity} {RootId}: expires {ExpiresDate} ({DaysLeft} days, {Threshold}-day reminder) and nobody can be told — " +
                    "sprk_grantedby, the record's owner and the record's creator are each empty or not an enabled person. correlationId={CorrelationId}",
                    grant.Id, grant.Root.EntityName, grant.RootId, grant.ExpiresDateText, grant.DaysLeft, grant.Threshold, correlationId);
                await idempotency.MarkEventAsProcessedAsync(reportKey, SentMarkerLifetime, CancellationToken.None).ConfigureAwait(false);
            }

            return;
        }

        if (await idempotency.IsEventProcessedAsync(key, ct).ConfigureAwait(false))
        {
            counts.AlreadySent++;
            return;
        }

        if (!await idempotency.TryAcquireProcessingLockAsync(key, SendLockDuration, ct).ConfigureAwait(false))
        {
            // Under the scheduler's lease no other run is sending it, so a held claim with no marker is a leftover from
            // a release that failed. It expires on its own; on the grant's last day the attempt is retried for it.
            counts.ClaimHeld++;
            if (grant.DaysLeft == 0)
            {
                counts.LastDayFailed++;
            }

            _logger.LogWarning(
                "[GRANT-EXPIRY-REMINDER] The {Threshold}-day reminder for grant {GrantId} was not sent: its claim is still held. correlationId={CorrelationId}",
                grant.Threshold, grant.Id, correlationId);
            return;
        }

        try
        {
            // Check again under the claim: a holder that sent and marked between the first check and the claim is
            // visible now (ADR-036 A1 rule 3 — the claim itself is check-then-set, #984).
            if (await idempotency.IsEventProcessedAsync(key, ct).ConfigureAwait(false))
            {
                counts.AlreadySent++;
                return;
            }

            try
            {
                await notifications.CreateNotificationAsync(
                    userId: recipient.UserId,
                    title: grant.DaysLeft switch
                    {
                        0 => "External access ends today",
                        1 => "External access ends tomorrow",
                        var days => $"External access ends in {days} days",
                    },
                    body: $"Access for {grant.GranteeText} to the {grant.Root.Label} \"{grant.RootName}\" ends after {grant.ExpiresDateText}. " +
                          $"To keep it, set a new expiration date in Manage Access on the {grant.Root.Label}.",
                    category: NotificationCategory,
                    priority: grant.DaysLeft <= 1 ? PriorityCritical : grant.DaysLeft <= 7 ? PriorityWarning : PriorityInformational,
                    actionUrl: $"/main.aspx?etn={grant.Root.EntityName}&id={grant.RootId}&pagetype=entityrecord",
                    regardingId: grant.RootId,
                    cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ct.IsCancellationRequested)
            {
                // Stopping. NotificationService wraps whatever the write threw, so our cancellation cannot be told
                // apart by exception type — it is OUR token that says so.
                throw new OperationCanceledException("Cancelled while writing a reminder.", ex, ct);
            }
            catch (Exception ex)
            {
                // Includes a timeout, which also surfaces as a cancellation — but not of our token.
                counts.Failed++;
                var retryable = grant.DaysLeft == 0 && IsTransient(ex);
                if (retryable)
                {
                    counts.LastDayFailed++;
                }

                _logger.LogWarning(ex,
                    "[GRANT-EXPIRY-REMINDER] Could not write the {Threshold}-day reminder for grant {GrantId} to user {UserId} ({DaysLeft} days left){Next}. correlationId={CorrelationId}",
                    grant.Threshold, grant.Id, recipient.UserId, grant.DaysLeft,
                    grant.DaysLeft > 0 ? "; tomorrow's run catches it up"
                        : retryable ? "; the attempt will be retried"
                        : "; the rejection is permanent, so it is not retried",
                    correlationId);
                return;
            }

            counts.Sent++;
            switch (recipient.Source)
            {
                case RecipientSource.Granter: counts.SentToGranter++; break;
                case RecipientSource.RecordOwner: counts.SentToOwner++; break;
                default: counts.SentToCreator++; break;
            }

            // The reminder went out: record it even if the host is stopping. IIdempotencyService swallows its own write
            // failures, so read the marker back — a marker that did not stick means a later run may send this again.
            bool marked;
            try
            {
                await idempotency.MarkEventAsProcessedAsync(key, SentMarkerLifetime, CancellationToken.None).ConfigureAwait(false);
                marked = await idempotency.IsEventProcessedAsync(key, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                marked = false;
                _logger.LogDebug(ex, "[GRANT-EXPIRY-REMINDER] Marker write for {Key} threw", key);
            }

            if (!marked)
            {
                counts.MarkFailed++;
                _logger.LogWarning(
                    "[GRANT-EXPIRY-REMINDER] Sent the {Threshold}-day reminder for grant {GrantId} but could not record it; a later run may repeat it. correlationId={CorrelationId}",
                    grant.Threshold, grant.Id, correlationId);
            }
        }
        finally
        {
            try
            {
                await idempotency.ReleaseProcessingLockAsync(key, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[GRANT-EXPIRY-REMINDER] Could not release the claim on {Key}; it expires on its own", key);
            }
        }
    }

    /// <summary>
    /// Whether another attempt in this run could plausibly succeed. A Dataverse fault that is not throttling is a
    /// rejection (privilege, validation) and is permanent; timeouts, network failures, throttling and anything
    /// unrecognised are treated as transient — a retry costs seconds, a lost last-day reminder costs the warning.
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case TimeoutException or TaskCanceledException or HttpRequestException or IOException:
                    return true;
                case FaultException<OrganizationServiceFault> fault:
                    return ThrottlingErrorCodes.Contains(fault.Detail?.ErrorCode ?? 0);
            }
        }

        return true;
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

    /// <summary>Joins a systemuser lookup to the columns that decide whether it is a person to remind.</summary>
    private static XElement PersonLink(string lookupAttribute, string alias)
        => OuterLink("systemuser", "systemuserid", lookupAttribute, alias, Attributes("isdisabled", "applicationid", "accessmode"));

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

    /// <summary>One grant in the window, read from a row of the window query.</summary>
    private sealed record DueGrant(
        Guid Id,
        DateOnly ExpiresDate,
        int DaysLeft,
        int Threshold,
        RootSpec Root,
        Guid RootId,
        string RootName,
        string GranteeText,
        Recipient? Recipient)
    {
        public string ExpiresDateText => ExpiresDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <returns><c>null</c> when the row has no expiry inside the window or no root record.</returns>
        public static DueGrant? Read(Entity row, DateOnly today)
        {
            if (row.GetAttributeValue<DateTime?>("sprk_expiresdate") is not { } expiresValue)
            {
                return null;
            }

            var expires = DateOnly.FromDateTime(expiresValue);
            var daysLeft = expires.DayNumber - today.DayNumber;
            if (daysLeft < 0 || daysLeft > WindowDays)
            {
                return null;
            }

            // The most urgent threshold whose day has come (catch-up, owner 2026-09-14).
            var threshold = ReminderDays.Where(days => days >= daysLeft).Min();

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
                : !string.IsNullOrWhiteSpace(organizationName) ? $"members of {organizationName}"
                : "an external user";

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
                threshold,
                root,
                rootId,
                string.IsNullOrWhiteSpace(rootName) ? "(unnamed)" : rootName!,
                granteeText,
                recipient);
        }

        /// <summary>
        /// A candidate counts only when its joined systemuser row positively says it is an enabled, interactive,
        /// non-application user. Absent join columns mean "not established", which falls through to the next one.
        /// </summary>
        private static Guid? Person(Guid? userId, Entity row, string alias)
            => userId is { } id
               && Aliased(row, alias, "isdisabled") is false
               && AsId(Aliased(row, alias, "applicationid")) is null
               && Aliased(row, alias, "accessmode") is OptionSetValue { Value: >= 0 and <= LastPersonAccessMode }
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
        public int InWindow;
        public int Sent;
        public int AlreadySent;
        public int Unroutable;
        public int Failed;
        public int LastDayFailed;
        public int ClaimHeld;
        public int MarkFailed;
        public int Skipped;
        public bool Truncated;
        public int SentToGranter;
        public int SentToOwner;
        public int SentToCreator;
    }
}
