namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Response model for POST /api/ai/chat/export/word.
/// Returned after DOCX generation and SPE upload completes successfully.
/// </summary>
/// <param name="SpeFileId">The SPE (SharePoint Embedded) file ID for the uploaded document.</param>
/// <param name="Filename">The filename of the uploaded document.</param>
/// <param name="WordOnlineUrl">
/// URL to open the document in Word Online.
/// Format: https://{tenant}.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc={speFileId}&amp;action=default
/// </param>
/// <param name="SizeBytes">Size of the generated DOCX file in bytes.</param>
/// <param name="GeneratedAt">UTC timestamp when the document was generated.</param>
public sealed record WordExportResponse(
    string SpeFileId,
    string Filename,
    string WordOnlineUrl,
    long SizeBytes,
    DateTimeOffset GeneratedAt);
