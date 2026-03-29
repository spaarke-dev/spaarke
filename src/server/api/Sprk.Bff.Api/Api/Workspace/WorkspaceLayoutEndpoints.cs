using System.Security.Claims;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Api.Workspace;

/// <summary>
/// API endpoints for workspace layout CRUD operations, section registry, and layout templates.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern (no controllers).
/// Follows ADR-008: Endpoint filters for authorization.
///
/// Routes are mapped under the /api/workspace route group shared with other workspace endpoints.
/// The sections and templates endpoints return static data from code constants — no Dataverse calls.
/// </remarks>
public static class WorkspaceLayoutEndpoints
{
    /// <summary>
    /// Registers workspace layout endpoints with the application.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkspaceLayoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspace")
            .RequireAuthorization()
            .WithTags("WorkspaceLayouts");

        // Layout CRUD endpoints
        group.MapGet("/layouts", GetLayouts)
            .WithName("GetWorkspaceLayouts")
            .WithSummary("Get all workspace layouts for the authenticated user")
            .WithDescription(
                "Returns all workspace layouts for the authenticated user, including system-provided " +
                "layouts (e.g., Corporate Workspace) and user-created custom layouts. System layouts " +
                "appear first, followed by user layouts sorted by sort order.")
            .Produces<IReadOnlyList<WorkspaceLayoutDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/layouts/default", GetDefaultLayout)
            .WithName("GetDefaultWorkspaceLayout")
            .WithSummary("Get the default workspace layout for the authenticated user")
            .WithDescription(
                "Returns the user's default workspace layout. If no user default is set, " +
                "falls back to the Corporate Workspace system layout.")
            .Produces<WorkspaceLayoutDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/layouts/{id:guid}", GetLayoutById)
            .WithName("GetWorkspaceLayoutById")
            .WithSummary("Get a specific workspace layout by ID")
            .WithDescription(
                "Returns a specific workspace layout by its ID. Checks system layouts first, " +
                "then queries Dataverse for user layouts. Returns 404 if the layout does not " +
                "exist or does not belong to the authenticated user.")
            .Produces<WorkspaceLayoutDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/layouts", CreateLayout)
            .WithName("CreateWorkspaceLayout")
            .WithSummary("Create a new workspace layout")
            .WithDescription(
                "Creates a new user workspace layout. Each user can have a maximum of 10 custom " +
                "layouts. If the new layout is marked as default, the previous default is cleared. " +
                "Returns 409 Conflict if the maximum layout count is exceeded.")
            .Produces<WorkspaceLayoutDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPut("/layouts/{id:guid}", UpdateLayout)
            .WithName("UpdateWorkspaceLayout")
            .WithSummary("Update an existing workspace layout")
            .WithDescription(
                "Updates an existing user workspace layout. System layouts cannot be modified " +
                "and will return 403 Forbidden. Returns 404 if the layout does not exist or " +
                "does not belong to the authenticated user.")
            .Produces<WorkspaceLayoutDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapDelete("/layouts/{id:guid}", DeleteLayout)
            .WithName("DeleteWorkspaceLayout")
            .WithSummary("Delete a workspace layout")
            .WithDescription(
                "Deletes a user workspace layout (soft delete via Dataverse deactivation). " +
                "System layouts cannot be deleted and will return 403 Forbidden. Returns 404 " +
                "if the layout does not exist or does not belong to the authenticated user.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Static data endpoints (no Dataverse calls)
        group.MapGet("/sections", GetSections)
            .WithName("GetWorkspaceSections")
            .WithSummary("Get all available workspace sections")
            .WithDescription(
                "Returns the static list of workspace sections available for placement in layouts. " +
                "Sections are grouped by category (core, ai, finance). This data is defined in code " +
                "constants and does not query Dataverse.")
            .Produces<IReadOnlyList<SectionDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/templates", GetTemplates)
            .WithName("GetWorkspaceTemplates")
            .WithSummary("Get all available layout templates")
            .WithDescription(
                "Returns the static list of layout templates that define grid structures for " +
                "workspace layouts. Templates specify row configurations and column definitions. " +
                "This data is defined in code constants and does not query Dataverse.")
            .Produces<IReadOnlyList<LayoutTemplateDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    // -------------------------------------------------------------------------
    // Layout CRUD Handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns all workspace layouts for the authenticated user.
    /// GET /api/workspace/layouts
    /// </summary>
    private static async Task<IResult> GetLayouts(
        WorkspaceLayoutService layoutService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null)
            return UnauthorizedProblem(httpContext);

        try
        {
            var layouts = await layoutService.GetLayoutsAsync(userId, ct);

            logger.LogInformation(
                "Returning {Count} layouts for user {UserId}. CorrelationId={CorrelationId}",
                layouts.Count, userId, httpContext.TraceIdentifier);

            return TypedResults.Ok(layouts);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to retrieve layouts. UserId={UserId}, CorrelationId={CorrelationId}",
                userId, httpContext.TraceIdentifier);

            return ServerErrorProblem(httpContext, "An error occurred while retrieving workspace layouts");
        }
    }

    /// <summary>
    /// Returns the user's default workspace layout.
    /// GET /api/workspace/layouts/default
    /// </summary>
    private static async Task<IResult> GetDefaultLayout(
        WorkspaceLayoutService layoutService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null)
            return UnauthorizedProblem(httpContext);

        try
        {
            var layout = await layoutService.GetDefaultLayoutAsync(userId, ct);

            logger.LogInformation(
                "Returning default layout {LayoutId} for user {UserId}. CorrelationId={CorrelationId}",
                layout.Id, userId, httpContext.TraceIdentifier);

            return TypedResults.Ok(layout);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to retrieve default layout. UserId={UserId}, CorrelationId={CorrelationId}",
                userId, httpContext.TraceIdentifier);

            return ServerErrorProblem(httpContext, "An error occurred while retrieving the default workspace layout");
        }
    }

    /// <summary>
    /// Returns a specific workspace layout by ID.
    /// GET /api/workspace/layouts/{id}
    /// </summary>
    private static async Task<IResult> GetLayoutById(
        Guid id,
        WorkspaceLayoutService layoutService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null)
            return UnauthorizedProblem(httpContext);

        try
        {
            var layout = await layoutService.GetLayoutByIdAsync(id, userId, ct);

            if (layout is null)
            {
                logger.LogInformation(
                    "Layout {LayoutId} not found for user {UserId}. CorrelationId={CorrelationId}",
                    id, userId, httpContext.TraceIdentifier);

                return Results.Problem(
                    detail: $"Workspace layout '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                    extensions: new Dictionary<string, object?>
                    {
                        ["correlationId"] = httpContext.TraceIdentifier
                    });
            }

            return TypedResults.Ok(layout);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to retrieve layout {LayoutId}. UserId={UserId}, CorrelationId={CorrelationId}",
                id, userId, httpContext.TraceIdentifier);

            return ServerErrorProblem(httpContext, $"An error occurred while retrieving workspace layout '{id}'");
        }
    }

    /// <summary>
    /// Creates a new workspace layout.
    /// POST /api/workspace/layouts
    /// </summary>
    private static async Task<IResult> CreateLayout(
        CreateWorkspaceLayoutRequest request,
        WorkspaceLayoutService layoutService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null)
            return UnauthorizedProblem(httpContext);

        try
        {
            var (layout, error) = await layoutService.CreateLayoutAsync(request, userId, ct);

            if (layout is null)
            {
                // Check if this is a max-layouts-exceeded error (409 Conflict)
                if (error?.Contains("Maximum", StringComparison.OrdinalIgnoreCase) == true)
                {
                    logger.LogWarning(
                        "Layout creation denied — max layouts exceeded. UserId={UserId}, CorrelationId={CorrelationId}",
                        userId, httpContext.TraceIdentifier);

                    return Results.Problem(
                        detail: error,
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Conflict",
                        type: "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                        extensions: new Dictionary<string, object?>
                        {
                            ["correlationId"] = httpContext.TraceIdentifier
                        });
                }

                return ServerErrorProblem(httpContext, error ?? "Failed to create workspace layout");
            }

            logger.LogInformation(
                "Created layout {LayoutId} for user {UserId}. CorrelationId={CorrelationId}",
                layout.Id, userId, httpContext.TraceIdentifier);

            return TypedResults.Created($"/api/workspace/layouts/{layout.Id}", layout);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to create layout. UserId={UserId}, CorrelationId={CorrelationId}",
                userId, httpContext.TraceIdentifier);

            return ServerErrorProblem(httpContext, "An error occurred while creating the workspace layout");
        }
    }

    /// <summary>
    /// Updates an existing workspace layout.
    /// PUT /api/workspace/layouts/{id}
    /// </summary>
    private static async Task<IResult> UpdateLayout(
        Guid id,
        UpdateWorkspaceLayoutRequest request,
        WorkspaceLayoutService layoutService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null)
            return UnauthorizedProblem(httpContext);

        try
        {
            var (layout, error) = await layoutService.UpdateLayoutAsync(id, request, userId, ct);

            if (layout is null)
            {
                // Determine the appropriate status code based on the error
                if (error?.Contains("System", StringComparison.OrdinalIgnoreCase) == true)
                {
                    logger.LogWarning(
                        "Update denied — system layout {LayoutId}. UserId={UserId}, CorrelationId={CorrelationId}",
                        id, userId, httpContext.TraceIdentifier);

                    return Results.Problem(
                        detail: error,
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Forbidden",
                        type: "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                        extensions: new Dictionary<string, object?>
                        {
                            ["correlationId"] = httpContext.TraceIdentifier
                        });
                }

                if (error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                {
                    logger.LogInformation(
                        "Layout {LayoutId} not found for update. UserId={UserId}, CorrelationId={CorrelationId}",
                        id, userId, httpContext.TraceIdentifier);

                    return Results.Problem(
                        detail: error,
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Not Found",
                        type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                        extensions: new Dictionary<string, object?>
                        {
                            ["correlationId"] = httpContext.TraceIdentifier
                        });
                }

                return ServerErrorProblem(httpContext, error ?? "Failed to update workspace layout");
            }

            logger.LogInformation(
                "Updated layout {LayoutId} for user {UserId}. CorrelationId={CorrelationId}",
                layout.Id, userId, httpContext.TraceIdentifier);

            return TypedResults.Ok(layout);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to update layout {LayoutId}. UserId={UserId}, CorrelationId={CorrelationId}",
                id, userId, httpContext.TraceIdentifier);

            return ServerErrorProblem(httpContext, $"An error occurred while updating workspace layout '{id}'");
        }
    }

    /// <summary>
    /// Deletes a workspace layout.
    /// DELETE /api/workspace/layouts/{id}
    /// </summary>
    private static async Task<IResult> DeleteLayout(
        Guid id,
        WorkspaceLayoutService layoutService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId is null)
            return UnauthorizedProblem(httpContext);

        try
        {
            var error = await layoutService.DeleteLayoutAsync(id, userId, ct);

            if (error is not null)
            {
                // Determine the appropriate status code based on the error
                if (error.Contains("System", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Delete denied — system layout {LayoutId}. UserId={UserId}, CorrelationId={CorrelationId}",
                        id, userId, httpContext.TraceIdentifier);

                    return Results.Problem(
                        detail: error,
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Forbidden",
                        type: "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                        extensions: new Dictionary<string, object?>
                        {
                            ["correlationId"] = httpContext.TraceIdentifier
                        });
                }

                if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation(
                        "Layout {LayoutId} not found for deletion. UserId={UserId}, CorrelationId={CorrelationId}",
                        id, userId, httpContext.TraceIdentifier);

                    return Results.Problem(
                        detail: error,
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Not Found",
                        type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                        extensions: new Dictionary<string, object?>
                        {
                            ["correlationId"] = httpContext.TraceIdentifier
                        });
                }

                return ServerErrorProblem(httpContext, error);
            }

            logger.LogInformation(
                "Deleted layout {LayoutId} for user {UserId}. CorrelationId={CorrelationId}",
                id, userId, httpContext.TraceIdentifier);

            return TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to delete layout {LayoutId}. UserId={UserId}, CorrelationId={CorrelationId}",
                id, userId, httpContext.TraceIdentifier);

            return ServerErrorProblem(httpContext, $"An error occurred while deleting workspace layout '{id}'");
        }
    }

    // -------------------------------------------------------------------------
    // Static Data Handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the static list of available workspace sections.
    /// GET /api/workspace/sections
    /// </summary>
    private static IResult GetSections()
    {
        return TypedResults.Ok(AvailableSections);
    }

    /// <summary>
    /// Returns the static list of available layout templates.
    /// GET /api/workspace/templates
    /// </summary>
    private static IResult GetTemplates()
    {
        return TypedResults.Ok(AvailableTemplates);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts the authenticated user's ID from the HTTP context.
    /// </summary>
    private static string? GetUserId(HttpContext httpContext)
        => httpContext.Items["UserId"]?.ToString()
           ?? httpContext.User.FindFirst("oid")?.Value
           ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Returns a 401 Unauthorized ProblemDetails response.
    /// </summary>
    private static IResult UnauthorizedProblem(HttpContext httpContext)
        => Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            detail: "User identity not found",
            type: "https://tools.ietf.org/html/rfc7235#section-3.1",
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = httpContext.TraceIdentifier
            });

    /// <summary>
    /// Returns a 500 Internal Server Error ProblemDetails response.
    /// </summary>
    private static IResult ServerErrorProblem(HttpContext httpContext, string detail)
        => Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = httpContext.TraceIdentifier
            });

    // -------------------------------------------------------------------------
    // Static Section Registry
    // -------------------------------------------------------------------------

    /// <summary>
    /// All workspace sections available for placement in layouts.
    /// Defined as code constants — no Dataverse queries needed.
    /// </summary>
    private static readonly IReadOnlyList<SectionDto> AvailableSections =
    [
        new SectionDto
        {
            Id = "get-started",
            Label = "Get Started",
            Description = "Welcome panel with onboarding guidance and quick links",
            Category = "core",
            IconName = "Rocket",
            DefaultHeight = "300px"
        },
        new SectionDto
        {
            Id = "quick-summary",
            Label = "Quick Summary",
            Description = "Portfolio health metrics, budget utilization, and AI briefing",
            Category = "core",
            IconName = "DataBarVertical",
            DefaultHeight = "300px"
        },
        new SectionDto
        {
            Id = "latest-updates",
            Label = "Latest Updates",
            Description = "Recent activity feed across matters, documents, and events",
            Category = "core",
            IconName = "News",
            DefaultHeight = "400px"
        },
        new SectionDto
        {
            Id = "todo",
            Label = "My To Do List",
            Description = "Upcoming events, deadlines, and AI-prioritized action items",
            Category = "core",
            IconName = "TaskListSquare",
            DefaultHeight = "400px"
        },
        new SectionDto
        {
            Id = "documents",
            Label = "My Documents",
            Description = "Recently accessed and starred documents across all matters",
            Category = "core",
            IconName = "DocumentMultiple",
            DefaultHeight = "400px"
        }
    ];

    // -------------------------------------------------------------------------
    // Static Layout Templates
    // -------------------------------------------------------------------------

    /// <summary>
    /// All layout templates available for workspace configuration.
    /// Templates define the grid structure (rows and columns) for section placement.
    /// Defined as code constants — no Dataverse queries needed.
    /// </summary>
    private static readonly IReadOnlyList<LayoutTemplateDto> AvailableTemplates =
    [
        new LayoutTemplateDto
        {
            Id = "1-column",
            Name = "Single Column",
            Description = "Full-width single column layout — ideal for focused workflows",
            Rows =
            [
                new LayoutTemplateRowDto { Id = "row-1", Columns = "1fr", ColumnsSmall = "1fr", SlotCount = 1 },
                new LayoutTemplateRowDto { Id = "row-2", Columns = "1fr", ColumnsSmall = "1fr", SlotCount = 1 },
                new LayoutTemplateRowDto { Id = "row-3", Columns = "1fr", ColumnsSmall = "1fr", SlotCount = 1 }
            ]
        },
        new LayoutTemplateDto
        {
            Id = "2-column",
            Name = "Two Columns",
            Description = "Equal two-column layout — balanced side-by-side sections",
            Rows =
            [
                new LayoutTemplateRowDto { Id = "row-1", Columns = "1fr 1fr", ColumnsSmall = "1fr", SlotCount = 2 },
                new LayoutTemplateRowDto { Id = "row-2", Columns = "1fr 1fr", ColumnsSmall = "1fr", SlotCount = 2 }
            ]
        },
        new LayoutTemplateDto
        {
            Id = "3-column",
            Name = "Three Columns",
            Description = "Equal three-column layout — maximum information density",
            Rows =
            [
                new LayoutTemplateRowDto { Id = "row-1", Columns = "1fr 1fr 1fr", ColumnsSmall = "1fr", SlotCount = 3 },
                new LayoutTemplateRowDto { Id = "row-2", Columns = "1fr 1fr 1fr", ColumnsSmall = "1fr", SlotCount = 3 }
            ]
        },
        new LayoutTemplateDto
        {
            Id = "3-row-mixed",
            Name = "Mixed Layout",
            Description = "Three rows with mixed widths — two-column top and bottom, full-width middle. Default for Corporate Workspace.",
            Rows =
            [
                new LayoutTemplateRowDto { Id = "row-1", Columns = "1fr 1fr", ColumnsSmall = "1fr", SlotCount = 2 },
                new LayoutTemplateRowDto { Id = "row-2", Columns = "1fr", ColumnsSmall = "1fr", SlotCount = 1 },
                new LayoutTemplateRowDto { Id = "row-3", Columns = "1fr 1fr", ColumnsSmall = "1fr", SlotCount = 2 }
            ]
        }
    ];
}
