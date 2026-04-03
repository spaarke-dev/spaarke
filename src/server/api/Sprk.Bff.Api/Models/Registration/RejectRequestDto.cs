using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Registration;

/// <summary>
/// DTO for rejecting a demo registration request.
/// POST /api/registration/requests/{id}/reject
/// </summary>
public record RejectRequestDto
{
    [Required]
    [MaxLength(500)]
    public required string Reason { get; init; }
}
