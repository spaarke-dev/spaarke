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

    /// <summary>
    /// Spaarke-intake-pipeline-r1 task 006 (design §10.1 item 7): the problem code (404) of a send that carries
    /// <c>InReplyToMessageId</c> whose parent message is not in the sending mailbox. Nothing is created or sent: a reply that cannot
    /// be threaded is never sent as a new, unthreaded message.
    /// </summary>
    public const string ReplyParentNotFoundCode = "sdap.communication.reply_parent_not_found";

    /// <summary>The problem code (413) of a threaded reply with an attachment above <see cref="MaxReplyAttachmentBytes"/>. Nothing is created or sent.</summary>
    public const string ReplyAttachmentTooLargeCode = "sdap.communication.reply_attachment_too_large";

    /// <summary>The problem code (400) of a system-mail send that is also a reply: a draft's <c>send</c> always saves to Sent Items, so the no-copy rule of system mail cannot hold. Nothing is created or sent.</summary>
    public const string ReplySystemMailUnsupportedCode = "sdap.communication.reply_system_mail_unsupported";

    /// <summary>
    /// Graph adds a file to a draft with a single request only below 3 MB (larger files need an upload session, which the reply
    /// branch does not use). A larger attachment fails the reply up front with <see cref="ReplyAttachmentTooLargeCode"/>.
    /// </summary>
    internal const int MaxReplyAttachmentBytes = 3 * 1024 * 1024;

    public async Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
    {
        // Task 006 (§10.1 item 7): a send that names its parent message goes out as a threaded reply (createReply on the parent, so
        // Exchange sets In-Reply-To and References). Every other send takes the path below, unchanged.
        if (!string.IsNullOrWhiteSpace(request.Communication.InReplyToMessageId))
        {
            return await SendReplyAsync(request, cancellationToken);
        }

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

        // Ontology task 214 (#2774): the Sent Items read below is bounded below by the moment this send began.
        var sendStartedUtc = DateTimeOffset.UtcNow;

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

            throw MapGraphFailure(request, ex, outcome);

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

            throw MapGraphFailure(request, ex, outcome);

        }

        // Best-effort capture of the just-sent message's real RFC-2822 Internet-Message-Id (FR-06 —
        // feeds the W1 thread rung). MUST NOT fail the send (NFR-06); returns null on any error.
        // A message that was not saved to Sent Items cannot be found there, so a system-mail send skips the lookup (no
        // Sent Items read, no "not found" warning on every invite).
        var internetMessageId = saveToSentItems
            ? await TryCaptureInternetMessageIdAsync(
                graphClient, request.UserMode, request.FromAddress, request.Communication.Subject,
                sendStartedUtc, request.CorrelationId, cancellationToken)
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

    /// <summary>
    /// Task 006 (§10.1 item 7): the threaded-reply send. Graph rejects a custom <c>internetMessageHeaders</c> entry that does not
    /// start with <c>x-</c>, so <c>In-Reply-To</c> and <c>References</c> cannot be set on a new message; Exchange sets them when the
    /// message is created as a reply. The sequence: find the parent in the sending mailbox, <c>createReply</c> on it (a draft), set the
    /// draft's subject, body, recipients and sender to the request's values and add its attachments, then <c>send</c> the draft.
    /// <list type="bullet">
    /// <item>The parent is looked up by its RFC-2822 Internet-Message-Id (the value of <c>InReplyToMessageId</c> for email); a value
    /// that is not in angle brackets is read as a Graph message id. A parent that is not found fails the send with
    /// <see cref="ReplyParentNotFoundCode"/>; it is never sent as a new, unthreaded message.</item>
    /// <item>The server-send marker is a custom header, and Graph does not list <c>internetMessageHeaders</c> among a draft's
    /// updatable properties, so the marker rides on the <c>createReply</c> request's <c>message</c> parameter instead.</item>
    /// <item>Every step before <c>send</c> leaves nothing sent, so a failure there is <see cref="SendOutcomeNotSent"/> and the draft
    /// is deleted (best-effort). A failure of the <c>send</c> itself is classified like <c>sendMail</c>'s, and its draft is kept
    /// when the outcome is <see cref="SendOutcomeUnknown"/>.</item>
    /// </list>
    /// A draft's <c>send</c> always saves the message to Sent Items, which is also where the Internet-Message-Id read-back looks.
    /// </summary>
    private async Task<ChannelSendResult> SendReplyAsync(ChannelSendRequest request, CancellationToken cancellationToken)
    {
        var parentReference = request.Communication.InReplyToMessageId!.Trim();

        if (request.Communication.SystemMail is not null)
        {
            throw new SdapProblemException(
                code: ReplySystemMailUnsupportedCode,
                title: "Reply Not Supported For System Mail",
                detail: "A system-mail send keeps no copy in Sent Items, and a threaded reply is always saved there.",
                statusCode: 400,
                extensions: new Dictionary<string, object> { ["correlationId"] = request.CorrelationId });
        }

        // Validate before anything is created in the mailbox.
        if (request.Attachments?.FirstOrDefault(a => a.Content.Length > MaxReplyAttachmentBytes) is { } tooLarge)
        {
            throw new SdapProblemException(
                code: ReplyAttachmentTooLargeCode,
                title: "Reply Attachment Too Large",
                detail: $"A threaded reply carries attachments up to {MaxReplyAttachmentBytes / (1024 * 1024)} MB each; '{tooLarge.Name}' is larger.",
                statusCode: 413,
                extensions: new Dictionary<string, object> { ["correlationId"] = request.CorrelationId });
        }

        var sendOption = new NoTransientRetryOption();
        var sendStartedUtc = DateTimeOffset.UtcNow;
        var sendAttempted = false;
        ReplyMailbox? mailbox = null;
        string? draftId = null;
        GraphServiceClient graphClient;

        try
        {
            if (request.UserMode)
            {
                graphClient = await _graphClientFactory.ForUserAsync(request.HttpContext!, cancellationToken);
                mailbox = new MeReplyMailbox(graphClient);
            }
            else
            {
                graphClient = _graphClientFactory.ForApp();
                mailbox = new UserReplyMailbox(graphClient, request.FromAddress);
            }

            var parentId = await mailbox.FindMessageIdAsync(parentReference, cancellationToken);
            if (parentId is null)
            {
                _logger.LogWarning(
                    "Reply parent message not found in the sending mailbox; nothing sent | CorrelationId: {CorrelationId}, From: {From}, UserMode: {UserMode}",
                    request.CorrelationId, request.FromAddress, request.UserMode);
                throw new SdapProblemException(
                    code: ReplyParentNotFoundCode,
                    title: "Reply Parent Not Found",
                    detail: "The message this email replies to was not found in the sending mailbox, so the reply cannot be threaded. Nothing was sent.",
                    statusCode: 404,
                    extensions: new Dictionary<string, object> { ["correlationId"] = request.CorrelationId });
            }

            // The update carries everything BuildGraphMessage sets except the marker (not patchable; see the summary). The property is
            // left unset, not set to null: an explicit null would be sent as "internetMessageHeaders": null.
            var update = BuildGraphMessage(request, carryServerSendMarker: false);
            var markerHeaders = request.ServerSendMarker is { Length: > 0 } marker
                ? new List<InternetMessageHeader> { new() { Name = ServerSendMarker.HeaderName, Value = marker } }
                : null;

            draftId = await mailbox.CreateReplyAsync(parentId, markerHeaders, cancellationToken);
            await mailbox.UpdateDraftAsync(draftId, update, cancellationToken);

            foreach (var attachment in request.Attachments ?? [])
            {
                await mailbox.AddAttachmentAsync(
                    draftId,
                    new FileAttachment
                    {
                        OdataType = "#microsoft.graph.fileAttachment",
                        Name = attachment.Name,
                        ContentType = attachment.ContentType,
                        ContentBytes = attachment.Content
                    },
                    cancellationToken);
            }

            sendAttempted = true;
            await mailbox.SendDraftAsync(draftId, sendOption, cancellationToken);

            _logger.LogInformation(
                "Communication reply sent successfully | CorrelationId: {CorrelationId}, From: {From}, To: {RecipientCount}, UserMode: {UserMode}, ParentMessageId: {ParentMessageId}, DraftId: {DraftId}",
                request.CorrelationId,
                request.FromAddress,
                request.Communication.To.Length,
                request.UserMode,
                parentId,
                draftId);
        }
        catch (SdapProblemException)
        {
            await TryDeleteReplyDraftAsync(mailbox, draftId, request.CorrelationId);
            throw;
        }
        catch (Exception ex)
        {
            // A failure before the send attempt left nothing sent; the send's own failure is classified like sendMail's.
            var outcome = sendAttempted ? OutcomeOf(sendOption, ex) : SendOutcomeNotSent;
            if (outcome == SendOutcomeNotSent)
            {
                await TryDeleteReplyDraftAsync(mailbox, draftId, request.CorrelationId);
            }

            _logger.LogError(
                ex,
                "Graph reply send failed | CorrelationId: {CorrelationId}, StatusCode: {StatusCode}, ErrorCode: {ErrorCode}, UserMode: {UserMode}, SendOutcome: {SendOutcome}",
                request.CorrelationId,
                (ex as ApiException)?.ResponseStatusCode,
                (ex as ODataError)?.Error?.Code,
                request.UserMode,
                outcome);
            throw MapGraphFailure(request, ex, outcome);
        }

        var internetMessageId = await TryCaptureInternetMessageIdAsync(
            graphClient, request.UserMode, request.FromAddress, request.Communication.Subject,
            sendStartedUtc, request.CorrelationId, cancellationToken);

        return new ChannelSendResult
        {
            FromAddress = request.FromAddress,
            ProviderMessageId = internetMessageId
        };
    }

    /// <summary>Best-effort removal of a reply draft that will never be sent; a failure is logged and swallowed.</summary>
    private async Task TryDeleteReplyDraftAsync(ReplyMailbox? mailbox, string? draftId, string correlationId)
    {
        if (mailbox is null || draftId is null)
            return;

        try
        {
            await mailbox.DeleteDraftAsync(draftId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Reply draft cleanup failed (non-fatal); the unsent draft remains in Drafts | CorrelationId: {CorrelationId}, DraftId: {DraftId}",
                correlationId, draftId);
        }
    }

    /// <summary>The <c>GRAPH_SEND_FAILED</c> problem for a Graph or unexpected failure of a send, with its <c>sendOutcome</c>.</summary>
    private static SdapProblemException MapGraphFailure(ChannelSendRequest request, Exception ex, string outcome)
    {
        var extensions = new Dictionary<string, object>
        {
            ["correlationId"] = request.CorrelationId,
            [SendOutcomeExtension] = outcome
        };
        if (request.UserMode) extensions["sendMode"] = "User";

        if (ex is ODataError odata)
        {
            extensions["graphErrorCode"] = odata.Error?.Code ?? "unknown";
            return new SdapProblemException(
                code: "GRAPH_SEND_FAILED",
                title: "Email Send Failed",
                detail: request.UserMode
                    ? $"Graph API error (OBO): {odata.Error?.Message ?? odata.Message}"
                    : $"Graph API error: {odata.Error?.Message ?? odata.Message}",
                statusCode: odata.ResponseStatusCode > 0 ? odata.ResponseStatusCode : 502,
                extensions: extensions);
        }

        return new SdapProblemException(
            code: "GRAPH_SEND_FAILED",
            title: "Email Send Failed",
            detail: request.UserMode
                ? $"Unexpected error (OBO): {ex.Message}"
                : $"Unexpected error: {ex.Message}",
            statusCode: 500,
            extensions: extensions);
    }

    /// <summary>
    /// The Graph calls of the reply branch against one mailbox: the signed-in user's (<c>/me</c>, OBO) or a named one
    /// (<c>/users/{address}</c>, app-only). Kiota gives the two their own request-builder types, so each call is written twice here
    /// and nowhere else.
    /// </summary>
    private abstract class ReplyMailbox
    {
        /// <summary>The Graph id of the message a reply is to, or null when it is not in the mailbox.</summary>
        internal abstract Task<string?> FindMessageIdAsync(string parentReference, CancellationToken ct);

        /// <summary>Creates the reply draft and returns its id. <paramref name="headers"/> go in the <c>message</c> parameter (null: no body).</summary>
        internal abstract Task<string> CreateReplyAsync(string parentId, List<InternetMessageHeader>? headers, CancellationToken ct);

        internal abstract Task UpdateDraftAsync(string draftId, Message update, CancellationToken ct);
        internal abstract Task AddAttachmentAsync(string draftId, FileAttachment attachment, CancellationToken ct);
        internal abstract Task SendDraftAsync(string draftId, NoTransientRetryOption option, CancellationToken ct);
        internal abstract Task DeleteDraftAsync(string draftId, CancellationToken ct);

        /// <summary>An RFC-2822 Internet-Message-Id is in angle brackets; anything else is read as a Graph message id.</summary>
        protected static bool IsInternetMessageId(string parentReference) => parentReference.StartsWith('<');

        /// <summary>The <c>$filter</c> for an Internet-Message-Id (the quote is doubled, OData escaping).</summary>
        protected static string InternetMessageIdFilter(string parentReference) =>
            $"internetMessageId eq '{parentReference.Replace("'", "''")}'";

        protected static string DraftIdOf(Message? draft) =>
            string.IsNullOrWhiteSpace(draft?.Id)
                ? throw new InvalidOperationException("Graph createReply returned no draft id.")
                : draft.Id;

        /// <summary>The <c>message</c> parameter of createReply: only the marker header, or none (an empty body).</summary>
        protected static Message? MessageWith(List<InternetMessageHeader>? headers) =>
            headers is { Count: > 0 } ? new Message { InternetMessageHeaders = headers } : null;

        protected static bool IsNotFound(ODataError ex) => ex.ResponseStatusCode == 404;
    }

    private sealed class MeReplyMailbox(GraphServiceClient client) : ReplyMailbox
    {
        internal override async Task<string?> FindMessageIdAsync(string parentReference, CancellationToken ct)
        {
            if (IsInternetMessageId(parentReference))
            {
                var page = await client.Me.Messages.GetAsync(rc =>
                {
                    rc.QueryParameters.Filter = InternetMessageIdFilter(parentReference);
                    rc.QueryParameters.Select = new[] { "id" };
                    rc.QueryParameters.Top = 1;
                }, ct);
                return page?.Value?.FirstOrDefault()?.Id;
            }

            try
            {
                return (await client.Me.Messages[parentReference].GetAsync(rc => rc.QueryParameters.Select = new[] { "id" }, ct))?.Id;
            }
            catch (ODataError ex) when (IsNotFound(ex))
            {
                return null;
            }
        }

        internal override async Task<string> CreateReplyAsync(string parentId, List<InternetMessageHeader>? headers, CancellationToken ct)
        {
            var body = new Microsoft.Graph.Me.Messages.Item.CreateReply.CreateReplyPostRequestBody { Message = MessageWith(headers) };
            return DraftIdOf(await client.Me.Messages[parentId].CreateReply.PostAsync(body, cancellationToken: ct));
        }

        internal override Task UpdateDraftAsync(string draftId, Message update, CancellationToken ct) =>
            client.Me.Messages[draftId].PatchAsync(update, cancellationToken: ct);

        internal override Task AddAttachmentAsync(string draftId, FileAttachment attachment, CancellationToken ct) =>
            client.Me.Messages[draftId].Attachments.PostAsync(attachment, cancellationToken: ct);

        internal override Task SendDraftAsync(string draftId, NoTransientRetryOption option, CancellationToken ct) =>
            client.Me.Messages[draftId].Send.PostAsync(rc => rc.Options.Add(option), ct);

        internal override Task DeleteDraftAsync(string draftId, CancellationToken ct) =>
            client.Me.Messages[draftId].DeleteAsync(cancellationToken: ct);
    }

    private sealed class UserReplyMailbox(GraphServiceClient client, string address) : ReplyMailbox
    {
        internal override async Task<string?> FindMessageIdAsync(string parentReference, CancellationToken ct)
        {
            if (IsInternetMessageId(parentReference))
            {
                var page = await client.Users[address].Messages.GetAsync(rc =>
                {
                    rc.QueryParameters.Filter = InternetMessageIdFilter(parentReference);
                    rc.QueryParameters.Select = new[] { "id" };
                    rc.QueryParameters.Top = 1;
                }, ct);
                return page?.Value?.FirstOrDefault()?.Id;
            }

            try
            {
                return (await client.Users[address].Messages[parentReference].GetAsync(rc => rc.QueryParameters.Select = new[] { "id" }, ct))?.Id;
            }
            catch (ODataError ex) when (IsNotFound(ex))
            {
                return null;
            }
        }

        internal override async Task<string> CreateReplyAsync(string parentId, List<InternetMessageHeader>? headers, CancellationToken ct)
        {
            var body = new Microsoft.Graph.Users.Item.Messages.Item.CreateReply.CreateReplyPostRequestBody { Message = MessageWith(headers) };
            return DraftIdOf(await client.Users[address].Messages[parentId].CreateReply.PostAsync(body, cancellationToken: ct));
        }

        internal override Task UpdateDraftAsync(string draftId, Message update, CancellationToken ct) =>
            client.Users[address].Messages[draftId].PatchAsync(update, cancellationToken: ct);

        internal override Task AddAttachmentAsync(string draftId, FileAttachment attachment, CancellationToken ct) =>
            client.Users[address].Messages[draftId].Attachments.PostAsync(attachment, cancellationToken: ct);

        internal override Task SendDraftAsync(string draftId, NoTransientRetryOption option, CancellationToken ct) =>
            client.Users[address].Messages[draftId].Send.PostAsync(rc => rc.Options.Add(option), ct);

        internal override Task DeleteDraftAsync(string draftId, CancellationToken ct) =>
            client.Users[address].Messages[draftId].DeleteAsync(cancellationToken: ct);
    }

    private static Message BuildGraphMessage(ChannelSendRequest request, bool carryServerSendMarker = true)
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
        if (carryServerSendMarker && request.ServerSendMarker is { Length: > 0 } marker)
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
    /// How far before the send start the Sent Items lower bound reaches, so a small clock difference between the BFF and Exchange
    /// cannot put the just-sent message just outside the filter.
    /// </summary>
    internal static readonly TimeSpan SentItemsLookupMargin = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The Sent Items <c>$filter</c>. Graph answers 400 <c>InefficientFilter</c> to a messages query whose <c>$orderby</c>
    /// property is not also in <c>$filter</c>, before any property that is not ordered (Microsoft Learn, "List messages",
    /// "Using filter and orderby in the same query"), so the ordered <c>sentDateTime</c> leads the filter and <c>subject</c>
    /// follows. The quote in a subject is doubled (OData escaping).
    /// </summary>
    internal static string BuildSentItemsFilter(string subject, DateTimeOffset sendStartedUtc)
    {
        var lowerBound = (sendStartedUtc - SentItemsLookupMargin).UtcDateTime
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return $"sentDateTime ge {lowerBound} and subject eq '{subject.Replace("'", "''")}'";
    }

    /// <summary>
    /// Locates the just-sent message in Sent Items (by subject, no older than the send start less
    /// <see cref="SentItemsLookupMargin"/>, newest first) and returns its Internet-Message-Id. Best-effort — any failure is
    /// logged and swallowed, returning null.
    /// (Relocated verbatim from CommunicationService.TryStampInternetMessageIdAsync; the Dataverse
    /// stamp itself remains in CommunicationService as channel-agnostic persistence.)
    /// </summary>
    private async Task<string?> TryCaptureInternetMessageIdAsync(
        GraphServiceClient graphClient,
        bool userMode,
        string senderEmail,
        string subject,
        DateTimeOffset sendStartedUtc,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            var filter = BuildSentItemsFilter(subject, sendStartedUtc);
            List<Message>? messages;
            if (userMode)
            {
                var page = await graphClient.Me.MailFolders["sentitems"].Messages.GetAsync(rc =>
                {
                    rc.QueryParameters.Filter = filter;
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
                    rc.QueryParameters.Filter = filter;
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
