namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Result of approved sender validation.
/// </summary>
public sealed record ApprovedSenderResult
{
    /// <summary>
    /// Whether the sender validation succeeded.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Resolved sender email address (set when IsValid is true).
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Resolved sender display name (set when IsValid is true).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Error code when IsValid is false. Values: INVALID_SENDER, NO_DEFAULT_SENDER.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Human-readable error detail when IsValid is false.
    /// </summary>
    public string? ErrorDetail { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ApprovedSenderResult Valid(string email, string displayName) => new()
    {
        IsValid = true,
        Email = email,
        DisplayName = displayName
    };

    /// <summary>
    /// Creates a failure result for an invalid sender.
    /// </summary>
    public static ApprovedSenderResult InvalidSender(string requestedMailbox) => new()
    {
        IsValid = false,
        ErrorCode = "INVALID_SENDER",
        ErrorDetail = $"The mailbox '{requestedMailbox}' is not in the approved senders list."
    };

    /// <summary>
    /// Creates a failure result when no default sender is configured.
    /// </summary>
    public static ApprovedSenderResult NoDefaultSender() => new()
    {
        IsValid = false,
        ErrorCode = "NO_DEFAULT_SENDER",
        ErrorDetail = "No default sender is configured and no fromMailbox was specified."
    };
}
