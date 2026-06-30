using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Focused service for AnalysisAction CRUD operations against Dataverse.
/// Extracted from ScopeResolverService as part of god-class decomposition (PPI-054).
/// </summary>
public class AnalysisActionService : DataverseHttpServiceBase
{
    public AnalysisActionService(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<AnalysisActionService> logger)
        : base(httpClient, configuration, credential, logger)
    {
    }

    /// <summary>
    /// Get action definition by ID.
    /// </summary>
    public async Task<AnalysisAction?> GetActionAsync(
        Guid actionId,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Loading action {ActionId} from Dataverse", actionId);

        await EnsureAuthenticatedAsync(cancellationToken);

        // R7 task 028 / FR-07: read path simplified. After single-hop dispatch (task 024),
        // the orchestrator reads ExecutorType from node.sprk_executortype DIRECTLY — Action is
        // no longer the dispatch axis. Action is now strictly a prompt-template carrier
        // {SystemPrompt + OutputSchema + Temperature}. The pre-R7 dispatch-identity columns
        // on the Action row were dropped in Wave 4 tasks 043 + 044 (2026-06-29); the
        // corresponding DTO projections + cascading dead helpers were removed by task 046.
        // R7 task 002 / FR-12: sprk_outputschemajson is retained on the projection so the
        // AnalysisAction record carries the canonical {SystemPrompt + OutputSchema + Temperature}
        // triple — consumed unconditionally by AiCompletionNodeExecutor; ignored by executors
        // that do not need a structured-output schema.
        var url = $"sprk_analysisactions({actionId})?$select=sprk_analysisactionid,sprk_name,sprk_description,sprk_systemprompt,sprk_temperature,sprk_outputschemajson";
        var response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("Action {ActionId} not found in Dataverse", actionId);
            return null;
        }

        await EnsureSuccessWithDiagnosticsAsync(response, $"GetActionAsync({actionId})", cancellationToken);

        var entity = await response.Content.ReadFromJsonAsync<ActionEntity>(cancellationToken);
        if (entity == null)
        {
            Logger.LogWarning("Failed to deserialize action {ActionId} from Dataverse response", actionId);
            return null;
        }

        var action = new AnalysisAction
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Action",
            Description = entity.Description,
            SystemPrompt = entity.SystemPrompt ?? "You are an AI assistant that analyzes documents.",
            // SortOrder previously derived from ActionTypeId.Name (e.g., "10 - AiAnalysis").
            // After the Wave 4 lookup-column drop (task 043) there is no source column on the
            // Action row. Defaulting to 0 is safe — the only live consumer is display sorting
            // in maker surfaces and tests, and full SortOrder reform is out of scope for R7.
            SortOrder = 0,
            // ExecutorType is set to the default value because the orchestrator no longer reads
            // Action.ExecutorType for dispatch (task 024 single-hop reads node.sprk_executortype
            // directly). The DTO property is retained on AnalysisAction for compatibility with
            // CRUD callers (Create/Update DTOs); future cleanup of those writers may retire it.
            ExecutorType = Nodes.ExecutorType.AiAnalysis,
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false,
            // Wave B-G9c1 (B6): per-action temperature override. Null = deterministic 0.0
            // (matches sibling structured methods' hardcoded Temperature=0 behavior).
            Temperature = entity.Temperature,
            // R7 task 002 / FR-12: structured-outputs JSON schema for prompt-driven executors.
            OutputSchemaJson = entity.OutputSchemaJson
        };

        Logger.LogInformation("Loaded action from Dataverse: {ActionName}", action.Name);

        return action;
    }

    /// <summary>
    /// Get action definition by its <c>sprk_actioncode</c> alternate key. Added by the
    /// R7 Wave 11 T116 narrator spike so code-based narrators can resolve Actions by
    /// stable string code (e.g., "BRIEF-NARRATE-TLDR") instead of by GUID.
    /// </summary>
    /// <remarks>
    /// Mirrors the column set of <see cref="GetActionAsync(Guid, CancellationToken)"/>
    /// exactly — returns the same shape with the same field semantics. Uses an OData
    /// filter on sprk_actioncode (not the alternate-key URL form) so the call works
    /// uniformly whether or not the alternate key is enabled at the Dataverse level.
    /// </remarks>
    public async Task<AnalysisAction?> GetActionByCodeAsync(
        string actionCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actionCode))
        {
            throw new ArgumentException("actionCode is required", nameof(actionCode));
        }

        Logger.LogDebug("Loading action by code {ActionCode} from Dataverse", actionCode);

        await EnsureAuthenticatedAsync(cancellationToken);

        // OData filter (URL-encoded single quotes around the literal). Top=1 because
        // sprk_actioncode is unique by design even when not enforced as an alternate key.
        var encoded = Uri.EscapeDataString(actionCode);
        var url = $"sprk_analysisactions?$select=sprk_analysisactionid,sprk_name,sprk_description,sprk_systemprompt,sprk_temperature,sprk_outputschemajson&$filter=sprk_actioncode eq '{encoded}'&$top=1";
        var response = await Http.GetAsync(url, cancellationToken);

        await EnsureSuccessWithDiagnosticsAsync(response, $"GetActionByCodeAsync({actionCode})", cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<ActionEntity>>(cancellationToken);
        if (result?.Value is null || result.Value.Count == 0)
        {
            Logger.LogWarning("Action with code {ActionCode} not found in Dataverse", actionCode);
            return null;
        }

        var entity = result.Value[0];
        var action = new AnalysisAction
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Action",
            Description = entity.Description,
            SystemPrompt = entity.SystemPrompt ?? "You are an AI assistant that analyzes documents.",
            SortOrder = 0,
            ExecutorType = Nodes.ExecutorType.AiAnalysis,
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false,
            Temperature = entity.Temperature,
            OutputSchemaJson = entity.OutputSchemaJson
        };

        Logger.LogInformation("Loaded action by code from Dataverse: {ActionCode} -> {ActionName} ({ActionId})",
            actionCode, action.Name, action.Id);

        return action;
    }

    /// <summary>
    /// List all available actions with pagination.
    /// </summary>
    public async Task<ScopeListResult<AnalysisAction>> ListActionsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("[LIST ACTIONS] Querying Dataverse: Page={Page}, PageSize={PageSize}, NameFilter={NameFilter}",
            options.Page, options.PageSize, options.NameFilter);

        await EnsureAuthenticatedAsync(cancellationToken);

        var sortMappings = new Dictionary<string, string>
        {
            ["name"] = "sprk_name",
            ["sortorder"] = "sprk_name"
        };

        // R7 task 028 / FR-07: read path simplified. ActionTypeId expand removed — Action is
        // no longer the dispatch axis (orchestrator reads node.sprk_executortype directly).
        // See GetActionAsync above for the full rationale and Wave 4 task 046 follow-up.
        var query = BuildODataQuery(
            options,
            selectFields: "sprk_analysisactionid,sprk_name,sprk_description,sprk_systemprompt,sprk_temperature,sprk_outputschemajson",
            expandClause: null,
            nameFieldPath: "sprk_name",
            categoryFieldPath: null,
            sortFieldMappings: sortMappings);

        var url = $"sprk_analysisactions?{query}";
        Logger.LogDebug("[LIST ACTIONS] Query URL: {Url}", url);

        var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<ActionEntity>>(cancellationToken);
        if (result == null)
        {
            Logger.LogWarning("[LIST ACTIONS] Failed to deserialize response");
            return new ScopeListResult<AnalysisAction>
            {
                Items = [],
                TotalCount = 0,
                Page = options.Page,
                PageSize = options.PageSize
            };
        }

        var actions = result.Value.Select(entity => new AnalysisAction
        {
            Id = entity.Id,
            Name = entity.Name ?? "Unnamed Action",
            Description = entity.Description,
            SystemPrompt = entity.SystemPrompt ?? "You are an AI assistant that analyzes documents.",
            // SortOrder defaults to 0 — see GetActionAsync rationale.
            SortOrder = 0,
            OwnerType = ScopeOwnerType.System,
            IsImmutable = false,
            // Wave B-G9c1 (B6): per-action temperature override.
            Temperature = entity.Temperature,
            // R7 task 002 / FR-12: structured-outputs JSON schema for prompt-driven executors.
            OutputSchemaJson = entity.OutputSchemaJson
        }).ToArray();

        Logger.LogInformation("[LIST ACTIONS] Retrieved {Count} actions from Dataverse (Total: {TotalCount})",
            actions.Length, result.ODataCount ?? actions.Length);

        return new ScopeListResult<AnalysisAction>
        {
            Items = actions,
            TotalCount = result.ODataCount ?? actions.Length,
            Page = options.Page,
            PageSize = options.PageSize
        };
    }

    /// <summary>
    /// Create a new analysis action.
    /// </summary>
    public async Task<AnalysisAction> CreateActionAsync(CreateActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt, nameof(request.SystemPrompt));

        var name = EnsureCustomerPrefix(request.Name);

        Logger.LogInformation("[CREATE ACTION] Creating action '{ActionName}' in Dataverse", name);

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = name,
            ["sprk_description"] = request.Description,
            ["sprk_systemprompt"] = request.SystemPrompt
        };

        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "sprk_analysisactions")
        {
            Content = httpContent
        };
        postRequest.Headers.Add("Prefer", "return=representation");

        var response = await Http.SendAsync(postRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var entity = await response.Content.ReadFromJsonAsync<ActionEntity>(cancellationToken);
        if (entity == null)
        {
            throw new InvalidOperationException("Failed to deserialize created action from Dataverse response");
        }

        // R7 task 028 / FR-07: ActionTypeId lookup-derived sortOrder removed. Honor the
        // CreateActionRequest.SortOrder verbatim (server-side default is 100 per the DTO).
        // ExecutorType is still surfaced from the request DTO for CRUD-caller compatibility.
        var action = new AnalysisAction
        {
            Id = entity.Id,
            Name = entity.Name ?? name,
            Description = entity.Description ?? request.Description,
            SystemPrompt = entity.SystemPrompt ?? request.SystemPrompt,
            SortOrder = request.SortOrder,
            ExecutorType = request.ExecutorType,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            // Wave B-G9c1 (B6): per-action temperature override. Reflects server-side value
            // (null when CREATE didn't set it; downstream defaults to 0.0).
            Temperature = entity.Temperature
        };

        Logger.LogInformation("[CREATE ACTION] Created action '{ActionName}' with ID {ActionId}", action.Name, action.Id);

        return action;
    }

    /// <summary>
    /// Update an existing analysis action.
    /// </summary>
    public async Task<AnalysisAction> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Logger.LogInformation("[UPDATE ACTION] Updating action {ActionId} in Dataverse", id);

        var existing = await GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[UPDATE ACTION] Action {ActionId} not found for update", id);
            throw new KeyNotFoundException($"Action with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        await EnsureAuthenticatedAsync(cancellationToken);

        var payload = new Dictionary<string, object?>();

        if (request.Name != null)
            payload["sprk_name"] = EnsureCustomerPrefix(request.Name);
        if (request.Description != null)
            payload["sprk_description"] = request.Description;
        if (request.SystemPrompt != null)
            payload["sprk_systemprompt"] = request.SystemPrompt;

        var url = $"sprk_analysisactions({id})";
        using var httpContent = JsonContent.Create(payload);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = httpContent
        };

        var response = await Http.SendAsync(patchRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updated = await GetActionAsync(id, cancellationToken);
        if (updated == null)
        {
            throw new InvalidOperationException($"Failed to retrieve updated action {id} from Dataverse");
        }

        Logger.LogInformation("[UPDATE ACTION] Updated action '{ActionName}' (ID: {ActionId})", updated.Name, id);

        return updated;
    }

    /// <summary>
    /// Delete an analysis action.
    /// </summary>
    public async Task<bool> DeleteActionAsync(Guid id, CancellationToken cancellationToken)
    {
        Logger.LogInformation("[DELETE ACTION] Deleting action {ActionId} from Dataverse", id);

        var existing = await GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            Logger.LogWarning("[DELETE ACTION] Action {ActionId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"sprk_analysisactions({id})";
        var response = await Http.DeleteAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogWarning("[DELETE ACTION] Action {ActionId} not found in Dataverse", id);
            return false;
        }

        response.EnsureSuccessStatusCode();

        Logger.LogInformation("[DELETE ACTION] Deleted action '{ActionName}' (ID: {ActionId})", existing.Name, id);
        return true;
    }

    /// <summary>
    /// Create a copy of an existing action with a new name.
    /// </summary>
    public async Task<AnalysisAction> SaveAsActionAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        Logger.LogInformation("Saving action {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetActionAsync(sourceId, cancellationToken);
        if (source == null)
        {
            Logger.LogWarning("Source action {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source action with ID '{sourceId}' not found.");
        }

        var createRequest = new CreateActionRequest
        {
            Name = newName,
            Description = source.Description,
            SystemPrompt = source.SystemPrompt,
            SortOrder = source.SortOrder,
            ExecutorType = source.ExecutorType
        };

        var copy = await CreateActionAsync(createRequest, cancellationToken);

        Logger.LogInformation("[SAVE AS ACTION] Created action copy '{Name}' (ID: {CopyId}) based on {SourceId}",
            copy.Name, copy.Id, sourceId);

        return copy with { BasedOnId = sourceId };
    }

    /// <summary>
    /// Create a child action that extends the parent.
    /// </summary>
    public async Task<AnalysisAction> ExtendActionAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        Logger.LogInformation("[EXTEND ACTION] Extending action {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetActionAsync(parentId, cancellationToken);
        if (parent == null)
        {
            Logger.LogWarning("[EXTEND ACTION] Parent action {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent action with ID '{parentId}' not found.");
        }

        var createRequest = new CreateActionRequest
        {
            Name = childName,
            Description = parent.Description,
            SystemPrompt = parent.SystemPrompt,
            SortOrder = parent.SortOrder,
            ExecutorType = parent.ExecutorType
        };

        var child = await CreateActionAsync(createRequest, cancellationToken);

        Logger.LogInformation("[EXTEND ACTION] Created child action '{Name}' (ID: {ChildId}) extending {ParentId}",
            child.Name, child.Id, parentId);

        return child with { ParentScopeId = parentId };
    }

    #region Private DTOs

    internal class ActionEntity
    {
        [JsonPropertyName("sprk_analysisactionid")]
        public Guid Id { get; set; }

        [JsonPropertyName("sprk_name")]
        public string? Name { get; set; }

        [JsonPropertyName("sprk_description")]
        public string? Description { get; set; }

        [JsonPropertyName("sprk_systemprompt")]
        public string? SystemPrompt { get; set; }

        /// <summary>
        /// Per-action temperature override (sprk_temperature, Decimal 0.0–2.0).
        /// Null = use deterministic default (0.0) downstream.
        /// Added Wave B-G9c1 (B6) — see <c>projects/spaarke-ai-platform-unification-r6/notes/wave-b-g9c-medium-bugs.md</c>.
        /// </summary>
        [JsonPropertyName("sprk_temperature")]
        public decimal? Temperature { get; set; }

        /// <summary>
        /// Structured-Outputs JSON Schema for the action (sprk_outputschemajson, multiline text).
        /// Consumed by prompt-driven executors (AiCompletion per R7 FR-12 — task 002) as the
        /// constrained-decoding schema arg to <c>IOpenAiClient.GetStructuredCompletionRawAsync</c>.
        /// Null when the column is empty or absent.
        /// </summary>
        [JsonPropertyName("sprk_outputschemajson")]
        public string? OutputSchemaJson { get; set; }
    }

    #endregion
}
