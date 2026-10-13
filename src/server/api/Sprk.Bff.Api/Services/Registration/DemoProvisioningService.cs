using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Registration;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Orchestrates the full demo provisioning pipeline: Entra user creation, license assignment,
/// Dataverse systemuser sync, SPE container access, and welcome email delivery.
/// Single entry point: <see cref="ProvisionDemoAccessAsync"/>.
/// ADR-004: Idempotent — checks existing state before acting.
/// ADR-010: Registered as concrete type (no interface).
/// </summary>
public sealed class DemoProvisioningService
{
    private readonly GraphUserService _graphUserService;
    private readonly RegistrationDataverseService _dataverseService;
    private readonly RegistrationEmailService _emailService;
    private readonly PasswordGenerator _passwordGenerator;
    private readonly SpeContainerMembershipService _membership;
    private readonly DemoProvisioningOptions _options;
    private readonly ILogger<DemoProvisioningService> _logger;

    public DemoProvisioningService(
        GraphUserService graphUserService,
        RegistrationDataverseService dataverseService,
        RegistrationEmailService emailService,
        PasswordGenerator passwordGenerator,
        SpeContainerOwnershipGuard ownership,
        IOptions<DemoProvisioningOptions> options,
        ILogger<DemoProvisioningService> logger,
        ILoggerFactory loggerFactory)
    {
        _graphUserService = graphUserService ?? throw new ArgumentNullException(nameof(graphUserService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _passwordGenerator = passwordGenerator ?? throw new ArgumentNullException(nameof(passwordGenerator));
        // Task 171 (finding 6): Step 8 grants through the ONE marked-grant primitive. The service is stateless over the
        // ownership guard (task 227d), so this singleton holds its own instance rather than reaching into a request scope.
        _membership = new SpeContainerMembershipService(
            ownership ?? throw new ArgumentNullException(nameof(ownership)),
            (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger<SpeContainerMembershipService>());
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Provisions full demo access for an approved registration request.
    /// Executes 9 steps in order:
    ///   1. Generate unique username (UPN)
    ///   2. Generate temporary password
    ///   3. Create Entra ID user
    ///   4. Add to demo security group
    ///   5. Assign licenses (Power Apps, Fabric, Power Automate)
    ///   6. Create systemuser in Dataverse
    ///   7. Add to Demo Team in Dataverse
    ///   8. Grant SPE container Writer access
    ///   9. Send welcome email to applicant's work email
    /// After all steps: updates registration record status to Provisioned.
    /// Idempotent: if sprk_demousername is already set, returns existing result.
    /// </summary>
    /// <param name="request">The approved registration request record.</param>
    /// <param name="environment">Target demo environment configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Approve response with status, username, and expiration date.</returns>
    /// <exception cref="DemoProvisioningException">
    /// Thrown when a provisioning step fails. Contains details of which steps succeeded.
    /// </exception>
    public async Task<ApproveResponseDto> ProvisionDemoAccessAsync(
        RegistrationRequestRecord request,
        DemoEnvironmentConfig environment,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(environment);

        _logger.LogInformation(
            "Starting demo provisioning for request {RequestId} ({Email}) in environment {Environment}",
            request.Id, request.Email, environment.Name);

        // ── Idempotency check (ADR-004) ──
        // If sprk_demousername is already set, this request was already provisioned.
        if (!string.IsNullOrWhiteSpace(request.DemoUsername))
        {
            _logger.LogInformation(
                "Request {RequestId} already provisioned with username {Username}, returning existing result",
                request.Id, request.DemoUsername);

            return new ApproveResponseDto
            {
                Status = "Provisioned",
                Username = request.DemoUsername,
                ExpirationDate = request.ExpirationDate ?? DateTimeOffset.UtcNow.AddDays(environment.DefaultDemoDurationDays)
            };
        }

        // Track completed steps for partial failure reporting
        var completedSteps = new List<string>();
        string? entraUserId = null;
        string? upn = null;
        string? temporaryPassword = null;
        Guid? dataverseSystemUserId = null;
        var expirationDate = DateTimeOffset.UtcNow.AddDays(environment.DefaultDemoDurationDays);

        try
        {
            // ── Step 1: Generate unique username ──
            _logger.LogInformation("[Step 1/9] Generating unique UPN for {FirstName} {LastName}",
                request.FirstName, request.LastName);
            upn = await _graphUserService.GenerateUniqueUPNAsync(
                request.FirstName!, request.LastName!, ct);
            completedSteps.Add("GenerateUPN");
            _logger.LogInformation("[Step 1/9] Generated UPN: {Upn}", upn);

            // ── Step 2: Generate temporary password ──
            _logger.LogInformation("[Step 2/9] Generating temporary password");
            temporaryPassword = _passwordGenerator.Generate();
            completedSteps.Add("GeneratePassword");
            _logger.LogInformation("[Step 2/9] Temporary password generated");

            // ── Step 3: Create Entra ID user ──
            _logger.LogInformation("[Step 3/9] Creating Entra ID user {Upn}", upn);
            var (userId, createdUpn, createdPassword) = await _graphUserService.CreateUserAsync(
                request.FirstName!, request.LastName!, request.Organization!, ct);
            entraUserId = userId;
            upn = createdUpn; // Use the UPN returned by CreateUserAsync (may differ due to collision)
            temporaryPassword = createdPassword; // Use the password generated internally by CreateUserAsync
            completedSteps.Add("CreateEntraUser");
            _logger.LogInformation("[Step 3/9] Created Entra user {UserId} with UPN {Upn}", entraUserId, upn);

            // ── Step 4: Add to demo security group ──
            _logger.LogInformation("[Step 4/9] Adding user {UserId} to demo security group {GroupId}",
                entraUserId, _options.DemoUsersGroupId);
            await _graphUserService.AddToGroupAsync(entraUserId, _options.DemoUsersGroupId, ct);
            completedSteps.Add("AddToSecurityGroup");
            _logger.LogInformation("[Step 4/9] Added user to demo security group");

            // ── Step 5: Assign licenses ──
            _logger.LogInformation("[Step 5/9] Assigning licenses to user {UserId}", entraUserId);
            await _graphUserService.AssignLicensesAsync(entraUserId, environment.Licenses, ct);
            completedSteps.Add("AssignLicenses");
            _logger.LogInformation("[Step 5/9] Licenses assigned");

            // ── Step 6: Create systemuser in Dataverse ──
            _logger.LogInformation("[Step 6/9] Creating systemuser in Dataverse for {Upn} in BU {BusinessUnit}",
                upn, environment.BusinessUnitName);
            dataverseSystemUserId = await _dataverseService.CreateSystemUserAsync(
                entraUserId,
                request.FirstName!,
                request.LastName!,
                upn,
                environment.BusinessUnitName,
                ct,
                targetDataverseUrl: environment.DataverseUrl);
            completedSteps.Add("CreateSystemUser");
            _logger.LogInformation("[Step 6/9] Created systemuser {SystemUserId}", dataverseSystemUserId);

            // ── Step 7: Add to Demo Team ──
            _logger.LogInformation("[Step 7/9] Adding systemuser {SystemUserId} to team {TeamName}",
                dataverseSystemUserId, environment.TeamName);
            await _dataverseService.AddUserToTeamAsync(
                environment.TeamName, dataverseSystemUserId.Value, ct,
                targetDataverseUrl: environment.DataverseUrl);
            completedSteps.Add("AddToTeam");
            _logger.LogInformation("[Step 7/9] Added to team {TeamName}", environment.TeamName);

            // ── Step 8: Grant SPE container Writer access ──
            // SPE container access is optional — skip gracefully if container ID is a placeholder or grant fails.
            // Only a container this BFF owns can be granted (task 227d, owner D29): a target environment served
            // by another BFF is refused (404 spe_container_not_owned) and lands here as a skipped step.
            try
            {
                _logger.LogInformation("[Step 8/9] Granting Writer access on SPE container {ContainerId} to user {UserId}",
                    environment.SpeContainerId, entraUserId);
                await GrantSpeContainerAccessAsync(environment.SpeContainerId, dataverseSystemUserId.Value, upn, ct);
                completedSteps.Add("GrantSpeContainerAccess");
                _logger.LogInformation("[Step 8/9] Granted SPE container Writer access");
            }
            catch (Exception speEx)
            {
                _logger.LogWarning(speEx, "[Step 8/9] SPE container access grant failed (non-fatal): {Message}", speEx.Message);
                completedSteps.Add("GrantSpeContainerAccess:SKIPPED");
            }

            // ── Step 9: Send welcome email ──
            // Welcome email goes to applicant's WORK email (request.Email), not demo.spaarke.com
            _logger.LogInformation("[Step 9/9] Sending welcome email to {WorkEmail}", request.Email);
            await _emailService.SendWelcomeEmailAsync(
                recipientEmail: request.Email!,
                firstName: request.FirstName!,
                username: upn,
                temporaryPassword: temporaryPassword,
                accessUrl: environment.DataverseUrl,
                expirationDate: expirationDate,
                environmentName: environment.Name,
                ct);
            completedSteps.Add("SendWelcomeEmail");
            _logger.LogInformation("[Step 9/9] Welcome email sent");

            // ── Post-provisioning: Update registration record ──
            _logger.LogInformation("Updating registration record {RequestId} to Provisioned status", request.Id);
            await _dataverseService.UpdateRequestStatusAsync(
                request.Id,
                RegistrationStatus.Provisioned,
                new Dictionary<string, object?>
                {
                    ["sprk_demousername"] = upn,
                    ["sprk_demouserobjectid"] = entraUserId,
                    ["sprk_provisioneddate"] = DateTimeOffset.UtcNow,
                    ["sprk_expirationdate"] = expirationDate,
                    ["sprk_environment"] = environment.Name
                },
                ct);

            _logger.LogInformation(
                "Demo provisioning complete for request {RequestId}: username={Username}, expires={ExpirationDate}",
                request.Id, upn, expirationDate);

            return new ApproveResponseDto
            {
                Status = "Provisioned",
                Username = upn,
                ExpirationDate = expirationDate
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Demo provisioning failed at step after [{CompletedSteps}] for request {RequestId}. " +
                "EntraUserId={EntraUserId}, UPN={Upn}, DataverseUserId={DataverseUserId}",
                string.Join(", ", completedSteps),
                request.Id,
                entraUserId,
                upn,
                dataverseSystemUserId);

            throw new DemoProvisioningException(
                $"Provisioning failed for request {request.Id} after completing steps: [{string.Join(", ", completedSteps)}]",
                completedSteps,
                entraUserId,
                upn,
                dataverseSystemUserId,
                ex);
        }
    }

    /// <summary>
    /// Grants the new user a STANDING writer role on the environment's SPE container — the same marked grant
    /// <c>SpeContainerMembershipSyncJob</c> keeps (unified-access-control-r2 task 171, adversarial finding 6). Before
    /// task 171 this POSTed an UNMARKED permission, which nothing could ever tell apart from a hand-granted role, so the
    /// sync never removed it (disabled, moved, flagged external — the role stayed). Marked, it is reconciled like every
    /// other standing writer; granting here (rather than waiting up to five minutes for the job) lets the new user edit
    /// in Office immediately. A user who already holds a role is left as they are (Graph 409 → nothing recorded).
    /// </summary>
    private async Task GrantSpeContainerAccessAsync(
        string containerId, Guid systemUserId, string upn, CancellationToken ct)
    {
        var outcome = await _membership
            .GrantMarkedWriterAsync(containerId, SpeContainerMembershipService.StandingWriterMarkerPrefix, systemUserId, upn, ct)
            .ConfigureAwait(false);

        if (outcome == SpeContainerMembershipService.MarkedGrantOutcome.Failed)
        {
            throw new InvalidOperationException(
                $"The standing writer grant on SPE container '{containerId}' for user {systemUserId} could not be made.");
        }

        _logger.LogInformation(
            "Standing writer on SPE container {ContainerId} for user {SystemUserId}: {Outcome}",
            containerId, systemUserId, outcome);
    }
}

/// <summary>
/// Exception thrown when demo provisioning fails partway through the pipeline.
/// Contains the list of completed steps for diagnostic and recovery purposes.
/// </summary>
public sealed class DemoProvisioningException : Exception
{
    /// <summary>Steps that completed successfully before the failure.</summary>
    public IReadOnlyList<string> CompletedSteps { get; }

    /// <summary>Entra ID user object ID, if the user was created before the failure.</summary>
    public string? EntraUserId { get; }

    /// <summary>Generated UPN, if UPN generation completed before the failure.</summary>
    public string? Upn { get; }

    /// <summary>Dataverse systemuser ID, if the user was synced before the failure.</summary>
    public Guid? DataverseSystemUserId { get; }

    public DemoProvisioningException(
        string message,
        IReadOnlyList<string> completedSteps,
        string? entraUserId,
        string? upn,
        Guid? dataverseSystemUserId,
        Exception innerException)
        : base(message, innerException)
    {
        CompletedSteps = completedSteps;
        EntraUserId = entraUserId;
        Upn = upn;
        DataverseSystemUserId = dataverseSystemUserId;
    }
}
