using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing structured Dataverse entity query capabilities to the SprkChatAgent.
///
/// Exposes two query methods:
///   - <see cref="QueryEntitiesAsync"/> — executes an OData filter query against a supported
///     entity type and returns a paged result set of entity summaries (id, display name, key fields).
///   - <see cref="GetEntityDetailAsync"/> — fetches the full detail record for a single entity
///     by type and GUID identifier.
///
/// Both methods enforce caller-scoped authorization: Dataverse requests are made using the
/// authenticated user's OBO token extracted from <see cref="HttpContext"/>. The AI can only
/// query data the authenticated user is permitted to see — no service-account elevation.
///
/// Entity type allow-list: sprk_matter, sprk_project, contact, account, sprk_document.
/// Requests for unsupported entity types return a descriptive error string (not an exception).
///
/// The <see cref="QueryEntitiesAsync"/> <c>top</c> parameter is silently capped at 50 to prevent
/// expensive queries from exhausting Dataverse API quota (ADR-016).
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// objects via <see cref="Microsoft.Extensions.AI.AIFunctionFactory.Create"/>.
/// (ADR-010: 0 additional DI registrations.)
///
/// Integration into <see cref="SprkChatAgentFactory.ResolveTools"/> is handled in task 061.
/// </summary>
public sealed class DataverseQueryTools
{
    private const int MaxTop = 50;
    private const int DefaultTop = 10;

    /// <summary>
    /// Entity types that the AI is permitted to query.
    /// Any request for an entity type not in this set is rejected with a descriptive error.
    /// </summary>
    private static readonly IReadOnlySet<string> AllowedEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_matter",
        "sprk_project",
        "contact",
        "account",
        "sprk_document"
    };

    /// <summary>
    /// OData entity set names that correspond to each allowed logical entity type.
    /// Dataverse OData uses the plural collection name (e.g., "contacts", "accounts").
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> EntitySetNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"]    = "sprk_matters",
            ["sprk_project"]   = "sprk_projects",
            ["contact"]        = "contacts",
            ["account"]        = "accounts",
            ["sprk_document"]  = "sprk_documents"
        };

    /// <summary>
    /// Fields to select for summary results in <see cref="QueryEntitiesAsync"/>.
    /// Each entry maps entity type → OData $select list.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> SummaryFields =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"]   = ["sprk_matterid", "sprk_name", "sprk_status", "sprk_mattertype", "sprk_clientid"],
            ["sprk_project"]  = ["sprk_projectid", "sprk_name", "sprk_status", "sprk_matterlookup"],
            ["contact"]       = ["contactid", "fullname", "emailaddress1", "jobtitle"],
            ["account"]       = ["accountid", "name", "telephone1", "websiteurl"],
            ["sprk_document"] = ["sprk_documentid", "sprk_name", "sprk_documenttype", "sprk_status"]
        };

    /// <summary>
    /// Primary ID field for each entity type (used when displaying records and for GetEntityDetail).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PrimaryIdFields =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"]   = "sprk_matterid",
            ["sprk_project"]  = "sprk_projectid",
            ["contact"]       = "contactid",
            ["account"]       = "accountid",
            ["sprk_document"] = "sprk_documentid"
        };

    /// <summary>
    /// Display name field for each entity type (used in summary formatting).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DisplayNameFields =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"]   = "sprk_name",
            ["sprk_project"]  = "sprk_name",
            ["contact"]       = "fullname",
            ["account"]       = "name",
            ["sprk_document"] = "sprk_name"
        };

    private readonly HttpClient _httpClient;
    private readonly HttpContext? _httpContext;
    private readonly string _dataverseBaseUrl;
    private readonly ILogger<DataverseQueryTools> _logger;

    /// <summary>
    /// Creates a new <see cref="DataverseQueryTools"/> instance.
    /// </summary>
    /// <param name="httpClient">
    /// Named HttpClient for Dataverse OData Web API calls.
    /// The factory wires the base URL; this class sets the Authorization header per-request
    /// using the caller's OBO token extracted from <paramref name="httpContext"/>.
    /// </param>
    /// <param name="dataverseOptions">
    /// Dataverse environment configuration — provides the environment URL for OData calls.
    /// </param>
    /// <param name="httpContext">
    /// HTTP context for OBO authentication. The caller's bearer token is extracted and
    /// forwarded as-is on Dataverse OData requests, ensuring Dataverse enforces its own
    /// row-level security without any permission elevation.
    /// May be null for non-HTTP contexts (e.g., background processing); both tool methods
    /// return an authorization-unavailable error when null.
    /// </param>
    /// <param name="logger">Logger for structured diagnostics (identifiers only — ADR-015).</param>
    public DataverseQueryTools(
        HttpClient httpClient,
        IOptions<DataverseOptions> dataverseOptions,
        HttpContext? httpContext,
        ILogger<DataverseQueryTools> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ = dataverseOptions ?? throw new ArgumentNullException(nameof(dataverseOptions));
        _dataverseBaseUrl = dataverseOptions.Value.EnvironmentUrl.TrimEnd('/');
        _httpContext = httpContext;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Queries Dataverse for entity records matching an optional OData filter expression.
    /// Returns a structured list of entity summaries — id, display name, and key fields
    /// defined per entity type. Supported entity types: sprk_matter, sprk_project, contact,
    /// account, sprk_document. The <paramref name="top"/> parameter is silently capped at 50
    /// to prevent expensive queries.
    ///
    /// Use this tool when the user asks to list, find, or search for Dataverse records —
    /// for example: "show me open matters", "find contacts named Smith",
    /// "list projects in active status".
    /// </summary>
    /// <param name="entityType">
    /// Dataverse entity type to query. Must be one of: sprk_matter, sprk_project,
    /// contact, account, sprk_document.
    /// </param>
    /// <param name="filter">
    /// Optional OData $filter expression (e.g., "sprk_status eq 'Active'").
    /// When null or empty, all records up to <paramref name="top"/> are returned.
    /// </param>
    /// <param name="top">
    /// Maximum number of records to return. Defaults to 10, capped at 50.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Formatted string listing matching entity records with their key fields,
    /// or a descriptive error message if the entity type is unsupported or the
    /// caller has no auth context available.
    /// </returns>
    public async Task<string> QueryEntitiesAsync(
        [Description("Dataverse entity type to query: sprk_matter, sprk_project, contact, account, or sprk_document")]
        string entityType,
        [Description("Optional OData $filter expression, e.g. \"sprk_status eq 'Active'\" or \"contains(fullname,'Smith')\"")]
        string? filter = null,
        [Description("Maximum number of records to return (default 10, max 50)")]
        int top = DefaultTop,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType, nameof(entityType));

        if (!AllowedEntityTypes.Contains(entityType))
        {
            return $"Unsupported entity type '{entityType}'. " +
                   $"Allowed types are: {string.Join(", ", AllowedEntityTypes)}.";
        }

        if (_httpContext == null)
        {
            return "Dataverse query is unavailable: no HTTP context for authorization. " +
                   "This tool requires an active authenticated session.";
        }

        // Cap top silently (ADR-016: prevent expensive queries from exhausting quota)
        var effectiveTop = Math.Clamp(top, 1, MaxTop);

        var entitySetName = EntitySetNames[entityType];
        var selectFields  = SummaryFields[entityType];
        var selectClause  = string.Join(",", selectFields);

        // Build OData query URL
        var queryUrl = $"{_dataverseBaseUrl}/api/data/v9.2/{entitySetName}?$select={selectClause}&$top={effectiveTop}";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            queryUrl += $"&$filter={Uri.EscapeDataString(filter)}";
        }

        _logger.LogInformation(
            "DataverseQueryTools.QueryEntities: entityType={EntityType}, top={Top}, hasFilter={HasFilter}",
            entityType, effectiveTop, !string.IsNullOrWhiteSpace(filter));

        try
        {
            var token = TokenHelper.ExtractBearerToken(_httpContext);
            using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "DataverseQueryTools.QueryEntities: access denied for entityType={EntityType}, status={Status}",
                    entityType, (int)response.StatusCode);
                return $"Access denied querying '{entityType}'. " +
                       "The current user does not have permission to read these records.";
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "DataverseQueryTools.QueryEntities: Dataverse returned {Status} for entityType={EntityType}",
                    (int)response.StatusCode, entityType);
                return $"Dataverse returned an error ({(int)response.StatusCode}) querying '{entityType}'. " +
                       "Please try again or refine your filter.";
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("value", out var valueArray) ||
                valueArray.GetArrayLength() == 0)
            {
                return $"No '{entityType}' records found" +
                       (string.IsNullOrWhiteSpace(filter) ? "." : $" matching the filter: {filter}");
            }

            var idField      = PrimaryIdFields[entityType];
            var nameField    = DisplayNameFields[entityType];
            var sb           = new System.Text.StringBuilder();
            var recordCount  = valueArray.GetArrayLength();

            sb.AppendLine($"Found {recordCount} '{entityType}' record(s)" +
                          (string.IsNullOrWhiteSpace(filter) ? ":" : $" matching '{filter}':"));
            sb.AppendLine();

            foreach (var record in valueArray.EnumerateArray())
            {
                var id          = GetStringField(record, idField);
                var displayName = GetStringField(record, nameField);

                sb.AppendLine($"  ID: {id}");
                sb.AppendLine($"  Name: {displayName}");

                // Append remaining key fields (skip id and name — already shown)
                foreach (var field in selectFields)
                {
                    if (string.Equals(field, idField, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(field, nameField, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = GetStringField(record, field);
                    if (!string.IsNullOrEmpty(value))
                    {
                        sb.AppendLine($"  {field}: {value}");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "DataverseQueryTools.QueryEntities: authorization extraction failed for entityType={EntityType}",
                entityType);
            return "Unable to query Dataverse: authorization token is missing or invalid.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DataverseQueryTools.QueryEntities: unexpected error for entityType={EntityType}",
                entityType);
            return $"An unexpected error occurred while querying '{entityType}'. Please try again.";
        }
    }

    /// <summary>
    /// Retrieves the full detail record for a single Dataverse entity by its GUID identifier.
    /// Returns all non-system fields as a formatted key-value listing.
    /// Returns a not-found result (not an exception) when no record matches the identifier.
    ///
    /// Use this tool when the user wants to see the full details of a specific record —
    /// for example: "show me the details of matter {id}", "what are the fields on contact {id}".
    /// </summary>
    /// <param name="entityType">
    /// Dataverse entity type to retrieve. Must be one of: sprk_matter, sprk_project,
    /// contact, account, sprk_document.
    /// </param>
    /// <param name="identifier">
    /// GUID identifier of the record (e.g., "a1b2c3d4-0000-0000-0000-000000000000").
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Formatted string of all accessible fields for the record, a not-found message when
    /// the record does not exist, or a descriptive error message for auth/other failures.
    /// </returns>
    public async Task<string> GetEntityDetailAsync(
        [Description("Dataverse entity type: sprk_matter, sprk_project, contact, account, or sprk_document")]
        string entityType,
        [Description("GUID identifier of the record, e.g. \"a1b2c3d4-0000-0000-0000-000000000000\"")]
        string identifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType, nameof(entityType));
        ArgumentException.ThrowIfNullOrEmpty(identifier, nameof(identifier));

        if (!AllowedEntityTypes.Contains(entityType))
        {
            return $"Unsupported entity type '{entityType}'. " +
                   $"Allowed types are: {string.Join(", ", AllowedEntityTypes)}.";
        }

        if (_httpContext == null)
        {
            return "Dataverse query is unavailable: no HTTP context for authorization. " +
                   "This tool requires an active authenticated session.";
        }

        // Validate GUID format before hitting the network
        if (!Guid.TryParse(identifier, out var recordGuid))
        {
            return $"Invalid identifier format '{identifier}'. Expected a GUID (e.g., a1b2c3d4-0000-0000-0000-000000000000).";
        }

        var entitySetName = EntitySetNames[entityType];
        var queryUrl      = $"{_dataverseBaseUrl}/api/data/v9.2/{entitySetName}({recordGuid:D})";

        _logger.LogInformation(
            "DataverseQueryTools.GetEntityDetail: entityType={EntityType}, recordId={RecordId}",
            entityType, recordGuid);

        try
        {
            var token = TokenHelper.ExtractBearerToken(_httpContext);
            using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return $"No '{entityType}' record found with ID '{identifier}'.";
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "DataverseQueryTools.GetEntityDetail: access denied for entityType={EntityType}, recordId={RecordId}, status={Status}",
                    entityType, recordGuid, (int)response.StatusCode);
                return $"Access denied reading '{entityType}' record '{identifier}'. " +
                       "The current user does not have permission to access this record.";
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "DataverseQueryTools.GetEntityDetail: Dataverse returned {Status} for entityType={EntityType}, recordId={RecordId}",
                    (int)response.StatusCode, entityType, recordGuid);
                return $"Dataverse returned an error ({(int)response.StatusCode}) retrieving '{entityType}' record '{identifier}'.";
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            return FormatEntityDetail(entityType, identifier, doc.RootElement);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "DataverseQueryTools.GetEntityDetail: authorization extraction failed for entityType={EntityType}, recordId={RecordId}",
                entityType, recordGuid);
            return "Unable to query Dataverse: authorization token is missing or invalid.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DataverseQueryTools.GetEntityDetail: unexpected error for entityType={EntityType}, recordId={RecordId}",
                entityType, recordGuid);
            return $"An unexpected error occurred while retrieving '{entityType}' record '{identifier}'. Please try again.";
        }
    }

    // === Private helpers ===

    /// <summary>
    /// Formats a full Dataverse entity record into a human-readable key-value listing.
    /// System metadata fields (prefixed with "@odata" or "_") are excluded to keep the
    /// output focused on business data (ADR-015: minimum data exposure).
    /// </summary>
    private static string FormatEntityDetail(string entityType, string identifier, JsonElement record)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"'{entityType}' record (ID: {identifier}):");
        sb.AppendLine();

        foreach (var property in record.EnumerateObject())
        {
            // Skip OData metadata annotations and navigation property links
            if (property.Name.StartsWith("@odata", StringComparison.OrdinalIgnoreCase) ||
                property.Name.StartsWith("_", StringComparison.Ordinal) ||
                property.Name.EndsWith("@OData.Community.Display.V1.FormattedValue", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = property.Value.ValueKind switch
            {
                JsonValueKind.Null      => "(null)",
                JsonValueKind.String    => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number    => property.Value.GetRawText(),
                JsonValueKind.True      => "true",
                JsonValueKind.False     => "false",
                _                       => property.Value.GetRawText()
            };

            sb.AppendLine($"  {property.Name}: {value}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Safely reads a string field from a JSON element, returning an empty string when absent or null.
    /// </summary>
    private static string GetStringField(JsonElement element, string fieldName)
    {
        if (element.TryGetProperty(fieldName, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString() ?? string.Empty,
                JsonValueKind.Null   => string.Empty,
                _                    => prop.GetRawText()
            };
        }

        return string.Empty;
    }
}
