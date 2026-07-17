using Azure;
using Azure.Communication;
using Azure.Communication.Chat;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Channels;

/// <summary>
/// Messaging (ACS Chat) implementation of <see cref="ICommunicationChannelSender"/> (ADR-045 rule 4 /
/// NFR-04) — the SECOND channel behind the shipped sender seam, proving "add a channel by additive
/// registration alone". Mirrors <see cref="EmailChannelSender"/> in shape but wraps ACS Chat instead of
/// Microsoft Graph: it owns ALL ACS wire concerns of a message send — resolving the sender's
/// <c>communicationUserId</c> (task 010 identity plane), minting the sender's <c>chat</c> token, resolving
/// or creating the ACS thread (task 011 thread plane), posting the message on the sender's minted token, and
/// mapping ACS SDK errors to <see cref="SdapProblemException"/> — so <see cref="CommunicationService"/> stays
/// ACS-free (NFR-04). Every <c>Azure.Communication.*</c> type stays inside this boundary (ADR-045).
///
/// The returned <see cref="ChannelSendResult.ProviderMessageId"/> is the ACS message id — the echo-dedup key
/// the inbound ingestor dedupes on (FR-04, tasks 031/051); <see cref="ChannelSendResult.ProviderThreadId"/>
/// carries the ACS <c>ChatThreadId</c> for task 051 to persist.
/// </summary>
/// <remarks>
/// ADR-028 / NFR-05: no credential is <c>new</c>'d here. The sender's chat token is minted by
/// <see cref="IAcsIdentityService"/>, itself rooted in the DI-injected central <c>TokenCredential</c>. The
/// class is left non-<c>sealed</c> and the raw ACS post is a <c>protected virtual</c> method: that is the
/// unit-test seam (ADR-010 — no extra interface), letting tests exercise the identity/token/thread/error
/// branches without a live ACS resource (none is provisioned in R1 — task 012).
/// </remarks>
public class MessagingChannelSender : ICommunicationChannelSender
{
    private readonly IAcsIdentityService _identityService;
    private readonly IAcsThreadService _threadService;
    private readonly IOptions<AcsOptions> _acsOptions;
    private readonly ILogger<MessagingChannelSender> _logger;

    public MessagingChannelSender(
        IAcsIdentityService identityService,
        IAcsThreadService threadService,
        IOptions<AcsOptions> acsOptions,
        ILogger<MessagingChannelSender> logger)
    {
        _identityService = identityService;
        _threadService = threadService;
        _acsOptions = acsOptions;
        _logger = logger;
    }

    public CommunicationType SupportedType => CommunicationType.Message;

    public async Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
    {
        var sender = request.SenderParticipant
            ?? throw new SdapProblemException(
                code: "CHANNEL_SEND_FAILED",
                title: "Message Send Failed",
                detail: "A sending participant (SenderParticipant) is required to transmit a message over ACS Chat.",
                statusCode: 400,
                extensions: new Dictionary<string, object> { ["correlationId"] = request.CorrelationId });

        // 1. Resolve the sender's ACS identity (idempotent — task 010) and mint a chat-scoped token so the BFF
        //    posts AS the sender (never an admin/service token to a client — NFR-04).
        var mapping = await _identityService.EnsureIdentityAsync(sender, cancellationToken);
        var token = await _identityService.MintChatTokenAsync(mapping.CommunicationUserId, validity: null, ct: cancellationToken);

        // 2. Resolve or create the ACS thread via the task-011 thread plane. Thread ASSIGNMENT is task 040's
        //    IThreadResolver; when the caller (051) has already resolved a thread it is passed in, otherwise a
        //    thread is created here seeded with the sender (membership reconcile of recipients is task 041).
        var threadId = request.AcsThreadId;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            var topic = string.IsNullOrWhiteSpace(request.Communication.Subject)
                ? "Spaarke conversation"
                : request.Communication.Subject;
            var created = await _threadService.CreateThreadAsync(
                topic,
                new[] { mapping.CommunicationUserId },
                retention: null,
                ct: cancellationToken);
            threadId = created.ChatThreadId;
        }

        // 3. Transmit over ACS Chat as the sender. All Azure.Communication wire concerns are confined to
        //    TransmitAsync (the test seam). A provider failure surfaces as SdapProblemException
        //    (CHANNEL_SEND_FAILED), the ACS analog of EmailChannelSender's GRAPH_SEND_FAILED.
        string acsMessageId;
        try
        {
            acsMessageId = await TransmitAsync(
                threadId!, token.Token, request.Communication.Body, request.FromDisplayName, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(
                ex,
                "ACS SendMessage failed | CorrelationId: {CorrelationId}, ThreadId: {ThreadId}, Status: {Status}, ErrorCode: {ErrorCode}",
                request.CorrelationId, threadId, ex.Status, ex.ErrorCode);

            throw new SdapProblemException(
                code: "CHANNEL_SEND_FAILED",
                title: "Message Send Failed",
                detail: $"ACS Chat error: {ex.Message}",
                statusCode: ex.Status > 0 ? ex.Status : 502,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = request.CorrelationId,
                    ["acsErrorCode"] = ex.ErrorCode ?? "unknown"
                });
        }
        catch (Exception ex) when (ex is not SdapProblemException)
        {
            _logger.LogError(
                ex,
                "Unexpected error sending message over ACS | CorrelationId: {CorrelationId}, ThreadId: {ThreadId}",
                request.CorrelationId, threadId);

            throw new SdapProblemException(
                code: "CHANNEL_SEND_FAILED",
                title: "Message Send Failed",
                detail: $"Unexpected error: {ex.Message}",
                statusCode: 500,
                extensions: new Dictionary<string, object> { ["correlationId"] = request.CorrelationId });
        }

        _logger.LogInformation(
            "Message sent over ACS Chat | CorrelationId: {CorrelationId}, ThreadId: {ThreadId}, AcsMessageId: {AcsMessageId}",
            request.CorrelationId, threadId, acsMessageId);

        return new ChannelSendResult
        {
            // Messaging has no mailbox — the sender's ACS communicationUserId is the "from" identity.
            FromAddress = mapping.CommunicationUserId,
            ProviderMessageId = acsMessageId, // ACS message id = echo-dedup key (FR-04)
            ProviderThreadId = threadId
        };
    }

    /// <summary>
    /// Raw ACS Chat send as the sending participant. Builds a <see cref="ChatThreadClient"/> from the ACS
    /// endpoint + the sender's server-minted chat token and posts a text message, returning the ACS message
    /// id. <c>protected virtual</c> is the unit-test seam (ADR-010 — no extra interface): tests override it to
    /// avoid a live ACS call (none is provisioned in R1) while still exercising <see cref="SendAsync"/>'s
    /// identity/token/thread/error-mapping branches.
    /// </summary>
    protected virtual async Task<string> TransmitAsync(
        string chatThreadId,
        string senderChatToken,
        string content,
        string? senderDisplayName,
        CancellationToken cancellationToken)
    {
        var endpoint = _acsOptions.Value.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "Communication:Acs:Endpoint must be configured to send a message over ACS Chat " +
                "(no live ACS resource is provisioned yet — see task 012).");
        }

        // The ACS Chat data plane has no app-only path; the client authenticates as the sender via their
        // server-minted chat token (rooted transitively in the central TokenCredential — ADR-028 / NFR-05).
        var chatClient = new ChatClient(new Uri(endpoint), new CommunicationTokenCredential(senderChatToken));
        var threadClient = chatClient.GetChatThreadClient(chatThreadId);

        var options = new SendChatMessageOptions
        {
            Content = content,
            MessageType = ChatMessageType.Text
        };
        if (!string.IsNullOrWhiteSpace(senderDisplayName))
        {
            options.SenderDisplayName = senderDisplayName;
        }

        Response<SendChatMessageResult> response = await threadClient.SendMessageAsync(options, cancellationToken);
        return response.Value.Id;
    }
}
