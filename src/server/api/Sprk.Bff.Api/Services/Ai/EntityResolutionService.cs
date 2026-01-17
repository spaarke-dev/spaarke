using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for entity resolution operations.
/// </summary>
public interface IEntityResolutionService
{
    /// <summary>
    /// Resolve a node reference to an actual canvas node.
    /// </summary>
    Task<EntityResolutionResult> ResolveNodeAsync(
        NodeResolutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a scope reference to an actual scope entity.
    /// </summary>
    Task<EntityResolutionResult> ResolveScopeAsync(
        ScopeResolutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves entity references from user messages to actual node/scope IDs.
/// Uses fuzzy matching and AI assistance when references are ambiguous.
/// </summary>
/// <remarks>
/// Entity resolution confidence threshold: 80%
/// Below 80%: Returns multiple candidate matches for user selection.
/// </remarks>
public class EntityResolutionService : IEntityResolutionService
{
    private readonly IOpenAiClient _openAiClient;
    private readonly IModelSelector _modelSelector;
    private readonly IScopeResolverService _scopeResolver;
    private readonly ILogger<EntityResolutionService> _logger;

    // Confidence threshold per design spec
    private const double EntityConfidenceThreshold = 0.80;

    public EntityResolutionService(
        IOpenAiClient openAiClient,
        IModelSelector modelSelector,
        IScopeResolverService scopeResolver,
        ILogger<EntityResolutionService> logger)
    {
        _openAiClient = openAiClient;
        _modelSelector = modelSelector;
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EntityResolutionResult> ResolveNodeAsync(
        NodeResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);

        _logger.LogDebug("Resolving node reference: {Reference}", request.Reference);

        var reference = request.Reference.Trim().ToLowerInvariant();
        var nodes = request.CanvasContext.Nodes ?? [];

        // Handle special references first
        if (IsSelectedNodeReference(reference))
        {
            return ResolveSelectedNode(request);
        }

        // No nodes available
        if (nodes.Length == 0)
        {
            return new EntityResolutionResult
            {
                OriginalReference = request.Reference,
                EntityType = EntityType.Node,
                Confidence = 0.0,
                Reasoning = "No nodes on canvas to match against"
            };
        }

        // Try exact/fuzzy matching first
        var localMatches = FindLocalNodeMatches(reference, nodes);

        if (localMatches.Length == 1 && localMatches[0].Confidence >= EntityConfidenceThreshold)
        {
            _logger.LogDebug(
                "Node resolved locally with high confidence: {Id} ({Confidence:P0})",
                localMatches[0].Id, localMatches[0].Confidence);

            return new EntityResolutionResult
            {
                OriginalReference = request.Reference,
                EntityType = EntityType.Node,
                BestMatch = localMatches[0],
                Confidence = localMatches[0].Confidence,
                Reasoning = $"Matched by {localMatches[0].MatchReason}"
            };
        }

        // Use AI for ambiguous or low-confidence cases
        var aiResult = await ResolveWithAiAsync(
            request.Reference,
            nodes.Select(n => new { n.Id, n.Label, n.Type }).ToArray(),
            "node",
            cancellationToken);

        return BuildNodeResolutionResult(request.Reference, aiResult, nodes, localMatches);
    }

    /// <inheritdoc />
    public async Task<EntityResolutionResult> ResolveScopeAsync(
        ScopeResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);

        _logger.LogDebug(
            "Resolving scope reference: {Reference} (category: {Category})",
            request.Reference, request.ExpectedCategory);

        // Load available scopes based on expected category
        var availableScopes = await LoadAvailableScopesAsync(
            request.ExpectedCategory,
            request.SearchAllCategories,
            cancellationToken);

        if (availableScopes.Length == 0)
        {
            return new EntityResolutionResult
            {
                OriginalReference = request.Reference,
                EntityType = EntityType.Scope,
                ScopeCategory = request.ExpectedCategory,
                Confidence = 0.0,
                Reasoning = "No scopes available to match against"
            };
        }

        var reference = request.Reference.Trim().ToLowerInvariant();

        // Try exact/fuzzy matching first
        var localMatches = FindLocalScopeMatches(reference, availableScopes);

        if (localMatches.Length == 1 && localMatches[0].Confidence >= EntityConfidenceThreshold)
        {
            _logger.LogDebug(
                "Scope resolved locally with high confidence: {Id} ({Confidence:P0})",
                localMatches[0].Id, localMatches[0].Confidence);

            return new EntityResolutionResult
            {
                OriginalReference = request.Reference,
                EntityType = EntityType.Scope,
                ScopeCategory = request.ExpectedCategory,
                BestMatch = localMatches[0],
                Confidence = localMatches[0].Confidence,
                Reasoning = $"Matched by {localMatches[0].MatchReason}"
            };
        }

        // Use AI for ambiguous cases
        var aiResult = await ResolveWithAiAsync(
            request.Reference,
            availableScopes,
            "scope",
            cancellationToken);

        return BuildScopeResolutionResult(
            request.Reference,
            request.ExpectedCategory,
            aiResult,
            availableScopes,
            localMatches);
    }

    #region Private Methods - Local Matching

    private static bool IsSelectedNodeReference(string reference)
    {
        var selectedPatterns = new[]
        {
            "this node", "this one", "selected node", "current node",
            "the selected", "the current", "it"
        };

        return selectedPatterns.Any(p => reference.Contains(p));
    }

    private static EntityResolutionResult ResolveSelectedNode(NodeResolutionRequest request)
    {
        if (string.IsNullOrEmpty(request.SelectedNodeId) ||
            string.IsNullOrEmpty(request.CanvasContext.SelectedNodeId))
        {
            return new EntityResolutionResult
            {
                OriginalReference = request.Reference,
                EntityType = EntityType.Node,
                Confidence = 0.0,
                Reasoning = "No node is currently selected"
            };
        }

        var selectedId = request.SelectedNodeId ?? request.CanvasContext.SelectedNodeId;
        var selectedNode = request.CanvasContext.Nodes?
            .FirstOrDefault(n => n.Id == selectedId);

        if (selectedNode == null)
        {
            return new EntityResolutionResult
            {
                OriginalReference = request.Reference,
                EntityType = EntityType.Node,
                Confidence = 0.0,
                Reasoning = "Selected node not found in canvas context"
            };
        }

        return new EntityResolutionResult
        {
            OriginalReference = request.Reference,
            EntityType = EntityType.Node,
            BestMatch = new EntityMatch
            {
                Id = selectedNode.Id,
                Label = selectedNode.Label ?? selectedNode.Id,
                Type = selectedNode.Type,
                Confidence = 1.0,
                MatchReason = "Reference to selected node"
            },
            Confidence = 1.0,
            Reasoning = "Resolved reference to currently selected node"
        };
    }

    private static EntityMatch[] FindLocalNodeMatches(string reference, CanvasNodeSummary[] nodes)
    {
        var matches = new List<EntityMatch>();

        foreach (var node in nodes)
        {
            var (confidence, reason) = CalculateNodeMatchScore(reference, node);

            if (confidence > 0.3) // Minimum threshold for candidates
            {
                matches.Add(new EntityMatch
                {
                    Id = node.Id,
                    Label = node.Label ?? node.Id,
                    Type = node.Type,
                    Confidence = confidence,
                    MatchReason = reason
                });
            }
        }

        return matches.OrderByDescending(m => m.Confidence).ToArray();
    }

    private static (double confidence, string reason) CalculateNodeMatchScore(
        string reference, CanvasNodeSummary node)
    {
        var label = (node.Label ?? "").ToLowerInvariant();
        var nodeId = node.Id.ToLowerInvariant();
        var nodeType = node.Type.ToLowerInvariant();

        // Exact ID match
        if (reference == nodeId || reference.Contains(nodeId))
        {
            return (0.95, "exact ID match");
        }

        // Exact label match
        if (!string.IsNullOrEmpty(label) && reference == label)
        {
            return (0.95, "exact label match");
        }

        // Label contained in reference
        if (!string.IsNullOrEmpty(label) && reference.Contains(label))
        {
            return (0.85, "label contained in reference");
        }

        // Reference contained in label
        if (!string.IsNullOrEmpty(label) && label.Contains(reference))
        {
            return (0.80, "reference contained in label");
        }

        // Type match (e.g., "the analysis node" matches an aiAnalysis node)
        if (reference.Contains(nodeType) || reference.Contains(nodeType.Replace("ai", "").ToLower()))
        {
            return (0.60, "node type match");
        }

        // Word overlap
        var refWords = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var labelWords = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (labelWords.Length > 0)
        {
            var overlap = refWords.Intersect(labelWords).Count();
            if (overlap > 0)
            {
                var overlapRatio = (double)overlap / Math.Max(refWords.Length, labelWords.Length);
                return (0.40 + (overlapRatio * 0.35), "word overlap");
            }
        }

        return (0.0, "no match");
    }

    private static EntityMatch[] FindLocalScopeMatches(string reference, object[] scopes)
    {
        var matches = new List<EntityMatch>();

        foreach (var scope in scopes)
        {
            var id = GetScopeId(scope);
            var name = GetScopeName(scope);
            var type = GetScopeType(scope);

            var (confidence, reason) = CalculateScopeMatchScore(reference, name);

            if (confidence > 0.3)
            {
                matches.Add(new EntityMatch
                {
                    Id = id,
                    Label = name,
                    Type = type,
                    Confidence = confidence,
                    MatchReason = reason
                });
            }
        }

        return matches.OrderByDescending(m => m.Confidence).ToArray();
    }

    private static (double confidence, string reason) CalculateScopeMatchScore(
        string reference, string name)
    {
        var nameLower = name.ToLowerInvariant();

        // Exact name match
        if (reference == nameLower)
        {
            return (0.95, "exact name match");
        }

        // Name contained in reference
        if (reference.Contains(nameLower))
        {
            return (0.85, "name contained in reference");
        }

        // Reference contained in name
        if (nameLower.Contains(reference))
        {
            return (0.80, "reference contained in name");
        }

        // Word overlap
        var refWords = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameWords = nameLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (nameWords.Length > 0)
        {
            var overlap = refWords.Intersect(nameWords).Count();
            if (overlap > 0)
            {
                var overlapRatio = (double)overlap / Math.Max(refWords.Length, nameWords.Length);
                return (0.40 + (overlapRatio * 0.40), "word overlap");
            }
        }

        return (0.0, "no match");
    }

    private static string GetScopeId(object scope) => scope switch
    {
        AnalysisAction a => a.Id.ToString(),
        AnalysisSkill s => s.Id.ToString(),
        AnalysisKnowledge k => k.Id.ToString(),
        AnalysisTool t => t.Id.ToString(),
        _ => throw new ArgumentException("Unknown scope type")
    };

    private static string GetScopeName(object scope) => scope switch
    {
        AnalysisAction a => a.Name,
        AnalysisSkill s => s.Name,
        AnalysisKnowledge k => k.Name,
        AnalysisTool t => t.Name,
        _ => throw new ArgumentException("Unknown scope type")
    };

    private static string GetScopeType(object scope) => scope switch
    {
        AnalysisAction => "action",
        AnalysisSkill s => s.Category ?? "skill",
        AnalysisKnowledge k => k.Type.ToString().ToLowerInvariant(),
        AnalysisTool t => t.Type.ToString().ToLowerInvariant(),
        _ => throw new ArgumentException("Unknown scope type")
    };

    #endregion

    #region Private Methods - AI Resolution

    private async Task<EntityResolutionAiResponse?> ResolveWithAiAsync<T>(
        string reference,
        T[] availableEntities,
        string entityTypeName,
        CancellationToken cancellationToken)
    {
        var model = _modelSelector.SelectModel(OperationType.EntityResolution);

        var prompt = BuildResolutionPrompt(reference, availableEntities, entityTypeName);

        try
        {
            var response = await _openAiClient.GetCompletionAsync(
                prompt, model, cancellationToken);

            return ParseAiResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI entity resolution failed, falling back to local matching");
            return null;
        }
    }

    private static string BuildResolutionPrompt<T>(
        string reference,
        T[] availableEntities,
        string entityTypeName)
    {
        var entitiesJson = JsonSerializer.Serialize(availableEntities, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return $$"""
            You are an entity resolution system. Match the user's reference to the most likely {{entityTypeName}}.

            User's reference: "{{reference}}"

            Available {{entityTypeName}}s:
            {{entitiesJson}}

            Analyze the reference and find the best match. Consider:
            - Exact matches in ID or label/name
            - Partial matches and common abbreviations
            - Semantic similarity (e.g., "summary" might match "TL;DR")
            - Context clues in the reference

            Return a JSON object with:
            {
                "matchedId": "best matching ID or null if no good match",
                "confidence": 0.0-1.0,
                "candidates": [
                    {"id": "...", "confidence": 0.0-1.0, "reason": "why this might match"}
                ],
                "reasoning": "explanation of matching decision"
            }

            If confidence is below 0.80, include multiple candidates ranked by confidence.
            If no reasonable match exists, set matchedId to null and confidence to 0.
            """;
    }

    private EntityResolutionAiResponse? ParseAiResponse(string response)
    {
        try
        {
            // Strip markdown code blocks if present
            var json = response.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('{');
                var end = json.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    json = json[start..(end + 1)];
                }
            }

            return JsonSerializer.Deserialize<EntityResolutionAiResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI entity resolution response");
            return null;
        }
    }

    #endregion

    #region Private Methods - Result Building

    private async Task<object[]> LoadAvailableScopesAsync(
        ScopeCategory? expectedCategory,
        bool searchAll,
        CancellationToken cancellationToken)
    {
        var options = new ScopeListOptions { PageSize = 100 };
        var scopes = new List<object>();

        if (searchAll || expectedCategory == null)
        {
            // Load all categories
            var actions = await _scopeResolver.ListActionsAsync(options, cancellationToken);
            var skills = await _scopeResolver.ListSkillsAsync(options, cancellationToken);
            var knowledge = await _scopeResolver.ListKnowledgeAsync(options, cancellationToken);
            var tools = await _scopeResolver.ListToolsAsync(options, cancellationToken);

            scopes.AddRange(actions.Items);
            scopes.AddRange(skills.Items);
            scopes.AddRange(knowledge.Items);
            scopes.AddRange(tools.Items);
        }
        else
        {
            // Load specific category
            switch (expectedCategory)
            {
                case ScopeCategory.Action:
                    var actions = await _scopeResolver.ListActionsAsync(options, cancellationToken);
                    scopes.AddRange(actions.Items);
                    break;
                case ScopeCategory.Skill:
                    var skills = await _scopeResolver.ListSkillsAsync(options, cancellationToken);
                    scopes.AddRange(skills.Items);
                    break;
                case ScopeCategory.Knowledge:
                    var knowledge = await _scopeResolver.ListKnowledgeAsync(options, cancellationToken);
                    scopes.AddRange(knowledge.Items);
                    break;
                case ScopeCategory.Tool:
                    var tools = await _scopeResolver.ListToolsAsync(options, cancellationToken);
                    scopes.AddRange(tools.Items);
                    break;
            }
        }

        return scopes.ToArray();
    }

    private EntityResolutionResult BuildNodeResolutionResult(
        string reference,
        EntityResolutionAiResponse? aiResult,
        CanvasNodeSummary[] nodes,
        EntityMatch[] localMatches)
    {
        // Prefer AI result if available and confident
        if (aiResult != null && !string.IsNullOrEmpty(aiResult.MatchedId))
        {
            var matchedNode = nodes.FirstOrDefault(n => n.Id == aiResult.MatchedId);
            if (matchedNode != null)
            {
                var candidates = aiResult.Candidates?
                    .Where(c => c.Confidence >= 0.30)
                    .Select(c =>
                    {
                        var node = nodes.FirstOrDefault(n => n.Id == c.Id);
                        return node == null ? null : new EntityMatch
                        {
                            Id = c.Id,
                            Label = node.Label ?? node.Id,
                            Type = node.Type,
                            Confidence = c.Confidence,
                            MatchReason = c.Reason
                        };
                    })
                    .Where(m => m != null)
                    .ToArray() as EntityMatch[];

                return new EntityResolutionResult
                {
                    OriginalReference = reference,
                    EntityType = EntityType.Node,
                    BestMatch = new EntityMatch
                    {
                        Id = matchedNode.Id,
                        Label = matchedNode.Label ?? matchedNode.Id,
                        Type = matchedNode.Type,
                        Confidence = aiResult.Confidence,
                        MatchReason = aiResult.Reasoning
                    },
                    Confidence = aiResult.Confidence,
                    CandidateMatches = aiResult.Confidence < EntityConfidenceThreshold ? candidates : null,
                    Reasoning = aiResult.Reasoning
                };
            }
        }

        // Fall back to local matches
        if (localMatches.Length > 0)
        {
            return new EntityResolutionResult
            {
                OriginalReference = reference,
                EntityType = EntityType.Node,
                BestMatch = localMatches[0],
                Confidence = localMatches[0].Confidence,
                CandidateMatches = localMatches[0].Confidence < EntityConfidenceThreshold
                    ? localMatches.Take(5).ToArray()
                    : null,
                Reasoning = $"Local fuzzy matching: {localMatches[0].MatchReason}"
            };
        }

        return new EntityResolutionResult
        {
            OriginalReference = reference,
            EntityType = EntityType.Node,
            Confidence = 0.0,
            Reasoning = "No matching node found"
        };
    }

    private EntityResolutionResult BuildScopeResolutionResult(
        string reference,
        ScopeCategory? expectedCategory,
        EntityResolutionAiResponse? aiResult,
        object[] availableScopes,
        EntityMatch[] localMatches)
    {
        // Prefer AI result if available and confident
        if (aiResult != null && !string.IsNullOrEmpty(aiResult.MatchedId))
        {
            var matchedScope = availableScopes.FirstOrDefault(s => GetScopeId(s) == aiResult.MatchedId);
            if (matchedScope != null)
            {
                var candidates = aiResult.Candidates?
                    .Where(c => c.Confidence >= 0.30)
                    .Select(c =>
                    {
                        var scope = availableScopes.FirstOrDefault(s => GetScopeId(s) == c.Id);
                        return scope == null ? null : new EntityMatch
                        {
                            Id = c.Id,
                            Label = GetScopeName(scope),
                            Type = GetScopeType(scope),
                            Confidence = c.Confidence,
                            MatchReason = c.Reason
                        };
                    })
                    .Where(m => m != null)
                    .ToArray() as EntityMatch[];

                return new EntityResolutionResult
                {
                    OriginalReference = reference,
                    EntityType = EntityType.Scope,
                    ScopeCategory = expectedCategory,
                    BestMatch = new EntityMatch
                    {
                        Id = GetScopeId(matchedScope),
                        Label = GetScopeName(matchedScope),
                        Type = GetScopeType(matchedScope),
                        Confidence = aiResult.Confidence,
                        MatchReason = aiResult.Reasoning
                    },
                    Confidence = aiResult.Confidence,
                    CandidateMatches = aiResult.Confidence < EntityConfidenceThreshold ? candidates : null,
                    Reasoning = aiResult.Reasoning
                };
            }
        }

        // Fall back to local matches
        if (localMatches.Length > 0)
        {
            return new EntityResolutionResult
            {
                OriginalReference = reference,
                EntityType = EntityType.Scope,
                ScopeCategory = expectedCategory,
                BestMatch = localMatches[0],
                Confidence = localMatches[0].Confidence,
                CandidateMatches = localMatches[0].Confidence < EntityConfidenceThreshold
                    ? localMatches.Take(5).ToArray()
                    : null,
                Reasoning = $"Local fuzzy matching: {localMatches[0].MatchReason}"
            };
        }

        return new EntityResolutionResult
        {
            OriginalReference = reference,
            EntityType = EntityType.Scope,
            ScopeCategory = expectedCategory,
            Confidence = 0.0,
            Reasoning = "No matching scope found"
        };
    }

    #endregion
}
