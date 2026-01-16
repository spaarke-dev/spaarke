using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Model deployment endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides read-only access to AI model deployments for node configuration.
/// </summary>
public static class ModelEndpoints
{
    // Stub data for Phase 1 (will query Dataverse in production)
    private static readonly ModelDeploymentDto[] StubModelDeployments =
    [
        new ModelDeploymentDto
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Name = "GPT-4o (Default)",
            Provider = AiProvider.AzureOpenAI,
            Capability = AiCapability.Chat,
            ModelId = "gpt-4o",
            ContextWindow = 128000,
            Description = "Latest GPT-4 model with improved performance and cost efficiency",
            IsActive = true
        },
        new ModelDeploymentDto
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000002"),
            Name = "GPT-4o Mini",
            Provider = AiProvider.AzureOpenAI,
            Capability = AiCapability.Chat,
            ModelId = "gpt-4o-mini",
            ContextWindow = 128000,
            Description = "Smaller, faster GPT-4o variant for simpler tasks",
            IsActive = true
        },
        new ModelDeploymentDto
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000003"),
            Name = "GPT-4 Turbo",
            Provider = AiProvider.AzureOpenAI,
            Capability = AiCapability.Chat,
            ModelId = "gpt-4-turbo",
            ContextWindow = 128000,
            Description = "GPT-4 Turbo with vision capabilities",
            IsActive = true
        },
        new ModelDeploymentDto
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000004"),
            Name = "text-embedding-3-large",
            Provider = AiProvider.AzureOpenAI,
            Capability = AiCapability.Embedding,
            ModelId = "text-embedding-3-large",
            ContextWindow = 8191,
            Description = "Large embedding model for high-quality vector representations",
            IsActive = true
        },
        new ModelDeploymentDto
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000005"),
            Name = "Claude 3.5 Sonnet",
            Provider = AiProvider.Anthropic,
            Capability = AiCapability.Chat,
            ModelId = "claude-3-5-sonnet-20241022",
            ContextWindow = 200000,
            Description = "Anthropic's balanced model with excellent reasoning",
            IsActive = false // Not yet configured
        }
    ];

    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/model-deployments")
            .RequireAuthorization()
            .WithTags("AI Model Deployments");

        // GET /api/ai/model-deployments - List available model deployments
        group.MapGet("/", ListModelDeployments)
            .WithName("ListModelDeployments")
            .WithSummary("List available AI model deployments")
            .WithDescription("Returns a paginated list of AI model deployments that can be used for node configuration.")
            .Produces<ModelDeploymentListResult>()
            .ProducesProblem(401);

        // GET /api/ai/model-deployments/{id} - Get specific model deployment
        group.MapGet("/{id:guid}", GetModelDeployment)
            .WithName("GetModelDeployment")
            .WithSummary("Get a specific AI model deployment")
            .WithDescription("Returns details of a specific AI model deployment by ID.")
            .Produces<ModelDeploymentDto>()
            .ProducesProblem(401)
            .ProducesProblem(404);

        return app;
    }

    /// <summary>
    /// List available AI model deployments.
    /// </summary>
    private static Task<IResult> ListModelDeployments(
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        AiCapability? capability = null,
        AiProvider? provider = null,
        bool? activeOnly = true,
        string sortBy = "name",
        bool sortDescending = false)
    {
        var logger = loggerFactory.CreateLogger("ModelEndpoints");
        logger.LogDebug("Listing model deployments: Page={Page}, PageSize={PageSize}, Capability={Capability}",
            page, pageSize, capability);

        // Sanitize pagination parameters
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        try
        {
            var items = StubModelDeployments.AsEnumerable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                items = items.Where(m => m.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) ||
                                         m.ModelId.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (capability.HasValue)
            {
                items = items.Where(m => m.Capability == capability.Value);
            }

            if (provider.HasValue)
            {
                items = items.Where(m => m.Provider == provider.Value);
            }

            if (activeOnly == true)
            {
                items = items.Where(m => m.IsActive);
            }

            // Apply sorting
            items = sortBy.ToLowerInvariant() switch
            {
                "name" => sortDescending ? items.OrderByDescending(m => m.Name) : items.OrderBy(m => m.Name),
                "provider" => sortDescending ? items.OrderByDescending(m => m.Provider) : items.OrderBy(m => m.Provider),
                "capability" => sortDescending ? items.OrderByDescending(m => m.Capability) : items.OrderBy(m => m.Capability),
                "contextwindow" => sortDescending ? items.OrderByDescending(m => m.ContextWindow) : items.OrderBy(m => m.ContextWindow),
                _ => items.OrderBy(m => m.Name)
            };

            var totalCount = items.Count();
            var pagedItems = items
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();

            var result = new ModelDeploymentListResult
            {
                Items = pagedItems,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };

            logger.LogDebug("Listed {Count} model deployments (page {Page})", pagedItems.Length, page);
            return Task.FromResult(Results.Ok(result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list model deployments");
            return Task.FromResult(Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to list model deployments"));
        }
    }

    /// <summary>
    /// Get a specific model deployment by ID.
    /// </summary>
    private static Task<IResult> GetModelDeployment(
        Guid id,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ModelEndpoints");
        logger.LogDebug("Getting model deployment {Id}", id);

        try
        {
            var deployment = StubModelDeployments.FirstOrDefault(m => m.Id == id);

            if (deployment == null)
            {
                logger.LogWarning("Model deployment {Id} not found", id);
                return Task.FromResult(Results.Problem(
                    statusCode: 404,
                    title: "Not Found",
                    detail: $"Model deployment {id} not found"));
            }

            return Task.FromResult(Results.Ok(deployment));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get model deployment {Id}", id);
            return Task.FromResult(Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to get model deployment"));
        }
    }
}

/// <summary>
/// Paginated result for model deployment listings.
/// </summary>
public record ModelDeploymentListResult
{
    /// <summary>
    /// The model deployments in this page.
    /// </summary>
    public required ModelDeploymentDto[] Items { get; init; }

    /// <summary>
    /// Total count of matching deployments.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Page size.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>
    /// Whether there are more pages.
    /// </summary>
    public bool HasMore => Page < TotalPages;
}
