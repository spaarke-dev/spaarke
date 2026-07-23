using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

// ---------------------------------------------------------------------------
// Session-agnostic Layer-A action seam — CreateNotification (task 031 / FR-07).
//
// Extracted from CreateNotificationNodeExecutor's private BuildNotificationEntity
// + CheckForDuplicateNotificationAsync + the idempotency-then-create flow, so an
// appnotification can be created WITHOUT a NodeExecutionContext (no playbook run,
// no chat session). The executor renders ConfigJson templates + resolves recipient/
// regarding/memberships, THEN hands already-resolved plain values here; ActionSeam
// supplies plain values directly.
//
// Two fields formerly baked in as playbook-specific are now explicit parameters so
// non-playbook callers (Phase 4/5) can supply their own: `source` (was hardcoded
// "playbook" → appnotification.sprk_source) and `correlationId` (was
// context.RunId.ToString() → appnotification.sprk_playbookrunid). The executor passes
// source:"playbook", correlationId:RunId to preserve today's exact field values —
// proven by task 030's characterization tests passing unmodified.
// ---------------------------------------------------------------------------

/// <summary>Session-agnostic input for an appnotification create — all values pre-rendered/typed.</summary>
internal sealed record NotificationActionInput(
    string Title,
    string Body,
    string? Category,
    int Priority,
    int ToastType,
    string? ActionUrl,
    Guid RecipientId,
    Guid? RegardingId,
    string? RegardingType,
    string? DueDate,
    // FR-6 enrichment (already resolved by the caller).
    string? RegardingName,
    string? SourceEntityType,
    string? SourceId,
    string? SourceModifiedOn,
    string? SourceOwningUser,
    string? ViaMatterId,
    string? ViaMatterName,
    IReadOnlyList<object>? ViaMatterMemberships,
    // Formerly playbook-baked; explicit so non-playbook callers can supply their own.
    string Source,
    string CorrelationId);

/// <summary>Result of an appnotification create attempt.</summary>
internal sealed record NotificationActionResult(
    Guid? NotificationId,
    bool Skipped,
    string? SkipReason);

/// <summary>
/// Session-agnostic core that performs the appnotification idempotency check and create.
/// Constructed inline by the executor (from its own injected fields) and by <c>ActionSeam</c>
/// (from its own injected fields).
/// </summary>
internal sealed class NotificationActionCore
{
    /// <summary>
    /// Dataverse <c>toasttype</c> option-set value for "Hidden" — the notification produces no visible toast.
    /// Hidden-toast notifications skip <c>data.actions[]</c> population (FR-18).
    /// </summary>
    private const int ToastTypeHidden = 100_000_000;

    private readonly IGenericEntityService _entityService;
    private readonly ILogger _logger;

    public NotificationActionCore(IGenericEntityService entityService, ILogger logger)
    {
        _entityService = entityService;
        _logger = logger;
    }

    /// <summary>
    /// Runs the idempotency check (skip-if-unread-duplicate by recipient + regarding + category) and,
    /// when not a duplicate, builds + creates the appnotification. Returns a skipped result or the
    /// created id.
    /// </summary>
    public async Task<NotificationActionResult> CreateAsync(
        NotificationActionInput input,
        CancellationToken cancellationToken)
    {
        // Idempotency check: query for existing unread notification with same user + regarding + category
        if (input.RegardingId.HasValue && !string.IsNullOrWhiteSpace(input.Category))
        {
            var isDuplicate = await CheckForDuplicateNotificationAsync(
                input.RecipientId,
                input.RegardingId.Value,
                input.Category!,
                cancellationToken);

            if (isDuplicate)
            {
                _logger.LogInformation(
                    "CreateNotification skipped — duplicate unread notification exists for user {UserId}, regarding {RegardingId}, category {Category}",
                    input.RecipientId,
                    input.RegardingId,
                    input.Category);

                return new NotificationActionResult(
                    NotificationId: null,
                    Skipped: true,
                    SkipReason: "Duplicate unread notification exists");
            }
        }

        var entity = BuildNotificationEntity(input);

        var notificationId = await _entityService.CreateAsync(entity, cancellationToken);

        return new NotificationActionResult(
            NotificationId: notificationId,
            Skipped: false,
            SkipReason: null);
    }

    /// <summary>
    /// Checks for an existing unread appnotification matching user + regarding + category.
    /// Returns true if a duplicate exists (skip creation).
    /// </summary>
    private async Task<bool> CheckForDuplicateNotificationAsync(
        Guid recipientId,
        Guid regardingId,
        string category,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new QueryExpression("appnotification")
            {
                ColumnSet = new ColumnSet("activityid"),
                TopCount = 1,
                Criteria = new FilterExpression(LogicalOperator.And)
            };
            query.Criteria.AddCondition("ownerid", ConditionOperator.Equal, recipientId);
            query.Criteria.AddCondition("sprk_category", ConditionOperator.Equal, category);
            query.Criteria.AddCondition("sprk_regardingid", ConditionOperator.Equal, regardingId.ToString());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var result = await _entityService.RetrieveMultipleAsync(query, cancellationToken);
            return result.Entities.Count > 0;
        }
        catch (Exception ex)
        {
            // If idempotency check fails, log and proceed with creation
            // (better to create a potential duplicate than to fail the entire node).
            _logger.LogWarning(
                ex,
                "Idempotency check failed — proceeding with notification creation");
            return false;
        }
    }

    /// <summary>
    /// Builds an SDK <see cref="Entity"/> representing the appnotification to create.
    /// Moved verbatim from CreateNotificationNodeExecutor.BuildNotificationEntity (task 031);
    /// the only signature change is that the formerly playbook-baked <c>sprk_source</c> and
    /// <c>sprk_playbookrunid</c> values now come from <see cref="NotificationActionInput.Source"/>
    /// and <see cref="NotificationActionInput.CorrelationId"/>.
    /// </summary>
    private static Entity BuildNotificationEntity(NotificationActionInput input)
    {
        var title = input.Title;
        var body = input.Body;
        var category = input.Category;
        var priority = input.Priority;
        var toastType = input.ToastType;
        var actionUrl = input.ActionUrl;
        var recipientId = input.RecipientId;
        var regardingId = input.RegardingId;
        var regardingType = input.RegardingType;
        var dueDate = input.DueDate;
        var regardingName = input.RegardingName;
        var sourceEntityType = input.SourceEntityType;
        var sourceId = input.SourceId;
        var sourceModifiedOn = input.SourceModifiedOn;
        var sourceOwningUser = input.SourceOwningUser;
        var viaMatterId = input.ViaMatterId;
        var viaMatterName = input.ViaMatterName;
        var viaMatterMemberships = input.ViaMatterMemberships;

        var entity = new Entity("appnotification");
        entity["title"] = title;
        entity["body"] = body;
        entity["priority"] = new OptionSetValue(priority);
        entity["toasttype"] = new OptionSetValue(toastType);
        entity["ownerid"] = new EntityReference("systemuser", recipientId);
        entity["ttlinseconds"] = 604800; // 7 days default TTL (increased from 3d on 2026-06-22 after UAT showed 36 notifications TTL-purged before user could review them)

        // Add category (custom field for idempotency grouping)
        if (!string.IsNullOrWhiteSpace(category))
        {
            entity["sprk_category"] = category;
        }

        // FR-18 (P3) + FR-6 (R4 task 020): build appnotification.data payload.
        var hasActionUrl = !string.IsNullOrWhiteSpace(actionUrl);
        var hasDueDate = !string.IsNullOrWhiteSpace(dueDate);
        var hasRegardingName = !string.IsNullOrWhiteSpace(regardingName);
        var hasRegardingId = regardingId.HasValue && regardingId.Value != Guid.Empty;
        var hasRegardingType = !string.IsNullOrWhiteSpace(regardingType);
        var hasSourceEntity = !string.IsNullOrWhiteSpace(sourceEntityType) || !string.IsNullOrWhiteSpace(sourceId)
                             || !string.IsNullOrWhiteSpace(sourceModifiedOn) || !string.IsNullOrWhiteSpace(sourceOwningUser);
        var hasViaMatter = !string.IsNullOrWhiteSpace(viaMatterId)
                           && (!string.IsNullOrWhiteSpace(viaMatterName) || viaMatterMemberships is { Count: > 0 });

        if (hasActionUrl || hasDueDate || hasRegardingName || hasRegardingId || hasSourceEntity || hasViaMatter)
        {
            var customData = new Dictionary<string, object?>();
            if (hasActionUrl) customData["actionUrl"] = actionUrl;
            if (hasDueDate) customData["dueDate"] = dueDate;

            // FR-6: regardingName + regardingEntityType + regardingId — flat fields used by widget
            // grounding + EntityNameValidator allow-list (FR-14).
            if (hasRegardingName) customData["regardingName"] = regardingName;
            if (hasRegardingType) customData["regardingEntityType"] = regardingType;
            if (hasRegardingId) customData["regardingId"] = regardingId!.Value.ToString();

            // FR-6: viaMatter object — present ONLY when matter linkage exists.
            if (hasViaMatter)
            {
                var viaMatter = new Dictionary<string, object?>
                {
                    ["id"] = viaMatterId
                };
                if (!string.IsNullOrWhiteSpace(viaMatterName))
                {
                    viaMatter["name"] = viaMatterName;
                }
                // memberships[] — one entry per role per FR-6 + AC-6 spec.
                viaMatter["memberships"] = viaMatterMemberships ?? (IReadOnlyList<object>)Array.Empty<object>();
                customData["viaMatter"] = viaMatter;
            }

            // FR-6: source object — captures originating record identity for widget grounding +
            // narration. Built when ANY source-* scalar is present (AC-6a).
            if (hasSourceEntity)
            {
                var source = new Dictionary<string, object?>();
                if (!string.IsNullOrWhiteSpace(sourceEntityType)) source["entityType"] = sourceEntityType;
                if (!string.IsNullOrWhiteSpace(sourceId)) source["id"] = sourceId;
                if (!string.IsNullOrWhiteSpace(sourceModifiedOn)) source["modifiedOn"] = sourceModifiedOn;
                if (!string.IsNullOrWhiteSpace(sourceOwningUser)) source["owningUser"] = sourceOwningUser;
                customData["source"] = source;
            }

            var isVisibleToast = hasActionUrl && toastType != ToastTypeHidden;
            object dataObject = isVisibleToast
                ? new
                {
                    actions = new[]
                    {
                        new
                        {
                            title = "Open",
                            data = new { url = actionUrl }
                        }
                    },
                    customData
                }
                : (object)new { customData };

            entity["data"] = JsonSerializer.Serialize(dataObject);
        }

        // Add regarding info if specified. appnotification is NOT an activity entity
        // (no polymorphic regardingobjectid lookup), so we store regarding as two text
        // fields: sprk_regardingid (GUID string) + sprk_regardingtype (entity logical name).
        // These are also used in CheckForDuplicateNotificationAsync for idempotency.
        if (regardingId.HasValue && !string.IsNullOrWhiteSpace(regardingType))
        {
            entity["sprk_regardingid"] = regardingId.Value.ToString();
            entity["sprk_regardingtype"] = regardingType;
        }

        // Add AI metadata (source + correlation — formerly playbook-baked, now explicit params).
        entity["sprk_source"] = input.Source;
        entity["sprk_playbookrunid"] = input.CorrelationId;

        return entity;
    }
}
