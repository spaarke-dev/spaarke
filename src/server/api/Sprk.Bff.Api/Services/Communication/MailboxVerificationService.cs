using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Tests send and/or read capabilities for a communication account
/// and persists verification results to Dataverse.
/// Registered as concrete type in AddCommunicationModule() per ADR-010.
/// </summary>
public sealed class MailboxVerificationService
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly IDataverseService _dataverseService;
    private readonly CommunicationAccountService _accountService;
    private readonly ILogger<MailboxVerificationService> _logger;

    public MailboxVerificationService(
        IGraphClientFactory graphClientFactory,
        IDataverseService dataverseService,
        CommunicationAccountService accountService,
        ILogger<MailboxVerificationService> logger)
    {
        _graphClientFactory = graphClientFactory;
        _dataverseService = dataverseService;
        _accountService = accountService;
        _logger = logger;
    }

    /// <summary>
    /// Verifies a communication account's mailbox capabilities.
    /// Tests send (if sprk_sendenableds=true) and read (if sprk_receiveenabled=true),
    /// then updates sprk_verificationstatus and sprk_lastverified on the account record.
    /// </summary>
    /// <param name="accountId">The Dataverse sprk_communicationaccount record ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result with details of which capabilities were tested.</returns>
    public async Task<VerificationResult> VerifyAsync(Guid accountId, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting mailbox verification for account {AccountId}", accountId);

        // 1. Retrieve the account record from Dataverse
        CommunicationAccount? account = null;
        try
        {
            var entity = await _dataverseService.RetrieveAsync(
                "sprk_communicationaccount",
                accountId,
                new[]
                {
                    "sprk_emailaddress", "sprk_displayname", "sprk_name",
                    "sprk_sendenableds", "sprk_receiveenabled",
                    "sprk_accounttype", "sprk_verificationstatus", "sprk_lastverified"
                },
                ct);

            account = MapToAccount(entity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Communication account {AccountId} not found in Dataverse", accountId);
            return null!; // Caller handles null → 404
        }

        if (account is null || string.IsNullOrWhiteSpace(account.EmailAddress))
        {
            _logger.LogWarning("Communication account {AccountId} not found or has no email address", accountId);
            return null!; // Caller handles null → 404
        }

        // 2. Set status to Pending
        await UpdateVerificationStatusAsync(accountId, VerificationStatus.Pending, ct);

        // 3. Test capabilities
        bool? sendVerified = null;
        bool? readVerified = null;
        string? failureReason = null;

        if (account.SendEnabled)
        {
            (sendVerified, var sendError) = await TestSendCapabilityAsync(account.EmailAddress, ct);
            if (sendError is not null)
            {
                failureReason = sendError;
            }
        }

        if (account.ReceiveEnabled)
        {
            (readVerified, var readError) = await TestReadCapabilityAsync(account.EmailAddress, ct);
            if (readError is not null)
            {
                failureReason = failureReason is null
                    ? readError
                    : $"{failureReason}; {readError}";
            }
        }

        // 4. Determine overall status
        var allTestedPassed = true;
        if (sendVerified == false) allTestedPassed = false;
        if (readVerified == false) allTestedPassed = false;

        // If neither capability is enabled, treat as verified (nothing to test)
        var overallStatus = allTestedPassed
            ? VerificationStatus.Verified
            : VerificationStatus.Failed;

        var verifiedAt = DateTimeOffset.UtcNow;

        // 5. Update Dataverse with results
        await UpdateVerificationResultAsync(accountId, overallStatus, verifiedAt, ct);

        _logger.LogInformation(
            "Mailbox verification completed for account {AccountId} ({Email}): {Status} | Send={Send}, Read={Read}",
            accountId, account.EmailAddress, overallStatus, sendVerified, readVerified);

        return new VerificationResult
        {
            AccountId = accountId,
            EmailAddress = account.EmailAddress,
            Status = overallStatus,
            VerifiedAt = verifiedAt,
            SendCapabilityVerified = sendVerified,
            ReadCapabilityVerified = readVerified,
            FailureReason = failureReason
        };
    }

    /// <summary>
    /// Tests send capability by sending a test email to the account's own address.
    /// </summary>
    private async Task<(bool success, string? error)> TestSendCapabilityAsync(string email, CancellationToken ct)
    {
        try
        {
            var graphClient = _graphClientFactory.ForApp();

            var message = new Message
            {
                Subject = "[Spaarke] Mailbox Verification Test",
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = "This is an automated verification test from Spaarke Communication Platform. This message confirms send capability is working. You may delete this email."
                },
                ToRecipients = new List<Recipient>
                {
                    new Recipient
                    {
                        EmailAddress = new EmailAddress { Address = email }
                    }
                }
            };

            await graphClient.Users[email].SendMail.PostAsync(
                new SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = false
                },
                cancellationToken: ct);

            _logger.LogDebug("Send verification succeeded for {Email}", email);
            return (true, null);
        }
        catch (ODataError ex)
        {
            var errorMessage = $"Send test failed: {ex.Error?.Code} - {ex.Error?.Message}";
            _logger.LogWarning(ex, "Send verification failed for {Email}: {Error}", email, errorMessage);
            return (false, errorMessage);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Send test failed: {ex.Message}";
            _logger.LogWarning(ex, "Send verification failed for {Email}: {Error}", email, errorMessage);
            return (false, errorMessage);
        }
    }

    /// <summary>
    /// Tests read capability by querying recent messages (GET /users/{email}/messages?$top=1).
    /// Success means the mailbox is accessible, even if zero messages are returned.
    /// </summary>
    private async Task<(bool success, string? error)> TestReadCapabilityAsync(string email, CancellationToken ct)
    {
        try
        {
            var graphClient = _graphClientFactory.ForApp();

            var messages = await graphClient.Users[email].Messages.GetAsync(
                requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Top = 1;
                    requestConfiguration.QueryParameters.Select = new[] { "id", "subject" };
                },
                cancellationToken: ct);

            _logger.LogDebug(
                "Read verification succeeded for {Email}: {Count} message(s) returned",
                email,
                messages?.Value?.Count ?? 0);

            return (true, null);
        }
        catch (ODataError ex)
        {
            var errorMessage = $"Read test failed: {ex.Error?.Code} - {ex.Error?.Message}";
            _logger.LogWarning(ex, "Read verification failed for {Email}: {Error}", email, errorMessage);
            return (false, errorMessage);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Read test failed: {ex.Message}";
            _logger.LogWarning(ex, "Read verification failed for {Email}: {Error}", email, errorMessage);
            return (false, errorMessage);
        }
    }

    /// <summary>
    /// Sets verification status to Pending at the start of verification.
    /// </summary>
    private async Task UpdateVerificationStatusAsync(Guid accountId, VerificationStatus status, CancellationToken ct)
    {
        try
        {
            var fields = new Dictionary<string, object>
            {
                ["sprk_verificationstatus"] = new OptionSetValue((int)status)
            };

            await _dataverseService.UpdateAsync("sprk_communicationaccount", accountId, fields, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update verification status to {Status} for account {AccountId}", status, accountId);
            // Don't throw — verification should continue even if status update fails
        }
    }

    /// <summary>
    /// Updates the account with final verification status and timestamp.
    /// </summary>
    private async Task UpdateVerificationResultAsync(
        Guid accountId,
        VerificationStatus status,
        DateTimeOffset verifiedAt,
        CancellationToken ct)
    {
        try
        {
            var fields = new Dictionary<string, object>
            {
                ["sprk_verificationstatus"] = new OptionSetValue((int)status),
                ["sprk_lastverified"] = verifiedAt.UtcDateTime
            };

            await _dataverseService.UpdateAsync("sprk_communicationaccount", accountId, fields, ct);

            _logger.LogDebug(
                "Updated verification result for account {AccountId}: Status={Status}, VerifiedAt={VerifiedAt}",
                accountId, status, verifiedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist verification result for account {AccountId}", accountId);
            // Don't throw — we still want to return the result to the caller
        }
    }

    /// <summary>
    /// Maps a Dataverse entity to a CommunicationAccount for verification purposes.
    /// </summary>
    private static CommunicationAccount MapToAccount(Microsoft.Xrm.Sdk.Entity entity)
    {
        return new CommunicationAccount
        {
            Id = entity.Id,
            Name = entity.GetAttributeValue<string>("sprk_name") ?? string.Empty,
            EmailAddress = entity.GetAttributeValue<string>("sprk_emailaddress") ?? string.Empty,
            DisplayName = entity.GetAttributeValue<string>("sprk_displayname"),
            AccountType = entity.Contains("sprk_accounttype")
                ? (AccountType)(entity.GetAttributeValue<OptionSetValue>("sprk_accounttype")?.Value ?? 100000000)
                : AccountType.SharedAccount,
            SendEnabled = entity.GetAttributeValue<bool>("sprk_sendenableds"),
            ReceiveEnabled = entity.GetAttributeValue<bool>("sprk_receiveenabled"),
            VerificationStatus = entity.Contains("sprk_verificationstatus")
                ? (VerificationStatus?)(entity.GetAttributeValue<OptionSetValue>("sprk_verificationstatus")?.Value)
                : null,
            LastVerified = entity.Contains("sprk_lastverified")
                ? entity.GetAttributeValue<DateTime?>("sprk_lastverified")
                : null
        };
    }
}
