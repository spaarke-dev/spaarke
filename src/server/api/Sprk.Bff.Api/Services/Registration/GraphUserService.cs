using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.AssignLicense;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Manages Entra ID user lifecycle for demo provisioning:
/// creates internal users, checks UPN availability with collision handling,
/// assigns licenses, manages security group membership, and disables accounts.
/// Uses GraphClientFactory.ForApp() for app-only tokens (ADR-004: service principal auth).
/// Registered as concrete type per ADR-010.
/// </summary>
public sealed class GraphUserService
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly PasswordGenerator _passwordGenerator;
    private readonly DemoProvisioningOptions _options;
    private readonly ILogger<GraphUserService> _logger;

    public GraphUserService(
        IGraphClientFactory graphClientFactory,
        PasswordGenerator passwordGenerator,
        IOptions<DemoProvisioningOptions> options,
        ILogger<GraphUserService> logger)
    {
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _passwordGenerator = passwordGenerator ?? throw new ArgumentNullException(nameof(passwordGenerator));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new Entra ID user account for a demo registrant.
    /// The UPN is generated from first/last name with collision handling.
    /// The user is created with forceChangePasswordNextSignIn = true and usageLocation = "US".
    /// </summary>
    /// <param name="firstName">User's first name.</param>
    /// <param name="lastName">User's last name.</param>
    /// <param name="companyName">User's company name (stored in profile).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple containing the created user's object ID, the generated UPN, and the temporary password.
    /// </returns>
    public async Task<(string UserId, string Upn, string TemporaryPassword)> CreateUserAsync(
        string firstName,
        string lastName,
        string companyName,
        CancellationToken ct = default)
    {
        var graphClient = _graphClientFactory.ForApp();

        // Generate a unique UPN with collision handling
        var upn = await GenerateUniqueUPNAsync(firstName, lastName, ct);
        var temporaryPassword = _passwordGenerator.Generate();

        _logger.LogInformation(
            "Creating Entra ID user: {Upn} for {FirstName} {LastName} ({Company})",
            upn, firstName, lastName, companyName);

        var user = new User
        {
            AccountEnabled = true,
            DisplayName = $"{firstName} {lastName}",
            GivenName = firstName,
            Surname = lastName,
            CompanyName = companyName,
            MailNickname = $"{SanitizeName(firstName)}.{SanitizeName(lastName)}",
            UserPrincipalName = upn,
            UsageLocation = "US",
            PasswordProfile = new PasswordProfile
            {
                ForceChangePasswordNextSignIn = true,
                Password = temporaryPassword
            }
        };

        var createdUser = await graphClient.Users.PostAsync(user, cancellationToken: ct);
        var userId = createdUser?.Id
            ?? throw new InvalidOperationException($"Graph API returned null user ID after creating {upn}");

        _logger.LogInformation("Created Entra ID user {UserId} with UPN {Upn}", userId, upn);

        return (userId, upn, temporaryPassword);
    }

    /// <summary>
    /// Checks whether the given UPN is available (not already assigned to a user).
    /// </summary>
    /// <param name="upn">The user principal name to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the UPN is available; false if it is already in use.</returns>
    public async Task<bool> CheckUPNAvailableAsync(string upn, CancellationToken ct = default)
    {
        var graphClient = _graphClientFactory.ForApp();

        try
        {
            // Filter users by UPN — returns empty collection if not found
            var result = await graphClient.Users.GetAsync(
                config =>
                {
                    config.QueryParameters.Filter = $"userPrincipalName eq '{upn}'";
                    config.QueryParameters.Select = new[] { "id" };
                    config.QueryParameters.Top = 1;
                },
                cancellationToken: ct);

            var isAvailable = result?.Value == null || result.Value.Count == 0;

            _logger.LogDebug("UPN availability check: {Upn} -> available={IsAvailable}", upn, isAvailable);

            return isAvailable;
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "Failed to check UPN availability for {Upn}", upn);
            throw;
        }
    }

    /// <summary>
    /// Generates a unique UPN in the format firstname.lastname@{AccountDomain}.
    /// If the base UPN is taken, appends an incrementing number (e.g., firstname.lastname2@domain).
    /// </summary>
    /// <param name="firstName">User's first name.</param>
    /// <param name="lastName">User's last name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A unique, available UPN.</returns>
    public async Task<string> GenerateUniqueUPNAsync(
        string firstName,
        string lastName,
        CancellationToken ct = default)
    {
        var sanitizedFirst = SanitizeName(firstName);
        var sanitizedLast = SanitizeName(lastName);
        var domain = _options.AccountDomain;

        // Try base UPN first: firstname.lastname@domain
        var baseUpn = $"{sanitizedFirst}.{sanitizedLast}@{domain}";
        if (await CheckUPNAvailableAsync(baseUpn, ct))
        {
            _logger.LogDebug("Base UPN {Upn} is available", baseUpn);
            return baseUpn;
        }

        // Collision: append incrementing number
        const int maxAttempts = 100;
        for (var i = 2; i <= maxAttempts + 1; i++)
        {
            var candidateUpn = $"{sanitizedFirst}.{sanitizedLast}{i}@{domain}";
            if (await CheckUPNAvailableAsync(candidateUpn, ct))
            {
                _logger.LogInformation(
                    "UPN collision resolved: {BaseUpn} taken, using {ResolvedUpn}",
                    baseUpn, candidateUpn);
                return candidateUpn;
            }
        }

        throw new InvalidOperationException(
            $"Unable to generate unique UPN for {firstName} {lastName} after {maxAttempts} attempts");
    }

    /// <summary>
    /// Assigns the configured demo licenses to a user.
    /// Idempotent: skips licenses already assigned.
    /// SKU IDs are sourced from DemoProvisioningOptions.Licenses.
    /// </summary>
    /// <param name="userId">Entra ID user object ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task AssignLicensesAsync(string userId, CancellationToken ct = default)
    {
        var graphClient = _graphClientFactory.ForApp();
        var licenses = _options.Licenses;

        var skuIds = new[]
        {
            licenses.PowerAppsPlan2TrialSkuId,
            licenses.FabricFreeSkuId,
            licenses.PowerAutomateFreeSkuId
        };

        // Check which licenses are already assigned (idempotent)
        var existingLicenses = await GetAssignedLicenseSkuIdsAsync(graphClient, userId, ct);
        var licensesToAssign = skuIds
            .Where(sku => !string.IsNullOrWhiteSpace(sku))
            .Where(sku => !existingLicenses.Contains(sku, StringComparer.OrdinalIgnoreCase))
            .Select(sku => new AssignedLicense { SkuId = Guid.Parse(sku) })
            .ToList();

        if (licensesToAssign.Count == 0)
        {
            _logger.LogInformation("All licenses already assigned to user {UserId}", userId);
            return;
        }

        _logger.LogInformation(
            "Assigning {Count} license(s) to user {UserId}: {SkuIds}",
            licensesToAssign.Count,
            userId,
            string.Join(", ", licensesToAssign.Select(l => l.SkuId)));

        await graphClient.Users[userId].AssignLicense.PostAsync(
            new AssignLicensePostRequestBody
            {
                AddLicenses = licensesToAssign,
                RemoveLicenses = new List<Guid?>()
            },
            cancellationToken: ct);

        _logger.LogInformation("Successfully assigned licenses to user {UserId}", userId);
    }

    /// <summary>
    /// Adds a user to the demo users security group.
    /// Idempotent: no-op if user is already a member.
    /// </summary>
    /// <param name="userId">Entra ID user object ID.</param>
    /// <param name="groupId">Security group object ID. Defaults to DemoUsersGroupId from config.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task AddToGroupAsync(
        string userId,
        string? groupId = null,
        CancellationToken ct = default)
    {
        var graphClient = _graphClientFactory.ForApp();
        var targetGroupId = groupId ?? _options.DemoUsersGroupId;

        // Idempotent: check if user is already a member
        if (await IsGroupMemberAsync(graphClient, targetGroupId, userId, ct))
        {
            _logger.LogInformation(
                "User {UserId} is already a member of group {GroupId}", userId, targetGroupId);
            return;
        }

        _logger.LogInformation("Adding user {UserId} to group {GroupId}", userId, targetGroupId);

        var directoryObject = new ReferenceCreate
        {
            OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{userId}"
        };

        await graphClient.Groups[targetGroupId].Members.Ref.PostAsync(
            directoryObject,
            cancellationToken: ct);

        _logger.LogInformation("Successfully added user {UserId} to group {GroupId}", userId, targetGroupId);
    }

    /// <summary>
    /// Removes a user from a security group.
    /// Idempotent: no-op if user is not a member.
    /// </summary>
    /// <param name="userId">Entra ID user object ID.</param>
    /// <param name="groupId">Security group object ID. Defaults to DemoUsersGroupId from config.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RemoveFromGroupAsync(
        string userId,
        string? groupId = null,
        CancellationToken ct = default)
    {
        var graphClient = _graphClientFactory.ForApp();
        var targetGroupId = groupId ?? _options.DemoUsersGroupId;

        // Idempotent: check if user is actually a member
        if (!await IsGroupMemberAsync(graphClient, targetGroupId, userId, ct))
        {
            _logger.LogInformation(
                "User {UserId} is not a member of group {GroupId}, nothing to remove",
                userId, targetGroupId);
            return;
        }

        _logger.LogInformation("Removing user {UserId} from group {GroupId}", userId, targetGroupId);

        await graphClient.Groups[targetGroupId].Members[userId].Ref.DeleteAsync(
            cancellationToken: ct);

        _logger.LogInformation(
            "Successfully removed user {UserId} from group {GroupId}", userId, targetGroupId);
    }

    /// <summary>
    /// Disables a user account (sets accountEnabled = false).
    /// Does NOT delete the account — expired accounts are disabled per policy.
    /// Idempotent: no-op if user is already disabled.
    /// </summary>
    /// <param name="userId">Entra ID user object ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DisableUserAsync(string userId, CancellationToken ct = default)
    {
        var graphClient = _graphClientFactory.ForApp();

        // Idempotent: check current state
        var existingUser = await graphClient.Users[userId].GetAsync(
            config => config.QueryParameters.Select = new[] { "id", "accountEnabled" },
            cancellationToken: ct);

        if (existingUser?.AccountEnabled == false)
        {
            _logger.LogInformation("User {UserId} is already disabled", userId);
            return;
        }

        _logger.LogInformation("Disabling user account {UserId}", userId);

        await graphClient.Users[userId].PatchAsync(
            new User { AccountEnabled = false },
            cancellationToken: ct);

        _logger.LogInformation("Successfully disabled user account {UserId}", userId);
    }

    /// <summary>
    /// Sanitizes a name for use in a UPN by lowercasing and removing non-alphanumeric characters.
    /// </summary>
    private static string SanitizeName(string name)
    {
        var sanitized = new string(name
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "user" : sanitized;
    }

    /// <summary>
    /// Gets the set of license SKU IDs already assigned to a user.
    /// </summary>
    private static async Task<HashSet<string>> GetAssignedLicenseSkuIdsAsync(
        GraphServiceClient graphClient,
        string userId,
        CancellationToken ct)
    {
        var user = await graphClient.Users[userId].GetAsync(
            config => config.QueryParameters.Select = new[] { "assignedLicenses" },
            cancellationToken: ct);

        var skuIds = user?.AssignedLicenses?
            .Where(l => l.SkuId.HasValue)
            .Select(l => l.SkuId!.Value.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return skuIds;
    }

    /// <summary>
    /// Checks whether a user is a member of a specific group.
    /// </summary>
    private static async Task<bool> IsGroupMemberAsync(
        GraphServiceClient graphClient,
        string groupId,
        string userId,
        CancellationToken ct)
    {
        try
        {
            var members = await graphClient.Groups[groupId].Members.GetAsync(
                config =>
                {
                    config.QueryParameters.Filter = $"id eq '{userId}'";
                    config.QueryParameters.Select = new[] { "id" };
                    config.QueryParameters.Top = 1;
                },
                cancellationToken: ct);

            return members?.Value != null && members.Value.Count > 0;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            // Group not found — treat as "not a member"
            return false;
        }
    }
}
