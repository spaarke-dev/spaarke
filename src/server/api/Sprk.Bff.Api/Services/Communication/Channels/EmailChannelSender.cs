using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Channels;

/// <summary>
/// Email implementation of <see cref="ICommunicationChannelSender"/> (ADR-045 rule 4). The ONLY R4
/// channel sender. Owns every Microsoft-Graph-specific concern of an email send — building the Graph
/// <see cref="Message"/>, resolving the app-only vs OBO client, calling <c>sendMail</c>, mapping Graph
/// errors to <see cref="SdapProblemException"/>, and best-effort capture of the sent message's
/// Internet-Message-Id — so <see cref="CommunicationService"/> is free of provider types (NFR-04).
///
/// This is a thin relocation of the send path that previously lived inline in
/// <see cref="CommunicationService"/>; email behavior is unchanged.
/// </summary>
public sealed class EmailChannelSender : ICommunicationChannelSender
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<EmailChannelSender> _logger;

    public EmailChannelSender(
        IGraphClientFactory graphClientFactory,
        ILogger<EmailChannelSender> logger)
    {
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }

    public CommunicationType SupportedType => CommunicationType.Email;

    public async Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
    {
        var message = BuildGraphMessage(request);

        if (request.Attachments is { Count: > 0 })
        {
            message.Attachments = request.Attachments
                .Select(a => (Attachment)new FileAttachment
                {
                    OdataType = "#microsoft.graph.fileAttachment",
                    Name = a.Name,
                    ContentType = a.ContentType,
                    ContentBytes = a.Content
                })
                .ToList();
        }

        GraphServiceClient graphClient;
        try
        {
            if (request.UserMode)
            {
                graphClient = await _graphClientFactory.ForUserAsync(request.HttpContext!, cancellationToken);
                await graphClient.Me.SendMail.PostAsync(
                    new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
                    {
                        Message = message,
                        SaveToSentItems = true
                    },
                    cancellationToken: cancellationToken);
            }
            else
            {
                graphClient = _graphClientFactory.ForApp();
                await graphClient.Users[request.FromAddress].SendMail.PostAsync(
                    new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                    {
                        Message = message,
                        SaveToSentItems = true
                    },
                    cancellationToken: cancellationToken);
            }

            _logger.LogInformation(
                "Communication sent successfully | CorrelationId: {CorrelationId}, From: {From}, To: {RecipientCount}, UserMode: {UserMode}",
                request.CorrelationId,
                request.FromAddress,
                request.Communication.To.Length,
                request.UserMode);
        }
        catch (ODataError ex)
        {
            _logger.LogError(
                ex,
                "Graph sendMail failed | CorrelationId: {CorrelationId}, StatusCode: {StatusCode}, ErrorCode: {ErrorCode}, UserMode: {UserMode}",
                request.CorrelationId,
                ex.ResponseStatusCode,
                ex.Error?.Code,
                request.UserMode);

            var extensions = new Dictionary<string, object>
            {
                ["correlationId"] = request.CorrelationId,
                ["graphErrorCode"] = ex.Error?.Code ?? "unknown"
            };
            if (request.UserMode) extensions["sendMode"] = "User";

            throw new SdapProblemException(
                code: "GRAPH_SEND_FAILED",
                title: "Email Send Failed",
                detail: request.UserMode
                    ? $"Graph API error (OBO): {ex.Error?.Message ?? ex.Message}"
                    : $"Graph API error: {ex.Error?.Message ?? ex.Message}",
                statusCode: ex.ResponseStatusCode > 0 ? ex.ResponseStatusCode : 502,
                extensions: extensions);
        }
        catch (Exception ex) when (ex is not SdapProblemException)
        {
            _logger.LogError(
                ex,
                "Unexpected error sending communication | CorrelationId: {CorrelationId}, UserMode: {UserMode}",
                request.CorrelationId,
                request.UserMode);

            var extensions = new Dictionary<string, object>
            {
                ["correlationId"] = request.CorrelationId
            };
            if (request.UserMode) extensions["sendMode"] = "User";

            throw new SdapProblemException(
                code: "GRAPH_SEND_FAILED",
                title: "Email Send Failed",
                detail: request.UserMode
                    ? $"Unexpected error (OBO): {ex.Message}"
                    : $"Unexpected error: {ex.Message}",
                statusCode: 500,
                extensions: extensions);
        }

        // Best-effort capture of the just-sent message's real RFC-2822 Internet-Message-Id (FR-06 —
        // feeds the W1 thread rung). MUST NOT fail the send (NFR-06); returns null on any error.
        var internetMessageId = await TryCaptureInternetMessageIdAsync(
            graphClient, request.UserMode, request.FromAddress, request.Communication.Subject,
            request.CorrelationId, cancellationToken);

        return new ChannelSendResult
        {
            FromAddress = request.FromAddress,
            ProviderMessageId = internetMessageId
        };
    }

    private static Message BuildGraphMessage(ChannelSendRequest request)
    {
        var req = request.Communication;
        var message = new Message
        {
            Subject = req.Subject,
            Body = new ItemBody
            {
                ContentType = req.BodyFormat == BodyFormat.PlainText ? BodyType.Text : BodyType.Html,
                Content = req.Body
            },
            ToRecipients = req.To.Select(email => new Recipient
            {
                EmailAddress = new EmailAddress { Address = email }
            }).ToList(),
            CcRecipients = (req.Cc ?? Array.Empty<string>()).Select(email => new Recipient
            {
                EmailAddress = new EmailAddress { Address = email }
            }).ToList(),
            BccRecipients = (req.Bcc ?? Array.Empty<string>()).Select(email => new Recipient
            {
                EmailAddress = new EmailAddress { Address = email }
            }).ToList()
        };

        // Shared-mailbox mode addresses the send from the approved sender; user (OBO) mode sends as
        // the authenticated user via /me and MUST NOT set an explicit From (preserves prior behavior).
        if (!request.UserMode)
        {
            message.From = new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = request.FromAddress,
                    Name = request.FromDisplayName
                }
            };
        }

        return message;
    }

    /// <summary>
    /// Locates the just-sent message in Sent Items (by subject, newest first) and returns its
    /// Internet-Message-Id. Best-effort — any failure is logged and swallowed, returning null.
    /// (Relocated verbatim from CommunicationService.TryStampInternetMessageIdAsync; the Dataverse
    /// stamp itself remains in CommunicationService as channel-agnostic persistence.)
    /// </summary>
    private async Task<string?> TryCaptureInternetMessageIdAsync(
        GraphServiceClient graphClient,
        bool userMode,
        string senderEmail,
        string subject,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            var safeSubject = subject.Replace("'", "''");
            List<Message>? messages;
            if (userMode)
            {
                var page = await graphClient.Me.MailFolders["sentitems"].Messages.GetAsync(rc =>
                {
                    rc.QueryParameters.Filter = $"subject eq '{safeSubject}'";
                    rc.QueryParameters.Orderby = new[] { "sentDateTime desc" };
                    rc.QueryParameters.Select = new[] { "internetMessageId", "sentDateTime" };
                    rc.QueryParameters.Top = 3;
                }, ct);
                messages = page?.Value;
            }
            else
            {
                var page = await graphClient.Users[senderEmail].MailFolders["sentitems"].Messages.GetAsync(rc =>
                {
                    rc.QueryParameters.Filter = $"subject eq '{safeSubject}'";
                    rc.QueryParameters.Orderby = new[] { "sentDateTime desc" };
                    rc.QueryParameters.Select = new[] { "internetMessageId", "sentDateTime" };
                    rc.QueryParameters.Top = 3;
                }, ct);
                messages = page?.Value;
            }

            var internetMessageId = messages?.FirstOrDefault()?.InternetMessageId;
            if (string.IsNullOrWhiteSpace(internetMessageId))
            {
                _logger.LogWarning(
                    "Internet-Message-Id not found in Sent Items (subject match); reply-thread closure may be impaired | CorrelationId: {CorrelationId}",
                    correlationId);
                return null;
            }

            return internetMessageId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Internet-Message-Id capture failed (non-fatal) | CorrelationId: {CorrelationId}",
                correlationId);
            return null;
        }
    }
}
