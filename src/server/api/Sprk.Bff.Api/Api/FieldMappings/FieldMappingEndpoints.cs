using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;

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
        IDataverseService dataverseService,
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
        IDataverseService dataverseService,
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
        IDataverseService dataverseService,
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
        IDataverseService dataverseService,
        string sourceEntity,
        string targetEntity,
        CancellationToken ct)
    {
        // Get profile by entity pair
        var profile = await dataverseService.GetFieldMappingProfileAsync(sourceEntity, targetEntity, ct);
        if (profile is null)
        {
            return null;
        }

        // Get rules for this profile
        var rules = await dataverseService.GetFieldMappingRulesAsync(profile.Id, activeOnly: true, ct);

        return new FieldMappingProfileWithRulesDto
        {
            Id = profile.Id,
            Name = profile.Name ?? string.Empty,
            SourceEntity = profile.SourceEntity ?? string.Empty,
            TargetEntity = profile.TargetEntity ?? string.Empty,
            SyncMode = MapSyncModeToString(profile.SyncMode),
            IsActive = profile.IsActive,
            Rules = rules.Select(MapRuleEntityToDto).ToArray()
        };
    }

    /// <summary>
    /// Maps a Dataverse FieldMappingRuleEntity to a DTO.
    /// </summary>
    private static FieldMappingRuleDto MapRuleEntityToDto(FieldMappingRuleEntity entity)
    {
        return new FieldMappingRuleDto
        {
            Id = entity.Id,
            SourceField = entity.SourceField ?? string.Empty,
            SourceFieldType = MapFieldTypeToString(entity.SourceFieldType),
            TargetField = entity.TargetField ?? string.Empty,
            TargetFieldType = MapFieldTypeToString(entity.TargetFieldType),
            Priority = entity.ExecutionOrder
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
        IDataverseService dataverseService,
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

            // Step 2: Get source record field values
            var sourceFields = profile.Rules.Select(r => r.SourceField).Distinct().ToArray();
            var sourceValues = await RetrieveSourceRecordValuesAsync(
                dataverseService,
                request.SourceEntity,
                request.SourceRecordId,
                sourceFields,
                ct);

            if (sourceValues is null)
            {
                logger.LogWarning(
                    "Source record not found. SourceEntity={SourceEntity}, SourceRecordId={SourceRecordId}",
                    request.SourceEntity, request.SourceRecordId);

                return Results.Problem(
                    detail: $"Source record '{request.SourceRecordId}' not found in entity '{request.SourceEntity}'.",
                    statusCode: 404,
                    title: "Source Record Not Found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // Step 3: Query all child records related to source (limit 500)
            var childRecords = await QueryChildRecordsAsync(
                dataverseService,
                request.SourceEntity,
                request.SourceRecordId,
                request.TargetEntity,
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

            // Step 4: For each child, apply mapping rules and update
            var (updatedCount, failedCount, skippedCount, errors, fieldResults) = await ApplyMappingsToChildRecordsAsync(
                dataverseService,
                profile.Rules,
                sourceValues,
                request.TargetEntity,
                childRecords.RecordIds,
                logger,
                ct);

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

        if (request.SourceRecordId == Guid.Empty)
        {
            errors["sourceRecordId"] = ["Source record ID is required and must be a valid GUID."];
        }

        if (string.IsNullOrWhiteSpace(request.TargetEntity))
        {
            errors["targetEntity"] = ["Target entity is required."];
        }

        return errors;
    }

    /// <summary>
    /// Retrieves field values from the source record.
    /// </summary>
    private static async Task<Dictionary<string, object?>?> RetrieveSourceRecordValuesAsync(
        IDataverseService dataverseService,
        string entityLogicalName,
        Guid recordId,
        string[] fields,
        CancellationToken ct)
    {
        try
        {
            return await dataverseService.RetrieveRecordFieldsAsync(entityLogicalName, recordId, fields, ct);
        }
        catch (Exception ex) when (ex.Message.Contains("404") || ex.Message.Contains("not found"))
        {
            // Record not found
            return null;
        }
    }

    /// <summary>
    /// Queries child records related to the source record.
    /// </summary>
    private static async Task<(Guid[] RecordIds, int TotalCount)> QueryChildRecordsAsync(
        IDataverseService dataverseService,
        string sourceEntity,
        Guid sourceRecordId,
        string targetEntity,
        int maxRecords,
        CancellationToken ct)
    {
        // Determine the parent lookup field based on source entity
        // Convention: sprk_regarding{sourceEntity} without prefix (e.g., sprk_regardingmatter)
        var parentLookupField = DetermineParentLookupField(sourceEntity);

        // Query child record IDs
        var recordIds = await dataverseService.QueryChildRecordIdsAsync(
            targetEntity,
            parentLookupField,
            sourceRecordId,
            ct);

        // Return with count (limit to maxRecords + 1 for checking if more exist)
        var limitedRecordIds = recordIds.Take(maxRecords + 1).ToArray();
        return (limitedRecordIds.Take(maxRecords).ToArray(), limitedRecordIds.Length);
    }

    /// <summary>
    /// Determines the parent lookup field name based on the source entity.
    /// </summary>
    private static string DetermineParentLookupField(string sourceEntity)
    {
        // For standard regarding records, use the convention: _sprk_regarding{entitybasename}_value
        // For example: sprk_matter -> _sprk_regardingmatter_value
        //              account -> _sprk_regardingaccount_value
        var entityBaseName = sourceEntity.Replace("sprk_", "");
        return $"_sprk_regarding{entityBaseName}_value";
    }

    /// <summary>
    /// Applies mapping rules to each child record and updates them.
    /// </summary>
    private static async Task<(int Updated, int Failed, int Skipped, PushFieldMappingsError[] Errors, FieldMappingResultDto[] FieldResults)> ApplyMappingsToChildRecordsAsync(
        IDataverseService dataverseService,
        FieldMappingRuleDto[] rules,
        Dictionary<string, object?> sourceValues,
        string targetEntity,
        Guid[] childRecordIds,
        ILogger logger,
        CancellationToken ct)
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
                    var fieldResult = ApplyMappingRule(rule, sourceValues, updatePayload);
                    recordFieldResults.Add(fieldResult);
                }

                if (updatePayload.Count > 0)
                {
                    await dataverseService.UpdateRecordFieldsAsync(targetEntity, childRecordId, updatePayload, ct);
                    updated++;
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
    private static FieldMappingResultDto ApplyMappingRule(
        FieldMappingRuleDto rule,
        Dictionary<string, object?> sourceValues,
        Dictionary<string, object?> updatePayload)
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
