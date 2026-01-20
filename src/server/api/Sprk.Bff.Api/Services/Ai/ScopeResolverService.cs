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
    public Task<ResolvedScopes> ResolveNodeScopesAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes from node {NodeId}", nodeId);

        // Phase 1: Node scope resolution not yet implemented
        // In Task 032, this will:
        // 1. Query sprk_playbooknode_skill N:N relationship for skills
        // 2. Query sprk_playbooknode_knowledge N:N relationship for knowledge
        // 3. Query sprk_toolid lookup for the single tool
        _logger.LogWarning("Node scope resolution not yet implemented, returning empty scopes");

        return Task.FromResult(new ResolvedScopes([], [], []));
    }

    /// <inheritdoc />
    public async Task<AnalysisAction?> GetActionAsync(
        Guid actionId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading action {ActionId} from Dataverse", actionId);

        try
        {
            // Fetch action from Dataverse
            var actionEntity = await _dataverseService.GetAnalysisActionAsync(actionId.ToString(), cancellationToken);

            if (actionEntity != null)
            {
                _logger.LogDebug("Found action in Dataverse: {ActionName}", actionEntity.Name);
                return new AnalysisAction
                {
                    Id = actionEntity.Id,
                    Name = actionEntity.Name ?? "Unknown",
                    Description = actionEntity.Description ?? "",
                    SystemPrompt = actionEntity.SystemPrompt ?? "You are an AI assistant that analyzes documents.",
                    SortOrder = actionEntity.SortOrder
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch action {ActionId} from Dataverse, falling back to stub data", actionId);
        }

        // Fallback: Check stub actions
        if (_stubActions.TryGetValue(actionId, out var action))
        {
            _logger.LogDebug("Found stub action: {ActionName}", action.Name);
            return action;
        }

        // Return a default action for any unknown ID
        _logger.LogInformation("Action {ActionId} not found in Dataverse or stub data, returning default action", actionId);
        return new AnalysisAction
        {
            Id = actionId,
            Name = "Default Analysis",
            Description = "Analyze the document",
            SystemPrompt = "You are an AI assistant that analyzes documents and provides helpful insights. " +
                          "Be thorough, accurate, and provide clear explanations.",
            SortOrder = 0
        };
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

    #region CRUD Operations (Task 002 Implementation)

    // Prefix constants for scope ownership
    private const string SystemPrefix = "SYS-";
    private const string CustomerPrefix = "CUST-";

    #region Private Helper Methods

    /// <summary>
    /// Validates that a scope is not system-owned (immutable).
    /// Throws ScopeOwnershipException if the scope has SYS- prefix or is marked immutable.
    /// </summary>
    /// <param name="name">Scope name to check.</param>
    /// <param name="isImmutable">Immutable flag from the record.</param>
    /// <param name="operation">Operation being attempted (for error message).</param>
    /// <exception cref="ScopeOwnershipException">Thrown if scope is system-owned.</exception>
    private void ValidateOwnership(string name, bool isImmutable, string operation)
    {
        if (isImmutable || name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Attempted to {Operation} system scope '{ScopeName}'", operation, name);
            throw new ScopeOwnershipException(name, operation);
        }
    }

    /// <summary>
    /// Determines if a name represents a system-owned scope.
    /// </summary>
    private static bool IsSystemScope(string name)
        => name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ensures a customer scope has the CUST- prefix.
    /// If the name already has SYS- or CUST- prefix, returns as-is.
    /// </summary>
    private static string EnsureCustomerPrefix(string name)
    {
        if (name.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(CustomerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return $"{CustomerPrefix}{name}";
    }

    #endregion

    #region Action CRUD

    /// <inheritdoc />
    public async Task<AnalysisAction> CreateActionAsync(CreateActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt, nameof(request.SystemPrompt));

        // Ensure customer prefix for new actions
        var name = EnsureCustomerPrefix(request.Name);

        _logger.LogInformation("Creating action '{ActionName}' with type {ActionType}", name, request.ActionType);

        // Generate new ID for the action
        var actionId = Guid.NewGuid();

        // Create in Dataverse via IDataverseService
        // Note: For Phase 1, we'll also store in stub data for fallback
        // Dataverse integration will be fully implemented in Task 032
        var action = new AnalysisAction
        {
            Id = actionId,
            Name = name,
            Description = request.Description,
            SystemPrompt = request.SystemPrompt,
            SortOrder = request.SortOrder,
            ActionType = request.ActionType,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false
        };

        // Add to stub data for Phase 1 (Dataverse persistence in Task 032)
        _stubActions[actionId] = action;

        _logger.LogInformation("Created action '{ActionName}' with ID {ActionId}", name, actionId);

        return await Task.FromResult(action);
    }

    /// <inheritdoc />
    public async Task<AnalysisAction> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Updating action {ActionId}", id);

        // Load existing action
        var existing = await GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("Action {ActionId} not found for update", id);
            throw new KeyNotFoundException($"Action with ID '{id}' not found.");
        }

        // Validate ownership - reject if system-owned
        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        // Apply updates (sparse update pattern)
        var updated = existing with
        {
            Name = request.Name != null ? EnsureCustomerPrefix(request.Name) : existing.Name,
            Description = request.Description ?? existing.Description,
            SystemPrompt = request.SystemPrompt ?? existing.SystemPrompt,
            SortOrder = request.SortOrder ?? existing.SortOrder,
            ActionType = request.ActionType ?? existing.ActionType
        };

        // Update in stub data (Dataverse persistence in Task 032)
        _stubActions[id] = updated;

        _logger.LogInformation("Updated action '{ActionName}' (ID: {ActionId})", updated.Name, id);

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteActionAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting action {ActionId}", id);

        // Load existing action
        var existing = await GetActionAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("Action {ActionId} not found for deletion", id);
            return false;
        }

        // Validate ownership - reject if system-owned
        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        // Delete from stub data (Dataverse persistence in Task 032)
        var removed = _stubActions.Remove(id);

        if (removed)
        {
            _logger.LogInformation("Deleted action '{ActionName}' (ID: {ActionId})", existing.Name, id);
        }

        return removed;
    }

    #endregion

    #region Skill CRUD

    /// <inheritdoc />
    public Task<AnalysisSkill?> GetSkillAsync(Guid skillId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting skill {SkillId}", skillId);

        // Check stub data for now
        if (_stubSkills.TryGetValue(skillId, out var skill))
        {
            return Task.FromResult<AnalysisSkill?>(skill);
        }

        return Task.FromResult<AnalysisSkill?>(null);
    }

    /// <inheritdoc />
    public async Task<AnalysisSkill> CreateSkillAsync(CreateSkillRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PromptFragment, nameof(request.PromptFragment));

        var name = EnsureCustomerPrefix(request.Name);

        _logger.LogInformation("Creating skill '{SkillName}'", name);

        var skillId = Guid.NewGuid();

        var skill = new AnalysisSkill
        {
            Id = skillId,
            Name = name,
            Description = request.Description,
            PromptFragment = request.PromptFragment,
            Category = request.Category,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false
        };

        _stubSkills[skillId] = skill;

        _logger.LogInformation("Created skill '{SkillName}' with ID {SkillId}", name, skillId);

        return await Task.FromResult(skill);
    }

    /// <inheritdoc />
    public async Task<AnalysisSkill> UpdateSkillAsync(Guid id, UpdateSkillRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Updating skill {SkillId}", id);

        var existing = await GetSkillAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("Skill {SkillId} not found for update", id);
            throw new KeyNotFoundException($"Skill with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        var updated = existing with
        {
            Name = request.Name != null ? EnsureCustomerPrefix(request.Name) : existing.Name,
            Description = request.Description ?? existing.Description,
            PromptFragment = request.PromptFragment ?? existing.PromptFragment,
            Category = request.Category ?? existing.Category
        };

        _stubSkills[id] = updated;

        _logger.LogInformation("Updated skill '{SkillName}' (ID: {SkillId})", updated.Name, id);

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSkillAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting skill {SkillId}", id);

        var existing = await GetSkillAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("Skill {SkillId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        var removed = _stubSkills.Remove(id);

        if (removed)
        {
            _logger.LogInformation("Deleted skill '{SkillName}' (ID: {SkillId})", existing.Name, id);
        }

        return removed;
    }

    #endregion

    #region Knowledge CRUD

    /// <inheritdoc />
    public Task<AnalysisKnowledge?> GetKnowledgeAsync(Guid knowledgeId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting knowledge {KnowledgeId}", knowledgeId);

        // Check stub data for now
        if (_stubKnowledge.TryGetValue(knowledgeId, out var knowledge))
        {
            return Task.FromResult<AnalysisKnowledge?>(knowledge);
        }

        return Task.FromResult<AnalysisKnowledge?>(null);
    }

    /// <inheritdoc />
    public async Task<AnalysisKnowledge> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));

        var name = EnsureCustomerPrefix(request.Name);

        _logger.LogInformation("Creating knowledge '{KnowledgeName}' with type {KnowledgeType}", name, request.Type);

        var knowledgeId = Guid.NewGuid();

        var knowledge = new AnalysisKnowledge
        {
            Id = knowledgeId,
            Name = name,
            Description = request.Description,
            Type = request.Type,
            Content = request.Content,
            DocumentId = request.DocumentId,
            DeploymentId = request.DeploymentId,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false
        };

        _stubKnowledge[knowledgeId] = knowledge;

        _logger.LogInformation("Created knowledge '{KnowledgeName}' with ID {KnowledgeId}", name, knowledgeId);

        return await Task.FromResult(knowledge);
    }

    /// <inheritdoc />
    public async Task<AnalysisKnowledge> UpdateKnowledgeAsync(Guid id, UpdateKnowledgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Updating knowledge {KnowledgeId}", id);

        var existing = await GetKnowledgeAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("Knowledge {KnowledgeId} not found for update", id);
            throw new KeyNotFoundException($"Knowledge with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        var updated = existing with
        {
            Name = request.Name != null ? EnsureCustomerPrefix(request.Name) : existing.Name,
            Description = request.Description ?? existing.Description,
            Type = request.Type ?? existing.Type,
            Content = request.Content ?? existing.Content,
            DocumentId = request.DocumentId ?? existing.DocumentId,
            DeploymentId = request.DeploymentId ?? existing.DeploymentId
        };

        _stubKnowledge[id] = updated;

        _logger.LogInformation("Updated knowledge '{KnowledgeName}' (ID: {KnowledgeId})", updated.Name, id);

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteKnowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting knowledge {KnowledgeId}", id);

        var existing = await GetKnowledgeAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("Knowledge {KnowledgeId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        var removed = _stubKnowledge.Remove(id);

        if (removed)
        {
            _logger.LogInformation("Deleted knowledge '{KnowledgeName}' (ID: {KnowledgeId})", existing.Name, id);
        }

        return removed;
    }

    #endregion

    #region Tool CRUD

    /// <inheritdoc />
    public Task<AnalysisTool?> GetToolAsync(Guid toolId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting tool {ToolId}", toolId);

        // Check stub data for now
        if (_stubTools.TryGetValue(toolId, out var tool))
        {
            return Task.FromResult<AnalysisTool?>(tool);
        }

        return Task.FromResult<AnalysisTool?>(null);
    }

    /// <inheritdoc />
    public async Task<AnalysisTool> CreateToolAsync(CreateToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request.Name));

        var name = EnsureCustomerPrefix(request.Name);

        _logger.LogInformation("Creating tool '{ToolName}' with type {ToolType}", name, request.Type);

        var toolId = Guid.NewGuid();

        var tool = new AnalysisTool
        {
            Id = toolId,
            Name = name,
            Description = request.Description,
            Type = request.Type,
            HandlerClass = request.HandlerClass,
            Configuration = request.Configuration,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false
        };

        _stubTools[toolId] = tool;

        _logger.LogInformation("Created tool '{ToolName}' with ID {ToolId}", name, toolId);

        return await Task.FromResult(tool);
    }

    /// <inheritdoc />
    public async Task<AnalysisTool> UpdateToolAsync(Guid id, UpdateToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug("Updating tool {ToolId}", id);

        var existing = await GetToolAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("Tool {ToolId} not found for update", id);
            throw new KeyNotFoundException($"Tool with ID '{id}' not found.");
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "update");

        var updated = existing with
        {
            Name = request.Name != null ? EnsureCustomerPrefix(request.Name) : existing.Name,
            Description = request.Description ?? existing.Description,
            Type = request.Type ?? existing.Type,
            HandlerClass = request.HandlerClass ?? existing.HandlerClass,
            Configuration = request.Configuration ?? existing.Configuration
        };

        _stubTools[id] = updated;

        _logger.LogInformation("Updated tool '{ToolName}' (ID: {ToolId})", updated.Name, id);

        return updated;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteToolAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting tool {ToolId}", id);

        var existing = await GetToolAsync(id, cancellationToken);
        if (existing == null)
        {
            _logger.LogWarning("Tool {ToolId} not found for deletion", id);
            return false;
        }

        ValidateOwnership(existing.Name, existing.IsImmutable, "delete");

        var removed = _stubTools.Remove(id);

        if (removed)
        {
            _logger.LogInformation("Deleted tool '{ToolName}' (ID: {ToolId})", existing.Name, id);
        }

        return removed;
    }

    #endregion

    #region Search Operations

    /// <inheritdoc />
    public Task<ScopeSearchResult> SearchScopesAsync(ScopeSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        _logger.LogDebug("Searching scopes with text '{SearchText}', types: {ScopeTypes}, ownerType: {OwnerType}",
            query.SearchText, query.ScopeTypes, query.OwnerType);

        // Determine which scope types to search
        var typesToSearch = query.ScopeTypes.Length > 0
            ? query.ScopeTypes
            : new[] { ScopeType.Action, ScopeType.Skill, ScopeType.Knowledge, ScopeType.Tool };

        var actions = Array.Empty<AnalysisAction>();
        var skills = Array.Empty<AnalysisSkill>();
        var knowledge = Array.Empty<AnalysisKnowledge>();
        var tools = Array.Empty<AnalysisTool>();

        var countsByType = new Dictionary<ScopeType, int>();

        // Search Actions
        if (typesToSearch.Contains(ScopeType.Action))
        {
            actions = SearchActions(query).ToArray();
            countsByType[ScopeType.Action] = actions.Length;
        }

        // Search Skills
        if (typesToSearch.Contains(ScopeType.Skill))
        {
            skills = SearchSkills(query).ToArray();
            countsByType[ScopeType.Skill] = skills.Length;
        }

        // Search Knowledge
        if (typesToSearch.Contains(ScopeType.Knowledge))
        {
            knowledge = SearchKnowledge(query).ToArray();
            countsByType[ScopeType.Knowledge] = knowledge.Length;
        }

        // Search Tools
        if (typesToSearch.Contains(ScopeType.Tool))
        {
            tools = SearchTools(query).ToArray();
            countsByType[ScopeType.Tool] = tools.Length;
        }

        var totalCount = countsByType.Values.Sum();

        _logger.LogDebug("Search found {TotalCount} results across {TypeCount} types", totalCount, countsByType.Count);

        return Task.FromResult(new ScopeSearchResult
        {
            Actions = actions,
            Skills = skills,
            Knowledge = knowledge,
            Tools = tools,
            TotalCount = totalCount,
            CountsByType = countsByType
        });
    }

    private IEnumerable<AnalysisAction> SearchActions(ScopeSearchQuery query)
    {
        var items = _stubActions.Values.AsEnumerable();

        // Apply text filter
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            items = items.Where(a =>
                a.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ||
                (a.Description?.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Apply owner type filter
        if (query.OwnerType.HasValue)
        {
            items = items.Where(a => a.OwnerType == query.OwnerType.Value);
        }

        // Apply editable-only filter
        if (query.EditableOnly)
        {
            items = items.Where(a => !a.IsImmutable && !IsSystemScope(a.Name));
        }

        // Apply pagination
        items = items
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize);

        return items;
    }

    private IEnumerable<AnalysisSkill> SearchSkills(ScopeSearchQuery query)
    {
        var items = _stubSkills.Values.AsEnumerable();

        // Apply text filter
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            items = items.Where(s =>
                s.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ||
                (s.Description?.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Apply owner type filter
        if (query.OwnerType.HasValue)
        {
            items = items.Where(s => s.OwnerType == query.OwnerType.Value);
        }

        // Apply category filter
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            items = items.Where(s => s.Category?.Equals(query.Category, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        // Apply editable-only filter
        if (query.EditableOnly)
        {
            items = items.Where(s => !s.IsImmutable && !IsSystemScope(s.Name));
        }

        // Apply pagination
        items = items
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize);

        return items;
    }

    private IEnumerable<AnalysisKnowledge> SearchKnowledge(ScopeSearchQuery query)
    {
        var items = _stubKnowledge.Values.AsEnumerable();

        // Apply text filter
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            items = items.Where(k =>
                k.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ||
                (k.Description?.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Apply owner type filter
        if (query.OwnerType.HasValue)
        {
            items = items.Where(k => k.OwnerType == query.OwnerType.Value);
        }

        // Apply editable-only filter
        if (query.EditableOnly)
        {
            items = items.Where(k => !k.IsImmutable && !IsSystemScope(k.Name));
        }

        // Apply pagination
        items = items
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize);

        return items;
    }

    private IEnumerable<AnalysisTool> SearchTools(ScopeSearchQuery query)
    {
        var items = _stubTools.Values.AsEnumerable();

        // Apply text filter
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            items = items.Where(t =>
                t.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ||
                (t.Description?.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Apply owner type filter
        if (query.OwnerType.HasValue)
        {
            items = items.Where(t => t.OwnerType == query.OwnerType.Value);
        }

        // Apply editable-only filter
        if (query.EditableOnly)
        {
            items = items.Where(t => !t.IsImmutable && !IsSystemScope(t.Name));
        }

        // Apply pagination
        items = items
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize);

        return items;
    }

    #endregion

    #region Save As Operations

    /// <inheritdoc />
    public async Task<AnalysisAction> SaveAsActionAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        _logger.LogInformation("Saving action {SourceId} as '{NewName}'", sourceId, newName);

        // Load source action
        var source = await GetActionAsync(sourceId, cancellationToken);
        if (source == null)
        {
            _logger.LogWarning("Source action {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source action with ID '{sourceId}' not found.");
        }

        // Create copy with new name and BasedOnId
        var name = EnsureCustomerPrefix(newName);
        var copyId = Guid.NewGuid();

        var copy = source with
        {
            Id = copyId,
            Name = name,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            BasedOnId = sourceId,
            ParentScopeId = null // Save As is a copy, not an extension
        };

        _stubActions[copyId] = copy;

        _logger.LogInformation("Created action copy '{Name}' (ID: {CopyId}) based on {SourceId}", name, copyId, sourceId);

        return copy;
    }

    /// <inheritdoc />
    public async Task<AnalysisSkill> SaveAsSkillAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        _logger.LogInformation("Saving skill {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetSkillAsync(sourceId, cancellationToken);
        if (source == null)
        {
            _logger.LogWarning("Source skill {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source skill with ID '{sourceId}' not found.");
        }

        var name = EnsureCustomerPrefix(newName);
        var copyId = Guid.NewGuid();

        var copy = source with
        {
            Id = copyId,
            Name = name,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            BasedOnId = sourceId,
            ParentScopeId = null
        };

        _stubSkills[copyId] = copy;

        _logger.LogInformation("Created skill copy '{Name}' (ID: {CopyId}) based on {SourceId}", name, copyId, sourceId);

        return copy;
    }

    /// <inheritdoc />
    public async Task<AnalysisKnowledge> SaveAsKnowledgeAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        _logger.LogInformation("Saving knowledge {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetKnowledgeAsync(sourceId, cancellationToken);
        if (source == null)
        {
            _logger.LogWarning("Source knowledge {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source knowledge with ID '{sourceId}' not found.");
        }

        var name = EnsureCustomerPrefix(newName);
        var copyId = Guid.NewGuid();

        var copy = source with
        {
            Id = copyId,
            Name = name,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            BasedOnId = sourceId,
            ParentScopeId = null
        };

        _stubKnowledge[copyId] = copy;

        _logger.LogInformation("Created knowledge copy '{Name}' (ID: {CopyId}) based on {SourceId}", name, copyId, sourceId);

        return copy;
    }

    /// <inheritdoc />
    public async Task<AnalysisTool> SaveAsToolAsync(Guid sourceId, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));

        _logger.LogInformation("Saving tool {SourceId} as '{NewName}'", sourceId, newName);

        var source = await GetToolAsync(sourceId, cancellationToken);
        if (source == null)
        {
            _logger.LogWarning("Source tool {SourceId} not found for Save As", sourceId);
            throw new KeyNotFoundException($"Source tool with ID '{sourceId}' not found.");
        }

        var name = EnsureCustomerPrefix(newName);
        var copyId = Guid.NewGuid();

        var copy = source with
        {
            Id = copyId,
            Name = name,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            BasedOnId = sourceId,
            ParentScopeId = null
        };

        _stubTools[copyId] = copy;

        _logger.LogInformation("Created tool copy '{Name}' (ID: {CopyId}) based on {SourceId}", name, copyId, sourceId);

        return copy;
    }

    #endregion

    #region Extend Operations

    /// <inheritdoc />
    public async Task<AnalysisAction> ExtendActionAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        _logger.LogInformation("Extending action {ParentId} with child '{ChildName}'", parentId, childName);

        // Load parent action
        var parent = await GetActionAsync(parentId, cancellationToken);
        if (parent == null)
        {
            _logger.LogWarning("Parent action {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent action with ID '{parentId}' not found.");
        }

        // Create child with parent reference
        var name = EnsureCustomerPrefix(childName);
        var childId = Guid.NewGuid();

        var child = parent with
        {
            Id = childId,
            Name = name,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            ParentScopeId = parentId,
            BasedOnId = null // Extend is not a copy
        };

        _stubActions[childId] = child;

        _logger.LogInformation("Created child action '{Name}' (ID: {ChildId}) extending {ParentId}", name, childId, parentId);

        return child;
    }

    /// <inheritdoc />
    public async Task<AnalysisSkill> ExtendSkillAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        _logger.LogInformation("Extending skill {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetSkillAsync(parentId, cancellationToken);
        if (parent == null)
        {
            _logger.LogWarning("Parent skill {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent skill with ID '{parentId}' not found.");
        }

        var name = EnsureCustomerPrefix(childName);
        var childId = Guid.NewGuid();

        var child = parent with
        {
            Id = childId,
            Name = name,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            ParentScopeId = parentId,
            BasedOnId = null
        };

        _stubSkills[childId] = child;

        _logger.LogInformation("Created child skill '{Name}' (ID: {ChildId}) extending {ParentId}", name, childId, parentId);

        return child;
    }

    /// <inheritdoc />
    public async Task<AnalysisKnowledge> ExtendKnowledgeAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        _logger.LogInformation("Extending knowledge {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetKnowledgeAsync(parentId, cancellationToken);
        if (parent == null)
        {
            _logger.LogWarning("Parent knowledge {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent knowledge with ID '{parentId}' not found.");
        }

        var name = EnsureCustomerPrefix(childName);
        var childId = Guid.NewGuid();

        var child = parent with
        {
            Id = childId,
            Name = name,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            ParentScopeId = parentId,
            BasedOnId = null
        };

        _stubKnowledge[childId] = child;

        _logger.LogInformation("Created child knowledge '{Name}' (ID: {ChildId}) extending {ParentId}", name, childId, parentId);

        return child;
    }

    /// <inheritdoc />
    public async Task<AnalysisTool> ExtendToolAsync(Guid parentId, string childName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childName, nameof(childName));

        _logger.LogInformation("Extending tool {ParentId} with child '{ChildName}'", parentId, childName);

        var parent = await GetToolAsync(parentId, cancellationToken);
        if (parent == null)
        {
            _logger.LogWarning("Parent tool {ParentId} not found for Extend", parentId);
            throw new KeyNotFoundException($"Parent tool with ID '{parentId}' not found.");
        }

        var name = EnsureCustomerPrefix(childName);
        var childId = Guid.NewGuid();

        var child = parent with
        {
            Id = childId,
            Name = name,
            OwnerType = ScopeOwnerType.Customer,
            IsImmutable = false,
            ParentScopeId = parentId,
            BasedOnId = null
        };

        _stubTools[childId] = child;

        _logger.LogInformation("Created child tool '{Name}' (ID: {ChildId}) extending {ParentId}", name, childId, parentId);

        return child;
    }

    #endregion

    #endregion
}
