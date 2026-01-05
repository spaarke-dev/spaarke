using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Resolves analysis scopes from Dataverse entities.
/// Loads Skills, Knowledge, Tools by ID or from Playbook configuration.
/// </summary>
/// <remarks>
/// Phase 1 Scaffolding: Returns stub data until Dataverse entity operations are implemented.
/// IDataverseService will be extended with Analysis entity methods in Task 032.
/// </remarks>
public class ScopeResolverService : IScopeResolverService
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<ScopeResolverService> _logger;

    // In-memory stub data for Phase 1 (will be replaced with Dataverse in Task 032)
    private static readonly Dictionary<Guid, AnalysisAction> _stubActions = new()
    {
        [Guid.Parse("00000000-0000-0000-0000-000000000001")] = new AnalysisAction
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "Summarize Document",
            Description = "Generate a comprehensive summary of the document",
            SystemPrompt = "You are an AI assistant that creates clear, comprehensive document summaries. " +
                          "Focus on key points, main arguments, and important details.",
            SortOrder = 1
        },
        [Guid.Parse("00000000-0000-0000-0000-000000000002")] = new AnalysisAction
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Name = "Review Agreement",
            Description = "Analyze legal agreement terms and risks",
            SystemPrompt = "You are a legal assistant analyzing document terms. " +
                          "Identify key obligations, risks, deadlines, and important clauses.",
            SortOrder = 2
        }
    };

    private static readonly Dictionary<Guid, AnalysisSkill> _stubSkills = new()
    {
        [Guid.Parse("10000000-0000-0000-0000-000000000001")] = new AnalysisSkill
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Name = "Legal Analysis",
            Description = "Analyze legal terms and contract language",
            PromptFragment = "Focus on identifying legal obligations, rights, and potential liabilities.",
            Category = "Legal"
        },
        [Guid.Parse("10000000-0000-0000-0000-000000000002")] = new AnalysisSkill
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Name = "Financial Analysis",
            Description = "Analyze financial data and metrics",
            PromptFragment = "Extract and analyze financial figures, trends, and key metrics.",
            Category = "Finance"
        },
        [Guid.Parse("10000000-0000-0000-0000-000000000003")] = new AnalysisSkill
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Name = "Risk Assessment",
            Description = "Identify and assess risks in documents",
            PromptFragment = "Evaluate potential risks, their severity, and suggested mitigations.",
            Category = "Risk"
        }
    };

    private static readonly Dictionary<Guid, AnalysisKnowledge> _stubKnowledge = new()
    {
        [Guid.Parse("20000000-0000-0000-0000-000000000001")] = new AnalysisKnowledge
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Name = "Standard Terms Reference",
            Description = "Reference guide for standard contract terms",
            Type = KnowledgeType.Inline,
            Content = "Standard contract terms include termination clauses, liability limitations, and payment terms."
        },
        [Guid.Parse("20000000-0000-0000-0000-000000000002")] = new AnalysisKnowledge
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Name = "Company Knowledge Base",
            Description = "RAG-enabled company knowledge base",
            Type = KnowledgeType.RagIndex,
            DeploymentId = Guid.Parse("30000000-0000-0000-0000-000000000001")
        }
    };

    private static readonly Dictionary<Guid, AnalysisTool> _stubTools = new()
    {
        [Guid.Parse("40000000-0000-0000-0000-000000000001")] = new AnalysisTool
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
            Name = "Document Summarizer",
            Description = "Generate document summaries",
            Type = ToolType.Summary,
            Configuration = """{"maxLength": 500, "format": "bullet"}"""
        },
        [Guid.Parse("40000000-0000-0000-0000-000000000002")] = new AnalysisTool
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000002"),
            Name = "Risk Detector",
            Description = "Detect and categorize risks",
            Type = ToolType.RiskDetector,
            Configuration = """{"severityLevels": ["High", "Medium", "Low"]}"""
        },
        [Guid.Parse("40000000-0000-0000-0000-000000000003")] = new AnalysisTool
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000003"),
            Name = "Date Extractor",
            Description = "Extract and normalize dates",
            Type = ToolType.DateExtractor,
            Configuration = """{"includeRelativeDates": true}"""
        },
        [Guid.Parse("40000000-0000-0000-0000-000000000004")] = new AnalysisTool
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000004"),
            Name = "Financial Calculator",
            Description = "Extract and calculate financial data",
            Type = ToolType.FinancialCalculator,
            Configuration = """{"currencies": ["USD", "EUR", "GBP"]}"""
        }
    };

    public ScopeResolverService(
        IDataverseService dataverseService,
        ILogger<ScopeResolverService> logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ResolvedScopes> ResolveScopesAsync(
        Guid[] skillIds,
        Guid[] knowledgeIds,
        Guid[] toolIds,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes: {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
            skillIds.Length, knowledgeIds.Length, toolIds.Length);

        // Phase 1: Return empty scopes (actual resolution in Task 032)
        _logger.LogInformation("Phase 1: Returning empty scopes (Dataverse integration in Task 032)");

        return Task.FromResult(new ResolvedScopes([], [], []));
    }

    /// <inheritdoc />
    public Task<ResolvedScopes> ResolvePlaybookScopesAsync(
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes from playbook {PlaybookId}", playbookId);

        // Phase 1: Playbook resolution not yet implemented
        _logger.LogWarning("Playbook resolution not yet implemented, returning empty scopes");

        return Task.FromResult(new ResolvedScopes([], [], []));
    }

    /// <inheritdoc />
    public Task<AnalysisAction?> GetActionAsync(
        Guid actionId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading action {ActionId}", actionId);

        // Phase 1: Use stub actions or create default
        if (_stubActions.TryGetValue(actionId, out var action))
        {
            _logger.LogDebug("Found stub action: {ActionName}", action.Name);
            return Task.FromResult<AnalysisAction?>(action);
        }

        // Return a default action for any unknown ID
        _logger.LogInformation("Action {ActionId} not in stub data, returning default action", actionId);
        return Task.FromResult<AnalysisAction?>(new AnalysisAction
        {
            Id = actionId,
            Name = "Default Analysis",
            Description = "Analyze the document",
            SystemPrompt = "You are an AI assistant that analyzes documents and provides helpful insights. " +
                          "Be thorough, accurate, and provide clear explanations.",
            SortOrder = 0
        });
    }

    /// <inheritdoc />
    public Task<ScopeListResult<AnalysisSkill>> ListSkillsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing skills: Page={Page}, PageSize={PageSize}", options.Page, options.PageSize);

        var items = _stubSkills.Values.AsEnumerable();

        // Apply name filter
        if (!string.IsNullOrWhiteSpace(options.NameFilter))
        {
            items = items.Where(s => s.Name.Contains(options.NameFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Apply category filter
        if (!string.IsNullOrWhiteSpace(options.CategoryFilter))
        {
            items = items.Where(s => s.Category?.Equals(options.CategoryFilter, StringComparison.OrdinalIgnoreCase) == true);
        }

        // Apply sorting
        items = options.SortBy.ToLowerInvariant() switch
        {
            "name" => options.SortDescending ? items.OrderByDescending(s => s.Name) : items.OrderBy(s => s.Name),
            "category" => options.SortDescending ? items.OrderByDescending(s => s.Category) : items.OrderBy(s => s.Category),
            _ => items.OrderBy(s => s.Name)
        };

        var totalCount = items.Count();
        var pagedItems = items
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToArray();

        return Task.FromResult(new ScopeListResult<AnalysisSkill>
        {
            Items = pagedItems,
            TotalCount = totalCount,
            Page = options.Page,
            PageSize = options.PageSize
        });
    }

    /// <inheritdoc />
    public Task<ScopeListResult<AnalysisKnowledge>> ListKnowledgeAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing knowledge: Page={Page}, PageSize={PageSize}", options.Page, options.PageSize);

        var items = _stubKnowledge.Values.AsEnumerable();

        // Apply name filter
        if (!string.IsNullOrWhiteSpace(options.NameFilter))
        {
            items = items.Where(k => k.Name.Contains(options.NameFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Apply sorting
        items = options.SortBy.ToLowerInvariant() switch
        {
            "name" => options.SortDescending ? items.OrderByDescending(k => k.Name) : items.OrderBy(k => k.Name),
            "type" => options.SortDescending ? items.OrderByDescending(k => k.Type) : items.OrderBy(k => k.Type),
            _ => items.OrderBy(k => k.Name)
        };

        var totalCount = items.Count();
        var pagedItems = items
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToArray();

        return Task.FromResult(new ScopeListResult<AnalysisKnowledge>
        {
            Items = pagedItems,
            TotalCount = totalCount,
            Page = options.Page,
            PageSize = options.PageSize
        });
    }

    /// <inheritdoc />
    public Task<ScopeListResult<AnalysisTool>> ListToolsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing tools: Page={Page}, PageSize={PageSize}", options.Page, options.PageSize);

        var items = _stubTools.Values.AsEnumerable();

        // Apply name filter
        if (!string.IsNullOrWhiteSpace(options.NameFilter))
        {
            items = items.Where(t => t.Name.Contains(options.NameFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Apply sorting
        items = options.SortBy.ToLowerInvariant() switch
        {
            "name" => options.SortDescending ? items.OrderByDescending(t => t.Name) : items.OrderBy(t => t.Name),
            "type" => options.SortDescending ? items.OrderByDescending(t => t.Type) : items.OrderBy(t => t.Type),
            _ => items.OrderBy(t => t.Name)
        };

        var totalCount = items.Count();
        var pagedItems = items
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToArray();

        return Task.FromResult(new ScopeListResult<AnalysisTool>
        {
            Items = pagedItems,
            TotalCount = totalCount,
            Page = options.Page,
            PageSize = options.PageSize
        });
    }

    /// <inheritdoc />
    public Task<ScopeListResult<AnalysisAction>> ListActionsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing actions: Page={Page}, PageSize={PageSize}", options.Page, options.PageSize);

        var items = _stubActions.Values.AsEnumerable();

        // Apply name filter
        if (!string.IsNullOrWhiteSpace(options.NameFilter))
        {
            items = items.Where(a => a.Name.Contains(options.NameFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Apply sorting
        items = options.SortBy.ToLowerInvariant() switch
        {
            "name" => options.SortDescending ? items.OrderByDescending(a => a.Name) : items.OrderBy(a => a.Name),
            "sortorder" => options.SortDescending ? items.OrderByDescending(a => a.SortOrder) : items.OrderBy(a => a.SortOrder),
            _ => items.OrderBy(a => a.SortOrder)
        };

        var totalCount = items.Count();
        var pagedItems = items
            .Skip((options.Page - 1) * options.PageSize)
            .Take(options.PageSize)
            .ToArray();

        return Task.FromResult(new ScopeListResult<AnalysisAction>
        {
            Items = pagedItems,
            TotalCount = totalCount,
            Page = options.Page,
            PageSize = options.PageSize
        });
    }
}
