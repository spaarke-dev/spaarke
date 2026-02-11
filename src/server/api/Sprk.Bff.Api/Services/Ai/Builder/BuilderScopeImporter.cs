using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Builder;

/// <summary>
/// Imports builder scope definitions from JSON files into Dataverse.
/// Used for initial seeding and updates of builder AI scopes.
/// </summary>
public class BuilderScopeImporter
{
    private readonly IScopeResolverService _scopeResolver;
    private readonly ILogger<BuilderScopeImporter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public BuilderScopeImporter(
        IScopeResolverService scopeResolver,
        ILogger<BuilderScopeImporter> logger)
    {
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    /// <summary>
    /// Import all builder scopes from JSON files in the specified directory.
    /// </summary>
    /// <param name="jsonDirectory">Directory containing builder scope JSON files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import result with counts and any errors.</returns>
    public async Task<BuilderScopeImportResult> ImportFromDirectoryAsync(
        string jsonDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = new BuilderScopeImportResult();

        if (!Directory.Exists(jsonDirectory))
        {
            result.Errors.Add($"Directory not found: {jsonDirectory}");
            return result;
        }

        var jsonFiles = Directory.GetFiles(jsonDirectory, "*.json");
        _logger.LogInformation("Found {Count} JSON files in {Directory}", jsonFiles.Length, jsonDirectory);

        foreach (var file in jsonFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var scope = JsonSerializer.Deserialize<BuilderScopeDefinition>(json, JsonOptions);

                if (scope == null)
                {
                    result.Errors.Add($"Failed to parse: {Path.GetFileName(file)}");
                    continue;
                }

                await ImportScopeAsync(scope, result, cancellationToken);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error processing {Path.GetFileName(file)}: {ex.Message}");
                _logger.LogError(ex, "Error importing scope from {File}", file);
            }
        }

        _logger.LogInformation(
            "Import complete: {Actions} actions, {Skills} skills, {Knowledge} knowledge, {Tools} tools, {Errors} errors",
            result.ActionsImported, result.SkillsImported, result.KnowledgeImported, result.ToolsImported, result.Errors.Count);

        return result;
    }

    /// <summary>
    /// Import a single builder scope from JSON content.
    /// </summary>
    public async Task<BuilderScopeImportResult> ImportFromJsonAsync(
        string json,
        CancellationToken cancellationToken = default)
    {
        var result = new BuilderScopeImportResult();

        try
        {
            var scope = JsonSerializer.Deserialize<BuilderScopeDefinition>(json, JsonOptions);
            if (scope == null)
            {
                result.Errors.Add("Failed to parse JSON");
                return result;
            }

            await ImportScopeAsync(scope, result, cancellationToken);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error: {ex.Message}");
            _logger.LogError(ex, "Error importing scope from JSON");
        }

        return result;
    }

    private async Task ImportScopeAsync(
        BuilderScopeDefinition scope,
        BuilderScopeImportResult result,
        CancellationToken cancellationToken)
    {
        switch (scope.ScopeType?.ToLowerInvariant())
        {
            case "action":
                await ImportActionAsync(scope, result, cancellationToken);
                break;
            case "skill":
                await ImportSkillAsync(scope, result, cancellationToken);
                break;
            case "knowledge":
                await ImportKnowledgeAsync(scope, result, cancellationToken);
                break;
            case "tool":
                await ImportToolAsync(scope, result, cancellationToken);
                break;
            default:
                result.Errors.Add($"Unknown scope type: {scope.ScopeType} for {scope.Name}");
                break;
        }
    }

    private async Task ImportActionAsync(
        BuilderScopeDefinition scope,
        BuilderScopeImportResult result,
        CancellationToken cancellationToken)
    {
        // Name prefix (SYS- vs CUST-) determines owner type in the service
        var request = new CreateActionRequest
        {
            Name = scope.Name ?? scope.DisplayName ?? "Unknown",
            Description = scope.Description,
            SystemPrompt = scope.SystemPrompt ?? string.Empty,
            ActionType = Sprk.Bff.Api.Services.Ai.Nodes.ActionType.AiAnalysis
        };

        try
        {
            var created = await _scopeResolver.CreateActionAsync(request, cancellationToken);
            result.ActionsImported++;
            result.ImportedIds.Add(created.Id);
            _logger.LogDebug("Imported action: {Name} ({Id})", created.Name, created.Id);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to import action {scope.Name}: {ex.Message}");
        }
    }

    private async Task ImportSkillAsync(
        BuilderScopeDefinition scope,
        BuilderScopeImportResult result,
        CancellationToken cancellationToken)
    {
        var request = new CreateSkillRequest
        {
            Name = scope.Name ?? scope.DisplayName ?? "Unknown",
            Description = scope.Description,
            PromptFragment = scope.PromptFragment ?? scope.SystemPrompt ?? string.Empty,
            Category = scope.Metadata?.Category
        };

        try
        {
            var created = await _scopeResolver.CreateSkillAsync(request, cancellationToken);
            result.SkillsImported++;
            result.ImportedIds.Add(created.Id);
            _logger.LogDebug("Imported skill: {Name} ({Id})", created.Name, created.Id);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to import skill {scope.Name}: {ex.Message}");
        }
    }

    private async Task ImportKnowledgeAsync(
        BuilderScopeDefinition scope,
        BuilderScopeImportResult result,
        CancellationToken cancellationToken)
    {
        // Content can be a string or object - serialize to string if needed
        var contentString = GetContentAsString(scope.Content, scope.SystemPrompt);

        var request = new CreateKnowledgeRequest
        {
            Name = scope.Name ?? scope.DisplayName ?? "Unknown",
            Description = scope.Description,
            Content = contentString,
            Type = KnowledgeType.Inline
        };

        try
        {
            var created = await _scopeResolver.CreateKnowledgeAsync(request, cancellationToken);
            result.KnowledgeImported++;
            result.ImportedIds.Add(created.Id);
            _logger.LogDebug("Imported knowledge: {Name} ({Id})", created.Name, created.Id);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to import knowledge {scope.Name}: {ex.Message}");
        }
    }

    private async Task ImportToolAsync(
        BuilderScopeDefinition scope,
        BuilderScopeImportResult result,
        CancellationToken cancellationToken)
    {
        // Parse tool type from configuration
        var toolType = ParseToolType(scope.Configuration?.HandlerType);

        var request = new CreateToolRequest
        {
            Name = scope.Name ?? scope.DisplayName ?? "Unknown",
            Description = scope.Description,
            Type = toolType,
            HandlerClass = scope.Configuration?.HandlerType,
            Configuration = scope.Configuration != null
                ? JsonSerializer.Serialize(scope.Configuration)
                : null
        };

        try
        {
            var created = await _scopeResolver.CreateToolAsync(request, cancellationToken);
            result.ToolsImported++;
            result.ImportedIds.Add(created.Id);
            _logger.LogDebug("Imported tool: {Name} ({Id})", created.Name, created.Id);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to import tool {scope.Name}: {ex.Message}");
        }
    }

    private static ToolType ParseToolType(string? handlerType)
    {
        return handlerType?.ToLowerInvariant() switch
        {
            "entityextractor" or "entityextractionhandler" => ToolType.EntityExtractor,
            "clauseanalyzer" or "clauseanalysishandler" => ToolType.ClauseAnalyzer,
            "documentclassifier" or "documentclassificationhandler" => ToolType.DocumentClassifier,
            "summary" or "summarizationhandler" => ToolType.Summary,
            "riskdetector" or "riskdetectionhandler" => ToolType.RiskDetector,
            "canvasoperationhandler" => ToolType.Custom, // Builder tools
            _ => ToolType.Custom
        };
    }

    /// <summary>
    /// Convert JsonElement content to string (handles both string and object values).
    /// </summary>
    private static string GetContentAsString(JsonElement? content, string? fallback)
    {
        if (content == null)
            return fallback ?? string.Empty;

        return content.Value.ValueKind switch
        {
            JsonValueKind.String => content.Value.GetString() ?? fallback ?? string.Empty,
            JsonValueKind.Object or JsonValueKind.Array => content.Value.GetRawText(),
            _ => fallback ?? string.Empty
        };
    }
}

/// <summary>
/// JSON structure for builder scope definitions.
/// </summary>
public record BuilderScopeDefinition
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? ScopeType { get; init; }
    public int OwnerType { get; init; } = 2; // Default to Customer
    public bool IsImmutable { get; init; }
    public string? SystemPrompt { get; init; }
    public string? PromptFragment { get; init; }
    public JsonElement? Content { get; init; }  // Can be string or object
    public BuilderScopeMetadata? Metadata { get; init; }
    public BuilderToolConfiguration? Configuration { get; init; }
}

public record BuilderScopeMetadata
{
    public string[]? Tags { get; init; }
    public string? Version { get; init; }
    public string? Category { get; init; }
}

public record BuilderToolConfiguration
{
    public string? HandlerType { get; init; }
    public string? Operation { get; init; }
    public JsonElement? InputSchema { get; init; }
    public JsonElement? OutputSchema { get; init; }
}

/// <summary>
/// Result of a builder scope import operation.
/// </summary>
public record BuilderScopeImportResult
{
    public int ActionsImported { get; set; }
    public int SkillsImported { get; set; }
    public int KnowledgeImported { get; set; }
    public int ToolsImported { get; set; }
    public List<Guid> ImportedIds { get; } = new();
    public List<string> Errors { get; } = new();

    public int TotalImported => ActionsImported + SkillsImported + KnowledgeImported + ToolsImported;
    public bool HasErrors => Errors.Count > 0;
}
