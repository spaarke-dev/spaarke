using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Scheduled job (<see cref="IScheduledJob"/>, ADR-036) that polls receive-enabled mailboxes for new messages,
/// catching any emails missed by the Graph webhook subscription. Every 5 minutes (<see cref="DefaultCronSchedule"/>);
/// one run is one poll of every receive-enabled account (<see cref="PollAllAccountsAsync"/>).
///
/// Messages found by polling are enqueued as IncomingCommunication jobs via JobSubmissionService.
///
/// Deduplication, honestly stated: the Service Bus <c>IdempotencyKey</c> becomes the MessageId, but duplicate
/// detection is OFF on the BFF queues (ADR-004 §5, #980), so the key does NOT de-duplicate a re-listed message.
/// The real layers are: the <c>isRead eq false</c> filter (the processor marks a message read once handled), this
/// service's per-mailbox record of the ids it has already enqueued above the watermark, and the Dataverse
/// <c>sprk_graphmessageid</c> check in IncomingCommunicationProcessor.
///
/// Backlog handling (#2749): each poll lists oldest-first with keyset paging (see <see cref="PollAccountAsync"/>),
/// enqueueing at most <see cref="MaxMessagesPerPoll"/> per mailbox so one busy mailbox cannot starve the others. The
/// per-mailbox watermark then moves only as far as what was actually enqueued, so the next poll resumes with no gap, and
/// it is persisted in <see cref="IDistributedCache"/> so a restart or deploy resumes from it instead of from
/// now − 15 minutes.
/// </summary>
/// <remarks>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): migrated by #2749 (PR #2757) from a hand-rolled
/// <c>PeriodicTimer</c> <c>BackgroundService</c> — ADR-052 §1 migrates a timer service when a PR changes its behaviour,
/// and #2749 changed it (owner decision 2026-11-13, ADR-052 §6.5 Path C). Same work, same 5-minute interval, same
/// dependencies: replaced surface, not added. The interval was never configurable, so the cron is a constant. There is
/// no startup run any more: the first poll after a deploy is the next 5-minute boundary (at most 5 minutes later, from
/// the persisted watermark). Non-production slots run no scheduled jobs (ADR-036 A1 rule 2), so a staging slot no longer
/// polls the production mailboxes.</para>
/// <para><b>One dispatch per tick</b> (ADR-036 A1 rule 1): the host runs each tick on ONE instance under its distributed
/// lease. That removes the previous known limit — every scaled-out instance polled every mailbox on its own timer and
/// enqueued the same messages once per instance.</para>
/// <para><b>State across runs.</b> This class is the singleton <c>AddScheduledJob</c> registers, so the per-account
/// cursors (<see cref="_cursors"/>) outlive a run. Consecutive ticks can run on different instances, so cursors differ
/// between instances. The rule (see <see cref="GetCursorAsync"/>): an instance that holds an in-memory cursor for an
/// account uses it and IGNORES the persisted watermark; the persisted watermark is read only when the instance has no
/// cursor (its first run, or after a restart). The persisted value is not trusted over a live cursor because it can be a
/// guess: an instance with no cursor whose key was lost (TTL, a cache flush, a replaced Redis) starts from the default
/// lookback and writes now − 2 minutes, which is not a true "everything before this was enqueued" claim. An instance
/// holding a real backlog cursor that adopted it would jump past mail it had not yet listed. The cost is duplicates: when
/// another instance ran later ticks, this one re-lists from its own older cursor and enqueues mail already sent. That
/// can only point BACKWARD, never skip, and the processor's <c>sprk_graphmessageid</c> check drops the duplicates. Every
/// run writes its own cursor back, so the persisted value is the last writer's and a restart resumes from it. The
/// in-memory map also holds the enqueued-id record (the ids at or after the watermark this instance already enqueued).</para>
/// <para><b>Redis outage.</b> A Redis outage stops the backup poll, because the scheduler cannot take its lease. Polling
/// resumes from the saved position when Redis returns; the webhook and delta-reconciliation paths keep running.</para>
/// <para><b>Per-unit idempotency</b> (ADR-036 A1 rule 3; ADR-052). The unit is one Graph message. Its side effect —
/// a <c>sprk_communication</c> — is created by IncomingCommunicationProcessor: its <c>sprk_graphmessageid</c> check skips
/// a message already captured, and the create goes through <c>CreateCommunicationRaceProofAsync</c>, whose unique
/// alternate key on <c>sprk_internetmessageid</c> is the atomic claim; marking the message read is the completion
/// marker that takes it out of this poll's <c>isRead eq false</c> listing. A re-dispatched run (a host
/// retry, a manual trigger, a tick on another instance) can therefore at worst enqueue a message again: the watermark
/// only advances past what a run listed, the enqueued-id record skips what this instance already sent, and the
/// processor's check drops the duplicate. The poll itself never claims a unit — Service Bus duplicate detection is off
/// (ADR-004 §5, #980), so the enqueue is at-least-once by design.</para>
/// <para><b>Retry and heartbeat</b> (ADR-036 A1 rules 4 and 5): the run never throws for a poll failure — a failed
/// account query or a failed mailbox leaves its watermark where it was, so the next tick (5 minutes later) redoes it,
/// and a host retry would only add load; the run is recorded as failed instead (<c>Success=false</c> with a message).
/// It does not throw a Graph exception for a second reason: the host logs a thrown exception object, and a Graph error
/// message can quote the mailbox address (ADR-015). Cancellation propagates. One heartbeat line per attempt, including
/// a run with no accounts.</para>
/// </remarks>
public sealed class InboundPollingBackupService : IScheduledJob
{
    /// <summary>The scheduled job id (ADR-036).</summary>
    public const string JobIdConstant = "inbound-polling-backup";

    /// <summary>Every 5 minutes — the interval the hand-rolled timer kept (the host evaluates cron in UTC).</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Graph page size for the message listing.</summary>
    private const int PageSize = 50;

    /// <summary>
    /// Largest <c>$top</c> a widened page asks for (Graph's maximum for a <c>messages</c> listing). Only used when an
    /// entire page shares one second; see <see cref="PollAccountAsync"/>.
    /// </summary>
    internal const int MaxPageSize = 1000;

    private const string WatermarkKeyPrefix = "comm:inbound-poll-watermark:";
    private static readonly TimeSpan WatermarkTtl = TimeSpan.FromDays(7);

    /// <summary>
    /// Most messages enqueued for one mailbox in one poll. 500 = 10 full pages: a catch-up after an outage drains over
    /// several 5-minute cycles instead of one mailbox holding the whole cycle (and the Graph throttle budget).
    /// </summary>
    internal const int MaxMessagesPerPoll = 500;

    /// <summary>
    /// When a poll drains the mailbox, the watermark stops this far behind the poll start rather than at it, so a message
    /// that Exchange stamped just before the poll but made visible to Graph just after it is still listed next time. The
    /// ids already enqueued in that window are remembered and skipped, so the overlap does not re-enqueue them.
    /// </summary>
    internal static readonly TimeSpan PollOverlap = TimeSpan.FromMinutes(2);

    private readonly CommunicationAccountService _accountService;
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<InboundPollingBackupService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Per-account polling cursor (keyed by account ID), kept across runs on this singleton. A cursor, once created, is
    /// this instance's own claim and is never replaced by the persisted watermark (see <see cref="GetCursorAsync"/>); its
    /// watermark is written back after every poll. The enqueued-id record is in memory only (after a restart, a message
    /// sharing the persisted watermark's second may be enqueued once more, and the processor's
    /// <c>sprk_graphmessageid</c> check absorbs it).
    /// </summary>
    private readonly ConcurrentDictionary<Guid, MailboxCursor> _cursors = new();

    /// <summary>
    /// Lookback used when this instance has no cursor for an account and no watermark is persisted for it: first run, a
    /// persisted watermark older than its TTL or lost, or a cache that cannot be read.
    /// </summary>
    private static readonly TimeSpan InitialLookbackWindow = TimeSpan.FromMinutes(15);

    public InboundPollingBackupService(
        CommunicationAccountService accountService,
        IGraphClientFactory graphClientFactory,
        JobSubmissionService jobSubmissionService,
        IDistributedCache cache,
        ILogger<InboundPollingBackupService> logger,
        TimeProvider? timeProvider = null)
    {
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Inbound Mail Polling Backup";

    /// <inheritdoc />
    public string Description =>
        "Every 5 minutes, lists each receive-enabled mailbox's unread mail since its persisted watermark (oldest first, " +
        "keyset paging, at most " + MaxMessagesPerPoll + " per mailbox per run) and enqueues an IncomingCommunication job " +
        "for each message the Graph webhook may have missed (#2749).";

    /// <summary>What one run did.</summary>
    internal sealed record PollCounts(int Accounts, int Enqueued, int FailedAccounts, bool AccountQueryFailed);

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();

        // Never throws for a poll failure (see the class remarks); only cancellation propagates.
        var counts = await PollAllAccountsAsync(context.CorrelationId, cancellationToken);

        var duration = _timeProvider.GetElapsedTime(started);
        _logger.LogInformation(
            "[INBOUND-POLL] heartbeat attempt={Attempt} accounts={Accounts} enqueued={Enqueued} " +
            "failedAccounts={FailedAccounts} accountQueryFailed={AccountQueryFailed} durationMs={DurationMs} " +
            "correlation {CorrelationId}",
            context.Attempt, counts.Accounts, counts.Enqueued, counts.FailedAccounts, counts.AccountQueryFailed,
            (long)duration.TotalMilliseconds, context.CorrelationId);

        var success = !counts.AccountQueryFailed && counts.FailedAccounts == 0;
        return new JobRunResult(
            Success: success,
            ErrorMessage: success
                ? null
                : counts.AccountQueryFailed
                    ? "The receive-enabled accounts could not be queried; the next run retries."
                    : $"{counts.FailedAccounts} of {counts.Accounts} mailboxes failed to poll — see the errors naming each " +
                      "account; their watermarks did not move and the next run retries them.",
            ProcessedItems: counts.Enqueued,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(counts, ResultJsonOptions));
    }

    /// <summary>
    /// One poll: queries all receive-enabled accounts and polls each one for unprocessed messages. A failed account
    /// query or a failed mailbox is logged and counted, never thrown; cancellation propagates.
    /// </summary>
    private async Task<PollCounts> PollAllAccountsAsync(string correlationId, CancellationToken ct)
    {
        _logger.LogDebug(
            "Starting inbound polling cycle, correlation {CorrelationId}",
            correlationId);

        CommunicationAccount[] accounts;
        try
        {
            // Strict: a Dataverse fault throws (the lenient method answers an empty list, which would record an outage as a
            // successful run with no accounts); a cancellation propagates out of the filter below.
            accounts = await _accountService.QueryReceiveEnabledAccountsStrictAsync(ct);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            _logger.LogError(ex,
                "Failed to query receive-enabled accounts, correlation {CorrelationId}",
                correlationId);
            return new PollCounts(0, 0, 0, AccountQueryFailed: true);
        }

        if (accounts.Length == 0)
        {
            _logger.LogDebug("No receive-enabled accounts found, skipping inbound polling");
            return new PollCounts(0, 0, 0, AccountQueryFailed: false);
        }

        _logger.LogInformation(
            "Polling {Count} receive-enabled accounts for missed messages, correlation {CorrelationId}",
            accounts.Length, correlationId);

        var totalEnqueued = 0;
        var totalErrors = 0;

        foreach (var account in accounts)
        {
            try
            {
                var enqueued = await PollAccountAsync(account, correlationId, ct);
                totalEnqueued += enqueued;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                totalErrors++;
                // The exception object is not logged: a Graph error message can quote the mailbox address
                // (ErrorInvalidUser: "The requested user '...' is invalid"), and ADR-015 keeps addresses out of logs.
                _logger.LogError(
                    "Failed to poll account {AccountId} ({ExceptionType}, Graph error code {GraphErrorCode}), " +
                    "correlation {CorrelationId}",
                    account.Id, ex.GetType().Name, GraphErrorCode(ex), correlationId);
            }
        }

        _logger.LogInformation(
            "Inbound polling cycle complete: {AccountCount} accounts polled, " +
            "{MessageCount} messages enqueued, {ErrorCount} errors, correlation {CorrelationId}",
            accounts.Length, totalEnqueued, totalErrors, correlationId);

        return new PollCounts(accounts.Length, totalEnqueued, totalErrors, AccountQueryFailed: false);
    }

    /// <summary>
    /// Polls a single account's monitor folder for unread messages received since its watermark, oldest first, until the
    /// mailbox is drained or <see cref="MaxMessagesPerPoll"/> is reached. Uses GraphClientFactory.ForApp() for app-only
    /// access.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyset paging, not <c>@odata.nextLink</c>. Graph's nextLink for a filtered <c>messages</c> listing is a
    /// <c>$skip</c> offset that re-applies the filter. The processor marks each message read once handled
    /// (IncomingCommunicationProcessor Step 7), and it runs concurrently with this poll, so the <c>isRead eq false</c>
    /// result set shrinks between pages and the offset jumps over unseen mail — which the watermark then passes for good.
    /// Each page is instead a fresh query anchored on the last <c>receivedDateTime</c> seen, floored to the second
    /// (<c>receivedDateTime ge {anchor}</c>, <c>$orderby receivedDateTime asc</c>). A message that flips to read between
    /// pages simply drops out of the next query without shifting anything behind it, because position comes from a
    /// timestamp, not a count.
    /// </para>
    /// <para>
    /// That is also why <c>isRead eq false</c> stays in the filter: with keyset paging it can no longer cause a skip, and
    /// it keeps the listing to mail that still needs processing (already-processed mail is not re-listed every cycle).
    /// </para>
    /// <para>
    /// Because the anchor is inclusive (<c>ge</c>), messages in the anchor's second are listed again on the next page;
    /// ids already seen this poll, or already enqueued above the watermark, are skipped. A page with no unseen id is
    /// therefore necessarily a page whose every message shares the anchor's second (an unseen message later than the
    /// anchor would be unseen). When an entire page shares one second, moving the anchor cannot make progress, so the
    /// same query is re-issued with <c>$top</c> doubled (up to <see cref="MaxPageSize"/>) until the page reaches past that
    /// second; <c>$top</c> then drops back to <see cref="PageSize"/>. Widening was chosen over following nextLink for
    /// that second because a widened page is still a fresh query, so it cannot skip; nextLink would reintroduce the
    /// offset skip inside the second. If even <see cref="MaxPageSize"/> messages share one second, the poll stops there
    /// and keeps the watermark at that second, like a capped poll; as the processor marks them read they leave the
    /// listing, so later polls get past it. Escaping a stalled second therefore relies on the processor marking mail
    /// read; if it never does (for example a mailbox granted only Mail.Read) the poll stays on that second, and the
    /// webhook and delta-reconciliation paths are the backstop.
    /// </para>
    /// <para>
    /// The persisted watermark is read only when this instance has no cursor for the account (first run or after a
    /// restart), and is only trusted when the cache answers. A read that THROWS (as opposed to the key being absent)
    /// polls from now − 15 minutes for that cycle only, keeping no cursor for the account and writing no watermark, so a
    /// transient cache fault cannot overwrite an older backlog watermark with a later one; the next cycle reads the cache
    /// again. Mail enqueued during a cycle without a cursor is not remembered, so it may be enqueued once more later and
    /// the processor's <c>sprk_graphmessageid</c> check absorbs it. An instance that already has a cursor never reads the
    /// persisted value, so a cache read fault cannot affect it.
    /// </para>
    /// </remarks>
    /// <returns>Number of messages enqueued.</returns>
    private async Task<int> PollAccountAsync(
        CommunicationAccount account, string correlationId, CancellationToken ct)
    {
        var started = _timeProvider.GetTimestamp();
        var pollStartTime = _timeProvider.GetUtcNow();
        var (cursor, cacheReadFailed) = await GetCursorAsync(account.Id, pollStartTime, ct);
        var watermark = cursor.Watermark;

        var monitorFolder = account.MonitorFolder ?? "Inbox";

        _logger.LogDebug(
            "Polling account {AccountId} folder '{Folder}' since {Watermark}, correlation {CorrelationId}",
            account.Id, monitorFolder, watermark, correlationId);

        var graphClient = _graphClientFactory.ForApp();
        var messagesBuilder = graphClient.Users[account.EmailAddress].MailFolders[monitorFolder].Messages;

        var anchor = watermark;
        var top = PageSize;
        var seenThisPoll = new HashSet<string>(StringComparer.Ordinal);

        var pages = 0;
        var listed = 0;
        var enqueued = 0;
        var alreadyEnqueued = 0;
        var capped = false;
        var stalled = false;
        DateTimeOffset? lastEnqueuedReceived = null;

        while (true)
        {
            var filterDateTime = anchor.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            var pageTop = top;
            var page = await messagesBuilder.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"receivedDateTime ge {filterDateTime} and isRead eq false";
                config.QueryParameters.Top = pageTop;
                config.QueryParameters.Select = new[] { "id", "receivedDateTime", "isRead" };
                config.QueryParameters.Orderby = new[] { "receivedDateTime asc" };
            }, ct);
            pages++;

            var items = page?.Value ?? [];
            DateTimeOffset? lastReceivedOnPage = null;

            foreach (var message in items)
            {
                if (message.ReceivedDateTime is { } pageReceived)
                    lastReceivedOnPage = pageReceived;

                if (string.IsNullOrEmpty(message.Id) || !seenThisPoll.Add(message.Id))
                    continue;

                listed++;

                if (cursor.Enqueued.ContainsKey(message.Id))
                {
                    alreadyEnqueued++;
                    continue;
                }

                if (enqueued >= MaxMessagesPerPoll)
                {
                    capped = true;
                    break;
                }

                await EnqueueAsync(account, monitorFolder, message.Id, ct);

                // Recorded per message, not per poll: if a later page or enqueue throws, the retry skips what already went.
                cursor.Enqueued[message.Id] = message.ReceivedDateTime ?? pollStartTime;
                enqueued++;
                if (message.ReceivedDateTime is { } received)
                    lastEnqueuedReceived = received;
            }

            // No nextLink: this query returned everything that matched it, so the mailbox is drained.
            if (capped || string.IsNullOrEmpty(page?.OdataNextLink))
                break;

            var nextAnchor = lastReceivedOnPage is { } last ? MailboxCursor.Floor(last) : anchor;
            if (nextAnchor > anchor)
            {
                anchor = nextAnchor;
                top = PageSize;
            }
            else if (top < MaxPageSize)
            {
                // The whole page shares the anchor's second: widen the same query (see remarks).
                top = Math.Min(top * 2, MaxPageSize);
            }
            else
            {
                stalled = true;
                break;
            }
        }

        // Where the next poll starts. Capped (or stalled in one second): only up to the newest message enqueued, so
        // everything after it is still ahead of the watermark (messages sharing that second are re-listed and skipped by
        // id). Drained: the poll start minus the overlap. Never backwards. A poll that throws never gets here, so its
        // watermark stays put and the retry re-lists everything it had not reached.
        var next = capped || stalled
            ? lastEnqueuedReceived ?? watermark
            : pollStartTime - PollOverlap;
        cursor.Advance(next > watermark ? next : watermark);
        if (!cacheReadFailed)
            await TrySetWatermarkAsync(account.Id, cursor.Watermark, ct);

        var elapsed = _timeProvider.GetElapsedTime(started);

        if (capped || stalled)
        {
            _logger.LogWarning(
                "Inbound poll for account {AccountId} stopped early ({Reason}, cap {Cap}, max page {MaxPageSize}); " +
                "resuming from {Watermark} next cycle. Pages {Pages}, listed {Listed}, enqueued {Enqueued}, " +
                "already enqueued {AlreadyEnqueued}, elapsed {ElapsedMs} ms, correlation {CorrelationId}",
                account.Id, capped ? "per-poll cap reached" : "one second holds more than the max page",
                MaxMessagesPerPoll, MaxPageSize, cursor.Watermark,
                pages, listed, enqueued, alreadyEnqueued, elapsed.TotalMilliseconds, correlationId);
        }
        else
        {
            // Every 5 minutes per mailbox: Information only when the poll found something.
            _logger.Log(
                listed > 0 ? LogLevel.Information : LogLevel.Debug,
                "Inbound poll for account {AccountId} drained the mailbox: pages {Pages}, listed {Listed}, " +
                "enqueued {Enqueued}, already enqueued {AlreadyEnqueued}, watermark {Watermark}, " +
                "elapsed {ElapsedMs} ms, correlation {CorrelationId}",
                account.Id, pages, listed, enqueued, alreadyEnqueued, cursor.Watermark,
                elapsed.TotalMilliseconds, correlationId);
        }

        return enqueued;
    }

    /// <summary>
    /// This instance's cursor for the account. When the instance already has one it is returned as it is and the persisted
    /// watermark is neither read nor used: the persisted value may be another instance's guess (a lost key restarts from
    /// the default lookback), and adopting it could carry a real backlog cursor past mail not yet listed. The persisted
    /// value is used only when there is no in-memory cursor (first run, or after a restart): the cursor is then created
    /// from it, or from now − 15 minutes when there is none. A cache read that throws gives a transient default cursor
    /// that is not stored. See the class remarks for the rule and its cost (duplicates the processor absorbs).
    /// </summary>
    private async Task<(MailboxCursor Cursor, bool CacheReadFailed)> GetCursorAsync(
        Guid accountId, DateTimeOffset pollStartTime, CancellationToken ct)
    {
        if (_cursors.TryGetValue(accountId, out var existing))
            return (existing, false);

        var read = await TryGetWatermarkAsync(accountId, ct);

        if (read.Failed)
        {
            // A transient cursor is NOT stored: a cursor kept for the instance's life would make a transient cache fault
            // permanent, and the poll would then persist a later watermark over an older backlog one. The caller writes no
            // watermark either.
            return (new MailboxCursor(pollStartTime - InitialLookbackWindow), true);
        }

        var cursor = _cursors.GetOrAdd(
            accountId, _ => new MailboxCursor(read.Value ?? pollStartTime - InitialLookbackWindow));
        return (cursor, false);
    }

    /// <summary>
    /// Reads the persisted watermark. <c>Failed</c> is true only when the cache THREW; an absent key or an unparseable
    /// value is a normal "no watermark" (<c>Value</c> null, <c>Failed</c> false).
    /// </summary>
    private async Task<(DateTimeOffset? Value, bool Failed)> TryGetWatermarkAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            // SYSTEM-LEVEL EXCEPTION (NFR-08): the polling watermark is org-wide inbound-mailbox state (one per
            // receive-enabled account per org/BFF); per ADR-029 single Redis instance per BFF org, cache is system-level.
            var value = await _cache.GetStringAsync(WatermarkKeyPrefix + accountId, ct);
            if (value is null)
                return (null, false);

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return (parsed, false);

            _logger.LogWarning(
                "Persisted inbound poll watermark for account {AccountId} is unreadable; polling from the default lookback",
                accountId);
            return (null, false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex,
                "Failed to read inbound poll watermark for account {AccountId}; polling this cycle from the default " +
                "lookback without saving a watermark, will re-read the cache next cycle",
                accountId);
            return (null, true);
        }
    }

    private async Task TrySetWatermarkAsync(Guid accountId, DateTimeOffset watermark, CancellationToken ct)
    {
        try
        {
            await _cache.SetStringAsync(
                WatermarkKeyPrefix + accountId,
                watermark.ToString("O", CultureInfo.InvariantCulture),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = WatermarkTtl },
                ct);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex,
                "Failed to persist inbound poll watermark for account {AccountId}; a restart resumes from the older persisted watermark",
                accountId);
        }
    }

    /// <summary>
    /// The Graph error code (<c>ErrorInvalidUser</c>, <c>ErrorAccessDenied</c>, ...) of a failure, or "none". Logged in
    /// place of the exception, whose message may quote the mailbox address.
    /// </summary>
    private static string GraphErrorCode(Exception ex)
        => (ex as ODataError ?? ex.InnerException as ODataError)?.Error?.Code ?? "none";

    private async Task EnqueueAsync(
        CommunicationAccount account, string monitorFolder, string messageId, CancellationToken ct)
    {
        var correlationIdForJob = Guid.NewGuid().ToString();

        var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            Resource = $"users/{account.EmailAddress}/mailFolders/{monitorFolder}/messages/{messageId}",
            MessageId = messageId,
            TriggerSource = "BackupPolling",
            AccountId = account.Id
        }));

        var job = new JobContract
        {
            JobType = "IncomingCommunication",
            SubjectId = messageId,
            CorrelationId = correlationIdForJob,
            IdempotencyKey = $"Communication:{messageId}:Process",
            Payload = jobPayload,
            MaxAttempts = 3
        };

        await _jobSubmissionService.SubmitCommunicationJobAsync(job, ct);

        _logger.LogInformation(
            "Enqueued IncomingCommunicationJob to communication queue from polling | MessageId={MessageId}, " +
            "Account={AccountId}, correlation {CorrelationId}",
            messageId, account.Id, correlationIdForJob);
    }

    /// <summary>
    /// One mailbox's polling state: the watermark the next listing starts from, and the ids already enqueued whose
    /// <c>receivedDateTime</c> is at or after it (the only ones a <c>ge</c> listing can return again).
    /// </summary>
    private sealed class MailboxCursor(DateTimeOffset watermark)
    {
        public DateTimeOffset Watermark { get; private set; } = Floor(watermark);

        public Dictionary<string, DateTimeOffset> Enqueued { get; } = new(StringComparer.Ordinal);

        public void Advance(DateTimeOffset to)
        {
            Watermark = Floor(to);

            foreach (var id in Enqueued.Where(e => e.Value < Watermark).Select(e => e.Key).ToList())
                Enqueued.Remove(id);
        }

        // Graph stamps whole seconds and the $filter literal is whole seconds; a fractional watermark would let an id be
        // pruned while the truncated filter still lists its message.
        public static DateTimeOffset Floor(DateTimeOffset value)
            => new(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
