namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Service for evaluating email filter rules to determine processing action.
/// Rules are loaded from Dataverse (sprk_emailprocessingrule) and cached in Redis.
/// </summary>
public interface IEmailFilterService
{
    /// <summary>
    /// Evaluate an email against all active filter rules and determine the processing action.
    /// Rules are evaluated in priority order (lower priority = evaluated first).
    /// First matching rule determines the action.
    /// </summary>
    /// <param name="context">Context containing email metadata for rule evaluation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating the action to take and which rule matched (if any).</returns>
    Task<EmailFilterResult> EvaluateAsync(EmailFilterContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Force refresh the cached filter rules from Dataverse.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active filter rules (for admin display/debugging).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active rules ordered by priority.</returns>
    Task<IReadOnlyList<EmailFilterRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for email filter evaluation containing metadata about the email.
/// </summary>
public class EmailFilterContext
{
    /// <summary>
    /// The email activity ID being evaluated.
    /// </summary>
    public Guid EmailId { get; init; }

    /// <summary>
    /// Email subject line.
    /// </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>
    /// Sender email address.
    /// </summary>
    public string From { get; init; } = string.Empty;

    /// <summary>
    /// Recipients (To field).
    /// </summary>
    public string To { get; init; } = string.Empty;

    /// <summary>
    /// CC recipients.
    /// </summary>
    public string? Cc { get; init; }

    /// <summary>
    /// Email direction: true = Received (Inbound), false = Sent (Outbound).
    /// </summary>
    public bool IsInbound { get; init; }

    /// <summary>
    /// Whether the email has attachments.
    /// </summary>
    public bool HasAttachments { get; init; }

    /// <summary>
    /// List of attachment filenames for pattern matching.
    /// </summary>
    public IReadOnlyList<string> AttachmentNames { get; init; } = [];

    /// <summary>
    /// Regarding object entity type (e.g., "sprk_matter").
    /// </summary>
    public string? RegardingEntityType { get; init; }

    /// <summary>
    /// Regarding object ID.
    /// </summary>
    public Guid? RegardingId { get; init; }

    /// <summary>
    /// Email body (for content-based filtering if needed).
    /// </summary>
    public string? Body { get; init; }
}

/// <summary>
/// Result of email filter evaluation.
/// </summary>
public class EmailFilterResult
{
    /// <summary>
    /// The action to take for this email.
    /// </summary>
    public EmailFilterAction Action { get; init; }

    /// <summary>
    /// The rule that matched (if any). Null if default action was applied.
    /// </summary>
    public EmailFilterRule? MatchedRule { get; init; }

    /// <summary>
    /// Reason for the action (rule name or "Default action").
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Whether to create separate documents for attachments.
    /// </summary>
    public bool CreateAttachmentDocuments { get; init; } = true;

    /// <summary>
    /// Whether the email should be processed (Action == AutoSave).
    /// </summary>
    public bool ShouldProcess => Action == EmailFilterAction.AutoSave;

    /// <summary>
    /// Creates a result for processing the email (AutoSave).
    /// </summary>
    public static EmailFilterResult Process(EmailFilterRule? rule = null, bool createAttachments = true) => new()
    {
        Action = EmailFilterAction.AutoSave,
        MatchedRule = rule,
        Reason = rule?.Name ?? "Default action",
        CreateAttachmentDocuments = createAttachments
    };

    /// <summary>
    /// Creates a result for ignoring the email.
    /// </summary>
    public static EmailFilterResult Ignore(EmailFilterRule? rule, string reason) => new()
    {
        Action = EmailFilterAction.Ignore,
        MatchedRule = rule,
        Reason = reason,
        CreateAttachmentDocuments = false
    };

    /// <summary>
    /// Creates a result for emails requiring manual review.
    /// </summary>
    public static EmailFilterResult RequireReview(EmailFilterRule? rule, string reason) => new()
    {
        Action = EmailFilterAction.ReviewRequired,
        MatchedRule = rule,
        Reason = reason,
        CreateAttachmentDocuments = false
    };
}

/// <summary>
/// Action to take for an email based on filter rules.
/// Values match Dataverse sprk_action OptionSet.
/// </summary>
public enum EmailFilterAction
{
    /// <summary>
    /// Automatically save as document.
    /// </summary>
    AutoSave = 0,

    /// <summary>
    /// Ignore this email (don't process).
    /// </summary>
    Ignore = 1,

    /// <summary>
    /// Flag for manual review.
    /// </summary>
    ReviewRequired = 2
}

/// <summary>
/// Email filter rule loaded from Dataverse.
/// </summary>
public class EmailFilterRule
{
    /// <summary>
    /// Rule ID (sprk_emailprocessingruleid).
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Rule display name (sprk_name).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Rule type: Exclude (0), Include (1), Route (2).
    /// </summary>
    public EmailRuleType RuleType { get; init; }

    /// <summary>
    /// Evaluation priority (lower = first). Default 100.
    /// </summary>
    public int Priority { get; init; } = 100;

    /// <summary>
    /// Whether the rule is active.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Target field for pattern matching: subject, from, to, attachmentname, body.
    /// </summary>
    public string TargetField { get; init; } = string.Empty;

    /// <summary>
    /// Regex pattern to match against the target field.
    /// </summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary>
    /// Optional JSON criteria for complex matching (future use).
    /// </summary>
    public string? CriteriaJson { get; init; }

    /// <summary>
    /// Rule description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether to create separate documents for attachments (for Include rules).
    /// </summary>
    public bool CreateAttachmentDocuments { get; init; } = true;
}

/// <summary>
/// Rule type values matching Dataverse sprk_ruletype OptionSet.
/// </summary>
public enum EmailRuleType
{
    /// <summary>
    /// Exclude matching emails from processing.
    /// </summary>
    Exclude = 0,

    /// <summary>
    /// Include matching emails for processing.
    /// </summary>
    Include = 1,

    /// <summary>
    /// Route matching emails to specific handling.
    /// </summary>
    Route = 2
}
