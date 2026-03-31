using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Sprk.Bff.Api.Services;

/// <summary>
/// Singleton service for creating Dataverse appnotification records.
/// Called inline by BFF endpoints after operations like document upload, analysis complete, etc.
/// </summary>
/// <remarks>
/// <para>
/// Uses IGenericEntityService to POST to the native <c>appnotification</c> entity.
/// Registered as a concrete singleton per ADR-010 (DI minimalism).
/// </para>
/// <para>
/// The appnotification entity is a native Dataverse entity supporting in-app notifications.
/// Required fields: title, ownerid. Optional: body, icontype, data, ttl.
/// </para>
/// </remarks>
public sealed class NotificationService
{
    private readonly Spaarke.Dataverse.IGenericEntityService _entityService;
    private readonly ILogger<NotificationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public NotificationService(
        Spaarke.Dataverse.IGenericEntityService entityService,
        ILogger<NotificationService> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates an in-app notification via the Dataverse appnotification entity.
    /// </summary>
    /// <param name="userId">Dataverse systemuserid of the target user (owner of the notification).</param>
    /// <param name="title">Notification title (required, max 256 chars).</param>
    /// <param name="body">Notification body text (optional, max 4000 chars).</param>
    /// <param name="category">Notification category for grouping (e.g., "documents", "analysis", "email").</param>
    /// <param name="priority">Priority level: 200000000 = Informational (default), 200000001 = Warning, 200000002 = Critical.</param>
    /// <param name="actionUrl">Optional deep-link URL for the notification action.</param>
    /// <param name="regardingId">Optional ID of the related record.</param>
    /// <param name="aiMetadata">Optional AI-generated metadata (stored as JSON in custom field).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the created appnotification record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId is empty or title is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the Dataverse create operation fails.</exception>
    public async Task<Guid> CreateNotificationAsync(
        Guid userId,
        string title,
        string? body = null,
        string? category = null,
        int priority = 200000000, // Informational
        string? actionUrl = null,
        Guid? regardingId = null,
        Dictionary<string, object?>? aiMetadata = null,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentNullException(nameof(userId), "Target user ID is required");
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentNullException(nameof(title), "Notification title is required");

        _logger.LogDebug(
            "Creating appnotification for user {UserId}: {Title} (category={Category}, priority={Priority})",
            userId, title, category ?? "(none)", priority);

        try
        {
            var entity = new Entity("appnotification");

            // Required fields
            entity["title"] = title;
            entity["ownerid"] = new EntityReference("systemuser", userId);

            // Optional body
            if (!string.IsNullOrWhiteSpace(body))
            {
                entity["body"] = body;
            }

            // Priority (option set): 200000000=Informational, 200000001=Warning, 200000002=Critical
            entity["priority"] = new OptionSetValue(priority);

            // Icon type based on category for visual distinction
            if (!string.IsNullOrWhiteSpace(category))
            {
                entity["icontype"] = new OptionSetValue(MapCategoryToIconType(category));
            }

            // Action data with deep link
            if (!string.IsNullOrWhiteSpace(actionUrl) || regardingId.HasValue || aiMetadata is not null)
            {
                var actionData = BuildActionData(actionUrl, regardingId, category, aiMetadata);
                entity["data"] = JsonSerializer.Serialize(actionData, JsonOptions);
            }

            // TTL in seconds (default: 7 days)
            entity["ttlindays"] = 7;

            var notificationId = await _entityService.CreateAsync(entity, cancellationToken);

            _logger.LogInformation(
                "Created appnotification {NotificationId} for user {UserId}: {Title} (category={Category})",
                notificationId, userId, title, category ?? "(none)");

            return notificationId;
        }
        catch (Exception ex) when (ex is not ArgumentNullException)
        {
            _logger.LogError(
                ex,
                "Failed to create appnotification for user {UserId}: {Title} — {ErrorMessage}",
                userId, title, ex.Message);

            throw new InvalidOperationException(
                $"Failed to create notification for user {userId}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Maps a notification category string to the appnotification icontype option set value.
    /// </summary>
    /// <remarks>
    /// Icon types: 100000000=Info, 100000001=Success, 100000002=Failure,
    /// 100000003=Warning, 100000004=Mention, 100000005=Custom.
    /// </remarks>
    private static int MapCategoryToIconType(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "documents" or "upload" => 100000001,   // Success
            "analysis" or "ai"     => 100000000,    // Info
            "email" or "communication" => 100000004, // Mention
            "tasks" or "assignment" => 100000003,    // Warning (attention needed)
            "error" or "failure"   => 100000002,     // Failure
            _                      => 100000000      // Info (default)
        };
    }

    /// <summary>
    /// Builds the JSON action data payload for the notification.
    /// </summary>
    private static Dictionary<string, object?> BuildActionData(
        string? actionUrl,
        Guid? regardingId,
        string? category,
        Dictionary<string, object?>? aiMetadata)
    {
        var data = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(actionUrl))
        {
            data["actionUrl"] = actionUrl;
        }

        if (regardingId.HasValue)
        {
            data["regardingId"] = regardingId.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            data["category"] = category;
        }

        if (aiMetadata is { Count: > 0 })
        {
            data["aiMetadata"] = aiMetadata;
        }

        return data;
    }

    /// <summary>
    /// Resolves a Dataverse systemuserid from an Azure AD Object ID.
    /// </summary>
    /// <param name="azureAdObjectId">The Azure AD oid claim value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Dataverse systemuserid, or null if not found.</returns>
    public async Task<Guid?> ResolveSystemUserIdAsync(Guid azureAdObjectId, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("azureactivedirectoryobjectid", ConditionOperator.Equal, azureAdObjectId.ToString())
                    }
                },
                TopCount = 1
            };

            var result = await _entityService.RetrieveMultipleAsync(query, cancellationToken);

            if (result.Entities.Count == 0)
            {
                _logger.LogWarning("No Dataverse user found for Azure AD OID {AzureAdOid}", azureAdObjectId);
                return null;
            }

            var systemUserId = result.Entities[0].Id;
            _logger.LogDebug("Mapped Azure AD OID {AzureAdOid} to Dataverse systemuserid {SystemUserId}",
                azureAdObjectId, systemUserId);

            return systemUserId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving Dataverse user for Azure AD OID {AzureAdOid}", azureAdObjectId);
            return null;
        }
    }
}
