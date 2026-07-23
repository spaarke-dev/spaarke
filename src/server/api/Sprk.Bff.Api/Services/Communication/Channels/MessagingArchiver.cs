using System.Text;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Channels;

/// <summary>
/// Messaging (ACS Chat) implementation of <see cref="ICommunicationArchiver"/> (ADR-045 rule 4 / NFR-04) —
/// the SECOND channel behind the shipped archiver seam. The chat-transcript analog of
/// <see cref="EmailArchiver"/>'s <c>.eml</c>: it emits a portable message-transcript artifact
/// (<see cref="EmlResult"/> bytes + filename) that the pipeline archives to SPE via
/// <c>SpeFileStore</c> (ADR-007) through the exact same <see cref="ICommunicationArchiver"/> →
/// <c>CommunicationService.ArchiveToSpeAsync</c> flow as email — no new storage path is introduced.
/// </summary>
/// <remarks>
/// The interface contract permits each channel its own portable transcript format; a chat send has no
/// RFC-2822 envelope, so this produces a plain-text transcript rather than a MIME <c>.eml</c>. Archival is
/// best-effort/non-fatal at the caller (a transcript failure must not fail the send — NFR-02); this method
/// is pure formatting and does not throw on normal input.
/// </remarks>
public sealed class MessagingArchiver : ICommunicationArchiver
{
    public CommunicationType SupportedType => CommunicationType.Message;

    public EmlResult GenerateEml(
        SendCommunicationRequest request,
        SendCommunicationResponse response,
        IReadOnlyList<EmlAttachment>? attachments = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Spaarke Message Transcript");
        sb.AppendLine("==========================");
        sb.AppendLine($"From: {response.From}");
        sb.AppendLine($"To: {string.Join("; ", request.To)}");
        if (request.Cc is { Length: > 0 })
            sb.AppendLine($"Cc: {string.Join("; ", request.Cc)}");
        sb.AppendLine($"Subject: {request.Subject}");
        sb.AppendLine($"Sent: {response.SentAt:u}");

        // The provider message id (ACS message id — the echo-dedup key) flows through the response's
        // provider-id field; record it in the transcript so the archived artifact carries the dedup key.
        if (!string.IsNullOrWhiteSpace(response.GraphMessageId))
            sb.AppendLine($"Message-Id: {response.GraphMessageId}");
        if (!string.IsNullOrWhiteSpace(response.CorrelationId))
            sb.AppendLine($"Correlation-Id: {response.CorrelationId}");
        if (attachments is { Count: > 0 })
            sb.AppendLine($"Attachments: {string.Join("; ", attachments.Select(a => a.FileName))}");

        sb.AppendLine();
        sb.AppendLine(request.Body);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());

        var sanitizedSubject = SanitizeFileName(request.Subject);
        var dateStr = response.SentAt.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{sanitizedSubject}_{dateStr}_transcript.txt";

        return new EmlResult(bytes, fileName);
    }

    // Windows-strict invalid-filename set (mirrors EmlGenerationService): .txt transcripts are routinely
    // opened on Windows clients, so strip the Windows-reserved characters regardless of the host OS.
    private static readonly HashSet<char> s_invalidFileNameChars = new(
        Path.GetInvalidFileNameChars()
            .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }));

    private static string SanitizeFileName(string input)
    {
        var sanitized = new string((input ?? string.Empty).Where(c => !s_invalidFileNameChars.Contains(c)).ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "message";
        return sanitized.Length > 50 ? sanitized[..50] : sanitized;
    }
}
