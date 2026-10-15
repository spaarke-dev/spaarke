namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// The outcome of <see cref="CommunicationService.PostToRecordThreadAsync"/> (spaarke-SPA-external-access-platform-r3
/// task 034, FR-11): the one <c>sprk_communication</c> row the post created, and the values it was written with — so the
/// route can answer 201 with the message without a read-back.
/// </summary>
/// <param name="CommunicationId">The created <c>sprk_communication</c>.</param>
/// <param name="PostedAt">When the row was prepared (UTC); Dataverse stamps <c>createdon</c> on create.</param>
/// <param name="Subject">The subject written (posts carry none from the caller).</param>
/// <param name="Body">The body written, verbatim, as plain text.</param>
/// <param name="BodyFormat">Always <see cref="Models.BodyFormat.PlainText"/>.</param>
/// <param name="Direction">Incoming for a contact author, Outgoing for a systemuser author.</param>
/// <param name="ThreadId">The thread the post joined, or null when thread assignment (best-effort) did not resolve one.</param>
public sealed record ThreadPostResult(
    Guid CommunicationId,
    DateTimeOffset PostedAt,
    string Subject,
    string Body,
    BodyFormat BodyFormat,
    CommunicationDirection Direction,
    Guid? ThreadId);
