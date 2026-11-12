using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
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
/// Backlog handling (#2749): each poll lists oldest-first and follows <c>@odata.nextLink</c>, enqueueing at most
/// <see cref="MaxMessagesPerPoll"/> per mailbox so one busy mailbox cannot starve the others. The per-mailbox watermark
/// then moves only as far as what was actually enqueued, so the next poll resumes with no gap.
/// </summary>
public sealed class InboundPollingBackupService : BackgroundService
{
    private static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromMinutes(5);

    /// <summary>Graph page size for the message listing.</summary>
    private const int PageSize = 50;

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
    private readonly ILogger<InboundPollingBackupService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _pollingInterval;

    /// <summary>
    /// Per-account polling cursor (keyed by account ID). In-memory is sufficient; on restart the service
    /// re-polls from 15 minutes ago as a safe lookback window.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, MailboxCursor> _cursors = new();

    /// <summary>
    /// Default lookback window used when no previous poll time exists for an account
    /// (e.g., on first run or after service restart).
    /// </summary>
    private static readonly TimeSpan InitialLookbackWindow = TimeSpan.FromMinutes(15);

    public InboundPollingBackupService(
        CommunicationAccountService accountService,
        IGraphClientFactory graphClientFactory,
        JobSubmissionService jobSubmissionService,
        ILogger<InboundPollingBackupService> logger,
        TimeProvider? timeProvider = null)
    {
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
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
    /// Polls a single account's monitor folder for unread messages received since its watermark, oldest first,
    /// following <c>@odata.nextLink</c> until the mailbox is drained or <see cref="MaxMessagesPerPoll"/> is reached.
    /// Uses GraphClientFactory.ForApp() for app-only access.
    /// </summary>
    /// <returns>Number of messages enqueued.</returns>
    private async Task<int> PollAccountAsync(
        CommunicationAccount account, string correlationId, CancellationToken ct)
    {
        var started = _timeProvider.GetTimestamp();
        var pollStartTime = _timeProvider.GetUtcNow();
        var cursor = _cursors.GetOrAdd(account.Id, _ => new MailboxCursor(pollStartTime - InitialLookbackWindow));
        var watermark = cursor.Watermark;

        var monitorFolder = account.MonitorFolder ?? "Inbox";

        _logger.LogDebug(
            "Polling account {AccountId} folder '{Folder}' since {Watermark}, correlation {CorrelationId}",
            account.Id, monitorFolder, watermark, correlationId);

        var graphClient = _graphClientFactory.ForApp();
        var filterDateTime = watermark.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var messagesBuilder = graphClient.Users[account.EmailAddress].MailFolders[monitorFolder].Messages;

        // Oldest first, so that a capped poll leaves only newer messages behind it and the watermark can follow the
        // last one enqueued. `ge` (not `gt`) keeps messages that share the watermark's second; `cursor.Enqueued` skips
        // the ones already sent.
        var page = await messagesBuilder.GetAsync(config =>
        {
            config.QueryParameters.Filter = $"receivedDateTime ge {filterDateTime} and isRead eq false";
            config.QueryParameters.Top = PageSize;
            config.QueryParameters.Select = new[] { "id", "receivedDateTime", "isRead" };
            config.QueryParameters.Orderby = new[] { "receivedDateTime asc" };
        }, ct);

        var pages = 0;
        var listed = 0;
        var enqueued = 0;
        var alreadyEnqueued = 0;
        var capped = false;
        DateTimeOffset? lastEnqueuedReceived = null;

        while (page is not null && !capped)
        {
            pages++;

            foreach (var message in page.Value ?? [])
            {
                if (string.IsNullOrEmpty(message.Id))
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

            if (capped || string.IsNullOrEmpty(page.OdataNextLink))
                break;

            page = await messagesBuilder.WithUrl(page.OdataNextLink).GetAsync(cancellationToken: ct);
        }

        // Where the next poll starts. Capped: only up to the newest message enqueued, so everything after it is still
        // ahead of the watermark (messages sharing that second are re-listed and skipped by id). Drained: the poll
        // start minus the overlap. Never backwards.
        var next = capped
            ? lastEnqueuedReceived ?? watermark
            : pollStartTime - PollOverlap;
        cursor.Advance(next > watermark ? next : watermark);

        var elapsed = _timeProvider.GetElapsedTime(started);

        if (capped)
        {
            _logger.LogWarning(
                "Inbound poll for account {AccountId} hit the per-poll cap of {Cap}; resuming from {Watermark} next cycle. " +
                "Pages {Pages}, listed {Listed}, enqueued {Enqueued}, already enqueued {AlreadyEnqueued}, " +
                "elapsed {ElapsedMs} ms, correlation {CorrelationId}",
                account.Id, MaxMessagesPerPoll, cursor.Watermark,
                pages, listed, enqueued, alreadyEnqueued, elapsed.TotalMilliseconds, correlationId);
        }
        else
        {
            _logger.LogInformation(
                "Inbound poll for account {AccountId} drained the mailbox: pages {Pages}, listed {Listed}, " +
                "enqueued {Enqueued}, already enqueued {AlreadyEnqueued}, watermark {Watermark}, " +
                "elapsed {ElapsedMs} ms, correlation {CorrelationId}",
                account.Id, pages, listed, enqueued, alreadyEnqueued, cursor.Watermark,
                elapsed.TotalMilliseconds, correlationId);
        }

        return enqueued;
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
        private static DateTimeOffset Floor(DateTimeOffset value)
            => new(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
