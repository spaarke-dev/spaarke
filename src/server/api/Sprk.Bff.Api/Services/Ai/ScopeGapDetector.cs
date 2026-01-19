using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Detects missing scopes based on playbook intent and available scope catalog.
/// Provides proactive suggestions for scope creation during playbook building.
/// </summary>
/// <remarks>
/// <para>
/// Gap detection workflow:
/// 1. Analyze playbook description and goals
/// 2. Extract required capabilities (entities, operations, outputs)
/// 3. Compare against available scopes in catalog
/// 4. Generate suggestions for missing scope types
/// </para>
/// <para>
/// Designed for low-latency execution during playbook creation wizard.
/// Uses lightweight AI classification for intent analysis.
/// </para>
/// </remarks>
public sealed class ScopeGapDetector : IScopeGapDetector
{
    private readonly IScopeResolverService _scopeResolver;
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<ScopeGapDetector> _logger;

    // Category keywords for intent matching
    private static readonly Dictionary<string, string[]> IntentKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["extraction"] = new[] { "extract", "identify", "find", "locate", "pull out", "get", "retrieve", "capture" },
        ["classification"] = new[] { "classify", "categorize", "determine type", "sort", "organize", "bucket" },
        ["summarization"] = new[] { "summarize", "summary", "overview", "brief", "condense", "highlight" },
        ["analysis"] = new[] { "analyze", "assess", "evaluate", "review", "examine", "check", "audit" },
        ["risk"] = new[] { "risk", "danger", "warning", "concern", "issue", "problem", "liability" },
        ["dates"] = new[] { "date", "deadline", "expiration", "renewal", "timeline", "schedule" },
        ["financial"] = new[] { "financial", "money", "amount", "payment", "price", "cost", "fee" },
        ["comparison"] = new[] { "compare", "contrast", "difference", "versus", "against", "match" },
    };

    public ScopeGapDetector(
        IScopeResolverService scopeResolver,
        IOpenAiClient openAiClient,
        ILogger<ScopeGapDetector> logger)
    {
        _scopeResolver = scopeResolver;
        _openAiClient = openAiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScopeGapAnalysis> AnalyzePlaybookIntentAsync(
        string playbookDescription,
        string? playbookGoals,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playbookDescription))
        {
            return new ScopeGapAnalysis
            {
                Suggestions = Array.Empty<ScopeSuggestion>(),
                AnalyzedIntent = "No description provided"
            };
        }

        _logger.LogInformation("Analyzing playbook intent for gap detection");

        var combinedText = string.Join(" ", playbookDescription, playbookGoals ?? "");

        // Quick keyword-based analysis first (low latency)
        var detectedIntents = DetectIntentsFromKeywords(combinedText);

        // Get available scopes to compare against
        var availableScopes = await GetAvailableScopeSummaryAsync(cancellationToken);

        // Generate suggestions based on gaps
        var suggestions = await GenerateSuggestionsAsync(
            combinedText,
            detectedIntents,
            availableScopes,
            cancellationToken);

        return new ScopeGapAnalysis
        {
            Suggestions = suggestions,
            AnalyzedIntent = string.Join(", ", detectedIntents),
            DetectedCapabilities = detectedIntents.ToList()
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScopeSuggestion>> SuggestToolsForIntentAsync(
        string intent,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<ScopeSuggestion>();

        // Map intent to tool types
        var toolMappings = intent.ToLowerInvariant() switch
        {
            "extraction" => new[] { ("EntityExtractor", "Extract structured entities from documents") },
            "classification" => new[] { ("DocumentClassifier", "Classify documents by type or category") },
            "summarization" => new[] { ("Summary", "Generate document summaries") },
            "analysis" => new[] { ("ClauseAnalyzer", "Analyze document clauses") },
            "risk" => new[] { ("RiskDetector", "Identify and assess risks") },
            "dates" => new[] { ("DateExtractor", "Extract and normalize dates") },
            "financial" => new[] { ("FinancialCalculator", "Process financial calculations") },
            "comparison" => new[] { ("ClauseComparison", "Compare document clauses") },
            _ => Array.Empty<(string, string)>()
        };

        // Check which tools are available
        var options = new ScopeListOptions { PageSize = 100 };
        var existingTools = await _scopeResolver.ListToolsAsync(options, cancellationToken);
        var existingToolTypes = existingTools.Items
            .Select(t => t.Type.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (toolType, description) in toolMappings)
        {
            if (!existingToolTypes.Contains(toolType))
            {
                suggestions.Add(new ScopeSuggestion
                {
                    ScopeType = ScopeType.Tool,
                    SuggestedName = $"CUST-{toolType}",
                    Description = description,
                    Reason = $"No {toolType} tool found for '{intent}' capability",
                    Priority = SuggestionPriority.Medium,
                    IsCustomRequired = true
                });
            }
        }

        return suggestions;
    }

    /// <summary>
    /// Detect intents from text using keyword matching.
    /// </summary>
    private static HashSet<string> DetectIntentsFromKeywords(string text)
    {
        var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lowerText = text.ToLowerInvariant();

        foreach (var (intent, keywords) in IntentKeywords)
        {
            if (keywords.Any(keyword => lowerText.Contains(keyword)))
            {
                detected.Add(intent);
            }
        }

        return detected;
    }

    /// <summary>
    /// Get a summary of available scopes by type.
    /// </summary>
    private async Task<AvailableScopesSummary> GetAvailableScopeSummaryAsync(CancellationToken cancellationToken)
    {
        var options = new ScopeListOptions { PageSize = 100 };

        var actions = await _scopeResolver.ListActionsAsync(options, cancellationToken);
        var skills = await _scopeResolver.ListSkillsAsync(options, cancellationToken);
        var tools = await _scopeResolver.ListToolsAsync(options, cancellationToken);
        var knowledge = await _scopeResolver.ListKnowledgeAsync(options, cancellationToken);

        return new AvailableScopesSummary
        {
            ActionNames = actions.Items.Select(a => a.Name).ToList(),
            SkillNames = skills.Items.Select(s => s.Name).ToList(),
            ToolNames = tools.Items.Select(t => t.Name).ToList(),
            ToolTypes = tools.Items.Select(t => t.Type.ToString()).Distinct().ToList(),
            KnowledgeNames = knowledge.Items.Select(k => k.Name).ToList()
        };
    }

    /// <summary>
    /// Generate suggestions based on detected gaps.
    /// </summary>
    private async Task<IReadOnlyList<ScopeSuggestion>> GenerateSuggestionsAsync(
        string playbookText,
        HashSet<string> detectedIntents,
        AvailableScopesSummary availableScopes,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<ScopeSuggestion>();

        // Check for tool gaps
        foreach (var intent in detectedIntents)
        {
            var toolSuggestions = await SuggestToolsForIntentAsync(intent, cancellationToken);
            suggestions.AddRange(toolSuggestions);
        }

        // Check for skill gaps based on common patterns
        if (detectedIntents.Contains("extraction") &&
            !availableScopes.SkillNames.Any(n => n.Contains("extract", StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add(new ScopeSuggestion
            {
                ScopeType = ScopeType.Skill,
                SuggestedName = "CUST-Extraction-Focus",
                Description = "Skill to enhance extraction accuracy",
                Reason = "Playbook requires extraction but no extraction-focused skill found",
                Priority = SuggestionPriority.Low,
                IsCustomRequired = false
            });
        }

        // Check for knowledge gaps
        if (playbookText.Contains("template", StringComparison.OrdinalIgnoreCase) &&
            !availableScopes.KnowledgeNames.Any(n => n.Contains("template", StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add(new ScopeSuggestion
            {
                ScopeType = ScopeType.Knowledge,
                SuggestedName = "CUST-Output-Template",
                Description = "Template for structured output formatting",
                Reason = "Playbook mentions templates but no template knowledge found",
                Priority = SuggestionPriority.Medium,
                IsCustomRequired = false,
                SuggestedContent = "Provide a template for the expected output format."
            });
        }

        // Use AI for more nuanced suggestions if few were found
        if (suggestions.Count < 2 && !string.IsNullOrWhiteSpace(playbookText))
        {
            var aiSuggestions = await GetAiSuggestionsAsync(playbookText, availableScopes, cancellationToken);
            suggestions.AddRange(aiSuggestions);
        }

        // Remove duplicates and sort by priority
        return suggestions
            .GroupBy(s => s.SuggestedName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(s => s.Priority)
            .Take(5) // Limit to top 5 suggestions
            .ToList();
    }

    /// <summary>
    /// Use AI to generate more nuanced suggestions.
    /// </summary>
    private async Task<IReadOnlyList<ScopeSuggestion>> GetAiSuggestionsAsync(
        string playbookText,
        AvailableScopesSummary availableScopes,
        CancellationToken cancellationToken)
    {
        var prompt = $$"""
            You are analyzing a playbook description to identify missing scopes.

            Playbook Description:
            {{playbookText}}

            Available Scopes:
            - Actions: {{string.Join(", ", availableScopes.ActionNames.Take(10))}}
            - Skills: {{string.Join(", ", availableScopes.SkillNames.Take(10))}}
            - Tools: {{string.Join(", ", availableScopes.ToolTypes)}}
            - Knowledge: {{string.Join(", ", availableScopes.KnowledgeNames.Take(10))}}

            Identify 1-2 scope gaps that would improve this playbook.
            Consider what tools, skills, or knowledge are needed but not available.

            Return ONLY valid JSON in this format:
            {
              "suggestions": [
                {
                  "scopeType": "Tool|Skill|Knowledge|Action",
                  "name": "CUST-SuggestedName",
                  "description": "What this scope would do",
                  "reason": "Why this scope is needed",
                  "priority": "High|Medium|Low"
                }
              ]
            }
            """;

        try
        {
            var response = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);

            // Parse response
            var json = ExtractJson(response);
            var parsed = JsonSerializer.Deserialize<AiSuggestionResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed?.Suggestions != null)
            {
                return parsed.Suggestions
                    .Select(s => new ScopeSuggestion
                    {
                        ScopeType = ParseScopeType(s.ScopeType),
                        SuggestedName = s.Name,
                        Description = s.Description,
                        Reason = s.Reason,
                        Priority = ParsePriority(s.Priority),
                        IsCustomRequired = true
                    })
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get AI suggestions for scope gaps");
        }

        return Array.Empty<ScopeSuggestion>();
    }

    private static string ExtractJson(string response)
    {
        var json = response.Trim();
        if (json.StartsWith("```"))
        {
            var startIndex = json.IndexOf('{');
            var endIndex = json.LastIndexOf('}');
            if (startIndex >= 0 && endIndex > startIndex)
            {
                json = json.Substring(startIndex, endIndex - startIndex + 1);
            }
        }
        return json;
    }

    private static ScopeType ParseScopeType(string? type) => type?.ToLowerInvariant() switch
    {
        "tool" => ScopeType.Tool,
        "skill" => ScopeType.Skill,
        "knowledge" => ScopeType.Knowledge,
        "action" => ScopeType.Action,
        _ => ScopeType.Tool
    };

    private static SuggestionPriority ParsePriority(string? priority) => priority?.ToLowerInvariant() switch
    {
        "high" => SuggestionPriority.High,
        "low" => SuggestionPriority.Low,
        _ => SuggestionPriority.Medium
    };
}

/// <summary>
/// Interface for scope gap detection.
/// </summary>
public interface IScopeGapDetector
{
    /// <summary>
    /// Analyzes playbook description and goals to detect missing scopes.
    /// </summary>
    Task<ScopeGapAnalysis> AnalyzePlaybookIntentAsync(
        string playbookDescription,
        string? playbookGoals,
        CancellationToken cancellationToken);

    /// <summary>
    /// Suggests tools for a specific intent.
    /// </summary>
    Task<IReadOnlyList<ScopeSuggestion>> SuggestToolsForIntentAsync(
        string intent,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of scope gap analysis.
/// </summary>
public record ScopeGapAnalysis
{
    /// <summary>Suggested scopes to fill gaps.</summary>
    public required IReadOnlyList<ScopeSuggestion> Suggestions { get; init; }

    /// <summary>Summary of analyzed intent.</summary>
    public string AnalyzedIntent { get; init; } = string.Empty;

    /// <summary>List of detected capabilities.</summary>
    public IReadOnlyList<string> DetectedCapabilities { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Suggestion for a missing scope.
/// </summary>
public record ScopeSuggestion
{
    /// <summary>Type of scope to create.</summary>
    public ScopeType ScopeType { get; init; }

    /// <summary>Suggested name for the scope.</summary>
    public required string SuggestedName { get; init; }

    /// <summary>Description of what this scope would do.</summary>
    public required string Description { get; init; }

    /// <summary>Why this scope is needed.</summary>
    public required string Reason { get; init; }

    /// <summary>Priority of this suggestion.</summary>
    public SuggestionPriority Priority { get; init; }

    /// <summary>Whether custom implementation is required.</summary>
    public bool IsCustomRequired { get; init; }

    /// <summary>Suggested content (for knowledge scopes).</summary>
    public string? SuggestedContent { get; init; }
}

/// <summary>
/// Priority level for scope suggestions.
/// </summary>
public enum SuggestionPriority
{
    /// <summary>Low priority - nice to have.</summary>
    Low = 0,

    /// <summary>Medium priority - recommended.</summary>
    Medium = 1,

    /// <summary>High priority - essential for playbook functionality.</summary>
    High = 2
}

// Internal types
internal record AvailableScopesSummary
{
    public List<string> ActionNames { get; init; } = new();
    public List<string> SkillNames { get; init; } = new();
    public List<string> ToolNames { get; init; } = new();
    public List<string> ToolTypes { get; init; } = new();
    public List<string> KnowledgeNames { get; init; } = new();
}

internal record AiSuggestionResponse
{
    public List<AiSuggestion>? Suggestions { get; init; }
}

internal record AiSuggestion
{
    public string? ScopeType { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string? Priority { get; init; }
}
