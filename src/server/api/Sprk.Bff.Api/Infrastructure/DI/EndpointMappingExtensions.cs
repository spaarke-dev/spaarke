using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Admin;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Api.Office;
using Sprk.Bff.Api.Api.Workspace;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// Extension methods for mapping all API endpoint groups (post-Build phase).
/// Extracts health, domain, and fallback endpoint registrations from Program.cs.
/// </summary>
public static class EndpointMappingExtensions
{
    /// <summary>
    /// Maps all endpoint groups: health, debug, domain endpoints, and SPA fallback.
    /// </summary>
    public static void MapSpaarkeEndpoints(this WebApplication app)
    {
        MapHealthEndpoints(app);
        app.MapDebugEndpoints();
        MapDomainEndpoints(app);
        MapSpaFallback(app);
    }

    private static void MapHealthEndpoints(WebApplication app)
    {
        app.MapHealthChecks("/healthz").AllowAnonymous();
        app.MapGet("/healthz/dataverse", TestDataverseConnectionAsync);
        app.MapGet("/healthz/dataverse/crud", TestDataverseCrudOperationsAsync);

        app.MapGet("/healthz/dataverse/doc/{id}", async (string id, IDocumentDataverseService dataverseService, ILogger<Program> logger) =>
        {
            logger.LogInformation("[DEBUG-ENDPOINT] Testing document retrieval for {Id}", id);
            try
            {
                var doc = await dataverseService.GetDocumentAsync(id);
                if (doc == null)
                    return Results.Ok(new { status = "NOT_FOUND", documentId = id, message = "Document not found in Dataverse" });

                return Results.Ok(new
                {
                    status = "FOUND",
                    documentId = doc.Id,
                    name = doc.Name,
                    fileName = doc.FileName,
                    isEmailArchive = doc.IsEmailArchive,
                    parentDocumentId = doc.ParentDocumentId,
                    matterId = doc.MatterId,
                    projectId = doc.ProjectId,
                    invoiceId = doc.InvoiceId,
                    emailConversationIndex = doc.EmailConversationIndex
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DEBUG-ENDPOINT] Error retrieving document {Id}", id);
                return Results.Ok(new { status = "ERROR", documentId = id, error = ex.Message, innerError = ex.InnerException?.Message });
            }
        }).AllowAnonymous();

        app.MapGet("/ping", () => Results.Text("pong"))
            .AllowAnonymous()
            .WithTags("Health")
            .WithDescription("Lightweight health check for warm-up agents. Returns 'pong' without authentication.");

        app.MapGet("/status", () =>
        {
            return TypedResults.Json(new
            {
                service = "Sprk.Bff.Api",
                version = "1.0.1-debug",
                timestamp = DateTimeOffset.UtcNow,
                debugEndpoints = new[] { "/healthz/dataverse/doc/{id}" }
            });
        })
            .AllowAnonymous()
            .WithTags("Health")
            .WithDescription("Service status with metadata (no sensitive info).");
    }

    private static void MapDomainEndpoints(WebApplication app)
    {
        app.MapUserEndpoints();
        app.MapPermissionsEndpoints();
        app.MapNavMapEndpoints();
        app.MapDataverseDocumentsEndpoints();
        app.MapFileAccessEndpoints();
        app.MapDocumentsEndpoints();
        app.MapUploadEndpoints();
        app.MapOBOEndpoints();
        app.MapDocumentOperationsEndpoints();
        app.MapEmailEndpoints();
        app.MapOfficeEndpoints();
        app.MapFieldMappingEndpoints();
        app.MapEventEndpoints();
        app.MapScorecardCalculatorEndpoints();

        if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
            app.Configuration.GetValue<bool>("Analysis:Enabled", true))
        {
            app.MapAnalysisEndpoints();
            app.MapPlaybookEndpoints();
            app.MapAiPlaybookBuilderEndpoints();
            app.MapScopeEndpoints();
            app.MapNodeEndpoints();
            app.MapPlaybookRunEndpoints();
            app.MapModelEndpoints();
            app.MapHandlerEndpoints();
        }

        app.MapRagEndpoints();
        app.MapKnowledgeBaseEndpoints();
        app.MapChatEndpoints();

        if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
            app.Configuration.GetValue<bool>("Analysis:Enabled", true))
        {
            app.MapSemanticSearchEndpoints();
            app.MapRecordSearchEndpoints();
        }

        app.MapVisualizationEndpoints();
        app.MapResilienceEndpoints();

        if (app.Configuration.GetValue<bool>("DocumentIntelligence:RecordMatchingEnabled"))
        {
            app.MapRecordMatchEndpoints();
            app.MapRecordMatchingAdminEndpoints();
        }

        // Admin endpoints that depend on Analysis services (ReferenceIndexingService, BuilderScopeImporter)
        if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
            app.Configuration.GetValue<bool>("Analysis:Enabled", true))
        {
            app.MapAdminKnowledgeEndpoints();
            app.MapBuilderScopeAdminEndpoints();
        }

        app.MapWorkspaceEndpoints();
        app.MapWorkspaceAiEndpoints();
        app.MapWorkspaceMatterEndpoints();
        app.MapWorkspaceProjectEndpoints();
        app.MapWorkspaceFileEndpoints();

        app.MapFinanceEndpoints();
        app.MapFinanceRollupEndpoints();
        app.MapCommunicationEndpoints();

        // SPE Admin endpoints (/api/spe/*) — environments, configs, business units, containers, audit log, dashboard
        app.MapSpeAdminEndpoints();

        // SPE container item endpoints (/api/spe/containers/{id}/items, /upload, /content, /preview, /versions, /thumbnails, /sharing, /folders)
        // Registered separately because ContainerItemEndpoints maps absolute paths (not relative to the /api/spe group).
        // Inherits auth via RequireAuthorization() called inside MapContainerItemEndpoints. (SPE-017 through SPE-021)
        app.MapContainerItemEndpoints();

        // External access endpoints:
        //   /api/v1/external/*        — Power Pages portal users (portal JWT auth)
        //   /api/v1/external-access/* — Internal management (Azure AD auth)
        app.MapExternalAccessEndpoints();
    }

    private static void MapSpaFallback(WebApplication app)
    {
        app.MapFallback(context =>
        {
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/playbook-builder/", StringComparison.OrdinalIgnoreCase) &&
                !Path.HasExtension(path))
            {
                context.Request.Path = "/playbook-builder/index.html";
                return context.RequestServices.GetRequiredService<IWebHostEnvironment>()
                    .WebRootFileProvider
                    .GetFileInfo("playbook-builder/index.html")
                    .Exists
                    ? Results.File(
                        Path.Combine(context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath!, "playbook-builder/index.html"),
                        "text/html").ExecuteAsync(context)
                    : Results.NotFound().ExecuteAsync(context);
            }
            return Results.NotFound().ExecuteAsync(context);
        });
    }

    private static async Task<IResult> TestDataverseConnectionAsync(IDataverseHealthService dataverseService)
    {
        try
        {
            var isConnected = await dataverseService.TestConnectionAsync();
            if (isConnected)
                return TypedResults.Ok(new { status = "healthy", message = "Dataverse connection successful" });
            else
                return TypedResults.Problem(detail: "Dataverse connection test failed", statusCode: 503, title: "Service Unavailable");
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: 503, title: "Dataverse Connection Error");
        }
    }

    private static async Task<IResult> TestDataverseCrudOperationsAsync(IDataverseHealthService dataverseService)
    {
        try
        {
            var testPassed = await dataverseService.TestDocumentOperationsAsync();
            if (testPassed)
                return TypedResults.Ok(new { status = "healthy", message = "Dataverse CRUD operations successful" });
            else
                return TypedResults.Problem(detail: "Dataverse CRUD operations test failed", statusCode: 503, title: "Service Unavailable");
        }
        catch (Exception ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: 503, title: "Dataverse CRUD Test Error");
        }
    }
}
