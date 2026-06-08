using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.Core;
using Json.Schema;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Focused service for AnalysisTool CRUD operations against Dataverse.
/// Extracted from ScopeResolverService as part of god-class decomposition (PPI-054).
/// </summary>
public class AnalysisToolService : DataverseHttpServiceBase
{
    public AnalysisToolService(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<AnalysisToolService> logger)
        : base(httpClient, configuration, credential, logger)
    {
    }

    /// <summary>
    /// Get a tool by ID.
    /// </summary>
    public async Task<AnalysisTool?> GetToolAsync(Guid toolId, CancellationToken cancellationToken)
    {
        Logger.LogDebug("Loading tool {ToolId} from Dataverse", toolId);

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysistools({toolId})?$expand=sprk_ToolTypeId($select=sprk_name)";
        var response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("Tool {ToolId} not found in Dataverse", toolId);
            return null;
        }

        await EnsureSuccessWithDiagnosticsAsync(response, $"GetToolAsync({toolId})", cancellationToken);

        var entity = await response.Content.ReadFromJsonAsync<ToolEntity>(cancellationToken);
        if (entity == null)
        {
            Logger.LogWarning("Failed to deserialize tool {ToolId} from Dataverse response", toolId);
            return null;
        }

        var handlerClass = !string.IsNullOrWhiteSpace(entity.HandlerClass)
            ? entity.HandlerClass
            : "GenericAnalysisHandler";

        var toolType = !string.IsNullOrEmpty(entity.HandlerClass)
            ? MapHandlerClassToToolType(entity.HandlerClass)
            : MapToolTypeName(entity.ToolTypeId?.Name ?? "");

        var tool = new AnalysisTool
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Tool",
            Description = entity.Description,
            Type = toolType,
            HandlerClass = handlerClass,
            Configuration = entity.Configuration,
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false,
            AvailableInContexts = MapAvailableInContexts(entity.AvailableInContexts),
            JsonSchema = MapJsonSchema(entity.JsonSchema, entity.Id, Logger)
        };

        var mappingSource = !string.IsNullOrEmpty(entity.HandlerClass) ? "HandlerClass" : "GenericAnalysisHandler (fallback)";
        Logger.LogInformation("Loaded tool from Dataverse: {ToolName} (Type: {ToolType}, MappedFrom: {MappingSource}, HandlerClass: {HandlerClass})",
            tool.Name, tool.Type, mappingSource, handlerClass);

        return tool;
    }

    /// <summary>
    /// List all available tools with pagination.
    /// </summary>
    public async Task<ScopeListResult<AnalysisTool>> ListToolsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("[LIST TOOLS] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}",
            options.Page, options.PageSize, options.NameFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["type"] = "sprk_name"
        };

        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysistoolid,sprk_name,sprk_description,sprk_handlerclass,sprk_configuration,sprk_availableincontexts,sprk_jsonschema",
            expandClause: "sprk_ToolTypeId($select=sprk_name)",
            nameFieldPath: "sprk_name",
            categoryFieldPath: null,
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysistools?{query}";
        Logger.LogDebug("[LIST TOOLS] Query URL: {Url}", url);

        var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<ToolEntity>>(cancellationToken);
        if (result == null)
        {
            Logger.LogWarning("[LIST TOOLS] Failed to deserialize response");
            return new ScopeListResult<AnalysisTool>
            {
                Items = [],
                TotalCount = 0,
                Page = options.Page,
                PageSize = options.PageSize
            };
        }

        var tools = result.Value.Select(entity =>
        {
            var toolType = !string.IsNullOrEmpty(entity.HandlerClass)
                ? MapHandlerClassToToolType(entity.HandlerClass)
                : MapToolTypeName(entity.ToolTypeId?.Name ?? "");

            return new AnalysisTool
            {
                Id = entity.Id,
                Name = entity.Name ?? "Unnamed Tool",
                Description = entity.Description,
                Type = toolType,
                HandlerClass = entity.HandlerClass,
                Configuration = entity.Configuration,
                OwnerType = ScopeOwnerType.System,
                IsImmutable = false,
                AvailableInContexts = MapAvailableInContexts(entity.AvailableInContexts),
                JsonSchema = MapJsonSchema(entity.JsonSchema, entity.Id, Logger)
            };
        }).ToArray();

        Logger.LogInformation("[LIST TOOLS] Retrieved {Count} tools from Dataverse (Total: {TotalCount})",
            tools.Length, result.ODataCount ?? tools.Length);

        return new ScopeListResult<AnalysisTool>
        {
            Items = tools,
            TotalCount = result.ODataCount ?? tools.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    /// <summary>
    /// Create a new analysis tool.
    /// </summary>
    public async Task<AnalysisTool> CreateToolAsync(CreateToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));

        var name = EnsureCustomerPrefix(request.Name);

        Logger.LogInformation("[CREATE TOOL] Creating tool '{ToolName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_handlerclass"] = request.HandlerClass,
            ["sprk_configuration"] = request.Configuration
        };

        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysistools")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await Http.SendAsync(postRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<ToolEntity>(cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException("Failed to deserialize created tool from Dataverse response");
        }

        var toolType = !string.IsNullOrEmpty(entity.HandlerClass)
            ? MapHandlerClassToToolType(entity.HandlerClass)
            : request.Type;

        var tool = new AnalysisTool
        {
            Id = entity.Id,
            Name = entity.Name ?? name,
            Description = entity.Description ?? request.Description,
            Type = toolType,
            HandlerClass = entity.HandlerClass ?? request.HandlerClass,
            Configuration = entity.Configuration ?? request.Configuration,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            AvailableInContexts = MapAvailableInContexts(entity.AvailableInContexts)
        };

        Logger.LogInformation("[CREATE TOOL] Created tool '{ToolName}' with ID {ToolId}", tool.Name, tool.Id);

        return tool;
    }

    /// <summary>
    /// Update an existing analysis tool.
    /// </summary>
    public async Task<AnalysisTool> UpdateToolAsync(Guid id, UpdateToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Logger.LogInformation("[UPDATE TOOL] Updating tool {ToolId} in Dataverse", id);

        var existing = await GetToolAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[UPDATE TOOL] Tool {ToolId} not found for update", id);
            throw new KeyNotFoundException($"Tool with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.HandlerClass != null)
            payload["sprk_handlerclass"] = request.HandlerClass;
        if (request.Configuration != null)
            payload["sprk_configuration"] = request.Configuration;

        var url = $"sprk_analysistools({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await Http.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await GetToolAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated tool {id} from Dataverse");
        }

        Logger.LogInformation("[UPDATE TOOL] Updated tool '{ToolName}' (ID: {ToolId})", updated.Name, id);

        return updated;
    }

    /// <summary>
    /// Delete an analysis tool.
    /// </summary>
    public async Task<bool> DeleteToolAsync(Guid id, CancellationToken cancellationToken)
    {
        Logger.LogInformation("[DELETE TOOL] Deleting tool {ToolId} from Dataverse", id);

        var existing = await GetToolAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[DELETE TOOL] Tool {ToolId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysistools({id})";
        var response = await Http.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("[DELETE TOOL] Tool {ToolId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        Logger.LogInformation("[DELETE TOOL] Deleted tool '{ToolName}' (ID: {ToolId})", existing.Name, id);
        return true;
    }

    /// <summary>
    /// Create a copy of an existing tool with a new name.
    /// </summary>
    public async Task<AnalysisTool> SaveAsToolAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        Logger.LogInformation("Saving tool {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetToolAsync(sourceId, cancellationToken);
        if (source == null)
        {
            Logger.LogWarning("Source tool {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source tool with ID '{sourceId}' not found.");
        }

        var createRequest = new CreateToolRequest
        {
            Name = newName,
            Description = source.Description,
            Type = source.Type,
            HandlerClass = source.HandlerClass,
            Configuration = source.Configuration
        };

        var copy = await CreateToolAsync(createRequest, cancellationToken);

        Logger.LogInformation("[SAVE AS TOOL] Created tool copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    /// <summary>
    /// Create a child tool that extends the parent.
    /// </summary>
    public async Task<AnalysisTool> ExtendToolAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        Logger.LogInformation("[EXTEND TOOL] Extending tool {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetToolAsync(parentId, cancellationToken);
        if (parent == null)
        {
            Logger.LogWarning("[EXTEND TOOL] Parent tool {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent tool with ID '{parentId}' not found.");
        }

        var createRequest = new CreateToolRequest
        {
            Name = childName,
            Description = parent.Description,
            Type = parent.Type,
            HandlerClass = parent.HandlerClass,
            Configuration = parent.Configuration
        };

        var child = await CreateToolAsync(createRequest, cancellationToken);

        Logger.LogInformation("[EXTEND TOOL] Created child tool '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    #region Private Helpers

    internal static ToolType MapHandlerClassToToolType(string handlerClass)
    {
        return handlerClass switch
        {
            string s when s.Contains("EntityExtractor", StringComparison.OrdinalIgnoreCase) => ToolType.EntityExtractor,
            string s when s.Contains("ClauseAnalyzer", StringComparison.OrdinalIgnoreCase) => ToolType.ClauseAnalyzer,
            string s when s.Contains("DocumentClassifier", StringComparison.OrdinalIgnoreCase) => ToolType.DocumentClassifier,
            string s when s.Contains("Summary", StringComparison.OrdinalIgnoreCase) => ToolType.Summary,
            string s when s.Contains("RiskDetector", StringComparison.OrdinalIgnoreCase) => ToolType.RiskDetector,
            string s when s.Contains("ClauseComparison", StringComparison.OrdinalIgnoreCase) => ToolType.ClauseComparison,
            string s when s.Contains("DateExtractor", StringComparison.OrdinalIgnoreCase) => ToolType.DateExtractor,
            string s when s.Contains("FinancialCalculator", StringComparison.OrdinalIgnoreCase) => ToolType.FinancialCalculator,
            _ => ToolType.Custom
        };
    }

    internal static ToolType MapToolTypeName(string typeName)
    {
        return typeName switch
        {
            string s when s.Contains("Entity Extraction", StringComparison.OrdinalIgnoreCase) => ToolType.EntityExtractor,
            string s when s.Contains("Classification", StringComparison.OrdinalIgnoreCase) => ToolType.DocumentClassifier,
            string s when s.Contains("Analysis", StringComparison.OrdinalIgnoreCase) => ToolType.Summary,
            string s when s.Contains("Calculation", StringComparison.OrdinalIgnoreCase) => ToolType.FinancialCalculator,
            _ => ToolType.Custom
        };
    }

    /// <summary>
    /// Map nullable raw Dataverse option-set value (100000000/100000001/100000002) to
    /// <see cref="ToolAvailabilityContext"/>. Returns <c>null</c> for unset/unknown values
    /// — callers treat <c>null</c> as <see cref="ToolAvailabilityContext.Playbook"/> for
    /// backward-compat per FR-07 (D-A-07). Unknown values are logged as warnings via
    /// caller log context but do not throw.
    /// </summary>
    internal static ToolAvailabilityContext? MapAvailableInContexts(int? rawValue)
    {
        return rawValue switch
        {
            (int)ToolAvailabilityContext.Playbook => ToolAvailabilityContext.Playbook,
            (int)ToolAvailabilityContext.Chat => ToolAvailabilityContext.Chat,
            (int)ToolAvailabilityContext.Both => ToolAvailabilityContext.Both,
            _ => null
        };
    }

    /// <summary>
    /// Map nullable raw Dataverse <c>sprk_jsonschema</c> Memo value to the DTO's
    /// <see cref="AnalysisTool.JsonSchema"/> string. Validates that the value parses
    /// as JSON (the DTO contract per FR-08 / task D-A-08) — malformed JSON is logged
    /// and stored as <c>null</c> rather than silently passed to the LLM downstream
    /// via <c>ToolHandlerToAIFunctionAdapter</c> (task D-A-10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// FR-08 contract: nullable on the DTO (backward-compat with pre-R6 playbook-only
    /// rows whose column is unpopulated). Required for chat-available tools — but
    /// that requirement is enforced by the chat-side resolver (task 011), NOT by this
    /// mapper, because the mapper must continue to work for playbook-only rows.
    /// </para>
    /// <para>
    /// Validation scope (R6 audit item 1 — strengthened 2026-06-07): this method now performs
    /// TWO layers of validation:
    /// </para>
    /// <list type="number">
    /// <item><b>JSON well-formedness</b> — parsable by
    /// <see cref="System.Text.Json.JsonDocument.Parse(string, System.Text.Json.JsonDocumentOptions)"/>.</item>
    /// <item><b>Semantic JSON Schema validity</b> — the value validates against the JSON
    /// Schema Draft 2020-12 meta-schema via <c>JsonSchema.Net</c>. This catches rows where
    /// an admin authored well-formed JSON that is not a legal schema (e.g., a properties
    /// member whose value is a primitive). Malformed schemas log a warning and map to null
    /// so the chat resolver (task 011) refuses to expose the tool to the LLM.</item>
    /// </list>
    /// <para>
    /// This double-layer validation (write-time at the mapper + chat-session-start at the
    /// <c>ToolHandlerToAIFunctionAdapter</c>) is intentional: admins editing
    /// <c>sprk_analysistool</c> rows in Power Apps see warnings in BFF logs as soon as the
    /// tool is loaded, and the adapter throws a clear exception if a malformed schema ever
    /// reaches function-calling registration.
    /// </para>
    /// <para>
    /// Empty / whitespace-only strings are treated as null (no schema set on this row).
    /// </para>
    /// </remarks>
    internal static string? MapJsonSchema(string? rawValue, Guid toolId, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        // Layer 1: well-formedness.
        try
        {
            using var _ = JsonDocument.Parse(rawValue);
        }
        catch (JsonException ex)
        {
            // FR-08: never silently pass malformed JSON to the LLM via the adapter.
            // Map to null and log so the chat resolver (task 011) can refuse to expose
            // the tool with a clear diagnostic trail.
            logger.LogWarning(
                ex,
                "[FR-08] sprk_jsonschema for tool {ToolId} is not valid JSON; mapping to null. " +
                "Schema length={Length}",
                toolId,
                rawValue.Length);
            return null;
        }

        // Layer 2: semantic JSON Schema validity (R6 audit item 1).
        try
        {
            var node = JsonNode.Parse(rawValue);
            if (node is null)
            {
                logger.LogWarning(
                    "[R6-audit-1] sprk_jsonschema for tool {ToolId} parsed to null JsonNode; " +
                    "mapping to null. Admins should set the column to a JSON object document.",
                    toolId);
                return null;
            }

            var metaResults = MetaSchemas.Draft202012.Evaluate(
                node,
                new EvaluationOptions
                {
                    OutputFormat = OutputFormat.List,
                    ValidateAgainstMetaSchema = false  // we ARE the meta-schema evaluation
                });

            if (!metaResults.IsValid)
            {
                var firstErrors = metaResults.Details
                    .Where(d => d.HasErrors && d.Errors is not null)
                    .SelectMany(d => d.Errors!.Select(kv => $"{d.InstanceLocation}: {kv.Value}"))
                    .Take(3)
                    .ToArray();

                logger.LogWarning(
                    "[R6-audit-1] sprk_jsonschema for tool {ToolId} is well-formed JSON but is " +
                    "not a valid JSON Schema (Draft 2020-12); mapping to null. " +
                    "Schema length={Length}, errors={Errors}",
                    toolId,
                    rawValue.Length,
                    firstErrors.Length > 0 ? string.Join("; ", firstErrors) : "(no detailed errors)");
                return null;
            }
        }
        catch (Exception ex)
        {
            // JsonSchema.Net can throw on some pathological inputs (e.g., $ref cycles).
            // Map to null and log — same UX as malformed JSON: chat resolver refuses to
            // expose the tool, admin sees a clear log entry.
            logger.LogWarning(
                ex,
                "[R6-audit-1] sprk_jsonschema for tool {ToolId} failed semantic JSON Schema " +
                "validation with an unexpected error; mapping to null. Schema length={Length}",
                toolId,
                rawValue.Length);
            return null;
        }

        return rawValue;
    }

    #endregion

    #region Private DTOs

    internal class ToolEntity
    {
        [JsonPropertyName("sprk_analysistoolid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        [JsonPropertyName("sprk_ToolTypeId")]
        public ToolTypeReference? ToolTypeId { get; set; }

        [JsonPropertyName("sprk_handlerclass")]
        public string? HandlerClass { get; set; }

        [JsonPropertyName("sprk_configuration")]
        public string? Configuration { get; set; }

        /// <summary>
        /// Option-set discriminator for invocation context — R6 Pillar 2 (FR-07; D-A-07).
        /// Nullable in flight for backward-compat with pre-R6 rows whose column wasn't yet
        /// populated; callers treat null as <see cref="ToolAvailabilityContext.Playbook"/>.
        /// 100000000=Playbook, 100000001=Chat, 100000002=Both.
        /// </summary>
        [JsonPropertyName("sprk_availableincontexts")]
        public int? AvailableInContexts { get; set; }

        /// <summary>
        /// JSON Schema document describing the tool's parameter shape for LLM
        /// function-calling — R6 Pillar 2 (FR-08; D-A-08). Backed by the
        /// <c>sprk_jsonschema</c> Memo (multi-line text, ~100 KB) attribute. Nullable
        /// in flight for backward-compat with pre-R6 playbook-only rows; required for
        /// chat-available tools (enforced at chat-side resolver per FR-08).
        /// Validated as well-formed JSON by
        /// <see cref="MapJsonSchema(string?, Guid, ILogger)"/> before reaching the DTO.
        /// </summary>
        [JsonPropertyName("sprk_jsonschema")]
        public string? JsonSchema { get; set; }
    }

    internal class ToolTypeReference
    {
        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }
    }

    #endregion
}
