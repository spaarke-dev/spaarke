namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Verification status matching Dataverse sprk_verificationstatus choice values.
/// </summary>
public enum VerificationStatus
{
    Verified = 100000000,
    Failed = 100000001,
    Pending = 100000002
}
