using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// CRUD endpoints for SPE environment configurations.
///
/// Routes (all under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   GET    /api/spe/environments                — List all environments
///   GET    /api/spe/environments/{id}           — Get single environment
///   POST   /api/spe/environments                — Create environment
///   PUT    /api/spe/environments/{id}           — Update environment (isDefault uniqueness enforced)
///   DELETE /api/spe/environments/{id}           — Delete environment (blocked if referenced by configs)
///
/// SPE environments represent tenant-level SharePoint Embedded configurations.
/// Container type configs reference environments via a lookup — attempting to delete
/// a referenced environment returns 409 Conflict.
///
/// All mutation operations (POST, PUT, DELETE) are logged to the sprk_speauditlog
/// table via <see cref="SpeAuditService"/>. Audit failures are non-fatal.
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — MapGet/MapPost/etc. on RouteGroupBuilder, no controllers.
/// ADR-008: Authorization inherited from the /api/spe route group (SpeAdminAuthorizationFilter).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// </remarks>
public static class EnvironmentEndpoints
{
    private const string EntitySet = "sprk_speenvironments";
    private const string ConfigEntitySet = "sprk_specontainertypeconfigs";
    private const string SelectFields =
        "sprk_speenvironmentid,sprk_name,sprk_tenantid,sprk_tenantname,sprk_rootsiteurl,sprk_graphendpoint,sprk_isdefault,statecode,createdon,modifiedon";

    /// <summary>
    /// Registers the environment CRUD endpoints on the /api/spe route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/>.
    /// </summary>
    /// <param name="group">The /api/spe route group (auth already applied).</param>
    /// <returns>The route group for chaining.</returns>
    public static RouteGroupBuilder MapEnvironmentEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/spe/environments
        group.MapGet("/environments", ListEnvironmentsAsync)
            .WithName("SpeListEnvironments")
            .WithSummary("List all SPE environment configurations")
            .WithDescription(
                "Returns all sprk_speenvironment records. " +
                "Environments represent tenant-level SharePoint Embedded configurations " +
                "that container type configs reference.")
            .Produces<IReadOnlyList<EnvironmentSummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/environments/{id}
        group.MapGet("/environments/{id:guid}", GetEnvironmentAsync)
            .WithName("SpeGetEnvironment")
            .WithSummary("Get a single SPE environment by ID")
            .WithDescription(
                "Returns the full detail of a single sprk_speenvironment record.")
            .Produces<EnvironmentDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/environments
        group.MapPost("/environments", CreateEnvironmentAsync)
            .WithName("SpeCreateEnvironment")
            .WithSummary("Create a new SPE environment configuration")
            .WithDescription(
                "Creates a new sprk_speenvironment record. " +
                "If isDefault is true, the existing default environment (if any) will be cleared first. " +
                "The operation is logged to the audit log.")
            .Produces<EnvironmentDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PUT /api/spe/environments/{id}
        group.MapPut("/environments/{id:guid}", UpdateEnvironmentAsync)
            .WithName("SpeUpdateEnvironment")
            .WithSummary("Update an existing SPE environment configuration")
            .WithDescription(
                "Updates a sprk_speenvironment record. " +
                "If isDefault is set to true, the existing default environment (if any) will be cleared first. " +
                "The operation is logged to the audit log.")
            .Produces<EnvironmentDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // DELETE /api/spe/environments/{id}
        group.MapDelete("/environments/{id:guid}", DeleteEnvironmentAsync)
            .WithName("SpeDeleteEnvironment")
            .WithSummary("Delete an SPE environment configuration")
            .WithDescription(
                "Deletes a sprk_speenvironment record. " +
                "Returns 409 Conflict if any active container type configs reference this environment. " +
                "The operation is logged to the audit log.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    /// <summary>
    /// GET /api/spe/environments — Returns all environment records.
    /// </summary>
    private static async Task<IResult> ListEnvironmentsAsync(
        DataverseWebApiClient dataverseClient,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            var rows = await dataverseClient.QueryAsync<EnvironmentDataverseRow>(
                EntitySet,
                select: SelectFields,
                cancellationToken: ct);

            var items = rows.Select(r => r.ToSummary()).ToList();

            return TypedResults.Ok(items);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list SPE environments");
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while listing SPE environments.",
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// GET /api/spe/environments/{id} — Returns a single environment record or 404.
    /// </summary>
    private static async Task<IResult> GetEnvironmentAsync(
        Guid id,
        DataverseWebApiClient dataverseClient,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            var row = await dataverseClient.RetrieveAsync<EnvironmentDataverseRow>(
                EntitySet,
                id,
                select: SelectFields,
                cancellationToken: ct);

            if (row is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"SPE environment '{id}' was not found.");
            }

            return TypedResults.Ok(row.ToDetail());
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"SPE environment '{id}' was not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve SPE environment {Id}", id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while retrieving the SPE environment.",
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// POST /api/spe/environments — Creates a new environment record.
    /// </summary>
    private static async Task<IResult> CreateEnvironmentAsync(
        CreateEnvironmentRequest request,
        DataverseWebApiClient dataverseClient,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // ── Validation ──────────────────────────────────────────────────────
        var validationErrors = ValidateCreateRequest(request);
        if (validationErrors.Count > 0)
        {
            return ProblemDetailsHelper.ValidationProblem(validationErrors);
        }

        try
        {
            // ── Enforce isDefault uniqueness ─────────────────────────────────
            if (request.IsDefault)
            {
                await ClearExistingDefaultAsync(dataverseClient, environmentIdToExclude: null, ct);
            }

            // ── Create the record ────────────────────────────────────────────
            var payload = BuildCreatePayload(request);
            var newId = await dataverseClient.CreateAsync(EntitySet, payload, ct);

            // ── Audit ────────────────────────────────────────────────────────
            await auditService.LogOperationAsync(
                operation: "CreateEnvironment",
                category: "Configuration",
                targetResource: newId.ToString(),
                responseStatus: StatusCodes.Status201Created,
                environmentId: newId,
                cancellationToken: ct);

            // ── Return detail ────────────────────────────────────────────────
            var created = await dataverseClient.RetrieveAsync<EnvironmentDataverseRow>(
                EntitySet, newId, select: SelectFields, cancellationToken: ct);

            if (created is null)
            {
                // Fallback: return a DTO built from the request if retrieve fails
                return Results.Created(
                    $"/api/spe/environments/{newId}",
                    new EnvironmentDetailDto
                    {
                        Id = newId,
                        Name = request.Name,
                        TenantId = request.TenantId,
                        TenantName = request.TenantName,
                        RootSiteUrl = request.RootSiteUrl,
                        GraphEndpoint = request.GraphEndpoint,
                        Description = request.Description,
                        IsDefault = request.IsDefault,
                        Status = request.Status
                    });
            }

            return Results.Created($"/api/spe/environments/{newId}", created.ToDetail());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create SPE environment '{Name}'", request.Name);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while creating the SPE environment.",
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// PUT /api/spe/environments/{id} — Updates an existing environment record.
    /// </summary>
    private static async Task<IResult> UpdateEnvironmentAsync(
        Guid id,
        UpdateEnvironmentRequest request,
        DataverseWebApiClient dataverseClient,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // ── Validation ──────────────────────────────────────────────────────
        var validationErrors = ValidateUpdateRequest(request);
        if (validationErrors.Count > 0)
        {
            return ProblemDetailsHelper.ValidationProblem(validationErrors);
        }

        try
        {
            // ── Verify the record exists ─────────────────────────────────────
            var existing = await dataverseClient.RetrieveAsync<EnvironmentDataverseRow>(
                EntitySet, id, select: SelectFields, cancellationToken: ct);

            if (existing is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"SPE environment '{id}' was not found.");
            }

            // ── Enforce isDefault uniqueness ─────────────────────────────────
            if (request.IsDefault == true && !existing.IsDefault)
            {
                await ClearExistingDefaultAsync(dataverseClient, environmentIdToExclude: id, ct);
            }

            // ── Patch the record ─────────────────────────────────────────────
            var payload = BuildUpdatePayload(request);
            await dataverseClient.UpdateAsync(EntitySet, id, payload, ct);

            // ── Audit ────────────────────────────────────────────────────────
            await auditService.LogOperationAsync(
                operation: "UpdateEnvironment",
                category: "Configuration",
                targetResource: id.ToString(),
                responseStatus: StatusCodes.Status200OK,
                environmentId: id,
                cancellationToken: ct);

            // ── Return updated detail ────────────────────────────────────────
            var updated = await dataverseClient.RetrieveAsync<EnvironmentDataverseRow>(
                EntitySet, id, select: SelectFields, cancellationToken: ct);

            return TypedResults.Ok(updated?.ToDetail() ?? existing.ToDetail());
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"SPE environment '{id}' was not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update SPE environment {Id}", id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while updating the SPE environment.",
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// DELETE /api/spe/environments/{id} — Deletes an environment record.
    /// Returns 409 Conflict if any container type configs reference this environment.
    /// </summary>
    private static async Task<IResult> DeleteEnvironmentAsync(
        Guid id,
        DataverseWebApiClient dataverseClient,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            // ── Verify the record exists ─────────────────────────────────────
            var existing = await dataverseClient.RetrieveAsync<EnvironmentDataverseRow>(
                EntitySet, id, select: "sprk_speenvironmentid,sprk_name", cancellationToken: ct);

            if (existing is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"SPE environment '{id}' was not found.");
            }

            // ── Check for referencing container type configs ──────────────────
            // A config references an environment via the sprk_EnvironmentId lookup.
            // The OData filter uses the _sprk_environmentid_value expansion field.
            var referencingConfigs = await dataverseClient.QueryAsync<ReferencingConfigRow>(
                ConfigEntitySet,
                filter: $"_sprk_environment_value eq {id}",
                select: "sprk_specontainertypeconfigid",
                top: 1,
                cancellationToken: ct);

            if (referencingConfigs.Count > 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: $"Cannot delete environment '{existing.Name}' because it is referenced by one or more container type configs. " +
                            "Reassign or delete those configs before deleting this environment.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "spe.environment.referenced_by_configs",
                        ["environmentId"] = id
                    });
            }

            // ── Delete the record ────────────────────────────────────────────
            await dataverseClient.DeleteAsync(EntitySet, id, ct);

            // ── Audit ────────────────────────────────────────────────────────
            await auditService.LogOperationAsync(
                operation: "DeleteEnvironment",
                category: "Configuration",
                targetResource: id.ToString(),
                responseStatus: StatusCodes.Status204NoContent,
                environmentId: id,
                cancellationToken: ct);

            return Results.NoContent();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"SPE environment '{id}' was not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete SPE environment {Id}", id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while deleting the SPE environment.",
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // Validation helpers
    // =========================================================================

    private static Dictionary<string, string[]> ValidateCreateRequest(CreateEnvironmentRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.RootSiteUrl))
        {
            errors["rootSiteUrl"] = ["rootSiteUrl is required."];
        }
        else if (!IsValidHttpsUrl(request.RootSiteUrl))
        {
            errors["rootSiteUrl"] = ["rootSiteUrl must be a valid HTTPS URL."];
        }

        if (request.GraphEndpoint is not null && !IsValidHttpsUrl(request.GraphEndpoint))
        {
            errors["graphEndpoint"] = ["graphEndpoint must be a valid HTTPS URL when provided."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdateRequest(UpdateEnvironmentRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.RootSiteUrl is not null && !IsValidHttpsUrl(request.RootSiteUrl))
        {
            errors["rootSiteUrl"] = ["rootSiteUrl must be a valid HTTPS URL."];
        }

        if (request.GraphEndpoint is not null && !IsValidHttpsUrl(request.GraphEndpoint))
        {
            errors["graphEndpoint"] = ["graphEndpoint must be a valid HTTPS URL when provided."];
        }

        return errors;
    }

    private static bool IsValidHttpsUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps;
    }

    // =========================================================================
    // isDefault uniqueness enforcement
    // =========================================================================

    /// <summary>
    /// Clears the sprk_isdefault flag on any existing default environment.
    /// Called before setting a new default to ensure uniqueness.
    /// </summary>
    /// <param name="dataverseClient">Dataverse REST client.</param>
    /// <param name="environmentIdToExclude">
    /// The ID of the environment being updated (excluded from the query so it isn't cleared).
    /// Pass null when creating a new environment.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    private static async Task ClearExistingDefaultAsync(
        DataverseWebApiClient dataverseClient,
        Guid? environmentIdToExclude,
        CancellationToken ct)
    {
        // Build filter: find existing defaults, optionally excluding the record being updated.
        var filter = environmentIdToExclude.HasValue
            ? $"sprk_isdefault eq true and sprk_speenvironmentid ne {environmentIdToExclude.Value}"
            : "sprk_isdefault eq true";

        var existingDefaults = await dataverseClient.QueryAsync<EnvironmentDataverseRow>(
            EntitySet,
            filter: filter,
            select: "sprk_speenvironmentid",
            cancellationToken: ct);

        foreach (var existing in existingDefaults)
        {
            await dataverseClient.UpdateAsync(
                EntitySet,
                existing.Id,
                new { sprk_isdefault = false },
                ct);
        }
    }

    // =========================================================================
    // Payload builders
    // =========================================================================

    private static object BuildCreatePayload(CreateEnvironmentRequest request) =>
        new
        {
            sprk_name = request.Name,
            sprk_tenantid = request.TenantId,
            sprk_tenantname = request.TenantName,
            sprk_rootsiteurl = request.RootSiteUrl,
            sprk_graphendpoint = request.GraphEndpoint,
            sprk_isdefault = request.IsDefault
        };

    private static object BuildUpdatePayload(UpdateEnvironmentRequest request)
    {
        // Only include fields that were provided. Use an ExpandoObject-style approach
        // via a Dictionary so null fields are omitted from the PATCH body.
        var dict = new Dictionary<string, object?>();

        if (request.Name is not null)
            dict["sprk_name"] = request.Name;

        if (request.TenantId is not null)
            dict["sprk_tenantid"] = request.TenantId;

        if (request.TenantName is not null)
            dict["sprk_tenantname"] = request.TenantName;

        if (request.RootSiteUrl is not null)
            dict["sprk_rootsiteurl"] = request.RootSiteUrl;

        if (request.GraphEndpoint is not null)
            dict["sprk_graphendpoint"] = request.GraphEndpoint;

        if (request.IsDefault.HasValue)
            dict["sprk_isdefault"] = request.IsDefault.Value;

        return dict;
    }

    // =========================================================================
    // Internal Dataverse row types (private to this class)
    // =========================================================================

    /// <summary>
    /// Minimal projection for checking referencing container type configs.
    /// Only the primary key is needed for the existence check.
    /// </summary>
    private sealed class ReferencingConfigRow
    {
        [JsonPropertyName("sprk_specontainertypeconfigid")]
        public Guid Id { get; set; }
    }
}
