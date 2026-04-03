using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Node executor for querying Dataverse via FetchXML from playbook execution.
/// Resolves template variables in FetchXML (date tokens, user references),
/// executes the query via OData Web API, and returns structured results
/// for downstream condition/notification nodes.
/// </summary>
public sealed class QueryDataverseNodeExecutor : INodeExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const int DefaultDueSoonDays = 7;
    private const int DefaultTimeWindowHours = 24;

    private readonly ITemplateEngine _templateEngine;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<QueryDataverseNodeExecutor> _logger;

    public QueryDataverseNodeExecutor(
        ITemplateEngine templateEngine,
        IHttpClientFactory httpClientFactory,
        ILogger<QueryDataverseNodeExecutor> logger)
    {
        _templateEngine = templateEngine;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionType> SupportedActionTypes { get; } = new[]
    {
        ActionType.QueryDataverse
    };

    /// <inheritdoc />
    public NodeValidationResult Validate(NodeExecutionContext context)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.Node.ConfigJson))
        {
            errors.Add("QueryDataverse node requires configuration (ConfigJson)");
            return NodeValidationResult.Failure(errors.ToArray());
        }

        try
        {
            var config = JsonSerializer.Deserialize<QueryDataverseNodeConfig>(context.Node.ConfigJson, JsonOptions);
            if (config is null)
                errors.Add("Failed to parse query configuration");
            else
            {
                if (string.IsNullOrWhiteSpace(config.EntityLogicalName))
                    errors.Add("EntityLogicalName is required");
                if (string.IsNullOrWhiteSpace(config.FetchXml))
                    errors.Add("FetchXml is required");
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid query configuration JSON: {ex.Message}");
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
            "Executing QueryDataverse node {NodeId} ({NodeName})",
            context.Node.Id,
            context.Node.Name);

        try
        {
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

            var config = JsonSerializer.Deserialize<QueryDataverseNodeConfig>(context.Node.ConfigJson!, JsonOptions)!;
            var userId = ResolveUserId(context);
            var resolvedFetchXml = ResolveFetchXmlVariables(config.FetchXml!, config.Parameters, userId);

            if (userId is not null)
                resolvedFetchXml = ReplaceEqUserIdOperator(resolvedFetchXml, userId);

            _logger.LogDebug(
                "QueryDataverse node {NodeId} -- entity: {Entity}, fetchXml length: {FetchXmlLength}",
                context.Node.Id,
                config.EntityLogicalName,
                resolvedFetchXml.Length);

            var results = await ExecuteFetchXmlAsync(
                config.EntityLogicalName!,
                resolvedFetchXml,
                cancellationToken);

            _logger.LogInformation(
                "QueryDataverse node {NodeId} completed -- entity: {Entity}, results: {ResultCount}",
                context.Node.Id,
                config.EntityLogicalName,
                results.Count);

            return NodeOutput.Ok(
                context.Node.Id,
                context.Node.OutputVariable,
                new
                {
                    count = results.Count,
                    items = results,
                    entityLogicalName = config.EntityLogicalName
                },
                textContent: $"Query returned {results.Count} records",
                metrics: NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QueryDataverse node {NodeId} failed: {ErrorMessage}", context.Node.Id, ex.Message);
            return NodeOutput.Error(
                context.Node.Id,
                context.Node.OutputVariable,
                $"Failed to execute Dataverse query: {ex.Message}",
                NodeErrorCodes.InternalError,
                NodeExecutionMetrics.Timed(startedAt, DateTimeOffset.UtcNow));
        }
    }

    private static string? ResolveUserId(NodeExecutionContext context)
    {
        // Primary: UserId set directly on the execution context by PlaybookSchedulerService
        if (context.UserId.HasValue)
            return context.UserId.Value.ToString();

        // Fallback: check previous outputs for userId
        foreach (var (_, output) in context.PreviousOutputs)
        {
            if (output.StructuredData.HasValue)
            {
                try
                {
                    var data = output.StructuredData.Value;
                    if (data.TryGetProperty("userId", out var userIdProp) &&
                        userIdProp.ValueKind == JsonValueKind.String)
                        return userIdProp.GetString();
                }
                catch { }
            }
        }
        return null;
    }

    private static string ResolveFetchXmlVariables(string fetchXml, QueryParameters? parameters, string? userId)
    {
        var now = DateTime.UtcNow;
        var dueSoonDays = parameters?.DueSoonDays ?? DefaultDueSoonDays;
        var timeWindowHours = parameters?.TimeWindowHours ?? DefaultTimeWindowHours;
        var resolved = fetchXml;
        resolved = resolved.Replace("{{todayUtc}}", now.ToString("yyyy-MM-dd"));
        resolved = resolved.Replace("{{dueSoonWindowUtc}}", now.AddDays(dueSoonDays).ToString("yyyy-MM-dd"));
        resolved = resolved.Replace("{{timeWindowHours}}", timeWindowHours.ToString());
        resolved = resolved.Replace("{{timeWindowStartUtc}}", now.AddHours(-timeWindowHours).ToString("yyyy-MM-ddTHH:mm:ssZ"));
        if (userId is not null)
            resolved = resolved.Replace("{{run.userId}}", userId);
        return resolved;
    }

    private static string ReplaceEqUserIdOperator(string fetchXml, string userId)
    {
        var result = fetchXml;
        result = result.Replace("operator=\"eq-userid\"", $"operator=\"eq\" value=\"{userId}\"");
        result = result.Replace("operator='eq-userid'", $"operator='eq' value='{userId}'");
        result = result.Replace("operator=\"ne-userid\"", $"operator=\"ne\" value=\"{userId}\"");
        result = result.Replace("operator='ne-userid'", $"operator='ne' value='{userId}'");
        return result;
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteFetchXmlAsync(
        string entityLogicalName,
        string fetchXml,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("DataverseApi");
        var entitySetName = GetEntitySetName(entityLogicalName);
        var encodedFetchXml = Uri.EscapeDataString(fetchXml);
        var requestUrl = $"{entitySetName}?fetchXml={encodedFetchXml}";

        _logger.LogDebug("Executing FetchXML query against {EntitySet}", entitySetName);

        var response = await client.GetAsync(requestUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "FetchXML query failed with status {StatusCode}: {ErrorContent}",
                response.StatusCode,
                errorContent.Length > 500 ? errorContent[..500] : errorContent);

            throw new HttpRequestException(
                $"Dataverse FetchXML query failed with status {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(content);
        var results = new List<Dictionary<string, object?>>();

        if (doc.RootElement.TryGetProperty("value", out var valueArray))
        {
            foreach (var item in valueArray.EnumerateArray())
            {
                var record = new Dictionary<string, object?>();
                foreach (var prop in item.EnumerateObject())
                {
                    if (prop.Name.StartsWith("@odata.") || prop.Name.StartsWith("@Microsoft."))
                        continue;

                    record[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }
                results.Add(record);
            }
        }

        return results;
    }

    private static string GetEntitySetName(string entityLogicalName)
    {
        return entityLogicalName switch
        {
            "sprk_event" => "sprk_events",
            "sprk_document" => "sprk_documents",
            "sprk_matter" => "sprk_matters",
            "sprk_project" => "sprk_projects",
            "sprk_todoitem" => "sprk_todoitems",
            "account" => "accounts",
            "contact" => "contacts",
            "task" => "tasks",
            "email" => "emails",
            "appointment" => "appointments",
            "appnotification" => "appnotifications",
            _ => entityLogicalName.EndsWith("s") ? entityLogicalName : entityLogicalName + "s"
        };
    }
}

internal sealed record QueryDataverseNodeConfig
{
    [JsonPropertyName("entityLogicalName")]
    public string? EntityLogicalName { get; init; }

    [JsonPropertyName("fetchXml")]
    public string? FetchXml { get; init; }

    [JsonPropertyName("parameters")]
    public QueryParameters? Parameters { get; init; }
}

internal sealed record QueryParameters
{
    [JsonPropertyName("dueSoonDays")]
    public int? DueSoonDays { get; init; }

    [JsonPropertyName("timeWindowHours")]
    public int? TimeWindowHours { get; init; }
}
