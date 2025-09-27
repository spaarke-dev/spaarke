using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Models;

public sealed class CreateContainerRequest
{
    [Required]
    public required string DisplayName { get; set; }

    public string? Description { get; set; }

    [Required]
    public required Guid ContainerTypeId { get; set; }
}

public sealed class CreateUploadSessionRequest
{
    [Required]
    public required string FileName { get; set; }

    [Required]
    [Range(1, long.MaxValue)]
    public required long FileSize { get; set; }

    public string? ConflictBehavior { get; set; } = "replace";
}