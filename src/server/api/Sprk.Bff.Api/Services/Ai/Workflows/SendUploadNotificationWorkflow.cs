using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Workflows;

/// <summary>
/// <b>E-30 proving consumer</b> — a self-contained coded workflow that emails the acting user a
/// NOTIFICATION acknowledging a file they uploaded in the chat session. It exercises the coded-
/// workflow DISPATCH SEAM end-to-end: chat request → <c>ActionKind.Coded</c> →
/// <see cref="ICodedWorkflowRegistry"/> → this workflow → the Email disposition
/// (<c>OutputRouter.DeliverEmailAsync</c>) → auto-send.
/// </summary>
/// <remarks>
/// <para>
/// <b>Self-contained</b> (unlike the Daily Briefing composite, which needs an external collection
/// step): its only inputs are the operand args (<c>fileName</c>) and the acting-user email resolved
/// at the dispatch seam (<see cref="CodedWorkflowContext.ActingUserEmail"/>). No prompted grounding,
/// no session access, no Dataverse, no OBO. This is the shape the future Action Engine's units take.
/// </para>
/// <para>
/// <b>Notification, not correspondence</b> (r2 email-split ruling, 2026-07-09): a system-generated
/// "here's your upload" email is a subscription/notification → the Email disposition AUTO-SENDS with
/// no confirmation gate. Person-facing correspondence stays on the gated <c>email.draft</c> path.
/// The recipient is SERVER-resolved from the caller's claims — never a client-supplied address.
/// </para>
/// <para>
/// <b>Deferred</b>: a clickable link to the file is intentionally omitted — the chat session stores
/// the file name + upload time but not a durable link, and resolving a user-openable SharePoint
/// Embedded URL requires the caller's OBO token (absent from the coded-workflow context). Adding the
/// link is a documented fast-follow (persist the upload <c>WebUrl</c> on <c>ChatSessionFile</c>, or
/// an OBO-safe link facade).
/// </para>
/// </remarks>
public sealed class SendUploadNotificationWorkflow : ICodedWorkflow
{
    /// <inheritdoc />
    public string WorkflowClassRef => nameof(SendUploadNotificationWorkflow);

    /// <inheritdoc />
    public Task<object?> ExecuteAsync(CodedWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var recipient = context.ActingUserEmail;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            // The recipient is resolved server-side from the caller's claims. Its absence is a real
            // failure, not a silent no-op — a notification with no addressee cannot send.
            throw new InvalidOperationException(
                "SendUploadNotificationWorkflow: no acting-user email was resolved at the dispatch seam, " +
                "so the upload notification has no addressee. The email is resolved from the caller's auth " +
                "claims (preferred_username / upn); a missing claim means the notification cannot be sent.");
        }

        // Operand (ADR-039 declared input): the file name to acknowledge, from the dispatch args.
        var fileName = "your file";
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(context.ArgumentsJson) ? "{}" : context.ArgumentsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("fileName", out var fn)
                && fn.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(fn.GetString()))
            {
                fileName = fn.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Malformed args degrade to the generic label — the notification still sends.
        }

        var encodedName = System.Net.WebUtility.HtmlEncode(fileName);

        // The Email disposition reads this envelope shape: { email: { to: string[], subject, htmlBody } }.
        // Storage precedes delivery (ADR-040): the router writes the SessionOutput, then sends.
        object? result = new
        {
            email = new
            {
                to = new[] { recipient },
                subject = "Here's the file you just uploaded",
                htmlBody =
                    $"<p>Your file <strong>{encodedName}</strong> was received in your Spaarke workspace.</p>" +
                    "<p>You can find it in this chat session's uploaded files.</p>",
            },
        };

        return Task.FromResult(result);
    }
}
