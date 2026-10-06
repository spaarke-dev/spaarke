using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Api.Filters;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// CRUD endpoints for SPE Container Type Config records (/api/spe/configs).
///
/// Endpoints:
///   GET    /api/spe/configs          — list configs, optionally filtered by BU and/or environment
///   GET    /api/spe/configs/{configId} — single config detail
///   POST   /api/spe/configs          — create new config (audit logged)
///   PUT    /api/spe/configs/{configId} — update config (audit logged)
///   DELETE /api/spe/configs/{configId} — delete config (audit logged)
///
/// Authorization: Inherited from /api/spe route group (SpeAdminAuthorizationFilter — System Admin only —
/// then SpeAdminTenantScopeFilter, which confines {configId} to the caller's business units). The route
/// parameter is named <c>configId</c> so that filter reads it: until task 165 it was <c>{id}</c>, the
/// filter found no configId, and any SPE admin could read, rewrite or delete any business unit's config.
/// POST and PUT also judge the BODY values the write would store (business unit; app identity) via
/// <see cref="SpeAdminTenantScope.DecideConfigWriteAsync"/> — they are not the config being acted on, so
/// the filter cannot judge them (a POST has no configId at all).
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

        // The three config RECORD routes never use the config's credential, so a config whose stored Key Vault secret
        // name is outside the allow-list can still be seen, corrected (PUT) and deleted (task 165, round 35 item 3).
        configs.MapGet("/{configId:guid}", GetConfigAsync)
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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        configs.MapPut("/{configId:guid}", UpdateConfigAsync)
            .WithName("UpdateSpeConfig")
            .WithSummary("Update an existing container type config")
            .Produces<ConfigDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        configs.MapDelete("/{configId:guid}", DeleteConfigAsync)
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

    /// <summary>
    /// Lists the container type configs the CALLER may see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>businessUnitId</c> is a narrowing filter, never a widening one.</b> It used to be the
    /// only business-unit constraint applied, and it came straight from the query string — so
    /// omitting it returned every customer's configuration, and supplying someone else's returned
    /// theirs. The accessible set is now always derived from the caller's own Dataverse user; the
    /// parameter can only intersect with it.
    /// </para>
    /// <para>
    /// This is what makes the container-type / container dropdowns scope themselves. Filtering in the
    /// client would be cosmetic — the API underneath would still answer.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ListConfigsAsync(
        [FromQuery] Guid? businessUnitId,
        [FromQuery] Guid? environmentId,
        DataverseWebApiClient dataverseClient,
        SpeAdminTenantScope tenantScope,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            var accessible = await tenantScope.GetAccessibleBusinessUnitsAsync(context.User, ct);

            if (accessible.Count == 0)
            {
                // The caller could not be resolved to a Dataverse user with a business unit. Fail
                // closed: an empty accessible set means "nothing", never "no filter".
                logger.LogWarning(
                    "ListSpeConfigs: caller has no resolvable business unit — returning empty. TraceId={TraceId}",
                    context.TraceIdentifier);
                return TypedResults.Ok(new List<ConfigSummaryDto>());
            }

            // Optional narrowing by the caller. Intersect rather than replace, so the parameter can
            // never reach outside the derived set.
            var scopedUnits = businessUnitId.HasValue
                ? accessible.Where(bu => bu == businessUnitId.Value).ToList()
                : accessible.ToList();

            if (scopedUnits.Count == 0)
            {
                return TypedResults.Ok(new List<ConfigSummaryDto>());
            }

            var filters = new List<string>
            {
                // Bare Edm.Guid literals (ADR-044) — a quoted GUID is Edm.String and Dataverse rejects it.
                $"({string.Join(" or ", scopedUnits.Select(bu => $"_sprk_businessunit_value eq {bu:D}"))})"
            };

            if (environmentId.HasValue)
                filters.Add($"_sprk_environment_value eq {environmentId.Value:D}");

            var filter = string.Join(" and ", filters);

            var rows = await dataverseClient.QueryAsync<ConfigDataverseRow>(
                EntitySet,
                filter: filter,
                select: ListSelect,
                cancellationToken: ct);

            var items = rows.Select(r => r.ToSummary()).ToList();

            logger.LogInformation(
                "ListSpeConfigs: returned {Count} configs across {BuCount} accessible business unit(s). environmentId={EnvId} correlationId={CorrelationId}",
                items.Count, scopedUnits.Count, environmentId, context.TraceIdentifier);

            return TypedResults.Ok(items);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ListSpeConfigs failed. correlationId={CorrelationId}",
                context.TraceIdentifier);

            return TypedResults.Problem(
                detail: ProblemDetailsHelper.Explain("Failed to retrieve container type configs.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/spe/configs/{configId}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetConfigAsync(
        Guid configId,
        DataverseWebApiClient dataverseClient,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            var row = await dataverseClient.RetrieveAsync<ConfigDataverseRow>(
                EntitySet,
                configId,
                select: DetailSelect,
                cancellationToken: ct);

            if (row == null)
            {
                logger.LogInformation(
                    "GetSpeConfig: not found. id={Id} correlationId={CorrelationId}",
                    configId, context.TraceIdentifier);

                return SpeAdminTenantScopeFilter.ConfigNotFound(configId, context.TraceIdentifier);
            }

            logger.LogInformation(
                "GetSpeConfig: retrieved config {Id} correlationId={CorrelationId}",
                configId, context.TraceIdentifier);

            return TypedResults.Ok(row.ToDetail());
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return SpeAdminTenantScopeFilter.ConfigNotFound(configId, context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "GetSpeConfig failed. id={Id} correlationId={CorrelationId}",
                configId, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: ProblemDetailsHelper.Explain("Failed to retrieve the container type config.", ex),
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
        SpeAdminTenantScope tenantScope,
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

        // keyVaultSecretName is OPTIONAL since 2026-10-04: SPE Admin authenticates as the BFF's own
        // identity and reads no credential from the config, so requiring a secret name only forced
        // operators to type a placeholder (a config was saved with the literal "null"). Validated when given.
        if (!string.IsNullOrEmpty(request.KeyVaultSecretName))
        {
            var kvValidation = ValidateKeyVaultSecretName(request.KeyVaultSecretName);
            if (kvValidation != null)
            {
                return ValidationProblem(kvValidation, context.TraceIdentifier);
            }

            // Task 165 round 35 item 3, narrowed by round 65: a SUPPLIED name must be under the ONE pinned prefix.
            // The BFF no longer reads the secret, but scripts/Backfill-SpeContainerBusinessUnitStamp.ps1 still does
            // (it lists a config's containers as its owning app), so no config may name another vault secret.
            if (!SpeConfigSecretNamePolicy.IsAllowed(request.KeyVaultSecretName))
            {
                return SecretNameNotAllowed(context.TraceIdentifier);
            }
        }

        // Task 165: a config with no business unit is visible to EVERY admin (the compatibility rule in
        // SpeAdminTenantScope.DecideConfigAccessAsync). Existing rows keep that rule; new ones may not
        // be created into it.
        if (request.BusinessUnitId is null || request.BusinessUnitId == Guid.Empty)
        {
            return ValidationProblem("'businessUnitId' is required.", context.TraceIdentifier);
        }

        // Task 165 (sweep #74): the business unit must be one the caller administers, the environment one
        // they can read (round 16 item 4), and no app-identity value may be borrowed from a config in a
        // unit they do not.
        var writeRefusal = WriteRefusal(
            await tenantScope.DecideConfigWriteAsync(
                context.User,
                request.BusinessUnitId,
                IdentityValues(
                    request.ContainerTypeId, request.OwningAppId, request.KeyVaultSecretName,
                    request.ConsumingAppId, request.ConsumingAppKeyVaultSecret),
                excludeConfigId: null,
                environmentId: request.EnvironmentId,
                ct),
            context.TraceIdentifier);

        if (writeRefusal is not null)
        {
            return writeRefusal;
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
                detail: ProblemDetailsHelper.Explain("Failed to create the container type config.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/spe/configs/{configId}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> UpdateConfigAsync(
        Guid configId,
        UpdateConfigRequest request,
        DataverseWebApiClient dataverseClient,
        SpeAdminTenantScope tenantScope,
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

            // Task 165, round 35 item 3. Judged whenever sent — an unchanged re-send of a name stored before the rule
            // is refused too, so saving a misconfigured config forces the rename.
            if (!SpeConfigSecretNamePolicy.IsAllowed(request.KeyVaultSecretName))
            {
                return SecretNameNotAllowed(context.TraceIdentifier);
            }
        }

        try
        {
            // Verify the record exists before attempting update
            ConfigDataverseRow? existing;
            try
            {
                existing = await dataverseClient.RetrieveAsync<ConfigDataverseRow>(
                    EntitySet, configId, select: DetailSelect, cancellationToken: ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                existing = null;
            }

            if (existing == null)
            {
                return SpeAdminTenantScopeFilter.ConfigNotFound(configId, context.TraceIdentifier);
            }

            // Task 165 (sweep #45). The filter confined the config being updated; these judge the values
            // the update would STORE. Identity fields are judged only where they CHANGE: the shipped client
            // sends every field on every save, unchanged ones included, and an unchanged value is not a new
            // borrowing. A linked environment is judged whenever sent: the config's current one is always
            // readable (this config reaches it), so the client's unchanged re-send is never refused.
            var writeRefusal = WriteRefusal(
                await tenantScope.DecideConfigWriteAsync(
                    context.User,
                    request.BusinessUnitId,
                    IdentityValues(
                        Changed(SpeAdminTenantScope.IdentityColumns.ContainerTypeId, request.ContainerTypeId, existing.ContainerTypeId),
                        Changed(SpeAdminTenantScope.IdentityColumns.OwningAppId, request.OwningAppId, existing.OwningAppId),
                        Changed(SpeAdminTenantScope.IdentityColumns.KeyVaultSecretName, request.KeyVaultSecretName, existing.KeyVaultSecretName),
                        Changed(SpeAdminTenantScope.IdentityColumns.ConsumingAppId, request.ConsumingAppId, existing.ConsumingAppId),
                        Changed(SpeAdminTenantScope.IdentityColumns.ConsumingAppKvSecret, request.ConsumingAppKeyVaultSecret, existing.ConsumingAppKvSecret)),
                    excludeConfigId: configId,
                    environmentId: request.EnvironmentId,
                    ct),
                context.TraceIdentifier);

            if (writeRefusal is not null)
            {
                return writeRefusal;
            }

            var payload = BuildUpdatePayload(request);
            await dataverseClient.UpdateAsync(EntitySet, configId, payload, ct);

            logger.LogInformation(
                "UpdateSpeConfig: updated config {Id} correlationId={CorrelationId}",
                configId, context.TraceIdentifier);

            // Audit log
            _ = auditService.LogOperationAsync(
                operation: "UpdateContainerTypeConfig",
                category: AuditCategory,
                targetResource: configId.ToString(),
                responseStatus: StatusCodes.Status200OK,
                configId: configId,
                environmentId: request.EnvironmentId ?? existing.EnvironmentId,
                businessUnitId: request.BusinessUnitId ?? existing.BusinessUnitId,
                cancellationToken: CancellationToken.None);

            // Return the updated record
            var updated = await dataverseClient.RetrieveAsync<ConfigDataverseRow>(
                EntitySet, configId, select: DetailSelect, cancellationToken: ct);

            return TypedResults.Ok(updated?.ToDetail() ?? existing.ToDetail());
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "UpdateSpeConfig failed. id={Id} correlationId={CorrelationId}",
                configId, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: ProblemDetailsHelper.Explain("Failed to update the container type config.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/spe/configs/{configId}
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> DeleteConfigAsync(
        Guid configId,
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
                    EntitySet, configId,
                    select: "sprk_specontainertypeconfigid,_sprk_businessunit_value,_sprk_environment_value",
                    cancellationToken: ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                existing = null;
            }

            if (existing == null)
            {
                return SpeAdminTenantScopeFilter.ConfigNotFound(configId, context.TraceIdentifier);
            }

            await dataverseClient.DeleteAsync(EntitySet, configId, ct);

            logger.LogInformation(
                "DeleteSpeConfig: deleted config {Id} correlationId={CorrelationId}",
                configId, context.TraceIdentifier);

            // Audit log
            _ = auditService.LogOperationAsync(
                operation: "DeleteContainerTypeConfig",
                category: AuditCategory,
                targetResource: configId.ToString(),
                responseStatus: StatusCodes.Status204NoContent,
                configId: configId,
                environmentId: existing.EnvironmentId,
                businessUnitId: existing.BusinessUnitId,
                cancellationToken: CancellationToken.None);

            return TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "DeleteSpeConfig failed. id={Id} correlationId={CorrelationId}",
                configId, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: ProblemDetailsHelper.Explain("Failed to delete the container type config.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Task 165: write-scope helpers (POST / PUT)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The app-identity values a write would store, keyed by Dataverse column.</summary>
    private static IReadOnlyDictionary<string, string?> IdentityValues(
        string? containerTypeId,
        string? owningAppId,
        string? keyVaultSecretName,
        string? consumingAppId,
        string? consumingAppKvSecret) =>
        new Dictionary<string, string?>
        {
            [SpeAdminTenantScope.IdentityColumns.ContainerTypeId] = containerTypeId,
            [SpeAdminTenantScope.IdentityColumns.OwningAppId] = owningAppId,
            [SpeAdminTenantScope.IdentityColumns.KeyVaultSecretName] = keyVaultSecretName,
            [SpeAdminTenantScope.IdentityColumns.ConsumingAppId] = consumingAppId,
            [SpeAdminTenantScope.IdentityColumns.ConsumingAppKvSecret] = consumingAppKvSecret,
        };

    /// <summary>
    /// The requested value when it differs from the stored one in the canonical comparison form
    /// (<see cref="SpeAdminTenantScope.CanonicalIdentityColumnValue"/>: a GUID-valued id compared as a GUID,
    /// anything else trimmed; case-insensitive); otherwise null, which the write check ignores.
    /// </summary>
    private static string? Changed(string column, string? requested, string? stored) =>
        requested is not null
        && !string.Equals(
            SpeAdminTenantScope.CanonicalIdentityColumnValue(column, requested),
            SpeAdminTenantScope.CanonicalIdentityColumnValue(column, stored),
            StringComparison.OrdinalIgnoreCase)
            ? requested
            : null;

    /// <summary>Maps a write-scope decision to its refusal, or null when the write may proceed.</summary>
    private static IResult? WriteRefusal(SpeAdminScopeDecision decision, string traceId) => decision switch
    {
        SpeAdminScopeDecision.Permitted => null,

        SpeAdminScopeDecision.BusinessUnitOutOfScope => ProblemDetailsHelper.Forbidden(
            "spe.admin.deny.business_unit_out_of_scope",
            "The business unit is not one you administer.",
            traceId),

        // One answer for an environment that does not exist and one the caller cannot read (round 16 item 4).
        SpeAdminScopeDecision.EnvironmentOutOfScope => ProblemDetailsHelper.Forbidden(
            "spe.admin.deny.environment_out_of_scope",
            "The SPE environment is not one you can use.",
            traceId),

        // Deliberately names neither the other config nor its business unit.
        SpeAdminScopeDecision.IdentityOutOfScope => ProblemDetailsHelper.Forbidden(
            "spe.admin.deny.config_identity_out_of_scope",
            "A container type, app or secret named in this configuration is already used by a configuration " +
            "outside the business units you administer.",
            traceId),

        // Unverifiable — and any case this method does not recognise — refuses (ADR-003 fail closed).
        _ => SpeAdminTenantScopeFilter.ScopeUnverifiable(traceId),
    };

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

    /// <summary>
    /// The 400 for a supplied <c>keyVaultSecretName</c> outside <see cref="SpeConfigSecretNamePolicy"/> (task 165, round 35
    /// item 3; since round 65 the ONLY place the BFF applies the rule: it reads no secret itself).
    /// </summary>
    private static IResult SecretNameNotAllowed(string correlationId) =>
        TypedResults.Problem(
            detail: $"'keyVaultSecretName' must start with '{SpeConfigSecretNamePolicy.RequiredPrefix}' — no other Key Vault " +
                    "secret may be named by a container type config.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation Error",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = SpeConfigSecretNamePolicy.NotAllowedReasonCode,
                ["correlationId"] = correlationId
            });

    /// <summary>Returns a 400 ProblemDetails result for validation errors (ADR-019).</summary>
    private static IResult ValidationProblem(string detail, string correlationId) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation Error",
            extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId });
}
