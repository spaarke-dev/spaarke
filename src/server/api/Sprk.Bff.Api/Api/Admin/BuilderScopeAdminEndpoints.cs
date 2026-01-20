using Sprk.Bff.Api.Services.Ai.Builder;

namespace Sprk.Bff.Api.Api.Admin;

/// <summary>
/// Admin endpoints for Builder Scope management.
/// These endpoints allow administrators to import builder scope definitions into Dataverse.
/// </summary>
public static class BuilderScopeAdminEndpoints
{
    public static IEndpointRouteBuilder MapBuilderScopeAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/builder-scopes")
            .RequireAuthorization()
            .WithTags("Admin");

        // GET /api/admin/builder-scopes/status - Check builder scopes file status (no auth for diagnostics)
        app.MapGet("/api/admin/builder-scopes/status", GetScopeFilesStatus)
            .WithName("GetBuilderScopeFilesStatus")
            .WithSummary("Check builder scope files status")
            .WithDescription("Returns a count of builder scope JSON files available for import. Does not require authentication.")
            .WithTags("Admin")
            .Produces<BuilderScopeFilesStatus>(StatusCodes.Status200OK);

        // POST /api/admin/builder-scopes/import - Import scopes from JSON directory
        group.MapPost("/import", ImportFromDirectory)
            .WithName("ImportBuilderScopes")
            .WithSummary("Import builder scopes from JSON files")
            .WithDescription("Imports all builder scope JSON files from the default builder-scopes directory into Dataverse.")
            .Produces<BuilderScopeImportResult>(StatusCodes.Status200OK)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // POST /api/admin/builder-scopes/import-json - Import a single scope from JSON body
        group.MapPost("/import-json", ImportFromJson)
            .WithName("ImportBuilderScopeJson")
            .WithSummary("Import a single builder scope from JSON")
            .WithDescription("Imports a single builder scope definition from the request body into Dataverse.")
            .Produces<BuilderScopeImportResult>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Import all builder scopes from the default JSON directory.
    /// </summary>
    private static async Task<IResult> ImportFromDirectory(
        BuilderScopeImporter importer,
        IWebHostEnvironment env,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Default to builder-scopes directory in content root
        var jsonDirectory = Path.Combine(env.ContentRootPath, "builder-scopes");

        logger.LogInformation("Admin triggered builder scope import from {Directory}", jsonDirectory);

        try
        {
            var result = await importer.ImportFromDirectoryAsync(jsonDirectory, cancellationToken);

            if (result.HasErrors)
            {
                logger.LogWarning(
                    "Builder scope import completed with errors: {Imported} imported, {Errors} errors",
                    result.TotalImported, result.Errors.Count);
            }
            else
            {
                logger.LogInformation(
                    "Builder scope import completed: {Actions} actions, {Skills} skills, {Knowledge} knowledge, {Tools} tools",
                    result.ActionsImported, result.SkillsImported, result.KnowledgeImported, result.ToolsImported);
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Builder scope import failed");
            return Results.Problem(
                title: "Builder scope import failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Import a single builder scope from JSON in the request body.
    /// </summary>
    private static async Task<IResult> ImportFromJson(
        BuilderScopeImporter importer,
        ImportScopeJsonRequest request,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Json))
        {
            return Results.BadRequest("'json' field is required and cannot be empty");
        }

        logger.LogInformation("Admin triggered single builder scope import from JSON");

        try
        {
            var result = await importer.ImportFromJsonAsync(request.Json, cancellationToken);

            if (result.HasErrors)
            {
                logger.LogWarning("Builder scope import had errors: {Errors}", string.Join("; ", result.Errors));
            }
            else
            {
                logger.LogInformation("Builder scope imported successfully: {Count} total", result.TotalImported);
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Builder scope import failed");
            return Results.Problem(
                title: "Builder scope import failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Get status of builder scope files available for import.
    /// </summary>
    private static IResult GetScopeFilesStatus(IWebHostEnvironment env)
    {
        var jsonDirectory = Path.Combine(env.ContentRootPath, "builder-scopes");
        var exists = Directory.Exists(jsonDirectory);
        var fileCount = exists ? Directory.GetFiles(jsonDirectory, "*.json").Length : 0;
        var fileNames = exists
            ? Directory.GetFiles(jsonDirectory, "*.json").Select(Path.GetFileName).ToList()
            : new List<string?>();

        return Results.Ok(new BuilderScopeFilesStatus
        {
            DirectoryExists = exists,
            DirectoryPath = jsonDirectory,
            FileCount = fileCount,
            FileNames = fileNames!
        });
    }
}

/// <summary>
/// Status of builder scope files available for import.
/// </summary>
public class BuilderScopeFilesStatus
{
    public bool DirectoryExists { get; set; }
    public string DirectoryPath { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public List<string> FileNames { get; set; } = new();
}

/// <summary>
/// Request model for importing a single scope from JSON.
/// </summary>
public class ImportScopeJsonRequest
{
    /// <summary>
    /// The JSON string containing the builder scope definition.
    /// </summary>
    public string Json { get; set; } = string.Empty;
}
