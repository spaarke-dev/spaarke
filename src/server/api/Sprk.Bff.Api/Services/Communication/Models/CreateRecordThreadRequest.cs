using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Request DTO for POST /api/communications/threads (R3 UAT 2026-07-23 item 9) — create a NEW named,
/// record-anchored thread. Unlike POST /threads/direct (participant-based 1:1), this creates an
/// Open/record-anchored thread keyed on an ADR-024 regarding record (no participant). The caller is
/// resolved server-side and becomes the thread owner.
/// </summary>
public sealed record CreateRecordThreadRequest
{
    /// <summary>
    /// Optional thread name. When blank, the name derives from <see cref="RegardingRecordName"/> (or a
    /// generic fallback). A provided name is stamped Edited so the auto re-derive never overwrites it.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The regarding record's logical entity name (one of the 11 ADR-024 families, e.g. <c>sprk_matter</c>).
    /// The client picks it via the wizard-associate record picker; stored on the denormalized
    /// <c>sprk_regardingrecordtype</c> pointer.
    /// </summary>
    [Required]
    public required string RegardingEntityType { get; init; }

    /// <summary>The regarding record's id (stored on the denormalized <c>sprk_regardingrecordid</c> pointer).</summary>
    [Required]
    public required Guid RegardingRecordId { get; init; }

    /// <summary>Optional regarding record display name (stored on <c>sprk_regardingrecordname</c> + used for name derivation).</summary>
    public string? RegardingRecordName { get; init; }
}
