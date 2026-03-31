using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services;

/// <summary>
/// Background service that periodically executes notification-mode playbooks for all active users.
/// Queries <c>sprk_analysisplaybook</c> where <c>sprk_playbooktype = Notification (2)</c>,
/// reads schedule config from <c>sprk_configjson</c>, and processes users in parallel.
/// </summary>
/// <remarks>
/// <para>
/// Implements ADR-001: Uses BackgroundService, not Azure Functions.
/// Implements ADR-010: Registered as hosted service via feature module.
/// </para>
/// <para>
/// Key behaviors:
/// </para>
/// <list type="bullet">
/// <item>Queries notification playbooks on each tick (default: 1 hour)</item>
/// <item>Reads schedule config from <c>sprk_configjson</c> on each playbook</item>
/// <item>Checks last-run timestamp vs. schedule to determine if playbook is due</item>
/// <item>Processes users in parallel with <c>Parallel.ForEachAsync</c> (MaxDegreeOfParallelism = 5)</item>
/// <item>Persists last-run timestamps in-memory (ConcurrentDictionary) with initial seed from Dataverse</item>
/// <item>Opt-out model: all playbooks run for all users by default</item>
/// </list>
/// </remarks>
public sealed class PlaybookSchedulerService : BackgroundService
{
    /// <summary>
    /// Default interval between scheduler ticks (1 hour).
    /// </summary>
    private static readonly TimeSpan DefaultTickInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Maximum number of users processed in parallel per playbook.
    /// </summary>
    private const int MaxDegreeOfParallelism = 5;

    /// <summary>
    /// Notification playbook type value in <c>sprk_playbooktype</c> OptionSet.
    /// </summary>
    private const int NotificationPlaybookType = 2;

    /// <summary>
    /// Delay before retrying after an unhandled error to avoid tight error loops.
    /// </summary>
    private static readonly TimeSpan ErrorRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Last-run timestamps keyed by playbook ID. Persisted in-memory; survives
    /// App Service restarts by re-reading <c>sprk_lastrundate</c> from Dataverse on startup.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastRunTimestamps = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlaybookSchedulerService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PlaybookSchedulerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PlaybookSchedulerService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PlaybookSchedulerService started — tick interval: {Interval}, max parallelism: {MaxParallelism}",
            GetTickInterval(), MaxDegreeOfParallelism);

        // Seed last-run timestamps from Dataverse on first startup
        await SeedLastRunTimestampsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteSchedulerTickAsync(stoppingToken);

                var interval = GetTickInterval();
                _logger.LogDebug("PlaybookSchedulerService sleeping for {Interval}", interval);
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("PlaybookSchedulerService stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PlaybookSchedulerService tick failed, retrying in {Delay}", ErrorRetryDelay);

                try
                {
                    await Task.Delay(ErrorRetryDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("PlaybookSchedulerService stopped");
    }

    /// <summary>
    /// Executes a single scheduler tick: query notification playbooks, check schedules, execute due playbooks.
    /// </summary>
    private async Task ExecuteSchedulerTickAsync(CancellationToken ct)
    {
        _logger.LogInformation("PlaybookSchedulerService tick started at {Time:u}", DateTimeOffset.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var entityService = scope.ServiceProvider.GetRequiredService<Spaarke.Dataverse.IGenericEntityService>();

        // Step 5: Query notification playbooks (sprk_playbooktype = 2)
        var playbooks = await QueryNotificationPlaybooksAsync(entityService, ct);

        if (playbooks.Count == 0)
        {
            _logger.LogDebug("No notification playbooks found");
            return;
        }

        _logger.LogInformation("Found {Count} notification playbooks", playbooks.Count);

        foreach (var playbook in playbooks)
        {
            var playbookId = playbook.Id;
            var playbookName = playbook.GetAttributeValue<string>("sprk_name") ?? "(unnamed)";

            try
            {
                // Step 6: Read schedule config from sprk_configjson
                var scheduleConfig = ParseScheduleConfig(playbook);

                // Step 7: Check if playbook is due based on last-run timestamp
                if (!IsPlaybookDue(playbookId, scheduleConfig))
                {
                    _logger.LogDebug(
                        "Playbook {PlaybookId} ({Name}) not due — last run: {LastRun}, frequency: {Frequency}",
                        playbookId, playbookName,
                        _lastRunTimestamps.TryGetValue(playbookId, out var lr) ? lr.ToString("u") : "never",
                        scheduleConfig.Frequency);
                    continue;
                }

                _logger.LogInformation(
                    "Executing notification playbook {PlaybookId} ({Name}) — frequency: {Frequency}",
                    playbookId, playbookName, scheduleConfig.Frequency);

                // Step 8-9: Query active users and process in parallel
                await ProcessPlaybookForAllUsersAsync(scope.ServiceProvider, playbookId, ct);

                // Step 10: Persist last-run timestamp
                var now = DateTimeOffset.UtcNow;
                _lastRunTimestamps[playbookId] = now;

                await PersistLastRunTimestampAsync(entityService, playbookId, now, ct);

                _logger.LogInformation(
                    "Completed notification playbook {PlaybookId} ({Name}) at {Time:u}",
                    playbookId, playbookName, now);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to execute notification playbook {PlaybookId} ({Name}) — continuing with next playbook",
                    playbookId, playbookName);
                // Continue processing remaining playbooks even if one fails
            }
        }

        _logger.LogInformation("PlaybookSchedulerService tick completed at {Time:u}", DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Queries <c>sprk_analysisplaybook</c> where <c>sprk_playbooktype = Notification (2)</c>
    /// and <c>statecode = Active (0)</c>.
    /// </summary>
    private static async Task<List<Entity>> QueryNotificationPlaybooksAsync(
        Spaarke.Dataverse.IGenericEntityService entityService,
        CancellationToken ct)
    {
        var query = new QueryExpression("sprk_analysisplaybook")
        {
            ColumnSet = new ColumnSet(
                "sprk_analysisplaybookid",
                "sprk_name",
                "sprk_configjson",
                "sprk_lastrundate"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("sprk_playbooktype", ConditionOperator.Equal, NotificationPlaybookType),
                    new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active
                }
            }
        };

        var result = await entityService.RetrieveMultipleAsync(query, ct);
        return result.Entities.ToList();
    }

    /// <summary>
    /// Queries active <c>systemuser</c> records (non-disabled, non-integration).
    /// </summary>
    private static async Task<List<Entity>> QueryActiveUsersAsync(
        Spaarke.Dataverse.IGenericEntityService entityService,
        CancellationToken ct)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "fullname"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false),
                    new ConditionExpression("accessmode", ConditionOperator.NotEqual, 4) // Exclude non-interactive (4)
                }
            }
        };

        var result = await entityService.RetrieveMultipleAsync(query, ct);
        return result.Entities.ToList();
    }

    /// <summary>
    /// Processes a single playbook for all active users in parallel (MaxDegreeOfParallelism = 5).
    /// </summary>
    private async Task ProcessPlaybookForAllUsersAsync(
        IServiceProvider serviceProvider,
        Guid playbookId,
        CancellationToken ct)
    {
        var entityService = serviceProvider.GetRequiredService<Spaarke.Dataverse.IGenericEntityService>();
        var users = await QueryActiveUsersAsync(entityService, ct);

        _logger.LogInformation(
            "Processing playbook {PlaybookId} for {UserCount} active users (max parallelism: {MaxParallelism})",
            playbookId, users.Count, MaxDegreeOfParallelism);

        var tenantId = _configuration["TENANT_ID"] ?? "default";
        var successCount = 0;
        var failureCount = 0;

        await Parallel.ForEachAsync(
            users,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = ct
            },
            async (user, userCt) =>
            {
                var userId = user.Id;
                var userName = user.GetAttributeValue<string>("fullname") ?? "(unknown)";

                try
                {
                    // Create a scoped service provider for each user execution
                    using var userScope = _scopeFactory.CreateScope();
                    var orchestrationService = userScope.ServiceProvider.GetRequiredService<IPlaybookOrchestrationService>();

                    // Build PlaybookRunRequest (no documents — notification playbooks query Dataverse directly)
                    var request = new PlaybookRunRequest
                    {
                        PlaybookId = playbookId,
                        DocumentIds = [],
                        UserContext = $"Scheduled notification run for user {userName}",
                        Parameters = new Dictionary<string, string>
                        {
                            ["userId"] = userId.ToString(),
                            ["userName"] = userName
                        }
                    };

                    // Execute the playbook in app-only mode (background, no HttpContext)
                    await foreach (var evt in orchestrationService.ExecuteAppOnlyAsync(request, tenantId, userCt))
                    {
                        // Drain the async enumerable — events are processed by the orchestration service
                        if (evt.Type == PlaybookEventType.RunFailed)
                        {
                            _logger.LogWarning(
                                "Playbook {PlaybookId} failed for user {UserId} ({UserName}): {Error}",
                                playbookId, userId, userName, evt.Error);
                            Interlocked.Increment(ref failureCount);
                            return;
                        }
                    }

                    Interlocked.Increment(ref successCount);

                    _logger.LogDebug(
                        "Playbook {PlaybookId} completed for user {UserId} ({UserName})",
                        playbookId, userId, userName);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failureCount);
                    _logger.LogWarning(
                        ex,
                        "Playbook {PlaybookId} failed for user {UserId} ({UserName})",
                        playbookId, userId, userName);
                }
            });

        _logger.LogInformation(
            "Playbook {PlaybookId} completed — success: {SuccessCount}, failures: {FailureCount}, total: {TotalCount}",
            playbookId, successCount, failureCount, users.Count);
    }

    /// <summary>
    /// Parses the schedule configuration from the playbook's <c>sprk_configjson</c> field.
    /// </summary>
    /// <remarks>
    /// Expected format: <c>{ "schedule": { "frequency": "daily", "time": "06:00" } }</c>
    /// Defaults to daily at 06:00 UTC if not specified.
    /// </remarks>
    private ScheduleConfig ParseScheduleConfig(Entity playbook)
    {
        var configJson = playbook.GetAttributeValue<string>("sprk_configjson");

        if (string.IsNullOrWhiteSpace(configJson))
        {
            return ScheduleConfig.Default;
        }

        try
        {
            using var doc = JsonDocument.Parse(configJson);

            if (doc.RootElement.TryGetProperty("schedule", out var scheduleElement))
            {
                var frequency = scheduleElement.TryGetProperty("frequency", out var freqProp)
                    ? freqProp.GetString() ?? "daily"
                    : "daily";

                var time = scheduleElement.TryGetProperty("time", out var timeProp)
                    ? timeProp.GetString() ?? "06:00"
                    : "06:00";

                return new ScheduleConfig(frequency, time);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse schedule config for playbook {PlaybookId} — using defaults",
                playbook.Id);
        }

        return ScheduleConfig.Default;
    }

    /// <summary>
    /// Determines if a playbook is due for execution based on last-run timestamp and schedule config.
    /// </summary>
    private bool IsPlaybookDue(Guid playbookId, ScheduleConfig schedule)
    {
        if (!_lastRunTimestamps.TryGetValue(playbookId, out var lastRun))
        {
            // Never run before — due immediately
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsed = now - lastRun;

        return schedule.Frequency.ToLowerInvariant() switch
        {
            "hourly" => elapsed >= TimeSpan.FromHours(1),
            "daily" => elapsed >= TimeSpan.FromHours(24),
            "weekly" => elapsed >= TimeSpan.FromDays(7),
            _ => elapsed >= TimeSpan.FromHours(24) // Default to daily
        };
    }

    /// <summary>
    /// Seeds the in-memory last-run timestamps from Dataverse <c>sprk_lastrundate</c> field.
    /// Called once at service startup to survive App Service restarts.
    /// </summary>
    private async Task SeedLastRunTimestampsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var entityService = scope.ServiceProvider.GetRequiredService<Spaarke.Dataverse.IGenericEntityService>();

            var playbooks = await QueryNotificationPlaybooksAsync(entityService, ct);

            foreach (var playbook in playbooks)
            {
                var lastRunDate = playbook.GetAttributeValue<DateTime?>("sprk_lastrundate");
                if (lastRunDate.HasValue)
                {
                    _lastRunTimestamps[playbook.Id] = new DateTimeOffset(lastRunDate.Value, TimeSpan.Zero);
                }
            }

            _logger.LogInformation(
                "Seeded {Count} last-run timestamps from Dataverse",
                _lastRunTimestamps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to seed last-run timestamps from Dataverse — will treat all playbooks as due");
        }
    }

    /// <summary>
    /// Persists the last-run timestamp back to Dataverse <c>sprk_lastrundate</c> field.
    /// </summary>
    private async Task PersistLastRunTimestampAsync(
        Spaarke.Dataverse.IGenericEntityService entityService,
        Guid playbookId,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        try
        {
            await entityService.UpdateAsync(
                "sprk_analysisplaybook",
                playbookId,
                new Dictionary<string, object>
                {
                    ["sprk_lastrundate"] = timestamp.UtcDateTime
                },
                ct);

            _logger.LogDebug(
                "Persisted last-run timestamp for playbook {PlaybookId}: {Timestamp:u}",
                playbookId, timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist last-run timestamp for playbook {PlaybookId} — in-memory value retained",
                playbookId);
            // Non-fatal: in-memory timestamp is still correct, will re-persist on next tick
        }
    }

    /// <summary>
    /// Gets the configurable tick interval from configuration or uses the default (1 hour).
    /// </summary>
    private TimeSpan GetTickInterval()
    {
        var intervalMinutes = _configuration.GetValue<int>("Notifications:SchedulerIntervalMinutes", 0);
        return intervalMinutes > 0
            ? TimeSpan.FromMinutes(intervalMinutes)
            : DefaultTickInterval;
    }

    /// <summary>
    /// Playbook schedule configuration parsed from <c>sprk_configjson</c>.
    /// </summary>
    internal sealed record ScheduleConfig(string Frequency, string Time)
    {
        /// <summary>
        /// Default schedule: daily at 06:00 UTC.
        /// </summary>
        public static readonly ScheduleConfig Default = new("daily", "06:00");
    }
}
