using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Nodes;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IActionSeam"/>. Maps public request records to the internal,
/// session-agnostic action cores (<c>Services/Ai/Nodes/ActionCore/*</c>) — the SAME cores the three
/// node executors call — so a notification/task/record write from a non-playbook caller is
/// byte-for-byte identical to the executor's write.
/// </summary>
/// <remarks>
/// Introduces zero behavior change vs. the executors' Dataverse writes (task 031 / FR-07). The value
/// of the facade is structural: it lets Phase 4/5 producers create these records WITHOUT constructing
/// a <c>NodeExecutionContext</c>, per ADR-013's facade discipline. Registered unconditionally (record
/// creation is not AI-model-gated, so — unlike <c>IBriefingAi</c> — it needs no null-object fallback).
/// </remarks>
public sealed class ActionSeam : IActionSeam
{
    private readonly IGenericEntityService _entityService;
    private readonly IFieldMappingDataverseService _fieldMappingService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ActionSeam> _logger;

    public ActionSeam(
        IGenericEntityService entityService,
        IFieldMappingDataverseService fieldMappingService,
        IServiceScopeFactory scopeFactory,
        ILogger<ActionSeam> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _fieldMappingService = fieldMappingService ?? throw new ArgumentNullException(nameof(fieldMappingService));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CreateNotificationResult> CreateNotificationAsync(
        CreateNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        // Required-field validation (the seam has no run-context fallback for recipient).
        if (request.RecipientId is null || request.RecipientId == Guid.Empty)
            return new CreateNotificationResult(false, null, false, "recipientId is required");
        if (string.IsNullOrWhiteSpace(request.Title))
            return new CreateNotificationResult(false, null, false, "title is required");
        if (string.IsNullOrWhiteSpace(request.Body))
            return new CreateNotificationResult(false, null, false, "body is required");

        var core = new NotificationActionCore(_entityService, _logger);
        var result = await core.CreateAsync(
            new NotificationActionInput(
                Title: request.Title,
                Body: request.Body,
                Category: request.Category,
                Priority: request.Priority,
                ToastType: request.ToastType,
                ActionUrl: request.ActionUrl,
                RecipientId: request.RecipientId.Value,
                RegardingId: request.RegardingId,
                RegardingType: request.RegardingType,
                DueDate: request.DueDate,
                RegardingName: request.RegardingName,
                SourceEntityType: request.SourceEntityType,
                SourceId: request.SourceId,
                SourceModifiedOn: request.SourceModifiedOn,
                SourceOwningUser: request.SourceOwningUser,
                ViaMatterId: request.ViaMatterId,
                ViaMatterName: request.ViaMatterName,
                ViaMatterMemberships: request.ViaMatterMemberships,
                Source: request.Source,
                CorrelationId: request.CorrelationId ?? string.Empty),
            cancellationToken);

        return new CreateNotificationResult(true, result.NotificationId, result.Skipped, null);
    }

    /// <inheritdoc />
    public async Task<CreateTaskResult> CreateTaskAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Subject))
            return new CreateTaskResult(false, Guid.Empty, "subject is required");

        var core = new TaskActionCore(_entityService, _logger);
        var taskId = await core.CreateAsync(
            new TaskActionInput(
                Subject: request.Subject,
                Description: request.Description,
                ScheduledEnd: request.DueDate?.ToUniversalTime(),
                RegardingObjectId: request.RegardingObjectId,
                RegardingObjectType: request.RegardingObjectType,
                OwnerId: request.OwnerId),
            cancellationToken);

        return new CreateTaskResult(true, taskId, null);
    }

    /// <inheritdoc />
    public async Task<UpdateRecordResult> UpdateRecordAsync(
        UpdateRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.EntityLogicalName))
            return new UpdateRecordResult(false, Array.Empty<string>(), "entityLogicalName is required");
        if (request.RecordId == Guid.Empty)
            return new UpdateRecordResult(false, Array.Empty<string>(), "recordId is required");

        var renderedMappings = request.FieldMappings?
            .Select(m => new RenderedFieldMapping(m.Field, MapFieldType(m.Type), m.Value, m.Options))
            .ToList();
        var renderedLookups = request.Lookups?
            .Select(l => new RenderedLookup(l.Field, l.TargetEntity, l.TargetId))
            .ToList();

        var core = new UpdateRecordActionCore(_fieldMappingService, _scopeFactory, _logger);
        try
        {
            var fieldsUpdated = await core.UpdateAsync(
                new UpdateRecordActionInput(
                    request.EntityLogicalName,
                    request.RecordId,
                    renderedMappings,
                    request.LegacyFields,
                    renderedLookups,
                    request.ImpersonateSystemUserId),
                cancellationToken);

            return new UpdateRecordResult(true, fieldsUpdated.ToArray(), null);
        }
        catch (FieldCoercionException ex)
        {
            // FAIL LOUD (FR-C1) surfaced as a typed failure — no PATCH was issued.
            return new UpdateRecordResult(false, Array.Empty<string>(), ex.Message);
        }
    }

    private static FieldMappingType MapFieldType(ActionFieldType type) => type switch
    {
        ActionFieldType.String => FieldMappingType.String,
        ActionFieldType.Choice => FieldMappingType.Choice,
        ActionFieldType.Boolean => FieldMappingType.Boolean,
        ActionFieldType.Number => FieldMappingType.Number,
        ActionFieldType.Lookup => FieldMappingType.Lookup,
        _ => FieldMappingType.String
    };
}
