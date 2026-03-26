using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Handles email drafting from the M365 Copilot agent.
///
/// Flow: resolve matter → query activity → resolve recipient → generate draft → return email preview card.
///
/// ADR-010: Concrete type, no interface — injected directly via DI.
/// ADR-015: Minimum text to AI; log only identifiers, sizes, and outcome codes.
/// ADR-016: Explicit timeouts for upstream AI calls; bounded concurrency.
/// </summary>
public sealed class EmailDraftService
{
    private readonly AdaptiveCardFormatterService _cardFormatter;
    private readonly ILogger<EmailDraftService> _logger;

    // TODO: Inject existing Dataverse query service for matter resolution (matter details, party roles, activity history).
    // TODO: Inject existing Azure OpenAI service for draft generation (e.g., AiToolService or OpenAI client wrapper).
    // TODO: Inject existing communications service for sending emails after user approval.

    /// <summary>
    /// Timeout for Azure OpenAI draft generation calls (ADR-016).
    /// </summary>
    private static readonly TimeSpan AiCallTimeout = TimeSpan.FromSeconds(30);

    public EmailDraftService(
        AdaptiveCardFormatterService cardFormatter,
        ILogger<EmailDraftService> logger)
    {
        _cardFormatter = cardFormatter ?? throw new ArgumentNullException(nameof(cardFormatter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates an email draft contextual to a matter and returns an Adaptive Card preview.
    /// </summary>
    /// <param name="matterId">The Dataverse matter record ID.</param>
    /// <param name="purpose">The purpose of the email (e.g., "status update", "document request", "meeting follow-up").</param>
    /// <param name="recipientRole">
    /// Optional party role to resolve the recipient from matter parties (e.g., "outside counsel", "client contact").
    /// When null, the caller must supply the recipient separately.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="EmailDraftResult"/> containing the draft and Adaptive Card JSON.</returns>
    public async Task<EmailDraftResult> DraftEmailAsync(
        Guid matterId,
        string purpose,
        string? recipientRole = null,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..12];

        // ADR-015: Log identifiers and metadata only — never log purpose text or email content.
        _logger.LogInformation(
            "[EMAIL-DRAFT] Starting draft: CorrelationId={CorrelationId}, MatterId={MatterId}, " +
            "PurposeLength={PurposeLength}, RecipientRole={RecipientRole}",
            correlationId, matterId, purpose.Length, recipientRole ?? "none");

        // Step 1: Resolve matter details from Dataverse.
        var matter = await ResolveMatterAsync(matterId, cancellationToken);

        // Step 2: Query recent matter activity (events, documents).
        var recentActivity = await QueryRecentActivityAsync(matterId, cancellationToken);

        // Step 3: Resolve recipient from matter party roles.
        var recipient = recipientRole is not null
            ? await ResolveRecipientAsync(matterId, recipientRole, cancellationToken)
            : new EmailRecipient { Email = "", DisplayName = "", Role = "unresolved" };

        // Step 4: Generate email body via Azure OpenAI with matter context.
        var draftBody = await GenerateDraftBodyAsync(matterId, purpose, recentActivity, cancellationToken);

        // Step 5: Assemble the draft and format the preview card.
        var communicationId = Guid.NewGuid().ToString();
        var subject = $"RE: {matter.MatterName} — {purpose}";

        var draft = new EmailDraft
        {
            CommunicationId = communicationId,
            MatterId = matterId.ToString(),
            RecipientEmail = recipient.Email,
            RecipientDisplayName = recipient.DisplayName,
            RecipientRole = recipient.Role,
            Subject = subject,
            Body = draftBody,
            Purpose = purpose
        };

        var cardJson = FormatEmailPreviewCard(draft);

        _logger.LogInformation(
            "[EMAIL-DRAFT] Draft complete: CorrelationId={CorrelationId}, MatterId={MatterId}, " +
            "CommunicationId={CommunicationId}, BodyLength={BodyLength}",
            correlationId, matterId, communicationId, draftBody.Length);

        return new EmailDraftResult
        {
            Draft = draft,
            AdaptiveCardJson = cardJson
        };
    }

    /// <summary>
    /// Resolves a recipient from matter party roles (e.g., "outside counsel", "client contact").
    /// Queries Dataverse for the party role association on the specified matter.
    /// </summary>
    /// <param name="matterId">The matter to look up parties for.</param>
    /// <param name="recipientRole">The role name to resolve (e.g., "outside counsel").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved <see cref="EmailRecipient"/>.</returns>
    public async Task<EmailRecipient> ResolveRecipientAsync(
        Guid matterId,
        string recipientRole,
        CancellationToken cancellationToken = default)
    {
        // ADR-015: Log identifiers only.
        _logger.LogInformation(
            "[EMAIL-DRAFT] Resolving recipient: MatterId={MatterId}, Role={RecipientRole}",
            matterId, recipientRole);

        // TODO: Query Dataverse for matter party roles.
        // Expected query: sprk_matterparty where sprk_matterid = matterId and sprk_role = recipientRole
        // Join to contact/account to get email and display name.
        // Example:
        //   var parties = await _dataverseService.QueryMatterPartiesAsync(matterId, recipientRole, cancellationToken);
        //   if (parties.Count == 0) return new EmailRecipient { Role = recipientRole, Email = "", DisplayName = "" };
        //   var primary = parties.First();
        //   return new EmailRecipient { Email = primary.Email, DisplayName = primary.Name, Role = recipientRole };

        await Task.CompletedTask;

        _logger.LogInformation(
            "[EMAIL-DRAFT] Recipient resolved: MatterId={MatterId}, Role={RecipientRole}, Found={Found}",
            matterId, recipientRole, false);

        return new EmailRecipient
        {
            Email = "",
            DisplayName = "",
            Role = recipientRole
        };
    }

    /// <summary>
    /// Generates a contextual email body using Azure OpenAI, incorporating matter context
    /// and recent activity to produce a professional, relevant draft.
    /// </summary>
    /// <param name="matterId">The matter ID for context scoping.</param>
    /// <param name="purpose">The email purpose (e.g., "status update").</param>
    /// <param name="recentActivity">Recent matter activity to incorporate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated email body text.</returns>
    public async Task<string> GenerateDraftBodyAsync(
        Guid matterId,
        string purpose,
        IReadOnlyList<MatterActivityItem> recentActivity,
        CancellationToken cancellationToken = default)
    {
        // ADR-015: Log identifiers and sizes only — never log purpose text or activity descriptions.
        _logger.LogInformation(
            "[EMAIL-DRAFT] Generating draft body: MatterId={MatterId}, PurposeLength={PurposeLength}, " +
            "ActivityCount={ActivityCount}",
            matterId, purpose.Length, recentActivity.Count);

        // TODO: Call Azure OpenAI to generate the email body.
        // ADR-015: Send minimum text required — only purpose and summarized activity, not full document content.
        // ADR-016: Apply timeout and cancellation to the AI call.
        //
        // Expected implementation:
        //   using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        //   cts.CancelAfter(AiCallTimeout);
        //
        //   var activitySummary = string.Join("\n", recentActivity.Select(a => $"- {a.Type}: {a.Summary} ({a.Date:d})"));
        //   var prompt = $"Draft a professional email for purpose: {purpose}.\n" +
        //                $"Recent matter activity:\n{activitySummary}\n" +
        //                $"Keep the tone professional and concise.";
        //
        //   var response = await _openAiService.GenerateCompletionAsync(prompt, cts.Token);
        //   return response.Content;

        await Task.CompletedTask;

        _logger.LogInformation(
            "[EMAIL-DRAFT] Draft body generated: MatterId={MatterId}, Outcome=Placeholder",
            matterId);

        // Placeholder until Azure OpenAI service is wired.
        return $"[Draft generation pending — Azure OpenAI service integration required]\n\n" +
               $"This email relates to the matter and covers: {purpose}.\n" +
               $"Recent activity items considered: {recentActivity.Count}.";
    }

    /// <summary>
    /// Formats an email draft into an Adaptive Card JSON preview with send/edit/cancel actions.
    /// Uses the <see cref="AdaptiveCardFormatterService.FormatEmailPreview"/> method.
    /// </summary>
    /// <param name="draft">The email draft to format.</param>
    /// <returns>Adaptive Card JSON string.</returns>
    public string FormatEmailPreviewCard(EmailDraft draft)
    {
        var cardItem = new EmailPreviewCardItem
        {
            CommunicationId = draft.CommunicationId,
            RecipientEmail = draft.RecipientEmail,
            RecipientRole = draft.RecipientRole,
            Subject = draft.Subject,
            Body = draft.Body
        };

        return _cardFormatter.FormatEmailPreview(cardItem);
    }

    // ──────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Resolves matter details from Dataverse.
    /// </summary>
    private async Task<MatterSummary> ResolveMatterAsync(
        Guid matterId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[EMAIL-DRAFT] Resolving matter: MatterId={MatterId}", matterId);

        // TODO: Query Dataverse for matter details.
        // Example:
        //   var matter = await _dataverseService.GetMatterAsync(matterId, cancellationToken);
        //   return new MatterSummary { MatterId = matter.Id, MatterName = matter.Name, ... };

        await Task.CompletedTask;

        return new MatterSummary
        {
            MatterId = matterId.ToString(),
            MatterName = $"Matter-{matterId.ToString()[..8]}"
        };
    }

    /// <summary>
    /// Queries recent matter activity (events, documents, communications).
    /// </summary>
    private async Task<IReadOnlyList<MatterActivityItem>> QueryRecentActivityAsync(
        Guid matterId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[EMAIL-DRAFT] Querying recent activity: MatterId={MatterId}", matterId);

        // TODO: Query Dataverse for recent matter activity.
        // Example:
        //   var activities = await _dataverseService.GetRecentMatterActivityAsync(matterId, top: 10, cancellationToken);
        //   return activities.Select(a => new MatterActivityItem { ... }).ToList();

        await Task.CompletedTask;

        return Array.Empty<MatterActivityItem>();
    }
}

// ──────────────────────────────────────────────
// Supporting models (co-located per task guidance)
// ──────────────────────────────────────────────

/// <summary>
/// Result of the email draft operation containing both the draft and the preview card.
/// </summary>
public sealed record EmailDraftResult
{
    /// <summary>The generated email draft.</summary>
    public required EmailDraft Draft { get; init; }

    /// <summary>Adaptive Card JSON for rendering in M365 Copilot.</summary>
    public required string AdaptiveCardJson { get; init; }
}

/// <summary>
/// Represents a generated email draft with all fields needed for preview and sending.
/// </summary>
public sealed record EmailDraft
{
    /// <summary>Unique identifier for this draft communication.</summary>
    public required string CommunicationId { get; init; }

    /// <summary>The matter this email relates to.</summary>
    public required string MatterId { get; init; }

    /// <summary>Recipient email address.</summary>
    public required string RecipientEmail { get; init; }

    /// <summary>Recipient display name.</summary>
    public required string RecipientDisplayName { get; init; }

    /// <summary>Recipient's role on the matter (e.g., "outside counsel").</summary>
    public required string RecipientRole { get; init; }

    /// <summary>Email subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>Generated email body text.</summary>
    public required string Body { get; init; }

    /// <summary>The purpose that was used to generate this draft.</summary>
    public required string Purpose { get; init; }
}

/// <summary>
/// A resolved email recipient from matter party roles.
/// </summary>
public sealed record EmailRecipient
{
    /// <summary>Email address.</summary>
    public required string Email { get; init; }

    /// <summary>Display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Party role on the matter.</summary>
    public required string Role { get; init; }
}

/// <summary>
/// Summary of a matter for email context.
/// </summary>
public sealed record MatterSummary
{
    /// <summary>Matter record ID.</summary>
    public required string MatterId { get; init; }

    /// <summary>Matter display name.</summary>
    public required string MatterName { get; init; }
}

/// <summary>
/// A single activity item from matter history (event, document upload, communication).
/// </summary>
public sealed record MatterActivityItem
{
    /// <summary>Activity type (e.g., "Document", "Event", "Communication").</summary>
    public required string Type { get; init; }

    /// <summary>Brief activity summary.</summary>
    public required string Summary { get; init; }

    /// <summary>When the activity occurred.</summary>
    public required DateTimeOffset Date { get; init; }
}
