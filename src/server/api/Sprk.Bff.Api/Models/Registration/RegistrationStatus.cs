namespace Sprk.Bff.Api.Models.Registration;

/// <summary>
/// Status values for a demo registration request lifecycle.
/// Maps to sprk_status choice column on sprk_registrationrequest entity.
/// </summary>
public enum RegistrationStatus
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2,
    Provisioned = 3,
    Expired = 4,
    Revoked = 5
}
