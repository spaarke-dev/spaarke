using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Background service that runs daily at midnight UTC to expire demo accounts.
/// A hand-rolled timer BackgroundService, same structure as DailySendCountResetService (existing debt, ADR-052 §1).
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
    private readonly SpeContainerOwnershipGuard _ownership;
    private readonly SpeContainerMembershipService _membership;
    private readonly DataverseEnvironmentService _environmentService;
    private readonly DemoProvisioningOptions _options;
    private readonly ILogger<DemoExpirationService> _logger;

    public DemoExpirationService(
        GraphUserService graphUserService,
        RegistrationDataverseService dataverseService,
        RegistrationEmailService emailService,
        SpeContainerOwnershipGuard ownership,
        DataverseEnvironmentService environmentService,
        IOptions<DemoProvisioningOptions> options,
        ILogger<DemoExpirationService> logger,
        ILoggerFactory loggerFactory)
        : this(
            graphUserService, dataverseService, emailService, ownership,
            // #1363: the revoke goes through the ONE marked-grant primitive, like Step 8's grant. Stateless over the
            // ownership guard (task 227d), so this singleton holds its own instance (the DemoProvisioningService shape).
            new SpeContainerMembershipService(
                ownership ?? throw new ArgumentNullException(nameof(ownership)),
                (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger<SpeContainerMembershipService>()),
            environmentService, options, logger)
    {
    }

    /// <summary>The test seam (ADR-038 §4): the marked-grant primitives are virtual, so a test substitutes them here.</summary>
    internal DemoExpirationService(
        GraphUserService graphUserService,
        RegistrationDataverseService dataverseService,
        RegistrationEmailService emailService,
        SpeContainerOwnershipGuard ownership,
        SpeContainerMembershipService membership,
        DataverseEnvironmentService environmentService,
        IOptions<DemoProvisioningOptions> options,
        ILogger<DemoExpirationService> logger)
    {
        _graphUserService = graphUserService ?? throw new ArgumentNullException(nameof(graphUserService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
        _environmentService = environmentService ?? throw new ArgumentNullException(nameof(environmentService));
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
    /// Internal (not private) so the test assembly (InternalsVisibleTo) runs the real pass over a faked Dataverse.
    /// </summary>
    internal async Task ProcessExpirationsAsync(CancellationToken ct)
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

            // The request's own target environment, resolved once for Steps 2 and 4. Null means it could not
            // be resolved (already logged as an error): those two steps are skipped, never redirected.
            var environment = await ResolveTargetEnvironmentAsync(request, ct);

            // Step 2: Remove from Demo Team in Dataverse
            if (environment != null)
            {
                try
                {
                    var teamName = environment.TeamName
                        ?? throw new InvalidOperationException(
                            $"Dataverse environment {environment.Id} is missing TeamName.");
                    // The systemuser and the team live in the target environment (provisioning writes them there),
                    // so both lookups and the disassociate go to its URL; a blank URL must not quietly mean "admin".
                    if (string.IsNullOrWhiteSpace(environment.DataverseUrl))
                    {
                        throw new InvalidOperationException(
                            $"Dataverse environment {environment.Id} is missing DataverseUrl.");
                    }

                    var systemUserId = await _dataverseService.ResolveSystemUserIdByAadObjectIdAsync(
                        aadObjectId, ct, targetDataverseUrl: environment.DataverseUrl);

                    if (systemUserId.HasValue)
                    {
                        _logger.LogInformation(
                            "[Expire] Removing systemuser {SystemUserId} from team {TeamName} for request {RequestId}",
                            systemUserId.Value, teamName, request.Id);
                        await _dataverseService.RemoveUserFromTeamAsync(
                            teamName, systemUserId.Value, ct, targetDataverseUrl: environment.DataverseUrl);
                        _logger.LogInformation("[Expire] Removed from team {TeamName}", teamName);
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
            if (environment != null)
            {
                try
                {
                    var speContainerId = environment.SpeContainerId
                        ?? throw new InvalidOperationException(
                            $"Dataverse environment {environment.Id} is missing SpeContainerId.");
                    _logger.LogInformation(
                        "[Expire] Revoking SPE container access for user {AadObjectId} on container {ContainerId}",
                        aadObjectId, speContainerId);
                    await RevokeSpeContainerAccessAsync(speContainerId, aadObjectId, ct);
                    _logger.LogInformation("[Expire] Revoked SPE container access");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[Expire] Failed to revoke SPE container access for request {RequestId}",
                        request.Id);
                }
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
    /// Removes the demo user's writer grant from the environment's SPE container through the marked-grant model (#1363):
    /// the grant Step 8 recorded is removed with <see cref="SpeContainerMembershipService.RemoveMarkedGrantAsync"/>, which
    /// deletes only a plain writer role, with <c>Prefer: onlyRemoveContainerScopedPermission</c>, and clears its marker.
    /// </summary>
    /// <remarks>
    /// <para>Which grant, in order:</para>
    /// <list type="number">
    /// <item>The <see cref="SpeContainerMembershipService.DemoWriterMarkerPrefix"/> marker keyed by the user's Entra object
    /// id — what Step 8 records.</item>
    /// <item>A <see cref="SpeContainerMembershipService.StandingWriterMarkerPrefix"/> marker whose value is the user's
    /// permission — what Step 8 recorded between task 171 and #1363 (keyed by a demo-environment systemuserid the
    /// standing pass could never resolve), or a standing grant of the same user in a single-environment setup.</item>
    /// <item>No marker names the user: a grant made before task 171, which carried no marker. It is deleted only while it
    /// is a plain writer role (the only role Step 8 ever made), with the same <c>Prefer</c> header.</item>
    /// </list>
    /// <para><b>Fails closed.</b> Every read fault, an incomplete role list that cannot prove an absence, and a removal the
    /// primitive reports as failed THROW, so the caller logs the error naming the request; the marker stays on the
    /// container, so the grant remains recorded and removable — never orphaned. A container this stamp does not own is
    /// refused before Graph is called (task 227d).</para>
    /// <para><b>Internal</b> so the test assembly (InternalsVisibleTo) drives it directly.</para>
    /// </remarks>
    internal async Task RevokeSpeContainerAccessAsync(
        string containerId, string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out var objectId) || objectId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"The demo user's Entra object id '{userId}' is not a GUID; its grant on container {containerId} cannot be named.");
        }

        var access = await _membership.ReadAccessAsync(containerId, ct).ConfigureAwait(false);
        if (access is null)
        {
            _logger.LogInformation(
                "[Expire] Container {ContainerId} does not exist; nothing to revoke for user {UserId}", containerId, userId);
            return;
        }

        var role = access.Roles.FirstOrDefault(r => r.IsFor(upn: null, objectId));

        string? markerKey = null;
        string? permissionId = null;
        var demoKey = SpeContainerMembershipService.MarkerKey(SpeContainerMembershipService.DemoWriterMarkerPrefix, objectId);
        if (access.Markers.TryGetValue(demoKey, out var demoPermission) && !string.IsNullOrWhiteSpace(demoPermission))
        {
            markerKey = demoKey;
            permissionId = demoPermission.Trim();
        }
        else if (role is not null)
        {
            var standing = access.Markers.FirstOrDefault(m =>
                m.Key.StartsWith(SpeContainerMembershipService.StandingWriterMarkerPrefix, StringComparison.Ordinal)
                && string.Equals(m.Value?.Trim(), role.PermissionId, StringComparison.Ordinal));
            if (standing.Key is not null)
            {
                markerKey = standing.Key;
                permissionId = role.PermissionId;
            }
        }

        if (markerKey is not null)
        {
            var outcome = await _membership
                .RemoveMarkedGrantAsync(containerId, markerKey, permissionId!, access, ct)
                .ConfigureAwait(false);
            if (outcome == SpeContainerMembershipService.MarkedRemovalOutcome.Failed)
            {
                throw new InvalidOperationException(
                    $"The marked grant {markerKey} (permission {permissionId}) on container {containerId} could not be removed; "
                    + "its marker is kept, so the grant stays recorded for the next attempt or an operator.");
            }

            _logger.LogInformation(
                "[Expire] Marked grant {MarkerKey} (permission {PermissionId}) for user {UserId} on container {ContainerId}: {Outcome}",
                markerKey, permissionId, userId, containerId, outcome);
            return;
        }

        // No marker names this user: a grant from before task 171, or none at all.
        if (role is null)
        {
            if (!access.RolesComplete)
            {
                throw new InvalidOperationException(
                    $"The roles on container {containerId} could not be fully enumerated, so user {userId} cannot be confirmed "
                    + "to hold none. They may RETAIN file access.");
            }

            _logger.LogInformation(
                "[Expire] No permission entry found for user {UserId} on container {ContainerId}", userId, containerId);
            return;
        }

        if (!role.IsPlainWriter)
        {
            _logger.LogWarning(
                "[Expire] User {UserId} holds an unrecorded role [{Roles}] (permission {PermissionId}) on container {ContainerId} "
                + "that demo provisioning never grants; it is left in place for an operator.",
                userId, string.Join(",", role.Roles), role.PermissionId, containerId);
            return;
        }

        // Refuses a container this BFF does not own before Graph is called (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct).ConfigureAwait(false);
        try
        {
            await graphClient.Storage.FileStorage.Containers[containerId]
                .Permissions[role.PermissionId]
                .DeleteAsync(rc => rc.Headers.Add("Prefer", SpeContainerMembershipService.OnlyContainerScopedPrefer), ct)
                .ConfigureAwait(false);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            // Already gone — the state expiry wanted.
        }

        _logger.LogInformation(
            "[Expire] Removed unrecorded (pre-task-171) permission {PermissionId} for user {UserId} from container {ContainerId}",
            role.PermissionId, userId, containerId);
    }

    /// <summary>
    /// Resolves the Dataverse environment a registration request was provisioned into, from the request's own
    /// <c>DataverseEnvironmentId</c> (the "Target Environment" lookup the approve path reads) via
    /// <see cref="DataverseEnvironmentService.GetByIdAsync"/>. Called once per record; Steps 2 and 4 share the result.
    /// </summary>
    /// <remarks>
    /// <para>Never falls back to a registry-wide "default" or "first active" row. Every customer stamp adds an active
    /// <c>sprk_dataverseenvironment</c> row, so any registry-wide pick lands on whichever row sorts first and the
    /// team removal and SPE revoke would run against the wrong environment (found at T186).</para>
    /// <para>The row's <c>IsActive</c> flag is deliberately not checked: an environment deactivated after the demo
    /// was provisioned still holds the user's team membership and container grant, and expiry must remove them.</para>
    /// </remarks>
    /// <returns>The environment, or <c>null</c> after logging an error naming the request when the request has no
    /// target environment, the row is gone, or the lookup failed. The caller skips Steps 2 and 4.</returns>
    private async Task<DataverseEnvironmentRecord?> ResolveTargetEnvironmentAsync(
        RegistrationRequestRecord request, CancellationToken ct)
    {
        if (!request.DataverseEnvironmentId.HasValue)
        {
            _logger.LogError(
                "[Expire] Request {RequestId} has no target environment; skipping team removal (Step 2) and SPE revoke (Step 4)",
                request.Id);
            return null;
        }

        try
        {
            var environment = await _environmentService.GetByIdAsync(request.DataverseEnvironmentId.Value, ct);
            if (environment == null)
            {
                _logger.LogError(
                    "[Expire] Target environment {EnvironmentId} of request {RequestId} was not found; skipping team removal (Step 2) and SPE revoke (Step 4)",
                    request.DataverseEnvironmentId.Value, request.Id);
            }

            return environment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Expire] Failed to read target environment {EnvironmentId} of request {RequestId}; skipping team removal (Step 2) and SPE revoke (Step 4)",
                request.DataverseEnvironmentId.Value, request.Id);
            return null;
        }
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
