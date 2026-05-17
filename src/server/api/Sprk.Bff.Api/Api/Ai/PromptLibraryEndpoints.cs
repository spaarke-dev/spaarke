using System.Security.Claims;
using Sprk.Bff.Api.Services.Ai.PromptLibrary;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Minimal API endpoints for the Prompt Library feature (AIPU2-035).
///
/// Routes:
///   GET    /api/ai/prompts              — list templates visible to the caller
///   GET    /api/ai/prompts/{id}         — get single template
///   POST   /api/ai/prompts              — create Personal or Team template
///   PUT    /api/ai/prompts/{id}         — update Personal or Team template
///   DELETE /api/ai/prompts/{id}         — delete Personal or Team template
///   POST   /api/ai/prompts/{id}/render  — render template with variable substitution
///
/// All routes require authentication (ADR-008). Tenant isolation is enforced by extracting
/// the <c>tid</c> claim from the user's JWT. Org and System templates are returned read-only;
/// write attempts return HTTP 403.
/// </summary>
public static class PromptLibraryEndpoints
{
    /// <summary>
    /// Registers all prompt library endpoints on the provided route builder.
    /// Called from <c>EndpointMappingExtensions.MapDomainEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapPromptLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/prompts")
            .RequireAuthorization()
            .WithTags("AI Prompt Library");

        // GET /api/ai/prompts — list all templates visible to the authenticated user
        group.MapGet("/", ListAsync)
            .RequireRateLimiting("ai-batch")
            .WithName("ListPromptTemplates")
            .WithSummary("List prompt templates visible to the authenticated user")
            .WithDescription(
                "Returns Personal templates owned by the caller, Team templates for any teamIds " +
                "supplied via query parameter, and all Org/System templates (read-only).")
            .Produces<IReadOnlyList<PromptTemplate>>()
            .ProducesProblem(401);

        // GET /api/ai/prompts/{id} — get single template
        group.MapGet("/{id}", GetAsync)
            .RequireRateLimiting("ai-batch")
            .WithName("GetPromptTemplate")
            .WithSummary("Get a single prompt template by ID")
            .Produces<PromptTemplate>()
            .ProducesProblem(401)
            .ProducesProblem(404);

        // POST /api/ai/prompts — create a new Personal or Team template
        group.MapPost("/", CreateAsync)
            .RequireRateLimiting("ai-batch")
            .WithName("CreatePromptTemplate")
            .WithSummary("Create a new prompt template (Personal or Team tier)")
            .Produces<PromptTemplate>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403);

        // PUT /api/ai/prompts/{id} — update a Personal or Team template
        group.MapPut("/{id}", UpdateAsync)
            .RequireRateLimiting("ai-batch")
            .WithName("UpdatePromptTemplate")
            .WithSummary("Update a prompt template (Personal or Team tier only)")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // DELETE /api/ai/prompts/{id} — delete a Personal or Team template
        group.MapDelete("/{id}", DeleteAsync)
            .RequireRateLimiting("ai-batch")
            .WithName("DeletePromptTemplate")
            .WithSummary("Delete a prompt template (Personal or Team tier only)")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // POST /api/ai/prompts/{id}/render — render a template with variable substitution
        group.MapPost("/{id}/render", RenderAsync)
            .RequireRateLimiting("ai-batch")
            .WithName("RenderPromptTemplate")
            .WithSummary("Render a prompt template by substituting variables")
            .WithDescription(
                "Substitutes {{variableName}} placeholders in the template body with the supplied values. " +
                "Returns 400 if required variables are missing.")
            .Produces<RenderPromptResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404);

        return app;
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        IPromptLibraryService service,
        [Microsoft.AspNetCore.Mvc.FromQuery] string? teamIds = null,
        CancellationToken ct = default)
    {
        var (tenantId, userId) = ExtractClaims(httpContext);
        if (tenantId is null || userId is null)
            return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "Missing tenant or user identity.");

        var teamIdList = teamIds?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var templates = await service.ListAsync(tenantId, userId, teamIdList, ct);
        return Results.Ok(templates);
    }

    private static async Task<IResult> GetAsync(
        string id,
        HttpContext httpContext,
        IPromptLibraryService service,
        CancellationToken ct = default)
    {
        var (tenantId, _) = ExtractClaims(httpContext);
        if (tenantId is null)
            return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "Missing tenant identity.");

        var template = await service.GetAsync(tenantId, id, ct);
        return template is null
            ? Results.Problem(statusCode: 404, title: "Not Found", detail: $"Prompt template '{id}' not found.")
            : Results.Ok(template);
    }

    private static async Task<IResult> CreateAsync(
        CreatePromptRequest request,
        HttpContext httpContext,
        IPromptLibraryService service,
        CancellationToken ct = default)
    {
        var (tenantId, userId) = ExtractClaims(httpContext);
        if (tenantId is null || userId is null)
            return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "Missing tenant or user identity.");

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Body))
            return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Name and Body are required.");

        try
        {
            var created = await service.CreateAsync(tenantId, userId, request, ct);
            return Results.Created($"/api/ai/prompts/{created.Id}", created);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(statusCode: 403, title: "Forbidden", detail: ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(statusCode: 400, title: "Bad Request", detail: ex.Message);
        }
    }

    private static async Task<IResult> UpdateAsync(
        string id,
        UpdatePromptRequest request,
        HttpContext httpContext,
        IPromptLibraryService service,
        CancellationToken ct = default)
    {
        var (tenantId, _) = ExtractClaims(httpContext);
        if (tenantId is null)
            return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "Missing tenant identity.");

        try
        {
            await service.UpdateAsync(tenantId, id, request, ct);
            return Results.NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Problem(statusCode: 404, title: "Not Found", detail: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(statusCode: 403, title: "Forbidden", detail: ex.Message);
        }
    }

    private static async Task<IResult> DeleteAsync(
        string id,
        HttpContext httpContext,
        IPromptLibraryService service,
        CancellationToken ct = default)
    {
        var (tenantId, _) = ExtractClaims(httpContext);
        if (tenantId is null)
            return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "Missing tenant identity.");

        try
        {
            await service.DeleteAsync(tenantId, id, ct);
            return Results.NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Problem(statusCode: 404, title: "Not Found", detail: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(statusCode: 403, title: "Forbidden", detail: ex.Message);
        }
    }

    private static async Task<IResult> RenderAsync(
        string id,
        RenderPromptRequest request,
        HttpContext httpContext,
        IPromptLibraryService service,
        CancellationToken ct = default)
    {
        var (tenantId, _) = ExtractClaims(httpContext);
        if (tenantId is null)
            return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "Missing tenant identity.");

        try
        {
            var rendered = await service.RenderAsync(tenantId, id, request.Variables, ct);
            return Results.Ok(new RenderPromptResponse(rendered));
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Problem(statusCode: 404, title: "Not Found", detail: ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(statusCode: 400, title: "Bad Request", detail: ex.Message);
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Extracts tenantId (<c>tid</c> claim) and userId (<c>oid</c> claim) from the user's JWT.
    /// Returns (null, null) when either claim is absent.
    /// </summary>
    private static (string? tenantId, string? userId) ExtractClaims(HttpContext httpContext)
    {
        var user = httpContext.User;
        var tenantId = user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        var userId = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return (tenantId, userId);
    }
}

// =========================================================================
// Request / response DTOs (endpoint-local, not part of the service layer)
// =========================================================================

/// <summary>Request body for the render endpoint.</summary>
/// <param name="Variables">Map of placeholder name → resolved value.</param>
public record RenderPromptRequest(Dictionary<string, string> Variables);

/// <summary>Response from the render endpoint.</summary>
/// <param name="RenderedText">The template body with all placeholders substituted.</param>
public record RenderPromptResponse(string RenderedText);
