using System.Net;
using System.Text.Json;
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
/// </remarks>
public sealed class CreateNotificationNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Default priority for notifications when not specified in config (Important = 200000000).
    /// </summary>
    private const int DefaultPriority = 200_000_000;

    private readonly ITemplateEngine _templateEngine;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CreateNotificationNodeExecutor> _logger;

    public CreateNotificationNodeExecutor(
        ITemplateEngine templateEngine,
        IHttpClientFactory httpClientFactory,
        ILogger<CreateNotificationNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _httpClientFactory = httpClientFactory;
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

            // Build appnotification payload
            var notificationPayload = BuildNotificationPayload(
                title, body, category, config.Priority ?? DefaultPriority,
                actionUrl, recipientId.Value, regardingId, regardingType,
                context);

            // Create the notification via Dataverse Web API
            var notificationId = await CreateAppNotificationAsync(notificationPayload, cancellationToken);

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
    /// Resolves the recipient systemuserid from config template or run context.
    /// </summary>
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

            var payload = BuildNotificationPayload(title, body, category, itemConfig.Priority ?? 200_000_000, actionUrl, recipientId.Value, regardingId, regardingType, context);
            await CreateAppNotificationAsync(payload, cancellationToken);
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
            var client = _httpClientFactory.CreateClient("DataverseApi");

            // Query for unread notifications matching user + regarding + category
            // appnotification: statecode 0 = Active (unread)
            var filter = $"_ownerid_value eq '{recipientId}' " +
                         $"and sprk_category eq '{category}' " +
                         $"and _regardingobjectid_value eq '{regardingId}' " +
                         $"and statecode eq 0";

            var requestUrl = $"appnotifications?$filter={Uri.EscapeDataString(filter)}&$top=1&$select=activityid";

            var response = await client.GetAsync(requestUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Idempotency check failed with status {StatusCode} — proceeding with creation",
                    response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);

            var values = doc.RootElement.GetProperty("value");
            return values.GetArrayLength() > 0;
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
    /// Builds the appnotification OData payload for Dataverse Web API.
    /// </summary>
    private static Dictionary<string, object?> BuildNotificationPayload(
        string title,
        string body,
        string? category,
        int priority,
        string? actionUrl,
        Guid recipientId,
        Guid? regardingId,
        string? regardingType,
        NodeExecutionContext context)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["body"] = body,
            ["priority"] = priority,
            ["ownerid@odata.bind"] = $"/systemusers({recipientId})",
            ["ttlinseconds"] = 259200  // 3 days default TTL
        };

        // Add category (custom field for idempotency grouping)
        if (!string.IsNullOrWhiteSpace(category))
        {
            payload["sprk_category"] = category;
        }

        // Add action URL if specified
        if (!string.IsNullOrWhiteSpace(actionUrl))
        {
            payload["data"] = JsonSerializer.Serialize(new
            {
                type = "link",
                url = actionUrl
            });
        }

        // Add regarding object if specified
        if (regardingId.HasValue && !string.IsNullOrWhiteSpace(regardingType))
        {
            var entitySetName = GetEntitySetName(regardingType);
            payload[$"regardingobjectid_{regardingType}@odata.bind"] = $"/{entitySetName}({regardingId.Value})";
        }

        // Add AI metadata (playbook run info)
        payload["sprk_source"] = "playbook";
        payload["sprk_playbookrunid"] = context.RunId.ToString();

        return payload;
    }

    /// <summary>
    /// Creates an appnotification record via Dataverse Web API.
    /// </summary>
    private async Task<Guid> CreateAppNotificationAsync(
        Dictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("DataverseApi");

        var jsonContent = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("appnotifications", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Extract the created record ID from the OData-EntityId header
        if (response.Headers.TryGetValues("OData-EntityId", out var entityIdValues))
        {
            var entityIdUrl = entityIdValues.FirstOrDefault();
            if (entityIdUrl is not null)
            {
                // Format: https://{org}.crm.dynamics.com/api/data/v9.2/appnotifications({guid})
                var guidStart = entityIdUrl.LastIndexOf('(') + 1;
                var guidEnd = entityIdUrl.LastIndexOf(')');
                if (guidStart > 0 && guidEnd > guidStart)
                {
                    var guidStr = entityIdUrl[guidStart..guidEnd];
                    if (Guid.TryParse(guidStr, out var createdId))
                        return createdId;
                }
            }
        }

        // Fallback: return a new GUID if we can't extract the ID
        return Guid.NewGuid();
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
            tenantId = context.TenantId
        };

        return templateContext;
    }

    /// <summary>
    /// Gets the OData entity set name (plural) for a Dataverse entity.
    /// </summary>
    private static string GetEntitySetName(string entityLogicalName)
    {
        return entityLogicalName switch
        {
            "sprk_document" => "sprk_documents",
            "sprk_matter" => "sprk_matters",
            "sprk_project" => "sprk_projects",
            "account" => "accounts",
            "contact" => "contacts",
            "task" => "tasks",
            _ => entityLogicalName.EndsWith("s") ? entityLogicalName : entityLogicalName + "s"
        };
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
}
