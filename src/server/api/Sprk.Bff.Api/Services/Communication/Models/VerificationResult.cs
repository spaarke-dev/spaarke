namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Result of a mailbox verification operation.
/// Contains details about which capabilities were tested and the outcome.
/// </summary>
public sealed class VerificationResult
{
    public required Guid AccountId { get; init; }
    public required string EmailAddress { get; init; }
    public required VerificationStatus Status { get; init; }
    public required DateTimeOffset VerifiedAt { get; init; }
    public bool? SendCapabilityVerified { get; init; }
    public bool? ReadCapabilityVerified { get; init; }
    public string? FailureReason { get; init; }
}
