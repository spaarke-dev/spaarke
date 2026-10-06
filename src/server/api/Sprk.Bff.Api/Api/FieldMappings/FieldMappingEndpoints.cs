using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Api.FieldMappings;

/// <summary>
/// API endpoints for field mapping profile operations.
/// Used by PCF controls and external integrations to query field mapping configurations.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern (no controllers).
/// Follows ADR-008: Endpoint filters for authorization.
/// Follows ADR-019: ProblemDetails for error responses.
/// </remarks>
public static class FieldMappingEndpoints
{
    /// <summary>
    /// Registers field mapping endpoints with the application.
    /// </summary>
    public static void MapFieldMappingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/field-mappings")
            .WithTags("Field Mappings")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization(); // All endpoints require authentication

        // GET /api/v1/field-mappings/profiles
        group.MapGet("profiles", GetProfilesAsync)
            .WithName("GetFieldMappingProfiles")
            .WithSummary("Get all field mapping profiles")
            .WithDescription("Returns all active field mapping profiles, optionally filtered by source or target entity.")
            .Produces<FieldMappingProfileListResponse>(200)
            .Produces(401)  // Unauthorized
            .Produces(500); // Internal Server Error

        // POST /api/v1/field-mappings/validate
        group.MapPost("validate", ValidateMappingAsync)
            .WithName("ValidateFieldMapping")
            .WithSummary("Validate type compatibility for a field mapping rule")
            .WithDescription("Validates whether a source field type can be mapped to a target field type. " +
                "Uses the Strict type compatibility matrix. Returns validation result with compatible type suggestions.")
            .Produces<ValidateMappingResponse>(200)
            .ProducesValidationProblem()
            .Produces(401)  // Unauthorized
            .Produces(500); // Internal Server Error

        // GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}
        group.MapGet("profiles/{sourceEntity}/{targetEntity}", GetProfileByEntityPairAsync)
            .WithName("GetFieldMappingProfileByEntityPair")
            .WithSummary("Get field mapping profile for an entity pair")
            .WithDescription("Returns the field mapping profile with all rules for a specific source/target entity pair. Returns 404 if no profile exists.")
            .Produces<FieldMappingProfileWithRulesDto>(200)
            .Produces(401)  // Unauthorized
            .Produces(404)  // Not Found
            .Produces(500); // Internal Server Error

        // POST /api/v1/field-mappings/push
        group.MapPost("push", PushFieldMappingsAsync)
            .WithName("PushFieldMappings")
            .WithSummary("Push field mappings from parent to all related child records")
            .WithDescription("Pushes field values from a parent record to all related child records based on " +
                "the active field mapping profile for the entity pair. Used by the UpdateRelatedButton PCF control. " +
                "Limit: 500 child records per operation. Continues on partial failure.")
            .Produces<PushFieldMappingsResponse>(200)
            .ProducesValidationProblem()
            .Produces(401)  // Unauthorized
            .Produces(404)  // Profile Not Found
            .Produces(409)  // Parent lookup missing or ambiguous in relationship metadata (task 166 r1)
            .Produces(500); // Internal Server Error
    }

    /// <summary>
    /// Validates type compatibility for a proposed field mapping rule.
    /// </summary>
    /// <param name="request">Validation request with source and target field types.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>Validation result with compatibility status and suggestions.</returns>
    private static IResult ValidateMappingAsync(
        [FromBody] ValidateMappingRequest request,
        ILogger<Program> logger)
    {
        logger.LogInformation(
            "Validating field mapping type compatibility. SourceType={SourceType}, TargetType={TargetType}",
            request.SourceFieldType, request.TargetFieldType);

        // Validate request
        if (string.IsNullOrWhiteSpace(request.SourceFieldType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sourceFieldType"] = ["Source field type is required."]
            });
        }

        if (string.IsNullOrWhiteSpace(request.TargetFieldType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["targetFieldType"] = ["Target field type is required."]
            });
        }

        try
        {
            var result = TypeCompatibilityValidator.Validate(
                request.SourceFieldType,
                request.TargetFieldType);

            logger.LogDebug(
                "Validation result: IsValid={IsValid}, Level={Level}, Errors={ErrorCount}",
                result.IsValid, result.CompatibilityLevel, result.Errors.Length);

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating field mapping type compatibility");

            return Results.Problem(
                detail: "An error occurred while validating type compatibility",
                statusCode: 500,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Gets all field mapping profiles with optional filtering.
    /// </summary>
    /// <param name="sourceEntity">Optional filter by source entity logical name.</param>
    /// <param name="targetEntity">Optional filter by target entity logical name.</param>
    /// <param name="isActive">Optional filter by active status. Defaults to true.</param>
    /// <param name="dataverseService">Dataverse service for querying.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of field mapping profiles.</returns>
    private static async Task<IResult> GetProfilesAsync(
        [FromQuery] string? sourceEntity,
        [FromQuery] string? targetEntity,
        [FromQuery] bool? isActive,
        IFieldMappingDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Retrieving field mapping profiles. SourceEntity={SourceEntity}, TargetEntity={TargetEntity}, IsActive={IsActive}",
            sourceEntity, targetEntity, isActive);

        try
        {
            // Query profiles from Dataverse with optional filtering
            var profiles = await QueryFieldMappingProfilesAsync(
                dataverseService,
                sourceEntity,
                targetEntity,
                isActive ?? true,
                ct);

            var response = new FieldMappingProfileListResponse
            {
                Items = profiles,
                TotalCount = profiles.Length
            };

            logger.LogDebug("Returning {Count} field mapping profiles", profiles.Length);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving field mapping profiles");

            return Results.Problem(
                detail: "An error occurred while retrieving field mapping profiles",
                statusCode: 500,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Queries field mapping profiles from Dataverse.
    /// </summary>
    private static async Task<FieldMappingProfileDto[]> QueryFieldMappingProfilesAsync(
        IFieldMappingDataverseService dataverseService,
        string? sourceEntity,
        string? targetEntity,
        bool isActive,
        CancellationToken ct)
    {
        var entities = await dataverseService.QueryFieldMappingProfilesAsync(ct);

        // Apply client-side filtering for optional parameters
        var filtered = entities.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(sourceEntity))
        {
            filtered = filtered.Where(e => string.Equals(e.SourceEntity, sourceEntity, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(targetEntity))
        {
            filtered = filtered.Where(e => string.Equals(e.TargetEntity, targetEntity, StringComparison.OrdinalIgnoreCase));
        }

        if (isActive)
        {
            filtered = filtered.Where(e => e.IsActive);
        }

        return filtered.Select(MapProfileEntityToDto).ToArray();
    }

    /// <summary>
    /// Maps a Dataverse FieldMappingProfileEntity to a DTO.
    /// </summary>
    private static FieldMappingProfileDto MapProfileEntityToDto(FieldMappingProfileEntity entity)
    {
        return new FieldMappingProfileDto
        {
            Id = entity.Id,
            Name = entity.Name ?? string.Empty,
            SourceEntity = entity.SourceEntity ?? string.Empty,
            TargetEntity = entity.TargetEntity ?? string.Empty,
            SyncMode = MapSyncModeToString(entity.SyncMode),
            IsActive = entity.IsActive
        };
    }

    /// <summary>
    /// Maps sync mode integer to string representation.
    /// </summary>
    private static string MapSyncModeToString(int syncMode)
    {
        return syncMode switch
        {
            0 => FieldMappingSyncMode.OneTime,
            1 => FieldMappingSyncMode.ManualRefresh,
            _ => FieldMappingSyncMode.OneTime
        };
    }

    /// <summary>
    /// Gets a field mapping profile with all rules for a specific source/target entity pair.
    /// </summary>
    /// <param name="sourceEntity">Logical name of the source entity (e.g., "sprk_matter", "account").</param>
    /// <param name="targetEntity">Logical name of the target entity (e.g., "sprk_event").</param>
    /// <param name="dataverseService">Dataverse service for querying.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Profile with rules or 404 if not found.</returns>
    private static async Task<IResult> GetProfileByEntityPairAsync(
        string sourceEntity,
        string targetEntity,
        IFieldMappingDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Retrieving field mapping profile for entity pair. SourceEntity={SourceEntity}, TargetEntity={TargetEntity}",
            sourceEntity, targetEntity);

        // Validate inputs
        if (string.IsNullOrWhiteSpace(sourceEntity))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sourceEntity"] = ["Source entity is required."]
            });
        }

        if (string.IsNullOrWhiteSpace(targetEntity))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["targetEntity"] = ["Target entity is required."]
            });
        }

        try
        {
            // Query profile with rules from Dataverse
            var profile = await QueryProfileWithRulesByEntityPairAsync(
                dataverseService,
                sourceEntity,
                targetEntity,
                ct);

            if (profile is null)
            {
                logger.LogDebug(
                    "No field mapping profile found for entity pair. SourceEntity={SourceEntity}, TargetEntity={TargetEntity}",
                    sourceEntity, targetEntity);

                return Results.Problem(
                    detail: $"No field mapping profile found for source entity '{sourceEntity}' and target entity '{targetEntity}'.",
                    statusCode: 404,
                    title: "Profile Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            logger.LogDebug(
                "Returning profile {ProfileId} with {RuleCount} rules for entity pair",
                profile.Id, profile.Rules.Length);

            return TypedResults.Ok(profile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error retrieving field mapping profile for entity pair. SourceEntity={SourceEntity}, TargetEntity={TargetEntity}",
                sourceEntity, targetEntity);

            return Results.Problem(
                detail: "An error occurred while retrieving the field mapping profile",
                statusCode: 500,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Queries a field mapping profile with its rules from Dataverse by entity pair.
    /// </summary>
    private static async Task<FieldMappingProfileWithRulesDto?> QueryProfileWithRulesByEntityPairAsync(
        IFieldMappingDataverseService dataverseService,
        string sourceEntity,
        string targetEntity,
        CancellationToken ct)
    {
        // PPI-024: Use $expand to get profile + rules in a single Dataverse call (2 calls → 1).
        // Falls back to sequential calls if $expand is not supported by the implementation.
        var profile = await dataverseService.GetFieldMappingProfileWithRulesAsync(sourceEntity, targetEntity, activeRulesOnly: true, ct);
        if (profile is null)
        {
            return null;
        }

        return new FieldMappingProfileWithRulesDto
        {
            Id = profile.Id,
            Name = profile.Name ?? string.Empty,
            SourceEntity = profile.SourceEntity ?? string.Empty,
            TargetEntity = profile.TargetEntity ?? string.Empty,
            SyncMode = MapSyncModeToString(profile.SyncMode),
            IsActive = profile.IsActive,
            Rules = (profile.Rules ?? []).Select(MapRuleEntityToDto).ToArray()
        };
    }

    /// <summary>
    /// Maps a Dataverse FieldMappingRuleEntity to a DTO.
    /// </summary>
    /// <remarks>
    /// Internal (not private) so the test assembly (InternalsVisibleTo, see .csproj) can exercise
    /// the projection directly without reflection — see FieldMappingRuleProjectionTests.
    /// </remarks>
    internal static FieldMappingRuleDto MapRuleEntityToDto(FieldMappingRuleEntity entity)
    {
        return new FieldMappingRuleDto
        {
            Id = entity.Id,
            SourceField = entity.SourceField ?? string.Empty,
            SourceFieldType = MapFieldTypeToString(entity.SourceFieldType),
            TargetField = entity.TargetField ?? string.Empty,
            TargetFieldType = MapFieldTypeToString(entity.TargetFieldType),
            Priority = entity.ExecutionOrder,
            MappingType = MapMappingTypeToString(entity.MappingType),
            DefaultValue = entity.DefaultValue,
            Expression = entity.Expression,
            IsRequired = entity.IsRequired,
            CompatibilityMode = MapCompatibilityModeToString(entity.CompatibilityMode)
        };
    }

    /// <summary>
    /// Maps the sprk_mapping_type int choice to its string representation.
    /// </summary>
    private static string MapMappingTypeToString(int mappingType)
    {
        return mappingType switch
        {
            0 => "Copy",
            1 => "Default",
            2 => "Concat",
            3 => "Template",
            _ => "Copy"
        };
    }

    /// <summary>
    /// Maps the sprk_compatibilitymode int choice to its string representation.
    /// </summary>
    private static string MapCompatibilityModeToString(int compatibilityMode)
    {
        return compatibilityMode switch
        {
            0 => "Strict",
            1 => "Resolve",
            _ => "Strict"
        };
    }

    /// <summary>
    /// Maps field type integer to string representation.
    /// </summary>
    private static string MapFieldTypeToString(int fieldType)
    {
        return fieldType switch
        {
            0 => "Text",
            1 => "Lookup",
            2 => "OptionSet",
            3 => "Number",
            4 => "DateTime",
            5 => "Boolean",
            6 => "Memo",
            _ => "Text"
        };
    }

    /// <summary>
    /// Maximum number of child records allowed per push operation.
    /// </summary>
    private const int MaxChildRecordsPerPush = 500;

    /// <summary>
    /// Pushes field mappings from a parent record to all related child records.
    /// </summary>
    /// <param name="request">Push request with source entity, record ID, and target entity.</param>
    /// <param name="dataverseService">Dataverse service for querying and updating records.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Push result with counts of updated, failed, and skipped records.</returns>
    private static async Task<IResult> PushFieldMappingsAsync(
        [FromBody] PushFieldMappingsRequest request,
        IFieldMappingDataverseService dataverseService,
        [FromServices] Sprk.Bff.Api.Services.Dataverse.CoreAncestorRestamper restamper,
        [FromServices] Sprk.Bff.Api.Services.Access.SecureRootFilingGate rootFiling,
        IServiceScopeFactory scopes,
        // Task 166 (S-67): the caller's own rights decide — source Read via the OBO probe, children read and
        // written AS the caller (MSCRMCallerID impersonation). See the gate below.
        CallerRecordAccessProbe probe,
        IImpersonatedCommunicationQuery impersonatedQuery,
        IGenericEntityService entityService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Pushing field mappings. SourceEntity={SourceEntity}, SourceRecordId={SourceRecordId}, TargetEntity={TargetEntity}",
            request.SourceEntity, request.SourceRecordId, request.TargetEntity);

        // Validate request
        var validationErrors = ValidatePushRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        // ── Task 166 (S-67): authorize the SOURCE as the caller, and establish who the caller IS, BEFORE anything ──
        //
        // This route read the source's mapped fields app-only, queried up to 500 children app-only, and PATCHed each
        // one app-only — for any source id any signed-in caller named. It was latent only because a doubled
        // `_…_value` wrap made the child query 400; fixing that bug without this gate would have made it a mass
        // write over children of records the caller cannot see. So the gate and the fix land together, gate first:
        //   (1) the caller must hold Read on the SOURCE record (CallerRecordAccessProbe, as the caller);
        //   (2) the caller's systemuserid must resolve (WhoAmI on their own token), because every child read and
        //       write below runs IMPERSONATED as that user — Dataverse then shows and writes only children THEY may.
        // An unmapped source type, an unreadable or non-existent source, a missing token, an unresolvable caller and
        // a probe fault are ONE 404, and no profile query, source read, child query or write has happened.
        var callerSystemUserId = await AuthorizePushSourceAsync(request, probe, httpContext, logger, ct);
        if (callerSystemUserId is null)
        {
            return SourceRecordNotFound();
        }

        try
        {
            // Step 1: Find mapping profile for entity pair
            var profile = await QueryProfileWithRulesByEntityPairAsync(
                dataverseService,
                request.SourceEntity,
                request.TargetEntity,
                ct);

            if (profile is null)
            {
                logger.LogWarning(
                    "No field mapping profile found for push. SourceEntity={SourceEntity}, TargetEntity={TargetEntity}",
                    request.SourceEntity, request.TargetEntity);

                return Results.Problem(
                    detail: $"No field mapping profile found for source entity '{request.SourceEntity}' and target entity '{request.TargetEntity}'.",
                    statusCode: 404,
                    title: "Profile Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            if (!profile.Rules.Any())
            {
                logger.LogWarning(
                    "Profile has no mapping rules. ProfileId={ProfileId}",
                    profile.Id);

                return TypedResults.Ok(new PushFieldMappingsResponse
                {
                    Success = true,
                    TargetEntity = request.TargetEntity,
                    TotalRecords = 0,
                    UpdatedCount = 0,
                    FailedCount = 0,
                    SkippedCount = 0,
                    Warnings = ["Profile has no mapping rules configured."]
                });
            }

            // Step 2: Get source record field values — AS THE CALLER (task 166 r2). The row-level Read gate above
            // does not cover field-level security: an app-only read would return a secured source column the caller
            // cannot read and copy it into children they CAN read. Impersonated, Dataverse returns such a column as
            // null (the rule is then skipped), and a source the caller cannot read at all returns no row (404).
            // Lookup-aware (2026-10-06): which rule fields are lookups — and which exist at all — is read from the
            // source's metadata, so a lookup is selected as `_x_value` and an unknown field never poisons the read.
            var sourceFields = profile.Rules.Select(r => r.SourceField).Distinct().ToArray();
            var sourceValues = await RetrieveSourceRecordValuesAsCallerAsync(
                impersonatedQuery,
                entityService,
                request.SourceEntity,
                request.SourceRecordId,
                sourceFields,
                callerSystemUserId.Value,
                ct);

            if (sourceValues is null)
            {
                logger.LogWarning(
                    "Source record not found. SourceEntity={SourceEntity}, SourceRecordId={SourceRecordId}",
                    request.SourceEntity, request.SourceRecordId);

                return SourceRecordNotFound();
            }

            // Step 3a (task 166 r1, owner round 21 item 3): WHICH lookup on the target names the source is read from
            // relationship metadata — the target's many-to-one relationships whose referenced entity is the source —
            // instead of the `sprk_regarding{base}` naming convention, which sprk_invoice (it carries sprk_matter) does
            // not follow. Exactly one lookup is required; none or several fail closed before any child is read.
            // The same one read of the target's relationships also tells the lookup writes below which navigation
            // property binds each target lookup.
            var targetRelationships = await ReadManyToOneRelationshipsAsync(
                impersonatedQuery, request.TargetEntity, callerSystemUserId.Value, ct);
            var (parentLookup, parentLookupCount) = ResolveParentLookup(targetRelationships, request.SourceEntity);
            if (parentLookup is null)
            {
                logger.LogWarning(
                    "Push refused: {TargetEntity} has {Count} lookup(s) to {SourceEntity} in relationship metadata; exactly "
                    + "one is required to find the children. Nothing was read or updated.",
                    request.TargetEntity, parentLookupCount, request.SourceEntity);
                return ParentLookupNotResolvable(request.SourceEntity, request.TargetEntity, parentLookupCount);
            }

            // Step 3: Query the child records related to source (limit 500) — AS THE CALLER (task 166), so a child
            // they cannot read never enters the set, the counts, the errors or the field results.
            var childRecords = await QueryChildRecordsAsync(
                impersonatedQuery,
                entityService,
                parentLookup,
                request.SourceRecordId,
                request.TargetEntity,
                callerSystemUserId.Value,
                MaxChildRecordsPerPush,
                ct);

            if (childRecords.TotalCount > MaxChildRecordsPerPush)
            {
                logger.LogWarning(
                    "Too many child records for push. Found={Count}, Limit={Limit}",
                    childRecords.TotalCount, MaxChildRecordsPerPush);

                return Results.Problem(
                    detail: $"Too many child records ({childRecords.TotalCount}). Maximum allowed per push operation is {MaxChildRecordsPerPush}. " +
                        "Consider filtering to a subset or using pagination.",
                    statusCode: 400,
                    title: "Limit Exceeded",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
            }

            if (childRecords.RecordIds.Length == 0)
            {
                logger.LogInformation("No child records found to update");

                return TypedResults.Ok(new PushFieldMappingsResponse
                {
                    Success = true,
                    TargetEntity = request.TargetEntity,
                    TotalRecords = 0,
                    UpdatedCount = 0,
                    FailedCount = 0,
                    SkippedCount = 0
                });
            }

            // Step 3b (2026-10-06): a rule whose TARGET is a lookup is written as `{nav}@odata.bind` — decided once here,
            // since the source value is the same for every child. A rule whose referent cannot be resolved is refused
            // on its own (an Error field result); it never fails the push or the other rules.
            var lookupBinds = await FieldMappingPushLookups.PlanLookupWritesAsync(
                profile.Rules,
                sourceValues,
                FieldMappingPushLookups.TargetLookups(targetRelationships),
                request.TargetEntity,
                entityService,
                ct);

            // Step 4: For each child, apply mapping rules and update
            var (updatedCount, failedCount, skippedCount, errors, fieldResults) = await ApplyMappingsToChildRecordsAsync(
                dataverseService,
                restamper,
                profile.Rules,
                sourceValues,
                request.TargetEntity,
                childRecords.RecordIds,
                callerSystemUserId.Value,
                logger,
                ct,
                scopes,
                rootFiling,
                lookupBinds);

            var success = updatedCount > 0 || (failedCount == 0 && childRecords.RecordIds.Length > 0);

            logger.LogInformation(
                "Push completed. Total={Total}, Updated={Updated}, Failed={Failed}, Skipped={Skipped}",
                childRecords.RecordIds.Length, updatedCount, failedCount, skippedCount);

            return TypedResults.Ok(new PushFieldMappingsResponse
            {
                Success = success,
                TargetEntity = request.TargetEntity,
                TotalRecords = childRecords.RecordIds.Length,
                UpdatedCount = updatedCount,
                FailedCount = failedCount,
                SkippedCount = skippedCount,
                Errors = errors,
                FieldResults = fieldResults
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error pushing field mappings. SourceEntity={SourceEntity}, SourceRecordId={SourceRecordId}, TargetEntity={TargetEntity}",
                request.SourceEntity, request.SourceRecordId, request.TargetEntity);

            return Results.Problem(
                detail: "An error occurred while pushing field mappings",
                statusCode: 500,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Validates the push request and returns any validation errors.
    /// </summary>
    private static Dictionary<string, string[]> ValidatePushRequest(PushFieldMappingsRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.SourceEntity))
        {
            errors["sourceEntity"] = ["Source entity is required."];
        }
        else if (!IsLogicalName(request.SourceEntity))
        {
            // Task 166 r1: entity names are interpolated into Dataverse queries and metadata paths.
            errors["sourceEntity"] = ["Source entity must be a Dataverse logical name (letters, digits and underscores)."];
        }

        if (request.SourceRecordId == Guid.Empty)
        {
            errors["sourceRecordId"] = ["Source record ID is required and must be a valid GUID."];
        }

        if (string.IsNullOrWhiteSpace(request.TargetEntity))
        {
            errors["targetEntity"] = ["Target entity is required."];
        }
        else if (!IsLogicalName(request.TargetEntity))
        {
            errors["targetEntity"] = ["Target entity must be a Dataverse logical name (letters, digits and underscores)."];
        }

        return errors;
    }

    /// <summary>
    /// The source-row query issued AS THE CALLER (task 166 r2): the mapped fields of exactly the authorized source row.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> so a test can pin the exact string. A rule field that is not a logical name is never interpolated
    /// into the query (it reads as null, so its rule is skipped); the row's own id column is always selected, so the
    /// <c>$select</c> is never empty.
    /// </remarks>
    internal static string BuildSourceRecordQuery(string sourceEntity, Guid sourceRecordId, IEnumerable<string> fields)
    {
        var select = fields.Where(IsLogicalName).Append($"{sourceEntity}id").Distinct(StringComparer.OrdinalIgnoreCase);
        return $"$select={string.Join(",", select)}&$filter={sourceEntity}id eq {sourceRecordId:D}&$top=1";
    }

    /// <summary>
    /// Retrieves the source record's mapped field values AS THE CALLER (task 166 r2) through the same impersonated seam
    /// the child query uses — so field-level security applies: a secured column the caller cannot read comes back
    /// null and its rule is skipped, instead of being copied into children the caller can read. No row (the caller
    /// cannot read the source, or it does not exist) → <see langword="null"/> (the route's 404). No app-only fallback:
    /// a fault propagates to the route's 500.
    /// </summary>
    /// <remarks>
    /// Replaces the app-only <c>IFieldMappingDataverseService.RetrieveRecordFieldsAsync</c> call. Values are converted
    /// exactly as that method converts them (string, Int64 or double, bool, null, else raw JSON text), so the rule
    /// engine sees the same shapes.
    /// <para><b>Lookup-aware (2026-10-06).</b> The source's attribute metadata (read as the caller, like everything
    /// here) decides each field's column: a lookup is selected as <c>_x_value</c> and returned as a
    /// <see cref="SourceLookupValue"/> under the rule's own key; a field that is not a readable attribute of the source
    /// is not selected and is absent from the result (its rule is skipped as "not found"). Before this, one lookup rule
    /// made Dataverse reject the whole read (400 0x80060888) and every push of the profile 500ed. Only when a selected
    /// field is a lookup are the source's relationships read, to learn which table each single-table lookup
    /// references.</para>
    /// </remarks>
    private static async Task<Dictionary<string, object?>?> RetrieveSourceRecordValuesAsCallerAsync(
        IImpersonatedCommunicationQuery impersonatedQuery,
        IGenericEntityService entityService,
        string sourceEntity,
        Guid sourceRecordId,
        string[] fields,
        Guid callerSystemUserId,
        CancellationToken ct)
    {
        var attributes = FieldMappingPushLookups.ReadableAttributes(await impersonatedQuery.QueryAsync(
            FieldMappingPushLookups.AttributeMetadataPath(sourceEntity),
            FieldMappingPushLookups.AttributeMetadataQuery,
            callerSystemUserId,
            ct));

        var selectsALookup = fields.Any(f => attributes.TryGetValue(f, out var type) && FieldMappingPushLookups.IsLookupType(type));
        var referents = selectsALookup
            ? FieldMappingPushLookups.SingleTableReferents(
                await ReadManyToOneRelationshipsAsync(impersonatedQuery, sourceEntity, callerSystemUserId, ct), attributes)
            : new Dictionary<string, string>();

        var entitySet = await entityService.GetEntitySetNameAsync(sourceEntity, ct);
        var rows = await impersonatedQuery.QueryAsync(
            entitySet,
            BuildSourceRecordQuery(sourceEntity, sourceRecordId, FieldMappingPushLookups.SourceSelectColumns(fields, attributes)),
            callerSystemUserId,
            ct);

        if (rows.Count == 0)
        {
            return null;
        }

        return FieldMappingPushLookups.MapSourceRow(rows[0], fields, attributes, referents, ToClrValue);
    }

    /// <summary>The JSON → CLR conversion the app-only field read applied (kept identical for the rule engine).</summary>
    private static object? ToClrValue(System.Text.Json.JsonElement element) => element.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => element.GetString(),
        System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.Null => null,
        System.Text.Json.JsonValueKind.Undefined => null,
        _ => element.GetRawText(),
    };

    /// <summary>
    /// The ONE "source not found" answer (task 166): an unknown source id, a source the caller cannot read, an
    /// unmapped source type, a missing token, an unresolvable caller and a probe fault all return exactly this. It
    /// names neither the id nor the entity, so the route cannot be used to learn which records exist.
    /// </summary>
    internal static IResult SourceRecordNotFound() =>
        Results.Problem(
            detail: "Source record not found.",
            statusCode: 404,
            title: "Source Record Not Found",
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");

    /// <summary>
    /// Task 166 (S-67): the caller's Read on the push SOURCE, then the caller's systemuserid. Returns the
    /// systemuserid to impersonate for every child read and write, or <see langword="null"/> — DENY — when any
    /// step cannot establish it.
    /// </summary>
    /// <remarks>
    /// Reuses, does not fork: the entity set comes from <see cref="EntityAccessFilter.TryResolveEntitySet"/> (a miss
    /// denies — a type whose per-record access cannot be evaluated is a type this route must not push from), the
    /// rights from <see cref="CallerRecordAccessProbe.GetCallerRightsAsync"/> under the existing <c>"read"</c> key,
    /// and the identity from <see cref="CallerRecordAccessProbe.GetCallerSystemUserIdAsync"/> (WhoAmI on the caller's
    /// own token — the one identity path that cannot be fooled). The try covers the decision only.
    /// </remarks>
    private static async Task<Guid?> AuthorizePushSourceAsync(
        PushFieldMappingsRequest request,
        CallerRecordAccessProbe probe,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        if (!EntityAccessFilter.TryResolveEntitySet(request.SourceEntity, out var sourceEntitySet))
        {
            logger.LogWarning(
                "[FIELD-MAPPING-PUSH] Denied: source entity '{SourceEntity}' is not in EntityAccessFilter's map, so the "
                + "caller's rights on it cannot be evaluated. Answered 404.", request.SourceEntity);
            return null;
        }

        var token = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        try
        {
            var rights = await probe.GetCallerRightsAsync(token, sourceEntitySet, request.SourceRecordId, ct);
            if (!OperationAccessPolicy.HasRequiredRights(rights, ReadOperation))
            {
                logger.LogWarning(
                    "[FIELD-MAPPING-PUSH] Denied: caller holds {Rights} on {EntitySet}({SourceRecordId}); Read required. "
                    + "Answered 404 — nothing read or written.", rights, sourceEntitySet, request.SourceRecordId);
                return null;
            }

            var callerSystemUserId = await probe.GetCallerSystemUserIdAsync(token, ct);
            if (callerSystemUserId is null || callerSystemUserId == Guid.Empty)
            {
                logger.LogWarning(
                    "[FIELD-MAPPING-PUSH] Denied: the caller's Dataverse systemuserid could not be resolved, so children "
                    + "cannot be read or written as the caller. Answered 404 (no app-only fallback).");
                return null;
            }

            return callerSystemUserId;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[FIELD-MAPPING-PUSH] The source authorization check faulted for {EntitySet}({SourceRecordId}); denying "
                + "(404).", sourceEntitySet, request.SourceRecordId);
            return null;
        }
    }

    /// <summary>The existing <see cref="OperationAccessPolicy"/> key for reading a record.</summary>
    private const string ReadOperation = "read";

    /// <summary>
    /// Builds the child-record query issued AS THE CALLER (task 166). <paramref name="parentLookupAttribute"/> is the
    /// target's lookup LOGICAL name (from <see cref="ResolveParentLookup"/>), and the <c>_…_value</c> wrap is
    /// applied EXACTLY ONCE, here. The old path wrapped an already-wrapped name a second time
    /// (<c>__sprk_regardingmatter_value_value</c>) — Dataverse answered 400 and the route always 500ed.
    /// </summary>
    /// <remarks><c>internal</c> so a test can pin the exact string — the double prefix must not come back.</remarks>
    internal static string BuildChildRecordQuery(string parentLookupAttribute, Guid sourceRecordId, string targetEntity, int top)
        => $"$filter=_{parentLookupAttribute}_value eq {sourceRecordId:D}"
           + $"&$select={targetEntity}id&$top={top}";

    /// <summary>
    /// The relationship-metadata collection the parent lookup is resolved from: the TARGET's many-to-one
    /// relationships (task 166 r1, owner round 21 item 3). <paramref name="targetEntity"/> has passed
    /// <see cref="IsLogicalName"/>, so it cannot break out of the quoted key.
    /// </summary>
    internal static string ParentLookupMetadataPath(string targetEntity)
        => $"EntityDefinitions(LogicalName='{targetEntity}')/ManyToOneRelationships";

    /// <summary>
    /// The columns read from each relationship (filtered in memory, so no metadata $filter support is assumed). The
    /// navigation property (2026-10-06) is what a lookup write binds through; the parent-lookup match ignores it.
    /// </summary>
    internal const string ParentLookupMetadataQuery = FieldMappingPushLookups.RelationshipMetadataQuery;

    /// <summary>An entity's many-to-one relationships, read AS THE CALLER through the impersonated seam.</summary>
    internal static Task<IReadOnlyList<Dictionary<string, System.Text.Json.JsonElement>>> ReadManyToOneRelationshipsAsync(
        IImpersonatedCommunicationQuery impersonatedQuery, string entity, Guid callerSystemUserId, CancellationToken ct)
        => impersonatedQuery.QueryAsync(ParentLookupMetadataPath(entity), ParentLookupMetadataQuery, callerSystemUserId, ct);

    /// <summary>
    /// Task 166 r1 (owner round 21 item 3): the target's ONE lookup that references the source entity, from
    /// relationship metadata read AS THE CALLER (the same impersonated seam the child query uses). Returns the lookup's
    /// logical name, or <see langword="null"/> with the number of matching lookups found (0 = none, &gt;1 = ambiguous) —
    /// both fail closed: with no lookup the children cannot be found, and with several the route cannot know which
    /// one names the parent, so it must not guess and write to children it may have chosen wrongly.
    /// </summary>
    /// <remarks>
    /// Replaces the <c>sprk_regarding{base}</c> naming convention (<c>DetermineParentLookupField</c>, deleted), which the
    /// live "Matter to Invoice (Attorney Matrix)" profile broke: <c>sprk_invoice</c> carries <c>sprk_matter</c>, not
    /// <c>sprk_regardingmatter</c>. Live metadata 2026-10-04 (read-only): sprk_workassignment, sprk_event and
    /// sprk_reportcard each have exactly one lookup to sprk_matter (sprk_regardingmatter); sprk_invoice has exactly one
    /// (sprk_matter). No schema change, no per-table convention, no deactivated profile. A metadata fault propagates
    /// to the route's 500 — never to a guessed lookup.
    /// </remarks>
    internal static (string? Attribute, int Matches) ResolveParentLookup(
        IEnumerable<Dictionary<string, System.Text.Json.JsonElement>> relationships,
        string sourceEntity)
    {
        var matches = relationships
            .Where(r => r.TryGetValue("ReferencedEntity", out var referenced)
                        && referenced.ValueKind == System.Text.Json.JsonValueKind.String
                        && string.Equals(referenced.GetString(), sourceEntity, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.TryGetValue("ReferencingAttribute", out var attribute)
                         && attribute.ValueKind == System.Text.Json.JsonValueKind.String
                ? attribute.GetString()
                : null)
            .Where(a => !string.IsNullOrWhiteSpace(a) && IsLogicalName(a!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches.Length == 1 ? (matches[0], 1) : (null, matches.Length);
    }

    /// <summary>A Dataverse logical name: letters, digits and underscores only (it is interpolated into OData).</summary>
    internal static bool IsLogicalName(string value)
        => value.Length is > 0 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>Stable reason codes for the two parent-lookup refusals (task 166 r1).</summary>
    internal const string ParentLookupMissingReasonCode = "field_mapping_parent_lookup_missing";
    internal const string ParentLookupAmbiguousReasonCode = "field_mapping_parent_lookup_ambiguous";

    /// <summary>
    /// 409: the profile's target table has no lookup — or more than one — to the source in relationship metadata, so
    /// the push cannot tell which children belong to this record. Nothing was read or updated. The source was already
    /// authorized for the caller, so naming the two tables discloses nothing they could not see.
    /// </summary>
    private static IResult ParentLookupNotResolvable(string sourceEntity, string targetEntity, int matches) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Parent Lookup Not Resolvable",
            detail: matches == 0
                ? $"'{targetEntity}' has no lookup to '{sourceEntity}', so its records related to this one cannot be found. Nothing was updated."
                : $"'{targetEntity}' has {matches} lookups to '{sourceEntity}', so which one names the parent is ambiguous. Nothing was updated.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = matches == 0 ? ParentLookupMissingReasonCode : ParentLookupAmbiguousReasonCode,
            });

    /// <summary>
    /// Queries child records related to the source record, AS THE CALLER (task 166): Dataverse returns only the
    /// children the caller may read, so a child they cannot see never reaches the counts, errors or field results.
    /// </summary>
    /// <remarks>
    /// Replaces the app-only <c>IFieldMappingDataverseService.QueryChildRecordIdsAsync</c> call (which also carried
    /// the double-prefix bug). No app-only fallback: an impersonation fault surfaces as the route's 500, never as an
    /// unscoped read.
    /// </remarks>
    private static async Task<(Guid[] RecordIds, int TotalCount)> QueryChildRecordsAsync(
        IImpersonatedCommunicationQuery impersonatedQuery,
        IGenericEntityService entityService,
        string parentLookupAttribute,
        Guid sourceRecordId,
        string targetEntity,
        Guid callerSystemUserId,
        int maxRecords,
        CancellationToken ct)
    {
        var entitySet = await entityService.GetEntitySetNameAsync(targetEntity, ct);
        var odataQuery = BuildChildRecordQuery(parentLookupAttribute, sourceRecordId, targetEntity, maxRecords + 1);

        var rows = await impersonatedQuery.QueryAsync(entitySet, odataQuery, callerSystemUserId, ct);

        var idColumn = $"{targetEntity}id";
        var recordIds = rows
            .Select(row => row.TryGetValue(idColumn, out var value)
                           && value.ValueKind == System.Text.Json.JsonValueKind.String
                           && Guid.TryParse(value.GetString(), out var id)
                ? id
                : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        // Return with count (limit to maxRecords + 1 for checking if more exist)
        var limitedRecordIds = recordIds.Take(maxRecords + 1).ToArray();
        return (limitedRecordIds.Take(maxRecords).ToArray(), limitedRecordIds.Length);
    }

    /// <summary>
    /// Applies mapping rules to each child record and updates them.
    /// </summary>
    /// <remarks>Internal (not private) so the test assembly can drive it directly: task 156 (the re-file cascade), and
    /// task 142's writer test, which asserts the inline Assigned-To trigger after a push that wrote a root's "Assigned *"
    /// column (InternalsVisibleTo).</remarks>
    internal static async Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> ApplyMappingsToChildRecordsAsync(
        IFieldMappingDataverseService dataverseService,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorRestamper restamper,
        FieldMappingRuleDto[] rules,
        Dictionary<string, object?> sourceValues,
        string targetEntity,
        Guid[] childRecordIds,
        Guid callerSystemUserId,
        ILogger logger,
        CancellationToken ct,
        IServiceScopeFactory? scopes = null,
        Sprk.Bff.Api.Services.Access.SecureRootFilingGate? rootFiling = null,
        IReadOnlyDictionary<FieldMappingRuleDto, LookupBindPlan>? lookupBinds = null)
    {
        var errors = new List<PushFieldMappingsError>();
        var fieldResults = new List<FieldMappingResultDto>();
        var updated = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var childRecordId in childRecordIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var updatePayload = new Dictionary<string, object?>();
                var recordFieldResults = new List<FieldMappingResultDto>();

                foreach (var rule in rules.OrderBy(r => r.Priority))
                {
                    var fieldResult = ApplyMappingRule(rule, sourceValues, updatePayload, lookupBinds);
                    recordFieldResults.Add(fieldResult);
                }

                if (updatePayload.Count > 0)
                {
                    // Task 158 (owner round 6): a work assignment or project this push files under a matter or project —
                    // whether that record is secure must be readable, or this record is not written (fail closed; a
                    // host without the gate refuses such a write too).
                    if (Sprk.Bff.Api.Services.Access.SecureRootInheritance.Inherits(targetEntity))
                    {
                        var refusal = rootFiling is not null
                            ? await rootFiling.CheckAsync(targetEntity, childRecordId, updatePayload, ct)
                            : Sprk.Bff.Api.Services.Access.SecureRootInheritance.FilingColumnsOf(targetEntity.Trim().ToLowerInvariant())
                                .Overlaps(updatePayload.Keys.Select(Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn))
                                ? Sprk.Bff.Api.Services.Dataverse.RecordOwnerResolution.Refused(
                                    Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.ParentUndetermined,
                                    "whether the record it would be filed under is secure cannot be checked here")
                                : null;
                        if (refusal is not null)
                        {
                            failed++;
                            errors.Add(new PushFieldMappingsError
                            {
                                RecordId = childRecordId,
                                Error = $"Not written: {refusal.Reason} ({refusal.RefusalCode})."
                            });
                            continue;
                        }
                    }

                    // Task 166: written AS THE CALLER (MSCRMCallerID) — Dataverse applies their Write on the child,
                    // so no app-only child write remains on this route.
                    await dataverseService.UpdateRecordFieldsAsync(
                        targetEntity, childRecordId, updatePayload, ct, impersonateSystemUserId: callerSystemUserId);
                    updated++;

                    // Task 156 (owner round 4 item 5, option b): a mapping rule can write a lookup — including what this
                    // record is filed under or its matter / project — so the copies that depend on it are re-stamped in
                    // the same operation. A write that cannot move a stamp reads nothing; a child that fails is logged
                    // and repaired by the reconciliation job, and never fails this push.
                    await restamper.AfterWriteAsync(targetEntity, childRecordId, updatePayload.Keys, CancellationToken.None);

                    // Task 158: a work assignment or project this push filed under a secure record is secured now (never
                    // thrown; an incomplete securing is logged and the secure-root inheritance job completes it).
                    if (rootFiling is not null)
                        await rootFiling.SecureAfterWriteAsync(targetEntity, childRecordId, updatePayload.Keys, traceId: null);

                    // Task 142 (L1): a push that wrote a ROOT's "Assigned *" column (a project/matter/work assignment
                    // child of the source) materializes its Assigned-To access now. A non-root target or a non-registry
                    // column is a no-op; after the update committed; never throws, never fails this push.
                    await Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer.RunAfterWriteAsync(
                        scopes, targetEntity, childRecordId, updatePayload.Keys, grantorOid: null, logger, ct);
                }
                else
                {
                    skipped++;
                }

                fieldResults.AddRange(recordFieldResults);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to update child record. TargetEntity={TargetEntity}, RecordId={RecordId}",
                    targetEntity, childRecordId);

                failed++;
                errors.Add(new PushFieldMappingsError
                {
                    RecordId = childRecordId,
                    Error = ex.Message
                });
            }
        }

        return (updated, failed, skipped, errors.ToArray(), fieldResults.ToArray());
    }

    /// <summary>
    /// Applies a single mapping rule to transform a source value for the target field.
    /// </summary>
    /// <remarks>
    /// Internal (not private) so the test assembly (InternalsVisibleTo, see .csproj) can exercise
    /// the push-path engine helper directly without reflection — see FieldMappingRuleProjectionTests.
    /// <para><b>Lookups (2026-10-06).</b> A rule in <paramref name="lookupBinds"/> targets a lookup: it writes its planned
    /// <c>{nav}@odata.bind</c>, or — when the plan refused it — reports an Error and writes nothing. A source lookup
    /// copied into a non-lookup target writes the referenced record's display name, never its raw id.</para>
    /// </remarks>
    internal static FieldMappingResultDto ApplyMappingRule(
        FieldMappingRuleDto rule,
        Dictionary<string, object?> sourceValues,
        Dictionary<string, object?> updatePayload,
        IReadOnlyDictionary<FieldMappingRuleDto, LookupBindPlan>? lookupBinds = null)
    {
        try
        {
            // Check if source field exists in source values
            if (!sourceValues.TryGetValue(rule.SourceField, out var sourceValue))
            {
                return new FieldMappingResultDto
                {
                    SourceField = rule.SourceField,
                    TargetField = rule.TargetField,
                    Status = FieldMappingStatus.Skipped,
                    ErrorMessage = "Source field not found in source record"
                };
            }

            // Skip if source value is null (unless rule requires it)
            if (sourceValue is null)
            {
                return new FieldMappingResultDto
                {
                    SourceField = rule.SourceField,
                    TargetField = rule.TargetField,
                    Status = FieldMappingStatus.Skipped,
                    ErrorMessage = "Source value is null"
                };
            }

            // Validate type compatibility
            var validation = TypeCompatibilityValidator.Validate(rule.SourceFieldType, rule.TargetFieldType);
            if (!validation.IsValid)
            {
                return new FieldMappingResultDto
                {
                    SourceField = rule.SourceField,
                    TargetField = rule.TargetField,
                    Status = FieldMappingStatus.Error,
                    ErrorMessage = $"Type compatibility error: {string.Join("; ", validation.Errors)}"
                };
            }

            if (lookupBinds is not null && lookupBinds.TryGetValue(rule, out var bind))
            {
                if (bind.BindKey is null || bind.BindValue is null)
                {
                    return new FieldMappingResultDto
                    {
                        SourceField = rule.SourceField,
                        TargetField = rule.TargetField,
                        Status = FieldMappingStatus.Error,
                        ErrorMessage = bind.Problem ?? "The lookup could not be resolved; it was not copied."
                    };
                }

                updatePayload[bind.BindKey] = bind.BindValue;
                return new FieldMappingResultDto
                {
                    SourceField = rule.SourceField,
                    TargetField = rule.TargetField,
                    Status = FieldMappingStatus.Mapped
                };
            }

            if (sourceValue is SourceLookupValue lookup)
            {
                if (string.IsNullOrEmpty(lookup.DisplayName))
                {
                    return new FieldMappingResultDto
                    {
                        SourceField = rule.SourceField,
                        TargetField = rule.TargetField,
                        Status = FieldMappingStatus.Skipped,
                        ErrorMessage = "The source lookup's display name is not available"
                    };
                }

                updatePayload[rule.TargetField] = lookup.DisplayName;
                return new FieldMappingResultDto
                {
                    SourceField = rule.SourceField,
                    TargetField = rule.TargetField,
                    Status = FieldMappingStatus.Mapped
                };
            }

            // Transform value if needed (basic implementation)
            var targetValue = TransformValue(sourceValue, rule.SourceFieldType, rule.TargetFieldType);
            updatePayload[rule.TargetField] = targetValue;

            return new FieldMappingResultDto
            {
                SourceField = rule.SourceField,
                TargetField = rule.TargetField,
                Status = FieldMappingStatus.Mapped
            };
        }
        catch (Exception ex)
        {
            return new FieldMappingResultDto
            {
                SourceField = rule.SourceField,
                TargetField = rule.TargetField,
                Status = FieldMappingStatus.Error,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Transforms a source value to the target field type.
    /// </summary>
    private static object? TransformValue(object? sourceValue, string sourceType, string targetType)
    {
        if (sourceValue is null)
        {
            return null;
        }

        // Same type: no transformation needed
        if (string.Equals(sourceType, targetType, StringComparison.OrdinalIgnoreCase))
        {
            return sourceValue;
        }

        // Converting to Text: format as string
        if (string.Equals(targetType, "Text", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetType, "Memo", StringComparison.OrdinalIgnoreCase))
        {
            return sourceValue switch
            {
                DateTime dt => dt.ToString("o"), // ISO 8601
                bool b => b ? "Yes" : "No",
                _ => sourceValue.ToString()
            };
        }

        // Other conversions: return as-is (Dataverse will handle compatible types)
        return sourceValue;
    }
}
