namespace Sprk.Bff.Api.Models.Registration;

/// <summary>
/// Request body for approving a demo registration request.
/// POST /api/registration/requests/{id}/approve
/// No body required — environment is determined from the Target Environment lookup
/// on the sprk_registrationrequest record.
/// </summary>
[Obsolete("Approve endpoint no longer requires a request body. Environment is read from Dataverse lookup.")]
public record ApproveRequestDto;
