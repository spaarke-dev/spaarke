namespace Sprk.Bff.Api.Models.Registration;

/// <summary>
/// Response after approving a demo registration request.
/// Returned by POST /api/registration/requests/{id}/approve
/// </summary>
public record ApproveResponseDto
{
    public required string Status { get; init; }
    public required string Username { get; init; }
    public required DateTimeOffset ExpirationDate { get; init; }
}
