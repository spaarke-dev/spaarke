using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Registration;

/// <summary>
/// DTO for submitting a demo access request from the website form.
/// POST /api/registration/demo-request
/// </summary>
public record DemoRequestDto
{
    [Required]
    [MaxLength(100)]
    public required string FirstName { get; init; }

    [Required]
    [MaxLength(100)]
    public required string LastName { get; init; }

    [Required]
    [MaxLength(200)]
    [EmailAddress]
    public required string Email { get; init; }

    [Required]
    [MaxLength(200)]
    public required string Organization { get; init; }

    [MaxLength(200)]
    public string? JobTitle { get; init; }

    [MaxLength(50)]
    public string? Phone { get; init; }

    [Required]
    public required string UseCase { get; init; }

    public string? ReferralSource { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    [Required]
    public required bool ConsentAccepted { get; init; }

    /// <summary>
    /// reCAPTCHA token from the website form.
    /// </summary>
    [Required]
    public required string RecaptchaToken { get; init; }
}

/// <summary>
/// Response after submitting a demo request.
/// </summary>
public record DemoRequestResponse
{
    public required string TrackingId { get; init; }
    public required string Message { get; init; }
}
