using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Daily at midnight UTC, expires demo accounts whose expiration date has passed and warns those expiring within three
/// days. Runs on the in-process <c>Spaarke.Scheduling</c> host (ADR-036), registered with <c>AddScheduledJob</c> inside
/// the demo-provisioning gate of <c>RegistrationModule</c>.
/// </summary>
/// <remarks>
/// <para><b>Expire</b> — a Provisioned request past its expiration date: (1) disable the Entra account, (2) remove the
/// systemuser from the environment's team, (3) remove the user from the demo security group, (4) remove the demo SPE
/// grant (#1363). Only when ALL four succeeded is the request marked <c>Expired</c> and the expired notice sent. A step
/// that failed leaves the request Provisioned: a Warning naming the request and the failed steps is logged, and the next
/// run repeats every step. Each step is idempotent on repeat — an account already disabled, a user no longer in the team
/// or group, a grant already removed all succeed without a change.</para>
/// <para><b>Target environment</b> (Steps 2 and 4) — read once, with a typed outcome (<see cref="TargetEnvironmentState"/>).
/// ABSENT (the request names none, or a successful registry read finds no such row): there is nothing in it to clean,
/// so Steps 2 and 4 are done and the request expires once Steps 1 and 3 succeed. UNREADABLE (the registry read
/// faulted): transient, so Steps 2 and 4 count as failed and the next run retries. Absence is never inferred from an
/// exception.</para>
/// <para><b>Known limits.</b></para>
/// <list type="bullet">
/// <item>(K1) "Not owned" is wider than a foreign container: <see cref="SpeContainerOwnershipGuard"/> also answers it
/// for a blank id, a container without the stamp's ownership marker, and a 400/403/404 from Graph while reading that
/// marker. Expiry treats every such answer as "nothing of this stamp's to revoke" (a Warning naming the container and
/// the request), since it repeats on every run.</item>
/// <item>(K2) The warning's once-per-day marker fails open on a Redis fault, so a manual trigger after a Redis blip may
/// resend that day's warning. The scheduled tick cannot double-send: the host's lease runs each occurrence once.</item>
/// <item>(K3) A permanent configuration fault (a lost TeamName or DataverseUrl, a renamed team, a persistent 5xx)
/// retries daily with a Warning and has no end state. That fails closed: the request is never marked Expired with
/// access possibly standing. Once a request is more than <see cref="OverdueEscalation"/> past its expiration date the
/// line is logged at Error, so it reaches alerting.</item>
/// </list>
/// <para><b>Each email once.</b> The expired notice is sent AFTER the status write, so a request is notified only once it
/// has left the Provisioned set the run reads (a status write that fails sends nothing; the next run repeats). A notice
/// that then fails to send is logged and not retried. The warning is sent at most once per request per UTC day — its
/// completion marker (<see cref="IIdempotencyService"/>) makes a manual trigger or a second run that day a no-op.</para>
/// <para><b>ADR-036 A1.</b> One dispatch per tick across every instance (the host's lease, A1-1). Retry (A1-4): the run
/// THROWS only when the Provisioned list itself cannot be read — nothing was done or sent, so a retry is safe; a
/// record whose cleanup failed is counted and revisited by the next daily run (the job's own retry would re-send
/// warnings). Heartbeat (A1-5): one structured line per attempt.</para>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): migrated by #1363 from a hand-rolled wait-until-midnight
/// <c>BackgroundService</c> (<c>DemoExpirationService</c>), which ran on every instance — a scaled-out BFF expired, and
/// emailed, each record once per instance. Same work, same schedule, same dependencies: replaced surface, not added.</para>
/// </remarks>
public sealed class DemoExpirationJob : IScheduledJob
{
    /// <summary>Stable job id.</summary>
    public const string JobIdConstant = "demo-expiration";

    /// <summary>Daily at midnight UTC — the schedule the hand-rolled loop kept (the host evaluates cron in UTC).</summary>
    internal const string DefaultCronSchedule = "0 0 * * *";

    /// <summary>A warning marker only has to outlive its UTC day; a few days covers clock skew between instances.</summary>
    internal static readonly TimeSpan WarningMarkerLifetime = TimeSpan.FromDays(4);

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly GraphUserService _graphUserService;
    private readonly RegistrationDataverseService _dataverseService;
    private readonly RegistrationEmailService _emailService;
    private readonly SpeContainerOwnershipGuard _ownership;
    private readonly SpeContainerMembershipService _membership;
    private readonly DataverseEnvironmentService _environmentService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly DemoProvisioningOptions _options;
    private readonly ILogger<DemoExpirationJob> _logger;

    public DemoExpirationJob(
        GraphUserService graphUserService,
        RegistrationDataverseService dataverseService,
        RegistrationEmailService emailService,
        SpeContainerOwnershipGuard ownership,
        DataverseEnvironmentService environmentService,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<DemoProvisioningOptions> options,
        ILogger<DemoExpirationJob> logger,
        ILoggerFactory loggerFactory)
        : this(
            graphUserService, dataverseService, emailService, ownership,
            // #1363: the revoke goes through the ONE marked-grant primitive, like Step 8's grant. Stateless over the
            // ownership guard (task 227d), so this singleton holds its own instance (the DemoProvisioningService shape).
            new SpeContainerMembershipService(
                ownership ?? throw new ArgumentNullException(nameof(ownership)),
                (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger<SpeContainerMembershipService>()),
            environmentService, scopeFactory, timeProvider, options, logger)
    {
    }

    /// <summary>The test seam (ADR-038 §4): the marked-grant primitives are virtual, so a test substitutes them here.</summary>
    internal DemoExpirationJob(
        GraphUserService graphUserService,
        RegistrationDataverseService dataverseService,
        RegistrationEmailService emailService,
        SpeContainerOwnershipGuard ownership,
        SpeContainerMembershipService membership,
        DataverseEnvironmentService environmentService,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<DemoProvisioningOptions> options,
        ILogger<DemoExpirationJob> logger)
    {
        _graphUserService = graphUserService ?? throw new ArgumentNullException(nameof(graphUserService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
        _environmentService = environmentService ?? throw new ArgumentNullException(nameof(environmentService));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Demo Account Expiration";

    /// <inheritdoc />
    public string Description =>
        "Daily at midnight UTC, expires demo accounts past their expiration date (disables the Entra account, removes the " +
        "team and group memberships and the SPE container grant, then marks the request Expired and sends the notice) and " +
        "warns accounts expiring within three days. A request whose cleanup failed stays Provisioned and is retried next run.";

    /// <summary>How long past its expiration date a request may stay unexpired before its retry line is logged at Error (K3).</summary>
    internal static readonly TimeSpan OverdueEscalation = TimeSpan.FromDays(7);

    /// <summary>What one pass did.</summary>
    internal sealed record PassCounts(
        int Provisioned, int Expired, int Retrying, int Warned, int AlreadyWarned, int WarningsFailed, int NoExpirationDate);

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();

        // Throws only when the Provisioned list cannot be read: nothing was done or sent, so the host's retry is safe.
        var counts = await ProcessExpirationsAsync(cancellationToken).ConfigureAwait(false);

        var duration = _timeProvider.GetElapsedTime(started);
        _logger.LogInformation(
            "[DEMO-EXPIRATION] heartbeat attempt={Attempt} provisioned={Provisioned} expired={Expired} retrying={Retrying} " +
            "warned={Warned} alreadyWarned={AlreadyWarned} warningsFailed={WarningsFailed} noExpirationDate={NoExpirationDate} durationMs={DurationMs}",
            context.Attempt, counts.Provisioned, counts.Expired, counts.Retrying, counts.Warned, counts.AlreadyWarned,
            counts.WarningsFailed, counts.NoExpirationDate, (long)duration.TotalMilliseconds);

        var success = counts.Retrying == 0 && counts.WarningsFailed == 0;
        return new JobRunResult(
            Success: success,
            ErrorMessage: success ? null : "Some expirations did not complete or some warnings failed — see the Warnings naming each request; the next run retries.",
            ProcessedItems: counts.Expired + counts.Warned,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(counts, ResultJsonOptions));
    }

    /// <summary>
    /// One pass over every Provisioned request: expire those past their date, warn those within three days.
    /// Internal so the test assembly (InternalsVisibleTo) runs the real pass over a faked Dataverse.
    /// </summary>
    internal async Task<PassCounts> ProcessExpirationsAsync(CancellationToken ct)
    {
        var provisionedRequests = await _dataverseService.GetRequestsByStatusAsync(
            RegistrationStatus.Provisioned, ct: ct).ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();
        int expired = 0, retrying = 0, warned = 0, alreadyWarned = 0, warningsFailed = 0, noDate = 0;

        using var scope = _scopeFactory.CreateScope();
        var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();

        foreach (var request in provisionedRequests)
        {
            ct.ThrowIfCancellationRequested();
            if (request.ExpirationDate == null)
            {
                _logger.LogWarning(
                    "Provisioned request {RequestId} ({Email}) has no expiration date, skipping",
                    request.Id, request.Email);
                noDate++;
                continue;
            }

            if (request.ExpirationDate <= now)
            {
                if (await ProcessExpiredRecordAsync(request, ct).ConfigureAwait(false))
                    expired++;
                else
                    retrying++;
            }
            else if (request.ExpirationDate <= now.AddDays(3))
            {
                switch (await ProcessExpirationWarningAsync(request, now, idempotency, ct).ConfigureAwait(false))
                {
                    case WarningOutcome.Sent: warned++; break;
                    case WarningOutcome.AlreadySent: alreadyWarned++; break;
                    case WarningOutcome.Failed: warningsFailed++; break;
                }
            }
        }

        return new PassCounts(provisionedRequests.Count, expired, retrying, warned, alreadyWarned, warningsFailed, noDate);
    }

    /// <summary>
    /// Expires one record. Returns <see langword="true"/> when it is now Expired; <see langword="false"/> when a cleanup
    /// step (or the status write) failed — the request stays Provisioned and the next run repeats every step.
    /// </summary>
    private async Task<bool> ProcessExpiredRecordAsync(RegistrationRequestRecord request, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing expired record {RequestId} ({Email}), expired on {ExpirationDate:u}",
            request.Id, request.Email, request.ExpirationDate);

        var failedSteps = new List<string>();
        var aadObjectId = request.DemoUserObjectId;
        if (string.IsNullOrWhiteSpace(aadObjectId))
        {
            // Nothing names the user anywhere, so no step can ever run; a retry would never do better.
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
            catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
                when (ex.ResponseStatusCode == 404 && Guid.TryParse(aadObjectId, out _))
            {
                // The account no longer exists (deleted): at least as closed as disabled, so the step is done. Only for
                // a GUID id: Graph resolves anything else as a UPN, and its 404 then proves nothing about the account.
                _logger.LogInformation(
                    "[Expire] Entra account {AadObjectId} of request {RequestId} no longer exists; nothing to disable",
                    aadObjectId, request.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failedSteps.Add("disable Entra account");
                _logger.LogError(ex,
                    "[Expire] Failed to disable Entra account {AadObjectId} for request {RequestId}",
                    aadObjectId, request.Id);
            }

            // The request's own target environment, resolved once for Steps 2 and 4 (never redirected). Absent: there is
            // nothing in it to clean, so both steps are done. Unreadable: both are failed, and the next run retries.
            var lookup = await ResolveTargetEnvironmentAsync(request, ct);
            var environment = lookup.Environment;
            if (lookup.State == TargetEnvironmentState.Unreadable)
            {
                failedSteps.Add("read target environment (team removal and SPE revoke)");
            }

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
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    failedSteps.Add("remove from team");
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
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failedSteps.Add("remove from security group");
                _logger.LogError(ex,
                    "[Expire] Failed to remove from security group for request {RequestId}",
                    request.Id);
            }

            // Step 4: Revoke SPE container access
            if (environment != null)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(environment.SpeContainerId))
                    {
                        // Step 8 grants nothing without a container, so there is nothing to revoke.
                        _logger.LogInformation(
                            "[Expire] Environment {EnvironmentId} has no SPE container; nothing to revoke for request {RequestId}",
                            environment.Id, request.Id);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[Expire] Revoking SPE container access for user {AadObjectId} on container {ContainerId}",
                            aadObjectId, environment.SpeContainerId);
                        await RevokeSpeContainerAccessAsync(
                            environment.SpeContainerId, aadObjectId, request.DemoUsername, ct);
                        _logger.LogInformation("[Expire] Revoked SPE container access");
                    }
                }
                catch (SdapProblemException ex) when (ex.Code == SpeContainerOwnershipGuard.NotOwnedErrorCode)
                {
                    // Not one of this stamp's containers: since task 227d Step 8 refuses such a container, so no grant this
                    // stamp could make stands there — and none it could remove. Not retried (the same answer every run).
                    _logger.LogWarning(
                        "[Expire] SPE container {ContainerId} of request {RequestId} is not one of this stamp's containers; "
                        + "no grant of this stamp can stand there, so there is nothing to revoke.",
                        environment.SpeContainerId, request.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    failedSteps.Add("revoke SPE container access");
                    _logger.LogError(ex,
                        "[Expire] Failed to revoke SPE container access for request {RequestId}",
                        request.Id);
                }
            }
        }

        if (failedSteps.Count > 0)
        {
            // K3: a fault that never clears retries daily; past OverdueEscalation the same line goes out at Error.
            var overdue = request.ExpirationDate is { } expires
                          && _timeProvider.GetUtcNow() - expires > OverdueEscalation;
            _logger.Log(
                overdue ? LogLevel.Error : LogLevel.Warning,
                "[Expire] Request {RequestId} was NOT expired this run: {FailedSteps} failed. It stays Provisioned and the next "
                + "run repeats every step; no expired notice is sent until it completes. Expired on {ExpirationDate:u}"
                + "{Overdue}.",
                request.Id, string.Join("; ", failedSteps), request.ExpirationDate,
                overdue ? $"; OVERDUE by more than {OverdueEscalation.TotalDays:0} days" : string.Empty);
            return false;
        }

        // Step 5: Update status to Expired — the request leaves the set the next run reads.
        try
        {
            _logger.LogInformation("[Expire] Updating request {RequestId} status to Expired", request.Id);
            await _dataverseService.UpdateRequestStatusAsync(request.Id, RegistrationStatus.Expired, ct: ct);
            _logger.LogInformation("[Expire] Updated request {RequestId} to Expired", request.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[Expire] Request {RequestId} was NOT expired this run: the status write failed. It stays Provisioned and the "
                + "next run repeats every step; no expired notice is sent until it completes.",
                request.Id);
            return false;
        }

        // Step 6: Send expired notification email — after the status write, so it is sent once.
        try
        {
            if (!string.IsNullOrWhiteSpace(request.Email) && !string.IsNullOrWhiteSpace(request.FirstName))
            {
                _logger.LogInformation("[Expire] Sending expired notification to {Email}", request.Email);
                await _emailService.SendExpiredNotificationAsync(request.Email, request.FirstName, ct);
                _logger.LogInformation("[Expire] Sent expired notification to {Email}", request.Email);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[Expire] Failed to send expired notification for request {RequestId}; the request is Expired, so it is not resent",
                request.Id);
        }

        return true;
    }

    private enum WarningOutcome { Sent, AlreadySent, Failed, NotSendable }

    /// <summary>
    /// Sends the pre-expiration warning for a record expiring within 3 days — at most once per request per UTC day, so a
    /// record gets one warning on each of its last days (the schedule the daily loop kept) however often the job runs.
    /// </summary>
    private async Task<WarningOutcome> ProcessExpirationWarningAsync(
        RegistrationRequestRecord request, DateTimeOffset now, IIdempotencyService idempotency, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FirstName))
            {
                _logger.LogWarning(
                    "Cannot send expiration warning for request {RequestId}: missing email or first name",
                    request.Id);
                return WarningOutcome.NotSendable;
            }

            // Invariant formatting: the key must not change with the server's culture, or every marker would be missed.
            var key = string.Create(CultureInfo.InvariantCulture, $"demo-expiration-warning:{request.Id:N}:{now.UtcDateTime:yyyyMMdd}");
            if (await idempotency.IsEventProcessedAsync(key, ct).ConfigureAwait(false))
            {
                return WarningOutcome.AlreadySent;
            }

            _logger.LogInformation(
                "[Warning] Sending expiration warning to {Email} for request {RequestId}, expires {ExpirationDate:u}",
                request.Email, request.Id, request.ExpirationDate);

            await _emailService.SendExpirationWarningAsync(
                request.Email, request.FirstName, request.ExpirationDate!.Value, ct);

            // Recorded even if the host is stopping: the warning went out. The scheduler's lease serializes runs, so
            // check-then-mark is enough here (ADR-036 A1-1).
            await idempotency.MarkEventAsProcessedAsync(key, WarningMarkerLifetime, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation("[Warning] Sent expiration warning to {Email}", request.Email);
            return WarningOutcome.Sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[Warning] Failed to send expiration warning for request {RequestId} ({Email})",
                request.Id, request.Email);
            return WarningOutcome.Failed;
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
    /// <para>The user's role is matched by UPN or Entra object id (<c>IsFor(upn, objectId)</c>, as every other caller):
    /// Graph keys a container grant by UPN and may return one with no object id.</para>
    /// <para><b>Internal</b> so the test assembly (InternalsVisibleTo) drives it directly.</para>
    /// </remarks>
    internal async Task RevokeSpeContainerAccessAsync(
        string containerId, string userId, string? upn, CancellationToken ct)
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

        var role = access.Roles.FirstOrDefault(r => r.IsFor(upn, objectId));

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
    /// <returns>
    /// <see cref="TargetEnvironmentState.Found"/> with the row; <see cref="TargetEnvironmentState.Absent"/> when the request
    /// names no environment or the registry answered that the row does not exist (a Warning naming the request);
    /// <see cref="TargetEnvironmentState.Unreadable"/> when the registry read faulted (an Error naming the request).
    /// </returns>
    private async Task<TargetEnvironmentLookup> ResolveTargetEnvironmentAsync(
        RegistrationRequestRecord request, CancellationToken ct)
    {
        if (!request.DataverseEnvironmentId.HasValue)
        {
            _logger.LogWarning(
                "[Expire] Request {RequestId} has no target environment; team removal (Step 2) and SPE revoke (Step 4) have nothing to clean",
                request.Id);
            return TargetEnvironmentLookup.Absent;
        }

        DataverseEnvironmentRecord? environment;
        try
        {
            // GetByIdAsync answers null only for a 404 on a successful call; every fault throws.
            environment = await _environmentService.GetByIdAsync(request.DataverseEnvironmentId.Value, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[Expire] Failed to read target environment {EnvironmentId} of request {RequestId}; team removal (Step 2) and SPE revoke (Step 4) are retried on the next run",
                request.DataverseEnvironmentId.Value, request.Id);
            return TargetEnvironmentLookup.Unreadable;
        }

        if (environment == null)
        {
            _logger.LogWarning(
                "[Expire] Target environment {EnvironmentId} of request {RequestId} does not exist; team removal (Step 2) and SPE revoke (Step 4) have nothing to clean",
                request.DataverseEnvironmentId.Value, request.Id);
            return TargetEnvironmentLookup.Absent;
        }

        return new TargetEnvironmentLookup(TargetEnvironmentState.Found, environment);
    }

    /// <summary>What the target-environment read established (#1363): a fact (found, absent) or a fault (unreadable).</summary>
    private enum TargetEnvironmentState
    {
        /// <summary>The row was read.</summary>
        Found,

        /// <summary>Affirmatively absent: the request names none, or a successful read found no such row.</summary>
        Absent,

        /// <summary>The read faulted (error, timeout, throttle, auth); says nothing about whether the row exists.</summary>
        Unreadable,
    }

    private readonly record struct TargetEnvironmentLookup(TargetEnvironmentState State, DataverseEnvironmentRecord? Environment)
    {
        public static TargetEnvironmentLookup Absent => new(TargetEnvironmentState.Absent, null);

        public static TargetEnvironmentLookup Unreadable => new(TargetEnvironmentState.Unreadable, null);
    }
}
