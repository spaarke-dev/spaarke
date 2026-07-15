using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Delta-query reconciliation backstop (FR-24 — the Microsoft-recommended "belt and suspenders"
/// paired with lifecycle notifications). Periodically runs a Graph mail-folder <c>messages/delta</c>
/// over every receive-enabled account and enqueues any newly-discovered message into the SAME
/// <c>IncomingCommunication</c> job path used by the webhook receiver and
/// <c>InboundPollingBackupService</c>. Idempotency key <c>Communication:{messageId}:Process</c> is
/// identical across all three sources, so the downstream Service Bus duplicate-detection +
/// <c>IncomingCommunicationProcessor</c> Dataverse <c>sprk_graphmessageid</c> check guarantee each
/// message is captured exactly once. This service therefore never touches
/// <c>IncomingCommunicationProcessor</c> directly.
///
/// Also invoked on-demand by <see cref="GraphSubscriptionManager"/> when a <c>missed</c> lifecycle
/// notification arrives (Graph could not deliver one or more change notifications).
///
/// The delta cursor (<c>@odata.deltaLink</c>) is persisted per account in the distributed cache so
/// that — unlike the coarse <c>receivedDateTime ge</c> time-window <c>InboundPollingBackupService</c>
/// — reconciliation uses Graph server-side change tracking and survives restarts.
///
/// ADR-001 BackgroundService + PeriodicTimer. All Graph access flows through
/// <see cref="GraphMailFolderDeltaReader"/> (ADR-028 central auth). Reconciliation is best-effort and
/// non-fatal (NFR-06): a failure never crashes the service or the inbound path.
/// </summary>
public class MailboxDeltaReconciliationService : BackgroundService
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    private const string DeltaCursorKeyPrefix = "comm:delta-cursor:";
    private static readonly TimeSpan DeltaCursorTtl = TimeSpan.FromDays(7);

    private readonly GraphMailFolderDeltaReader _deltaReader;
    private readonly CommunicationAccountService _accountService;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<MailboxDeltaReconciliationService> _logger;

    public MailboxDeltaReconciliationService(
        GraphMailFolderDeltaReader deltaReader,
        CommunicationAccountService accountService,
        JobSubmissionService jobSubmissionService,
        IDistributedCache cache,
        ILogger<MailboxDeltaReconciliationService> logger)
    {
        _deltaReader = deltaReader ?? throw new ArgumentNullException(nameof(deltaReader));
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MailboxDeltaReconciliationService starting with {Interval} minute interval",
            ReconcileInterval.TotalMinutes);

        // Brief delay to let dependencies warm up during app startup.
        await Task.Delay(StartupDelay, stoppingToken);

        using var timer = new PeriodicTimer(ReconcileInterval);

        // Execute immediately on startup, then on interval. A startup failure must not kill the service.
        try
        {
            await ReconcileAllAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial delta reconciliation cycle failed, will retry on next interval");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await ReconcileAllAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("MailboxDeltaReconciliationService stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during delta reconciliation cycle, will retry on next interval");
                // Continue running -- don't let one failure stop the service.
            }
        }

        _logger.LogInformation("MailboxDeltaReconciliationService stopped");
    }

    /// <summary>
    /// Reconciles every receive-enabled account. Best-effort: a per-account failure is logged and
    /// does not abort the cycle (NFR-06). Virtual to support unit-test verification of the
    /// <c>missed</c>-lifecycle trigger.
    /// </summary>
    public virtual async Task ReconcileAllAsync(CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid().ToString();

        CommunicationAccount[] accounts;
        try
        {
            accounts = await _accountService.QueryReceiveEnabledAccountsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to query receive-enabled accounts for delta reconciliation, correlation {CorrelationId}",
                correlationId);
            return;
        }

        if (accounts.Length == 0)
        {
            _logger.LogDebug("No receive-enabled accounts found, skipping delta reconciliation");
            return;
        }

        var totalReconciled = 0;
        foreach (var account in accounts)
        {
            try
            {
                totalReconciled += await ReconcileAccountAsync(account, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Delta reconciliation failed for account {AccountId} ({Email}) (non-fatal), correlation {CorrelationId}",
                    account.Id, account.EmailAddress, correlationId);
            }
        }

        _logger.LogInformation(
            "Delta reconciliation cycle complete: {AccountCount} accounts, {Reconciled} missed messages reconciled, " +
            "correlation {CorrelationId}",
            accounts.Length, totalReconciled, correlationId);
    }

    /// <summary>
    /// Reconciles a single account's monitor folder via delta query, enqueuing missed messages into
    /// the <c>IncomingCommunication</c> job path. Idempotent: dedupes within the delta batch and keys
    /// each job on <c>Communication:{messageId}:Process</c> so downstream dedup captures each message
    /// exactly once. Returns the number of missed messages enqueued. Virtual for unit-test overriding.
    /// </summary>
    public virtual async Task<int> ReconcileAccountAsync(CommunicationAccount account, CancellationToken ct = default)
    {
        var monitorFolder = string.IsNullOrWhiteSpace(account.MonitorFolder) ? "Inbox" : account.MonitorFolder!;
        var cursor = await TryGetCursorAsync(account.Id, ct);

        MailFolderDeltaResult result;
        try
        {
            result = await _deltaReader.QueryDeltaAsync(account.EmailAddress, monitorFolder, cursor, ct);
        }
        catch (Exception ex)
        {
            // A stale/invalid deltaLink (Graph 410 Gone) or transient failure is non-fatal (NFR-06);
            // drop the cursor so the next cycle performs a full delta.
            _logger.LogWarning(ex,
                "Delta query failed for account {AccountId} ({Email}); clearing cursor for full re-delta next cycle",
                account.Id, account.EmailAddress);
            await TryClearCursorAsync(account.Id, ct);
            return 0;
        }

        var enqueued = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var message in result.Messages)
        {
            if (message.IsRemoved || string.IsNullOrEmpty(message.MessageId))
                continue;

            // Dedupe within this delta batch so the same message id enqueues exactly one job.
            if (!seen.Add(message.MessageId))
                continue;

            try
            {
                await EnqueueReconciledMessageAsync(account, monitorFolder, message.MessageId, ct);
                enqueued++;
            }
            catch (Exception ex)
            {
                // Non-fatal (NFR-06): one message's enqueue failure must not abort the rest.
                _logger.LogWarning(ex,
                    "Failed to enqueue reconciled message {MessageId} for account {AccountId} ({Email}) (non-fatal)",
                    message.MessageId, account.Id, account.EmailAddress);
            }
        }

        // Persist the advanced cursor so the next cycle only observes newer changes.
        if (result.DeltaLink is not null)
            await TrySetCursorAsync(account.Id, result.DeltaLink, ct);

        if (enqueued > 0)
        {
            _logger.LogInformation(
                "Delta reconciliation enqueued {Count} missed messages for account {AccountId} ({Email})",
                enqueued, account.Id, account.EmailAddress);
        }

        return enqueued;
    }

    /// <summary>
    /// Enqueues a reconciled message into the shared <c>IncomingCommunication</c> job path with the
    /// canonical idempotency key so it is deduped against webhook + polling deliveries.
    /// </summary>
    private async Task EnqueueReconciledMessageAsync(
        CommunicationAccount account, string monitorFolder, string messageId, CancellationToken ct)
    {
        var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            Resource = $"users/{account.EmailAddress}/mailFolders/{monitorFolder}/messages/{messageId}",
            MessageId = messageId,
            SubscriptionId = account.SubscriptionId,
            TriggerSource = "DeltaReconciliation",
            AccountId = account.Id
        }));

        var job = new JobContract
        {
            JobType = "IncomingCommunication",
            SubjectId = messageId,
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = $"Communication:{messageId}:Process",
            Payload = jobPayload,
            MaxAttempts = 3
        };

        await _jobSubmissionService.SubmitCommunicationJobAsync(job, ct);
    }

    private async Task<string?> TryGetCursorAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            // SYSTEM-LEVEL EXCEPTION (NFR-08): the delta cursor is org-wide inbound-subscription state
            // (one set per org/BFF); per ADR-029 single Redis instance per BFF org, cache is system-level.
            return await _cache.GetStringAsync(DeltaCursorKeyPrefix + accountId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read delta cursor for account {AccountId}; performing full delta", accountId);
            return null;
        }
    }

    private async Task TrySetCursorAsync(Guid accountId, string deltaLink, CancellationToken ct)
    {
        try
        {
            await _cache.SetStringAsync(
                DeltaCursorKeyPrefix + accountId,
                deltaLink,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = DeltaCursorTtl },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist delta cursor for account {AccountId}; next cycle performs full delta", accountId);
        }
    }

    private async Task TryClearCursorAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            await _cache.RemoveAsync(DeltaCursorKeyPrefix + accountId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear delta cursor for account {AccountId}", accountId);
        }
    }
}
