using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Background service that polls receive-enabled mailboxes for new messages,
/// catching any emails missed by the Graph webhook subscription.
///
/// A hand-rolled timer BackgroundService with PeriodicTimer (5-minute interval) — existing debt, ADR-052 §1.
/// Follows the same pattern as GraphSubscriptionManager.
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
public sealed class InboundPollingBackupService : BackgroundService
{
    private static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromMinutes(5);

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
    private readonly TimeSpan _pollingInterval;

    /// <summary>
    /// Per-account polling cursor (keyed by account ID). The watermark is loaded from the distributed cache the first
    /// time this instance polls an account and written back after every poll; the enqueued-id record is in memory only
    /// (after a restart, a message sharing the persisted watermark's second may be enqueued once more, and the
    /// processor's <c>sprk_graphmessageid</c> check absorbs it).
    /// </summary>
    private readonly ConcurrentDictionary<Guid, MailboxCursor> _cursors = new();

    /// <summary>
    /// Lookback used when no watermark exists for an account: first run, a persisted watermark older than its TTL, or a
    /// cache that cannot be read.
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
        _pollingInterval = DefaultPollingInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "InboundPollingBackupService starting with {Interval} minute interval",
            _pollingInterval.TotalMinutes);

        // Brief delay to let dependencies warm up during app startup
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        // Use PeriodicTimer for efficient periodic execution (ADR-001 pattern)
        using var timer = new PeriodicTimer(_pollingInterval);

        // Execute immediately on startup, then on interval
        // Wrapped in try-catch so a startup failure doesn't kill the service
        try
        {
            await PollAllAccountsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Initial inbound polling cycle failed, will retry on next interval");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await PollAllAccountsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("InboundPollingBackupService stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inbound polling cycle, will retry on next interval");
                // Continue running -- don't let one failure stop the service
            }
        }

        _logger.LogInformation("InboundPollingBackupService stopped");
    }

    /// <summary>
    /// Queries all receive-enabled accounts and polls each one for unprocessed messages.
    /// <c>internal</c> so a test drives one cycle without the startup delay or the timer.
    /// </summary>
    internal async Task PollAllAccountsAsync(CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString();

        _logger.LogDebug(
            "Starting inbound polling cycle, correlation {CorrelationId}",
            correlationId);

        CommunicationAccount[] accounts;
        try
        {
            accounts = await _accountService.QueryReceiveEnabledAccountsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to query receive-enabled accounts, correlation {CorrelationId}",
                correlationId);
            return;
        }

        if (accounts.Length == 0)
        {
            _logger.LogDebug("No receive-enabled accounts found, skipping inbound polling");
            return;
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
            catch (Exception ex)
            {
                totalErrors++;
                _logger.LogError(ex,
                    "Failed to poll account {AccountId}, correlation {CorrelationId}",
                    account.Id, correlationId);
            }
        }

        _logger.LogInformation(
            "Inbound polling cycle complete: {AccountCount} accounts polled, " +
            "{MessageCount} messages enqueued, {ErrorCount} errors, correlation {CorrelationId}",
            accounts.Length, totalEnqueued, totalErrors, correlationId);
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
    /// listing, so later polls get past it.
    /// </para>
    /// </remarks>
    /// <returns>Number of messages enqueued.</returns>
    private async Task<int> PollAccountAsync(
        CommunicationAccount account, string correlationId, CancellationToken ct)
    {
        var started = _timeProvider.GetTimestamp();
        var pollStartTime = _timeProvider.GetUtcNow();
        var cursor = await GetCursorAsync(account.Id, pollStartTime, ct);
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
    /// This instance's cursor for the account, created on first use from the persisted watermark (or now − 15 minutes
    /// when there is none or the cache cannot be read).
    /// </summary>
    private async Task<MailboxCursor> GetCursorAsync(Guid accountId, DateTimeOffset pollStartTime, CancellationToken ct)
    {
        if (_cursors.TryGetValue(accountId, out var existing))
            return existing;

        var persisted = await TryGetWatermarkAsync(accountId, ct);
        return _cursors.GetOrAdd(accountId, _ => new MailboxCursor(persisted ?? pollStartTime - InitialLookbackWindow));
    }

    private async Task<DateTimeOffset?> TryGetWatermarkAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            // SYSTEM-LEVEL EXCEPTION (NFR-08): the polling watermark is org-wide inbound-mailbox state (one per
            // receive-enabled account per org/BFF); per ADR-029 single Redis instance per BFF org, cache is system-level.
            var value = await _cache.GetStringAsync(WatermarkKeyPrefix + accountId, ct);
            if (value is null)
                return null;

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;

            _logger.LogWarning(
                "Persisted inbound poll watermark for account {AccountId} is unreadable; polling from the default lookback",
                accountId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read inbound poll watermark for account {AccountId}; polling from the default lookback",
                accountId);
            return null;
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist inbound poll watermark for account {AccountId}; a restart polls from the default lookback",
                accountId);
        }
    }

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
