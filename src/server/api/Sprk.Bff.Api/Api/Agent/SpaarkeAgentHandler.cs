using System.Text.Json;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.Compat;
using Microsoft.Agents.Core.Models;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// M365 Agents SDK activity handler for the Spaarke Copilot agent.
///
/// Receives activities from M365 channels (Teams, Copilot) and routes them to
/// the BFF agent gateway. This is a THIN ADAPTER — all business logic lives in
/// existing BFF services (chat, search, playbook execution).
///
/// Extends <see cref="ActivityHandler"/> from the Compat namespace which provides
/// the familiar Bot Framework activity routing pattern on top of the new M365 Agents SDK.
///
/// ADR-010: Concrete type, injected via DI — no unnecessary interface.
/// ADR-015: Never log message content; log only identifiers, sizes, and outcome codes.
/// </summary>
public class SpaarkeAgentHandler : ActivityHandler
{
    private readonly ILogger<SpaarkeAgentHandler> _logger;

    // TODO: Inject AgentTokenService when MCI-014 is implemented — provides OBO token exchange for agent-to-BFF calls.
    // TODO: Inject ChatSessionManager when agent message routing is wired — routes messages to existing chat service.
    // TODO: Inject PlaybookCatalogService when playbook listing is wired — returns available playbooks for the agent.
    // TODO: Inject PlaybookExecutionEngine when playbook execution is wired — enqueues playbook runs.
    // TODO: Inject HandoffUrlBuilder for generating deep-link URLs in Adaptive Card responses.

    /// <summary>
    /// Initializes a new instance of the <see cref="SpaarkeAgentHandler"/> class.
    /// ADR-010: Inject only what is needed — ILogger for now, services added as implemented.
    /// </summary>
    public SpaarkeAgentHandler(ILogger<SpaarkeAgentHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ────────────────────────────────────────────────────────────────
    // Message Activities
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles incoming user text messages from M365 Copilot / Teams.
    ///
    /// Extracts the user identity, conversation reference, and optional entity context,
    /// then routes the message to the appropriate BFF service via the agent gateway.
    /// </summary>
    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var activity = turnContext.Activity;
        var correlationId = activity.Id ?? Guid.NewGuid().ToString();
        var userId = ExtractUserId(activity);
        var conversationRef = activity.GetConversationReference();

        // ADR-015: Log identifiers and metadata only — never log message content.
        _logger.LogInformation(
            "[AGENT-HANDLER] Message activity received: CorrelationId={CorrelationId}, UserId={UserId}, " +
            "ChannelId={ChannelId}, ConversationId={ConversationId}, TextLength={TextLength}",
            correlationId,
            userId,
            activity.ChannelId ?? "unknown",
            conversationRef.Conversation?.Id ?? "unknown",
            activity.Text?.Length ?? 0);

        try
        {
            // Send typing indicator while processing — signals the agent is working.
            await SendTypingIndicatorAsync(turnContext, cancellationToken);

            var messageText = activity.Text?.Trim();
            if (string.IsNullOrEmpty(messageText))
            {
                _logger.LogWarning(
                    "[AGENT-HANDLER] Empty message received: CorrelationId={CorrelationId}, UserId={UserId}",
                    correlationId, userId);

                await turnContext.SendActivityAsync(
                    "I didn't receive any text. Please try sending your message again.",
                    cancellationToken: cancellationToken);
                return;
            }

            // Extract optional entity context from Copilot (e.g., current page/form context).
            var entityContext = ExtractEntityContext(activity);

            // TODO: Route message to existing BFF chat service via ChatSessionManager.
            // The routing logic will:
            //   1. Resolve or create a chat session from the conversation reference
            //   2. Map entity context to ChatHostContext for entity-scoped RAG
            //   3. Forward the message to ChatSessionManager.SendMessageAsync()
            //   4. Collect the streamed response into a single reply
            //   5. Optionally format an Adaptive Card for rich results

            // Placeholder response until service wiring is implemented.
            var responseText = "I received your message. Agent service wiring is pending — " +
                               "this handler will route to existing BFF chat, search, and playbook services.";

            await turnContext.SendActivityAsync(
                responseText,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "[AGENT-HANDLER] Message processed: CorrelationId={CorrelationId}, UserId={UserId}, Outcome=Success",
                correlationId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AGENT-HANDLER] Message processing failed: CorrelationId={CorrelationId}, UserId={UserId}",
                correlationId, userId);

            await SendErrorCardAsync(turnContext, correlationId, "Failed to process your message.", cancellationToken);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Invoke Activities (Adaptive Card Action.Submit / Action.Execute)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles invoke activities from Adaptive Card actions (Action.Submit, Action.Execute).
    ///
    /// Extracts the action verb and payload data from the invoke, then routes to
    /// the appropriate BFF service handler.
    /// </summary>
    protected override async Task<InvokeResponse> OnInvokeActivityAsync(
        ITurnContext<IInvokeActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var activity = turnContext.Activity;
        var correlationId = activity.Id ?? Guid.NewGuid().ToString();
        var userId = ExtractUserId(activity);

        // ADR-015: Log activity metadata, not payload content.
        _logger.LogInformation(
            "[AGENT-HANDLER] Invoke activity received: CorrelationId={CorrelationId}, UserId={UserId}, " +
            "ChannelId={ChannelId}, InvokeName={InvokeName}",
            correlationId,
            userId,
            activity.ChannelId ?? "unknown",
            activity.Name ?? "unnamed");

        try
        {
            // Send typing indicator for longer invoke operations.
            await SendTypingIndicatorAsync(turnContext, cancellationToken);

            // Extract action data from the invoke activity Value.
            var actionData = ExtractInvokeActionData(activity);

            // ADR-015: Log action verb (identifier), not the payload data.
            _logger.LogInformation(
                "[AGENT-HANDLER] Invoke action: CorrelationId={CorrelationId}, Verb={Verb}, HasPayload={HasPayload}",
                correlationId,
                actionData.Verb ?? "none",
                actionData.Data != null);

            // TODO: Route invoke actions to appropriate BFF service handlers.
            // Known action verbs to support:
            //   "run-playbook"    → PlaybookExecutionEngine.EnqueueAsync()
            //   "view-document"   → Generate HandoffUrlBuilder deep link
            //   "search-documents"→ Forward to existing search service
            //   "open-workspace"  → Generate workspace deep link via HandoffUrlBuilder
            //   "refresh-status"  → Poll playbook job status

            // Placeholder: return 200 OK with acknowledgment.
            var response = new InvokeResponse
            {
                Status = 200,
                Body = new { message = "Invoke received. Action routing pending.", verb = actionData.Verb }
            };

            _logger.LogInformation(
                "[AGENT-HANDLER] Invoke processed: CorrelationId={CorrelationId}, UserId={UserId}, Outcome=Success",
                correlationId, userId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AGENT-HANDLER] Invoke processing failed: CorrelationId={CorrelationId}, UserId={UserId}",
                correlationId, userId);

            return new InvokeResponse
            {
                Status = 500,
                Body = CreateErrorResponseBody(correlationId, "Failed to process action.")
            };
        }
    }

    /// <summary>
    /// Handles Adaptive Card Action.Execute invoke activities specifically.
    /// Called by the base <see cref="OnInvokeActivityAsync"/> when the invoke name
    /// matches the Adaptive Card execute pattern.
    /// </summary>
    protected override async Task<AdaptiveCardInvokeResponse> OnAdaptiveCardInvokeAsync(
        ITurnContext<IInvokeActivity> turnContext,
        AdaptiveCardInvokeValue invokeValue,
        CancellationToken cancellationToken)
    {
        var activity = turnContext.Activity;
        var correlationId = activity.Id ?? Guid.NewGuid().ToString();
        var userId = ExtractUserId(activity);
        var actionVerb = invokeValue.Action?.Verb;

        // ADR-015: Log action verb (identifier), not the submitted data.
        _logger.LogInformation(
            "[AGENT-HANDLER] Adaptive Card execute: CorrelationId={CorrelationId}, UserId={UserId}, Verb={Verb}",
            correlationId, userId, actionVerb ?? "none");

        try
        {
            await SendTypingIndicatorAsync(turnContext, cancellationToken);

            // TODO: Route Adaptive Card actions to BFF services based on verb.
            // The invokeValue.Action.Data contains the card's submitted form data.

            return new AdaptiveCardInvokeResponse
            {
                StatusCode = 200,
                Type = "application/vnd.microsoft.card.adaptive",
                Value = new { }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AGENT-HANDLER] Adaptive Card execute failed: CorrelationId={CorrelationId}, UserId={UserId}, Verb={Verb}",
                correlationId, userId, actionVerb ?? "none");

            return new AdaptiveCardInvokeResponse
            {
                StatusCode = 500,
                Type = "application/vnd.microsoft.error",
                Value = CreateErrorResponseBody(correlationId, "Failed to process card action.")
            };
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Conversation Lifecycle
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles new members being added to a conversation (e.g., bot installed).
    /// Sends a welcome message introducing the Spaarke agent capabilities.
    /// </summary>
    protected override async Task OnMembersAddedAsync(
        IList<ChannelAccount> membersAdded,
        ITurnContext<IConversationUpdateActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var botId = turnContext.Activity.Recipient?.Id;

        foreach (var member in membersAdded)
        {
            // Only greet when the bot itself is added, not when other users join.
            if (member.Id == botId)
            {
                _logger.LogInformation(
                    "[AGENT-HANDLER] Bot added to conversation: ConversationId={ConversationId}, ChannelId={ChannelId}",
                    turnContext.Activity.Conversation?.Id ?? "unknown",
                    turnContext.Activity.ChannelId ?? "unknown");

                await turnContext.SendActivityAsync(
                    "Hello! I'm the Spaarke assistant. I can help you search documents, " +
                    "run analysis playbooks, and answer questions about your matters. " +
                    "Just type a message to get started.",
                    cancellationToken: cancellationToken);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Response Formatting Helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a typing indicator activity to show the agent is working.
    /// </summary>
    private static async Task SendTypingIndicatorAsync(
        ITurnContext turnContext,
        CancellationToken cancellationToken)
    {
        var typingActivity = new Activity { Type = ActivityTypes.Typing };
        await turnContext.SendActivityAsync(typingActivity, cancellationToken);
    }

    /// <summary>
    /// Creates and sends an Adaptive Card attachment.
    /// </summary>
    internal static Attachment CreateAdaptiveCardAttachment(string cardJson)
    {
        return new Attachment
        {
            ContentType = "application/vnd.microsoft.card.adaptive",
            Content = JsonSerializer.Deserialize<JsonElement>(cardJson)
        };
    }

    /// <summary>
    /// Sends a user-friendly error card when processing fails.
    /// Includes the correlation ID for support reference but no internal error details.
    /// </summary>
    private async Task SendErrorCardAsync(
        ITurnContext turnContext,
        string correlationId,
        string userMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            // Error card follows ProblemDetails-style pattern with user-friendly content.
            // Uses a simple JSON template — a dedicated error-card.json template can be
            // loaded from embedded resources in a future iteration.
            var errorCardJson = JsonSerializer.Serialize(new
            {
                type = "AdaptiveCard",
                version = "1.5",
                body = new object[]
                {
                    new
                    {
                        type = "TextBlock",
                        text = "Something went wrong",
                        weight = "Bolder",
                        size = "Medium",
                        color = "Attention"
                    },
                    new
                    {
                        type = "TextBlock",
                        text = userMessage,
                        wrap = true
                    },
                    new
                    {
                        type = "TextBlock",
                        text = $"Reference: {correlationId}",
                        size = "Small",
                        isSubtle = true
                    }
                },
                schema = "http://adaptivecards.io/schemas/adaptive-card.json"
            });

            var attachment = CreateAdaptiveCardAttachment(errorCardJson);
            var reply = new Activity
            {
                Type = ActivityTypes.Message,
                Attachments = new List<Attachment> { attachment }
            };

            await turnContext.SendActivityAsync(reply, cancellationToken);
        }
        catch (Exception ex)
        {
            // Last-resort: if card rendering fails, send plain text.
            _logger.LogError(ex,
                "[AGENT-HANDLER] Failed to send error card: CorrelationId={CorrelationId}",
                correlationId);

            await turnContext.SendActivityAsync(
                $"An error occurred. Reference: {correlationId}",
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Creates a ProblemDetails-style error response body for invoke responses.
    /// </summary>
    private static object CreateErrorResponseBody(string correlationId, string detail)
    {
        return new
        {
            type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            title = "Internal Server Error",
            status = 500,
            detail,
            instance = correlationId
        };
    }

    // ────────────────────────────────────────────────────────────────
    // Identity & Context Extraction
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the user identity from the activity.
    /// In M365 Copilot / Teams channels, <see cref="IActivity.From"/> carries the
    /// AAD Object ID of the authenticated user.
    /// </summary>
    private static string ExtractUserId(IActivity activity)
    {
        return activity.From?.AadObjectId
            ?? activity.From?.Id
            ?? "unknown";
    }

    /// <summary>
    /// Extracts optional entity context from the activity.
    /// Copilot may forward page/form context via <see cref="IActivity.Entities"/>
    /// or <see cref="IActivity.ChannelData"/>. This enables entity-scoped RAG search.
    /// </summary>
    private static EntityContext? ExtractEntityContext(IActivity activity)
    {
        // Check for entity context in the activity's channel data (Copilot-provided metadata).
        if (activity.ChannelData is JsonElement channelData)
        {
            if (channelData.TryGetProperty("entityContext", out var entityCtx))
            {
                try
                {
                    return JsonSerializer.Deserialize<EntityContext>(entityCtx.GetRawText());
                }
                catch (JsonException)
                {
                    // Malformed context — continue without it.
                }
            }
        }

        // Check for entity references in activity.Value (used by some card submissions).
        if (activity.Value is JsonElement valueElement)
        {
            if (valueElement.TryGetProperty("entityType", out var entityType) &&
                valueElement.TryGetProperty("entityId", out var entityId))
            {
                return new EntityContext
                {
                    EntityType = entityType.GetString(),
                    EntityId = entityId.GetString()
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts action verb and data from an invoke activity's Value property.
    /// Invoke activities from Adaptive Card Action.Submit carry their data in <see cref="IActivity.Value"/>.
    /// </summary>
    private static InvokeActionData ExtractInvokeActionData(IActivity activity)
    {
        if (activity.Value is JsonElement valueElement)
        {
            string? verb = null;
            if (valueElement.TryGetProperty("verb", out var verbProp))
                verb = verbProp.GetString();
            else if (valueElement.TryGetProperty("action", out var actionProp))
                verb = actionProp.GetString();

            return new InvokeActionData { Verb = verb, Data = valueElement };
        }

        return new InvokeActionData();
    }

    // ────────────────────────────────────────────────────────────────
    // Supporting Types (lightweight, co-located with handler)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents optional entity context forwarded from M365 Copilot.
    /// Maps to the Dataverse entity the user is currently viewing.
    /// </summary>
    internal sealed record EntityContext
    {
        /// <summary>Dataverse entity logical name (e.g., "sprk_matter", "sprk_document").</summary>
        public string? EntityType { get; init; }

        /// <summary>Entity record GUID.</summary>
        public string? EntityId { get; init; }

        /// <summary>Optional display name of the entity record.</summary>
        public string? EntityName { get; init; }

        /// <summary>Optional workspace type context (e.g., "AnalysisWorkspace").</summary>
        public string? WorkspaceType { get; init; }
    }

    /// <summary>
    /// Parsed action data from an invoke activity.
    /// </summary>
    internal sealed record InvokeActionData
    {
        /// <summary>The action verb identifying which handler to route to.</summary>
        public string? Verb { get; init; }

        /// <summary>The raw JSON payload submitted with the action.</summary>
        public JsonElement? Data { get; init; }
    }
}
