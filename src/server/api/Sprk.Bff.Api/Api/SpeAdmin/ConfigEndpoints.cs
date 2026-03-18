using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// CRUD endpoints for SPE Container Type Config records (/api/spe/configs).
///
/// Endpoints:
///   GET    /api/spe/configs          — list configs, optionally filtered by BU and/or environment
///   GET    /api/spe/configs/{id}     — single config detail
///   POST   /api/spe/configs          — create new config (audit logged)
///   PUT    /api/spe/configs/{id}     — update config (audit logged)
///   DELETE /api/spe/configs/{id}     — delete config (audit logged)
///
/// Authorization: Inherited from /api/spe route group (SpeAdminAuthorizationFilter — System Admin only).
/// Follows ADR-001: Minimal API; ADR-019: ProblemDetails for all errors.
/// </summary>
public static class ConfigEndpoints
{
    private const string EntitySet = "sprk_specontainertypeconfigs";
    private const string AuditCategory = "Configuration";

    // Azure Key Vault secret name rules:
    //   • Alphanumeric characters and hyphens only
    //   • 1–127 characters
    //   • See: https://learn.microsoft.com/azure/key-vault/general/about-keys-secrets-certificates
    private static readonly Regex KeyVaultSecretNameRegex =
        new(@"^[a-zA-Z0-9-]{1,127}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    // OData $select for list queries — summary fields only (performance)
    private const string ListSelect =
        "sprk_specontainertypeconfigid,sprk_name,sprk_containertypeid,sprk_containertypename," +
        "sprk_billingclassification,sprk_owningappid,sprk_isregistered,statecode,createdon,modifiedon," +
        "_sprk_businessunit_value,_sprk_environment_value";

    // OData $select for detail queries — all fields
    private const string DetailSelect =
        "sprk_specontainertypeconfigid,sprk_name,sprk_containertypeid,sprk_containertypename," +
        "sprk_billingclassification,sprk_owningappid,sprk_keyvaultsecretname," +
        "sprk_consumingappid,sprk_consumingappkvsecret," +
        "sprk_delegatedpermission,sprk_applicationpermissions," +
        "sprk_isregistered,sprk_registeredon,sprk_defaultcontainerid," +
        "sprk_maxstorageperbytes,sprk_sharingcapability," +
        "sprk_itemversioningenabled,sprk_itemmajorversionlimit," +
        "sprk_notes,statecode,createdon,modifiedon," +
        "_sprk_businessunit_value,_sprk_environment_value";

    /// <summary>
    /// Registers all /api/spe/configs endpoints onto the provided route group.
    /// Call from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> to inherit
    /// the parent group's RequireAuthorization() and SpeAdminAuthorizationFilter.
    /// </summary>
    public static RouteGroupBuilder MapConfigEndpoints(this RouteGroupBuilder group)
    {
        var configs = group.MapGroup("/configs")
            .WithTags("SpeAdmin - Configs");

        configs.MapGet("/", ListConfigsAsync)
            .WithName("ListSpeConfigs")
            .WithSummary("List container type configs, optionally filtered by business unit and environment")
            .Produces<IReadOnlyList<ConfigSummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        configs.MapGet("/{id:guid}", GetConfigAsync)
            .WithName("GetSpeConfig")
            .WithSummary("Get a single container type config by ID")
            .Produces<ConfigDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        configs.MapPost("/", CreateConfigAsync)
            .WithName("CreateSpeConfig")
            .WithSummary("Create a new container type config")
            .Produces<ConfigDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        configs.MapPut("/{id:guid}", UpdateConfigAsync)
            .WithName("UpdateSpeConfig")
            .WithSummary("Update an existing container type config")
            .Produces<ConfigDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        configs.MapDelete("/{id:guid}", DeleteConfigAsync)
            .WithName("DeleteSpeConfig")
            .WithSummary("Delete a container type config")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/spe/configs
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListConfigsAsync(
        [FromQuery] Guid? businessUnitId,
        [FromQuery] Guid? environmentId,
        DataverseWebApiClient dataverseClient,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            // Build OData $filter from optional query parameters
            var filters = new List<string>();

            if (businessUnitId.HasValue)
                filters.Add($"_sprk_businessunit_value eq {businessUnitId.Value}");

            if (environmentId.HasValue)
                filters.Add($"_sprk_environment_value eq {environmentId.Value}");

            var filter = filters.Count > 0 ? string.Join(" and ", filters) : null;

            var rows = await dataverseClient.QueryAsync<ConfigDataverseRow>(
                EntitySet,
                filter: filter,
                select: ListSelect,
                cancellationToken: ct);

            var items = rows.Select(r => r.ToSummary()).ToList();

            logger.LogInformation(
                "ListSpeConfigs: returned {Count} configs. businessUnitId={BuId} environmentId={EnvId} correlationId={CorrelationId}",
                items.Count, businessUnitId, environmentId, context.TraceIdentifier);

            return TypedResults.Ok(items);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ListSpeConfigs failed. correlationId={CorrelationId}",
                context.TraceIdentifier);

            return TypedResults.Problem(
                detail: "Failed to retrieve container type configs.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/spe/configs/{id}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetConfigAsync(
        Guid id,
        DataverseWebApiClient dataverseClient,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            var row = await dataverseClient.RetrieveAsync<ConfigDataverseRow>(
                EntitySet,
                id,
                select: DetailSelect,
                cancellationToken: ct);

            if (row == null)
            {
                logger.LogInformation(
                    "GetSpeConfig: not found. id={Id} correlationId={CorrelationId}",
                    id, context.TraceIdentifier);

                return TypedResults.Problem(
                    detail: $"Container type config '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "GetSpeConfig: retrieved config {Id} correlationId={CorrelationId}",
                id, context.TraceIdentifier);

            return TypedResults.Ok(row.ToDetail());
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return TypedResults.Problem(
                detail: $"Container type config '{id}' was not found.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "GetSpeConfig failed. id={Id} correlationId={CorrelationId}",
                id, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: "Failed to retrieve the container type config.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/spe/configs
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> CreateConfigAsync(
        CreateConfigRequest request,
        DataverseWebApiClient dataverseClient,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("'name' is required.", context.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(request.OwningAppId))
        {
            return ValidationProblem("'owningAppId' is required.", context.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(request.ContainerTypeId))
        {
            return ValidationProblem("'containerTypeId' is required.", context.TraceIdentifier);
        }

        if (string.IsNullOrWhiteSpace(request.KeyVaultSecretName))
        {
            return ValidationProblem("'keyVaultSecretName' is required.", context.TraceIdentifier);
        }

        // Validate Key Vault secret name format
        var kvValidation = ValidateKeyVaultSecretName(request.KeyVaultSecretName);
        if (kvValidation != null)
        {
            return ValidationProblem(kvValidation, context.TraceIdentifier);
        }

        try
        {
            var payload = BuildCreatePayload(request);
            var newId = await dataverseClient.CreateAsync(EntitySet, payload, ct);

            logger.LogInformation(
                "CreateSpeConfig: created config {Id} name={Name} correlationId={CorrelationId}",
                newId, request.Name, context.TraceIdentifier);

            // Audit log — fire-and-forget (audit failures must not block the response)
            _ = auditService.LogOperationAsync(
                operation: "CreateContainerTypeConfig",
                category: AuditCategory,
                targetResource: newId.ToString(),
                responseStatus: StatusCodes.Status201Created,
                configId: newId,
                environmentId: request.EnvironmentId,
                businessUnitId: request.BusinessUnitId,
                cancellationToken: CancellationToken.None);

            // Retrieve the newly created record to return the full detail DTO
            var created = await dataverseClient.RetrieveAsync<ConfigDataverseRow>(
                EntitySet, newId, select: DetailSelect, cancellationToken: ct);

            if (created == null)
            {
                // Fallback: return minimal response if retrieve fails
                return TypedResults.Created(
                    $"/api/spe/configs/{newId}",
                    new ConfigDetailDto { Id = newId, Name = request.Name });
            }

            return TypedResults.Created($"/api/spe/configs/{newId}", created.ToDetail());
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "CreateSpeConfig failed. name={Name} correlationId={CorrelationId}",
                request.Name, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: "Failed to create the container type config.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/spe/configs/{id}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> UpdateConfigAsync(
        Guid id,
        UpdateConfigRequest request,
        DataverseWebApiClient dataverseClient,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate Key Vault secret name if provided
        if (!string.IsNullOrEmpty(request.KeyVaultSecretName))
        {
            var kvValidation = ValidateKeyVaultSecretName(request.KeyVaultSecretName);
            if (kvValidation != null)
            {
                return ValidationProblem(kvValidation, context.TraceIdentifier);
            }
        }

        try
        {
            // Verify the record exists before attempting update
            ConfigDataverseRow? existing;
            try
            {
                existing = await dataverseClient.RetrieveAsync<ConfigDataverseRow>(
                    EntitySet, id, select: DetailSelect, cancellationToken: ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                existing = null;
            }

            if (existing == null)
            {
                return TypedResults.Problem(
                    detail: $"Container type config '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
            }

            var payload = BuildUpdatePayload(request);
            await dataverseClient.UpdateAsync(EntitySet, id, payload, ct);

            logger.LogInformation(
                "UpdateSpeConfig: updated config {Id} correlationId={CorrelationId}",
                id, context.TraceIdentifier);

            // Audit log
            _ = auditService.LogOperationAsync(
                operation: "UpdateContainerTypeConfig",
                category: AuditCategory,
                targetResource: id.ToString(),
                responseStatus: StatusCodes.Status200OK,
                configId: id,
                environmentId: request.EnvironmentId ?? existing.EnvironmentId,
                businessUnitId: request.BusinessUnitId ?? existing.BusinessUnitId,
                cancellationToken: CancellationToken.None);

            // Return the updated record
            var updated = await dataverseClient.RetrieveAsync<ConfigDataverseRow>(
                EntitySet, id, select: DetailSelect, cancellationToken: ct);

            return TypedResults.Ok(updated?.ToDetail() ?? existing.ToDetail());
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "UpdateSpeConfig failed. id={Id} correlationId={CorrelationId}",
                id, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: "Failed to update the container type config.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/spe/configs/{id}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> DeleteConfigAsync(
        Guid id,
        DataverseWebApiClient dataverseClient,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            // Retrieve so we can capture BU/env for audit log before deletion
            ConfigDataverseRow? existing;
            try
            {
                existing = await dataverseClient.RetrieveAsync<ConfigDataverseRow>(
                    EntitySet, id,
                    select: "sprk_specontainertypeconfigid,_sprk_businessunit_value,_sprk_environment_value",
                    cancellationToken: ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                existing = null;
            }

            if (existing == null)
            {
                return TypedResults.Problem(
                    detail: $"Container type config '{id}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
            }

            await dataverseClient.DeleteAsync(EntitySet, id, ct);

            logger.LogInformation(
                "DeleteSpeConfig: deleted config {Id} correlationId={CorrelationId}",
                id, context.TraceIdentifier);

            // Audit log
            _ = auditService.LogOperationAsync(
                operation: "DeleteContainerTypeConfig",
                category: AuditCategory,
                targetResource: id.ToString(),
                responseStatus: StatusCodes.Status204NoContent,
                configId: id,
                environmentId: existing.EnvironmentId,
                businessUnitId: existing.BusinessUnitId,
                cancellationToken: CancellationToken.None);

            return TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DeleteSpeConfig failed. id={Id} correlationId={CorrelationId}",
                id, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: "Failed to delete the container type config.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 7: Key Vault secret name validation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates an Azure Key Vault secret name.
    /// Returns null if valid; returns an error message string if invalid.
    ///
    /// Azure Key Vault secret name rules:
    ///   - Alphanumeric characters (a-z, A-Z, 0-9) and hyphens (-) only
    ///   - Must be 1–127 characters long
    ///   - See: https://learn.microsoft.com/azure/key-vault/general/about-keys-secrets-certificates
    /// </summary>
    private static string? ValidateKeyVaultSecretName(string secretName)
    {
        if (string.IsNullOrEmpty(secretName))
        {
            return "'keyVaultSecretName' cannot be empty.";
        }

        if (secretName.Length > 127)
        {
            return $"'keyVaultSecretName' must be 127 characters or fewer (provided: {secretName.Length} characters).";
        }

        if (!KeyVaultSecretNameRegex.IsMatch(secretName))
        {
            return "'keyVaultSecretName' must contain only alphanumeric characters (a-z, A-Z, 0-9) and hyphens (-).";
        }

        return null; // valid
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Payload builders — Dataverse Web API write payloads
    // Property names must match Dataverse logical attribute names.
    // Lookup fields use OData @odata.bind syntax.
    // ─────────────────────────────────────────────────────────────────────────

    private static object BuildCreatePayload(CreateConfigRequest r)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = r.Name,
            ["sprk_containertypeid"] = r.ContainerTypeId,
            ["sprk_billingclassification"] = ConfigDataverseRow.BillingToInt(r.BillingClassification),
            ["sprk_owningappid"] = r.OwningAppId,
            ["sprk_keyvaultsecretname"] = r.KeyVaultSecretName,
            ["sprk_isregistered"] = false,
            ["sprk_itemversioningenabled"] = r.IsItemVersioningEnabled
        };

        // Optional scalar fields — only include if provided
        if (r.ContainerTypeName != null) payload["sprk_containertypename"] = r.ContainerTypeName;
        if (r.ConsumingAppId != null) payload["sprk_consumingappid"] = r.ConsumingAppId;
        if (r.ConsumingAppKeyVaultSecret != null) payload["sprk_consumingappkvsecret"] = r.ConsumingAppKeyVaultSecret;
        if (r.DelegatedPermissions != null) payload["sprk_delegatedpermission"] = r.DelegatedPermissions;
        if (r.ApplicationPermissions != null) payload["sprk_applicationpermissions"] = r.ApplicationPermissions;
        if (r.DefaultContainerId != null) payload["sprk_defaultcontainerid"] = r.DefaultContainerId;
        if (r.MaxStoragePerBytes.HasValue) payload["sprk_maxstorageperbytes"] = r.MaxStoragePerBytes.Value;
        if (r.SharingCapability != null) payload["sprk_sharingcapability"] = ConfigDataverseRow.SharingToInt(r.SharingCapability);
        if (r.ItemMajorVersionLimit.HasValue) payload["sprk_itemmajorversionlimit"] = r.ItemMajorVersionLimit.Value;
        if (r.Notes != null) payload["sprk_notes"] = r.Notes;

        // Lookup fields — OData bind syntax required by Dataverse REST API
        if (r.BusinessUnitId.HasValue)
            payload["sprk_BusinessUnit@odata.bind"] = $"/businessunits({r.BusinessUnitId.Value})";

        if (r.EnvironmentId.HasValue)
            payload["sprk_Environment@odata.bind"] = $"/sprk_speenvironments({r.EnvironmentId.Value})";

        return payload;
    }

    private static object BuildUpdatePayload(UpdateConfigRequest r)
    {
        var payload = new Dictionary<string, object?>();

        // Only include fields that were explicitly provided in the request
        if (r.Name != null) payload["sprk_name"] = r.Name;
        if (r.ContainerTypeId != null) payload["sprk_containertypeid"] = r.ContainerTypeId;
        if (r.ContainerTypeName != null) payload["sprk_containertypename"] = r.ContainerTypeName;
        if (r.BillingClassification != null) payload["sprk_billingclassification"] = ConfigDataverseRow.BillingToInt(r.BillingClassification);
        if (r.OwningAppId != null) payload["sprk_owningappid"] = r.OwningAppId;
        if (r.KeyVaultSecretName != null) payload["sprk_keyvaultsecretname"] = r.KeyVaultSecretName;
        if (r.ConsumingAppId != null) payload["sprk_consumingappid"] = r.ConsumingAppId;
        if (r.ConsumingAppKeyVaultSecret != null) payload["sprk_consumingappkvsecret"] = r.ConsumingAppKeyVaultSecret;
        if (r.DelegatedPermissions != null) payload["sprk_delegatedpermission"] = r.DelegatedPermissions;
        if (r.ApplicationPermissions != null) payload["sprk_applicationpermissions"] = r.ApplicationPermissions;
        if (r.DefaultContainerId != null) payload["sprk_defaultcontainerid"] = r.DefaultContainerId;
        if (r.MaxStoragePerBytes.HasValue) payload["sprk_maxstorageperbytes"] = r.MaxStoragePerBytes.Value;
        if (r.SharingCapability != null) payload["sprk_sharingcapability"] = ConfigDataverseRow.SharingToInt(r.SharingCapability);
        if (r.IsItemVersioningEnabled.HasValue) payload["sprk_itemversioningenabled"] = r.IsItemVersioningEnabled.Value;
        if (r.ItemMajorVersionLimit.HasValue) payload["sprk_itemmajorversionlimit"] = r.ItemMajorVersionLimit.Value;
        if (r.Notes != null) payload["sprk_notes"] = r.Notes;

        // Lookup fields
        if (r.BusinessUnitId.HasValue)
            payload["sprk_BusinessUnit@odata.bind"] = $"/businessunits({r.BusinessUnitId.Value})";

        if (r.EnvironmentId.HasValue)
            payload["sprk_Environment@odata.bind"] = $"/sprk_speenvironments({r.EnvironmentId.Value})";

        return payload;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns a 400 ProblemDetails result for validation errors (ADR-019).</summary>
    private static IResult ValidationProblem(string detail, string correlationId) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation Error",
            extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId });
}
