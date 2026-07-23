namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Result of creating an ACS chat thread (FR-15). Carries the ACS <c>ChatThreadId</c> that the caller
/// persists as the thread↔channel reference (<c>sprk_communicationchannelref.sprk_externalref</c>,
/// as-built task 004 schema) — written by the W2/W4 caller (020/031), NOT by this service.
/// </summary>
/// <remarks>
/// Server-side model only (ADR-045). Per NFR-04 the <see cref="ChatThreadId"/> is transport addressing
/// held by the BFF; it is NOT an admin capability and NOT a token — it never grants ACS access on its own.
/// The thread is created with a 30-day auto-delete retention policy so ACS never becomes a shadow record
/// store (design §8.7); Dataverse <c>sprk_communication</c> remains the record.
/// </remarks>
public sealed record AcsThreadCreateResult
{
    /// <summary>The ACS <c>ChatThreadId</c> of the newly-created thread.</summary>
    public required string ChatThreadId { get; init; }

    /// <summary>The auto-delete retention window applied at create time (FR-15; 30 days by default).</summary>
    public required int RetentionDays { get; init; }
}
