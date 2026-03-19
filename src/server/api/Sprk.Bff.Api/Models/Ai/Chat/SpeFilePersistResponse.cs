namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Response body for POST /api/ai/chat/sessions/{sessionId}/documents/{documentId}/persist.
///
/// Returned after a user-uploaded document has been persisted to the entity's SPE container
/// via <see cref="Infrastructure.Graph.SpeFileStore"/> (ADR-007). The document also continues
/// to exist in session-scoped Redis for AI context (NFR-06: session storage not deleted on persist).
/// </summary>
/// <param name="SpeFileId">SharePoint Embedded file ID (Graph driveItem ID) of the persisted document.</param>
/// <param name="Filename">Filename used for the persisted file (original or user-overridden).</param>
/// <param name="Url">SharePoint Embedded URL for accessing the persisted file (WebUrl from Graph API).</param>
/// <param name="SizeBytes">Size of the persisted file in bytes.</param>
/// <param name="UploadedAt">UTC timestamp when the file was persisted to SPE.</param>
public record SpeFilePersistResponse(
    string SpeFileId,
    string Filename,
    string Url,
    long SizeBytes,
    DateTimeOffset UploadedAt);

/// <summary>
/// Optional request body for the persist endpoint.
/// All fields are optional — if omitted, the original uploaded filename is used.
/// </summary>
/// <param name="Filename">
/// Optional filename override for the persisted file.
/// If not provided, the original filename from the upload is used.
/// </param>
public record SpeFilePersistRequest(
    string? Filename = null);
