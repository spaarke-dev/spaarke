using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Http;
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

    /// <summary>
    /// Ontology task 191 (D-223, #2779): the <c>GRAPH_SEND_FAILED</c> extension that says what is known about a failed send:
    /// <see cref="SendOutcomeNotSent"/> or <see cref="SendOutcomeUnknown"/>.
    /// </summary>
    internal const string SendOutcomeExtension = "sendOutcome";

    /// <summary>The email provably did not go out: the failure came before the request left (no token, an open circuit, a
    /// refused connect), or the last attempt got a settled refusal (a 4xx other than 408, a 429 included: Graph did not
    /// process it).</summary>
    internal const string SendOutcomeNotSent = "NotSent";

    /// <summary>The email may have gone out: the request left and got no answer (a timeout, a transport fault, a cancellation)
    /// or an answer Graph may send after accepting it (a 5xx, a 408).</summary>
    internal const string SendOutcomeUnknown = "Unknown";

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

        // Task 185 (owner round 98 Q4): a system-mail send (an invite link with its one-time code) keeps no copy in the
        // sending mailbox's Sent Items. Every other send is unchanged (true).
        var saveToSentItems = request.Communication.SystemMail is null;

        // Ontology task 191 (D-223, #2779): sendMail is not idempotent, so the Graph handler must not send it again after a 5xx,
        // a 408 or a transport fault (Graph may already have delivered it): only after a 429, which Graph did not process. The
        // handler records on the option whether an attempt left and what it got, which is how a failure is classified below
        // (the container create's pattern, ContainerOperations.CreateContainerAsync).
        var sendOption = new NoTransientRetryOption();

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
                        SaveToSentItems = saveToSentItems
                    },
                    rc => rc.Options.Add(sendOption),
                    cancellationToken);
            }
            else
            {
                graphClient = _graphClientFactory.ForApp();
                await graphClient.Users[request.FromAddress].SendMail.PostAsync(
                    new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                    {
                        Message = message,
                        SaveToSentItems = saveToSentItems
                    },
                    rc => rc.Options.Add(sendOption),
                    cancellationToken);
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
            var outcome = OutcomeOf(sendOption, ex);
            _logger.LogError(
                ex,
                "Graph sendMail failed | CorrelationId: {CorrelationId}, StatusCode: {StatusCode}, ErrorCode: {ErrorCode}, UserMode: {UserMode}, SendOutcome: {SendOutcome}",
                request.CorrelationId,
                ex.ResponseStatusCode,
                ex.Error?.Code,
                request.UserMode,
                outcome);

            var extensions = new Dictionary<string, object>
            {
                ["correlationId"] = request.CorrelationId,
                ["graphErrorCode"] = ex.Error?.Code ?? "unknown",
                [SendOutcomeExtension] = outcome
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
            var outcome = OutcomeOf(sendOption, ex);
            _logger.LogError(
                ex,
                "Unexpected error sending communication | CorrelationId: {CorrelationId}, UserMode: {UserMode}, SendOutcome: {SendOutcome}",
                request.CorrelationId,
                request.UserMode,
                outcome);

            var extensions = new Dictionary<string, object>
            {
                ["correlationId"] = request.CorrelationId,
                [SendOutcomeExtension] = outcome
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
        // A message that was not saved to Sent Items cannot be found there, so a system-mail send skips the lookup (no
        // Sent Items read, no "not found" warning on every invite).
        var internetMessageId = saveToSentItems
            ? await TryCaptureInternetMessageIdAsync(
                graphClient, request.UserMode, request.FromAddress, request.Communication.Subject,
                request.CorrelationId, cancellationToken)
            : null;

        return new ChannelSendResult
        {
            FromAddress = request.FromAddress,
            ProviderMessageId = internetMessageId
        };
    }

    /// <summary>
    /// Ontology task 191 (D-223, #2779): <see cref="SendOutcomeUnknown"/> when an attempt left and its answer did not settle that
    /// Graph refused it (the handler's record on <paramref name="option"/>), or when the exception itself carries a 5xx or a 408
    /// (an answer proves the request was sent, even through a client built without the Graph handler); otherwise
    /// <see cref="SendOutcomeNotSent"/>. Reading the option closes it, so an attempt not yet sent cannot overtake the answer.
    /// </summary>
    private static string OutcomeOf(NoTransientRetryOption option, Exception ex) =>
        option.CloseAndReadOutcomeUnknown() || ex is ApiException { ResponseStatusCode: >= 500 or 408 }
            ? SendOutcomeUnknown
            : SendOutcomeNotSent;

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

        // Ebilling-r1 task 151c-1a: a server-only send carries its signed marker, so the inbound path does not file its attachments again.
        if (request.ServerSendMarker is { Length: > 0 } marker)
        {
            message.InternetMessageHeaders =
            [
                new InternetMessageHeader { Name = ServerSendMarker.HeaderName, Value = marker },
            ];
        }

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
