using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Background service that runs daily at midnight UTC to expire demo accounts.
/// Implements ADR-001 BackgroundService pattern (same structure as DailySendCountResetService).
///
/// Two responsibilities:
///   1. Expire: Provisioned records past their expiration date — disable Entra account,
///      remove from Demo Team, revoke SPE container access, send notification, update status.
///   2. Warn: Provisioned records expiring within 3 days — send warning email.
///
/// Each record is processed independently — one failure does not block others.
/// </summary>
public sealed class DemoExpirationService : BackgroundService
{
    private readonly GraphUserService _graphUserService;
    private readonly RegistrationDataverseService _dataverseService;
    private readonly RegistrationEmailService _emailService;
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly DemoProvisioningOptions _options;
    private readonly ILogger<DemoExpirationService> _logger;

    public DemoExpirationService(
        GraphUserService graphUserService,
        RegistrationDataverseService dataverseService,
        RegistrationEmailService emailService,
        IGraphClientFactory graphClientFactory,
        IOptions<DemoProvisioningOptions> options,
        ILogger<DemoExpirationService> logger)
    {
        _graphUserService = graphUserService ?? throw new ArgumentNullException(nameof(graphUserService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DemoExpirationService started — will process expirations at midnight UTC each day");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delayUntilMidnight = CalculateDelayUntilMidnightUtc();

                _logger.LogDebug(
                    "DemoExpirationService sleeping for {Hours:F1} hours until next midnight UTC",
                    delayUntilMidnight.TotalHours);

                await Task.Delay(delayUntilMidnight, stoppingToken);

                await ProcessExpirationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("DemoExpirationService stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during demo expiration processing, will retry in 1 minute");

                // Wait 1 minute before retrying to avoid tight error loops
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("DemoExpirationService stopped");
    }

    /// <summary>
    /// Main processing method: handles both expired records and pre-expiration warnings.
    /// </summary>
    private async Task ProcessExpirationsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting demo expiration processing at {Time:u}", DateTime.UtcNow);

        // Get all Provisioned requests
        var provisionedRequests = await _dataverseService.GetRequestsByStatusAsync(
            RegistrationStatus.Provisioned, ct: ct);

        _logger.LogInformation("Found {Count} provisioned requests to evaluate", provisionedRequests.Count);

        var now = DateTimeOffset.UtcNow;
        var expiredCount = 0;
        var warningCount = 0;

        foreach (var request in provisionedRequests)
        {
            if (request.ExpirationDate == null)
            {
                _logger.LogWarning(
                    "Provisioned request {RequestId} ({Email}) has no expiration date, skipping",
                    request.Id, request.Email);
                continue;
            }

            if (request.ExpirationDate <= now)
            {
                // Record has expired — process full expiration
                await ProcessExpiredRecordAsync(request, ct);
                expiredCount++;
            }
            else if (request.ExpirationDate <= now.AddDays(3))
            {
                // Record expires within 3 days — send warning
                await ProcessExpirationWarningAsync(request, ct);
                warningCount++;
            }
        }

        _logger.LogInformation(
            "Demo expiration processing completed at {Time:u}: {Expired} expired, {Warned} warnings sent",
            DateTime.UtcNow, expiredCount, warningCount);
    }

    /// <summary>
    /// Processes a single expired record: disable account, remove from team, revoke SPE access,
    /// send notification, update status. Each sub-operation is wrapped in try/catch so one
    /// failure doesn't block others.
    /// </summary>
    private async Task ProcessExpiredRecordAsync(RegistrationRequestRecord request, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing expired record {RequestId} ({Email}), expired on {ExpirationDate:u}",
            request.Id, request.Email, request.ExpirationDate);

        var aadObjectId = request.DemoUserObjectId;
        if (string.IsNullOrWhiteSpace(aadObjectId))
        {
            _logger.LogWarning(
                "Expired request {RequestId} has no DemoUserObjectId, skipping Entra/SPE cleanup",
                request.Id);
        }
        else
        {
            // Step 1: Disable Entra account
            try
            {
                _logger.LogInformation("[Expire] Disabling Entra account {AadObjectId} for request {RequestId}",
                    aadObjectId, request.Id);
                await _graphUserService.DisableUserAsync(aadObjectId, ct);
                _logger.LogInformation("[Expire] Disabled Entra account {AadObjectId}", aadObjectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Expire] Failed to disable Entra account {AadObjectId} for request {RequestId}",
                    aadObjectId, request.Id);
            }

            // Step 2: Remove from Demo Team in Dataverse
            try
            {
                var defaultEnv = ResolveDefaultEnvironment();
                var systemUserId = await _dataverseService.ResolveSystemUserIdByAadObjectIdAsync(aadObjectId, ct);

                if (systemUserId.HasValue)
                {
                    _logger.LogInformation(
                        "[Expire] Removing systemuser {SystemUserId} from team {TeamName} for request {RequestId}",
                        systemUserId.Value, defaultEnv.TeamName, request.Id);
                    await _dataverseService.RemoveUserFromTeamAsync(defaultEnv.TeamName, systemUserId.Value, ct);
                    _logger.LogInformation("[Expire] Removed from team {TeamName}", defaultEnv.TeamName);
                }
                else
                {
                    _logger.LogWarning(
                        "[Expire] Could not resolve systemuser for AAD object {AadObjectId}, skipping team removal",
                        aadObjectId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Expire] Failed to remove from Demo Team for request {RequestId}",
                    request.Id);
            }

            // Step 3: Remove from Entra security group
            try
            {
                _logger.LogInformation(
                    "[Expire] Removing user {AadObjectId} from security group {GroupId} for request {RequestId}",
                    aadObjectId, _options.DemoUsersGroupId, request.Id);
                await _graphUserService.RemoveFromGroupAsync(aadObjectId, _options.DemoUsersGroupId, ct);
                _logger.LogInformation("[Expire] Removed from security group");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Expire] Failed to remove from security group for request {RequestId}",
                    request.Id);
            }

            // Step 4: Revoke SPE container access
            try
            {
                var defaultEnv = ResolveDefaultEnvironment();
                _logger.LogInformation(
                    "[Expire] Revoking SPE container access for user {AadObjectId} on container {ContainerId}",
                    aadObjectId, defaultEnv.SpeContainerId);
                await RevokeSpeContainerAccessAsync(defaultEnv.SpeContainerId, aadObjectId, ct);
                _logger.LogInformation("[Expire] Revoked SPE container access");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Expire] Failed to revoke SPE container access for request {RequestId}",
                    request.Id);
            }
        }

        // Step 5: Send expired notification email
        try
        {
            if (!string.IsNullOrWhiteSpace(request.Email) && !string.IsNullOrWhiteSpace(request.FirstName))
            {
                _logger.LogInformation("[Expire] Sending expired notification to {Email}", request.Email);
                await _emailService.SendExpiredNotificationAsync(request.Email, request.FirstName, ct);
                _logger.LogInformation("[Expire] Sent expired notification to {Email}", request.Email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Expire] Failed to send expired notification for request {RequestId}",
                request.Id);
        }

        // Step 6: Update status to Expired
        try
        {
            _logger.LogInformation("[Expire] Updating request {RequestId} status to Expired", request.Id);
            await _dataverseService.UpdateRequestStatusAsync(request.Id, RegistrationStatus.Expired, ct: ct);
            _logger.LogInformation("[Expire] Updated request {RequestId} to Expired", request.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Expire] Failed to update status to Expired for request {RequestId}",
                request.Id);
        }
    }

    /// <summary>
    /// Sends a pre-expiration warning email for a record expiring within 3 days.
    /// Warning is sent once per run — the check is date-based (expiration within 3 days
    /// AND more than 0 days away), so the same record won't get multiple warnings
    /// since after expiration it enters the expired path instead.
    /// </summary>
    private async Task ProcessExpirationWarningAsync(RegistrationRequestRecord request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FirstName))
            {
                _logger.LogWarning(
                    "Cannot send expiration warning for request {RequestId}: missing email or first name",
                    request.Id);
                return;
            }

            _logger.LogInformation(
                "[Warning] Sending expiration warning to {Email} for request {RequestId}, expires {ExpirationDate:u}",
                request.Email, request.Id, request.ExpirationDate);

            await _emailService.SendExpirationWarningAsync(
                request.Email, request.FirstName, request.ExpirationDate!.Value, ct);

            _logger.LogInformation("[Warning] Sent expiration warning to {Email}", request.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Warning] Failed to send expiration warning for request {RequestId} ({Email})",
                request.Id, request.Email);
        }
    }

    /// <summary>
    /// Revokes SPE container permission for a user by listing permissions and deleting the matching entry.
    /// Same pattern as RevokeExternalAccessEndpoint.
    /// </summary>
    private async Task RevokeSpeContainerAccessAsync(
        string containerId, string userId, CancellationToken ct)
    {
        var graphClient = _graphClientFactory.ForApp();

        // List all permissions on the container
        var permissions = await graphClient.Storage.FileStorage.Containers[containerId].Permissions
            .GetAsync(cancellationToken: ct);

        if (permissions?.Value == null || permissions.Value.Count == 0)
        {
            _logger.LogInformation(
                "[Expire] No permissions found on container {ContainerId}, nothing to revoke",
                containerId);
            return;
        }

        // Find permission entry matching the user's AAD object ID
        var userPermission = permissions.Value.FirstOrDefault(p =>
        {
            var user = p.GrantedToV2?.User;
            return user?.Id != null &&
                   user.Id.Equals(userId, StringComparison.OrdinalIgnoreCase);
        });

        if (userPermission?.Id == null)
        {
            _logger.LogInformation(
                "[Expire] No permission entry found for user {UserId} on container {ContainerId}",
                userId, containerId);
            return;
        }

        // Delete the permission entry
        await graphClient.Storage.FileStorage.Containers[containerId]
            .Permissions[userPermission.Id]
            .DeleteAsync(cancellationToken: ct);

        _logger.LogInformation(
            "[Expire] Removed permission {PermissionId} for user {UserId} from container {ContainerId}",
            userPermission.Id, userId, containerId);
    }

    /// <summary>
    /// Resolves the default demo environment configuration.
    /// </summary>
    private DemoEnvironmentConfig ResolveDefaultEnvironment()
    {
        return _options.Environments.FirstOrDefault(e => e.Name == _options.DefaultEnvironment)
            ?? _options.Environments.First();
    }

    /// <summary>
    /// Calculates the delay from now until the next midnight UTC.
    /// If it's exactly midnight, schedules for tomorrow's midnight.
    /// </summary>
    internal static TimeSpan CalculateDelayUntilMidnightUtc()
    {
        var now = DateTime.UtcNow;
        var nextMidnight = now.Date.AddDays(1);
        var delay = nextMidnight - now;

        // Safety: ensure at least 1 second delay to avoid tight loops
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromDays(1) : delay;
    }
}
