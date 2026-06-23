using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for creating Dataverse appnotification records from playbook execution.
/// Uses TemplateEngine for variable substitution in notification fields.
/// Includes idempotency check — skips creation if an unread notification already exists
/// for the same user, regarding record, and category.
/// </summary>
/// <remarks>
/// <para>
/// Notification configuration is read from node.ConfigJson with structure:
/// </para>
/// <code>
/// {
///   "title": "New document uploaded: {{node1.output.documentName}}",
///   "body": "{{node1.output.summary}}",
///   "category": "document-upload",
///   "priority": 200000000,
///   "actionUrl": "/main.aspx?pagetype=entityrecord&amp;etn=sprk_document&amp;id={{document.id}}",
///   "regardingId": "{{document.id}}",
///   "regardingType": "sprk_document",
///   "recipientId": "{{run.userId}}"
/// }
/// </code>
/// <para>
/// Priority values follow Dataverse appnotification convention:
///   100000000 = Informational, 200000000 = Important (default), 300000000 = Urgent
/// </para>
/// <para>
/// Uses the canonical <see cref="IGenericEntityService"/> shared library
/// (which forwards to <c>DataverseServiceClientImpl</c>) rather than an
/// app-private named HttpClient. This gives correct BaseAddress + token
/// acquisition + lifecycle management for app-only execution paths.
/// </para>
/// </remarks>
public sealed class CreateNotificationNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Default priority for notifications when not specified in config (Important = 200000000).
    /// </summary>
    private const int DefaultPriority = 200_000_000;

    /// <summary>
    /// Dataverse <c>toasttype</c> option-set value for "Hidden" — the notification produces no visible toast.
    /// Per FR-18, hidden-toast notifications skip <c>data.actions[]</c> population because the MDA native bell
    /// surface that would render the action is not shown.
    /// </summary>
    private const int ToastTypeHidden = 100_000_000;

    /// <summary>
    /// Dataverse <c>toasttype</c> option-set default value ("Timed") — visible toast that auto-dismisses.
    /// Used when no explicit ToastType is supplied in config.
    /// </summary>
    private const int DefaultToastType = 200_000_000;

    private readonly ITemplateEngine _templateEngine;
    private readonly IGenericEntityService _entityService;
    private readonly ILogger<CreateNotificationNodeExecutor> _logger;

    public CreateNotificationNodeExecutor(
        ITemplateEngine templateEngine,
        IGenericEntityService entityService,
        ILogger<CreateNotificationNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _entityService = entityService;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.CreateNotification
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("CreateNotification node requires configuration (ConfigJson)");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        try
        {
            var config = JsonSerializer.Deserialize<NotificationNodeConfig>(context.Node.ConfigJson, JsonOptions);
            if (config is null)
            {
                errors.Add("Failed to parse notification configuration");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(config.Title))
                    errors.Add("Notification title is required");

                if (string.IsNullOrWhiteSpace(config.Body))
                    errors.Add("Notification body is required");
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid notification configuration JSON: {ex.Message}");
        }

        return errors.Count > 0
            ? NodeValidationResult.Failure(errors.ToArray())
            : NodeValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogDebug(
            "Executing CreateNotification node {NodeId} ({NodeName})",
            context.Node.Id,
            context.Node.Name);

        try
        {
            // Validate first
            var validation = Validate(context);
            if (!validation.IsValid)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    string.Join("; ", validation.Errors),
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Parse configuration
            var config = JsonSerializer.Deserialize<NotificationNodeConfig>(context.Node.ConfigJson!, JsonOptions)!;

            // Handle iterate items mode: create one notification per item from upstream query
            if (config.IterateItems && config.ItemNotification is not null)
            {
                return await ExecuteIterateItemsAsync(context, config, startedAt, cancellationToken);
            }

            // Build template context from previous outputs
            var templateContext = BuildTemplateContext(context);

            // Render template fields
            var title = _templateEngine.Render(config.Title!, templateContext);
            var body = _templateEngine.Render(config.Body!, templateContext);
            var category = config.Category is not null
                ? _templateEngine.Render(config.Category, templateContext)
                : null;
            var actionUrl = config.ActionUrl is not null
                ? _templateEngine.Render(config.ActionUrl, templateContext)
                : null;
            var dueDate = config.DueDate is not null
                ? _templateEngine.Render(config.DueDate, templateContext)
                : null;

            // Resolve recipient
            var recipientId = ResolveRecipientId(config, templateContext);
            if (recipientId is null)
            {
                return NodeOutput.Error(
                    context.Node.Id,
                    context.Node.OutputVariable,
                    "Cannot determine notification recipient: recipientId is required",
                    NodeErrorCodes.ValidationFailed,
                    NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
            }

            // Resolve regarding record
            Guid? regardingId = null;
            string? regardingType = null;
            if (!string.IsNullOrWhiteSpace(config.RegardingId))
            {
                var regardingIdStr = _templateEngine.Render(config.RegardingId, templateContext);
                if (Guid.TryParse(regardingIdStr, out var parsedRegardingId))
                {
                    regardingId = parsedRegardingId;
                    regardingType = config.RegardingType;
                }
            }

            _logger.LogDebug(
                "CreateNotification node {NodeId} — title: {Title}, recipient: {RecipientId}, category: {Category}",
                context.Node.Id,
                title,
                recipientId,
                category);

            // Idempotency check: query for existing unread notification with same user + regarding + category
            if (regardingId.HasValue && !string.IsNullOrWhiteSpace(category))
            {
                var isDuplicate = await CheckForDuplicateNotificationAsync(
                    recipientId.Value,
                    regardingId.Value,
                    category,
                    cancellationToken);

                if (isDuplicate)
                {
                    _logger.LogInformation(
                        "CreateNotification node {NodeId} skipped — duplicate unread notification exists for user {UserId}, regarding {RegardingId}, category {Category}",
                        context.Node.Id,
                        recipientId,
                        regardingId,
                        category);

                    return NodeOutput.Ok(
                        context.Node.Id,
                        context.Node.OutputVariable,
                        new
                        {
                            skipped = true,
                            reason = "Duplicate unread notification exists",
                            recipientId = recipientId,
                            regardingId = regardingId,
                            category = category
                        },
                        textContent: $"Notification skipped (duplicate): {title}",
                        metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
                }
            }

            // Build appnotification entity
            var entity = BuildNotificationEntity(
                title, body, category, config.Priority ?? DefaultPriority,
                config.ToastType ?? DefaultToastType,
                actionUrl, recipientId.Value, regardingId, regardingType,
                dueDate,
                context);

            // Create the notification via shared Dataverse client
            var notificationId = await _entityService.CreateAsync(entity, cancellationToken);

            _logger.LogInformation(
                "CreateNotification node {NodeId} completed — notification {NotificationId} created for user {UserId}: {Title}",
                context.Node.Id,
                notificationId,
                recipientId,
                title);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new
                {
                    notificationId = notificationId,
                    title = title,
                    recipientId = recipientId,
                    category = category,
                    skipped = false,
                    createdAt = DateTimeOffset.UtcNow
                },
                textContent: $"Notification created: {title}",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CreateNotification node {NodeId} failed: {ErrorMessage}",
                context.Node.Id,
                ex.Message);

            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Failed to create notification: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Executes the iterate-items path: loops over items from upstream query output,
    /// creates one notification per item using the itemNotification template.
    /// </summary>
    private async Task<NodeOutput> ExecuteIterateItemsAsync(
        NodeExecutionContext context,
        NotificationNodeConfig config,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var templateContext = BuildTemplateContext(context);

        // Find upstream query results from previous outputs
        List<JsonElement>? items = null;
        foreach (var (varName, output) in context.PreviousOutputs)
        {
            if (output.StructuredData.HasValue)
            {
                var data = output.StructuredData.Value;
                if (data.TryGetProperty("items", out var itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
                {
                    items = new List<JsonElement>();
                    foreach (var item in itemsArray.EnumerateArray())
                        items.Add(item);
                    break;
                }
            }
        }

        if (items is null || items.Count == 0)
        {
            _logger.LogInformation(
                "CreateNotification node {NodeId} iterate mode -- no items found, skipping",
                context.Node.Id);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new { created = 0, skipped = 0, reason = "No items from upstream query" },
                textContent: "No items to notify about",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }

        var itemConfig = config.ItemNotification!;
        var created = 0;
        var skipped = 0;

        foreach (var item in items)
        {
            // Build item-specific template context
            var itemContext = new Dictionary<string, object?>(templateContext);
            var itemDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(item.GetRawText(), JsonOptions);
            itemContext["item"] = itemDict;

            var title = _templateEngine.Render(itemConfig.Title!, itemContext);
            var body = _templateEngine.Render(itemConfig.Body!, itemContext);
            var category = itemConfig.Category is not null ? _templateEngine.Render(itemConfig.Category, itemContext) : null;
            var actionUrl = itemConfig.ActionUrl is not null ? _templateEngine.Render(itemConfig.ActionUrl, itemContext) : null;
            var dueDate = itemConfig.DueDate is not null ? _templateEngine.Render(itemConfig.DueDate, itemContext) : null;

            var recipientId = ResolveRecipientId(itemConfig, itemContext);
            if (recipientId is null)
            {
                skipped++;
                continue;
            }

            Guid? regardingId = null;
            string? regardingType = null;
            if (!string.IsNullOrWhiteSpace(itemConfig.RegardingId))
            {
                var regardingIdStr = _templateEngine.Render(itemConfig.RegardingId, itemContext);
                if (Guid.TryParse(regardingIdStr, out var parsedRegardingId))
                {
                    regardingId = parsedRegardingId;
                    regardingType = itemConfig.RegardingType;
                }
            }

            // Idempotency check
            if (regardingId.HasValue && !string.IsNullOrWhiteSpace(category))
            {
                var isDuplicate = await CheckForDuplicateNotificationAsync(recipientId.Value, regardingId.Value, category, cancellationToken);
                if (isDuplicate) { skipped++; continue; }
            }

            var entity = BuildNotificationEntity(
                title, body, category, itemConfig.Priority ?? DefaultPriority,
                itemConfig.ToastType ?? DefaultToastType,
                actionUrl, recipientId.Value, regardingId, regardingType,
                dueDate, context);
            await _entityService.CreateAsync(entity, cancellationToken);
            created++;
        }

        _logger.LogInformation(
            "CreateNotification node {NodeId} iterate completed -- created: {Created}, skipped: {Skipped}, total items: {Total}",
            context.Node.Id, created, skipped, items.Count);

        return NodeOutput.Ok(
            context.Node.Id,
            context.Node.OutputVariable,
            new { created, skipped, totalItems = items.Count },
            textContent: $"Created {created} notifications ({skipped} skipped)",
            metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Resolves the recipient systemuserid from config template or run context.
    /// </summary>
    private Guid? ResolveRecipientId(NotificationNodeConfig config, Dictionary<string, object?> templateContext)
    {
        if (!string.IsNullOrWhiteSpace(config.RecipientId))
        {
            var renderedId = _templateEngine.Render(config.RecipientId, templateContext);
            if (Guid.TryParse(renderedId, out var parsedId))
                return parsedId;
        }

        // Fallback: check if userId is available in run context
        if (templateContext.TryGetValue("run", out var runObj) && runObj is not null)
        {
            var runJson = JsonSerializer.SerializeToElement(runObj);
            if (runJson.TryGetProperty("userId", out var userIdProp) &&
                userIdProp.ValueKind == JsonValueKind.String &&
                Guid.TryParse(userIdProp.GetString(), out var userId))
            {
                return userId;
            }
        }

        return null;
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
            query.Criteria.AddCondition("regardingobjectid", ConditionOperator.Equal, regardingId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var result = await _entityService.RetrieveMultipleAsync(query, cancellationToken);
            return result.Entities.Count > 0;
        }
        catch (Exception ex)
        {
            // If idempotency check fails, log and proceed with creation
            // (better to create a potential duplicate than to fail the entire node)
            _logger.LogWarning(
                ex,
                "Idempotency check failed — proceeding with notification creation");
            return false;
        }
    }

    /// <summary>
    /// Builds an SDK <see cref="Entity"/> representing the appnotification to create.
    /// </summary>
    /// <remarks>
    /// Per FR-18 (P3): when <paramref name="actionUrl"/> is present, <c>data</c> is serialized as
    /// <c>{ actions, customData }</c>. <c>customData.actionUrl</c> is populated regardless of
    /// <paramref name="toastType"/> (consumed by the Daily Briefing UI). The <c>actions</c> array
    /// (<c>[{ title: "Open", data: { url: actionUrl } }]</c>) is populated ONLY when the toast is
    /// visible (<paramref name="toastType"/> != <see cref="ToastTypeHidden"/>) so the MDA native bell
    /// icon shows a clickable "Open" button. Hidden-toast notifications skip <c>data.actions</c>
    /// because no visible surface renders them.
    /// </remarks>
    private static Entity BuildNotificationEntity(
        string title,
        string body,
        string? category,
        int priority,
        int toastType,
        string? actionUrl,
        Guid recipientId,
        Guid? regardingId,
        string? regardingType,
        string? dueDate,
        NodeExecutionContext context)
    {
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

        // FR-18 (P3): build appnotification.data payload.
        // - customData.actionUrl is populated regardless of toasttype (Daily Briefing UI consumer).
        // - customData.dueDate is populated when provided (R2.2 — Daily Briefing per-item due-date UX).
        // - data.actions[] is populated ONLY when actionUrl is present AND toasttype != Hidden
        //   (so MDA native bell icon shows a clickable "Open" button).
        // R2.2: data is built whenever actionUrl OR dueDate is present (was: only actionUrl).
        var hasActionUrl = !string.IsNullOrWhiteSpace(actionUrl);
        var hasDueDate = !string.IsNullOrWhiteSpace(dueDate);
        if (hasActionUrl || hasDueDate)
        {
            var customData = new Dictionary<string, object?>();
            if (hasActionUrl) customData["actionUrl"] = actionUrl;
            if (hasDueDate) customData["dueDate"] = dueDate;

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

        // Add regarding object if specified
        if (regardingId.HasValue && !string.IsNullOrWhiteSpace(regardingType))
        {
            entity["regardingobjectid"] = new EntityReference(regardingType, regardingId.Value);
        }

        // Add AI metadata (playbook run info)
        entity["sprk_source"] = "playbook";
        entity["sprk_playbookrunid"] = context.RunId.ToString();

        return entity;
    }

    /// <summary>
    /// Builds template context dictionary from previous node outputs and execution metadata.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? JsonSerializer.Deserialize<object>(output.StructuredData.Value.GetRawText())
                    : null,
                text = output.TextContent,
                success = output.Success
            };
        }

        if (context.Document is not null)
        {
            templateContext["document"] = new
            {
                id = context.Document.DocumentId.ToString(),
                name = context.Document.Name,
                fileName = context.Document.FileName
            };
        }

        templateContext["run"] = new
        {
            id = context.RunId.ToString(),
            playbookId = context.PlaybookId.ToString(),
            tenantId = context.TenantId,
            userId = context.UserId?.ToString()
        };

        return templateContext;
    }
}

/// <summary>
/// Configuration for CreateNotification node from ConfigJson.
/// </summary>
internal sealed record NotificationNodeConfig
{
    /// <summary>Notification title (supports template variables).</summary>
    public string? Title { get; init; }

    /// <summary>Notification body text (supports template variables).</summary>
    public string? Body { get; init; }

    /// <summary>Category string for grouping and idempotency check.</summary>
    public string? Category { get; init; }

    /// <summary>
    /// Priority value: 100000000=Informational, 200000000=Important, 300000000=Urgent.
    /// Defaults to 200000000 (Important) when not specified.
    /// </summary>
    public int? Priority { get; init; }

    /// <summary>
    /// Dataverse appnotification <c>toasttype</c> option-set value: 100000000=Hidden (no toast),
    /// 200000000=Timed (auto-dismiss; default), 300000000=Standard (persistent).
    /// Per FR-18 (P3): when this value is Hidden, <c>data.actions[]</c> is NOT populated
    /// because the MDA native bell surface that would render the "Open" action is not shown.
    /// </summary>
    public int? ToastType { get; init; }

    /// <summary>Action URL to navigate when notification is clicked (supports template variables).</summary>
    public string? ActionUrl { get; init; }

    /// <summary>Regarding record ID (supports template variables).</summary>
    public string? RegardingId { get; init; }

    /// <summary>Regarding record entity logical name (e.g., "sprk_document").</summary>
    public string? RegardingType { get; init; }

    /// <summary>Recipient systemuserid (supports template variables). Falls back to run context userId.</summary>
    public string? RecipientId { get; init; }
    /// <summary>When true, iterate over items from upstream query output and create one notification per item.</summary>
    public bool IterateItems { get; init; }

    /// <summary>Notification template for each item when IterateItems is true. Supports {{item.fieldName}} variables.</summary>
    public NotificationNodeConfig? ItemNotification { get; init; }

    /// <summary>
    /// Optional ISO-8601 due-date string written into <c>appnotification.data.customData.dueDate</c>
    /// when present. Used by Daily Briefing consumers to render per-item due dates (R2.2).
    /// Supports template variables (e.g. <c>"{{item.scheduledend}}"</c>). Playbooks that don't
    /// emit a due date can omit this field — consumers render no due-date row when missing.
    /// </summary>
    public string? DueDate { get; init; }
}
