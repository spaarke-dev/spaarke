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
    public IReadOnlyList<ExecutorType> SupportedExecutorTypes { get; } = new[]
    {
        ExecutorType.CreateNotification
    };

    // R7 task 032 / FR-16 — typed config schema for Playbook Builder canvas (Wave 8 FR-23).
    // Derived from this executor's ConfigJson consumption via NotificationNodeConfig record.
    // Required (per Validate): title, body. Optional core: category, priority, toastType,
    // actionUrl, regardingId, regardingType, recipientId, dueDate, iterateItems, itemNotification.
    // Optional FR-6 enrichment (R4 task 020): regardingName, source*, viaMatter*.
    // See projects/spaarke-ai-platform-unification-r7/notes/spikes/executor-config-fields-inventory.md §5.
    private static readonly ExecutorConfigSchema ConfigSchemaInstance = new(
        ExecutorTypeName: nameof(ExecutorType.CreateNotification),
        ExecutorTypeValue: (int)ExecutorType.CreateNotification,
        Description: "Creates a Dataverse appnotification record for the recipient. Supports template substitution in all text fields, idempotency by (user + regarding + category), and per-item iteration over upstream query results.",
        Fields: new ConfigSchemaField[]
        {
            new(
                Name: "title",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Notification title. Supports {{templateVars}} resolved against previous node outputs.",
                Default: null),
            new(
                Name: "body",
                Type: SchemaFieldType.String,
                Required: true,
                Description: "Notification body text. Supports {{templateVars}} resolved against previous node outputs.",
                Default: null),
            new(
                Name: "recipientId",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Recipient systemuserid (GUID). Supports templates. Falls back to run-context userId when not specified.",
                Default: null),
            new(
                Name: "category",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Category string for grouping and idempotency check (skip-if-unread-duplicate per user + regarding + category).",
                Default: null),
            new(
                Name: "priority",
                Type: SchemaFieldType.Number,
                Required: false,
                Description: "Priority: 100000000=Informational, 200000000=Important (default), 300000000=Urgent.",
                Default: 200000000),
            new(
                Name: "toastType",
                Type: SchemaFieldType.Number,
                Required: false,
                Description: "Toast visibility: 100000000=Hidden, 200000000=Timed (default), 300000000=Standard.",
                Default: 200000000),
            new(
                Name: "actionUrl",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "URL to navigate when the notification is clicked. Supports {{templateVars}}.",
                Default: null),
            new(
                Name: "regardingId",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Regarding record ID (GUID). Supports templates. Required for idempotency check.",
                Default: null),
            new(
                Name: "regardingType",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Regarding entity logical name (e.g., 'sprk_document', 'sprk_matter').",
                Default: null),
            new(
                Name: "dueDate",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "Optional ISO-8601 due date written into customData.dueDate. Supports templates (e.g., '{{item.scheduledend}}').",
                Default: null),
            new(
                Name: "iterateItems",
                Type: SchemaFieldType.Boolean,
                Required: false,
                Description: "When true, iterate over items from upstream query output and create one notification per item using itemNotification template.",
                Default: false),
            new(
                Name: "itemNotification",
                Type: SchemaFieldType.Object,
                Required: false,
                Description: "Per-item notification template (same shape as the top-level config) used when iterateItems is true. Supports {{item.field}} variables.",
                Default: null),
            new(
                Name: "regardingName",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "FR-6 enrichment: display name of the regarding entity. Written to customData.regardingName for widget grounding + FR-14 EntityNameValidator allow-list.",
                Default: null),
            new(
                Name: "sourceEntityType",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "FR-6 enrichment: source record entity logical name (e.g., 'sprk_event'). Written to customData.source.entityType.",
                Default: null),
            new(
                Name: "sourceId",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "FR-6 enrichment: source record GUID. Written to customData.source.id.",
                Default: null),
            new(
                Name: "sourceModifiedOn",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "FR-6 enrichment: source record modifiedon timestamp (ISO-8601). Written to customData.source.modifiedOn.",
                Default: null),
            new(
                Name: "sourceOwningUser",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "FR-6 enrichment: source record owning-user GUID. Written to customData.source.owningUser.",
                Default: null),
            new(
                Name: "viaMatterId",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "FR-6 enrichment: matter ID linking the source record. Written to customData.viaMatter.id when matter linkage exists; entire viaMatter object omitted otherwise.",
                Default: null),
            new(
                Name: "viaMatterName",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "FR-6 enrichment: display name of the matter. Written to customData.viaMatter.name when viaMatterId is present.",
                Default: null),
            new(
                Name: "viaMatterMembershipsVariable",
                Type: SchemaFieldType.String,
                Required: false,
                Description: "FR-6 enrichment: name of an upstream LookupUserMembership node's OutputVariable (default 'myMatters'). Used to project per-role memberships into customData.viaMatter.memberships[].",
                Default: "myMatters")
        });

    /// <inheritdoc />
    public ExecutorConfigSchema GetConfigSchema() => ConfigSchemaInstance;

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

            // FR-6 (R4 task 020): render enrichment template strings before passing to entity builder.
            // Each is null-safe — playbooks that don't supply the field skip the entire rendering pass.
            var regardingName = config.RegardingName is not null
                ? _templateEngine.Render(config.RegardingName, templateContext)
                : null;
            var sourceEntityType = config.SourceEntityType is not null
                ? _templateEngine.Render(config.SourceEntityType, templateContext)
                : null;
            var sourceId = config.SourceId is not null
                ? _templateEngine.Render(config.SourceId, templateContext)
                : null;
            var sourceModifiedOn = config.SourceModifiedOn is not null
                ? _templateEngine.Render(config.SourceModifiedOn, templateContext)
                : null;
            var sourceOwningUser = config.SourceOwningUser is not null
                ? _templateEngine.Render(config.SourceOwningUser, templateContext)
                : null;
            var viaMatterId = config.ViaMatterId is not null
                ? _templateEngine.Render(config.ViaMatterId, templateContext)
                : null;
            var viaMatterName = config.ViaMatterName is not null
                ? _templateEngine.Render(config.ViaMatterName, templateContext)
                : null;
            // viaMatter.memberships[] is sourced from an upstream LookupUserMembership node
            // output via `viaMatterMembershipsVariable` (the OutputVariable name; default
            // "myMatters" matches the canonical playbook pattern in PB-016). Memberships
            // are projected from `byRole` for the resolved matter ID per FR-6.
            var viaMatterMemberships = ResolveViaMatterMemberships(
                context,
                config.ViaMatterMembershipsVariable,
                viaMatterId);

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
                regardingName, sourceEntityType, sourceId, sourceModifiedOn, sourceOwningUser,
                viaMatterId, viaMatterName, viaMatterMemberships,
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

            // FR-6 enrichment (R4 task 020) — iterate-items path mirrors standard path.
            var regardingName = itemConfig.RegardingName is not null ? _templateEngine.Render(itemConfig.RegardingName, itemContext) : null;
            var sourceEntityType = itemConfig.SourceEntityType is not null ? _templateEngine.Render(itemConfig.SourceEntityType, itemContext) : null;
            var sourceId = itemConfig.SourceId is not null ? _templateEngine.Render(itemConfig.SourceId, itemContext) : null;
            var sourceModifiedOn = itemConfig.SourceModifiedOn is not null ? _templateEngine.Render(itemConfig.SourceModifiedOn, itemContext) : null;
            var sourceOwningUser = itemConfig.SourceOwningUser is not null ? _templateEngine.Render(itemConfig.SourceOwningUser, itemContext) : null;
            var viaMatterId = itemConfig.ViaMatterId is not null ? _templateEngine.Render(itemConfig.ViaMatterId, itemContext) : null;
            var viaMatterName = itemConfig.ViaMatterName is not null ? _templateEngine.Render(itemConfig.ViaMatterName, itemContext) : null;
            var viaMatterMemberships = ResolveViaMatterMemberships(
                context,
                itemConfig.ViaMatterMembershipsVariable,
                viaMatterId);

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
                dueDate,
                regardingName, sourceEntityType, sourceId, sourceModifiedOn, sourceOwningUser,
                viaMatterId, viaMatterName, viaMatterMemberships,
                context);
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
            query.Criteria.AddCondition("sprk_regardingid", ConditionOperator.Equal, regardingId.ToString());
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
        // FR-6 (R4 task 020): enrichment scalars + viaMatter projection.
        // All inputs are optional — playbooks not yet migrated to enriched shape produce the
        // legacy customData payload unchanged (AC-6b backward compat).
        string? regardingName,
        string? sourceEntityType,
        string? sourceId,
        string? sourceModifiedOn,
        string? sourceOwningUser,
        string? viaMatterId,
        string? viaMatterName,
        IReadOnlyList<object>? viaMatterMemberships,
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

        // FR-18 (P3) + FR-6 (R4 task 020): build appnotification.data payload.
        // - customData.actionUrl is populated regardless of toasttype (Daily Briefing UI consumer).
        // - customData.dueDate is populated when provided (R2.2 — Daily Briefing per-item due-date UX).
        // - data.actions[] is populated ONLY when actionUrl is present AND toasttype != Hidden
        //   (so MDA native bell icon shows a clickable "Open" button).
        // - FR-6 enriched fields (regardingName, regardingEntityType, regardingId, viaMatter, source)
        //   are added conditionally so old-shape playbooks remain backward compatible (AC-6b).
        // - viaMatter is OMITTED entirely when no matter linkage (AC-6 / FR-6 requirement: omit, not null).
        // - Payload typical <2KB, hard ceiling <10KB (AC-6c). Memberships array is the only field
        //   that can grow; we don't truncate here — upstream LookupUserMembership already caps via
        //   MembershipResolveOptions.DefaultLimit.
        // R2.2: data is built whenever actionUrl OR dueDate is present (was: only actionUrl).
        // R4 task 020: also build data when ANY FR-6 enrichment scalar is present (e.g., notifications
        // with regarding info but no URL).
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

            // FR-6: viaMatter object — present ONLY when matter linkage exists. Per FR-6 + AC-6b,
            // omit field entirely (not null) when source-record has no matter linkage.
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

        // Add AI metadata (playbook run info)
        entity["sprk_source"] = "playbook";
        entity["sprk_playbookrunid"] = context.RunId.ToString();

        return entity;
    }

    /// <summary>
    /// FR-6 (R4 task 020): Projects the viaMatter.memberships[] array for the resolved
    /// matter ID by reading the upstream LookupUserMembership node's StructuredData output
    /// (bound to <paramref name="membershipVariable"/>, default "myMatters").
    /// </summary>
    /// <remarks>
    /// <para>
    /// The LookupUserMembership executor produces:
    /// <code>
    /// {
    ///   "byRole": { "owner": ["matterId1", ...], "assignedAttorney": ["matterId1", ...] }
    /// }
    /// </code>
    /// We project by iterating <c>byRole</c> keys and emitting a one-entry-per-role list
    /// for the matter IDs that match the resolved <paramref name="viaMatterId"/>. When the
    /// upstream node is absent, the matter is not in the bucket, or the membership variable
    /// is not configured, returns null — the caller then omits <c>viaMatter</c> entirely
    /// (per FR-6 omission rule).
    /// </para>
    /// </remarks>
    private static IReadOnlyList<object>? ResolveViaMatterMemberships(
        NodeExecutionContext context,
        string? membershipVariable,
        string? viaMatterId)
    {
        if (string.IsNullOrWhiteSpace(viaMatterId))
        {
            return null;
        }

        // Default variable name matches canonical PB-016 playbook pattern.
        var variableName = string.IsNullOrWhiteSpace(membershipVariable)
            ? "myMatters"
            : membershipVariable.Trim();

        if (!context.PreviousOutputs.TryGetValue(variableName, out var output)
            || !output.StructuredData.HasValue)
        {
            return null;
        }

        try
        {
            var data = output.StructuredData.Value;
            if (!data.TryGetProperty("byRole", out var byRoleProp)
                || byRoleProp.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var memberships = new List<object>();
            foreach (var roleEntry in byRoleProp.EnumerateObject())
            {
                if (roleEntry.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                // Match the matter ID inside this role's array (case-insensitive Guid string compare).
                foreach (var idElement in roleEntry.Value.EnumerateArray())
                {
                    if (idElement.ValueKind != JsonValueKind.String) continue;
                    var idStr = idElement.GetString();
                    if (string.Equals(idStr, viaMatterId, StringComparison.OrdinalIgnoreCase))
                    {
                        memberships.Add(new Dictionary<string, object?>
                        {
                            ["role"] = roleEntry.Name
                        });
                        break; // one entry per role; don't duplicate if list contains duplicates
                    }
                }
            }

            return memberships.Count > 0 ? memberships : null;
        }
        catch
        {
            // Defensive — malformed upstream output is not fatal; viaMatter is omitted gracefully.
            return null;
        }
    }

    /// <summary>
    /// Builds template context dictionary from previous node outputs and execution metadata.
    /// </summary>
    private static Dictionary<string, object?> BuildTemplateContext(NodeExecutionContext context)
    {
        var templateContext = new Dictionary<string, object?>();

        foreach (var (varName, output) in context.PreviousOutputs)
        {
            // 2026-06-24 (bug #10 fix): use TemplateEngine.ConvertJsonElement so Handlebars
            // can navigate nested paths like {{varName.output.count}}. See full rationale on
            // the matching change in ConditionNodeExecutor.BuildTemplateContext.
            templateContext[varName] = new
            {
                output = output.StructuredData.HasValue
                    ? TemplateEngine.ConvertJsonElement(output.StructuredData.Value)
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

    // -- FR-6 enrichment (R4 task 020) -----------------------------------------------------
    // All fields below are OPTIONAL — playbooks not yet migrated to enriched shape skip them
    // entirely. Backward-compatible per AC-6b. Each supports template variables.

    /// <summary>
    /// Display name of the regarding entity (e.g., matter name). Written to
    /// <c>customData.regardingName</c>. Used by widget grounding + FR-14 EntityNameValidator
    /// allow-list. Supports template variables (e.g. <c>"{{item.matterName}}"</c>).
    /// </summary>
    public string? RegardingName { get; init; }

    /// <summary>
    /// Source record entity logical name (e.g., <c>sprk_document</c>, <c>sprk_event</c>).
    /// Written to <c>customData.source.entityType</c>. Captures originating record identity
    /// for FR-6 widget grounding. Supports template variables.
    /// </summary>
    public string? SourceEntityType { get; init; }

    /// <summary>
    /// Source record ID (Guid string). Written to <c>customData.source.id</c>. Supports
    /// template variables (e.g. <c>"{{item.id}}"</c>).
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Source record <c>modifiedon</c> timestamp (ISO-8601). Written to
    /// <c>customData.source.modifiedOn</c>. Supports template variables.
    /// </summary>
    public string? SourceModifiedOn { get; init; }

    /// <summary>
    /// Source record owning user GUID. Written to <c>customData.source.owningUser</c>.
    /// Supports template variables.
    /// </summary>
    public string? SourceOwningUser { get; init; }

    /// <summary>
    /// Matter ID the source record is regarding. When present + matter linkage exists,
    /// written to <c>customData.viaMatter.id</c>. When absent, the entire <c>viaMatter</c>
    /// field is OMITTED from customData (per FR-6 / AC-6 omission rule — not null).
    /// Supports template variables (e.g. <c>"{{item.regardingMatterId}}"</c>).
    /// </summary>
    public string? ViaMatterId { get; init; }

    /// <summary>
    /// Display name of the matter. Written to <c>customData.viaMatter.name</c> when
    /// <see cref="ViaMatterId"/> is present. Supports template variables.
    /// </summary>
    public string? ViaMatterName { get; init; }

    /// <summary>
    /// Name of the upstream <c>LookupUserMembership</c> node's OutputVariable
    /// (default "myMatters" matches canonical PB-016 pattern). The executor reads this
    /// variable's StructuredData.byRole bucket, finds entries matching <see cref="ViaMatterId"/>,
    /// and emits one membership-array entry per role to <c>customData.viaMatter.memberships[]</c>.
    /// Per FR-6: multi-role case (owner + assignedAttorney) produces multiple array entries.
    /// </summary>
    public string? ViaMatterMembershipsVariable { get; init; }
}
