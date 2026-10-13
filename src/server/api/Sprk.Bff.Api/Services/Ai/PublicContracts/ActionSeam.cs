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
    private readonly Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver _coreAncestors;
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownership;
    private readonly Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService _identity;
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly ILogger<ActionSeam> _logger;

    public ActionSeam(
        IGenericEntityService entityService,
        IFieldMappingDataverseService fieldMappingService,
        IServiceScopeFactory scopeFactory,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver coreAncestors,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService identity,
        ICommunicationDataverseService recordTypes,
        ILogger<ActionSeam> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _fieldMappingService = fieldMappingService ?? throw new ArgumentNullException(nameof(fieldMappingService));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        // Task 146: a task created through the seam is owned by its regarding record's team (TaskActionCore).
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        // Task 156, owner round 8 item 2: the sprk_recordtype_ref lookup for the task's ADR-024 regarding pair
        // (TaskActionCore). Unconditionally registered (GraphModule), so no asymmetric registration (§10 F.1).
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
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

        var core = new TaskActionCore(_entityService, _coreAncestors, _ownership, _identity, _recordTypes, _logger);
        var taskId = await core.CreateAsync(
            new TaskActionInput(
                Subject: request.Subject,
                Description: request.Description,
                // Task 098: calendar dates, passed as written. ToUniversalTime() moved an Unspecified/Local midnight
                // by the machine's offset — the previous day east of UTC — before a Date Only column stored its date.
                ScheduledEnd: request.DueDate,
                FinalDueDate: request.FinalDueDate,
                RegardingObjectId: request.RegardingObjectId,
                RegardingObjectType: request.RegardingObjectType,
                OwnerId: request.OwnerId,
                ActingUserId: request.ActingUserId,
                AssignedToContactId: request.AssignedToContactId,
                RequestedBySystemUserId: request.RequestedBySystemUserId), // task 146 c1-r1
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
        catch (Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException ex)
        {
            // Task 146: the update re-files a child record whose new owner cannot be resolved — no PATCH was issued.
            // A typed failure carrying the stable code (a Dataverse fault still propagates).
            return new UpdateRecordResult(false, Array.Empty<string>(), $"{ex.RefusalCode}: {ex.Message}");
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
