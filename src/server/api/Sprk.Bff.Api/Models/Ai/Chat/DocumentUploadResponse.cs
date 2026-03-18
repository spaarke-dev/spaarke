namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Response body for POST /api/ai/chat/sessions/{sessionId}/documents (202 Accepted).
///
/// Returned after a user-uploaded document has been processed by Document Intelligence
/// and stored in session-scoped Redis. The <see cref="DocumentId"/> is used in subsequent
/// calls to reference the uploaded document.
/// </summary>
/// <param name="DocumentId">Generated GUID identifying the uploaded document within the session.</param>
/// <param name="Filename">Original or user-supplied filename of the uploaded document.</param>
/// <param name="Status">Processing status: "processing" (async, future) or "ready" (synchronous R2).</param>
/// <param name="PageCount">Number of pages detected by Document Intelligence (0 for native text files).</param>
/// <param name="TokenEstimate">Estimated token count of the extracted text (chars / 4).</param>
/// <param name="WasTruncated">True if the extracted text was truncated to fit within token limits.</param>
public record DocumentUploadResponse(
    string DocumentId,
    string Filename,
    string Status,
    int PageCount,
    int TokenEstimate,
    bool WasTruncated);
