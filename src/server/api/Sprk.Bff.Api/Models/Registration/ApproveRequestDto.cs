namespace Sprk.Bff.Api.Models.Registration;

/// <summary>
/// Optional request body for approving a demo registration request.
/// POST /api/registration/requests/{id}/approve
/// If no body is provided, the default environment from config is used.
/// </summary>
public record ApproveRequestDto
{
    /// <summary>
    /// Target environment name (e.g., "Demo 1"). If null, uses DefaultEnvironment from config.
    /// Must match a configured environment name in DemoProvisioning:Environments.
    /// </summary>
    public string? Environment { get; init; }
}
