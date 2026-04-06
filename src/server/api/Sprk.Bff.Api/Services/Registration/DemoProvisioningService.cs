using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Configuration;
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
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly DemoProvisioningOptions _options;
    private readonly ILogger<DemoProvisioningService> _logger;

    public DemoProvisioningService(
        GraphUserService graphUserService,
        RegistrationDataverseService dataverseService,
        RegistrationEmailService emailService,
        PasswordGenerator passwordGenerator,
        IGraphClientFactory graphClientFactory,
        IOptions<DemoProvisioningOptions> options,
        ILogger<DemoProvisioningService> logger)
    {
        _graphUserService = graphUserService ?? throw new ArgumentNullException(nameof(graphUserService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _passwordGenerator = passwordGenerator ?? throw new ArgumentNullException(nameof(passwordGenerator));
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
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
            await _graphUserService.AssignLicensesAsync(entraUserId, ct);
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
                ct);
            completedSteps.Add("CreateSystemUser");
            _logger.LogInformation("[Step 6/9] Created systemuser {SystemUserId}", dataverseSystemUserId);

            // ── Step 7: Add to Demo Team ──
            _logger.LogInformation("[Step 7/9] Adding systemuser {SystemUserId} to team {TeamName}",
                dataverseSystemUserId, environment.TeamName);
            await _dataverseService.AddUserToTeamAsync(
                environment.TeamName, dataverseSystemUserId.Value, ct);
            completedSteps.Add("AddToTeam");
            _logger.LogInformation("[Step 7/9] Added to team {TeamName}", environment.TeamName);

            // ── Step 8: Grant SPE container Writer access ──
            // SPE container access is optional — skip gracefully if container ID is a placeholder or grant fails
            try
            {
                _logger.LogInformation("[Step 8/9] Granting Writer access on SPE container {ContainerId} to user {UserId}",
                    environment.SpeContainerId, entraUserId);
                await GrantSpeContainerAccessAsync(environment.SpeContainerId, entraUserId, upn, ct);
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
    /// Grants Writer access on an SPE container to the specified user via Graph API.
    /// Uses the same Graph SDK pattern as SpeAdminGraphService.GrantContainerPermissionAsync.
    /// POST /storage/fileStorage/containers/{containerId}/permissions
    /// </summary>
    private async Task GrantSpeContainerAccessAsync(
        string containerId, string userId, string upn, CancellationToken ct)
    {
        var graphClient = _graphClientFactory.ForApp();

        var permissionRequest = new Permission
        {
            Roles = new List<string> { "writer" },
            GrantedToV2 = new SharePointIdentitySet
            {
                User = new SharePointIdentity
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["userPrincipalName"] = upn
                    }
                }
            }
        };

        var created = await graphClient.Storage.FileStorage.Containers[containerId].Permissions
            .PostAsync(permissionRequest, cancellationToken: ct);

        if (created is null)
        {
            throw new InvalidOperationException(
                $"Graph returned null when granting Writer permission on SPE container '{containerId}' for user '{userId}'.");
        }

        _logger.LogInformation(
            "Granted Writer permission on SPE container {ContainerId} for user {UserId}, permissionId={PermissionId}",
            containerId, userId, created.Id);
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
