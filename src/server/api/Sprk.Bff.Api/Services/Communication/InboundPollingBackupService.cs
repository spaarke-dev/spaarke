using System.Collections.Concurrent;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Background service that polls receive-enabled mailboxes for new messages,
/// catching any emails missed by the Graph webhook subscription.
///
/// Implements ADR-001 BackgroundService pattern with PeriodicTimer (5-minute interval).
/// Follows the same pattern as EmailPollingBackupService (Jobs) and GraphSubscriptionManager.
///
/// Messages found by polling are logged for now; the actual processor (IncomingCommunicationJob
/// from Task 072) will consume them once implemented. Deduplication via sprk_graphmessageid
/// in sprk_communication prevents duplicate records.
/// </summary>
public sealed class InboundPollingBackupService : BackgroundService
{
    private static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromMinutes(5);

    private readonly CommunicationAccountService _accountService;
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<InboundPollingBackupService> _logger;
    private readonly TimeSpan _pollingInterval;

    /// <summary>
    /// Tracks the last successful poll time per account (keyed by account ID).
    /// In-memory dictionary is sufficient; on restart, the service will re-poll
    /// from 15 minutes ago as a safe lookback window.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastPollTimes = new();

    /// <summary>
    /// Default lookback window used when no previous poll time exists for an account
    /// (e.g., on first run or after service restart).
    /// </summary>
    private static readonly TimeSpan InitialLookbackWindow = TimeSpan.FromMinutes(15);

    public InboundPollingBackupService(
        CommunicationAccountService accountService,
        IGraphClientFactory graphClientFactory,
        ILogger<InboundPollingBackupService> logger)
    {
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollingInterval = DefaultPollingInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "InboundPollingBackupService starting with {Interval} minute interval",
            _pollingInterval.TotalMinutes);

        // Use PeriodicTimer for efficient periodic execution (ADR-001 pattern)
        using var timer = new PeriodicTimer(_pollingInterval);

        // Execute immediately on startup, then on interval
        await PollAllAccountsAsync(stoppingToken);

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
    /// </summary>
    private async Task PollAllAccountsAsync(CancellationToken ct)
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

        var totalFound = 0;
        var totalErrors = 0;

        foreach (var account in accounts)
        {
            try
            {
                var found = await PollAccountAsync(account, correlationId, ct);
                totalFound += found;
            }
            catch (Exception ex)
            {
                totalErrors++;
                _logger.LogError(ex,
                    "Failed to poll account {AccountId} ({Email}), correlation {CorrelationId}",
                    account.Id, account.EmailAddress, correlationId);
            }
        }

        _logger.LogInformation(
            "Inbound polling cycle complete: {AccountCount} accounts polled, " +
            "{MessageCount} messages found, {ErrorCount} errors, correlation {CorrelationId}",
            accounts.Length, totalFound, totalErrors, correlationId);
    }

    /// <summary>
    /// Polls a single account's monitor folder for messages received since the last poll time.
    /// Uses GraphClientFactory.ForApp() for app-only access.
    /// </summary>
    /// <returns>Number of unprocessed messages found.</returns>
    private async Task<int> PollAccountAsync(
        CommunicationAccount account, string correlationId, CancellationToken ct)
    {
        var lastPollTime = _lastPollTimes.GetOrAdd(
            account.Id,
            _ => DateTimeOffset.UtcNow.Subtract(InitialLookbackWindow));

        var monitorFolder = account.MonitorFolder ?? "Inbox";

        _logger.LogDebug(
            "Polling account {AccountId} ({Email}) folder '{Folder}' since {LastPollTime}, " +
            "correlation {CorrelationId}",
            account.Id, account.EmailAddress, monitorFolder, lastPollTime, correlationId);

        var graphClient = _graphClientFactory.ForApp();
        var pollStartTime = DateTimeOffset.UtcNow;
        var filterDateTime = lastPollTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Query Graph for messages received since last poll
        var messages = await graphClient.Users[account.EmailAddress]
            .MailFolders[monitorFolder]
            .Messages
            .GetAsync(config =>
            {
                config.QueryParameters.Filter = $"receivedDateTime ge {filterDateTime}";
                config.QueryParameters.Top = 50;
                config.QueryParameters.Select = new[] { "id", "receivedDateTime", "subject", "from", "isRead" };
                config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
            }, ct);

        var messageList = messages?.Value ?? [];

        if (messageList.Count == 0)
        {
            _logger.LogDebug(
                "No new messages for account {AccountId} ({Email}) since {LastPollTime}",
                account.Id, account.EmailAddress, lastPollTime);

            // Update last poll time even if nothing found
            _lastPollTimes[account.Id] = pollStartTime;
            return 0;
        }

        _logger.LogInformation(
            "Found {Count} messages for account {AccountId} ({Email}) since {LastPollTime}, " +
            "correlation {CorrelationId}",
            messageList.Count, account.Id, account.EmailAddress, lastPollTime, correlationId);

        // Log each message for now; actual processing is via IncomingCommunicationJob (Task 072)
        foreach (var message in messageList)
        {
            _logger.LogInformation(
                "Unprocessed message found: GraphMessageId={MessageId}, Subject='{Subject}', " +
                "ReceivedAt={ReceivedDateTime}, Account={AccountId} ({Email}), " +
                "correlation {CorrelationId}",
                message.Id,
                message.Subject,
                message.ReceivedDateTime,
                account.Id,
                account.EmailAddress,
                correlationId);
        }

        // Update last poll time to the start of this poll cycle
        _lastPollTimes[account.Id] = pollStartTime;

        return messageList.Count;
    }
}
