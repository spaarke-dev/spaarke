namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Service for determining which Matter, Account, or Contact an email should be associated with.
/// Uses multiple signals (tracking tokens, threading, sender matching) with confidence scoring.
/// </summary>
public interface IEmailAssociationService
{
    /// <summary>
    /// Determine the best association for an email based on multiple signals.
    /// Returns the highest confidence match, or null if no match above threshold.
    /// </summary>
    /// <param name="emailId">The email activity ID to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The best association result, or null if no match found.</returns>
    Task<AssociationResult?> DetermineAssociationAsync(Guid emailId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all association signals for an email for debugging/preview.
    /// Returns all detected signals with their individual confidence scores.
    /// </summary>
    /// <param name="emailId">The email activity ID to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All detected association signals.</returns>
    Task<AssociationSignalsResponse> GetAssociationSignalsAsync(Guid emailId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of association determination - the best match for the email.
/// </summary>
public class AssociationResult
{
    /// <summary>
    /// The entity type to associate with (e.g., "sprk_matter", "account", "contact").
    /// </summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>
    /// The entity ID to associate with.
    /// </summary>
    public Guid EntityId { get; init; }

    /// <summary>
    /// Display name of the associated entity.
    /// </summary>
    public string EntityName { get; init; } = string.Empty;

    /// <summary>
    /// The method that produced this association.
    /// </summary>
    public AssociationMethod Method { get; init; }

    /// <summary>
    /// Confidence score 0.0 - 1.0.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Human-readable reason for this association.
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Response containing all detected association signals for an email.
/// </summary>
public class AssociationSignalsResponse
{
    /// <summary>
    /// The email activity ID analyzed.
    /// </summary>
    public Guid EmailId { get; init; }

    /// <summary>
    /// All detected signals, ordered by confidence (highest first).
    /// </summary>
    public IReadOnlyList<AssociationSignal> Signals { get; init; } = [];

    /// <summary>
    /// The recommended association (highest confidence above threshold).
    /// </summary>
    public AssociationResult? RecommendedAssociation { get; init; }

    /// <summary>
    /// The minimum confidence threshold for automatic association.
    /// </summary>
    public double ConfidenceThreshold { get; init; }
}

/// <summary>
/// A single association signal detected during analysis.
/// </summary>
public class AssociationSignal
{
    /// <summary>
    /// The entity type this signal points to.
    /// </summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>
    /// The entity ID this signal points to.
    /// </summary>
    public Guid EntityId { get; init; }

    /// <summary>
    /// Display name of the entity.
    /// </summary>
    public string EntityName { get; init; } = string.Empty;

    /// <summary>
    /// The method that detected this signal.
    /// </summary>
    public AssociationMethod Method { get; init; }

    /// <summary>
    /// Confidence score for this signal (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Human-readable description of why this signal was detected.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Methods for determining email associations.
/// Ordered by typical confidence level (highest first).
/// </summary>
public enum AssociationMethod
{
    /// <summary>
    /// Match via tracking token in subject line (0.95 confidence).
    /// Format: [SPRK:ABC123] or similar token pattern.
    /// </summary>
    TrackingToken = 0,

    /// <summary>
    /// Match via conversation thread index (0.90 confidence).
    /// Email is part of a thread with existing associations.
    /// </summary>
    ConversationThread = 1,

    /// <summary>
    /// Match via email's existing regardingobjectid (0.85 confidence).
    /// Email already linked to Matter/Account in Dataverse.
    /// </summary>
    ExistingRegarding = 2,

    /// <summary>
    /// Match via recent sender activity to Matter (0.70 confidence).
    /// Sender has recent emails/activities on a Matter.
    /// </summary>
    RecentSenderActivity = 3,

    /// <summary>
    /// Match via email domain to Account (0.60 confidence).
    /// Sender domain matches Account's website/email domain.
    /// </summary>
    DomainToAccount = 4,

    /// <summary>
    /// Match via contact email address (0.50 confidence).
    /// Sender matches a Contact's email address.
    /// </summary>
    ContactEmailMatch = 5,

    /// <summary>
    /// Manual override by user (1.0 confidence).
    /// </summary>
    ManualOverride = 6
}
